using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>Ein Knoten im Straßennetz: Straßenende, Kreuzung oder Stützpunkt einer Brücke.</summary>
    public sealed class RoadNode
    {
        internal readonly List<int> segmentIds = new List<int>();

        public int Id { get; }
        public Vector2 Position { get; }
        /// <summary>Höhe über dem Boden in Metern (Brücken &gt; 0).</summary>
        public float Height { get; }
        public IReadOnlyList<int> SegmentIds => segmentIds;

        internal RoadNode(int id, Vector2 position, float height)
        {
            Id = id;
            Position = position;
            Height = height;
        }

        public override string ToString() => $"Node {Id} ({Position.X:0},{Position.Y:0} h{Height:0.#})";
    }
}
