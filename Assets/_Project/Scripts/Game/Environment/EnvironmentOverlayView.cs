using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.Environment
{
    /// <summary>
    /// Blendet ein Umwelt-Raster als farbige Fläche über der Karte ein (wie die Info-Ansichten
    /// in Cities: Skylines). N = Lärm, P = Abgas, F2 = Bodenwert (G6), F3 = Ressourcen (Welt, durchschalten), nochmal drücken = aus.
    /// </summary>
    public sealed class EnvironmentOverlayView : MonoBehaviour
    {
        /// <summary><see cref="Resource"/> = Ressourcen-Raster der Welt (Etappe 5), Art in <see cref="CurrentResourceId"/>, F3 schaltet durch.</summary>
        public enum Overlay { None, Noise, AirPollution, LandValue, Resource, WaterQuality } // WaterQuality: G3 erweitert (Knopf in der Ansichtsleiste, keine Taste)

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
                if (kb.f3Key.wasPressedThisFrame) CycleResource(); // Welt (Etappe 5)
                if (kb.f2Key.wasPressedThisFrame) Toggle(Overlay.LandValue); // G6
            }

            if (Current == Overlay.None) return;

            var env = runner.World.Environment;
            // --- Welt (Etappe 5): Ressourcen-Raster (gleiche Zellen wie die Umwelt-Raster)
            if (Current == Overlay.Resource)
            {
                var res = runner.World.Resources?.Layer(CurrentResourceId);
                if (res == null || res.Width != env.Noise.Width) { Toggle(Overlay.Resource); return; }
                if (res == shownLayer && res.Version == shownVersion) return;
                RefreshTexture(res, ResourceGradient(resourceIndex));
                return;
            }
            // --- Ende Welt
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
            // --- G3 erweitert: Wasserverschmutzung (nur Wasserzellen), braun = verschmutzt
            if (Current == Overlay.WaterQuality)
            {
                var wq = runner.World.WaterQuality?.Pollution;
                if (wq == null || wq.Width != env.Noise.Width) { Toggle(Overlay.WaterQuality); return; }
                if (wq == shownLayer && wq.Version == shownVersion) return;
                if (waterGradient == null) waterGradient = DefaultGradient(new Color(0.55f, 0.75f, 0.35f), new Color(0.4f, 0.22f, 0.08f));
                RefreshTexture(wq, waterGradient);
                return;
            }
            // --- Ende
            var layer = Current == Overlay.Noise ? env.Noise : env.AirPollution;
            if (layer == shownLayer && layer.Version == shownVersion) return;

            RefreshTexture(layer, Current == Overlay.Noise ? noiseGradient : pollutionGradient);
        }

        // --- Welt (Etappe 5): Ressourcen-Ansicht
        int resourceIndex = -1;
        Gradient[] resourceGradients;

        /// <summary>Gezeigte Ressource (Id aus <see cref="ResourceIds"/>) oder null.</summary>
        public string CurrentResourceId => Current == Overlay.Resource && resourceIndex >= 0 ? ResourceIds.All[resourceIndex] : null;

        /// <summary>F3: Boden → Wald → Erz → Kohle → Öl → Sand → Grundwasser → aus.</summary>
        public void CycleResource()
        {
            if (Current != Overlay.Resource) resourceIndex = 0;
            else resourceIndex++;
            if (resourceIndex >= ResourceIds.All.Length) { resourceIndex = -1; Current = Overlay.None; }
            else Current = Overlay.Resource;
            quadRenderer.enabled = Current != Overlay.None;
            shownLayer = null;
        }

        /// <summary>Bestimmte Ressource zeigen (null = aus).</summary>
        public void ShowResource(string id)
        {
            resourceIndex = id == null ? -1 : System.Array.IndexOf(ResourceIds.All, id);
            Current = resourceIndex >= 0 ? Overlay.Resource : Overlay.None;
            quadRenderer.enabled = Current != Overlay.None;
            shownLayer = null;
        }

        Gradient ResourceGradient(int i)
        {
            if (resourceGradients == null)
                resourceGradients = new[]
                {
                    DefaultGradient(new Color(0.75f, 0.65f, 0.35f), new Color(0.2f, 0.55f, 0.1f)),   // Boden
                    DefaultGradient(new Color(0.55f, 0.75f, 0.35f), new Color(0.05f, 0.35f, 0.1f)),  // Wald
                    DefaultGradient(new Color(0.75f, 0.55f, 0.45f), new Color(0.55f, 0.15f, 0.1f)),  // Erz
                    DefaultGradient(new Color(0.55f, 0.55f, 0.55f), new Color(0.08f, 0.08f, 0.08f)), // Kohle
                    DefaultGradient(new Color(0.55f, 0.45f, 0.65f), new Color(0.2f, 0.05f, 0.3f)),   // Öl
                    DefaultGradient(new Color(0.95f, 0.9f, 0.6f), new Color(0.85f, 0.65f, 0.2f)),    // Sand/Kies
                    DefaultGradient(new Color(0.6f, 0.85f, 1f), new Color(0.05f, 0.25f, 0.8f)),      // Grundwasser
                };
            return resourceGradients[Mathf.Clamp(i, 0, resourceGradients.Length - 1)];
        }

        GUIStyle legendStyle;

        void OnGUI()
        {
            if (Current != Overlay.Resource || runner == null || runner.World.Resources == null) return;
            var kind = runner.World.Resources.Kind(CurrentResourceId);
            if (kind == null) return;
            if (legendStyle == null) legendStyle = new GUIStyle(GUI.skin.box) { fontSize = 13, alignment = TextAnchor.MiddleCenter, richText = true };
            string region = runner.World.StartRegion != null ? $" · Region {runner.World.StartRegion.Name}" : "";
            var rect = new Rect(Screen.width * 0.5f - 210f, CityBuilder.Game.UI.HudLayout.TopInset + 4f, 420f, 26f);
            GUI.Box(rect, $"<b>{kind.DisplayName}</b> (F3 weiter){region} · gesamt {runner.World.Resources.TotalAmount(kind.Id):N0} {kind.Unit}", legendStyle);
        }
        // --- Ende Welt
        Gradient waterGradient; // G3 erweitert: Wasserqualität
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
