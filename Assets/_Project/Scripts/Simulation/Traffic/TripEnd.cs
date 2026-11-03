using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Traffic
{
    /// <summary>
    /// Start oder Ziel einer Fahrt: entweder eine Stelle auf einem Segment (Gebäudezufahrt, Segment + t)
    /// oder ein Knoten (Außenanschluss am Kartenrand).
    /// </summary>
    public readonly struct TripEnd
    {
        public readonly int SegmentId;
        public readonly float T;
        public readonly RoadNode Node;

        TripEnd(int segmentId, float t, RoadNode node)
        {
            SegmentId = segmentId;
            T = t;
            Node = node;
        }

        public static TripEnd AtSegment(int segmentId, float t) => new TripEnd(segmentId, t, null);
        public static TripEnd AtNode(RoadNode node) => new TripEnd(-1, 0f, node);

        public bool IsNode => Node != null;

        public override string ToString() => IsNode ? $"Knoten {Node.Id}" : $"Segment {SegmentId} @ {T:0.00}";
    }
}
