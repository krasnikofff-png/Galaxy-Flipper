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

        Transform _surface, _clouds, _halo;
        Material _surfaceMat, _cloudMat, _haloMat;
        PlanetTextures _tex;
        PlanetStyle _style;
        Task<PlanetPixels> _baking;
        PlanetLook _bakingLook;
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
            Build(Look, PlanetBaker.BakePixels(Look, Seed, Resolution));
        }

        /// <summary>
        /// Пересобрать в фоне: пиксели считаются в другом потоке, старый облик виден, пока не готов новый.
        /// Для карты, где планет много.
        /// </summary>
        public void RebuildAsync()
        {
            var look = Look;
            int seed = Seed, res = Resolution;
            _bakingLook = look;
            _baking = Task.Run(() => PlanetBaker.BakePixels(look, seed, res));
        }

        /// <summary>Убрать поверхность и отменить запекание: планета пропадает до следующей сборки.</summary>
        public void Clear()
        {
            _baking = null;
            Cleanup();
        }

        void Build(PlanetLook look, PlanetPixels pixels)
        {
            Cleanup();
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
                if (task.Status == TaskStatus.RanToCompletion) Build(_bakingLook, task.Result);
                else Debug.LogException(task.Exception);
            }
            if (_surface != null) _surface.Rotate(Vector3.up, -SpinSpeed * Time.deltaTime, Space.Self);
            if (_clouds != null) _clouds.Rotate(Vector3.up, -(SpinSpeed + CloudDrift) * Time.deltaTime, Space.Self);
        }

        void Cleanup()
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);
            if (_tex != null)
            {
                Kill(_tex.Albedo);
                Kill(_tex.Normal);
                if (_tex.Emission != Texture2D.blackTexture) Kill(_tex.Emission);
                if (_tex.Clouds != null) Kill(_tex.Clouds);
                _tex = null;
            }
            if (_surfaceMat) Kill(_surfaceMat);
            if (_cloudMat) Kill(_cloudMat);
            if (_haloMat) Kill(_haloMat);
            _surface = _clouds = _halo = null;
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
