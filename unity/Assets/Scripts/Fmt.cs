using System.Globalization;
using PlanetTrader.Core;
using UnityEngine;

namespace PlanetTrader.Client
{
    /// <summary>Форматирование чисел и подписи для интерфейса.</summary>
    public static class Fmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>1234567 → «1 234 567». Без культуры ru-RU: её может не быть в сборке IL2CPP.</summary>
        public static string Money(double x) => Mathf.RoundToInt((float)x).ToString("#,0", Inv).Replace(",", " ");

        public static string Amount(double x) => x.ToString(x < 10 ? "0.#" : "0", Inv);

        public static string Percent(double x) => (x * 100).ToString("0", Inv) + "%";

        public static string Multiplier(double x) => "×" + x.ToString("0.00", Inv);

        public static string OwnerName(Owner o, bool foundByProbe)
        {
            switch (o)
            {
                case Owner.Player: return "ваша";
                case Owner.Ai: return "магнат";
                default: return foundByProbe ? "свободна (нашёл ваш зонд)" : "свободна";
            }
        }

        public static string MineName(Planet p)
        {
            switch (p.Mine)
            {
                case MineState.Building: return $"строится, ещё {p.BuildTurnsLeft} х.";
                case MineState.Working: return p.AccidentTurnsLeft > 0 ? $"авария, ещё {p.AccidentTurnsLeft} х." : "работает";
                default: return "нет";
            }
        }

        /// <summary>Цвет планеты по ресурсу.</summary>
        public static Color ResourceColor(Resource r)
        {
            switch (r)
            {
                case Resource.Ice: return new Color(0.62f, 0.86f, 1f);
                case Resource.Metal: return new Color(0.72f, 0.58f, 0.45f);
                case Resource.Gas: return new Color(0.98f, 0.72f, 0.35f);
                case Resource.Biomass: return new Color(0.42f, 0.85f, 0.42f);
                case Resource.Crystals: return new Color(0.82f, 0.52f, 1f);
                case Resource.Quantium: return new Color(1f, 0.35f, 0.62f);
                default: return Color.white;
            }
        }

        /// <summary>Цвет кольца вокруг планеты по владельцу.</summary>
        public static Color OwnerColor(Owner o, bool foundByProbe)
        {
            switch (o)
            {
                case Owner.Player: return new Color(0.3f, 1f, 0.45f);
                case Owner.Ai: return new Color(1f, 0.3f, 0.3f);
                default: return foundByProbe ? new Color(1f, 0.92f, 0.3f) : new Color(0.55f, 0.6f, 0.7f);
            }
        }
    }
}
