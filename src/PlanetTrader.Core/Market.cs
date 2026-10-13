using System;

namespace PlanetTrader.Core
{
    /// <summary>Галактическая биржа: у каждого ресурса своя цена, продажи её роняют.</summary>
    public sealed class Market
    {
        readonly Balance _b;
        readonly double[] _price = new double[ResourceTypes.Count];
        /// <summary>Множитель события: к какой цене тянется рынок, пока событие идёт.</summary>
        readonly double[] _eventFactor = { 1, 1, 1, 1, 1, 1 };

        public Market(Balance balance)
        {
            _b = balance;
            foreach (var r in ResourceTypes.All) _price[(int)r] = balance.BasePrice(r);
        }

        public double Price(Resource r) => _price[(int)r];

        /// <summary>Во сколько раз рынок глубже базового: в кооперативе игроков больше, и спроса тоже. Задаёт игра.</summary>
        public double SectorFactor = 1;

        /// <summary>Глубина рынка для продавца; depthFactor — его экономисты делают рынок глубже.</summary>
        public double Depth(Resource r, double depthFactor = 1) => _b.Depth(r) * SectorFactor * depthFactor;

        /// <summary>К какой цене возвращается рынок: базовая, а во время события — с его множителем.</summary>
        public double TargetPrice(Resource r) => _b.BasePrice(r) * _eventFactor[(int)r];

        /// <summary>Событие началось (factors) или кончилось (null). Цена сразу прыгает, а после события плавно возвращается.</summary>
        public void SetEvent(double[] factors)
        {
            foreach (var r in ResourceTypes.All)
            {
                int i = (int)r;
                double f = factors?[i] ?? 1;
                if (factors != null && f != 1) Shock(r, f);
                _eventFactor[i] = f;
            }
        }

        /// <summary>Во сколько раз упадёт цена, если продать amount.</summary>
        double DropFactor(Resource r, double amount, double depthFactor) =>
            Math.Max(_b.MinPriceShare * _b.BasePrice(r) / _price[(int)r],
                1 - _b.SalesImpact * amount / Depth(r, depthFactor));

        /// <summary>Выручка за amount без изменения цены (средняя цена между «до» и «после»).</summary>
        public double Quote(Resource r, double amount, double depthFactor = 1)
        {
            if (amount <= 0) return 0;
            double p = _price[(int)r];
            return amount * p * (1 + DropFactor(r, amount, depthFactor)) / 2;
        }

        /// <summary>Цена, до которой опустится ресурс после продажи amount.</summary>
        public double PriceAfter(Resource r, double amount, double depthFactor = 1) => _price[(int)r] * DropFactor(r, Math.Max(0, amount), depthFactor);

        public double Sell(Resource r, double amount, double depthFactor = 1)
        {
            if (amount <= 0) return 0;
            double revenue = Quote(r, amount, depthFactor);
            _price[(int)r] *= DropFactor(r, amount, depthFactor);
            return revenue;
        }

        /// <summary>Резкое изменение цены, например из-за слуха.</summary>
        public void Shock(Resource r, double factor)
        {
            _price[(int)r] = Math.Max(_b.MinPriceShare * _b.BasePrice(r), _price[(int)r] * factor);
        }

        /// <summary>Конец хода: цена возвращается к базовой и немного шумит.</summary>
        public void Tick(Rng rng)
        {
            foreach (var r in ResourceTypes.All)
            {
                int i = (int)r;
                double baseP = _b.BasePrice(r);
                double p = _price[i] + _b.PriceRecovery * (TargetPrice(r) - _price[i]);
                p *= 1 + rng.Range(-_b.MarketNoise, _b.MarketNoise);
                _price[i] = Math.Max(_b.MinPriceShare * baseP, p);
            }
        }
    }
}
