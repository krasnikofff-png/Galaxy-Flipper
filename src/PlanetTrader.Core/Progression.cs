namespace PlanetTrader.Core
{
    /// <summary>
    /// Уровень корпорации (этап 8). Растёт вместе с капиталом и никогда не падает: прогресс не сбрасывается.
    /// Каждый уровень что-то открывает: больше менеджеров, лучшие зонды, дальше — лицензии на новые секторы и постройки.
    /// </summary>
    public sealed class CorporationLevel
    {
        public string Name;
        /// <summary>С какого капитала открывается.</summary>
        public double Capital;
        /// <summary>Сколько менеджеров можно держать, не считая директора.</summary>
        public int Managers;

        public CorporationLevel(string name, double capital, int managers)
        {
            Name = name;
            Capital = capital;
            Managers = managers;
        }
    }

    /// <summary>
    /// Стартовый персонаж (этап 8): директор со своими навыками, сильной стороной и слабостью.
    /// Сумма навыков у всех одинаковая (12, как у среднего директора), отличаются они тем, где сильны.
    /// </summary>
    public sealed class StartCharacter
    {
        public string Name;
        /// <summary>Чем хорош и чем плох — для экрана выбора.</summary>
        public string Plus, Minus;
        /// <summary>Навыки директора 1–5 по порядку enum Skill.</summary>
        public int[] Skills;
        /// <summary>Прибавка к стартовым деньгам (бывает и меньше обычного).</summary>
        public double ExtraMoney;
        public double Reputation = 50;
        /// <summary>На стартовой планете — дополнительное месторождение.</summary>
        public bool ExtraDeposit;
        /// <summary>Стартовая планета обитаема.</summary>
        public bool Inhabited;

        public StartCharacter(string name, int[] skills)
        {
            Name = name;
            Skills = skills;
        }
    }

    /// <summary>
    /// Сектор галактики (этап 8). У каждого своя карта, своя биржа-станция в центре и свои магнаты; цены на ресурсы общие.
    /// Стартовый сектор открыт всем, остальные — по лицензии: её продают с нужного уровня корпорации.
    /// Сектор появляется, когда первую лицензию на него покупает хоть кто-то из игроков.
    /// </summary>
    public sealed class SectorSpec
    {
        public string Name;
        /// <summary>Чем сектор хорош — для интерфейса.</summary>
        public string Description;
        /// <summary>С какого уровня корпорации продают лицензию (номер в CorporationLevels).</summary>
        public int LicenseLevel;
        public double LicenseCost;
        /// <summary>Планет при открытии и предел открытий. У стартового сектора — SectorPlanets и MaxSectorPlanets из Balance.</summary>
        public int Planets = 30, MaxPlanets = 60;
        /// <summary>Планеты богаче: добыча, запас, цена и шахта ×Richness, содержание ×UpkeepFactor.</summary>
        public double Richness = 1, UpkeepFactor = 1;
        /// <summary>Казна магнатов сектора, когда он открывается. У стартового — MagnateStartMoney.</summary>
        public double MagnateMoney;

        public SectorSpec(string name)
        {
            Name = name;
        }
    }

    public static class Words
    {
        /// <summary>Число со словом в нужной форме: 1 менеджер, 2 менеджера, 5 менеджеров.</summary>
        public static string Count(int n, string one, string few, string many)
        {
            int last2 = n % 100, last = n % 10;
            string word = last2 >= 11 && last2 <= 14 ? many : last == 1 ? one : last >= 2 && last <= 4 ? few : many;
            return $"{n} {word}";
        }
    }
}
