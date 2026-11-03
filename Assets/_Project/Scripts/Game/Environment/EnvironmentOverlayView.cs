using CityBuilder.Simulation.Environment;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.Environment
{
    /// <summary>
    /// Blendet ein Umwelt-Raster als farbige Fläche über der Karte ein (wie die Info-Ansichten
    /// in Cities: Skylines). N = Lärm, P = Abgas, F2 = Bodenwert (G6), nochmal drücken = aus.
    /// </summary>
    public sealed class EnvironmentOverlayView : MonoBehaviour
    {
        public enum Overlay { None, Noise, AirPollution, LandValue }

        [SerializeField] SimulationRunner runner;
        [SerializeField] Material overlayMaterial;
        [SerializeField] float height = 0.2f;
        [SerializeField] Gradient noiseGradient;
        [SerializeField] Gradient pollutionGradient;

        Texture2D texture;
        Color32[] pixels;
        MeshRenderer quadRenderer;
        Material materialInstance;
        int shownVersion = -1;
        GridLayer shownLayer;

        public Overlay Current { get; private set; } = Overlay.None;

        void Start()
        {
            var env = runner.World.Environment;
            var layer = env.Noise;

            texture = new Texture2D(layer.Width, layer.Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "EnvironmentOverlay"
            };
            pixels = new Color32[layer.Width * layer.Height];

            // Fläche folgt dem Gelände (Gitter im Raster des Umwelt-Layers)
            var quad = new GameObject("Environment Overlay");
            quad.transform.SetParent(transform, false);
            quad.AddComponent<MeshFilter>().sharedMesh = BuildTerrainGrid(layer);
            quad.AddComponent<MeshRenderer>();

            materialInstance = new Material(overlayMaterial) { mainTexture = texture };
            quadRenderer = quad.GetComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = materialInstance;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
            quadRenderer.enabled = false;

            if (noiseGradient == null || noiseGradient.colorKeys.Length < 2) noiseGradient = DefaultGradient(new Color(1f, 0.9f, 0.2f), new Color(0.9f, 0.1f, 0.1f));
            if (pollutionGradient == null || pollutionGradient.colorKeys.Length < 2) pollutionGradient = DefaultGradient(new Color(0.6f, 0.5f, 0.3f), new Color(0.25f, 0.15f, 0.05f));
        }

        Mesh BuildTerrainGrid(GridLayer layer)
        {
            var terrain = runner.World.Terrain;
            int n = layer.Width;
            var verts = new Vector3[(n + 1) * (n + 1)];
            var uvs = new Vector2[verts.Length];
            for (int y = 0; y <= n; y++)
            for (int x = 0; x <= n; x++)
            {
                var p = layer.Origin + new System.Numerics.Vector2(x * layer.CellSize, y * layer.CellSize);
                float h = Mathf.Max(terrain.HeightAt(p), terrain.WaterLevel) + height + 1f;
                verts[y * (n + 1) + x] = new Vector3(p.X, h, p.Y);
                uvs[y * (n + 1) + x] = new Vector2(x / (float)n, y / (float)n);
            }
            var tris = new int[n * n * 6];
            int k = 0;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                int b = y * (n + 1) + x;
                tris[k++] = b; tris[k++] = b + n + 1; tris[k++] = b + 1;
                tris[k++] = b + 1; tris[k++] = b + n + 1; tris[k++] = b + n + 2;
            }
            var mesh = new Mesh { name = "OverlayGrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        void OnDestroy()
        {
            if (texture != null) Destroy(texture);
            if (materialInstance != null) Destroy(materialInstance);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.nKey.wasPressedThisFrame) Toggle(Overlay.Noise);
                if (kb.pKey.wasPressedThisFrame) Toggle(Overlay.AirPollution);
                if (kb.f2Key.wasPressedThisFrame) Toggle(Overlay.LandValue); // G6
            }

            if (Current == Overlay.None) return;

            var env = runner.World.Environment;
            // --- G6 Bodenwert: eigenes Raster im selben Gitter, Farbverlauf rot (niedrig) → gelb → grün (hoch)
            if (Current == Overlay.LandValue)
            {
                var lv = runner.World.LandValue?.Layer;
                if (lv == null || (lv == shownLayer && lv.Version == shownVersion)) return;
                if (landValueGradient == null) landValueGradient = LandValueGradient();
                RefreshTexture(lv, landValueGradient, opaqueLow: true);
                return;
            }
            // --- Ende G6
            var layer = Current == Overlay.Noise ? env.Noise : env.AirPollution;
            if (layer == shownLayer && layer.Version == shownVersion) return;

            RefreshTexture(layer, Current == Overlay.Noise ? noiseGradient : pollutionGradient);
        }

        // --- G6 Bodenwert
        Gradient landValueGradient;

        static Gradient LandValueGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(0.85f, 0.2f, 0.15f), 0f), new GradientColorKey(new Color(0.95f, 0.85f, 0.25f), 0.45f),
                        new GradientColorKey(new Color(0.2f, 0.75f, 0.3f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
        // --- Ende G6

        public void Toggle(Overlay overlay)
        {
            Current = Current == overlay ? Overlay.None : overlay;
            quadRenderer.enabled = Current != Overlay.None;
            shownLayer = null;
        }

        void RefreshTexture(GridLayer layer, Gradient gradient, bool opaqueLow = false)
        {
            var values = layer.RawValues;
            for (int i = 0; i < values.Length; i++)
            {
                float v = Mathf.Clamp01(values[i]);
                if (v <= 0.001f) { pixels[i] = new Color32(0, 0, 0, 0); continue; }
                Color c = gradient.Evaluate(v);
                // Bodenwert (G6): niedrige Werte sind die Aussage → gleichmäßig deckend statt nach Wert
                c.a = opaqueLow ? 0.6f : Mathf.Lerp(0.25f, 0.75f, v);
                pixels[i] = c;
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
            shownLayer = layer;
            shownVersion = layer.Version;
        }

        static Gradient DefaultGradient(Color low, Color high)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(low, 0f), new GradientColorKey(high, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
