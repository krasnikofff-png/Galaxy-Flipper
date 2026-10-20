using UnityEngine;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Картинки автора из Resources/Pictures/&lt;категория&gt;/&lt;имя&gt; (готовит tools/pictures/make_pictures.py).
    /// Всё, что показывает картинку, сначала спрашивает <see cref="Has"/> и без неё рисует запасной вариант
    /// (бюст, значок, нарисованный фон): новая картинка подключается просто файлом.
    /// </summary>
    public static class Pictures
    {
        public static Texture2D Load(string category, string name) =>
            string.IsNullOrEmpty(name) ? null : Resources.Load<Texture2D>($"Pictures/{category}/{name}");

        /// <summary>Карта глубины (белое — ближе); null — картинка плоская или её нет.</summary>
        public static Texture2D Depth(string category, string name) =>
            string.IsNullOrEmpty(name) ? null : Resources.Load<Texture2D>($"Pictures/{category}/{name}_depth");

        public static bool Has(string category, string name) => Load(category, name) != null;
    }
}
