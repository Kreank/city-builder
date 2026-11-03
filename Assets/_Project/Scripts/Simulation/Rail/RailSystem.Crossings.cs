using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;

namespace CityBuilder.Simulation.Rail
{
    /// <summary>
    /// Bahnübergänge: ebenerdige Schnittpunkte Stadtstraße × Gleis (abgeleitet aus beiden Netzen, nicht gespeichert). Die
    /// Schranke schließt, wenn ein Zug in <see cref="RailRules.CrossingWarnSeconds"/> da wäre oder den Übergang belegt, und
    /// öffnet <see cref="RailRules.CrossingOpenDelay"/> s nach dem Zugende. Geschlossen wirkt sie im Verkehr wie eine Haltelinie
    /// (<see cref="TrafficSystem.BarrierAhead"/>) – Autos, Busse und Lkw warten.
    /// </summary>
    public sealed partial class RailSystem
    {
        readonly List<LevelCrossing> crossings = new List<LevelCrossing>();
        readonly Dictionary<int, List<LevelCrossing>> crossingsByRoad = new Dictionary<int, List<LevelCrossing>>();
        bool crossingsDirty = true;
        int crossingRoadVersion = -1, crossingTrackVersion = -1;

        public IReadOnlyList<LevelCrossing> Crossings { get { EnsureCrossings(); return crossings; } }

        void EnsureCrossings()
        {
            if (!crossingsDirty && crossingRoadVersion == roads.Version && crossingTrackVersion == Tracks.Version) return;
            crossingsDirty = false;
            crossingRoadVersion = roads.Version;
            crossingTrackVersion = Tracks.Version;
            // Zustand (geschlossen) über die Neuberechnung retten, damit Schranken nicht flackern
            var closed = new List<(Vector2 p, bool closed, double openAt, int count)>();
            foreach (var c in crossings) closed.Add((c.Position, c.Closed, c.OpenAt, c.ClosuresToday));
            crossings.Clear();
            crossingsByRoad.Clear();
            if (Tracks.SegmentCount == 0) return;
            foreach (var rail in Tracks.Segments)
            {
                Bezier.Bounds(rail.Start.Position, rail.Control, rail.End.Position, out var rlo, out var rhi);
                int m = Math.Clamp((int)MathF.Ceiling(rail.Length / 3f), 8, 256);
                var rp = new Vector2[m + 1];
                for (int j = 0; j <= m; j++) rp[j] = rail.Evaluate(j / (float)m);
                foreach (var road in roads.Segments)
                {
                    if (road.Type.Category != RoadCategory.City) continue;
                    Bezier.Bounds(road.Start.Position, road.Control, road.End.Position, out var lo, out var hi);
                    if (hi.X < rlo.X - 1f || hi.Y < rlo.Y - 1f || lo.X > rhi.X + 1f || lo.Y > rhi.Y + 1f) continue;
                    int n = Math.Clamp((int)MathF.Ceiling(road.Length / 3f), 8, 256);
                    var prev = road.Start.Position;
                    for (int i = 1; i <= n; i++)
                    {
                        var cur = road.Evaluate(i / (float)n);
                        for (int j = 0; j < m; j++)
                        {
                            if (!Bezier.LineSegmentsIntersect(prev, cur, rp[j], rp[j + 1], out float u, out float v)) continue;
                            float tRoad = (i - 1 + u) / n, tRail = (j + v) / m;
                            if (MathF.Abs(road.HeightAt(tRoad) - rail.HeightAt(tRail)) > RoadNetwork.SameLevelTolerance) continue;
                            float angle = RoadGeometry.Angle(road.Tangent(tRoad), rail.Tangent(tRail));
                            if (angle > 90f) angle = 180f - angle;
                            float sin = MathF.Max(0.3f, MathF.Sin(angle * MathF.PI / 180f));
                            var c = new LevelCrossing
                            {
                                RoadSegmentId = road.Id, RoadT = tRoad, RailSegmentId = rail.Id, RailT = tRail,
                                Position = Vector2.Lerp(prev, cur, u), Height = road.HeightAt(tRoad), Angle = angle,
                                HalfSpan = (rail.Type.HalfWidth + RailRules.StopLineGap) / sin,
                            };
                            foreach (var old in closed)
                                if (Vector2.Distance(old.p, c.Position) < 2f) { c.Closed = old.closed; c.OpenAt = old.openAt; c.ClosuresToday = old.count; }
                            crossings.Add(c);
                            if (!crossingsByRoad.TryGetValue(road.Id, out var list)) crossingsByRoad[road.Id] = list = new List<LevelCrossing>();
                            list.Add(c);
                        }
                        prev = cur;
                    }
                }
            }
        }

        /// <summary>Bahnübergänge einer Straße (für Verkehr/Darstellung).</summary>
        public IReadOnlyList<LevelCrossing> CrossingsOnRoad(int roadSegmentId)
        {
            EnsureCrossings();
            return crossingsByRoad.TryGetValue(roadSegmentId, out var l) ? l : (IReadOnlyList<LevelCrossing>)Array.Empty<LevelCrossing>();
        }

        void UpdateCrossings(double now)
        {
            foreach (var c in crossings)
            {
                bool needed = false;
                foreach (var t in trains)
                {
                    if (t.State == TrainState.Waiting) continue;
                    float d = RouteDistance(t, c.RailSegmentId, c.RailT, Math.Max(0, t.ReleasedBefore - 1));
                    if (d < 0f) continue;
                    float ahead = d - t.HeadS;
                    if (ahead >= -t.Length - 8f && ahead <= 8f) { needed = true; break; }                 // Zug auf dem Übergang
                    if (ahead > 0f && ahead <= t.Speed * RailRules.CrossingWarnSeconds + RailRules.CrossingWarnMeters) { needed = true; break; }
                }
                if (needed)
                {
                    if (!c.Closed) c.ClosuresToday++;
                    c.Closed = true;
                    c.OpenAt = now + RailRules.CrossingOpenDelay;
                }
                else if (c.Closed && now >= c.OpenAt) c.Closed = false;
            }
        }

        /// <summary>
        /// Hook für den Verkehr: Strecke (auf der Spur) der Haltelinie vor dem nächsten geschlossenen Bahnübergang, den das
        /// Fahrzeug noch nicht passiert hat; −1 = keiner.
        /// </summary>
        float BarrierAhead(Lane lane, float s)
        {
            if (crossings.Count == 0 || !crossingsByRoad.TryGetValue(lane.Segment.Id, out var list)) return -1f;
            float best = -1f;
            foreach (var c in list)
            {
                if (!c.Closed) continue;
                float sc = lane.Along ? c.RoadT * lane.Length : (1f - c.RoadT) * lane.Length;
                float stop = sc - c.HalfSpan;
                if (stop < s - 0.5f) continue; // schon an der Haltelinie vorbei
                if (best < 0f || stop < best) best = stop;
            }
            return best;
        }
    }
}
