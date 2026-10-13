using System.Collections.Generic;
using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>Планета на 3D-карте: где она на экране и как выглядит.</summary>
    public struct MapPlanet
    {
        public int Id;
        /// <summary>Центр в пикселях экрана, отсчёт от левого верхнего угла (как в IMGUI).</summary>
        public Vector2 Center;
        /// <summary>Радиус в пикселях.</summary>
        public float Radius;
        public PlanetLook Look;
        public int Seed;
        /// <summary>Только силуэт: тип неизвестен, планета серая.</summary>
        public bool Silhouette;
        /// <summary>Выработана: тусклая.</summary>
        public bool Depleted;
        /// <summary>Над планетой дым: авария на шахте или свежие обломки.</summary>
        public bool Smoke;

        public float Saturation => Silhouette ? 0f : Depleted ? 0.35f : 1f;
        public float Brightness => Silhouette ? 0.7f : Depleted ? 0.55f : 1f;
    }

    /// <summary>Корабль на карте: зонд в полёте или грузовик между добывающей планетой и биржей.</summary>
    public struct MapShip
    {
        /// <summary>Постоянный ключ корабля (номер зонда, номер планеты грузовика).</summary>
        public int Key;
        public bool Probe;
        /// <summary>Откуда и куда, в пикселях экрана (от левого верхнего угла).</summary>
        public Vector2 From, To;
        /// <summary>Зонд: доля пути (корабль плавно догоняет её). Грузовик сам летает туда-обратно.</summary>
        public float Progress;
        /// <summary>Длина корабля в пикселях.</summary>
        public float Size;
        public Color Accent;
    }

    /// <summary>
    /// Карта сектора в 3D: своя камера рисует настоящие вращающиеся планеты в прямоугольнике карты,
    /// а интерфейс (кольца владельцев, подписи, клики) рисуется поверх. Единица мира — пиксель экрана,
    /// поэтому интерфейс передаёт координаты прямо в пикселях.
    /// </summary>
    public sealed class SectorMapView : MonoBehaviour
    {
        /// <summary>Ширина текстур планеты на карте: планета там не больше 50 пикселей.</summary>
        const int Resolution = 256;
        /// <summary>Ширина черновой текстуры: пока карта не допеклась, планеты видны мыльными, но сразу.</summary>
        const int PreviewResolution = 64;
        /// <summary>Мир карты далеко от начала координат, чтобы не попадать в другие камеры.</summary>
        static readonly Vector3 Origin = new Vector3(0, -10000, 0);

        sealed class Entry
        {
            public PlanetView View;
            public PlanetLook Look;
            public int Seed;
            public bool Seen;
            public GameObject Smoke;
        }

        sealed class Ship
        {
            public Transform Holder;
            public float Shown = -1;
            public bool Seen;
        }

        readonly Dictionary<int, Ship> _ships = new Dictionary<int, Ship>();
        readonly List<int> _goneShips = new List<int>();
        Transform _station;
        /// <summary>Цвет биржи: жёлтые полосы и модули станции.</summary>
        static readonly Color ExchangeColor = new Color(0.95f, 0.8f, 0.3f);

        Transform _root;
        Camera _camera;
        Transform _backdrop;
        Mesh _backdropMesh;
        Material _backdropMat;
        Texture2D _stars;
        readonly Dictionary<int, Entry> _planets = new Dictionary<int, Entry>();
        readonly List<int> _gone = new List<int>();
        Rect _box;
        Vector2Int _screen;

        void Awake()
        {
            _root = new GameObject("Карта сектора 3D").transform;
            _root.position = Origin;
            Object.DontDestroyOnLoad(_root.gameObject);

            _camera = new GameObject("Камера карты").AddComponent<Camera>();
            _camera.transform.SetParent(_root, false);
            _camera.transform.localPosition = new Vector3(0, 0, -200);
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.01f, 0.012f, 0.03f);
            _camera.nearClipPlane = 1;
            _camera.farClipPlane = 400;
            _camera.depth = 0;
            _camera.allowHDR = false;
            _camera.allowMSAA = true;

            // Солнце сверху слева и чуть спереди — как в витрине.
            var sun = new GameObject("Солнце карты").AddComponent<Light>();
            sun.transform.SetParent(_root, false);
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.LookRotation(-new Vector3(-0.5f, 0.4f, -0.78f).normalized);

            _stars = BakeStars(1024, 4242);
            _backdropMat = new Material(Shader.Find("PlanetTrader/PlanetSky"));
            _backdropMat.SetTexture("_MainTex", _stars);
            _backdropMat.SetFloat("_Cull", 0);
            _backdropMesh = new Mesh { name = "Фон карты" };
            _backdrop = new GameObject("Фон карты").transform;
            _backdrop.SetParent(_root, false);
            _backdrop.localPosition = new Vector3(0, 0, 150);
            _backdrop.gameObject.AddComponent<MeshFilter>().sharedMesh = _backdropMesh;
            _backdrop.gameObject.AddComponent<MeshRenderer>().sharedMaterial = _backdropMat;
        }

        void OnEnable()
        {
            if (_root != null) _root.gameObject.SetActive(true);
        }

        void OnDisable()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        /// <summary>
        /// Показать планеты. box — прямоугольник карты в пикселях экрана (от левого верхнего угла).
        /// Звать каждый кадр: новые планеты появятся, пропавшие исчезнут, сменившие облик перезапекутся.
        /// </summary>
        public void Show(Rect box, IReadOnlyList<MapPlanet> planets)
        {
            if (box.width < 1 || box.height < 1) return;
            if (box != _box || _screen.x != Screen.width || _screen.y != Screen.height) Frame(box);

            foreach (var e in _planets.Values) e.Seen = false;
            foreach (var p in planets)
            {
                if (!_planets.TryGetValue(p.Id, out var e))
                {
                    e = new Entry { View = Create(p), Look = p.Look, Seed = p.Seed };
                    _planets.Add(p.Id, e);
                    e.View.RebuildAsync();
                }
                else if (e.Look != p.Look || e.Seed != p.Seed)
                {
                    // Скан открыл тип планеты — запекаем настоящий облик, старый виден, пока новый не готов.
                    e.Look = e.View.Look = p.Look;
                    e.Seed = e.View.Seed = p.Seed;
                    e.View.Shared = p.Silhouette;
                    e.View.RebuildAsync();
                }
                e.Seen = true;

                e.View.SetMood(p.Saturation, p.Brightness);

                var t = e.View.transform.parent;
                t.localPosition = new Vector3(p.Center.x - _box.center.x, _box.center.y - p.Center.y, 0);
                t.localScale = Vector3.one * p.Radius;

                // Дым над планетой с бедой: поднимается вверх по экрану.
                if (p.Smoke && e.Smoke == null)
                {
                    e.Smoke = new GameObject("Дым");
                    e.Smoke.transform.SetParent(t, false);
                    var marker = new GameObject("map__smoke_map").transform;
                    marker.SetParent(e.Smoke.transform, false);
                    marker.localPosition = new Vector3(0.15f, 0.75f, -0.3f);
                    SmokeEmitter.Attach(e.Smoke, (p.Id * 0.137f) % 1, true);
                }
                if (e.Smoke != null && e.Smoke.activeSelf != p.Smoke) e.Smoke.SetActive(p.Smoke);
            }

            _gone.Clear();
            foreach (var kv in _planets)
                if (!kv.Value.Seen) _gone.Add(kv.Key);
            foreach (int id in _gone)
            {
                Destroy(_planets[id].View.transform.parent.gameObject);
                _planets.Remove(id);
            }
        }

        /// <summary>Биржа в центре сектора: станция с вращающимся кольцом, повёрнутая к нам на три четверти.</summary>
        public void ShowExchange(Vector2 center, float radius)
        {
            if (_station == null)
            {
                _station = new GameObject("Биржа").transform;
                _station.SetParent(_root, false);
                PlanetBuildings.Instantiate("station", _station, ExchangeColor, 0.3f);
            }
            _station.localPosition = new Vector3(center.x - _box.center.x, _box.center.y - center.y, -10);
            _station.localRotation = Quaternion.Euler(-62, 0, 18);
            _station.localScale = Vector3.one * (radius / 1.05f);
        }

        /// <summary>
        /// Корабли на карте: зонды летят к цели (плавно догоняя ход игры), грузовики курсируют между добывающими
        /// планетами и биржей. Звать каждый кадр; пропавшие корабли убираются.
        /// </summary>
        public void ShowShips(IReadOnlyList<MapShip> ships)
        {
            foreach (var s in _ships.Values) s.Seen = false;
            float time = Time.time;
            foreach (var m in ships)
            {
                if (!_ships.TryGetValue(m.Key, out var s))
                {
                    s = new Ship { Holder = new GameObject(m.Probe ? "Зонд" : "Грузовик").transform };
                    s.Holder.SetParent(_root, false);
                    PlanetBuildings.Instantiate(m.Probe ? "probe" : "cargo", s.Holder, m.Accent, (m.Key * 0.618f) % 1);
                    _ships.Add(m.Key, s);
                }
                s.Seen = true;

                Vector2 from = m.From, to = m.To, pos, dir;
                float scale = m.Size;
                if (m.Probe)
                {
                    s.Shown = s.Shown < 0 ? m.Progress : Mathf.MoveTowards(s.Shown, m.Progress, Time.deltaTime * 0.35f);
                    pos = Vector2.Lerp(from, to, s.Shown);
                    dir = to - from;
                    pos += new Vector2(0, Mathf.Sin(time * 1.7f + m.Key) * 1.5f);
                }
                else
                {
                    // Туда и обратно; у причалов корабль сжимается до нуля — так не видно разворота.
                    float length = Mathf.Max(20, (to - from).magnitude);
                    float period = length / 22f + 3;
                    float t = Mathf.Repeat(time / period + (m.Key * 0.37f) % 1, 1);
                    bool outbound = t < 0.5f;
                    float k = outbound ? t * 2 : 2 - t * 2;
                    float u = Mathf.SmoothStep(0, 1, k);
                    pos = Vector2.Lerp(from, to, u);
                    dir = outbound ? to - from : from - to;
                    float dock = Mathf.Min(k, 1 - k);
                    scale *= Mathf.Clamp01(dock / 0.08f);
                }
                var dirWorld = new Vector3(dir.x, -dir.y, 0);
                if (dirWorld.sqrMagnitude < 1e-4f) dirWorld = Vector3.right;
                s.Holder.localPosition = new Vector3(pos.x - _box.center.x, _box.center.y - pos.y, -45);
                // Нос — по курсу, крыша — к нам, с лёгким креном.
                s.Holder.localRotation = Quaternion.LookRotation(dirWorld.normalized, Vector3.back) * Quaternion.Euler(-20, 0, 25);
                s.Holder.localScale = Vector3.one * Mathf.Max(0.001f, scale);
            }
            _goneShips.Clear();
            foreach (var kv in _ships)
                if (!kv.Value.Seen) _goneShips.Add(kv.Key);
            foreach (int key in _goneShips)
            {
                Destroy(_ships[key].Holder.gameObject);
                _ships.Remove(key);
            }
        }

        PlanetView Create(MapPlanet p)
        {
            // Ось наклонена у каждой планеты по-своему, но все смотрят на нас примерно одинаково.
            // Зерно облика у силуэтов общее, поэтому наклон и вращение — ещё и от номера планеты.
            var rnd = new System.Random(p.Seed + p.Id * 7919);
            var pivot = new GameObject($"Планета {p.Id}").transform;
            pivot.SetParent(_root, false);
            var view = new GameObject("Вид").AddComponent<PlanetView>();
            view.transform.SetParent(pivot, false);
            view.transform.localRotation = Quaternion.Euler(12, (float)rnd.NextDouble() * 360, (float)(rnd.NextDouble() * 40 - 20));
            view.Look = p.Look;
            view.Seed = p.Seed;
            view.Resolution = Resolution;
            // Сначала вся карта — мелкими черновиками за доли секунды, потом планеты по одной становятся чёткими.
            view.PreviewResolution = PreviewResolution;
            view.Shared = p.Silhouette;
            view.SpinSpeed = 5 + (float)rnd.NextDouble() * 5;
            view.CloudDrift = 2;
            return view;
        }

        /// <summary>Камера рисует только прямоугольник карты; один пиксель экрана — одна единица мира.</summary>
        void Frame(Rect box)
        {
            _box = box;
            _screen = new Vector2Int(Screen.width, Screen.height);
            float w = Screen.width, h = Screen.height;
            _camera.rect = new Rect(box.x / w, (h - box.yMax) / h, box.width / w, box.height / h);
            _camera.orthographicSize = box.height / 2;

            // Фон закрывает весь прямоугольник; звёзды не растягиваются по одной оси.
            float hw = box.width / 2, hh = box.height / 2, side = Mathf.Max(box.width, box.height);
            float u = box.width / side, v = box.height / side;
            _backdropMesh.Clear();
            _backdropMesh.vertices = new[] { new Vector3(-hw, -hh), new Vector3(hw, -hh), new Vector3(-hw, hh), new Vector3(hw, hh) };
            _backdropMesh.uv = new[] { new Vector2(0, 0), new Vector2(u, 0), new Vector2(0, v), new Vector2(u, v) };
            _backdropMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            _backdropMesh.RecalculateBounds();
        }

        /// <summary>Звёзды разной яркости и слабая туманность.</summary>
        internal static Texture2D BakeStars(int size, int seed)
        {
            var noise = new PlanetNoise(seed);
            var rnd = new System.Random(seed);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var q = new Vector3(x / (float)size, y / (float)size, 0.37f);
                float neb = Mathf.Clamp01(noise.Fbm(q * 3.5f, 5, 400) * 0.09f + 0.02f);
                float tint = Mathf.Clamp01(noise.Fbm(q * 2f, 3, 401) + 0.5f);
                var c = Color.Lerp(new Color(0.20f, 0.28f, 0.55f), new Color(0.45f, 0.22f, 0.5f), tint) * neb;
                c.a = 1;
                px[y * size + x] = c;
            }
            // Звёзды — мягкие точки, у ярких небольшой ореол.
            int count = size * size / 900;
            for (int i = 0; i < count; i++)
            {
                float sx = (float)rnd.NextDouble() * size, sy = (float)rnd.NextDouble() * size;
                float b = Mathf.Pow((float)rnd.NextDouble(), 3) * 0.9f + 0.1f;
                var col = Color.Lerp(new Color(0.75f, 0.85f, 1f), new Color(1f, 0.9f, 0.75f), (float)rnd.NextDouble()) * b;
                float r = 0.6f + b * 1.4f;
                for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int ix = (int)sx + dx, iy = (int)sy + dy;
                    if (ix < 0 || iy < 0 || ix >= size || iy >= size) continue;
                    float d2 = (ix + 0.5f - sx) * (ix + 0.5f - sx) + (iy + 0.5f - sy) * (iy + 0.5f - sy);
                    float a = Mathf.Exp(-d2 / (r * r * 0.5f));
                    px[iy * size + ix] += col * a;
                }
            }
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            t.SetPixels(px);
            t.Apply(true, true);
            return t;
        }

        void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
            if (_stars != null) Destroy(_stars);
            if (_backdropMat != null) Destroy(_backdropMat);
            if (_backdropMesh != null) Destroy(_backdropMesh);
        }
    }
}
