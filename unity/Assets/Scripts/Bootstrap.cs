using UnityEngine;

namespace PlanetTrader.Client
{
    /// <summary>
    /// Запускает игру в любой сцене, даже в пустой: создаёт камеру и объект с интерфейсом.
    /// Сцены для прототипа не нужны — достаточно нажать Play.
    /// </summary>
    public static class Bootstrap
    {
        const string AppObjectName = "Planet Trader";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
            if (GameObject.Find(AppObjectName) != null) return;

            if (Camera.main == null)
            {
                var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.03f, 0.03f, 0.07f);
                cam.orthographic = true;
            }

            var app = new GameObject(AppObjectName);
            Object.DontDestroyOnLoad(app);
            app.AddComponent<PlanetTraderApp>();
        }
    }
}
