using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>
    /// Geländehöhe für das Straßennetz. Straßenhöhen sind absolut (Welt-y); ob eine Straße Brücke,
    /// Damm, Trog oder Tunnel ist, ergibt sich aus dem Abstand zum Gelände (Elevation).
    /// </summary>
    public interface IGround
    {
        float HeightAt(Vector2 position);
        float WaterLevel { get; }
        bool IsWater(Vector2 position);
    }

    /// <summary>Flacher Boden auf Höhe 0 ohne Wasser (Standard, z. B. für Tests).</summary>
    public sealed class FlatGround : IGround
    {
        public static readonly FlatGround Instance = new FlatGround();
        public float HeightAt(Vector2 position) => 0f;
        public float WaterLevel => -1000f;
        public bool IsWater(Vector2 position) => false;
    }
}
