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
        public int Accidents;
        /// <summary>Сколько планет забрали коллекторы.</summary>
        public int Seizures;
        public readonly List<string> Events = new List<string>();

        public double TotalCosts => Upkeep + Salaries + Interest + Insurance + Shipping + Wages;
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
        readonly double[] _stock = new double[ResourceTypes.Count];
        int _nextPlanetId;
        int _nextOrderId;
        int _nextProbeId;
        int _nextRequestId;

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

        // Статистика для симулятора и плейтестов.
        public int TotalSeizures { get; private set; }
        public int TurnsActionsExhausted { get; private set; }
        public readonly List<Planet> SoldPlanets = new List<Planet>();

        public IReadOnlyList<Planet> AllPlanets => _planets;
        public IReadOnlyList<Order> Orders => _orders;
        public IReadOnlyList<Probe> Probes => _probes;
        public IReadOnlyList<Shipment> Shipments => _shipments;
        public IReadOnlyList<ResidentRequest> Requests => _requests;
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
        }

        public static Game New(Balance balance, ulong seed)
        {
            var g = new Game(balance, seed);
            g.Money = balance.StartMoney;
            g.Debt = balance.StartDebt;

            for (int i = 0; i < balance.SectorPlanets; i++)
                g._planets.Add(g.GeneratePlanet(g.RandomType()));

            int aiCount = (int)Math.Round(balance.SectorPlanets * balance.AiStartShare);
            foreach (var p in g._planets.OrderBy(_ => g._rng.NextULong()).Take(aiCount))
            {
                p.Owner = Owner.Ai;
                p.Mine = MineState.Working;
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

        public double CostsPerTurn =>
            UpkeepPerTurn + Managers * Balance.ManagerSalary + Debt * Balance.InterestPerTurn + InsurancePerTurn + ShippingPerTurn + WagesPerTurn;

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

        public double OfferChance(Planet p, Offer o)
        {
            if (o.WithSchool) return Balance.SchoolDealChance;
            double chance = Balance.OfferChanceAtFullPrice + (o.PriceShare - 1) * Balance.OfferChancePerShare;
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
            _requests.RemoveAll(r => r.PlanetId == p.Id);
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
            // Зарплаты жителям уже посчитаны в UpdatePeople.
            double multiplier = Balance.UpkeepMultiplier(UpkeepCurvePlanets);
            foreach (var p in PlayerPlanets)
                p.Earned -= p.BaseUpkeep(Balance) * multiplier;
            Money -= report.TotalCosts;

            if (Money < 0) CallCollectors(report);

            ResolveProbes(report);
            UpdateOrders(report);
            Discover(report);
            AiTurn(report);
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
            if (!_rng.Chance(Balance.OrderChancePerTurn)) return;

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

        /// <summary>ИИ выкупает лучшую свободную планету и иногда перепродаёт свою.</summary>
        void AiTurn(TurnReport report)
        {
            if (_rng.Chance(Balance.AiBuyChance))
            {
                var best = PlanetsForSale
                    .Where(p => !p.FoundByProbe)
                    // ИИ знает о планетах то же, что реестр.
                    .OrderByDescending(p => p.EstimatedValue(Balance) / Math.Max(1, p.ListPrice(Balance)))
                    .FirstOrDefault();
                if (best != null && best.EstimatedValue(Balance) > 0)
                {
                    best.Owner = Owner.Ai;
                    if (best.Mine == MineState.None) best.Mine = MineState.Working;
                    report.Events.Add($"ИИ-магнат купил планету {best.Type.Name} #{best.Id}");
                }
            }

            if (_rng.Chance(Balance.AiResaleChance))
            {
                var aiPlanets = _planets.Where(p => p.Owner == Owner.Ai && !p.IsDepleted).ToList();
                if (aiPlanets.Count > 0)
                {
                    var p = aiPlanets[_rng.Range(0, aiPlanets.Count)];
                    p.Owner = Owner.Registry;
                    report.Events.Add($"ИИ-магнат выставил на продажу планету {p.Type.Name} #{p.Id}");
                }
            }
        }
    }
}
