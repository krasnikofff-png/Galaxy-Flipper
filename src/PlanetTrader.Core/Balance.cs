using System;

namespace PlanetTrader.Core
{
    public sealed class PlanetType
    {
        public string Name;
        public Resource Resource;
        /// <summary>Цена планеты при среднем запасе (до надбавки аукциона).</summary>
        public double Price;
        public double Upkeep;
        public double MineCost;
        public double OutputPerTurn;
        public double AverageReserve;
        /// <summary>Относительная частота в секторе.</summary>
        public double Weight;
        /// <summary>Встречается только в этом секторе (новые ресурсы этапа 8); −1 — во всех.</summary>
        public int OnlySector = -1;

        public PlanetType(string name, Resource resource, double price, double upkeep,
            double mineCost, double output, double reserve, double weight)
        {
            Name = name;
            Resource = resource;
            Price = price;
            Upkeep = upkeep;
            MineCost = mineCost;
            OutputPerTurn = output;
            AverageReserve = reserve;
            Weight = weight;
        }
    }

    public static class PlanetTypes
    {
        /// <summary>Тот же тип в богатом секторе: добыча, запас, цена и шахта × richness, содержание × upkeep.</summary>
        public static PlanetType Scaled(this PlanetType t, double richness, double upkeep) =>
            richness == 1 && upkeep == 1 ? t
                : new PlanetType(t.Name, t.Resource, t.Price * richness, t.Upkeep * upkeep, t.MineCost * richness,
                    t.OutputPerTurn * richness, t.AverageReserve * richness, t.Weight) { OnlySector = t.OnlySector };
    }

    /// <summary>
    /// Все числа экономики. Исходные значения взяты из таблицы
    /// «Planet Trader — баланс экономики прототипа» (вкладки «Параметры», «Типы планет»,
    /// «Кривая с полкой») и подогнаны симулятором под «План проверки» — см. docs/BALANCE.md.
    /// Параметры, которых в таблице не было, помечены «допущение».
    /// </summary>
    public sealed class Balance
    {
        // --- Старт ---
        public double StartMoney = 2300;
        public double StartDebt = 1000;
        /// <summary>Есть ли на стартовой планете готовая шахта (допущение).</summary>
        public bool StartPlanetHasMine = true;

        // --- Кредит ---
        public double InterestPerTurn = 0.02;
        /// <summary>Сколько можно занять: доля рыночной стоимости планет.</summary>
        public double CreditLimitShare = 0.80;

        // --- Ход и менеджеры ---
        /// <summary>Действий за ход у самого игрока.</summary>
        public int BaseActionsPerTurn = 3;
        /// <summary>Каждый нанятый менеджер даёт +1 действие (допущение: в таблице только зарплата и максимум).</summary>
        public int ActionsPerManager = 1;
        /// <summary>Зарплата менеджера со средними навыками; за каждое очко навыков выше или ниже среднего — ± эта доля.</summary>
        public double ManagerSalary = 60;
        public double ManagerSalaryPerSkill = 0.12;
        /// <summary>Сколько кандидатов на выбор и раз во сколько ходов они меняются.</summary>
        public int ManagerCandidates = 3;
        public int CandidateRefreshTurns = 8;
        /// <summary>
        /// Что даёт каждое очко навыка выше среднего (ниже — наоборот): переговоры — скидка при покупке и сила ставки,
        /// прибавка к шансу согласия жителей и к цене продажи планеты; геология — скидка на шахты и экстракторы,
        /// прибавка к шансу найти скрытую планету; грязные дела — реже штраф, чаще удаётся подкуп, дольше действует слух;
        /// работа с людьми — сильнее праздник, дешевле школы, учёба и колонисты.
        /// </summary>
        public double NegotiationPriceStep = 0.08;
        public double NegotiationChanceStep = 0.08;
        public double NegotiationSaleStep = 0.05;
        public double GeologyCostStep = 0.1;
        public double GeologyFindStep = 0.08;
        public double ShadyFineStep = 0.3;
        public double ShadyChanceStep = 0.12;
        public double PeopleFestivalStep = 0.25;
        public double PeopleCostStep = 0.1;
        /// <summary>
        /// Кого автоматически ставить на дело: навык минус эта доля разрыва до его сильной стороны.
        /// Геолога с геологией 5 и переговорами 3 не отправят торговаться, если директор торгуется так же, —
        /// его действие сбережётся для шахты. Заметно лучший в деле всё равно возьмётся за него.
        /// </summary>
        public double SpecialistReserve = 0.6;

        // --- Секторы (этап 8, допущения) ---
        /// <summary>
        /// Решение автора: у каждого сектора своя карта; лицензию продают за уровень корпорации и деньги, репутация не нужна.
        /// Планеты новых секторов дороже, но выгоднее на одно место в кривой содержания: добыча растёт быстрее содержания.
        /// Число планет стартового сектора — SectorPlanets и MaxSectorPlanets.
        /// </summary>
        public SectorSpec[] Sectors =
        {
            new SectorSpec("Окраина") { Description = "Стартовый сектор: скромные планеты, все шесть ресурсов." },
            new SectorSpec("Огненный пояс")
            {
                LicenseLevel = 2, LicenseCost = 15_000, Richness = 1.6, UpkeepFactor = 1.4, MagnateMoney = 20_000,
                Description = "Планеты богаче: добыча, запас и цена ×1,6, содержание ×1,4. Свои магнаты.",
                // Персонажи автора. Веса пакостей: подкуп, слух, заказчик, менеджер, учёные, рейдерский захват.
                Magnates = new[]
                {
                    new Magnate(10, "Нова Рики", Personality.Upstart, trickChance: 0.25, auctionInterest: 0.55, bidMin: 1.0, bidMax: 1.4,
                        trickWeights: new[] { 0.15, 0.45, 0.35, 0.05, 0.0, 0.1 })
                        { Bio = "Ещё вчера пасла космокоз на захолустной луне, сегодня скупает планеты. Грубая, крикливая, книжек не читает и гордится этим." },
                    new Magnate(11, "Орфей Младший", Personality.Heir, trickChance: 0.18, auctionInterest: 0.7, bidMin: 0.95, bidMax: 1.35,
                        trickWeights: new[] { 0.1, 0.2, 0.1, 0.35, 0.1, 0.4 })
                        { MoneyFactor = 2, ReserveFactor = 0.4, Bio = "Молодой наследник магната, который недавно отошёл от дел. Высокомерный и хвастливый; уверен, что всё заработал сам." },
                    new Magnate(12, "Ксенон", Personality.Sheikh, trickChance: 0.08, auctionInterest: 0.45, bidMin: 1.0, bidMax: 1.25,
                        trickWeights: new[] { 0.5, 0.0, 0.0, 0.0, 0.0, 0.5 })
                        { MoneyFactor = 3, Bio = "Загадочный молчаливый «шейх». Никто не знает, откуда у его семьи столько денег. Говорят, у него есть планета, которой нет ни на одной карте." },
                },
            },
            new SectorSpec("Лазурная бездна")
            {
                LicenseLevel = 4, LicenseCost = 60_000, Richness = 2.5, UpkeepFactor = 2, MagnateMoney = 80_000,
                Description = "Самые богатые планеты: добыча, запас и цена ×2,5, содержание ×2. Свои магнаты.",
            },
        };

