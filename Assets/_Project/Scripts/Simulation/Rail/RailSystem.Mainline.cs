using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;

namespace CityBuilder.Simulation.Rail
{
    /// <summary>
    /// Fernbahn-Hauptstrecke auf der Bahntrasse der Gateways (geschwungen: Geraden + Bögen R ≥ 1 000 m, s. RoutePlanner):
    /// zweigleisig (Rechtsverkehr: Gleis 1 von A nach B rechts der Achse, Gleis 2 zurück), Gradiente auf dem geglätteten
    /// Gelände mit 2 % Steigung, Brücken über Wasser und über Straßen (Autobahn) mit lichter Höhe, Stücke à ≤ ~260 m =
    /// Blockabschnitte. Gehört der Welt (gesperrt, Kosten/Unterhalt trägt sie).
    /// </summary>
    public sealed partial class RailSystem
    {
        bool buildingMainline;

        /// <summary>Endknoten der Hauptstrecke: Gleis 1 (A → B) und Gleis 2 (B → A); 0 = keine Hauptstrecke.</summary>
        public int Track1StartNode { get; private set; }
        public int Track1EndNode { get; private set; }
        public int Track2StartNode { get; private set; }
        public int Track2EndNode { get; private set; }
        /// <summary>Rail-Gateways am Kartenrand: A (Beginn Gleis 1) und B.</summary>
        public int GatewayA { get; private set; }
        public int GatewayB { get; private set; }
        public bool HasMainline => Track1StartNode != 0 && Track1EndNode != 0;
        /// <summary>Bericht der Erzeugung (nicht gespeichert).</summary>
        public string MainlineReport { get; private set; }

