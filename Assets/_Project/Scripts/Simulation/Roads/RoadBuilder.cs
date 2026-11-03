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
        public float Height;
        public RoadNode Node;
        public RoadSegment Segment;
        public float T;

        public static RoadAnchor Free(Vector2 p, float height = 0f) =>
            new RoadAnchor { Kind = AnchorKind.Free, Position = p, Height = height };
        public static RoadAnchor AtNode(RoadNode n) =>
            new RoadAnchor { Kind = AnchorKind.Node, Position = n.Position, Height = n.Height, Node = n };
        public static RoadAnchor OnSegment(RoadSegment s, float t, Vector2 p) =>
            new RoadAnchor { Kind = AnchorKind.Segment, Position = p, Height = s.HeightAt(t), Segment = s, T = t };
    }

    /// <summary>Stelle, an der die geplante Straße eine vorhandene ebenerdig kreuzt.</summary>
    public sealed class RoadCrossing
    {
        public RoadSegment Segment;
        /// <summary>Gesetzt, wenn die Kreuzung auf einem vorhandenen Knoten liegt.</summary>
        public RoadNode Node;
        public Vector2 Point;
        public float Height;
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
        TooSteep,
        InsufficientClearance,
        InvalidHeight,
        OverWater,
    }

    public sealed class RoadPlan
    {
        public RoadType Type;
        public RoadSegmentFlags Flags;
        public Vector2 Start, Control, End;
        public float StartHeight, EndHeight;
        public RoadAnchor StartAnchor, EndAnchor;
        public readonly List<RoadCrossing> Crossings = new List<RoadCrossing>();
        /// <summary>Anzahl Straßen, die kreuzungsfrei über-/unterquert werden.</summary>
        public int GradeSeparations;
        public RoadBuildError Error;
        public float Length;
        public float Cost;
        /// <summary>Höhenprofil, wenn die Straße dem Gelände folgt (sonst null = gerade Rampe).</summary>
        public float[] Profile;
        public bool IsValid => Error == RoadBuildError.None;
        public float HeightAt(float t) => Profile != null ? Profiles.Sample(Profile, t) : StartHeight + (EndHeight - StartHeight) * t;
    }

    public sealed class RoundaboutPlan
    {
        public Vector2 Center;
        public float Radius;
        public float Height;
        public bool FollowTerrain;
        public RoadType Type;
        public float StartAngle;
        public readonly List<RoadPlan> Arcs = new List<RoadPlan>();
        public RoadBuildError Error;
        public float Cost;
        public bool IsValid => Error == RoadBuildError.None;
    }

    /// <summary>Zwei Richtungsfahrbahnen (Autobahn im Doppelbau).</summary>
    public sealed class DualCarriagewayPlan
    {
        public RoadPlan Right;  // in Bau-/Zeichenrichtung
        public RoadPlan Left;   // Gegenrichtung
        public bool FollowTerrain;
        public RoadBuildError Error;
        public float Cost => (Right?.Cost ?? 0f) + (Left?.Cost ?? 0f);
        public bool IsValid => Error == RoadBuildError.None;
    }

    /// <summary>
    /// Plant und baut Straßen: Einrasten an Knoten/Straßen, Prüfregeln, automatische Kreuzungen
    /// (vorhandene Straßen werden zerschnitten), Brücken (Höhen), Kreisverkehre, Doppelbau und Umbau.
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
        /// <summary>Mindest-Höhenunterschied für eine kreuzungsfreie Über-/Unterführung.</summary>
        public const float Clearance = 4.5f;
        public const float MaxHeight = 40f;
        /// <summary>Tiefste Bauhöhe (Tunnel).</summary>
        public const float MinHeight = -30f;
        /// <summary>Ab dieser Tiefe ist eine Straße überdeckt (Tunnel), darüber offener Trog.</summary>
        public const float TunnelDepth = 7f;

        public const float MinRoundaboutRadius = 12f;
        public const float MaxRoundaboutRadius = 120f;

        // ------------------------------------------------------------------ Anker

        /// <summary>
        /// Wohin rastet ein Punkt ein? Knoten im Umkreis <paramref name="snap"/> → Knoten.
        /// Punkt auf (oder dicht an) einer Straße → Straße, bzw. deren Endknoten wenn nah dran. Sonst frei.
        /// Mit <paramref name="height"/> wird nur an Straßen auf gleicher Ebene eingerastet (Brücken!).
        /// </summary>
        public static RoadAnchor ResolveAnchor(RoadNetwork network, Vector2 point, float snap, float? height = null)
        {
            var node = network.FindNearestNode(point, snap, height);
            if (node != null) return RoadAnchor.AtNode(node);

            var segment = network.FindNearestSegment(point, snap * 0.3f, height);
            if (segment == null) return RoadAnchor.Free(point, height ?? 0f);

            float t = segment.ClosestT(point);
            var pos = segment.Evaluate(t);
            float nearEnd = MathF.Max(NodeMergeDistance, segment.Type.HalfWidth);
            if (Vector2.Distance(pos, segment.Start.Position) < nearEnd) return RoadAnchor.AtNode(segment.Start);
            if (Vector2.Distance(pos, segment.End.Position) < nearEnd) return RoadAnchor.AtNode(segment.End);
            return RoadAnchor.OnSegment(segment, t, pos);
        }

        // ------------------------------------------------------------------ Straße planen

        /// <summary>
        /// Plant eine Straße. Freie Enden bekommen die Höhen <paramref name="startHeight"/>/<paramref name="endHeight"/>,
        /// angeschlossene Enden die Höhe des Anschlusses. Eingerastet wird nur auf gleicher Ebene.
        /// </summary>
        /// <param name="followTerrain">
        /// Straße folgt dem Gelände (Höhenprofil, geglättet und auf die Maximalsteigung begrenzt); das Gelände
        /// wird danach unter ihr eingeebnet. Sonst gerade Rampe zwischen den Endhöhen (Brücke/Tunnel).
        /// </param>
        public static RoadPlan Plan(RoadNetwork network, Vector2 start, float startSnap, Vector2? control,
            Vector2 end, float endSnap, RoadType type, float startHeight = 0f, float endHeight = 0f,
            RoadSegmentFlags flags = RoadSegmentFlags.None, bool followTerrain = false)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            var plan = new RoadPlan
            {
                Type = type,
                Flags = flags,
                StartAnchor = ResolveAnchor(network, start, startSnap, startHeight),
                EndAnchor = ResolveAnchor(network, end, endSnap, endHeight),
            };
            plan.Start = plan.StartAnchor.Position;
            plan.End = plan.EndAnchor.Position;
            plan.StartHeight = plan.StartAnchor.Kind == AnchorKind.Free ? startHeight : plan.StartAnchor.Height;
            plan.EndHeight = plan.EndAnchor.Kind == AnchorKind.Free ? endHeight : plan.EndAnchor.Height;
            plan.Control = control ?? (plan.Start + plan.End) * 0.5f;
            plan.Length = Bezier.ApproximateLength(plan.Start, plan.Control, plan.End);
            if (followTerrain && plan.Length >= RoadNetwork.MinSegmentLength)
            {
                plan.Profile = Profiles.FollowTerrain(network.Ground, plan.Start, plan.Control, plan.End, plan.Length,
                    plan.StartHeight, plan.EndHeight, type.MaxSlope);
                if (plan.Profile == null)
                {
                    // Grund nennen: Wasser im Weg (Ufer zu niedrig für Brückenrampe) oder einfach zu steil
                    bool water = false;
                    for (int i = 0; i <= 16 && !water; i++) water = network.Ground.IsWater(Bezier.Evaluate(plan.Start, plan.Control, plan.End, i / 16f));
                    plan.Error = water ? RoadBuildError.OverWater : RoadBuildError.TooSteep;
                    return plan;
                }
            }
            ElevationRange(network.Ground, plan, out float minElev, out float maxElev);
            plan.Cost = plan.Length * type.CostPerMeter * CostFactor(minElev, maxElev);
            plan.Error = Validate(network, plan);
            return plan;
        }

        /// <summary>Kleinste/größte Höhe über Gelände entlang der geplanten Straße.</summary>
        public static void ElevationRange(IGround ground, RoadPlan plan, out float min, out float max)
        {
            min = float.MaxValue;
            max = float.MinValue;
            for (int i = 0; i <= 12; i++)
            {
                float t = i / 12f;
                float e = plan.HeightAt(t) - ground.HeightAt(Bezier.Evaluate(plan.Start, plan.Control, plan.End, t));
                min = MathF.Min(min, e);
                max = MathF.Max(max, e);
            }
        }

        /// <summary>Höhe über Gelände an einem Punkt (für Werkzeug und Prüfungen).</summary>
        public static float Elevation(RoadNetwork network, Vector2 p, float height) => height - network.Ground.HeightAt(p);

        static RoadBuildError Validate(RoadNetwork network, RoadPlan plan)
        {
            var type = plan.Type;
            if (plan.Length < RoadNetwork.MinSegmentLength) return RoadBuildError.TooShort;
            var ground = network.Ground;
            float e0 = plan.StartHeight - ground.HeightAt(plan.Start), e1 = plan.EndHeight - ground.HeightAt(plan.End);
            if (e0 < MinHeight - 0.01f || e1 < MinHeight - 0.01f || e0 > MaxHeight || e1 > MaxHeight) return RoadBuildError.InvalidHeight;
            float slope = plan.Profile != null ? Profiles.MaxSlope(plan.Profile, plan.Length) : MathF.Abs(plan.EndHeight - plan.StartHeight) / plan.Length;
            if (slope > type.MaxSlope + 1e-3f) return RoadBuildError.TooSteep;

            // Wasser nur auf einer Brücke queren
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 16f;
                var p = Bezier.Evaluate(plan.Start, plan.Control, plan.End, t);
                if (ground.IsWater(p) && plan.HeightAt(t) < ground.WaterLevel + 2.5f) return RoadBuildError.OverWater;
            }

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

            var error = FindCrossings(network, plan);
            if (error != RoadBuildError.None) return error;

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

        /// <summary>Brücken kosten mit der Höhe über Gelände mehr, Tunnel deutlich mehr (Ausbruch, Lüftung).</summary>
        public static float CostFactor(float minElevation, float maxElevation)
        {
            float high = maxElevation, low = minElevation;
            float f = 1f;
            if (high > 0f) f += high * 0.05f;
            if (low < 0f) f += MathF.Min(-low, TunnelDepth) * 0.1f + MathF.Max(0f, -low - TunnelDepth) * 0.05f + (low <= -TunnelDepth ? 1.5f : 0f);
            return f;
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

        /// <summary>
        /// Sucht alle Schnittpunkte mit vorhandenen Straßen. Gleiche Ebene → Kreuzung (wird zerteilt),
        /// genug Höhenunterschied → Brücke/Unterführung, dazwischen → Fehler.
        /// </summary>
        static RoadBuildError FindCrossings(RoadNetwork network, RoadPlan plan)
        {
            var result = plan.Crossings;
            result.Clear();
            plan.GradeSeparations = 0;
            var separated = new HashSet<int>();
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
                    if (!Bezier.LineSegmentsIntersect(newPts[i], newPts[i + 1], segPts[j], segPts[j + 1], out float u, out float v))
                        continue;

                    var p = Vector2.Lerp(newPts[i], newPts[i + 1], u);
                    // Anschlusspunkte selbst sind keine Kreuzungen
                    if (Vector2.Distance(p, plan.Start) < EndpointTolerance || Vector2.Distance(p, plan.End) < EndpointTolerance) continue;

                    float tNew = (i + u) / n;
                    float tSeg = (j + v) / m;
                    float dh = MathF.Abs(plan.HeightAt(tNew) - seg.HeightAt(tSeg));
                    if (dh > RoadNetwork.SameLevelTolerance)
                    {
                        if (dh < Clearance) return RoadBuildError.InsufficientClearance;
                        if (separated.Add(seg.Id)) plan.GradeSeparations++;
                        continue;
                    }

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
                        Height = atNode != null ? atNode.Height : seg.HeightAt(tSeg),
                        TNew = tNew,
                    });
                }
            }
            result.Sort((a, b) => a.TNew.CompareTo(b.TNew));
            return RoadBuildError.None;
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
            points.Add((0f, Realize(network, plan.StartAnchor.Kind, plan.Start, plan.StartHeight)));
            foreach (var c in plan.Crossings)
                points.Add((c.TNew, Realize(network, c.Node != null ? AnchorKind.Node : AnchorKind.Segment, c.Point, c.Height)));
            points.Add((1f, Realize(network, plan.EndAnchor.Kind, plan.End, plan.EndHeight)));

            for (int i = 0; i < points.Count - 1; i++)
            {
                var (ta, a) = points[i];
                var (tb, b) = points[i + 1];
                var control = Bezier.Blossom(plan.Start, plan.Control, plan.End, ta, tb);
                var profile = plan.Profile != null ? Profiles.Resample(plan.HeightAt, ta, tb, plan.Length) : null;
                var seg = network.AddSegment(a, b, control, plan.Type, plan.Flags, profile);
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
        static RoadNode Realize(RoadNetwork network, AnchorKind kind, Vector2 point, float height)
        {
            var node = network.FindNearestNode(point, kind == AnchorKind.Free ? 0.01f : 0.75f, height);
            if (node != null) return node;
            if (kind == AnchorKind.Free) return network.AddNode(point, height);

            RoadSegment best = null;
            float bestDist = 1f, bestT = 0f;
            foreach (var s in network.Segments)
            {
                float t = s.ClosestT(point);
                if (MathF.Abs(s.HeightAt(t) - height) > RoadNetwork.SameLevelTolerance) continue;
                float d = Vector2.Distance(s.Evaluate(t), point);
                if (d < bestDist) { bestDist = d; best = s; bestT = t; }
            }
            if (best == null) return network.AddNode(point, height);

            var p = best.Evaluate(bestT);
            // Keine Mini-Segmente erzeugen
            if (Vector2.Distance(p, best.Start.Position) < RoadNetwork.MinSegmentLength) return best.Start;
            if (Vector2.Distance(p, best.End.Position) < RoadNetwork.MinSegmentLength) return best.End;
            return network.SplitSegment(best, bestT);
        }

        // ------------------------------------------------------------------ Umbau

        /// <summary>Darf das Segment auf einen anderen Typ umgebaut werden?</summary>
        public static RoadBuildError CheckReplace(RoadNetwork network, RoadSegment segment, RoadType type)
        {
            if (type == null || segment == null) return RoadBuildError.InvalidType;
            if (segment.Type == type) return RoadBuildError.Duplicate;
            if (segment.MaxSlope > type.MaxSlope + 1e-3f) return RoadBuildError.TooSteep;
            if (segment.IsRoundabout && (!type.IsOneWay || type.Category != RoadCategory.City)) return RoadBuildError.InvalidType;
            foreach (var node in new[] { segment.Start, segment.End })
            foreach (int id in node.SegmentIds)
            {
                if (id == segment.Id) continue;
                if (!type.CanConnectTo(network.GetSegment(id).Type)) return RoadBuildError.IncompatibleConnection;
            }
            return RoadBuildError.None;
        }

        public static RoadSegment Replace(RoadNetwork network, RoadSegment segment, RoadType type) =>
            CheckReplace(network, segment, type) == RoadBuildError.None ? network.ReplaceType(segment, type) : null;

        /// <summary>Kosten eines Umbaus (halber Neubaupreis).</summary>
        public static float ReplaceCost(RoadSegment segment, RoadType type) => segment.Length * type.CostPerMeter * 0.5f;

        // ------------------------------------------------------------------ Doppelbau (Richtungsfahrbahnen)

        /// <summary>
        /// Plant zwei parallele Einbahn-Fahrbahnen: rechts in Zeichenrichtung, links entgegen (Rechtsverkehr).
        /// <paramref name="median"/> = lichter Abstand zwischen den Fahrbahnen.
        /// </summary>
        public static DualCarriagewayPlan PlanDualCarriageway(RoadNetwork network, Vector2 start, Vector2? control, Vector2 end,
            RoadType type, float snap, float startHeight = 0f, float endHeight = 0f, float median = 3f, bool followTerrain = false)
        {
            var plan = new DualCarriagewayPlan();
            if (type == null || !type.IsOneWay) { plan.Error = RoadBuildError.InvalidType; return plan; }
            if (Vector2.Distance(start, end) < RoadNetwork.MinSegmentLength) { plan.Error = RoadBuildError.TooShort; return plan; }

            var c = control ?? (start + end) * 0.5f;
            float offset = type.HalfWidth + median * 0.5f;
            OffsetCurve(start, c, end, offset, out var r0, out var rc, out var r2);
            OffsetCurve(start, c, end, -offset, out var l0, out var lc, out var l2);

            // Geländefolgend: jede Fahrbahn nimmt die Geländehöhe an ihren eigenen Enden
            var g = network.Ground;
            float rs = followTerrain ? g.HeightAt(r0) : startHeight, re = followTerrain ? g.HeightAt(r2) : endHeight;
            float ls = followTerrain ? g.HeightAt(l2) : endHeight, le = followTerrain ? g.HeightAt(l0) : startHeight;
            plan.Right = Plan(network, r0, snap, rc, r2, snap, type, rs, re, followTerrain: followTerrain);
            plan.Left = Plan(network, l2, snap, lc, l0, snap, type, ls, le, followTerrain: followTerrain);
            plan.FollowTerrain = followTerrain;
            plan.Error = plan.Right.IsValid ? plan.Left.Error : plan.Right.Error;
            return plan;
        }

        public static List<RoadSegment> BuildDualCarriageway(RoadNetwork network, DualCarriagewayPlan plan, float snap)
        {
            var built = new List<RoadSegment>();
            if (plan == null || !plan.IsValid) return built;
            built.AddRange(Build(network, plan.Right));
            // Neu planen, falls die erste Fahrbahn das Netz verändert hat
            var left = Plan(network, plan.Left.Start, snap, plan.Left.Control, plan.Left.End, snap, plan.Left.Type,
                plan.Left.StartHeight, plan.Left.EndHeight, followTerrain: plan.FollowTerrain);
            built.AddRange(Build(network, left));
            return built;
        }

        /// <summary>Parallelkurve im Abstand d (positiv = rechts). Kontrollpunkt = Schnitt der verschobenen Tangenten.</summary>
        public static void OffsetCurve(Vector2 p0, Vector2 c, Vector2 p2, float d, out Vector2 o0, out Vector2 oc, out Vector2 o2)
        {
            var t0 = SafeDirection(c - p0, p2 - p0);
            var t2 = SafeDirection(p2 - c, p2 - p0);
            o0 = p0 + new Vector2(t0.Y, -t0.X) * d;
            o2 = p2 + new Vector2(t2.Y, -t2.X) * d;
            oc = RayIntersection(o0, t0, o2, -t2) ?? (o0 + o2) * 0.5f;
        }

        /// <summary>Schnittpunkt zweier Strahlen (nur wenn vor beiden Startpunkten).</summary>
        public static Vector2? RayIntersection(Vector2 a, Vector2 da, Vector2 b, Vector2 db)
        {
            float denom = da.X * db.Y - da.Y * db.X;
            if (MathF.Abs(denom) < 1e-5f) return null;
            var ab = b - a;
            float t = (ab.X * db.Y - ab.Y * db.X) / denom;
            float u = (ab.X * da.Y - ab.Y * da.X) / denom;
            if (t < 0f || u < 0f) return null;
            return a + da * t;
        }

        // ------------------------------------------------------------------ Kreisverkehr

        /// <summary>
        /// Plant einen Kreisverkehr aus Einbahn-Bögen gegen den Uhrzeigersinn (Rechtsverkehr).
        /// Vorhandene Straßen, die den Ring kreuzen, werden angeschlossen; was innen liegt, wird entfernt.
        /// </summary>
        public static RoundaboutPlan PlanRoundabout(RoadNetwork network, Vector2 center, float radius, RoadType type, float height = 0f,
            bool followTerrain = false)
        {
            var plan = new RoundaboutPlan { Center = center, Radius = radius, Type = type, Height = height, FollowTerrain = followTerrain };
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
                    var arc = PlanArc(network, plan, p0, c, p2, 0f);
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

        static RoadPlan PlanArc(RoadNetwork network, RoundaboutPlan plan, Vector2 p0, Vector2 c, Vector2 p2, float snap)
        {
            float h0 = plan.FollowTerrain ? network.Ground.HeightAt(p0) : plan.Height;
            float h2 = plan.FollowTerrain ? network.Ground.HeightAt(p2) : plan.Height;
            return Plan(network, p0, snap, c, p2, snap, plan.Type, h0, h2, RoadSegmentFlags.Roundabout, plan.FollowTerrain);
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
                var arc = PlanArc(network, plan, p0, c, p2, 0.5f);
                built.AddRange(Build(network, arc));
            }

            // Alles, was jetzt innerhalb des Rings auf gleicher Ebene liegt, entfernen (z. B. die alte Kreuzung).
            var inside = new List<int>();
            foreach (var s in network.Segments)
            {
                if (s.IsRoundabout) continue;
                float level = plan.FollowTerrain ? network.Ground.HeightAt(s.Evaluate(0.5f)) : plan.Height;
                if (MathF.Abs(s.HeightAt(0.5f) - level) > RoadNetwork.SameLevelTolerance * 2f) continue;
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
