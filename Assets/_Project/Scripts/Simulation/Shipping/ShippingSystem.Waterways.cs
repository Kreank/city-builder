using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Shipping
{
    /// <summary>
    /// Fahrrinnen (abgeleitet, nicht gespeichert): Durchgangsrinnen zwischen den schiffbaren Wasserläufen am Kartenrand (beim
    /// Erzeugen der Welt, aus dem natürlichen Gelände) und je Hafen eine Rinne vom Wasser-Gateway zum Anfahrpunkt vor dem Kai.
    /// Daraus: verlangte Mindesthöhe von Brücken über der Rinne (Trassierung), Terraforming-Sperre, Brücken-Querungen mit
    /// lichter Höhe (fest/Klappbrücke) und der Hinweis im Werkzeug.
    /// </summary>
    public sealed partial class ShippingSystem
    {
        readonly List<Waterway> waterways = new List<Waterway>();
        readonly List<Waterway> trunks = new List<Waterway>();
        readonly Dictionary<int, List<Waterway>> harborWaterways = new Dictionary<int, List<Waterway>>();
        /// <summary>Wasserläufe am Kartenrand (Lage, Richtung nach außen, Meer?) – Enden der Rinnen.</summary>
        readonly List<(Vector2 center, Vector2 outward, bool sea, float width, float depth)> edgeRuns = new List<(Vector2, Vector2, bool, float, float)>();
        int nextWaterwayId = 1;
        float[] floorGrid;
        int waterwayVersion;

        public IReadOnlyList<Waterway> Waterways => waterways;
        public int WaterwayVersion => waterwayVersion;

        public Waterway GetWaterway(int id) { foreach (var w in waterways) if (w.Id == id) return w; return null; }

        public IReadOnlyList<Waterway> WaterwaysOf(Harbor h) =>
            h != null && harborWaterways.TryGetValue(h.Id, out var l) ? l : (IReadOnlyList<Waterway>)Array.Empty<Waterway>();

        // ------------------------------------------------------------ Durchgangsrinnen (Kartenstart)

        void BuildTrunks()
        {
            foreach (var run in GatewayGenerator.WaterAtEdges(world))
            {
                bool navigable = run.Sea || (run.Width >= GatewayRules.NavigableWidth && run.Depth >= GatewayRules.NavigableDepth);
                if (navigable) edgeRuns.Add((run.Center, run.Outward, run.Sea, run.Width, run.Depth));
            }
            float minDepth = ShipCatalog.Barge.RequiredDepth;
            for (int i = 0; i < edgeRuns.Count; i++)
            {
                int src = Grid.NearestNavigable(edgeRuns[i].center, minDepth, 160f);
                if (src < 0) continue;
                int[] prev = null;
                for (int j = i + 1; j < edgeRuns.Count; j++)
                {
                    if (edgeRuns[i].sea && edgeRuns[j].sea) continue;
                    int dst = Grid.NearestNavigable(edgeRuns[j].center, minDepth, 160f);
                    if (dst < 0) continue;
                    prev ??= Grid.Search(src, minDepth);
                    var cells = Grid.Trace(prev, dst);
                    if (cells == null || cells.Count < 2) continue;
                    var w = MakeWaterway(WaterwayKind.Through, cells, edgeRuns[i], edgeRuns[j].center + edgeRuns[j].outward * ShippingRules.OutsideRun, edgeRuns[j].center);
                    w.Sea = edgeRuns[i].sea && edgeRuns[j].sea;
                    w.EndB = edgeRuns[j].center;
                    trunks.Add(w);
                    waterways.Add(w);
                }
            }
            RebuildFloor();
        }

        /// <summary>
        /// Linienzug: von draußen (Gateway + OutsideRun) über den Rand-Punkt, die geglätteten Rasterzellen bis zum Ende
        /// (<paramref name="endInner"/>, <paramref name="endOuter"/> = optional über den Rand hinaus).
        /// </summary>
        Waterway MakeWaterway(WaterwayKind kind, List<Vector2> cells, (Vector2 center, Vector2 outward, bool sea, float width, float depth) start,
            Vector2? endOuter, Vector2 endInner)
        {
            var pts = new List<Vector2> { start.center + start.outward * ShippingRules.OutsideRun, start.center };
            var smooth = NavGrid.Smooth(cells, 4, ShippingRules.PointSpacing);
            // erste/letzte Rasterpunkte dicht am Rand-Punkt weglassen (kein Zickzack)
            foreach (var p in smooth)
            {
                if (Vector2.Distance(p, start.center) < ShippingRules.PointSpacing * 0.75f) continue;
                if (Vector2.Distance(p, endInner) < ShippingRules.PointSpacing * 0.75f) continue;
                pts.Add(p);
            }
            pts.Add(endInner);
            if (endOuter.HasValue) pts.Add(endOuter.Value);
            var w = new Waterway { Id = nextWaterwayId++, Kind = kind, EndA = start.center, Sea = start.sea };
            SetPoints(w, pts);
            return w;
        }

        void SetPoints(Waterway w, List<Vector2> pts)
        {
            w.Points = pts.ToArray();
            w.Cum = new float[w.Points.Length];
            for (int i = 1; i < w.Points.Length; i++) w.Cum[i] = w.Cum[i - 1] + Vector2.Distance(w.Points[i - 1], w.Points[i]);
            w.Length = w.Cum.Length > 0 ? w.Cum[w.Cum.Length - 1] : 0f;
            Grid.Measure(pts, out w.Depth, out w.Width);
            // Tiefe/Breite nur innerhalb der Karte messen (draußen läuft das Gewässer weiter)
            float minD = float.MaxValue, minW = float.MaxValue;
            for (int i = 0; i < w.Points.Length; i++)
            {
                if (!world.Contains(w.Points[i])) continue;
                if (Vector2.Distance(w.Points[i], w.EndA) < 1f) continue; // Rand-Punkt selbst (Mitte des Laufs am Rand)
                minD = MathF.Min(minD, w.Depth[i]);
                minW = MathF.Min(minW, w.Width[i]);
            }
            w.MinDepth = minD == float.MaxValue ? 0f : minD;
            w.MinWidth = minW == float.MaxValue ? 0f : minW;
        }

        // ------------------------------------------------------------ Gateways zuordnen (über die Lage)

        int linkedGateways = -1;

        void EnsureLinks()
        {
            var gw = world.Gateways;
            if (gw == null || gw.All.Count == linkedGateways) return;
            linkedGateways = gw.All.Count;
            foreach (var w in waterways)
            {
                w.GatewayA = WaterGatewayAt(w.EndA);
                if (w.Kind == WaterwayKind.Through) w.GatewayB = WaterGatewayAt(w.EndB);
            }
        }

        int WaterGatewayAt(Vector2 p)
        {
            var gw = world.Gateways;
            if (gw == null) return 0;
            int best = 0;
            float bestD = 60f;
            foreach (var g in gw.All)
            {
                if (g.Kind != GatewayKind.Water || !g.HasLocation) continue;
                float d = Vector2.Distance(g.Position, p);
                if (d < bestD) { bestD = d; best = g.Id; }
            }
            return best;
        }

        // ------------------------------------------------------------ Hafen-Rinnen

        /// <summary>Rinnen eines Hafens neu suchen (Bau/Laden): je schiffbarem Wasserlauf ein Weg zum Anfahrpunkt.</summary>
        void BuildHarborWaterways(Harbor h)
        {
            RemoveHarborWaterways(h.Id, false);
            var list = new List<Waterway>();
            // Erst mit Seeschiff-Tiefgang (falls der Kai tief genug ist), sonst mit Binnenschiff-Tiefgang
            float berthDepth = BerthDepth(h.Position, h.Front, h.Type);
            var classes = berthDepth >= ShipCatalog.Coaster.RequiredDepth ? new[] { ShipCatalog.Coaster, ShipCatalog.Barge } : new[] { ShipCatalog.Barge };
            ShipClass chosen = ShipCatalog.Barge;
            foreach (var c in classes)
            {
                int src = Grid.NearestNavigable(h.Approach, c.RequiredDepth, 90f);
                if (src < 0) continue;
                var prev = Grid.Search(src, c.RequiredDepth);
                foreach (var run in edgeRuns)
                {
                    if (c == ShipCatalog.Coaster && !run.sea) continue; // Küstenfrachter nur über den Seeweg
                    int dst = Grid.NearestNavigable(run.center, c.RequiredDepth, 160f);
                    var cells = dst >= 0 ? Grid.Trace(prev, dst) : null;
                    if (cells == null) continue;
                    cells.Reverse(); // Gateway → Hafen
                    var w = MakeWaterway(WaterwayKind.Harbor, cells, run, null, h.Approach);
                    w.HarborId = h.Id;
                    list.Add(w);
                }
                if (list.Count > 0) { chosen = c; break; }
            }
            h.ShipClass = chosen;
            harborWaterways[h.Id] = list;
            waterways.AddRange(list);
            linkedGateways = -1;
            EnsureLinks();
            RebuildFloor();
            crossingsDirty = true;
            waterwayVersion++;
        }

        void RemoveHarborWaterways(int harborId, bool rebuild = true)
        {
            if (!harborWaterways.TryGetValue(harborId, out var list)) return;
            foreach (var w in list) waterways.Remove(w);
            harborWaterways.Remove(harborId);
            if (!rebuild) return;
            RebuildFloor();
            crossingsDirty = true;
            waterwayVersion++;
        }

        // ------------------------------------------------------------ Mindesthöhe / Sperre (Raster)

        bool suppressFloor;

        void RebuildFloor()
        {
            int r = Grid.Resolution;
            floorGrid = new float[r * r];
            foreach (var w in waterways)
            {
                float need = w.DesignClearance + ShippingRules.RoadDeck;
                for (int i = 0; i + 1 < w.Points.Length; i++)
                {
                    var a = w.Points[i]; var b = w.Points[i + 1];
                    float half = w.ChannelHalfWidth(w.Cum[i]) + ShippingRules.GridCell;
                    var lo = Vector2.Min(a, b) - new Vector2(half);
                    var hi = Vector2.Max(a, b) + new Vector2(half);
                    Grid.CellOf(lo, out int x0, out int y0);
                    Grid.CellOf(hi, out int x1, out int y1);
                    for (int y = y0; y <= y1; y++)
                        for (int x = x0; x <= x1; x++)
                        {
                            var c = Grid.Center(x, y);
                            if (DistanceToSegment(c, a, b) > half) continue;
                            int k = y * r + x;
                            if (need > floorGrid[k]) floorGrid[k] = need;
                        }
                }
            }
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float len2 = ab.LengthSquared();
            float u = len2 > 1e-6f ? Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
            return Vector2.Distance(p, a + ab * u);
        }

        /// <summary>Hook <c>TerrainMap.NavigationClearance</c>: verlangte Höhe über Wasser an p (0 = keine Fahrrinne).</summary>
        float NavigationFloor(Vector2 p)
        {
            if (suppressFloor || floorGrid == null || !world.Contains(p)) return 0f;
            Grid.CellOf(p, out int x, out int y);
            return floorGrid[y * Grid.Resolution + x];
        }

        /// <summary>Liegt p in einer Fahrrinne (Terraforming gesperrt, Overlay)?</summary>
        public bool InChannel(Vector2 p)
        {
            if (floorGrid == null || !world.Contains(p)) return false;
            Grid.CellOf(p, out int x, out int y);
            return floorGrid[y * Grid.Resolution + x] > 0f && terrain.IsWater(p);
        }

        // ------------------------------------------------------------ Brücken-Querungen

        bool crossingsDirty = true;
        int crossingRoadVersion = -1, crossingTrackVersion = -1, crossingWaterwayVersion = -1;

        void EnsureCrossings()
        {
            if (!Active) return;
            int trackVersion = world.Rail?.Tracks.Version ?? 0;
            if (!crossingsDirty && crossingRoadVersion == roads.Version && crossingTrackVersion == trackVersion && crossingWaterwayVersion == waterwayVersion) return;
            crossingsDirty = false;
            crossingRoadVersion = roads.Version;
            crossingTrackVersion = trackVersion;
            crossingWaterwayVersion = waterwayVersion;
            RebuildDrawbridges();
            foreach (var w in waterways)
            {
                w.CrossingList.Clear();
                Collect(w, roads, false);
                if (world.Rail != null) Collect(w, world.Rail.Tracks, true);
                w.CrossingList.Sort((a, b) => a.S.CompareTo(b.S));
            }
            accessDirty = true;
            Version++;
        }

        void Collect(Waterway w, RoadNetwork net, bool rail)
        {
            if (w.Points.Length < 2) return;
            var wlo = w.Points[0]; var whi = w.Points[0];
            foreach (var p in w.Points) { wlo = Vector2.Min(wlo, p); whi = Vector2.Max(whi, p); }
            foreach (var seg in net.Segments)
            {
                Bezier.Bounds(seg.Start.Position, seg.Control, seg.End.Position, out var lo, out var hi);
                if (hi.X < wlo.X - 2f || hi.Y < wlo.Y - 2f || lo.X > whi.X + 2f || lo.Y > whi.Y + 2f) continue;
                var hit = CrossSegment(w, seg.Start.Position, seg.Control, seg.End.Position, seg.Length, seg.HeightAt, rail);
                if (hit == null) continue;
                hit.SegmentId = seg.Id;
                if (!rail && seg.IsDrawbridge) hit.Bridge = DrawbridgeOf(seg.Id);
                w.CrossingList.Add(hit);
            }
        }

        /// <summary>
        /// Schnitt einer Kurve (Straße/Gleis/Plan) mit der Fahrrinne: Lage, Strecke auf der Rinne, lichte Höhe (tiefster Punkt der
        /// Fahrbahn über der Rinne − Tragwerk). Tunnel unter dem Gewässergrund → null (kein Hindernis).
        /// </summary>
        WaterwayCrossing CrossSegment(Waterway w, Vector2 p0, Vector2 c, Vector2 p2, float length, Func<float, float> heightAt, bool rail)
        {
            int n = Math.Clamp((int)MathF.Ceiling(length / 3f), 8, 256);
            var prev = p0;
            WaterwayCrossing result = null;
            for (int i = 1; i <= n && result == null; i++)
            {
                var cur = Bezier.Evaluate(p0, c, p2, i / (float)n);
                for (int j = 0; j + 1 < w.Points.Length; j++)
                {
                    var a = w.Points[j]; var b = w.Points[j + 1];
                    if (MathF.Max(prev.X, cur.X) < MathF.Min(a.X, b.X) || MathF.Min(prev.X, cur.X) > MathF.Max(a.X, b.X)) continue;
                    if (MathF.Max(prev.Y, cur.Y) < MathF.Min(a.Y, b.Y) || MathF.Min(prev.Y, cur.Y) > MathF.Max(a.Y, b.Y)) continue;
                    if (!Bezier.LineSegmentsIntersect(prev, cur, a, b, out float u, out float v)) continue;
                    float t = (i - 1 + u) / n;
                    var p = Vector2.Lerp(prev, cur, u);
                    float s = w.Cum[j] + (w.Cum[j + 1] - w.Cum[j]) * v;
                    float h = heightAt(t);
                    if (h < terrain.HeightAt(p) - 1f) return null; // Tunnel unter dem Gewässer
                    // tiefster Punkt über der Rinne (Fahrbahn kann zur Mitte hin steigen)
                    float half = w.ChannelHalfWidth(s);
                    float minH = h;
                    for (int k = 0; k <= n; k++)
                    {
                        float tk = k / (float)n;
                        var q = Bezier.Evaluate(p0, c, p2, tk);
                        if (Vector2.Distance(q, p) > half * 2.5f) continue;
                        w.Project(q, out float dq);
                        if (dq <= half && terrain.IsWater(q)) minH = MathF.Min(minH, heightAt(tk));
                    }
                    float deck = rail ? ShippingRules.RailDeck : ShippingRules.RoadDeck;
                    result = new WaterwayCrossing { S = s, Rail = rail, Position = p, Clearance = minH - terrain.WaterLevel - deck };
                    break;
                }
                prev = cur;
            }
            return result;
        }

        // ------------------------------------------------------------ Planprüfung + Werkzeug-Hinweis

        /// <summary>Hook am Ende von <c>RoadBuilder.Validate</c>: Klappbrücken müssen genau eine Fahrrinne queren.</summary>
        RoadBuildError CheckRoadPlan(RoadPlan plan)
        {
            if ((plan.Flags & RoadSegmentFlags.Drawbridge) == 0) return RoadBuildError.None;
            if (plan.Type.Category != RoadCategory.City) return RoadBuildError.DrawbridgeNoFairway;
            int hits = 0;
            foreach (var w in waterways)
                if (CrossSegment(w, plan.Start, plan.Control, plan.End, plan.Length, plan.HeightAt, false) != null) hits++;
            return hits > 0 ? RoadBuildError.None : RoadBuildError.DrawbridgeNoFairway;
        }

        /// <summary>
        /// Prüft eine geplante Straße bzw. ein Gleis gegen die Fahrrinnen: lichte Höhe, Bemessungshöhe, Hinweistext
        /// („zu niedrig für Schiffe – höher bauen oder Klappbrücke“). Ohne Fahrrinne: <see cref="BridgeCheck.CrossesFairway"/> false.
        /// </summary>
        public BridgeCheck CheckBridge(RoadPlan plan, bool rail = false)
        {
            var r = new BridgeCheck { Clearance = float.MaxValue };
            if (!Active || plan == null || plan.Length < 1f) return r;
            bool drawbridge = (plan.Flags & RoadSegmentFlags.Drawbridge) != 0;
            foreach (var w in waterways)
            {
                var x = CrossSegment(w, plan.Start, plan.Control, plan.End, plan.Length, plan.HeightAt, rail);
                if (x == null)
                {
                    // Tunnel? (Schnitt mit dem Linienzug, aber unter dem Grund)
                    if (IntersectsPolyline(w, plan)) { r.Tunnel = true; r.CrossesFairway = true; }
                    continue;
                }
                r.CrossesFairway = true;
                if (x.Clearance < r.Clearance) { r.Clearance = x.Clearance; r.Design = w.DesignClearance; }
                else r.Design = MathF.Max(r.Design, w.DesignClearance);
            }
            if (!r.CrossesFairway) return r;
            r.Drawbridge = drawbridge;
            if (r.Clearance == float.MaxValue) { r.Text = "Tunnel unter der Fahrrinne – Schiffe fahren frei darüber"; return r; }
            if (drawbridge) { r.Text = $"Klappbrücke über der Fahrrinne: öffnet für Schiffe (geschlossen {MathF.Max(0f, r.Clearance):0.0} m frei)"; return r; }
            r.TooLow = r.Clearance < r.Design;
            if (r.TooLow)
                r.Text = $"Zu niedrig für Schiffe: lichte Höhe {MathF.Max(0f, r.Clearance):0.0} m < {r.Design:0.0} m – höher bauen (Bild↑) oder Klappbrücke (Hafen-Werkzeug 8)";
            else
            {
                string extra = "";
                foreach (var c in ShipCatalog.All)
                    if (r.Clearance < c.RequiredClearance && c.Role != ShipRole.Cargo) extra += (extra.Length > 0 ? ", " : "") + $"{c.DisplayName} ({c.AirDraft:0} m)";
                r.Text = $"Brücke über die Fahrrinne: lichte Höhe {r.Clearance:0.0} m ✓" + (extra.Length > 0 ? $" · zu niedrig für {extra} – die fahren dann nicht durch" : "");
            }
            return r;
        }

        static bool IntersectsPolyline(Waterway w, RoadPlan plan)
        {
            int n = Math.Clamp((int)MathF.Ceiling(plan.Length / 3f), 8, 256);
            var prev = plan.Start;
            for (int i = 1; i <= n; i++)
            {
                var cur = Bezier.Evaluate(plan.Start, plan.Control, plan.End, i / (float)n);
                for (int j = 0; j + 1 < w.Points.Length; j++)
                    if (Bezier.LineSegmentsIntersect(prev, cur, w.Points[j], w.Points[j + 1], out _, out _)) return true;
                prev = cur;
            }
            return false;
        }
    }
}
