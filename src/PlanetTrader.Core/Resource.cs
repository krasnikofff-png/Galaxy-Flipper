namespace PlanetTrader.Core
{
    public enum Resource
    {
        Ice,
        Metal,
        Gas,
        Biomass,
        Crystals,
        Quantium,
        /// <summary>Плазма — вулканические планеты сектора «Огненный пояс» (этап 8).</summary>
        Plasma,
        /// <summary>Дейтерий — планеты-океаны сектора «Лазурная бездна» (этап 8).</summary>
        Deuterium,
    }

    public static class ResourceTypes
    {
        public const int Count = 8;

        public static readonly Resource[] All =
        {
            Resource.Ice, Resource.Metal, Resource.Gas,
            Resource.Biomass, Resource.Crystals, Resource.Quantium,
            Resource.Plasma, Resource.Deuterium,
        };

        public static string RuName(this Resource r)
        {
            switch (r)
            {
                case Resource.Ice: return "Лёд";
                case Resource.Metal: return "Металл";
                case Resource.Gas: return "Газ";
                case Resource.Biomass: return "Биомасса";
                case Resource.Crystals: return "Кристаллы";
                case Resource.Quantium: return "Квантий";
                case Resource.Plasma: return "Плазма";
                case Resource.Deuterium: return "Дейтерий";
                default: return r.ToString();
            }
        }
    }
}
