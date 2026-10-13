using System;
using System.Collections.Generic;
using System.Linq;

namespace PlanetTrader.Core
{
    public readonly struct ActionResult
    {
        public readonly bool Ok;
        public readonly string Error;

        ActionResult(bool ok, string error)
        {
            Ok = ok;
            Error = error;
        }

        public static readonly ActionResult Success = new ActionResult(true, null);
        public static ActionResult Fail(string error) => new ActionResult(false, error);
    }

    /// <summary>Что произошло за ход. Нужен интерфейсу и симулятору.</summary>
    public sealed class TurnReport
    {
        public int Turn;
        public double Upkeep;
        public double Salaries;
        public double Interest;
        public double Insurance;
        public double InsurancePayout;
        /// <summary>Доставка добытого на биржу.</summary>
        public double Shipping;
        /// <summary>Зарплаты жителям.</summary>
        public double Wages;
        /// <summary>Налоги с жителей (доход).</summary>
        public double Taxes;
        /// <summary>Охрана планет от подкупа.</summary>
        public double Guards;
        public int Accidents;
        /// <summary>Сколько планет забрали коллекторы.</summary>
        public int Seizures;
        public readonly List<string> Events = new List<string>();

        public double TotalCosts => Upkeep + Salaries + Interest + Insurance + Shipping + Wages + Guards;
    }

    /// <summary>
    /// Состояние партии и все правила экономики. Один игрок против ИИ.
    /// Действия (покупка, шахта, продажа планеты, запуск зонда, найм) тратят очки действий,
    /// торговля на бирже, заказы и кредит — бесплатны.
    /// </summary>
    public sealed class Game
    {
        static readonly string[] Customers =
        {
            "Империя Зорг", "Корпорация «Вечный понедельник»", "Синдикат Глорб",
            "Республика Пыльных Лун", "Орден Бухгалтеров", "Кооператив «Тёплая туманность»",
        };

        public readonly Balance Balance;
        public readonly Market Market;
        readonly Rng _rng;
        /// <summary>Отдельный генератор для карты, чтобы раскладка планет не меняла ход партии.</summary>
        readonly Rng _mapRng;
        /// <summary>Отдельный генератор для оценок реестра и сюрпризов.</summary>
        readonly Rng _surveyRng;
        readonly List<Planet> _planets = new List<Planet>();
        readonly List<Order> _orders = new List<Order>();
        readonly List<Probe> _probes = new List<Probe>();
        readonly List<Shipment> _shipments = new List<Shipment>();
        readonly List<ResidentRequest> _requests = new List<ResidentRequest>();
        readonly List<Magnate> _magnates;
        readonly List<BribeAttempt> _bribes = new List<BribeAttempt>();
        readonly double[] _stock = new double[ResourceTypes.Count];
        int _nextPlanetId;
        int _nextOrderId;
        int _nextProbeId;
        int _nextRequestId;
        int _nextBribeId;

        /// <summary>
        /// Сообщения о том, что случилось во время действий игрока (например, нашлись руины при покупке).
        /// Интерфейс забирает их и очищает список.
        /// </summary>
        public readonly List<string> Notices = new List<string>();

        public int Turn { get; private set; } = 1;
        public double Money { get; private set; }
        public double Debt { get; private set; }
        public int Managers { get; private set; }
        public int ActionsLeft { get; private set; }
        public bool Insured { get; private set; }
        /// <summary>Репутация 0–100: грязные дела её портят, выполненные заказы чинят.</summary>
        public double Reputation { get; private set; }

        // Статистика для симулятора и плейтестов.
        public int TotalSeizures { get; private set; }
        public int TurnsActionsExhausted { get; private set; }
        public int PlanetsLostToBribes { get; private set; }
        public int AuctionsWon { get; private set; }
        public int AuctionsLost { get; private set; }
        public double FinesPaid { get; private set; }
        public readonly List<Planet> SoldPlanets = new List<Planet>();

        public IReadOnlyList<Planet> AllPlanets => _planets;
        public IReadOnlyList<Order> Orders => _orders;
        public IReadOnlyList<Probe> Probes => _probes;
        public IReadOnlyList<Shipment> Shipments => _shipments;
        public IReadOnlyList<ResidentRequest> Requests => _requests;
        public IReadOnlyList<Magnate> Magnates => _magnates;
        public IReadOnlyList<BribeAttempt> Bribes => _bribes;

        public Magnate GetMagnate(int id) => _magnates.FirstOrDefault(m => m.Id == id);

        /// <summary>Магнаты, которые не отбиваются от слухов.</summary>
        IEnumerable<Magnate> ActiveMagnates => _magnates.Where(m => Turn >= m.QuietUntilTurn);
        public IEnumerable<Planet> PlayerPlanets => _planets.Where(p => p.Owner == Owner.Player);
        public int PlayerPlanetCount => _planets.Count(p => p.Owner == Owner.Player);

        /// <summary>Планеты, которые игрок может купить прямо сейчас.</summary>
        public IEnumerable<Planet> PlanetsForSale => _planets.Where(p => p.Owner == Owner.Registry && !p.IsDepleted);

        public double Stock(Resource r) => _stock[(int)r];

        public double InTransit(Resource r) => _shipments.Where(x => x.Resource == r).Sum(x => x.Amount);

        public int ActionsPerTurn => Balance.BaseActionsPerTurn + Managers * Balance.ActionsPerManager;

        Game(Balance balance, ulong seed)
        {
            Balance = balance;
            _rng = new Rng(seed);
            _mapRng = new Rng(seed ^ 0x5EC7_0A11UL);
            _surveyRng = new Rng(seed ^ 0x50_17E5UL);
            Market = new Market(balance);
            _magnates = balance.Magnates.Select(m => m.Clone()).ToList();
            Reputation = balance.StartReputation;
        }

        public static Game New(Balance balance, ulong seed)
        {
            var g = new Game(balance, seed);
            g.Money = balance.StartMoney;
            g.Debt = balance.StartDebt;

            for (int i = 0; i < balance.SectorPlanets; i++)
                g._planets.Add(g.GeneratePlanet(g.RandomType()));

            int aiCount = (int)Math.Round(balance.SectorPlanets * balance.AiStartShare);
            int k = 0;
            foreach (var p in g._planets.OrderBy(_ => g._rng.NextULong()).Take(aiCount))
            {
                p.Owner = Owner.Ai;
                p.Mine = MineState.Working;
                p.MagnateId = g._magnates.Count == 0 ? -1 : g._magnates[k++ % g._magnates.Count].Id;
            }

            // Стартовая планета: скромный ледяной карлик со средним запасом и работающей шахтой.
            var start = new Planet(g._nextPlanetId++, balance.TypeOf(Resource.Ice), balance.TypeOf(Resource.Ice).AverageReserve)
            {
                Owner = Owner.Player,
                Mine = balance.StartPlanetHasMine ? MineState.Working : MineState.None,
                BoughtOnTurn = 0,
            };
            g.PlaceOnMap(start);
            start.Knowledge = Knowledge.Explored;
            g._planets.Add(start);

            // Окрестности своей планеты игрок знает по скану, остальное — силуэты.
            foreach (var p in g._planets)
                if (p != start && Geometry.Distance(p.MapX, p.MapY, start.MapX, start.MapY) <= balance.StartScanRadius)
                    p.Knowledge = Knowledge.Scanned;

            g.ActionsLeft = g.ActionsPerTurn;
            return g;
        }

