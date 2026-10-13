using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Крупный план выбранной планеты для окна планеты: своя камера рисует 3D-планету на звёздном фоне
    /// в текстуру, а интерфейс вставляет её как картинку (поэтому кадр можно прокручивать вместе с панелью).
    /// Камера работает, только пока кадр показывают.
    /// </summary>
    public sealed class PlanetPortrait : MonoBehaviour
    {
        const int Resolution = 1024;
        /// <summary>Свой угол мира, подальше от карты и витрины.</summary>
        static readonly Vector3 Origin = new Vector3(0, -20000, 0);

        Transform _root;
        Camera _camera;
        PlanetView _view;
        RenderTexture _rt;
        Texture2D _stars;
        Material _backdropMat;
        Mesh _backdropMesh;
        int _shownFrame = -10;
        int _id = -1;

        void Awake()
        {
            _root = new GameObject("Окно планеты 3D").transform;
            _root.position = Origin;
            Object.DontDestroyOnLoad(_root.gameObject);

            _camera = new GameObject("Камера окна планеты").AddComponent<Camera>();
            _camera.transform.SetParent(_root, false);
            _camera.transform.localPosition = new Vector3(0, 0, -4.6f);
            _camera.fieldOfView = 30;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 100;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.01f, 0.012f, 0.03f);
            _camera.enabled = false;

            var sun = new GameObject("Солнце окна планеты").AddComponent<Light>();
            sun.transform.SetParent(_root, false);
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.97f, 0.92f);
            sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.LookRotation(-new Vector3(-0.5f, 0.4f, -0.78f).normalized);

            // Звёзды на плоскости за планетой; ширины хватает на кадр втрое шире высоты.
            _stars = SectorMapView.BakeStars(1024, 777);
            _backdropMat = new Material(Shader.Find("PlanetTrader/PlanetSky"));
            _backdropMat.SetTexture("_MainTex", _stars);
            _backdropMat.SetFloat("_Cull", 0);
            const float half = 14;
            _backdropMesh = new Mesh
            {
                name = "Фон окна планеты",
                vertices = new[] { new Vector3(-half, -half), new Vector3(half, -half), new Vector3(-half, half), new Vector3(half, half) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) },
                triangles = new[] { 0, 2, 1, 1, 2, 3 },
            };
            var backdrop = new GameObject("Фон");
            backdrop.transform.SetParent(_root, false);
            backdrop.transform.localPosition = new Vector3(0, 0, 8);
            backdrop.AddComponent<MeshFilter>().sharedMesh = _backdropMesh;
            backdrop.AddComponent<MeshRenderer>().sharedMaterial = _backdropMat;

            var pivot = new GameObject("Ось").transform;
            pivot.SetParent(_root, false);
            pivot.localRotation = Quaternion.Euler(14, 0, 0);
            _view = new GameObject("Планета").AddComponent<PlanetView>();
            _view.transform.SetParent(pivot, false);
            _view.transform.localRotation = Quaternion.Euler(0, 0, 18);
            _view.Resolution = Resolution;
            _view.SpinSpeed = 6;
            _view.CloudDrift = 2;
            _view.enabled = false;   // не запекать в Start: облик задаёт Show
        }

        /// <summary>
        /// Кадр с планетой размером pixels (в пикселях экрана). Звать в каждом кадре, пока он виден;
        /// смена планеты или её облика запекает новую поверхность в фоне.
        /// </summary>
        public Texture Show(MapPlanet p, Vector2 pixels)
        {
            int w = Mathf.Clamp(Mathf.RoundToInt(pixels.x), 16, 4096), h = Mathf.Clamp(Mathf.RoundToInt(pixels.y), 16, 4096);
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt != null)
                {
                    _camera.targetTexture = null;
                    _rt.Release();
                    Destroy(_rt);
                }
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Окно планеты" };
                _camera.targetTexture = _rt;
            }

            if (_id != p.Id || _view.Look != p.Look || _view.Seed != p.Seed)
            {
                bool samePlanet = _id == p.Id;
                _id = p.Id;
                _view.Look = p.Look;
                _view.Seed = p.Seed;
                _view.enabled = true;
                // Другая планета — сразу убрать старую, чтобы не показывать чужой облик.
                if (!samePlanet) _view.Clear();
                _view.RebuildAsync();
            }
            _view.SetMood(p.Saturation, p.Brightness);
            _shownFrame = Time.frameCount;
            return _rt;
        }

        void LateUpdate()
        {
            // Окно планеты закрыто — камера не тратит время.
            _camera.enabled = Time.frameCount - _shownFrame <= 2;
        }

        void OnEnable()
        {
            if (_root != null) _root.gameObject.SetActive(true);
        }

        void OnDisable()
        {
            if (_root != null) _root.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
            if (_stars != null) Destroy(_stars);
            if (_backdropMat != null) Destroy(_backdropMat);
            if (_backdropMesh != null) Destroy(_backdropMesh);
        }
    }
}
