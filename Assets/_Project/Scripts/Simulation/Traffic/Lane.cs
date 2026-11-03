using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Traffic
{
    /// <summary>Ein Segment in einer Fahrtrichtung – die Kante für die Wegfindung.</summary>
    public readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        public readonly int SegmentId;
        /// <summary>true = in Segmentrichtung (Start → Ende).</summary>
        public readonly bool Along;

        public EdgeKey(int segmentId, bool along)
        {
            SegmentId = segmentId;
            Along = along;
        }

        public bool Equals(EdgeKey other) => SegmentId == other.SegmentId && Along == other.Along;
        public override bool Equals(object obj) => obj is EdgeKey o && Equals(o);
        public override int GetHashCode() => SegmentId * 2 + (Along ? 1 : 0);
        public override string ToString() => $"{SegmentId}{(Along ? "+" : "-")}";
    }

    public enum TurnType { Straight, Left, Right, UTurn }

    /// <summary>Eine befahrbare Spur eines Segments mit den darauf fahrenden Fahrzeugen.</summary>
    public sealed class Lane
    {
        public RoadSegment Segment { get; }
        public int Index { get; }
        public LaneDefinition Definition { get; }
        /// <summary>Fährt in Segmentrichtung?</summary>
        public bool Along { get; }
        public VehicleClass Allowed { get; }
        public EdgeKey Edge => new EdgeKey(Segment.Id, Along);
        public RoadNode From => Along ? Segment.Start : Segment.End;
        public RoadNode To => Along ? Segment.End : Segment.Start;
        public float Length => Segment.Length;
        public float Offset => Definition.Offset;

        /// <summary>Position quer zur Fahrtrichtung aus Fahrersicht (0 = ganz links).</summary>
        public int Rank { get; internal set; }
        public Lane LeftNeighbor { get; internal set; }
        public Lane RightNeighbor { get; internal set; }

        public readonly List<LaneConnection> Outgoing = new List<LaneConnection>();
        /// <summary>Fahrzeuge, vorderstes zuerst (absteigend nach S).</summary>
        public readonly List<Vehicle> Vehicles = new List<Vehicle>();
        /// <summary>Gleitender Mittelwert Ist-/Sollgeschwindigkeit (1 = frei, 0 = Stau).</summary>
        public float SpeedRatio = 1f;

        internal Lane(RoadSegment segment, int index, LaneDefinition definition)
        {
            Segment = segment;
            Index = index;
            Definition = definition;
            Along = definition.Direction != LaneDirection.Backward;
            Allowed = VehicleClasses.AllowedOn(definition.Kind);
        }

        /// <summary>Segment-Parameter t für eine gefahrene Strecke s (von From aus).</summary>
        public float TAt(float s)
        {
            float f = Length > 0f ? Math.Clamp(s / Length, 0f, 1f) : 0f;
            return Along ? f : 1f - f;
        }

        /// <summary>Weltposition (x, y), Höhe und Fahrtrichtung bei Strecke s, optional seitlich verschoben.</summary>
        public void Sample(float s, float lateral, out Vector2 position, out float height, out Vector2 direction)
        {
            float t = TAt(s);
            var tangent = Segment.Tangent(t);
            var right = new Vector2(tangent.Y, -tangent.X);
            position = Segment.Evaluate(t) + right * (Offset + lateral);
            height = Segment.HeightAt(t);
            direction = Along ? tangent : -tangent;
        }

        /// <summary>Hinterstes Fahrzeug (zuletzt eingefahren).</summary>
        public Vehicle Last => Vehicles.Count > 0 ? Vehicles[Vehicles.Count - 1] : null;

        /// <summary>Freier Platz am Spuranfang bis zum Heck des letzten Fahrzeugs.</summary>
        public float FreeSpaceAtStart => Last == null ? Length : Last.S - Last.Length;

        internal void Insert(Vehicle v)
        {
            int i = 0;
            while (i < Vehicles.Count && Vehicles[i].S >= v.S) i++;
            Vehicles.Insert(i, v);
        }

        public override string ToString() => $"Lane {Segment.Id}/{Index}{(Along ? "+" : "-")} {Definition.Kind}";
    }

    /// <summary>Verbindung zweier Spuren über einen Knoten (Abbiegebeziehung) inkl. Fahrkurve.</summary>
    public sealed class LaneConnection
    {
        public Lane From { get; }
        public Lane To { get; }
        public RoadNode Node { get; }
        public TurnType Turn { get; }
        public Vector2 P0 { get; }
        public Vector2 Control { get; }
        public Vector2 P2 { get; }
        public float Length { get; }

        /// <summary>Fahrzeuge auf der Kreuzungsfläche, vorderstes zuerst.</summary>
        public readonly List<Vehicle> Vehicles = new List<Vehicle>();

        internal LaneConnection(Lane from, Lane to, RoadNode node, TurnType turn)
        {
            From = from;
            To = to;
            Node = node;
            Turn = turn;
            from.Sample(from.Length, 0f, out var p0, out _, out var d0);
            to.Sample(0f, 0f, out var p2, out _, out var d2);
            P0 = p0;
            P2 = p2;
            Control = RoadBuilder.RayIntersection(p0, d0, p2, -d2) ?? (p0 + p2) * 0.5f;
            if (turn == TurnType.UTurn) Control = node.Position + (d0 * 6f);
            Length = MathF.Max(0.5f, Bezier.ApproximateLength(P0, Control, P2, 12));
        }

        public void Sample(float s, out Vector2 position, out float height, out Vector2 direction)
        {
            float t = Math.Clamp(s / Length, 0f, 1f);
            position = Bezier.Evaluate(P0, Control, P2, t);
            var d = Bezier.Derivative(P0, Control, P2, t);
            direction = d.LengthSquared() > 1e-6f ? Vector2.Normalize(d) : Vector2.Normalize(P2 - P0 + new Vector2(1e-4f, 0f));
            height = Node.Height;
        }

        public Vehicle Last => Vehicles.Count > 0 ? Vehicles[Vehicles.Count - 1] : null;

        internal void Insert(Vehicle v)
        {
            int i = 0;
            while (i < Vehicles.Count && Vehicles[i].S >= v.S) i++;
            Vehicles.Insert(i, v);
        }

        public override string ToString() => $"{From} → {To} ({Turn})";
    }
}
