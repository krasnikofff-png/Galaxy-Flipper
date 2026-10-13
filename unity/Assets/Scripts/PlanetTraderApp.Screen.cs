using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;
using PlanetTrader.Visuals;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Экран на UI Toolkit: верхняя панель, полоса события, правая панель с вкладками, журнал,
    /// пауза «ходят соперники» и окно планеты. Разметка — Resources/UI/Game.uxml, вид — Game.uss.
    /// </summary>
    public sealed partial class PlanetTraderApp
    {
        // Интерфейс задан в точках для экрана 1600×900 и масштабируется под настоящий, не выходя за его края.
        static readonly Vector2Int ReferenceResolution = new Vector2Int(1600, 900);

        UIDocument _document;
        VisualElement _screen, _stats, _tabs, _rivals, _rivalsList, _rivalsProgress;
        ScrollView _sideScroll, _logView;
        Label _eventBar, _tooltip, _rivalsTitle;
        Button _probesButton;
        readonly List<Label> _statLabels = new List<Label>();

        /// <summary>Состояние игры изменилось — пересобрать панели в конце кадра.</summary>
        bool _dirty = true;
        /// <summary>Для какой вкладки и планеты собрана правая панель: сменились — прокрутка наверх.</summary>
        (Tab tab, int planet) _panelFor = ((Tab)(-1), -1);

        // Окно планеты: живёт всё время, при пересборке вкладки переставляется в новое место.
        VisualElement _portraitBox;
        Button _portraitButton;
        Texture _portraitTexture;

        void MarkDirty() => _dirty = true;

        /// <summary>Сколько пикселей экрана в одной точке интерфейса.</summary>
        float _pixelScale = 1;

        void CreateScreen()
        {
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "Интерфейс игры";
            settings.themeStyleSheet = Resources.Load<ThemeStyleSheet>("UI/Theme");
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = ReferenceResolution;
            settings.screenMatchMode = PanelScreenMatchMode.Shrink;

            var go = new GameObject("Интерфейс");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            _document = go.AddComponent<UIDocument>();
            _document.panelSettings = settings;
            _document.visualTreeAsset = Resources.Load<VisualTreeAsset>("UI/Game");
            go.SetActive(true);

            var root = _document.rootVisualElement;
            _screen = root.Q("screen");
            _stats = root.Q("stats");
            _tabs = root.Q("tabs");
            _sideScroll = root.Q<ScrollView>("sideScroll");
            _logView = root.Q<ScrollView>("log");
            _eventBar = root.Q<Label>("eventBar");
            _tooltip = root.Q<Label>("tooltip");
            _rivals = root.Q("rivals");
            _rivalsList = root.Q("rivalsList");
            _rivalsProgress = root.Q("rivalsProgress");
            _rivalsTitle = root.Q<Label>("rivalsTitle");
            _probesButton = root.Q<Button>("probesButton");

            _probesButton.clicked += () => Later(() => _probeMode = !_probeMode);
            root.Q<Button>("endTurnButton").clicked += () => Later(RequestEndTurn);
            root.Q<Button>("showcaseButton").clicked += () => Later(() => PlanetShowcase.Toggle(this));
            root.Q<Button>("newGameButton").clicked += () => Later(NewGame);

            foreach (var name in new[] { "Ход", "Деньги", "Долг", "Капитал", "Расходы/ход", "Действия", "Репутация" })
            {
                var l = new Label { name = name };
                l.AddToClassList("stat");
                _stats.Add(l);
                _statLabels.Add(l);
            }
            PanelBuilder.MakeChoice(TabNames, (int)_tab, i => Later(() => _tab = (Tab)i), _tabs);

            CreatePortrait();
            CreateMap(root);
        }

        void ShowScreen(bool shown) => _screen?.EnableInClassList("hidden", !shown);

        /// <summary>Новая партия: журнал пуст, панели — заново.</summary>
        void ResetScreen()
        {
            _logView.Clear();
            HideTooltip();
            ShowRivals(false);
            ResetMap();
            MarkDirty();
        }

        void RefreshScreen()
        {
            _pixelScale = _screen.layout.width > 1 ? Screen.width / _screen.layout.width : 1;
            _mapCanvas.MarkDirtyRepaint();
            if (_mapDirty && !_dirty) RefreshMap();
            if (!_dirty) return;
            _dirty = false;
            RefreshTopBar();
            RefreshEventBar();
            RefreshSidePanel();
            RefreshMap();
        }

        void Log(string message)
        {
            var line = new Label(message);
            line.AddToClassList("log-line");
            _logView.Insert(0, line);
            var lines = _logView.contentContainer;
            while (lines.childCount > MaxLogLines) lines.RemoveAt(lines.childCount - 1);
        }

        // ------------------------------------------------------------------
        // Верхняя панель и полоса события
        // ------------------------------------------------------------------

        void RefreshTopBar()
        {
            var g = _game;
            string[] values =
            {
                g.Turn.ToString(), Fmt.Money(g.Money), Fmt.Money(g.Debt), Fmt.Money(g.Capital), Fmt.Money(g.CostsPerTurn),
                $"{g.ActionsLeft}/{g.ActionsPerTurn}", Mathf.RoundToInt((float)g.Reputation).ToString(),
            };
            for (int i = 0; i < values.Length; i++)
                _statLabels[i].text = $"<color=#8a93a6>{_statLabels[i].name}</color> <b>{values[i]}</b>";
            _probesButton.text = g.Probes.Count > 0 ? $"Зонды ({g.Probes.Count} в полёте)" : "Зонды";
            _probesButton.EnableInClassList("selected", _probeMode);
        }

        /// <summary>Полоса сезонного события: что идёт, сколько ещё и чем грозит; в затишье — когда ждать следующего.</summary>
        void RefreshEventBar()
        {
            var g = _game;
            var ev = g.CurrentEvent;
            _eventBar.EnableInClassList("active", ev != null);
            _eventBar.text = ev == null
                ? $"Затишье. Следующее галактическое событие — не раньше хода {Mathf.Max(g.NextEventTurn, g.Turn + 1)}."
                : $"<b>{ev.Event.Name}</b> (ещё {ev.EndTurn - g.Turn + 1} х.): {ev.Event.Description} <color=#ffd27a>{EventEffects(ev.Event)}</color>";
        }

        // ------------------------------------------------------------------
        // Правая панель
        // ------------------------------------------------------------------

        void RefreshSidePanel()
        {
            PanelBuilder.Select(_tabs, (int)_tab);
            var key = (_tab, _selectedId);
            var offset = key == _panelFor ? _sideScroll.scrollOffset : Vector2.zero;
            _panelFor = key;
            _sideScroll.Clear();
            var ui = new PanelBuilder(_sideScroll.contentContainer, Later);
            switch (_tab)
            {
                case Tab.Planet: PlanetTab(ui); break;
                case Tab.Market: MarketTab(ui); break;
                case Tab.Orders: OrdersTab(ui); break;
                case Tab.Finance: FinanceTab(ui); break;
                case Tab.Team: TeamTab(ui); break;
                case Tab.Development: DevelopmentTab(ui); break;
                case Tab.Magnates: MagnatesTab(ui); break;
            }
            // Новое содержимое ещё не размечено: прокрутку возвращаем и сразу, и после разметки.
            _sideScroll.scrollOffset = offset;
            _sideScroll.schedule.Execute(() => _sideScroll.scrollOffset = offset);
        }

        // ------------------------------------------------------------------
        // Окно планеты
        // ------------------------------------------------------------------

        void CreatePortrait()
        {
            _portraitBox = new VisualElement { name = "portrait" };
            _portraitBox.AddToClassList("portrait");
            _portraitButton = PanelBuilder.MakeButton("К постройкам", () => _portrait.ToggleView());
            _portraitButton.AddToClassList("portrait-button");
            _portraitBox.Add(_portraitButton);

            // Тянуть мышью — вращать планету (облетать базу), колесо — ближе или дальше.
            _portraitBox.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0 || e.target != _portraitBox) return;
                _portraitBox.CapturePointer(e.pointerId);
                _portrait.BeginDrag();
                e.StopPropagation();
            });
            _portraitBox.RegisterCallback<PointerMoveEvent>(e =>
            {
                if (_portraitBox.HasPointerCapture(e.pointerId)) _portrait.Drag(e.deltaPosition);
            });
            _portraitBox.RegisterCallback<PointerUpEvent>(e =>
            {
                if (_portraitBox.HasPointerCapture(e.pointerId)) _portraitBox.ReleasePointer(e.pointerId);
            });
            // Отпустили мышь (или захват потерян) — планета дальше крутится по инерции.
            _portraitBox.RegisterCallback<PointerCaptureOutEvent>(_ => _portrait.EndDrag());
            // Колесо над окном приближает, а не листает панель.
            _portraitBox.RegisterCallback<WheelEvent>(e =>
            {
                _portrait.Zoom(e.delta.y * 3);
                e.StopPropagation();
            });
        }

        /// <summary>Кадр окна планеты: пока оно на экране, камера окна рисует выбранную планету.</summary>
        void ShowPortrait()
        {
            var p = Selected;
            var size = _portraitBox.layout.size;
            if (p == null || _portraitBox.panel == null || float.IsNaN(size.x) || size.x < 2 || size.y < 2) return;
            var texture = _portrait.Show(Describe(p), Structures(p), size * _pixelScale);
            if (texture != _portraitTexture)
            {
                _portraitTexture = texture;
                _portraitBox.style.backgroundImage = Background.FromRenderTexture(texture as RenderTexture);
            }
            _portraitButton.EnableInClassList("hidden", !_portrait.HasBuildings);
            _portraitButton.text = _portrait.CloseUp ? "Вся планета" : "К постройкам";
        }

        // ------------------------------------------------------------------
        // Ход соперников
        // ------------------------------------------------------------------

        void ShowRivals(bool shown)
        {
            _rivals.EnableInClassList("hidden", !shown);
            if (!shown) return;
            HideTooltip();
            _rivalsList.Clear();
            foreach (var m in _game.Magnates)
            {
                var state = new Label();
                state.AddToClassList("text");
                _rivalsList.Add(state);
                var said = _game.RecentRemarks.LastOrDefault(x => x.MagnateId == m.Id && x.Turn >= _game.Turn - 3);
                if (said != null)
                {
                    var l = new Label($"<i>«{said.Text}»</i>");
                    l.AddToClassList("small");
                    _rivalsList.Add(l);
                }
            }
            UpdateRivals();
        }

        /// <summary>Магнаты «ходят» по очереди; полоска — сколько осталось ждать.</summary>
        void UpdateRivals()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _rivalsTurnStarted) / RivalsTurnSeconds);
            _rivalsTitle.text = "Ходят соперники" + new string('.', 1 + (int)(Time.unscaledTime * 3) % 3);
            var magnates = _game.Magnates;
            int current = Mathf.Min(magnates.Count - 1, (int)(t * magnates.Count));
            var states = _rivalsList.Children().Where(c => c.ClassListContains("text")).Cast<Label>().ToList();
            for (int i = 0; i < magnates.Count && i < states.Count; i++)
            {
                var m = magnates[i];
                string state = _game.Turn < m.QuietUntilTurn ? "отбивается от слухов" : i < current ? "походил(а)" : i == current ? "думает" : "ждёт";
                states[i].text = $"{m.Name}, {m.PersonalityName}: {state}";
            }
            _rivalsProgress.style.width = Length.Percent(t * 100);
        }

        // ------------------------------------------------------------------
        // Подсказка у курсора
        // ------------------------------------------------------------------

        /// <summary>Подсказка рядом с курсором; at — точка в координатах экрана интерфейса.</summary>
        void ShowTooltip(string text, Vector2 at)
        {
            if (string.IsNullOrEmpty(text) || RivalsTurn)
            {
                HideTooltip();
                return;
            }
            _tooltip.text = text;
            _tooltip.RemoveFromClassList("hidden");
            // У правого края экрана подсказка уходит влево от курсора.
            float width = float.IsNaN(_tooltip.layout.width) ? 0 : _tooltip.layout.width;
            float x = at.x + 16;
            if (x + width > _screen.layout.width - 4) x = at.x - 12 - width;
            _tooltip.style.left = x;
            _tooltip.style.top = at.y + 12;
        }

        void HideTooltip() => _tooltip?.AddToClassList("hidden");
    }
}
