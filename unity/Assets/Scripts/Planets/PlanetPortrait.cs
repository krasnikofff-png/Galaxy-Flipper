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
        const int Resolution = 2048;   // на крупном плане построек поверхность видна близко
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
        readonly PlanetBuildings _buildings = new PlanetBuildings();
        /// <summary>Для какой планеты уже повернули постройки к камере.</summary>
        int _facedId = -1;
        /// <summary>Куда поворачивать постройки: к камере, в середину кадра (солнце и так светит слева).</summary>
        public static readonly Vector3 FacingDir = new Vector3(-0.08f, 0.2f, -1f).normalized;

        // Два кадра: вся планета и крупный план построек — камера над базой, за ней горизонт и звёзды.
        static readonly Vector3 WholePos = new Vector3(0, 0, -4.6f), WholeLook = Vector3.zero;
        /// <summary>Крупный план: насколько камера выше базы и насколько отходит от неё (в радиусах планеты).</summary>
        const float CloseUpHeight = 0.36f, CloseUpDistance = 0.8f;
        /// <summary>Сейчас между кадрами: 0 — вся планета, 1 — крупный план.</summary>
        float _closeUp;
        bool _wantCloseUp;
        const float Spin = 3;

        /// <summary>
        /// Поставить камеру между кадрами «вся планета» (0) и «крупный план» (1).
        /// focus — главная постройка на поверхности, center — центр планеты (обе точки в мире).
        /// </summary>
        public static void PlaceCamera(Transform camera, Transform root, float closeUp, Vector3 focus, Vector3 center)
        {
            float t = Mathf.SmoothStep(0, 1, closeUp);
            var wholePos = root.TransformPoint(WholePos);
            var wholeRot = Quaternion.LookRotation(root.TransformPoint(WholeLook) - wholePos, root.up);
            if (t <= 0)
            {
                camera.SetPositionAndRotation(wholePos, wholeRot);
                return;
            }
            // Над базой и чуть в сторону зрителя: база внизу кадра, за ней изгиб планеты и звёзды.
            var up = (focus - center).normalized;
            var toViewer = Vector3.ProjectOnPlane(-root.forward, up).normalized;
            var closePos = focus + up * CloseUpHeight + toViewer * CloseUpDistance;
            var closeRot = Quaternion.LookRotation(focus + up * 0.1f - closePos, up);
            camera.SetPositionAndRotation(Vector3.Lerp(wholePos, closePos, t), Quaternion.Slerp(wholeRot, closeRot, t));
        }

        void Awake()
        {
            _root = new GameObject("Окно планеты 3D").transform;
            _root.position = Origin;
            Object.DontDestroyOnLoad(_root.gameObject);

            _camera = new GameObject("Камера окна планеты").AddComponent<Camera>();
            _camera.transform.SetParent(_root, false);
            PlaceCamera(_camera.transform, _root, 0, Vector3.zero, Vector3.zero);
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
            _view.SpinSpeed = Spin;
            _view.CloudDrift = 2;
            _view.enabled = false;   // не запекать в Start: облик задаёт Show
        }

        /// <summary>
        /// Кадр с планетой размером pixels (в пикселях экрана). Звать в каждом кадре, пока он виден;
        /// смена планеты или её облика запекает новую поверхность в фоне.
        /// </summary>
        public Texture Show(MapPlanet p, PlanetStructures structures, Vector2 pixels)
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
                if (!samePlanet)
                {
                    _view.Clear();
                    // Новая планета показывается сразу целиком, потом камера подлетает к постройкам.
                    _closeUp = 0;
                }
                _view.RebuildAsync();
            }
            _view.SetMood(p.Saturation, p.Brightness);

            // Постройки — на готовую поверхность; новую планету поворачиваем к камере главной постройкой.
            _buildings.Sync(_view, structures);
            if (_buildings.JustPlaced && _facedId != p.Id && !structures.IsEmpty)
            {
                _view.TurnToward(_buildings.Focus, FacingDir);
                _facedId = p.Id;
            }
            _wantCloseUp = !structures.IsEmpty && _view.Ready;
            _shownFrame = Time.frameCount;
            return _rt;
        }

        void LateUpdate()
        {
            // Окно планеты закрыто — камера не тратит время.
            _camera.enabled = Time.frameCount - _shownFrame <= 2;
            float target = _wantCloseUp ? 1 : 0;
            _closeUp = Mathf.MoveTowards(_closeUp, target, Time.unscaledDeltaTime * 0.8f);
            // На крупном плане планета замирает (облака плывут), иначе база уехала бы из кадра.
            _view.SpinSpeed = Spin * (1 - Mathf.SmoothStep(0, 1, _closeUp));
            var surface = _view.Surface;
            if (surface != null && _closeUp > 0)
                PlaceCamera(_camera.transform, _root, _closeUp, surface.TransformPoint(_buildings.Focus), _view.transform.position);
            else
                PlaceCamera(_camera.transform, _root, 0, Vector3.zero, Vector3.zero);
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
