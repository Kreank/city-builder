using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>Ein Knoten im Straßennetz: Straßenende oder Kreuzung.</summary>
    public sealed class RoadNode
    {
        internal readonly List<int> segmentIds = new List<int>();

        public int Id { get; }
        public Vector2 Position { get; }
        public IReadOnlyList<int> SegmentIds => segmentIds;

        internal RoadNode(int id, Vector2 position)
        {
            Id = id;
            Position = position;
        }
    }
}
