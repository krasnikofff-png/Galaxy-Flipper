using System;
using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;
using UnityEngine;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Прототип интерфейса на IMGUI: карта сектора с разведкой, окно планеты, биржа, заказы, финансы и журнал.
    /// Вся экономика — в PlanetTrader.Core, здесь только показ и кнопки.
    /// </summary>
    public sealed class PlanetTraderApp : MonoBehaviour
    {
        // Интерфейс рисуется в виртуальных координатах и масштабируется под экран.
        const float RefWidth = 1600, RefHeight = 900;
        const float SidePanelWidth = 560, Gap = 10, TopBarHeight = 52, LogHeight = 190;
        const int MaxLogLines = 200;

        enum Tab { Planet, Market, Orders, Finance }
        static readonly string[] TabNames = { "Планета", "Биржа", "Заказы", "Финансы" };

        Core.Game _game;
        int _selectedId = -1;
        Tab _tab = Tab.Planet;
        readonly List<string> _log = new List<string>();
        Vector2 _logScroll, _panelScroll;
        Texture2D _circle, _ring;

        // Режим зонда: игрок выбирает тип и задание, потом кликает по карте.
        bool _probeMode;
        int _probeTypeIndex = 1;
        ProbeMission _probeMission = ProbeMission.ScanArea;
        /// <summary>Квадрат карты на экране: координаты 0..1 планет растягиваются на него.</summary>
        Rect _mapArea;

        GUIStyle _text, _small, _title, _button, _mapLabel, _logText, _header;
        bool _stylesReady;

        /// <summary>
        /// Изменения от кнопок выполняются после кадра интерфейса. Если менять состояние прямо в OnGUI,
        /// набор элементов между проходами Layout и Repaint разойдётся, и IMGUI бросит исключение.
        /// </summary>
        readonly List<Action> _pending = new List<Action>();

        void Later(Action action) => _pending.Add(action);

        /// <summary>Действие игрока после кадра: при успехе — запись в журнал, при отказе — причина.</summary>
        void Act(Func<ActionResult> action, string success) => Later(() =>
        {
            if (Do(action())) Log(success);
        });

        void Update()
        {
            if (_pending.Count > 0)
            {
                var actions = _pending.ToArray();
                _pending.Clear();
                foreach (var a in actions) a();
            }
            // Сообщения ядра о том, что случилось во время действий (руины при покупке и т. п.).
            if (_game.Notices.Count > 0)
            {
                foreach (var n in _game.Notices) Log(n);
                _game.Notices.Clear();
            }
        }

        void Awake()
        {
            _circle = MakeCircle(64);
            _ring = MakeRing(256, 3);
            NewGame();
        }

        void NewGame()
        {
            var seed = (ulong)DateTime.Now.Ticks;
            _game = Core.Game.New(new Balance(), seed);
            _selectedId = _game.PlayerPlanets.First().Id;
            _tab = Tab.Planet;
            _log.Clear();
            var b = _game.Balance;
            Log($"Новая партия. У вас ледяной карлик с шахтой, {Fmt.Money(b.StartMoney)} денег и долг {Fmt.Money(b.StartDebt)}.");
            Log($"За ход можно сделать {_game.ActionsPerTurn} действия: купить, построить шахту, продать планету, запустить зонд, нанять менеджера.");
            Log("Продажа ресурсов, заказы и кредит действий не тратят. Пробел или Enter — конец хода.");
            Log("Серые планеты — силуэты: тип и запас неизвестны. Кнопка «Зонды» — скан области или полная разведка планеты.");
        }

        void Log(string message)
        {
            _log.Insert(0, message);
            if (_log.Count > MaxLogLines) _log.RemoveAt(_log.Count - 1);
        }

        /// <summary>Пишет ошибку действия в журнал. Успешные действия описывают вызывающие.</summary>
        bool Do(ActionResult result)
        {
            if (!result.Ok) Log("Нельзя: " + result.Error);
            return result.Ok;
        }

        Planet Selected => _selectedId < 0 ? null : _game.GetPlanet(_selectedId);

        // ------------------------------------------------------------------
        // Кадр
        // ------------------------------------------------------------------

        void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight);
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1));
            float width = Screen.width / scale, height = Screen.height / scale;

            HandleKeys();

            float bodyTop = TopBarHeight + Gap;
            float bodyHeight = height - bodyTop - LogHeight - 2 * Gap;
            float mapWidth = width - SidePanelWidth - 3 * Gap;

            DrawTopBar(new Rect(0, 0, width, TopBarHeight));
            DrawMap(new Rect(Gap, bodyTop, mapWidth, bodyHeight));
            DrawSidePanel(new Rect(2 * Gap + mapWidth, bodyTop, SidePanelWidth, bodyHeight));
            DrawLog(new Rect(Gap, height - LogHeight - Gap, width - 2 * Gap, LogHeight));
            DrawTooltip();
        }

        void HandleKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                Later(EndTurn);
                e.Use();
            }
            else if (e.keyCode == KeyCode.Escape && _probeMode)
            {
                Later(() => _probeMode = false);
                e.Use();
            }
        }

        void EndTurn()
        {
            var report = _game.EndTurn();
            string line = $"— Ход {report.Turn} завершён. Расходы {Fmt.Money(report.TotalCosts)}";
            if (report.InsurancePayout > 0) line += $", страховка выплатила {Fmt.Money(report.InsurancePayout)}";
            Log(line + ".");
            foreach (var e in report.Events) Log(e);
            if (report.Seizures > 0) Log("Денег не хватило: недостача ушла в долг, а долг превысил кредитный лимит.");
        }

        // ------------------------------------------------------------------
        // Верхняя панель
        // ------------------------------------------------------------------

        void DrawTopBar(Rect r)
        {
            GUI.Box(r, GUIContent.none);
            var g = _game;
            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 12));
            GUILayout.BeginHorizontal();
            Stat("Ход", g.Turn.ToString());
            Stat("Деньги", Fmt.Money(g.Money));
            Stat("Долг", Fmt.Money(g.Debt));
            Stat("Капитал", Fmt.Money(g.Capital));
            Stat("Расходы/ход", Fmt.Money(g.CostsPerTurn));
            Stat("Действия", $"{g.ActionsLeft}/{g.ActionsPerTurn}");
            GUILayout.FlexibleSpace();

            string probesLabel = g.Probes.Count > 0 ? $"Зонды ({g.Probes.Count} в полёте)" : "Зонды";
            bool mode = GUILayout.Toggle(_probeMode, probesLabel, _button, GUILayout.Height(34));
            if (mode != _probeMode) Later(() => _probeMode = mode);
            if (GUILayout.Button("Конец хода ▶", _button, GUILayout.Width(150), GUILayout.Height(34)))
                Later(EndTurn);
            if (GUILayout.Button("Новая игра", _button, GUILayout.Width(120), GUILayout.Height(34)))
                Later(NewGame);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void Stat(string name, string value)
        {
            GUILayout.Label($"<color=#8a93a6>{name}</color> <b>{value}</b>", _text, GUILayout.ExpandWidth(false));
            GUILayout.Space(22);
        }

        // ------------------------------------------------------------------
        // Карта
        // ------------------------------------------------------------------

        Vector2 ToScreen(double x, double y) => new Vector2(_mapArea.x + (float)x * _mapArea.width, _mapArea.y + (float)y * _mapArea.height);

        Vector2 ToMap(Vector2 screen) => new Vector2((screen.x - _mapArea.x) / _mapArea.width, (screen.y - _mapArea.y) / _mapArea.height);

        ProbeType SelectedProbe => _game.Balance.ProbeTypes[Mathf.Clamp(_probeTypeIndex, 0, _game.Balance.ProbeTypes.Length - 1)];

        void DrawMap(Rect r)
        {
            GUI.Box(r, GUIContent.none);
            var g = _game;
            var b = g.Balance;
            // Карта квадратная, чтобы расстояния и радиусы на экране не искажались.
            float top = _probeMode ? 48 : 16;
            float side = Mathf.Min(r.width - 60, r.height - top - 46);
            _mapArea = new Rect(r.x + (r.width - side) / 2, r.y + top + 6, side, side);

            if (_probeMode)
            {
                DrawProbeToolbar(new Rect(r.x + 12, r.y + 8, r.width - 24, 34));
                // Дальность выбранного зонда от каждой точки запуска.
                var probe = SelectedProbe;
                DrawRing(new Vector2((float)b.ExchangeX, (float)b.ExchangeY), probe.Range, new Color(0.4f, 0.7f, 1f, 0.25f));
                foreach (var own in g.PlayerPlanets)
                    DrawRing(new Vector2((float)own.MapX, (float)own.MapY), probe.Range, new Color(0.4f, 0.7f, 1f, 0.25f));
            }

            // Биржа.
            var ex = ToScreen(b.ExchangeX, b.ExchangeY);
            var old = GUI.color;
            GUI.color = new Color(0.95f, 0.85f, 0.4f);
            GUI.DrawTexture(new Rect(ex.x - 9, ex.y - 9, 18, 18), Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(ex.x - 60, ex.y + 11, 120, 20), "Биржа", _mapLabel);

            // Зонды в полёте: пунктир до цели и точка там, где зонд сейчас.
            foreach (var pr in g.Probes)
            {
                var from = ToScreen(pr.FromX, pr.FromY);
                var to = ToScreen(pr.TargetX, pr.TargetY);
                for (float t = 0; t <= 1; t += 0.05f) DrawCircle(Vector2.Lerp(from, to, t), 1.5f, new Color(0.6f, 0.8f, 1f, 0.5f));
                DrawCircle(Vector2.Lerp(from, to, (float)pr.Progress(g.Turn)), 5, new Color(0.6f, 0.9f, 1f));
                if (pr.Mission == ProbeMission.ScanArea)
                    DrawRing(new Vector2((float)pr.TargetX, (float)pr.TargetY), pr.Type.ScanRadius, new Color(0.6f, 0.9f, 1f, 0.6f));
                int left = pr.ResolveTurn - g.Turn + 1;
                GUI.Label(new Rect(to.x - 60, to.y - 26, 120, 20), $"зонд: {left} х.", _mapLabel);
            }

            foreach (var p in g.AllPlanets)
            {
                var center = ToScreen(p.MapX, p.MapY);
                float radius = PlanetRadius(p);

                if (p.Id == _selectedId) DrawCircle(center, radius + 9, Color.white);
                DrawCircle(center, radius + 4, Fmt.OwnerColor(p.Owner, p.FoundByProbe));
                var body = p.Knowledge == Knowledge.Silhouette ? new Color(0.35f, 0.37f, 0.42f) : Fmt.ResourceColor(p.Resource);
                if (p.IsDepleted) body = Color.Lerp(body, new Color(0.15f, 0.15f, 0.18f), 0.75f);
                DrawCircle(center, radius, body);
                if (p.Knowledge == Knowledge.Explored && p.Owner != Owner.Player)
                    DrawCircle(center, 3, Color.white);

                string label = MapLabel(p);
                if (label != null)
                    GUI.Label(new Rect(center.x - 70, center.y + radius + 5, 140, 20), label, _mapLabel);

                var hit = new Rect(center.x - radius - 6, center.y - radius - 6, 2 * radius + 12, 2 * radius + 12);
                if (GUI.Button(hit, new GUIContent("", PlanetTooltip(p)), GUIStyle.none))
                {
                    int id = p.Id;
                    if (_probeMode && _probeMission == ProbeMission.Explore)
                        LaunchExplore(id);
                    else if (_probeMode)
                        LaunchScan(p.MapX, p.MapY);
                    else
                        Later(() =>
                        {
                            _selectedId = id;
                            _tab = Tab.Planet;
                        });
                }
            }

            if (_probeMode) HandleProbeCursor();
            DrawLegend(new Rect(r.x + 14, r.yMax - 32, r.width - 28, 24));
        }

        string PlanetTooltip(Planet p)
        {
            string name = p.Knowledge == Knowledge.Silhouette ? $"Силуэт #{p.Id}" : $"{p.Type.Name} #{p.Id}, {p.Resource.RuName()}";
            string tip = $"{name}\n{Fmt.OwnerName(p.Owner, p.FoundByProbe)}";
            if (_probeMode && _probeMission == ProbeMission.Explore && p.Knowledge != Knowledge.Explored && p.Owner != Owner.Player)
                tip += "\n" + ProbeHint(p.MapX, p.MapY, ProbeMission.Explore);
            return tip;
        }

        /// <summary>Строка о зонде к точке: долетит ли, за сколько ходов и за сколько денег.</summary>
        string ProbeHint(double x, double y, ProbeMission mission)
        {
            var probe = SelectedProbe;
            if (!_game.InRange(probe, x, y)) return $"«{probe.Name}» не долетит";
            int turns = _game.ProbeTurns(probe, x, y, mission);
            return $"«{probe.Name}»: {Fmt.Money(probe.Cost)}, результат через {turns} х.";
        }

        void DrawProbeToolbar(Rect r)
        {
            GUILayout.BeginArea(r);
            GUILayout.BeginHorizontal();
            var types = _game.Balance.ProbeTypes;
            var names = types.Select(t => $"{t.Name} · {Fmt.Money(t.Cost)}").ToArray();
            int index = GUILayout.Toolbar(_probeTypeIndex, names, _button, GUILayout.Height(30), GUILayout.Width(420));
            if (index != _probeTypeIndex) Later(() => _probeTypeIndex = index);
            GUILayout.Space(12);
            int mission = GUILayout.Toolbar((int)_probeMission, new[] { "Скан области", "Разведка планеты" }, _button, GUILayout.Height(30), GUILayout.Width(300));
            if (mission != (int)_probeMission) Later(() => _probeMission = (ProbeMission)mission);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Готово", _button, GUILayout.Height(30), GUILayout.Width(90))) Later(() => _probeMode = false);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        /// <summary>В режиме скана под курсором — круг скана; клик по пустому месту запускает зонд.</summary>
        void HandleProbeCursor()
        {
            var e = Event.current;
            var mouse = e.mousePosition;
            if (!_mapArea.Contains(mouse) || _probeMission != ProbeMission.ScanArea) return;
            var point = ToMap(mouse);
            var probe = SelectedProbe;
            bool reach = _game.InRange(probe, point.x, point.y);
            DrawRing(point, probe.ScanRadius, reach ? new Color(0.5f, 1f, 0.6f, 0.8f) : new Color(1f, 0.4f, 0.4f, 0.8f));
            GUI.Label(new Rect(mouse.x + 14, mouse.y - 22, 320, 20), ProbeHint(point.x, point.y, ProbeMission.ScanArea), _small);
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                LaunchScan(point.x, point.y);
                e.Use();
            }
        }

        void LaunchScan(double x, double y)
        {
            var probe = SelectedProbe;
            Act(() => _game.LaunchScan(probe, x, y), $"Зонд «{probe.Name}» отправлен сканировать область.");
        }

        void LaunchExplore(int planetId)
        {
            var probe = SelectedProbe;
            Act(() => _game.LaunchExplore(probe, planetId), $"Зонд «{probe.Name}» отправлен на разведку планеты #{planetId}.");
        }

        /// <summary>Кольцо радиусом radius в координатах карты.</summary>
        void DrawRing(Vector2 mapCenter, double radius, Color color)
        {
            var c = ToScreen(mapCenter.x, mapCenter.y);
            float w = (float)radius * _mapArea.width, h = (float)radius * _mapArea.height;
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(c.x - w, c.y - h, 2 * w, 2 * h), _ring);
            GUI.color = old;
        }

        static float PlanetRadius(Planet p)
        {
            // Чем дороже тип планеты, тем она крупнее на карте.
            return 11 + Mathf.Clamp((float)(p.Type.Price - 1600) / 2600f, 0, 1) * 10;
        }

        string MapLabel(Planet p)
        {
            string marks = "";
            if (p.Knowledge == Knowledge.Explored && p.HasUnlootedRuins) marks += " · руины";
            if (p.Knowledge == Knowledge.Explored && p.Inhabited) marks += " · жители";
            switch (p.Owner)
            {
                case Owner.Player: return (p.IsDepleted ? "ВЫ · пусто" : "ВЫ") + marks;
                case Owner.Ai: return "ИИ";
                default: return p.IsDepleted ? null : Fmt.Money(p.ListPrice(_game.Balance)) + marks;
            }
        }

        void DrawLegend(Rect r)
        {
            float x = r.x;
            x = LegendItem(x, r.y, Fmt.OwnerColor(Owner.Player, false), "ваши");
            x = LegendItem(x, r.y, Fmt.OwnerColor(Owner.Ai, false), "ИИ");
            x = LegendItem(x, r.y, Fmt.OwnerColor(Owner.Registry, false), "аукцион (цена)");
            x = LegendItem(x, r.y, Fmt.OwnerColor(Owner.Registry, true), "нашёл ваш зонд");
            x = LegendItem(x, r.y, new Color(0.35f, 0.37f, 0.42f), "силуэт");
            x += 20;
            foreach (var res in ResourceTypes.All)
                x = LegendItem(x, r.y, Fmt.ResourceColor(res), res.RuName());
        }

        float LegendItem(float x, float y, Color color, string text)
        {
            DrawCircle(new Vector2(x + 7, y + 11), 6, color);
            var size = _small.CalcSize(new GUIContent(text));
            GUI.Label(new Rect(x + 17, y + 1, size.x + 4, 22), text, _small);
            return x + 17 + size.x + 16;
        }

        void DrawCircle(Vector2 center, float radius, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, 2 * radius, 2 * radius), _circle);
            GUI.color = old;
        }

        // ------------------------------------------------------------------
        // Правая панель
        // ------------------------------------------------------------------

        void DrawSidePanel(Rect r)
        {
            GUI.Box(r, GUIContent.none);
            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 10, r.width - 24, r.height - 20));
            var tab = (Tab)GUILayout.Toolbar((int)_tab, TabNames, _button, GUILayout.Height(34));
            if (tab != _tab) Later(() => _tab = tab);
            GUILayout.Space(10);
            _panelScroll = GUILayout.BeginScrollView(_panelScroll);
            switch (_tab)
            {
                case Tab.Planet: DrawPlanetTab(); break;
                case Tab.Market: DrawMarketTab(); break;
                case Tab.Orders: DrawOrdersTab(); break;
                case Tab.Finance: DrawFinanceTab(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void Line(string name, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, _text, GUILayout.Width(200));
            GUILayout.Label(value, _text);
            GUILayout.EndHorizontal();
        }

        bool ActionButton(string text, bool enabled)
        {
            GUI.enabled = enabled;
            bool pressed = GUILayout.Button(text, _button, GUILayout.Height(36));
            GUI.enabled = true;
            return pressed;
        }

        void DrawPlanetTab()
        {
            var p = Selected;
            if (p == null)
            {
                GUILayout.Label("Выберите планету на карте.", _text);
                return;
            }

            var g = _game;
            var b = g.Balance;
            bool silhouette = p.Knowledge == Knowledge.Silhouette;
            bool exact = p.Owner == Owner.Player || p.Knowledge == Knowledge.Explored;

            GUILayout.Label(silhouette ? $"Силуэт #{p.Id}" : $"{p.Type.Name} #{p.Id}", _title);
            Line("Владелец", Fmt.OwnerName(p.Owner, p.FoundByProbe));
            Line("Знание", silhouette ? "силуэт: тип и запас неизвестны" : exact ? "полная разведка" : "скан: тип и оценка реестра");
            Line("До биржи", $"{Fmt.Amount(g.DistanceToExchange(p) * 10)} св. лет, груз идёт {g.ShipTurns(p)} х.");

            if (!silhouette)
            {
                Line("Ресурс", $"{p.Resource.RuName()}, на бирже сейчас {Fmt.Money(g.Market.Price(p.Resource))} за ед.");
                if (exact)
                {
                    Line("Запас", $"{Fmt.Amount(p.Reserve)} из {Fmt.Amount(p.InitialReserve)}");
                    DrawBar(p.InitialReserve > 0 ? (float)(p.Reserve / p.InitialReserve) : 0);
                }
                else
                    Line("Запас", $"≈ {Fmt.Amount(p.EstimatedReserve)} по оценке реестра (может врать)");
                double known = g.KnownReserve(p) ?? 0;
                Line("Добыча", $"{Fmt.Amount(p.Type.OutputPerTurn)} за ход, хватит на {(exact ? "" : "≈ ")}{Fmt.Amount(known / p.Type.OutputPerTurn)} х.");
                Line("Шахта", Fmt.MineName(p));
                if (p.Owner == Owner.Player)
                    Line("Содержание", $"{Fmt.Money(g.PlanetUpkeep(p))} за ход ({Fmt.Multiplier(g.UpkeepMultiplierNow)} за число планет)");
                else
                    Line("Содержание", $"{Fmt.Money(p.Type.Upkeep)} за ход, без множителя");
                Line(exact ? "Рыночная стоимость" : "Стоимость по оценке", Fmt.Money(g.KnownValue(p) ?? 0));
                if (p.Knowledge == Knowledge.Explored || p.Owner == Owner.Player)
                {
                    var surprises = new List<string>();
                    if (p.HasUnlootedRuins) surprises.Add($"руины с артефактами на {Fmt.Money(p.RuinsValue)}");
                    if (p.RuinsLooted) surprises.Add("руины разграблены");
                    if (p.Inhabited) surprises.Add("жители (появятся на этапе 4)");
                    if (p.Reserve < 0.6 * p.EstimatedReserve) surprises.Add("подвох: реестр завысил запас");
                    Line("Сюрпризы", surprises.Count == 0 ? "нет" : string.Join(", ", surprises));
                }
            }
            GUILayout.Space(12);

            switch (p.Owner)
            {
                case Owner.Registry:
                    if (p.IsDepleted)
                    {
                        GUILayout.Label("Планета выработана, покупать её незачем.", _text);
                        break;
                    }
                    double price = p.ListPrice(b);
                    string note = p.FoundByProbe ? "без надбавки: её нашёл ваш зонд" : $"с надбавкой аукциона {Fmt.Percent(b.AuctionMarkup)}";
                    if (p.Mine == MineState.Working) note += ", шахта уже стоит";
                    GUILayout.Label($"Цена {Fmt.Money(price)} ({note}). Реестр считает цену по своей оценке запаса.", _text);
                    if (p.Mine == MineState.None)
                        GUILayout.Label($"Потом шахта: {Fmt.Money(p.Type.MineCost)} и {b.MineBuildTurns} хода постройки.", _small);
                    string blind = silhouette ? " вслепую" : exact ? "" : " по оценке";
                    if (ActionButton($"Купить{blind} за {Fmt.Money(price)} · 1 действие", g.ActionsLeft > 0 && g.Money >= price))
                        Act(() => g.BuyPlanet(p.Id), $"Куплена планета {p.Type.Name} #{p.Id} за {Fmt.Money(price)}.");
                    DrawExploreButtons(p);
                    break;

                case Owner.Player:
                    if (p.Mine == MineState.None && !p.IsDepleted)
                    {
                        if (ActionButton($"Построить шахту за {Fmt.Money(p.Type.MineCost)} · 1 действие, {b.MineBuildTurns} хода",
                                g.ActionsLeft > 0 && g.Money >= p.Type.MineCost))
                            Act(() => g.BuildMine(p.Id), $"Начата постройка шахты на {p.Type.Name} #{p.Id}.");
                    }
                    if (p.IsDepleted)
                        GUILayout.Label("Планета выработана, но содержание за неё идёт. Продайте её, чтобы от него избавиться.", _text);
                    double sale = g.SalePrice(p);
                    if (ActionButton($"Продать за {Fmt.Money(sale)} (комиссия {Fmt.Percent(b.ResaleCommission)}) · 1 действие", g.ActionsLeft > 0))
                        Act(() => g.SellPlanet(p.Id), $"Продана планета {p.Type.Name} #{p.Id} за {Fmt.Money(sale)}.");
                    break;

                case Owner.Ai:
                    GUILayout.Label("Планета ИИ-магната. Купить её можно, когда он выставит её на аукцион.", _text);
                    DrawExploreButtons(p);
                    break;
            }
        }

        /// <summary>Кнопки полной разведки — по одной на тип зонда, с ценой и сроком.</summary>
        void DrawExploreButtons(Planet p)
        {
            if (p.Knowledge == Knowledge.Explored) return;
            var g = _game;
            GUILayout.Space(8);
            if (g.ProbeHeadingTo(p.Id))
            {
                GUILayout.Label("К планете уже летит зонд.", _small);
                return;
            }
            GUILayout.Label("Полная разведка покажет точный запас, подвохи и руины:", _small);
            foreach (var t in g.Balance.ProbeTypes)
            {
                bool reach = g.InRange(t, p.MapX, p.MapY);
                string text = reach
                    ? $"Разведать зондом «{t.Name}» за {Fmt.Money(t.Cost)} · {g.ProbeTurns(t, p.MapX, p.MapY, ProbeMission.Explore)} х., 1 действие"
                    : $"«{t.Name}» не долетит";
                var probe = t;
                int id = p.Id;
                if (ActionButton(text, reach && g.ActionsLeft > 0 && g.Money >= t.Cost))
                    Act(() => g.LaunchExplore(probe, id), $"Зонд «{probe.Name}» отправлен на разведку планеты #{id}.");
            }
        }

        void DrawBar(float share)
        {
            var r = GUILayoutUtility.GetRect(100, 10, GUILayout.ExpandWidth(true));
            r.x += 200;
            r.width -= 200;
            var old = GUI.color;
            GUI.color = new Color(1, 1, 1, 0.15f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(1f, 0.4f, 0.3f), new Color(0.4f, 0.9f, 0.5f), share);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(share), r.height), Texture2D.whiteTexture);
            GUI.color = old;
            GUILayout.Space(4);
        }

        void DrawMarketTab()
        {
            var g = _game;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Ресурс", _header, GUILayout.Width(110));
            GUILayout.Label("Склад", _header, GUILayout.Width(90));
            GUILayout.Label("Цена", _header, GUILayout.Width(110));
            GUILayout.Label("Продать", _header);
            GUILayout.EndHorizontal();

            foreach (var r in ResourceTypes.All)
            {
                double stock = g.Stock(r);
                double price = g.Market.Price(r);
                double toBase = price / g.Balance.BasePrice(r) - 1;
                string trend = toBase >= 0 ? $"<color=#7be38b>+{Fmt.Percent(toBase)}</color>" : $"<color=#ff8a7a>{Fmt.Percent(toBase)}</color>";

                GUILayout.BeginHorizontal();
                GUILayout.Label(r.RuName(), _text, GUILayout.Width(110));
                double transit = g.InTransit(r);
                GUILayout.Label(transit > 0 ? $"{Fmt.Amount(stock)} <color=#8a93a6>+{Fmt.Amount(transit)}</color>" : Fmt.Amount(stock), _text, GUILayout.Width(90));
                GUILayout.Label($"{Fmt.Money(price)} {trend}", _text, GUILayout.Width(110));

                double half = Math.Floor(stock / 2);
                GUI.enabled = half >= 1;
                if (GUILayout.Button($"½ ≈ {Fmt.Money(g.Market.Quote(r, half))}", _button, GUILayout.Width(120), GUILayout.Height(30)))
                    Act(() => g.SellResource(r, half), $"Продано {Fmt.Amount(half)} ({r.RuName()}).");
                GUI.enabled = stock > 0;
                double revenue = g.Market.Quote(r, stock);
                if (GUILayout.Button($"всё ≈ {Fmt.Money(revenue)}", _button, GUILayout.Width(130), GUILayout.Height(30)))
                    Act(() => g.SellResource(r, stock), $"Продано {Fmt.Amount(stock)} ({r.RuName()}) за {Fmt.Money(revenue)}.");
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(12);
            var b = g.Balance;
            GUILayout.Label(
                $"Каждая продажа роняет цену: на {Fmt.Percent(b.SalesImpact)}, если продать столько, сколько составляет глубина рынка. " +
                $"За ход цена возвращается к базовой на {Fmt.Percent(b.PriceRecovery)} разрыва. " +
                "Глубина: " + string.Join(", ", ResourceTypes.All.Select(r => $"{r.RuName()} {Fmt.Amount(b.Depth(r))}")) + ". " +
                "Серым после склада — груз в пути с дальних планет.",
                _small);
        }

        void DrawOrdersTab()
        {
            var g = _game;
            if (g.Orders.Count == 0)
            {
                GUILayout.Label("Заказов пока нет. Новые появляются примерно каждые два хода.", _text);
                return;
            }

            foreach (var o in g.Orders.ToList())
            {
                double have = g.Stock(o.Resource);
                int turnsLeft = o.DeadlineTurn - g.Turn;
                bool reachable = g.OrderReachable(o);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"<b>{o.Customer}</b>: {Fmt.Amount(o.Amount)} ({o.Resource.RuName()})", _text);
                GUILayout.Label($"Срок через {turnsLeft} х., доставка {g.DeliveryTurns(o)} х. за {Fmt.Money(g.DeliveryCost(o))}. " +
                                $"Заплатят ≈ {Fmt.Money(o.GrossPayout(g.Market, g.Balance))} (×{g.Balance.OrderMarkup} к бирже), " +
                                $"на руки ≈ {Fmt.Money(g.OrderNetPayout(o))}. На складе {Fmt.Amount(have)}.", _small);
                if (ActionButton(reachable ? "Отправить" : "Не успеть к сроку", reachable && have >= o.Amount))
                {
                    var order = o;
                    Later(() =>
                    {
                        double payout = g.OrderNetPayout(order);
                        if (Do(g.FulfillOrder(order.Id)))
                            Log($"Заказ отправлен: {order.Customer} заплатил {Fmt.Money(payout)} за вычетом доставки.");
                    });
                }
                GUILayout.EndVertical();
                GUILayout.Space(6);
            }
        }

        void DrawFinanceTab()
        {
            var g = _game;
            var b = g.Balance;

            GUILayout.Label("Кредит", _header);
            Line("Долг", $"{Fmt.Money(g.Debt)}, {Fmt.Percent(b.InterestPerTurn)} за ход");
            Line("Лимит", $"{Fmt.Money(g.CreditLimit)} ({Fmt.Percent(b.CreditLimitShare)} стоимости планет)");
            Line("Можно занять", Fmt.Money(g.AvailableCredit));
            GUILayout.BeginHorizontal();
            if (ActionButton("Взять 500", g.AvailableCredit >= 500)) Act(() => g.TakeLoan(500), "Взято в долг 500.");
            double all = Math.Floor(g.AvailableCredit);
            if (ActionButton($"Взять {Fmt.Money(all)}", all >= 1)) Act(() => g.TakeLoan(all), $"Взято в долг {Fmt.Money(all)}.");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            double repay = Math.Min(500, g.Debt);
            if (ActionButton("Погасить 500", g.Debt > 0 && g.Money >= repay)) Act(() => g.RepayLoan(repay), $"Погашено {Fmt.Money(repay)}.");
            double repayAll = Math.Min(g.Debt, Math.Floor(g.Money));
            if (ActionButton($"Погасить {Fmt.Money(repayAll)}", repayAll >= 1)) Act(() => g.RepayLoan(repayAll), $"Погашено {Fmt.Money(repayAll)}.");
            GUILayout.EndHorizontal();
            GUILayout.Label("Если денег на расходы не хватит, недостача уйдёт в долг. Когда долг выше лимита, коллекторы забирают самые дорогие планеты.", _small);

            GUILayout.Space(14);
            GUILayout.Label("Страховка", _header);
            double insurance = b.InsuranceRate * g.PlanetsValue;
            bool insured = GUILayout.Toggle(g.Insured,
                $" Страховать планеты: {Fmt.Money(insurance)} за ход ({Fmt.Percent(b.InsuranceRate)} стоимости). Возмещает добычу во время аварий.", _text);
            if (insured != g.Insured)
                Later(() =>
                {
                    g.SetInsurance(insured);
                    Log(insured ? "Страховка включена." : "Страховка отключена.");
                });

            GUILayout.Space(14);
            GUILayout.Label("Менеджеры", _header);
            Line("Нанято", $"{g.Managers} из {b.MaxManagers}, по {Fmt.Money(b.ManagerSalary)} за ход");
            GUILayout.Label($"Каждый менеджер даёт +{b.ActionsPerManager} действие со следующего хода.", _small);
            GUILayout.BeginHorizontal();
            if (ActionButton("Нанять · 1 действие", g.ActionsLeft > 0 && g.Managers < b.MaxManagers))
                Act(() => g.HireManager(), "Нанят менеджер.");
            if (ActionButton("Уволить", g.Managers > 0))
                Act(() => g.FireManager(), "Менеджер уволен.");
            GUILayout.EndHorizontal();

            GUILayout.Space(14);
            GUILayout.Label("Расходы за ход", _header);
            Line("Содержание планет", $"{Fmt.Money(g.UpkeepPerTurn)} ({Fmt.Multiplier(g.UpkeepMultiplierNow)} за {g.UpkeepCurvePlanets} раб. планет)");
            Line("Зарплаты", Fmt.Money(g.Managers * b.ManagerSalary));
            Line("Проценты", Fmt.Money(g.Debt * b.InterestPerTurn));
            Line("Страховка", Fmt.Money(g.InsurancePerTurn));
            Line("Доставка на биржу", Fmt.Money(g.ShippingPerTurn));
            Line("<b>Итого</b>", $"<b>{Fmt.Money(g.CostsPerTurn)}</b>");
        }

        // ------------------------------------------------------------------
        // Журнал и подсказки
        // ------------------------------------------------------------------

        void DrawLog(Rect r)
        {
            GUI.Box(r, GUIContent.none);
            GUILayout.BeginArea(new Rect(r.x + 12, r.y + 8, r.width - 24, r.height - 16));
            _logScroll = GUILayout.BeginScrollView(_logScroll);
            foreach (var line in _log) GUILayout.Label(line, _logText);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawTooltip()
        {
            if (string.IsNullOrEmpty(GUI.tooltip)) return;
            var content = new GUIContent(GUI.tooltip);
            var size = _small.CalcSize(content) + new Vector2(16, 10);
            var mouse = Event.current.mousePosition;
            var r = new Rect(mouse.x + 16, mouse.y + 12, size.x, size.y);
            GUI.Box(r, GUIContent.none);
            GUI.Box(r, GUIContent.none);
            GUI.Label(new Rect(r.x + 8, r.y + 5, size.x, size.y), content, _small);
        }

        // ------------------------------------------------------------------
        // Стили и текстуры
        // ------------------------------------------------------------------

        void EnsureStyles()
        {
            if (_stylesReady) return;
            _text = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = false, richText = true };
            _small.normal.textColor = new Color(0.75f, 0.78f, 0.85f);
            _title = new GUIStyle(_text) { fontSize = 24, fontStyle = FontStyle.Bold };
            _header = new GUIStyle(_text) { fontSize = 17, fontStyle = FontStyle.Bold };
            _header.normal.textColor = new Color(0.6f, 0.75f, 1f);
            _button = new GUIStyle(GUI.skin.button) { fontSize = 15, wordWrap = false };
            _mapLabel = new GUIStyle(_small) { alignment = TextAnchor.UpperCenter, fontSize = 12 };
            _logText = new GUIStyle(_text) { fontSize = 14 };
            _stylesReady = true;
        }

        /// <summary>Белое кольцо толщиной thickness пикселей со сглаженными краями.</summary>
        static Texture2D MakeRing(int size, float thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float r = size / 2f - 1;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f));
                float a = Mathf.Clamp01(thickness / 2 - Mathf.Abs(d - (r - thickness / 2)) + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>Белый круг со сглаженным краем; цвет задаётся через GUI.color.</summary>
        static Texture2D MakeCircle(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            float r = size / 2f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                byte a = (byte)(Mathf.Clamp01(r - d) * 255);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }
    }
}
