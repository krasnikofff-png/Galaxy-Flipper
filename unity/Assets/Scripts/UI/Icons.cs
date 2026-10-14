using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Значки интерфейса. Каждый — несколько контуров в записи SVG-путей на поле 24×24
    /// (M, L, H, V, C, Q, Z, строчные — относительно; O cx cy r — окружность). Рисуются Painter2D
    /// цветом текста элемента (USS <c>color</c>), поэтому значок перекрашивается стилями, как текст.
    /// В разметке значок — элемент с классами <c>icon icon-имя</c>; в коде — <see cref="Make"/>.
    /// </summary>
    public static class Icons
    {
        /// <summary>
        /// Контуры значков. Префикс: «s» — обвести (s2.5 — линией толще), «f» — залить.
        /// </summary>
        static readonly Dictionary<string, string[]> Set = new Dictionary<string, string[]>
        {
            ["logo"] = new[] { "f:M2 4h6l4 9 4-9h6L12 22z" },
            ["coins"] = new[] { "s:M5 7C5 5.3 8.1 4 12 4C15.9 4 19 5.3 19 7C19 8.7 15.9 10 12 10C8.1 10 5 8.7 5 7Z M5 7v5C5 13.7 8.1 15 12 15C15.9 15 19 13.7 19 12V7 M5 12v5C5 18.7 8.1 20 12 20C15.9 20 19 18.7 19 17V12" },
            ["coin"] = new[] { "s2:O12 12 8", "f:O12 12 3.5" },
            ["layers"] = new[] { "s:M12 3l9 4.5-9 4.5-9-4.5z M3 12l9 4.5 9-4.5 M3 16.5l9 4.5 9-4.5" },
            ["flame"] = new[] { "f:M12 2C13 6 18 8.5 18 14C18 18 15.3 21.5 12 21.5C8.7 21.5 6 18.5 6 14.5C6 11.5 8 9.5 9 7.5C10 9.5 11 10.5 12.5 10.5C12.5 7.5 11.2 5 12 2Z" },
            ["bolt"] = new[] { "f:M13.5 2L4.5 13.5h6.5l-1.5 8.5 9.5-12h-6.5l1.5-8z" },
            ["planet"] = new[] { "s:O12 12 6", "s:M5.2 15.6C2.2 18.8 6.5 19.2 12.5 16.2C18.5 13.2 22.5 8.6 19.6 7.6C18.6 7.3 17.5 7.4 16.2 7.8" },
            ["chart"] = new[] { "s:M3 20.5h18 M5 16l4.5-5 3.5 3.5 6-7.5 M15.5 7h3.5v3.5" },
            ["clipboard"] = new[] { "s:M9 3.5h6v3H9z M7 5H5v16h14V5h-2 M8 11h8 M8 15h6" },
            ["bars"] = new[] { "s2.6:M5 20v-6 M10 20V10 M15 20v-8 M20 20V6" },
            ["person"] = new[] { "s:O12 8 4 M4 21C4 16.5 7.5 14 12 14C16.5 14 20 16.5 20 21" },
            ["flask"] = new[] { "s:M9 3h6 M10 3v6.5L4.8 18.5C4.2 19.7 5 21 6.4 21h11.2C19 21 19.8 19.7 19.2 18.5L14 9.5V3 M7 15h10" },
            ["crown"] = new[] { "s:M3 18L4 7l5 5 3-7 3 7 5-5 1 11z M3 21h18" },
            ["arrow"] = new[] { "s2.2:M4 12h15 M13 6l6 6-6 6" },
            ["chevron"] = new[] { "s2:M9 5l7 7-7 7" },
            ["target"] = new[] { "s:O12 12 7 M12 2v4 M12 18v4 M2 12h4 M18 12h4", "f:O12 12 1.6" },
            ["minus"] = new[] { "s2:M5 12h14" },
            ["plus"] = new[] { "s2:M12 5v14M5 12h14" },
            ["rocket"] = new[] { "s:M12 2.5C15.5 5.5 16.5 10.5 15 16H9C7.5 10.5 8.5 5.5 12 2.5Z M9 16l-3 4h4 M15 16l3 4h-4 O12 9.5 1.8" },
            ["leaf"] = new[] { "s:M5 19C5 9.5 11.5 4 20 4C20 12.5 14.5 19 5 19Z M5 19l9-9" },
            ["flag"] = new[] { "s:M5 21V3.5 M5 4h12l-2.5 4.5L17 13H5" },
            ["eye"] = new[] { "s:M2 12C5 6.5 19 6.5 22 12C19 17.5 5 17.5 2 12Z O12 12 3" },
            ["pickaxe"] = new[] { "s2:M14 10L4.5 19.5", "s2:M6.5 6C10.5 3.2 16 3.4 19.5 6.8 M17 4.5C20.5 8 21 12.5 18.5 17" },
            ["tag"] = new[] { "s:M3 12.5V4h8.5l9.5 9.5-8 8z O7.5 8.5 1.5" },
            ["gear"] = new[] { "s:O12 12 3.2 O12 12 7 M12 2v3 M12 19v3 M2 12h3 M19 12h3 M4.9 4.9L7 7 M17 17l2.1 2.1 M4.9 19.1L7 17 M17 7l2.1-2.1" },
            ["cube"] = new[] { "s:M12 3l8 4.5v9L12 21l-8-4.5v-9z M4 7.5l8 4.5 8-4.5 M12 12v9" },
            ["station"] = new[] { "s:O12 12 2.6 O12 12 7.5 M12 2v2.5 M12 19.5V22 M2 12h2.5 M19.5 12H22" },
            ["people"] = new[] { "s:O9 8 3 M3 20C3 16.5 5.5 14 9 14C12.5 14 15 16.5 15 20 O17 9 2.5 M16.5 14C19 14 21 16 21 19" },
            ["info"] = new[] { "s:O12 12 9 M12 11v6", "f:O12 7.5 1.2" },
            ["check"] = new[] { "s2.2:M5 12.5l4.5 4.5L19 7.5" },
            ["up"] = new[] { "f:M12 5l7 10H5z" },
            ["down"] = new[] { "f:M12 19l7-10H5z" },
            ["close"] = new[] { "s2:M6 6l12 12M18 6L6 18" },
            ["journal"] = new[] { "s:M6 3h12v18H6z M9 8h6 M9 12h6 M9 16h4" },
            ["warning"] = new[] { "s:M12 3l10 18H2z M12 10v5", "f:O12 18 1.1" },
            ["ice"] = new[] { "s:M12 2v20 M3.3 7l17.4 10 M3.3 17l17.4-10 M9 3.5l3 2.5 3-2.5 M9 20.5l3-2.5 3 2.5" },
            ["metal"] = new[] { "s:M3 17l3.5-7h11l3.5 7z M8 10l1.5-3h5L16 10" },
            ["gem"] = new[] { "s:M7 3.5h10l4.5 5.5L12 21 2.5 9z M2.5 9h19 M9.5 3.5L8 9l4 12 4-12-1.5-5.5" },
            ["rank"] = new[] { "s2:M5 11.5l7-6 7 6 M5 18.5l7-6 7 6" },
            ["star"] = new[] { "f:M12 2.5l2.9 6 6.6.9-4.8 4.6 1.2 6.5L12 17.4 6.1 20.5l1.2-6.5L2.5 9.4l6.6-.9z" },
            ["spark"] = new[] { "f:M12 2l2.2 7.8L22 12l-7.8 2.2L12 22l-2.2-7.8L2 12l7.8-2.2z" },
        };

        enum Op { Move, Line, Cubic, Quad, Close, Circle }

        struct Cmd
        {
            public Op Op;
            public Vector2 A, B, C;
            public float R;
        }

        sealed class Shape
        {
            public bool Fill;
            public float Width;
            public List<Cmd> Cmds;
        }

        static readonly Dictionary<string, Shape[]> Parsed = new Dictionary<string, Shape[]>();
        static readonly Regex Tokens = new Regex(@"[MmLlHhVvCcQqZzO]|-?(?:\d+\.?\d*|\.\d+)", RegexOptions.Compiled);

        /// <summary>Есть ли такой значок.</summary>
        public static bool Has(string name) => Set.ContainsKey(name);

        /// <summary>Новый элемент-значок; размер и цвет — из USS (класс <c>icon</c> и свои классы).</summary>
        public static VisualElement Make(string name, string cls = null)
        {
            var e = new VisualElement();
            e.AddToClassList("icon");
            if (cls != null) e.AddToClassList(cls);
            Attach(e, name);
            return e;
        }

        /// <summary>Оживить значки разметки: элементы с классами <c>icon icon-имя</c>.</summary>
        public static void AttachAll(VisualElement root)
        {
            root.Query(className: "icon").ForEach(e =>
            {
                foreach (var c in e.GetClasses())
                    if (c.StartsWith("icon-") && Set.ContainsKey(c.Substring(5)))
                    {
                        Attach(e, c.Substring(5));
                        break;
                    }
            });
        }

        /// <summary>Сменить значок у готового элемента.</summary>
        public static void Change(VisualElement e, string name)
        {
            e.userData = name;
            e.MarkDirtyRepaint();
        }

        static void Attach(VisualElement e, string name)
        {
            e.pickingMode = PickingMode.Ignore;
            e.userData = name;
            e.generateVisualContent += ctx => Draw(ctx, e);
        }

        static void Draw(MeshGenerationContext ctx, VisualElement e)
        {
            if (!(e.userData is string name)) return;
            var shapes = Shapes(name);
            if (shapes == null) return;
            var r = e.contentRect;
            if (r.width < 1 || r.height < 1) return;
            float s = Mathf.Min(r.width, r.height) / 24f;
            var origin = new Vector2(r.x + (r.width - 24 * s) / 2, r.y + (r.height - 24 * s) / 2);
            var color = e.resolvedStyle.color;
            var p = ctx.painter2D;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            foreach (var shape in shapes)
            {
                p.BeginPath();
                foreach (var c in shape.Cmds)
                {
                    var a = origin + c.A * s;
                    switch (c.Op)
                    {
                        case Op.Move: p.MoveTo(a); break;
                        case Op.Line: p.LineTo(a); break;
                        case Op.Cubic: p.BezierCurveTo(a, origin + c.B * s, origin + c.C * s); break;
                        case Op.Quad: p.QuadraticCurveTo(a, origin + c.B * s); break;
                        case Op.Close: p.ClosePath(); break;
                        case Op.Circle:
                            p.MoveTo(a + new Vector2(c.R * s, 0));
                            p.Arc(a, c.R * s, Angle.Degrees(0), Angle.Degrees(360));
                            p.ClosePath();
                            break;
                    }
                }
                if (shape.Fill)
                {
                    p.fillColor = color;
                    p.Fill();
                }
                else
                {
                    p.strokeColor = color;
                    p.lineWidth = shape.Width * s;
                    p.Stroke();
                }
            }
        }

        static Shape[] Shapes(string name)
        {
            if (Parsed.TryGetValue(name, out var shapes)) return shapes;
            if (!Set.TryGetValue(name, out var defs)) return null;
            shapes = new Shape[defs.Length];
            for (int i = 0; i < defs.Length; i++)
            {
                int colon = defs[i].IndexOf(':');
                string head = defs[i].Substring(0, colon);
                shapes[i] = new Shape
                {
                    Fill = head[0] == 'f',
                    Width = head.Length > 1 ? float.Parse(head.Substring(1), CultureInfo.InvariantCulture) : 1.8f,
                    Cmds = Parse(defs[i].Substring(colon + 1)),
                };
            }
            Parsed[name] = shapes;
            return shapes;
        }

        /// <summary>Разбор SVG-пути (подмножество команд, см. описание класса).</summary>
        static List<Cmd> Parse(string d)
        {
            var cmds = new List<Cmd>();
            var tokens = new List<string>();
            foreach (Match m in Tokens.Matches(d)) tokens.Add(m.Value);
            var pos = Vector2.zero;
            var start = Vector2.zero;
            char op = 'M';
            int i = 0;
            float Num() => float.Parse(tokens[i++], CultureInfo.InvariantCulture);
            while (i < tokens.Count)
            {
                if (char.IsLetter(tokens[i][0])) op = tokens[i++][0];
                bool rel = char.IsLower(op);
                var o = rel ? pos : Vector2.zero;
                switch (char.ToUpper(op))
                {
                    case 'M':
                        pos = o + new Vector2(Num(), Num());
                        start = pos;
                        cmds.Add(new Cmd { Op = Op.Move, A = pos });
                        // После M следующие пары — это L.
                        op = rel ? 'l' : 'L';
                        break;
                    case 'L':
                        pos = o + new Vector2(Num(), Num());
                        cmds.Add(new Cmd { Op = Op.Line, A = pos });
                        break;
                    case 'H':
                        pos = new Vector2((rel ? pos.x : 0) + Num(), pos.y);
                        cmds.Add(new Cmd { Op = Op.Line, A = pos });
                        break;
                    case 'V':
                        pos = new Vector2(pos.x, (rel ? pos.y : 0) + Num());
                        cmds.Add(new Cmd { Op = Op.Line, A = pos });
                        break;
                    case 'C':
                    {
                        var a = o + new Vector2(Num(), Num());
                        var b = o + new Vector2(Num(), Num());
                        pos = o + new Vector2(Num(), Num());
                        cmds.Add(new Cmd { Op = Op.Cubic, A = a, B = b, C = pos });
                        break;
                    }
                    case 'Q':
                    {
                        var a = o + new Vector2(Num(), Num());
                        pos = o + new Vector2(Num(), Num());
                        cmds.Add(new Cmd { Op = Op.Quad, A = a, B = pos });
                        break;
                    }
                    case 'Z':
                        cmds.Add(new Cmd { Op = Op.Close });
                        pos = start;
                        // Число сразу после Z — ошибка записи; пропускаем, чтобы не зациклиться.
                        if (i < tokens.Count && !char.IsLetter(tokens[i][0])) i++;
                        break;
                    case 'O':
                    {
                        var c = new Vector2(Num(), Num());
                        cmds.Add(new Cmd { Op = Op.Circle, A = c, R = Num() });
                        pos = c;
                        break;
                    }
                    default:
                        i++;
                        break;
                }
            }
            return cmds;
        }
    }
}
