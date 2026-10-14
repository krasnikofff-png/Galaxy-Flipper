using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Рамки панелей в духе научной фантастики: срезанные углы, тонкая бронзовая линия и яркие засечки у углов.
    /// Элемент с классом <c>frame</c> рисует рамку сам (Painter2D); цвета и срез задаются в USS своими свойствами:
    /// <c>--frame-fill</c>, <c>--frame-line</c>, <c>--frame-accent</c>, <c>--frame-cut</c>.
    /// </summary>
    public static class Frames
    {
        static readonly CustomStyleProperty<Color> FillProp = new CustomStyleProperty<Color>("--frame-fill");
        static readonly CustomStyleProperty<Color> LineProp = new CustomStyleProperty<Color>("--frame-line");
        static readonly CustomStyleProperty<Color> AccentProp = new CustomStyleProperty<Color>("--frame-accent");
        static readonly CustomStyleProperty<float> CutProp = new CustomStyleProperty<float>("--frame-cut");

        sealed class Look
        {
            public Color Fill = new Color(0.035f, 0.047f, 0.075f, 0.94f);
            public Color Line = new Color(0.32f, 0.27f, 0.18f);
            public Color Accent = new Color(0.86f, 0.62f, 0.27f);
            public float Cut = 10;
            public float AccentWidth = 1.6f;
        }

        /// <summary>Оживить рамки разметки: все элементы с классом <c>frame</c>.</summary>
        public static void AttachAll(VisualElement root) => root.Query(className: "frame").ForEach(Attach);

        public static void Attach(VisualElement e)
        {
            var look = new Look();
            e.RegisterCallback<CustomStyleResolvedEvent>(evt =>
            {
                var style = evt.customStyle;
                if (style.TryGetValue(FillProp, out var fill)) look.Fill = fill;
                if (style.TryGetValue(LineProp, out var line)) look.Line = line;
                if (style.TryGetValue(AccentProp, out var accent)) look.Accent = accent;
                if (style.TryGetValue(CutProp, out var cut)) look.Cut = cut;
                e.MarkDirtyRepaint();
            });
            e.generateVisualContent += ctx => Draw(ctx, e, look);
        }

        /// <summary>Рамка своих цветов (портрет магната — в его цвет); стили USS её не меняют.</summary>
        public static void Attach(VisualElement e, Color line, Color accent, Color fill, float accentWidth = 1.6f, float cut = 12)
        {
            var look = new Look { Line = line, Accent = accent, Fill = fill, AccentWidth = accentWidth, Cut = cut };
            e.generateVisualContent += ctx => Draw(ctx, e, look);
        }

        static void Draw(MeshGenerationContext ctx, VisualElement e, Look look)
        {
            var r = e.layout;
            float w = r.width, h = r.height;
            if (w < 4 || h < 4) return;
            float c = Mathf.Min(look.Cut, Mathf.Min(w, h) / 3);
            var p = ctx.painter2D;
            // Срезаны левый верхний и правый нижний углы — сильнее, два других — чуть-чуть.
            float s = c * 0.35f;
            var pts = new List<Vector2>
            {
                new Vector2(c, 0.5f), new Vector2(w - s, 0.5f), new Vector2(w - 0.5f, s),
                new Vector2(w - 0.5f, h - c), new Vector2(w - c, h - 0.5f),
                new Vector2(s, h - 0.5f), new Vector2(0.5f, h - s), new Vector2(0.5f, c),
            };
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Count; i++) p.LineTo(pts[i]);
            p.ClosePath();
            p.fillColor = look.Fill;
            p.Fill();
            p.strokeColor = look.Line;
            p.lineWidth = 1;
            p.Stroke();

            // Засечки: срезанные углы и короткие отрезки вдоль краёв от них.
            float tick = Mathf.Min(28, w / 5);
            p.strokeColor = look.Accent;
            p.lineWidth = look.AccentWidth;
            p.lineCap = LineCap.Butt;
            p.BeginPath();
            p.MoveTo(new Vector2(0.8f, c + tick * 0.6f));
            p.LineTo(new Vector2(0.8f, c));
            p.LineTo(new Vector2(c, 0.8f));
            p.LineTo(new Vector2(c + tick, 0.8f));
            p.Stroke();
            p.BeginPath();
            p.MoveTo(new Vector2(w - 0.8f, h - c - tick * 0.6f));
            p.LineTo(new Vector2(w - 0.8f, h - c));
            p.LineTo(new Vector2(w - c, h - 0.8f));
            p.LineTo(new Vector2(w - c - tick, h - 0.8f));
            p.Stroke();
        }
    }

    /// <summary>Маленький график цены: линия по последним ходам и мягкая заливка под ней.</summary>
    public sealed class Sparkline : VisualElement
    {
        IReadOnlyList<double> _values;
        Color _color = Color.white;

        public Sparkline()
        {
            AddToClassList("sparkline");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        public void Show(IReadOnlyList<double> values, Color color)
        {
            _values = values;
            _color = color;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (_values == null || _values.Count < 2 || r.width < 4 || r.height < 4) return;
            double min = double.MaxValue, max = double.MinValue;
            foreach (var v in _values)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }
            // Почти ровная цена — рисуем почти ровной, а не растягиваем шум на всю высоту.
            double span = System.Math.Max(max - min, max * 0.08 + 1e-6);
            double mid = (max + min) / 2;
            var pts = new Vector2[_values.Count];
            for (int i = 0; i < pts.Length; i++)
            {
                float x = r.x + r.width * i / (pts.Length - 1);
                float y = r.y + r.height * (0.5f - (float)((_values[i] - mid) / span) * 0.9f);
                pts[i] = new Vector2(x, y);
            }
            var p = ctx.painter2D;
            p.BeginPath();
            p.MoveTo(new Vector2(pts[0].x, r.yMax));
            foreach (var pt in pts) p.LineTo(pt);
            p.LineTo(new Vector2(pts[pts.Length - 1].x, r.yMax));
            p.ClosePath();
            p.fillColor = new Color(_color.r, _color.g, _color.b, 0.12f);
            p.Fill();
            p.BeginPath();
            p.MoveTo(pts[0]);
            for (int i = 1; i < pts.Length; i++) p.LineTo(pts[i]);
            p.strokeColor = _color;
            p.lineWidth = 1.4f;
            p.lineJoin = LineJoin.Round;
            p.Stroke();
        }
    }
}
