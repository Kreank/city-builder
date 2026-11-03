using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Traffic;

namespace CityBuilder.Simulation.Rail
{
    /// <summary>
    /// Bahnhöfe: Personenbahnhof (Empfangsgebäude mit Straßenfront, Bahnsteige an den Gleisen dahinter) und Güterbahnhof
    /// (Umschlaghalle, Ladegleise). Rasten am nächsten Gleis ein (Rückseite parallel zum Gleis, Bahnsteig dazwischen), die
    /// Vorderseite zeigt vom Gleis weg zur Straße. In Betrieb, wenn eine Stadtstraße vor der Vorderseite liegt
    /// (<see cref="Station.HasAccess"/>) und ein Bahnsteiggleis auf einem Fahrweg der Hauptstrecke liegt (<see cref="Station.OnMainline"/>).
    /// </summary>
    public sealed partial class RailSystem
    {
        public Station GetStation(int id) { foreach (var s in stations) if (s.Id == id) return s; return null; }

        public Station StationAt(Vector2 p, float margin = 0f)
        {
            foreach (var s in stations) if (s.Contains(p, margin)) return s;
            return null;
        }

        bool StationOverlaps(Vector2 c, Vector2 f, float w, float d, int exceptId)
        {
            foreach (var s in stations)
            {
                if (s.Id == exceptId) continue;
                if (ServiceGeometry.RectsOverlap(c, f, w, d, s.Position, s.Front, s.Type.Width, s.Type.Depth)) return true;
                var pa = s.PlatformArea;
                if (ServiceGeometry.RectsOverlap(c, f, w, d, pa.Center, pa.Front, pa.Width, pa.Depth)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ Planen/Bauen

        /// <summary>
        /// Bahnhof planen: rastet am nächsten ebenerdigen Gleis im Umkreis <see cref="RailRules.StationSnapRadius"/> ein
        /// (Rückseite <see cref="RailRules.PlatformGap"/> hinter der Gleismitte, parallel, mittig zum Mauszeiger).
        /// </summary>
        public StationPlan PlanStation(StationType type, Vector2 cursor, int movingId = 0)
        {
            var plan = new StationPlan { Type = type, Position = cursor, Front = Vector2.UnitY, Cost = movingId != 0 ? type.Cost * 0.1 : type.Cost, MovingId = movingId };
            RoadSegment best = null;
            float bestD = RailRules.StationSnapRadius, bestT = 0f;
            foreach (var s in Tracks.Segments)
            {
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
                float r = bestD + s.Type.HalfWidth;
                if (cursor.X < lo.X - r || cursor.Y < lo.Y - r || cursor.X > hi.X + r || cursor.Y > hi.Y + r) continue;
                float t = s.ClosestT(cursor);
                if (MathF.Abs(s.ElevationAt(t)) > RoadFront.AtGradeElevation) continue;
                float d = Vector2.Distance(s.Evaluate(t), cursor);
                if (d < bestD - 1e-4f || (MathF.Abs(d - bestD) <= 1e-4f && best != null && s.Id < best.Id)) { bestD = d; best = s; bestT = t; }
            }
            if (best == null) { plan.Error = StationBuildError.NoTrack; return plan; }
            var q = best.Evaluate(bestT);
            var tan = best.Tangent(bestT);
            var normal = new Vector2(-tan.Y, tan.X);
            if (Vector2.Dot(cursor - q, normal) < 0f) normal = -normal;
            plan.Front = normal;
            plan.Position = q + normal * (RailRules.PlatformGap + type.Depth * 0.5f);
            plan.Height = best.HeightAt(bestT);
            plan.Error = ValidateStation(plan);
            return plan;
        }

        StationBuildError ValidateStation(StationPlan plan)
        {
            var type = plan.Type;
            var fp = new Footprint(plan.Position, plan.Front, type.Width, type.Depth);
            foreach (var c in fp.Corners())
                if (!world.Contains(c)) return StationBuildError.OutOfWorld;
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i <= 4; i++)
                for (int j = 0; j <= 4; j++)
                {
                    var p = plan.Position + fp.Side * (type.Width * (i / 4f - 0.5f)) + plan.Front * (type.Depth * (j / 4f - 0.5f));
                    if (terrain.IsWater(p)) return StationBuildError.OnWater;
                    float h = terrain.HeightAt(p);
                    lo = MathF.Min(lo, h); hi = MathF.Max(hi, h);
                }
            if (hi - lo > RailRules.StationMaxSlope || MathF.Abs(plan.Height - (lo + hi) * 0.5f) > RailRules.StationMaxSlope) return StationBuildError.TooSteep;
            foreach (var s in roads.Segments)
                if (ServiceGeometry.RoadCrossesRect(s, plan.Position, plan.Front, type.Width, type.Depth, -0.5f)) return StationBuildError.OverlapsRoad;
            foreach (var s in Tracks.Segments)
                if (ServiceGeometry.RoadCrossesRect(s, plan.Position, plan.Front, type.Width, type.Depth, -0.5f)) return StationBuildError.OverlapsTrack;
            foreach (var b in roads.Structures)
            {
                if (b.Kind == StationBlockKind && Math.Abs(b.Id) == plan.MovingId) continue;
                var o = b.Footprint;
                if (ServiceGeometry.RectsOverlap(plan.Position, plan.Front, type.Width, type.Depth, o.Center, o.Front, o.Width, o.Depth))
                {
                    plan.BlockedBy = b;
                    return StationBuildError.OverlapsStructure;
                }
            }
            plan.PlatformTracks = CountPlatformTracks(plan.Position, plan.Front, type);
            if (plan.PlatformTracks == 0) return StationBuildError.NoTrack;
            plan.HasRoadAccess = RoadFront.FindAccess(roads, fp, RailRules.StationRoadReach, out _, out _);
            if (!Affordable(plan.Cost)) return StationBuildError.InsufficientFunds;
            return StationBuildError.None;
        }

        public Station BuildStation(StationPlan plan)
        {
            if (plan == null || !plan.IsValid || plan.MovingId != 0) return null;
            if (!Affordable(plan.Cost)) return null;
            Budget?.Spend(Economy.BudgetCategory.RailConstruction, plan.Cost);
            var s = new Station { Id = nextStationId++, Type = plan.Type, Position = plan.Position, Front = plan.Front, Height = plan.Height };
            s.Name = StationName(s);
            stations.Add(s);
            PrepareSite(s);
            platformsDirty = true;
            EnsurePlatforms();
            Version++;
            return s;
        }

        /// <summary>Kurzform für Werkzeuge/Tests.</summary>
        public Station TryBuildStation(StationType type, Vector2 cursor) => BuildStation(PlanStation(type, cursor));

        /// <summary>Spieler-Abriss (25 % zurück): Wartende gehen anders weiter, Fahrgäste im Zug fahren durch.</summary>
        public bool DemolishStation(int id)
        {
            var s = GetStation(id);
            if (s == null) return false;
            foreach (var p in s.Waiting.ToArray()) Finish(p, RailRideResult.GaveUp, GiveUpTicks(p));
            foreach (var p in outsideWaiting.ToArray()) if (p.StationId == id) Finish(p, RailRideResult.GaveUp, GiveUpTicks(p));
            foreach (var t in trains)
            {
                foreach (var p in t.Riders.ToArray()) if (!p.Outbound && p.StationId == id) { t.Riders.Remove(p); Finish(p, RailRideResult.GaveUp, GiveUpTicks(p)); }
                for (int k = t.Stops.Count - 1; k >= t.NextStop; k--) if (t.Stops[k].station == id) t.Stops.RemoveAt(k);
            }
            stations.Remove(s);
            Budget?.Refund(s.Type.Cost * Economy.EconomyRules.DemolitionRefundShare);
            platformsDirty = true;
            Version++;
            return true;
        }

        void PrepareSite(Station s)
        {
            terrain.FlattenArea(s.Position, s.Front, s.Type.Width, s.Type.Depth, s.Height);
            var city = world.City;
            if (city == null) return;
            var remove = new List<Zoning.Building>();
            foreach (var b in city.Buildings)
            {
                if (b.IsPlayerPlaced) continue;
                if (ServiceGeometry.RectsOverlap(s.Position, s.Front, s.Type.Width, s.Type.Depth, b.Position, b.Front, b.Width, b.Depth)) remove.Add(b);
            }
            foreach (var b in remove) city.RemoveBuilding(b);
        }

        string StationName(Station s)
        {
            string city = world.Realm?.CityName ?? "Neustadt";
            string baseName = s.IsPassenger ? city : "Güterbahnhof " + city;
            int same = 0;
            foreach (var o in stations) if (o.Type.Kind == s.Type.Kind) same++;
            if (same == 0) return s.IsPassenger ? city + " Hbf" : baseName;
            string compass = Gateways.GatewayRules.Compass(s.Position);
            return baseName + " " + compass;
        }

        // ------------------------------------------------------------ Bahnsteige, Zufahrt, Anbindung (abgeleitet)

        bool platformsDirty = true;
        int platformTrackVersion = -1, platformRoadVersion = -1;

        void EnsurePlatforms()
        {
            if (!platformsDirty && platformTrackVersion == Tracks.Version && platformRoadVersion == roads.Version) return;
            platformsDirty = false;
            platformTrackVersion = Tracks.Version;
            platformRoadVersion = roads.Version;
            EnsureGraph();
            var reach = MainlineEdges();
            foreach (var s in stations)
            {
                FindPlatforms(s);
                s.OnMainline = false;
                foreach (var p in s.PlatformList)
                    if (reach.Contains(new EdgeKey(p.SegmentId, true)) || reach.Contains(new EdgeKey(p.SegmentId, false))) s.OnMainline = true;
                var fp = s.Footprint;
                if (RoadFront.FindAccess(roads, fp, RailRules.StationRoadReach, out int seg, out float t))
                {
                    s.AccessSegmentId = seg;
                    s.AccessT = t;
                    var road = roads.GetSegment(seg);
                    s.AccessNodeId = Vector2.Distance(road.Start.Position, fp.FrontMid) <= Vector2.Distance(road.End.Position, fp.FrontMid) ? road.Start.Id : road.End.Id;
                }
                else { s.AccessSegmentId = -1; s.AccessNodeId = 0; }
            }
            Version++;
        }

        /// <summary>Gleise hinter einer Rückseite, parallel, mittig über ≥ 60 % der Bahnsteiglänge.</summary>
        int CountPlatformTracks(Vector2 center, Vector2 front, StationType type) => CollectPlatforms(center, front, type, null);

        void FindPlatforms(Station s)
        {
            s.PlatformList.Clear();
            CollectPlatforms(s.Position, s.Front, s.Type, s.PlatformList);
        }

        int CollectPlatforms(Vector2 center, Vector2 front, StationType type, List<Platform> into)
        {
            var side = new Vector2(-front.Y, front.X);
            var back = center - front * (type.Depth * 0.5f);
            var found = new List<Platform>();
            foreach (var seg in Tracks.Segments)
            {
                Bezier.Bounds(seg.Start.Position, seg.Control, seg.End.Position, out var lo, out var hi);
                float r = RailRules.PlatformMaxOffset + 2f;
                if (back.X < lo.X - r || back.Y < lo.Y - r || back.X > hi.X + r || back.Y > hi.Y + r) continue;
                float t = seg.ClosestT(back);
                var q = seg.Evaluate(t);
                float offset = Vector2.Dot(back - q, front);
                if (offset < RailRules.PlatformMinOffset || offset > RailRules.PlatformMaxOffset) continue;
                if (MathF.Abs(Vector2.Dot(q - back, side)) > 3f) continue; // Gleis liegt nicht mittig hinter dem Gebäude
                if (MathF.Abs(Vector2.Dot(seg.Tangent(t), front)) > 0.17f) continue; // nicht parallel (> 10°)
                if (MathF.Abs(seg.ElevationAt(t)) > RoadFront.AtGradeElevation) continue;
                bool dup = false;
                foreach (var f in found) if (MathF.Abs(f.Offset - offset) < 1.5f) { dup = true; break; }
                if (dup) continue;
                // Gleis läuft über den Großteil der Bahnsteiglänge weiter (beide Enden)
                float half = type.PlatformLength * 0.3f;
                if (!TrackNearPoint(back + side * half - front * offset, 1.5f) || !TrackNearPoint(back - side * half - front * offset, 1.5f)) continue;
                found.Add(new Platform { SegmentId = seg.Id, T = t, Offset = offset, Position = q });
            }
            found.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            int n = Math.Min(found.Count, RailRules.MaxPlatforms);
            if (into != null)
                for (int i = 0; i < n; i++) { found[i].Number = i + 1; into.Add(found[i]); }
            return n;
        }

        bool TrackNearPoint(Vector2 p, float tolerance)
        {
            foreach (var s in Tracks.Segments)
            {
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
                if (p.X < lo.X - tolerance || p.Y < lo.Y - tolerance || p.X > hi.X + tolerance || p.Y > hi.Y + tolerance) continue;
                if (s.DistanceTo(p) <= tolerance) return true;
            }
            return false;
        }

        /// <summary>
        /// Gerichtete Gleisstücke, die auf einem Fahrweg von einer Einfahrt der Hauptstrecke zu einer Ausfahrt liegen
        /// (vorwärts erreichbar und von dort weiter zur Ausfahrt).
        /// </summary>
        HashSet<EdgeKey> MainlineEdges()
        {
            var result = new HashSet<EdgeKey>();
            if (!HasMainline) return result;
            var fwd = new HashSet<EdgeKey>();
            var queue = new Queue<EdgeKey>();
            foreach (int n in new[] { Track1StartNode, Track2StartNode })
            {
                var e = EntryEdge(n);
                if (e.HasValue && fwd.Add(e.Value)) queue.Enqueue(e.Value);
            }
            while (queue.Count > 0)
            {
                var e = queue.Dequeue();
                if (!next.TryGetValue(e, out var l)) continue;
                foreach (var x in l) if (fwd.Add(x)) queue.Enqueue(x);
            }
            // rückwärts von den Ausfahrten
            var reverse = new Dictionary<EdgeKey, List<EdgeKey>>();
            foreach (var kv in next)
                foreach (var x in kv.Value)
                {
                    if (!reverse.TryGetValue(x, out var l)) reverse[x] = l = new List<EdgeKey>();
                    l.Add(kv.Key);
                }
            var back = new HashSet<EdgeKey>();
            foreach (int n in new[] { Track1EndNode, Track2EndNode })
            {
                var node = Tracks.GetNode(n);
                if (node == null) continue;
                foreach (int id in node.SegmentIds)
                {
                    var s = Tracks.GetSegment(id);
                    var e = new EdgeKey(id, s.End == node);
                    if (back.Add(e)) queue.Enqueue(e);
                }
            }
            while (queue.Count > 0)
            {
                var e = queue.Dequeue();
                if (!reverse.TryGetValue(e, out var l)) continue;
                foreach (var x in l) if (back.Add(x)) queue.Enqueue(x);
            }
            foreach (var e in fwd) if (back.Contains(e)) result.Add(e);
            return result;
        }
    }
}
