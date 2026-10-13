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
        public static void BuildMac()
        {
            string output = "Build/PlanetTrader.app";
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
            EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
