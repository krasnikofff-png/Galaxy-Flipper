using System.IO;
using PlanetTrader.Visuals;
using UnityEditor;
using UnityEngine;

namespace PlanetTrader.EditorTools
{
    /// <summary>
    /// Снимок всех шести типов планет настоящим рендером Unity — проверка шейдеров без Play.
    /// Из меню: Planet Trader → Снимок планет. Из командной строки:
    /// unity run unity -- -executeMethod PlanetTrader.EditorTools.PlanetSnapshots.RenderFromCommandLine -snapOut ../docs/art/planets-unity.png
    /// </summary>
    public static class PlanetSnapshots
    {
        const int Tile = 480;
        static readonly int[] Seeds = { 7, 7, 7, 7, 7, 7 };

        [MenuItem("Planet Trader/Снимок планет")]
        static void RenderFromMenu()
        {
            var path = EditorUtility.SaveFilePanel("Снимок планет", "", "planets-unity.png", "png");
            if (!string.IsNullOrEmpty(path)) Render(path, 1024);
        }

        public static void RenderFromCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            string path = "planets-unity.png";
            int res = 1024;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-snapOut") path = args[i + 1];
                if (args[i] == "-snapRes") res = int.Parse(args[i + 1]);
            }
            int code = 0;
            try { Render(path, res); }
            catch (System.Exception e) { Debug.LogException(e); code = 1; }
            EditorApplication.Exit(code);
        }

        public static void Render(string path, int resolution)
        {
            var root = new GameObject("Снимок планет") { hideFlags = HideFlags.DontSave };
            try
            {
                var cam = new GameObject("Камера").AddComponent<Camera>();
                cam.transform.SetParent(root.transform, false);
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
                cam.fieldOfView = 35;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 50;
                cam.transform.position = new Vector3(0, 0, -3.4f);
                cam.transform.LookAt(Vector3.zero);
                cam.enabled = false;

                // Солнце — как в витрине.
                var sun = new GameObject("Солнце").AddComponent<Light>();
                sun.transform.SetParent(root.transform, false);
                sun.type = LightType.Directional;
                sun.color = new Color(1f, 0.97f, 0.92f);
                sun.intensity = 1.1f;
                sun.transform.rotation = Quaternion.LookRotation(-new Vector3(-0.5f, 0.4f, -0.78f).normalized);

                var rt = new RenderTexture(Tile, Tile, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                cam.targetTexture = rt;
                var sheet = new Texture2D(Tile * 3, Tile * 2, TextureFormat.RGB24, false);

                for (int i = 0; i < 6; i++)
                {
                    var look = (PlanetLook)i;
                    var pivot = new GameObject("Ось").transform;
                    pivot.SetParent(root.transform, false);
                    pivot.localRotation = Quaternion.Euler(20, 0, 0);
                    var view = new GameObject(look.ToString()).AddComponent<PlanetView>();
                    view.transform.SetParent(pivot, false);
                    view.transform.localRotation = Quaternion.Euler(0, 0, 20);
                    view.Look = look;
                    view.Seed = Seeds[i];
                    view.Resolution = resolution;
                    view.Rebuild();

                    cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, Tile, Tile), (i % 3) * Tile, (1 - i / 3) * Tile);
                    RenderTexture.active = null;
                    Object.DestroyImmediate(pivot.gameObject);
                }

                sheet.Apply();
                var full = Path.GetFullPath(path);
                File.WriteAllBytes(full, sheet.EncodeToPNG());
                Debug.Log($"Снимок планет: {full}");
                Object.DestroyImmediate(sheet);
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
