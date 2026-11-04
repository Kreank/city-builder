using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;

namespace CityBuilder.Simulation.Rail
{
    /// <summary>
    /// Gleise: planen/bauen/abreißen über <see cref="RoadBuilder"/> auf dem Gleisnetz (Einrasten, Kreuzungen = Zerteilen,
    /// Höhen, automatische Trassierung mit Tunnel/Brücke), plus Bahnregeln (Radius, tangentiale Weichen, belegte Gleise) und
    /// die gemeinsame Prüfung Straße ↔ Gleis (Bahnübergang nur ebenerdig mit Stadtstraße, sonst lichte Höhe).
    /// Fahrweg-Graph: gerichtete Gleisstücke, Übergang an einem Knoten nur bei Ablenkung ≤ <see cref="RailRules.MaxDeflection"/>.
    /// </summary>
    public sealed partial class RailSystem
    {
        // ------------------------------------------------------------ Gleise planen/bauen

        /// <summary>
        /// Gleis planen (wie <see cref="RoadBuilder.Plan"/>): Einrasten an Gleisknoten/-stücken im Umkreis <paramref name="snap"/>,
        /// <paramref name="followTerrain"/> = automatische Trassierung (Bauhöhe 0), sonst gerade Rampe zwischen den Höhen.
        /// </summary>
        public RoadPlan PlanTrack(Vector2 start, Vector2? control, Vector2 end, float snap = 6f, float startHeight = 0f, float endHeight = 0f,
            bool followTerrain = true)
        {
            if (followTerrain)
            {
                startHeight = terrain.HeightAt(start);
                endHeight = terrain.HeightAt(end);
            }
            return RoadBuilder.Plan(Tracks, start, snap, control, end, snap, RoadCatalog.RailTrack, startHeight, endHeight,
                RoadSegmentFlags.None, followTerrain);
        }

        public List<RoadSegment> BuildTrack(RoadPlan plan)
        {
            if (plan == null || !plan.IsValid || !plan.Type.IsRail) return new List<RoadSegment>();
            var built = RoadBuilder.Build(Tracks, plan);
            if (built.Count > 0) MarkDirty();
            return built;
        }

        /// <summary>Spieler-Abriss eines Gleisstücks (25 % zurück) – nicht die Hauptstrecke, nicht unter einem Zug.</summary>
        public bool DemolishTrack(int segmentId)
        {
            var seg = Tracks.GetSegment(segmentId);
            if (seg == null || seg.IsLocked || IsSegmentBusy(segmentId)) return false;
            bool ok = RoadBuilder.Demolish(Tracks, seg);
            if (ok) MarkDirty();
            return ok;
        }

        /// <summary>Warum ein Gleisstück nicht abgerissen werden kann (null = erlaubt).</summary>
        public string DemolishBlockReason(int segmentId)
        {
            var seg = Tracks.GetSegment(segmentId);
            if (seg == null) return "kein Gleis";
            if (seg.IsLocked) return "Gehört zur Fernbahn-Hauptstrecke (Welt) – nicht abreißbar. Anschließen: Weiche ans Hauptgleis bauen.";
            if (IsSegmentBusy(segmentId)) return "Gleis ist von einem Zug belegt – warten, bis er vorbei ist.";
            return null;
        }

        /// <summary>Ist das Gleisstück von einem Zug belegt oder für ihn reserviert?</summary>
        public bool IsSegmentBusy(int segmentId) => segmentOwner.ContainsKey(segmentId) || PlannedByWaitingTrain(segmentId);

        bool PlannedByWaitingTrain(int segmentId) => false;

