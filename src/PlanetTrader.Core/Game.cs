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
        /// <summary>Умственный труд образованных жителей (доход).</summary>
        public double Intellect;
        /// <summary>Охрана планет от подкупа.</summary>
        public double Guards;
        public int Accidents;
        /// <summary>Сколько планет забрали коллекторы.</summary>
        public int Seizures;
        /// <summary>События хода. У игроков кооператива список общий.</summary>
        public readonly List<string> Events;
        /// <summary>Важное для этой корпорации (у каждого игрока своё): интерфейс показывает окном. Строки — и в Events.</summary>
        public readonly List<Alert> Alerts = new List<Alert>();
        /// <summary>Сезон шоу кончился в этот ход — итоги для сцены с кубком; null — сезон идёт.</summary>
        public SeasonSummary Season;

        public TurnReport(List<string> events = null)
        {
            Events = events ?? new List<string>();
        }

        public double TotalCosts => Upkeep + Salaries + Interest + Insurance + Shipping + Wages + Guards;
    }

    /// <summary>
    /// Состояние партии и все правила экономики. Один игрок против ИИ.
    /// Действия (покупка, шахта, продажа планеты, запуск зонда, найм) тратят очки действий,
    /// торговля на бирже, заказы и кредит — бесплатны.
    /// </summary>
    public sealed partial class Game
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
        /// <summary>Отдельный генератор для сезонных событий.</summary>
        readonly Rng _eventRng;
        int _nextEventTurn;
        GalacticEvent _lastEvent;
        readonly List<Planet> _planets = new List<Planet>();
        /// <summary>Открытые секторы: у них есть планеты и магнаты. Стартовый открыт всегда.</summary>
        readonly HashSet<int> _openSectors = new HashSet<int> { 0 };
        /// <summary>Типы планет каждого сектора: в богатых — с множителями.</summary>
        [NonSerialized] readonly Dictionary<int, PlanetType[]> _sectorTypes = new Dictionary<int, PlanetType[]>();
        readonly List<Order> _orders = new List<Order>();
        readonly List<Probe> _probes = new List<Probe>();
        List<Shipment> _shipments => _current.Shipments;
        readonly List<ResidentRequest> _requests = new List<ResidentRequest>();
        readonly List<Magnate> _magnates;
        readonly List<BribeAttempt> _bribes = new List<BribeAttempt>();
        double[] _stock => _current.Stock;
        readonly List<Corporation> _corps = new List<Corporation>();
        /// <summary>Корпорация, от имени которой сейчас выполняются действия и считается её часть хода.</summary>
        Corporation _current;
        int _nextPlanetId;
        int _nextOrderId;
        int _nextProbeId;
        int _nextRequestId;
        int _nextBribeId;
        int _nextManagerId;
        /// <summary>Отдельный генератор для кандидатов на работу.</summary>
        readonly Rng _staffRng;

        /// <summary>
        /// Сообщения о том, что случилось во время действий игрока (например, нашлись руины при покупке).
        /// Интерфейс забирает их и очищает список.
        /// </summary>
        [NonSerialized] public readonly List<string> Notices = new List<string>();

        public int Turn { get; private set; } = 1;

        /// <summary>Все корпорации партии: игроков (сначала) и магнатов.</summary>
        public IReadOnlyList<Corporation> Corporations => _corps;
        /// <summary>Корпорации игроков. В одиночной игре — одна.</summary>
        public IEnumerable<Corporation> Players => _corps.Where(c => !c.IsAi);
        /// <summary>Корпорация, от имени которой выполняются действия. Деньги, планеты, склад и т. п. ниже — её.</summary>
        public Corporation Current => _current;

        /// <summary>Действовать от имени другого игрока (кооператив): его деньги, планеты, действия.</summary>
        public void SetCurrent(int corporationId)
        {
            var c = _corps.FirstOrDefault(x => x.Id == corporationId && !x.IsAi);
            if (c == null) throw new ArgumentException("Нет такого игрока: " + corporationId);
            _current = c;
        }

        /// <summary>Своя планета — принадлежит текущей корпорации.</summary>
        public bool IsMine(Planet p) => p != null && p.OwnerCorp == _current;

        public double Money { get => _current.Money; private set => _current.Money = value; }
        public double Debt { get => _current.Debt; private set => _current.Debt = value; }
        /// <summary>Сколько нанято менеджеров (без директора).</summary>
        public int Managers => _current.Staff.Count(m => !m.IsDirector);
        /// <summary>Сколько менеджеров можно держать на нынешнем уровне корпорации.</summary>
        public int MaxManagers => LevelInfo.Managers;
        /// <summary>Сколько действий осталось у всей команды.</summary>
        public int ActionsLeft => _current.Staff.Sum(m => m.ActionsLeft);
        public IReadOnlyList<Manager> Staff => _current.Staff;
        public IReadOnlyList<Manager> Candidates => _current.Candidates;
        public bool Insured { get => _current.Insured; private set => _current.Insured = value; }
        /// <summary>Репутация 0–100: грязные дела её портят, выполненные заказы чинят.</summary>
        public double Reputation { get => _current.Reputation; private set => _current.Reputation = value; }
        /// <summary>Сезонное событие, которое идёт сейчас; null — затишье.</summary>
        public ActiveEvent CurrentEvent { get; private set; }
        /// <summary>С какого хода может начаться следующее событие.</summary>
        public int NextEventTurn => _nextEventTurn;

        // Статистика для симулятора и плейтестов.
        public int TotalSeizures { get => _current.TotalSeizures; private set => _current.TotalSeizures = value; }
        public int TurnsActionsExhausted { get => _current.TurnsActionsExhausted; private set => _current.TurnsActionsExhausted = value; }
        public int PlanetsLostToBribes { get => _current.PlanetsLostToBribes; private set => _current.PlanetsLostToBribes = value; }
        public int AuctionsWon { get => _current.AuctionsWon; private set => _current.AuctionsWon = value; }
        public int AuctionsLost { get => _current.AuctionsLost; private set => _current.AuctionsLost = value; }
        public double FinesPaid { get => _current.FinesPaid; private set => _current.FinesPaid = value; }
        public readonly List<Planet> SoldPlanets = new List<Planet>();

        public IReadOnlyList<Planet> AllPlanets => _planets;
        public IReadOnlyList<Order> Orders => _orders;
        public IReadOnlyList<Probe> Probes => _probes;
        public IReadOnlyList<Shipment> Shipments => _shipments;
        public IReadOnlyList<ResidentRequest> Requests => _requests;
        public IReadOnlyList<Magnate> Magnates => _magnates;
        public IReadOnlyList<BribeAttempt> Bribes => _bribes;
        readonly List<HostileClaim> _claims = new List<HostileClaim>();
        int _nextClaimId;
        public IReadOnlyList<HostileClaim> Claims => _claims;

        /// <summary>Против текущего игрока действует картель: до какого хода и насколько меньше платит биржа.</summary>
        public bool CartelActive => _current.CartelUntilTurn >= Turn;
        public double CartelCut => !CartelActive ? 0 : Balance.CartelPriceCut * (Reputation >= Balance.CartelReputationShield ? 0.5 : 1);

        public Magnate GetMagnate(int id) => _magnates.FirstOrDefault(m => m.Id == id);

        /// <summary>Магнаты, которые не отбиваются от слухов.</summary>
        IEnumerable<Magnate> ActiveMagnates => _magnates.Where(m => Turn >= m.QuietUntilTurn);
        public IEnumerable<Planet> PlayerPlanets => _planets.Where(p => p.OwnerCorp == _current);
        public int PlayerPlanetCount => _planets.Count(p => p.OwnerCorp == _current);

        /// <summary>Планеты, которые игрок может купить прямо сейчас.</summary>
        public IEnumerable<Planet> PlanetsForSale => _planets.Where(p => p.Owner == Owner.Registry && !p.IsDepleted && HasLicense(p.Sector) && !IsLot(p));

        public double Stock(Resource r) => _stock[(int)r];

        public double InTransit(Resource r) => _shipments.Where(x => x.Resource == r).Sum(x => x.Amount);

        public int ActionsPerTurn => _current.Staff.Sum(m => m.ActionsPerTurn);

        void ResetActions()
        {
            foreach (var m in _current.Staff) m.ActionsLeft = m.ActionsPerTurn;
        }

        /// <summary>Зарплаты команды за ход.</summary>
        public double SalariesPerTurn => _current.Staff.Sum(m => m.Salary);

        /// <summary>Зерно партии: из него — карта и облик планет.</summary>
        public ulong Seed { get; private set; }

        /// <summary>Сохранить партию целиком (см. <see cref="SaveGame"/>).</summary>
        public string Save() => SaveGame.Save(this);

        /// <summary>Партия из сохранения; balance — нынешний баланс игры.</summary>
        public static Game Load(string json, Balance balance) => SaveGame.Load(json, balance);

        Game(Balance balance, ulong seed)
        {
            Balance = balance;
            Seed = seed;
            _rng = new Rng(seed);
            _mapRng = new Rng(seed ^ 0x5EC7_0A11UL);
            _surveyRng = new Rng(seed ^ 0x50_17E5UL);
            _eventRng = new Rng(seed ^ 0xE7E47UL);
            _staffRng = new Rng(seed ^ 0x57AFFUL);
            _nextEventTurn = _eventRng.Range(balance.FirstEventMinTurn, balance.FirstEventMaxTurn + 1);
            Market = new Market(balance);
            _magnates = balance.Magnates.Select(m => m.Clone()).ToList();
            // Большому сектору — больше магнатов: недостающих придумываем по образцам из списка.
            int wanted = balance.PlanetsPerMagnate <= 0 ? 0 : (int)Math.Round(balance.SectorPlanets / (double)balance.PlanetsPerMagnate);
            if (wanted > _magnates.Count && balance.Magnates.Length > 0)
            {
                var nameRng = new Rng(seed ^ 0x3A6_4A7EUL);
                while (_magnates.Count < wanted)
                    _magnates.Add(Magnate.Generate(_magnates.Count, balance.Magnates[_magnates.Count % balance.Magnates.Length], nameRng));
            }
        }

        /// <summary>
        /// Новая партия. players — сколько игроков (больше одного — кооператив): у каждого своя корпорация и стартовая планета.
        /// characters — стартовые персонажи игроков по порядку; кому не хватило — средний директор.
        /// </summary>
        public static Game New(Balance balance, ulong seed, int players = 1, params StartCharacter[] characters)
        {
            var g = new Game(balance, seed);
            for (int i = 0; i < Math.Max(1, players); i++)
            {
                var character = characters != null && i < characters.Length ? characters[i] : null;
                var corp = new Corporation(i, i == 0 ? "Вы" : $"Игрок {i + 1}")
                {
                    Character = character,
                    Money = balance.StartMoney + (character?.ExtraMoney ?? 0),
                    Debt = balance.StartDebt,
                    Reputation = character?.Reputation ?? balance.StartReputation,
                };
                string director = character == null ? "Вы (директор)" : $"Вы ({character.Name.ToLowerInvariant()})";
                corp.Staff.Add(Manager.Director(g._nextManagerId++, director, balance.BaseActionsPerTurn, character?.Skills));
                g._corps.Add(corp);
            }
            foreach (var m in g._magnates)
            {
                m.Corp = new Corporation(g._corps.Count, m.Name) { Magnate = m, Money = balance.MagnateStartMoney, Reputation = balance.StartReputation };
                m.Corp.Staff.Add(Manager.Director(g._nextManagerId++, m.Name, balance.BaseActionsPerTurn));
                g._corps.Add(m.Corp);
            }
            g._current = g._corps[0];

            for (int i = 0; i < balance.SectorPlanets; i++)
                g._planets.Add(g.GeneratePlanet(g.RandomType()));

            int aiCount = (int)Math.Round(balance.SectorPlanets * balance.AiStartShare);
            int k = 0;
            foreach (var p in g._planets.OrderBy(_ => g._rng.NextULong()).Take(aiCount))
            {
                if (g._magnates.Count == 0) break;
                p.OwnerCorp = g._magnates[k++ % g._magnates.Count].Corp;
                p.Mine = MineState.Working;
            }

            // Стартовая планета каждого игрока: скромный ледяной карлик со средним запасом и работающей шахтой.
            // Игроки кооператива видят сектор вместе.
            foreach (var corp in g.Players.ToList())
            {
                var start = new Planet(g._nextPlanetId++, balance.TypeOf(Resource.Ice), balance.TypeOf(Resource.Ice).AverageReserve)
                {
                    OwnerCorp = corp,
                    Mine = balance.StartPlanetHasMine ? MineState.Working : MineState.None,
                    BoughtOnTurn = 0,
                };
                g.PlaceOnMap(start);
                start.Knowledge = Knowledge.Explored;
                g.ApplyCharacter(start, corp.Character);
                g._planets.Add(start);

                // Окрестности своей планеты игрок знает по скану, остальное — силуэты.
                foreach (var p in g._planets)
                    if (p != start && p.Knowledge == Knowledge.Silhouette && Geometry.Distance(p.MapX, p.MapY, start.MapX, start.MapY) <= balance.StartScanRadius)
                        p.Knowledge = Knowledge.Scanned;
                g._current = corp;
                g.RefreshCandidates();
                g.UpdateLevel(null, false);
            }
            g._current = g._corps[0];
            g.Market.SectorFactor = Math.Pow(Math.Max(1, players), balance.MarketGrowthPower);
            if (g.ShowOn) g.StartSeason();
            return g;
        }

        /// <summary>Что персонаж приносит на стартовую планету: жителей (Филантроп), второе месторождение (Геолог).</summary>
        void ApplyCharacter(Planet start, StartCharacter character)
        {
            if (character == null) return;
            if (character.Inhabited)
            {
                start.Inhabited = true;
                start.Loyalty = Balance.ColonyLoyalty;
            }
            if (character.ExtraDeposit)
            {
                // Богатое месторождение металла или газа: добыча и запас — по верхней границе обычных.
                var r = _surveyRng.Chance(0.5) ? Resource.Metal : Resource.Gas;
                double output = Math.Round(Balance.TypeOf(r).OutputPerTurn * Balance.ExtraOutputMax, 1);
                double capacity = Math.Round(output * Balance.ExtraCapacityTurnsMax);
                start.ExtraDeposits.Add(new Deposit { Resource = r, OutputPerTurn = output, Capacity = capacity, InitialCapacity = capacity, Reserve = capacity });
            }
        }

        /// <summary>Типы планет сектора: в стартовом — как в Balance, в богатых — с множителями сектора.</summary>
        public PlanetType[] SectorTypes(int sector)
        {
            if (_sectorTypes.TryGetValue(sector, out var types)) return types;
            var spec = Balance.Sectors[sector];
            types = Balance.PlanetTypes.Where(t => t.OnlySector < 0 || t.OnlySector == sector)
                .Select(t => t.Scaled(spec.Richness, spec.UpkeepFactor)).ToArray();
            _sectorTypes[sector] = types;
            return types;
        }

        /// <summary>Какие ресурсы встречаются в секторе: шесть общих и, в новых секторах, свой.</summary>
        public IEnumerable<Resource> SectorResources(int sector) => SectorTypes(sector).Select(t => t.Resource);

        /// <summary>Ресурсы, с которыми корпорация имеет дело: из секторов, где у неё лицензия, и те, что лежат на складе.</summary>
        public IEnumerable<Resource> KnownResources =>
            ResourceTypes.All.Where(r => _current.Licenses.Any(s => SectorOpen(s) && SectorResources(s).Contains(r)) || _stock[(int)r] + InTransit(r) > 0);

        /// <summary>Ресурсы открытых секторов: только их заказывают и просят жители.</summary>
        Resource[] OpenResources => ResourceTypes.All.Where(r => _openSectors.Any(s => SectorResources(s).Contains(r))).ToArray();

        /// <summary>Тип планеты с основным ресурсом r в секторе.</summary>
        public PlanetType TypeIn(int sector, Resource r) => SectorTypes(sector).First(t => t.Resource == r);

        PlanetType RandomType(int sector = 0)
        {
            var types = SectorTypes(sector);
            double total = types.Sum(t => t.Weight);
            double roll = _rng.NextDouble() * total;
            foreach (var t in types)
            {
                roll -= t.Weight;
                if (roll < 0) return t;
            }
            return types[types.Length - 1];
        }

        Planet GeneratePlanet(PlanetType type, int sector = 0)
        {
            double spread = Balance.ReserveSpread;
            double reserve = Math.Round(type.AverageReserve * _rng.Range(1 - spread, 1 + spread));
            var p = new Planet(_nextPlanetId++, type, reserve) { Sector = sector };
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

            double roll = _surveyRng.NextDouble();
            int extra = roll < Balance.TwoExtraDepositsChance ? 2 : roll < Balance.OneExtraDepositChance ? 1 : 0;
            var others = SectorResources(p.Sector).Where(r => r != p.Resource).OrderBy(_ => _surveyRng.NextULong()).Take(extra);
            foreach (var r in others)
            {
                double output = Math.Round(TypeIn(p.Sector, r).OutputPerTurn * _surveyRng.Range(Balance.ExtraOutputMin, Balance.ExtraOutputMax), 1);
                double capacity = Math.Round(output * _surveyRng.Range(Balance.ExtraCapacityTurnsMin, Balance.ExtraCapacityTurnsMax));
                p.ExtraDeposits.Add(new Deposit { Resource = r, OutputPerTurn = Math.Max(1, output), Capacity = capacity, InitialCapacity = capacity, Reserve = capacity });
            }
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
                    if (other.Sector != p.Sector) continue;
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
        public double StockValue => ResourceTypes.All.Sum(r => Quote(r, _stock[(int)r] + InTransit(r)));

        /// <summary>Экономисты текущего игрока делают рынок для него глубже.</summary>
        public double MarketDepthFactor => 1 + SpecialtyBonus(Specialty.Economists);
        public double MarketDepth(Resource r) => Market.Depth(r, MarketDepthFactor);
        /// <summary>Сколько текущий игрок выручит за amount с учётом падения цены.</summary>
        public double Quote(Resource r, double amount) => Market.Quote(r, amount, MarketDepthFactor) * (1 - CartelCut);

        /// <summary>Капитал = деньги − долг + стоимость планет + стоимость склада.</summary>
        public double Capital => Money - Debt + PlanetsValue + StockValue;

        // ------------------------------------------------------------------
        // Уровень корпорации
        // ------------------------------------------------------------------

        /// <summary>Уровень текущей корпорации, номер с нуля.</summary>
        public int Level => _current.Level;
        public CorporationLevel LevelInfo => Balance.CorporationLevels[Level];
        /// <summary>Следующий уровень; null — выше некуда.</summary>
        public CorporationLevel NextLevel => Level + 1 < Balance.CorporationLevels.Length ? Balance.CorporationLevels[Level + 1] : null;
        public bool ProbeUnlocked(ProbeType t) => t.MinLevel <= Level;
        /// <summary>Зонды, которые корпорации уже по уровню.</summary>
        public IEnumerable<ProbeType> UnlockedProbes => Balance.ProbeTypes.Where(ProbeUnlocked);

        /// <summary>Что открывает уровень: строки для интерфейса и событий.</summary>
        public List<string> LevelUnlocks(int level)
        {
            var levels = Balance.CorporationLevels;
            var list = new List<string>();
            int managers = levels[level].Managers, before = level > 0 ? levels[level - 1].Managers : 0;
            if (managers > before)
                list.Add(level == 0 ? Words.Count(managers, "менеджер", "менеджера", "менеджеров") : $"до {Words.Count(managers, "менеджера", "менеджеров", "менеджеров")}");
            foreach (var t in Balance.ProbeTypes.Where(t => t.MinLevel == level))
                list.Add($"зонд «{t.Name}»");
            foreach (var s in Balance.Sectors.Where((s, i) => i > 0 && s.LicenseLevel == level))
                list.Add($"лицензия на сектор «{s.Name}»");
            return list;
        }

        // ------------------------------------------------------------------
        // Секторы и лицензии
        // ------------------------------------------------------------------

        public IReadOnlyList<SectorSpec> Sectors => Balance.Sectors;
        public bool HasLicense(int sector) => _current.Licenses.Contains(sector);
        /// <summary>Открыт ли сектор: у него есть планеты и магнаты (лицензию на него уже кто-то купил).</summary>
        public bool SectorOpen(int sector) => _openSectors.Contains(sector);

        /// <summary>Почему лицензию сейчас не купить; null — можно.</summary>
        public string LicenseBlocker(int sector)
        {
            if (sector < 0 || sector >= Balance.Sectors.Length) return "Такого сектора нет";
            if (HasLicense(sector)) return "Лицензия уже есть";
            var spec = Balance.Sectors[sector];
            if (Level < spec.LicenseLevel) return $"Лицензию продают с уровня «{Balance.CorporationLevels[spec.LicenseLevel].Name}»";
            if (Money < spec.LicenseCost) return "Не хватает денег";
            return null;
        }

        /// <summary>
        /// Лицензия на сектор: его планеты можно покупать и разведывать, зонды летят от его биржи и ваших планет в нём.
        /// Первая лицензия открывает сектор: появляются планеты и свои магнаты. Окрестности биржи видны сканом.
        /// </summary>
        public ActionResult BuyLicense(int sector)
        {
            string blocker = LicenseBlocker(sector);
            if (blocker != null) return ActionResult.Fail(blocker);
            var a = SpendAction(Skill.Negotiation);
            if (!a.Ok) return a;
            var spec = Balance.Sectors[sector];
            Money -= spec.LicenseCost;
            _current.Licenses.Add(sector);
            if (!SectorOpen(sector)) OpenSector(sector);
            int seen = 0;
            foreach (var p in _planets.Where(x => x.Sector == sector && x.Knowledge == Knowledge.Silhouette))
                if (Geometry.Distance(p.MapX, p.MapY, Balance.ExchangeX, Balance.ExchangeY) <= Balance.StartScanRadius)
                {
                    p.Knowledge = Knowledge.Scanned;
                    seen++;
                }
            Notices.Add($"Лицензия на сектор «{spec.Name}» куплена. Планет в секторе {_planets.Count(x => x.Sector == sector)}, у биржи видно {seen}. " +
                        $"Магнаты сектора: {string.Join(", ", _magnates.Where(m => m.Sector == sector).Select(m => m.Name))}.");
            return ActionResult.Success;
        }

        /// <summary>Сектор появляется: планеты, свои магнаты, часть планет — у них, с шахтами.</summary>
        void OpenSector(int sector)
        {
            var spec = Balance.Sectors[sector];
            _openSectors.Add(sector);
            var fresh = new List<Planet>();
            for (int i = 0; i < spec.Planets; i++)
            {
                var p = GeneratePlanet(RandomType(sector), sector);
                _planets.Add(p);
                fresh.Add(p);
            }
            int count = Balance.PlanetsPerMagnate <= 0 || Balance.Magnates.Length == 0 ? 0 : Math.Max(1, (int)Math.Round(spec.Planets / (double)Balance.PlanetsPerMagnate));
            var local = new List<Magnate>();
            for (int i = 0; i < count; i++)
            {
                // Сначала — персонажи автора, недостающих придумываем по образцам.
                int id = _magnates.Max(x => (int?)x.Id) + 1 ?? 0;
                Magnate m;
                if (spec.Magnates != null && i < spec.Magnates.Length)
                {
                    m = spec.Magnates[i].Clone();
                    m.Id = id;
                }
                else
                    m = Magnate.Generate(id, Balance.Magnates[i % Balance.Magnates.Length], _rng);
                m.Sector = sector;
                AddMagnateCorp(m, spec.MagnateMoney * m.MoneyFactor);
                _magnates.Add(m);
                local.Add(m);
            }
            int aiCount = (int)Math.Round(spec.Planets * Balance.AiStartShare);
            int k = 0;
            foreach (var p in fresh.OrderBy(_ => _rng.NextULong()).Take(aiCount))
            {
                if (local.Count == 0) break;
                p.OwnerCorp = local[k++ % local.Count].Corp;
                p.Mine = MineState.Working;
            }
        }

        /// <summary>Корпорация нового магната: казна и директор.</summary>
        void AddMagnateCorp(Magnate m, double money)
        {
            m.Corp = new Corporation(_corps.Max(x => x.Id) + 1, m.Name) { Magnate = m, Money = money, Reputation = Balance.StartReputation };
            m.Corp.Staff.Add(Manager.Director(_nextManagerId++, m.Name, Balance.BaseActionsPerTurn));
            _corps.Add(m.Corp);
        }

        /// <summary>Ближайший следующий уровень, где выполняется условие; null — такого нет.</summary>
        public CorporationLevel NextLevelWith(Func<CorporationLevel, bool> test) =>
            Balance.CorporationLevels.Skip(Level + 1).FirstOrDefault(test);

        /// <summary>Поднять уровень, если капитал дорос до следующего; announce — сказать об этом в событиях хода.</summary>
        void UpdateLevel(TurnReport report, bool announce)
        {
            var levels = Balance.CorporationLevels;
            if (_current.Level + 1 >= levels.Length || Capital < levels[_current.Level + 1].Capital) return;
            double capital = Capital;
            while (_current.Level + 1 < levels.Length && capital >= levels[_current.Level + 1].Capital)
            {
                _current.Level++;
                if (!announce) continue;
                string who = Players.Count() > 1 ? $"Корпорация «{_current.Name}»" : "Ваша корпорация";
                var unlocks = LevelUnlocks(_current.Level);
                Important(report, AlertKind.Good, $"Новый уровень: {levels[_current.Level].Name}",
                    $"{who} доросла до уровня «{levels[_current.Level].Name}»" + (unlocks.Count > 0 ? ": " + string.Join(", ", unlocks) + "." : "."));
            }
        }

        /// <summary>Запас, который знает игрок: точный у своих и разведанных, оценка у просканированных, null у силуэтов.</summary>
        public double? KnownReserve(Planet p)
        {
            if (IsMine(p) || p.Knowledge == Knowledge.Explored) return p.Reserve;
            if (p.Knowledge == Knowledge.Scanned) return p.EstimatedReserve;
            return null;
        }

        /// <summary>Ёмкость основного месторождения по тому, что знает игрок; null у силуэтов.</summary>
        public double? KnownCapacity(Planet p)
        {
            if (IsMine(p) || p.Knowledge == Knowledge.Explored) return p.Capacity;
            if (p.Knowledge == Knowledge.Scanned) return p.EstimatedCapacity;
            return null;
        }

        /// <summary>Стоимость планеты по тому, что знает игрок; null у силуэтов.</summary>
        public double? KnownValue(Planet p)
        {
            if (IsMine(p) || p.Knowledge == Knowledge.Explored) return p.MarketValue(Balance);
            if (p.Knowledge == Knowledge.Scanned) return p.EstimatedValue(Balance);
            return null;
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
        public double ShippingPerTurn => PlayerPlanets.Sum(p =>
            (p.Mine == MineState.Working && !p.IsDepleted ? ShippingCost(p.Resource, Math.Min(p.Type.OutputPerTurn, p.Reserve), DistanceToExchange(p)) : 0)
            + p.ExtraDeposits.Where(CanMine).Sum(d => ShippingCost(d.Resource, Math.Min(d.OutputPerTurn, d.Reserve), DistanceToExchange(p))));

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

        /// <summary>Сколько планет считается в множителе содержания сектора: свои целиком, из других секторов — долей.</summary>
        public double UpkeepCurvePlanetsIn(int sector)
        {
            int own = 0, other = 0;
            foreach (var p in PlayerPlanets)
            {
                if (!Balance.DepletedCountsInUpkeepCurve && p.IsDepleted) continue;
                if (p.Sector == sector) own++;
                else other++;
            }
            return own + Balance.OtherSectorsUpkeepShare * other;
        }

        /// <summary>
        /// Содержание за ход без событий, если к планетам добавить extra (null — как сейчас). Для оценки покупки:
        /// новая планета дорожит и свой сектор, и, долей, остальные.
        /// </summary>
        public double UpkeepWith(Planet extra)
        {
            var planets = PlayerPlanets.ToList();
            if (extra != null && !planets.Contains(extra)) planets.Add(extra);
            bool Counts(Planet p) => Balance.DepletedCountsInUpkeepCurve || !p.IsDepleted;
            double total = 0;
            foreach (var sector in planets.GroupBy(p => p.Sector))
            {
                int own = planets.Count(p => p.Sector == sector.Key && Counts(p));
                int other = planets.Count(p => p.Sector != sector.Key && Counts(p));
                total += sector.Sum(p => p.BaseUpkeep(Balance)) * Balance.UpkeepMultiplier(own + Balance.OtherSectorsUpkeepShare * other);
            }
            return total;
        }

        /// <summary>Множитель содержания в стартовом секторе.</summary>
        public double UpkeepMultiplierNow => Balance.UpkeepMultiplier(UpkeepCurvePlanetsIn(0));

        public double UpkeepMultiplierIn(int sector) => Balance.UpkeepMultiplier(UpkeepCurvePlanetsIn(sector));

        /// <summary>Сколько планета игрока стоит в содержании прямо сейчас, с множителем её сектора.</summary>
        public double PlanetUpkeep(Planet p) => p.BaseUpkeep(Balance) * UpkeepMultiplierIn(p.Sector) * EventUpkeepFactor;

        public double UpkeepPerTurn =>
            PlayerPlanets.GroupBy(p => p.Sector).Sum(s => s.Sum(p => p.BaseUpkeep(Balance)) * UpkeepMultiplierIn(s.Key)) * EventUpkeepFactor;

        GalacticEvent Ev => CurrentEvent?.Event;

        /// <summary>Множитель содержания от события: амнистия — у всех, лицензионный сбор — только у крупных.</summary>
        public double EventUpkeepFactor =>
            Ev == null || PlayerPlanetCount < Ev.UpkeepFactorFromPlanets ? 1 : Ev.UpkeepFactor;

        public double InsurancePerTurn => Insured ? Balance.InsuranceRate * PlanetsValue : 0;

        public double WagesPerTurn => PlayerPlanets.Count(p => p.Inhabited && p.PaysWages) * Balance.WagesCost;

        /// <summary>Налоги за ход с небастующих планет.</summary>
        public double TaxesPerTurn => PlayerPlanets.Where(p => p.Inhabited && !p.OnStrike).Sum(p => Balance.TaxIncome[(int)p.Tax]);

        /// <summary>Умственный труд образованных жителей за ход (доход).</summary>
        public double IntellectPerTurn => PlayerPlanets.Sum(IntellectIncome);

        public double GuardsPerTurn => PlayerPlanets.Count(p => p.Guarded) * Balance.GuardsCost;

        public double CostsPerTurn =>
            UpkeepPerTurn + SalariesPerTurn + Debt * Balance.InterestPerTurn + InsurancePerTurn + ShippingPerTurn + WagesPerTurn + GuardsPerTurn;

        // ------------------------------------------------------------------
        // Действия, которые тратят очки действий
        // ------------------------------------------------------------------

        /// <summary>Действие выполняет конкретный сотрудник: выбранный вручную или лучший в этом деле.</summary>
        ActionResult SpendAction(Skill? skill = null)
        {
            var who = WhoActs(skill);
            if (who == null) return ActionResult.Fail("Действия на этот ход закончились");
            who.ActionsLeft--;
            if (ActionsLeft == 0) TurnsActionsExhausted++;
            return ActionResult.Success;
        }

        /// <summary>
        /// Кто выполнит следующее действие: выбранный сотрудник, если у него остались действия, иначе лучший в этом деле.
        /// Для дел без навыка — тот, чьи навыки нужнее меньше всего. null — действий не осталось.
        /// </summary>
        public Manager WhoActs(Skill? skill = null)
        {
            var free = _current.Staff.Where(m => m.ActionsLeft > 0).ToList();
            if (free.Count == 0) return null;
            var chosen = free.FirstOrDefault(m => m.Id == _current.PerformerId);
            if (chosen != null) return chosen;
            if (skill == null) return free.OrderBy(m => m.SkillLevels.Max()).ThenByDescending(m => m.IsDirector).First();
            // По способностям: кто сильнее в этом деле, но специалиста в другом деле бережём для его дела.
            // При равенстве — у кого остальные навыки слабее (он меньше нужен в другом месте), потом менеджер раньше директора.
            var s = skill.Value;
            return free.OrderByDescending(m => Fit(m, s)).ThenBy(m => m.SkillLevels.Sum()).ThenBy(m => m.IsDirector).First();
        }

        /// <summary>Насколько человек подходит к делу: навык минус «цена» отвлечь его от сильной стороны.</summary>
        double Fit(Manager m, Skill s) => m.Level(s) - Balance.SpecialistReserve * (m.SkillLevels.Max() - m.Level(s));

        /// <summary>Насколько тот, кто возьмётся за дело, лучше среднего: от −2 до +2.</summary>
        public int Edge(Skill skill) => WhoActs(skill)?.Edge(skill) ?? 0;

        /// <summary>Кто будет выполнять действия; −1 — подбирать лучшего под каждое дело.</summary>
        public ActionResult SetPerformer(int managerId)
        {
            if (managerId >= 0 && _current.Staff.All(m => m.Id != managerId)) return ActionResult.Fail("Нет такого сотрудника");
            _current.PerformerId = managerId;
            return ActionResult.Success;
        }

        /// <summary>Цена покупки у реестра: переговорщик выбивает скидку.</summary>
        public double PurchasePrice(Planet p) => Math.Round(p.ListPrice(Balance) * (1 - Balance.NegotiationPriceStep * Edge(Skill.Negotiation)));
        public double MineCost(Planet p) => Math.Round(p.Type.MineCost * (1 - Balance.GeologyCostStep * Edge(Skill.Geology)));
        public double SchoolCost => Math.Round(Balance.SchoolCost * (1 - Balance.PeopleCostStep * Edge(Skill.People)));
        public double ColonyCost => Math.Round(Balance.ColonyCost * (1 - Balance.PeopleCostStep * Edge(Skill.People)));
        public double EducationCost(Planet p) =>
            p.Education + 1 >= Balance.EducationCost.Length ? 0 : Math.Round(Balance.EducationCost[p.Education + 1] * (1 - Balance.PeopleCostStep * Edge(Skill.People)));
        public double FestivalLoyalty => Balance.FestivalLoyalty * (1 + Balance.PeopleFestivalStep * Edge(Skill.People));
        /// <summary>Сколько дадут за планету при продаже действием: переговорщик выторгует больше.</summary>
        public double SellPlanetPrice(Planet p) => SalePrice(p) * (1 + Balance.NegotiationSaleStep * Edge(Skill.Negotiation));

        public ActionResult BuyPlanet(int planetId)
        {
            var p = GetPlanet(planetId);
            if (p == null || p.Owner != Owner.Registry) return ActionResult.Fail("Планета не продаётся");
            if (IsLot(p)) return LotOnly;
            if (!HasLicense(p.Sector)) return NoLicense(p);
            if (p.IsDepleted) return ActionResult.Fail("Планета выработана");
            if (p.Inhabited)
            {
                if (p.Knowledge == Knowledge.Silhouette) p.Knowledge = Knowledge.Scanned;
                return ActionResult.Fail("Планета обитаема: её продают сами жители, сделайте им предложение");
            }
            if (p.SoldAtAuction) return ActionResult.Fail("Планета продаётся с аукциона: сделайте ставку");
            double price = PurchasePrice(p);
            if (Money < price) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction(Skill.Negotiation);
            if (!a.Ok) return a;

            Acquire(p, price);
            return ActionResult.Success;
        }

        static readonly ActionResult LotOnly = ActionResult.Fail("Это «Лот недели»: планету продают только на торгах");

        ActionResult NoLicense(Planet p) => ActionResult.Fail($"Нет лицензии на сектор «{Balance.Sectors[p.Sector].Name}»");

        const string NeedExploration = "Жители говорят только с тем, кто разведал планету: сначала разведайте её зондом";

        /// <summary>Можно ли игроку покупать эту планету: обитаемую — только разведанную, у реестра — и по оценке.</summary>
        public bool ExploredEnoughToBuy(Planet p) =>
            !p.Inhabited || !Balance.ResidentsNeedExploration || p.Knowledge == Knowledge.Explored;

        void Acquire(Planet p, double price)
        {
            _current.Stats.PlanetsBought++;
            Money -= price;
            p.OwnerCorp = _current;
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
            chance += Balance.NegotiationChanceStep * Edge(Skill.Negotiation);
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
            if (IsLot(p)) return LotOnly;
            if (!HasLicense(p.Sector)) return NoLicense(p);
            if (!p.Inhabited) return ActionResult.Fail("Планета необитаема, её продаёт реестр");
            if (p.IsDepleted) return ActionResult.Fail("Планета выработана");
            if (Turn < p.NoTalksUntilTurn) return ActionResult.Fail($"Жители обиделись и не разговаривают до хода {p.NoTalksUntilTurn}");
            if (!ExploredEnoughToBuy(p)) return ActionResult.Fail(NeedExploration);
            if (Money < OfferCost(p, offer)) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction(Skill.Negotiation);
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
            if (offer.WithSchool || offer.PriceShare >= 1.1) RecordDeed(DeedKind.GenerousDeal, p);
            else if (offer.PriceShare <= 0.8) RecordDeed(DeedKind.LowballDeal, p);
            Notices.Add($"Жители {p.Type.Name} #{p.Id} согласились. Лояльность {Math.Round(p.Loyalty)}.");
            return ActionResult.Success;
        }

        static double Clamp(double loyalty) => Math.Min(100, Math.Max(0, loyalty));

        Planet OwnInhabited(int planetId, out ActionResult error)
        {
            var p = GetPlanet(planetId);
            error = ActionResult.Success;
            if (p == null || !IsMine(p)) error = ActionResult.Fail("Это не ваша планета");
            else if (!p.Inhabited) error = ActionResult.Fail("На планете нет жителей");
            return error.Ok ? p : null;
        }

        public ActionResult SetTax(int planetId, TaxLevel tax)
        {
            var p = OwnInhabited(planetId, out var error);
            if (p == null) return error;
            if (tax == TaxLevel.High && p.Tax != TaxLevel.High) RecordDeed(DeedKind.HighTax, p);
            p.Tax = tax;
            return ActionResult.Success;
        }

        public ActionResult SetWages(int planetId, bool on)
        {
            var p = OwnInhabited(planetId, out var error);
            if (p == null) return error;
            if (on && !p.PaysWages) RecordDeed(DeedKind.Wages, p);
            p.PaysWages = on;
            return ActionResult.Success;
        }

        /// <summary>Праздник: разово поднимает лояльность.</summary>
        public ActionResult HoldFestival(int planetId)
        {
            var p = OwnInhabited(planetId, out var error);
            if (p == null) return error;
            if (Money < Balance.FestivalCost) return ActionResult.Fail("Не хватает денег");
            double festival = FestivalLoyalty;
            var a = SpendAction(Skill.People);
            if (!a.Ok) return a;
            Money -= Balance.FestivalCost;
            p.Earned -= Balance.FestivalCost;
            p.Loyalty = Clamp(p.Loyalty + festival);
            RecordDeed(DeedKind.Festival, p);
            _current.Stats.Festivals++;
            return ActionResult.Success;
        }

        // ------------------------------------------------------------------
        // Образование и умственный труд
        // ------------------------------------------------------------------

        /// <summary>Сколько уровней образования у ваших планет этой специальности.</summary>
        public int SpecialtyLevels(Specialty s) =>
            PlayerPlanets.Where(p => p.Inhabited && p.Specialty == s).Sum(p => p.Education);

        /// <summary>Бонус корпорации от специальности: растёт с каждым уровнем, но не выше предела.</summary>
        public double SpecialtyBonus(Specialty s) =>
            Math.Min(Balance.SpecialtyBonusMax[(int)s], Balance.SpecialtyBonusPerLevel[(int)s] * SpecialtyLevels(s));

        /// <summary>
        /// Доход от умственного труда за ход. Чем довольнее жители, тем лучше работают:
        /// лояльность 50 — обычный доход, 100 — на четверть больше. Пока шахта работает, трудится часть жителей.
        /// </summary>
        public double IntellectIncome(Planet p)
        {
            double income = p.IntellectBase(Balance);
            if (income <= 0 || p.OnStrike) return 0;
            if (p.Mine == MineState.Working && !p.IsDepleted) income *= Balance.IntellectWhileMining;
            if (Balance.CartelHitsIntellect && p.OwnerCorp != null && p.OwnerCorp.CartelUntilTurn >= Turn)
                income *= 1 - Balance.CartelPriceCut * (p.OwnerCorp.Reputation >= Balance.CartelReputationShield ? 0.5 : 1);
            return income * (1 + (p.Loyalty - 50) * Balance.IntellectPerLoyalty);
        }

        public bool CanEducate(Planet p, out string reason)
        {
            var b = Balance;
            reason = null;
            if (!IsMine(p)) reason = "Это не ваша планета";
            else if (!p.Inhabited) reason = "На планете нет жителей";
            else if (p.Education >= b.EducationCost.Length - 1) reason = "Жители уже получили высшее образование";
            else if (p.IsStudying) reason = "Жители уже учатся";
            else if (!p.HasSchool) reason = "Сначала нужна школа";
            else if (p.OnStrike) reason = "Жители бастуют";
            else if (p.Loyalty < b.EducationMinLoyalty) reason = $"Жители не станут учиться при лояльности ниже {b.EducationMinLoyalty}";
            else if (Money < EducationCost(p)) reason = "Не хватает денег";
            return reason == null;
        }

        /// <summary>
        /// Обучить жителей следующему уровню. На первом уровне выбирается специальность, дальше она не меняется.
        /// </summary>
        public ActionResult Educate(int planetId, Specialty specialty = Specialty.None)
        {
            var p = GetPlanet(planetId);
            if (p == null) return ActionResult.Fail("Нет такой планеты");
            if (!CanEducate(p, out var reason)) return ActionResult.Fail(reason);
            if (p.Education == 0 && specialty == Specialty.None) return ActionResult.Fail("Выберите специальность");
            double cost = EducationCost(p);
            var a = SpendAction(Skill.People);
            if (!a.Ok) return a;
            int next = p.Education + 1;
            Money -= cost;
            p.Invested += cost;
            if (p.Education == 0) p.Specialty = specialty;
            p.EducationTurnsLeft = Balance.EducationTurns[next];
            return ActionResult.Success;
        }

        /// <summary>Заселить колонистов на вашу необитаемую планету: у неё появятся жители со своими заботами.</summary>
        public ActionResult SettleColony(int planetId)
        {
            var p = GetPlanet(planetId);
            if (!IsMine(p)) return ActionResult.Fail("Это не ваша планета");
            if (p.Inhabited) return ActionResult.Fail("Здесь уже живут");
            double cost = ColonyCost;
            if (Money < cost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction(Skill.People);
            if (!a.Ok) return a;
            Money -= cost;
            p.Invested += cost;
            p.Inhabited = true;
            p.Loyalty = Balance.ColonyLoyalty;
            p.Tax = TaxLevel.None;
            p.OnStrike = false;
            RecordDeed(DeedKind.Colony, p);
            _current.Stats.Colonies++;
            return ActionResult.Success;
        }

        /// <summary>Школа: навсегда добавляет лояльности каждый ход.</summary>
        public ActionResult BuildSchool(int planetId)
        {
            var p = OwnInhabited(planetId, out var error);
            if (p == null) return error;
            if (p.HasSchool) return ActionResult.Fail("Школа уже есть");
            double cost = SchoolCost;
            if (Money < cost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction(Skill.People);
            if (!a.Ok) return a;
            Money -= cost;
            p.Invested += cost;
            p.HasSchool = true;
            RecordDeed(DeedKind.School, p);
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
            RecordDeed(DeedKind.RequestDone, p);
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
            if (Ev != null) delta -= Ev.ExtraLoyaltyDrain;
            delta += TechLoyaltyBonus + SpecialtyBonus(Specialty.Biologists);
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
                if (!p.OnStrike)
                {
                    double intellect = IntellectIncome(p);
                    Money += intellect;
                    p.Earned += intellect;
                    report.Intellect += intellect;

                    if (p.EducationTurnsLeft > 0 && --p.EducationTurnsLeft == 0)
                    {
                        p.Education++;
                        RecordDeed(DeedKind.Education, p);
                        _current.Stats.EducationLevels++;
                        report.Events.Add($"Жители {p.Type.Name} #{p.Id} выучились: {p.Specialty.RuName()}, уровень {p.Education}. " +
                                          $"Умственный труд — {Math.Round(IntellectIncome(p))} за ход.");
                    }
                }

                p.Loyalty = Clamp(p.Loyalty + LoyaltyChangePerTurn(p));

                if (!p.OnStrike && p.Loyalty < b.StrikeBelow)
                {
                    p.OnStrike = true;
                    RecordDeed(DeedKind.Strike, p);
                    Important(report, AlertKind.Disaster, "Забастовка",
                        $"Забастовка на {p.Type.Name} #{p.Id}: лояльность {Math.Round(p.Loyalty)}, добычи и налогов нет.", p);
                }
                else if (p.OnStrike && p.Loyalty >= b.StrikeEndsAt)
                {
                    p.OnStrike = false;
                    report.Events.Add($"Забастовка на {p.Type.Name} #{p.Id} закончилась.");
                }

                // Бунт: доведённые до отчаяния жители громят шахту. Страховка не платит.
                if (p.OnStrike && p.Loyalty < b.RiotBelowLoyalty && p.Mine != MineState.None && _rng.Chance(b.RiotChance))
                {
                    p.Mine = MineState.None;
                    p.BuildTurnsLeft = 0;
                    p.MineWreckedTurn = Turn;
                    p.MineWreckedByRiot = true;
                    RecordDeed(DeedKind.Riot, p);
                    Important(report, AlertKind.Disaster, "Бунт",
                        $"Бунт на {p.Type.Name} #{p.Id}: жители разнесли шахту. Страховка бунты не покрывает.", p);
                }
            }

            if (_current.IsAi) return;
            foreach (var r in _requests.Where(r => r.DeadlineTurn <= Turn).ToList())
            {
                _requests.Remove(r);
                var p = GetPlanet(r.PlanetId);
                if (!IsMine(p)) continue;
                p.Loyalty = Clamp(p.Loyalty - b.RequestLoyaltyPenalty);
                RecordDeed(DeedKind.RequestFailed, p);
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
                    var open = OpenResources;
                    r.Resource = open[_rng.Range(0, open.Length)];
                    r.Amount = Math.Max(5, Math.Round(b.TypeOf(r.Resource).OutputPerTurn * _rng.Range(1.0, 3.0) / 5) * 5);
                    report.Events.Add($"Жители {p.Type.Name} #{p.Id} просят {r.Amount} ({r.Resource.RuName()}) за {b.RequestTurns} ходов.");
                }
                _requests.Add(r);
            }
        }

        public ActionResult BuildMine(int planetId)
        {
            var p = GetPlanet(planetId);
            if (!IsMine(p)) return ActionResult.Fail("Это не ваша планета");
            if (p.Mine != MineState.None) return ActionResult.Fail("Шахта уже есть");
            double cost = MineCost(p);
            if (Money < cost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction(Skill.Geology);
            if (!a.Ok) return a;

            Money -= cost;
            p.Invested += cost;
            p.Mine = MineState.Building;
            p.BuildTurnsLeft = Balance.MineBuildTurns;
            return ActionResult.Success;
        }

        /// <summary>Продать планету по рыночной стоимости минус комиссия. Она уходит в реестр.</summary>
        public ActionResult SellPlanet(int planetId)
        {
            var p = GetPlanet(planetId);
            if (!IsMine(p)) return ActionResult.Fail("Это не ваша планета");
            double proceeds = SellPlanetPrice(p);
            var a = SpendAction(Skill.Negotiation);
            if (!a.Ok) return a;

            Money += proceeds;
            p.Earned += proceeds;
            if (p.Earned > p.Invested) _current.Stats.ProfitableSales++;
            ReleaseToRegistry(p);
            SoldPlanets.Add(p);
            return ActionResult.Success;
        }

        /// <summary>Сколько дадут за планету: рыночная стоимость, множитель события, минус комиссия.</summary>
        public double SalePrice(Planet p) =>
            p.MarketValue(Balance) * (Ev?.PlanetSale(p.Resource) ?? 1) * (1 - Balance.ResaleCommission);

        void ReleaseToRegistry(Planet p)
        {
            p.OwnerCorp = null;
            p.FoundByProbe = false;
            p.Tax = TaxLevel.None;
            p.PaysWages = false;
            p.OnStrike = false;
            p.Loyalty = 50;
            p.Guarded = false;
            _requests.RemoveAll(r => r.PlanetId == p.Id);
            _bribes.RemoveAll(x => x.PlanetId == p.Id);
            _claims.RemoveAll(x => x.PlanetId == p.Id);
        }

        // ------------------------------------------------------------------
        // Аукционы и магнаты
        // ------------------------------------------------------------------

        public double BidPrice(Planet p, double factor) => Math.Round(p.BasePrice() * factor);

        /// <summary>
        /// Шанс выиграть аукцион ставкой factor от базовой цены: каждый магнат может заинтересоваться
        /// и поставить случайно в своих пределах; выигрывает тот, кто поставил больше.
        /// </summary>
        public double AuctionWinChance(Planet p, double factor)
        {
            double chance = 1;
            factor *= 1 + Balance.NegotiationPriceStep * Edge(Skill.Negotiation);
            double basePrice = Math.Max(1, p.BasePrice());
            foreach (var m in ActiveMagnates.Where(x => x.Sector == p.Sector))
            {
                // Магнат ставит в своих пределах, но не больше, чем есть в казне.
                double max = Math.Min(m.BidMax, m.Money / basePrice);
                if (max < m.BidMin) continue;
                double below = max <= m.BidMin ? (factor > m.BidMin ? 1 : 0) : Math.Min(1, Math.Max(0, (factor - m.BidMin) / (max - m.BidMin)));
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
            if (IsLot(p)) return LotOnly;
            if (!HasLicense(p.Sector)) return NoLicense(p);
            if (p.IsDepleted) return ActionResult.Fail("Планета выработана");
            double bid = BidPrice(p, factor);
            if (Money < bid) return ActionResult.Fail("Не хватает денег");
            double strength = bid * (1 + Balance.NegotiationPriceStep * Edge(Skill.Negotiation));
            var a = SpendAction(Skill.Negotiation);
            if (!a.Ok) return a;

            Magnate rival = null;
            double rivalBid = 0;
            foreach (var m in ActiveMagnates.Where(x => x.Sector == p.Sector))
            {
                if (!_rng.Chance(m.AuctionInterest)) continue;
                double x = p.BasePrice() * _rng.Range(m.BidMin, m.BidMax);
                if (x > m.Money) x = m.Money;
                if (x < p.BasePrice() * m.BidMin) continue;
                if (x > rivalBid)
                {
                    rivalBid = x;
                    rival = m;
                }
            }

            // Переговорщик убедительнее: его ставка весит больше, хотя платит он названную сумму.
            if (strength > rivalBid)
            {
                won = true;
                AuctionsWon++;
                Acquire(p, bid);
                return ActionResult.Success;
            }

            AuctionsLost++;
            rival.Money -= rivalBid;
            GiveToMagnate(p, rival);
            Notices.Add($"{rival.Name} перебил(а) вашу ставку {Math.Round(bid)}: {Math.Round(rivalBid)} за {p.Type.Name} #{p.Id}.");
            return ActionResult.Success;
        }

        void GiveToMagnate(Planet p, Magnate m)
        {
            _requests.RemoveAll(r => r.PlanetId == p.Id);
            _bribes.RemoveAll(x => x.PlanetId == p.Id);
            _claims.RemoveAll(x => x.PlanetId == p.Id);
            p.OwnerCorp = m?.Corp;
            p.Guarded = false;
            p.Tax = TaxLevel.None;
            p.PaysWages = false;
            p.OnStrike = false;
        }

        public double ClaimDefenseCost(Planet p) => Math.Round(p.MarketValue(Balance) * Balance.ClaimDefenseShare);

        /// <summary>Оплатить защиту от рейдерского захвата: юристы реестра отклоняют иск. Действия не тратит.</summary>
        public ActionResult DefendClaim(int claimId)
        {
            var x = _claims.FirstOrDefault(c => c.Id == claimId);
            if (x == null) return ActionResult.Fail("Иска нет");
            var p = GetPlanet(x.PlanetId);
            if (!IsMine(p)) return ActionResult.Fail("Это не ваша планета");
            double cost = ClaimDefenseCost(p);
            if (Money < cost) return ActionResult.Fail("Не хватает денег");
            Money -= cost;
            p.Earned -= cost;
            _claims.Remove(x);
            Notices.Add($"Иск против {p.Type.Name} #{p.Id} отклонён: юристы обошлись в {Math.Round(cost)}.");
            return ActionResult.Success;
        }

        /// <summary>Рейдерские иски, у которых вышел срок: планета уходит магнату, игрок получает компенсацию.</summary>
        void ResolveClaims(TurnReport report)
        {
            foreach (var x in _claims.Where(c => c.ResolveTurn <= Turn && IsMine(GetPlanet(c.PlanetId))).ToList())
            {
                _claims.Remove(x);
                var p = GetPlanet(x.PlanetId);
                var m = GetMagnate(x.MagnateId);
                if (m?.Corp == null || m.Corp.Bankrupt) continue;
                double compensation = Math.Round(p.MarketValue(Balance) * Balance.ClaimCompensationShare);
                m.Money -= compensation;
                Money += compensation;
                p.Earned += compensation;
                GiveToMagnate(p, m);
                Important(report, AlertKind.PlanetLost, "Планету отсудили",
                    $"Реестр отдал {p.Type.Name} #{p.Id} магнату {m.Name}. Компенсация — {compensation}.", p, m);
            }
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
            RecordDeed(DeedKind.CounterBribe, p);
            return ActionResult.Success;
        }

        public ActionResult SetGuards(int planetId, bool on)
        {
            var p = GetPlanet(planetId);
            if (!IsMine(p)) return ActionResult.Fail("Это не ваша планета");
            p.Guarded = on;
            return ActionResult.Success;
        }

        /// <summary>Ваш слух о магнате: несколько ходов он не покупает и не пакостит. Грязно: репутация и риск штрафа.</summary>
        public ActionResult SpreadRumor(int magnateId)
        {
            var m = GetMagnate(magnateId);
            if (m == null) return ActionResult.Fail("Нет такого магната");
            if (Money < Balance.PlayerRumorCost) return ActionResult.Fail("Не хватает денег");
            int shady = Edge(Skill.Shady);
            var a = SpendAction(Skill.Shady);
            if (!a.Ok) return a;
            Money -= Balance.PlayerRumorCost;
            m.QuietUntilTurn = Math.Max(m.QuietUntilTurn, Turn + 1) + Math.Max(1, Balance.PlayerRumorQuietTurns + shady) - 1;
            ChangeReputation(Balance.PlayerRumorReputation);
            Notices.Add($"Слух сработал: {m.Name} до хода {m.QuietUntilTurn} отбивается и не мешает вам.");
            MaybeFine(shady);
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
            int shadyEdge = Edge(Skill.Shady);
            var a = SpendAction(Skill.Shady);
            if (!a.Ok) return a;
            Money -= cost;
            ChangeReputation(Balance.PlayerBribeReputation);
            RecordDeed(DeedKind.BribedRivalResidents, p);
            if (_rng.Chance(Balance.PlayerBribeChance + Balance.ShadyChanceStep * shadyEdge))
            {
                success = true;
                var m = GetMagnate(p.MagnateId);
                p.OwnerCorp = _current;
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
            MaybeFine(shadyEdge);
            return ActionResult.Success;
        }

        void ChangeReputation(double delta) => Reputation = Math.Min(100, Math.Max(0, Reputation + delta));

        /// <summary>Реестр ловит грязное дело; мастер грязных дел попадается реже.</summary>
        void MaybeFine(int shadyEdge)
        {
            if (!_rng.Chance(Balance.FineChance * Math.Max(0, 1 - Balance.ShadyFineStep * shadyEdge))) return;
            Money -= Balance.FineAmount;
            FinesPaid += Balance.FineAmount;
            ChangeReputation(Balance.FineReputation);
            Notices.Add($"Галактический реестр раскрыл грязное дело: штраф {Math.Round(Balance.FineAmount)}, репутация {Math.Round(Reputation)}.");
        }

        // ------------------------------------------------------------------
        // Зонды
        // ------------------------------------------------------------------

        /// <summary>Откуда полетит зонд к точке сектора: ближайшая ваша планета в этом секторе или его биржа.</summary>
        public (double x, double y, double distance) LaunchOrigin(double x, double y, int sector = 0)
        {
            double bx = Balance.ExchangeX, by = Balance.ExchangeY;
            double best = Geometry.Distance(bx, by, x, y);
            foreach (var p in PlayerPlanets)
            {
                if (p.Sector != sector) continue;
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

        public (double x, double y, double distance) LaunchOrigin(Planet p) => LaunchOrigin(p.MapX, p.MapY, p.Sector);

        public bool InRange(ProbeType t, double x, double y, int sector = 0) => LaunchOrigin(x, y, sector).distance <= t.Range + 1e-9;
        public bool InRange(ProbeType t, Planet p) => InRange(t, p.MapX, p.MapY, p.Sector);

        /// <summary>Через сколько ходов будет результат: 1 — уже к следующему ходу.</summary>
        public int ProbeTurns(ProbeType t, double x, double y, ProbeMission mission, int sector = 0)
        {
            int flight = Math.Max(1, (int)Math.Ceiling(LaunchOrigin(x, y, sector).distance / t.Speed));
            return flight + (mission == ProbeMission.Explore ? Balance.ExploreTurnsOnSite : 0);
        }

        public int ProbeTurns(ProbeType t, Planet p) => ProbeTurns(t, p.MapX, p.MapY, ProbeMission.Explore, p.Sector);

        /// <summary>Летит ли уже зонд разведывать эту планету.</summary>
        public bool ProbeHeadingTo(int planetId) => _probes.Any(x => x.TargetPlanetId == planetId);

        /// <summary>Скан области: все планеты в радиусе станут просканированными, и, может быть, найдётся скрытая.</summary>
        public ActionResult LaunchScan(ProbeType t, double x, double y, int sector = 0)
        {
            if (!HasLicense(sector)) return ActionResult.Fail($"Нет лицензии на сектор «{Balance.Sectors[sector].Name}»");
            if (!InRange(t, x, y, sector)) return ActionResult.Fail($"«{t.Name}» не долетит: слишком далеко от ваших планет и биржи");
            return Launch(t, ProbeMission.ScanArea, x, y, -1, sector);
        }

        /// <summary>Полная разведка планеты: точный запас и сюрпризы.</summary>
        public ActionResult LaunchExplore(ProbeType t, int planetId)
        {
            var p = GetPlanet(planetId);
            if (p == null) return ActionResult.Fail("Нет такой планеты");
            if (p.Knowledge == Knowledge.Explored || IsMine(p)) return ActionResult.Fail("Планета уже разведана");
            if (ProbeHeadingTo(planetId)) return ActionResult.Fail("К планете уже летит зонд");
            if (!HasLicense(p.Sector)) return NoLicense(p);
            if (!InRange(t, p)) return ActionResult.Fail($"«{t.Name}» не долетит: слишком далеко от ваших планет и биржи");
            return Launch(t, ProbeMission.Explore, p.MapX, p.MapY, planetId, p.Sector);
        }

        ActionResult Launch(ProbeType t, ProbeMission mission, double x, double y, int planetId, int sector)
        {
            if (!ProbeUnlocked(t)) return ActionResult.Fail($"Зонд «{t.Name}» откроется на уровне «{Balance.CorporationLevels[t.MinLevel].Name}»");
            if (Money < t.Cost) return ActionResult.Fail("Не хватает денег");
            double findBonus = Balance.GeologyFindStep * Edge(Skill.Geology);
            var a = SpendAction(Skill.Geology);
            if (!a.Ok) return a;

            Money -= t.Cost;
            var origin = LaunchOrigin(x, y, sector);
            _probes.Add(new Probe
            {
                Sector = sector,
                CorporationId = _current.Id,
                FindBonus = findBonus,
                Id = _nextProbeId++,
                Type = t,
                Mission = mission,
                FromX = origin.x,
                FromY = origin.y,
                TargetX = x,
                TargetY = y,
                TargetPlanetId = planetId,
                LaunchTurn = Turn,
                ResolveTurn = Turn + ProbeTurns(t, x, y, mission, sector) - 1,
            });
            return ActionResult.Success;
        }

        void ResolveProbes(TurnReport report)
        {
            foreach (var probe in _probes.Where(x => x.ResolveTurn <= Turn && x.CorporationId == _current.Id).ToList())
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
                if (p.Knowledge != Knowledge.Silhouette || p.Sector != probe.Sector) continue;
                if (Geometry.Distance(p.MapX, p.MapY, probe.TargetX, probe.TargetY) > probe.Type.ScanRadius) continue;
                p.Knowledge = Knowledge.Scanned;
                scanned++;
            }
            string line = $"Зонд «{probe.Type.Name}» просканировал область: новых планет в скане — {scanned}";
            if (IsSectorFull(probe.Sector)) line += ". Скрытых планет в секторе не осталось";
            else if (_rng.Chance(probe.Type.FindChance + probe.FindBonus))
            {
                var found = GeneratePlanet(RandomType(probe.Sector), probe.Sector);
                PlaceOnMap(found, probe.TargetX, probe.TargetY, probe.Type.ScanRadius);
                found.FoundByProbe = true;
                found.Knowledge = Knowledge.Scanned;
                _planets.Add(found);
                line += $". Найдена скрытая планета {found.Type.Name} #{found.Id}: её видите только вы";
                _current.Stats.HiddenFound++;
            }
            report.Events.Add(line + ".");
        }

        void ResolveExplore(Probe probe, TurnReport report)
        {
            var p = GetPlanet(probe.TargetPlanetId);
            if (p == null) return;
            p.Knowledge = Knowledge.Explored;
            _current.Stats.PlanetsExplored++;
            report.Events.Add(DescribeExploration(p));
            if (IsMine(p)) LootRuins(p, report.Events);
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
            if (!IsMine(p) || p.Knowledge != Knowledge.Explored || !p.HasUnlootedRuins) return;
            p.RuinsLooted = true;
            Money += p.RuinsValue;
            p.Earned += p.RuinsValue;
            events.Add($"Артефакты из руин на {p.Type.Name} #{p.Id} проданы за {Math.Round(p.RuinsValue)}.");
        }

        /// <summary>Нанять менеджера. Даёт действие со следующего хода.</summary>
        /// <summary>Нанять кандидата: работает со следующего хода, зарплата — каждый ход. Тратит действие.</summary>
        public ActionResult HireManager(int candidateIndex = 0)
        {
            if (Managers >= MaxManagers)
                return ActionResult.Fail(NextLevelWith(l => l.Managers > MaxManagers) is CorporationLevel more
                    ? $"Больше менеджеров — на уровне «{more.Name}»"
                    : "Больше менеджеров нанять нельзя");
            if (candidateIndex < 0 || candidateIndex >= _current.Candidates.Count) return ActionResult.Fail("Нет такого кандидата");
            var a = SpendAction();
            if (!a.Ok) return a;
            var m = _current.Candidates[candidateIndex];
            _current.Candidates.RemoveAt(candidateIndex);
            m.ActionsLeft = 0;
            _current.Staff.Add(m);
            return ActionResult.Success;
        }

        /// <summary>Уволить менеджера; −1 — последнего нанятого. Действия не тратит.</summary>
        public ActionResult FireManager(int managerId = -1)
        {
            var m = managerId < 0 ? _current.Staff.LastOrDefault(x => !x.IsDirector) : _current.Staff.FirstOrDefault(x => x.Id == managerId && !x.IsDirector);
            if (m == null) return ActionResult.Fail("Такого менеджера нет");
            _current.Staff.Remove(m);
            if (_current.PerformerId == m.Id) _current.PerformerId = -1;
            return ActionResult.Success;
        }

        /// <summary>Новые кандидаты на работу.</summary>
        void RefreshCandidates()
        {
            _current.Candidates.Clear();
            for (int i = 0; i < Balance.ManagerCandidates; i++)
                _current.Candidates.Add(Manager.Candidate(_nextManagerId++, _staffRng, Balance.ManagerSalary, Balance.ManagerSalaryPerSkill));
            _current.NextCandidatesTurn = Turn + Balance.CandidateRefreshTurns;
        }

        // ------------------------------------------------------------------
        // Бесплатные действия
        // ------------------------------------------------------------------

        public ActionResult SellResource(Resource r, double amount)
        {
            if (amount <= 0) return ActionResult.Fail("Нечего продавать");
            if (_stock[(int)r] + 1e-9 < amount) return ActionResult.Fail("Столько ресурса нет на складе");
            _stock[(int)r] -= amount;
            double proceeds = Market.Sell(r, amount, MarketDepthFactor) * (1 - CartelCut);
            Money += proceeds;
            _current.Stats.SoldOnExchange += proceeds;
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
            _current.Stats.OrdersFulfilled++;
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

        /// <summary>Отчёты всех игроков за последний ход. События у них общие.</summary>
        public IReadOnlyDictionary<int, TurnReport> LastReports => _lastReports;
        [NonSerialized] Dictionary<int, TurnReport> _lastReports = new Dictionary<int, TurnReport>();

        /// <summary>
        /// Конец хода для всех. Сначала каждый игрок: добыча; затем общее: магнаты добывают, месторождения восстанавливаются;
        /// снова каждый игрок: исследования, грузы, жители, расходы, коллекторы, зонды, подкупы; затем общее: заказы, открытия,
        /// магнаты покупают; каждый игрок: пакости магнатов; общее: магнаты развиваются; каждый игрок: реплики, репутация;
        /// общее: биржа, события. Возвращает отчёт текущего игрока.
        /// </summary>
        public TurnReport EndTurn()
        {
            var caller = _current;
            var events = new List<string>();
            var players = Players.ToList();
            var reports = players.ToDictionary(c => c.Id, c => new TurnReport(events) { Turn = Turn });
            var shared = reports[caller.Id];
            if (players.All(c => c.Bankrupt))
            {
                events.Add("Корпорация обанкротилась, партия окончена.");
                return shared;
            }
            var active = players.Where(c => !c.Bankrupt).ToList();
            // Магнаты живут по тем же правилам, но их аварии и забастовки игроку не показываем.
            var rivals = _magnates.Select(m => m.Corp).Where(c => c != null && !c.Bankrupt).ToList();
            foreach (var c in rivals) reports[c.Id] = new TurnReport { Turn = Turn };

            void Each(Action<TurnReport> phase) => Run(active, phase);
            void EachCorp(Action<TurnReport> phase) => Run(active.Concat(rivals), phase);
            void Run(IEnumerable<Corporation> corps, Action<TurnReport> phase)
            {
                foreach (var c in corps)
                {
                    _current = c;
                    phase(reports[c.Id]);
                }
                _current = caller;
            }

            // Кто не ответил на случай до конца хода — промолчал.
            Each(ExpireIncident);
            EachCorp(ProducePlayer);
            Regenerate(shared);
            EachCorp(r =>
            {
                AdvanceResearch(r);
                DeliverShipments();
                UpdatePeople(r);
                PayCosts(r);
                ResolveProbes(r);
                ResolveBribes(r);
                ResolveClaims(r);
            });
            UpdateOrders(shared);
            Discover(shared);
            AiTurn(shared);
            Run(rivals, r => MagnateManage(_current.Magnate, r));
            ReplaceBankruptMagnates(shared);
            Each(MagnatesPester);
            Each(r =>
            {
                MagnatesRemark(r);
                // Репутация понемногу возвращается к 50.
                if (Reputation < 50) ChangeReputation(Math.Min(Balance.ReputationRecovery, 50 - Reputation));
                else if (Reputation > 50) ChangeReputation(-Math.Min(Balance.ReputationRecovery, Reputation - 50));
            });
            Market.Tick(_rng);
            Market.SectorFactor = Math.Pow(Math.Max(1, active.Count), Balance.MarketGrowthPower);
            UpdateEvent(shared);
            ResolveLot(reports, shared);
            UpdateShow(reports, shared);

            Turn++;
            EachCorp(_ => ResetActions());
            // Уровень — по капиталу на начало нового хода; магнаты растут по тем же правилам.
            Each(r => UpdateLevel(r, true));
            Run(rivals, r => UpdateLevel(r, false));
            Each(_ => { if (Turn >= _current.NextCandidatesTurn) RefreshCandidates(); });
            NextSeason(reports, active);
            AnnounceLot(reports, active);
            Each(_ => OfferIncident());
            _lastReports = reports;
            return shared;
        }

        /// <summary>Расходы текущего игрока по состоянию на конец хода. Доставка уже посчитана при добыче, зарплаты жителям — в UpdatePeople.</summary>
        void PayCosts(TurnReport report)
        {
            report.Upkeep = UpkeepPerTurn;
            report.Salaries = SalariesPerTurn;
            report.Interest = Debt * Balance.InterestPerTurn;
            report.Insurance = InsurancePerTurn;
            report.Guards = GuardsPerTurn;
            // Множитель — один на сектор: считаем его раз, а не для каждой планеты (у магнатов их сотни).
            var multipliers = new Dictionary<int, double>();
            foreach (var p in PlayerPlanets)
            {
                if (!multipliers.TryGetValue(p.Sector, out double m)) multipliers[p.Sector] = m = UpkeepMultiplierIn(p.Sector);
                p.Earned -= p.BaseUpkeep(Balance) * m;
            }
            Money -= report.TotalCosts;

            if (Money < 0) CallCollectors(report);
        }

        void ProducePlayer(TurnReport report)
        {
            double techBonus = OutputBonus;
            // Инженеры берегут шахты от аварий и метеоритов. За время добычи их не прибавляется, поэтому бонус считаем один раз.
            double engineers = SpecialtyBonus(Specialty.Engineers);
            foreach (var p in PlayerPlanets)
            {
                // Шахта, которая достроилась в этот ход, начнёт добывать со следующего.
                bool justBuilt = false;
                if (p.Mine == MineState.Building && --p.BuildTurnsLeft <= 0)
                {
                    p.Mine = MineState.Working;
                    justBuilt = true;
                }
                if (p.OnStrike) continue;
                bool mainWorks = p.Mine == MineState.Working && !p.IsDepleted && !justBuilt;
                var extras = p.ExtraDeposits.Where(CanMine).ToList();
                if (!mainWorks && extras.Count == 0) continue;

                // Метеорит разбивает основную шахту; страховка возмещает её стоимость.
                if (p.Mine != MineState.None && _rng.Chance(Balance.MeteorChance * (Ev?.MeteorFactor ?? 1) * (1 - engineers)))
                {
                    p.Mine = MineState.None;
                    p.AccidentTurnsLeft = 0;
                    p.MineWreckedTurn = Turn;
                    p.MineWreckedByRiot = false;
                    mainWorks = false;
                    string line = $"Метеорит разбил шахту на {p.Type.Name} #{p.Id}";
                    if (Insured)
                    {
                        Money += p.Type.MineCost;
                        report.InsurancePayout += p.Type.MineCost;
                        line += $", страховка выплатила {Math.Round(p.Type.MineCost)}";
                    }
                    // Метеориты часты, а страховка их покрывает — только в журнал, без окна.
                    report.Events.Add(line + ".");
                }

                // Эпидемия на обитаемой планете: добыча вполовину, жители недовольны.
                if (p.Inhabited && p.EpidemicTurnsLeft == 0 && _rng.Chance(Balance.EpidemicChance * (Ev?.EpidemicFactor ?? 1)))
                {
                    p.EpidemicTurnsLeft = Balance.EpidemicTurns;
                    p.Loyalty = Clamp(p.Loyalty + Balance.EpidemicLoyalty);
                    report.Events.Add($"Эпидемия на {p.Type.Name} #{p.Id}: {Balance.EpidemicTurns} хода добыча вполовину, лояльность {Math.Round(p.Loyalty)}.");
                }

                if (p.AccidentTurnsLeft == 0 && _rng.Chance(Balance.AccidentChance * (Ev?.AccidentFactor ?? 1) * (1 - engineers)))
                {
                    p.AccidentTurnsLeft = Balance.AccidentTurns;
                    report.Accidents++;
                    report.Events.Add($"Авария на планете {p.Type.Name} #{p.Id}");
                }

                // Что добыли бы без аварии и эпидемии: основное месторождение и дополнительные.
                var batch = new List<(Resource r, double amount, Deposit d)>();
                if (mainWorks) batch.Add((p.Resource, Math.Min(p.Type.OutputPerTurn * (1 + techBonus), p.Reserve), null));
                foreach (var d in extras) batch.Add((d.Resource, Math.Min(d.OutputPerTurn * (1 + techBonus), d.Reserve), d));

                if (p.AccidentTurnsLeft > 0)
                {
                    p.AccidentTurnsLeft--;
                    if (Insured)
                        foreach (var x in batch)
                        {
                            double payout = x.amount * Market.Price(x.r);
                            Money += payout;
                            report.InsurancePayout += payout;
                        }
                    continue;
                }

                double share = 1;
                if (p.EpidemicTurnsLeft > 0)
                {
                    p.EpidemicTurnsLeft--;
                    share = Balance.EpidemicOutputShare;
                }

                foreach (var x in batch)
                {
                    double amount = x.amount * share;
                    if (Insured && share < 1)
                    {
                        double payout = x.amount * (1 - share) * Market.Price(x.r);
                        Money += payout;
                        report.InsurancePayout += payout;
                    }
                    p.Earned += amount * Market.Price(x.r);
                    Ship(p, x.r, amount, report);
                    if (x.d == null)
                    {
                        p.Reserve -= amount;
                        if (p.Reserve <= 1e-9)
                        {
                            StartRegen(p);
                            report.Events.Add($"{p.Type.Name} #{p.Id}: основное месторождение ({p.Resource.RuName()}) выработано, восстановится за {p.RegenTotal} х.");
                        }
                    }
                    else
                    {
                        x.d.Reserve -= amount;
                        if (x.d.Reserve <= 1e-9)
                        {
                            StartRegen(x.d, true);
                            report.Events.Add($"{p.Type.Name} #{p.Id}: месторождение «{x.d.Resource.RuName()}» выработано, восстановится за {x.d.RegenTotal} х.");
                        }
                    }
                }
            }
        }

        /// <summary>Добытое летит на биржу: стоит денег и, с дальних планет, времени.</summary>
        void Ship(Planet p, Resource r, double amount, TurnReport report)
        {
            double cost = ShippingCost(r, amount, DistanceToExchange(p));
            report.Shipping += cost;
            p.Earned -= cost;
            int turns = ShipTurns(p);
            if (turns == 0) _stock[(int)r] += amount;
            else _shipments.Add(new Shipment { Resource = r, Amount = amount, ResolveTurn = Turn + turns });
        }

        // ------------------------------------------------------------------
        // Восстановление месторождений и развитие
        // ------------------------------------------------------------------

        int RegenTurnsFor(Resource r, bool playerOwned) =>
            Math.Max(1, (int)Math.Round(Balance.RegenTurns[(int)r] * (playerOwned ? RegenFactor * (1 - SpecialtyBonus(Specialty.Chemists)) : 1)));

        /// <summary>Месторождение истощилось: восстановится, но немного беднее, чем было.</summary>
        void StartRegen(Planet p)
        {
            p.Reserve = 0;
            p.Capacity = Math.Max(p.Capacity * Balance.RegenCapacityShare, p.InitialReserve * Balance.RegenCapacityFloor);
            p.RegenTotal = RegenTurnsFor(p.Resource, IsMine(p));
            p.RegenTurnsLeft = p.RegenTotal;
        }

        void StartRegen(Deposit d, bool playerOwned)
        {
            d.Reserve = 0;
            d.Capacity = Math.Max(d.Capacity * Balance.RegenCapacityShare, d.InitialCapacity * Balance.RegenCapacityFloor);
            d.RegenTotal = RegenTurnsFor(d.Resource, playerOwned);
            d.RegenTurnsLeft = d.RegenTotal;
        }

        /// <summary>Выработанные месторождения понемногу наполняются и к концу срока снова полны.</summary>
        void Regenerate(TurnReport report)
        {
            foreach (var p in _planets)
            {
                if (p.RegenTurnsLeft > 0)
                {
                    p.Reserve += p.Capacity / p.RegenTotal;
                    if (--p.RegenTurnsLeft == 0)
                    {
                        p.Reserve = p.Capacity;
                        if (p.Owner == Owner.Player) report.Events.Add($"{p.Type.Name} #{p.Id}: основное месторождение восстановилось.");
                    }
                }
                foreach (var d in p.ExtraDeposits)
                {
                    if (d.RegenTurnsLeft <= 0) continue;
                    d.Reserve += d.Capacity / d.RegenTotal;
                    if (--d.RegenTurnsLeft == 0)
                    {
                        d.Reserve = d.Capacity;
                        if (p.Owner == Owner.Player && d.HasExtractor) report.Events.Add($"{p.Type.Name} #{p.Id}: месторождение «{d.Resource.RuName()}» восстановилось.");
                    }
                }
            }
        }

        public bool CanMine(Deposit d) => d.HasExtractor && !d.IsRegenerating && IsUnlocked(d.Resource);

        HashSet<string> _researched => _current.Researched;

        /// <summary>Исследование, которое идёт сейчас; null — никакого.</summary>
        public Research CurrentResearch { get => _current.CurrentResearch; private set => _current.CurrentResearch = value; }

        public bool IsResearched(string techId) => _researched.Contains(techId);

        IEnumerable<Technology> Researched => Balance.Technologies.Where(t => _researched.Contains(t.Id));

        /// <summary>Можно ли добывать ресурс на дополнительных месторождениях.</summary>
        public bool IsUnlocked(Resource r) => Researched.Any(t => t.UnlocksResource == r);

        public double OutputBonus => Researched.Sum(t => t.OutputBonus);
        public double RegenFactor => Researched.Aggregate(1.0, (f, t) => f * t.RegenFactor);
        public double TechLoyaltyBonus => Researched.Sum(t => t.LoyaltyBonus);

        /// <summary>Цена технологии: физики делают исследования дешевле.</summary>
        public double ResearchCost(Technology t) => Math.Round(t.Cost * (1 - SpecialtyBonus(Specialty.Physicists)));

        public bool CanResearch(Technology t) =>
            !_researched.Contains(t.Id) && t.Requires.All(_researched.Contains) && CurrentResearch?.Technology != t && HasLicense(t.Sector);

        /// <summary>Начать исследование: платите сразу, тратите действие, результат через несколько ходов.</summary>
        public ActionResult StartResearch(string techId)
        {
            var t = Balance.Technologies.FirstOrDefault(x => x.Id == techId);
            if (t == null) return ActionResult.Fail("Нет такой технологии");
            if (CurrentResearch != null) return ActionResult.Fail("Уже идёт исследование: " + CurrentResearch.Technology.Name);
            if (!HasLicense(t.Sector)) return ActionResult.Fail($"Нужна лицензия на сектор «{Balance.Sectors[t.Sector].Name}»");
            if (!CanResearch(t)) return ActionResult.Fail("Сначала нужны: " + string.Join(", ", t.Requires.Where(r => !_researched.Contains(r))
                .Select(r => Balance.Technologies.First(x => x.Id == r).Name)));
            double cost = ResearchCost(t);
            if (Money < cost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction();
            if (!a.Ok) return a;
            Money -= cost;
            CurrentResearch = new Research { Technology = t, TurnsLeft = t.Turns };
            return ActionResult.Success;
        }

        void AdvanceResearch(TurnReport report)
        {
            if (CurrentResearch == null || --CurrentResearch.TurnsLeft > 0) return;
            _researched.Add(CurrentResearch.Technology.Id);
            _current.Stats.ResearchDone++;
            report.Events.Add($"Исследование «{CurrentResearch.Technology.Name}» завершено: {CurrentResearch.Technology.Description}");
            CurrentResearch = null;
        }

        public double ExtractorCost(Deposit d) =>
            Math.Round(Balance.TypeOf(d.Resource).MineCost * Balance.ExtractorCostShare * (1 - Balance.GeologyCostStep * Edge(Skill.Geology)));

        /// <summary>Экстрактор на дополнительном месторождении: нужна технология для ресурса. Работает со следующего хода.</summary>
        public ActionResult BuildExtractor(int planetId, int depositIndex)
        {
            var p = GetPlanet(planetId);
            if (!IsMine(p)) return ActionResult.Fail("Это не ваша планета");
            if (depositIndex < 0 || depositIndex >= p.ExtraDeposits.Count) return ActionResult.Fail("Нет такого месторождения");
            var d = p.ExtraDeposits[depositIndex];
            if (d.HasExtractor) return ActionResult.Fail("Экстрактор уже стоит");
            if (!IsUnlocked(d.Resource)) return ActionResult.Fail($"Нужна технология добычи ресурса «{d.Resource.RuName()}»");
            double cost = ExtractorCost(d);
            if (Money < cost) return ActionResult.Fail("Не хватает денег");
            var a = SpendAction(Skill.Geology);
            if (!a.Ok) return a;
            Money -= cost;
            p.Invested += cost;
            d.HasExtractor = true;
            return ActionResult.Success;
        }

        // ------------------------------------------------------------------
        // Память магнатов: поступки игрока с жителями и реплики
        // ------------------------------------------------------------------

        List<Deed> _deeds => _current.Deeds;
        readonly List<Remark> _remarks = new List<Remark>();
        int _nextDeedId;

        public IReadOnlyList<Deed> Deeds => _deeds;
        public IReadOnlyList<Remark> RecentRemarks => _remarks;

        void RecordDeed(DeedKind kind, Planet p) =>
            _deeds.Add(new Deed { Id = _nextDeedId++, Turn = Turn, Kind = kind, PlanetId = p?.Id ?? -1, PlanetName = p == null ? "" : $"{p.Type.Name} #{p.Id}" });

        /// <summary>Добряк или живодёр: сумма поступков с жителями за последние ходы.</summary>
        public int Kindness => _deeds.Where(d => d.Turn > Turn - Balance.KindnessMemoryTurns).Sum(d => d.Kindness);

        /// <summary>Магнаты вспоминают свежие поступки игрока — каждый в своём характере.</summary>
        void MagnatesRemark(TurnReport report)
        {
            foreach (var m in _magnates)
            {
                if (!_rng.Chance(Balance.RemarkChance)) continue;
                var deed = _deeds
                    .Where(d => d.Turn > Turn - Balance.RemarkMemoryTurns && !m.CommentedDeeds.Contains(d.Id))
                    .OrderByDescending(d => d.Turn)
                    .FirstOrDefault();
                string text;
                if (deed != null)
                {
                    m.CommentedDeeds.Add(deed.Id);
                    text = Remarks.For(m.Personality, deed.Kind)?.Replace("{p}", deed.PlanetName);
                }
                else if (_deeds.Count > 0 && _rng.Chance(0.25))
                    text = Remarks.Overall(m.Personality, Kindness);
                else
                    continue;
                if (text == null) continue;
                _remarks.Add(new Remark { Turn = Turn, MagnateId = m.Id, Text = text });
                if (_remarks.Count > 30) _remarks.RemoveAt(0);
                report.Events.Add($"{m.Name}: «{text}»");
            }
        }

        void DeliverShipments()
        {
            foreach (var x in _shipments.Where(x => x.ResolveTurn <= Turn).ToList())
            {
                _stock[(int)x.Resource] += x.Amount;
                _shipments.Remove(x);
            }
        }

        IEnumerable<Planet> MagnatePlanets(Magnate m) => _planets.Where(p => p.OwnerCorp != null && p.OwnerCorp == m.Corp);

        /// <summary>Богатство магната, как капитал игрока: деньги минус долг, рыночная стоимость планет, склад по базовым ценам.</summary>
        public double MagnateWealth(Magnate m) =>
            m.Corp == null ? m.Money
                : m.Corp.Money - m.Corp.Debt + MagnatePlanets(m).Sum(p => p.MarketValue(Balance))
                  + ResourceTypes.All.Sum(r => (m.Corp.Stock[(int)r] + m.Corp.Shipments.Where(x => x.Resource == r).Sum(x => x.Amount)) * Balance.BasePrice(r));

        public int MagnatePlanetCount(Magnate m) => MagnatePlanets(m).Count();

        /// <summary>Сколько денег магнат держит про запас и не тратит.</summary>
        double MagnateReserve(Magnate m) => m.ReserveFactor * Balance.MagnateReserveTurns * MagnatePlanets(m).Sum(p => p.BaseUpkeep(Balance));

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
                Important(report, AlertKind.PlanetLost, "Коллекторы забрали планету",
                    $"Коллекторы забрали планету {best.Type.Name} #{best.Id}: денег не хватило, а долг превысил кредитный лимит.", best);
            }
            if (Debt < 0)
            {
                Money += -Debt;
                Debt = 0;
            }
            // Планет не осталось, а долг есть: платить нечем, корпорация банкрот, партия окончена.
            if (PlayerPlanetCount == 0 && Debt > 1e-9)
            {
                Bankrupt = true;
                Important(report, AlertKind.Bankrupt, "Банкротство",
                    $"Коллекторы забрали последнюю планету, а долг {Math.Round(Debt)} остался. Корпорация обанкротилась.");
            }
        }

        /// <summary>Банкротство: коллекторы забрали все планеты, а долг остался. Ходы больше не идут.</summary>
        public bool Bankrupt { get => _current.Bankrupt; private set => _current.Bankrupt = value; }

        void UpdateOrders(TurnReport report)
        {
            // Заказ снимается, когда груз уже не успеет даже при отправке в следующем ходу.
            _orders.RemoveAll(o => Turn + 1 + DeliveryTurns(o) > o.DeadlineTurn);
            // Репутация: при 0 заказов вдвое меньше, при 100 — в полтора раза больше.
            if (!_rng.Chance(Balance.OrderChancePerTurn * (0.5 + Reputation / 100) * (Ev?.OrderChanceFactor ?? 1))) return;

            var open = OpenResources;
            var r = open[_rng.Range(0, open.Length)];
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

        /// <summary>В секторе открыты все планеты: новые не находятся ни зондами, ни случайно.</summary>
        public bool IsSectorFull(int sector = 0) =>
            _planets.Count(p => p.Sector == sector) >= (sector == 0 ? Balance.MaxSectorPlanets : Balance.Sectors[sector].MaxPlanets);

        /// <summary>Случайные открытия в открытых секторах: новая планета попадает в реестр, её видят все, в том числе ИИ.</summary>
        void Discover(TurnReport report)
        {
            foreach (int sector in _openSectors.OrderBy(x => x).ToList())
            {
                if (IsSectorFull(sector) || !_rng.Chance(Balance.DiscoveryChancePerTurn)) continue;
                var p = GeneratePlanet(RandomType(sector), sector);
                _planets.Add(p);
                if (Players.Any(c => c.Licenses.Contains(sector)))
                    report.Events.Add($"Открыта новая планета {p.Type.Name} #{p.Id}" + (sector > 0 ? $" в секторе «{Balance.Sectors[sector].Name}»" : ""));
            }
        }

        /// <summary>Подкуп, о котором предупредили: если лояльность так и не поднялась, планета уходит к магнату.</summary>
        void ResolveBribes(TurnReport report)
        {
            foreach (var x in _bribes.Where(b => b.ResolveTurn <= Turn && IsMine(GetPlanet(b.PlanetId))).ToList())
            {
                _bribes.Remove(x);
                var p = GetPlanet(x.PlanetId);
                var m = GetMagnate(x.MagnateId);
                if (p.Loyalty < Balance.BribeSuccessLoyalty)
                {
                    GiveToMagnate(p, m);
                    PlanetsLostToBribes++;
                    Important(report, AlertKind.PlanetLost, "Жители ушли к магнату",
                        $"{m.Name} подкупил(а) жителей {p.Type.Name} #{p.Id}: планета теперь у магната.", p, m);
                }
                else
                    Important(report, AlertKind.Good, "Жители не продались",
                        $"Жители {p.Type.Name} #{p.Id} отказали {m.Name}: лояльность {Math.Round(p.Loyalty)}.", p, m);
            }
        }

        /// <summary>Конец хода: событие кончается или, после затишья, начинается новое — со следующего хода.</summary>
        void UpdateEvent(TurnReport report)
        {
            if (CurrentEvent != null && CurrentEvent.EndTurn <= Turn)
            {
                report.Events.Add($"Событие «{CurrentEvent.Event.Name}» закончилось.");
                Market.SetEvent(null);
                CurrentEvent = null;
                _nextEventTurn = Turn + 1 + _eventRng.Range(Balance.EventGapMin, Balance.EventGapMax + 1);
            }
            if (CurrentEvent != null || Balance.Events.Length == 0 || Turn + 1 < _nextEventTurn) return;

            GalacticEvent ev;
            do ev = Balance.Events[_eventRng.Range(0, Balance.Events.Length)];
            while (ev == _lastEvent && Balance.Events.Length > 1);
            _lastEvent = ev;
            CurrentEvent = new ActiveEvent { Event = ev, StartTurn = Turn + 1, EndTurn = Turn + ev.Duration };
            Market.SetEvent(ev.PriceFactor);
            report.Events.Add($"Событие «{ev.Name}» на {ev.Duration} ходов: {ev.Description}");
        }

        // ------------------------------------------------------------------
        // Мозг магната: те же действия, что у игрока
        // ------------------------------------------------------------------

        /// <summary>
        /// Магнат распоряжается своей корпорацией через те же действия, что доступны игроку:
        /// продаёт на бирже, строит шахты, ставит налог, устраивает праздники, строит школы, учит жителей,
        /// исследует, гасит долг. Характер решает, насколько он жаден и заботлив.
        /// </summary>
        void MagnateManage(Magnate m, TurnReport report)
        {
            var b = Balance;
            double before = Money;
            double sold = 0;

            // Магнат продаёт всё по текущей цене биржи и цену не двигает: её роняют только игроки.
            foreach (var r in ResourceTypes.All)
            {
                double amount = _stock[(int)r];
                if (amount <= 0) continue;
                double revenue = amount * Market.Price(r);
                _stock[(int)r] = 0;
                Money += revenue;
                sold += revenue;
            }

            if (Debt > 0 && Money > MagnateReserve(m)) RepayLoan(Math.Min(Debt, Money - MagnateReserve(m)));

            foreach (var p in PlayerPlanets.Where(x => x.Mine == MineState.None && !x.IsDepleted).ToList())
                if (Money - MineCost(p) >= MagnateReserve(m)) BuildMine(p.Id);

            // Жители: налог по настроению, праздник, если дело идёт к забастовке, школа и учёба, когда есть деньги.
            // Выскочка дерёт налог почти всегда, шейх — бережно.
            var who = m.Personality;
            double highTax = who == Personality.Greedy ? 55 : who == Personality.Cautious || who == Personality.Sheikh ? 75 : who == Personality.Upstart ? 40 : 65;
            double lowTax = who == Personality.Greedy ? 35 : who == Personality.Cautious || who == Personality.Sheikh ? 50 : who == Personality.Upstart ? 20 : 45;
            // Выскочка наукой не занимается: ни школ, ни учёбы, ни исследований.
            bool learns = who != Personality.Upstart;
            foreach (var p in PlayerPlanets.Where(x => x.Inhabited).ToList())
            {
                SetTax(p.Id, p.Loyalty >= highTax ? TaxLevel.High : p.Loyalty >= lowTax ? TaxLevel.Low : TaxLevel.None);
                if ((p.Loyalty < 40 || p.OnStrike) && Money - b.FestivalCost >= MagnateReserve(m)) HoldFestival(p.Id);
                if (!learns) continue;
                bool caring = who == Personality.Cautious || who == Personality.Sheikh || _rng.Chance(0.3);
                if (!p.HasSchool && caring && Money - b.SchoolCost >= MagnateReserve(m) + 1000) BuildSchool(p.Id);
                if (_rng.Chance(b.MagnateEducateChance) && CanEducate(p, out _) && Money - EducationCost(p) >= MagnateReserve(m))
                    Educate(p.Id, p.Education > 0 ? p.Specialty : Specialties.Natural(p.Resource));
            }

            // Исследует, когда богат: то, что дешевле. Наследник — охотнее, его учили лучшие профессора галактики.
            if (learns && CurrentResearch == null && _rng.Chance(who == Personality.Heir ? 0.2 : 0.1))
            {
                var tech = b.Technologies.Where(CanResearch).OrderBy(ResearchCost).FirstOrDefault();
                if (tech != null && Money - ResearchCost(tech) >= MagnateReserve(m) + 3 * ResearchCost(tech)) StartResearch(tech.Id);
            }

            var r0 = report;
            m.LastIncome = sold + r0.Taxes + r0.Intellect - r0.TotalCosts;
        }

        /// <summary>
        /// Слияния: богатый магнат поглощает того, кто беднее во много раз, — забирает планеты, деньги и долги.
        /// Разорившийся или поглощённый магнат уходит из сектора, на его место приходит новый с такими же повадками.
        /// </summary>
        void ReplaceBankruptMagnates(TurnReport report)
        {
            if (_magnates.Count >= 2 && _rng.Chance(Balance.MergerChance))
            {
                var byWealth = _magnates.Where(x => x.Corp != null && !x.Corp.Bankrupt).OrderByDescending(MagnateWealth).ToList();
                var rich = byWealth.FirstOrDefault();
                var poor = byWealth.LastOrDefault();
                if (rich != null && poor != null && rich != poor && rich.Sector == poor.Sector && MagnateWealth(rich) >= Balance.MergerWealthRatio * Math.Max(1, MagnateWealth(poor)))
                {
                    foreach (var p in MagnatePlanets(poor).ToList()) p.OwnerCorp = rich.Corp;
                    rich.Corp.Money += poor.Corp.Money;
                    rich.Corp.Debt += poor.Corp.Debt;
                    for (int r = 0; r < ResourceTypes.Count; r++) rich.Corp.Stock[r] += poor.Corp.Stock[r];
                    foreach (var x in _bribes.Where(x => x.MagnateId == poor.Id)) x.MagnateId = rich.Id;
                    foreach (var x in _claims.Where(x => x.MagnateId == poor.Id)) x.MagnateId = rich.Id;
                    poor.Corp.Money = 0;
                    poor.Corp.Debt = 0;
                    poor.Corp.Bankrupt = true;
                    report.Events.Add($"{rich.Name} поглотил(а) корпорацию {poor.Name}. Теперь у него {MagnatePlanetCount(rich)} планет.");
                }
            }

            for (int i = 0; i < _magnates.Count; i++)
            {
                var old = _magnates[i];
                if (old.Corp == null || !old.Corp.Bankrupt) continue;
                _claims.RemoveAll(x => x.MagnateId == old.Id);
                foreach (var p in MagnatePlanets(old).ToList()) ReleaseToRegistry(p);
                _bribes.RemoveAll(x => x.MagnateId == old.Id);
                var pattern = Balance.Magnates.FirstOrDefault(x => x.Personality == old.Personality) ?? old;
                var fresh = Magnate.Generate(_magnates.Max(x => x.Id) + 1, pattern, _rng);
                fresh.Sector = old.Sector;
                AddMagnateCorp(fresh, old.Sector == 0 ? Balance.MagnateStartMoney : Balance.Sectors[old.Sector].MagnateMoney);
                _magnates[i] = fresh;
                report.Events.Add($"{old.Name} разорился(ась) и покидает сектор. На его место приходит {fresh.Name} ({fresh.PersonalityName}).");
            }
        }

        /// <summary>Магнаты покупают планеты на свои деньги и иногда перепродают свои.</summary>
        void AiTurn(TurnReport report)
        {
            var active = ActiveMagnates.ToList();
            // Каждый магнат покупает на свои деньги: богатый магнат растёт, бедный ждёт.
            foreach (var buyer in active.OrderBy(_ => _rng.NextULong()))
            {
                if (!_rng.Chance(Balance.AiBuyChance * buyer.AuctionInterest / 0.45)) continue;
                double budget = buyer.Money - MagnateReserve(buyer);
                var best = _planets
                    .Where(p => p.Owner == Owner.Registry && !p.IsDepleted && p.Sector == buyer.Sector && !p.FoundByProbe && !IsLot(p) && p.ListPrice(Balance) <= budget)
                    // ИИ знает о планетах то же, что реестр.
                    .OrderByDescending(p => p.EstimatedValue(Balance) / Math.Max(1, p.ListPrice(Balance)))
                    .FirstOrDefault();
                if (best == null || best.EstimatedValue(Balance) <= 0) continue;
                buyer.Money -= best.ListPrice(Balance);
                GiveToMagnate(best, buyer);
                report.Events.Add($"{buyer.Name} купил(а) планету {best.Type.Name} #{best.Id}.");
            }

            if (_rng.Chance(Balance.AiResaleChance))
            {
                // Магнат избавляется от худшей планеты, если их у него хотя бы три.
                var sellers = _magnates.Where(x => x.Corp != null && !x.Corp.Bankrupt && MagnatePlanetCount(x) >= 3).ToList();
                if (sellers.Count > 0)
                {
                    var p = MagnatePlanets(sellers[_rng.Range(0, sellers.Count)]).OrderBy(x => x.MarketValue(Balance)).First();
                    var seller = GetMagnate(p.MagnateId);
                    if (seller != null) seller.Money += p.MarketValue(Balance) * (1 - Balance.ResaleCommission);
                    p.OwnerCorp = null;
                    report.Events.Add($"{seller?.Name ?? "ИИ-магнат"} выставил(а) на продажу планету {p.Type.Name} #{p.Id}.");
                }
            }

        }

        /// <summary>Пакости магнатов текущему игроку. Чем он заметнее, тем больше пакостей: при внимании выше 1 бывает и по нескольку за ход.</summary>
        void MagnatesPester(TurnReport report)
        {
            var active = ActiveMagnates.ToList();
            double attention = MagnateAttention;
            // Лидера травят сообща: картель со скупщиками.
            if (!CartelActive && active.Count >= 2 && Rivalry >= Balance.CartelRivalry && _rng.Chance(Balance.CartelChance))
            {
                _current.CartelUntilTurn = Turn + Balance.CartelTurns;
                Important(report, AlertKind.Trick, "Картель против вас",
                    $"Магнаты сговорились со скупщиками против вас: {Balance.CartelTurns} ходов биржа платит на {Math.Round(CartelCut * 100)}% меньше" +
                    (Reputation >= Balance.CartelReputationShield ? " (репутация смягчила удар вдвое)." : ". Репутация от " + Balance.CartelReputationShield + " смягчила бы удар вдвое."));
            }
            double crowd = Balance.RivalMagnates > 0 && _magnates.Count > Balance.RivalMagnates ? Balance.RivalMagnates / (double)_magnates.Count : 1;
            foreach (var m in active)
            {
                double expected = m.TrickChance * attention * crowd;
                int tricks = (int)Math.Floor(expected) + (_rng.Chance(expected - Math.Floor(expected)) ? 1 : 0);
                for (int i = 0; i < tricks; i++) PlayTrick(m, PickTrick(m), report);
            }
        }

        /// <summary>
        /// Насколько магнаты замечают игрока. Пока у него мало планет — меньше 1. Когда он богаче среднего магната,
        /// внимание растёт пропорционально, но не выше MagnateMaxAttention: лидера травят все.
        /// </summary>
        public double MagnateAttention
        {
            get
            {
                double ramp = Balance.MagnateAttentionPlanets <= 0 ? 1 : Math.Min(1, PlayerPlanetCount / (double)Balance.MagnateAttentionPlanets);
                return ramp * Rivalry;
            }
        }

        /// <summary>
        /// Во сколько раз игрок богаче среднего магната там, где он ведёт дела, — в секторах с его лицензией
        /// (от 1 до MagnateMaxAttention). Магнаты секторов, куда игрок не ходит, ему не соперники.
        /// </summary>
        public double Rivalry
        {
            get
            {
                var rivals = _magnates.Where(m => HasLicense(m.Sector)).ToList();
                if (rivals.Count == 0) rivals = _magnates;
                if (rivals.Count == 0) return 1;
                double average = rivals.Average(MagnateWealth);
                if (average <= 1) return Balance.MagnateMaxAttention;
                return Math.Min(Balance.MagnateMaxAttention, Math.Max(1, Capital / average));
            }
        }


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

        /// <summary>Событие, которое серьёзно касается текущей корпорации: строка в журнал и окно «Важное за ход».</summary>
        static void Important(TurnReport report, AlertKind kind, string title, string text, Planet p = null, Magnate m = null)
        {
            report.Events.Add(text);
            report.Alerts.Add(new Alert { Kind = kind, Title = title, Text = text, PlanetId = p?.Id ?? -1, MagnateId = m?.Id ?? -1 });
        }

        void PlayTrick(Magnate m, TrickKind kind, TurnReport report)
        {
            switch (kind)
            {
                case TrickKind.Bribe:
                {
                    // Осторожная не трогает заботливых, жадный щадит «родственную душу».
                    if (m.Personality == Personality.Cautious && Kindness >= Balance.CautiousSparesKindness) return;
                    if (m.Personality == Personality.Greedy && Kindness <= Balance.GreedyLikesKindness && _rng.Chance(0.5)) return;
                    var target = PlayerPlanets
                        .Where(p => p.Inhabited && !p.IsDepleted && !_bribes.Any(b => b.PlanetId == p.Id)
                                    && p.Loyalty < (p.Guarded ? Balance.GuardedBribeLoyalty : Balance.BribeTargetLoyalty))
                        .OrderBy(p => p.Loyalty)
                        .FirstOrDefault();
                    if (target == null) return;
                    double bribeCost = Math.Round(target.BasePrice() * Balance.PlayerBribeShare);
                    if (m.Money - bribeCost < MagnateReserve(m)) return;
                    m.Money -= bribeCost;
                    _bribes.Add(new BribeAttempt { Id = _nextBribeId++, PlanetId = target.Id, MagnateId = m.Id, ResolveTurn = Turn + Balance.BribeWarningTurns });
                    Important(report, AlertKind.Threat, "Магнат подкупает жителей",
                        $"{m.Name} подкупает жителей {target.Type.Name} #{target.Id}! Через {Balance.BribeWarningTurns} хода они уйдут, " +
                        $"если лояльность не поднимется до {Math.Round(Balance.BribeSuccessLoyalty)}. Помогут праздник или встречный подкуп.", target, m);
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
                case TrickKind.HostileClaim:
                {
                    // Новичка не трогают; жители своих не отдают — оспаривают только необитаемые.
                    if (PlayerPlanetCount < Balance.MagnateAttentionPlanets) return;
                    var target = PlayerPlanets
                        .Where(p => !p.Inhabited && !p.IsDepleted && !_claims.Any(c => c.PlanetId == p.Id))
                        .OrderByDescending(p => p.MarketValue(Balance))
                        .FirstOrDefault();
                    if (target == null) return;
                    double compensation = target.MarketValue(Balance) * Balance.ClaimCompensationShare;
                    if (m.Money - compensation < MagnateReserve(m)) return;
                    _claims.Add(new HostileClaim { Id = _nextClaimId++, PlanetId = target.Id, MagnateId = m.Id, ResolveTurn = Turn + Balance.ClaimWarningTurns });
                    Important(report, AlertKind.Threat, "Иск в реестре",
                        $"{m.Name} оспаривает в реестре права на {target.Type.Name} #{target.Id}! Через {Balance.ClaimWarningTurns} хода планета уйдёт к нему за полцены, " +
                        $"если не оплатить защиту ({Math.Round(ClaimDefenseCost(target))}).", target, m);
                    return;
                }
                case TrickKind.PoachScientists:
                {
                    // Осторожная не трогает заботливых и здесь.
                    if (m.Personality == Personality.Cautious && Kindness >= Balance.CautiousSparesKindness) return;
                    var target = PlayerPlanets
                        .Where(p => p.Inhabited && p.Education > 0 && !p.IsStudying && p.Loyalty < Balance.PoachScientistsBelowLoyalty)
                        .OrderByDescending(p => p.Education).ThenBy(p => p.Loyalty)
                        .FirstOrDefault();
                    if (target == null) return;
                    target.Education--;
                    Important(report, AlertKind.Trick, "Учёных переманили",
                        $"{m.Name} переманил(а) учёных с {target.Type.Name} #{target.Id}: образование упало до уровня {target.Education}. " +
                        $"Жители с лояльностью от {Math.Round(Balance.PoachScientistsBelowLoyalty)} не уезжают.", target, m);
                    return;
                }
                case TrickKind.PoachManager:
                {
                    var best = _current.Staff.Where(x => !x.IsDirector).OrderByDescending(x => x.TotalSkill).FirstOrDefault();
                    if (best == null) return;
                    _current.Staff.Remove(best);
                    if (_current.PerformerId == best.Id) _current.PerformerId = -1;
                    Important(report, AlertKind.Trick, "Менеджера переманили",
                        $"{m.Name} переманил(а) вашего лучшего менеджера — {best.Name}. Действий на ход станет меньше.", null, m);
                    return;
                }
            }
        }
    }
}
