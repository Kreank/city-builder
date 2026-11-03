using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Roads
{
    /// <summary>
    /// Das Straßennetz als Graph (Knoten + Segmente) mit den Grundoperationen.
    /// Das eigentliche Bauen mit Prüfungen, Kreuzungen, Kreisverkehren und Vorlagen macht <see cref="RoadBuilder"/>.
    /// </summary>
    public sealed class RoadNetwork
    {
        public const float MinSegmentLength = 4f;
        /// <summary>Höhenunterschied, bis zu dem zwei Straßen als „gleiche Ebene“ gelten.</summary>
        public const float SameLevelTolerance = 1.5f;

        readonly Dictionary<int, RoadNode> nodes = new Dictionary<int, RoadNode>();
        readonly Dictionary<int, RoadSegment> segments = new Dictionary<int, RoadSegment>();
        int nextNodeId = 1;
        int nextSegmentId = 1;

        public event Action<RoadSegment> SegmentAdded;
        public event Action<RoadSegment> SegmentRemoved;
        public event Action<RoadNode> NodeAdded;
        public event Action<RoadNode> NodeRemoved;
        /// <summary>
        /// Ein Segment wurde durch andere ersetzt (Zerteilen, Typwechsel, Umdrehen).
        /// Wird nach SegmentRemoved/SegmentAdded ausgelöst – z. B. um Verkehrsregeln zu übernehmen.
        /// </summary>
        public event Action<RoadSegment, IReadOnlyList<RoadSegment>> SegmentReplaced;

        /// <summary>Wird bei jeder Änderung erhöht.</summary>
        public int Version { get; private set; }

        /// <summary>Gelände unter dem Netz (Standard: flach auf 0).</summary>
        public IGround Ground { get; set; } = FlatGround.Instance;

        // --- Stadtkasse (Hook): prüft/bucht Baukosten in RoadBuilder. null = Bauen kostenlos (reine Netz-Tests).
        public Economy.IConstructionBudget Budget { get; set; }
        // --- Ende Stadtkasse

        public IEnumerable<RoadNode> Nodes => nodes.Values;
        public IEnumerable<RoadSegment> Segments => segments.Values;
        public int NodeCount => nodes.Count;
        public int SegmentCount => segments.Count;

        public RoadNode GetNode(int id) => nodes.TryGetValue(id, out var n) ? n : null;
        public RoadSegment GetSegment(int id) => segments.TryGetValue(id, out var s) ? s : null;
        public bool Contains(RoadNode node) => node != null && nodes.ContainsKey(node.Id);
        public bool Contains(RoadSegment segment) => segment != null && segments.ContainsKey(segment.Id);

        public RoadNode AddNode(Vector2 position, float height = 0f)
        {
            var node = new RoadNode(nextNodeId++, position, height);
            nodes.Add(node.Id, node);
            Version++;
            NodeAdded?.Invoke(node);
            return node;
        }

        /// <summary>Verbindet zwei Knoten. Liefert null, wenn das Segment zu kurz oder doppelt wäre.</summary>
        public RoadSegment AddSegment(RoadNode start, RoadNode end, Vector2 control, RoadType type,
            RoadSegmentFlags flags = RoadSegmentFlags.None, float[] profile = null)
        {
            if (start == null || end == null || type == null) throw new ArgumentNullException();
            if (start == end) return null;
            if (!Contains(start) || !Contains(end)) throw new ArgumentException("Knoten gehört nicht zu diesem Netz.");
            if (Vector2.Distance(start.Position, end.Position) < MinSegmentLength) return null;
            if (HasMatchingSegment(start, end, control)) return null;
            return CreateSegment(start, end, control, type, flags, profile);
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
        /// Zerteilt ein Segment bei t in zwei Segmente gleichen Typs, gleicher Richtung und Flags.
        /// Liefert den neuen Knoten an der Schnittstelle (Höhe interpoliert).
        /// </summary>
        public RoadNode SplitSegment(RoadSegment segment, float t)
        {
            if (!Contains(segment)) throw new ArgumentException("Segment gehört nicht zu diesem Netz.");
            Bezier.Split(segment.Start.Position, segment.Control, segment.End.Position, t,
                out var leftControl, out var point, out var rightControl);

            var node = AddNode(point, segment.HeightAt(t));
            DetachSegment(segment);
            float[] pa = null, pb = null;
            if (segment.FollowsTerrain)
            {
                pa = Profiles.Resample(segment.HeightAt, 0f, t, segment.Length);
                pb = Profiles.Resample(segment.HeightAt, t, 1f, segment.Length);
            }
            var a = CreateSegment(segment.Start, node, leftControl, segment.Type, segment.Flags, pa);
            var b = CreateSegment(node, segment.End, rightControl, segment.Type, segment.Flags, pb);
            SegmentReplaced?.Invoke(segment, new[] { a, b });
            return node;
        }

        /// <summary>Tauscht den Straßentyp (gleiche Form/Richtung). Liefert das neue Segment.</summary>
        public RoadSegment ReplaceType(RoadSegment segment, RoadType type)
        {
            if (!Contains(segment)) throw new ArgumentException("Segment gehört nicht zu diesem Netz.");
            DetachSegment(segment);
            var replacement = CreateSegment(segment.Start, segment.End, segment.Control, type, segment.Flags, segment.Profile);
            SegmentReplaced?.Invoke(segment, new[] { replacement });
            return replacement;
        }

        /// <summary>Dreht die Fahrtrichtung um (wichtig für Einbahnstraßen). Liefert das neue Segment.</summary>
        public RoadSegment Reverse(RoadSegment segment)
        {
            if (!Contains(segment)) throw new ArgumentException("Segment gehört nicht zu diesem Netz.");
            DetachSegment(segment);
            var profile = segment.Profile;
            if (profile != null) Array.Reverse(profile);
            var replacement = CreateSegment(segment.End, segment.Start, segment.Control, segment.Type, segment.Flags, profile);
            SegmentReplaced?.Invoke(segment, new[] { replacement });
            return replacement;
        }

        /// <summary>Nächster Knoten im Umkreis; mit <paramref name="height"/> nur Knoten auf gleicher Ebene.</summary>
        public RoadNode FindNearestNode(Vector2 position, float maxDistance, float? height = null)
        {
            RoadNode best = null;
            float bestDist = maxDistance;
            foreach (var node in nodes.Values)
            {
                if (height.HasValue && MathF.Abs(node.Height - height.Value) > SameLevelTolerance) continue;
                float d = Vector2.Distance(node.Position, position);
                if (d <= bestDist)
                {
                    bestDist = d;
                    best = node;
                }
            }
            return best;
        }

        /// <summary>
        /// Nächstes Segment, dessen Fahrbahnrand höchstens <paramref name="maxDistance"/> entfernt ist
        /// (0 = Punkt liegt auf der Straße). Mit <paramref name="height"/> nur Straßen auf gleicher Ebene.
        /// </summary>
        public RoadSegment FindNearestSegment(Vector2 position, float maxDistance, float? height = null)
        {
            RoadSegment best = null;
            float bestDist = maxDistance;
            foreach (var segment in segments.Values)
            {
                float margin = segment.Type.HalfWidth + maxDistance;
                Bezier.Bounds(segment.Start.Position, segment.Control, segment.End.Position, out var min, out var max);
                if (position.X < min.X - margin || position.Y < min.Y - margin ||
                    position.X > max.X + margin || position.Y > max.Y + margin) continue;

                float t = segment.ClosestT(position);
                if (height.HasValue && MathF.Abs(segment.HeightAt(t) - height.Value) > SameLevelTolerance) continue;
                float d = Vector2.Distance(segment.Evaluate(t), position) - segment.Type.HalfWidth;
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
            Version++;
            NodeRemoved?.Invoke(node);
        }

        RoadSegment CreateSegment(RoadNode start, RoadNode end, Vector2 control, RoadType type, RoadSegmentFlags flags,
            float[] profile = null)
        {
            var segment = new RoadSegment(nextSegmentId++, start, end, control, type, flags, Ground, profile);
            segments.Add(segment.Id, segment);
            start.segmentIds.Add(segment.Id);
            end.segmentIds.Add(segment.Id);
            Version++;
            SegmentAdded?.Invoke(segment);
            return segment;
        }

        void DetachSegment(RoadSegment segment)
        {
            segments.Remove(segment.Id);
            segment.Start.segmentIds.Remove(segment.Id);
            segment.End.segmentIds.Remove(segment.Id);
            Version++;
            SegmentRemoved?.Invoke(segment);
        }
    }
}