        public static string ErrorText(RoadBuildError e)
        {
            switch (e)
            {
                case RoadBuildError.TooSteep: return $"zu steil für ein Gleis (max. {RoadType.RailMaxSlope * 100f:0.#} %)";
                case RoadBuildError.TrackGeometry: return $"Bogen zu eng (Radius ≥ {RailRules.MinRadius:0} m) oder Anschluss nicht tangential (Weiche ≤ {RailRules.MaxSwitchAngle:0}°)";
                case RoadBuildError.TrackOccupied: return "Gleis gerade von einem Zug belegt – warten, bis er vorbei ist";
                case RoadBuildError.RailCrossing: return "Bahnübergang hier nicht möglich (nur Stadtstraße, ≥ 35°, ≥ 20 m von Kreuzungen/Weichen, nicht am Bahnsteig) – Brücke/Tunnel bauen";
                case RoadBuildError.OverlapsRail: return "Straße und Gleis dürfen nicht übereinander längs laufen – nur queren";
                case RoadBuildError.InsufficientClearance: return $"zu wenig lichte Höhe (Gleis über Straße ≥ {RailRules.RailOverRoadClearance:0.#} m, Straße über Gleis ≥ {RailRules.RoadOverRailClearance:0.#} m)";
                case RoadBuildError.OverlapsStructure: return "Gleis überschneidet ein Bauwerk";
                case RoadBuildError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                case RoadBuildError.OverWater: return "Wasser nur auf einer Brücke queren";
                case RoadBuildError.AngleTooSharp: return "zu spitzer Winkel";
                case RoadBuildError.TooShort: return "zu kurz";
                case RoadBuildError.Duplicate: return "Gleis gibt es schon";
                default: return e.ToString();
            }
        }

        // ------------------------------------------------------------ Prüfung Gleisplan (Hook Tracks.ExternalCheck)

        RoadBuildError CheckTrackPlan(RoadPlan plan)
        {
            // Hauptstrecke wird ohne diese Prüfung gebaut (eigene Trassierung, s. BuildMainline)
            if (buildingMainline) return RoadBuildError.None;
            if (!plan.Type.IsRail) return RoadBuildError.None;

            // 1) Mindestradius
            if (MinRadius(plan.Start, plan.Control, plan.End) < RailRules.MinRadius - 0.5f) return RoadBuildError.TrackGeometry;

            // 2) Tangentialer Anschluss (Weiche/Fortsetzung) an Knoten bzw. mitten auf einem Gleis
            var startDir = Dir(plan.Control - plan.Start, plan.End - plan.Start);
            var endDir = Dir(plan.Control - plan.End, plan.Start - plan.End);
            if (!TangentOk(plan.StartAnchor, startDir) || !TangentOk(plan.EndAnchor, endDir)) return RoadBuildError.TrackGeometry;

            // 3) Belegte Gleise werden nicht zerteilt/angeschlossen
            if (AnchorBusy(plan.StartAnchor) || AnchorBusy(plan.EndAnchor)) return RoadBuildError.TrackOccupied;
            foreach (var c in plan.Crossings)
            {
                if (c.Segment != null && IsSegmentBusy(c.Segment.Id)) return RoadBuildError.TrackOccupied;
                // Kreuzungsweiche (ebenerdige Gleiskreuzung) nur ausreichend steil
                var nd = Dir(Bezier.Derivative(plan.Start, plan.Control, plan.End, c.TNew), startDir);
                var ex = c.Segment.Tangent(c.Segment.ClosestT(c.Point));
                float a = RoadGeometry.Angle(nd, ex);
                if (a > 90f) a = 180f - a;
                if (a < RailRules.MinDiamondAngle) return RoadBuildError.TrackGeometry;
            }

            // 4) Straßen queren: Bahnübergang oder lichte Höhe
            return CrossCheck(plan, planIsRail: true);
        }

        static Vector2 Dir(Vector2 v, Vector2 fallback)
        {
            if (v.LengthSquared() > 1e-6f) return Vector2.Normalize(v);
            return fallback.LengthSquared() > 1e-6f ? Vector2.Normalize(fallback) : Vector2.UnitY;
        }

