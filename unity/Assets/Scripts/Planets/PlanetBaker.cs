using System.Threading.Tasks;
using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>Готовые текстуры планеты: цвет (A — блеск), нормали в пространстве планеты, свечение, облака.</summary>
    public sealed class PlanetTextures
    {
        public Texture2D Albedo, Normal, Emission, Clouds;
    }

    /// <summary>
    /// Запекает текстуры планеты в развёртку «широта–долгота» (ширина вдвое больше высоты).
    /// Считает на всех ядрах; 1024×512 — около секунды.
    /// </summary>
    public static class PlanetBaker
    {
        /// <summary>Точка сферы по координатам развёртки: u — долгота 0…1, v — широта 0…1 (снизу вверх).</summary>
        public static Vector3 Direction(float u, float v)
        {
            float lon = u * 2 * Mathf.PI - Mathf.PI;
            float lat = (v - 0.5f) * Mathf.PI;
            float c = Mathf.Cos(lat);
            return new Vector3(c * Mathf.Cos(lon), Mathf.Sin(lat), c * Mathf.Sin(lon));
        }

        /// <summary>Запечь и сразу выгрузить в текстуры. Только из главного потока.</summary>
        public static PlanetTextures Bake(PlanetLook look, int seed, int width = 1024) => BakePixels(look, seed, width).ToTextures();

        /// <summary>
        /// Посчитать пиксели текстур. Не трогает Unity, поэтому можно звать из фонового потока
        /// (так планеты карты запекаются, не подвешивая игру); текстуры потом делает <see cref="PlanetPixels.ToTextures"/>.
        /// </summary>
        public static PlanetPixels BakePixels(PlanetLook look, int seed, int width = 1024)
        {
            int height = width / 2;
            var noise = new PlanetNoise(seed);
            var cloudNoise = new PlanetNoise(seed + 1);
            var style = PlanetSurface.Style(look);
            var albedo = new Color32[width * height];
            var normal = new Color32[width * height];
            var emission = new Color32[width * height];
            var clouds = new Color32[width * height];
            const float eps = 0.0025f;
            float bump = style.Smooth ? 0 : 1;
            bool hasGlow = false;

            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    var q = Direction((x + 0.5f) / width, (y + 0.5f) / height);
                    var s = PlanetSurface.Evaluate(look, noise, q);
                    // Рельеф: градиент высоты вдоль поверхности — наклоняем нормаль.
                    var t1 = Vector3.Cross(q, new Vector3(0, 1, 1e-4f)).normalized;
                    var t2 = Vector3.Cross(q, t1);
                    float hx = PlanetSurface.Evaluate(look, noise, q + t1 * eps).Height;
                    float hy = PlanetSurface.Evaluate(look, noise, q + t2 * eps).Height;
                    var nrm = (q - bump * ((hx - s.Height) / eps * t1 + (hy - s.Height) / eps * t2)).normalized;

                    int i = y * width + x;
                    albedo[i] = new Color(s.Color.x, s.Color.y, s.Color.z, Mathf.Clamp01(s.Spec));
                    normal[i] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1);
                    emission[i] = new Color(s.Glow.x, s.Glow.y, s.Glow.z, 1);
                    if (s.Glow.sqrMagnitude > 0) hasGlow = true;
                    float c = PlanetSurface.Cloud(cloudNoise, q, style.Clouds);
                    clouds[i] = new Color(1, 1, 1, c);
                }
            });

            return new PlanetPixels
            {
                Width = width,
                Height = height,
                Albedo = albedo,
                Normal = normal,
                Emission = hasGlow ? emission : null,
                Clouds = style.Clouds > 0 ? clouds : null,
            };
        }

        internal static Texture2D Make(Color32[] pixels, int w, int h, bool linear)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true, linear)
            {
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            t.SetPixels32(pixels);
            t.Apply(true, true);
            return t;
        }
    }

    /// <summary>Посчитанные пиксели текстур планеты, ещё не выгруженные в Unity.</summary>
    public sealed class PlanetPixels
    {
        public int Width, Height;
        public Color32[] Albedo, Normal, Emission, Clouds;

        /// <summary>Сделать текстуры. Только из главного потока.</summary>
        public PlanetTextures ToTextures() => new PlanetTextures
        {
            Albedo = PlanetBaker.Make(Albedo, Width, Height, linear: false),
            Normal = PlanetBaker.Make(Normal, Width, Height, linear: true),
            Emission = Emission != null ? PlanetBaker.Make(Emission, Width, Height, linear: false) : Texture2D.blackTexture,
            Clouds = Clouds != null ? PlanetBaker.Make(Clouds, Width, Height, linear: false) : null,
        };
    }
}
