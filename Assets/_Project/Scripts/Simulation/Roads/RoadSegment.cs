using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>Ein (gerades oder gekrümmtes) Straßenstück zwischen zwei Knoten.</summary>
    public sealed class RoadSegment
    {
        public int Id { get; }
        public RoadNode Start { get; }
        public RoadNode End { get; }
        /// <summary>Bézier-Kontrollpunkt. Bei geraden Straßen die Mitte zwischen Start und Ende.</summary>
        public Vector2 Control { get; }
        public RoadType Type { get; }
        public float Length { get; }

        internal RoadSegment(int id, RoadNode start, RoadNode end, Vector2 control, RoadType type)
        {
            Id = id;
            Start = start;
            End = end;
            Control = control;
            Type = type;
            Length = Bezier.ApproximateLength(start.Position, control, end.Position);
        }

        public Vector2 Evaluate(float t) => Bezier.Evaluate(Start.Position, Control, End.Position, t);

        public Vector2 Tangent(float t)
        {
            Vector2 d = Bezier.Derivative(Start.Position, Control, End.Position, t);
            float len = d.Length();
            return len > 1e-6f ? d / len : Vector2.Normalize(End.Position - Start.Position);
        }

        public float DistanceTo(Vector2 point) =>
            Bezier.DistanceToPoint(Start.Position, Control, End.Position, point);

        public RoadNode Other(RoadNode node) => node == Start ? End : Start;
    }
}
