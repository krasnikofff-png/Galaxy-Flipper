using PlanetTrader.Core;
using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>Как выглядит планета. Соответствует типам планет в игре.</summary>
    public enum PlanetLook
    {
        /// <summary>Джунгли (биомасса): тропическая зелень, моря и лагуны.</summary>
        Jungle,
        /// <summary>Ледяной карлик (лёд): ледники, торосы, замёрзшие моря.</summary>
        Ice,
        /// <summary>Скалистая (металл): плато, каньоны, кратеры, блестящие жилы.</summary>
        Rocky,
        /// <summary>Газовый гигант (газ): полосы облаков и шторм.</summary>
        Gas,
        /// <summary>Кристальная (кристаллы): поля кристаллов с гранями.</summary>
        Crystal,
        /// <summary>Аномалия (квантий): тёмная поверхность со светящимися разломами.</summary>
        Anomaly,
    }

    /// <summary>Точка поверхности: цвет, высота (для рельефа), блеск, свечение.</summary>
    public struct SurfaceSample
    {
        public Vector3 Color;
        public float Height;
        public float Spec;
        public Vector3 Glow;
    }

    /// <summary>Общий облик типа: атмосфера, облака, сглаженный рельеф (газовый гигант).</summary>
    public sealed class PlanetStyle
    {
        public Color Atmosphere;
        public float Clouds;
        public Color CloudTint = Color.white;
        public bool Smooth;
    }

    /// <summary>
    /// Правила поверхности для каждого типа. Тот же алгоритм, что в tools/planet-preview/planet.py,
    /// но по одной точке. q — направление из центра планеты (единичный вектор, y — ось вращения).
    /// </summary>
    public static class PlanetSurface
    {
        public static PlanetLook FromResource(Resource r)
        {
            switch (r)
            {
                case Resource.Ice: return PlanetLook.Ice;
                case Resource.Metal: return PlanetLook.Rocky;
                case Resource.Gas: return PlanetLook.Gas;
                case Resource.Biomass: return PlanetLook.Jungle;
                case Resource.Crystals: return PlanetLook.Crystal;
                default: return PlanetLook.Anomaly;
            }
        }

        public static PlanetStyle Style(PlanetLook look)
        {
            switch (look)
            {
                case PlanetLook.Jungle: return new PlanetStyle { Atmosphere = C(90, 190, 255), Clouds = 0.58f };
                case PlanetLook.Ice: return new PlanetStyle { Atmosphere = C(160, 215, 255), Clouds = 0.25f, CloudTint = C(240, 248, 255) };
                case PlanetLook.Rocky: return new PlanetStyle { Atmosphere = C(240, 170, 110), Clouds = 0.12f, CloudTint = C(235, 205, 170) };
                case PlanetLook.Gas: return new PlanetStyle { Atmosphere = C(255, 210, 150), Clouds = 0, Smooth = true };
                case PlanetLook.Crystal: return new PlanetStyle { Atmosphere = C(190, 140, 255), Clouds = 0.15f, CloudTint = C(220, 210, 255) };
                default: return new PlanetStyle { Atmosphere = C(200, 90, 255), Clouds = 0 };
            }
        }

        static Color C(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);
        static Vector3 V(int r, int g, int b) => new Vector3(r / 255f, g / 255f, b / 255f);
        static float Sat(float x) => Mathf.Clamp01(x);
        static Vector3 Mix(Vector3 a, Vector3 b, float t) => a + (b - a) * t;

        static Vector3 Warp(PlanetNoise n, Vector3 q, float freq, float amount) =>
            q + amount * new Vector3(n.Fbm(q * freq, 4, 5.1f), n.Fbm(q * freq, 4, 11.7f), n.Fbm(q * freq, 4, 23.3f));

        public static SurfaceSample Evaluate(PlanetLook look, PlanetNoise n, Vector3 q)
        {
            switch (look)
            {
                case PlanetLook.Jungle: return Jungle(n, q);
                case PlanetLook.Ice: return Ice(n, q);
                case PlanetLook.Rocky: return Rocky(n, q);
                case PlanetLook.Gas: return Gas(n, q);
                case PlanetLook.Crystal: return Crystal(n, q);
                default: return Anomaly(n, q);
            }
        }

        static SurfaceSample Jungle(PlanetNoise n, Vector3 q)
        {
            var qw = Warp(n, q, 1.4f, 0.38f);
            float elev = n.Fbm(qw * 1.15f, 7) * 1.3f + 0.03f;
            float lat = Mathf.Abs(q.y);
            float moist = n.Fbm(q * 2.0f, 5, 101) * 0.9f + 0.25f;
            float mount = Sat((n.Ridged(qw * 3.0f) - 0.70f) * 5) * Sat(elev * 5);
            var s = new SurfaceSample();
            if (elev <= 0)
            {
                float depth = Sat(-elev * 5);
                s.Color = Mix(Mix(V(48, 190, 190), V(20, 120, 160), Sat(depth / 0.25f)), V(12, 58, 110), Sat((depth - 0.25f) / 0.75f));
                s.Color += Vector3.Scale(V(120, 255, 240), Vector3.one * Sat(1 - depth * 4) * 0.10f);
                s.Height = -0.01f;
                s.Spec = 1;
                return s;
            }
            float grove = n.Fbm(q * 10, 3, 77);
            float tree = n.Perlin(q * 110 + Vector3.one * 3.3f) * 0.6f + n.Perlin(q * 230 + Vector3.one * 7.7f) * 0.4f;
            float dry = Sat((0.1f - moist) * 3);
            var b = Mix(V(80, 140, 45), V(170, 120, 70), dry);
            b = Mix(b, V(190, 95, 60), Sat((-0.2f - moist) * 4));
            float beach = Sat(1 - elev * 30);
            float forest = Sat((grove * 1.4f + moist * 0.9f) * 2.5f) * (1 - beach) * (1 - mount);
            float treeness = Sat((tree + 0.1f) * 3.2f) * forest;
            var leaf = Mix(V(28, 92, 30), V(55, 130, 40), Sat(tree * 1.5f + 0.5f));
            b = Mix(b, leaf, treeness);
            b = Mix(b, V(225, 205, 150), beach * 0.9f);
            b = Mix(b, V(120, 110, 90), mount * 0.85f);
            b = Mix(b, V(240, 245, 248), Sat((mount - 0.6f) * 3) * Sat(elev * 3));
            b = Mix(b, V(235, 240, 245), Sat((lat - 0.86f) * 8));
            s.Color = b;
            s.Height = elev * 0.12f + mount * 0.22f + treeness * 0.012f;
            return s;
        }

        static SurfaceSample Ice(PlanetNoise n, Vector3 q)
        {
            var w = new Vector3(n.Fbm(q * 1.6f, 4, 5.1f), n.Fbm(q * 1.6f, 4, 11.7f), n.Fbm(q * 1.6f, 4, 23.3f));
            var qw = q + 0.3f * w;
            float elev = n.Fbm(qw * 1.3f, 7) * 1.2f - 0.05f;
            var s = new SurfaceSample();
            if (elev > 0)
            {
                float crack = Sat((n.Ridged(qw * 6, 4, 13) - 0.82f) * 7);
                float drift = n.Fbm(q * 14, 4, 44);
                var b = Mix(V(225, 238, 248), V(190, 215, 240), Sat(drift * 1.5f + 0.3f));
                b = Mix(b, V(120, 170, 215), crack * 0.7f);
                float ridges = Sat((n.Ridged(qw * 2.5f, 5, 61) - 0.72f) * 5) * Sat(elev * 4);
                b = Mix(b, V(160, 190, 215), ridges * 0.5f);
                s.Color = b;
                s.Height = elev * 0.10f + ridges * 0.16f - crack * 0.02f;
                s.Spec = 0.35f;
                return s;
            }
            n.Cells(qw + 0.08f * w, 4.5f, 3.3f, out float d1, out float d2, out _);
            float floeEdge = Sat(1 - (d2 - d1) * 14) * Sat((n.Fbm(q * 3, 3, 71) + 0.1f) * 3);
            var sea = Mix(V(150, 190, 220), V(95, 145, 190), Sat(-elev * 4));
            sea = Mix(sea, V(150, 195, 225), floeEdge * 0.45f);
            sea = Mix(sea, V(215, 232, 246), Sat((n.Fbm(q * 5, 4, 88) + 0.1f) * 2.5f) * 0.75f);
            s.Color = sea;
            s.Height = -0.005f + floeEdge * 0.01f;
            s.Spec = 0.8f;
            return s;
        }

        static SurfaceSample Rocky(PlanetNoise n, Vector3 q)
        {
            var qw = Warp(n, q, 1.5f, 0.45f);
            float elev = n.Fbm(qw * 1.3f, 7);
            float canyon = Sat((n.Ridged(qw * 2.2f, 5, 71) - 0.8f) * 6);
            float mesa = Mathf.Round(Sat(elev * 1.6f + 0.5f) * 6) / 6;
            float strata = Mathf.Sin(elev * 60 + n.Fbm(q * 8, 2, 9) * 3) * 0.5f + 0.5f;
            var b = Mix(V(150, 95, 60), V(190, 140, 95), mesa);
            b = Mix(b, V(110, 70, 50), strata * 0.25f);
            b = Mix(b, V(80, 55, 45), canyon * 0.85f);
            n.Cells(q, 5, 8.8f, out float cd, out _, out int cid);
            bool crater = cid % 3 == 0;
            float rim = crater ? Sat(1 - Mathf.Abs(cd - 0.32f) * 14) : 0;
            float bowl = crater ? Sat(1 - cd / 0.3f) : 0;
            b = Mix(b, V(120, 80, 60), bowl * 0.5f);
            b = Mix(b, V(215, 170, 120), rim * 0.6f);
            float vein = Sat((n.Ridged(q * 9, 3, 91) - 0.9f) * 12);
            b = Mix(b, V(205, 210, 215), vein * 0.8f);
            return new SurfaceSample { Color = b, Height = mesa * 0.10f - canyon * 0.08f - bowl * 0.05f + rim * 0.04f, Spec = vein * 0.9f };
        }

        static SurfaceSample Gas(PlanetNoise n, Vector3 q)
        {
            float turb = n.Fbm(Vector3.Scale(q, new Vector3(1.5f, 6, 1.5f)), 6, 5);
            float swirl = n.Fbm(Vector3.Scale(q, new Vector3(3, 12, 3)) + Vector3.one * turb * 0.8f, 5, 19);
            float bnd = q.y * 7 + turb * 1.8f + swirl * 0.8f;
            float band = Mathf.Sin(bnd) * 0.5f + 0.5f;
            float band2 = Mathf.Sin(bnd * 2.3f + 1.1f) * 0.5f + 0.5f;
            var b = Mix(V(235, 215, 175), V(205, 140, 85), band);
            b = Mix(b, V(150, 90, 60), Sat(band2 - 0.6f) * 1.6f);
            b = Mix(b, V(250, 240, 220), Sat(swirl * 2) * 0.3f);
            var spotCenter = new Vector3(0.55f, -0.3f, 0.78f).normalized;
            float d = Vector3.Scale(q - spotCenter, new Vector3(1, 1.9f, 1)).magnitude;
            float spot = Sat(1 - d / 0.2f);
            float ring = Mathf.Sin(d * 60 + n.Fbm(q * 10, 2, 3) * 4) * 0.5f + 0.5f;
            b = Mix(b, Mix(V(200, 95, 60), V(240, 170, 120), ring), Mathf.Pow(spot, 0.6f));
            return new SurfaceSample { Color = b, Height = band * 0.01f };
        }

        static SurfaceSample Crystal(PlanetNoise n, Vector3 q)
        {
            var qw = Warp(n, q, 1.4f, 0.3f);
            float elev = n.Fbm(qw * 1.3f, 6);
            n.Cells(qw, 14, 4.4f, out float d1, out float d2, out int cid);
            float facet = (cid % 7) / 7f;
            float field = Sat((n.Fbm(q * 2.5f, 4, 66) + 0.05f) * 4);
            float edge = Sat(1 - (d2 - d1) * 10);
            var ground = Mix(V(70, 60, 95), V(110, 95, 130), Sat(elev + 0.5f));
            var crys = Mix(V(120, 90, 220), V(150, 230, 255), facet);
            crys = Mix(crys, V(235, 215, 255), edge * 0.7f);
            return new SurfaceSample
            {
                Color = Mix(ground, crys, field),
                Height = elev * 0.08f + field * (0.06f - d1 * 0.08f),
                Spec = field * 0.9f,
                Glow = V(170, 130, 255) * (field * edge * 0.25f),
            };
        }

        static SurfaceSample Anomaly(PlanetNoise n, Vector3 q)
        {
            var qw = Warp(n, q, 2f, 0.6f);
            float elev = n.Fbm(qw * 1.5f, 6);
            float fiss = Sat((n.Ridged(qw * 3.0f, 5, 13) - 0.74f) * 6);
            var b = Mix(V(45, 35, 70), V(95, 75, 125), Sat(elev + 0.5f));
            b = Mix(b, V(30, 22, 50), Sat(n.Fbm(q * 6, 3, 8) * 2) * 0.6f);
            float hue = Sat(n.Fbm(q * 1.2f, 3, 2) * 2 + 0.5f);
            var glowColor = Mix(V(60, 240, 230), V(255, 80, 220), hue);
            return new SurfaceSample
            {
                Color = b,
                Height = elev * 0.1f - fiss * 0.05f,
                Spec = 0.2f,
                Glow = glowColor * (Mathf.Pow(fiss, 1.2f) * 1.6f),
            };
        }

        /// <summary>Плотность облаков 0…1 для типа с облаками.</summary>
        public static float Cloud(PlanetNoise n, Vector3 q, float amount)
        {
            if (amount <= 0) return 0;
            float cl = n.Fbm(q * 2.4f, 6, 200) + n.Fbm(q * 8, 3, 300) * 0.3f;
            float thr = 0.55f - amount * 0.6f;
            return Sat((cl - thr + 0.15f) * 3.5f) * 0.9f;
        }
    }
}
