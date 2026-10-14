using System.Collections.Generic;
using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Портреты магнатов под своими камерами; кадр — в текстуру для карточки интерфейса. Камеры стоят далеко от карты
    /// и окна планеты и работают, только пока портрет кто-то показывает.
    /// Если у магната есть нарисованный портрет (Resources/Magnates/Portraits, готовит tools/portraits/make_depth.py), он
    /// в 2,5D: картинка на сетке, выдавленной по карте глубины, медленно покачивается — ближнее смещается сильнее
    /// дальнего, и портрет оживает. Иначе — 3D-бюст из tools/magnates/make_magnates.py с покачивающейся головой.
    /// </summary>
    public sealed class MagnatePortraits : MonoBehaviour
    {
        static readonly Vector3 Origin = new Vector3(0, 20000, 0);
        public const int Width = 360, Height = 440;
        /// <summary>Срез углов кадра — доля ширины; рамка интерфейса срезает углы так же.</summary>
        public const float CornerCut = 0.15f;
        /// <summary>Сколько кадров камера ещё работает после последнего показа портрета.</summary>
        const int KeepFrames = 3;
        /// <summary>Сетка 2,5D-портрета: ширина картинки в единицах сцены, клеток по ширине и высоте.</summary>
        const float PlaneWidth = 1.6f;
        const int GridX = 96, GridY = 120;

        sealed class Rig
        {
            public Transform Root, Model, Head;
            public Quaternion HeadRest;
            public Camera Camera;
            public RenderTexture Texture;
            public bool Flat;
            public int LastUsed = -100;
            public float Phase;
        }

        readonly Dictionary<string, Rig> _rigs = new Dictionary<string, Rig>();
        Shader _bustShader, _depthShader;

        /// <summary>
        /// Текстура с портретом. key — кто (у каждого магната свой кадр), model — модель в Resources/Magnates
        /// (по ней же ищется нарисованный портрет), accent — цвет магната: у бюста ленты, камни и контровой свет.
        /// </summary>
        public Texture Portrait(string key, string model, Color accent)
        {
            if (!_rigs.TryGetValue(key, out var rig))
            {
                rig = Create(model, accent, _rigs.Count);
                _rigs.Add(key, rig);
            }
            rig.LastUsed = Time.frameCount;
            if (!rig.Camera.enabled)
            {
                rig.Camera.enabled = true;
                Pose(rig);
                // Первый кадр — сразу, чтобы карточка не мигнула пустой.
                rig.Camera.Render();
            }
            return rig.Texture;
        }

        Rig Create(string model, Color accent, int index)
        {
            var rig = new Rig { Phase = index * 1.7f };
            rig.Root = new GameObject("Портрет " + model).transform;
            rig.Root.SetParent(transform, false);
            rig.Root.position = Origin + Vector3.right * (index * 20);

            string name = model.Replace("magnate_", "");
            var picture = LoadPng("Magnates/Portraits/" + name, false);
            var depth = LoadPng("Magnates/Portraits/" + name + "_depth", true);
            if (picture != null && depth != null) CreateFlat(rig, picture, depth);
            else CreateBust(rig, model, accent);

            rig.Texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8, name = "Портрет " + model };
            var cam = rig.Camera = new GameObject("Камера портрета").AddComponent<Camera>();
            cam.transform.SetParent(rig.Root, false);
            cam.nearClipPlane = 1;
            cam.farClipPlane = 12;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.targetTexture = rig.Texture;
            cam.enabled = false;
            if (rig.Flat)
            {
                // Камера видит картинку чуть меньше её размера: края сетки при покачивании не выглядывают.
                float height = PlaneWidth * picture.height / picture.width;
                cam.transform.localPosition = new Vector3(0, height / 2, -6);
                cam.transform.LookAt(rig.Root.position + new Vector3(0, height / 2, 0));
                cam.fieldOfView = 2 * Mathf.Atan(height * 0.46f / 6) * Mathf.Rad2Deg;
                // Срезанные углы прозрачны — там видна карточка с рамкой.
                cam.backgroundColor = new Color(0, 0, 0, 0);
            }
            else
            {
                // Кадр — голова и плечи; цилиндр Гуго и причёска Новы могут чуть выходить за край.
                cam.transform.localPosition = new Vector3(0.25f, 1.55f, -5.6f);
                cam.transform.LookAt(rig.Root.position + new Vector3(0, 1.42f, 0));
                cam.fieldOfView = 19;
                // Фон прозрачный: под бюстом — подложка карточки со срезанными углами.
                cam.backgroundColor = new Color(0, 0, 0, 0);
            }
            return rig;
        }

        /// <summary>PNG из Resources (файл .bytes) — без сжатия и растягивания импортом; null, если такого нет.</summary>
        static Texture2D LoadPng(string path, bool linear)
        {
            var asset = Resources.Load<TextAsset>(path);
            if (asset == null) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear) { name = path, wrapMode = TextureWrapMode.Clamp };
            tex.LoadImage(asset.bytes, true);
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 4;
            return tex;
        }

        /// <summary>2,5D: картинка на сетке, которую шейдер выдавливает по карте глубины.</summary>
        void CreateFlat(Rig rig, Texture2D picture, Texture2D depth)
        {
            if (_depthShader == null) _depthShader = Shader.Find("PlanetTrader/PortraitDepth");
            var material = new Material(_depthShader) { name = "Портрет " + picture.name };
            material.SetTexture("_MainTex", picture);
            material.SetTexture("_DepthTex", depth);
            material.SetFloat("_Cut", CornerCut);
            material.SetFloat("_Aspect", Height / (float)Width);
            float height = PlaneWidth * picture.height / picture.width;
            var go = new GameObject("Картина");
            go.transform.SetParent(rig.Root, false);
            // Поворачивается вокруг середины рельефа, а не вокруг плоскости картинки.
            go.transform.localPosition = new Vector3(0, height / 2, -0.32f);
            var mesh = new GameObject("Сетка");
            mesh.transform.SetParent(go.transform, false);
            mesh.transform.localPosition = new Vector3(0, -height / 2, 0.32f);
            mesh.AddComponent<MeshFilter>().sharedMesh = Grid(height);
            mesh.AddComponent<MeshRenderer>().sharedMaterial = material;
            rig.Model = go.transform;
            rig.Flat = true;
        }

        /// <summary>Плоская сетка картинки: снизу вверх от 0 до height, по ширине — от −PlaneWidth/2 до PlaneWidth/2.</summary>
        Mesh Grid(float height)
        {
            var verts = new Vector3[(GridX + 1) * (GridY + 1)];
            var uv = new Vector2[verts.Length];
            for (int y = 0; y <= GridY; y++)
            for (int x = 0; x <= GridX; x++)
            {
                int i = y * (GridX + 1) + x;
                float u = x / (float)GridX, v = y / (float)GridY;
                verts[i] = new Vector3((u - 0.5f) * PlaneWidth, v * height, 0);
                uv[i] = new Vector2(u, v);
            }
            var tris = new int[GridX * GridY * 6];
            int k = 0;
            for (int y = 0; y < GridY; y++)
            for (int x = 0; x < GridX; x++)
            {
                int a = y * (GridX + 1) + x, b = a + 1, c = a + GridX + 1, d = c + 1;
                tris[k++] = a; tris[k++] = c; tris[k++] = b;
                tris[k++] = b; tris[k++] = c; tris[k++] = d;
            }
            var mesh = new Mesh { name = "Сетка портрета", vertices = verts, uv = uv, triangles = tris };
            // Рельеф добавляет шейдер — границы с запасом, чтобы сетку не отсекало.
            mesh.bounds = new Bounds(new Vector3(0, height / 2, -0.5f), new Vector3(PlaneWidth, height, 2));
            return mesh;
        }

        void CreateBust(Rig rig, string model, Color accent)
        {
            var prefab = Resources.Load<GameObject>("Magnates/" + model) ?? Resources.Load<GameObject>("Magnates/magnate_shadow");
            if (_bustShader == null) _bustShader = Shader.Find("PlanetTrader/Portrait");
            var material = new Material(_bustShader) { name = "Портрет " + model };
            material.SetColor("_Accent", accent);
            material.SetColor("_RimColor", Color.Lerp(accent, Color.white, 0.15f) * 1.2f);
            if (prefab == null) return;
            var go = Instantiate(prefab, rig.Root, false);
            go.name = model;
            rig.Model = go.transform;
            // Модели смотрят в −Y Blender; после импорта лицо смотрит в +Z Unity — разворачиваем к камере.
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = material;
            foreach (var t in go.GetComponentsInChildren<Transform>())
                if (t.name.EndsWith("__head")) rig.Head = t;
            if (rig.Head != null) rig.HeadRest = rig.Head.localRotation;
        }

        void LateUpdate()
        {
            foreach (var rig in _rigs.Values)
            {
                bool shown = Time.frameCount - rig.LastUsed <= KeepFrames;
                if (rig.Camera.enabled != shown) rig.Camera.enabled = shown;
                if (shown) Pose(rig);
            }
        }

        /// <summary>Живой магнат: картина медленно поворачивается (рельеф даёт объём), у бюста — голова и дыхание.</summary>
        void Pose(Rig rig)
        {
            float t = Time.unscaledTime + rig.Phase;
            if (rig.Flat)
            {
                rig.Model.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.53f) * 3.5f, Mathf.Sin(t * 0.37f) * 9f, 0);
                return;
            }
            if (rig.Head != null)
                rig.Head.localRotation = rig.HeadRest * Quaternion.Euler(Mathf.Sin(t * 0.7f) * 2.5f, Mathf.Sin(t * 0.45f) * 7f, 0);
            if (rig.Model != null)
                rig.Model.localScale = Vector3.one * (1 + Mathf.Sin(t * 1.6f) * 0.006f);
        }

        void OnDestroy()
        {
            foreach (var rig in _rigs.Values)
                if (rig.Texture != null) rig.Texture.Release();
        }
    }
}
