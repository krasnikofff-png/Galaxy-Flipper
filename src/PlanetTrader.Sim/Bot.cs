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
        /// <summary>Продаёт выработанные и почти выработанные планеты.</summary>
        public bool SellsSpentPlanets;
        /// <summary>Берёт кредит, чтобы купить выгодную планету.</summary>
        public bool UsesCredit;
        /// <summary>Перед покупкой полностью разведывает планету зондом, а не верит оценке реестра.</summary>
        public bool Explores;
        /// <summary>Следит за лояльностью жителей: налоги по настроению, праздники, школы, просьбы.</summary>
        public bool CaresForPeople;

        public static readonly Strategy Passive = new Strategy { Name = "Пассивный", Explores = true, CaresForPeople = true };
        public static readonly Strategy Active = new Strategy { Name = "Активный", SellsSpentPlanets = true, Explores = true, CaresForPeople = true };
        public static readonly Strategy ActiveCredit = new Strategy { Name = "Активный + кредит", SellsSpentPlanets = true, UsesCredit = true, Explores = true, CaresForPeople = true };
        /// <summary>Для проверки, окупается ли разведка: тот же активный, но покупает по оценке реестра.</summary>
        public static readonly Strategy ActiveNoScouting = new Strategy { Name = "Активный без разведки", SellsSpentPlanets = true, CaresForPeople = true };
        /// <summary>Для проверки этапа 4: тот же активный, но жителей не замечает.</summary>
        public static readonly Strategy ActiveIgnoresPeople = new Strategy { Name = "Активный, жители побоку", SellsSpentPlanets = true, Explores = true };

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

        public Bot(Strategy strategy, ulong seed = 1)
        {
            _s = strategy;
            _rng = new Rng(seed ^ 0xB07UL);
        }

        public void PlayTurn(Game g)
        {
            if (_ranOutOfActions && g.Managers < g.Balance.MaxManagers && IncomePerTurn(g) > 3 * g.Balance.ManagerSalary)
                g.HireManager();
            _ranOutOfActions = false;

            FulfillOrders(g);
            if (_s.CaresForPeople) ManagePeople(g);
            SellResources(g);
            if (_s.SellsSpentPlanets) SellSpentPlanets(g);
            BuildMines(g);
            BuyPlanets(g);
            if (_s.Explores) ExploreAhead(g);
            ManageDebt(g);
        }

        static double Buffer(Game g) => CashBufferTurns * g.CostsPerTurn;

        /// <summary>Что бот предлагает жителям: заботливый — сделку за школу, если хватает денег, иначе 90% цены.</summary>
        Offer OfferFor(Game g, Planet p)
        {
            if (!_s.CaresForPeople) return new Offer(1.0);
            var school = new Offer(1.0, withSchool: true);
            return g.OfferCost(p, school) + Buffer(g) <= g.Money ? school : new Offer(0.9);
        }

        /// <summary>Налог по настроению, праздник при низкой лояльности, школа на долгоживущих планетах, просьбы.</summary>
        void ManagePeople(Game g)
        {
            var b = g.Balance;
            foreach (var r in g.Requests.ToList())
            {
                var p = g.GetPlanet(r.PlanetId);
                if (r.Kind == RequestKind.Resource && g.Stock(r.Resource) >= r.Amount) g.FulfillRequest(r.Id);
                else if (r.Kind == RequestKind.School && g.ActionsLeft > 0 && g.Money - b.SchoolCost >= Buffer(g)) g.BuildSchool(p.Id);
            }
            foreach (var p in g.PlayerPlanets.Where(x => x.Inhabited).ToList())
            {
                g.SetTax(p.Id, p.Loyalty >= 70 ? TaxLevel.High : p.Loyalty >= 50 ? TaxLevel.Low : TaxLevel.None);
                if (!p.HasSchool && p.RemainingTurns > 15 && g.ActionsLeft > 0 && g.Money - b.SchoolCost >= Buffer(g))
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
                if (order != null && producing) stock = Math.Max(0, stock - order.Amount);
                if (stock <= 0) continue;

                // Сколько можно продать, не уронив цену ниже порога.
                double share = stock > 2 * g.Balance.Depth(r) ? DumpPriceShare : MinSellPriceShare;
                double floor = share * g.Balance.BasePrice(r);
                double price = g.Market.Price(r);
                if (price <= floor) continue;
                double maxDrop = 1 - floor / price;
                double maxAmount = maxDrop / g.Balance.SalesImpact * g.Balance.Depth(r);
                double amount = Math.Min(stock, maxAmount);
                if (amount > 0.5) g.SellResource(r, amount);
            }
        }

        void SellSpentPlanets(Game g)
        {
            foreach (var p in g.PlayerPlanets.ToList())
            {
                if (g.ActionsLeft == 0) { _ranOutOfActions = true; return; }
                if (p.Mine == MineState.Working && p.RemainingTurns < 2)
                    g.SellPlanet(p.Id);
            }
        }

        void BuildMines(Game g)
        {
            foreach (var p in g.PlayerPlanets.Where(p => p.Mine == MineState.None && !p.IsDepleted).ToList())
            {
                if (g.ActionsLeft == 0) { _ranOutOfActions = true; return; }
                if (g.Money - p.Type.MineCost < Buffer(g)) return;
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
            double net = p.Type.OutputPerTurn * b.BasePrice(p.Resource) - extraUpkeep;
            double shipping = g.ShippingCost(p.Resource, p.Type.OutputPerTurn, g.DistanceToExchange(p));
            double bonus = p.Knowledge == Knowledge.Explored && p.HasUnlootedRuins ? p.RuinsValue : 0;
            double cost = p.ListPrice(b) + (p.Mine == MineState.None ? p.Type.MineCost : 0);
            // У обитаемых планет ещё и налог; заботливый бот рассчитывает на низкий.
            if (p.Inhabited) net += b.TaxIncome[(int)TaxLevel.Low];
            return ((net - shipping) * known.Value / p.Type.OutputPerTurn + bonus) / cost;
        }

        void BuyPlanets(Game g)
        {
            // Пока планеты не окупают расходы, не расширяемся.
            if (IncomePerTurn(g) < g.CostsPerTurn) return;

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
                // Силуэтов в досягаемости нет: ищем скрытые планеты «Стандартом», как раньше.
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

        double FullCost(Game g, Planet p) =>
            (p.Inhabited ? g.OfferCost(p, OfferFor(g, p)) : p.ListPrice(g.Balance)) + (p.Mine == MineState.None ? p.Type.MineCost : 0);

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
