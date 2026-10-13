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
        /// <summary>Крупный план: расстояние от базы (в радиусах планеты) и высота взгляда над горизонтом, градусы.</summary>
        const float CloseUpRadius = 0.88f, DefaultElevation = 24;
        /// <summary>Сейчас между кадрами: 0 — вся планета, 1 — крупный план.</summary>
        float _closeUp;
        bool _wantCloseUp;
        const float Spin = 3;
        /// <summary>Наклон оси планеты к камере, градусы.</summary>
        const float Tilt = 14;

        // Управление мышью. На крупном плане — облёт базы, иначе — вращение и наклон планеты.
        bool _hasBuildings;
        /// <summary>Игрок попросил крупный план построек. По умолчанию планета видна целиком.</summary>
        bool _userCloseUp;
        float _orbitYaw, _orbitElevation = DefaultElevation, _orbitZoom = 1, _wholeZoom = 1, _tilt;
        float _lastInput = -10;

        /// <summary>На планете есть постройки: можно переключаться между крупным планом и всей планетой.</summary>
        public bool HasBuildings => _hasBuildings;
        /// <summary>Сейчас крупный план построек (или камера летит к нему).</summary>
        public bool CloseUp => _wantCloseUp;

        /// <summary>
        /// Поставить камеру между кадрами «вся планета» (0) и «крупный план» (1).
        /// focus — главная постройка на поверхности, center — центр планеты (обе точки в мире);
        /// yaw, elevation, zoom — облёт базы, wholeZoom — удаление от всей планеты.
        /// </summary>
        public static void PlaceCamera(Transform camera, Transform root, float closeUp, Vector3 focus, Vector3 center,
            float yaw = 0, float elevation = DefaultElevation, float zoom = 1, float wholeZoom = 1)
        {
            float t = Mathf.SmoothStep(0, 1, closeUp);
            var wholePos = root.TransformPoint(WholePos * wholeZoom);
            var wholeRot = Quaternion.LookRotation(root.TransformPoint(WholeLook) - wholePos, root.up);
            if (t <= 0)
            {
                camera.SetPositionAndRotation(wholePos, wholeRot);
                return;
            }
            // Над базой и со стороны зрителя: база внизу кадра, за ней изгиб планеты и звёзды.
            var up = (focus - center).normalized;
            var toViewer = Quaternion.AngleAxis(yaw, up) * Vector3.ProjectOnPlane(-root.forward, up).normalized;
            float e = elevation * Mathf.Deg2Rad;
            var closePos = focus + (toViewer * Mathf.Cos(e) + up * Mathf.Sin(e)) * (CloseUpRadius * zoom);
            // Взгляд чуть выше основания построек; чем ближе камера, тем ниже — иначе крупный план уходит в небо.
            var closeRot = Quaternion.LookRotation(focus + up * (0.1f * Mathf.Min(1, zoom)) - closePos, up);
            // Камера летит по дуге вокруг центра планеты, а не напрямую — так она не проходит сквозь планету.
            var pos = center + Vector3.Slerp(wholePos - center, closePos - center, t);
            camera.SetPositionAndRotation(pos, Quaternion.Slerp(wholeRot, closeRot, t));
        }

        /// <summary>Мышь тянут по кадру: на крупном плане — облёт базы, иначе — повернуть и наклонить планету.</summary>
        public void Drag(Vector2 delta)
        {
            _lastInput = Time.unscaledTime;
            if (_wantCloseUp)
            {
                _orbitYaw += delta.x * 0.4f;
                _orbitElevation = Mathf.Clamp(_orbitElevation + delta.y * 0.3f, 6, 80);
            }
            else
            {
                _view.Turn(delta.x * 0.4f);
                _tilt = Mathf.Clamp(_tilt + delta.y * 0.25f, -45, 45);
                _view.transform.parent.localRotation = Quaternion.Euler(Tilt + _tilt, 0, 0);
            }
        }

        /// <summary>Колесо мыши: ближе или дальше.</summary>
        public void Zoom(float wheel)
        {
            _lastInput = Time.unscaledTime;
            float k = 1 + Mathf.Clamp(wheel, -5, 5) * 0.06f;
            if (_wantCloseUp) _orbitZoom = Mathf.Clamp(_orbitZoom * k, 0.45f, 2.2f);
            else _wholeZoom = Mathf.Clamp(_wholeZoom * k, 0.62f, 1.7f);
        }

        /// <summary>Переключить крупный план построек и всю планету.</summary>
        public void ToggleView()
        {
            _userCloseUp = !_userCloseUp;
            _lastInput = Time.unscaledTime;
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
            _camera.nearClipPlane = 0.02f;
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

            // Звёздное небо — сфера вокруг всей сцены: камера облетает базу и смотрит в любую сторону.
            _stars = PlanetShowcase.BakeSky(2048, 777);
            _backdropMat = new Material(Shader.Find("PlanetTrader/PlanetSky"));
            _backdropMat.SetTexture("_MainTex", _stars);
            _backdropMesh = SphereMesh.Build(64, 32);
            var backdrop = new GameObject("Небо");
            backdrop.transform.SetParent(_root, false);
            backdrop.transform.localScale = Vector3.one * 40;
            backdrop.AddComponent<MeshFilter>().sharedMesh = _backdropMesh;
            backdrop.AddComponent<MeshRenderer>().sharedMaterial = _backdropMat;

            var pivot = new GameObject("Ось").transform;
            pivot.SetParent(_root, false);
            pivot.localRotation = Quaternion.Euler(Tilt, 0, 0);
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
                    // Новая планета показывается целиком; к постройкам — по кнопке.
                    _closeUp = 0;
                    _userCloseUp = false;
                    _orbitYaw = 0;
                    _orbitElevation = DefaultElevation;
                    _orbitZoom = _wholeZoom = 1;
                    _tilt = 0;
                    _view.transform.parent.localRotation = Quaternion.Euler(Tilt, 0, 0);
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
            _hasBuildings = !structures.IsEmpty;
            _wantCloseUp = _hasBuildings && _view.Ready && _userCloseUp;
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
            // Целиком — вращается сама, но не пока её крутят мышью.
            bool resting = Time.unscaledTime - _lastInput > 2.5f;
            _view.SpinSpeed = resting ? Spin * (1 - Mathf.SmoothStep(0, 1, _closeUp)) : 0;
            var surface = _view.Surface;
            if (surface != null && _closeUp > 0)
                PlaceCamera(_camera.transform, _root, _closeUp, surface.TransformPoint(_buildings.Focus), _view.transform.position,
                    _orbitYaw, _orbitElevation, _orbitZoom, _wholeZoom);
            else
                PlaceCamera(_camera.transform, _root, 0, Vector3.zero, Vector3.zero, wholeZoom: _wholeZoom);
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
