using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PlanetTrader.EditorTools
{
    /// <summary>
    /// Сборка игры из командной строки:
    /// unity run unity -- -executeMethod PlanetTrader.EditorTools.BuildTools.BuildMac -buildOut Build/PlanetTrader.app
    /// Сцена не нужна: Bootstrap запускает игру в пустой.
    /// </summary>
    public static class BuildTools
    {
        /// <summary>
        /// Сборка для плейтеста: игра называется «Planet Trader», версия с датой, своя папка сохранений и журналов.
        /// Настройки проекта после сборки возвращаются как были (у автора в редакторе всё по-старому).
        /// unity run unity -- -executeMethod PlanetTrader.EditorTools.BuildTools.BuildPlaytest -buildOut "Build/Playtest/Planet Trader.app" -buildVersion "0.9 плейтест"
        /// </summary>
        public static void BuildPlaytest()
        {
            var args = System.Environment.GetCommandLineArgs();
            string version = "0.9";
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-buildVersion") version = args[i + 1];
            string product = PlayerSettings.productName, company = PlayerSettings.companyName, bundle = PlayerSettings.bundleVersion;
            PlayerSettings.productName = "Planet Trader";
            PlayerSettings.companyName = "Galaxy Flipper";
            PlayerSettings.bundleVersion = version;
            try
            {
                Build("Build/Playtest/Planet Trader.app", false);
            }
            finally
            {
                PlayerSettings.productName = product;
                PlayerSettings.companyName = company;
                PlayerSettings.bundleVersion = bundle;
            }
            EditorApplication.Exit(_lastOk ? 0 : 1);
        }

        static bool _lastOk;

        public static void BuildMac()
        {
            Build("Build/PlanetTrader.app", true);
        }

        static void Build(string output, bool exit)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-buildOut") output = args[i + 1];

            const string scene = "Assets/Scenes/Main.unity";
            if (!System.IO.File.Exists(scene))
            {
                System.IO.Directory.CreateDirectory("Assets/Scenes");
                var s = UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene);
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(s, scene);
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = output,
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.None,
            });
            Debug.Log($"Сборка: {report.summary.result}, {report.summary.totalErrors} ошибок, {output}");
            _lastOk = report.summary.result == BuildResult.Succeeded;
            if (exit) EditorApplication.Exit(_lastOk ? 0 : 1);
        }
    }
}