        /// <summary>
        /// Baut die Hauptstrecke auf der Trasse (nur neue Welt, nach <c>Gateways.Generate</c>): Stücke der geplanten Mittellinie
        /// (Geraden + weite Bögen), je Gleis um ±2,25 m versetzt. Kosten trägt die Welt.
        /// </summary>
        public bool BuildMainline()
        {
            var gw = world.Gateways;
            if (gw == null || !gw.HasRailCorridor) { MainlineReport = "kein Bahnkorridor"; return false; }
            var a = gw.RailStart;
            var b = gw.RailEnd;
            var center = gw.RailPieces != null && gw.RailPieces.Count > 0 ? new List<RoutePiece>(gw.RailPieces) : StraightPieces(a, b);
            float length = 0f;
            foreach (var p in center) length += p.Length;
            if (length < 200f) { MainlineReport = "Korridor zu kurz"; return false; }

            // --- Gradiente entlang der Mittellinie (alle ≈ 4 m)
            const float ds = 4f;
            var starts = new List<int>();
            var pts = RoutePlanner.Sample(center, ds, starts);
            int count = pts.Count;
            var dist = new float[count];
            for (int k = 1; k < count; k++) dist[k] = dist[k - 1] + Vector2.Distance(pts[k - 1], pts[k]);
            var raw = new float[count];
            var water = new bool[count];
            var floor = new float[count];
            for (int k = 0; k < count; k++)
            {
                var p = pts[k];
                water[k] = terrain.IsWater(p);
                raw[k] = water[k] ? Profiles.WaterFloor(terrain, p, Profiles.WaterClearance + 2f) : terrain.HeightAt(p); // Wasserstraße: Fahrrinne
                floor[k] = water[k] ? raw[k] : float.MinValue;
            }
            var grade = BoxSmooth(raw, Math.Max(1, (int)(RailRules.MainlineSmoothing / ds)));

            // Straßen (Autobahn, Zubringer), die die Trasse kreuzen: Gleis darüber mit lichter Höhe
            var crossings = new List<float>(); // Strecke der Kreuzungen entlang der Mittellinie
            foreach (var seg in roads.Segments)
            {
                if (!CrossesPath(seg, pts, dist, out float along, out float roadH, out float angle)) continue;
                float sin = MathF.Max(0.3f, MathF.Sin(angle * MathF.PI / 180f));
                float half = (seg.Type.HalfWidth + RoadCatalog.RailMain.HalfWidth + RailRules.TrackSpacing) / sin + 6f;
                float need = roadH + RailRules.RailOverRoadClearance + 1.2f; // Konstruktionshöhe der Brücke
                for (int k = 0; k < count; k++)
                    if (MathF.Abs(dist[k] - along) <= half) floor[k] = MathF.Max(floor[k], need);
                crossings.Add(along);
            }
            for (int k = 1; k < count; k++) floor[k] = MathF.Max(floor[k], floor[k - 1] - RailRules.MainlineGrade * (dist[k] - dist[k - 1]));
            for (int k = count - 2; k >= 0; k--) floor[k] = MathF.Max(floor[k], floor[k + 1] - RailRules.MainlineGrade * (dist[k + 1] - dist[k]));
            for (int k = 0; k < count; k++) grade[k] = MathF.Max(grade[k], floor[k]);
            for (int k = 1; k < count; k++) { float g = RailRules.MainlineGrade * (dist[k] - dist[k - 1]); grade[k] = Math.Clamp(grade[k], grade[k - 1] - g, grade[k - 1] + g); }
            for (int k = count - 2; k >= 0; k--) { float g = RailRules.MainlineGrade * (dist[k + 1] - dist[k]); grade[k] = Math.Clamp(grade[k], grade[k + 1] - g, grade[k + 1] + g); }

            // --- Blockabschnitte = Stücke der Trasse; Grenzen über Wasser/an Kreuzungen fallen weg (gerade Stücke verschmelzen)
            bool Free(int k)
            {
                for (int j = 0; j < count; j++)
                    if (water[j] && MathF.Abs(dist[j] - dist[k]) < 40f) return false;
                foreach (float c in crossings) if (MathF.Abs(dist[k] - c) < 60f) return false;
                return true;
            }
            var blocks = new List<RoutePiece>();
            var blockStart = new List<float>(); // Strecke am Anfang jedes Blocks
            for (int i = 0; i < center.Count; i++)
            {
                var p = center[i];
                if (blocks.Count > 0)
                {
                    var prev = blocks[blocks.Count - 1];
                    bool collinear = prev.Turn == 0 && p.Turn == 0 && Vector2.Dot(Vector2.Normalize(prev.P2 - prev.P0), Vector2.Normalize(p.P2 - p.P0)) > 0.99996f;
                    if (collinear && !Free(starts[i]))
                    {
                        prev.P2 = p.P2;
                        prev.C = (prev.P0 + prev.P2) * 0.5f;
                        prev.Length = Vector2.Distance(prev.P0, prev.P2);
                        prev.H1 = p.H1;
                        blocks[blocks.Count - 1] = prev;
                        continue;
                    }
                }
                blocks.Add(p);
                blockStart.Add(dist[starts[i]]);
            }
            blockStart.Add(dist[count - 1]);

            // --- Bauen (Kasse abgehängt: die Welt zahlt), beide Gleise
            var budget = Tracks.Budget;
            Tracks.Budget = null;
            buildingMainline = true;
            int built = 0;
            float edge = world.Settings.SizeMeters * 0.5f - GatewayRules.EdgeInset;
            float GradeAt(float s)
            {
                int lo = 0, hi = count - 1;
                while (hi - lo > 1) { int m = (lo + hi) / 2; if (dist[m] <= s) lo = m; else hi = m; }
                float span = dist[hi] - dist[lo];
                float f = span > 1e-4f ? Math.Clamp((s - dist[lo]) / span, 0f, 1f) : 0f;
                return grade[lo] + (grade[hi] - grade[lo]) * f;
            }
            try
            {
                built += BuildMainTrack(blocks, blockStart, -RailRules.TrackSpacing * 0.5f, GradeAt, edge, forward: true, out int s1, out int e1);
                built += BuildMainTrack(blocks, blockStart, RailRules.TrackSpacing * 0.5f, GradeAt, edge, forward: false, out int s2, out int e2);
                Track1StartNode = s1; Track1EndNode = e1; Track2StartNode = s2; Track2EndNode = e2;
            }
            finally
            {
                buildingMainline = false;
                Tracks.Budget = budget;
            }

            // Rail-Gateways zuordnen (A = Beginn Gleis 1 am Kartenrand)
            foreach (var g2 in gw.All)
            {
                if (g2.Kind != GatewayKind.Rail || !g2.HasLocation) continue;
                if (Vector2.Distance(g2.Position, a) < Vector2.Distance(g2.Position, b)) GatewayA = g2.Id; else GatewayB = g2.Id;
            }
            MarkDirty();
            int curves = 0;
            foreach (var p in blocks) if (p.Turn != 0) curves++;
            MainlineReport = $"Hauptstrecke {length:0} m, {blocks.Count} Blöcke je Gleis ({curves} im Bogen), {crossings.Count} Straßenquerungen (Brücke), {built} Gleisstücke";
            return HasMainline;
        }

