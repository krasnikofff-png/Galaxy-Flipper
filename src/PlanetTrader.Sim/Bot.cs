using System;
using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;

namespace PlanetTrader.Sim
{
    /// <summary>Стиль игры бота. Те же три стиля, что в таблице баланса.</summary>
    public sealed class Strategy
    {
        public string Name;
        /// <summary>Развивает планеты: исследует технологии, ставит экстракторы, учит жителей, заселяет колонистов.</summary>
        public bool Develops;
        /// <summary>Берёт кредит, чтобы купить выгодную планету.</summary>
        public bool UsesCredit;
        /// <summary>Перед покупкой полностью разведывает планету зондом, а не верит оценке реестра.</summary>
        public bool Explores;
        /// <summary>Следит за лояльностью жителей: налоги по настроению, праздники, школы, просьбы.</summary>
        public bool CaresForPeople;

        public static readonly Strategy Passive = new Strategy { Name = "Пассивный", Explores = true, CaresForPeople = true };
        public static readonly Strategy Active = new Strategy { Name = "Активный", Develops = true, Explores = true, CaresForPeople = true };
        public static readonly Strategy ActiveCredit = new Strategy { Name = "Активный + кредит", Develops = true, UsesCredit = true, Explores = true, CaresForPeople = true };
        /// <summary>Для проверки, окупается ли разведка: тот же активный, но покупает по оценке реестра.</summary>
        public static readonly Strategy ActiveNoScouting = new Strategy { Name = "Активный без разведки", Develops = true, CaresForPeople = true };
        /// <summary>Для проверки этапа 4: тот же активный, но жителей не замечает.</summary>
        public static readonly Strategy ActiveIgnoresPeople = new Strategy { Name = "Активный, жители побоку", Develops = true, Explores = true };

        public static readonly Strategy[] All = { Passive, Active, ActiveCredit };
    }

    /// <summary>
    /// Простой, но не глупый игрок: выполняет заказы, продаёт ресурсы без обвала цены,
    /// покупает планеты с хорошей окупаемостью, сканирует силуэты зондами.
    /// Видит только то, что знает игрок: силуэты не покупает, просканированные оценивает по реестру.
    /// </summary>
    public sealed class Bot
    {
        /// <summary>Покупаем, только если планета за оставшуюся жизнь вернёт вложенное с запасом.</summary>
        const double MinPaybackRatio = 1.4;
        /// <summary>Сколько ходов расходов держать на счету.</summary>
        const double CashBufferTurns = 3;
        /// <summary>Продаём на бирже, пока цена не упадёт ниже этой доли базовой.</summary>
        const double MinSellPriceShare = 0.85;
        /// <summary>Если на складе больше двух глубин рынка, продаём и дешевле.</summary>
        const double DumpPriceShare = 0.6;

        readonly Strategy _s;
        readonly Rng _rng;
        bool _ranOutOfActions;
        /// <summary>Видит выгодную планету, но пока не по карману: копит на неё и не тратится на развитие.</summary>
        bool _saving;

        public Bot(Strategy strategy, ulong seed = 1)
        {
            _s = strategy;
            _rng = new Rng(seed ^ 0xB07UL);
        }

        public void PlayTurn(Game g)
        {
            if (_ranOutOfActions && g.Managers < g.Balance.MaxManagers && IncomePerTurn(g) > 3 * g.Balance.ManagerSalary)
                g.HireManager(BestCandidate(g));
            _ranOutOfActions = false;

            FulfillOrders(g);
            if (_s.CaresForPeople) ManagePeople(g);
            SellResources(g);
            BuildMines(g);
            BuyPlanets(g);
            // Развитие — на то, что осталось после покупок: новая планета обычно окупается быстрее.
            if (_s.Develops && !_saving) Develop(g);
            if (_s.Explores) ExploreAhead(g);
            ManageDebt(g);
        }

        static double Buffer(Game g) => CashBufferTurns * g.CostsPerTurn;

