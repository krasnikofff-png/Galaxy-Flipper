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
        const float SidePanelWidth = 560, Gap = 10, TopBarHeight = 52, EventBarHeight = 30, LogHeight = 190;
        const int MaxLogLines = 200;

        enum Tab { Planet, Market, Orders, Finance, Magnates }
        static readonly string[] TabNames = { "Планета", "Биржа", "Заказы", "Финансы", "Магнаты" };

        /// <summary>Сколько секунд длится «ход соперников» после нажатия «Конец хода».</summary>
        const float RivalsTurnSeconds = 1.2f;
        /// <summary>Когда начался ход соперников; меньше нуля — сейчас ход игрока.</summary>
        float _rivalsTurnStarted = -1;
        bool RivalsTurn => _rivalsTurnStarted >= 0;

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
            if (RivalsTurn && Time.unscaledTime - _rivalsTurnStarted >= RivalsTurnSeconds) FinishTurn();
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
            _rivalsTurnStarted = -1;
            _selectedId = _game.PlayerPlanets.First().Id;
            _tab = Tab.Planet;
            _log.Clear();
            var b = _game.Balance;
            Log($"Новая партия. У вас ледяной карлик с шахтой, {Fmt.Money(b.StartMoney)} денег и долг {Fmt.Money(b.StartDebt)}.");
            Log($"За ход можно сделать {_game.ActionsPerTurn} действия: купить, построить шахту, продать планету, запустить зонд, нанять менеджера.");
            Log("Продажа ресурсов, заказы и кредит действий не тратят. Пробел или Enter — конец хода.");
            Log("Серые планеты — силуэты: тип и запас неизвестны. Кнопка «Зонды» — скан области или полная разведка планеты.");
            Log("Соперники: " + string.Join(" и ", _game.Magnates.Select(m => $"{m.Name} ({m.PersonalityName})")) + ". Чем больше у вас планет, тем больше они пакостят.");
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

            // Пока ходят соперники, интерфейс не принимает клики и клавиши; клик или пробел — пропустить паузу.
            var ev = Event.current;
            if (RivalsTurn && (ev.isMouse || ev.isKey))
            {
                if (ev.type == EventType.MouseDown || ev.type == EventType.KeyDown) Later(FinishTurn);
                ev.Use();
            }
            else
                HandleKeys();

            float bodyTop = TopBarHeight + EventBarHeight + Gap;
            float bodyHeight = height - bodyTop - LogHeight - 2 * Gap;
            float mapWidth = width - SidePanelWidth - 3 * Gap;

            DrawTopBar(new Rect(0, 0, width, TopBarHeight));
            DrawEventBar(new Rect(0, TopBarHeight, width, EventBarHeight));
            DrawMap(new Rect(Gap, bodyTop, mapWidth, bodyHeight));
            DrawSidePanel(new Rect(2 * Gap + mapWidth, bodyTop, SidePanelWidth, bodyHeight));
            DrawLog(new Rect(Gap, height - LogHeight - Gap, width - 2 * Gap, LogHeight));
            if (RivalsTurn) DrawRivalsTurn(width, height);
            else DrawTooltip();
        }

        /// <summary>Плашка «ходят соперники» с именами магнатов и полоской времени.</summary>
        void DrawRivalsTurn(float width, float height)
        {
            var old = GUI.color;
            GUI.color = new Color(0, 0, 0, 0.45f);
            GUI.DrawTexture(new Rect(0, 0, width, height), Texture2D.whiteTexture);
            GUI.color = old;

            var r = new Rect(width / 2 - 230, height / 2 - 95, 460, 190);
            GUI.Box(r, GUIContent.none);
            GUI.Box(r, GUIContent.none);
            float t = Mathf.Clamp01((Time.unscaledTime - _rivalsTurnStarted) / RivalsTurnSeconds);
            string dots = new string('.', 1 + (int)(Time.unscaledTime * 3) % 3);
            GUILayout.BeginArea(new Rect(r.x + 20, r.y + 14, r.width - 40, r.height - 28));
            GUILayout.Label($"Ходят соперники{dots}", _title);
            var magnates = _game.Magnates;
            // Магнаты «ходят» по очереди.
            int current = Mathf.Min(magnates.Count - 1, (int)(t * magnates.Count));
            for (int i = 0; i < magnates.Count; i++)
            {
                var m = magnates[i];
                string state = _game.Turn < m.QuietUntilTurn ? "отбивается от слухов" : i < current ? "походил(а)" : i == current ? "думает" : "ждёт";
                GUILayout.Label($"{m.Name}, {m.PersonalityName}: {state}", _text);
            }
            GUILayout.FlexibleSpace();
            var bar = GUILayoutUtility.GetRect(100, 8, GUILayout.ExpandWidth(true));
            GUI.color = new Color(1, 1, 1, 0.15f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.6f, 0.8f, 1f);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * t, bar.height), Texture2D.whiteTexture);
            GUI.color = old;
            GUILayout.Label("Клик или пробел — пропустить", _small);
            GUILayout.EndArea();
        }

        /// <summary>«Конец хода»: сначала пауза на ход соперников, потом сам ход.</summary>
        void RequestEndTurn()
        {
            if (RivalsTurn) return;
            _probeMode = false;
            _rivalsTurnStarted = Time.unscaledTime;
        }

        void FinishTurn()
        {
            if (!RivalsTurn) return;
            _rivalsTurnStarted = -1;
            EndTurn();
        }

        void HandleKeys()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            if (e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                Later(RequestEndTurn);
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
            if (report.Taxes > 0) line += $", налоги принесли {Fmt.Money(report.Taxes)}";
            Log(line + ".");
            foreach (var e in report.Events) Log(e);
            if (report.Seizures > 0) Log("Денег не хватило: недостача ушла в долг, а долг превысил кредитный лимит.");
        }

        // ------------------------------------------------------------------
        // Верхняя панель
        // ------------------------------------------------------------------

        /// <summary>Полоса сезонного события: что идёт, сколько ещё и чем грозит; в затишье — когда ждать следующего.</summary>
        void DrawEventBar(Rect r)
        {
            var g = _game;
            var ev = g.CurrentEvent;
            var old = GUI.color;
            GUI.color = ev == null ? new Color(1, 1, 1, 0.06f) : new Color(0.95f, 0.75f, 0.3f, 0.25f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            string text = ev == null
                ? $"<color=#8a93a6>Затишье. Следующее галактическое событие — не раньше хода {Mathf.Max(g.NextEventTurn, g.Turn + 1)}.</color>"
                : $"<b>{ev.Event.Name}</b> (ещё {ev.EndTurn - g.Turn + 1} х.): {ev.Event.Description} <color=#ffd27a>{EventEffects(ev.Event)}</color>";
            GUI.Label(new Rect(r.x + 14, r.y + 5, r.width - 28, r.height - 6), text, _small);
        }

        /// <summary>Коротко, что событие меняет.</summary>
        static string EventEffects(GalacticEvent e)
        {
            var parts = new List<string>();
            foreach (var r in ResourceTypes.All)
            {
                if (e.Price(r) != 1) parts.Add($"{r.RuName()} на бирже ×{e.Price(r):0.##}");
                if (e.PlanetSale(r) != 1) parts.Add($"планеты с ресурсом «{r.RuName()}» продаются ×{e.PlanetSale(r):0.##}");
            }
            if (e.UpkeepFactor != 1)
                parts.Add($"содержание ×{e.UpkeepFactor:0.##}" + (e.UpkeepFactorFromPlanets > 0 ? $" у корпораций от {e.UpkeepFactorFromPlanets} планет" : ""));
            if (e.AccidentFactor != 1) parts.Add($"аварии ×{e.AccidentFactor:0.##}");
            if (e.MeteorFactor != 1) parts.Add($"метеориты ×{e.MeteorFactor:0.##}");
            if (e.EpidemicFactor != 1) parts.Add($"эпидемии ×{e.EpidemicFactor:0.##}");
            if (e.OrderChanceFactor != 1) parts.Add($"заказов ×{e.OrderChanceFactor:0.##}");
            if (e.ExtraLoyaltyDrain != 0) parts.Add($"лояльность жителей −{e.ExtraLoyaltyDrain:0.##} за ход");
            return string.Join(", ", parts);
        }

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
            Stat("Репутация", Mathf.RoundToInt((float)g.Reputation).ToString());
            GUILayout.FlexibleSpace();

            string probesLabel = g.Probes.Count > 0 ? $"Зонды ({g.Probes.Count} в полёте)" : "Зонды";
            bool mode = GUILayout.Toggle(_probeMode, probesLabel, _button, GUILayout.Height(34));
            if (mode != _probeMode) Later(() => _probeMode = mode);
            if (GUILayout.Button("Конец хода ▶", _button, GUILayout.Width(150), GUILayout.Height(34)))
                Later(RequestEndTurn);
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
                DrawCircle(center, radius + 4, RingColor(p));
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

        /// <summary>Цвет кольца: владелец, у магнатов — свой цвет у каждого.</summary>
        static Color RingColor(Planet p) =>
            p.Owner == Owner.Ai && p.MagnateId == 1 ? new Color(1f, 0.6f, 0.15f) : Fmt.OwnerColor(p.Owner, p.FoundByProbe);

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
            if (p.Knowledge != Knowledge.Silhouette && p.Inhabited) marks += " · жители";
            switch (p.Owner)
            {
                case Owner.Player:
                    string people = p.Inhabited ? (p.OnStrike ? " · забастовка" : $" · лоял. {Mathf.RoundToInt((float)p.Loyalty)}") : "";
                    return (p.IsDepleted ? "ВЫ · пусто" : "ВЫ") + people + marks.Replace(" · жители", "");
                case Owner.Ai: return _game.GetMagnate(p.MagnateId)?.Name.Split(' ')[0] ?? "ИИ";
                default: return p.IsDepleted ? null : Fmt.Money(p.ListPrice(_game.Balance)) + marks;
            }
        }

        void DrawLegend(Rect r)
        {
            float x = r.x;
            x = LegendItem(x, r.y, Fmt.OwnerColor(Owner.Player, false), "ваши");
            foreach (var m in _game.Magnates)
                x = LegendItem(x, r.y, RingColor(new Planet(-1, _game.Balance.PlanetTypes[0], 0) { Owner = Owner.Ai, MagnateId = m.Id }), m.Name.Split(' ')[0]);
            x = LegendItem(x, r.y, Fmt.OwnerColor(Owner.Registry, false), "свободна (цена)");
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
                case Tab.Magnates: DrawMagnatesTab(); break;
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
                Line("Шахта", Fmt.MineName(p) + (p.EpidemicTurnsLeft > 0 ? $", эпидемия ещё {p.EpidemicTurnsLeft} х.: добыча вполовину" : ""));
                if (p.Owner == Owner.Player)
                    Line("Содержание", $"{Fmt.Money(g.PlanetUpkeep(p))} за ход ({Fmt.Multiplier(g.UpkeepMultiplierNow)} за число планет)");
                else
                    Line("Содержание", $"{Fmt.Money(p.Type.Upkeep)} за ход, без множителя");
                Line(exact ? "Рыночная стоимость" : "Стоимость по оценке", Fmt.Money(g.KnownValue(p) ?? 0));
                if (p.Inhabited) Line("Жители", p.Owner == Owner.Player ? "есть, см. ниже" : "есть: планету продают сами жители");
                if (p.Knowledge == Knowledge.Explored || p.Owner == Owner.Player)
                {
                    var surprises = new List<string>();
                    if (p.HasUnlootedRuins) surprises.Add($"руины с артефактами на {Fmt.Money(p.RuinsValue)}");
                    if (p.RuinsLooted) surprises.Add("руины разграблены");
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
                    if (p.Inhabited && !silhouette)
                        DrawNegotiation(p);
                    else if (p.SoldAtAuction)
                        DrawAuction(p, silhouette, exact);
                    else
                    {
                        string blind = silhouette ? " вслепую" : exact ? "" : " по оценке";
                        if (ActionButton($"Купить{blind} за {Fmt.Money(price)} · 1 действие", g.ActionsLeft > 0 && g.Money >= price))
                            Act(() => g.BuyPlanet(p.Id), $"Куплена планета {p.Type.Name} #{p.Id} за {Fmt.Money(price)}.");
                    }
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
                    if (p.Inhabited) DrawPeople(p);
                    DrawGuards(p);
                    double sale = g.SalePrice(p);
                    double hype = g.CurrentEvent?.Event.PlanetSale(p.Resource) ?? 1;
                    if (hype != 1)
                        GUILayout.Label($"<color=#ffd27a>Событие «{g.CurrentEvent.Event.Name}»: такие планеты сейчас продаются ×{hype:0.##}.</color>", _text);
                    if (ActionButton($"Продать за {Fmt.Money(sale)} (комиссия {Fmt.Percent(b.ResaleCommission)}) · 1 действие", g.ActionsLeft > 0))
                        Act(() => g.SellPlanet(p.Id), $"Продана планета {p.Type.Name} #{p.Id} за {Fmt.Money(sale)}.");
                    break;

                case Owner.Ai:
                    var owner = g.GetMagnate(p.MagnateId);
                    GUILayout.Label($"Планета магната {owner?.Name ?? "ИИ"}. Купить её можно, когда он выставит её на аукцион.", _text);
                    if (p.Inhabited && !silhouette && !p.IsDepleted)
                    {
                        double bribe = g.MagnateBribeCost(p);
                        GUILayout.Label($"Можно подкупить жителей: шанс {Fmt.Percent(b.PlayerBribeChance)}, репутация {Fmt.Amount(b.PlayerBribeReputation)}, " +
                                        $"Галактический реестр ловит в {Fmt.Percent(b.FineChance)} случаев (штраф {Fmt.Money(b.FineAmount)}).", _small);
                        int id = p.Id;
                        if (ActionButton($"Подкупить жителей за {Fmt.Money(bribe)} · 1 действие", g.ActionsLeft > 0 && g.Money >= bribe))
                            Later(() => Do(g.BribeMagnateResidents(id, out _)));
                    }
                    DrawExploreButtons(p);
                    break;
            }
        }

        static readonly double[] OfferShares = { 0.8, 0.9, 1.0, 1.1 };

        /// <summary>Торг с жителями: несколько цен с шансом согласия и сделка «за школу».</summary>
        void DrawNegotiation(Planet p)
        {
            var g = _game;
            if (g.Turn < p.NoTalksUntilTurn)
            {
                GUILayout.Label($"Жители обиделись после отказа и не разговаривают до хода {p.NoTalksUntilTurn}.", _text);
                return;
            }
            GUILayout.Label("Планету продают сами жители. Чем щедрее предложение, тем охотнее согласятся и тем выше будет их лояльность. Отказ тратит действие.", _small);
            var offers = OfferShares.Select(x => new Offer(x)).ToList();
            offers.Add(new Offer(1, withSchool: true));
            foreach (var o in offers)
            {
                double cost = g.OfferCost(p, o);
                string what = o.WithSchool
                    ? $"Сделка «за школу»: {Fmt.Money(g.OfferPrice(p, o))} + школа {Fmt.Money(g.Balance.SchoolCost)}"
                    : $"Предложить {Fmt.Percent(o.PriceShare)} цены: {Fmt.Money(cost)}";
                string text = $"{what} · согласятся с шансом {Fmt.Percent(g.OfferChance(p, o))}";
                var offer = o;
                int id = p.Id;
                if (ActionButton(text, g.ActionsLeft > 0 && g.Money >= cost))
                    Later(() =>
                    {
                        if (Do(g.MakeOffer(id, offer, out bool accepted)) && accepted)
                            Log($"Сделка с жителями #{id}: планета ваша за {Fmt.Money(g.OfferPrice(g.GetPlanet(id), offer))}.");
                    });
            }
        }

        /// <summary>Лояльность, налог, зарплаты, праздник, школа и просьбы жителей своей планеты.</summary>
        void DrawPeople(Planet p)
        {
            var g = _game;
            var b = g.Balance;
            GUILayout.Space(8);
            GUILayout.Label("Жители", _header);
            double delta = g.LoyaltyChangePerTurn(p);
            string trend = delta >= 0 ? $"<color=#7be38b>+{Fmt.Amount(delta)}</color>" : $"<color=#ff8a7a>{Fmt.Amount(delta)}</color>";
            Line("Лояльность", $"{Mathf.RoundToInt((float)p.Loyalty)} из 100, за ход {trend}");
            DrawBar((float)(p.Loyalty / 100));
            if (p.OnStrike)
                GUILayout.Label($"<color=#ff8a7a>Забастовка: ни добычи, ни налогов, пока лояльность не поднимется до {Fmt.Amount(b.StrikeEndsAt)}.</color>", _text);
            else
                GUILayout.Label($"Ниже {Fmt.Amount(b.StrikeBelow)} — забастовка. Шахта снижает лояльность на {Fmt.Amount(b.LoyaltyMiningDrain)} за ход.", _small);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Налог", _text, GUILayout.Width(80));
            var taxNames = new[]
            {
                "нет",
                $"низкий +{Fmt.Money(b.TaxIncome[1])} ({Fmt.Amount(b.TaxLoyalty[1])})",
                $"высокий +{Fmt.Money(b.TaxIncome[2])} ({Fmt.Amount(b.TaxLoyalty[2])})",
            };
            int tax = GUILayout.Toolbar((int)p.Tax, taxNames, _button, GUILayout.Height(30));
            if (tax != (int)p.Tax)
            {
                int id = p.Id;
                Later(() => Do(g.SetTax(id, (TaxLevel)tax)));
            }
            GUILayout.EndHorizontal();

            bool wages = GUILayout.Toggle(p.PaysWages, $" Платить зарплаты: {Fmt.Money(b.WagesCost)} за ход, лояльность +{Fmt.Amount(b.WagesLoyalty)} за ход", _text);
            if (wages != p.PaysWages)
            {
                int id = p.Id;
                Later(() => Do(g.SetWages(id, wages)));
            }

            if (ActionButton($"Праздник за {Fmt.Money(b.FestivalCost)}: лояльность +{Fmt.Amount(b.FestivalLoyalty)} · 1 действие", g.ActionsLeft > 0 && g.Money >= b.FestivalCost))
                Act(() => g.HoldFestival(p.Id), $"Праздник на {p.Type.Name} #{p.Id}.");
            if (!p.HasSchool && ActionButton($"Построить школу за {Fmt.Money(b.SchoolCost)}: +{Fmt.Amount(b.SchoolLoyalty)} лояльности каждый ход · 1 действие",
                    g.ActionsLeft > 0 && g.Money >= b.SchoolCost))
                Act(() => g.BuildSchool(p.Id), $"Построена школа на {p.Type.Name} #{p.Id}.");
            if (p.HasSchool) GUILayout.Label("Школа построена.", _small);

            foreach (var r in g.Requests.Where(r => r.PlanetId == p.Id).ToList())
                DrawRequest(r, p);
            foreach (var x in g.Bribes.Where(x => x.PlanetId == p.Id).ToList())
                DrawBribe(x, p);
        }

        /// <summary>Подкуп магната: предупреждение и встречный подкуп.</summary>
        void DrawBribe(BribeAttempt x, Planet p)
        {
            var g = _game;
            var b = g.Balance;
            var m = g.GetMagnate(x.MagnateId);
            int left = x.ResolveTurn - g.Turn + 1;
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<color=#ff8a7a><b>{m?.Name} подкупает жителей {p.Type.Name} #{p.Id}!</b></color> Через {left} х. планета уйдёт к магнату, " +
                            $"если лояльность ({Mathf.RoundToInt((float)p.Loyalty)}) не поднимется до {Fmt.Amount(b.BribeSuccessLoyalty)}. Помогут праздник или встречный подкуп.", _small);
            int id = x.Id;
            if (ActionButton($"Встречный подкуп за {Fmt.Money(b.CounterBribeCost)}: лояльность +{Fmt.Amount(b.CounterBribeLoyalty)}, без действия", g.Money >= b.CounterBribeCost))
                Act(() => g.CounterBribe(id), $"Встречный подкуп на {p.Type.Name} #{p.Id}: жители остаются с вами.");
            GUILayout.EndVertical();
        }

        void DrawGuards(Planet p)
        {
            if (!p.Inhabited) return;
            var g = _game;
            var b = g.Balance;
            bool guards = GUILayout.Toggle(p.Guarded,
                $" Охрана: {Fmt.Money(b.GuardsCost)} за ход. Магнаты подкупают охраняемых жителей, только если лояльность ниже {Fmt.Amount(b.GuardedBribeLoyalty)}", _text);
            if (guards != p.Guarded)
            {
                int id = p.Id;
                Later(() => Do(g.SetGuards(id, guards)));
            }
        }

        /// <summary>Ставки на аукционе с шансом выиграть.</summary>
        void DrawAuction(Planet p, bool silhouette, bool exact)
        {
            var g = _game;
            GUILayout.Label("Аукцион реестра: магнаты тоже ставят. Проиграете — планета уйдёт к ним, действие потрачено." +
                            (silhouette ? " Это силуэт: ставка вслепую." : exact ? "" : " Ставка по оценке реестра."), _small);
            foreach (var f in g.Balance.BidFactors)
            {
                double bid = g.BidPrice(p, f);
                int id = p.Id;
                double factor = f;
                if (ActionButton($"Ставка {Fmt.Percent(f)}: {Fmt.Money(bid)} · выиграете с шансом {Fmt.Percent(g.AuctionWinChance(f))}", g.ActionsLeft > 0 && g.Money >= bid))
                    Later(() =>
                    {
                        if (Do(g.PlaceBid(id, factor, out bool won)) && won)
                            Log($"Аукцион выигран: планета #{id} ваша за {Fmt.Money(bid)}.");
                    });
            }
        }

        void DrawMagnatesTab()
        {
            var g = _game;
            var b = g.Balance;
            GUILayout.Label("Репутация", _header);
            Line("Ваша репутация", $"{Mathf.RoundToInt((float)g.Reputation)} из 100");
            DrawBar((float)(g.Reputation / 100));
            GUILayout.Label("Грязные дела её портят, выполненные заказы чинят, со временем она возвращается к 50. " +
                            "От репутации зависят согласие жителей и число заказов.", _small);
            if (g.FinesPaid > 0) Line("Штрафы реестра", Fmt.Money(g.FinesPaid));
            Line("Внимание магнатов", $"{Fmt.Percent(g.MagnateAttention)}: чем больше у вас планет, тем чаще пакости");
            GUILayout.Space(10);

            foreach (var m in g.Magnates)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                int planets = g.AllPlanets.Count(p => p.Owner == Owner.Ai && p.MagnateId == m.Id);
                GUILayout.Label($"<b>{m.Name}</b>, {m.PersonalityName}. Планет: {planets}.", _text);
                GUILayout.Label(m.Personality == Personality.Greedy
                    ? "Скупает выгодные планеты, щедро ставит на аукционах, подкупает жителей с низкой лояльностью."
                    : "Непредсказуема: ставки вразброс, распускает слухи, переманивает заказчиков и менеджеров.", _small);
                if (g.Turn < m.QuietUntilTurn)
                    GUILayout.Label($"Отбивается от ваших слухов до хода {m.QuietUntilTurn}: не покупает и не пакостит.", _small);
                int id = m.Id;
                if (ActionButton($"Распустить слух о магнате за {Fmt.Money(b.PlayerRumorCost)}: {b.PlayerRumorQuietTurns} х. тишины · 1 действие",
                        g.ActionsLeft > 0 && g.Money >= b.PlayerRumorCost))
                    Later(() => Do(g.SpreadRumor(id)));
                GUILayout.Label($"Грязное дело: репутация {Fmt.Amount(b.PlayerRumorReputation)}, реестр ловит в {Fmt.Percent(b.FineChance)} случаев (штраф {Fmt.Money(b.FineAmount)}).", _small);
                GUILayout.EndVertical();
                GUILayout.Space(6);
            }

            if (g.Bribes.Count > 0)
            {
                GUILayout.Label("Подкупы ваших жителей", _header);
                foreach (var x in g.Bribes.ToList())
                {
                    var p = g.GetPlanet(x.PlanetId);
                    if (p != null) DrawBribe(x, p);
                }
            }
        }

        void DrawRequest(ResidentRequest r, Planet p)
        {
            var g = _game;
            var b = g.Balance;
            int left = r.DeadlineTurn - g.Turn;
            GUILayout.BeginVertical(GUI.skin.box);
            if (r.Kind == RequestKind.School)
            {
                GUILayout.Label($"<b>{p.Type.Name} #{p.Id}</b>: жители просят школу. Осталось ходов: {left}. " +
                                $"Выполните — лояльность +{Fmt.Amount(b.RequestLoyaltyReward)}, нет — −{Fmt.Amount(b.RequestLoyaltyPenalty)}.", _small);
                if (ActionButton($"Построить школу за {Fmt.Money(b.SchoolCost)} · 1 действие", g.ActionsLeft > 0 && g.Money >= b.SchoolCost))
                    Act(() => g.BuildSchool(p.Id), $"Построена школа на {p.Type.Name} #{p.Id}.");
            }
            else
            {
                double have = g.Stock(r.Resource);
                GUILayout.Label($"<b>{p.Type.Name} #{p.Id}</b>: жители просят {Fmt.Amount(r.Amount)} ({r.Resource.RuName()}). Осталось ходов: {left}. " +
                                $"На складе {Fmt.Amount(have)}. Выполните — лояльность +{Fmt.Amount(b.RequestLoyaltyReward)}, нет — −{Fmt.Amount(b.RequestLoyaltyPenalty)}.", _small);
                int id = r.Id;
                if (ActionButton("Отдать со склада биржи", have >= r.Amount))
                    Act(() => g.FulfillRequest(id), "Просьба жителей выполнена.");
            }
            GUILayout.EndVertical();
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
            if (g.Bribes.Count > 0)
            {
                GUILayout.Label("Подкупы ваших жителей", _header);
                foreach (var x in g.Bribes.ToList())
                {
                    var p = g.GetPlanet(x.PlanetId);
                    if (p != null) DrawBribe(x, p);
                }
                GUILayout.Space(10);
            }
            if (g.Requests.Count > 0)
            {
                GUILayout.Label("Просьбы жителей", _header);
                foreach (var r in g.Requests.ToList())
                {
                    var p = g.GetPlanet(r.PlanetId);
                    if (p != null) DrawRequest(r, p);
                }
                GUILayout.Space(10);
                GUILayout.Label("Заказы", _header);
            }
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
                $" Страховать планеты: {Fmt.Money(insurance)} за ход ({Fmt.Percent(b.InsuranceRate)} стоимости). Возмещает добычу при авариях и эпидемиях и шахту, разбитую метеоритом. Бунты не покрывает.", _text);
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
            Line("Зарплаты жителям", Fmt.Money(g.WagesPerTurn));
            Line("Охрана", Fmt.Money(g.GuardsPerTurn));
            Line("<b>Итого</b>", $"<b>{Fmt.Money(g.CostsPerTurn)}</b>");
            Line("Налоги с жителей", $"+{Fmt.Money(g.TaxesPerTurn)} за ход");
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
