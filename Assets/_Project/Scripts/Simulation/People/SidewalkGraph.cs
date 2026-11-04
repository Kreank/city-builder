using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.People
{
    /// <summary>Punkt auf einem Gehweg: Segment, Seite (in Segmentrichtung, +1 rechts / −1 links) und Bogenlänge.</summary>
    public struct SidewalkAccess
    {
        public int SegmentId;
        /// <summary>+1 = rechter Gehweg, −1 = linker Gehweg (in Segmentrichtung Start → Ende).</summary>
        public int Side;
        /// <summary>Bogenlänge auf der Mittellinie (gleiche Abtastung wie das Straßen-Mesh).</summary>
        public float D;
        public Vector2 Point;
        public float Height;
    }

    /// <summary>
    /// Fußwegenetz aus den Gehwegen des Straßennetzes (rein geometrisch, ungerichtet). Knoten des Graphen sind die
    /// Gehweg-Enden je Straßenarm an einem Straßenknoten (links/rechts, vom Knoten weg gesehen); Kanten sind
    /// (1) Gehwege entlang der Segmente (Versatz = Fahrbahnrand + halbe Gehwegbreite, `LaneDefinition.Offset` des
    /// Gehwegs), (2) Gehwegecken um die Kreuzung (Mitte zwischen gerundetem Bordstein und Außenkante aus
    /// <see cref="JunctionGeometry.Layout"/>, im Knick entlang des Eckstücks) und (3) Querungen (Zebrastreifen) über
    /// einen Arm – nur an Kreuzungsflächen (<see cref="NodeShape.Junction"/>) und an Sackgassen-Enden.
    /// Straßen ohne Gehweg (Autobahn, Rampe) gehören nicht dazu. Neu bauen, wenn sich <see cref="RoadNetwork.Version"/> ändert.
    /// </summary>
    public sealed class SidewalkGraph
    {
        /// <summary>Gehweg liegt so hoch über der Fahrbahn (wie <c>RoadMeshBuilder.SidewalkHeight</c>).</summary>
        public const float SidewalkLift = 0.2f;
        /// <summary>Fahrbahnbelag (wie <c>RoadMeshBuilder.RoadHeight</c>).</summary>
        public const float RoadLift = 0.05f;
        /// <summary>Zuschlag (m) je Straßenquerung – Fußgänger bleiben lieber auf ihrer Seite.</summary>
        public const float CrossingPenalty = 6f;

        public RoadNetwork Network { get; }
        /// <summary>Stand des Straßennetzes beim Bau.</summary>
        public int Version { get; }

        readonly List<Vector2> vertexPos = new List<Vector2>();
        readonly List<float> vertexHeight = new List<float>();
        readonly List<List<Link>> links = new List<List<Link>>();
        readonly List<Edge> edges = new List<Edge>();
        /// <summary>(Segment-Id, Seite) → Gehweg-Kante.</summary>
        readonly Dictionary<long, int> sidewalkEdges = new Dictionary<long, int>();
        readonly Dictionary<int, SampledCurve> curves = new Dictionary<int, SampledCurve>();

        public int VertexCount => vertexPos.Count;
        public int EdgeCount => edges.Count;
        public int CrossingCount { get; private set; }
        public Vector2 VertexPosition(int v) => vertexPos[v];

        struct Link
        {
            public int To;
            public int Edge;
            public float Cost;
        }

        sealed class Edge
        {
            public int A, B;
            public float Length;
            public bool Crossing;
            // Gehweg entlang eines Segments (A bei D0, B bei D1)
            public RoadSegment Segment;
            public int Side;
            public float Offset, D0, D1;
            // sonst: fester Linienzug von A nach B
            public Vector2[] Points;
            public float[] Heights;
            public bool[] Low; // Punkt liegt auf Fahrbahnhöhe (Querung)
        }

        struct Arm
        {
            public RoadSegment Segment;
            public bool AtStart;
            public Vector2 Point, Dir, Right;
            public float Height, Trim;
            public float LeftOffset, RightOffset; // je ≥ 0, vom Knoten weg gesehen
            public int L, R;                      // Graph-Knoten
        }

        public static bool HasSidewalks(RoadType t) =>
            t.Lanes.Count >= 2 && t.Lanes[0].Kind == LaneKind.Sidewalk && t.Lanes[t.Lanes.Count - 1].Kind == LaneKind.Sidewalk;

        /// <summary>Mittellinienabstand des Gehwegs auf Seite <paramref name="side"/> (+1 rechts, −1 links; Vorzeichen = Seite).</summary>
        public static float SidewalkOffset(RoadType t, int side) => side > 0 ? t.Lanes[t.Lanes.Count - 1].Offset : t.Lanes[0].Offset;

        static long Key(int segmentId, int side) => ((long)segmentId << 1) | (side > 0 ? 1L : 0L);

        public SidewalkGraph(RoadNetwork network)
        {
            Network = network;
            Version = network.Version;
            var armsAt = new Dictionary<(int node, int seg), Arm>();
            var nodes = new List<RoadNode>(network.Nodes);
            nodes.Sort((a, b) => a.Id.CompareTo(b.Id)); // deterministische Reihenfolge
            foreach (var node in nodes) BuildNode(node, armsAt);

            var segs = new List<RoadSegment>(network.Segments);
            segs.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var s in segs)
            {
                if (!HasSidewalks(s.Type) || s.Start == s.End) continue;
                if (!armsAt.TryGetValue((s.Start.Id, s.Id), out var a) || !armsAt.TryGetValue((s.End.Id, s.Id), out var b)) continue;
                var curve = Curve(s);
                float d0 = a.Trim, d1 = Math.Max(d0, curve.Length - b.Trim);
                // Rechts (Segmentrichtung): am Start rechts vom Arm, am Ende links vom Arm
                AddSidewalk(s, +1, a.R, b.L, d0, d1);
                AddSidewalk(s, -1, a.L, b.R, d0, d1);
            }
        }

        // ------------------------------------------------------------ Aufbau

        SampledCurve Curve(RoadSegment s)
        {
            if (!curves.TryGetValue(s.Id, out var c)) curves[s.Id] = c = JunctionGeometry.Sample(s);
            return c;
        }

        int AddVertex(Vector2 p, float h)
        {
            vertexPos.Add(p);
            vertexHeight.Add(h);
            links.Add(new List<Link>());
            return vertexPos.Count - 1;
        }

        void Connect(Edge e)
        {
            int id = edges.Count;
            edges.Add(e);
            float cost = e.Length + (e.Crossing ? CrossingPenalty : 0f);
            links[e.A].Add(new Link { To = e.B, Edge = id, Cost = cost });
            links[e.B].Add(new Link { To = e.A, Edge = id, Cost = cost });
            if (e.Crossing) CrossingCount++;
        }

        void AddSidewalk(RoadSegment s, int side, int a, int b, float d0, float d1)
        {
            var e = new Edge { A = a, B = b, Segment = s, Side = side, Offset = SidewalkOffset(s.Type, side), D0 = d0, D1 = d1 };
            e.Length = PolyLength(SidewalkPoints(Curve(s), e.Offset, d0, d1));
            sidewalkEdges[Key(s.Id, side)] = edges.Count;
            Connect(e);
        }

        void AddPoly(int a, int b, List<Vector2> pts, List<float> hs, List<bool> low, bool crossing)
        {
            var e = new Edge { A = a, B = b, Points = pts.ToArray(), Heights = hs.ToArray(), Low = low.ToArray(), Crossing = crossing };
            e.Length = PolyLength(e.Points);
            Connect(e);
        }

        void BuildNode(RoadNode node, Dictionary<(int, int), Arm> armsAt)
        {
            var shape = JunctionGeometry.Classify(Network, node);
            JunctionLayout layout = shape == NodeShape.Junction ? JunctionGeometry.Layout(Network, node) : null;
            float bendTrim = 0f;
            BendLayout bend = default;
            if (shape == NodeShape.Bend) { bend = JunctionGeometry.Bend(Network, node); bendTrim = bend.Trim; }

            var arms = new List<(float angle, Arm arm, int layoutIndex)>();
            foreach (int id in node.SegmentIds)
            {
                var s = Network.GetSegment(id);
                if (s == null || s.Start == s.End || !HasSidewalks(s.Type)) continue;
                bool atStart = s.Start == node;
                var arm = new Arm { Segment = s, AtStart = atStart };
                int li = -1;
                if (layout != null)
                {
                    for (int k = 0; k < layout.Arms.Count; k++)
                        if (layout.Arms[k].Segment == s) { li = k; break; }
                }
                if (li >= 0)
                {
                    var la = layout.Arms[li];
                    arm.Point = la.Point; arm.Dir = la.Dir; arm.Right = la.Right; arm.Height = la.Height; arm.Trim = la.Trim;
                }
                else
                {
                    float trim = shape == NodeShape.Bend ? bendTrim : 0f;
                    JunctionGeometry.EndFrame(s, node, trim, out var p, out var dir, out float h);
                    arm.Point = p; arm.Dir = dir; arm.Right = Geometry2D.Right(dir); arm.Height = h; arm.Trim = trim;
                }
                // Vom Knoten weg gesehen: am Start ist rechts = Segment-rechts, am Ende vertauscht
                float segRight = SidewalkOffset(s.Type, +1), segLeft = -SidewalkOffset(s.Type, -1);
                arm.RightOffset = atStart ? segRight : segLeft;
                arm.LeftOffset = atStart ? segLeft : segRight;
                float lift = arm.Height + SidewalkLift;
                arm.L = AddVertex(arm.Point - arm.Right * arm.LeftOffset, lift);
                arm.R = AddVertex(arm.Point + arm.Right * arm.RightOffset, lift);
                arms.Add((MathF.Atan2(arm.Dir.Y, arm.Dir.X), arm, li));
            }
            if (arms.Count == 0) return;
            // Gegen den Uhrzeigersinn; an Kreuzungen exakt in der Reihenfolge des Layouts (Ecken passen dann zu den Armen)
            if (layout != null) arms.Sort((x, y) => x.layoutIndex.CompareTo(y.layoutIndex));
            else arms.Sort((x, y) => x.angle.CompareTo(y.angle));
            foreach (var e in arms) armsAt[(node.Id, e.arm.Segment.Id)] = e.arm;

            var pts = new List<Vector2>();
            var hs = new List<float>();
            var low = new List<bool>();
            int n = arms.Count;

            // Gehwegecken: linker Gehweg von Arm i → rechter Gehweg von Arm i+1
            if (n >= 2)
                for (int i = 0; i < n; i++)
                {
                    var a = arms[i];
                    var b = arms[(i + 1) % n];
                    pts.Clear(); hs.Clear(); low.Clear();
                    bool crossing = false;
                    var from = vertexPos[a.arm.L];
                    var to = vertexPos[b.arm.R];
                    float h0 = vertexHeight[a.arm.L], h1 = vertexHeight[b.arm.R];
                    if (layout != null && a.layoutIndex >= 0 && b.layoutIndex == (a.layoutIndex + 1) % layout.Arms.Count
                        && layout.Corners[a.layoutIndex].HasSidewalk)
                    {
                        var corner = layout.Corners[a.layoutIndex];
                        int m = corner.Curb.Length;
                        for (int k = 0; k < m; k++)
                        {
                            pts.Add(k == 0 ? from : k == m - 1 ? to : (corner.Curb[k] + corner.Outer[k]) * 0.5f);
                            hs.Add(h0 + (h1 - h0) * k / (m - 1f));
                            low.Add(false);
                        }
                    }
                    else if (shape == NodeShape.Bend && n == 2)
                    {
                        BendCorner(bend, from, to, h0, h1, pts, hs, low);
                    }
                    else
                    {
                        // Gerade Fortsetzung (Punkte fallen zusammen) oder Ecke über einen Arm ohne Gehweg
                        crossing = layout != null;
                        pts.Add(from); hs.Add(h0); low.Add(false);
                        pts.Add(to); hs.Add(h1); low.Add(false);
                    }
                    AddPoly(a.arm.L, b.arm.R, pts, hs, low, crossing);
                }

            // Querungen (Zebrastreifen) über einen Arm: an Kreuzungsflächen und Sackgassen-Enden
            bool deadEnd = node.SegmentIds.Count == 1;
            if (shape == NodeShape.Junction || deadEnd)
                foreach (var e in arms)
                {
                    var arm = e.arm;
                    pts.Clear(); hs.Clear(); low.Clear();
                    float c = arm.Segment.Type.CarriagewayHalfWidth;
                    float road = arm.Height + RoadLift, walk = arm.Height + SidewalkLift;
                    pts.Add(vertexPos[arm.R]); hs.Add(walk); low.Add(false);
                    pts.Add(arm.Point + arm.Right * c); hs.Add(road); low.Add(true);
                    pts.Add(arm.Point - arm.Right * c); hs.Add(road); low.Add(true);
                    pts.Add(vertexPos[arm.L]); hs.Add(walk); low.Add(false);
                    AddPoly(arm.R, arm.L, pts, hs, low, true);
                }
        }

        /// <summary>Gehweg um ein Knick-Eckstück: versetzte Mittellinie des Eckstücks (gleiche Seite wie Start/Ziel).</summary>
        static void BendCorner(BendLayout bend, Vector2 from, Vector2 to, float h0, float h1, List<Vector2> pts, List<float> hs, List<bool> low)
        {
            var curve = new SampledCurve(bend.P0, bend.Control, bend.P2, bend.HeightAt); // gleiche Höhe wie das Eckstück-Mesh
            // Versatz-Seite und Laufrichtung so wählen, dass die Enden zu den Gehweg-Enden passen
            float off = MathF.Abs(SidewalkOffset(bend.Type, +1));
            Vector2[] best = null;
            float bestErr = float.MaxValue;
            bool reverse = false;
            foreach (float o in new[] { off, -off })
            {
                var line = curve.OffsetLine(o);
                float fwd = Vector2.Distance(line[0], from) + Vector2.Distance(line[line.Length - 1], to);
                float bwd = Vector2.Distance(line[line.Length - 1], from) + Vector2.Distance(line[0], to);
                if (fwd < bestErr) { bestErr = fwd; best = line; reverse = false; }
                if (bwd < bestErr) { bestErr = bwd; best = line; reverse = true; }
            }
            int m = best.Length;
            for (int k = 0; k < m; k++)
            {
                var p = best[reverse ? m - 1 - k : k];
                pts.Add(k == 0 ? from : k == m - 1 ? to : p);
                hs.Add(h0 + (h1 - h0) * k / (m - 1f));
                low.Add(false);
            }
        }

        // ------------------------------------------------------------ Zugänge

        /// <summary>Gehweg-Punkt bei Bogenlänge d (auf den Gehweg zwischen den Knotenflächen begrenzt).</summary>
        public bool TryAccess(int segmentId, int side, float d, out SidewalkAccess access)
        {
            access = default;
            if (!sidewalkEdges.TryGetValue(Key(segmentId, side), out int ei)) return false;
            var e = edges[ei];
            var curve = Curve(e.Segment);
            d = Math.Clamp(d, e.D0, e.D1);
            curve.At(d, out _, out _, out float h);
            access = new SidewalkAccess
            {
                SegmentId = segmentId, Side = side > 0 ? 1 : -1, D = d,
                Point = curve.OffsetAt(d, e.Offset), Height = h + SidewalkLift,
            };
            return true;
        }

        /// <summary>Gehweg-Punkt auf der Seite von <paramref name="towards"/> bei Segment-Parameter t (z. B. Zufahrt eines Gebäudes).</summary>
        public bool TryAccessAtT(int segmentId, float t, Vector2 towards, out SidewalkAccess access)
        {
            access = default;
            var s = Network.GetSegment(segmentId);
            if (s == null || !HasSidewalks(s.Type)) return false;
            var p = s.Evaluate(t);
            int side = Vector2.Dot(towards - p, s.Right(t)) >= 0f ? 1 : -1;
            return TryAccess(segmentId, side, ArcLength(Curve(s), p), out access);
        }

        /// <summary>Nächster Gehweg-Punkt zu einem beliebigen Ort (z. B. Haltestelle, Kartenrand) im Umkreis.</summary>
        public bool TryNearestAccess(Vector2 p, float maxDistance, out SidewalkAccess access)
        {
            access = default;
            float best = maxDistance;
            bool found = false;
            foreach (var kv in sidewalkEdges)
            {
                if ((kv.Key & 1L) == 0) continue; // je Segment einmal (rechte Seite), Seite wird danach bestimmt
                var s = edges[kv.Value].Segment;
                float d = s.DistanceTo(p);
                if (d >= best) continue;
                float t = s.ClosestT(p);
                if (!TryAccessAtT(s.Id, t, p, out var a)) continue;
                best = d;
                access = a;
                found = true;
            }
            return found;
        }

        /// <summary>Gehweg-Punkt am Ende eines Segments bei einem Knoten (z. B. Außenanschluss am Kartenrand).</summary>
        public bool TryAccessAtNode(RoadNode node, out SidewalkAccess access)
        {
            access = default;
            if (node == null) return false;
            foreach (int id in node.SegmentIds)
            {
                var s = Network.GetSegment(id);
                if (s == null || !HasSidewalks(s.Type)) continue;
                return TryAccess(id, 1, node == s.Start ? 0f : float.MaxValue, out access);
            }
            return false;
        }

        /// <summary>Bogenlänge des Kurvenpunkts, der <paramref name="p"/> am nächsten liegt.</summary>
        static float ArcLength(SampledCurve c, Vector2 p)
        {
            float best = float.MaxValue, result = 0f;
            for (int i = 0; i < c.Count - 1; i++)
            {
                var a = c.Point(i);
                var ab = c.Point(i + 1) - a;
                float len2 = ab.LengthSquared();
                float u = len2 > 1e-8f ? Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
                float d2 = Vector2.DistanceSquared(a + ab * u, p);
                if (d2 < best) { best = d2; result = c.Distance(i) + (c.Distance(i + 1) - c.Distance(i)) * u; }
            }
            return result;
        }

        // ------------------------------------------------------------ Wegfindung

        sealed class Heap
        {
            readonly List<(float f, int v)> items = new List<(float, int)>();
            public int Count => items.Count;
            public void Clear() => items.Clear();
            public void Push(float f, int v)
            {
                items.Add((f, v));
                int i = items.Count - 1;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (items[p].f <= items[i].f) break;
                    (items[p], items[i]) = (items[i], items[p]);
                    i = p;
                }
            }
            public (float f, int v) Pop()
            {
                var top = items[0];
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < items.Count && items[l].f < items[m].f) m = l;
                    if (r < items.Count && items[r].f < items[m].f) m = r;
                    if (m == i) break;
                    (items[m], items[i]) = (items[i], items[m]);
                    i = m;
                }
                return top;
            }
        }

        readonly Heap heap = new Heap();
        float[] gScore = new float[0];
        int[] cameFrom = new int[0];
        int[] cameEdge = new int[0];

        /// <summary>
        /// Weg über die Gehwege von <paramref name="from"/> nach <paramref name="to"/> (A* mit Luftlinie als Schätzung)
        /// und hängt den Linienzug an <paramref name="route"/> an. false = nicht verbunden.
        /// </summary>
        public bool AppendPath(in SidewalkAccess from, in SidewalkAccess to, PedestrianRouteBuilder route)
        {
            if (!sidewalkEdges.TryGetValue(Key(from.SegmentId, from.Side), out int fe) ||
                !sidewalkEdges.TryGetValue(Key(to.SegmentId, to.Side), out int te)) return false;
            var ef = edges[fe];
            var et = edges[te];

            int n = vertexPos.Count;
            int target = n;
            if (gScore.Length < n + 1) { gScore = new float[n + 1]; cameFrom = new int[n + 1]; cameEdge = new int[n + 1]; }
            for (int i = 0; i <= n; i++) { gScore[i] = float.MaxValue; cameFrom[i] = -1; cameEdge[i] = -1; }
            heap.Clear();

            // Direkt auf demselben Gehweg
            float direct = fe == te ? Along(ef, to.D - from.D) : float.MaxValue;
            float toA = Along(et, to.D - et.D0), toB = Along(et, et.D1 - to.D);

            var goal = to.Point;
            void Seed(int v, float g)
            {
                if (g >= gScore[v]) return;
                gScore[v] = g;
                cameFrom[v] = -2; // Start
                heap.Push(g + Vector2.Distance(vertexPos[v], goal), v);
            }
            Seed(ef.A, Along(ef, from.D - ef.D0));
            Seed(ef.B, Along(ef, ef.D1 - from.D));
            if (direct < float.MaxValue) { gScore[target] = direct; cameFrom[target] = -2; }

            while (heap.Count > 0)
            {
                var (f, v) = heap.Pop();
                if (f >= gScore[target]) break;
                float g = gScore[v];
                if (f - Vector2.Distance(vertexPos[v], to.Point) > g + 1e-3f) continue; // veraltet
                // Ziel erreichbar über die Gehweg-Kante des Ziels
                if (v == et.A && g + toA < gScore[target]) { gScore[target] = g + toA; cameFrom[target] = v; }
                if (v == et.B && g + toB < gScore[target]) { gScore[target] = g + toB; cameFrom[target] = v; }
                foreach (var l in links[v])
                {
                    float ng = g + l.Cost;
                    if (ng >= gScore[l.To]) continue;
                    gScore[l.To] = ng;
                    cameFrom[l.To] = v;
                    cameEdge[l.To] = l.Edge;
                    heap.Push(ng + Vector2.Distance(vertexPos[l.To], to.Point), l.To);
                }
            }
            if (gScore[target] == float.MaxValue) return false;

            if (cameFrom[target] == -2)
            {
                AppendSidewalk(ef, from.D, to.D, route);
                return true;
            }
            // Pfad rückwärts einsammeln
            var chain = new List<int>();
            for (int v = cameFrom[target]; v >= 0; v = cameFrom[v] == -2 ? -1 : cameFrom[v]) chain.Add(v);
            chain.Reverse();
            int first = chain[0];
            AppendSidewalk(ef, from.D, first == ef.A ? ef.D0 : ef.D1, route);
            for (int i = 1; i < chain.Count; i++) AppendEdge(edges[cameEdge[chain[i]]], chain[i - 1], route);
            int last = chain[chain.Count - 1];
            AppendSidewalk(et, last == et.A ? et.D0 : et.D1, to.D, route);
            return true;
        }

        /// <summary>Gehweg-Stück eines Segments von Bogenlänge d0 nach d1 (beliebige Richtung).</summary>
        void AppendSidewalk(Edge e, float d0, float d1, PedestrianRouteBuilder route)
        {
            var c = Curve(e.Segment);
            var st = new List<float>();
            bool rev = d1 < d0;
            c.Stations(MathF.Min(d0, d1), MathF.Max(d0, d1), st);
            if (rev) st.Reverse();
            foreach (float d in st)
            {
                c.At(d, out _, out _, out float h);
                route.Add(c.OffsetAt(d, e.Offset), h + SidewalkLift, PedestrianRoute.SurfaceSidewalk);
            }
        }

        void AppendEdge(Edge e, int fromVertex, PedestrianRouteBuilder route)
        {
            bool forward = fromVertex == e.A;
            if (e.Segment != null) { AppendSidewalk(e, forward ? e.D0 : e.D1, forward ? e.D1 : e.D0, route); return; }
            int m = e.Points.Length;
            for (int k = 0; k < m; k++)
            {
                int i = forward ? k : m - 1 - k;
                route.Add(e.Points[i], e.Heights[i], e.Low[i] ? PedestrianRoute.SurfaceCrossing : PedestrianRoute.SurfaceSidewalk);
            }
        }

        static List<Vector2> SidewalkPoints(SampledCurve c, float offset, float d0, float d1)
        {
            var st = new List<float>();
            c.Stations(d0, d1, st);
            var into = new List<Vector2>(st.Count);
            foreach (float d in st) into.Add(c.OffsetAt(d, offset));
            return into;
        }

        /// <summary>Weglänge auf dem Gehweg für ein Stück Mittellinie (Versatz in Kurven berücksichtigt).</summary>
        static float Along(Edge e, float dd) => e.D1 - e.D0 > 1e-3f ? MathF.Abs(dd) * e.Length / (e.D1 - e.D0) : MathF.Abs(dd);

        static float PolyLength(IReadOnlyList<Vector2> pts)
        {
            float l = 0f;
            for (int i = 1; i < pts.Count; i++) l += Vector2.Distance(pts[i - 1], pts[i]);
            return l;
        }
    }
}