        /// <summary>Kleinster Krümmungsradius einer quadratischen Bézierkurve (m; gerade = ∞).</summary>
        public static float MinRadius(Vector2 p0, Vector2 c, Vector2 p2)
        {
            var dd = 2f * (p0 - 2f * c + p2);
            float best = float.PositiveInfinity;
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 16f;
                var d = Bezier.Derivative(p0, c, p2, t);
                float len = d.Length();
                float cross = MathF.Abs(d.X * dd.Y - d.Y * dd.X);
                if (cross < 1e-5f || len < 1e-4f) continue;
                best = MathF.Min(best, len * len * len / cross);
            }
            return best;
        }

        /// <summary>
        /// Gleis schließt tangential an: am Knoten setzt es ein vorhandenes Gleis fort (Ablenkung ≤ Weichenwinkel) – an einem
        /// freien Gleisende auch als Verlängerung –, mitten auf einem Gleis zweigt es flach ab (Weiche).
        /// </summary>
        bool TangentOk(RoadAnchor anchor, Vector2 outgoing)
        {
            switch (anchor.Kind)
            {
                case AnchorKind.Node:
                    if (anchor.Node.SegmentIds.Count == 0) return true;
                    if (anchor.Node.SegmentIds.Count >= 3) return false; // höchstens einfache Weiche am Knoten
                    foreach (int id in anchor.Node.SegmentIds)
                    {
                        var seg = Tracks.GetSegment(id);
                        var od = RoadGeometry.OutgoingDirection(seg, anchor.Node);
                        // Fortsetzung: neues Gleis verlässt den Knoten etwa entgegen dem vorhandenen
                        if (RoadGeometry.Angle(-od, outgoing) <= RailRules.MaxSwitchAngle) return true;
                        // Abzweig neben einem vorhandenen Gleis (Weiche am Knoten, in dieselbe Richtung)
                        if (anchor.Node.SegmentIds.Count == 2 && RoadGeometry.Angle(od, outgoing) <= RailRules.MaxSwitchAngle) return true;
                    }
                    return false;
                case AnchorKind.Segment:
                    var tan = anchor.Segment.Tangent(anchor.T);
                    float a = RoadGeometry.Angle(tan, outgoing);
                    return a <= RailRules.MaxSwitchAngle || a >= 180f - RailRules.MaxSwitchAngle;
                default:
                    return true;
            }
        }

        bool AnchorBusy(RoadAnchor a)
        {
            if (a.Kind == AnchorKind.Segment) return IsSegmentBusy(a.Segment.Id);
            if (a.Kind == AnchorKind.Node) return nodeOwner.ContainsKey(a.Node.Id);
            return false;
        }

        // ------------------------------------------------------------ Prüfung Straßenplan (Hook roads.ExternalCheck)

        RoadBuildError CheckRoadPlan(RoadPlan plan)
        {
            if (Tracks.SegmentCount == 0) return RoadBuildError.None;
            return CrossCheck(plan, planIsRail: false);
        }