        PlanetType RandomType()
        {
            double total = Balance.PlanetTypes.Sum(t => t.Weight);
            double roll = _rng.NextDouble() * total;
            foreach (var t in Balance.PlanetTypes)
            {
                roll -= t.Weight;
                if (roll < 0) return t;
            }
            return Balance.PlanetTypes[Balance.PlanetTypes.Length - 1];
        }

        Planet GeneratePlanet(PlanetType type)
        {
            double spread = Balance.ReserveSpread;
            double reserve = Math.Round(type.AverageReserve * _rng.Range(1 - spread, 1 + spread));
            var p = new Planet(_nextPlanetId++, type, reserve);
            PlaceOnMap(p);
            Survey(p);
            return p;
        }

        /// <summary>Оценка реестра с ошибкой и сюрпризы, которые откроет только полная разведка.</summary>
        void Survey(Planet p)
        {
            double s = Balance.RegistryEstimateSpread;
            double estimate = p.InitialReserve * _surveyRng.Range(1 - s, 1 + s);
            if (_surveyRng.Chance(Balance.DudChance)) estimate = p.InitialReserve / Balance.DudReserveShare;
            p.EstimatedInitialReserve = Math.Round(estimate);
            if (_surveyRng.Chance(Balance.RuinsChance))
                p.RuinsValue = Math.Round(_surveyRng.Range(Balance.RuinsMinValue, Balance.RuinsMaxValue) / 10) * 10;
            p.Inhabited = _surveyRng.Chance(Balance.InhabitedChance);
        }

        /// <summary>Случайное место на карте, не слишком близко к другим планетам и к бирже.</summary>
        void PlaceOnMap(Planet p) => PlaceOnMap(p, 0.5, 0.5, 0.45);

        void PlaceOnMap(Planet p, double centerX, double centerY, double radius)
        {
            const double minDistance = 0.09;
            double bestX = centerX, bestY = centerY, bestGap = -1;
            for (int attempt = 0; attempt < 40; attempt++)
            {
                double x = Math.Min(0.95, Math.Max(0.05, centerX + _mapRng.Range(-radius, radius)));
                double y = Math.Min(0.95, Math.Max(0.05, centerY + _mapRng.Range(-radius, radius)));
                double gap = Geometry.Distance(x, y, Balance.ExchangeX, Balance.ExchangeY);
                foreach (var other in _planets)
                {
                    double dx = other.MapX - x, dy = other.MapY - y;
                    gap = Math.Min(gap, Math.Sqrt(dx * dx + dy * dy));
                }
                if (gap > bestGap)
                {
                    bestX = x;
                    bestY = y;
                    bestGap = gap;
                }
                if (gap >= minDistance) break;
            }
            p.MapX = bestX;
            p.MapY = bestY;
        }

        public Planet GetPlanet(int id) => _planets.FirstOrDefault(p => p.Id == id);

        // ------------------------------------------------------------------
        // Оценки
        // ------------------------------------------------------------------

        public double PlanetsValue => PlayerPlanets.Sum(p => p.MarketValue(Balance));
        /// <summary>Склад и грузы в пути по выручке, если продать всё разом, — с учётом падения цены.</summary>
        public double StockValue => ResourceTypes.All.Sum(r => Market.Quote(r, _stock[(int)r] + InTransit(r)));

        /// <summary>Капитал = деньги − долг + стоимость планет + стоимость склада.</summary>
        public double Capital => Money - Debt + PlanetsValue + StockValue;

        /// <summary>Запас, который знает игрок: точный у своих и разведанных, оценка у просканированных, null у силуэтов.</summary>
        public double? KnownReserve(Planet p)
        {
            if (p.Owner == Owner.Player || p.Knowledge == Knowledge.Explored) return p.Reserve;
            if (p.Knowledge == Knowledge.Scanned) return p.EstimatedReserve;
            return null;
        }

        /// <summary>Стоимость планеты по тому, что знает игрок; null у силуэтов.</summary>
        public double? KnownValue(Planet p)
        {
            var reserve = KnownReserve(p);
            return reserve.HasValue ? p.ValueWithReserve(Balance, reserve.Value) : (double?)null;
        }

        // ------------------------------------------------------------------
        // Расстояния и доставка
        // ------------------------------------------------------------------

        public double DistanceToExchange(Planet p) => Geometry.Distance(p.MapX, p.MapY, Balance.ExchangeX, Balance.ExchangeY);

        /// <summary>Сколько ходов груз идёт с планеты на биржу: ближние доставляют в тот же ход.</summary>
        public int ShipTurns(Planet p) => (int)Math.Floor(DistanceToExchange(p) / Balance.ShipSpeed);

        public double ShippingCost(Resource r, double amount, double distance) =>
            amount * Balance.BasePrice(r) * Balance.ShippingCostPerDistance * distance;

        /// <summary>Доставка добычи за ход, если все шахты работают.</summary>
        public double ShippingPerTurn => PlayerPlanets
            .Where(p => p.Mine == MineState.Working && !p.IsDepleted)
            .Sum(p => ShippingCost(p.Resource, Math.Min(p.Type.OutputPerTurn, p.Reserve), DistanceToExchange(p)));

        public int DeliveryTurns(Order o) => Math.Max(1, (int)Math.Ceiling(o.Distance(Balance) / Balance.ShipSpeed));
        public double DeliveryCost(Order o) => ShippingCost(o.Resource, o.Amount, o.Distance(Balance));
        public double OrderNetPayout(Order o) => o.GrossPayout(Market, Balance) - DeliveryCost(o);
        /// <summary>Успеет ли груз, если отправить его сейчас.</summary>
        public bool OrderReachable(Order o) => Turn + DeliveryTurns(o) <= o.DeadlineTurn;

        public double CreditLimit => Balance.CreditLimitShare * PlanetsValue;
        public double AvailableCredit => Math.Max(0, CreditLimit - Debt);

        /// <summary>Сколько планет учитывается в множителе содержания.</summary>
        public int UpkeepCurvePlanets =>
            PlayerPlanets.Count(p => Balance.DepletedCountsInUpkeepCurve || !p.IsDepleted);

        public double UpkeepMultiplierNow => Balance.UpkeepMultiplier(UpkeepCurvePlanets);

        /// <summary>Сколько планета игрока стоит в содержании прямо сейчас, с множителем.</summary>
        public double PlanetUpkeep(Planet p) => p.BaseUpkeep(Balance) * UpkeepMultiplierNow;