        /// <summary>Gerade Trasse A → B in Stücken à ~260 m (Rückfall ohne geplante Stücke).</summary>
        static List<RoutePiece> StraightPieces(Vector2 a, Vector2 b)
        {
            var list = new List<RoutePiece>();
            float len = Vector2.Distance(a, b);
            if (len < 1f) return list;
            var d = (b - a) / len;
            float heading = MathF.Atan2(d.Y, d.X);
            int n = Math.Max(1, (int)MathF.Round(len / RailRules.MainlineChunk));
            for (int i = 0; i < n; i++)
            {
                var p0 = a + d * (len * i / n); var p2 = a + d * (len * (i + 1) / n);
                list.Add(new RoutePiece { P0 = p0, P2 = p2, C = (p0 + p2) * 0.5f, H0 = heading, H1 = heading, Length = Vector2.Distance(p0, p2) });
            }
            return list;
        }

        int BuildMainTrack(List<RoutePiece> blocks, List<float> blockStart, float offset, Func<float, float> gradeAt, float edge, bool forward,
            out int startNode, out int endNode)
        {
            startNode = endNode = 0;
            var pieces = new List<RoutePiece>(blocks.Count);
            foreach (var p in blocks) pieces.Add(RoutePlanner.Offset(p, offset));
            RoutePlanner.ClampEndToEdge(pieces, edge, false);
            RoutePlanner.ClampEndToEdge(pieces, edge, true);
            var nodes = new List<RoadNode> { Tracks.AddNode(pieces[0].P0, gradeAt(blockStart[0])) };
            for (int i = 0; i < pieces.Count; i++) nodes.Add(Tracks.AddNode(pieces[i].P2, gradeAt(blockStart[i + 1])));
            int built = 0;
            for (int i = 0; i < pieces.Count; i++)
            {
                float s0 = blockStart[i], s1 = blockStart[i + 1];
                int n = Math.Clamp((int)MathF.Ceiling((s1 - s0) / 4f) + 1, 2, 257);
                var profile = new float[n];
                for (int j = 0; j < n; j++) profile[j] = gradeAt(s0 + (s1 - s0) * j / (n - 1));
                RoadNode from = nodes[i], to = nodes[i + 1];
                if (!forward) { (from, to) = (to, from); Array.Reverse(profile); }
                var seg = Tracks.AddSegment(from, to, pieces[i].C, RoadCatalog.RailMain, RoadSegmentFlags.Locked, profile);
                if (seg != null) built++;
            }
            startNode = forward ? nodes[0].Id : nodes[nodes.Count - 1].Id;
            endNode = forward ? nodes[nodes.Count - 1].Id : nodes[0].Id;
            return built;
        }

