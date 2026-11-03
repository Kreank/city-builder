using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Traffic
{
    public enum JunctionControl
    {
        /// <summary>Automatisch: Autobahn/Kreisverkehr/größere Straße hat Vorfahrt, sonst rechts vor links.</summary>
        Auto,
        RightBeforeLeft,
        /// <summary>Vorfahrt über Schilder je Zufahrt.</summary>
        Signs,
        TrafficLights,
    }

    public enum ApproachSign
    {
        None,
        /// <summary>Vorfahrtstraße.</summary>
        Priority,
        /// <summary>Vorfahrt gewähren.</summary>
        Yield,
        Stop,
    }

    public sealed class NodeRules
    {
        public JunctionControl Control = JunctionControl.Auto;
        /// <summary>Schild je Zufahrt (Segment-Id), nur bei <see cref="JunctionControl.Signs"/>.</summary>
        public readonly Dictionary<int, ApproachSign> Signs = new Dictionary<int, ApproachSign>();
        /// <summary>Abbiegeverbote: (von Segment, nach Segment) → gesperrte Fahrzeugklassen.</summary>
        public readonly Dictionary<(int from, int to), VehicleClass> BannedTurns = new Dictionary<(int, int), VehicleClass>();
        /// <summary>Grünzeit je Ampelphase in Sekunden.</summary>
        public float GreenSeconds = 20f;

        public bool IsEmpty => Control == JunctionControl.Auto && Signs.Count == 0 && BannedTurns.Count == 0 && Math.Abs(GreenSeconds - 20f) < 0.01f;
    }

    public sealed class SegmentRules
    {
        /// <summary>Straße gesperrt (Umleitung erzwingen).</summary>
        public bool Closed;
        /// <summary>Fahrverbot für diese Klassen (z. B. Lkw-Verbot).</summary>
        public VehicleClass Banned;
        /// <summary>Tempolimit in km/h, 0 = Standard des Straßentyps.</summary>
        public float SpeedLimitKmh;

        public bool IsEmpty => !Closed && Banned == VehicleClass.None && SpeedLimitKmh <= 0f;
    }

    /// <summary>
    /// Alle vom Spieler gesetzten Verkehrsregeln. Übernimmt Regeln automatisch, wenn Segmente
    /// zerteilt, umgebaut oder umgedreht werden.
    /// </summary>
    public sealed class TrafficRules
    {
        readonly RoadNetwork network;
        readonly Dictionary<int, NodeRules> nodes = new Dictionary<int, NodeRules>();
        readonly Dictionary<int, SegmentRules> segments = new Dictionary<int, SegmentRules>();

        /// <summary>Wird bei jeder Regeländerung erhöht (Caches, Wegfindung).</summary>
        public int Version { get; private set; }

        public TrafficRules(RoadNetwork network)
        {
            this.network = network;
            network.SegmentReplaced += OnSegmentReplaced;
            network.NodeRemoved += n => { if (nodes.Remove(n.Id)) Version++; };
        }

        // ------------------------------------------------------------ Lesen

        public NodeRules GetNode(int nodeId) => nodes.TryGetValue(nodeId, out var r) ? r : null;
        public SegmentRules GetSegment(int segmentId) => segments.TryGetValue(segmentId, out var r) ? r : null;

        public JunctionControl ControlAt(int nodeId) => GetNode(nodeId)?.Control ?? JunctionControl.Auto;

        public ApproachSign SignAt(int nodeId, int segmentId)
        {
            var r = GetNode(nodeId);
            return r != null && r.Signs.TryGetValue(segmentId, out var s) ? s : ApproachSign.None;
        }

        public bool IsTurnAllowed(int nodeId, int fromSegment, int toSegment, VehicleClass cls)
        {
            var r = GetNode(nodeId);
            if (r == null || !r.BannedTurns.TryGetValue((fromSegment, toSegment), out var banned)) return true;
            return (banned & cls) == 0;
        }

        public bool IsSegmentAllowed(int segmentId, VehicleClass cls)
        {
            var r = GetSegment(segmentId);
            return r == null || (!r.Closed && (r.Banned & cls) == 0);
        }

        public float SpeedLimit(RoadSegment segment)
        {
            var r = GetSegment(segment.Id);
            float kmh = r != null && r.SpeedLimitKmh > 0f ? r.SpeedLimitKmh : segment.Type.SpeedLimitKmh;
            return kmh / 3.6f;
        }

        // ------------------------------------------------------------ Schreiben (Spieleraktionen)

        public void SetControl(int nodeId, JunctionControl control)
        {
            EditNode(nodeId).Control = control;
            Changed();
        }

        public void SetSign(int nodeId, int segmentId, ApproachSign sign)
        {
            var r = EditNode(nodeId);
            if (sign == ApproachSign.None) r.Signs.Remove(segmentId);
            else r.Signs[segmentId] = sign;
            Changed();
        }

        public void SetGreenSeconds(int nodeId, float seconds)
        {
            EditNode(nodeId).GreenSeconds = Math.Clamp(seconds, 5f, 90f);
            Changed();
        }

        public void SetTurnBan(int nodeId, int fromSegment, int toSegment, VehicleClass banned)
        {
            var r = EditNode(nodeId);
            if (banned == VehicleClass.None) r.BannedTurns.Remove((fromSegment, toSegment));
            else r.BannedTurns[(fromSegment, toSegment)] = banned;
            Changed();
        }

        public VehicleClass GetTurnBan(int nodeId, int fromSegment, int toSegment)
        {
            var r = GetNode(nodeId);
            return r != null && r.BannedTurns.TryGetValue((fromSegment, toSegment), out var b) ? b : VehicleClass.None;
        }

        public void SetClosed(int segmentId, bool closed) { EditSegment(segmentId).Closed = closed; Changed(); }
        public void SetBanned(int segmentId, VehicleClass banned) { EditSegment(segmentId).Banned = banned; Changed(); }
        public void SetSpeedLimit(int segmentId, float kmh) { EditSegment(segmentId).SpeedLimitKmh = Math.Max(0f, kmh); Changed(); }

        NodeRules EditNode(int id)
        {
            if (!nodes.TryGetValue(id, out var r)) nodes[id] = r = new NodeRules();
            return r;
        }

        SegmentRules EditSegment(int id)
        {
            if (!segments.TryGetValue(id, out var r)) segments[id] = r = new SegmentRules();
            return r;
        }

        void Changed() => Version++;

        // ------------------------------------------------------------ Regeln mitnehmen

        void OnSegmentReplaced(RoadSegment old, IReadOnlyList<RoadSegment> replacements)
        {
            bool changed = false;
            if (segments.TryGetValue(old.Id, out var segRules))
            {
                segments.Remove(old.Id);
                foreach (var r in replacements)
                    segments[r.Id] = new SegmentRules { Closed = segRules.Closed, Banned = segRules.Banned, SpeedLimitKmh = segRules.SpeedLimitKmh };
                changed = true;
            }

            // Knotenregeln (Schilder/Abbiegeverbote) auf das Nachfolgesegment am selben Knoten umschreiben
            foreach (var node in new[] { old.Start, old.End })
            {
                if (!nodes.TryGetValue(node.Id, out var nr)) continue;
                RoadSegment successor = null;
                foreach (var r in replacements) if (r.Touches(node)) { successor = r; break; }
                if (successor == null) continue;

                if (nr.Signs.TryGetValue(old.Id, out var sign))
                {
                    nr.Signs.Remove(old.Id);
                    nr.Signs[successor.Id] = sign;
                    changed = true;
                }
                var moved = new List<KeyValuePair<(int, int), VehicleClass>>();
                foreach (var kv in nr.BannedTurns)
                    if (kv.Key.from == old.Id || kv.Key.to == old.Id) moved.Add(kv);
                foreach (var kv in moved)
                {
                    nr.BannedTurns.Remove(kv.Key);
                    int from = kv.Key.Item1 == old.Id ? successor.Id : kv.Key.Item1;
                    int to = kv.Key.Item2 == old.Id ? successor.Id : kv.Key.Item2;
                    nr.BannedTurns[(from, to)] = kv.Value;
                    changed = true;
                }
            }
            if (changed) Changed();
        }
    }
}
