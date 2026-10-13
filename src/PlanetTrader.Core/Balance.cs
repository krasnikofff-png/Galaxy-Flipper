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
        public double StartMoney = 1300;
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
        public double ManagerSalary = 60;
        public int MaxManagers = 3;

        // --- Содержание: кривая с полкой ---
        public double UpkeepCurveA = 1.0;
        public double UpkeepCurveK = 1.336;
        public double UpkeepCurveP = 5;
        public int UpkeepShelfStart = 8;
        public int UpkeepShelfEnd = 13;
        /// <summary>Содержание выработанной планеты как доля обычного.</summary>
        public double DepletedUpkeepShare = 0.7;
        /// <summary>Считаются ли выработанные планеты в множителе содержания.</summary>
        public bool DepletedCountsInUpkeepCurve = false;

        // --- Сделки с планетами ---
        public double ResaleCommission = 0.10;
        public double AuctionMarkup = 0.25;
        /// <summary>Ставка дисконтирования при оценке планеты.</summary>
        public double DiscountRate = 0.10;
        /// <summary>Запас планеты случайный ±20% от среднего.</summary>
        public double ReserveSpread = 0.20;
        public int MineBuildTurns = 3;

        // --- Биржа ---
        /// <summary>Цена падает на SalesImpact × (продано / глубина рынка).</summary>
        public double SalesImpact = 0.10;
        public double PriceRecovery = 0.15;
        public double MarketNoise = 0.08;
        /// <summary>Нижний предел цены как доля базовой (допущение).</summary>
        public double MinPriceShare = 0.20;
        public double[] MarketDepth = { 150, 120, 140, 50, 35, 10 };

        // --- Заказы ---
        public double OrderMarkup = 1.5;
        public double OrderChancePerTurn = 0.5;
        /// <summary>Объём заказа в ходах добычи одной планеты (допущение).</summary>
        public int OrderMinTurnsOfOutput = 3;
        public int OrderMaxTurnsOfOutput = 7;
        /// <summary>Срок на выполнение заказа (допущение, пример из дизайн-документа).</summary>
        public int OrderDeadlineTurns = 5;

        // --- Риски ---
        public double AccidentChance = 0.06;
        public int AccidentTurns = 2;
        /// <summary>Страховка за ход: доля стоимости застрахованных планет.</summary>
        public double InsuranceRate = 0.02;

        // --- ИИ и сектор ---
        public int SectorPlanets = 30;
        public double AiStartShare = 0.33;
        public double AiBuyChance = 0.35;
        /// <summary>Шанс, что ИИ выставит одну из своих планет на продажу (допущение).</summary>
        public double AiResaleChance = 0.10;

        // --- Зонды ---
        public double ProbeCost = 400;
        public double ProbeFindChance = 0.60;

        public PlanetType[] PlanetTypes =
        {
            new PlanetType("Ледяной карлик", Resource.Ice, 1600, 40, 600, 28, 840, 6),
            new PlanetType("Скалистая", Resource.Metal, 1800, 45, 700, 23, 644, 6),
            new PlanetType("Газовый гигант", Resource.Gas, 2400, 60, 900, 33, 1122, 5),
            new PlanetType("Джунгли", Resource.Biomass, 2200, 55, 800, 15, 390, 5),
            new PlanetType("Кристальная", Resource.Crystals, 2600, 60, 1000, 13, 312, 4),
            new PlanetType("Аномалия", Resource.Quantium, 4200, 90, 1600, 8, 160, 2),
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