        /// <summary>Kreuzt die Straße die Trasse? Strecke entlang der Mittellinie, Straßenhöhe dort, Winkel (0..90°).</summary>
        static bool CrossesPath(RoadSegment seg, List<Vector2> pts, float[] dist, out float along, out float height, out float angle)
        {
            along = height = angle = 0f;
            int m = Math.Clamp((int)MathF.Ceiling(seg.Length / 3f), 8, 256);
            var prev = seg.Start.Position;
            for (int j = 1; j <= m; j++)
            {
                var cur = seg.Evaluate(j / (float)m);
                var lo = Vector2.Min(prev, cur); var hi = Vector2.Max(prev, cur);
                for (int k = 0; k < pts.Count - 1; k++)
                {
                    var a = pts[k]; var b = pts[k + 1];
                    if (MathF.Max(a.X, b.X) < lo.X || MathF.Min(a.X, b.X) > hi.X || MathF.Max(a.Y, b.Y) < lo.Y || MathF.Min(a.Y, b.Y) > hi.Y) continue;
                    if (!Bezier.LineSegmentsIntersect(a, b, prev, cur, out float s, out float v)) continue;
                    float t = (j - 1 + v) / m;
                    along = dist[k] + s * (dist[k + 1] - dist[k]);
                    height = seg.HeightAt(t);
                    angle = RoadGeometry.Angle(b - a, seg.Tangent(t));
                    if (angle > 90f) angle = 180f - angle;
                    return true;
                }
                prev = cur;
            }
            return false;
        }

        static float[] BoxSmooth(float[] src, int radius)
        {
            var dst = new float[src.Length];
            double sum = 0;
            int lo = 0, hi = -1;
            for (int i = 0; i < src.Length; i++)
            {
                int a = Math.Max(0, i - radius), b = Math.Min(src.Length - 1, i + radius);
                while (hi < b) sum += src[++hi];
                while (lo < a) sum -= src[lo++];
                dst[i] = (float)(sum / (hi - lo + 1));
            }
            return dst;
        }

        // ------------------------------------------------------------ Einfahrten/Ausfahrten der Hauptstrecke

        /// <summary>Gerichtetes erstes Gleisstück ab einem Endknoten am Kartenrand (Einfahrt), null = keins.</summary>
        EdgeKey? EntryEdge(int nodeId)
        {
            var node = Tracks.GetNode(nodeId);
            if (node == null || node.SegmentIds.Count == 0) return null;
            var s = Tracks.GetSegment(node.SegmentIds[0]);
            return new EdgeKey(s.Id, s.Start == node);
        }

        /// <summary>Rail-Gateway (A/B) als Objekt.</summary>
        public Gateway GatewayOf(bool a) => world.Gateways?.Get(a ? GatewayA : GatewayB);

        /// <summary>Hält ein Fernzug irgendwo in der Stadt (mindestens ein Personenbahnhof in Betrieb)?</summary>
        public bool HasPassengerService
        {
            get { foreach (var s in stations) if (s.IsPassenger && s.InService) return true; return false; }
        }

        public bool HasFreightService
        {
            get { foreach (var s in stations) if (!s.IsPassenger && s.InService) return true; return false; }
        }

        /// <summary>Rail-Gateways werden befahrbar, sobald ein Bahnhof an der Hauptstrecke in Betrieb ist.</summary>
        void UpdateGatewayStatus()
        {
            var gw = world.Gateways;
            if (gw == null || !HasMainline) return;
            bool on = HasPassengerService || HasFreightService;
            foreach (var g in gw.All)
            {
                if (g.Kind != GatewayKind.Rail) continue;
                g.Status = on ? GatewayStatus.Operational : GatewayStatus.Planned;
                g.Capacity = 2 * (RailRules.LastIntercityHour - RailRules.FirstIntercityHour + 1) + 24 / RailRules.FreightEveryHours * 2;
                g.RailBuilt = true;
            }
        }
    }
}