        // --- Стартовые персонажи (этап 8, допущения; выбрал автор) ---
        /// <summary>
        /// Навыки по порядку: переговоры, геология, грязные дела, работа с людьми. Сумма у всех 12, как у среднего директора.
        /// Без персонажа (симулятор, тесты) директор средний: все навыки 3.
        /// </summary>
        public StartCharacter[] Characters =
        {
            // Без минуса по деньгам Геолог к 120-му ходу на 14% богаче среднего директора, остальные — на 0–6%.
            new StartCharacter("Геолог", new[] { 2, 5, 2, 3 })
            {
                ExtraMoney = -300,
                ExtraDeposit = true,
                Plus = "Шахты и экстракторы на 20% дешевле, зонды чаще находят скрытые планеты. На стартовой планете — второе месторождение: металл или газ.",
                Minus = "На 300 меньше денег: всё ушло на буровые. Торгуется плохо: покупает дороже, продаёт дешевле, жители реже соглашаются.",
            },
            new StartCharacter("Аферист", new[] { 3, 3, 5, 1 })
            {
                ExtraMoney = 800,
                Reputation = 35,
                Plus = "На 800 больше денег на старте. Слухи и подкуп удаются чаще, реестр ловит реже.",
                Minus = "Репутация 35: заказов меньше, жители соглашаются реже. С людьми не ладит: праздники слабее, школы дороже.",
            },
            new StartCharacter("Филантроп", new[] { 3, 2, 2, 5 })
            {
                ExtraMoney = -300,
                Reputation = 65,
                Inhabited = true,
                Plus = "Стартовая планета обитаема: налоги, школа, учёба. Праздники сильнее, школы и учёба дешевле. Репутация 65.",
                Minus = "На 300 меньше денег. Шахты дороже. Жители требуют заботы: шахта снижает их лояльность.",
            },
        };

        // --- Уровни корпорации (этап 8, допущения) ---
        /// <summary>
        /// Уровень растёт с капиталом и не падает. Пороги — по кривой капитала «Активного» бота: Контора к ~30-му ходу,
        /// Компания к ~70-му, Корпорация к ~130-му, Концерн к ~250-му. Больше менеджеров — больше действий за ход.
        /// </summary>
        public CorporationLevel[] CorporationLevels =
        {
            new CorporationLevel("Стартап", 0, managers: 1),
            new CorporationLevel("Контора", 10_000, managers: 2),
            new CorporationLevel("Компания", 30_000, managers: 3),
            new CorporationLevel("Корпорация", 100_000, managers: 4),
            new CorporationLevel("Концерн", 300_000, managers: 5),
        };

        // --- Содержание: кривая с полкой ---
        public double UpkeepCurveA = 1.0;
        public double UpkeepCurveK = 1.6;
        public double UpkeepCurveP = 5;
        public int UpkeepShelfStart = 8;
        public int UpkeepShelfEnd = 13;
        /// <summary>Содержание выработанной планеты как доля обычного.</summary>
        public double DepletedUpkeepShare = 0.7;
        /// <summary>Считаются ли выработанные планеты в множителе содержания.</summary>
        public bool DepletedCountsInUpkeepCurve = false;
        /// <summary>
        /// У каждого сектора своя администрация (этап 8): в множителе содержания планеты других секторов считаются
        /// только этой долей. При 1 кривая общая, и новые секторы не окупаются — каждая их планета дорожит содержание всех;
        /// при 0 рост взрывается (×2,4 к 1000-му ходу).
        /// </summary>
        public double OtherSectorsUpkeepShare = 0.5;

        // --- Сделки с планетами ---
        public double ResaleCommission = 0.10;
        public double AuctionMarkup = 0.15;
        /// <summary>Ставка дисконтирования при оценке планеты.</summary>
        public double DiscountRate = 0.10;
        /// <summary>Запас планеты случайный ±20% от среднего.</summary>
        public double ReserveSpread = 0.20;
        public int MineBuildTurns = 2;

        // --- Биржа ---
        /// <summary>Цена падает на SalesImpact × (продано / глубина рынка).</summary>
        public double SalesImpact = 0.15;
        public double PriceRecovery = 0.15;
        public double MarketNoise = 0.08;
        /// <summary>Нижний предел цены как доля базовой (допущение).</summary>
        public double MinPriceShare = 0.20;
        /// <summary>Плазма и Дейтерий (этап 8) — свои рынки: в новых секторах продажи не роняют цены старых ресурсов.</summary>
        public double[] MarketDepth = { 150, 120, 140, 50, 35, 10, 30, 20 };
        /// <summary>
        /// В кооперативе рынок глубже: глубина × (число игроков)^это. Магнаты цены не двигают —
        /// продают по текущей цене биржи, её роняют только игроки.
        /// </summary>
        public double MarketGrowthPower = 0.5;

        // --- Заказы ---
        public double OrderMarkup = 1.5;
        public double OrderChancePerTurn = 0.5;
        /// <summary>Объём заказа в ходах добычи одной планеты (допущение).</summary>
        public int OrderMinTurnsOfOutput = 3;
        public int OrderMaxTurnsOfOutput = 7;
        /// <summary>Срок заказа, включая доставку заказчику (допущение; до этапа 3 было 5 ходов без доставки).</summary>
        public int OrderDeadlineTurns = 7;

        // --- Риски ---
        public double AccidentChance = 0.06;
        public int AccidentTurns = 2;
        /// <summary>Страховка за ход: доля стоимости застрахованных планет.</summary>
        public double InsuranceRate = 0.02;

        // --- ИИ и сектор ---
        public int SectorPlanets = 30;
        /// <summary>Шанс за ход, что в реестре появится новая открытая планета (~1 за 7 ходов).</summary>
        public double DiscoveryChancePerTurn = 1.0 / 7;
        /// <summary>Больше планет в секторе не бывает: открытия и находки зондов прекращаются (допущение; новые секторы — этап 8).</summary>
        public int MaxSectorPlanets = 60;
        public double AiStartShare = 0.33;
        /// <summary>Шанс за ход, что магнат со средним интересом к аукционам купит планету, если хватает денег.</summary>
        public double AiBuyChance = 0.12;
        /// <summary>Шанс, что ИИ выставит одну из своих планет на продажу (допущение).</summary>
        public double AiResaleChance = 0.10;

        // --- Разведка и зонды (этап 3, всё — допущения) ---
        /// <summary>
        /// Зонды: дороже — дальше, быстрее и шире скан. «Стандарт» соответствует старому зонду из таблицы (400, 60%).
        /// «Дальнобой» открывается на втором уровне корпорации.
        /// </summary>
        public ProbeType[] ProbeTypes =
        {
            new ProbeType("Москит", cost: 150, range: 0.35, speed: 0.35, scanRadius: 0.12, findChance: 0.30),
            new ProbeType("Стандарт", cost: 400, range: 0.70, speed: 0.50, scanRadius: 0.20, findChance: 0.60),
            new ProbeType("Дальнобой", cost: 900, range: 1.50, speed: 0.90, scanRadius: 0.30, findChance: 0.80) { MinLevel = 1 },
        };
        /// <summary>Сколько ходов зонд изучает планету при полной разведке, сверх полёта.</summary>
        public int ExploreTurnsOnSite = 0;
        /// <summary>Ошибка оценки запаса в реестре: настоящий запас отличается от оценки до ±20%.</summary>
        public double RegistryEstimateSpread = 0.20;
        /// <summary>«Подвох»: у части планет реестр сильно завышает запас. Видно только после разведки.</summary>
        public double DudChance = 0.15;
        /// <summary>Сколько на самом деле у планеты с подвохом от оценки реестра.</summary>
        public double DudReserveShare = 0.40;
        /// <summary>Планеты в этом радиусе от стартовой видны сканом с самого начала.</summary>
        public double StartScanRadius = 0.25;
        public double RuinsChance = 0.15;
        public double RuinsMinValue = 300;
        public double RuinsMaxValue = 1200;
        public double InhabitedChance = 0.20;
        /// <summary>
        /// Решение автора: жители продают свою планету только игроку, который её полностью разведал зондом.
        /// У реестра (на аукционе и напрямую) можно покупать и по оценке. Магнаты покупают по оценке реестра.
        /// </summary>
        public bool ResidentsNeedExploration = true;

