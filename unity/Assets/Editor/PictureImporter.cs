using UnityEditor;

namespace PlanetTrader.EditorTools
{
    /// <summary>
    /// Настройки импорта картинок автора из Resources/Pictures (готовит tools/pictures/make_pictures.py).
    /// Цвет: sRGB, сжатие высокого качества (BC7 на компьютере, ASTC на телефоне), мипмапы — карточки бывают мелкими.
    /// Карта глубины (…_depth): линейная, без сжатия и мипмапов — её читает вершинный шейдер, артефакты сжатия стали бы буграми.
    /// Размер не меняется: стороны картинок уже кратны четырём.
    /// </summary>
    public sealed class PictureImporter : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Pictures/")) return;
            var importer = (TextureImporter)assetImporter;
            bool depth = assetPath.EndsWith("_depth.png");
            importer.textureType = TextureImporterType.Default;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.isReadable = false;
            importer.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            importer.maxTextureSize = 2048;
            importer.alphaIsTransparency = !depth;
            importer.sRGBTexture = !depth;
            importer.mipmapEnabled = !depth;
            importer.filterMode = depth ? UnityEngine.FilterMode.Bilinear : UnityEngine.FilterMode.Trilinear;
            importer.anisoLevel = depth ? 1 : 4;
            importer.textureCompression = depth ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;
        }
    }
}
