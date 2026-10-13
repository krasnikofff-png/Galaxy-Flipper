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

    /// <summary>
    /// Все числа экономики. Исходные значения взяты из таблицы
    /// «Planet Trader — баланс экономики прототипа» (вкладки «Параметры», «Типы планет»,
    /// «Кривая с полкой») и подогнаны симулятором под «План проверки» — см. docs/BALANCE.md.
    /// Параметры, которых в таблице не было, помечены «допущение».
    /// </summary>
    public sealed class Balance
    {
        // --- Старт ---
        public double StartMoney = 2000;
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
        public int MaxManagers = 3;
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
        public double[] MarketDepth = { 150, 120, 140, 50, 35, 10 };
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
        /// <summary>Зонды: дороже — дальше, быстрее и шире скан. «Стандарт» соответствует старому зонду из таблицы (400, 60%).</summary>
        public ProbeType[] ProbeTypes =
        {
            new ProbeType("Москит", cost: 150, range: 0.35, speed: 0.35, scanRadius: 0.12, findChance: 0.30),
            new ProbeType("Стандарт", cost: 400, range: 0.70, speed: 0.50, scanRadius: 0.20, findChance: 0.60),
            new ProbeType("Дальнобой", cost: 900, range: 1.50, speed: 0.90, scanRadius: 0.30, findChance: 0.80),
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
        public int[] RegenTurns = { 10, 24, 14, 6, 32, 45 };
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
        };

        /// <summary>Базовые цены ресурсов, в порядке enum Resource.</summary>
        public double[] BaseResourcePrice = { 10, 14, 12, 28, 40, 110 };

        public double BasePrice(Resource r) => BaseResourcePrice[(int)r];
        public double Depth(Resource r) => MarketDepth[(int)r];

        /// <summary>
        /// Множитель содержания всех планет при числе планет L. Растёт до L1 (×1,5),
        /// держится до L2, потом растёт снова.
        /// </summary>
        public double UpkeepMultiplier(int planetCount)
        {
            if (planetCount <= 1) return UpkeepCurveA;
            int growing = Math.Min(planetCount, UpkeepShelfStart) - 1
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