        // --- Жители (этап 4, всё — допущения) ---
        /// <summary>Работающая шахта каждый ход снижает лояльность: выкачивание ресурсов.</summary>
        public double LoyaltyMiningDrain = 2;
        /// <summary>Налог за ход по уровням None, Low, High.</summary>
        public double[] TaxIncome = { 0, 35, 80 };
        /// <summary>Как налог меняет лояльность за ход по уровням None, Low, High.</summary>
        public double[] TaxLoyalty = { 0, -1, -3 };
        public double SchoolCost = 600;
        public double SchoolLoyalty = 2;
        public double WagesCost = 30;
        public double WagesLoyalty = 2;
        public double FestivalCost = 250;
        public double FestivalLoyalty = 15;
        /// <summary>Ниже — забастовка: ни добычи, ни налогов.</summary>
        public double StrikeBelow = 30;
        /// <summary>Забастовка кончается, когда лояльность поднимется до этого уровня.</summary>
        public double StrikeEndsAt = 40;
        public double RequestChancePerPlanet = 0.10;
        public int RequestTurns = 5;
        public double RequestLoyaltyReward = 20;
        public double RequestLoyaltyPenalty = 10;
        /// <summary>Шанс, что жители примут предложение, в зависимости от доли цены: 80% → 20%, 100% → 60%, 110% → 80%.</summary>
        public double OfferChanceAtFullPrice = 0.60;
        public double OfferChancePerShare = 2.0;
        /// <summary>Сделка «за школу»: доля цены и шанс согласия.</summary>
        public double SchoolDealShare = 0.75;
        public double SchoolDealChance = 0.70;
        /// <summary>Лояльность после покупки: 60 при полной цене, ±10 за каждые 10% цены.</summary>
        public double LoyaltyAfterFairDeal = 60;
        public double LoyaltyAfterSchoolDeal = 70;
        public int RefusalCooldownTurns = 3;

        // --- ИИ-магнаты (этап 5, всё — допущения) ---
        public Magnate[] Magnates =
        {
            new Magnate(0, "Гуго Жаднер", Personality.Greedy, trickChance: 0.20, auctionInterest: 0.60, bidMin: 0.90, bidMax: 1.20,
                trickWeights: new[] { 0.5, 0.2, 0.3, 0.0, 0.3, 0.3 }),
            new Magnate(1, "Зизи Квазар", Personality.Chaotic, trickChance: 0.30, auctionInterest: 0.40, bidMin: 0.80, bidMax: 1.30,
                trickWeights: new[] { 0.25, 0.35, 0.25, 0.15, 0.2, 0.2 }),
            new Magnate(2, "Матильда Ласкова", Personality.Cautious, trickChance: 0.12, auctionInterest: 0.35, bidMin: 0.90, bidMax: 1.15,
                trickWeights: new[] { 0.3, 0.2, 0.5, 0.0, 0.3, 0.1 }),
        };
        /// <summary>Сколько планет сектора приходится на одного магната. Если магнатов из списка меньше, недостающие придумываются.</summary>
        public int PlanetsPerMagnate = 10;
        /// <summary>
        /// Сколько магнатов в полную силу пакостят одному игроку. В большом секторе магнатов больше, но каждый пакостит реже:
        /// общее давление — как от стольких магнатов.
        /// </summary>
        public int RivalMagnates = 3;
        /// <summary>Казна магната на старте.</summary>
        public double MagnateStartMoney = 3000;
        /// <summary>Магнат не тратит казну ниже стольких ходов содержания своих планет.</summary>
        public double MagnateReserveTurns = 5;
        /// <summary>Шанс за ход, что магнат выучит жителей одной своей обитаемой планеты на следующий уровень, если хватает денег.</summary>
        public double MagnateEducateChance = 0.05;
        /// <summary>Когда игрок богаче среднего магната, магнаты пакостят чаще — пропорционально, но не больше, чем во столько раз.</summary>
        public double MagnateMaxAttention = 3;
        /// <summary>Шанс за ход, что магнат выскажется о ваших поступках с жителями.</summary>
        public double RemarkChance = 0.3;
        /// <summary>О поступках за сколько последних ходов магнаты вспоминают.</summary>
        public int RemarkMemoryTurns = 8;
        /// <summary>Как давно поступки считаются в общей репутации «добряк или живодёр».</summary>
        public int KindnessMemoryTurns = 40;
        /// <summary>Осторожная не подкупает жителей у игрока с добротой от этого уровня.</summary>
        public int CautiousSparesKindness = 5;
        /// <summary>Жадный реже пакостит «родственной душе»: игроку с добротой до этого уровня.</summary>
        public int GreedyLikesKindness = -5;
        /// <summary>Магнаты замечают игрока по мере роста: шанс пакости полный, когда у него столько планет.</summary>
        public int MagnateAttentionPlanets = 6;
        /// <summary>Ставки игрока на аукционе: доля базовой цены реестра.</summary>
        public double[] BidFactors = { 1.0, 1.1, 1.2, 1.3 };
        /// <summary>Подкупать магнаты пытаются жителей с лояльностью ниже этой.</summary>
        public double BribeTargetLoyalty = 50;
        /// <summary>Через сколько ходов после предупреждения жители перейдут к магнату.</summary>
        public int BribeWarningTurns = 2;
        /// <summary>Подкуп удаётся, если к сроку лояльность всё ещё ниже этой.</summary>
        public double BribeSuccessLoyalty = 50;
        /// <summary>Встречный подкуп: цена и прибавка лояльности. Действия не тратит.</summary>
        public double CounterBribeCost = 300;
        public double CounterBribeLoyalty = 25;
        /// <summary>Охрана: за ход; охраняемую планету подкупают, только если лояльность ниже этой.</summary>
        public double GuardsCost = 40;
        public double GuardedBribeLoyalty = 25;
        /// <summary>Рейдерский захват: срок, цена защиты (доля рыночной стоимости планеты) и компенсация, если планету отняли.</summary>
        public int ClaimWarningTurns = 3;
        public double ClaimDefenseShare = 0.15;
        public double ClaimCompensationShare = 0.5;
        /// <summary>
        /// Картель против лидера: когда игрок во столько раз богаче среднего магната, магнаты с таким шансом за ход
        /// сговариваются со скупщиками на столько ходов, и биржа платит ему меньше. Репутация от CartelReputationShield вдвое смягчает удар.
        /// </summary>
        public double CartelRivalry = 2;
        public double CartelChance = 0.06;
        public int CartelTurns = 10;
        public double CartelPriceCut = 0.3;
        public double CartelReputationShield = 70;
        /// <summary>Картель давит и на заказчиков умственного труда: он тоже приносит меньше.</summary>
        public bool CartelHitsIntellect = true;
        /// <summary>Слияние: магнат, который во столько раз богаче другого, с таким шансом за ход поглощает его.</summary>
        public double MergerWealthRatio = 4;
        public double MergerChance = 0.02;
        /// <summary>Учёных переманивают с планет, где лояльность ниже этой.</summary>
        public double PoachScientistsBelowLoyalty = 70;
        /// <summary>Слух магната: на сколько падает цена вашего главного ресурса.</summary>
        public double RumorPriceDrop = 0.25;
        /// <summary>Репутация игрока 0–100. Влияет на согласие жителей и число заказов.</summary>
        public double StartReputation = 50;
        /// <summary>За ход репутация возвращается к 50 на столько очков.</summary>
        public double ReputationRecovery = 0.5;
        public double ReputationPerOrder = 1;
        /// <summary>Ваш слух о магнате: цена, сколько ходов он не покупает и не пакостит, штраф за пакость.</summary>
        public double PlayerRumorCost = 300;
        public int PlayerRumorQuietTurns = 4;
        public double PlayerRumorReputation = -10;
        /// <summary>Подкуп жителей магната: доля базовой цены планеты, шанс успеха, урон репутации.</summary>
        public double PlayerBribeShare = 0.5;
        public double PlayerBribeChance = 0.5;
        public double PlayerBribeReputation = -15;
        public double PlayerBribeLoyalty = 40;
        /// <summary>Галактический реестр ловит грязные дела: шанс, штраф и дополнительный урон репутации.</summary>
        public double FineChance = 0.30;
        public double FineAmount = 800;
        public double FineReputation = -10;

