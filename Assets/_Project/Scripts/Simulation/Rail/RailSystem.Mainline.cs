using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;

namespace CityBuilder.Simulation.Rail
{
    /// <summary>
    /// Fernbahn-Hauptstrecke auf dem Bahnkorridor der Gateways: zweigleisig (Rechtsverkehr: Gleis 1 von A nach B rechts der
    /// Achse, Gleis 2 zurück), Gradiente auf dem geglätteten Gelände mit 2 % Steigung, Brücken über Wasser und über Straßen
    /// (Autobahn) mit lichter Höhe, Stücke à ~260 m = Blockabschnitte. Gehört der Welt (gesperrt, Kosten/Unterhalt trägt sie).
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

        /// <summary>Baut die Hauptstrecke auf dem Korridor (nur neue Welt, nach <c>Gateways.Generate</c>). Kosten trägt die Welt.</summary>
        public bool BuildMainline()
        {
            var gw = world.Gateways;
            if (gw == null || !gw.HasRailCorridor) { MainlineReport = "kein Bahnkorridor"; return false; }
            var a = gw.RailStart;
            var b = gw.RailEnd;
            float length = Vector2.Distance(a, b);
            if (length < 200f) { MainlineReport = "Korridor zu kurz"; return false; }
            var u = Vector2.Normalize(b - a);
            var right = new Vector2(u.Y, -u.X);

            // --- Gradiente entlang der Achse (alle 4 m)
            const float ds = 4f;
            int count = (int)MathF.Ceiling(length / ds) + 1;
            var raw = new float[count];
            var water = new bool[count];
            for (int k = 0; k < count; k++)
            {
                var p = a + u * MathF.Min(length, k * ds);
                water[k] = terrain.IsWater(p);
                raw[k] = water[k] ? Profiles.WaterFloor(terrain, p, Profiles.WaterClearance + 2f) : terrain.HeightAt(p); // Wasserstraße: Fahrrinne
            }
            var grade = BoxSmooth(raw, Math.Max(1, (int)(RailRules.MainlineSmoothing / ds)));
            var floor = new float[count];
            for (int k = 0; k < count; k++) floor[k] = water[k] ? Profiles.WaterFloor(terrain, a + u * MathF.Min(length, k * ds), Profiles.WaterClearance + 2f) : float.MinValue; // Wasserstraße

            // Straßen (Autobahn, Zubringer), die den Korridor kreuzen: Gleis darüber mit lichter Höhe
            var crossings = new List<float>(); // Achsen-Strecke der Kreuzungen
            foreach (var seg in roads.Segments)
            {
                if (!CrossesAxis(seg, a, u, length, out float along, out float roadH, out float angle)) continue;
                float sin = MathF.Max(0.3f, MathF.Sin(angle * MathF.PI / 180f));
                float half = (seg.Type.HalfWidth + RoadCatalog.RailMain.HalfWidth + RailRules.TrackSpacing) / sin + 6f;
                float need = roadH + RailRules.RailOverRoadClearance + 1.2f; // Konstruktionshöhe der Brücke
                for (int k = 0; k < count; k++)
                    if (MathF.Abs(k * ds - along) <= half) floor[k] = MathF.Max(floor[k], need);
                crossings.Add(along);
            }
            float g = RailRules.MainlineGrade * ds;
            for (int k = 1; k < count; k++) floor[k] = MathF.Max(floor[k], floor[k - 1] - g);
            for (int k = count - 2; k >= 0; k--) floor[k] = MathF.Max(floor[k], floor[k + 1] - g);
            for (int k = 0; k < count; k++) grade[k] = MathF.Max(grade[k], floor[k]);
            for (int k = 1; k < count; k++) grade[k] = Math.Clamp(grade[k], grade[k - 1] - g, grade[k - 1] + g);
            for (int k = count - 2; k >= 0; k--) grade[k] = Math.Clamp(grade[k], grade[k + 1] - g, grade[k + 1] + g);

            // --- Stückgrenzen (Blockabschnitte): ~260 m, nicht über Wasser/Kreuzungen
            int clear = (int)(40f / ds);
            bool Free(int k)
            {
                for (int j = Math.Max(0, k - clear); j <= Math.Min(count - 1, k + clear); j++) if (water[j]) return false;
                foreach (float c in crossings) if (MathF.Abs(k * ds - c) < 60f) return false;
                return true;
            }
            var cuts = new List<int> { 0 };
            int last = count - 1;
            int step = (int)(RailRules.MainlineChunk / ds), maxStep = (int)(RailRules.MainlineMaxChunk / ds), minRest = (int)(80f / ds);
            int cur = 0;
            while (last - cur > step * 3 / 2)
            {
                int found = -1;
                for (int k = cur + step; k <= Math.Min(cur + maxStep, last - minRest); k++) if (Free(k)) { found = k; break; }
                if (found < 0) for (int k = cur + step - 1; k > cur + minRest; k--) if (Free(k)) { found = k; break; }
                if (found < 0) break;
                cuts.Add(found);
                cur = found;
            }
            cuts.Add(last);

            // --- Bauen (Kasse abgehängt: die Welt zahlt), beide Gleise
            var budget = Tracks.Budget;
            Tracks.Budget = null;
            buildingMainline = true;
            int built = 0;
            try
            {
                built += BuildMainTrack(a, u, right * (RailRules.TrackSpacing * 0.5f), cuts, grade, ds, length, forward: true, out int s1, out int e1);
                built += BuildMainTrack(a, u, -right * (RailRules.TrackSpacing * 0.5f), cuts, grade, ds, length, forward: false, out int s2, out int e2);
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
            MainlineReport = $"Hauptstrecke {length:0} m, {cuts.Count - 1} Blöcke je Gleis, {crossings.Count} Straßenquerungen (Brücke), {built} Gleisstücke";
            return HasMainline;
        }

        int BuildMainTrack(Vector2 a, Vector2 u, Vector2 offset, List<int> cuts, float[] grade, float ds, float length, bool forward,
            out int startNode, out int endNode)
        {
            startNode = endNode = 0;
            var nodes = new List<RoadNode>();
            foreach (int k in cuts)
            {
                var p = a + u * MathF.Min(length, k * ds) + offset;
                nodes.Add(Tracks.AddNode(p, grade[k]));
            }
            int built = 0;
            for (int i = 0; i < cuts.Count - 1; i++)
            {
                int k0 = cuts[i], k1 = cuts[i + 1];
                int n = Math.Clamp(k1 - k0 + 1, 2, 257);
                var profile = new float[n];
                for (int j = 0; j < n; j++)
                {
                    float f = k0 + (k1 - k0) * j / (float)(n - 1);
                    int lo = Math.Min((int)f, grade.Length - 2);
                    profile[j] = grade[lo] + (grade[lo + 1] - grade[lo]) * (f - lo);
                }
                RoadNode from = nodes[i], to = nodes[i + 1];
                if (!forward) { (from, to) = (to, from); Array.Reverse(profile); }
                var seg = Tracks.AddSegment(from, to, (from.Position + to.Position) * 0.5f, RoadCatalog.RailMain, RoadSegmentFlags.Locked, profile);
                if (seg != null) built++;
            }
            startNode = forward ? nodes[0].Id : nodes[nodes.Count - 1].Id;
            endNode = forward ? nodes[nodes.Count - 1].Id : nodes[0].Id;
            return built;
        }

        /// <summary>Kreuzt die Straße die Korridor-Achse? Strecke auf der Achse, Straßenhöhe dort, Winkel (0..90°).</summary>
        static bool CrossesAxis(RoadSegment seg, Vector2 a, Vector2 u, float length, out float along, out float height, out float angle)
        {
            along = height = angle = 0f;
            var b = a + u * length;
            int m = Math.Clamp((int)MathF.Ceiling(seg.Length / 3f), 8, 256);
            var prev = seg.Start.Position;
            for (int j = 1; j <= m; j++)
            {
                var cur = seg.Evaluate(j / (float)m);
                if (Bezier.LineSegmentsIntersect(a, b, prev, cur, out float s, out float v))
                {
                    float t = (j - 1 + v) / m;
                    along = s * length;
                    height = seg.HeightAt(t);
                    angle = RoadGeometry.Angle(u, seg.Tangent(t));
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
