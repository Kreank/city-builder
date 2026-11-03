using System;
using System.Collections.Generic;
using System.Linq;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Traffic
{
    /// <summary>
    /// Spurgraph: alle befahrbaren Spuren und die Abbiegebeziehungen an den Knoten.
    /// Wird automatisch aus dem Straßennetz abgeleitet und bei Änderungen knotenweise neu aufgebaut.
    /// Abbiegeregel (Rechtsverkehr): linke Spuren biegen links ab, rechte rechts, geradeaus von allen.
    /// </summary>
    public sealed class LaneGraph
    {
        const float StraightAngle = 35f;

        readonly RoadNetwork network;
        readonly Dictionary<int, Lane[]> segmentLanes = new Dictionary<int, Lane[]>();
        readonly Dictionary<int, List<LaneConnection>> nodeConnections = new Dictionary<int, List<LaneConnection>>();
        readonly HashSet<int> dirtyNodes = new HashSet<int>();
        static readonly Lane[] NoLanes = new Lane[0];
        static readonly List<LaneConnection> NoConnections = new List<LaneConnection>();

        /// <summary>Spuren eines entfernten Segments – Fahrzeuge darauf müssen weg.</summary>
        public event Action<Lane[]> LanesRemoved;
        /// <summary>Verbindungen eines Knotens werden ersetzt – Fahrzeuge darauf müssen weg/umgeleitet werden.</summary>
        public event Action<List<LaneConnection>> ConnectionsRemoved;

        public int Version { get; private set; }

        public LaneGraph(RoadNetwork network)
        {
            this.network = network;
            network.SegmentAdded += OnSegmentAdded;
            network.SegmentRemoved += OnSegmentRemoved;
            network.NodeRemoved += OnNodeRemoved;
            foreach (var s in network.Segments) OnSegmentAdded(s);
            Update();
        }

        public IEnumerable<Lane> AllLanes => segmentLanes.Values.SelectMany(l => l);
        public IEnumerable<LaneConnection> AllConnections => nodeConnections.Values.SelectMany(c => c);

        public Lane[] GetLanes(int segmentId) => segmentLanes.TryGetValue(segmentId, out var l) ? l : NoLanes;

        public IReadOnlyList<LaneConnection> GetConnections(int nodeId) =>
            nodeConnections.TryGetValue(nodeId, out var c) ? c : NoConnections;

        public IEnumerable<Lane> LanesOf(EdgeKey edge)
        {
            foreach (var l in GetLanes(edge.SegmentId)) if (l.Along == edge.Along) yield return l;
        }

        public RoadNode EdgeTo(EdgeKey edge)
        {
            var s = network.GetSegment(edge.SegmentId);
            return s == null ? null : edge.Along ? s.End : s.Start;
        }

        public RoadNode EdgeFrom(EdgeKey edge)
        {
            var s = network.GetSegment(edge.SegmentId);
            return s == null ? null : edge.Along ? s.Start : s.End;
        }

        /// <summary>Fahrspuren, die am Knoten enden (Zufahrten).</summary>
        public IEnumerable<Lane> IncomingLanes(RoadNode node)
        {
            foreach (int id in node.SegmentIds)
                foreach (var l in GetLanes(id))
                    if (l.To == node) yield return l;
        }

        /// <summary>Fahrspuren, die am Knoten beginnen (Ausfahrten).</summary>
        public IEnumerable<Lane> OutgoingLanes(RoadNode node)
        {
            foreach (int id in node.SegmentIds)
                foreach (var l in GetLanes(id))
                    if (l.From == node) yield return l;
        }

        // ------------------------------------------------------------ Änderungen

        void OnSegmentAdded(RoadSegment segment)
        {
            var lanes = new List<Lane>();
            var defs = segment.Type.Lanes;
            for (int i = 0; i < defs.Count; i++)
                if (defs[i].IsVehicleLane) lanes.Add(new Lane(segment, i, defs[i]));

            // Reihenfolge aus Fahrersicht (links → rechts) und Nachbarn je Fahrtrichtung
            foreach (bool along in new[] { true, false })
            {
                var dir = lanes.Where(l => l.Along == along)
                    .OrderBy(l => along ? l.Offset : -l.Offset).ToList();
                for (int i = 0; i < dir.Count; i++)
                {
                    dir[i].Rank = i;
                    dir[i].LeftNeighbor = i > 0 ? dir[i - 1] : null;
                    dir[i].RightNeighbor = i < dir.Count - 1 ? dir[i + 1] : null;
                }
            }
            segmentLanes[segment.Id] = lanes.ToArray();
            dirtyNodes.Add(segment.Start.Id);
            dirtyNodes.Add(segment.End.Id);
            Version++;
        }

        void OnSegmentRemoved(RoadSegment segment)
        {
            if (segmentLanes.TryGetValue(segment.Id, out var lanes))
            {
                segmentLanes.Remove(segment.Id);
                LanesRemoved?.Invoke(lanes);
            }
            dirtyNodes.Add(segment.Start.Id);
            dirtyNodes.Add(segment.End.Id);
            Version++;
        }

        void OnNodeRemoved(RoadNode node)
        {
            if (nodeConnections.TryGetValue(node.Id, out var old))
            {
                nodeConnections.Remove(node.Id);
                ConnectionsRemoved?.Invoke(old);
            }
            dirtyNodes.Remove(node.Id);
        }

        /// <summary>Baut die Abbiegebeziehungen aller geänderten Knoten neu auf.</summary>
        public void Update()
        {
            if (dirtyNodes.Count == 0) return;
            foreach (int id in dirtyNodes.ToList())
            {
                var node = network.GetNode(id);
                if (node != null) RebuildNode(node);
            }
            dirtyNodes.Clear();
            Version++;
        }

        void RebuildNode(RoadNode node)
        {
            if (nodeConnections.TryGetValue(node.Id, out var old))
            {
                foreach (var c in old) c.From.Outgoing.Remove(c);
                if (old.Count > 0) ConnectionsRemoved?.Invoke(old);
            }
            var result = new List<LaneConnection>();
            nodeConnections[node.Id] = result;

            var incoming = IncomingLanes(node).ToList();
            var outgoing = OutgoingLanes(node).ToList();
            if (incoming.Count == 0 || outgoing.Count == 0) return;

            bool deadEnd = node.SegmentIds.Count == 1;
            foreach (var approach in incoming.GroupBy(l => l.Segment.Id))
            {
                var inLanes = approach.OrderBy(l => l.Rank).ToList();
                var inDir = Direction(inLanes[0], atEnd: true);

                // Ausfahrten (Segmente) nach Abbiegewinkel sortiert: links (positiv) → rechts (negativ)
                var exits = outgoing
                    .Where(l => l.Segment.Id != approach.Key || deadEnd)
                    .GroupBy(l => l.Segment.Id)
                    .Select(g => new
                    {
                        Lanes = g.OrderBy(l => l.Rank).ToList(),
                        Angle = RoadGeometry.SignedAngle(inDir, Direction(g.First(), atEnd: false)),
                        UTurn = g.Key == approach.Key,
                    })
                    .OrderByDescending(e => e.Angle)
                    .ToList();
                if (exits.Count == 0) continue;

                // Straßenbahn und Straßenverkehr getrennt verbinden
                foreach (bool tram in new[] { false, true })
                {
                    var ins = inLanes.Where(l => (l.Allowed == VehicleClass.Tram) == tram).ToList();
                    if (ins.Count == 0) continue;
                    var validExits = exits
                        .Select(e => new { e.Angle, e.UTurn, Lanes = e.Lanes.Where(l => (l.Allowed == VehicleClass.Tram) == tram).ToList() })
                        .Where(e => e.Lanes.Count > 0)
                        .ToList();
                    int m = validExits.Count;
                    for (int j = 0; j < m; j++)
                    {
                        var exit = validExits[j];
                        var turn = exit.UTurn ? TurnType.UTurn
                            : exit.Angle > StraightAngle ? TurnType.Left
                            : exit.Angle < -StraightAngle ? TurnType.Right
                            : TurnType.Straight;

                        // Welche Zufahrtsspuren dürfen in diese Ausfahrt?
                        int n = ins.Count;
                        int from, to;
                        if (turn == TurnType.Straight || m == 1) { from = 0; to = n - 1; }
                        else
                        {
                            from = (int)Math.Floor(j * n / (double)m);
                            to = Math.Max(from, (int)Math.Ceiling((j + 1) * n / (double)m) - 1);
                        }
                        Map(node, ins.GetRange(from, to - from + 1), exit.Lanes, turn, result);
                    }
                }
            }
        }

        /// <summary>Verteilt a Zufahrtsspuren auf b Ausfahrtsspuren (positionstreu) und sorgt dafür, dass jede Klasse weiterkommt.</summary>
        static void Map(RoadNode node, List<Lane> ins, List<Lane> outs, TurnType turn, List<LaneConnection> result)
        {
            int a = ins.Count, b = outs.Count;
            for (int r = 0; r < a; r++)
            {
                int lo, hi;
                if (a == 1) { lo = 0; hi = b - 1; }
                else
                {
                    lo = (int)Math.Floor(r * b / (double)a);
                    hi = Math.Max(lo, (int)Math.Ceiling((r + 1) * b / (double)a) - 1);
                }
                var src = ins[r];
                bool any = false;
                for (int k = lo; k <= hi; k++)
                {
                    if ((src.Allowed & outs[k].Allowed) == 0) continue;
                    Add(node, src, outs[k], turn, result);
                    any = true;
                }
                // z. B. Pkw-Spur, deren Gegenstück eine Busspur ist: nächstgelegene passende Spur nehmen
                if (!any)
                {
                    var alt = outs.Where(o => (o.Allowed & src.Allowed) != 0)
                        .OrderBy(o => Math.Abs(o.Rank - (lo + hi) * 0.5)).FirstOrDefault();
                    if (alt != null) Add(node, src, alt, turn, result);
                }
            }
        }

        static void Add(RoadNode node, Lane from, Lane to, TurnType turn, List<LaneConnection> result)
        {
            var c = new LaneConnection(from, to, node, turn);
            from.Outgoing.Add(c);
            result.Add(c);
        }

        /// <summary>Fahrtrichtung einer Spur am Knoten (am Spurende bzw. -anfang).</summary>
        static System.Numerics.Vector2 Direction(Lane lane, bool atEnd)
        {
            lane.Sample(atEnd ? lane.Length : 0f, 0f, out _, out _, out var dir);
            return dir;
        }
    }
}
