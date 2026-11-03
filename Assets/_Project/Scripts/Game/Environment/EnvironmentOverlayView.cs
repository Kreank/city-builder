using CityBuilder.Simulation.Environment;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.Environment
{
    /// <summary>
    /// Blendet ein Umwelt-Raster als farbige Fläche über der Karte ein (wie die Info-Ansichten
    /// in Cities: Skylines). N = Lärm, P = Abgas, nochmal drücken = aus.
    /// </summary>
    public sealed class EnvironmentOverlayView : MonoBehaviour
    {
        public enum Overlay { None, Noise, AirPollution }

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

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Environment Overlay";
            Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(transform, false);
            float size = layer.Width * layer.CellSize;
            quad.transform.position = new Vector3(layer.Origin.X + size * 0.5f, height, layer.Origin.Y + size * 0.5f);
            quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = new Vector3(size, size, 1f);

            materialInstance = new Material(overlayMaterial) { mainTexture = texture };
            quadRenderer = quad.GetComponent<MeshRenderer>();
            quadRenderer.sharedMaterial = materialInstance;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
            quadRenderer.enabled = false;

            if (noiseGradient == null || noiseGradient.colorKeys.Length < 2) noiseGradient = DefaultGradient(new Color(1f, 0.9f, 0.2f), new Color(0.9f, 0.1f, 0.1f));
            if (pollutionGradient == null || pollutionGradient.colorKeys.Length < 2) pollutionGradient = DefaultGradient(new Color(0.6f, 0.5f, 0.3f), new Color(0.25f, 0.15f, 0.05f));
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
            }

            if (Current == Overlay.None) return;

            var env = runner.World.Environment;
            var layer = Current == Overlay.Noise ? env.Noise : env.AirPollution;
            if (layer == shownLayer && layer.Version == shownVersion) return;

            RefreshTexture(layer, Current == Overlay.Noise ? noiseGradient : pollutionGradient);
        }

        public void Toggle(Overlay overlay)
        {
            Current = Current == overlay ? Overlay.None : overlay;
            quadRenderer.enabled = Current != Overlay.None;
            shownLayer = null;
        }

        void RefreshTexture(GridLayer layer, Gradient gradient)
        {
            var values = layer.RawValues;
            for (int i = 0; i < values.Length; i++)
            {
                float v = Mathf.Clamp01(values[i]);
                if (v <= 0.001f) { pixels[i] = new Color32(0, 0, 0, 0); continue; }
                Color c = gradient.Evaluate(v);
                c.a = Mathf.Lerp(0.25f, 0.75f, v);
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
