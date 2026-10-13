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
    /// Считает на всех ядрах; 2048×1024 — около двух секунд.
    /// </summary>
    public static class PlanetBaker
    {
        /// <summary>
        /// С какой ширины текстуры рельеф считается по соседним пикселям: втрое быстрее, а на мелких текстурах
        /// ещё и чище (нет ряби от деталей мельче пикселя). Совсем мелкие черновики — по-старому.
        /// </summary>
        const int GridNormalsFrom = 256;

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

            // Рельеф (наклон нормали) — по разнице высот соседних пикселей: поверхность считается один раз на пиксель, а не три.
            bool fromGrid = width >= GridNormalsFrom;
            var heights = fromGrid ? new float[width * height] : null;

            Parallel.For(0, height, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    var q = Direction((x + 0.5f) / width, (y + 0.5f) / height);
                    var s = PlanetSurface.Evaluate(look, noise, q);
                    int i = y * width + x;
                    if (fromGrid)
                        heights[i] = s.Height;
                    else
                    {
                        // Рельеф: градиент высоты вдоль поверхности — наклоняем нормаль.
                        var t1 = Vector3.Cross(q, new Vector3(0, 1, 1e-4f)).normalized;
                        var t2 = Vector3.Cross(q, t1);
                        float hx = PlanetSurface.Evaluate(look, noise, q + t1 * eps).Height;
                        float hy = PlanetSurface.Evaluate(look, noise, q + t2 * eps).Height;
                        var nrm = (q - bump * ((hx - s.Height) / eps * t1 + (hy - s.Height) / eps * t2)).normalized;
                        normal[i] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1);
                    }

                    albedo[i] = new Color(s.Color.x, s.Color.y, s.Color.z, Mathf.Clamp01(s.Spec));
                    emission[i] = new Color(s.Glow.x, s.Glow.y, s.Glow.z, 1);
                    if (s.Glow.sqrMagnitude > 0) hasGlow = true;
                    float c = PlanetSurface.Cloud(cloudNoise, q, style.Clouds);
                    clouds[i] = new Color(1, 1, 1, c);
                }
            });

            if (fromGrid)
            {
                float dLon = 2 * Mathf.PI / width, dLat = Mathf.PI / height;
                Parallel.For(0, height, y =>
                {
                    float lat = ((y + 0.5f) / height - 0.5f) * Mathf.PI;
                    float sinLat = Mathf.Sin(lat), cosLat = Mathf.Cos(lat);
                    // Шаг на восток короче у полюсов; совсем у полюса не даём ему стать нулём.
                    float eastStep = 2 * dLon * Mathf.Max(cosLat, 0.02f);
                    int north = Mathf.Min(y + 1, height - 1), south = Mathf.Max(y - 1, 0);
                    float northStep = (north - south) * dLat;
                    for (int x = 0; x < width; x++)
                    {
                        float lon = (x + 0.5f) / width * 2 * Mathf.PI - Mathf.PI;
                        float sinLon = Mathf.Sin(lon), cosLon = Mathf.Cos(lon);
                        var q = new Vector3(cosLat * cosLon, sinLat, cosLat * sinLon);
                        var east = new Vector3(-sinLon, 0, cosLon);
                        var up = new Vector3(-sinLat * cosLon, cosLat, -sinLat * sinLon);
                        int row = y * width;
                        float dEast = (heights[row + (x + 1) % width] - heights[row + (x + width - 1) % width]) / eastStep;
                        float dNorth = (heights[north * width + x] - heights[south * width + x]) / northStep;
                        var nrm = (q - bump * (dEast * east + dNorth * up)).normalized;
                        normal[row + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1);
                    }
                });
            }

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
