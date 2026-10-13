namespace PlanetTrader.Core
{
    /// <summary>Заказ: «Империи Зорг нужно 500 тонн льда за 5 ходов».</summary>
    public sealed class Order
    {
        public readonly int Id;
        public readonly string Customer;
        public readonly Resource Resource;
        public readonly double Amount;
        /// <summary>Ход, к которому груз должен быть у заказчика.</summary>
        public readonly int DeadlineTurn;
        /// <summary>Где заказчик: на краю сектора.</summary>
        public readonly double X, Y;

        public Order(int id, string customer, Resource resource, double amount, int deadlineTurn, double x, double y)
        {
            Id = id;
            Customer = customer;
            Resource = resource;
            Amount = amount;
            DeadlineTurn = deadlineTurn;
            X = x;
            Y = y;
        }

        /// <summary>Заказ платит рыночную цену с наценкой, без влияния на биржу. Доставку оплачивает игрок.</summary>
        public double GrossPayout(Market market, Balance b) => Amount * market.Price(Resource) * b.OrderMarkup;

        public double Distance(Balance b) => Geometry.Distance(b.ExchangeX, b.ExchangeY, X, Y);
    }
}
