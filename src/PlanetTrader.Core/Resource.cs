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
    }

    public static class ResourceTypes
    {
        public const int Count = 6;

        public static readonly Resource[] All =
        {
            Resource.Ice, Resource.Metal, Resource.Gas,
            Resource.Biomass, Resource.Crystals, Resource.Quantium,
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
                default: return r.ToString();
            }
        }
    }
}
