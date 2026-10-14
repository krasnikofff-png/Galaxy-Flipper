using System.Collections.Generic;
using PlanetTrader.Core;
using PlanetTrader.Visuals;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Облик магнатов: у каждого свой цвет (кольца его планет на карте, рамка портрета, ленты и камни на бюсте)
    /// и 3D-бюст. Придуманные магнаты без своей модели — тёмный силуэт в цвет из запасной палитры.
    /// </summary>
    public sealed partial class PlanetTraderApp
    {
        MagnatePortraits _portraits;

        /// <summary>Персонажи автора: по первому слову имени — модель и цвет.</summary>
        static readonly Dictionary<string, (string model, Color color)> MagnateLooks = new Dictionary<string, (string, Color)>
        {
            ["Гуго"] = ("magnate_hugo", new Color(1f, 0.3f, 0.27f)),
            ["Зизи"] = ("magnate_zizi", new Color(1f, 0.6f, 0.15f)),
            ["Матильда"] = ("magnate_matilda", new Color(0.8f, 0.6f, 1f)),
            ["Нова"] = ("magnate_nova", new Color(1f, 0.36f, 0.75f)),
            ["Орфей"] = ("magnate_orpheus", new Color(0.36f, 0.65f, 1f)),
            ["Ксенон"] = ("magnate_xenon", new Color(0.2f, 0.86f, 0.84f)),
        };

        /// <summary>Цвета придуманных магнатов — по номеру.</summary>
        static readonly Color[] SparePalette =
        {
            new Color(0.95f, 0.45f, 0.35f), new Color(0.6f, 0.85f, 0.35f), new Color(0.95f, 0.8f, 0.55f),
            new Color(0.55f, 0.55f, 0.95f), new Color(0.9f, 0.5f, 0.95f), new Color(0.5f, 0.8f, 0.95f),
        };

        static (string model, Color color) LookOf(Magnate m)
        {
            if (m == null) return ("magnate_shadow", new Color(0.7f, 0.7f, 0.75f));
            string first = m.Name.Split(' ')[0];
            return MagnateLooks.TryGetValue(first, out var look) ? look : ("magnate_shadow", SparePalette[m.Id % SparePalette.Length]);
        }

        Color MagnateColor(int magnateId) => LookOf(_game.GetMagnate(magnateId)).color;

        /// <summary>Портрет магната в рамке его цвета; size — ширина кадра в точках интерфейса.</summary>
        VisualElement MagnatePortrait(Magnate m, float width, bool highlight = false)
        {
            var (model, color) = LookOf(m);
            var frame = new VisualElement { pickingMode = PickingMode.Ignore };
            frame.AddToClassList("magnate-frame");
            frame.style.width = width;
            frame.style.height = width * 440f / 360f;
            Frames.Attach(frame, Color.Lerp(color, Color.black, highlight ? 0.1f : 0.45f), color,
                Color.Lerp(new Color(0.03f, 0.035f, 0.06f), color, 0.13f), highlight ? 2.4f : 1.6f);
            var image = new Image { image = _portraits.Portrait(m?.Name ?? "?", model, color), scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
            image.AddToClassList("magnate-image");
            frame.Add(image);
            // Портрет живой: перерисовывать, пока карточка на экране.
            image.schedule.Execute(() =>
            {
                _portraits.Portrait(m?.Name ?? "?", model, color);
                image.MarkDirtyRepaint();
            }).Every(33);
            return frame;
        }
    }
}
