using System.Collections.Generic;

namespace PlanetTrader.Core
{
    /// <summary>
    /// Корпорация: всё, что принадлежит одному участнику партии, — деньги, долг, действия, склад на бирже,
    /// исследования, поступки с жителями, статистика. Корпорацией управляет игрок (их может быть несколько,
    /// например в кооперативе) или ИИ-магнат.
    /// </summary>
    public sealed class Corporation
    {
        public readonly int Id;
        public string Name;
        /// <summary>Магнат, который управляет корпорацией; null — корпорация игрока.</summary>
        public Magnate Magnate;
        public bool IsAi => Magnate != null;

        public double Money;
        public double Debt;
        /// <summary>Команда: первым — директор (несколько действий за ход), дальше нанятые менеджеры.</summary>
        public readonly List<Manager> Staff = new List<Manager>();
        /// <summary>Кандидаты на работу; обновляются раз в несколько ходов.</summary>
        public readonly List<Manager> Candidates = new List<Manager>();
        public int NextCandidatesTurn;
        /// <summary>Кто выполняет следующие действия; −1 — подбирать лучшего под дело.</summary>
        public int PerformerId = -1;
        public bool Insured;
        /// <summary>Репутация 0–100: грязные дела её портят, выполненные заказы чинят.</summary>
        public double Reputation;
        /// <summary>До этого хода против корпорации действует картель магнатов: биржа платит ей меньше.</summary>
        public int CartelUntilTurn;
        /// <summary>Коллекторы забрали последнюю планету, а долг остался.</summary>
        public bool Bankrupt;

        /// <summary>Склад на бирже и грузы в пути.</summary>
        public readonly double[] Stock = new double[ResourceTypes.Count];
        public readonly List<Shipment> Shipments = new List<Shipment>();

        public readonly HashSet<string> Researched = new HashSet<string>();
        public Research CurrentResearch;
        /// <summary>Поступки с жителями: их помнят магнаты.</summary>
        public readonly List<Deed> Deeds = new List<Deed>();

        // Статистика для симулятора и плейтестов.
        public int TotalSeizures;
        public int TurnsActionsExhausted;
        public int PlanetsLostToBribes;
        public int AuctionsWon;
        public int AuctionsLost;
        public double FinesPaid;

        public Corporation(int id, string name)
        {
            Id = id;
            Name = name;
        }

        public override string ToString() => Name;
    }
}