        // --- Сезонные события и риски (этап 7, всё — допущения) ---
        public int FirstEventMinTurn = 6, FirstEventMaxTurn = 10;
        /// <summary>Затишье между событиями, ходов.</summary>
        public int EventGapMin = 5, EventGapMax = 10;
        public GalacticEvent[] Events =
        {
            new GalacticEvent("Ледниковая мода", "На столичных планетах в моде коктейли со льдом из хвоста кометы.", 5)
                { PriceFactor = GalacticEvent.Only((Resource.Ice, 1.6)), PlanetSaleFactor = GalacticEvent.Only((Resource.Ice, 1.5)) },
            new GalacticEvent("Газовая лихорадка", "Инвесторы уверены, что газ — это новый газ.", 5)
                { PriceFactor = GalacticEvent.Only((Resource.Gas, 1.3)), PlanetSaleFactor = GalacticEvent.Only((Resource.Gas, 1.6)) },
            new GalacticEvent("Биржевая паника", "Кто-то чихнул в зале торгов. Все продают всё.", 4)
                { PriceFactor = GalacticEvent.All(0.7) },
            new GalacticEvent("Метеоритный сезон", "Астрономы обещают красивое зрелище. Страховщики нервничают.", 4)
                { AccidentFactor = 2.5, MeteorFactor = 5 },
            new GalacticEvent("Налоговая амнистия", "Реестр празднует юбилей и прощает всем всё. Почти.", 4)
                { UpkeepFactor = 0.6 },
            new GalacticEvent("Лицензионный сбор", "Реестр решил, что крупным корпорациям живётся слишком весело.", 6)
                { UpkeepFactor = 1.6, UpkeepFactorFromPlanets = 10 },
            new GalacticEvent("Кристальная лихорадка", "Кристаллы признаны лучшим подарком к любому празднику, включая похороны.", 5)
                { PriceFactor = GalacticEvent.Only((Resource.Crystals, 1.5), (Resource.Quantium, 1.5)),
                  PlanetSaleFactor = GalacticEvent.Only((Resource.Crystals, 1.5), (Resource.Quantium, 1.5)) },
            new GalacticEvent("Профсоюзная весна", "Жители всех планет одновременно прочитали одну и ту же брошюру.", 5)
                { ExtraLoyaltyDrain = 2 },
            new GalacticEvent("Ярмарка заказов", "Вся галактика затеяла ремонт.", 5)
                { OrderChanceFactor = 2.5 },
            new GalacticEvent("Космический насморк", "Чихают все, даже роботы. Особенно роботы.", 4)
                { EpidemicFactor = 6 },
            new GalacticEvent("Биомассовый бум", "Диетологи галактики открыли, что биомасса полезна. Опять.", 5)
                { PriceFactor = GalacticEvent.Only((Resource.Biomass, 1.6)), PlanetSaleFactor = GalacticEvent.Only((Resource.Biomass, 1.4)) },
            new GalacticEvent("Металлический голод", "Все строят всё, и всем не хватает гвоздей.", 5)
                { PriceFactor = GalacticEvent.Only((Resource.Metal, 1.5)), PlanetSaleFactor = GalacticEvent.Only((Resource.Metal, 1.4)) },
        };
        /// <summary>Метеорит разбивает шахту: шанс за ход на планету с шахтой. Страховка платит за шахту.</summary>
        public double MeteorChance = 0.01;
        /// <summary>Эпидемия на обитаемой планете: шанс за ход, длительность, лояльность, доля добычи.</summary>
        public double EpidemicChance = 0.015;
        public int EpidemicTurns = 4;
        public double EpidemicLoyalty = -10;
        public double EpidemicOutputShare = 0.5;
        /// <summary>Бунт: бастующие жители с лояльностью ниже этой громят шахту. Страховка не платит.</summary>
        public double RiotBelowLoyalty = 15;
        public double RiotChance = 0.2;

        // --- Развитие планет (этап 7, всё — допущения) ---
        /// <summary>
        /// За сколько ходов месторождение восстанавливается после истощения, по ресурсам:
        /// биомасса отрастает быстро, квантий — очень долго. Разные сроки — чтобы всё не кончалось разом.
        /// </summary>
        public int[] RegenTurns = { 10, 24, 14, 6, 32, 45, 20, 28 };
        /// <summary>Шанс одного и двух дополнительных месторождений у планеты.</summary>
        public double OneExtraDepositChance = 0.5;
        public double TwoExtraDepositsChance = 0.2;
        /// <summary>Добыча доп. месторождения — доля от обычной добычи этого ресурса.</summary>
        public double ExtraOutputMin = 0.2, ExtraOutputMax = 0.35;
        /// <summary>Запас доп. месторождения в ходах его добычи.</summary>
        public double ExtraCapacityTurnsMin = 8, ExtraCapacityTurnsMax = 14;
        /// <summary>Экстрактор для доп. месторождения: доля стоимости обычной шахты этого ресурса.</summary>
        public double ExtractorCostShare = 0.4;
        /// <summary>После каждого восстановления месторождение наполняется до этой доли прежней ёмкости: медленно стареет.</summary>
        public double RegenCapacityShare = 0.9;
        /// <summary>Но не ниже этой доли начальной ёмкости: старое месторождение беднеет, но не исчезает.</summary>
        public double RegenCapacityFloor = 0.3;

