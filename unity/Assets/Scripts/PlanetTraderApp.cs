using System;
using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;
using UnityEngine;
using PlanetTrader.Visuals;

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

        enum Tab { Planet, Market, Orders, Finance, Team, Development, Magnates }
        static readonly string[] TabNames = { "Планета", "Биржа", "Заказы", "Финансы", "Команда", "Развитие", "Магнаты" };

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
        /// <summary>Для какой вкладки и планеты прокручена правая панель: сменились — прокрутка наверх.</summary>
        (Tab tab, int planet) _panelScrollFor;
        Texture2D _circle, _ring, _thinRing;

        // Планеты на карте — настоящие 3D-сферы своей камеры; интерфейс рисует поверх кольца и подписи.
        SectorMapView _map3d;
        PlanetPortrait _portrait;
        const float PortraitHeight = 210;
        readonly List<MapPlanet> _mapPlanets = new List<MapPlanet>();
        /// <summary>Прямоугольник карты в виртуальных координатах интерфейса.</summary>
        Rect _mapBox;
        /// <summary>Во сколько раз интерфейс увеличен под экран.</summary>
        float _guiScale = 1;
        /// <summary>Зерно облика планет: в каждой партии планеты выглядят по-новому.</summary>
        int _lookSeed;
        /// <summary>F3 — показ построек: 0 — как в игре, 1 — все постройки на выбранной планете, 2 — то же со стройкой шахты.</summary>
        int _buildingsDemo;

        // Режим зонда: игрок выбирает тип и задание, потом кликает по карте.
        bool _probeMode;
        int _probeTypeIndex = 1;
        ProbeMission _probeMission = ProbeMission.ScanArea;
        /// <summary>Квадрат карты на экране: координаты 0..1 планет растягиваются на него.</summary>
        Rect _mapArea;

        GUIStyle _text, _small, _title, _button, _mapLabel, _logText, _header, _tooltipText, _portraitButton, _portraitHint;
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
            // Витрина планет: крупный план планет каждого типа.
            if (Input.GetKeyDown(KeyCode.F2))
            {
                PlanetShowcase.Toggle(this);
                return;
            }
            // Показ построек: только картинка в окне планеты, экономика не меняется.
            if (Input.GetKeyDown(KeyCode.F3))
            {
                _buildingsDemo = (_buildingsDemo + 1) % 3;
                Log(_buildingsDemo == 0 ? "Показ построек выключен." :
                    _buildingsDemo == 1 ? "Показ построек (F3): на выбранной планете все постройки — только в окне планеты, в игре их нет. F3 — стройка шахты." :
                    "Показ построек (F3): шахта строится. F3 — выключить показ.");
            }
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
            _thinRing = MakeRing(64, 4);
            _map3d = gameObject.AddComponent<SectorMapView>();
            _portrait = gameObject.AddComponent<PlanetPortrait>();
            NewGame();
        }

        // Пока открыта витрина, интерфейс выключен — вместе с ним прячется и 3D-карта.
        void OnEnable()
        {
            if (_map3d != null) _map3d.enabled = true;
            if (_portrait != null) _portrait.enabled = true;
        }

        void OnDisable()
        {
            if (_map3d != null) _map3d.enabled = false;
            if (_portrait != null) _portrait.enabled = false;
        }

        /// <summary>Как выглядит планета в 3D: тип, зерно, силуэт или выработана.</summary>
        MapPlanet Describe(Planet p)
        {
            bool silhouette = p.Knowledge == Knowledge.Silhouette;
            return new MapPlanet
            {
                Id = p.Id,
                // Тип силуэта неизвестен — у всех силуэтов серый каменный облик.
                Look = silhouette ? PlanetLook.Rocky : PlanetSurface.FromResource(p.Resource),
                Seed = _lookSeed ^ (p.Id * 7919),
                Silhouette = silhouette,
                Depleted = p.IsDepleted,
            };
        }

        /// <summary>Какие постройки видны на планете в 3D-окне. У силуэта не видно ничего.</summary>
        PlanetStructures Structures(Planet p)
        {
            if (_buildingsDemo != 0) return DemoStructures(p);
            if (p.Knowledge == Knowledge.Silhouette) return default;
            var mine = p.Mine == MineState.Building ? MineLook.Building
                : p.Mine == MineState.Working ? (p.IsProducing ? MineLook.Working : MineLook.Idle)
                : MineLook.None;
            return new PlanetStructures
            {
                Mine = mine,
                Extractors = p.ExtraDeposits.Where(d => d.HasExtractor).Select(d => Fmt.ResourceColor(d.Resource)).ToArray(),
                Settlement = p.Inhabited,
                School = p.HasSchool,
                Guard = p.Guarded,
                Ruins = p.Knowledge == Knowledge.Explored && p.HasUnlootedRuins,
                Accent = p.Owner == Owner.Registry ? new Color(0.75f, 0.78f, 0.85f) : RingColor(p),
            };
        }

        /// <summary>F3: все постройки разом — посмотреть модели и их движение, не строя их в игре.</summary>
        PlanetStructures DemoStructures(Planet p)
        {
            var others = ResourceTypes.All.Where(r => r != p.Resource).Take(2).Select(Fmt.ResourceColor).ToArray();
            return new PlanetStructures
            {
                Mine = _buildingsDemo == 2 ? MineLook.Building : MineLook.Working,
                Extractors = others,
                Settlement = true,
                School = true,
                Guard = true,
                Ruins = true,
                Accent = p.Owner == Owner.Registry ? Fmt.OwnerColor(Owner.Player, false) : RingColor(p),
            };
        }

        /// <summary>Расставить 3D-планеты по местам, которые карта получила в последнем OnGUI.</summary>
        void LateUpdate()
        {
            if (_mapArea.width <= 0) return;
            _mapPlanets.Clear();
            foreach (var p in _game.AllPlanets)
            {
                var m = Describe(p);
                m.Center = ToScreen(p.MapX, p.MapY) * _guiScale;
                m.Radius = PlanetRadius(p) * _guiScale;
                _mapPlanets.Add(m);
            }
            var box = new Rect(_mapBox.x * _guiScale, _mapBox.y * _guiScale, _mapBox.width * _guiScale, _mapBox.height * _guiScale);
            _map3d.Show(box, _mapPlanets);
        }

        void NewGame()
        {
            var seed = (ulong)DateTime.Now.Ticks;
            _game = Core.Game.New(new Balance(), seed);
            _lookSeed = (int)(seed % int.MaxValue);
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

        /// <summary>Для проверки графики (AutoScreenshot): партия и выбор планеты.</summary>
        internal Core.Game CurrentGame => _game;
        internal void Select(int planetId) => _selectedId = planetId;

        // ------------------------------------------------------------------
        // Кадр
        // ------------------------------------------------------------------

        void OnGUI()
        {
            EnsureStyles();
            float scale = Mathf.Min(Screen.width / RefWidth, Screen.height / RefHeight);
            _guiScale = scale;
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

            var r = new Rect(width / 2 - 330, height / 2 - 150, 660, 300);
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
                var said = _game.RecentRemarks.LastOrDefault(x => x.MagnateId == m.Id && x.Turn >= _game.Turn - 3);
                if (said != null) GUILayout.Label($"   <i>«{said.Text}»</i>", _small);
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
            if (report.Intellect > 0) line += $", умственный труд — {Fmt.Money(report.Intellect)}";
            Log(line + ".");
            foreach (var e in report.Events) Log(e);
            if (report.Seizures > 0) Log("Денег не хватило: недостача ушла в долг, а долг превысил кредитный лимит.");
            if (_game.Bankrupt) Log("<color=#ff8a7a>Банкротство: планет не осталось, а долг есть. Партия окончена — нажмите «Новая игра».</color>");
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
            if (GUILayout.Button("Планеты (F2)", _button, GUILayout.Width(130), GUILayout.Height(34)))
                Later(() => PlanetShowcase.Toggle(this));
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
            // Фон и сами планеты рисует камера 3D-карты (SectorMapView) в этом же прямоугольнике.
            _mapBox = r;
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

                // Сама планета — 3D-сфера под интерфейсом; здесь только кольца вокруг неё.
                if (p.Id == _selectedId) DrawThinRing(center, radius + 11, Color.white);
                DrawThinRing(center, radius + 6, RingColor(p));
                if (p.Knowledge == Knowledge.Explored && p.Owner != Owner.Player)
                    DrawCircle(center + new Vector2(radius + 2, -radius - 2), 3.5f, Color.white);

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
        static Color RingColor(Planet p) => RingColor(p.Owner, p.MagnateId, p.FoundByProbe);

        static Color RingColor(Owner owner, int magnateId, bool foundByProbe) =>
            owner == Owner.Ai && magnateId == 1 ? new Color(1f, 0.6f, 0.15f) : Fmt.OwnerColor(owner, foundByProbe);

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
            return 13 + Mathf.Clamp((float)(p.Type.Price - 1600) / 2600f, 0, 1) * 11;
        }

        string MapLabel(Planet p)
        {
            string marks = "";
            if (p.Knowledge == Knowledge.Explored && p.HasUnlootedRuins) marks += " · руины";
            if (p.Knowledge != Knowledge.Silhouette && p.Inhabited) marks += " · жители";
            switch (p.Owner)
            {
                case Owner.Player when p.IsRegenerating:
                    return $"ВЫ · восст. {p.RegenTurnsLeft} х.";
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
                x = LegendItem(x, r.y, RingColor(Owner.Ai, m.Id, false), m.Name.Split(' ')[0]);
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
            var size = _mapLabel.CalcSize(new GUIContent(text));
            GUI.Label(new Rect(x + 17, y + 1, size.x + 4, 22), text, _mapLabel);
            return x + 17 + size.x + 16;
        }

        /// <summary>Тонкое кольцо вокруг планеты: владелец, выбранная планета.</summary>
        void DrawThinRing(Vector2 center, float radius, Color color)
        {
            var old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, 2 * radius, 2 * radius), _thinRing);
            GUI.color = old;
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
            if (_panelScrollFor != (_tab, _selectedId))
            {
                _panelScrollFor = (_tab, _selectedId);
                _panelScroll = Vector2.zero;
            }
            _panelScroll = GUILayout.BeginScrollView(_panelScroll);
            switch (_tab)
            {
                case Tab.Planet: DrawPlanetTab(); break;
                case Tab.Market: DrawMarketTab(); break;
                case Tab.Orders: DrawOrdersTab(); break;
                case Tab.Finance: DrawFinanceTab(); break;
                case Tab.Team: DrawTeamTab(); break;
                case Tab.Development: DrawDevelopmentTab(); break;
                case Tab.Magnates: DrawMagnatesTab(); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>Крупный план выбранной планеты в 3D.</summary>
        void DrawPortrait(Planet p)
        {
            var rect = GUILayoutUtility.GetRect(10, PortraitHeight, GUILayout.ExpandWidth(true));
            var ev = Event.current;
            int id = GUIUtility.GetControlID(FocusType.Passive);
            if (ev.type == EventType.Repaint)
            {
                GUI.DrawTexture(rect, _portrait.Show(Describe(p), Structures(p), rect.size * _guiScale), ScaleMode.StretchToFill, false);
                GUI.Label(new Rect(rect.x + 10, rect.yMax - 24, rect.width - 20, 20),
                    _portrait.CloseUp ? "тянуть мышью — облететь базу, колесо — ближе/дальше" : "тянуть мышью — вращать планету, колесо — ближе/дальше",
                    _portraitHint);
            }
            // Кнопка — раньше перетаскивания, чтобы клик по ней не начинал вращение.
            if (_portrait.HasBuildings &&
                GUI.Button(new Rect(rect.xMax - 132, rect.y + 8, 124, 26), _portrait.CloseUp ? "Вся планета" : "К постройкам", _portraitButton))
                _portrait.ToggleView();

            switch (ev.GetTypeForControl(id))
            {
                case EventType.MouseDown when ev.button == 0 && rect.Contains(ev.mousePosition):
                    GUIUtility.hotControl = id;
                    ev.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    _portrait.Drag(ev.delta);
                    ev.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    ev.Use();
                    break;
                case EventType.ScrollWheel when rect.Contains(ev.mousePosition):
                    // Колесо над кадром приближает, а не листает панель.
                    _portrait.Zoom(ev.delta.y);
                    ev.Use();
                    break;
            }
            GUILayout.Space(8);
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
            DrawPortrait(p);
            Line("Владелец", Fmt.OwnerName(p.Owner, p.FoundByProbe));
            Line("Знание", silhouette ? "силуэт: тип и запас неизвестны" : exact ? "полная разведка" : "скан: тип и оценка реестра");
            Line("До биржи", $"{Fmt.Amount(g.DistanceToExchange(p) * 10)} св. лет, груз идёт {g.ShipTurns(p)} х.");

            if (!silhouette)
            {
                Line("Ресурс", $"{p.Resource.RuName()}, на бирже сейчас {Fmt.Money(g.Market.Price(p.Resource))} за ед.");
                if (exact)
                {
                    string aged = p.Capacity < p.InitialReserve - 0.5 ? $" (было {Fmt.Amount(p.InitialReserve)}: месторождение стареет)" : "";
                    Line("Запас", $"{Fmt.Amount(p.Reserve)} из {Fmt.Amount(p.Capacity)}{aged}");
                    DrawBar(p.Capacity > 0 ? (float)(p.Reserve / p.Capacity) : 0);
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
                DrawDeposits(p, exact);
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
                    double price = g.PurchasePrice(p);
                    string note = p.FoundByProbe ? "без надбавки: её нашёл ваш зонд" : $"с надбавкой аукциона {Fmt.Percent(b.AuctionMarkup)}";
                    if (p.Mine == MineState.Working) note += ", шахта уже стоит";
                    GUILayout.Label($"Цена {Fmt.Money(price)} ({note}). Реестр считает цену по своей оценке запаса.", _text);
                    if (p.Mine == MineState.None)
                        GUILayout.Label($"Потом шахта: {Fmt.Money(g.MineCost(p))} и {b.MineBuildTurns} хода постройки.", _small);
                    if (p.Inhabited && !silhouette)
                        DrawNegotiation(p);
                    else if (p.SoldAtAuction)
                        DrawAuction(p, silhouette, exact);
                    else
                    {
                        string blind = silhouette ? " вслепую" : exact ? "" : " по оценке";
                        if (ActionButton($"Купить{blind} за {Fmt.Money(price)} · {Doer(Skill.Negotiation)}", g.ActionsLeft > 0 && g.Money >= price))
                            Act(() => g.BuyPlanet(p.Id), $"Куплена планета {p.Type.Name} #{p.Id} за {Fmt.Money(price)}.");
                    }
                    DrawExploreButtons(p);
                    break;

                case Owner.Player:
                    if (p.Mine == MineState.None && !p.IsDepleted)
                    {
                        double mine = g.MineCost(p);
                        if (ActionButton($"Построить шахту за {Fmt.Money(mine)}, {b.MineBuildTurns} хода · {Doer(Skill.Geology)}",
                                g.ActionsLeft > 0 && g.Money >= mine))
                            Act(() => g.BuildMine(p.Id), $"Начата постройка шахты на {p.Type.Name} #{p.Id}.");
                    }
                    if (p.IsDepleted)
                        GUILayout.Label(p.Inhabited && p.Education > 0
                            ? "Основное месторождение восстанавливается. Пока шахта стоит, все жители заняты умственным трудом."
                            : "Основное месторождение восстанавливается, а содержание идёт. Жители с образованием зарабатывали бы и сейчас.", _text);
                    foreach (var x in g.Claims.Where(c => c.PlanetId == p.Id).ToList())
                        DrawClaim(x, p);
                    if (p.Inhabited) DrawPeople(p);
                    else
                    {
                        GUILayout.Label($"Планета необитаема. Колонисты будут платить налоги, а выучившись — зарабатывать умом, даже когда месторождение выработано. " +
                                        $"Но шахта их раздражает: −{Fmt.Amount(b.LoyaltyMiningDrain)} лояльности за ход.", _small);
                        if (ActionButton($"Заселить колонистов за {Fmt.Money(g.ColonyCost)}: лояльность {Fmt.Amount(b.ColonyLoyalty)} · {Doer(Skill.People)}",
                                g.ActionsLeft > 0 && g.Money >= g.ColonyCost))
                            Act(() => g.SettleColony(p.Id), $"На {p.Type.Name} #{p.Id} прибыли колонисты.");
                    }
                    DrawGuards(p);
                    double sale = g.SellPlanetPrice(p);
                    double hype = g.CurrentEvent?.Event.PlanetSale(p.Resource) ?? 1;
                    if (hype != 1)
                        GUILayout.Label($"<color=#ffd27a>Событие «{g.CurrentEvent.Event.Name}»: такие планеты сейчас продаются ×{hype:0.##}.</color>", _text);
                    if (ActionButton($"Продать за {Fmt.Money(sale)} (комиссия {Fmt.Percent(b.ResaleCommission)}) · {Doer(Skill.Negotiation)}", g.ActionsLeft > 0))
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

            if (ActionButton($"Праздник за {Fmt.Money(b.FestivalCost)}: лояльность +{Fmt.Amount(g.FestivalLoyalty)} · {Doer(Skill.People)}", g.ActionsLeft > 0 && g.Money >= b.FestivalCost))
                Act(() => g.HoldFestival(p.Id), $"Праздник на {p.Type.Name} #{p.Id}.");
            if (!p.HasSchool && ActionButton($"Построить школу за {Fmt.Money(g.SchoolCost)}: +{Fmt.Amount(b.SchoolLoyalty)} лояльности каждый ход · {Doer(Skill.People)}",
                    g.ActionsLeft > 0 && g.Money >= g.SchoolCost))
                Act(() => g.BuildSchool(p.Id), $"Построена школа на {p.Type.Name} #{p.Id}.");
            if (p.HasSchool) GUILayout.Label("Школа построена.", _small);
            DrawEducation(p);

            foreach (var r in g.Requests.Where(r => r.PlanetId == p.Id).ToList())
                DrawRequest(r, p);
            foreach (var x in g.Bribes.Where(x => x.PlanetId == p.Id).ToList())
                DrawBribe(x, p);
        }

        /// <summary>Образование жителей: специальность, уровень, доход от умственного труда.</summary>
        void DrawEducation(Planet p)
        {
            var g = _game;
            var b = g.Balance;
            GUILayout.Space(6);
            GUILayout.Label("Образование", _header);
            int max = b.EducationCost.Length - 1;
            if (p.Education > 0)
            {
                string natural = p.NaturalSpecialty ? $", склонны к этому: +{Fmt.Percent(b.NaturalSpecialtyBonus)} к доходу" : "";
                Line("Специальность", $"{p.Specialty.RuName()}, уровень {p.Education} из {max}{natural}");
                string mining = p.Mine == MineState.Working && !p.IsDepleted ? $" (пока шахта работает, умом занято {Fmt.Percent(b.IntellectWhileMining)} жителей)" : "";
                Line("Умственный труд", $"+{Fmt.Money(g.IntellectIncome(p))} за ход{mining}");
                Line("Бонус корпорации", p.Specialty.BonusText());
            }
            else
                GUILayout.Label($"Образованные жители зарабатывают умственным трудом: меньше, чем шахта, зато всегда, даже пока месторождение восстанавливается. " +
                                $"Каждый уровень даёт и бонус всей корпорации. Доход растёт с лояльностью.", _small);

            if (p.IsStudying)
            {
                GUILayout.Label($"Жители учатся: уровень {p.Education + 1} через {p.EducationTurnsLeft} х.", _text);
                return;
            }
            if (p.Education >= max) return;

            int next = p.Education + 1;
            double cost = g.EducationCost(p);
            g.CanEducate(p, out string reason);
            bool canClick = reason == null && g.ActionsLeft > 0;
            if (reason != null && reason != "Не хватает денег")
                GUILayout.Label(reason + ".", _small);
            string income = $"+{Fmt.Money(b.IntellectIncome[next])} за ход при лояльности 50";
            if (p.Education == 0)
            {
                var natural = Specialties.Natural(p.Resource);
                foreach (var sp in Specialties.All)
                {
                    var choice = sp;
                    string tag = sp == natural ? " · склонны" : "";
                    if (ActionButton($"Учить: {sp.RuName()}{tag} ({sp.BonusText()}) — {Fmt.Money(cost)}, {b.EducationTurns[next]} х. · 1 действие", canClick))
                        Act(() => g.Educate(p.Id, choice), $"Жители {p.Type.Name} #{p.Id} учатся: {choice.RuName()}.");
                }
                GUILayout.Label($"Первый уровень: {income}.", _small);
            }
            else if (ActionButton($"Уровень {next}: {income} — {Fmt.Money(cost)}, {b.EducationTurns[next]} х. · 1 действие", canClick))
                Act(() => g.Educate(p.Id), $"Жители {p.Type.Name} #{p.Id} учатся дальше.");
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

        /// <summary>Рейдерский иск: срок и кнопка защиты.</summary>
        void DrawClaim(HostileClaim x, Planet p)
        {
            var g = _game;
            var b = g.Balance;
            var m = g.GetMagnate(x.MagnateId);
            int left = x.ResolveTurn - g.Turn + 1;
            double cost = g.ClaimDefenseCost(p);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<color=#ff8a7a><b>{m?.Name} оспаривает в реестре {p.Type.Name} #{p.Id}!</b></color> Через {left} х. планета уйдёт к магнату " +
                            $"за {Fmt.Percent(b.ClaimCompensationShare)} стоимости, если не оплатить юристов.", _small);
            int id = x.Id;
            if (ActionButton($"Оплатить защиту: {Fmt.Money(cost)}, без действия", g.Money >= cost))
                Act(() => g.DefendClaim(id), $"Иск против {p.Type.Name} #{p.Id} отклонён.");
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
                if (ActionButton($"Ставка {Fmt.Percent(f)}: {Fmt.Money(bid)} · выиграете с шансом {Fmt.Percent(g.AuctionWinChance(p, f))}", g.ActionsLeft > 0 && g.Money >= bid))
                    Later(() =>
                    {
                        if (Do(g.PlaceBid(id, factor, out bool won)) && won)
                            Log($"Аукцион выигран: планета #{id} ваша за {Fmt.Money(bid)}.");
                    });
            }
        }

        /// <summary>Кто возьмётся за дело: имя и навык. Для кнопок действий.</summary>
        string Doer(Skill skill)
        {
            var m = _game.WhoActs(skill);
            if (m == null) return "действий нет";
            string who = m.IsDirector ? "вы" : m.Name.Split(' ')[0];
            return $"{who}, {skill.RuName()} {m.Level(skill)}";
        }

        static string SkillsLine(Manager m) =>
            string.Join(", ", Skills.All.Select(s => $"{s.RuName()} {m.Level(s)}"));

        /// <summary>Команда: директор и менеджеры, кто действует, кандидаты на работу.</summary>
        void DrawTeamTab()
        {
            var g = _game;
            var b = g.Balance;
            GUILayout.Label("Команда", _header);
            GUILayout.Label("Каждое действие выполняет конкретный человек. Любого можно отправить на любое дело, но результат зависит от навыка (1–5, средний 3): " +
                            "переговоры — покупка, ставки, сделки с жителями, продажа планет; геология — шахты, экстракторы, зонды; " +
                            "грязные дела — слухи и подкуп; работа с людьми — праздники, школы, учёба, колонисты.", _small);
            var performer = g.Staff.FirstOrDefault(m => m.Id == g.Current.PerformerId);
            Line("Действует", performer == null ? "лучший под каждое дело (авто)" : performer.Name);
            if (performer != null && ActionButton("Подбирать лучшего под дело", true))
                Later(() => Do(g.SetPerformer(-1)));
            GUILayout.Space(6);

            foreach (var m in g.Staff.ToList())
            {
                GUILayout.BeginVertical(GUI.skin.box);
                string salary = m.IsDirector ? "" : $", зарплата {Fmt.Money(m.Salary)}";
                GUILayout.Label($"<b>{m.Name}</b> — действий {m.ActionsLeft}/{m.ActionsPerTurn}{salary}", _text);
                GUILayout.Label(SkillsLine(m), _small);
                GUILayout.BeginHorizontal();
                int id = m.Id;
                if (performer != m && ActionButton("Поручать дела ему", m.ActionsLeft > 0))
                    Later(() => Do(g.SetPerformer(id)));
                if (!m.IsDirector && ActionButton("Уволить", true))
                    Act(() => g.FireManager(id), $"{m.Name} уволен(а).");
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(10);
            GUILayout.Label($"Кандидаты (нанято {g.Managers} из {b.MaxManagers})", _header);
            GUILayout.Label($"Новые кандидаты — раз в {b.CandidateRefreshTurns} ходов. Нанятый работает со следующего хода. Магнаты переманивают лучших.", _small);
            for (int i = 0; i < g.Candidates.Count; i++)
            {
                var c = g.Candidates[i];
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"<b>{c.Name}</b> — зарплата {Fmt.Money(c.Salary)} за ход, сильная сторона: {c.BestSkill.RuName()}", _text);
                GUILayout.Label(SkillsLine(c), _small);
                int index = i;
                if (ActionButton("Нанять · 1 действие", g.ActionsLeft > 0 && g.Managers < b.MaxManagers))
                    Act(() => g.HireManager(index), $"Нанят(а) {c.Name}.");
                GUILayout.EndVertical();
            }
        }

        /// <summary>Дерево технологий: что изучено, что идёт, что можно начать.</summary>
        void DrawDevelopmentTab()
        {
            var g = _game;
            GUILayout.Label("Исследования", _header);
            if (g.CurrentResearch != null)
            {
                var r = g.CurrentResearch;
                Line("Идёт", $"«{r.Technology.Name}», ещё {r.TurnsLeft} х.");
                DrawBar(1 - r.TurnsLeft / (float)r.Technology.Turns);
            }
            else
                GUILayout.Label("Сейчас ничего не исследуется. Одно исследование за раз, начало стоит действие.", _small);
            GUILayout.Space(8);

            foreach (var t in g.Balance.Technologies)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                string state = g.IsResearched(t.Id) ? "<color=#7be38b>изучено</color>"
                    : g.CurrentResearch?.Technology == t ? "<color=#ffd27a>исследуется</color>"
                    : "";
                GUILayout.Label($"<b>{t.Name}</b> {state}", _text);
                GUILayout.Label(t.Description, _small);
                if (!g.IsResearched(t.Id) && g.CurrentResearch?.Technology != t)
                {
                    var missing = t.Requires.Where(x => !g.IsResearched(x)).Select(x => g.Balance.Technologies.First(y => y.Id == x).Name).ToList();
                    if (missing.Count > 0)
                        GUILayout.Label("Сначала: " + string.Join(", ", missing), _small);
                    else
                    {
                        string id = t.Id;
                        double cost = g.ResearchCost(t);
                        if (ActionButton($"Исследовать за {Fmt.Money(cost)}, {t.Turns} х. · 1 действие",
                                g.CurrentResearch == null && g.ActionsLeft > 0 && g.Money >= cost))
                            Act(() => g.StartResearch(id), $"Начато исследование «{t.Name}».");
                    }
                }
                GUILayout.EndVertical();
            }

            GUILayout.Space(12);
            GUILayout.Label("Специальности жителей", _header);
            GUILayout.Label("Каждый уровень образования на ваших планетах усиливает бонус своей специальности. Учить жителей можно в окне планеты.", _small);
            var b = g.Balance;
            foreach (var sp in Specialties.All)
            {
                int levels = g.SpecialtyLevels(sp);
                double bonus = g.SpecialtyBonus(sp);
                string value = sp == Specialty.Biologists ? $"+{Fmt.Amount(bonus)} лояльности за ход" : Fmt.Percent(bonus);
                Line(sp.RuName(), $"{sp.BonusText()}: {value} (уровней {levels}, предел {(sp == Specialty.Biologists ? "+" + Fmt.Amount(b.SpecialtyBonusMax[(int)sp]) : Fmt.Percent(b.SpecialtyBonusMax[(int)sp]))})");
            }
            Line("Умственный труд", $"+{Fmt.Money(g.IntellectPerTurn)} за ход");
        }

        /// <summary>Основное и дополнительные месторождения планеты: что добывается, что восстанавливается.</summary>
        void DrawDeposits(Planet p, bool exact)
        {
            var g = _game;
            var b = g.Balance;
            GUILayout.Space(6);
            GUILayout.Label("Месторождения", _header);
            string main = p.IsRegenerating
                ? $"восстанавливается, ещё {p.RegenTurnsLeft} из {p.RegenTotal} х."
                : $"добыча {Fmt.Amount(p.Type.OutputPerTurn * (p.Owner == Owner.Player ? 1 + g.OutputBonus : 1))} за ход";
            Line($"{p.Resource.RuName()} (основное)", $"{main}; после истощения восстанавливается за {b.RegenTurns[(int)p.Resource]} х.");
            if (!exact)
            {
                GUILayout.Label("Дополнительные месторождения покажет полная разведка.", _small);
                return;
            }
            if (p.ExtraDeposits.Count == 0)
            {
                GUILayout.Label("Других месторождений нет.", _small);
                return;
            }
            for (int i = 0; i < p.ExtraDeposits.Count; i++)
            {
                var d = p.ExtraDeposits[i];
                string state = d.IsRegenerating ? $"восстанавливается, ещё {d.RegenTurnsLeft} х."
                    : d.HasExtractor ? (g.IsUnlocked(d.Resource) ? $"добывается, запас {Fmt.Amount(d.Reserve)} из {Fmt.Amount(d.Capacity)}" : "экстрактор есть, нет технологии")
                    : $"запас {Fmt.Amount(d.Capacity)}";
                Line($"{d.Resource.RuName()} (доп.)", $"{Fmt.Amount(d.OutputPerTurn)} за ход; {state}; восстановление {b.RegenTurns[(int)d.Resource]} х.");
                if (p.Owner == Owner.Player && !d.HasExtractor)
                {
                    int index = i;
                    double cost = g.ExtractorCost(d);
                    if (!g.IsUnlocked(d.Resource))
                    {
                        var tech = b.Technologies.FirstOrDefault(t => t.UnlocksResource == d.Resource);
                        GUILayout.Label($"Чтобы добывать, нужна технология «{tech?.Name}» (вкладка «Развитие»).", _small);
                    }
                    else if (ActionButton($"Поставить экстрактор за {Fmt.Money(cost)} · 1 действие", g.ActionsLeft > 0 && g.Money >= cost))
                        Act(() => g.BuildExtractor(p.Id, index), $"Экстрактор на {p.Type.Name} #{p.Id}: {d.Resource.RuName()}.");
                }
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
            int kindness = g.Kindness;
            Line("Как вы обходитесь с жителями", kindness > 0 ? $"<color=#7be38b>добряк ({kindness})</color>" : kindness < 0 ? $"<color=#ff8a7a>живодёр ({kindness})</color>" : "пока никак");
            GUILayout.Label("Магнаты помнят ваши поступки с жителями. Матильда не подкупает жителей у заботливых, Гуго щадит «родственную душу».", _small);
            Line("Внимание магнатов", $"{Fmt.Percent(g.MagnateAttention)}: чем больше у вас планет, тем чаще пакости");
            if (g.Rivalry > 1)
                GUILayout.Label($"<color=#ffd27a>Вы богаче среднего магната в {g.Rivalry:0.#} раза — лидера травят все, пакостей больше.</color>", _small);
            GUILayout.Label($"Жители с лояльностью от {Fmt.Amount(b.PoachScientistsBelowLoyalty)} не дают переманить своих учёных. " +
                            $"Обитаемые планеты не оспорить в реестре: жители своих не отдают.", _small);
            if (g.CartelActive)
                GUILayout.Label($"<color=#ff8a7a>Картель магнатов против вас до хода {g.Current.CartelUntilTurn}: биржа платит на {Fmt.Percent(g.CartelCut)} меньше.</color>", _text);
            else if (g.Rivalry >= b.CartelRivalry)
                GUILayout.Label($"<color=#ffd27a>Вы так богаты, что магнаты могут сговориться против вас (картель).</color> Репутация от {Fmt.Amount(b.CartelReputationShield)} смягчит удар вдвое.", _small);
            GUILayout.Space(10);

            foreach (var m in g.Magnates)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                int planets = g.MagnatePlanetCount(m);
                GUILayout.Label($"<b>{m.Name}</b>, {m.PersonalityName}. Планет: {planets}.", _text);
                string income = m.LastIncome >= 0 ? $"+{Fmt.Money(m.LastIncome)}" : Fmt.Money(m.LastIncome);
                GUILayout.Label($"Казна {Fmt.Money(m.Money)} ({income} за ход), состояние {Fmt.Money(g.MagnateWealth(m))}.", _small);
                GUILayout.Label(m.Personality == Personality.Greedy
                    ? "Скупает выгодные планеты, щедро ставит на аукционах, подкупает жителей с низкой лояльностью. Хвалит жёсткость с жителями."
                    : m.Personality == Personality.Chaotic
                        ? "Непредсказуема: ставки вразброс, распускает слухи, переманивает заказчиков и менеджеров."
                        : "Осторожна: ставит скромно, пакостит редко, переманивает заказчиков. Ценит заботу о жителях.", _small);
                if (g.Turn < m.QuietUntilTurn)
                    GUILayout.Label($"Отбивается от ваших слухов до хода {m.QuietUntilTurn}: не покупает и не пакостит.", _small);
                var said = g.RecentRemarks.LastOrDefault(x => x.MagnateId == m.Id);
                if (said != null) GUILayout.Label($"<i>«{said.Text}»</i> (ход {said.Turn})", _small);
                int id = m.Id;
                if (ActionButton($"Распустить слух о магнате за {Fmt.Money(b.PlayerRumorCost)}: {b.PlayerRumorQuietTurns} х. тишины · 1 действие",
                        g.ActionsLeft > 0 && g.Money >= b.PlayerRumorCost))
                    Later(() => Do(g.SpreadRumor(id)));
                GUILayout.Label($"Грязное дело: репутация {Fmt.Amount(b.PlayerRumorReputation)}, реестр ловит в {Fmt.Percent(b.FineChance)} случаев (штраф {Fmt.Money(b.FineAmount)}).", _small);
                GUILayout.EndVertical();
                GUILayout.Space(6);
            }

            var claims = g.Claims.Where(c => g.IsMine(g.GetPlanet(c.PlanetId))).ToList();
            if (claims.Count > 0)
            {
                GUILayout.Label("Рейдерские иски", _header);
                foreach (var x in claims) DrawClaim(x, g.GetPlanet(x.PlanetId));
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
                if (ActionButton($"Построить школу за {Fmt.Money(g.SchoolCost)} · {Doer(Skill.People)}", g.ActionsLeft > 0 && g.Money >= g.SchoolCost))
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
            if (g.CartelActive)
                GUILayout.Label($"<color=#ff8a7a>Картель магнатов: до хода {g.Current.CartelUntilTurn} вы получаете на {Fmt.Percent(g.CartelCut)} меньше за всё проданное.</color>", _text);
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
                if (GUILayout.Button($"½ ≈ {Fmt.Money(g.Quote(r, half))}", _button, GUILayout.Width(120), GUILayout.Height(30)))
                    Act(() => g.SellResource(r, half), $"Продано {Fmt.Amount(half)} ({r.RuName()}).");
                GUI.enabled = stock > 0;
                double revenue = g.Quote(r, stock);
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
            var claims = g.Claims.Where(c => g.IsMine(g.GetPlanet(c.PlanetId))).ToList();
            if (claims.Count > 0)
            {
                GUILayout.Label("Рейдерские иски", _header);
                foreach (var x in claims) DrawClaim(x, g.GetPlanet(x.PlanetId));
            }
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
            Line("Нанято", $"{g.Managers} из {b.MaxManagers}, зарплаты {Fmt.Money(g.SalariesPerTurn)} за ход — подробнее во вкладке «Команда»");

            GUILayout.Space(14);
            GUILayout.Label("Расходы за ход", _header);
            Line("Содержание планет", $"{Fmt.Money(g.UpkeepPerTurn)} ({Fmt.Multiplier(g.UpkeepMultiplierNow)} за {g.UpkeepCurvePlanets} раб. планет)");
            Line("Зарплаты", Fmt.Money(g.SalariesPerTurn));
            Line("Проценты", Fmt.Money(g.Debt * b.InterestPerTurn));
            Line("Страховка", Fmt.Money(g.InsurancePerTurn));
            Line("Доставка на биржу", Fmt.Money(g.ShippingPerTurn));
            Line("Зарплаты жителям", Fmt.Money(g.WagesPerTurn));
            Line("Охрана", Fmt.Money(g.GuardsPerTurn));
            Line("<b>Итого</b>", $"<b>{Fmt.Money(g.CostsPerTurn)}</b>");
            Line("Налоги с жителей", $"+{Fmt.Money(g.TaxesPerTurn)} за ход");
            Line("Умственный труд", $"+{Fmt.Money(g.IntellectPerTurn)} за ход");
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
            var size = _tooltipText.CalcSize(content) + new Vector2(16, 10);
            var mouse = Event.current.mousePosition;
            var r = new Rect(mouse.x + 16, mouse.y + 12, size.x, size.y);
            GUI.Box(r, GUIContent.none);
            GUI.Box(r, GUIContent.none);
            GUI.Label(new Rect(r.x + 8, r.y + 5, size.x, size.y), content, _tooltipText);
        }

        // ------------------------------------------------------------------
        // Стили и текстуры
        // ------------------------------------------------------------------

        void EnsureStyles()
        {
            if (_stylesReady) return;
            _text = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true, richText = true };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, richText = true };
            _small.normal.textColor = new Color(0.75f, 0.78f, 0.85f);
            _portraitButton = new GUIStyle(GUI.skin.button) { fontSize = 13 };
            _portraitHint = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = false };
            _portraitHint.normal.textColor = new Color(0.75f, 0.8f, 0.9f, 0.55f);
            _title = new GUIStyle(_text) { fontSize = 24, fontStyle = FontStyle.Bold };
            _header = new GUIStyle(_text) { fontSize = 17, fontStyle = FontStyle.Bold };
            _header.normal.textColor = new Color(0.6f, 0.75f, 1f);
            _button = new GUIStyle(GUI.skin.button) { fontSize = 15, wordWrap = false };
            _mapLabel = new GUIStyle(_small) { alignment = TextAnchor.UpperCenter, fontSize = 12, wordWrap = false };
            _tooltipText = new GUIStyle(_small) { wordWrap = false };
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
