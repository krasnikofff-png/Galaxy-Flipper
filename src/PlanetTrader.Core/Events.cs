namespace PlanetTrader.Core
{
    /// <summary>
    /// Сезонное галактическое событие: на несколько ходов меняет цены и правила.
    /// Все множители по умолчанию 1 — событие меняет только то, что задано.
    /// </summary>
    public sealed class GalacticEvent
    {
        public string Name;
        public string Description;
        public int Duration;

        /// <summary>Цена ресурса на бирже, по ресурсам; null — без изменений.</summary>
        public double[] PriceFactor;
        /// <summary>За сколько можно продать планету этого типа (по ресурсу); null — без изменений.</summary>
        public double[] PlanetSaleFactor;
        public double UpkeepFactor = 1;
        /// <summary>Содержание умножается только у корпораций с таким числом планет и больше; 0 — у всех.</summary>
        public int UpkeepFactorFromPlanets;
        public double AccidentFactor = 1;
        public double MeteorFactor = 1;
        public double EpidemicFactor = 1;
        public double OrderChanceFactor = 1;
        /// <summary>Дополнительно к выкачиванию ресурсов, лояльность за ход на обитаемых планетах.</summary>
        public double ExtraLoyaltyDrain;

        public GalacticEvent(string name, string description, int duration)
        {
            Name = name;
            Description = description;
            Duration = duration;
        }

        public double Price(Resource r) => PriceFactor == null ? 1 : PriceFactor[(int)r];
        public double PlanetSale(Resource r) => PlanetSaleFactor == null ? 1 : PlanetSaleFactor[(int)r];

        /// <summary>Множители по ресурсам: все 1, кроме перечисленных.</summary>
        public static double[] Only(params (Resource r, double factor)[] items)
        {
            var a = new double[ResourceTypes.Count];
            for (int i = 0; i < a.Length; i++) a[i] = 1;
            foreach (var (r, f) in items) a[(int)r] = f;
            return a;
        }

        public static double[] All(double factor)
        {
            var a = new double[ResourceTypes.Count];
            for (int i = 0; i < a.Length; i++) a[i] = factor;
            return a;
        }
    }

    /// <summary>Идущее событие.</summary>
    public sealed class ActiveEvent
    {
        public GalacticEvent Event;
        public int StartTurn;
        /// <summary>Последний ход, на котором событие действует.</summary>
        public int EndTurn;
    }
}