        public double UpkeepPerTurn =>
            PlayerPlanets.Sum(p => p.BaseUpkeep(Balance)) * Balance.UpkeepMultiplier(UpkeepCurvePlanets);

        public double InsurancePerTurn => Insured ? Balance.InsuranceRate * PlanetsValue : 0;

        public double WagesPerTurn => PlayerPlanets.Count(p => p.Inhabited && p.PaysWages) * Balance.WagesCost;

        /// <summary>Налоги за ход с небастующих планет.</summary>
        public double TaxesPerTurn => PlayerPlanets.Where(p => p.Inhabited && !p.OnStrike).Sum(p => Balance.TaxIncome[(int)p.Tax]);

        public double GuardsPerTurn => PlayerPlanets.Count(p => p.Guarded) * Balance.GuardsCost;

        public double CostsPerTurn =>
            UpkeepPerTurn + Managers * Balance.ManagerSalary + Debt * Balance.InterestPerTurn + InsurancePerTurn + ShippingPerTurn + WagesPerTurn + GuardsPerTurn;

        // ------------------------------------------------------------------
        // Действия, которые тратят очки действий
        // ------------------------------------------------------------------

        ActionResult SpendAction()
        {
            if (ActionsLeft <= 0) return ActionResult.Fail("Действия на этот ход закончились");
            ActionsLeft--;
            if (ActionsLeft == 0) TurnsActionsExhausted++;
            return ActionResult.Success;
        }

        public ActionResult BuyPlanet(int planetId)
        {
            var p = GetPlanet(planetId);
            if (p == null || p.Owner != Owner.Registry) return ActionResult.Fail("Планета не продаётся");
            if (p.IsDepleted) return ActionResult.Fail("Планета выработана");
            if (p.Inhabited)
            {
                if (p.Knowledge == Knowledge.Silhouette) p.Knowledge = Knowledge.Scanned;
                return ActionResult.Fail("Планета обитаема: её продают сами жители, сделайте им предложение");
            }
            if (p.SoldAtAuction) return ActionResult.Fail("Планета продаётся с аукциона: сделайте ставку");
            double price = p.ListPrice(Balance);
            if (Money < price) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;

            Acquire(p, price);
            return ActionResult.Success;
        }

        void Acquire(Planet p, double price)
        {
            Money -= price;
            p.Owner = Owner.Player;
            p.BoughtOnTurn = Turn;
            p.Invested += price;
            // С покупкой приходят все документы: планета становится разведанной.
            if (p.Knowledge != Knowledge.Explored)
            {
                p.Knowledge = Knowledge.Explored;
                Notices.Add(DescribeExploration(p));
            }
            LootRuins(p, Notices);
        }

        // ------------------------------------------------------------------
        // Жители
        // ------------------------------------------------------------------

        public double OfferPrice(Planet p, Offer o) =>
            Math.Round(p.ListPrice(Balance) * (o.WithSchool ? Balance.SchoolDealShare : o.PriceShare));

        /// <summary>Сколько денег нужно на сделку: цена и, если обещана школа, её постройка.</summary>
        public double OfferCost(Planet p, Offer o) => OfferPrice(p, o) + (o.WithSchool ? Balance.SchoolCost : 0);

        /// <summary>Шанс согласия жителей. Репутация сдвигает его до ±20%.</summary>
        public double OfferChance(Planet p, Offer o)
        {
            double chance = o.WithSchool
                ? Balance.SchoolDealChance
                : Balance.OfferChanceAtFullPrice + (o.PriceShare - 1) * Balance.OfferChancePerShare;
            chance += (Reputation - 50) / 250;
            return Math.Min(0.95, Math.Max(0.05, chance));
        }

        /// <summary>
        /// Предложение жителям, которые продают свою планету. Тратит действие, даже если откажут.
        /// После отказа жители несколько ходов не разговаривают.
        /// </summary>
        public ActionResult MakeOffer(int planetId, Offer offer, out bool accepted)
        {
            accepted = false;
            var p = GetPlanet(planetId);
            if (p == null || p.Owner != Owner.Registry) return ActionResult.Fail("Планета не продаётся");
            if (!p.Inhabited) return ActionResult.Fail("Планета необитаема, её продаёт реестр");
            if (p.IsDepleted) return ActionResult.Fail("Планета выработана");
            if (Turn < p.NoTalksUntilTurn) return ActionResult.Fail($"Жители обиделись и не разговаривают до хода {p.NoTalksUntilTurn}");
            if (Money < OfferCost(p, offer)) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;

            if (!_rng.Chance(OfferChance(p, offer)))
            {
                p.NoTalksUntilTurn = Turn + Balance.RefusalCooldownTurns;
                Notices.Add($"Жители {p.Type.Name} #{p.Id} отказались и не будут разговаривать до хода {p.NoTalksUntilTurn}.");
                return ActionResult.Success;
            }

            accepted = true;
            Acquire(p, OfferPrice(p, offer));
            if (offer.WithSchool)
            {
                Money -= Balance.SchoolCost;
                p.Invested += Balance.SchoolCost;
                p.HasSchool = true;
                p.Loyalty = Balance.LoyaltyAfterSchoolDeal;
            }
            else
                p.Loyalty = Clamp(Balance.LoyaltyAfterFairDeal + (offer.PriceShare - 1) * 100);
            p.OnStrike = false;
            Notices.Add($"Жители {p.Type.Name} #{p.Id} согласились. Лояльность {Math.Round(p.Loyalty)}.");
            return ActionResult.Success;
        }

        static double Clamp(double loyalty) => Math.Min(100, Math.Max(0, loyalty));

        Planet OwnInhabited(int planetId, out ActionResult error)
        {
            var p = GetPlanet(planetId);
            error = ActionResult.Success;
            if (p == null || p.Owner != Owner.Player) error = ActionResult.Fail("Это не ваша планета");
            else if (!p.Inhabited) error = ActionResult.Fail("На планете нет жителей");
            return error.Ok ? p : null;
        }

        public ActionResult SetTax(int planetId, TaxLevel tax)
        {
            var p = OwnInhabited(planetId, out var error);
            if (p == null) return error;
            p.Tax = tax;
            return ActionResult.Success;
        }

        public ActionResult SetWages(int planetId, bool on)
        {
            var p = OwnInhabited(planetId, out var error);
            if (p == null) return error;
            p.PaysWages = on;
            return ActionResult.Success;
        }

