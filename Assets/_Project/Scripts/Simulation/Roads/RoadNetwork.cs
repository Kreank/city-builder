using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>
    /// Das Straßennetz als Graph (Knoten + Segmente) mit den Grundoperationen.
    /// Das eigentliche Bauen mit Prüfungen, Kreuzungen und Kreisverkehren macht <see cref="RoadBuilder"/>.
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
        public bool Contains(RoadNode node) => node != null && nodes.ContainsKey(node.Id);
        public bool Contains(RoadSegment segment) => segment != null && segments.ContainsKey(segment.Id);

        public RoadNode AddNode(Vector2 position)
        {
            var node = new RoadNode(nextNodeId++, position);
            nodes.Add(node.Id, node);
            NodeAdded?.Invoke(node);
            return node;
        }

        /// <summary>Verbindet zwei Knoten. Liefert null, wenn das Segment zu kurz oder doppelt wäre.</summary>
        public RoadSegment AddSegment(RoadNode start, RoadNode end, Vector2 control, RoadType type)
        {
            if (start == null || end == null || type == null) throw new ArgumentNullException();
            if (start == end) return null;
            if (!Contains(start) || !Contains(end)) throw new ArgumentException("Knoten gehört nicht zu diesem Netz.");
            if (Vector2.Distance(start.Position, end.Position) < MinSegmentLength) return null;
            if (HasMatchingSegment(start, end, control)) return null;
            return CreateSegment(start, end, control, type);
        }

        public bool RemoveSegment(int segmentId)
        {
            if (!segments.TryGetValue(segmentId, out var segment)) return false;
            DetachSegment(segment);
            RemoveNodeIfOrphan(segment.Start);
            RemoveNodeIfOrphan(segment.End);
            return true;
        }

        /// <summary>
        /// Zerteilt ein Segment bei t in zwei Segmente gleichen Typs und gleicher Richtung.
        /// Liefert den neuen Knoten an der Schnittstelle.
        /// </summary>
        public RoadNode SplitSegment(RoadSegment segment, float t)
        {
            if (!Contains(segment)) throw new ArgumentException("Segment gehört nicht zu diesem Netz.");
            Bezier.Split(segment.Start.Position, segment.Control, segment.End.Position, t,
                out var leftControl, out var point, out var rightControl);

            var node = AddNode(point);
            DetachSegment(segment);
            CreateSegment(segment.Start, node, leftControl, segment.Type);
            CreateSegment(node, segment.End, rightControl, segment.Type);
            return node;
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

        /// <summary>Nächstes Segment, dessen Fahrbahnrand höchstens <paramref name="maxDistance"/> entfernt ist (0 = Punkt liegt auf der Straße).</summary>
        public RoadSegment FindNearestSegment(Vector2 position, float maxDistance)
        {
            RoadSegment best = null;
            float bestDist = maxDistance;
            foreach (var segment in segments.Values)
            {
                float margin = segment.Type.HalfWidth + maxDistance;
                Bezier.Bounds(segment.Start.Position, segment.Control, segment.End.Position, out var min, out var max);
                if (position.X < min.X - margin || position.Y < min.Y - margin ||
                    position.X > max.X + margin || position.Y > max.Y + margin) continue;

                float d = segment.DistanceTo(position) - segment.Type.HalfWidth;
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
                if (segments[id].Other(a) == b) return true;
            return false;
        }

        /// <summary>
        /// Gibt es zwischen a und b schon ein Segment mit (fast) derselben Form?
        /// Unterschiedliche Formen zwischen denselben Knoten sind erlaubt (z. B. Gerade + Bogen).
        /// </summary>
        public bool HasMatchingSegment(RoadNode a, RoadNode b, Vector2 control, float tolerance = 1f)
        {
            foreach (int id in a.segmentIds)
            {
                var s = segments[id];
                if (s.Other(a) == b && Vector2.Distance(s.Control, control) < tolerance) return true;
            }
            return false;
        }

        /// <summary>Entfernt den Knoten, wenn keine Straße mehr daran hängt.</summary>
        public void RemoveNodeIfOrphan(RoadNode node)
        {
            if (node.segmentIds.Count > 0 || !nodes.Remove(node.Id)) return;
            NodeRemoved?.Invoke(node);
        }

        RoadSegment CreateSegment(RoadNode start, RoadNode end, Vector2 control, RoadType type)
        {
            var segment = new RoadSegment(nextSegmentId++, start, end, control, type);
            segments.Add(segment.Id, segment);
            start.segmentIds.Add(segment.Id);
            end.segmentIds.Add(segment.Id);
            SegmentAdded?.Invoke(segment);
            return segment;
        }

        void DetachSegment(RoadSegment segment)
        {
            segments.Remove(segment.Id);
            segment.Start.segmentIds.Remove(segment.Id);
            segment.End.segmentIds.Remove(segment.Id);
            SegmentRemoved?.Invoke(segment);
        }
    }
}
