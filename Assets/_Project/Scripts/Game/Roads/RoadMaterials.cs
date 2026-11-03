using UnityEngine;

namespace CityBuilder.Game.Roads
{
    /// <summary>Oberflächen eines Straßen-Meshes – je eine Submesh/ein Material.</summary>
    public enum RoadSurface
    {
        Asphalt = 0,
        Sidewalk = 1,
        Marking = 2,
        BusLane = 3,
        TramBed = 4,
        Rail = 5,
        Median = 6,
    }

    /// <summary>
    /// Platzhalter-Materialien für Straßen, zur Laufzeit aus einem Basis-Material abgeleitet
    /// (nur andere Farben). Wird später durch echte Materialien/Texturen ersetzt.
    /// </summary>
    public sealed class RoadMaterials
    {
        public const int SurfaceCount = 7;

        static readonly Color[] Colors =
        {
            new Color(0.20f, 0.20f, 0.22f), // Asphalt
            new Color(0.62f, 0.60f, 0.57f), // Gehweg
            new Color(0.92f, 0.92f, 0.90f), // Markierung
            new Color(0.55f, 0.18f, 0.16f), // Busspur
            new Color(0.38f, 0.36f, 0.33f), // Gleisbett
            new Color(0.12f, 0.11f, 0.10f), // Schiene
            new Color(0.33f, 0.45f, 0.26f), // Mittelstreifen
        };

        public Material[] All { get; } = new Material[SurfaceCount];
        public Material this[RoadSurface s] => All[(int)s];

        public RoadMaterials(Material baseMaterial)
        {
            for (int i = 0; i < SurfaceCount; i++)
            {
                var m = new Material(baseMaterial) { name = $"Road_{(RoadSurface)i}" };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Colors[i]);
                else m.color = Colors[i];
                All[i] = m;
            }
        }

        public void Destroy()
        {
            foreach (var m in All) if (m != null) Object.Destroy(m);
        }
    }
}
