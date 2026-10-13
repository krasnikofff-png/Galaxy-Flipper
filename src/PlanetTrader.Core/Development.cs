using System;

namespace PlanetTrader.Core
{
    /// <summary>
    /// Дополнительное месторождение: небольшое, другого ресурса, чем основной.
    /// Чтобы его добывать, нужна технология для этого ресурса и экстрактор на планете.
    /// </summary>
    public sealed class Deposit
    {
        public Resource Resource;
        public double Capacity;
        public double Reserve;
        public double OutputPerTurn;
        public bool HasExtractor;
        /// <summary>Сколько ходов ещё восстанавливается после истощения; 0 — можно добывать.</summary>
        public int RegenTurnsLeft;
        public int RegenTotal;

        public bool IsRegenerating => RegenTurnsLeft > 0;
    }

    /// <summary>Технология в дереве развития корпорации.</summary>
    public sealed class Technology
    {
        public string Id;
        public string Name;
        public string Description;
        public double Cost;
        /// <summary>Сколько ходов идёт исследование.</summary>
        public int Turns;
        /// <summary>Какие технологии нужны раньше (все).</summary>
        public string[] Requires = new string[0];
        /// <summary>Открывает добычу этого ресурса на дополнительных месторождениях.</summary>
        public Resource? UnlocksResource;
        /// <summary>Прибавка к добыче на всех ваших планетах, доля.</summary>
        public double OutputBonus;
        /// <summary>Множитель срока восстановления месторождений на ваших планетах.</summary>
        public double RegenFactor = 1;
        /// <summary>Прибавка к лояльности жителей за ход на всех ваших обитаемых планетах.</summary>
        public double LoyaltyBonus;

        public Technology(string id, string name, double cost, int turns, string description)
        {
            Id = id;
            Name = name;
            Cost = cost;
            Turns = turns;
            Description = description;
        }
    }

    /// <summary>Исследование, которое идёт сейчас.</summary>
    public sealed class Research
    {
        public Technology Technology;
        public int TurnsLeft;
    }

    /// <summary>Поступок игрока по отношению к жителям — магнаты его запоминают.</summary>
    public enum DeedKind
    {
        Festival,
        School,
        Wages,
        RequestDone,
        RequestFailed,
        HighTax,
        Strike,
        Riot,
        LowballDeal,
        GenerousDeal,
        BribedRivalResidents,
        CounterBribe,
    }

    public sealed class Deed
    {
        public int Id;
        public int Turn;
        public DeedKind Kind;
        public int PlanetId;
        public string PlanetName;

        /// <summary>Хорошо это для жителей (+) или плохо (−).</summary>
        public int Kindness => Weight(Kind);

        public static int Weight(DeedKind k)
        {
            switch (k)
            {
                case DeedKind.School:
                case DeedKind.RequestDone:
                    return 2;
                case DeedKind.Festival:
                case DeedKind.Wages:
                case DeedKind.GenerousDeal:
                case DeedKind.CounterBribe:
                    return 1;
                case DeedKind.HighTax:
                case DeedKind.LowballDeal:
                    return -1;
                case DeedKind.RequestFailed:
                case DeedKind.Strike:
                case DeedKind.BribedRivalResidents:
                    return -2;
                case DeedKind.Riot:
                    return -3;
                default:
                    return 0;
            }
        }
    }

    /// <summary>Реплика магната.</summary>
    public sealed class Remark
    {
        public int Turn;
        public int MagnateId;
        public string Text;
    }
}
