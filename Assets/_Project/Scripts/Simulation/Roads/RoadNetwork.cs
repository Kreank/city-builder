using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>
    /// Das Straßennetz als Graph (Knoten + Segmente). Grundlage für Verkehr,
    /// Wegfindung, Bauzonen entlang der Straßen und Lärm.
    /// </summary>
    public sealed class RoadNetwork
    {
        public const float MinSegmentLength = 4f;

        readonly Dictionary<int, RoadNode> nodes = new Dictionary<int, RoadNode>();
        readonly Dictionary<int, RoadSegment> segments = new Dictionary<int, RoadSegment>();
        int nextNodeId = 1;
        int nextSegmentId = 1;

        public event Action<RoadSegment> SegmentAdded;
        public event Action<RoadSegment> SegmentRemoved;
        public event Action<RoadNode> NodeAdded;
        public event Action<RoadNode> NodeRemoved;

        public IEnumerable<RoadNode> Nodes => nodes.Values;
        public IEnumerable<RoadSegment> Segments => segments.Values;
        public int NodeCount => nodes.Count;
        public int SegmentCount => segments.Count;

        public RoadNode GetNode(int id) => nodes.TryGetValue(id, out var n) ? n : null;
        public RoadSegment GetSegment(int id) => segments.TryGetValue(id, out var s) ? s : null;

        public RoadNode AddNode(Vector2 position)
        {
            var node = new RoadNode(nextNodeId++, position);
            nodes.Add(node.Id, node);
            NodeAdded?.Invoke(node);
            return node;
        }

        /// <summary>Verbindet zwei Knoten. Liefert null, wenn die Straße ungültig ist.</summary>
        public RoadSegment AddSegment(RoadNode start, RoadNode end, Vector2 control, RoadType type)
        {
            if (start == null || end == null || type == null) throw new ArgumentNullException();
            if (start == end) return null;
            if (!nodes.ContainsKey(start.Id) || !nodes.ContainsKey(end.Id)) throw new ArgumentException("Knoten gehört nicht zu diesem Netz.");
            if (Vector2.Distance(start.Position, end.Position) < MinSegmentLength) return null;
            if (AreConnected(start, end)) return null;

            var segment = new RoadSegment(nextSegmentId++, start, end, control, type);
            segments.Add(segment.Id, segment);
            start.segmentIds.Add(segment.Id);
            end.segmentIds.Add(segment.Id);
            SegmentAdded?.Invoke(segment);
            return segment;
        }

        /// <summary>
        /// Baut eine Straße von A nach B. Endpunkte rasten an vorhandenen Knoten ein,
        /// wenn sie näher als <paramref name="snapDistance"/> liegen.
        /// </summary>
        public RoadSegment BuildRoad(Vector2 start, Vector2? control, Vector2 end, RoadType type, float snapDistance = 0f)
        {
            if (Vector2.Distance(start, end) < MinSegmentLength) return null;

            var startNode = FindNearestNode(start, snapDistance);
            var endNode = FindNearestNode(end, snapDistance);
            Vector2 a = startNode?.Position ?? start;
            Vector2 b = endNode?.Position ?? end;
            if (Vector2.Distance(a, b) < MinSegmentLength) return null;
            if (startNode != null && endNode != null && AreConnected(startNode, endNode)) return null;

            startNode ??= AddNode(a);
            endNode ??= AddNode(b);
            var c = control ?? (a + b) * 0.5f;

            var segment = AddSegment(startNode, endNode, c, type);
            if (segment == null)
            {
                RemoveNodeIfOrphan(startNode);
                RemoveNodeIfOrphan(endNode);
            }
            return segment;
        }

        public bool RemoveSegment(int segmentId)
        {
            if (!segments.TryGetValue(segmentId, out var segment)) return false;
            segments.Remove(segmentId);
            segment.Start.segmentIds.Remove(segmentId);
            segment.End.segmentIds.Remove(segmentId);
            SegmentRemoved?.Invoke(segment);
            RemoveNodeIfOrphan(segment.Start);
            RemoveNodeIfOrphan(segment.End);
            return true;
        }

        public RoadNode FindNearestNode(Vector2 position, float maxDistance)
        {
            RoadNode best = null;
            float bestDist = maxDistance;
            foreach (var node in nodes.Values)
            {
                float d = Vector2.Distance(node.Position, position);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = node;
                }
            }
            return best;
        }

        public RoadSegment FindNearestSegment(Vector2 position, float maxDistance)
        {
            RoadSegment best = null;
            float bestDist = maxDistance;
            foreach (var segment in segments.Values)
            {
                float d = segment.DistanceTo(position) - segment.Type.Width * 0.5f;
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = segment;
                }
            }
            return best;
        }

        public bool AreConnected(RoadNode a, RoadNode b)
        {
            foreach (int id in a.segmentIds)
            {
                var s = segments[id];
                if (s.Other(a) == b) return true;
            }
            return false;
        }

        void RemoveNodeIfOrphan(RoadNode node)
        {
            if (node.segmentIds.Count > 0 || !nodes.Remove(node.Id)) return;
            NodeRemoved?.Invoke(node);
        }
    }
}
