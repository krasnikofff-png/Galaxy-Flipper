using System.Collections;
using UnityEngine;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Снимок экрана собранной игры для проверки графики без человека:
    /// запуск с ключом -autoShot путь.png делает снимок через несколько секунд и закрывает игру.
    /// -autoShotDelay секунды — сколько ждать (по умолчанию 6: планеты карты запекаются в фоне).
    /// -autoShotReveal — открыть все планеты сектора (виден облик каждого типа) и выбрать газовый гигант.
    /// Без ключа ничего не делает.
    /// </summary>
    public sealed class AutoScreenshot : MonoBehaviour
    {
        string _path;
        float _delay = 6;
        bool _reveal;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = System.Environment.GetCommandLineArgs();
            string path = null;
            float delay = 6;
            bool reveal = System.Array.IndexOf(args, "-autoShotReveal") >= 0;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-autoShot") path = args[i + 1];
                if (args[i] == "-autoShotDelay") float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out delay);
            }
            if (path == null) return;
            var go = new GameObject("Автоснимок");
            DontDestroyOnLoad(go);
            var shot = go.AddComponent<AutoScreenshot>();
            shot._path = path;
            shot._delay = delay;
            shot._reveal = reveal;
        }

        IEnumerator Start()
        {
            yield return null;
            var app = FindAnyObjectByType<PlanetTraderApp>();
            if (_reveal && app != null)
            {
                foreach (var p in app.CurrentGame.AllPlanets)
                    if (p.Knowledge == Core.Knowledge.Silhouette) p.Knowledge = Core.Knowledge.Scanned;
                foreach (var p in app.CurrentGame.AllPlanets)
                    if (p.Resource == Core.Resource.Gas) { app.Select(p.Id); break; }
            }
            yield return new WaitForSecondsRealtime(_delay);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(_path);
            yield return null;
            yield return null;
            Debug.Log($"Автоснимок: {_path}");
            Application.Quit();
        }
    }
}
