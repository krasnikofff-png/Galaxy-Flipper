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
        /// <summary>Выскочка (Нова Рики): наукой не занимается, жителей доит, на аукционах переплачивает, громко сплетничает.</summary>
        Upstart,
        /// <summary>Наследник (Орфей): папины деньги не считает, проигрывать не умеет, давит юристами и перекупает менеджеров.</summary>
        Heir,
        /// <summary>Шейх (Ксенон): молчит, богаче всех, пакостит редко, но крупно — подкупом и исками.</summary>
        Sheikh,
    }

    public enum TrickKind
    {
        Bribe,
        Rumor,
        PoachOrder,
        PoachManager,
        /// <summary>Переманить учёных: образованные жители планеты с невысокой лояльностью уезжают к магнату, уровень образования падает.</summary>
        PoachScientists,
        /// <summary>Рейдерский захват: магнат оспаривает в реестре вашу необитаемую планету.</summary>
        HostileClaim,
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
        /// <summary>Веса пакостей: подкуп, слух, переманить заказчика, переманить менеджера, переманить учёных, рейдерский захват.</summary>
        public double[] TrickWeights;
        /// <summary>До этого хода магнат отбивается от слухов: не покупает и не пакостит.</summary>
        public int QuietUntilTurn;
        /// <summary>Корпорация магната: казна, планеты, статистика — как у игрока.</summary>
        public Corporation Corp;
        /// <summary>Сектор, где магнат живёт: покупает планеты и ставит на аукционах только там.</summary>
        public int Sector;
        /// <summary>Во сколько раз казна на старте больше обычной для сектора (наследство, нефть…).</summary>
        public double MoneyFactor = 1;
        /// <summary>Доля обычного запаса денег, ниже которого магнат не тратит: наследник тратит без счёта.</summary>
        public double ReserveFactor = 1;
        /// <summary>Кто это — для интерфейса; пусто у придуманных магнатов.</summary>
        public string Bio;
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

        public string PersonalityName
        {
            get
            {
                switch (Personality)
                {
                    case Personality.Greedy: return "жадный";
                    case Personality.Chaotic: return "хаотичная";
                    case Personality.Cautious: return "осторожная";
                    case Personality.Upstart: return "выскочка";
                    case Personality.Heir: return "наследник";
                    default: return "шейх";
                }
            }
        }

        /// <summary>Как магнат ведёт дела — для интерфейса.</summary>
        public string Habits
        {
            get
            {
                switch (Personality)
                {
                    case Personality.Greedy: return "Скупает выгодные планеты, щедро ставит на аукционах, подкупает жителей с низкой лояльностью. Хвалит жёсткость с жителями.";
                    case Personality.Chaotic: return "Непредсказуема: ставки вразброс, распускает слухи, переманивает заказчиков и менеджеров.";
                    case Personality.Cautious: return "Осторожна: ставит скромно, пакостит редко, переманивает заказчиков. Ценит заботу о жителях.";
                    case Personality.Upstart: return "Наукой не занимается, школ не строит, жителей доит налогами. На аукционах переплачивает, лишь бы не уступить. Сплетничает и уводит заказчиков горлом.";
                    case Personality.Heir: return "Тратит папины деньги без счёта и не умеет проигрывать аукционы. Натравливает отцовских юристов на ваши планеты и перекупает менеджеров.";
                    default: return "Молчит и почти не пакостит. Но если взялся — подкупает жителей или оспаривает планету в реестре, и денег на это у него хватает.";
                }
            }
        }

        /// <summary>Своя копия на партию: у магната есть состояние, а баланс общий.</summary>
        public Magnate Clone() => new Magnate(Id, Name, Personality, TrickChance, AuctionInterest, BidMin, BidMax, (double[])TrickWeights.Clone())
            { MoneyFactor = MoneyFactor, ReserveFactor = ReserveFactor, Bio = Bio };

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
                bidMin, Math.Max(bidMin + 0.05, J(pattern.BidMax)), (double[])pattern.TrickWeights.Clone())
                { MoneyFactor = pattern.MoneyFactor, ReserveFactor = pattern.ReserveFactor };
        }
    }

    /// <summary>Рейдерский захват: если до срока не оплатить защиту, планета уйдёт магнату за полцены.</summary>
    public sealed class HostileClaim
    {
        public int Id;
        public int PlanetId;
        public int MagnateId;
        /// <summary>В конце этого хода всё решится.</summary>
        public int ResolveTurn;
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
