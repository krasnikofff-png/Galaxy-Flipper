using UnityEngine;

namespace PlanetTrader.Visuals
{
    /// <summary>
    /// Планета на сцене: поверхность, облака и ореол атмосферы. Текстуры запекаются при старте
    /// по типу и зерну — у каждой планеты свой облик.
    /// </summary>
    public sealed class PlanetView : MonoBehaviour
    {
        public PlanetLook Look = PlanetLook.Jungle;
        public int Seed = 7;
        [Tooltip("Ширина текстуры; высота вдвое меньше. 1024 — для крупного плана, 256 — для карты.")]
        public int Resolution = 1024;
        [Tooltip("Градусов в секунду.")]
        public float SpinSpeed = 4f;
        public float CloudDrift = 1.5f;

        Transform _surface, _clouds, _halo;
        Material _surfaceMat, _cloudMat, _haloMat;
        PlanetTextures _tex;
        static Mesh _sphere;

        void Start() => Rebuild();

        /// <summary>Пересобрать планету: после смены типа или зерна.</summary>
        public void Rebuild()
        {
            Cleanup();
            var style = PlanetSurface.Style(Look);
            _tex = PlanetBaker.Bake(Look, Seed, Resolution);
            if (_sphere == null) _sphere = SphereMesh.Build(96, 48);

            _surfaceMat = new Material(Shader.Find("PlanetTrader/Planet"));
            _surfaceMat.SetTexture("_MainTex", _tex.Albedo);
            _surfaceMat.SetTexture("_NormalTex", _tex.Normal);
            _surfaceMat.SetTexture("_EmissionTex", _tex.Emission);
            _surfaceMat.SetColor("_AtmoColor", style.Atmosphere);
            _surface = Layer("Поверхность", _surfaceMat, 1f);

            if (_tex.Clouds != null)
            {
                _cloudMat = new Material(Shader.Find("PlanetTrader/PlanetClouds"));
                _cloudMat.SetTexture("_MainTex", _tex.Clouds);
                _cloudMat.SetColor("_Color", style.CloudTint);
                _clouds = Layer("Облака", _cloudMat, 1.012f);
            }

            _haloMat = new Material(Shader.Find("PlanetTrader/PlanetAtmosphere"));
            _haloMat.SetColor("_Color", style.Atmosphere);
            _haloMat.SetFloat("_Inner", 1f / HaloScale);
            _halo = Layer("Атмосфера", _haloMat, HaloScale);
        }

        const float HaloScale = 1.14f;

        Transform Layer(string name, Material mat, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = _sphere;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        void Update()
        {
            if (_surface != null) _surface.Rotate(Vector3.up, -SpinSpeed * Time.deltaTime, Space.Self);
            if (_clouds != null) _clouds.Rotate(Vector3.up, -(SpinSpeed + CloudDrift) * Time.deltaTime, Space.Self);
        }

        void Cleanup()
        {
            foreach (Transform child in transform) Destroy(child.gameObject);
            if (_tex != null)
            {
                Destroy(_tex.Albedo);
                Destroy(_tex.Normal);
                if (_tex.Emission != Texture2D.blackTexture) Destroy(_tex.Emission);
                if (_tex.Clouds != null) Destroy(_tex.Clouds);
            }
            if (_surfaceMat) Destroy(_surfaceMat);
            if (_cloudMat) Destroy(_cloudMat);
            if (_haloMat) Destroy(_haloMat);
            _clouds = null;
        }

        void OnDestroy() => Cleanup();
    }

    /// <summary>UV-сфера радиуса 1 с той же развёрткой, что у запечённых текстур.</summary>
    public static class SphereMesh
    {
        public static Mesh Build(int segments, int rings)
        {
            var verts = new Vector3[(segments + 1) * (rings + 1)];
            var uvs = new Vector2[verts.Length];
            var normals = new Vector3[verts.Length];
            for (int r = 0; r <= rings; r++)
            for (int s = 0; s <= segments; s++)
            {
                float u = s / (float)segments, v = r / (float)rings;
                var p = PlanetBaker.Direction(u, v);
                int i = r * (segments + 1) + s;
                verts[i] = p;
                normals[i] = p;
                uvs[i] = new Vector2(u, v);
            }
            var tris = new int[segments * rings * 6];
            int t = 0;
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = r * (segments + 1) + s, b = a + segments + 1;
                tris[t++] = a; tris[t++] = b; tris[t++] = a + 1;
                tris[t++] = a + 1; tris[t++] = b; tris[t++] = b + 1;
            }
            var mesh = new Mesh { name = "Planet sphere", vertices = verts, normals = normals, uv = uvs, triangles = tris };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
