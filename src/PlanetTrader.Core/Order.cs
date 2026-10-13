namespace PlanetTrader.Core
{
    /// <summary>Заказ: «Империи Зорг нужно 500 тонн льда за 5 ходов».</summary>
    public sealed class Order
    {
        public readonly int Id;
        public readonly string Customer;
        public readonly Resource Resource;
        public readonly double Amount;
        /// <summary>Последний ход, на котором заказ можно выполнить.</summary>
        public readonly int DeadlineTurn;

        public Order(int id, string customer, Resource resource, double amount, int deadlineTurn)
        {
            Id = id;
            Customer = customer;
            Resource = resource;
            Amount = amount;
            DeadlineTurn = deadlineTurn;
        }

        /// <summary>Заказ платит рыночную цену с наценкой, без влияния на биржу.</summary>
        public double Payout(Market market, Balance b) => Amount * market.Price(Resource) * b.OrderMarkup;
    }
}