        /// <summary>
        /// Gemeinsame Regel Straße ↔ Gleis für einen Plan gegen das jeweils andere Netz: gleiche Ebene (±1,5 m) = Bahnübergang
        /// (nur Stadtstraße, Winkel ≥ 35°, Abstand zu Straßenknoten ≥ 20 m und zu Weichen ≥ 25 m, nicht am Bahnsteig),
        /// sonst lichte Höhe (Gleis über Straße 5 m, Straße über Gleis 6,5 m); längs übereinander verboten.
        /// </summary>
        RoadBuildError CrossCheck(RoadPlan plan, bool planIsRail)
        {
            var other = planIsRail ? roads : Tracks;
            if (other.SegmentCount == 0) return RoadBuildError.None;
            int n = Math.Clamp((int)MathF.Ceiling(plan.Length / 3f), 8, 256);
            var pts = new Vector2[n + 1];
            for (int i = 0; i <= n; i++) pts[i] = Bezier.Evaluate(plan.Start, plan.Control, plan.End, i / (float)n);
            Bezier.Bounds(plan.Start, plan.Control, plan.End, out var lo, out var hi);
            var hits = new List<(Vector2 p, float angle, float span)>();

            foreach (var seg in other.Segments)
            {
                float reach = seg.Type.HalfWidth + plan.Type.HalfWidth + 1f;
                Bezier.Bounds(seg.Start.Position, seg.Control, seg.End.Position, out var smin, out var smax);
                if (smax.X < lo.X - reach || smax.Y < lo.Y - reach || smin.X > hi.X + reach || smin.Y > hi.Y + reach) continue;
                int m = Math.Clamp((int)MathF.Ceiling(seg.Length / 3f), 8, 256);
                Vector2 prev = seg.Start.Position;
                for (int j = 1; j <= m; j++)
                {
                    var cur = seg.Evaluate(j / (float)m);
                    for (int i = 0; i < n; i++)
                    {
                        if (!Bezier.LineSegmentsIntersect(pts[i], pts[i + 1], prev, cur, out float u, out float v)) continue;
                        float tPlan = (i + u) / n, tSeg = (j - 1 + v) / m;
                        var p = Vector2.Lerp(pts[i], pts[i + 1], u);
                        float hPlan = plan.HeightAt(tPlan), hSeg = seg.HeightAt(tSeg);
                        float dh = hPlan - hSeg;
                        var tanPlan = Dir(Bezier.Derivative(plan.Start, plan.Control, plan.End, tPlan), plan.End - plan.Start);
                        var tanSeg = seg.Tangent(tSeg);
                        float angle = RoadGeometry.Angle(tanPlan, tanSeg);
                        if (angle > 90f) angle = 180f - angle;
                        if (MathF.Abs(dh) <= RoadNetwork.SameLevelTolerance)
                        {
                            var road = planIsRail ? seg.Type : plan.Type;
                            if (road.Category != RoadCategory.City) return RoadBuildError.RailCrossing;
                            if (angle < RailRules.MinCrossingAngle) return RoadBuildError.RailCrossing;
                            if (!CrossingPlaceOk(plan, planIsRail, seg, p)) return RoadBuildError.RailCrossing;
                            float sin = MathF.Max(0.3f, MathF.Sin(angle * MathF.PI / 180f));
                            hits.Add((p, angle, (seg.Type.HalfWidth + plan.Type.HalfWidth) / sin + 2f));
                        }
                        else
                        {
                            bool roadAbove = planIsRail ? dh < 0f : dh > 0f;
                            float need = roadAbove ? RailRules.RoadOverRailClearance : RailRules.RailOverRoadClearance;
                            if (MathF.Abs(dh) < need) return RoadBuildError.InsufficientClearance;
                        }
                    }
                    prev = cur;
                }
            }

            // Längs übereinander (auf gleicher Ebene, außerhalb der Querungsstellen)
            for (int i = 0; i <= n; i++)
            {
                var p = pts[i];
                float h = plan.HeightAt(i / (float)n);
                if (MathF.Abs(h - terrain.HeightAt(p)) > RoadFront.AtGradeElevation + 2f) continue; // Brücke/Tunnel
                bool nearHit = false;
                foreach (var hit in hits) if (Vector2.Distance(hit.p, p) < hit.span) { nearHit = true; break; }
                if (nearHit) continue;
                if (other.FindNearestSegment(p, plan.Type.HalfWidth - 0.75f, h) != null) return RoadBuildError.OverlapsRail;
            }
            return RoadBuildError.None;
        }

