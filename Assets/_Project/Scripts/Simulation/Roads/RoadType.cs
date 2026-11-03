namespace CityBuilder.Simulation.Roads
{
    /// <summary>
    /// Definition eines Straßentyps. Später datengetrieben (JSON/ScriptableObject),
    /// vorerst fest im Code.
    /// </summary>
    public sealed class RoadType
    {
        public string Id { get; }
        public string DisplayName { get; }
        public float Width { get; }
        public float CostPerMeter { get; }
        /// <summary>Lärm direkt an der Straße (0..1).</summary>
        public float NoiseLevel { get; }
        /// <summary>Wie weit der Lärm reicht, in Metern.</summary>
        public float NoiseRadius { get; }

        public RoadType(string id, string displayName, float width, float costPerMeter, float noiseLevel, float noiseRadius)
        {
            Id = id;
            DisplayName = displayName;
            Width = width;
            CostPerMeter = costPerMeter;
            NoiseLevel = noiseLevel;
            NoiseRadius = noiseRadius;
        }

        public static readonly RoadType Small = new RoadType("road_small", "Kleine Straße", 8f, 10f, 0.35f, 40f);
        public static readonly RoadType Medium = new RoadType("road_medium", "Hauptstraße", 14f, 25f, 0.6f, 70f);
        public static readonly RoadType Highway = new RoadType("road_highway", "Schnellstraße", 22f, 60f, 1f, 120f);

        public static readonly RoadType[] All = { Small, Medium, Highway };
    }
}
