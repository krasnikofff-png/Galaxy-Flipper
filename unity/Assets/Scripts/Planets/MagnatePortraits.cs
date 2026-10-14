using System.Collections.Generic;
using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Портреты магнатов: 3D-бюст (модели из tools/magnates/make_magnates.py) под своей камерой, кадр — в текстуру
    /// для карточки интерфейса. Бюсты стоят далеко от карты и окна планеты; камера включена, только пока портрет
    /// кто-то показывает. Голова покачивается, грудь дышит — магнат живой.
    /// </summary>
    public sealed class MagnatePortraits : MonoBehaviour
    {
        static readonly Vector3 Origin = new Vector3(0, 20000, 0);
        const int Width = 360, Height = 440;
        /// <summary>Сколько кадров камера ещё работает после последнего показа портрета.</summary>
        const int KeepFrames = 3;

        sealed class Rig
        {
            public Transform Root, Model, Head;
            public Quaternion HeadRest;
            public Camera Camera;
            public RenderTexture Texture;
            public int LastUsed = -100;
            public float Phase;
        }

        readonly Dictionary<string, Rig> _rigs = new Dictionary<string, Rig>();
        Shader _shader;

        /// <summary>
        /// Текстура с портретом. key — кто (у каждого магната свой кадр), model — модель в Resources/Magnates,
        /// accent — цвет магната: ленты, броши, камни и контровой свет.
        /// </summary>
        public Texture Portrait(string key, string model, Color accent)
        {
            if (!_rigs.TryGetValue(key, out var rig))
            {
                rig = Create(model, accent, _rigs.Count);
                _rigs.Add(key, rig);
            }
            rig.LastUsed = Time.frameCount;
            if (!rig.Camera.enabled)
            {
                rig.Camera.enabled = true;
                Pose(rig);
                // Первый кадр — сразу, чтобы карточка не мигнула пустой.
                rig.Camera.Render();
            }
            return rig.Texture;
        }

        Rig Create(string model, Color accent, int index)
        {
            var rig = new Rig { Phase = index * 1.7f };
            rig.Root = new GameObject("Портрет " + model).transform;
            rig.Root.SetParent(transform, false);
            rig.Root.position = Origin + Vector3.right * (index * 20);

            var prefab = Resources.Load<GameObject>("Magnates/" + model) ?? Resources.Load<GameObject>("Magnates/magnate_shadow");
            if (_shader == null) _shader = Shader.Find("PlanetTrader/Portrait");
            var material = new Material(_shader) { name = "Портрет " + model };
            material.SetColor("_Accent", accent);
            material.SetColor("_RimColor", Color.Lerp(accent, Color.white, 0.15f) * 1.2f);
            if (prefab != null)
            {
                var go = Instantiate(prefab, rig.Root, false);
                go.name = model;
                rig.Model = go.transform;
                // Модели смотрят в −Y Blender; после импорта лицо смотрит в +Z Unity — разворачиваем к камере.
                go.transform.localRotation = Quaternion.Euler(0, 180, 0);
                foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = material;
                foreach (var t in go.GetComponentsInChildren<Transform>())
                    if (t.name.EndsWith("__head")) rig.Head = t;
                if (rig.Head != null) rig.HeadRest = rig.Head.localRotation;
            }

            rig.Texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8, name = "Портрет " + model };
            var cam = new GameObject("Камера портрета").AddComponent<Camera>();
            cam.transform.SetParent(rig.Root, false);
            // Кадр — голова и плечи; цилиндр Гуго и причёска Новы могут чуть выходить за край.
            cam.transform.localPosition = new Vector3(0.25f, 1.55f, -5.6f);
            cam.transform.LookAt(rig.Root.position + new Vector3(0, 1.42f, 0));
            cam.fieldOfView = 19;
            cam.nearClipPlane = 1;
            cam.farClipPlane = 12;
            cam.clearFlags = CameraClearFlags.SolidColor;
            // Фон — тёмный с оттенком магната; виньетку и рамку рисует интерфейс.
            cam.backgroundColor = Color.Lerp(new Color(0.03f, 0.035f, 0.06f), accent, 0.13f);
            cam.targetTexture = rig.Texture;
            cam.enabled = false;
            rig.Camera = cam;
            return rig;
        }

        void LateUpdate()
        {
            foreach (var rig in _rigs.Values)
            {
                bool shown = Time.frameCount - rig.LastUsed <= KeepFrames;
                if (rig.Camera.enabled != shown) rig.Camera.enabled = shown;
                if (shown) Pose(rig);
            }
        }

        /// <summary>Живой магнат: голова чуть поворачивается и кивает, грудь дышит.</summary>
        void Pose(Rig rig)
        {
            float t = Time.unscaledTime + rig.Phase;
            if (rig.Head != null)
                rig.Head.localRotation = rig.HeadRest * Quaternion.Euler(Mathf.Sin(t * 0.7f) * 2.5f, Mathf.Sin(t * 0.45f) * 7f, 0);
            if (rig.Model != null)
                rig.Model.localScale = Vector3.one * (1 + Mathf.Sin(t * 1.6f) * 0.006f);
        }

        void OnDestroy()
        {
            foreach (var rig in _rigs.Values)
                if (rig.Texture != null) rig.Texture.Release();
        }
    }
}
