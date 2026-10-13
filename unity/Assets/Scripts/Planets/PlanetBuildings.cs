using System.Collections.Generic;
using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>Что видно с орбиты у шахты.</summary>
    public enum MineLook
    {
        None,
        /// <summary>Строится: недостроенная вышка и кран.</summary>
        Building,
        /// <summary>Добывает: огонь на вышке мигает.</summary>
        Working,
        /// <summary>Стоит (авария, месторождение выработано): огни еле горят.</summary>
        Idle,
    }

    /// <summary>Что осталось от разбитой шахты.</summary>
    public enum WreckLook
    {
        None,
        /// <summary>Метеорит: обломки и кратер рядом.</summary>
        Meteor,
        /// <summary>Бунт: обломки.</summary>
        Riot,
    }

    /// <summary>Постройки на планете — по ним расставляются модели в 3D-окне планеты.</summary>
    public struct PlanetStructures
    {
        public MineLook Mine;
        /// <summary>Экстракторы на дополнительных месторождениях: цвет бака — цвет ресурса.</summary>
        public Color[] Extractors;
        /// <summary>Есть жители: купола поселения.</summary>
        public bool Settlement;
        public bool School;
        /// <summary>Охрана от подкупа жителей.</summary>
        public bool Guard;
        /// <summary>Неразграбленные руины (видны после полной разведки).</summary>
        public bool Ruins;
        /// <summary>Цвет владельца: полоски, флаги, баки.</summary>
        public Color Accent;

        // События.
        /// <summary>Авария на шахте: пожар, чёрный дым, пожарные.</summary>
        public bool Accident;
        /// <summary>Забастовка: пикет с плакатами у шахты (или у поселения, если шахты нет).</summary>
        public bool Strike;
        /// <summary>Эпидемия: карантин у поселения.</summary>
        public bool Epidemic;
        /// <summary>Шахту недавно разбили: обломки на её месте.</summary>
        public WreckLook Wreck;
        /// <summary>Вид жителей; null — по зерну планеты (для показа видов).</summary>
        public int? SpeciesOverride;
        public int Species => SpeciesOverride ?? -1;

        public bool IsEmpty => Mine == MineLook.None && (Extractors == null || Extractors.Length == 0) && !Settlement && !School && !Guard && !Ruins
                               && !Strike && !Epidemic && Wreck == WreckLook.None;

        /// <summary>Ключ для сравнения: изменилось ли что-то, что надо перестроить.</summary>
        public string Key()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append((int)Mine).Append(Settlement ? 'S' : '-').Append(School ? 'K' : '-').Append(Guard ? 'G' : '-').Append(Ruins ? 'R' : '-');
            sb.Append(Accident ? 'A' : '-').Append(Strike ? 'T' : '-').Append(Epidemic ? 'E' : '-').Append((int)Wreck).Append(Species);
            sb.Append(ColorUtility.ToHtmlStringRGB(Accent));
            if (Extractors != null)
                foreach (var c in Extractors) sb.Append(ColorUtility.ToHtmlStringRGB(c));
            return sb.ToString();
        }
    }

    /// <summary>
    /// Ставит модели построек на поверхность планеты. Места выбираются по зерну планеты и её рельефу:
    /// промышленная база (шахта и экстракторы) и поселение (купола, школа, охрана) — на суше, в стороне друг от друга,
    /// руины — отдельно. Постройки дочерние к поверхности, поэтому вращаются вместе с планетой.
    /// </summary>
    public sealed class PlanetBuildings
    {
        /// <summary>Модели в единицах Blender (высота около 2) — в радиусы планеты.</summary>
        const float Scale = 0.1f;

        static readonly Dictionary<string, GameObject> Models = new Dictionary<string, GameObject>();
        static Material _material;

        readonly List<GameObject> _placed = new List<GameObject>();
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        Transform _parent;
        string _key;
        int _seed, _species = -1;
        /// <summary>Модели, где стоят жители: на их места «…__resident» ставятся инопланетяне планеты.</summary>
        static readonly HashSet<string> WithResidents = new HashSet<string> { "settlement", "picket", "quarantine" };

        /// <summary>Главная точка построек в пространстве планеты: её поворачивают к камере.</summary>
        public Vector3 Focus { get; private set; }
        /// <summary>Постройки расставлены заново на последнем вызове Sync.</summary>
        public bool JustPlaced { get; private set; }

        /// <summary>Расставить постройки, если поменялась планета, её облик или постройки.</summary>
        public void Sync(PlanetView view, PlanetStructures s)
        {
            JustPlaced = false;
            var parent = view.Surface;
            if (parent == null)
            {
                Clear();
                return;
            }
            string key = $"{view.Look}/{view.Seed}/{s.Key()}";
            if (parent == _parent && key == _key) return;
            Clear();
            _parent = parent;
            _key = key;
            Place(view.Look, view.Seed, s);
            JustPlaced = true;
        }

        public void Clear()
        {
            foreach (var go in _placed)
                if (go != null) Kill(go);
            _placed.Clear();
            _parent = null;
            _key = null;
        }

        void Place(PlanetLook look, int seed, PlanetStructures s)
        {
            var noise = new PlanetNoise(seed);
            var rnd = new System.Random(seed * 31 + 7);
            _seed = seed;
            _species = s.Species;
            bool gas = look == PlanetLook.Gas;
            var used = new List<Vector3>();
            int extractors = s.Extractors?.Length ?? 0;
            Focus = Vector3.zero;

            Vector3 industry = Vector3.zero, town = Vector3.zero;
            bool wreck = s.Mine == MineLook.None && s.Wreck != WreckLook.None;
            if (s.Mine != MineLook.None || extractors > 0 || wreck)
            {
                industry = FindSite(look, noise, rnd, used, Vector3.zero, 0, 9);
                if (s.Mine != MineLook.None)
                {
                    string model = s.Mine == MineLook.Building ? "mine_build" : gas ? "mine_gas" : "mine_rig";
                    // Дым из трубы — только пока шахта добывает.
                    var go = Put(model, industry, gas ? 1.07f : 0.996f, 1f, rnd, s.Accent, working: s.Mine == MineLook.Working);
                    float glow = s.Mine == MineLook.Idle ? 0.25f : 1.3f;
                    SetLights(go, glow, s.Mine == MineLook.Working ? 1 : 0, s.Accent);
                    // Авария — пожар, дым и пожарные поверх той же шахты; мигалки мигают.
                    if (s.Accident && s.Mine != MineLook.Building)
                        SetLights(PutOver("accident", go, rnd, s.Accent), 1.4f, 1, s.Accent);
                }
                else if (wreck)
                {
                    Put("wreck", industry, gas ? 1.07f : 0.996f, 1f, rnd, s.Accent);
                    if (s.Wreck == WreckLook.Meteor)
                        Put("crater", FindSite(look, noise, rnd, used, industry, 13, 10), gas ? 1.02f : 0.996f, 0.9f, rnd, s.Accent);
                }
                for (int i = 0; i < extractors; i++)
                {
                    var site = FindSite(look, noise, rnd, used, industry, 15, 10);
                    Put("extractor", site, gas ? 1.04f : 0.996f, 0.8f, rnd, s.Extractors[i]);
                }
                Focus = industry;
            }

            if (s.Settlement || s.School || s.Guard)
            {
                // Поселение — в стороне от базы, но рядом: в кадре «горизонт» видно вместе с ней.
                town = FindSite(look, noise, rnd, used, industry, industry == Vector3.zero ? 0 : 20, 14);
                if (s.Settlement) Put("settlement", town, gas ? 1.03f : 0.996f, 0.95f, rnd, s.Accent);
                if (s.School) Put("school", FindSite(look, noise, rnd, used, town, 17, 12), gas ? 1.03f : 0.996f, 0.8f, rnd, s.Accent);
                if (s.Guard) Put("guard_tower", FindSite(look, noise, rnd, used, town, 16, 11), gas ? 1.03f : 0.996f, 0.75f, rnd, s.Accent);
                if (industry == Vector3.zero) Focus = town;
            }

            // Пикет встаёт у шахты — мешать работе; если шахты нет — у поселения.
            if (s.Strike)
            {
                var near = s.Mine != MineLook.None || wreck ? industry : town;
                Put("picket", FindSite(look, noise, rnd, used, near, near == Vector3.zero ? 0 : 11, 8), gas ? 1.03f : 0.996f, 0.8f, rnd, s.Accent);
                if (Focus == Vector3.zero) Focus = near;
            }
            if (s.Epidemic)
            {
                var near = town != Vector3.zero ? town : industry;
                SetLights(Put("quarantine", FindSite(look, noise, rnd, used, near, near == Vector3.zero ? 0 : 15, 10), gas ? 1.03f : 0.996f, 0.85f, rnd, s.Accent),
                    1.3f, 1, s.Accent);
            }

            if (s.Ruins)
            {
                var site = FindSite(look, noise, rnd, used, Vector3.zero, 0, 12);
                Put("ruins", site, gas ? 1.03f : 0.996f, 0.8f, rnd, s.Accent);
                if (industry == Vector3.zero && town == Vector3.zero) Focus = site;
            }
        }

        /// <summary>
        /// Место на суше подальше от полюсов и от уже занятых мест (minGap — в градусах).
        /// near ≠ 0 и spread > 0 — примерно в spread градусах от near.
        /// </summary>
        static Vector3 FindSite(PlanetLook look, PlanetNoise noise, System.Random rnd, List<Vector3> used, Vector3 near, float spread, float minGap)
        {
            Vector3 best = Vector3.forward;
            float bestScore = float.MinValue;
            for (int attempt = 0; attempt < 80; attempt++)
            {
                Vector3 q;
                if (near == Vector3.zero || spread <= 0)
                    q = Random3(rnd);
                else
                {
                    var axis = Vector3.Cross(near, Random3(rnd)).normalized;
                    float angle = spread * (0.7f + 0.3f * (float)rnd.NextDouble());
                    q = Quaternion.AngleAxis(angle, axis) * near.normalized;
                }
                // Лучше всего видно на средних северных широтах: в кадре это верх планеты,
                // постройки там видны сбоку, а не сверху. На суше — обязательно (где она есть).
                float score = -Mathf.Abs(q.y - PreferredLatitude) * 2;
                if (!OnLand(look, noise, q)) score -= 10;
                foreach (var u in used)
                    if (Vector3.Angle(u, q) < minGap) score -= 5;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = q;
                }
                if (score > -0.6f) break;
            }
            used.Add(best);
            return best;
        }

        /// <summary>Синус широты, где удобнее всего ставить постройки (около 35°).</summary>
        const float PreferredLatitude = 0.57f;

        static Vector3 Random3(System.Random rnd)
        {
            var v = new Vector3((float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1, (float)rnd.NextDouble() * 2 - 1);
            return v.sqrMagnitude < 1e-4f ? Vector3.forward : v.normalized;
        }

        static bool OnLand(PlanetLook look, PlanetNoise noise, Vector3 q)
        {
            switch (look)
            {
                case PlanetLook.Jungle: return PlanetSurface.Evaluate(look, noise, q).Height > 0.004f;
                case PlanetLook.Ice: return PlanetSurface.Evaluate(look, noise, q).Height > 0.008f;
                default: return true;
            }
        }

        GameObject Put(string model, Vector3 q, float radius, float size, System.Random rnd, Color accent, bool working = true) =>
            Spawn(model, q * radius, Quaternion.FromToRotation(Vector3.up, q) * Quaternion.AngleAxis((float)rnd.NextDouble() * 360, Vector3.up),
                Scale * size, rnd, accent, working);

        /// <summary>Модель поверх другой, в той же точке и с тем же поворотом (пожар на шахте).</summary>
        GameObject PutOver(string model, GameObject target, System.Random rnd, Color accent)
        {
            if (target == null) return null;
            var t = target.transform;
            return Spawn(model, t.localPosition, t.localRotation, t.localScale.x, rnd, accent, true);
        }

        GameObject Spawn(string model, Vector3 position, Quaternion rotation, float scale, System.Random rnd, Color accent, bool working)
        {
            var prefab = LoadModel(model);
            if (prefab == null) return null;
            // Держатель стоит на поверхности, внутри — модель как есть: корпус и подвижные части.
            var go = new GameObject(model);
            var t = go.transform;
            t.SetParent(_parent, false);
            t.localPosition = position;
            t.localRotation = rotation;
            t.localScale = Vector3.one * scale;
            float offset = (float)rnd.NextDouble();
            var model3d = Instantiate(model, t, accent, offset, working);
            if (WithResidents.Contains(model))
                Residents.Populate(model3d, _seed, _placed.Count, Material, _block, _species);
            SetLights(go, 1.3f, 0, accent);
            _placed.Add(go);
            return go;
        }

        void SetLights(GameObject go, float glow, float pulse, Color accent)
        {
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                r.GetPropertyBlock(_block);
                _block.SetColor("_Accent", accent);
                _block.SetFloat("_Glow", glow);
                _block.SetFloat("_Pulse", pulse);
                r.SetPropertyBlock(_block);
            }
        }

        static Material Material
        {
            get
            {
                if (_material == null) _material = new Material(Shader.Find("PlanetTrader/Building")) { name = "Постройки" };
                return _material;
            }
        }

        /// <summary>
        /// Поставить модель внутрь parent: общий материал построек, движение частей, дым и огонь, цвет владельца.
        /// Годится не только для планет — так же на карте стоят биржа, зонды и грузовики.
        /// </summary>
        public static GameObject Instantiate(string model, Transform parent, Color accent, float offset, bool working = true)
        {
            var prefab = LoadModel(model);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            go.name = model;
            int layer = parent.gameObject.layer;
            foreach (var tr in go.GetComponentsInChildren<Transform>()) tr.gameObject.layer = layer;
            var block = new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                r.sharedMaterial = Material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.GetPropertyBlock(block);
                block.SetColor("_Accent", accent);
                r.SetPropertyBlock(block);
            }
            BuildingMotion.Attach(go, offset);
            SmokeEmitter.Attach(go, offset, working);
            return go;
        }

        /// <summary>Модель из Resources/Buildings (FBX, выгруженный tools/buildings/make_buildings.py).</summary>
        public static GameObject LoadModel(string model)
        {
            if (Models.TryGetValue(model, out var go) && go != null) return go;
            go = Resources.Load<GameObject>("Buildings/" + model);
            if (go == null) Debug.LogWarning($"Нет модели постройки Buildings/{model}");
            Models[model] = go;
            return go;
        }

        static void Kill(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
