using System;

namespace PlanetTrader.Core
{
    public enum Owner
    {
        /// <summary>Ничья, продаётся через Галактический реестр.</summary>
        Registry,
        Player,
        Ai,
    }

    public enum MineState
    {
        None,
        Building,
        Working,
    }

    public sealed class Planet
    {
        public readonly int Id;
        public readonly PlanetType Type;
        public double Reserve;
        public readonly double InitialReserve;
        public Owner Owner;
        public MineState Mine;
        public int BuildTurnsLeft;
        public int AccidentTurnsLeft;
        /// <summary>Найдена зондом игрока: её видит только игрок, покупка без надбавки ИИ.</summary>
        public bool FoundByProbe;
        /// <summary>Ход, на котором игрок купил планету (для статистики).</summary>
        public int BoughtOnTurn = -1;
        /// <summary>Сколько игрок вложил в планету: покупка + шахта.</summary>
        public double Invested;
        /// <summary>Сколько игрок заработал с планеты продажей ресурсов (оценка по рынку).</summary>
        public double Earned;

        public Planet(int id, PlanetType type, double reserve)
        {
            Id = id;
            Type = type;
            Reserve = reserve;
            InitialReserve = reserve;
        }

        public Resource Resource => Type.Resource;
        public bool IsDepleted => Reserve <= 1e-9;
        public bool IsProducing => Mine == MineState.Working && AccidentTurnsLeft == 0 && !IsDepleted;

        /// <summary>Содержание без множителя за число планет. Выработанная планета обходится дешевле.</summary>
        public double BaseUpkeep(Balance b) => IsDepleted ? Type.Upkeep * b.DepletedUpkeepShare : Type.Upkeep;

        /// <summary>Сколько ходов ещё можно добывать.</summary>
        public double RemainingTurns => Reserve / Type.OutputPerTurn;

        /// <summary>Цена в реестре: пропорциональна запасу, с надбавкой аукциона, если планету видят ИИ.</summary>
        public double ListPrice(Balance b)
        {
            double price = Type.Price * InitialReserve / Type.AverageReserve;
            // Частично выработанную планету реестр продаёт пропорционально остатку.
            price *= Reserve / InitialReserve;
            // Готовая шахта продаётся вместе с планетой.
            if (Mine != MineState.None) price += Type.MineCost;
            if (!FoundByProbe) price *= 1 + b.AuctionMarkup;
            return Math.Round(price);
        }

        /// <summary>
        /// Рыночная стоимость: дисконтированный чистый доход от оставшейся добычи
        /// по базовым ценам, минус стоимость шахты, если её ещё нет. Не ниже нуля.
        /// </summary>
        public double MarketValue(Balance b)
        {
            if (IsDepleted) return 0;
            double r = b.DiscountRate;
            double n = RemainingTurns;
            double net = Type.OutputPerTurn * b.BasePrice(Resource) - Type.Upkeep;
            double annuity = (1 - Math.Pow(1 + r, -n)) / r;
            double value = net * annuity;
            if (Mine == MineState.None) value -= Type.MineCost;
            return Math.Max(0, value);
        }
    }
}
