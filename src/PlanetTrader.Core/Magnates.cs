using System;
using System.Collections.Generic;

namespace PlanetTrader.Core
{
    public enum Personality
    {
        /// <summary>Охотится за выгодными планетами, щедро ставит на аукционах, подкупает жителей.</summary>
        Greedy,
        /// <summary>Непредсказуем: ставки вразброс, слухи, переманивает заказчиков и менеджеров.</summary>
        Chaotic,
        /// <summary>Осторожна и благородна: ставит скромно, пакостит редко, заботливых не трогает.</summary>
        Cautious,
    }

    public enum TrickKind
    {
        Bribe,
        Rumor,
        PoachOrder,
        PoachManager,
        /// <summary>Переманить учёных: образованные жители планеты с невысокой лояльностью уезжают к магнату, уровень образования падает.</summary>
        PoachScientists,
    }

    /// <summary>ИИ-магнат: соперник игрока со своим характером.</summary>
    public sealed class Magnate
    {
        public int Id;
        public string Name;
        public Personality Personality;
        /// <summary>Шанс пакости за ход.</summary>
        public double TrickChance;
        /// <summary>Шанс заинтересоваться аукционом и разброс его ставки (доля базовой цены).</summary>
        public double AuctionInterest;
        public double BidMin, BidMax;
        /// <summary>Веса пакостей: подкуп, слух, переманить заказчика, переманить менеджера, переманить учёных.</summary>
        public double[] TrickWeights;
        /// <summary>До этого хода магнат отбивается от слухов: не покупает и не пакостит.</summary>
        public int QuietUntilTurn;
        /// <summary>Корпорация магната: казна, планеты, статистика — как у игрока.</summary>
        public Corporation Corp;
        double _money;
        /// <summary>Казна: магнат покупает планеты, подкупает и учит жителей на свои деньги.</summary>
        public double Money
        {
            get => Corp?.Money ?? _money;
            set
            {
                if (Corp != null) Corp.Money = value;
                else _money = value;
            }
        }
        /// <summary>Чистый доход за прошлый ход: добыча и умственный труд минус содержание.</summary>
        public double LastIncome;
        /// <summary>Поступки игрока, о которых магнат уже высказался.</summary>
        public readonly HashSet<int> CommentedDeeds = new HashSet<int>();

        public Magnate(int id, string name, Personality personality, double trickChance, double auctionInterest,
            double bidMin, double bidMax, double[] trickWeights)
        {
            Id = id;
            Name = name;
            Personality = personality;
            TrickChance = trickChance;
            AuctionInterest = auctionInterest;
            BidMin = bidMin;
            BidMax = bidMax;
            TrickWeights = trickWeights;
        }

        public string PersonalityName =>
            Personality == Personality.Greedy ? "жадный" : Personality == Personality.Chaotic ? "хаотичная" : "осторожная";

        /// <summary>Своя копия на партию: у магната есть состояние, а баланс общий.</summary>
        public Magnate Clone() => new Magnate(Id, Name, Personality, TrickChance, AuctionInterest, BidMin, BidMax, (double[])TrickWeights.Clone());

        static readonly string[] FirstNames = { "Борис", "Ада", "Ксенон", "Луиза", "Орфей", "Глория", "Тимур", "Нова", "Платон", "Вега", "Феликс", "Ирма" };
        static readonly string[] LastNames = { "Кометов", "Пылесосова", "Червоточкин", "Астероидова", "Гравицкий", "Туманова", "Орбитов", "Квазарова", "Метеоров", "Спутникова" };

        /// <summary>
        /// Придуманный магнат для большого сектора: характер и повадки берутся у образца того же характера с разбросом ±15%.
        /// </summary>
        public static Magnate Generate(int id, Magnate pattern, Rng rng)
        {
            string name = FirstNames[rng.Range(0, FirstNames.Length)] + " " + LastNames[rng.Range(0, LastNames.Length)];
            double J(double x) => x * rng.Range(0.85, 1.15);
            double bidMin = J(pattern.BidMin);
            return new Magnate(id, name, pattern.Personality, J(pattern.TrickChance), Math.Min(1, J(pattern.AuctionInterest)),
                bidMin, Math.Max(bidMin + 0.05, J(pattern.BidMax)), (double[])pattern.TrickWeights.Clone());
        }
    }

    /// <summary>Магнат подкупает жителей вашей планеты. Если не помешать, планета перейдёт к нему.</summary>
    public sealed class BribeAttempt
    {
        public int Id;
        public int PlanetId;
        public int MagnateId;
        /// <summary>В конце этого хода всё решится.</summary>
        public int ResolveTurn;
    }
}
