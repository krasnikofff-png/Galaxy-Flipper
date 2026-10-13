namespace PlanetTrader.Core
{
    /// <summary>Налог с жителей обитаемой планеты.</summary>
    public enum TaxLevel
    {
        None,
        Low,
        High,
    }

    public enum RequestKind
    {
        /// <summary>Привезти ресурс со склада биржи.</summary>
        Resource,
        /// <summary>Построить школу.</summary>
        School,
    }

    /// <summary>Просьба жителей. Выполните к сроку — лояльность вырастет, не выполните — упадёт.</summary>
    public sealed class ResidentRequest
    {
        public int Id;
        public int PlanetId;
        public RequestKind Kind;
        public Resource Resource;
        public double Amount;
        /// <summary>Последний ход, на котором просьбу можно выполнить.</summary>
        public int DeadlineTurn;
    }

    /// <summary>Предложение жителям, которые продают свою планету.</summary>
    public sealed class Offer
    {
        /// <summary>Доля цены реестра, которую предлагает игрок.</summary>
        public double PriceShare;
        /// <summary>Игрок обещает построить школу: жители уступают в цене.</summary>
        public bool WithSchool;

        public Offer(double priceShare, bool withSchool = false)
        {
            PriceShare = priceShare;
            WithSchool = withSchool;
        }
    }
}
