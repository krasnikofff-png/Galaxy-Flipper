using System;

namespace PlanetTrader.Core
{
    /// <summary>Галактическая биржа: у каждого ресурса своя цена, продажи её роняют.</summary>
    public sealed class Market
    {
        readonly Balance _b;
        readonly double[] _price = new double[Resources.Count];

        public Market(Balance balance)
        {
            _b = balance;
            foreach (var r in Resources.All) _price[(int)r] = balance.BasePrice(r);
        }

        public double Price(Resource r) => _price[(int)r];

        /// <summary>Во сколько раз упадёт цена, если продать amount.</summary>
        double DropFactor(Resource r, double amount) =>
            Math.Max(_b.MinPriceShare * _b.BasePrice(r) / _price[(int)r],
                1 - _b.SalesImpact * amount / _b.Depth(r));

        /// <summary>Выручка за amount без изменения цены (средняя цена между «до» и «после»).</summary>
        public double Quote(Resource r, double amount)
        {
            if (amount <= 0) return 0;
            double p = _price[(int)r];
            return amount * p * (1 + DropFactor(r, amount)) / 2;
        }

        /// <summary>Цена, до которой опустится ресурс после продажи amount.</summary>
        public double PriceAfter(Resource r, double amount) => _price[(int)r] * DropFactor(r, Math.Max(0, amount));

        public double Sell(Resource r, double amount)
        {
            if (amount <= 0) return 0;
            double revenue = Quote(r, amount);
            _price[(int)r] *= DropFactor(r, amount);
            return revenue;
        }

        /// <summary>Конец хода: цена возвращается к базовой и немного шумит.</summary>
        public void Tick(Rng rng)
        {
            foreach (var r in Resources.All)
            {
                int i = (int)r;
                double baseP = _b.BasePrice(r);
                double p = _price[i] + _b.PriceRecovery * (baseP - _price[i]);
                p *= 1 + rng.Range(-_b.MarketNoise, _b.MarketNoise);
                _price[i] = Math.Max(_b.MinPriceShare * baseP, p);
            }
        }
    }
}