        // --- Образование жителей и умственный труд (этап 7, всё — допущения) ---
        /// <summary>Цена и срок обучения до уровня i (0 — без образования). Для первого уровня нужна школа.</summary>
        public double[] EducationCost = { 0, 800, 1500, 2500 };
        public int[] EducationTurns = { 0, 4, 5, 6 };
        /// <summary>Доход за ход от умственного труда по уровням при лояльности 50. Даже на высшем уровне меньше, чем даёт шахта.</summary>
        public double[] IntellectIncome = { 0, 60, 110, 160 };
        /// <summary>Довольные жители работают лучше: доход × (1 + (лояльность − 50) × это).</summary>
        public double IntellectPerLoyalty = 0.005;
        /// <summary>Жители, склонные к своей специальности (по типу планеты), зарабатывают больше.</summary>
        public double NaturalSpecialtyBonus = 0.25;
        /// <summary>Пока шахта работает, умственным трудом занята только часть жителей.</summary>
        public double IntellectWhileMining = 0.5;
        /// <summary>Учиться жители соглашаются при лояльности от этой.</summary>
        public double EducationMinLoyalty = 50;
        /// <summary>Колонисты на необитаемой планете: цена и лояльность на старте.</summary>
        public double ColonyCost = 800;
        public double ColonyLoyalty = 60;
        /// <summary>
        /// Бонус корпорации за каждый уровень образования планет данной специальности и его предел,
        /// по порядку enum Specialty: —, технари (аварии реже), химики (восстановление быстрее),
        /// физики (исследования дешевле), биологи (лояльность за ход), экономисты (глубина рынка).
        /// </summary>
        public double[] SpecialtyBonusPerLevel = { 0, 0.08, 0.06, 0.08, 0.3, 0.05 };
        public double[] SpecialtyBonusMax = { 0, 0.5, 0.4, 0.5, 2.0, 0.4 };
        public Technology[] Technologies =
        {
            new Technology("ice", "Ледоколы", 800, 3, "Добыча льда на дополнительных месторождениях.") { UnlocksResource = Resource.Ice },
            new Technology("metal", "Буровые", 1000, 3, "Добыча металла на дополнительных месторождениях.") { UnlocksResource = Resource.Metal },
            new Technology("gas", "Газовые ловушки", 1000, 3, "Добыча газа на дополнительных месторождениях.") { UnlocksResource = Resource.Gas },
            new Technology("social", "Социология", 1500, 4, "Жители всех ваших планет +1 лояльности за ход.") { LoyaltyBonus = 1 },
            new Technology("biomass", "Оранжереи", 1500, 4, "Добыча биомассы на дополнительных месторождениях.") { UnlocksResource = Resource.Biomass, Requires = new[] { "ice" } },
            new Technology("enrich", "Обогащение руды", 2500, 5, "+15% добычи на всех ваших планетах.") { OutputBonus = 0.15, Requires = new[] { "metal" } },
            new Technology("regen", "Ускоренная регенерация", 2500, 5, "Месторождения на ваших планетах восстанавливаются на 30% быстрее.") { RegenFactor = 0.7, Requires = new[] { "biomass" } },
            new Technology("crystals", "Кристаллические резонаторы", 3500, 6, "Добыча кристаллов на дополнительных месторождениях.") { UnlocksResource = Resource.Crystals, Requires = new[] { "enrich" } },
            new Technology("quantium", "Квантовые ловушки", 5000, 7, "Добыча квантия на дополнительных месторождениях.") { UnlocksResource = Resource.Quantium, Requires = new[] { "crystals", "gas" } },
            new Technology("plasma", "Плазменные ловушки", 3000, 5, "Добыча плазмы на дополнительных месторождениях. Нужна лицензия на «Огненный пояс».") { UnlocksResource = Resource.Plasma, Requires = new[] { "gas" }, Sector = 1 },
            new Technology("deuterium", "Дейтериевые фильтры", 4000, 6, "Добыча дейтерия на дополнительных месторождениях. Нужна лицензия на «Лазурную бездну».") { UnlocksResource = Resource.Deuterium, Requires = new[] { "ice" }, Sector = 2 },
        };

        // --- Карта и доставка (допущения) ---
        /// <summary>Где на карте галактическая биржа.</summary>
        public double ExchangeX = 0.5, ExchangeY = 0.5;
        /// <summary>Скорость грузовиков: расстояние за ход.</summary>
        public double ShipSpeed = 0.40;
        /// <summary>Стоимость доставки: доля базовой цены груза за единицу расстояния (вся карта — 1).</summary>
        public double ShippingCostPerDistance = 0.08;

        /// <summary>Средний запас — втрое больше, чем в таблице: планеты живут 60–100 ходов, а не 20–34.</summary>
        public PlanetType[] PlanetTypes =
        {
            new PlanetType("Ледяной карлик", Resource.Ice, 1600, 40, 600, 28, 2520, 6),
            new PlanetType("Скалистая", Resource.Metal, 1800, 45, 700, 23, 1932, 6),
            new PlanetType("Газовый гигант", Resource.Gas, 2400, 60, 900, 33, 3366, 5),
            new PlanetType("Джунгли", Resource.Biomass, 2200, 55, 800, 15, 1170, 5),
            new PlanetType("Кристальная", Resource.Crystals, 2600, 60, 1000, 13, 936, 4),
            new PlanetType("Аномалия", Resource.Quantium, 4200, 90, 1600, 8, 480, 2),
            // Новые ресурсы (этап 8): только в своих секторах; там же их цены и запасы умножаются на богатство сектора.
            new PlanetType("Вулканическая", Resource.Plasma, 3000, 65, 1100, 11, 880, 6) { OnlySector = 1 },
            new PlanetType("Океан", Resource.Deuterium, 3600, 75, 1300, 9, 650, 6) { OnlySector = 2 },
        };

        /// <summary>Базовые цены ресурсов, в порядке enum Resource.</summary>
        public double[] BaseResourcePrice = { 10, 14, 12, 28, 40, 110, 60, 85 };

        public double BasePrice(Resource r) => BaseResourcePrice[(int)r];
        public double Depth(Resource r) => MarketDepth[(int)r];

        // ------------------------------------------------------------------
        // Шоу «Риелтор года» — цель партии (решение автора). Сезон с заданиями на очки против магнатов.
        // ------------------------------------------------------------------

        /// <summary>Длина сезона в ходах; 0 — шоу выключено.</summary>
        public int SeasonTurns = 40;
        public int SeasonTasks = 3;
        /// <summary>Сколько магнатов соревнуется с игроками: самые богатые в секторах с лицензией.</summary>
        public int ShowRivals = 3;
        /// <summary>
        /// Шанс магната за ход выполнить задание, которое честно не посчитать (колонисты, учёба, разведка…).
        /// Рост капитала и лоты у магнатов считаются по-настоящему.
        /// </summary>
        public double MagnateTaskChance = 0.017;
        /// <summary>Призы за 1-е, 2-е и 3-е место (× масштаб уровня корпорации) и прибавка к репутации.</summary>
        public double[] ShowPrize = { 1200, 600, 300 };
        public double[] ShowPrizeReputation = { 5, 2, 0 };
        /// <summary>Масштаб денег в призах и случаях по уровню корпорации: Стартап, Контора, Компания, Корпорация, Концерн.</summary>
        public double[] ShowLevelScale = { 1, 1.6, 2.5, 4, 6 };

