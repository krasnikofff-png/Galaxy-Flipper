using System.Collections;
using UnityEngine;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Снимок экрана собранной игры для проверки графики без человека:
    /// запуск с ключом -autoShot путь.png делает снимок через несколько секунд и закрывает игру.
    /// -autoShotDelay секунды — сколько ждать (по умолчанию 6: планеты карты запекаются в фоне).
    /// -autoShotReveal — полностью разведать все планеты сектора (виден облик каждого типа, руины), выбрать свою планету,
    /// устроить на ней аварию и запустить зонд — чтобы на снимке карты было всё.
    /// -autoShotDrag dx — перед снимком «потянуть мышью» окно планеты на dx пикселей (облёт базы).
    /// Без ключа ничего не делает.
    /// </summary>
    public sealed class AutoScreenshot : MonoBehaviour
    {
        string _path;
        float _delay = 6;
        bool _reveal;
        float _drag;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = System.Environment.GetCommandLineArgs();
            string path = null;
            float delay = 6;
            bool reveal = System.Array.IndexOf(args, "-autoShotReveal") >= 0;
            float drag = 0;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-autoShot") path = args[i + 1];
                if (args[i] == "-autoShotDrag") float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out drag);
                if (args[i] == "-autoShotDelay") float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out delay);
            }
            if (path == null) return;
            var go = new GameObject("Автоснимок");
            DontDestroyOnLoad(go);
            var shot = go.AddComponent<AutoScreenshot>();
            shot._path = path;
            shot._delay = delay;
            shot._reveal = reveal;
            shot._drag = drag;
        }

        IEnumerator Start()
        {
            yield return null;
            var app = FindAnyObjectByType<PlanetTraderApp>();
            if (_reveal && app != null)
            {
                foreach (var p in app.CurrentGame.AllPlanets)
                    p.Knowledge = Core.Knowledge.Explored;
                foreach (var p in app.CurrentGame.PlayerPlanets)
                {
                    app.Select(p.Id);
                    // Для снимка карты: авария (дым над планетой) и зонд в полёте.
                    p.AccidentTurnsLeft = 2;
                    break;
                }
                var g = app.CurrentGame;
                g.LaunchScan(g.Balance.ProbeTypes[2], 0.85, 0.2);
            }
            if (_drag != 0 && app != null)
            {
                yield return new WaitForSecondsRealtime(_delay * 0.6f);
                var portrait = app.GetComponent<Visuals.PlanetPortrait>();
                for (int i = 0; i < 20; i++)
                {
                    portrait.Drag(new Vector2(_drag / 20, -1));
                    yield return null;
                }
                yield return new WaitForSecondsRealtime(_delay * 0.4f);
            }
            else
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
