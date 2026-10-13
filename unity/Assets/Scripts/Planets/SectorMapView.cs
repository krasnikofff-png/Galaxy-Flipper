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

        public float Saturation => Silhouette ? 0f : Depleted ? 0.35f : 1f;
        public float Brightness => Silhouette ? 0.7f : Depleted ? 0.55f : 1f;
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
        /// <summary>Мир карты далеко от начала координат, чтобы не попадать в другие камеры.</summary>
        static readonly Vector3 Origin = new Vector3(0, -10000, 0);

        sealed class Entry
        {
            public PlanetView View;
            public PlanetLook Look;
            public int Seed;
            public bool Seen;
        }

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
                    e.View.RebuildAsync();
                }
                e.Seen = true;

                e.View.SetMood(p.Saturation, p.Brightness);

                var t = e.View.transform.parent;
                t.localPosition = new Vector3(p.Center.x - _box.center.x, _box.center.y - p.Center.y, 0);
                t.localScale = Vector3.one * p.Radius;
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

        PlanetView Create(MapPlanet p)
        {
            // Ось наклонена у каждой планеты по-своему, но все смотрят на нас примерно одинаково.
            var rnd = new System.Random(p.Seed);
            var pivot = new GameObject($"Планета {p.Id}").transform;
            pivot.SetParent(_root, false);
            var view = new GameObject("Вид").AddComponent<PlanetView>();
            view.transform.SetParent(pivot, false);
            view.transform.localRotation = Quaternion.Euler(12, (float)rnd.NextDouble() * 360, (float)(rnd.NextDouble() * 40 - 20));
            view.Look = p.Look;
            view.Seed = p.Seed;
            view.Resolution = Resolution;
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