        public ShowTaskSpec[] ShowTasks =
        {
            new ShowTaskSpec { Kind = ShowTaskKind.BuyPlanets, Title = "Скупщик", Text = "Купить {0}", Noun = new[] { "планету", "планеты", "планет" }, Target = 2, TargetPerLevel = 0.5, Points = 15 },
            new ShowTaskSpec { Kind = ShowTaskKind.FlipPlanet, Title = "Флиппер", Text = "Продать {0}, которая принесла больше, чем в неё вложено", Noun = new[] { "планету", "планеты", "планет" }, Target = 1, Points = 20 },
            new ShowTaskSpec { Kind = ShowTaskKind.GrowCapital, Title = "Рост", Text = "Вырастить капитал на {0}% за сезон", Target = 40, TargetPerLevel = -5, Points = 20 },
            new ShowTaskSpec { Kind = ShowTaskKind.WinLot, Title = "Лот недели", Text = "Выиграть «Лот недели»", Target = 1, Points = 25 },
            new ShowTaskSpec { Kind = ShowTaskKind.SettleColony, Title = "Новосёлы", Text = "Заселить колонистов: {0}", Noun = new[] { "планета", "планеты", "планет" }, Target = 1, Points = 15 },
            new ShowTaskSpec { Kind = ShowTaskKind.Educate, Title = "Учитель года", Text = "Выучить жителей: {0} образования", Noun = new[] { "уровень", "уровня", "уровней" }, Target = 1, TargetPerLevel = 0.5, Points = 20 },
            new ShowTaskSpec { Kind = ShowTaskKind.FindHidden, Title = "Следопыт", Text = "Найти зондом {0}", Noun = new[] { "скрытую планету", "скрытые планеты", "скрытых планет" }, Target = 1, Points = 15 },
            new ShowTaskSpec { Kind = ShowTaskKind.ExplorePlanets, Title = "Разведчик", Text = "Полностью разведать {0}", Noun = new[] { "планету", "планеты", "планет" }, Target = 3, TargetPerLevel = 1, Points = 10 },
            new ShowTaskSpec { Kind = ShowTaskKind.FulfillOrders, Title = "Поставщик", Text = "Выполнить {0}", Noun = new[] { "заказ", "заказа", "заказов" }, Target = 2, TargetPerLevel = 1, Points = 15 },
            new ShowTaskSpec { Kind = ShowTaskKind.Research, Title = "Прорыв", Text = "Завершить {0}", Noun = new[] { "исследование", "исследования", "исследований" }, Target = 1, Points = 10 },
            new ShowTaskSpec { Kind = ShowTaskKind.Festivals, Title = "Душа компании", Text = "Устроить {0}", Noun = new[] { "праздник", "праздника", "праздников" }, Target = 2, Points = 10 },
            new ShowTaskSpec { Kind = ShowTaskKind.SellOnExchange, Title = "Биржевик", Text = "Продать на бирже ресурсов на {0}", Target = 4000, TargetPerLevel = 4000, Points = 10 },
        };

        /// <summary>Реплики ведущего, Брэда Квазара.</summary>
        public string[] ShowHostStart =
        {
            "«Новый сезон, новые планеты, старые долги!»",
            "«Мы снова в эфире! Приготовьте кошельки и нервы!»",
            "«Дамы, господа и прочие формы жизни — поехали!»",
        };
        public string[] ShowHostWin =
        {
            "«Невероятно! Кубок уезжает к тому, кто умеет считать не только чужие деньги!»",
            "«Магнаты в слезах, зрители в восторге, рейтинги в открытом космосе!»",
            "«Встречайте риелтора сезона! Аплодисменты — щупальцами, кто чем может!»",
        };
        public string[] ShowHostLose =
        {
            "«Не расстраивайтесь: это первое место среди проигравших!»",
            "«Зрители вас любят. Особенно когда вы проигрываете!»",
            "«В следующем сезоне всё будет иначе. Наверное.»",
        };
        public string[] ShowHostLot =
        {
            "«Продано! Кто-то сегодня спит на мешке с кредитами — и это не магнаты!»",
            "«Сделка недели! Мои поздравления и соболезнования вашему бухгалтеру!»",
        };

        // ------------------------------------------------------------------
        // Случаи: раз в несколько ходов — гость, сделка или беда с вариантами ответа.
        // Деньги в случаях умножаются на ShowLevelScale. Последний вариант выбирается за игрока, если тот промолчал.
        // ------------------------------------------------------------------

        public int IncidentFirstTurn = 3;
        /// <summary>Сколько ходов между случаями; IncidentGapMax = 0 — случаев нет.</summary>
        public int IncidentGapMin = 2, IncidentGapMax = 4;

