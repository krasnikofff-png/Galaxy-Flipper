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
        /// <summary>Положение на карте сектора, от 0 до 1 по каждой оси.</summary>
        public double MapX, MapY;

        /// <summary>Что о планете знает игрок.</summary>
        public Knowledge Knowledge;
        /// <summary>Оценка начального запаса по данным реестра: с ней планету видят все, по ней считается цена.</summary>
        public double EstimatedInitialReserve;
        /// <summary>Руины: разовая находка, достаётся владельцу разведанной планеты. 0 — руин нет.</summary>
        public double RuinsValue;
        public bool RuinsLooted;
        /// <summary>Есть жители: у планеты лояльность, налоги, забастовки, а продают её сами жители.</summary>
        public bool Inhabited;
        /// <summary>Лояльность жителей к владельцу, 0–100.</summary>
        public double Loyalty = 50;
        public TaxLevel Tax;
        public bool HasSchool;
        public bool PaysWages;
        public bool OnStrike;
        /// <summary>До какого хода жители не хотят разговаривать после отказа.</summary>
        public int NoTalksUntilTurn;
        /// <summary>Какой магнат владеет планетой; −1 — не магнат.</summary>
        public int MagnateId = -1;
        /// <summary>Охрана против подкупа жителей.</summary>
        public bool Guarded;

        public Planet(int id, PlanetType type, double reserve)
        {
            Id = id;
            Type = type;
            Reserve = reserve;
            InitialReserve = reserve;
            EstimatedInitialReserve = reserve;
        }

        public Resource Resource => Type.Resource;
        public bool IsDepleted => Reserve <= 1e-9;
        public bool IsProducing => Mine == MineState.Working && AccidentTurnsLeft == 0 && !IsDepleted;

        /// <summary>Содержание без множителя за число планет. Выработанная планета обходится дешевле.</summary>
        public double BaseUpkeep(Balance b) => IsDepleted ? Type.Upkeep * b.DepletedUpkeepShare : Type.Upkeep;

        /// <summary>Сколько ходов ещё можно добывать.</summary>
        public double RemainingTurns => Reserve / Type.OutputPerTurn;

        /// <summary>Текущий запас по оценке реестра.</summary>
        public double EstimatedReserve => InitialReserve > 0 ? EstimatedInitialReserve * Reserve / InitialReserve : 0;

        public bool HasUnlootedRuins => RuinsValue > 0 && !RuinsLooted;

        /// <summary>
        /// Цена в реестре: пропорциональна запасу по оценке реестра (а не настоящему), с надбавкой аукциона,
        /// если планету видят ИИ. Разведка показывает, переплачиваете вы или нет.
        /// </summary>
        public double ListPrice(Balance b) => Math.Round(BasePrice() * (FoundByProbe ? 1 : 1 + b.AuctionMarkup));

        /// <summary>Базовая цена реестра, от которой считаются ставки на аукционе.</summary>
        public double BasePrice()
        {
            double price = Type.Price * EstimatedReserve / Type.AverageReserve;
            // Готовая шахта продаётся вместе с планетой.
            if (Mine != MineState.None) price += Type.MineCost;
            return Math.Round(price);
        }

        /// <summary>Продают ли планету с аукциона, где ставят магнаты.</summary>
        public bool SoldAtAuction => Owner == Owner.Registry && !FoundByProbe && !Inhabited;

        /// <summary>
        /// Рыночная стоимость: дисконтированный чистый доход от оставшейся добычи
        /// по базовым ценам, минус стоимость шахты, если её ещё нет. Не ниже нуля.
        /// </summary>
        public double MarketValue(Balance b) => ValueWithReserve(b, Reserve);

        /// <summary>Стоимость по оценке реестра — так планету видят все, кто её не разведал.</summary>
        public double EstimatedValue(Balance b) => ValueWithReserve(b, EstimatedReserve);

        public double ValueWithReserve(Balance b, double reserve)
        {
            if (reserve <= 1e-9) return 0;
            double r = b.DiscountRate;
            double n = reserve / Type.OutputPerTurn;
            double net = Type.OutputPerTurn * b.BasePrice(Resource) - Type.Upkeep;
            double annuity = (1 - Math.Pow(1 + r, -n)) / r;
            double value = net * annuity;
            if (Mine == MineState.None) value -= Type.MineCost;
            return Math.Max(0, value);
        }
    }
}
