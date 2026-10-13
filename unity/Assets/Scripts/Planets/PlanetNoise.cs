using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Шум для процедурных планет: Perlin в 3D, fBm, «хребты» и ячейки Вороного.
    /// Считается на сфере в 3D, поэтому у текстуры нет швов. Потокобезопасен после создания.
    /// </summary>
    public sealed class PlanetNoise
    {
        static readonly Vector3[] Grad =
        {
            new Vector3(1,1,0), new Vector3(-1,1,0), new Vector3(1,-1,0), new Vector3(-1,-1,0),
            new Vector3(1,0,1), new Vector3(-1,0,1), new Vector3(1,0,-1), new Vector3(-1,0,-1),
            new Vector3(0,1,1), new Vector3(0,-1,1), new Vector3(0,1,-1), new Vector3(0,-1,-1),
        };

        readonly int[] _p = new int[512];

        public PlanetNoise(int seed)
        {
            var r = new System.Random(seed);
            var perm = new int[256];
            for (int i = 0; i < 256; i++) perm[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = r.Next(i + 1);
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }
            for (int i = 0; i < 512; i++) _p[i] = perm[i & 255];
        }

        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

        float G(int ix, int iy, int iz, float dx, float dy, float dz)
        {
            var g = Grad[_p[_p[_p[ix] + iy] + iz] % 12];
            return g.x * dx + g.y * dy + g.z * dz;
        }

        public float Perlin(Vector3 q)
        {
            int x0 = Mathf.FloorToInt(q.x), y0 = Mathf.FloorToInt(q.y), z0 = Mathf.FloorToInt(q.z);
            float fx = q.x - x0, fy = q.y - y0, fz = q.z - z0;
            int x = x0 & 255, y = y0 & 255, z = z0 & 255;
            int x1 = (x + 1) & 255, y1 = (y + 1) & 255, z1 = (z + 1) & 255;
            float ux = Fade(fx), uy = Fade(fy), uz = Fade(fz);
            float a = G(x, y, z, fx, fy, fz), b = G(x1, y, z, fx - 1, fy, fz);
            float c = G(x, y1, z, fx, fy - 1, fz), d = G(x1, y1, z, fx - 1, fy - 1, fz);
            float e = G(x, y, z1, fx, fy, fz - 1), f = G(x1, y, z1, fx - 1, fy, fz - 1);
            float g = G(x, y1, z1, fx, fy - 1, fz - 1), h = G(x1, y1, z1, fx - 1, fy - 1, fz - 1);
            float l0 = a + ux * (b - a), l1 = c + ux * (d - c), l2 = e + ux * (f - e), l3 = g + ux * (h - g);
            float m0 = l0 + uy * (l1 - l0), m1 = l2 + uy * (l3 - l2);
            return m0 + uz * (m1 - m0);
        }

        /// <summary>Фрактальный шум, примерно −0,7…0,7.</summary>
        public float Fbm(Vector3 q, int octaves = 6, float offset = 0, float gain = 0.5f, float lacunarity = 2f)
        {
            float s = 0, a = 1, f = 1, n = 0;
            for (int o = 0; o < octaves; o++)
            {
                s += a * Perlin(q * f + Vector3.one * (offset + o * 17.13f));
                n += a;
                a *= gain;
                f *= lacunarity;
            }
            return s / n;
        }

        /// <summary>«Хребты»: острые гребни там, где шум пересекает ноль. 0…1.</summary>
        public float Ridged(Vector3 q, int octaves = 5, float offset = 31.7f)
        {
            float s = 0, a = 1, f = 1, n = 0;
            for (int o = 0; o < octaves; o++)
            {
                float r = 1 - Mathf.Abs(Perlin(q * f + Vector3.one * (offset + o * 9.1f)));
                s += a * r * r;
                n += a;
                a *= 0.5f;
                f *= 2.1f;
            }
            return s / n;
        }

        /// <summary>Ячейки Вороного: расстояние до ближайшей точки, до второй и номер ячейки.</summary>
        public void Cells(Vector3 q, float scale, float offset, out float d1, out float d2, out int id)
        {
            var qq = q * scale + Vector3.one * offset;
            int bx = Mathf.FloorToInt(qq.x), by = Mathf.FloorToInt(qq.y), bz = Mathf.FloorToInt(qq.z);
            d1 = 9; d2 = 9; id = 0;
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                int cx = bx + dx, cy = by + dy, cz = bz + dz;
                int h = _p[(_p[(_p[cx & 255] + cy) & 511] + cz) & 511];
                var jit = new Vector3(_p[(h + 1) & 511], _p[(h + 2) & 511], _p[(h + 3) & 511]) / 255f;
                float d = (new Vector3(cx, cy, cz) + jit - qq).magnitude;
                if (d < d1)
                {
                    d2 = d1;
                    d1 = d;
                    id = h;
                }
                else if (d < d2) d2 = d;
            }
        }
    }
}
