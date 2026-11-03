namespace CityBuilder.Simulation.Roads
{
    public enum LaneKind
    {
        Car,
        Bus,        // Busspur (später: auch Taxis/Einsatzfahrzeuge)
        Tram,       // Straßenbahngleis
        Sidewalk,   // Gehweg
        Shoulder,   // Standstreifen / Randstreifen
        Median,     // Mittelstreifen
    }

    /// <summary>Fahrtrichtung relativ zur Segmentrichtung (Start → Ende).</summary>
    public enum LaneDirection
    {
        None,
        Forward,
        Backward,
    }

    /// <summary>Eine Spur im Querschnitt einer Straße.</summary>
    public sealed class LaneDefinition
    {
        public LaneKind Kind { get; }
        public LaneDirection Direction { get; }
        public float Width { get; }

        /// <summary>Abstand der Spurmitte zur Straßenmitte. Positiv = rechts (in Segmentrichtung gesehen).</summary>
        public float Offset { get; }

        public bool IsVehicleLane => Kind == LaneKind.Car || Kind == LaneKind.Bus || Kind == LaneKind.Tram;
        public float Left => Offset - Width * 0.5f;
        public float Right => Offset + Width * 0.5f;

        public LaneDefinition(LaneKind kind, LaneDirection direction, float width, float offset)
        {
            Kind = kind;
            Direction = direction;
            Width = width;
            Offset = offset;
        }
    }
}
