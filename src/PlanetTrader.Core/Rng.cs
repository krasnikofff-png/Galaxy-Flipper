using System;

namespace PlanetTrader.Core
{
    /// <summary>
    /// Детерминированный генератор (SplitMix64). Одинаковый сид даёт одинаковую партию
    /// и в .NET, и в Unity — в отличие от System.Random.
    /// </summary>
    public sealed class Rng
    {
        ulong _state;

        public Rng(ulong seed)
        {
            _state = seed;
        }

        public ulong NextULong()
        {
            ulong z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Число в [0, 1).</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

        /// <summary>Целое в [min, max).</summary>
        public int Range(int min, int max)
        {
            if (max <= min) throw new ArgumentException("max must be greater than min");
            return min + (int)(NextULong() % (ulong)(max - min));
        }

        public double Range(double min, double max) => min + (max - min) * NextDouble();

        public bool Chance(double p) => NextDouble() < p;
    }
}
