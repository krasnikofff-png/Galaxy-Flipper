using System.Collections.Generic;
using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Живые картинки под своими камерами; кадр — в текстуру для карточки интерфейса. Камеры стоят далеко от карты
    /// и окна планеты и работают, только пока картинку кто-то показывает.
    /// Картинка автора с картой глубины (Resources/Pictures, готовит tools/pictures/make_pictures.py) — в 2,5D: лежит
    /// на сетке, выдавленной по глубине, и медленно покачивается — ближнее смещается сильнее дальнего, и она оживает.
    /// Так показываются портреты магнатов и ведущего, карточки случаев, легендарные планеты. У магната без портрета —
    /// 3D-бюст из tools/magnates/make_magnates.py с покачивающейся головой.
    /// </summary>
    public sealed class MagnatePortraits : MonoBehaviour
    {
        static readonly Vector3 Origin = new Vector3(0, 20000, 0);
        /// <summary>Кадр портрета магната.</summary>
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
            /// <summary>Размах покачивания: портрет живее, широкая карточка — спокойнее.</summary>
            public float Sway = 1;
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
                string name = model.Replace("magnate_", "");
                var picture = Client.Pictures.Load("magnates", name);
                rig = picture != null
                    ? CreatePicture(picture, Client.Pictures.Depth("magnates", name), Width, Height, CornerCut, 1)
                    : CreateBust(model, accent);
                _rigs.Add(key, rig);
            }
            return Use(rig);
        }

        /// <summary>
        /// Любая картинка автора из Resources/Pictures в 2,5D (карточка случая, легендарная планета): кадр шириной width
        /// в пропорции картинки, cut — срез углов долей ширины. null — такой картинки нет (интерфейс рисует запасной вариант).
        /// </summary>
        public Texture Picture(string key, string category, string name, int width, float cut)
        {
            if (!_rigs.TryGetValue(key, out var rig))
            {
                var picture = Client.Pictures.Load(category, name);
                if (picture == null) return null;
                int height = Mathf.RoundToInt(width * picture.height / (float)picture.width);
                // Широкая карточка качается вполовину слабее портрета: рельеф на ней заметнее.
                rig = CreatePicture(picture, Client.Pictures.Depth(category, name), width, height, cut, height >= width ? 1 : 0.5f);
                _rigs.Add(key, rig);
            }
            return Use(rig);
        }

        Texture Use(Rig rig)
        {
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

        Rig NewRig(string name, int width, int height)
        {
            var rig = new Rig { Phase = _rigs.Count * 1.7f };
            rig.Root = new GameObject("Картинка " + name).transform;
            rig.Root.SetParent(transform, false);
            rig.Root.position = Origin + Vector3.right * (_rigs.Count * 20);
            rig.Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8, name = "Картинка " + name };
            var cam = rig.Camera = new GameObject("Камера").AddComponent<Camera>();
            cam.transform.SetParent(rig.Root, false);
            cam.nearClipPlane = 1;
            cam.farClipPlane = 12;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Срезанные углы и фон прозрачны — там видна карточка с рамкой.
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.targetTexture = rig.Texture;
            cam.enabled = false;
            return rig;
        }

        /// <summary>2,5D: картинка на сетке, которую шейдер выдавливает по карте глубины (без глубины — плоская).</summary>
        Rig CreatePicture(Texture2D picture, Texture2D depth, int width, int height, float cut, float sway)
        {
            var rig = NewRig(picture.name, width, height);
            rig.Sway = sway;
            if (_depthShader == null) _depthShader = Shader.Find("PlanetTrader/PortraitDepth");
            var material = new Material(_depthShader) { name = "Картинка " + picture.name };
            material.SetTexture("_MainTex", picture);
            material.SetTexture("_DepthTex", depth != null ? depth : Texture2D.blackTexture);
            material.SetFloat("_Cut", cut);
            material.SetFloat("_Aspect", height / (float)width);
            float plane = PlaneWidth * picture.height / picture.width;
            var go = new GameObject("Картина");
            go.transform.SetParent(rig.Root, false);
            // Поворачивается вокруг середины рельефа, а не вокруг плоскости картинки.
            go.transform.localPosition = new Vector3(0, plane / 2, -0.32f);
            var mesh = new GameObject("Сетка");
            mesh.transform.SetParent(go.transform, false);
            mesh.transform.localPosition = new Vector3(0, -plane / 2, 0.32f);
            mesh.AddComponent<MeshFilter>().sharedMesh = Grid(plane);
            mesh.AddComponent<MeshRenderer>().sharedMaterial = material;
            rig.Model = go.transform;
            rig.Flat = true;
            // Камера видит картинку чуть меньше её размера: края сетки при покачивании не выглядывают.
            var cam = rig.Camera;
            cam.transform.localPosition = new Vector3(0, plane / 2, -6);
            cam.transform.LookAt(rig.Root.position + new Vector3(0, plane / 2, 0));
            cam.fieldOfView = 2 * Mathf.Atan(plane * 0.46f / 6) * Mathf.Rad2Deg;
            return rig;
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

        Rig CreateBust(string model, Color accent)
        {
            var rig = NewRig(model, Width, Height);
            // Кадр — голова и плечи; цилиндр Гуго и причёска Новы могут чуть выходить за край.
            var cam = rig.Camera;
            cam.transform.localPosition = new Vector3(0.25f, 1.55f, -5.6f);
            cam.transform.LookAt(rig.Root.position + new Vector3(0, 1.42f, 0));
            cam.fieldOfView = 19;
            var prefab = Resources.Load<GameObject>("Magnates/" + model) ?? Resources.Load<GameObject>("Magnates/magnate_shadow");
            if (_bustShader == null) _bustShader = Shader.Find("PlanetTrader/Portrait");
            var material = new Material(_bustShader) { name = "Портрет " + model };
            material.SetColor("_Accent", accent);
            material.SetColor("_RimColor", Color.Lerp(accent, Color.white, 0.15f) * 1.2f);
            if (prefab == null) return rig;
            var go = Instantiate(prefab, rig.Root, false);
            go.name = model;
            rig.Model = go.transform;
            // Модели смотрят в −Y Blender; после импорта лицо смотрит в +Z Unity — разворачиваем к камере.
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = material;
            foreach (var t in go.GetComponentsInChildren<Transform>())
                if (t.name.EndsWith("__head")) rig.Head = t;
            if (rig.Head != null) rig.HeadRest = rig.Head.localRotation;
            return rig;
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
                rig.Model.localRotation = Quaternion.Euler(Mathf.Sin(t * 0.53f) * 3.5f * rig.Sway, Mathf.Sin(t * 0.37f) * 9f * rig.Sway, 0);
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
