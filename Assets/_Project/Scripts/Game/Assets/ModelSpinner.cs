using UnityEngine;

namespace CityBuilder.Game.Assets
{
    /// <summary>
    /// Eigene Assets: dreht ein bewegliches Kindobjekt eines Blender-Modells (Konvention: benanntes Kindobjekt, Ursprung =
    /// Drehpunkt) um seine lokale Z-Achse – z. B. „Wheel“ des Riesenrads. Tempo folgt der Animationszeit des Spiels.
    /// </summary>
    public sealed class ModelSpinner : MonoBehaviour
    {
        /// <summary>Grad je Sekunde Animationszeit.</summary>
        public float DegreesPerSecond = 6f;
        public System.Func<float> TimeScale;

        void Update()
        {
            float scale = TimeScale != null ? Mathf.Clamp(TimeScale(), 0f, 3f) : 1f;
            transform.Rotate(0f, 0f, DegreesPerSecond * scale * Time.deltaTime, Space.Self);
        }

        /// <summary>Hängt Spinner an alle Kindobjekte mit einem der Namen (Groß/Klein egal).</summary>
        public static void Attach(GameObject root, System.Func<float> timeScale, float degreesPerSecond, params string[] names)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                foreach (var n in names)
                    if (string.Equals(t.name, n, System.StringComparison.OrdinalIgnoreCase) && t.GetComponent<ModelSpinner>() == null)
                    {
                        var s = t.gameObject.AddComponent<ModelSpinner>();
                        s.TimeScale = timeScale;
                        s.DegreesPerSecond = degreesPerSecond;
                    }
        }
    }
}
