using System;

namespace PlanetTrader.Core
{
    /// <summary>Что игрок знает о планете.</summary>
    public enum Knowledge
    {
        /// <summary>Только положение и размер.</summary>
        Silhouette,
        /// <summary>Тип и примерный запас.</summary>
        Scanned,
        /// <summary>Точный запас и сюрпризы: руины, жители.</summary>
        Explored,
    }

    public enum ProbeMission
    {
        /// <summary>Сканирует все планеты в радиусе и может найти скрытую.</summary>
        ScanArea,
        /// <summary>Полная разведка одной планеты.</summary>
        Explore,
    }

    public sealed class ProbeType
    {
        public string Name;
        public double Cost;
        /// <summary>Как далеко может улететь от вашей планеты или биржи.</summary>
        public double Range;
        /// <summary>Расстояние за ход.</summary>
        public double Speed;
        public double ScanRadius;
        /// <summary>Шанс найти скрытую планету при скане области.</summary>
        public double FindChance;
        /// <summary>С какого уровня корпорации доступен (номер с нуля).</summary>
        public int MinLevel;

        public ProbeType(string name, double cost, double range, double speed, double scanRadius, double findChance)
        {
            Name = name;
            Cost = cost;
            Range = range;
            Speed = speed;
            ScanRadius = scanRadius;
            FindChance = findChance;
        }
    }

    /// <summary>Зонд в полёте.</summary>
    public sealed class Probe
    {
        public int Id;
        /// <summary>Чей зонд: находки и руины достаются этой корпорации.</summary>
        public int CorporationId;
        /// <summary>Прибавка к шансу найти скрытую планету: зонд запускал геолог.</summary>
        public double FindBonus;
        public ProbeType Type;
        public ProbeMission Mission;
        /// <summary>В каком секторе летит: координаты — на его карте.</summary>
        public int Sector;
        public double FromX, FromY, TargetX, TargetY;
        /// <summary>Цель разведки; −1 у скана области.</summary>
        public int TargetPlanetId = -1;
        public int LaunchTurn;
        /// <summary>Ход, в конце которого зонд долетит и сработает.</summary>
        public int ResolveTurn;

        /// <summary>Доля пути, пройденная к началу хода turn (для отрисовки).</summary>
        public double Progress(int turn)
        {
            int total = ResolveTurn - LaunchTurn + 1;
            return Math.Min(1, Math.Max(0, (turn - LaunchTurn) / (double)total));
        }
    }

    /// <summary>Груз ресурса, который летит с планеты на биржу.</summary>
    public sealed class Shipment
    {
        public Resource Resource;
        public double Amount;
        /// <summary>Ход, в конце которого груз попадёт на склад биржи.</summary>
        public int ResolveTurn;
    }

    public static class Geometry
    {
        public static double Distance(double x1, double y1, double x2, double y2)
        {
            double dx = x1 - x2, dy = y1 - y2;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