        /// <summary>Lage eines Bahnübergangs: weg von Straßenknoten, Weichen und Bahnsteigen.</summary>
        bool CrossingPlaceOk(RoadPlan plan, bool planIsRail, RoadSegment seg, Vector2 p)
        {
            float roadClear = RailRules.CrossingClearFromRoadNode, switchClear = RailRules.CrossingClearFromSwitch;
            if (planIsRail)
            {
                // Straßenknoten der vorhandenen Straße; Enden des neuen Gleises werden ggf. Weichen
                if (Vector2.Distance(p, seg.Start.Position) < roadClear || Vector2.Distance(p, seg.End.Position) < roadClear) return false;
                if (plan.StartAnchor.Kind != AnchorKind.Free && Vector2.Distance(p, plan.Start) < switchClear) return false;
                if (plan.EndAnchor.Kind != AnchorKind.Free && Vector2.Distance(p, plan.End) < switchClear) return false;
            }
            else
            {
                // Enden der neuen Straße und ihre Kreuzungen mit anderen Straßen sind Straßenknoten
                if (Vector2.Distance(p, plan.Start) < roadClear || Vector2.Distance(p, plan.End) < roadClear) return false;
                foreach (var c in plan.Crossings) if (Vector2.Distance(p, c.Point) < roadClear) return false;
                // Weichen am Gleis (Knoten mit ≥ 3 Gleisen)
                if ((seg.Start.SegmentIds.Count >= 3 && Vector2.Distance(p, seg.Start.Position) < switchClear) ||
                    (seg.End.SegmentIds.Count >= 3 && Vector2.Distance(p, seg.End.Position) < switchClear)) return false;
            }
            foreach (var s in stations)
                if (s.PlatformArea.Contains(p, 2f)) return false;
            return true;
        }

        // ------------------------------------------------------------ Gelände, Grundstücke, Bauwerke

