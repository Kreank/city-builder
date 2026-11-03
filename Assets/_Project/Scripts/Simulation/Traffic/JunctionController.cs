using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Traffic
{
    public enum LightState { Green, Yellow, Red }

    /// <summary>Ausgewertete Regeln eines Knotens (zwischengespeichert, bis sich Netz oder Regeln ändern).</summary>
    public sealed class JunctionInfo
    {
        public RoadNode Node;
        /// <summary>Tatsächlich wirksame Steuerung (Auto ist hier bereits aufgelöst, außer bei Ampeln).</summary>
        public JunctionControl Control;
        /// <summary>Zufahrten (Segment-Ids mit Spuren, die hier enden).</summary>
        public readonly List<int> Approaches = new List<int>();
        public readonly Dictionary<int, List<Lane>> IncomingLanes = new Dictionary<int, List<Lane>>();
        public readonly Dictionary<int, Vector2> InDirection = new Dictionary<int, Vector2>();
        /// <summary>Vorrang: 2 = Vorfahrt, 1 = rechts vor links, 0 = wartepflichtig.</summary>
        public readonly Dictionary<int, int> Rank = new Dictionary<int, int>();
        public readonly Dictionary<int, ApproachSign> Sign = new Dictionary<int, ApproachSign>();
        public readonly List<List<int>> Phases = new List<List<int>>();
        public float GreenSeconds = 20f;
        public float PhaseOffset;

        public int RankOf(int segmentId) => Rank.TryGetValue(segmentId, out int r) ? r : 1;
        public ApproachSign SignOf(int segmentId) => Sign.TryGetValue(segmentId, out var s) ? s : ApproachSign.None;
        public bool IsJunction => Approaches.Count >= 2;

        /// <summary>Kommt Zufahrt <paramref name="other"/> von rechts (aus Sicht von <paramref name="mine"/>)?</summary>
        public bool FromRight(int mine, int other)
        {
            float a = RoadGeometry.SignedAngle(InDirection[mine], InDirection[other]);
            return a > 30f && a < 150f;
        }
    }

    /// <summary>
    /// Wertet Kreuzungsregeln aus: automatische Vorfahrt, Schilder, rechts vor links und Ampelphasen.
    /// </summary>
    public sealed class JunctionController
    {
        public const float YellowSeconds = 3f;

        readonly RoadNetwork network;
        readonly LaneGraph lanes;
        readonly TrafficRules rules;
        readonly Dictionary<int, JunctionInfo> cache = new Dictionary<int, JunctionInfo>();
        int laneVersion = -1, rulesVersion = -1;

        public JunctionController(RoadNetwork network, LaneGraph lanes, TrafficRules rules)
        {
            this.network = network;
            this.lanes = lanes;
            this.rules = rules;
        }

        public JunctionInfo Get(RoadNode node)
        {
            if (lanes.Version != laneVersion || rules.Version != rulesVersion)
            {
                cache.Clear();
                laneVersion = lanes.Version;
                rulesVersion = rules.Version;
            }
            if (!cache.TryGetValue(node.Id, out var info))
            {
                info = Build(node);
                cache[node.Id] = info;
            }
            return info;
        }

        public LightState LightFor(JunctionInfo j, int approach, double time)
        {
            if (j.Control != JunctionControl.TrafficLights || j.Phases.Count == 0) return LightState.Green;
            float cycle = j.Phases.Count * j.GreenSeconds;
            float t = (float)((time + j.PhaseOffset) % cycle);
            int phase = (int)(t / j.GreenSeconds);
            if (phase >= j.Phases.Count) phase = j.Phases.Count - 1;
            if (!j.Phases[phase].Contains(approach)) return LightState.Red;
            return t - phase * j.GreenSeconds > j.GreenSeconds - YellowSeconds ? LightState.Yellow : LightState.Green;
        }

        JunctionInfo Build(RoadNode node)
        {
            var info = new JunctionInfo { Node = node, PhaseOffset = (node.Id * 7.31f) % 60f };
            foreach (var lane in lanes.IncomingLanes(node))
            {
                int id = lane.Segment.Id;
                if (!info.IncomingLanes.TryGetValue(id, out var list))
                {
                    info.IncomingLanes[id] = list = new List<Lane>();
                    info.Approaches.Add(id);
                    lane.Sample(lane.Length, 0f, out _, out _, out var dir);
                    info.InDirection[id] = dir;
                }
                list.Add(lane);
            }

            var nodeRules = rules.GetNode(node.Id);
            var control = nodeRules?.Control ?? JunctionControl.Auto;
            info.GreenSeconds = nodeRules?.GreenSeconds ?? 20f;
            if (!info.IsJunction) { info.Control = JunctionControl.Auto; return info; }

            var auto = AutoRanks(info);
            switch (control)
            {
                case JunctionControl.TrafficLights:
                    info.Control = JunctionControl.TrafficLights;
                    foreach (int a in info.Approaches) info.Rank[a] = 1;
                    BuildPhases(info);
                    break;
                case JunctionControl.RightBeforeLeft:
                    info.Control = JunctionControl.RightBeforeLeft;
                    foreach (int a in info.Approaches) info.Rank[a] = 1;
                    break;
                case JunctionControl.Signs:
                    info.Control = JunctionControl.Signs;
                    foreach (int a in info.Approaches)
                    {
                        var sign = rules.SignAt(node.Id, a);
                        if (sign == ApproachSign.None) sign = auto[a] == 2 ? ApproachSign.Priority : auto[a] == 0 ? ApproachSign.Yield : ApproachSign.None;
                        info.Sign[a] = sign;
                        info.Rank[a] = sign == ApproachSign.Priority ? 2 : sign == ApproachSign.None ? 1 : 0;
                    }
                    break;
                default:
                    info.Control = JunctionControl.Auto;
                    foreach (int a in info.Approaches)
                    {
                        info.Rank[a] = auto[a];
                        info.Sign[a] = auto[a] == 2 ? ApproachSign.Priority : auto[a] == 0 ? ApproachSign.Yield : ApproachSign.None;
                    }
                    break;
            }
            return info;
        }

        /// <summary>
        /// Automatische Vorfahrt: Autobahn vor Rampe, Kreisverkehr vor Zufahrt, größere Straße vor kleinerer,
        /// sonst rechts vor links.
        /// </summary>
        Dictionary<int, int> AutoRanks(JunctionInfo info)
        {
            var result = new Dictionary<int, int>();
            var segs = info.Approaches.Select(id => network.GetSegment(id)).ToList();

            if (segs.Any(s => s.Type.IsHighway))
            {
                foreach (var s in segs) result[s.Id] = s.Type.IsHighway ? 2 : 0;
                return result;
            }
            if (segs.Any(s => s.IsRoundabout))
            {
                foreach (var s in segs) result[s.Id] = s.IsRoundabout ? 2 : 0;
                return result;
            }
            float Importance(RoadSegment s) => (s.Type.CarLanesForward + s.Type.CarLanesBackward) * 10f + s.Type.SpeedLimitKmh / 10f
                + (s.Type.Category == RoadCategory.Ramp ? -100f : 0f);
            float max = segs.Max(Importance);
            bool allEqual = segs.All(s => Math.Abs(Importance(s) - max) < 0.01f);
            foreach (var s in segs) result[s.Id] = allEqual ? 1 : Math.Abs(Importance(s) - max) < 0.01f ? 2 : 0;
            return result;
        }

        /// <summary>Ampelphasen: gegenüberliegende Zufahrten bekommen gemeinsam Grün.</summary>
        static void BuildPhases(JunctionInfo info)
        {
            var left = new List<int>(info.Approaches);
            while (left.Count > 0)
            {
                int a = left[0];
                left.RemoveAt(0);
                var phase = new List<int> { a };
                int partner = -1;
                float bestAngle = 135f;
                foreach (int b in left)
                {
                    float angle = RoadGeometry.Angle(info.InDirection[a], info.InDirection[b]);
                    if (angle > bestAngle) { bestAngle = angle; partner = b; }
                }
                if (partner >= 0) { phase.Add(partner); left.Remove(partner); }
                info.Phases.Add(phase);
            }
        }
    }
}
