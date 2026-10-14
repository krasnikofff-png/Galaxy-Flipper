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
        /// <summary>Ёмкость сейчас: после каждого восстановления месторождение немного беднеет.</summary>
        public double Capacity;
        /// <summary>Ёмкость при открытии: ниже её доли RegenCapacityFloor месторождение не беднеет.</summary>
        public double InitialCapacity;
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
        /// <summary>Исследовать можно только с лицензией на этот сектор: технология для его ресурса.</summary>
        public int Sector;

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
        /// <summary>Жители получили новый уровень образования.</summary>
        Education,
        /// <summary>На необитаемую планету заселили колонистов.</summary>
        Colony,
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
                case DeedKind.Education:
                    return 2;
                case DeedKind.Festival:
                case DeedKind.Wages:
                case DeedKind.GenerousDeal:
                case DeedKind.CounterBribe:
                case DeedKind.Colony:
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

    /// <summary>
    /// Чем зарабатывают образованные жители. Умственный труд приносит меньше ресурсов,
    /// зато не кончается и даёт небольшой бонус всей корпорации.
    /// </summary>
    public enum Specialty
    {
        None,
        /// <summary>Технари: реже аварии и метеориты на всех ваших планетах.</summary>
        Engineers,
        /// <summary>Химики: месторождения восстанавливаются быстрее.</summary>
        Chemists,
        /// <summary>Физики: исследования дешевле.</summary>
        Physicists,
        /// <summary>Биологи: жители всех планет довольнее, эпидемии реже.</summary>
        Biologists,
        /// <summary>Экономисты: рынок глубже, продажи меньше роняют цену.</summary>
        Economists,
    }

    public static class Specialties
    {
        public static readonly Specialty[] All =
            { Specialty.Engineers, Specialty.Chemists, Specialty.Physicists, Specialty.Biologists, Specialty.Economists };

        public static string RuName(this Specialty s)
        {
            switch (s)
            {
                case Specialty.Engineers: return "технари";
                case Specialty.Chemists: return "химики";
                case Specialty.Physicists: return "физики";
                case Specialty.Biologists: return "биологи";
                case Specialty.Economists: return "экономисты";
                default: return "—";
            }
        }

        /// <summary>Что даёт корпорации каждый уровень образования планеты с этой специальностью.</summary>
        public static string BonusText(this Specialty s)
        {
            switch (s)
            {
                case Specialty.Engineers: return "аварии и метеориты реже";
                case Specialty.Chemists: return "месторождения восстанавливаются быстрее";
                case Specialty.Physicists: return "исследования дешевле";
                case Specialty.Biologists: return "жители довольнее, эпидемии реже";
                case Specialty.Economists: return "продажи меньше роняют цену";
                default: return "";
            }
        }

        /// <summary>К чему склонны жители планеты: так они зарабатывают больше.</summary>
        public static Specialty Natural(Resource r)
        {
            switch (r)
            {
                case Resource.Ice:
                case Resource.Metal: return Specialty.Engineers;
                case Resource.Gas: return Specialty.Chemists;
                case Resource.Biomass: return Specialty.Biologists;
                case Resource.Deuterium: return Specialty.Chemists;
                default: return Specialty.Physicists;
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