        public Incident[] Incidents =
        {
            new Incident
            {
                Id = "tax", Title = "Налоговая проверка", Icon = "clipboard", Target = IncidentTarget.None,
                Text = "Инспектор Галактической налоговой нашёл в ваших бумагах «творческий подход к арифметике».",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Сунуть конверт", Cost = 120, Chance = 0.7,
                        Success = new IncidentOutcome { Reputation = -2, Text = "Инспектор внезапно нашёл ошибку в собственных расчётах." },
                        Failure = new IncidentOutcome { Money = -500, Reputation = -6, Text = "Инспектор оказался честным. Штраф вырос, а по бирже пошли слухи." } },
                    new IncidentChoice { Label = "Спорить в суде", Chance = 0.5,
                        Success = new IncidentOutcome { Text = "Суд признал ваш «творческий подход» искусством." },
                        Failure = new IncidentOutcome { Money = -450, Text = "Суд не оценил искусство. Штраф с процентами." } },
                    new IncidentChoice { Label = "Заплатить штраф", Cost = 300,
                        Success = new IncidentOutcome { Text = "Инспектор пересчитал деньги дважды и ушёл довольный." } },
                },
            },
            new Incident
            {
                Id = "refugees", Title = "Беженцы", Icon = "people", Target = IncidentTarget.MyInhabited,
                Text = "К {planet} причалил ковчег беженцев с Кометы-7. Просят приютить хотя бы до зимы (зима на комете — вечно).",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Приютить", Cost = 200,
                        Success = new IncidentOutcome { Loyalty = 12, Reputation = 3, Text = "Беженцы оказались отличными соседями и научили всех печь пироги из метеоритов." } },
                    new IncidentChoice { Label = "Дать денег на дорогу", Cost = 80,
                        Success = new IncidentOutcome { Reputation = 1, Text = "Беженцы улетели к соседям. Соседи вам это припомнят." } },
                    new IncidentChoice { Label = "Отказать",
                        Success = new IncidentOutcome { Reputation = -3, Loyalty = -5, Text = "Ковчег развернулся. Ваши жители долго смотрели ему вслед." } },
                },
            },
            new Incident
            {
                Id = "filming", Title = "Съёмки шоу", Icon = "star", Target = IncidentTarget.MyPlanet,
                Text = "Продюсеры «Риелтора года» хотят снять на {planet} выпуск «Один день из жизни риелтора».",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Снять с размахом", Cost = 300,
                        Success = new IncidentOutcome { ShowPoints = 10, Reputation = 2, Text = "Брэд Квазар прошёлся по вашей шахте в белом костюме. Рейтинги взлетели: +10 очков в шоу." } },
                    new IncidentChoice { Label = "Согласиться",
                        Success = new IncidentOutcome { Money = 150, ShowPoints = 4, Loyalty = -4, Text = "Съёмочная группа вытоптала газон, но выпуск вышел бодрым: +4 очка в шоу." } },
                    new IncidentChoice { Label = "Отказаться",
                        Success = new IncidentOutcome { Text = "Продюсеры пожали плечами и поехали снимать магнатов." } },
                },
            },
            new Incident
            {
                Id = "inventor", Title = "Безумный изобретатель", Icon = "gear", Target = IncidentTarget.MyMine,
                Text = "Профессор Шестерёнкин предлагает «квантовую кирку» для шахты на {planet}. Гарантия — честное слово профессора.",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Купить кирку", Cost = 400, Chance = 0.6,
                        Success = new IncidentOutcome { ReserveShare = 0.25, Text = "Кирка прошла сквозь скалу и нашла новую жилу: запас +25%." },
                        Failure = new IncidentOutcome { AccidentTurns = 2, Text = "Кирка оказалась квантовой в худшем смысле: шахта на 2 хода встала." } },
                    new IncidentChoice { Label = "Вежливо отказать",
                        Success = new IncidentOutcome { Text = "Профессор ушёл предлагать кирку магнатам. Пусть у них что-нибудь взорвётся." } },
                },
            },
            new Incident
            {
                Id = "wedding", Title = "Свадьба вождя", Icon = "heart", Target = IncidentTarget.MyInhabited,
                Text = "Жители {planet} женят вождя. По местной традиции банкет оплачивает самый богатый гость. Угадайте кто.",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Оплатить банкет", Cost = 250,
                        Success = new IncidentOutcome { Loyalty = 12, Text = "Банкет удался: вас трижды подбросили к потолку купола." } },
                    new IncidentChoice { Label = "Прийти и сказать речь", Chance = 0.6,
                        Success = new IncidentOutcome { Loyalty = 6, Text = "Речь тронула всех — особенно место про снижение налогов." },
                        Failure = new IncidentOutcome { Loyalty = -6, Text = "Речь про эффективность добычи гостям не зашла." } },
                    new IncidentChoice { Label = "Прислать открытку",
                        Success = new IncidentOutcome { Loyalty = -4, Text = "Открытку повесили на видное место. Рядом — список обид." } },
                },
            },
            new Incident
            {
                Id = "offer", Title = "Предложение магната", Icon = "tag", Target = IncidentTarget.MyPlanetAndMagnate, MinTurn = 10,
                Text = "{magnate} хочет купить {planet} за {price} — заметно дороже рынка. «Не продашь сейчас — пожалеешь потом», — добавляет с улыбкой.",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Продать",
                        Success = new IncidentOutcome { SalePremium = 1.35, Text = "Сделка закрыта. {magnate} уже рассказывает в эфире, как обвёл вас вокруг пальца." } },
                    new IncidentChoice { Label = "Отказать",
                        Success = new IncidentOutcome { Text = "{magnate} пожимает плечами. Кажется, это запомнят." } },
                },
            },
            new Incident
            {
                Id = "comet", Title = "Ледяная комета", Icon = "ice", Target = IncidentTarget.None,
                Text = "Мимо проносится ледяная комета. Если поймать её сетью, весь лёд ваш.",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Поймать сетью", Cost = 150, Chance = 0.75,
                        Success = new IncidentOutcome { Stock = 60, Resource = PlanetTrader.Core.Resource.Ice, Text = "Комета в сети! На склад биржи упало 60 льда." },
                        Failure = new IncidentOutcome { Text = "Комета порвала сеть и улетела, помахав хвостом." } },
                    new IncidentChoice { Label = "Пусть летит",
                        Success = new IncidentOutcome { Text = "Комета улетела. Кто-то на соседней планете загадал желание." } },
                },
            },
            new Incident
            {
                Id = "museum", Title = "Музей риелторов", Icon = "flag", Target = IncidentTarget.None,
                Text = "Галактический музей готовит выставку «Риелторы прошлого и настоящего» и просит пожертвование.",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Пожертвовать", Cost = 300,
                        Success = new IncidentOutcome { Reputation = 5, ShowPoints = 3, Text = "Ваш портрет повесили между основателями галактики. Немного криво." } },
                    new IncidentChoice { Label = "Отдать старый офисный стул",
                        Success = new IncidentOutcome { Reputation = 1, Text = "Стул выставили с табличкой «Трон молодого риелтора». Ходят смотреть." } },
                    new IncidentChoice { Label = "Отказать",
                        Success = new IncidentOutcome { Reputation = -1, Text = "Музей обиделся и повесил на ваше место портрет магната." } },
                },
            },
            new Incident
            {
                Id = "tabloid", Title = "Жёлтая пресса", Icon = "journal", Target = IncidentTarget.None,
                Text = "Таблоид «Галактический сплетник» пишет, что вы держите жителей на диете из вакуума.",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Нанять пиарщика", Cost = 300,
                        Success = new IncidentOutcome { Reputation = 4, Text = "Пиарщик развернул историю: теперь вы держите жителей на здоровой диете." } },
                    new IncidentChoice { Label = "Подать в суд", Chance = 0.5,
                        Success = new IncidentOutcome { Money = 400, Text = "Таблоид извинился и заплатил. Извинение напечатали мелко, деньги — крупно." },
                        Failure = new IncidentOutcome { Money = -300, Reputation = -2, Text = "Суд решил, что вакуум — это всё-таки диета." } },
                    new IncidentChoice { Label = "Не обращать внимания",
                        Success = new IncidentOutcome { Reputation = -3, Text = "Слух прожил неделю и умер своей смертью. Осадок остался." } },
                },
            },
            new Incident
            {
                Id = "truck", Title = "Сломанный грузовик", Icon = "rocket", Target = IncidentTarget.MyMine,
                Text = "Грузовик с {planet} заглох посреди пустоты. Пилот просит денег на запчасти, иначе бросит груз и улетит на попутке.",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Оплатить ремонт", Cost = 120,
                        Success = new IncidentOutcome { Text = "Грузовик снова в пути. Пилот прислал селфи на фоне туманности." } },
                    new IncidentChoice { Label = "Пусть бросает груз",
                        Success = new IncidentOutcome { Stock = -25, Text = "25 единиц ({resource}) дрейфуют где-то между звёзд." } },
                },
            },
            new Incident
            {
                Id = "warehouse", Title = "Забытый склад", Icon = "cube", Target = IncidentTarget.None,
                Text = "В подвале биржи нашли склад с вашим именем на двери. Хранение не оплачено с прошлого года.",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Оплатить и забрать", Cost = 150,
                        Success = new IncidentOutcome { Stock = 40, Resource = PlanetTrader.Core.Resource.Metal, Text = "Внутри 40 металла и чей-то бутерброд. Бутерброд оставили." } },
                    new IncidentChoice { Label = "Отказаться",
                        Success = new IncidentOutcome { Text = "Склад продали с молотка. Бутерброд ушёл за 3 кредита." } },
                },
            },
            new Incident
            {
                Id = "preacher", Title = "Проповедник", Icon = "warning", Target = IncidentTarget.MyInhabited,
                Text = "На {planet} пришёл бродячий проповедник и учит жителей, что работа — пережиток прошлого.",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Нанять его учителем", Cost = 200,
                        Success = new IncidentOutcome { Loyalty = 4, Text = "Проповедник ведёт в школе философию. Жители спорят, но работают." } },
                    new IncidentChoice { Label = "Выслать с планеты",
                        Success = new IncidentOutcome { Loyalty = -5, Text = "Проповедника выслали. Его последователи обиделись." } },
                    new IncidentChoice { Label = "Пусть говорит", Chance = 0.5,
                        Success = new IncidentOutcome { Text = "Проповедника никто не слушал: все были на смене." },
                        Failure = new IncidentOutcome { Loyalty = -12, Text = "Проповедь нашла отклик. На площади уже рисуют плакаты." } },
                },
            },
            new Incident
            {
                Id = "spy", Title = "Шпион магната", Icon = "eye", Target = IncidentTarget.Magnate, MinTurn = 8,
                Text = "Охрана поймала шпиона, которого подослал(а) {magnate}. Шпион готов перейти на вашу сторону — за скромное вознаграждение.",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Перевербовать", Cost = 250,
                        Success = new IncidentOutcome { QuietTurns = 8, Text = "Шпион кормит хозяина ложными сводками: 8 ходов {magnate} занят(а) ерундой и вам не пакостит." } },
                    new IncidentChoice { Label = "Сдать в реестр",
                        Success = new IncidentOutcome { Reputation = 3, Text = "Реестр поблагодарил за бдительность. {magnate} делает вид, что впервые видит этого человека." } },
                    new IncidentChoice { Label = "Отпустить",
                        Success = new IncidentOutcome { Text = "Шпион ушёл, прихватив ваш степлер." } },
                },
            },
            new Incident
            {
                Id = "tourists", Title = "Туристы", Icon = "planet", Target = IncidentTarget.MyPlanet,
                Text = "У {planet} застрял автобус туристов. Гид предлагает устроить им экскурсию — за деньги, конечно.",
                Choices = new[]
                {
                    new IncidentChoice { Label = "Экскурсия с банкетом", Cost = 100, Chance = 0.7,
                        Success = new IncidentOutcome { Money = 400, ShowPoints = 2, Text = "Туристы в восторге и оставили щедрые чаевые." },
                        Failure = new IncidentOutcome { Text = "Половина туристов уснула. Чаевые — пуговица и билет в один конец." } },
                    new IncidentChoice { Label = "Простая экскурсия",
                        Success = new IncidentOutcome { Money = 120, Loyalty = -2, Text = "Туристы скупили сувениры, включая образцы породы. Жители недовольны толпой." } },
                    new IncidentChoice { Label = "Не пускать",
                        Success = new IncidentOutcome { Text = "Туристы уехали фотографировать чужие планеты." } },
                },
            },
        };

        // ------------------------------------------------------------------
        // Лот недели: легендарная планета на закрытых торгах против магнатов сектора.
        // ------------------------------------------------------------------

        public int LotFirstTurn = 12;
        /// <summary>Сколько ходов между торгами; LotGapMax = 0 — лотов нет.</summary>
        public int LotGapMin = 12, LotGapMax = 18;
        /// <summary>За сколько ходов до торгов объявляют лот.</summary>
        public int LotNoticeTurns = 4;
        /// <summary>Ставки игрока: доли стартовой цены.</summary>
        public double[] LotBidFactors = { 1.0, 1.25, 1.5, 2.0 };
        /// <summary>Стартовая цена — от рыночной стоимости с руинами.</summary>
        public double LotPriceFactor = 0.9;
        /// <summary>Лот не дороже такой доли капитала самого богатого игрока (иначе запас планеты урезается, но не ниже обычного).</summary>
        public double LotBudgetShare = 0.8;
        /// <summary>Интерес магната к лоту — его интерес к аукционам, умноженный на это.</summary>
        public double LotInterestFactor = 1.6;
        /// <summary>Ставка магната — случайная доля стартовой цены, не больше свободных денег.</summary>
        public double LotRivalBidMin = 1.0, LotRivalBidMax = 1.6;
        /// <summary>Сколько магнатов самое большее приходит на торги (в большом секторе их десятки).</summary>
        public int LotMaxRivals = 3;
        /// <summary>Наследник на торгах не уступает: ставит больше.</summary>
        public double HeirLotMarkup = 1.15;
        /// <summary>Репутация, если на ставку не хватило денег.</summary>
        public double LotVoidReputation = -5;

        public LegendaryPlanet[] Legends =
        {
            new LegendaryPlanet { Id = "eden", Name = "Алмазный Эдем", Resource = PlanetTrader.Core.Resource.Crystals, ReserveFactor = 2.5,
                Description = "Кристаллы здесь растут на деревьях. Местные птицы вьют из них гнёзда." },
            new LegendaryPlanet { Id = "sanatorium", Name = "Санаторий Туманность", Inhabited = true, Loyalty = 85, Education = 2, Specialty = Specialty.Physicists, HasSchool = true, ReserveFactor = 1.2,
                Description = "Жители — академики на пенсии. Любят тишину, кроссворды и платить налоги вовремя." },
            new LegendaryPlanet { Id = "capital", Name = "Старая столица", Ruins = 4000, ReserveFactor = 1.5,
                Description = "Бывшая столица древней империи. Под каждым камнем — артефакт, под каждым артефактом — проклятие (по слухам)." },
            new LegendaryPlanet { Id = "chest", Name = "Сундук пирата", ExtraDeposits = 2, ReserveFactor = 1.6,
                Description = "Пираты прятали здесь добычу. Добычу растащили, а месторождения остались." },
            new LegendaryPlanet { Id = "forge", Name = "Кузница богов", Resource = PlanetTrader.Core.Resource.Metal, ReserveFactor = 2.2, MineBuilt = true,
                Description = "Тут уже стоит мегашахта. Кто её построил — неизвестно. Работает без выходных." },
            new LegendaryPlanet { Id = "resort", Name = "Курорт Аква-Лакс", Resource = PlanetTrader.Core.Resource.Biomass, Inhabited = true, Loyalty = 75, ReserveFactor = 1.8,
                Description = "Пляжи, коктейли и плантации биомассы. Жители загорелые и довольные." },
            new LegendaryPlanet { Id = "volcano", Name = "Вечный вулкан", OnlySector = 1, Resource = PlanetTrader.Core.Resource.Plasma, ReserveFactor = 2.5,
                Description = "Вулкан извергается строго по расписанию — удобно для добычи плазмы." },
            new LegendaryPlanet { Id = "abyss", Name = "Дно бездны", OnlySector = 2, Resource = PlanetTrader.Core.Resource.Deuterium, ReserveFactor = 2.5, ExtraDeposits = 1,
                Description = "Самый глубокий океан галактики. На дне что-то светится и, кажется, машет." },
        };

        /// <summary>
        /// Множитель содержания всех планет при числе планет L. Растёт до L1 (×1,5),
        /// держится до L2, потом растёт снова.
        /// </summary>
        public double UpkeepMultiplier(double planetCount)
        {
            if (planetCount <= 1) return UpkeepCurveA;
            double growing = Math.Min(planetCount, UpkeepShelfStart) - 1
                             + Math.Max(0, planetCount - UpkeepShelfEnd);
            return UpkeepCurveA * Math.Pow(UpkeepCurveK, growing / UpkeepCurveP);
        }

        public PlanetType TypeOf(Resource r)
        {
            foreach (var t in PlanetTypes)
                if (t.Resource == r) return t;
            throw new ArgumentException("No planet type for " + r);
        }
    }
}
