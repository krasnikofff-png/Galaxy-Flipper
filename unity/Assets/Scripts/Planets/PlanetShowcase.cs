using System.Threading.Tasks;
using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Витрина планет: крупный план одной планеты на звёздном небе. Открывается из игры клавишей F2.
    /// 1–8 — тип планеты, R — другая планета того же типа, мышь — вращать, колесо — ближе/дальше, F2 или Esc — назад к игре.
    /// </summary>
    public sealed class PlanetShowcase : MonoBehaviour
    {
        static PlanetShowcase _instance;
        public static bool Active => _instance != null;

        /// <summary>Открыть или закрыть витрину. hide — что спрятать, пока витрина открыта (например, интерфейс игры).</summary>
        public static void Toggle(Behaviour hide)
        {
            if (_instance != null)
            {
                Destroy(_instance.gameObject);
                return;
            }
            var go = new GameObject("Витрина планет");
            _instance = go.AddComponent<PlanetShowcase>();
            _instance._hidden = hide;
            if (hide != null) hide.enabled = false;
        }

        static readonly string[] Names = { "Джунгли", "Ледяной карлик", "Скалистая", "Газовый гигант", "Кристальная", "Аномалия", "Вулканическая", "Океан" };

        Behaviour _hidden;
        Camera _camera;
        Light _sun;
        PlanetView _planet;
        Transform _pivot;
        GameObject _sky;
        Texture2D _skyTex;
        float _distance = 3.4f;
        Vector2 _orbit = new Vector2(20, 0);
        GUIStyle _label;

        void Start()
        {
            _camera = new GameObject("Камера витрины").AddComponent<Camera>();
            _camera.transform.SetParent(transform, false);
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _camera.fieldOfView = 35;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 200;
            _camera.depth = 10;

            // Солнце сверху слева и чуть спереди — как на превью.
            _sun = new GameObject("Солнце").AddComponent<Light>();
            _sun.transform.SetParent(transform, false);
            _sun.type = LightType.Directional;
            _sun.color = new Color(1f, 0.97f, 0.92f);
            _sun.intensity = 1.1f;
            _sun.transform.rotation = Quaternion.LookRotation(-new Vector3(-0.5f, 0.4f, -0.78f).normalized);

            _sky = new GameObject("Небо");
            _sky.transform.SetParent(transform, false);
            _sky.transform.localScale = Vector3.one * 100;
            _sky.AddComponent<MeshFilter>().sharedMesh = SphereMesh.Build(64, 32);
            _skyTex = BakeSky(2048, 99);
            var skyMat = new Material(Shader.Find("PlanetTrader/PlanetSky"));
            skyMat.SetTexture("_MainTex", _skyTex);
            _sky.AddComponent<MeshRenderer>().sharedMaterial = skyMat;

            _pivot = new GameObject("Ось планеты").transform;
            _pivot.SetParent(transform, false);
            _planet = new GameObject("Планета").AddComponent<PlanetView>();
            _planet.transform.SetParent(_pivot, false);
            _planet.transform.localRotation = Quaternion.Euler(0, 0, 20);   // наклон оси
            _planet.Resolution = 2048;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2) || Input.GetKeyDown(KeyCode.Escape))
            {
                Destroy(gameObject);
                return;
            }
            for (int i = 0; i < Names.Length; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i)) Show((PlanetLook)i, _planet.Seed);
            if (Input.GetKeyDown(KeyCode.R)) Show(_planet.Look, Random.Range(1, 100000));

            if (Input.GetMouseButton(0))
            {
                _orbit.x -= Input.GetAxis("Mouse Y") * 3;
                _orbit.y += Input.GetAxis("Mouse X") * 3;
                _orbit.x = Mathf.Clamp(_orbit.x, -80, 80);
            }
            _distance = Mathf.Clamp(_distance - Input.mouseScrollDelta.y * 0.2f, 1.6f, 8f);
            _pivot.localRotation = Quaternion.Euler(_orbit.x, _orbit.y, 0);
            _camera.transform.localPosition = new Vector3(0, 0, -_distance);
            _camera.transform.LookAt(Vector3.zero);
        }

        void Show(PlanetLook look, int seed)
        {
            _planet.Look = look;
            _planet.Seed = seed;
            _planet.Rebuild();
        }

        void OnGUI()
        {
            if (_label == null)
                _label = new GUIStyle(GUI.skin.label) { fontSize = 18, richText = true, normal = { textColor = new Color(0.75f, 0.95f, 1f) } };
            GUI.Label(new Rect(20, 16, 900, 30), $"<b>{Names[(int)_planet.Look]}</b>   зерно {_planet.Seed}", _label);
            GUI.Label(new Rect(20, 44, 1200, 30),
                "1 джунгли · 2 лёд · 3 скалистая · 4 газ · 5 кристаллы · 6 аномалия · R другая планета · мышь — вращать · колесо — масштаб · F2/Esc — назад", _label);
        }

        void OnDestroy()
        {
            if (_hidden != null) _hidden.enabled = true;
            if (_skyTex != null) Destroy(_skyTex);
            if (_instance == this) _instance = null;
        }

        /// <summary>Звёздное небо с лёгкой туманностью.</summary>
        public static Texture2D BakeSky(int width, int seed)
        {
            int height = width / 2;
            var noise = new PlanetNoise(seed);
            var px = new Color32[width * height];
            Parallel.For(0, height, y =>
            {
                var rnd = new System.Random(seed * 7919 + y);
                for (int x = 0; x < width; x++)
                {
                    var q = PlanetBaker.Direction((x + 0.5f) / width, (y + 0.5f) / height);
                    float neb = Mathf.Clamp01(noise.Fbm(q * 2.2f, 5, 400) * 0.10f + 0.025f);
                    float tint = Mathf.Clamp01(noise.Fbm(q * 1.3f, 3, 401) + 0.5f);
                    var c = Color.Lerp(new Color(0.20f, 0.28f, 0.50f), new Color(0.40f, 0.22f, 0.45f), tint) * neb;
                    if (rnd.NextDouble() > 0.9985) c += Color.white * (float)(rnd.NextDouble() * 0.7 + 0.2);
                    c.a = 1;
                    px[y * width + x] = c;
                }
            });
            var t = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp };
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }
    }
}
