using System.Collections.Generic;
using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Дым и огонь над постройками (труба цеха, выхлоп качалки, факел газовой платформы).
    /// Источник — пустой объект модели «модель__smoke_dark», «__smoke_light» или «__flame»
    /// (его ставит tools/buildings/make_buildings.py). Клубы рисует и двигает шейдер Smoke, здесь — только время.
    /// </summary>
    public sealed class SmokeEmitter : MonoBehaviour
    {
        sealed class Spec
        {
            public int Puffs;
            public Color Color;
            public float Life, Rise, Size0, Size1, Wind, Fire;
            public bool Additive;
            /// <summary>Цвет огня у основания и на конце; по умолчанию жёлто-оранжевый.</summary>
            public Color FireA = new Color(1, 0.92f, 0.55f), FireB = new Color(1, 0.35f, 0.08f);
        }

        static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
        {
            // Труба цеха и копоть факела: густой тёмный дым, высоко, сносит ветром.
            ["smoke_dark"] = new Spec { Puffs = 22, Color = new Color(0.22f, 0.22f, 0.24f, 0.9f), Life = 4.5f, Rise = 1.5f, Size0 = 0.12f, Size1 = 0.9f, Wind = 0.5f },
            // Выхлоп мотора: светлый, лёгкий, невысоко.
            ["smoke_light"] = new Spec { Puffs = 12, Color = new Color(0.8f, 0.8f, 0.82f, 0.7f), Life = 2.6f, Rise = 0.7f, Size0 = 0.05f, Size1 = 0.42f, Wind = 0.35f },
            // Чёрный дым пожара: густой, высоко.
            ["smoke_black"] = new Spec { Puffs = 26, Color = new Color(0.07f, 0.07f, 0.08f, 0.95f), Life = 5f, Rise = 2f, Size0 = 0.2f, Size1 = 1.2f, Wind = 0.45f },
            // Дым над планетой на карте сектора: светлый, иначе на чёрном космосе его не видно.
            ["smoke_map"] = new Spec { Puffs = 24, Color = new Color(0.66f, 0.64f, 0.62f, 0.95f), Life = 4.5f, Rise = 2.8f, Size0 = 0.45f, Size1 = 1.7f, Wind = 0.7f },
            // Дымок горящей бочки и тлеющих обломков.
            ["smoke_small"] = new Spec { Puffs = 10, Color = new Color(0.35f, 0.34f, 0.34f, 0.7f), Life = 3f, Rise = 0.6f, Size0 = 0.05f, Size1 = 0.35f, Wind = 0.3f },
            // Пожар: высокие яркие языки.
            ["fire"] = new Spec { Puffs = 18, Color = new Color(1, 0.6f, 0.2f, 0.95f), Life = 0.9f, Rise = 0.45f, Size0 = 0.32f, Size1 = 0.08f, Wind = 0.15f, Fire = 1, Additive = true },
            // Двигатели зонда и грузовика: голубой факел назад.
            ["thruster"] = new Spec { Puffs = 10, Color = new Color(0.5f, 0.8f, 1f, 0.95f), Life = 0.35f, Rise = 0.45f, Size0 = 0.22f, Size1 = 0.04f, Wind = 0, Fire = 1, Additive = true,
                FireA = new Color(0.8f, 0.95f, 1f), FireB = new Color(0.2f, 0.45f, 1f) },
            // Огонь в бочке: маленький, живой.
            ["fire_small"] = new Spec { Puffs = 12, Color = new Color(1, 0.6f, 0.2f, 0.95f), Life = 0.7f, Rise = 0.2f, Size0 = 0.13f, Size1 = 0.04f, Wind = 0.1f, Fire = 1, Additive = true },
            // Огонь факела: короткие яркие языки.
            ["flame"] = new Spec { Puffs = 14, Color = new Color(1, 0.6f, 0.2f, 0.95f), Life = 0.6f, Rise = 0.28f, Size0 = 0.22f, Size1 = 0.06f, Wind = 0.2f, Fire = 1, Additive = true },
        };

        static Texture2D _puff;
        static Material _smoke, _fire;
        static readonly Dictionary<int, Mesh> PuffMeshes = new Dictionary<int, Mesh>();

        MeshRenderer _renderer;
        MaterialPropertyBlock _block;
        float _offset;

        /// <summary>Пустить дым у источников модели. working = false — производство стоит, дыма нет.</summary>
        public static void Attach(GameObject model, float offset, bool working)
        {
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
            {
                var key = BuildingMotion.PartName(t.name);
                if (key == null || !Specs.TryGetValue(key, out var spec)) continue;
                var go = new GameObject(key);
                go.layer = t.gameObject.layer;
                go.transform.SetParent(t, false);
                go.AddComponent<MeshFilter>().sharedMesh = PuffMesh(spec);
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = spec.Additive ? FireMaterial : SmokeMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                var e = go.AddComponent<SmokeEmitter>();
                e._renderer = r;
                e._block = new MaterialPropertyBlock();
                e._offset = offset * 100;
                // Ветер у каждой постройки свой.
                var rnd = new System.Random((int)(offset * 100000) + t.name.GetHashCode());
                float a = (float)rnd.NextDouble() * Mathf.PI * 2;
                r.GetPropertyBlock(e._block);
                e._block.SetColor("_Color", spec.Color);
                e._block.SetFloat("_Life", spec.Life);
                e._block.SetFloat("_Rise", spec.Rise);
                e._block.SetFloat("_Size0", spec.Size0);
                e._block.SetFloat("_Size1", spec.Size1);
                e._block.SetVector("_Wind", new Vector4(Mathf.Cos(a) * spec.Wind, 0, Mathf.Sin(a) * spec.Wind, 0));
                e._block.SetFloat("_Fire", spec.Fire);
                e._block.SetColor("_FireA", spec.FireA);
                e._block.SetColor("_FireB", spec.FireB);
                r.SetPropertyBlock(e._block);
                go.SetActive(working);
            }
        }

        void Update() => Pose(Time.time);

        /// <summary>Поставить дым так, как он выглядит в момент time (секунды). Для записи видео — без Play.</summary>
        public void Pose(float time)
        {
            _renderer.GetPropertyBlock(_block);
            _block.SetFloat("_T", time + _offset);
            _renderer.SetPropertyBlock(_block);
        }

        static Material SmokeMaterial => _smoke != null ? _smoke : (_smoke = MakeMaterial(UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha, "Дым"));
        static Material FireMaterial => _fire != null ? _fire : (_fire = MakeMaterial(UnityEngine.Rendering.BlendMode.One, "Огонь"));

        static Material MakeMaterial(UnityEngine.Rendering.BlendMode dst, string name)
        {
            var m = new Material(Shader.Find("PlanetTrader/Smoke")) { name = name };
            m.SetTexture("_MainTex", Puff);
            m.SetFloat("_DstBlend", (float)dst);
            return m;
        }

        /// <summary>Клубы: квадраты в одной точке, их раздвигает и двигает шейдер.</summary>
        static Mesh PuffMesh(Spec spec)
        {
            int n = spec.Puffs;
            if (PuffMeshes.TryGetValue(n, out var mesh) && mesh != null) return mesh;
            var verts = new Vector3[n * 4];
            var corners = new Vector2[n * 4];
            var puff = new Vector2[n * 4];
            var tris = new int[n * 6];
            var rnd = new System.Random(n);
            for (int i = 0; i < n; i++)
            {
                float seed = (float)rnd.NextDouble();
                var c = new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
                for (int k = 0; k < 4; k++)
                {
                    corners[i * 4 + k] = c[k];
                    puff[i * 4 + k] = new Vector2(i / (float)n, seed);
                }
                tris[i * 6 + 0] = i * 4; tris[i * 6 + 1] = i * 4 + 2; tris[i * 6 + 2] = i * 4 + 1;
                tris[i * 6 + 3] = i * 4; tris[i * 6 + 4] = i * 4 + 3; tris[i * 6 + 5] = i * 4 + 2;
            }
            mesh = new Mesh { name = $"Клубы ×{n}", vertices = verts, uv = corners, uv2 = puff, triangles = tris };
            // Вершины стоят в одной точке — границы задаём сами, иначе дым отсекался бы как невидимый.
            mesh.bounds = new Bounds(new Vector3(0, 1, 0), Vector3.one * 4);
            PuffMeshes[n] = mesh;
            return mesh;
        }

        /// <summary>Мягкий клуб с рваным краем.</summary>
        static Texture2D Puff
        {
            get
            {
                if (_puff != null) return _puff;
                const int size = 64;
                var noise = new PlanetNoise(5);
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2 - 1, v = (y + 0.5f) / size * 2 - 1;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float n = noise.Fbm(new Vector3(u * 2.5f, v * 2.5f, 0.3f), 4, 9) * 0.5f + 0.5f;
                    float a = Mathf.Clamp01((1 - r) * 1.6f) * Mathf.Lerp(0.55f, 1f, n);
                    a = Mathf.SmoothStep(0, 1, a);
                    byte shade = (byte)(Mathf.Lerp(0.8f, 1f, n) * 255);
                    px[y * size + x] = new Color32(shade, shade, shade, (byte)(a * 255));
                }
                _puff = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Клуб дыма" };
                _puff.SetPixels32(px);
                _puff.Apply(true, true);
                return _puff;
            }
        }
    }
}
