using System.Collections.Generic;
using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Жители планет — десять видов инопланетян (модели alien_* из tools/buildings). На каждой планете свой вид,
    /// цвет кожи и палитра одежды — по зерну планеты; в толпе одежда немного разная.
    /// </summary>
    public static class Residents
    {
        sealed class Species
        {
            public string Model, Name;
            /// <summary>Оттенки кожи этого вида: диапазон тона (0…1, может переходить через 1), насыщенность, яркость.</summary>
            public float Hue0, Hue1, Sat0, Sat1, Val0, Val1;
        }

        static readonly Species[] All =
        {
            new Species { Model = "alien_grey", Name = "серые гуманоиды", Hue0 = 0.25f, Hue1 = 0.65f, Sat0 = 0.05f, Sat1 = 0.3f, Val0 = 0.6f, Val1 = 0.85f },
            new Species { Model = "alien_octo", Name = "осьминоги", Hue0 = 0.75f, Hue1 = 1.08f, Sat0 = 0.4f, Sat1 = 0.7f, Val0 = 0.6f, Val1 = 0.9f },
            new Species { Model = "alien_centaur", Name = "кентавры", Hue0 = 0.02f, Hue1 = 0.6f, Sat0 = 0.3f, Sat1 = 0.6f, Val0 = 0.45f, Val1 = 0.8f },
            new Species { Model = "alien_slug", Name = "слизняки", Hue0 = 0.12f, Hue1 = 0.85f, Sat0 = 0.45f, Sat1 = 0.75f, Val0 = 0.65f, Val1 = 0.95f },
            new Species { Model = "alien_lizard", Name = "ящеры", Hue0 = 0.05f, Hue1 = 0.5f, Sat0 = 0.45f, Sat1 = 0.75f, Val0 = 0.45f, Val1 = 0.8f },
            new Species { Model = "alien_shroom", Name = "грибовики", Hue0 = -0.05f, Hue1 = 0.1f, Sat0 = 0.5f, Sat1 = 0.85f, Val0 = 0.55f, Val1 = 0.9f },
            new Species { Model = "alien_robot", Name = "роботы", Hue0 = 0f, Hue1 = 1f, Sat0 = 0.2f, Sat1 = 0.6f, Val0 = 0.6f, Val1 = 0.9f },
            new Species { Model = "alien_mantis", Name = "богомолы", Hue0 = 0.1f, Hue1 = 0.4f, Sat0 = 0.45f, Sat1 = 0.75f, Val0 = 0.45f, Val1 = 0.8f },
            new Species { Model = "alien_jelly", Name = "медузы", Hue0 = 0.45f, Hue1 = 0.95f, Sat0 = 0.25f, Sat1 = 0.5f, Val0 = 0.8f, Val1 = 1f },
            new Species { Model = "alien_bird", Name = "птицы", Hue0 = 0f, Hue1 = 1f, Sat0 = 0.5f, Sat1 = 0.85f, Val0 = 0.7f, Val1 = 0.95f },
        };

        public static int Count => All.Length;

        /// <summary>Вид жителей планеты с этим зерном.</summary>
        public static int SpeciesOf(int seed) => (int)((uint)(seed * 2654435761u) % (uint)All.Length);

        public static string SpeciesName(int species) => All[species].Name;

        /// <summary>
        /// Поставить жителей на места «…__resident» в модели (пикет, поселение, карантин).
        /// Вид, кожа и палитра одежды — по зерну планеты seed (species ≥ 0 — вид задан явно);
        /// group — номер группы на планете, чтобы в разных группах были разные лица и наряды.
        /// </summary>
        public static void Populate(GameObject model, int seed, int group, Material material, MaterialPropertyBlock block, int species = -1)
        {
            int kind = species >= 0 ? species % All.Length : SpeciesOf(seed);
            var sp = All[kind];
            var prefab = PlanetBuildings.LoadModel(sp.Model);
            if (prefab == null) return;
            var rnd = new System.Random(seed * 7 + 3 + group * 7919);
            var skin = SkinColor(sp, new System.Random(seed));
            var clothHue = (float)new System.Random(seed + 11).NextDouble();

            var spots = new List<Transform>();
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (BuildingMotion.PartName(t.name) == "resident") spots.Add(t);
            int layer = model.layer;
            foreach (var spot in spots)
            {
                var r = Object.Instantiate(prefab, spot, false);
                r.name = sp.Model;
                foreach (var tr in r.GetComponentsInChildren<Transform>()) tr.gameObject.layer = layer;
                // Одежда у всех из палитры планеты, но у каждого своего оттенка; кожа у сородичей чуть разная.
                var cloth = Color.HSVToRGB(Mathf.Repeat(clothHue + (float)(rnd.NextDouble() - 0.5) * 0.18f, 1),
                    0.35f + (float)rnd.NextDouble() * 0.4f, 0.45f + (float)rnd.NextDouble() * 0.45f);
                var mySkin = skin * (0.9f + (float)rnd.NextDouble() * 0.2f);
                foreach (var mr in r.GetComponentsInChildren<MeshRenderer>())
                {
                    mr.sharedMaterial = material;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.GetPropertyBlock(block);
                    block.SetColor("_Skin", mySkin);
                    block.SetColor("_Cloth", cloth);
                    mr.SetPropertyBlock(block);
                }
                BuildingMotion.Attach(r, (float)rnd.NextDouble());
            }
        }

        static Color SkinColor(Species sp, System.Random rnd)
        {
            float h = Mathf.Repeat(Mathf.Lerp(sp.Hue0, sp.Hue1, (float)rnd.NextDouble()), 1);
            float s = Mathf.Lerp(sp.Sat0, sp.Sat1, (float)rnd.NextDouble());
            float v = Mathf.Lerp(sp.Val0, sp.Val1, (float)rnd.NextDouble());
            return Color.HSVToRGB(h, s, v);
        }
    }
}