        /// <summary>Кандидат, у которого сильная сторона лучше всего окупает зарплату.</summary>
        static int BestCandidate(Game g)
        {
            int best = 0;
            double bestScore = double.MinValue;
            for (int i = 0; i < g.Candidates.Count; i++)
            {
                var c = g.Candidates[i];
                double score = c.SkillLevels.Max() * 2 + c.TotalSkill - c.Salary / 20;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>Что бот предлагает жителям: заботливый — сделку за школу, если хватает денег, иначе 90% цены.</summary>
        Offer OfferFor(Game g, Planet p)
        {
            if (!_s.CaresForPeople) return new Offer(1.0);
            var school = new Offer(1.0, withSchool: true);
            return g.OfferCost(p, school) + Buffer(g) <= g.Money ? school : new Offer(0.9);
        }

        /// <summary>Наименьшая ставка, с которой аукцион выигрывается хотя бы в 55% случаев.</summary>
        static double BidFactor(Game g, Planet p)
        {
            foreach (var f in g.Balance.BidFactors)
                if (g.AuctionWinChance(p, f) >= 0.55) return f;
            return g.Balance.BidFactors[g.Balance.BidFactors.Length - 1];
        }

        /// <summary>Налог по настроению, праздник при низкой лояльности, школа на долгоживущих планетах, просьбы.</summary>
        void ManagePeople(Game g)
        {
            var b = g.Balance;
            // Подкуп магната: встречный подкуп, а если денег нет — праздник.
            foreach (var x in g.Bribes.ToList())
            {
                var target = g.GetPlanet(x.PlanetId);
                if (g.Money - b.CounterBribeCost >= Buffer(g) / 2) g.CounterBribe(x.Id);
                else if (g.ActionsLeft > 0 && g.Money >= b.FestivalCost) g.HoldFestival(target.Id);
            }
            // Рейдерский захват: защита дешевле, чем отдать планету за полцены.
            foreach (var c in g.Claims.ToList())
            {
                var target = g.GetPlanet(c.PlanetId);
                if (g.IsMine(target) && g.Money >= g.ClaimDefenseCost(target)) g.DefendClaim(c.Id);
            }
            foreach (var r in g.Requests.ToList())
            {
                var p = g.GetPlanet(r.PlanetId);
                if (r.Kind == RequestKind.Resource && g.Stock(r.Resource) >= r.Amount) g.FulfillRequest(r.Id);
                else if (r.Kind == RequestKind.School && g.ActionsLeft > 0 && g.Money - b.SchoolCost >= Buffer(g)) g.BuildSchool(p.Id);
            }
            foreach (var p in g.PlayerPlanets.Where(x => x.Inhabited).ToList())
            {
                g.SetTax(p.Id, p.Loyalty >= 70 ? TaxLevel.High : p.Loyalty >= 50 ? TaxLevel.Low : TaxLevel.None);
                if (!p.HasSchool && (p.RemainingTurns > 15 || _s.Develops) && g.ActionsLeft > 0 && g.Money - b.SchoolCost >= Buffer(g))
                    g.BuildSchool(p.Id);
                if ((p.Loyalty < 45 || p.OnStrike) && g.ActionsLeft > 0 && g.Money - b.FestivalCost >= Buffer(g))
                    g.HoldFestival(p.Id);
            }
        }

        static double IncomePerTurn(Game g) =>
            g.PlayerPlanets.Where(p => p.Mine == MineState.Working && !p.IsDepleted)
                .Sum(p => p.Type.OutputPerTurn * g.Balance.BasePrice(p.Resource));

        static void FulfillOrders(Game g)
        {
            foreach (var o in g.Orders.ToList())
                if (g.Stock(o.Resource) >= o.Amount && g.OrderReachable(o) && g.OrderNetPayout(o) > 0)
                    g.FulfillOrder(o.Id);
        }

        static void SellResources(Game g)
        {
            foreach (var r in ResourceTypes.All)
            {
                double stock = g.Stock(r);
                // Придерживаем ресурс под открытый заказ, если сами его добываем.
                bool producing = g.PlayerPlanets.Any(p => p.Resource == r && p.Mine != MineState.None && !p.IsDepleted);
                var order = g.Orders.Where(o => o.Resource == r && g.OrderReachable(o)).OrderBy(o => o.Amount).FirstOrDefault();
                // В самом начале деньги на вторую планету важнее заказа.
                if (order != null && producing && g.PlayerPlanetCount > 1) stock = Math.Max(0, stock - order.Amount);
                if (stock <= 0) continue;

                // Сколько можно продать, не уронив цену ниже порога.
                double share = stock > 2 * g.MarketDepth(r) ? DumpPriceShare : MinSellPriceShare;
                double floor = share * g.Balance.BasePrice(r);
                double price = g.Market.Price(r);
                if (price <= floor) continue;
                double maxDrop = 1 - floor / price;
                double maxAmount = maxDrop / g.Balance.SalesImpact * g.MarketDepth(r);
                double amount = Math.Min(stock, maxAmount);
                if (amount > 0.5) g.SellResource(r, amount);
            }
        }

        /// <summary>
        /// Развитие: экстракторы на открытых месторождениях, затем следующее исследование —
        /// сначала то, что открывает ресурсы своих дополнительных месторождений.
        /// </summary>
        void Develop(Game g)
        {
            foreach (var p in g.PlayerPlanets.ToList())
                for (int i = 0; i < p.ExtraDeposits.Count; i++)
                {
                    var d = p.ExtraDeposits[i];
                    if (d.HasExtractor || !g.IsUnlocked(d.Resource)) continue;
                    if (g.ActionsLeft == 0) { _ranOutOfActions = true; return; }
                    if (g.Money - g.ExtractorCost(d) < Buffer(g)) return;
                    g.BuildExtractor(p.Id, i);
                }

            EducatePeople(g);
            SettleColonies(g);

            if (g.CurrentResearch != null || g.ActionsLeft == 0 || g.PlayerPlanetCount < 3) return;
            var tech = g.Balance.Technologies
                .Where(g.CanResearch)
                .Select(t => (tech: t, value: TechValue(g, t, 0)))
                .Where(x => x.value >= 1.5 * g.ResearchCost(x.tech))
                .OrderByDescending(x => x.value / g.ResearchCost(x.tech))
                .Select(x => x.tech)
                .FirstOrDefault();
            if (tech != null && g.Money - g.ResearchCost(tech) >= Buffer(g) + 2000) g.StartResearch(tech.Id);
        }

        /// <summary>
        /// Учит жителей, если прибавка умственного труда за 40 ходов окупает обучение.
        /// Специальность — та, к которой склонны жители.
        /// </summary>
        void EducatePeople(Game g)
        {
            var b = g.Balance;
            const double horizon = 40;
            foreach (var p in g.PlayerPlanets.Where(x => x.Inhabited && x.HasSchool && !x.IsStudying && x.Education < 3)
                         .OrderByDescending(x => x.IsDepleted).ThenByDescending(x => x.Education).ToList())
            {
                int next = p.Education + 1;
                double cost = b.EducationCost[next];
                if (g.Money - cost < Buffer(g) + 1000) return;
                if (!g.CanEducate(p, out _)) continue;
                var specialty = p.Education > 0 ? p.Specialty : Specialties.Natural(p.Resource);
                double natural = specialty == Specialties.Natural(p.Resource) ? 1 + b.NaturalSpecialtyBonus : 1;
                // Пока шахта работает, трудится часть жителей; к выработке — все.
                double working = p.Mine == MineState.Working && !p.IsDepleted && p.RemainingTurns > horizon / 2 ? b.IntellectWhileMining : 1;
                double gain = (b.IntellectIncome[next] - b.IntellectIncome[p.Education]) * natural * working
                              * (1 + (p.Loyalty - 50) * b.IntellectPerLoyalty) * horizon
                              + 150 * next;  // бонус корпорации от специальности, грубо
                // Тот же порог, что для покупки планеты: окупить с запасом за 40 ходов.
                if (gain < MinPaybackRatio * cost) continue;
                if (g.ActionsLeft == 0) { _ranOutOfActions = true; return; }
                g.Educate(p.Id, specialty);
            }
        }

        /// <summary>
        /// Заселяет колонистов на планеты, где основное месторождение выработано или скоро кончится:
        /// шахта жителей не раздражает, а умом они заработают и во время восстановления.
        /// </summary>
        void SettleColonies(Game g)
        {
            var b = g.Balance;
            foreach (var p in g.PlayerPlanets.Where(x => !x.Inhabited && (x.IsDepleted || x.RemainingTurns < 10)).ToList())
            {
                if (g.Money - b.ColonyCost - b.SchoolCost - b.EducationCost[1] < Buffer(g)) return;
                if (g.ActionsLeft == 0) { _ranOutOfActions = true; return; }
                g.SettleColony(p.Id);
            }
        }

        /// <summary>
        /// Сколько технология принесёт за 40 ходов на нынешних планетах, плюс половина пользы тех,
        /// что она открывает дальше.
        /// </summary>
        static double TechValue(Game g, Technology t, int depth)
        {
            const double horizon = 40;
            var b = g.Balance;
            double revenue = g.PlayerPlanets.Where(p => p.Mine == MineState.Working)
                .Sum(p => Planet.CycleAverage(p.Capacity, p.Type.OutputPerTurn, b.RegenTurns[(int)p.Resource]) * b.BasePrice(p.Resource));
            double value = 0;
            if (t.UnlocksResource.HasValue)
                value += g.PlayerPlanets.SelectMany(p => p.ExtraDeposits).Where(d => d.Resource == t.UnlocksResource.Value)
                    .Sum(d => Planet.CycleAverage(d.Capacity, d.OutputPerTurn, b.RegenTurns[(int)d.Resource]) * b.BasePrice(d.Resource) * horizon - g.ExtractorCost(d));
            value += t.OutputBonus * revenue * horizon;
            value += (1 - t.RegenFactor) * 0.1 * revenue * horizon;
            value += t.LoyaltyBonus * g.PlayerPlanets.Count(p => p.Inhabited) * 150;
            if (depth < 2)
                foreach (var next in b.Technologies.Where(x => x.Requires.Contains(t.Id)))
                    value += 0.5 * Math.Max(0, TechValue(g, next, depth + 1) - next.Cost);
            return value;
        }

        void BuildMines(Game g)
        {
            foreach (var p in g.PlayerPlanets.Where(p => p.Mine == MineState.None && !p.IsDepleted).ToList())
            {
                if (g.ActionsLeft == 0) { _ranOutOfActions = true; return; }
                if (g.Money - g.MineCost(p) < Buffer(g)) return;
                g.BuildMine(p.Id);
            }
        }

        /// <summary>
        /// Чистый доход планеты за оставшуюся жизнь с учётом роста общего содержания, к цене.
        /// Запас — тот, что знает игрок; у силуэтов оценки нет.
        /// </summary>
        static double PaybackRatio(Game g, Planet p)
        {
            var known = g.KnownReserve(p);
            if (!known.HasValue) return 0;
            var b = g.Balance;
            int n = g.UpkeepCurvePlanets;
            double baseUpkeep = g.PlayerPlanets.Sum(x => x.BaseUpkeep(b));
            double extraUpkeep = (baseUpkeep + p.Type.Upkeep) * b.UpkeepMultiplier(n + 1) - baseUpkeep * b.UpkeepMultiplier(n);
            // Месторождения восстанавливаются: считаем средний за цикл доход на горизонте в 40 ходов.
            double capacity = g.KnownCapacity(p) ?? known.Value;
            double avgOutput = Planet.CycleAverage(capacity, p.Type.OutputPerTurn, b.RegenTurns[(int)p.Resource]);
            double net = avgOutput * b.BasePrice(p.Resource) - extraUpkeep;
            double shipping = g.ShippingCost(p.Resource, avgOutput, g.DistanceToExchange(p));
            double bonus = p.Knowledge == Knowledge.Explored && p.HasUnlootedRuins ? p.RuinsValue : 0;
            double cost = g.PurchasePrice(p) + (p.Mine == MineState.None ? g.MineCost(p) : 0);
            // У обитаемых планет ещё и налог; заботливый бот рассчитывает на низкий.
            if (p.Inhabited) net += b.TaxIncome[(int)TaxLevel.Low];
            const double horizon = 40;
            return ((net - shipping) * horizon + bonus) / cost;
        }

        void BuyPlanets(Game g)
        {
            // Пока планеты не окупают расходы, не расширяемся.
            if (IncomePerTurn(g) < g.CostsPerTurn) return;

            _saving = false;
            var skip = new HashSet<int>();
            while (g.ActionsLeft > 0)
            {
                // Сколько можно потратить: деньги сверх запаса, а с кредитной стратегией — ещё и свободный кредит.
                // Занимаем только в пределах полки содержания: дальше кредит не окупается.
                bool borrow = _s.UsesCredit && g.UpkeepCurvePlanets < g.Balance.UpkeepShelfEnd;
                double budget = g.Money - Buffer(g) + (borrow ? g.AvailableCredit : 0);
                bool anyGood = false;
                Planet pick = null;
                double pickRatio = 0;
                foreach (var p in g.PlanetsForSale)
                {
                    if (skip.Contains(p.Id) || (p.Inhabited && g.Turn < p.NoTalksUntilTurn)) continue;
                    double ratio = PaybackRatio(g, p);
                    if (ratio < MinPaybackRatio) continue;
                    anyGood = true;
                    if (FullCost(g, p) <= budget && ratio > pickRatio)
                    {
                        pick = p;
                        pickRatio = ratio;
                    }
                }

                if (pick == null)
                {
                    // Выгодного не видно — сканируем силуэты или ищем скрытые планеты. Если выгодное есть, но не по карману, копим.
                    if (!anyGood && Scan(g)) continue;
                    _saving = anyGood;
                    return;
                }

                if (_s.Explores && pick.Knowledge != Knowledge.Explored)
                {
                    // Вслепую не покупаем: её разведает ExploreAhead.
                    skip.Add(pick.Id);
                    continue;
                }

                double shortfall = FullCost(g, pick) + Buffer(g) - g.Money;
                if (shortfall > 0) g.TakeLoan(Math.Min(shortfall, g.AvailableCredit));

                if (pick.Inhabited)
                {
                    g.MakeOffer(pick.Id, OfferFor(g, pick), out bool accepted);
                    if (!accepted) continue;
                }
                else if (pick.SoldAtAuction)
                {
                    g.PlaceBid(pick.Id, BidFactor(g, pick), out bool won);
                    if (!won) continue;
                }
                else
                    g.BuyPlanet(pick.Id);
                if (g.ActionsLeft == 0) break;
                if (pick.Mine == MineState.None) g.BuildMine(pick.Id);
            }
            _ranOutOfActions = true;
        }

        /// <summary>
        /// Свободным действием разведывает самую перспективную по оценке реестра планету —
        /// заранее, пока копятся деньги, чтобы к покупке уже знать правду.
        /// </summary>
        void ExploreAhead(Game g)
        {
            if (g.ActionsLeft == 0 || g.Probes.Any(x => x.Mission == ProbeMission.Explore)) return;
            bool borrow = _s.UsesCredit && g.UpkeepCurvePlanets < g.Balance.UpkeepShelfEnd;
            double budget = g.Money - Buffer(g) + (borrow ? g.AvailableCredit : 0);

            var candidates = g.PlanetsForSale
                .Where(p => p.Knowledge == Knowledge.Scanned && !g.ProbeHeadingTo(p.Id))
                .Select(p => (planet: p, ratio: PaybackRatio(g, p)))
                .Where(x => x.ratio >= MinPaybackRatio)
                .OrderByDescending(x => x.ratio)
                .Select(x => x.planet)
                .ToList();
            // Сначала то, что можно купить уже сейчас. Дорогую цель — только если копить больше не на что.
            var target = candidates.FirstOrDefault(p => FullCost(g, p) <= budget);
            bool hasExploredGoal = g.PlanetsForSale.Any(p => p.Knowledge == Knowledge.Explored && PaybackRatio(g, p) >= MinPaybackRatio);
            if (target == null && !hasExploredGoal) target = candidates.FirstOrDefault();
            if (target == null) return;

            var probe = CheapestProbe(g, target.MapX, target.MapY);
            if (probe != null && g.Money - probe.Cost >= Buffer(g)) g.LaunchExplore(probe, target.Id);
        }

        static ProbeType CheapestProbe(Game g, double x, double y) =>
            g.Balance.ProbeTypes.Where(t => g.InRange(t, x, y)).OrderBy(t => t.Cost).FirstOrDefault();

        /// <summary>
        /// Один скан за раз: туда, где больше всего силуэтов на единицу цены зонда.
        /// Если силуэтов нет — в случайную точку искать скрытые планеты.
        /// </summary>
        bool Scan(Game g)
        {
            if (g.Probes.Any(x => x.Mission == ProbeMission.ScanArea)) return false;
            var silhouettes = g.PlanetsForSale.Where(p => p.Knowledge == Knowledge.Silhouette).ToList();

            ProbeType bestType = null;
            double bestX = 0, bestY = 0, bestScore = 0;
            foreach (var t in g.Balance.ProbeTypes)
            {
                if (g.Money - t.Cost < Buffer(g) + 1500) continue;
                foreach (var target in silhouettes)
                {
                    if (!g.InRange(t, target.MapX, target.MapY)) continue;
                    int covered = silhouettes.Count(p => Geometry.Distance(p.MapX, p.MapY, target.MapX, target.MapY) <= t.ScanRadius);
                    double score = covered / t.Cost;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestType = t;
                        bestX = target.MapX;
                        bestY = target.MapY;
                    }
                }
            }

            if (bestType == null)
            {
                // Силуэтов в досягаемости нет: ищем скрытые планеты «Стандартом», как раньше, если они ещё остались.
                if (g.SectorFull) return false;
                var t = g.Balance.ProbeTypes.FirstOrDefault(x => x.Name == "Стандарт") ?? g.Balance.ProbeTypes[0];
                if (g.Money - t.Cost < Buffer(g) + 1500) return false;
                for (int attempt = 0; attempt < 20 && bestType == null; attempt++)
                {
                    double x = _rng.Range(0.05, 0.95), y = _rng.Range(0.05, 0.95);
                    if (!g.InRange(t, x, y)) continue;
                    bestType = t;
                    bestX = x;
                    bestY = y;
                }
                if (bestType == null) return false;
            }
            return g.LaunchScan(bestType, bestX, bestY).Ok;
        }

        double FullCost(Game g, Planet p) => PurchasePrice(g, p) + (p.Mine == MineState.None ? g.MineCost(p) : 0);

        double PurchasePrice(Game g, Planet p) =>
            p.Inhabited ? g.OfferCost(p, OfferFor(g, p))
            : p.SoldAtAuction ? g.BidPrice(p, BidFactor(g, p))
            : g.PurchasePrice(p);

        void ManageDebt(Game g)
        {
            if (g.Debt <= 0) return;
            // Без кредитной стратегии держим запас на покупку планеты. Заёмщик гасит всё лишнее
            // и при нужде займёт снова.
            double keep = Buffer(g) + (_s.UsesCredit ? 0 : 4000);
            double spare = g.Money - keep;
            if (spare > 0) g.RepayLoan(Math.Min(spare, g.Debt));
        }
    }
}
