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
        /// <summary>Веса пакостей: подкуп, слух, переманить заказчика, переманить менеджера.</summary>
        public double[] TrickWeights;
        /// <summary>До этого хода магнат отбивается от слухов: не покупает и не пакостит.</summary>
        public int QuietUntilTurn;
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
