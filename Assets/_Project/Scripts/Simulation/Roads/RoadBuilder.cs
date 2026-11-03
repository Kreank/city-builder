using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    public enum AnchorKind { Free, Node, Segment }

    /// <summary>Woran ein Straßenende hängt: frei, an einem Knoten oder mitten auf einer Straße.</summary>
    public struct RoadAnchor
    {
        public AnchorKind Kind;
        public Vector2 Position;
        public RoadNode Node;
        public RoadSegment Segment;
        public float T;

        public static RoadAnchor Free(Vector2 p) => new RoadAnchor { Kind = AnchorKind.Free, Position = p };
        public static RoadAnchor AtNode(RoadNode n) => new RoadAnchor { Kind = AnchorKind.Node, Position = n.Position, Node = n };
        public static RoadAnchor OnSegment(RoadSegment s, float t, Vector2 p) =>
            new RoadAnchor { Kind = AnchorKind.Segment, Position = p, Segment = s, T = t };
    }

    /// <summary>Stelle, an der die geplante Straße eine vorhandene kreuzt.</summary>
    public sealed class RoadCrossing
    {
        public RoadSegment Segment;
        /// <summary>Gesetzt, wenn die Kreuzung auf einem vorhandenen Knoten liegt.</summary>
        public RoadNode Node;
        public Vector2 Point;
        /// <summary>Parameter auf der neuen Straße.</summary>
        public float TNew;
    }

    public enum RoadBuildError
    {
        None,
        TooShort,
        SameNode,
        Duplicate,
        AngleTooSharp,
        HighwayCrossing,
        IncompatibleConnection,
        TooCloseToCrossing,
        InvalidRadius,
        InvalidType,
    }

    public sealed class RoadPlan
    {
        public RoadType Type;
        public Vector2 Start, Control, End;
        public RoadAnchor StartAnchor, EndAnchor;
        public readonly List<RoadCrossing> Crossings = new List<RoadCrossing>();
        public RoadBuildError Error;
        public float Length;
        public float Cost;
        public bool IsValid => Error == RoadBuildError.None;
    }

    public sealed class RoundaboutPlan
    {
        public Vector2 Center;
        public float Radius;
        public RoadType Type;
        public float StartAngle;
        public readonly List<RoadPlan> Arcs = new List<RoadPlan>();
        public RoadBuildError Error;
        public float Cost;
        public bool IsValid => Error == RoadBuildError.None;
    }

    /// <summary>
    /// Plant und baut Straßen: Einrasten an Knoten/Straßen, Prüfregeln, automatische Kreuzungen
    /// (vorhandene Straßen werden zerschnitten) und Kreisverkehre.
    /// Ablauf immer: Plan…() → Vorschau anzeigen → Build…(plan).
    /// </summary>
    public static class RoadBuilder
    {
        /// <summary>Kleinster erlaubter Winkel zwischen zwei Straßen an einem Knoten.</summary>
        public const float MinJunctionAngle = 10f;
        /// <summary>Innerhalb dieses Abstands zu den Enden zählt ein Schnittpunkt als Anschluss, nicht als Kreuzung.</summary>
        public const float EndpointTolerance = 1.5f;
        /// <summary>Liegt ein Anschluss/Schnitt so nah an einem Knoten, wird der Knoten benutzt.</summary>
        public const float NodeMergeDistance = 8f;

        public const float MinRoundaboutRadius = 12f;
        public const float MaxRoundaboutRadius = 120f;

        // ------------------------------------------------------------------ Anker

        /// <summary>
        /// Wohin rastet ein Punkt ein? Knoten im Umkreis <paramref name="snap"/> → Knoten.
        /// Punkt auf (oder dicht an) einer Straße → Straße, bzw. deren Endknoten wenn nah dran. Sonst frei.
        /// </summary>
        public static RoadAnchor ResolveAnchor(RoadNetwork network, Vector2 point, float snap)
        {
            var node = network.FindNearestNode(point, snap);
            if (node != null) return RoadAnchor.AtNode(node);

            var segment = network.FindNearestSegment(point, snap * 0.3f);
            if (segment == null) return RoadAnchor.Free(point);

            float t = segment.ClosestT(point);
            var pos = segment.Evaluate(t);
            float nearEnd = MathF.Max(NodeMergeDistance, segment.Type.HalfWidth);
            if (Vector2.Distance(pos, segment.Start.Position) < nearEnd) return RoadAnchor.AtNode(segment.Start);
            if (Vector2.Distance(pos, segment.End.Position) < nearEnd) return RoadAnchor.AtNode(segment.End);
            return RoadAnchor.OnSegment(segment, t, pos);
        }

        // ------------------------------------------------------------------ Straße planen

        public static RoadPlan Plan(RoadNetwork network, Vector2 start, float startSnap, Vector2? control,
            Vector2 end, float endSnap, RoadType type)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            var plan = new RoadPlan
            {
                Type = type,
                StartAnchor = ResolveAnchor(network, start, startSnap),
                EndAnchor = ResolveAnchor(network, end, endSnap),
            };
            plan.Start = plan.StartAnchor.Position;
            plan.End = plan.EndAnchor.Position;
            plan.Control = control ?? (plan.Start + plan.End) * 0.5f;
            plan.Length = Bezier.ApproximateLength(plan.Start, plan.Control, plan.End);
            plan.Cost = plan.Length * type.CostPerMeter;
            plan.Error = Validate(network, plan);
            return plan;
        }

        static RoadBuildError Validate(RoadNetwork network, RoadPlan plan)
        {
            var type = plan.Type;
            if (plan.Length < RoadNetwork.MinSegmentLength) return RoadBuildError.TooShort;

            var sa = plan.StartAnchor;
            var ea = plan.EndAnchor;
            if (sa.Kind == AnchorKind.Node && ea.Kind == AnchorKind.Node)
            {
                if (sa.Node == ea.Node) return RoadBuildError.SameNode;
                if (network.HasMatchingSegment(sa.Node, ea.Node, plan.Control)) return RoadBuildError.Duplicate;
            }

            if (!AnchorCompatible(network, sa, type) || !AnchorCompatible(network, ea, type))
                return RoadBuildError.IncompatibleConnection;

            var startDir = SafeDirection(plan.Control - plan.Start, plan.End - plan.Start);
            var endDir = SafeDirection(plan.Control - plan.End, plan.Start - plan.End);
            if (!AnchorAngleOk(network, sa, startDir) || !AnchorAngleOk(network, ea, endDir))
                return RoadBuildError.AngleTooSharp;

            FindCrossings(network, plan, plan.Crossings);

            float prevT = 0f;
            foreach (var c in plan.Crossings)
            {
                if (!type.CanCrossAtGrade(c.Segment.Type)) return RoadBuildError.HighwayCrossing;

                var newDir = SafeDirection(Bezier.Derivative(plan.Start, plan.Control, plan.End, c.TNew), startDir);
                var existing = c.Segment.Tangent(c.Segment.ClosestT(c.Point));
                float a = RoadGeometry.Angle(newDir, existing);
                if (a < MinJunctionAngle || a > 180f - MinJunctionAngle) return RoadBuildError.AngleTooSharp;

                if ((c.TNew - prevT) * plan.Length < RoadNetwork.MinSegmentLength) return RoadBuildError.TooCloseToCrossing;
                prevT = c.TNew;
            }
            if (plan.Crossings.Count > 0 && (1f - prevT) * plan.Length < RoadNetwork.MinSegmentLength)
                return RoadBuildError.TooCloseToCrossing;

            return RoadBuildError.None;
        }

        static bool AnchorCompatible(RoadNetwork network, RoadAnchor anchor, RoadType type)
        {
            switch (anchor.Kind)
            {
                case AnchorKind.Node:
                    foreach (int id in anchor.Node.SegmentIds)
                        if (!type.CanConnectTo(network.GetSegment(id).Type)) return false;
                    return true;
                case AnchorKind.Segment:
                    return type.CanConnectTo(anchor.Segment.Type);
                default:
                    return true;
            }
        }

        static bool AnchorAngleOk(RoadNetwork network, RoadAnchor anchor, Vector2 direction)
        {
            switch (anchor.Kind)
            {
                case AnchorKind.Node:
                    foreach (int id in anchor.Node.SegmentIds)
                    {
                        var dir = RoadGeometry.OutgoingDirection(network.GetSegment(id), anchor.Node);
                        if (RoadGeometry.Angle(dir, direction) < MinJunctionAngle) return false;
                    }
                    return true;
                case AnchorKind.Segment:
                    var tangent = anchor.Segment.Tangent(anchor.T);
                    float a = RoadGeometry.Angle(tangent, direction);
                    return a >= MinJunctionAngle && a <= 180f - MinJunctionAngle;
                default:
                    return true;
            }
        }

        /// <summary>Alle Schnittpunkte der geplanten Kurve mit vorhandenen Straßen, sortiert entlang der neuen Straße.</summary>
        static void FindCrossings(RoadNetwork network, RoadPlan plan, List<RoadCrossing> result)
        {
            result.Clear();
            int n = Math.Clamp((int)MathF.Ceiling(plan.Length / 3f), 8, 256);
            var newPts = Sample(plan.Start, plan.Control, plan.End, n);
            Bezier.Bounds(plan.Start, plan.Control, plan.End, out var newMin, out var newMax);

            foreach (var seg in network.Segments)
            {
                Bezier.Bounds(seg.Start.Position, seg.Control, seg.End.Position, out var min, out var max);
                if (max.X < newMin.X - 1f || max.Y < newMin.Y - 1f || min.X > newMax.X + 1f || min.Y > newMax.Y + 1f) continue;

                int m = Math.Clamp((int)MathF.Ceiling(seg.Length / 3f), 8, 256);
                var segPts = Sample(seg.Start.Position, seg.Control, seg.End.Position, m);

                for (int i = 0; i < n; i++)
                for (int j = 0; j < m; j++)
                {
                    if (!Bezier.LineSegmentsIntersect(newPts[i], newPts[i + 1], segPts[j], segPts[j + 1], out float u, out _))
                        continue;

                    var p = Vector2.Lerp(newPts[i], newPts[i + 1], u);
                    // Anschlusspunkte selbst sind keine Kreuzungen
                    if (Vector2.Distance(p, plan.Start) < EndpointTolerance || Vector2.Distance(p, plan.End) < EndpointTolerance) continue;

                    RoadNode atNode = null;
                    if (Vector2.Distance(p, seg.Start.Position) < NodeMergeDistance) atNode = seg.Start;
                    else if (Vector2.Distance(p, seg.End.Position) < NodeMergeDistance) atNode = seg.End;
                    if (atNode != null && (atNode == plan.StartAnchor.Node || atNode == plan.EndAnchor.Node)) continue;

                    if (IsDuplicate(result, seg, atNode, p)) continue;
                    result.Add(new RoadCrossing
                    {
                        Segment = seg,
                        Node = atNode,
                        Point = atNode != null ? atNode.Position : p,
                        TNew = (i + u) / n,
                    });
                }
            }
            result.Sort((a, b) => a.TNew.CompareTo(b.TNew));
        }

        static bool IsDuplicate(List<RoadCrossing> list, RoadSegment seg, RoadNode node, Vector2 p)
        {
            foreach (var c in list)
            {
                if (node != null && c.Node == node) return true;
                if (c.Segment == seg && Vector2.Distance(c.Point, p) < 1f) return true;
            }
            return false;
        }

        static Vector2[] Sample(Vector2 p0, Vector2 p1, Vector2 p2, int pieces)
        {
            var pts = new Vector2[pieces + 1];
            for (int i = 0; i <= pieces; i++) pts[i] = Bezier.Evaluate(p0, p1, p2, i / (float)pieces);
            return pts;
        }

        static Vector2 SafeDirection(Vector2 v, Vector2 fallback)
        {
            if (v.LengthSquared() > 1e-6f) return Vector2.Normalize(v);
            return fallback.LengthSquared() > 1e-6f ? Vector2.Normalize(fallback) : Vector2.UnitY;
        }

        // ------------------------------------------------------------------ Straße bauen

        /// <summary>Führt einen gültigen Plan aus. Liefert die neu gebauten Segmente in Baurichtung.</summary>
        public static List<RoadSegment> Build(RoadNetwork network, RoadPlan plan)
        {
            var built = new List<RoadSegment>();
            if (plan == null || !plan.IsValid) return built;

            var points = new List<(float t, RoadNode node)>();
            points.Add((0f, Realize(network, plan.StartAnchor.Kind, plan.Start)));
            foreach (var c in plan.Crossings)
                points.Add((c.TNew, Realize(network, c.Node != null ? AnchorKind.Node : AnchorKind.Segment, c.Point)));
            points.Add((1f, Realize(network, plan.EndAnchor.Kind, plan.End)));

            for (int i = 0; i < points.Count - 1; i++)
            {
                var (ta, a) = points[i];
                var (tb, b) = points[i + 1];
                var control = Bezier.Blossom(plan.Start, plan.Control, plan.End, ta, tb);
                var seg = network.AddSegment(a, b, control, plan.Type);
                if (seg != null) built.Add(seg);
            }

            // Falls etwas nicht gebaut werden konnte: keine verwaisten Knoten zurücklassen.
            foreach (var (_, node) in points) network.RemoveNodeIfOrphan(node);
            return built;
        }

        /// <summary>
        /// Macht aus einem Planpunkt einen echten Knoten. Arbeitet über die Position (nicht über
        /// gespeicherte Segmente), weil vorherige Schritte Segmente schon zerteilt haben können.
        /// </summary>
        static RoadNode Realize(RoadNetwork network, AnchorKind kind, Vector2 point)
        {
            var node = network.FindNearestNode(point, kind == AnchorKind.Free ? 0.01f : 0.75f);
            if (node != null) return node;
            if (kind == AnchorKind.Free) return network.AddNode(point);

            RoadSegment best = null;
            float bestDist = 1f;
            foreach (var s in network.Segments)
            {
                float d = s.DistanceTo(point);
                if (d < bestDist) { bestDist = d; best = s; }
            }
            if (best == null) return network.AddNode(point);

            float t = best.ClosestT(point);
            var p = best.Evaluate(t);
            // Keine Mini-Segmente erzeugen
            if (Vector2.Distance(p, best.Start.Position) < RoadNetwork.MinSegmentLength) return best.Start;
            if (Vector2.Distance(p, best.End.Position) < RoadNetwork.MinSegmentLength) return best.End;
            return network.SplitSegment(best, t);
        }

        // ------------------------------------------------------------------ Kreisverkehr

        /// <summary>
        /// Plant einen Kreisverkehr aus Einbahn-Bögen gegen den Uhrzeigersinn (Rechtsverkehr).
        /// Vorhandene Straßen, die den Ring kreuzen, werden angeschlossen; was innen liegt, wird entfernt.
        /// </summary>
        public static RoundaboutPlan PlanRoundabout(RoadNetwork network, Vector2 center, float radius, RoadType type)
        {
            var plan = new RoundaboutPlan { Center = center, Radius = radius, Type = type };
            if (radius < MinRoundaboutRadius || radius > MaxRoundaboutRadius) { plan.Error = RoadBuildError.InvalidRadius; return plan; }
            if (type == null || !type.IsOneWay || type.Category != RoadCategory.City) { plan.Error = RoadBuildError.InvalidType; return plan; }

            int arcs = RoundaboutArcCount(radius);
            float step = MathF.PI * 2f / arcs;

            // Ringknoten etwas drehen, falls eine vorhandene Straße zu dicht an einem Ringknoten kreuzt.
            RoadBuildError firstError = RoadBuildError.None;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                float offset = step * attempt / 8f;
                plan.Arcs.Clear();
                plan.Cost = 0f;
                RoadBuildError error = RoadBuildError.None;
                for (int i = 0; i < arcs && error == RoadBuildError.None; i++)
                {
                    GetArc(center, radius, offset + step * i, step, out var p0, out var c, out var p2);
                    var arc = Plan(network, p0, 0f, c, p2, 0f, type);
                    // Die Ringbögen kennen sich im Plan noch nicht – nur Fehler gegenüber dem Bestand zählen.
                    if (!arc.IsValid && arc.Error != RoadBuildError.Duplicate) error = arc.Error;
                    plan.Arcs.Add(arc);
                    plan.Cost += arc.Cost;
                }
                if (error == RoadBuildError.None)
                {
                    plan.StartAngle = offset;
                    plan.Error = RoadBuildError.None;
                    return plan;
                }
                if (firstError == RoadBuildError.None) firstError = error;
            }
            plan.Error = firstError;
            return plan;
        }

        public static int RoundaboutArcCount(float radius) =>
            Math.Clamp((int)MathF.Round(MathF.PI * 2f * radius / 25f), 4, 16);

        /// <summary>Bézier-Näherung eines Kreisbogens von <paramref name="angle"/> über <paramref name="sweep"/> (Bogenmaß).</summary>
        public static void GetArc(Vector2 center, float radius, float angle, float sweep, out Vector2 p0, out Vector2 control, out Vector2 p2)
        {
            p0 = center + radius * new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            p2 = center + radius * new Vector2(MathF.Cos(angle + sweep), MathF.Sin(angle + sweep));
            float mid = angle + sweep * 0.5f;
            control = center + radius / MathF.Cos(sweep * 0.5f) * new Vector2(MathF.Cos(mid), MathF.Sin(mid));
        }

        public static List<RoadSegment> BuildRoundabout(RoadNetwork network, RoundaboutPlan plan)
        {
            var built = new List<RoadSegment>();
            if (plan == null || !plan.IsValid) return built;

            int arcs = plan.Arcs.Count;
            float step = MathF.PI * 2f / arcs;
            for (int i = 0; i < arcs; i++)
            {
                GetArc(plan.Center, plan.Radius, plan.StartAngle + step * i, step, out var p0, out var c, out var p2);
                // Neu planen, damit der Bogen an den gerade gebauten Ringknoten anschließt.
                var arc = Plan(network, p0, 0.5f, c, p2, 0.5f, plan.Type);
                built.AddRange(Build(network, arc));
            }

            // Alles, was jetzt innerhalb des Rings liegt, entfernen (z. B. die alte Kreuzung in der Mitte).
            var inside = new List<int>();
            foreach (var s in network.Segments)
            {
                if (Vector2.Distance(s.Evaluate(0.5f), plan.Center) < plan.Radius - 1f &&
                    Vector2.Distance(s.Start.Position, plan.Center) < plan.Radius + 0.5f &&
                    Vector2.Distance(s.End.Position, plan.Center) < plan.Radius + 0.5f)
                    inside.Add(s.Id);
            }
            foreach (int id in inside) network.RemoveSegment(id);

            return built;
        }
    }
}
