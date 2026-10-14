using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Кольца планет: у газовых гигантов почти всегда, у ледяных — у каждой четвёртой, у скалистых и кристальных изредка.
    /// Всё по зерну планеты: ширина, цвета, полосы и просветы.
    /// </summary>
    public static class PlanetRings
    {
        public sealed class Style
        {
            public float Inner, Outer;
            public Color A, B;
            public int Seed;
        }

        public static Style For(PlanetLook look, int seed)
        {
            var rnd = new System.Random(seed * 13 + 5);
            double chance = look == PlanetLook.Gas ? 0.75 : look == PlanetLook.Ice ? 0.25
                : look == PlanetLook.Rocky || look == PlanetLook.Crystal || look == PlanetLook.Ocean ? 0.1 : 0;
            if (rnd.NextDouble() >= chance) return null;
            float inner = 1.25f + (float)rnd.NextDouble() * 0.2f;
            var s = new Style { Inner = inner, Outer = inner + 0.5f + (float)rnd.NextDouble() * 0.45f, Seed = seed };
            switch (look)
            {
                case PlanetLook.Gas:
                    s.A = new Color(0.92f, 0.82f, 0.65f);
                    s.B = rnd.NextDouble() < 0.5 ? new Color(0.6f, 0.45f, 0.32f) : new Color(0.7f, 0.78f, 0.85f);
                    break;
                case PlanetLook.Ice:
                    s.A = new Color(0.92f, 0.96f, 1f);
                    s.B = new Color(0.6f, 0.75f, 0.9f);
                    break;
                case PlanetLook.Crystal:
                    s.A = new Color(0.85f, 0.75f, 1f);
                    s.B = new Color(0.55f, 0.85f, 0.95f);
                    break;
                default:
                    s.A = new Color(0.7f, 0.66f, 0.6f);
                    s.B = new Color(0.45f, 0.4f, 0.36f);
                    break;
            }
            return s;
        }

        /// <summary>Плоское кольцо в плоскости экватора (XZ) в радиусах планеты; UV.x — от внутреннего края к внешнему.</summary>
        public static Mesh BuildMesh(Style s, int segments = 128)
        {
            var verts = new Vector3[(segments + 1) * 2];
            var uvs = new Vector2[verts.Length];
            var normals = new Vector3[verts.Length];
            var tris = new int[segments * 6];
            for (int i = 0; i <= segments; i++)
            {
                float a = 2 * Mathf.PI * i / segments;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                verts[i * 2] = d * s.Inner;
                verts[i * 2 + 1] = d * s.Outer;
                uvs[i * 2] = new Vector2(0, i / (float)segments);
                uvs[i * 2 + 1] = new Vector2(1, i / (float)segments);
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                tris[i * 6] = a; tris[i * 6 + 1] = a + 1; tris[i * 6 + 2] = a + 3;
                tris[i * 6 + 3] = a; tris[i * 6 + 4] = a + 3; tris[i * 6 + 5] = a + 2;
            }
            var mesh = new Mesh { name = "Кольца", vertices = verts, uv = uvs, normals = normals, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Полосы по радиусу: тонкие кольца разной плотности, широкий просвет, мягкие края.</summary>
        public static Texture2D BuildBands(Style s, int width = 512)
        {
            var noise = new PlanetNoise(s.Seed + 77);
            var rnd = new System.Random(s.Seed + 3);
            float gap = 0.45f + (float)rnd.NextDouble() * 0.25f, gapWidth = 0.03f + (float)rnd.NextDouble() * 0.04f;
            var px = new Color[width];
            for (int x = 0; x < width; x++)
            {
                float t = x / (float)(width - 1);
                float fine = noise.Fbm(new Vector3(t * 40, 0.5f, 0.3f), 4, 1) * 0.5f + 0.5f;
                float coarse = noise.Fbm(new Vector3(t * 6, 1.5f, 0.7f), 3, 2) * 0.5f + 0.5f;
                float alpha = Mathf.Clamp01(0.25f + fine * 0.6f + coarse * 0.35f);
                alpha *= Mathf.SmoothStep(0, 1, t / 0.08f) * Mathf.SmoothStep(0, 1, (1 - t) / 0.12f);
                alpha *= Mathf.Clamp01(Mathf.Abs(t - gap) / gapWidth);
                var c = Color.Lerp(s.A, s.B, Mathf.Clamp01(coarse * 1.2f - 0.1f));
                c *= 0.85f + fine * 0.3f;
                c.a = alpha * 0.9f;
                px[x] = c;
            }
            var tex = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Полосы колец" };
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
