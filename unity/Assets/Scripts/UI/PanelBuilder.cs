using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Собирает содержимое панели сверху вниз, как раньше IMGUI: заголовок, строки, кнопки, карточки.
    /// Панель пересобирается целиком, когда меняется состояние игры, — кода меньше, а ходов немного.
    /// Вид — в Resources/UI/Game.uss, здесь только классы.
    /// </summary>
    public sealed class PanelBuilder
    {
        readonly Stack<VisualElement> _stack = new Stack<VisualElement>();
        /// <summary>Как выполнить нажатие кнопки: приложение откладывает действие до своего Update.</summary>
        readonly Action<Action> _run;

        public PanelBuilder(VisualElement root, Action<Action> run)
        {
            _stack.Push(root);
            _run = run;
        }

        VisualElement Current => _stack.Peek();

        public T Add<T>(T element) where T : VisualElement
        {
            Current.Add(element);
            return element;
        }

        public Label Label(string text, string cls)
        {
            var l = new Label(text);
            l.AddToClassList(cls);
            return Add(l);
        }

        public Label Title(string text) => Label(text, "title");
        public Label Header(string text) => Label(text, "header");
        public Label Text(string text) => Label(text, "text");
        public Label Small(string text) => Label(text, "small");

        /// <summary>Строка «название — значение».</summary>
        public void Line(string name, string value)
        {
            var row = Add(new VisualElement());
            row.AddToClassList("line");
            var n = new Label(name);
            n.AddToClassList("line-name");
            var v = new Label(value);
            v.AddToClassList("line-value");
            row.Add(n);
            row.Add(v);
        }

        public void Space(string size = "m") => Add(new VisualElement()).AddToClassList("space-" + size);

        /// <summary>Полоска от красной (0) до зелёной (1), под значением строки.</summary>
        public void Bar(float share) =>
            Add(MakeBar(share, Color.Lerp(new Color(1f, 0.4f, 0.3f), new Color(0.4f, 0.9f, 0.5f), share)));

        public static VisualElement MakeBar(float share, Color color)
        {
            var bar = new VisualElement();
            bar.AddToClassList("bar");
            var fill = new VisualElement();
            fill.AddToClassList("bar-fill");
            // Доля и цвет — данные, а не оформление, поэтому заданы прямо на элементе.
            fill.style.width = Length.Percent(Mathf.Clamp01(share) * 100);
            fill.style.backgroundColor = color;
            bar.Add(fill);
            return bar;
        }

        public Button Button(string text, bool enabled, Action onClick)
        {
            var b = MakeButton(text, () => _run(onClick));
            b.SetEnabled(enabled);
            return Add(b);
        }

        public static Button MakeButton(string text, Action onClick)
        {
            var b = new Button(onClick) { text = text, focusable = false };
            b.AddToClassList("button");
            return b;
        }

        /// <summary>Флажок: квадратик и текст; нажатие сообщает новое значение.</summary>
        public void Check(bool on, string text, Action<bool> onChange)
        {
            var b = new Button(() => _run(() => onChange(!on))) { focusable = false };
            b.AddToClassList("check");
            b.EnableInClassList("on", on);
            var box = new Label("✓");
            box.AddToClassList("check-box");
            var t = new Label(text);
            t.AddToClassList("check-text");
            b.Add(box);
            b.Add(t);
            Add(b);
        }

        /// <summary>Ряд кнопок, из которых выбрана одна (налог, тип зонда, вкладки).</summary>
        public VisualElement Choice(IReadOnlyList<string> names, int selected, Action<int> onPick)
        {
            var row = MakeChoice(names, selected, i => _run(() => onPick(i)));
            return Add(row);
        }

        public static VisualElement MakeChoice(IReadOnlyList<string> names, int selected, Action<int> onPick, VisualElement row = null)
        {
            row ??= new VisualElement();
            row.Clear();
            row.AddToClassList("segmented");
            for (int i = 0; i < names.Count; i++)
            {
                int index = i;
                var b = MakeButton(names[i], () => onPick(index));
                b.AddToClassList("segment");
                if (i == 0) b.AddToClassList("first");
                if (i == names.Count - 1) b.AddToClassList("last");
                b.EnableInClassList("selected", i == selected);
                row.Add(b);
            }
            return row;
        }

        /// <summary>Отметить выбранную кнопку ряда, не пересобирая его.</summary>
        public static void Select(VisualElement row, int selected)
        {
            for (int i = 0; i < row.childCount; i++)
                row[i].EnableInClassList("selected", i == selected);
        }

        /// <summary>Карточка в рамке; всё до конца using попадает внутрь.</summary>
        public IDisposable Card(bool alert = false)
        {
            var card = new VisualElement();
            card.AddToClassList("card");
            if (alert) card.AddToClassList("alert");
            return Open(card);
        }

        /// <summary>Контейнер с классом; всё до конца using попадает внутрь.</summary>
        public IDisposable Box(string cls)
        {
            var box = new VisualElement();
            box.AddToClassList(cls);
            return Open(box);
        }

        /// <summary>Большая кнопка: значок, крупная надпись и строка пояснения под ней.</summary>
        public Button BigButton(string icon, string title, string sub, string cls, bool enabled, Action onClick)
        {
            var b = new Button(() => _run(onClick)) { focusable = false };
            b.AddToClassList("big-button");
            b.AddToClassList(cls);
            b.Add(Icons.Make(icon, "big-icon"));
            var text = new VisualElement { pickingMode = PickingMode.Ignore };
            text.AddToClassList("big-text");
            text.Add(new Label(title) { pickingMode = PickingMode.Ignore }.WithClass("big-title"));
            if (!string.IsNullOrEmpty(sub)) text.Add(new Label(sub) { pickingMode = PickingMode.Ignore }.WithClass("big-sub"));
            b.Add(text);
            b.SetEnabled(enabled);
            return Add(b);
        }

        /// <summary>Ряд элементов слева направо.</summary>
        public IDisposable Row()
        {
            var row = new VisualElement();
            row.AddToClassList("row");
            return Open(row);
        }

        IDisposable Open(VisualElement e)
        {
            Add(e);
            _stack.Push(e);
            return new Closer(_stack);
        }

        sealed class Closer : IDisposable
        {
            readonly Stack<VisualElement> _stack;
            public Closer(Stack<VisualElement> stack) => _stack = stack;
            public void Dispose() => _stack.Pop();
        }
    }

    public static class UiExtensions
    {
        /// <summary>Добавить класс и вернуть тот же элемент — чтобы собирать элементы одной строкой.</summary>
        public static T WithClass<T>(this T e, string cls) where T : VisualElement
        {
            e.AddToClassList(cls);
            return e;
        }
    }
}
