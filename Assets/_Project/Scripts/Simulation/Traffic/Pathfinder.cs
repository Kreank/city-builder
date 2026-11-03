using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Traffic
{
    /// <summary>
    /// A*-Wegfindung über gerichtete Segmente. Kosten = Fahrzeit (Tempolimit × Stau) + Abbiegezeit
    /// + Wartezeit an Ampeln/Schildern. Beachtet Sperrungen, Fahrverbote und Abbiegeverbote.
    /// </summary>
    public sealed class Pathfinder
    {
        const int MaxExpansions = 30000;
        const float HeuristicSpeed = 140f / 3.6f;

        readonly RoadNetwork network;
        readonly LaneGraph lanes;
        readonly TrafficRules rules;

        // Wiederverwendete Puffer
        readonly Dictionary<EdgeKey, float> best = new Dictionary<EdgeKey, float>();
        readonly Dictionary<EdgeKey, EdgeKey> cameFrom = new Dictionary<EdgeKey, EdgeKey>();
        readonly SortedSet<(float f, int seq, EdgeKey e)> open = new SortedSet<(float, int, EdgeKey)>(new OpenComparer());
        readonly HashSet<EdgeKey> transitions = new HashSet<EdgeKey>();
        int seq;

        public Pathfinder(RoadNetwork network, LaneGraph lanes, TrafficRules rules)
        {
            this.network = network;
            this.lanes = lanes;
            this.rules = rules;
        }

        sealed class OpenComparer : IComparer<(float f, int seq, EdgeKey e)>
        {
            public int Compare((float f, int seq, EdgeKey e) a, (float f, int seq, EdgeKey e) b)
            {
                int c = a.f.CompareTo(b.f);
                return c != 0 ? c : a.seq.CompareTo(b.seq);
            }
        }

        /// <summary>Kosten (geschätzte Fahrzeit in s) des zuletzt gefundenen Wegs.</summary>
        public float LastCost { get; private set; }

        /// <summary>
        /// Weg von <paramref name="start"/> (inklusive) bis zu einer Kante, die in <paramref name="destination"/> endet.
        /// Mit <paramref name="fromLane"/> sind am ersten Knoten nur Abbiegungen erlaubt, die diese Spur hat.
        /// </summary>
        public List<EdgeKey> FindPath(EdgeKey start, RoadNode destination, VehicleClass cls, Lane fromLane = null)
        {
            if (destination == null) return null;
            return Search(start, e => lanes.EdgeTo(e) == destination, destination.Position, cls, fromLane);
        }

        /// <summary>
        /// Weg bis zu einer Stelle mitten auf einem Segment (z. B. Gebäudezufahrt): Ziel ist eine der beiden
        /// Fahrtrichtungen des Segments. Liegt das Ziel auf der Startkante voraus, ist der Weg nur die Startkante.
        /// <paramref name="startS"/> = gefahrene Strecke auf der Startkante.
        /// </summary>
        public List<EdgeKey> FindPathToSegment(EdgeKey start, float startS, int segmentId, float t, VehicleClass cls, Lane fromLane = null)
        {
            var seg = network.GetSegment(segmentId);
            if (seg == null) return null;
            if (start.SegmentId == segmentId)
            {
                float targetS = start.Along ? t * seg.Length : (1f - t) * seg.Length;
                if (targetS > startS + 1f)
                {
                    LastCost = (targetS - startS) / MathF.Max(1f, rules.SpeedLimit(seg));
                    return new List<EdgeKey> { start };
                }
            }
            var goal = seg.Evaluate(t);
            return Search(start, e => e.SegmentId == segmentId && !e.Equals(start) && HasLaneFor(e, cls), goal, cls, fromLane);
        }

        bool HasLaneFor(EdgeKey edge, VehicleClass cls)
        {
            foreach (var l in lanes.LanesOf(edge)) if ((l.Allowed & cls) != 0) return true;
            return false;
        }

        List<EdgeKey> Search(EdgeKey start, Func<EdgeKey, bool> isGoal, Vector2 goal, VehicleClass cls, Lane fromLane)
        {
            best.Clear();
            cameFrom.Clear();
            open.Clear();
            if (network.GetSegment(start.SegmentId) == null) return null;

            best[start] = 0f;
            open.Add((Heuristic(start, goal), seq++, start));
            int expansions = 0;

            while (open.Count > 0 && expansions++ < MaxExpansions)
            {
                var current = open.Min;
                open.Remove(current);
                var edge = current.e;
                float g = best[edge];
                if (current.f - Heuristic(edge, goal) > g + 0.01f) continue; // veralteter Eintrag

                if (isGoal(edge))
                {
                    LastCost = g;
                    return Reconstruct(edge);
                }

                var node = lanes.EdgeTo(edge);
                CollectTransitions(edge, node, cls, edge.Equals(start) ? fromLane : null);
                foreach (var next in transitions)
                {
                    var seg = network.GetSegment(next.SegmentId);
                    float cost = g + EdgeCost(seg, next) + NodeCost(node, edge, next);
                    if (best.TryGetValue(next, out float old) && old <= cost) continue;
                    best[next] = cost;
                    cameFrom[next] = edge;
                    open.Add((cost + Heuristic(next, goal), seq++, next));
                }
            }
            return null;
        }

        void CollectTransitions(EdgeKey edge, RoadNode node, VehicleClass cls, Lane onlyFrom)
        {
            transitions.Clear();
            if (node == null) return;
            foreach (var c in lanes.GetConnections(node.Id))
            {
                if (c.From.Segment.Id != edge.SegmentId || c.From.Along != edge.Along) continue;
                if (onlyFrom != null && c.From != onlyFrom) continue;
                if ((c.From.Allowed & cls) == 0 || (c.To.Allowed & cls) == 0) continue;
                if (!rules.IsSegmentAllowed(c.To.Segment.Id, cls)) continue;
                if (!rules.IsTurnAllowed(node.Id, edge.SegmentId, c.To.Segment.Id, cls)) continue;
                transitions.Add(c.To.Edge);
            }
        }

        float EdgeCost(RoadSegment seg, EdgeKey edge)
        {
            float speed = rules.SpeedLimit(seg);
            float ratio = 1f;
            int count = 0;
            foreach (var l in lanes.LanesOf(edge)) { ratio += l.SpeedRatio; count++; }
            ratio = count > 0 ? (ratio - 1f) / count : 1f;
            return seg.Length / (speed * MathF.Max(0.15f, ratio));
        }

        float NodeCost(RoadNode node, EdgeKey from, EdgeKey to)
        {
            if (from.SegmentId == to.SegmentId) return 30f; // Wenden
            float cost = 0f;
            var control = rules.ControlAt(node.Id);
            if (control == JunctionControl.TrafficLights) cost += 8f;
            var sign = rules.SignAt(node.Id, from.SegmentId);
            if (sign == ApproachSign.Stop) cost += 5f;
            else if (sign == ApproachSign.Yield) cost += 2f;
            if (node.SegmentIds.Count >= 3) cost += 1.5f;
            return cost;
        }

        float Heuristic(EdgeKey edge, Vector2 goal)
        {
            var to = lanes.EdgeTo(edge);
            return to == null ? 0f : Vector2.Distance(to.Position, goal) / HeuristicSpeed;
        }

        List<EdgeKey> Reconstruct(EdgeKey last)
        {
            var path = new List<EdgeKey> { last };
            while (cameFrom.TryGetValue(path[path.Count - 1], out var prev)) path.Add(prev);
            path.Reverse();
            return path;
        }
    }
}
