using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using PlanetTrader.Core;
using PlanetTrader.Visuals;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Экран на UI Toolkit: верхняя панель с показателями и вкладками, правая панель, нижние панели
    /// «Рынок», «События», «Действия в этом ходу», журнал, меню, пауза «ходят соперники» и окно планеты.
    /// Разметка — Resources/UI/Game.uxml, вид — Game.uss; значки — <see cref="Icons"/>, рамки — <see cref="Frames"/>.
    /// </summary>
    public sealed partial class PlanetTraderApp
    {
        // Интерфейс задан в точках для экрана 1600×900 и масштабируется под настоящий, не выходя за его края.
        static readonly Vector2Int ReferenceResolution = new Vector2Int(1600, 900);

        static readonly string[] TabIcons = { "planet", "chart", "clipboard", "bars", "person", "flask", "crown" };

        UIDocument _document;
        VisualElement _screen, _tabs, _rivals, _rivalsList, _rivalsProgress, _menu, _journal, _eventsList, _marketCards;
        ScrollView _sideScroll, _actionsList, _journalList;
        Label _turnValue, _tooltip, _rivalsTitle, _actionsCount;
        readonly Dictionary<string, Label> _statValues = new Dictionary<string, Label>();
        VisualElement _levelFill;

        /// <summary>Состояние игры изменилось — пересобрать панели в конце кадра.</summary>
        bool _dirty = true;
        /// <summary>Для какой вкладки и планеты собрана правая панель: сменились — прокрутка наверх.</summary>
        (Tab tab, int planet) _panelFor = ((Tab)(-1), -1);

        // Окно планеты: живёт всё время, при пересборке вкладки переставляется в новое место.
        VisualElement _portraitBox;
        Texture _portraitTexture;

        /// <summary>Запись журнала: ход, текст и цвет точки (good, bad, warn, info или пусто).</summary>
        sealed class LogEntry
        {
            public int Turn;
            public string Text, Kind;
        }

        readonly List<LogEntry> _logEntries = new List<LogEntry>();
        /// <summary>Сколько всего записей было в журнале — чтобы понять, что добавило действие.</summary>
        int _logSerial;
        /// <summary>Что сделано за этот ход: по строке на потраченное действие.</summary>
        readonly List<string> _turnActions = new List<string>();
        /// <summary>Цены на бирже по ходам — для графиков в панели «Рынок».</summary>
        readonly Dictionary<Resource, List<double>> _priceHistory = new Dictionary<Resource, List<double>>();
        const int PriceHistoryLength = 24;

        sealed class MarketCard
        {
            public Label Price, Change;
            public VisualElement ChangeBox, ChangeIcon;
            public Sparkline Line;
        }

        readonly Dictionary<Resource, MarketCard> _market = new Dictionary<Resource, MarketCard>();

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
            _tabs = root.Q("tabs");
            _sideScroll = root.Q<ScrollView>("sideScroll");
            _tooltip = root.Q<Label>("tooltip");
            _turnValue = root.Q<Label>("turnValue");
            _rivals = root.Q("rivals");
            _rivalsList = root.Q("rivalsList");
            _rivalsProgress = root.Q("rivalsProgress");
            _rivalsTitle = root.Q<Label>("rivalsTitle");
            _menu = root.Q("menu");
            _journal = root.Q("journal");
            _journalList = root.Q<ScrollView>("journalList");
            _eventsList = root.Q("eventsList");
            _actionsList = root.Q<ScrollView>("actionsList");
            _actionsCount = root.Q<Label>("actionsCount");
            _marketCards = root.Q("marketCards");

            root.Q<Button>("endTurnButton").clicked += () => Later(RequestEndTurn);
            var logo = root.Q<Button>("logoButton");
            logo.clicked += () => _menu.ToggleInClassList("hidden");
            root.Q<Button>("newGameButton").clicked += () =>
            {
                _menu.AddToClassList("hidden");
                Later(NewGame);
            };
            root.Q<Button>("showcaseButton").clicked += () =>
            {
                _menu.AddToClassList("hidden");
                Later(() => PlanetShowcase.Toggle(this));
            };
            root.Q<Button>("journalButton").clicked += OpenJournal;
            root.Q<Button>("journalClose").clicked += () => _journal.AddToClassList("hidden");
            // Клик мимо меню закрывает его; клик по тёмному фону журнала — закрывает журнал.
            _screen.RegisterCallback<PointerDownEvent>(e =>
            {
                if (_menu.ClassListContains("hidden")) return;
                if (e.target is VisualElement t && (_menu.Contains(t) || logo.Contains(t))) return;
                _menu.AddToClassList("hidden");
            }, TrickleDown.TrickleDown);
            _journal.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == _journal) _journal.AddToClassList("hidden");
            });

            var stats = root.Q("stats");
            AddStat(stats, "money", "coins", "Деньги");
            AddStat(stats, "debt", "coins", "Долг");
            AddStat(stats, "capital", "layers", "Капитал");
            AddLevelStat(stats);
            AddStat(stats, "costs", "flame", "Расходы");
            AddStat(stats, "actions", "bolt", "Действия");
            AddStat(stats, "reputation", "star", "Репутация");

            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var tab = new Button(() => Later(() => _tab = (Tab)index)) { focusable = false };
                tab.AddToClassList("tab");
                tab.Add(Icons.Make(TabIcons[i], "tab-icon"));
                tab.Add(new Label(TabNames[i]) { pickingMode = PickingMode.Ignore });
                _tabs.Add(tab);
            }

            foreach (var r in ResourceTypes.All)
                _marketCards.Add(MakeMarketCard(r));

            CreatePortrait();
            CreateMap(root);
            Icons.AttachAll(root);
            Frames.AttachAll(root);
        }

        void AddStat(VisualElement parent, string key, string icon, string name)
        {
            var stat = new VisualElement();
            stat.AddToClassList("stat");
            stat.AddToClassList(key);
            stat.Add(Icons.Make(icon, "stat-icon"));
            var text = new VisualElement();
            var label = new Label(name);
            label.AddToClassList("stat-label");
            var value = new Label();
            value.AddToClassList("stat-value");
            text.Add(label);
            text.Add(value);
            stat.Add(text);
            parent.Add(stat);
            _statValues[key] = value;
        }

        /// <summary>Уровень корпорации: название и полоска до следующего. Клик — вкладка «Развитие», где все уровни.</summary>
        void AddLevelStat(VisualElement parent)
        {
            AddStat(parent, "level", "rank", "Уровень");
            var stat = parent[parent.childCount - 1];
            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("level-bar");
            _levelFill = new VisualElement { pickingMode = PickingMode.Ignore };
            _levelFill.AddToClassList("level-fill");
            bar.Add(_levelFill);
            stat[1].Add(bar);
            stat.Query<VisualElement>().ForEach(e => { if (e != stat) e.pickingMode = PickingMode.Ignore; });
            stat.AddToClassList("clickable");
            stat.RegisterCallback<ClickEvent>(_ => Later(() => _tab = Tab.Development));
            stat.RegisterCallback<PointerMoveEvent>(e => ShowTooltip(LevelTooltip(), _screen.WorldToLocal(e.position)));
            stat.RegisterCallback<PointerLeaveEvent>(_ => HideTooltip());
        }

        string LevelTooltip()
        {
            var g = _game;
            var next = g.NextLevel;
            if (next == null) return $"Уровень «{g.LevelInfo.Name}» — высший.";
            return $"До уровня «{next.Name}»: капитал {Fmt.Money(g.Capital)} из {Fmt.Money(next.Capital)}.\nОткроет: {string.Join(", ", g.LevelUnlocks(g.Level + 1))}.";
        }

        static string ResourceIcon(Resource r)
        {
            switch (r)
            {
                case Resource.Ice: return "ice";
                case Resource.Metal: return "metal";
                case Resource.Gas: return "flame";
                case Resource.Biomass: return "leaf";
                case Resource.Crystals: return "gem";
                default: return "spark";
            }
        }

        VisualElement MakeMarketCard(Resource r)
        {
            var card = new VisualElement();
            card.AddToClassList("market-card");
            card.RegisterCallback<ClickEvent>(_ => Later(() => _tab = Tab.Market));
            var head = new VisualElement();
            head.AddToClassList("mc-head");
            var icon = Icons.Make(ResourceIcon(r), "mc-icon");
            // Цвет значка — цвет ресурса, такой же, как у планет на карте.
            icon.style.color = Fmt.ResourceColor(r);
            head.Add(icon);
            var name = new Label(r.RuName());
            name.AddToClassList("mc-name");
            head.Add(name);
            card.Add(head);
            var m = new MarketCard { Price = new Label(), ChangeBox = new VisualElement(), Change = new Label(), Line = new Sparkline() };
            m.Price.AddToClassList("mc-price");
            m.ChangeBox.AddToClassList("mc-change");
            m.Change.AddToClassList("label-text");
            m.ChangeIcon = Icons.Make("up");
            m.ChangeBox.Add(m.Change);
            m.ChangeBox.Add(m.ChangeIcon);
            card.Add(m.Price);
            card.Add(m.ChangeBox);
            card.Add(m.Line);
            _market[r] = m;
            return card;
        }

        void ShowScreen(bool shown) => _screen?.EnableInClassList("hidden", !shown);

        /// <summary>Новая партия: журнал и история цен — заново.</summary>
        void ResetScreen()
        {
            _logEntries.Clear();
            _turnActions.Clear();
            _priceHistory.Clear();
            RecordPrices();
            HideTooltip();
            ShowRivals(false);
            _journal.AddToClassList("hidden");
            ResetMap();
            MarkDirty();
        }

        /// <summary>Запомнить цены биржи этого хода.</summary>
        void RecordPrices()
        {
            foreach (var r in ResourceTypes.All)
            {
                if (!_priceHistory.TryGetValue(r, out var list)) _priceHistory[r] = list = new List<double>();
                list.Add(_game.Market.Price(r));
                if (list.Count > PriceHistoryLength) list.RemoveAt(0);
            }
        }

        void RefreshScreen()
        {
            _pixelScale = _screen.layout.width > 1 ? Screen.width / _screen.layout.width : 1;
            _mapCanvas.MarkDirtyRepaint();
            if (_mapDirty && !_dirty) RefreshMap();
            if (!_dirty) return;
            _dirty = false;
            RefreshTopBar();
            RefreshSidePanel();
            RefreshMap();
            RefreshMarket();
            RefreshEvents();
            RefreshActions();
        }

        // ------------------------------------------------------------------
        // Журнал
        // ------------------------------------------------------------------

        void Log(string message)
        {
            _logEntries.Insert(0, new LogEntry { Turn = _game?.Turn ?? 1, Text = message, Kind = EventKind(message) });
            _logSerial++;
            if (_logEntries.Count > MaxLogLines) _logEntries.RemoveAt(_logEntries.Count - 1);
            if (!_journal.ClassListContains("hidden")) _journalList.Insert(0, EventRow(_logEntries[0]));
            MarkDirty();
        }

        static readonly Regex Tags = new Regex("<[^>]+>");

        /// <summary>Цвет точки у записи: хорошее, плохое, предупреждение, сведения.</summary>
        static string EventKind(string text)
        {
            var t = Tags.Replace(text, "").ToLowerInvariant();
            if (t.StartsWith("нельзя") || t.Contains("банкрот") || t.Contains("забастов") || t.Contains("авари") || t.Contains("подкуп")
                || t.Contains("иск ") || t.Contains("коллектор") || t.Contains("штраф") || t.Contains("отказал") || t.Contains("перебил")
                || t.Contains("метеорит") || t.Contains("эпидеми") || t.Contains("бунт") || t.Contains("не хватило"))
                return "bad";
            if (t.Contains("куплен") || t.Contains("продан") || t.Contains("построен") || t.Contains("выполнен") || t.Contains("согласил")
                || t.Contains("выигран") || t.Contains("нанят") || t.Contains("отклонён") || t.Contains("сработал") || t.Contains("ушли от"))
                return "good";
            if (t.StartsWith("—") || t.Contains("событие") || t.Contains("картел") || t.Contains("показ")) return "warn";
            if (t.Contains("зонд") || t.Contains("разведк") || t.Contains("скан")) return "info";
            return "";
        }

        VisualElement EventRow(LogEntry e)
        {
            var row = new VisualElement();
            row.AddToClassList("event-row");
            var dot = new VisualElement();
            dot.AddToClassList("event-dot");
            if (e.Kind != "") dot.AddToClassList(e.Kind);
            var turn = new Label($"ход {e.Turn}");
            turn.AddToClassList("event-turn");
            var text = new Label(e.Text);
            text.AddToClassList("event-text");
            row.Add(dot);
            row.Add(turn);
            row.Add(text);
            return row;
        }

        void RefreshEvents()
        {
            _eventsList.Clear();
            foreach (var e in _logEntries.Take(4)) _eventsList.Add(EventRow(e));
        }

        void OpenJournal()
        {
            _journalList.Clear();
            foreach (var e in _logEntries) _journalList.Add(EventRow(e));
            _journal.RemoveFromClassList("hidden");
            _journalList.scrollOffset = Vector2.zero;
        }

        /// <summary>Действие игрока прошло: если на него ушли очки действий — записать в «Действия в этом ходу».</summary>
        void RecordAction(int actionsBefore, int logBefore)
        {
            if (_game.ActionsLeft >= actionsBefore) return;
            string text = _logSerial > logBefore ? Tags.Replace(_logEntries[0].Text, "") : "Действие";
            for (int i = _game.ActionsLeft; i < actionsBefore; i++) _turnActions.Add(text);
        }

        void RefreshActions()
        {
            var g = _game;
            _actionsCount.text = $"{g.ActionsPerTurn - g.ActionsLeft} / {g.ActionsPerTurn}";
            _actionsList.Clear();
            int total = Mathf.Max(g.ActionsPerTurn, _turnActions.Count);
            for (int i = 0; i < total; i++)
            {
                bool done = i < _turnActions.Count;
                var row = new VisualElement();
                row.AddToClassList("action-row");
                if (!done) row.AddToClassList("free");
                var num = new Label((i + 1).ToString());
                num.AddToClassList("action-num");
                var text = new Label(done ? _turnActions[i] : "— доступно действие");
                text.AddToClassList("action-text");
                var check = new VisualElement();
                check.AddToClassList("action-check");
                if (done)
                {
                    check.AddToClassList("done");
                    check.Add(Icons.Make("check"));
                }
                row.Add(num);
                row.Add(text);
                row.Add(check);
                _actionsList.Add(row);
            }
        }

        // ------------------------------------------------------------------
        // Верхняя панель и рынок
        // ------------------------------------------------------------------

        void RefreshTopBar()
        {
            var g = _game;
            _turnValue.text = g.Turn.ToString();
            _statValues["money"].text = Fmt.Money(g.Money);
            _statValues["debt"].text = Fmt.Money(g.Debt);
            _statValues["capital"].text = Fmt.Money(g.Capital);
            _statValues["level"].text = g.LevelInfo.Name;
            _levelFill.style.width = Length.Percent(LevelProgress() * 100);
            _statValues["costs"].text = $"{Fmt.Money(g.CostsPerTurn)} / ход";
            _statValues["actions"].text = $"{g.ActionsLeft}/{g.ActionsPerTurn}";
            _statValues["reputation"].text = Mathf.RoundToInt((float)g.Reputation).ToString();
            for (int i = 0; i < _tabs.childCount; i++)
                _tabs[i].EnableInClassList("selected", i == (int)_tab);
        }

        void RefreshMarket()
        {
            var g = _game;
            foreach (var r in ResourceTypes.All)
            {
                var m = _market[r];
                double price = g.Market.Price(r);
                double toBase = price / g.Balance.BasePrice(r) - 1;
                m.Price.text = Fmt.Money(price);
                string kind = toBase > 0.005 ? "up" : toBase < -0.005 ? "down" : "flat";
                m.ChangeBox.EnableInClassList("up", kind == "up");
                m.ChangeBox.EnableInClassList("down", kind == "down");
                m.ChangeBox.EnableInClassList("flat", kind == "flat");
                m.Change.text = (toBase >= 0 ? "+" : "") + Fmt.Percent(toBase);
                Icons.Change(m.ChangeIcon, kind == "down" ? "down" : "up");
                m.ChangeIcon.EnableInClassList("hidden", kind == "flat");
                // График — прошлые ходы и цена прямо сейчас (её двигают продажи этого хода).
                var values = new List<double>(_priceHistory[r]) { price };
                m.Line.Show(values, kind == "down" ? new Color(1f, 0.36f, 0.31f) : new Color(0.35f, 0.84f, 0.55f));
            }
        }

        // ------------------------------------------------------------------
        // Правая панель
        // ------------------------------------------------------------------

        void RefreshSidePanel()
        {
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

            // Тянуть мышью — вращать планету (облетать базу), колесо — ближе или дальше.
            _portraitBox.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
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