        /// <summary>Wie die Straßenregel der Welt: unter ebenerdigen Gleisen liegt das Gelände knapp unter dem Schotterbett.</summary>
        float TrackRule(Vector2 p, float proposed, out bool underTrack)
        {
            underTrack = false;
            if (Tracks.SegmentCount == 0) return proposed;
            float under = float.MaxValue, beside = float.MaxValue;
            float margin = terrain.RoadMargin;
            foreach (var s in Tracks.Segments)
            {
                float reach = s.Type.HalfWidth + margin;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var min, out var max);
                if (p.X < min.X - reach || p.Y < min.Y - reach || p.X > max.X + reach || p.Y > max.Y + reach) continue;
                float t = s.ClosestT(p);
                var q = s.Evaluate(t);
                float d = Vector2.Distance(p, q);
                if (d > reach) continue;
                float h = s.HeightAt(t);
                float natural = h - terrain.NaturalHeightAt(q);
                float limit = s.FollowsTerrain ? TerrainMap.MaxCutFill + 0.5f : TerrainMap.ConformRange + 0.5f;
                if (natural > limit || natural < -limit) continue;
                float surface = h - TerrainMap.UnderRoad;
                if (d <= s.Type.HalfWidth) under = MathF.Min(under, surface);
                else beside = MathF.Min(beside, surface);
            }
            underTrack = under < float.MaxValue;
            return MathF.Min(underTrack ? under : proposed, beside);
        }

        /// <summary>
        /// Lärm der Gleise (vom <see cref="Environment.NoiseSystem"/> stündlich): Gleistyp-Lärm × 0,75 („Schienenbonus“: Züge fahren
        /// im Takt, Straßen dauernd), Gleise ohne Zugverkehr (keine Hauptstrecke) leiser; Tunnel still, Trog gedämpft.
        /// </summary>
        public void StampNoise(GridLayer noise)
        {
            if (Tracks.SegmentCount == 0) return;
            float step = noise.CellSize * 0.5f;
            float activity = HasMainline ? 0.75f : 0.3f;
            var tiles = world.Tiles; // Kacheln (Leistung): nur Gleise, deren Lärm gekauftes Land erreicht
            bool limit = tiles != null && tiles.Restricted;
            foreach (var s in Tracks.Segments)
            {
                if (limit)
                {
                    Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var bmin, out var bmax);
                    var reach = new Vector2(s.Type.NoiseRadius);
                    if (!tiles.RectTouchesOwned(bmin - reach, bmax + reach)) continue;
                }
                int samples = Math.Max(2, (int)MathF.Ceiling(s.Length / step) + 1);
                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)(samples - 1);
                    float level = s.IsCoveredAt(t) ? 0f : s.ElevationAt(t) < -0.3f ? s.Type.NoiseLevel * 0.5f : s.Type.NoiseLevel;
                    if (level > 0f) noise.StampRadialMax(s.Evaluate(t), s.Type.NoiseRadius, level * activity);
                }
            }
        }

        /// <summary>Liegt ein ebenerdiges Gleis (± Rand) an diesem Punkt?</summary>
        public bool TrackAt(Vector2 p, float margin = 0f)
        {
            foreach (var s in Tracks.Segments)
            {
                float reach = s.Type.HalfWidth + margin;
                Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var min, out var max);
                if (p.X < min.X - reach || p.Y < min.Y - reach || p.X > max.X + reach || p.Y > max.Y + reach) continue;
                float t = s.ClosestT(p);
                if (Vector2.Distance(s.Evaluate(t), p) <= reach && MathF.Abs(s.ElevationAt(t)) <= RoadFront.AtGradeElevation) return true;
            }
            return false;
        }

        /// <summary>Nächstes Gleisstück im Umkreis (Rand), für Werkzeug/Inspektor.</summary>
        public RoadSegment TrackNear(Vector2 p, float radius) => Tracks.FindNearestSegment(p, radius);

        /// <summary>Bauwerke der Straßen-Sperre außer den Bahnsteigflächen (Gleise dürfen an Bahnsteigen entlang).</summary>
        IEnumerable<StructureBlock> ForeignStructures()
        {
            foreach (var b in roads.Structures)
                if (b.Kind != StationBlockKind) yield return b;
        }

        const string StationBlockKind = "station";

        /// <summary>Für Straßen: Gebäude + Bahnsteigfläche jedes Bahnhofs.</summary>
        IEnumerable<StructureBlock> StationBlocksForRoads()
        {
            foreach (var s in stations)
            {
                yield return new StructureBlock(StationBlockKind, s.Id, s.Type.DisplayName, s.Footprint, s.Height);
                yield return new StructureBlock(StationBlockKind, -s.Id, s.Type.DisplayName + " (Bahnsteig)", s.PlatformArea, s.Height);
            }
        }

        /// <summary>Für Gleise: nur die Gebäude (Gleise dürfen an der Bahnsteigkante entlang).</summary>
        IEnumerable<StructureBlock> StationBuildingBlocks()
        {
            foreach (var s in stations)
                yield return new StructureBlock("station_building", s.Id, s.Type.DisplayName, s.Footprint, s.Height);
        }

        /// <summary>Neues Gleis durch Zonen-Grundstücke → Gebäude weg (wie bei Straßen; gesetzte Häuser sperren vorher).</summary>
        void OnTrackAdded(RoadSegment s)
        {
            var city = world.City;
            if (city == null) return;
            var hit = new List<Zoning.Building>();
            int samples = Math.Max(2, (int)MathF.Ceiling(s.Length / 4f));
            foreach (var b in city.Buildings)
            {
                if (b.IsPlayerPlaced) continue;
                float r = MathF.Max(b.Width, b.Depth) + s.Type.HalfWidth;
                if (s.DistanceTo(b.Position) > r + 2f) continue;
                for (int i = 0; i <= samples; i++)
                {
                    float t = i / (float)samples;
                    if (MathF.Abs(s.ElevationAt(t)) > 3f) continue;
                    if (b.Contains(s.Evaluate(t), s.Type.HalfWidth - 0.5f)) { hit.Add(b); break; }
                }
            }
            foreach (var b in hit) city.RemoveBuilding(b);
        }

        // ------------------------------------------------------------ Fahrweg-Graph

        bool graphDirty = true;
        int graphVersion = -1;
        /// <summary>Je gerichtetem Gleisstück die möglichen Folgestücke (Ablenkung ≤ MaxDeflection), nach Id sortiert.</summary>
        readonly Dictionary<EdgeKey, List<EdgeKey>> next = new Dictionary<EdgeKey, List<EdgeKey>>();

        void EnsureGraph()
        {
            if (!graphDirty && graphVersion == Tracks.Version) return;
            graphDirty = false;
            graphVersion = Tracks.Version;
            next.Clear();
            foreach (var s in Tracks.Segments)
                for (int k = 0; k < 2; k++)
                {
                    var e = new EdgeKey(s.Id, k == 0);
                    var node = e.Along ? s.End : s.Start;
                    var arrive = -RoadGeometry.OutgoingDirection(s, node); // Fahrtrichtung beim Erreichen des Knotens
                    var list = new List<EdgeKey>();
                    foreach (int id in node.SegmentIds)
                    {
                        if (id == s.Id) continue;
                        var o = Tracks.GetSegment(id);
                        var leave = RoadGeometry.OutgoingDirection(o, node);
                        if (RoadGeometry.Angle(arrive, leave) > RailRules.MaxDeflection) continue;
                        list.Add(new EdgeKey(id, o.Start == node));
                    }
                    list.Sort((a, b) => a.SegmentId != b.SegmentId ? a.SegmentId.CompareTo(b.SegmentId) : a.Along.CompareTo(b.Along));
                    next[e] = list;
                }
            // Fahrwege der Züge prüfen (Gleis abgerissen/zerteilt → neu suchen)
            RevalidateRoutes();
        }

        /// <summary>Folgestücke eines gerichteten Gleisstücks (leer = Gleisende/kein tangentialer Anschluss).</summary>
        public IReadOnlyList<EdgeKey> NextEdges(EdgeKey e)
        {
            EnsureGraph();
            return next.TryGetValue(e, out var l) ? l : (IReadOnlyList<EdgeKey>)Array.Empty<EdgeKey>();
        }

        static RoadNode EndNode(RoadSegment s, EdgeKey e) => e.Along ? s.End : s.Start;
        static RoadNode StartNode(RoadSegment s, EdgeKey e) => e.Along ? s.Start : s.End;

        float EdgeSpeed(RoadSegment s) => s.Type.SpeedLimitKmh / 3.6f;

        /// <summary>
        /// Schnellster Fahrweg (Fahrzeit) von <paramref name="from"/> (inkl.) bis zu einem Stück, das <paramref name="goal"/>
        /// erfüllt (inkl.). Deterministisch (Kosten, dann Id). null = keiner.
        /// </summary>
        List<EdgeKey> FindPath(EdgeKey from, Func<EdgeKey, bool> goal)
        {
            EnsureGraph();
            if (Tracks.GetSegment(from.SegmentId) == null) return null;
            var dist = new Dictionary<EdgeKey, float>();
            var prev = new Dictionary<EdgeKey, EdgeKey>();
            var open = new SortedSet<(float cost, int seg, bool along)>();
            float c0 = Tracks.GetSegment(from.SegmentId).Length / EdgeSpeed(Tracks.GetSegment(from.SegmentId));
            dist[from] = c0;
            open.Add((c0, from.SegmentId, from.Along));
            while (open.Count > 0)
            {
                var top = open.Min;
                open.Remove(top);
                var e = new EdgeKey(top.seg, top.along);
                if (top.cost > dist[e] + 1e-4f) continue;
                if (goal(e))
                {
                    var path = new List<EdgeKey> { e };
                    while (!e.Equals(from)) { e = prev[e]; path.Add(e); }
                    path.Reverse();
                    return path;
                }
                if (!next.TryGetValue(e, out var nexts)) continue;
                foreach (var n in nexts)
                {
                    var s = Tracks.GetSegment(n.SegmentId);
                    float c = top.cost + s.Length / EdgeSpeed(s);
                    if (dist.TryGetValue(n, out float old) && old <= c) continue;
                    if (dist.ContainsKey(n)) open.Remove((old, n.SegmentId, n.Along));
                    dist[n] = c;
                    prev[n] = e;
                    open.Add((c, n.SegmentId, n.Along));
                }
            }
            return null;
        }
    }
}
