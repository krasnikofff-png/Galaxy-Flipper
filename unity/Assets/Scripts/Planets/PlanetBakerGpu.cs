using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Запекание текстур планеты на видеокарте (Resources/Shaders/PlanetBake.compute) — тот же облик, что у
    /// <see cref="PlanetBaker"/>, но за миллисекунды вместо секунд, и без копирования в память: текстуры рождаются
    /// на видеокарте и сразу идут в материал. Кэш на диске не нужен — видеокарта быстрее диска.
    /// Где compute shader не работает (<see cref="Supported"/>), планеты пекутся по-старому на процессоре.
    /// </summary>
    public static class PlanetBakerGpu
    {
        /// <summary>Выключить, чтобы сравнить облик с процессорным запеканием (снимки до/после).</summary>
        public static bool Enabled = true;

        static ComputeShader _shader;
        static bool _looked;
        static int _surfaceKernel, _normalsKernel;

        static ComputeShader Shader
        {
            get
            {
                if (_looked) return _shader;
                _looked = true;
                _shader = Resources.Load<ComputeShader>("Shaders/PlanetBake");
                if (_shader == null) return null;
                _surfaceKernel = _shader.FindKernel("Surface");
                _normalsKernel = _shader.FindKernel("Normals");
                return _shader;
            }
        }

        public static bool Supported => Enabled && SystemInfo.supportsComputeShaders && Shader != null;

        /// <summary>Запечь текстуры планеты; width — ширина развёртки (высота вдвое меньше). Только из главного потока.</summary>
        public static PlanetTextures Bake(PlanetLook look, int seed, int width)
        {
            var shader = Shader;
            int height = width / 2;
            var style = PlanetSurface.Style(look);
            bool hasGlow = look == PlanetLook.Crystal || look == PlanetLook.Anomaly || look == PlanetLook.Volcanic;

            var perm = new ComputeBuffer(512, sizeof(int));
            var cloudPerm = new ComputeBuffer(512, sizeof(int));
            var heights = new ComputeBuffer(width * height, sizeof(float));
            perm.SetData(new PlanetNoise(seed).Table);
            cloudPerm.SetData(new PlanetNoise(seed + 1).Table);

            var albedo = Target(width, height, "Планета: цвет");
            var normal = Target(width, height, "Планета: рельеф");
            var emission = Target(width, height, "Планета: свечение");
            var clouds = Target(width, height, "Планета: облака");

            shader.SetInt("_Width", width);
            shader.SetInt("_Height", height);
            shader.SetInt("_Look", (int)look);
            shader.SetFloat("_CloudAmount", style.Clouds);
            shader.SetInt("_Smooth", style.Smooth ? 1 : 0);
            foreach (int kernel in new[] { _surfaceKernel, _normalsKernel })
            {
                shader.SetBuffer(kernel, "_Perm", perm);
                shader.SetBuffer(kernel, "_CloudPerm", cloudPerm);
                shader.SetBuffer(kernel, "_Heights", heights);
                shader.SetTexture(kernel, "_Albedo", albedo);
                shader.SetTexture(kernel, "_Emission", emission);
                shader.SetTexture(kernel, "_Clouds", clouds);
                shader.SetTexture(kernel, "_Normal", normal);
            }
            int gx = (width + 7) / 8, gy = (height + 7) / 8;
            shader.Dispatch(_surfaceKernel, gx, gy, 1);
            shader.Dispatch(_normalsKernel, gx, gy, 1);
            albedo.GenerateMips();
            normal.GenerateMips();
            emission.GenerateMips();
            clouds.GenerateMips();
            perm.Release();
            cloudPerm.Release();
            heights.Release();

            if (!hasGlow) Kill(emission);
            if (style.Clouds <= 0) Kill(clouds);
            return new PlanetTextures
            {
                Albedo = albedo,
                Normal = normal,
                Emission = hasGlow ? emission : Texture2D.blackTexture,
                Clouds = style.Clouds > 0 ? clouds : null,
            };
        }

        /// <summary>Удалить и в игре, и в редакторе (снимки планет делаются без Play).</summary>
        static void Kill(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }

        /// <summary>Текстура под запись из compute shader, с мипмапами; развёртка повторяется по долготе.</summary>
        static RenderTexture Target(int width, int height, string name)
        {
            var rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = name,
                enableRandomWrite = true,
                useMipMap = true,
                autoGenerateMips = false,
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4,
            };
            rt.Create();
            return rt;
        }
    }
}