        /// <summary>Праздник: разово поднимает лояльность.</summary>
        public ActionResult HoldFestival(int planetId)
        {
            var p = OwnInhabited(planetId, out var error);
            if (p == null) return error;
            if (Money < Balance.FestivalCost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;
            Money -= Balance.FestivalCost;
            p.Earned -= Balance.FestivalCost;
            p.Loyalty = Clamp(p.Loyalty + Balance.FestivalLoyalty);
            return ActionResult.Success;
        }

        /// <summary>Школа: навсегда добавляет лояльности каждый ход.</summary>
        public ActionResult BuildSchool(int planetId)
        {
            var p = OwnInhabited(planetId, out var error);
            if (p == null) return error;
            if (p.HasSchool) return ActionResult.Fail("Школа уже есть");
            if (Money < Balance.SchoolCost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;
            Money -= Balance.SchoolCost;
            p.Invested += Balance.SchoolCost;
            p.HasSchool = true;
            foreach (var r in _requests.Where(r => r.PlanetId == p.Id && r.Kind == RequestKind.School).ToList())
                CompleteRequest(r, p, Notices);
            return ActionResult.Success;
        }

        /// <summary>Выполнить просьбу жителей: ресурс берётся со склада биржи, доставка бесплатна.</summary>
        public ActionResult FulfillRequest(int requestId)
        {
            var r = _requests.FirstOrDefault(x => x.Id == requestId);
            if (r == null) return ActionResult.Fail("Просьбы нет");
            var p = GetPlanet(r.PlanetId);
            if (r.Kind == RequestKind.School) return ActionResult.Fail("Жители просят школу — постройте её на планете");
            if (_stock[(int)r.Resource] + 1e-9 < r.Amount) return ActionResult.Fail("Не хватает ресурса на складе биржи");
            _stock[(int)r.Resource] -= r.Amount;
            CompleteRequest(r, p, Notices);
            return ActionResult.Success;
        }

        void CompleteRequest(ResidentRequest r, Planet p, List<string> events)
        {
            _requests.Remove(r);
            p.Loyalty = Clamp(p.Loyalty + Balance.RequestLoyaltyReward);
            events.Add($"Жители {p.Type.Name} #{p.Id} довольны: просьба выполнена, лояльность {Math.Round(p.Loyalty)}.");
        }

        /// <summary>Как изменится лояльность за ход: налог, выкачивание ресурсов, школа, зарплаты.</summary>
        public double LoyaltyChangePerTurn(Planet p)
        {
            var b = Balance;
            double delta = b.TaxLoyalty[(int)p.Tax];
            if (p.Mine == MineState.Working && !p.IsDepleted && !p.OnStrike) delta -= b.LoyaltyMiningDrain;
            if (p.HasSchool) delta += b.SchoolLoyalty;
            if (p.PaysWages) delta += b.WagesLoyalty;
            return delta;
        }

        /// <summary>Налоги, зарплаты, лояльность, забастовки и просьбы жителей за ход.</summary>
        void UpdatePeople(TurnReport report)
        {
            var b = Balance;
            foreach (var p in PlayerPlanets.Where(x => x.Inhabited))
            {
                if (!p.OnStrike)
                {
                    double tax = b.TaxIncome[(int)p.Tax];
                    Money += tax;
                    p.Earned += tax;
                    report.Taxes += tax;
                }
                if (p.PaysWages)
                {
                    report.Wages += b.WagesCost;
                    p.Earned -= b.WagesCost;
                }

                p.Loyalty = Clamp(p.Loyalty + LoyaltyChangePerTurn(p));

                if (!p.OnStrike && p.Loyalty < b.StrikeBelow)
                {
                    p.OnStrike = true;
                    report.Events.Add($"Забастовка на {p.Type.Name} #{p.Id}: лояльность {Math.Round(p.Loyalty)}, добычи и налогов нет.");
                }
                else if (p.OnStrike && p.Loyalty >= b.StrikeEndsAt)
                {
                    p.OnStrike = false;
                    report.Events.Add($"Забастовка на {p.Type.Name} #{p.Id} закончилась.");
                }
            }

            foreach (var r in _requests.Where(r => r.DeadlineTurn <= Turn).ToList())
            {
                _requests.Remove(r);
                var p = GetPlanet(r.PlanetId);
                if (p == null || p.Owner != Owner.Player) continue;
                p.Loyalty = Clamp(p.Loyalty - b.RequestLoyaltyPenalty);
                report.Events.Add($"Жители {p.Type.Name} #{p.Id} обижены: просьбу не выполнили, лояльность {Math.Round(p.Loyalty)}.");
            }

            foreach (var p in PlayerPlanets.Where(x => x.Inhabited).ToList())
            {
                if (_requests.Any(r => r.PlanetId == p.Id) || !_rng.Chance(b.RequestChancePerPlanet)) continue;
                var r = new ResidentRequest { Id = _nextRequestId++, PlanetId = p.Id, DeadlineTurn = Turn + b.RequestTurns };
                if (!p.HasSchool && _rng.Chance(0.3))
                {
                    r.Kind = RequestKind.School;
                    report.Events.Add($"Жители {p.Type.Name} #{p.Id} просят построить школу за {b.RequestTurns} ходов.");
                }
                else
                {
                    r.Kind = RequestKind.Resource;
                    r.Resource = ResourceTypes.All[_rng.Range(0, ResourceTypes.Count)];
                    r.Amount = Math.Max(5, Math.Round(b.TypeOf(r.Resource).OutputPerTurn * _rng.Range(1.0, 3.0) / 5) * 5);
                    report.Events.Add($"Жители {p.Type.Name} #{p.Id} просят {r.Amount} ({r.Resource.RuName()}) за {b.RequestTurns} ходов.");
                }
                _requests.Add(r);
            }
        }

        public ActionResult BuildMine(int planetId)
        {
            var p = GetPlanet(planetId);
            if (p == null || p.Owner != Owner.Player) return ActionResult.Fail("Это не ваша планета");
            if (p.Mine != MineState.None) return ActionResult.Fail("Шахта уже есть");
            if (Money < p.Type.MineCost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;

            Money -= p.Type.MineCost;
            p.Invested += p.Type.MineCost;
            p.Mine = MineState.Building;
            p.BuildTurnsLeft = Balance.MineBuildTurns;
            return ActionResult.Success;
        }

        /// <summary>Продать планету по рыночной стоимости минус комиссия. Она уходит в реестр.</summary>
        public ActionResult SellPlanet(int planetId)
        {
            var p = GetPlanet(planetId);
            if (p == null || p.Owner != Owner.Player) return ActionResult.Fail("Это не ваша планета");
            var a = SpendAction();
            if (!a.Ok) return a;

            double proceeds = SalePrice(p);
            Money += proceeds;
            p.Earned += proceeds;
            ReleaseToRegistry(p);
            SoldPlanets.Add(p);
            return ActionResult.Success;
        }

        public double SalePrice(Planet p) => p.MarketValue(Balance) * (1 - Balance.ResaleCommission);

        void ReleaseToRegistry(Planet p)
        {
            p.Owner = Owner.Registry;
            p.FoundByProbe = false;
            p.Tax = TaxLevel.None;
            p.PaysWages = false;
            p.OnStrike = false;
            p.Loyalty = 50;
            p.Guarded = false;
            p.MagnateId = -1;
            _requests.RemoveAll(r => r.PlanetId == p.Id);
            _bribes.RemoveAll(x => x.PlanetId == p.Id);
        }

        // ------------------------------------------------------------------
        // Аукционы и магнаты
        // ------------------------------------------------------------------

        public double BidPrice(Planet p, double factor) => Math.Round(p.BasePrice() * factor);

        /// <summary>
        /// Шанс выиграть аукцион ставкой factor от базовой цены: каждый магнат может заинтересоваться
        /// и поставить случайно в своих пределах; выигрывает тот, кто поставил больше.
        /// </summary>
        public double AuctionWinChance(double factor)
        {
            double chance = 1;
            foreach (var m in ActiveMagnates)
            {
                double below = Math.Min(1, Math.Max(0, (factor - m.BidMin) / (m.BidMax - m.BidMin)));
                chance *= 1 - m.AuctionInterest + m.AuctionInterest * below;
            }
            return chance;
        }

        /// <summary>Ставка на аукционе. Тратит действие; если магнат перебьёт, планета уйдёт к нему.</summary>
        public ActionResult PlaceBid(int planetId, double factor, out bool won)
        {
            won = false;
            var p = GetPlanet(planetId);
            if (p == null || !p.SoldAtAuction) return ActionResult.Fail("Планета не продаётся с аукциона");
            if (p.IsDepleted) return ActionResult.Fail("Планета выработана");
            double bid = BidPrice(p, factor);
            if (Money < bid) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;

            Magnate rival = null;
            double rivalBid = 0;
            foreach (var m in ActiveMagnates)
            {
                if (!_rng.Chance(m.AuctionInterest)) continue;
                double x = p.BasePrice() * _rng.Range(m.BidMin, m.BidMax);
                if (x > rivalBid)
                {
                    rivalBid = x;
                    rival = m;
                }
            }

            if (bid > rivalBid)
            {
                won = true;
                AuctionsWon++;
                Acquire(p, bid);
                return ActionResult.Success;
            }

            AuctionsLost++;
            GiveToMagnate(p, rival);
            Notices.Add($"{rival.Name} перебил(а) вашу ставку {Math.Round(bid)}: {Math.Round(rivalBid)} за {p.Type.Name} #{p.Id}.");
            return ActionResult.Success;
        }

        void GiveToMagnate(Planet p, Magnate m)
        {
            _requests.RemoveAll(r => r.PlanetId == p.Id);
            _bribes.RemoveAll(x => x.PlanetId == p.Id);
            p.Owner = Owner.Ai;
            p.MagnateId = m?.Id ?? -1;
            p.Guarded = false;
            p.Tax = TaxLevel.None;
            p.PaysWages = false;
            p.OnStrike = false;
            if (p.Mine == MineState.None) p.Mine = MineState.Working;
        }

        /// <summary>Встречный подкуп: жители остаются с вами и становятся лояльнее. Действия не тратит.</summary>
        public ActionResult CounterBribe(int attemptId)
        {
            var x = _bribes.FirstOrDefault(b => b.Id == attemptId);
            if (x == null) return ActionResult.Fail("Подкупа нет");
            if (Money < Balance.CounterBribeCost) return ActionResult.Fail("Не хватает денег");
            var p = GetPlanet(x.PlanetId);
            Money -= Balance.CounterBribeCost;
            p.Earned -= Balance.CounterBribeCost;
            p.Loyalty = Clamp(p.Loyalty + Balance.CounterBribeLoyalty);
            _bribes.Remove(x);
            return ActionResult.Success;
        }

        public ActionResult SetGuards(int planetId, bool on)
        {
            var p = GetPlanet(planetId);
            if (p == null || p.Owner != Owner.Player) return ActionResult.Fail("Это не ваша планета");
            p.Guarded = on;
            return ActionResult.Success;
        }

        /// <summary>Ваш слух о магнате: несколько ходов он не покупает и не пакостит. Грязно: репутация и риск штрафа.</summary>
        public ActionResult SpreadRumor(int magnateId)
        {
            var m = GetMagnate(magnateId);
            if (m == null) return ActionResult.Fail("Нет такого магната");
            if (Money < Balance.PlayerRumorCost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;
            Money -= Balance.PlayerRumorCost;
            m.QuietUntilTurn = Math.Max(m.QuietUntilTurn, Turn + 1) + Balance.PlayerRumorQuietTurns - 1;
            ChangeReputation(Balance.PlayerRumorReputation);
            Notices.Add($"Слух сработал: {m.Name} до хода {m.QuietUntilTurn} отбивается и не мешает вам.");
            MaybeFine();
            return ActionResult.Success;
        }

        public double MagnateBribeCost(Planet p) => Math.Round(p.BasePrice() * Balance.PlayerBribeShare);

        /// <summary>Подкуп жителей планеты магната. Удастся — планета ваша. Грязно: репутация и риск штрафа.</summary>
        public ActionResult BribeMagnateResidents(int planetId, out bool success)
        {
            success = false;
            var p = GetPlanet(planetId);
            if (p == null || p.Owner != Owner.Ai) return ActionResult.Fail("Это не планета магната");
            if (!p.Inhabited || p.Knowledge == Knowledge.Silhouette) return ActionResult.Fail("На планете нет известных вам жителей");
            if (p.IsDepleted) return ActionResult.Fail("Планета выработана");
            double cost = MagnateBribeCost(p);
            if (Money < cost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;
            Money -= cost;
            ChangeReputation(Balance.PlayerBribeReputation);
            if (_rng.Chance(Balance.PlayerBribeChance))
            {
                success = true;
                var m = GetMagnate(p.MagnateId);
                p.Owner = Owner.Player;
                p.MagnateId = -1;
                p.BoughtOnTurn = Turn;
                p.Invested += cost;
                p.Loyalty = Balance.PlayerBribeLoyalty;
                if (p.Knowledge != Knowledge.Explored)
                {
                    p.Knowledge = Knowledge.Explored;
                    Notices.Add(DescribeExploration(p));
                }
                LootRuins(p, Notices);
                Notices.Add($"Жители {p.Type.Name} #{p.Id} ушли от {m?.Name ?? "магната"} к вам.");
            }
            else
                Notices.Add($"Жители {p.Type.Name} #{p.Id} взяли деньги и остались у магната.");
            MaybeFine();
            return ActionResult.Success;
        }

        void ChangeReputation(double delta) => Reputation = Math.Min(100, Math.Max(0, Reputation + delta));

        void MaybeFine()
        {
            if (!_rng.Chance(Balance.FineChance)) return;
            Money -= Balance.FineAmount;
            FinesPaid += Balance.FineAmount;
            ChangeReputation(Balance.FineReputation);
            Notices.Add($"Галактический реестр раскрыл грязное дело: штраф {Math.Round(Balance.FineAmount)}, репутация {Math.Round(Reputation)}.");
        }

        // ------------------------------------------------------------------
        // Зонды
        // ------------------------------------------------------------------

        /// <summary>Откуда полетит зонд к точке: ближайшая ваша планета или биржа.</summary>
        public (double x, double y, double distance) LaunchOrigin(double x, double y)
        {
            double bx = Balance.ExchangeX, by = Balance.ExchangeY;
            double best = Geometry.Distance(bx, by, x, y);
            foreach (var p in PlayerPlanets)
            {
                double d = Geometry.Distance(p.MapX, p.MapY, x, y);
                if (d < best)
                {
                    best = d;
                    bx = p.MapX;
                    by = p.MapY;
                }
            }
            return (bx, by, best);
        }

        public bool InRange(ProbeType t, double x, double y) => LaunchOrigin(x, y).distance <= t.Range + 1e-9;

        /// <summary>Через сколько ходов будет результат: 1 — уже к следующему ходу.</summary>
        public int ProbeTurns(ProbeType t, double x, double y, ProbeMission mission)
        {
            int flight = Math.Max(1, (int)Math.Ceiling(LaunchOrigin(x, y).distance / t.Speed));
            return flight + (mission == ProbeMission.Explore ? Balance.ExploreTurnsOnSite : 0);
        }

        /// <summary>Летит ли уже зонд разведывать эту планету.</summary>
        public bool ProbeHeadingTo(int planetId) => _probes.Any(x => x.TargetPlanetId == planetId);

        /// <summary>Скан области: все планеты в радиусе станут просканированными, и, может быть, найдётся скрытая.</summary>
        public ActionResult LaunchScan(ProbeType t, double x, double y)
        {
            if (!InRange(t, x, y)) return ActionResult.Fail($"«{t.Name}» не долетит: слишком далеко от ваших планет и биржи");
            return Launch(t, ProbeMission.ScanArea, x, y, -1);
        }

        /// <summary>Полная разведка планеты: точный запас и сюрпризы.</summary>
        public ActionResult LaunchExplore(ProbeType t, int planetId)
        {
            var p = GetPlanet(planetId);
            if (p == null) return ActionResult.Fail("Нет такой планеты");
            if (p.Knowledge == Knowledge.Explored || p.Owner == Owner.Player) return ActionResult.Fail("Планета уже разведана");
            if (ProbeHeadingTo(planetId)) return ActionResult.Fail("К планете уже летит зонд");
            if (!InRange(t, p.MapX, p.MapY)) return ActionResult.Fail($"«{t.Name}» не долетит: слишком далеко от ваших планет и биржи");
            return Launch(t, ProbeMission.Explore, p.MapX, p.MapY, planetId);
        }

        ActionResult Launch(ProbeType t, ProbeMission mission, double x, double y, int planetId)
        {
            if (Money < t.Cost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;

            Money -= t.Cost;
            var origin = LaunchOrigin(x, y);
            _probes.Add(new Probe
            {
                Id = _nextProbeId++,
                Type = t,
                Mission = mission,
                FromX = origin.x,
                FromY = origin.y,
                TargetX = x,
                TargetY = y,
                TargetPlanetId = planetId,
                LaunchTurn = Turn,
                ResolveTurn = Turn + ProbeTurns(t, x, y, mission) - 1,
            });
            return ActionResult.Success;
        }

        void ResolveProbes(TurnReport report)
        {
            foreach (var probe in _probes.Where(x => x.ResolveTurn <= Turn).ToList())
            {
                _probes.Remove(probe);
                if (probe.Mission == ProbeMission.ScanArea) ResolveScan(probe, report);
                else ResolveExplore(probe, report);
            }
        }

        void ResolveScan(Probe probe, TurnReport report)
        {
            int scanned = 0;
            foreach (var p in _planets)
            {
                if (p.Knowledge != Knowledge.Silhouette) continue;
                if (Geometry.Distance(p.MapX, p.MapY, probe.TargetX, probe.TargetY) > probe.Type.ScanRadius) continue;
                p.Knowledge = Knowledge.Scanned;
                scanned++;
            }
            string line = $"Зонд «{probe.Type.Name}» просканировал область: новых планет в скане — {scanned}";
            if (_rng.Chance(probe.Type.FindChance))
            {
                var found = GeneratePlanet(RandomType());
                PlaceOnMap(found, probe.TargetX, probe.TargetY, probe.Type.ScanRadius);
                found.FoundByProbe = true;
                found.Knowledge = Knowledge.Scanned;
                _planets.Add(found);
                line += $". Найдена скрытая планета {found.Type.Name} #{found.Id}: её видите только вы";
            }
            report.Events.Add(line + ".");
        }

        void ResolveExplore(Probe probe, TurnReport report)
        {
            var p = GetPlanet(probe.TargetPlanetId);
            if (p == null) return;
            p.Knowledge = Knowledge.Explored;
            report.Events.Add(DescribeExploration(p));
            if (p.Owner == Owner.Player) LootRuins(p, report.Events);
        }

        string DescribeExploration(Planet p)
        {
            string line = $"Разведка {p.Type.Name} #{p.Id}: запас {Math.Round(p.Reserve)}, реестр оценивал в {Math.Round(p.EstimatedReserve)}";
            if (p.Reserve < 0.6 * p.EstimatedReserve) line += " — подвох, реестр сильно завысил запас";
            if (p.HasUnlootedRuins) line += $"; руины с артефактами на {Math.Round(p.RuinsValue)}";
            if (p.Inhabited) line += "; планета обитаема, продают её сами жители";
            return line + ".";
        }

        /// <summary>Руины достаются владельцу разведанной планеты один раз.</summary>
        void LootRuins(Planet p, List<string> events)
        {
            if (p.Owner != Owner.Player || p.Knowledge != Knowledge.Explored || !p.HasUnlootedRuins) return;
            p.RuinsLooted = true;
            Money += p.RuinsValue;
            p.Earned += p.RuinsValue;
            events.Add($"Артефакты из руин на {p.Type.Name} #{p.Id} проданы за {Math.Round(p.RuinsValue)}.");
        }

        /// <summary>Нанять менеджера. Даёт действие со следующего хода.</summary>
        public ActionResult HireManager()
        {
            if (Managers >= Balance.MaxManagers) return ActionResult.Fail("Больше менеджеров нанять нельзя");
            var a = SpendAction();
            if (!a.Ok) return a;
            Managers++;
            return ActionResult.Success;
        }

        public ActionResult FireManager()
        {
            if (Managers == 0) return ActionResult.Fail("Менеджеров нет");
            Managers--;
            return ActionResult.Success;
        }

        // ------------------------------------------------------------------
        // Бесплатные действия
        // ------------------------------------------------------------------

        public ActionResult SellResource(Resource r, double amount)
        {
            if (amount <= 0) return ActionResult.Fail("Нечего продавать");
            if (_stock[(int)r] + 1e-9 < amount) return ActionResult.Fail("Столько ресурса нет на складе");
            _stock[(int)r] -= amount;
            Money += Market.Sell(r, amount);
            return ActionResult.Success;
        }

        /// <summary>Отправить заказ со склада биржи. Платят сразу, доставку оплачивает игрок.</summary>
        public ActionResult FulfillOrder(int orderId)
        {
            var o = _orders.FirstOrDefault(x => x.Id == orderId);
            if (o == null) return ActionResult.Fail("Заказа нет");
            if (_stock[(int)o.Resource] + 1e-9 < o.Amount) return ActionResult.Fail("Не хватает ресурса на складе биржи");
            if (!OrderReachable(o)) return ActionResult.Fail("Груз не успеет к сроку");
            _stock[(int)o.Resource] -= o.Amount;
            Money += OrderNetPayout(o);
            ChangeReputation(Balance.ReputationPerOrder);
            _orders.Remove(o);
            return ActionResult.Success;
        }

        public ActionResult TakeLoan(double amount)
        {
            if (amount <= 0) return ActionResult.Fail("Сумма должна быть больше нуля");
            if (amount > AvailableCredit + 1e-9) return ActionResult.Fail("Превышен кредитный лимит");
            Debt += amount;
            Money += amount;
            return ActionResult.Success;
        }

        public ActionResult RepayLoan(double amount)
        {
            amount = Math.Min(amount, Debt);
            if (amount <= 0) return ActionResult.Fail("Долга нет");
            if (Money < amount) return ActionResult.Fail("Не хватает денег");
            Debt -= amount;
            Money -= amount;
            return ActionResult.Success;
        }

        public void SetInsurance(bool on) => Insured = on;

        // ------------------------------------------------------------------
        // Конец хода
        // ------------------------------------------------------------------

        public TurnReport EndTurn()
        {
            var report = new TurnReport { Turn = Turn };

            ProducePlayer(report);
            ProduceAi();
            DeliverShipments();
            UpdatePeople(report);

            // Расходы считаются по состоянию на конец хода. Доставка уже посчитана при добыче.
            report.Upkeep = UpkeepPerTurn;
            report.Salaries = Managers * Balance.ManagerSalary;
            report.Interest = Debt * Balance.InterestPerTurn;
            report.Insurance = InsurancePerTurn;
            report.Guards = GuardsPerTurn;
            // Зарплаты жителям уже посчитаны в UpdatePeople.
            double multiplier = Balance.UpkeepMultiplier(UpkeepCurvePlanets);
            foreach (var p in PlayerPlanets)
                p.Earned -= p.BaseUpkeep(Balance) * multiplier;
            Money -= report.TotalCosts;

            if (Money < 0) CallCollectors(report);

            ResolveProbes(report);
            ResolveBribes(report);
            UpdateOrders(report);
            Discover(report);
            AiTurn(report);
            // Репутация понемногу возвращается к 50.
            if (Reputation < 50) ChangeReputation(Math.Min(Balance.ReputationRecovery, 50 - Reputation));
            else if (Reputation > 50) ChangeReputation(-Math.Min(Balance.ReputationRecovery, Reputation - 50));
            Market.Tick(_rng);

            Turn++;
            ActionsLeft = ActionsPerTurn;
            return report;
        }

        void ProducePlayer(TurnReport report)
        {
            foreach (var p in PlayerPlanets)
            {
                if (p.Mine == MineState.Building)
                {
                    if (--p.BuildTurnsLeft <= 0) p.Mine = MineState.Working;
                    continue;
                }
                if (p.Mine != MineState.Working || p.IsDepleted || p.OnStrike) continue;

                if (p.AccidentTurnsLeft == 0 && _rng.Chance(Balance.AccidentChance))
                {
                    p.AccidentTurnsLeft = Balance.AccidentTurns;
                    report.Accidents++;
                    report.Events.Add($"Авария на планете {p.Type.Name} #{p.Id}");
                }

                double amount = Math.Min(p.Type.OutputPerTurn, p.Reserve);
                if (p.AccidentTurnsLeft > 0)
                {
                    p.AccidentTurnsLeft--;
                    if (Insured)
                    {
                        double payout = amount * Market.Price(p.Resource);
                        Money += payout;
                        report.InsurancePayout += payout;
                    }
                    continue;
                }

                p.Reserve -= amount;
                p.Earned += amount * Market.Price(p.Resource);
                Ship(p, amount, report);
            }
        }

        /// <summary>Добытое летит на биржу: стоит денег и, с дальних планет, времени.</summary>
        void Ship(Planet p, double amount, TurnReport report)
        {
            double cost = ShippingCost(p.Resource, amount, DistanceToExchange(p));
            report.Shipping += cost;
            p.Earned -= cost;
            int turns = ShipTurns(p);
            if (turns == 0) _stock[(int)p.Resource] += amount;
            else _shipments.Add(new Shipment { Resource = p.Resource, Amount = amount, ResolveTurn = Turn + turns });
        }

        void DeliverShipments()
        {
            foreach (var x in _shipments.Where(x => x.ResolveTurn <= Turn).ToList())
            {
                _stock[(int)x.Resource] += x.Amount;
                _shipments.Remove(x);
            }
        }

        void ProduceAi()
        {
            foreach (var p in _planets)
                if (p.Owner == Owner.Ai && p.Mine == MineState.Working && !p.IsDepleted)
                    p.Reserve -= Math.Min(p.Type.OutputPerTurn, p.Reserve);
        }

        /// <summary>
        /// Невыплата: недостача уходит в долг. Если долг выше кредитного лимита,
        /// коллекторы забирают самые дорогие планеты, пока долг не войдёт в лимит.
        /// Игра продолжается.
        /// </summary>
        void CallCollectors(TurnReport report)
        {
            Debt += -Money;
            Money = 0;
            while (Debt > CreditLimit + 1e-9)
            {
                var best = PlayerPlanets.OrderByDescending(p => p.MarketValue(Balance)).FirstOrDefault();
                if (best == null || best.MarketValue(Balance) <= 0) break;
                double proceeds = SalePrice(best);
                Debt -= proceeds;
                best.Earned += proceeds;
                ReleaseToRegistry(best);
                SoldPlanets.Add(best);
                report.Seizures++;
                TotalSeizures++;
                report.Events.Add($"Коллекторы забрали планету {best.Type.Name} #{best.Id}");
            }
            if (Debt < 0)
            {
                Money += -Debt;
                Debt = 0;
            }
        }

        void UpdateOrders(TurnReport report)
        {
            // Заказ снимается, когда груз уже не успеет даже при отправке в следующем ходу.
            _orders.RemoveAll(o => Turn + 1 + DeliveryTurns(o) > o.DeadlineTurn);
            // Репутация: при 0 заказов вдвое меньше, при 100 — в полтора раза больше.
            if (!_rng.Chance(Balance.OrderChancePerTurn * (0.5 + Reputation / 100))) return;

            var r = ResourceTypes.All[_rng.Range(0, ResourceTypes.Count)];
            var type = Balance.TypeOf(r);
            double amount = type.OutputPerTurn * _rng.Range(Balance.OrderMinTurnsOfOutput, Balance.OrderMaxTurnsOfOutput + 1);
            amount = Math.Max(1, Math.Round(amount / 5) * 5);
            // Заказчик — на краю сектора.
            double along = _mapRng.NextDouble();
            int side = _mapRng.Range(0, 4);
            double x = side == 0 ? 0 : side == 1 ? 1 : along;
            double y = side == 2 ? 0 : side == 3 ? 1 : along;
            var o = new Order(_nextOrderId++, Customers[_rng.Range(0, Customers.Length)], r, amount, Turn + Balance.OrderDeadlineTurns, x, y);
            _orders.Add(o);
            report.Events.Add($"{o.Customer}: нужно {o.Amount} ({r.RuName()}) за {Balance.OrderDeadlineTurns} ходов, с доставкой");
        }

        /// <summary>Случайное открытие: новая планета попадает в реестр, её видят все, в том числе ИИ.</summary>
        void Discover(TurnReport report)
        {
            if (!_rng.Chance(Balance.DiscoveryChancePerTurn)) return;
            var p = GeneratePlanet(RandomType());
            _planets.Add(p);
            report.Events.Add($"Открыта новая планета {p.Type.Name} #{p.Id}");
        }

        /// <summary>Подкуп, о котором предупредили: если лояльность так и не поднялась, планета уходит к магнату.</summary>
        void ResolveBribes(TurnReport report)
        {
            foreach (var x in _bribes.Where(b => b.ResolveTurn <= Turn).ToList())
            {
                _bribes.Remove(x);
                var p = GetPlanet(x.PlanetId);
                var m = GetMagnate(x.MagnateId);
                if (p == null || p.Owner != Owner.Player) continue;
                if (p.Loyalty < Balance.BribeSuccessLoyalty)
                {
                    GiveToMagnate(p, m);
                    PlanetsLostToBribes++;
                    report.Events.Add($"{m.Name} подкупил(а) жителей {p.Type.Name} #{p.Id}: планета теперь у магната.");
                }
                else
                    report.Events.Add($"Жители {p.Type.Name} #{p.Id} отказали {m.Name}: лояльность {Math.Round(p.Loyalty)}.");
            }
        }

        /// <summary>Магнаты покупают планеты, иногда перепродают свои и пакостят игроку.</summary>
        void AiTurn(TurnReport report)
        {
            var active = ActiveMagnates.ToList();
            if (active.Count > 0 && _rng.Chance(Balance.AiBuyChance))
            {
                var buyer = active[_rng.Range(0, active.Count)];
                var best = PlanetsForSale
                    .Where(p => !p.FoundByProbe)
                    // ИИ знает о планетах то же, что реестр.
                    .OrderByDescending(p => p.EstimatedValue(Balance) / Math.Max(1, p.ListPrice(Balance)))
                    .FirstOrDefault();
                if (best != null && best.EstimatedValue(Balance) > 0)
                {
                    GiveToMagnate(best, buyer);
                    report.Events.Add($"{buyer.Name} купил(а) планету {best.Type.Name} #{best.Id}.");
                }
            }

            if (_rng.Chance(Balance.AiResaleChance))
            {
                var aiPlanets = _planets.Where(p => p.Owner == Owner.Ai && !p.IsDepleted).ToList();
                if (aiPlanets.Count > 0)
                {
                    var p = aiPlanets[_rng.Range(0, aiPlanets.Count)];
                    var seller = GetMagnate(p.MagnateId);
                    p.Owner = Owner.Registry;
                    p.MagnateId = -1;
                    report.Events.Add($"{seller?.Name ?? "ИИ-магнат"} выставил(а) на продажу планету {p.Type.Name} #{p.Id}.");
                }
            }

            foreach (var m in active)
                if (_rng.Chance(m.TrickChance * MagnateAttention))
                    PlayTrick(m, PickTrick(m), report);
        }

        /// <summary>Насколько магнаты замечают игрока: от 0 до 1 по числу его планет.</summary>
        public double MagnateAttention =>
            Balance.MagnateAttentionPlanets <= 0 ? 1 : Math.Min(1, PlayerPlanetCount / (double)Balance.MagnateAttentionPlanets);


        TrickKind PickTrick(Magnate m)
        {
            double roll = _rng.NextDouble() * m.TrickWeights.Sum();
            for (int i = 0; i < m.TrickWeights.Length; i++)
            {
                roll -= m.TrickWeights[i];
                if (roll < 0) return (TrickKind)i;
            }
            return TrickKind.Rumor;
        }

        void PlayTrick(Magnate m, TrickKind kind, TurnReport report)
        {
            switch (kind)
            {
                case TrickKind.Bribe:
                {
                    var target = PlayerPlanets
                        .Where(p => p.Inhabited && !p.IsDepleted && !_bribes.Any(b => b.PlanetId == p.Id)
                                    && p.Loyalty < (p.Guarded ? Balance.GuardedBribeLoyalty : Balance.BribeTargetLoyalty))
                        .OrderBy(p => p.Loyalty)
                        .FirstOrDefault();
                    if (target == null) return;
                    _bribes.Add(new BribeAttempt { Id = _nextBribeId++, PlanetId = target.Id, MagnateId = m.Id, ResolveTurn = Turn + Balance.BribeWarningTurns });
                    report.Events.Add($"{m.Name} подкупает жителей {target.Type.Name} #{target.Id}! Через {Balance.BribeWarningTurns} хода они уйдут, " +
                                      $"если лояльность не поднимется до {Math.Round(Balance.BribeSuccessLoyalty)}.");
                    return;
                }
                case TrickKind.Rumor:
                {
                    // Бьёт по ресурсу, которого у игрока больше всего на складе и в добыче.
                    var produced = PlayerPlanets.Where(p => p.Mine == MineState.Working && !p.IsDepleted).Select(p => p.Resource).ToList();
                    if (produced.Count == 0) return;
                    var r = ResourceTypes.All
                        .OrderByDescending(x => _stock[(int)x] * Balance.BasePrice(x) + produced.Count(y => y == x) * Balance.TypeOf(x).OutputPerTurn * Balance.BasePrice(x))
                        .First();
                    Market.Shock(r, 1 - Balance.RumorPriceDrop);
                    report.Events.Add($"{m.Name} распускает слух, что {r.RuName()} радиоактивен: цена упала на {Math.Round(Balance.RumorPriceDrop * 100)}%.");
                    return;
                }
                case TrickKind.PoachOrder:
                {
                    if (_orders.Count == 0) return;
                    var o = _orders[_rng.Range(0, _orders.Count)];
                    _orders.Remove(o);
                    report.Events.Add($"{m.Name} переманил(а) заказчика: {o.Customer} больше не ждёт {o.Amount} ({o.Resource.RuName()}).");
                    return;
                }
                case TrickKind.PoachManager:
                {
                    if (Managers == 0) return;
                    Managers--;
                    report.Events.Add($"{m.Name} переманил(а) вашего менеджера. Действий на ход станет меньше.");
                    return;
                }
            }
        }
    }
}
