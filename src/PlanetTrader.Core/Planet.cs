using System;
using System.Collections.Generic;

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
        /// <summary>Корпорация-владелец; null — планета в реестре.</summary>
        public Corporation OwnerCorp;
        /// <summary>Кто владеет: реестр, игрок (любой, если игроков несколько) или ИИ-магнат.</summary>
        public Owner Owner => OwnerCorp == null ? Owner.Registry : OwnerCorp.IsAi ? Owner.Ai : Owner.Player;
        public MineState Mine;
        public int BuildTurnsLeft;
        public int AccidentTurnsLeft;
        /// <summary>Найдена зондом игрока: её видит только игрок, покупка без надбавки ИИ.</summary>
        public bool FoundByProbe;
        /// <summary>Имя легендарной планеты («Лот недели»); null — обычная.</summary>
        public string Legend;
        /// <summary>Ход, на котором игрок купил планету (для статистики).</summary>
        public int BoughtOnTurn = -1;
        /// <summary>Сколько игрок вложил в планету: покупка + шахта.</summary>
        public double Invested;
        /// <summary>Сколько игрок заработал с планеты продажей ресурсов (оценка по рынку).</summary>
        public double Earned;
        /// <summary>Сектор (номер в Balance.Sectors); у каждого сектора своя карта.</summary>
        public int Sector;
        /// <summary>Положение на карте своего сектора, от 0 до 1 по каждой оси.</summary>
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
        public int MagnateId => OwnerCorp?.Magnate?.Id ?? -1;
        /// <summary>Охрана против подкупа жителей.</summary>
        public bool Guarded;
        /// <summary>Сколько ещё ходов идёт эпидемия: добыча вполовину.</summary>
        public int EpidemicTurnsLeft;
        /// <summary>Ход, когда шахту разбил метеорит или разнесли бунтовщики; −1 — не было. Нужен картинке: обломки.</summary>
        public int MineWreckedTurn = -1;
        /// <summary>Шахту разнесли бунтовщики, а не метеорит.</summary>
        public bool MineWreckedByRiot;
        /// <summary>Основное месторождение восстанавливается: столько ходов ещё, из скольких.</summary>
        public int RegenTurnsLeft;
        public int RegenTotal;
        /// <summary>Ёмкость основного месторождения сейчас: после каждого восстановления немного меньше.</summary>
        public double Capacity;
        /// <summary>Уровень образования жителей 0–3: чем выше, тем больше доход от умственного труда.</summary>
        public int Education;
        public Specialty Specialty;
        /// <summary>Сколько ходов ещё учатся до следующего уровня; 0 — не учатся.</summary>
        public int EducationTurnsLeft;
        /// <summary>Дополнительные месторождения: небольшие, других ресурсов.</summary>
        public readonly List<Deposit> ExtraDeposits = new List<Deposit>();

        public Planet(int id, PlanetType type, double reserve)
        {
            Id = id;
            Type = type;
            Reserve = reserve;
            InitialReserve = reserve;
            Capacity = reserve;
            EstimatedInitialReserve = reserve;
        }

        public Resource Resource => Type.Resource;
        /// <summary>Основное месторождение выработано и восстанавливается.</summary>
        public bool IsDepleted => Reserve <= 1e-9 || RegenTurnsLeft > 0;
        public bool IsRegenerating => RegenTurnsLeft > 0;
        public bool IsProducing => Mine == MineState.Working && AccidentTurnsLeft == 0 && !IsDepleted;

        /// <summary>Содержание без множителя за число планет. Выработанная планета обходится дешевле.</summary>
        public double BaseUpkeep(Balance b) => IsDepleted ? Type.Upkeep * b.DepletedUpkeepShare : Type.Upkeep;

        /// <summary>Сколько ходов ещё можно добывать.</summary>
        public double RemainingTurns => Reserve / Type.OutputPerTurn;

        /// <summary>Текущий запас по оценке реестра.</summary>
        public double EstimatedReserve => InitialReserve > 0 ? EstimatedInitialReserve * Reserve / InitialReserve : 0;

        /// <summary>Ёмкость основного месторождения по оценке реестра.</summary>
        public double EstimatedCapacity => InitialReserve > 0 ? EstimatedInitialReserve * Capacity / InitialReserve : 0;

        public bool IsStudying => EducationTurnsLeft > 0;

        /// <summary>Склонны ли жители к своей специальности.</summary>
        public bool NaturalSpecialty => Specialty != Specialty.None && Specialty == Specialties.Natural(Resource);

        /// <summary>Доход от умственного труда за ход при лояльности 50 и без шахты.</summary>
        public double IntellectBase(Balance b) =>
            !Inhabited || Education <= 0 ? 0 : b.IntellectIncome[Education] * (NaturalSpecialty ? 1 + b.NaturalSpecialtyBonus : 1);

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
        /// Рыночная стоимость. Месторождения не кончаются, а восстанавливаются, поэтому это бесконечный доход:
        /// средняя за цикл «добыча + восстановление» выручка минус содержание, делённые на ставку дисконтирования.
        /// Пока основное месторождение восстанавливается, стоимость дисконтируется на оставшиеся ходы.
        /// Дополнительные месторождения с экстракторами добавляют свою среднюю выручку, образованные жители — умственный труд.
        /// </summary>
        public double MarketValue(Balance b) => ValueWithCapacity(b, Capacity);

        /// <summary>Стоимость по оценке реестра — так планету видят все, кто её не разведал.</summary>
        public double EstimatedValue(Balance b) => ValueWithCapacity(b, EstimatedCapacity);

        /// <summary>Средняя добыча за цикл: месторождение capacity, добыча output, восстановление regen ходов.</summary>
        public static double CycleAverage(double capacity, double output, double regen)
        {
            if (capacity <= 1e-9 || output <= 0) return 0;
            double mining = capacity / output;
            return output * mining / (mining + regen);
        }

        public double ValueWithCapacity(Balance b, double capacity)
        {
            double r = b.DiscountRate;
            double mainAverage = CycleAverage(capacity, Type.OutputPerTurn, b.RegenTurns[(int)Resource]);
            double revenue = mainAverage * b.BasePrice(Resource);
            // Умственный труд: пока шахта работает, им занята только часть жителей.
            double miningShare = Mine == MineState.None ? 0 : mainAverage / Type.OutputPerTurn;
            revenue += IntellectBase(b) * (1 - miningShare * (1 - b.IntellectWhileMining));
            foreach (var d in ExtraDeposits)
                if (d.HasExtractor)
                    revenue += CycleAverage(d.Capacity, d.OutputPerTurn, b.RegenTurns[(int)d.Resource]) * b.BasePrice(d.Resource);
            double value = (revenue - Type.Upkeep) / r;
            if (RegenTurnsLeft > 0) value *= Math.Pow(1 + r, -RegenTurnsLeft);
            if (Mine == MineState.None) value -= Type.MineCost;
            return Math.Max(0, value);
        }
    }
}
