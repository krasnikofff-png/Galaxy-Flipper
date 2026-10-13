using System.Threading.Tasks;
using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Планета на сцене: поверхность, облака и ореол атмосферы. Текстуры запекаются при старте
    /// по типу и зерну — у каждой планеты свой облик.
    /// </summary>
    public sealed class PlanetView : MonoBehaviour
    {
        public PlanetLook Look = PlanetLook.Jungle;
        public int Seed = 7;
        [Tooltip("Ширина текстуры; высота вдвое меньше. 1024 — для крупного плана, 256 — для карты.")]
        public int Resolution = 1024;
        [Tooltip("Градусов в секунду.")]
        public float SpinSpeed = 4f;
        public float CloudDrift = 1.5f;
        [Tooltip("Насыщенность цвета: 1 — как есть, 0 — серая (силуэт на карте).")]
        [Range(0, 1)] public float Saturation = 1;
        [Tooltip("Яркость: меньше 1 — тусклая (выработанная планета).")]
        [Range(0, 1.5f)] public float Brightness = 1;
        [Tooltip("Черновик: сначала быстро запечь планету с такой шириной текстуры, потом подменить чёткой. 0 — без черновика.")]
        public int PreviewResolution;
        [Tooltip("Сколько последних планет помнить готовыми: к ним возвращаешься без запекания. 0 — не помнить.")]
        public int CacheSize;
        [Tooltip("Запекать без очереди: окно планеты не ждёт, пока допекутся планеты карты.")]
        public bool Priority;

        /// <summary>
        /// Очередь фоновых запеканий: планеты карты печёт по одной (каждая и так на всех ядрах),
        /// чтобы срочное запекание окна планеты не стояло за десятками планет.
        /// </summary>
        static readonly System.Threading.SemaphoreSlim Queue = new System.Threading.SemaphoreSlim(1);

        Transform _surface, _clouds, _halo, _rings;
        Material _surfaceMat, _cloudMat, _haloMat, _ringMat;
        Mesh _ringMesh;
        Texture2D _ringTex;
        PlanetTextures _tex;
        PlanetStyle _style;
        Task<PlanetPixels> _baking;
        PlanetLook _bakingLook;
        int _bakingSeed, _bakingRes;
        /// <summary>После черновика — запечь начисто с такой шириной; 0 — больше ничего не печь.</summary>
        int _nextRes;
        /// <summary>Что сейчас собрано: облик, зерно и ширина текстуры.</summary>
        PlanetLook _builtLook;
        int _builtSeed = int.MinValue;
        readonly System.Collections.Generic.List<(PlanetLook look, int seed, int res, PlanetPixels pixels)> _cache =
            new System.Collections.Generic.List<(PlanetLook, int, int, PlanetPixels)>();
        static Mesh _sphere;

        /// <summary>Готова ли поверхность (после асинхронного запекания — не сразу).</summary>
        public bool Ready => _surface != null;

        /// <summary>Вращающаяся поверхность: к ней крепятся постройки. Null, пока планета не собрана.</summary>
        public Transform Surface => _surface;

        /// <summary>Повернуть планету вокруг оси на degrees градусов (мышью в окне планеты).</summary>
        public void Turn(float degrees)
        {
            if (_surface == null) return;
            var turn = Quaternion.AngleAxis(-degrees, Vector3.up);
            _surface.localRotation = turn * _surface.localRotation;
            if (_clouds != null) _clouds.localRotation = turn * _clouds.localRotation;
        }

        /// <summary>
        /// Повернуть планету вокруг оси так, чтобы точка поверхности localPoint (в пространстве планеты)
        /// смотрела в сторону worldDir — например, постройками к камере.
        /// </summary>
        public void TurnToward(Vector3 localPoint, Vector3 worldDir)
        {
            if (_surface == null) return;
            var target = transform.InverseTransformDirection(worldDir);
            var current = _surface.localRotation * localPoint;
            float angle = Vector3.SignedAngle(Vector3.ProjectOnPlane(current, Vector3.up), Vector3.ProjectOnPlane(target, Vector3.up), Vector3.up);
            var turn = Quaternion.AngleAxis(angle, Vector3.up);
            _surface.localRotation = turn * _surface.localRotation;
            if (_clouds != null) _clouds.localRotation = turn * _clouds.localRotation;
        }

        void Start()
        {
            if (_surface == null && _baking == null) Rebuild();
        }

        /// <summary>Пересобрать планету сразу: после смены типа или зерна.</summary>
        public void Rebuild()
        {
            _baking = null;
            _nextRes = 0;
            Build(Look, Seed, Resolution, PlanetBaker.BakePixels(Look, Seed, Resolution));
        }

        /// <summary>
        /// Пересобрать в фоне: пиксели считаются в другом потоке, старый облик виден, пока не готов новый.
        /// Для карты, где планет много.
        /// </summary>
        public void RebuildAsync()
        {
            var look = Look;
            int seed = Seed, res = Resolution;
            int cached = _cache.FindIndex(c => c.look == look && c.seed == seed && c.res == res);
            if (cached >= 0)
            {
                // Недавно была — собрать сразу, без запекания, и поднять в начало очереди.
                var entry = _cache[cached];
                _cache.RemoveAt(cached);
                _cache.Insert(0, entry);
                _baking = null;
                _nextRes = 0;
                Build(look, seed, res, entry.pixels);
                return;
            }
            bool preview = PreviewResolution > 0 && PreviewResolution < res;
            _nextRes = preview ? res : 0;
            StartBake(look, seed, preview ? PreviewResolution : res);
        }

        void StartBake(PlanetLook look, int seed, int res)
        {
            _bakingLook = look;
            _bakingSeed = seed;
            _bakingRes = res;
            if (Priority)
            {
                _baking = Task.Run(() => PlanetBaker.BakePixels(look, seed, res));
                return;
            }
            _baking = Task.Run(async () =>
            {
                await Queue.WaitAsync();
                try { return PlanetBaker.BakePixels(look, seed, res); }
                finally { Queue.Release(); }
            });
        }

        /// <summary>Убрать поверхность и отменить запекание: планета пропадает до следующей сборки.</summary>
        public void Clear()
        {
            _baking = null;
            _nextRes = 0;
            Cleanup();
        }

        void Build(PlanetLook look, int seed, int res, PlanetPixels pixels)
        {
            Cleanup();
            _builtLook = look;
            _builtSeed = seed;
            _style = PlanetSurface.Style(look);
            _tex = pixels.ToTextures();
            if (_sphere == null) _sphere = SphereMesh.Build(96, 48);

            _surfaceMat = new Material(Shader.Find("PlanetTrader/Planet"));
            _surfaceMat.SetTexture("_MainTex", _tex.Albedo);
            _surfaceMat.SetTexture("_NormalTex", _tex.Normal);
            _surfaceMat.SetTexture("_EmissionTex", _tex.Emission);
            _surface = Layer("Поверхность", _surfaceMat, 1f);

            if (_tex.Clouds != null)
            {
                _cloudMat = new Material(Shader.Find("PlanetTrader/PlanetClouds"));
                _cloudMat.SetTexture("_MainTex", _tex.Clouds);
                _clouds = Layer("Облака", _cloudMat, 1.012f);
            }

            _haloMat = new Material(Shader.Find("PlanetTrader/PlanetAtmosphere"));
            _haloMat.SetFloat("_Inner", 1f / HaloScale);
            _halo = Layer("Атмосфера", _haloMat, HaloScale);

            // Кольца — не на поверхности: планета под ними вращается, а кольца нет.
            var rings = PlanetRings.For(look, Seed);
            if (rings != null)
            {
                _ringMesh = PlanetRings.BuildMesh(rings);
                _ringTex = PlanetRings.BuildBands(rings);
                _ringMat = new Material(Shader.Find("PlanetTrader/PlanetRing"));
                _ringMat.SetTexture("_MainTex", _ringTex);
                var go = new GameObject("Кольца");
                go.transform.SetParent(transform, false);
                go.layer = gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = _ringMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _ringMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _rings = go.transform;
            }
            ApplyMood();
        }

        /// <summary>Сменить насыщенность и яркость, если они другие.</summary>
        public void SetMood(float saturation, float brightness)
        {
            if (Saturation == saturation && Brightness == brightness) return;
            Saturation = saturation;
            Brightness = brightness;
            ApplyMood();
        }

        /// <summary>Применить насыщенность и яркость к уже собранной планете.</summary>
        public void ApplyMood()
        {
            if (_style == null) return;
            _surfaceMat.SetFloat("_Saturation", Saturation);
            _surfaceMat.SetFloat("_Brightness", Brightness);
            _surfaceMat.SetColor("_AtmoColor", Mood(_style.Atmosphere));
            if (_cloudMat) _cloudMat.SetColor("_Color", Mood(_style.CloudTint));
            _haloMat.SetColor("_Color", Mood(_style.Atmosphere));
            if (_ringMat)
            {
                _ringMat.SetFloat("_Brightness", Brightness);
                _ringMat.SetFloat("_Saturation", Saturation);
            }
        }

        Color Mood(Color c)
        {
            float gray = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
            return Color.Lerp(new Color(gray, gray, gray), c, Saturation) * Brightness;
        }

        const float HaloScale = 1.14f;

        Transform Layer(string name, Material mat, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * scale;
            go.layer = gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = _sphere;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        void Update()
        {
            if (_baking != null && _baking.IsCompleted)
            {
                var task = _baking;
                _baking = null;
                if (task.Status == TaskStatus.RanToCompletion) Baked(task.Result);
                else Debug.LogException(task.Exception);
            }
            if (_surface != null) _surface.Rotate(Vector3.up, -SpinSpeed * Time.deltaTime, Space.Self);
            if (_clouds != null) _clouds.Rotate(Vector3.up, -(SpinSpeed + CloudDrift) * Time.deltaTime, Space.Self);
        }

        /// <summary>Запекание готово: собрать планету или подменить текстуры черновика чёткими.</summary>
        void Baked(PlanetPixels pixels)
        {
            var look = _bakingLook;
            int seed = _bakingSeed, res = _bakingRes;
            if (_surface != null && _builtLook == look && _builtSeed == seed)
                Sharpen(res, pixels);
            else
                Build(look, seed, res, pixels);
            if (res == Resolution && CacheSize > 0)
            {
                _cache.Insert(0, (look, seed, res, pixels));
                if (_cache.Count > CacheSize) _cache.RemoveAt(_cache.Count - 1);
            }
            if (_nextRes > 0)
            {
                int next = _nextRes;
                _nextRes = 0;
                StartBake(look, seed, next);
            }
        }

        /// <summary>
        /// Та же планета, текстуры чётче: меняем только текстуры, а сферу, её поворот и постройки на ней не трогаем.
        /// </summary>
        void Sharpen(int res, PlanetPixels pixels)
        {
            var old = _tex;
            _tex = pixels.ToTextures();
            _surfaceMat.SetTexture("_MainTex", _tex.Albedo);
            _surfaceMat.SetTexture("_NormalTex", _tex.Normal);
            _surfaceMat.SetTexture("_EmissionTex", _tex.Emission);
            if (_cloudMat != null && _tex.Clouds != null) _cloudMat.SetTexture("_MainTex", _tex.Clouds);
            KillTextures(old);
        }

        static void KillTextures(PlanetTextures t)
        {
            if (t == null) return;
            Kill(t.Albedo);
            Kill(t.Normal);
            if (t.Emission != Texture2D.blackTexture) Kill(t.Emission);
            if (t.Clouds != null) Kill(t.Clouds);
        }

        void Cleanup()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            KillTextures(_tex);
            _tex = null;
            _builtSeed = int.MinValue;
            if (_surfaceMat) Kill(_surfaceMat);
            if (_cloudMat) Kill(_cloudMat);
            if (_haloMat) Kill(_haloMat);
            if (_ringMat) Kill(_ringMat);
            if (_ringMesh) Kill(_ringMesh);
            if (_ringTex) Kill(_ringTex);
            _surface = _clouds = _halo = _rings = null;
            _style = null;
        }

        /// <summary>Удалить объект и в игре, и в редакторе (снимки планет делаются без Play).</summary>
        static void Kill(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void OnDestroy()
        {
            _baking = null;
            Cleanup();
        }
    }

    /// <summary>UV-сфера радиуса 1 с той же развёрткой, что у запечённых текстур.</summary>
    public static class SphereMesh
    {
        public static Mesh Build(int segments, int rings)
        {
            var verts = new Vector3[(segments + 1) * (rings + 1)];
            var uvs = new Vector2[verts.Length];
            var normals = new Vector3[verts.Length];
            for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= segments; s++)
            {
                float u = s / (float)segments, v = r / (float)rings;
                var p = PlanetBaker.Direction(u, v);
                int i = r * (segments + 1) + s;
                verts[i] = p;
                normals[i] = p;
                uvs[i] = new Vector2(u, v);
            }
            var tris = new int[segments * rings * 6];
            int t = 0;
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * (segments + 1) + s, b = a + segments + 1;
                tris[t++] = a; tris[t++] = b; tris[t++] = a + 1;
                tris[t++] = a + 1; tris[t++] = b; tris[t++] = b + 1;
            }
            var mesh = new Mesh { name = "Planet sphere", vertices = verts, normals = normals, uv = uvs, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
