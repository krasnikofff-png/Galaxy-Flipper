using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PlanetTrader.EditorTools
{
    /// <summary>
    /// Настройка URP (Universal Render Pipeline) для проекта: рендерер Forward+ и ресурс конвейера в Assets/Settings,
    /// назначение во все уровни качества. Из меню: Planet Trader → Настроить URP. Из командной строки:
    /// unity run unity -- -executeMethod PlanetTrader.EditorTools.RenderPipelineSetup.ApplyFromCommandLine [-linear]
    /// -linear — заодно перевести проект в линейное цветовое пространство.
    /// Запуск повторно безопасен: ресурсы не пересоздаются.
    /// </summary>
    public static class RenderPipelineSetup
    {
        const string Dir = "Assets/Settings";
        const string RendererPath = Dir + "/UniversalRenderer.asset";
        const string AssetPath = Dir + "/UniversalRP.asset";

        [MenuItem("Planet Trader/Настроить URP")]
        public static void Apply() => Apply(false);

        public static void ApplyFromCommandLine()
        {
            bool linear = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-linear") >= 0;
            int code = 0;
            try { Apply(linear); }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        static void Apply(bool linear)
        {
            if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "Settings");

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            // Forward+: сколько угодно источников света без лимита на объект; нужен и для GPU Resident Drawer.
            renderer.renderingMode = RenderingMode.ForwardPlus;
            EditorUtility.SetDirty(renderer);

            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
            if (asset == null)
            {
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, AssetPath);
            }
            asset.msaaSampleCount = PlanetTrader.Visuals.CameraRender.Msaa;
            asset.supportsHDR = true;
            EditorUtility.SetDirty(asset);

            GraphicsSettings.defaultRenderPipeline = asset;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(current, false);

            if (linear) PlayerSettings.colorSpace = ColorSpace.Linear;
            AssetDatabase.SaveAssets();
            // URP создаёт глобальные настройки и профиль в корне Assets — переложить к остальным.
            foreach (var name in new[] { "UniversalRenderPipelineGlobalSettings.asset", "DefaultVolumeProfile.asset" })
                if (AssetDatabase.LoadMainAssetAtPath("Assets/" + name) != null)
                    AssetDatabase.MoveAsset("Assets/" + name, Dir + "/" + name);
            AssetDatabase.SaveAssets();
            Debug.Log($"URP настроен: {AssetPath}, Forward+, цвет — {PlayerSettings.colorSpace}");
        }
    }
}
