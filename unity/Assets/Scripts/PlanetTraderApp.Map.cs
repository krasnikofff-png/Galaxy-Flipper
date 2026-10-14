using System.Collections.Generic;
using System.Linq;
using PlanetTrader.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Карта сектора. Планеты, станцию и корабли рисует 3D-камера (SectorMapView) в прямоугольнике карты;
    /// интерфейс поверх рисует орбиты, торговые пути, кольца владельцев, прицел выбранной планеты, пунктир зондов
    /// и круг скана (Painter2D), подписи, легенду и кнопки приближения, и ловит клики по планетам.
    /// </summary>
    public sealed partial class PlanetTraderApp
    {
        Label _sectorName, _eventLine, _zoomValue, _probesText, _probeInfo;
        Button _probesButton;

        /// <summary>Подпись на карте: имя (магнат, «ВЫ», «Биржа») и строка с ценой или пометкой.</summary>
        sealed class MapLabelView
        {
            public VisualElement Root, PriceRow, Coin;
            public Label Name, Price;
        }

        static readonly string[] SectorNames =
            { "Алгол", "Вега", "Ригель", "Денеб", "Капелла", "Альтаир", "Антарес", "Бетельгейзе", "Мира", "Спика", "Поллукс", "Процион" };
        /// <summary>Карту приблизили или сдвинули — переставить подписи (правую панель пересобирать незачем).</summary>
        bool _mapDirty;
        VisualElement _map, _mapCanvas, _mapLabels, _legend, _probeBar, _probeTypes, _probeMissions;
        readonly List<MapLabelView> _mapLabelPool = new List<MapLabelView>();
        /// <summary>Курсор над картой, в точках относительно карты; null — курсор не над картой.</summary>
        Vector2? _mapPointer;
        /// <summary>Квадрат с планетами в точках относительно карты: координаты 0..1 планет растягиваются на него.</summary>
        Rect _mapArea;

        // Приближение карты: колесо — ближе или дальше к курсору, перетаскивание — сдвиг.
        const float MaxZoom = 5;
        float _zoom = 1;
        /// <summary>Сдвиг приближенной карты, в точках.</summary>
        Vector2 _pan;
        /// <summary>Карту тянут мышью: откуда начали и сдвинули ли уже (тогда отпускание — не клик).</summary>
        bool _panning, _panMoved;
        Vector2 _panStart;

        static readonly string[] MissionNames = { "Скан области", "Разведка планеты" };

        void CreateMap(VisualElement root)
        {
            _map = root.Q("map");
            _mapCanvas = root.Q("mapCanvas");
            _mapLabels = root.Q("mapLabels");
            _legend = root.Q("legend");
            _sectorName = root.Q<Label>("sectorName");
            _eventLine = root.Q<Label>("eventLine");
            _zoomValue = root.Q<Label>("zoomValue");
            _probesText = root.Q<Label>("probesText");
            _probeInfo = root.Q<Label>("probeInfo");
            _probesButton = root.Q<Button>("probesButton");
            _probesButton.clicked += () => Later(() => _probeMode = !_probeMode);
            root.Q<Button>("zoomInButton").clicked += () => ZoomMap(-2, _map.contentRect.center);
            root.Q<Button>("zoomOutButton").clicked += () => ZoomMap(2, _map.contentRect.center);
            root.Q<Button>("centerButton").clicked += () =>
            {
                _zoom = 1;
                _pan = Vector2.zero;
                _mapDirty = true;
            };
            _probeBar = root.Q("probeBar");
            _probeTypes = root.Q("probeTypes");
            _probeMissions = root.Q("probeMissions");
            root.Q<Button>("probeDone").clicked += () => Later(() => _probeMode = false);

            _mapCanvas.generateVisualContent += PaintMap;
            _mapCanvas.RegisterCallback<PointerMoveEvent>(e => OnMapPointer(e.localPosition, e.position, e.deltaPosition));
            _mapCanvas.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                _mapPointer = null;
                HideTooltip();
            });
            _mapCanvas.RegisterCallback<PointerDownEvent>(OnMapDown);
            _mapCanvas.RegisterCallback<PointerUpEvent>(OnMapUp);
            _mapCanvas.RegisterCallback<WheelEvent>(e =>
            {
                ZoomMap(e.delta.y, e.localMousePosition);
                e.StopPropagation();
            });
            // Карта сменила размер — подписи на новые места.
            _map.RegisterCallback<GeometryChangedEvent>(_ => MarkDirty());
        }

        /// <summary>Новая партия: другие магнаты в легенде, свои типы зондов.</summary>
        void ResetMap()
        {
            _zoom = 1;
            _pan = Vector2.zero;
            _probeTypeIndex = 1;
            _probeMission = ProbeMission.ScanArea;
            var types = _game.Balance.ProbeTypes;
            PanelBuilder.MakeChoice(types.Select(t => $"{t.Name} · {Fmt.Money(t.Cost)}").ToList(), _probeTypeIndex,
                i => Later(() => _probeTypeIndex = i), _probeTypes);
            PanelBuilder.MakeChoice(MissionNames, (int)_probeMission, i => Later(() => _probeMission = (ProbeMission)i), _probeMissions);

            _sectorName.text = "Сектор " + SectorNames[(int)((uint)_lookSeed % SectorNames.Length)];

            _legend.Clear();
            LegendItem(Fmt.OwnerColor(Owner.Player, false), "Ваши");
            foreach (var m in _game.Magnates)
                LegendItem(RingColor(Owner.Ai, m.Id, false), m.Name.Split(' ')[0]);
            LegendItem(Fmt.OwnerColor(Owner.Registry, false), "Свободные (цена)");
            LegendItem(Fmt.OwnerColor(Owner.Registry, true), "Нашёл ваш зонд");
            var route = new VisualElement();
            route.AddToClassList("legend-item");
            var dash = new VisualElement();
            dash.AddToClassList("route-dash");
            route.Add(dash);
            route.Add(new Label("Торговые пути"));
            _legend.Add(route);
        }

        void LegendItem(Color color, string text)
        {
            var item = new VisualElement();
            item.AddToClassList("legend-item");
            var dot = new VisualElement();
            dot.AddToClassList("dot");
            dot.style.backgroundColor = color;
            item.Add(dot);
            item.Add(new Label(text));
            _legend.Add(item);
        }

        /// <summary>Квадрат карты внутри её прямоугольника. false — карта ещё не размечена.</summary>
        bool LayoutMap()
        {
            var r = _map.contentRect;
            if (float.IsNaN(r.width) || r.width < 100 || r.height < 100) return false;
            // Карта квадратная, чтобы расстояния и радиусы на экране не искажались.
            // Сверху — заголовок и легенда (и полоска зонда), снизу — кнопки приближения.
            float top = _probeMode ? 150 : 70;
            float side = Mathf.Min(r.width - 60, r.height - top - 58);
            _mapArea = new Rect((r.width - side) / 2, top, side, side);
            return true;
        }

        /// <summary>Сторона квадрата карты на экране 1600×900: при ней планеты, станция и корабли — своего обычного размера.</summary>
        const float ReferenceMapSide = 536;

        /// <summary>
        /// Во сколько раз карта больше или меньше обычной. Планеты, станция и корабли растут и уменьшаются вместе с ней,
        /// иначе на маленьком окне планеты наезжали бы друг на друга. При приближении они растут медленнее расстояний
        /// (как корень из приближения) — тесные скопления расходятся.
        /// </summary>
        float MapScale => Mathf.Clamp(_mapArea.width / ReferenceMapSide, 0.35f, 2f) * Mathf.Sqrt(_zoom);

        /// <summary>Радиус планеты на карте в точках интерфейса.</summary>
        float MapRadius(Planet p) => PlanetRadius(p) * MapScale;

        /// <summary>Точка сектора (0..1) → точка карты на экране с учётом приближения и сдвига.</summary>
        Vector2 ToMap(double x, double y)
        {
            var flat = new Vector2(_mapArea.x + (float)x * _mapArea.width, _mapArea.y + (float)y * _mapArea.height);
            return _mapArea.center + (flat - _mapArea.center) * _zoom + _pan;
        }

        /// <summary>Точка карты на экране → точка сектора (0..1).</summary>
        Vector2 FromMap(Vector2 local)
        {
            var flat = (local - _pan - _mapArea.center) / _zoom + _mapArea.center;
            return new Vector2((flat.x - _mapArea.x) / _mapArea.width, (flat.y - _mapArea.y) / _mapArea.height);
        }

        /// <summary>Колесо над картой: приблизить или отдалить, не сдвигая точку под курсором.</summary>
        void ZoomMap(float wheel, Vector2 at)
        {
            if (!LayoutMap()) return;
            var sector = FromMap(at);
            _zoom = Mathf.Clamp(_zoom * Mathf.Pow(1.15f, -Mathf.Clamp(wheel, -5, 5)), 1, MaxZoom);
            _pan = Vector2.zero;
            _pan = at - ToMap(sector.x, sector.y);
            ClampPan();
            _mapDirty = true;
        }

        /// <summary>Не уводить сектор за край: при отдалении до конца карта снова по центру.</summary>
        void ClampPan()
        {
            var limit = _mapArea.size * ((_zoom - 1) / 2);
            _pan = new Vector2(Mathf.Clamp(_pan.x, -limit.x, limit.x), Mathf.Clamp(_pan.y, -limit.y, limit.y));
        }

        /// <summary>Точка карты → пиксели экрана от левого верхнего угла (так считает 3D-карта).</summary>
        Vector2 ToPixels(Vector2 local) => (_map.worldBound.position + local) * _pixelScale;

        Rect MapPixels
        {
            get
            {
                var w = _map.worldBound;
                return new Rect(w.x * _pixelScale, w.y * _pixelScale, w.width * _pixelScale, w.height * _pixelScale);
            }
        }

        ProbeType SelectedProbe => _game.Balance.ProbeTypes[Mathf.Clamp(_probeTypeIndex, 0, _game.Balance.ProbeTypes.Length - 1)];

        void RefreshMap()
        {
            _mapDirty = false;
            _probeBar.EnableInClassList("hidden", !_probeMode);
            _probesButton.EnableInClassList("selected", _probeMode);
            _probeInfo.EnableInClassList("hidden", !_probeMode);
            _probeInfo.text = $"«{SelectedProbe.Name}», {Fmt.Money(SelectedProbe.Cost)}. {ProbeDescription(SelectedProbe)} " +
                (_probeMission == ProbeMission.ScanArea
                    ? "Скан области: кликните по карте — все планеты в круге станут известны (тип и оценка реестра)."
                    : "Разведка планеты: кликните по планете — узнаете точный запас, подвохи и руины. Без разведки с жителями не поговорить.");
            _probesText.text = _game.Probes.Count > 0 ? $"Зонды ({_game.Probes.Count} в полёте)" : "Зонды";
            _zoomValue.text = $"{Mathf.RoundToInt(_zoom * 100)}%";
            RefreshEventLine();
            PanelBuilder.Select(_probeTypes, _probeTypeIndex);
            PanelBuilder.Select(_probeMissions, (int)_probeMission);
            if (!LayoutMap()) return;

            int used = 0;
            var b = _game.Balance;
            // Биржа — 3D-станция карты; здесь только подпись.
            var ex = ToMap(b.ExchangeX, b.ExchangeY);
            MapLabel(ref used, ex + new Vector2(0, ExchangeRadius * MapScale * 0.8f), "Биржа", Color.white, null, false);
            foreach (var pr in _game.Probes)
                MapLabel(ref used, ToMap(pr.TargetX, pr.TargetY) - new Vector2(0, 26), null, default, $"зонд: {pr.ResolveTurn - _game.Turn + 1} х.", false);
            foreach (var p in _game.AllPlanets)
            {
                var at = ToMap(p.MapX, p.MapY) + new Vector2(0, MapRadius(p) + 4);
                string marks = "";
                if (p.Knowledge == Knowledge.Explored && p.HasUnlootedRuins) marks += " · руины";
                if (p.Knowledge != Knowledge.Silhouette && p.Inhabited) marks += " · жители";
                switch (p.Owner)
                {
                    case Owner.Player:
                        string note = p.IsRegenerating ? $"восст. {p.RegenTurnsLeft} х." : p.IsDepleted ? "пусто"
                            : p.Inhabited ? (p.OnStrike ? "забастовка" : $"лоял. {Mathf.RoundToInt((float)p.Loyalty)}") : null;
                        MapLabel(ref used, at, "ВЫ", RingColor(p), note, false);
                        break;
                    case Owner.Ai:
                        MapLabel(ref used, at, _game.GetMagnate(p.MagnateId)?.Name.Split(' ')[0] ?? "ИИ", RingColor(p), null, false);
                        break;
                    default:
                        if (!p.IsDepleted) MapLabel(ref used, at, null, default, Fmt.Money(p.ListPrice(b)) + marks, true);
                        break;
                }
            }
            for (int i = used; i < _mapLabelPool.Count; i++) _mapLabelPool[i].Root.AddToClassList("hidden");
        }

        /// <summary>
        /// Подпись на карте с центром по x в точке at: имя цветом владельца (или без имени)
        /// и строка под ним — цена с монеткой или пометка.
        /// </summary>
        void MapLabel(ref int used, Vector2 at, string name, Color nameColor, string line, bool coin)
        {
            if (used == _mapLabelPool.Count)
            {
                var v = new MapLabelView { Root = new VisualElement(), Name = new Label(), PriceRow = new VisualElement(), Coin = Icons.Make("coin"), Price = new Label() };
                v.Root.AddToClassList("map-label");
                v.Root.pickingMode = PickingMode.Ignore;
                v.Name.AddToClassList("map-label-name");
                v.PriceRow.AddToClassList("map-label-price");
                v.Price.AddToClassList("label-text");
                v.PriceRow.Add(v.Coin);
                v.PriceRow.Add(v.Price);
                v.Root.Add(v.Name);
                v.Root.Add(v.PriceRow);
                v.Root.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
                _mapLabels.Add(v.Root);
                _mapLabelPool.Add(v);
            }
            var label = _mapLabelPool[used++];
            label.Root.RemoveFromClassList("hidden");
            label.Name.EnableInClassList("hidden", name == null);
            label.Name.text = name ?? "";
            // Цвет имени — цвет владельца на карте: данные, а не оформление.
            if (name != null) label.Name.style.color = nameColor;
            label.PriceRow.EnableInClassList("hidden", line == null);
            label.Price.text = line ?? "";
            label.Coin.EnableInClassList("hidden", !coin);
            label.Root.style.left = at.x - 75;
            label.Root.style.top = at.y;
        }

        /// <summary>Строка под заголовком карты: идущее галактическое событие или затишье.</summary>
        void RefreshEventLine()
        {
            var g = _game;
            var ev = g.CurrentEvent;
            _eventLine.EnableInClassList("active", ev != null);
            _eventLine.text = ev == null
                ? $"Затишье. Следующее галактическое событие — не раньше хода {Mathf.Max(g.NextEventTurn, g.Turn + 1)}."
                : $"<b>{ev.Event.Name}</b> (ещё {ev.EndTurn - g.Turn + 1} х.): {ev.Event.Description} {EventEffects(ev.Event)}";
        }

        // ------------------------------------------------------------------
        // Рисование поверх 3D-карты
        // ------------------------------------------------------------------

        void PaintMap(MeshGenerationContext ctx)
        {
            if (_game == null || !LayoutMap()) return;
            var paint = ctx.painter2D;
            var g = _game;
            var b = g.Balance;

            // Орбиты вокруг биржи — едва заметные, чтобы карта читалась как сектор.
            foreach (float orbit in new[] { 0.17f, 0.31f, 0.45f })
                MapRing(paint, b.ExchangeX, b.ExchangeY, orbit, new Color(1, 1, 1, 0.045f), 1);

            // Торговые пути: пунктир от добывающих планет к бирже (по ним летают грузовики).
            var ex = ToMap(b.ExchangeX, b.ExchangeY);
            foreach (var p in g.AllPlanets)
            {
                if (!p.IsProducing || p.Knowledge == Knowledge.Silhouette) continue;
                var from = ToMap(p.MapX, p.MapY);
                float length = Vector2.Distance(from, ex);
                for (float d = MapRadius(p) + 6; d < length - ExchangeRadius * MapScale * 0.7f; d += 7)
                    Disc(paint, Vector2.Lerp(from, ex, d / length), 1.1f, new Color(0.95f, 0.62f, 0.2f, 0.55f));
            }

            if (_probeMode)
            {
                // Дальность выбранного зонда от каждой точки запуска.
                var range = new Color(0.4f, 0.7f, 1f, 0.25f);
                MapRing(paint, b.ExchangeX, b.ExchangeY, SelectedProbe.Range, range);
                foreach (var own in g.PlayerPlanets)
                    MapRing(paint, own.MapX, own.MapY, SelectedProbe.Range, range);
            }

            // Зонды в полёте: пунктир до цели; сам зонд — 3D-модель на карте.
            foreach (var pr in g.Probes)
            {
                var from = ToMap(pr.FromX, pr.FromY);
                var to = ToMap(pr.TargetX, pr.TargetY);
                for (float t = 0; t <= 1; t += 0.05f) Disc(paint, Vector2.Lerp(from, to, t), 1.5f, new Color(0.6f, 0.8f, 1f, 0.5f));
                if (pr.Mission == ProbeMission.ScanArea)
                    MapRing(paint, pr.TargetX, pr.TargetY, pr.Type.ScanRadius, new Color(0.6f, 0.9f, 1f, 0.6f));
            }

            foreach (var p in g.AllPlanets)
            {
                var center = ToMap(p.MapX, p.MapY);
                float radius = MapRadius(p);
                // Сама планета — 3D-сфера под интерфейсом; здесь только кольца вокруг неё.
                if (p.Id == _selectedId) Brackets(paint, center, radius + 9, new Color(0.45f, 0.95f, 0.85f));
                Ring(paint, center, radius + 4, RingColor(p), 0.8f);
                if (p.Knowledge == Knowledge.Explored && p.Owner != Owner.Player)
                    Disc(paint, center + new Vector2(radius + 2, -radius - 2), 3.5f, Color.white);
            }

            // Режим скана: круг скана под курсором — зелёный, если зонд долетит.
            if (_probeMode && _probeMission == ProbeMission.ScanArea && _mapPointer is Vector2 at && _mapArea.Contains(at) && HitPlanet(at) == null)
            {
                var point = FromMap(at);
                bool reach = g.InRange(SelectedProbe, point.x, point.y);
                MapRing(paint, point.x, point.y, SelectedProbe.ScanRadius, reach ? new Color(0.5f, 1f, 0.6f, 0.8f) : new Color(1f, 0.4f, 0.4f, 0.8f));
            }
        }

        /// <summary>Кольцо радиусом radius в координатах карты (0..1).</summary>
        void MapRing(Painter2D paint, double x, double y, double radius, Color color, float width = 2) =>
            Ring(paint, ToMap(x, y), (float)radius * _mapArea.width * _zoom, color, width);

        /// <summary>Прицел выбранной планеты: четыре уголка вокруг неё.</summary>
        static void Brackets(Painter2D paint, Vector2 center, float half, Color color)
        {
            float arm = half * 0.45f;
            paint.strokeColor = color;
            paint.lineWidth = 1.6f;
            paint.lineCap = LineCap.Butt;
            foreach (var corner in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) })
            {
                var c = center + corner * half;
                paint.BeginPath();
                paint.MoveTo(c - new Vector2(0, corner.y * arm));
                paint.LineTo(c);
                paint.LineTo(c - new Vector2(corner.x * arm, 0));
                paint.Stroke();
            }
        }

        static void Ring(Painter2D paint, Vector2 center, float radius, Color color, float width)
        {
            paint.strokeColor = color;
            paint.lineWidth = width;
            paint.BeginPath();
            paint.Arc(center, radius, Angle.Degrees(0), Angle.Degrees(360));
            paint.ClosePath();
            paint.Stroke();
        }

        static void Disc(Painter2D paint, Vector2 center, float radius, Color color)
        {
            paint.fillColor = color;
            paint.BeginPath();
            paint.Arc(center, radius, Angle.Degrees(0), Angle.Degrees(360));
            paint.ClosePath();
            paint.Fill();
        }

        // ------------------------------------------------------------------
        // Мышь над картой
        // ------------------------------------------------------------------

        /// <summary>Планета под точкой карты (с запасом на кольцо) или null.</summary>
        Planet HitPlanet(Vector2 local)
        {
            Planet best = null;
            float bestDistance = float.MaxValue;
            foreach (var p in _game.AllPlanets)
            {
                float d = Vector2.Distance(local, ToMap(p.MapX, p.MapY));
                if (d <= MapRadius(p) + 6 && d < bestDistance)
                {
                    best = p;
                    bestDistance = d;
                }
            }
            return best;
        }

        /// <summary>
        /// Нажатие на карте: левой кнопкой по планете — выбрать её (в режиме зонда — запустить зонд);
        /// по пустому месту или правой кнопкой — тянуть карту. В режиме скана клик по пустому месту запускает зонд.
        /// </summary>
        void OnMapDown(PointerDownEvent e)
        {
            Vector2 local = e.localPosition;
            bool scan = _probeMode && _probeMission == ProbeMission.ScanArea;
            if (e.button == 0 && (HitPlanet(local) != null || scan))
            {
                OnMapClick(local);
                return;
            }
            if (e.button > 2) return;
            _panning = true;
            _panMoved = false;
            _panStart = local;
            _mapCanvas.CapturePointer(e.pointerId);
        }

        void OnMapUp(PointerUpEvent e)
        {
            if (!_panning) return;
            _panning = false;
            if (_mapCanvas.HasPointerCapture(e.pointerId)) _mapCanvas.ReleasePointer(e.pointerId);
        }

        void OnMapPointer(Vector2 local, Vector2 screenPoint, Vector2 delta)
        {
            _mapPointer = local;
            if (_panning)
            {
                if (!_panMoved && (local - _panStart).sqrMagnitude < 16) return;
                _panMoved = true;
                _pan += delta;
                ClampPan();
                _mapDirty = true;
                HideTooltip();
                return;
            }
            if (!LayoutMap()) return;
            var p = HitPlanet(local);
            if (p != null)
                ShowTooltip(PlanetTooltip(p), screenPoint);
            else if (_probeMode && _probeMission == ProbeMission.ScanArea && _mapArea.Contains(local))
            {
                var point = FromMap(local);
                ShowTooltip(ProbeHint(point.x, point.y, ProbeMission.ScanArea), screenPoint);
            }
            else
                HideTooltip();
        }

        void OnMapClick(Vector2 local)
        {
            if (RivalsTurn || !LayoutMap()) return;
            var p = HitPlanet(local);
            if (p != null)
            {
                int id = p.Id;
                double x = p.MapX, y = p.MapY;
                if (_probeMode && _probeMission == ProbeMission.Explore)
                    Later(() => LaunchExplore(id));
                else if (_probeMode)
                    Later(() => LaunchScan(x, y));
                else
                    Later(() =>
                    {
                        _selectedId = id;
                        _tab = Tab.Planet;
                    });
            }
            else if (_probeMode && _probeMission == ProbeMission.ScanArea && _mapArea.Contains(local))
            {
                var point = FromMap(local);
                Later(() => LaunchScan(point.x, point.y));
            }
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
    }
}
