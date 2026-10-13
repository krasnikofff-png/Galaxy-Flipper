using System;
using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;
using UnityEngine;
using PlanetTrader.Visuals;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Игра: карта сектора с разведкой, окно планеты, биржа, заказы, финансы и журнал.
    /// Вся экономика — в PlanetTrader.Core, здесь только показ и кнопки.
    /// Интерфейс — UI Toolkit (Resources/UI/Game.uxml и Game.uss); экран собирается в PlanetTraderApp.Screen.cs,
    /// карта — в PlanetTraderApp.Map.cs, вкладки правой панели — в PlanetTraderApp.Tabs.cs.
    /// </summary>
    public sealed partial class PlanetTraderApp : MonoBehaviour
    {
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

        // Планеты на карте — настоящие 3D-сферы своей камеры; интерфейс рисует поверх кольца и подписи.
        SectorMapView _map3d;
        PlanetPortrait _portrait;
        readonly List<MapPlanet> _mapPlanets = new List<MapPlanet>();
        readonly List<MapShip> _mapShips = new List<MapShip>();
        /// <summary>Радиус станции-биржи на карте, в точках интерфейса.</summary>
        const float ExchangeRadius = 30;
        /// <summary>Зерно облика планет: в каждой партии планеты выглядят по-новому.</summary>
        int _lookSeed;
        /// <summary>F3 — показ построек и событий на выбранной планете (только картинка); 0 — как в игре.</summary>
        int _buildingsDemo;
        static readonly string[] DemoNames =
        {
            "Показ построек выключен.",
            "Показ (F3): на выбранной планете все постройки — только в окне планеты, в игре их нет. «К постройкам» — крупно. Дальше F3 — стройка шахты.",
            "Показ (F3): шахта строится. Дальше F3 — беды: авария, забастовка, эпидемия.",
            "Показ (F3): авария на шахте, забастовка, эпидемия. Дальше F3 — обломки после метеорита.",
            "Показ (F3): метеорит разбил шахту. Дальше F3 — обломки после бунта.",
            "Показ (F3): бунтовщики разнесли шахту, забастовка продолжается. F3 — выключить показ.",
        };
        /// <summary>F4 — показать на выбранной планете другой вид жителей; −1 — вид, который на ней живёт.</summary>
        int _speciesShown = -1;
        /// <summary>Сколько ходов лежат обломки разбитой шахты (если новую не начали строить раньше).</summary>
        const int WreckTurns = 8;

        // Режим зонда: игрок выбирает тип и задание, потом кликает по карте.
        bool _probeMode;
        int _probeTypeIndex = 1;
        ProbeMission _probeMission = ProbeMission.ScanArea;

        /// <summary>
        /// Нажатия кнопок выполняются в Update, а не прямо в обработчике события:
        /// после действия панель пересобирается, и кнопка не должна удалять сама себя посреди клика.
        /// </summary>
        readonly List<Action> _pending = new List<Action>();

        void Later(Action action) => _pending.Add(action);

        /// <summary>Действие игрока: при успехе — запись в журнал, при отказе — причина.</summary>
        void Act(Func<ActionResult> action, string success)
        {
            if (Do(action())) Log(success);
        }

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
                _buildingsDemo = (_buildingsDemo + 1) % DemoNames.Length;
                Log(DemoNames[_buildingsDemo]);
            }
            // Показ видов жителей: только картинка в окне планеты.
            if (Input.GetKeyDown(KeyCode.F4))
            {
                _speciesShown = _speciesShown + 1 >= Residents.Count ? -1 : _speciesShown + 1;
                Log(_speciesShown < 0 ? "Жители снова свои (F4 — показать другие виды)."
                    : $"Показ (F4): жители — {Residents.SpeciesName(_speciesShown)}, {_speciesShown + 1} из {Residents.Count}. Видно в пикете, поселении и карантине; F3 — показать их.");
            }
            HandleKeys();
            if (RivalsTurn)
            {
                UpdateRivals();
                if (Time.unscaledTime - _rivalsTurnStarted >= RivalsTurnSeconds) FinishTurn();
            }
            if (_pending.Count > 0)
            {
                var actions = _pending.ToArray();
                _pending.Clear();
                foreach (var a in actions) a();
                MarkDirty();
            }
            // Сообщения ядра о том, что случилось во время действий (руины при покупке и т. п.).
            if (_game.Notices.Count > 0)
            {
                foreach (var n in _game.Notices) Log(n);
                _game.Notices.Clear();
                MarkDirty();
            }
            RefreshScreen();
        }

        void Awake()
        {
            _map3d = gameObject.AddComponent<SectorMapView>();
            _portrait = gameObject.AddComponent<PlanetPortrait>();
            CreateScreen();
            NewGame();
        }

        // Пока открыта витрина, интерфейс выключен — вместе с ним прячется и 3D-карта.
        void OnEnable()
        {
            if (_map3d != null) _map3d.enabled = true;
            if (_portrait != null) _portrait.enabled = true;
            ShowScreen(true);
        }

        void OnDisable()
        {
            if (_map3d != null) _map3d.enabled = false;
            if (_portrait != null) _portrait.enabled = false;
            ShowScreen(false);
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
                // Дым над картой: авария на шахте или свежие обломки (у силуэтов ничего не видно).
                Smoke = !silhouette && ((p.Mine == MineState.Working && p.AccidentTurnsLeft > 0)
                    || (p.Mine == MineState.None && p.MineWreckedTurn >= 0 && _game.Turn - p.MineWreckedTurn <= WreckTurns)),
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
                SpeciesOverride = _speciesShown >= 0 ? _speciesShown : (int?)null,
                Accident = p.Mine == MineState.Working && p.AccidentTurnsLeft > 0,
                Strike = p.Inhabited && p.OnStrike,
                Epidemic = p.Inhabited && p.EpidemicTurnsLeft > 0,
                // Обломки лежат, пока не начали строить новую шахту, но не дольше WreckTurns ходов.
                Wreck = p.Mine == MineState.None && p.MineWreckedTurn >= 0 && _game.Turn - p.MineWreckedTurn <= WreckTurns
                    ? (p.MineWreckedByRiot ? WreckLook.Riot : WreckLook.Meteor)
                    : WreckLook.None,
            };
        }

        /// <summary>F3: все постройки разом — посмотреть модели и их движение, не строя их в игре.</summary>
        PlanetStructures DemoStructures(Planet p)
        {
            var others = ResourceTypes.All.Where(r => r != p.Resource).Take(2).Select(Fmt.ResourceColor).ToArray();
            return new PlanetStructures
            {
                Mine = _buildingsDemo == 2 ? MineLook.Building : _buildingsDemo == 3 ? MineLook.Idle : _buildingsDemo >= 4 ? MineLook.None : MineLook.Working,
                Extractors = others,
                Settlement = true,
                School = true,
                Guard = true,
                Ruins = true,
                Accent = p.Owner == Owner.Registry ? Fmt.OwnerColor(Owner.Player, false) : RingColor(p),
                SpeciesOverride = _speciesShown >= 0 ? _speciesShown : (int?)null,
                Accident = _buildingsDemo == 3,
                Strike = _buildingsDemo == 3 || _buildingsDemo == 5,
                Epidemic = _buildingsDemo == 3,
                Wreck = _buildingsDemo == 4 ? WreckLook.Meteor : _buildingsDemo == 5 ? WreckLook.Riot : WreckLook.None,
            };
        }

        /// <summary>Расставить 3D-планеты, станцию и корабли по местам карты; показать окно планеты.</summary>
        void LateUpdate()
        {
            if (!LayoutMap()) return;
            _mapPlanets.Clear();
            foreach (var p in _game.AllPlanets)
            {
                var m = Describe(p);
                m.Center = ToPixels(ToMap(p.MapX, p.MapY));
                m.Radius = PlanetRadius(p) * _pixelScale;
                _mapPlanets.Add(m);
            }
            _map3d.Show(MapPixels, _mapPlanets);

            var b = _game.Balance;
            var exchange = ToPixels(ToMap(b.ExchangeX, b.ExchangeY));
            _map3d.ShowExchange(exchange, ExchangeRadius * _pixelScale);

            // Зонды игрока в полёте и грузовики добывающих планет (кроме силуэтов).
            _mapShips.Clear();
            foreach (var pr in _game.Probes)
                _mapShips.Add(new MapShip
                {
                    Key = pr.Id,
                    Probe = true,
                    From = ToPixels(ToMap(pr.FromX, pr.FromY)),
                    To = ToPixels(ToMap(pr.TargetX, pr.TargetY)),
                    Progress = (float)pr.Progress(_game.Turn),
                    Size = 15 * _pixelScale,
                    Accent = Fmt.OwnerColor(Owner.Player, false),
                });
            foreach (var p in _game.AllPlanets)
                if (p.IsProducing && p.Knowledge != Knowledge.Silhouette)
                    _mapShips.Add(new MapShip
                    {
                        Key = 1_000_000 + p.Id,
                        From = ToPixels(ToMap(p.MapX, p.MapY)),
                        To = exchange,
                        Size = 12 * _pixelScale,
                        Accent = RingColor(p),
                    });
            _map3d.ShowShips(_mapShips);

            ShowPortrait();
        }

        void NewGame()
        {
            var seed = (ulong)DateTime.Now.Ticks;
            _game = Core.Game.New(new Balance(), seed);
            _lookSeed = (int)(seed % int.MaxValue);
            _rivalsTurnStarted = -1;
            _selectedId = _game.PlayerPlanets.First().Id;
            _tab = Tab.Planet;
            _probeMode = false;
            ResetScreen();
            var b = _game.Balance;
            Log($"Новая партия. У вас ледяной карлик с шахтой, {Fmt.Money(b.StartMoney)} денег и долг {Fmt.Money(b.StartDebt)}.");
            Log($"За ход можно сделать {_game.ActionsPerTurn} действия: купить, построить шахту, продать планету, запустить зонд, нанять менеджера.");
            Log("Продажа ресурсов, заказы и кредит действий не тратят. Пробел или Enter — конец хода.");
            Log("Серые планеты — силуэты: тип и запас неизвестны. Кнопка «Зонды» — скан области или полная разведка планеты.");
            Log("Соперники: " + string.Join(" и ", _game.Magnates.Select(m => $"{m.Name} ({m.PersonalityName})")) + ". Чем больше у вас планет, тем больше они пакостят.");
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

        internal void Select(int planetId)
        {
            _selectedId = planetId;
            MarkDirty();
        }

        internal void ShowTab(int tab) => Later(() => _tab = (Tab)Mathf.Clamp(tab, 0, TabNames.Length - 1));
        internal void SetProbeMode(bool on) => Later(() => _probeMode = on);
        internal void StartRivalsTurn() => Later(RequestEndTurn);

        // ------------------------------------------------------------------
        // Ход
        // ------------------------------------------------------------------

        /// <summary>«Конец хода»: сначала пауза на ход соперников, потом сам ход.</summary>
        void RequestEndTurn()
        {
            if (RivalsTurn) return;
            _probeMode = false;
            _rivalsTurnStarted = Time.unscaledTime;
            ShowRivals(true);
        }

        void FinishTurn()
        {
            if (!RivalsTurn) return;
            _rivalsTurnStarted = -1;
            ShowRivals(false);
            EndTurn();
            MarkDirty();
        }

        /// <summary>Пробел или Enter — конец хода; Esc — выйти из режима зонда. Пока ходят соперники, любая клавиша или клик — пропустить паузу.</summary>
        void HandleKeys()
        {
            if (RivalsTurn)
            {
                if (Input.anyKeyDown) Later(FinishTurn);
                return;
            }
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                Later(RequestEndTurn);
            else if (Input.GetKeyDown(KeyCode.Escape) && _probeMode)
                Later(() => _probeMode = false);
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

        /// <summary>Коротко, что галактическое событие меняет.</summary>
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

        // ------------------------------------------------------------------
        // Подписи
        // ------------------------------------------------------------------

        /// <summary>Цвет кольца: владелец, у магнатов — свой цвет у каждого.</summary>
        static Color RingColor(Planet p) => RingColor(p.Owner, p.MagnateId, p.FoundByProbe);

        static Color RingColor(Owner owner, int magnateId, bool foundByProbe) =>
            owner == Owner.Ai && magnateId == 1 ? new Color(1f, 0.6f, 0.15f) : Fmt.OwnerColor(owner, foundByProbe);

        static float PlanetRadius(Planet p)
        {
            // Чем дороже тип планеты, тем она крупнее на карте.
            return 13 + Mathf.Clamp((float)(p.Type.Price - 1600) / 2600f, 0, 1) * 11;
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
    }
}
