using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Gateways
{
    /// <summary>Eine Richtung, in die die Außenwelt zieht (Nachbarregion, Land, Hauptstadt, ohne Welt: Umland).</summary>
    public sealed class GatewayTarget
    {
        /// <summary>Richtung von der Stadt aus (Einheitsvektor, x = Ost, y = Nord).</summary>
        public Vector2 Direction;
        public float Weight;
        public GatewayTargetKind Kind;
        public string Name = "";
        public int CountryId = -1, RegionId = -1;
        public override string ToString() => $"{Name} ({GatewayRules.Compass(Direction)}, {Weight:0.00})";
    }

    /// <summary>Ergebnis der Erzeugung (für Tests/Anzeige).</summary>
    public sealed class GatewayLayoutReport
    {
        public bool HighwayBuilt;
        public float HighwayAngle;
        public Vector2 Interchange;
        public int Attempts;
        public int SegmentsBuilt;
        public string Failure;
        public override string ToString() =>
            HighwayBuilt ? $"Autobahn {HighwayAngle:0}° · Anschlussstelle ({Interchange.X:0}/{Interchange.Y:0}) · {SegmentsBuilt} Stücke · {Attempts} Versuche"
                         : $"keine Autobahn: {Failure}";
    }

    /// <summary>
    /// Erzeugt beim Kartenstart die Außenanbindung (deterministisch): Ziele aus der Weltkarte (Regionsnachbarn, Hauptstädte,
    /// ohne Welt das Umland), die Autobahnachse mit dem besten Bezug zu den wichtigen Richtungen, darauf eine
    /// Anschlussstelle (Vorlage „Raute“) nahe der Kartenmitte mit Zubringer, geländefolgende Richtungsfahrbahnen bis an
    /// den Kartenrand (automatische Trassierung, Brücke über den Fluss), einen Bahnkorridor quer dazu und
    /// Wasserstraßen an den Stellen, wo Fluss/Meer den Kartenrand berühren.
    /// </summary>
    public static class GatewayGenerator
    {
        // ------------------------------------------------------------ Ziele

        /// <summary>Ziele aus der Weltkarte; ohne Welt vier Umland-Richtungen.</summary>
        public static List<GatewayTarget> Targets(Planet planet)
        {
            var list = new List<GatewayTarget>();
            var start = planet?.StartRegion;
            if (start == null)
            {
                string[] names = { "Umland (Ost)", "Umland (Nord)", "Umland (West)", "Umland (Süd)" };
                Vector2[] dirs = { Vector2.UnitX, Vector2.UnitY, -Vector2.UnitX, -Vector2.UnitY };
                for (int i = 0; i < 4; i++)
                    list.Add(new GatewayTarget { Direction = dirs[i], Weight = i % 2 == 0 ? 1.1f : 1f, Kind = GatewayTargetKind.Hinterland, Name = names[i] });
                return list;
            }
            int player = start.CountryId;
            // Nachbarregionen: fremde Länder und Hauptstadt-Regionen zählen mehr
            foreach (int nb in start.Neighbors)
            {
                if (nb < 0 || nb >= planet.Regions.Count) continue;
                var r = planet.Regions[nb];
                var dir = Bearing(start.Latitude, start.Longitude, r.Latitude, r.Longitude);
                if (dir == Vector2.Zero) continue;
                var country = r.CountryId >= 0 ? planet.Countries[r.CountryId] : null;
                bool foreign = country != null && r.CountryId != player;
                bool capital = country != null && country.CapitalRegionId == r.Id;
                float w = 1f + (foreign ? 1.5f : 0f) + (capital ? 1.5f : 0f) + PopulationWeight(country) * 0.5f;
                list.Add(new GatewayTarget
                {
                    Direction = dir, Weight = w,
                    Kind = foreign ? GatewayTargetKind.Country : GatewayTargetKind.Region,
                    Name = foreign ? country.Name : r.Name,
                    CountryId = r.CountryId, RegionId = r.Id,
                });
            }
            // Hauptstädte anderer Länder (nach Größe und Entfernung), die wichtigsten 6
            var capitals = new List<GatewayTarget>();
            foreach (var c in planet.Countries)
            {
                if (c.Id == player || c.CapitalCell < 0) continue;
                float lat = planet.LatitudeOf(c.CapitalCell), lon = planet.Longitude(planet.XOf(c.CapitalCell));
                var dir = Bearing(start.Latitude, start.Longitude, lat, lon);
                if (dir == Vector2.Zero) continue;
                float km = DistanceKm(start.Latitude, start.Longitude, lat, lon);
                float w = PopulationWeight(c) * Math.Clamp(2500f / MathF.Max(250f, km), 0.15f, 2f);
                capitals.Add(new GatewayTarget
                {
                    Direction = dir, Weight = w, Kind = GatewayTargetKind.Country, Name = c.Name,
                    CountryId = c.Id, RegionId = c.CapitalRegionId,
                });
            }
            capitals.Sort((a, b) => b.Weight != a.Weight ? b.Weight.CompareTo(a.Weight) : a.CountryId.CompareTo(b.CountryId));
            for (int i = 0; i < capitals.Count && i < 6; i++) list.Add(capitals[i]);
            // Eigene Hauptstadt (wenn nicht die Startregion): Inland-Ziel
            var own = player >= 0 ? planet.Countries[player] : null;
            if (own != null && own.CapitalRegionId != start.Id && own.CapitalCell >= 0)
            {
                float lat = planet.LatitudeOf(own.CapitalCell), lon = planet.Longitude(planet.XOf(own.CapitalCell));
                var dir = Bearing(start.Latitude, start.Longitude, lat, lon);
                if (dir != Vector2.Zero)
                    list.Add(new GatewayTarget
                    {
                        Direction = dir, Weight = 2f, Kind = GatewayTargetKind.Region,
                        Name = own.CapitalName ?? own.Name, CountryId = own.Id, RegionId = own.CapitalRegionId,
                    });
            }
            if (list.Count == 0) return Targets(null);
            return list;
        }

        static float PopulationWeight(Country c)
        {
            if (c == null) return 0.5f;
            double pop = c.Economy != null ? c.Economy.Population : c.AreaKm2 * 40.0;
            return Math.Clamp((float)Math.Log10(Math.Max(1.0, pop)) - 5f, 0.3f, 3f);
        }

        /// <summary>Richtung (Ost/Nord) von Punkt 1 zu Punkt 2 auf der Kugel (vereinfachte Peilung).</summary>
        public static Vector2 Bearing(float lat1, float lon1, float lat2, float lon2)
        {
            float dLon = ((lon2 - lon1 + 540f) % 360f) - 180f;
            float midLat = (lat1 + lat2) * 0.5f * MathF.PI / 180f;
            var v = new Vector2(dLon * MathF.Cos(midLat), lat2 - lat1);
            return v.LengthSquared() < 1e-8f ? Vector2.Zero : Vector2.Normalize(v);
        }

        static float DistanceKm(float lat1, float lon1, float lat2, float lon2)
        {
            double r = Planet.EarthCircumferenceKm / (2.0 * Math.PI);
            double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180, dl = (lon2 - lon1) * Math.PI / 180;
            double c = Math.Sin(p1) * Math.Sin(p2) + Math.Cos(p1) * Math.Cos(p2) * Math.Cos(dl);
            return (float)(Math.Acos(Math.Clamp(c, -1.0, 1.0)) * r);
        }

        /// <summary>Bestes Ziel in Richtung <paramref name="dir"/> (Gewicht × Ausrichtung); null, wenn keins vorn liegt.</summary>
        public static GatewayTarget BestTarget(List<GatewayTarget> targets, Vector2 dir, out float importance)
        {
            GatewayTarget best = null;
            float bestScore = 0f;
            importance = 0f;
            foreach (var t in targets)
            {
                float cos = Vector2.Dot(t.Direction, dir);
                if (cos < 0.35f) continue;
                float s = t.Weight * cos;
                importance += t.Weight * cos * cos;
                if (s > bestScore) { bestScore = s; best = t; }
            }
            return best;
        }

        /// <summary>Wie gut passt die Achse (beide Richtungen) zu den Zielen?</summary>
        static float AxisScore(List<GatewayTarget> targets, Vector2 u)
        {
            float s = 0f;
            foreach (var t in targets)
            {
                float c = Vector2.Dot(t.Direction, u);
                s += t.Weight * c * c * c * c; // beide Richtungen, scharf gebündelt
            }
            return s;
        }

        /// <summary>Kandidaten-Winkel (Grad, 0 = Ost, gegen den Uhrzeigersinn) nach Eignung sortiert.</summary>
        public static List<float> AxisCandidates(List<GatewayTarget> targets, float stepDegrees = 15f)
        {
            var list = new List<(float angle, float score, int k)>();
            int n = (int)MathF.Round(180f / stepDegrees);
            for (int k = 0; k < n; k++)
            {
                float a = k * stepDegrees;
                list.Add((a, AxisScore(targets, Dir(a)), k));
            }
            list.Sort((x, y) => MathF.Abs(x.score - y.score) > 1e-4f ? y.score.CompareTo(x.score) : x.k.CompareTo(y.k));
            var result = new List<float>();
            foreach (var e in list) result.Add(e.angle);
            return result;
        }

        public static Vector2 Dir(float degrees)
        {
            float r = degrees * MathF.PI / 180f;
            // Auf ganze Achsen runden, damit 0°/90° exakt achsparallel sind (sauberes Kartenbild, stabile Tests)
            float c = MathF.Cos(r), s = MathF.Sin(r);
            if (MathF.Abs(c) < 1e-6f) c = 0f;
            if (MathF.Abs(s) < 1e-6f) s = 0f;
            return new Vector2(c, s);
        }

        // ------------------------------------------------------------ Geometrie der Karte

        /// <summary>Parameter s, bei dem die Gerade p + s·d das (eingerückte) Kartenquadrat verlässt (s &gt; 0).</summary>
        public static float ExitDistance(Vector2 p, Vector2 d, float half)
        {
            float s = float.MaxValue;
            if (d.X > 1e-6f) s = MathF.Min(s, (half - p.X) / d.X);
            if (d.X < -1e-6f) s = MathF.Min(s, (-half - p.X) / d.X);
            if (d.Y > 1e-6f) s = MathF.Min(s, (half - p.Y) / d.Y);
            if (d.Y < -1e-6f) s = MathF.Min(s, (-half - p.Y) / d.Y);
            return s;
        }

        static bool Inside(Vector2 p, float half) => MathF.Abs(p.X) <= half && MathF.Abs(p.Y) <= half;

        // ------------------------------------------------------------ Autobahn planen

        /// <summary>Ein geplantes Stück: entweder ein Vorlagen-Teil oder ein Fahrbahnstück zwischen zwei Stückgrenzen.</summary>
        sealed class Piece
        {
            public Vector2 P0, Control, P2;
            public float H0, H2;
            public RoadType Type;
            public bool FollowTerrain;
            public RoadSegmentFlags Flags;
        }

        sealed class HighwayPlan
        {
            public float Angle;
            public Vector2 Origin;
            public readonly List<Piece> Pieces = new List<Piece>();
            /// <summary>Enden am Kartenrand: A = +u-Seite, B = −u-Seite (Einfahrt/Ausfahrt je Seite).</summary>
            public Vector2 EntryA, ExitA, EntryB, ExitB;
            /// <summary>Zubringer-Enden (offene Stadtstraßen-Knoten der Anschlussstelle).</summary>
            public Vector2 StubA, StubB;
        }

        const float DiamondE = 400f, DiamondS = 12f, DiamondC = 52f;

        /// <summary>
        /// Baut die Autobahn (beide Richtungsfahrbahnen, Anschlussstelle, Zubringer) und legt die Gateways an.
        /// Kosten trägt die Welt: die Stadtkasse ist während des Baus abgehängt.
        /// </summary>
        internal static GatewayLayoutReport BuildHighway(SimulationWorld world, GatewaySystem gateways, List<GatewayTarget> targets)
        {
            var report = new GatewayLayoutReport();
            var roads = world.Roads;
            float half = world.Settings.SizeMeters * 0.5f;
            if (half < DiamondE + 300f) { report.Failure = "Karte zu klein für Autobahn + Anschlussstelle"; return report; }

            HighwayPlan plan = null;
            foreach (float angle in AxisCandidates(targets))
            {
                var u = Dir(angle);
                var n = new Vector2(-u.Y, u.X);
                foreach (var (a, b) in OriginOffsets(half))
                {
                    var origin = u * a + n * b;
                    report.Attempts++;
                    plan = TryPlan(world, angle, origin, half);
                    if (plan != null) break;
                }
                if (plan != null) break;
            }
            if (plan == null) { report.Failure = "keine baubare Trasse gefunden"; return report; }

            // Bauen: Kasse abhängen (die Welt zahlt), Stücke mit Einrasten neu planen
            var budget = roads.Budget;
            roads.Budget = null;
            try
            {
                foreach (var p in plan.Pieces)
                {
                    var rp = RoadBuilder.Plan(roads, p.P0, 0.5f, p.Control, p.P2, 0.5f, p.Type, p.H0, p.H2, p.Flags, p.FollowTerrain);
                    if (!rp.IsValid && rp.Error != RoadBuildError.Duplicate)
                    {
                        report.Failure ??= $"Stück nicht baubar: {rp.Error}";
                        continue;
                    }
                    report.SegmentsBuilt += RoadBuilder.Build(roads, rp).Count;
                }
            }
            finally { roads.Budget = budget; }

            report.HighwayBuilt = report.SegmentsBuilt > 0;
            report.HighwayAngle = plan.Angle;
            report.Interchange = plan.Origin;
            if (!report.HighwayBuilt) return report;

            gateways.AddHighway(plan.Angle, plan.Origin, plan.EntryA, plan.ExitA, plan.EntryB, plan.ExitB, plan.StubA, plan.StubB, targets);
            return report;
        }

        /// <summary>Lage der Anschlussstelle relativ zur Mitte (entlang u, seitlich n): bevorzugt seitlich versetzt, Mitte bleibt frei.</summary>
        static IEnumerable<(float along, float side)> OriginOffsets(float half)
        {
            float o = GatewayRules.PreferredInterchangeOffset;
            float[] sides = { -o, o, -o * 0.5f, o * 0.5f, 0f, -o * 1.5f, o * 1.5f };
            float[] alongs = { 0f, -200f, 200f, -400f, 400f };
            foreach (float s in sides)
                foreach (float a in alongs)
                    yield return (a, s);
        }

        static HighwayPlan TryPlan(SimulationWorld world, float angle, Vector2 origin, float half)
        {
            var roads = world.Roads;
            var ground = roads.Ground;
            var u = Dir(angle);
            var n = new Vector2(-u.Y, u.X);
            float inner = half - 60f;
            // Vorlage muss ganz in der Karte liegen
            foreach (var c in new[] { origin + u * DiamondE + n * DiamondE, origin + u * DiamondE - n * DiamondE,
                                      origin - u * DiamondE + n * DiamondE, origin - u * DiamondE - n * DiamondE })
                if (!Inside(c, inner)) return null;

            var plan = new HighwayPlan { Angle = angle, Origin = origin };
            float edge = half - GatewayRules.EdgeInset;

            // Anschlussstelle (Raute): Autobahn + Rampen + Brücke gehören der Welt, die äußeren Querstraßen-Stücke sind der Zubringer
            var tp = RoadTemplates.Plan(roads, RoadTemplateKind.DiamondInterchange, origin, angle, RoadCatalog.Highway2, RoadCatalog.CityTwoLane);
            if (!tp.IsValid) return null;
            foreach (var p in tp.Pieces)
            {
                bool stub = p.Type.Category == RoadCategory.City &&
                            (MathF.Abs(Vector2.Dot(p.P0 - origin, n)) > DiamondE - 1f || MathF.Abs(Vector2.Dot(p.P2 - origin, n)) > DiamondE - 1f);
                plan.Pieces.Add(new Piece
                {
                    P0 = p.P0, Control = p.Control, P2 = p.P2, H0 = p.H0, H2 = p.H2, Type = p.Type, FollowTerrain = p.FollowTerrain,
                    Flags = stub ? RoadSegmentFlags.None : RoadSegmentFlags.Locked,
                });
            }
            if (!BridgeClears(roads, plan.Pieces, origin, u, n)) return null;
            plan.StubA = origin + n * DiamondE;
            plan.StubB = origin - n * DiamondE;
            // Zubringer-Enden auf Land
            if (ground.IsWater(plan.StubA) || ground.IsWater(plan.StubB)) return null;

            // Richtungsfahrbahnen außerhalb der Vorlage: rechts (+u) bei −S, links (−u) bei +S
            var right = -n * DiamondS;
            var left = n * DiamondS;
            // +u-Seite: rechte Fahrbahn hinaus (Ausfahrt A), linke herein (Einfahrt A)
            if (!PlanCarriageway(plan, roads, origin + u * DiamondE + right, u, edge, outward: true, out plan.ExitA)) return null;
            if (!PlanCarriageway(plan, roads, origin + u * DiamondE + left, u, edge, outward: false, out plan.EntryA)) return null;
            // −u-Seite: linke Fahrbahn hinaus (Ausfahrt B), rechte herein (Einfahrt B)
            if (!PlanCarriageway(plan, roads, origin - u * DiamondE + left, -u, edge, outward: true, out plan.ExitB)) return null;
            if (!PlanCarriageway(plan, roads, origin - u * DiamondE + right, -u, edge, outward: false, out plan.EntryB)) return null;
            return plan;
        }

        /// <summary>
        /// Die Vorlage prüft ihre Teile nur gegen den Bestand, nicht untereinander: am Hang kann die Brücke der Querstraße
        /// (8 m über ihren Enden) der geländefolgenden Autobahn zu nahe kommen. Hier vorab: lichte Höhe an beiden Fahrbahnen.
        /// </summary>
        static bool BridgeClears(RoadNetwork roads, List<Piece> pieces, Vector2 origin, Vector2 u, Vector2 n)
        {
            Piece bridge = null;
            foreach (var p in pieces)
                if (p.Type.Category == RoadCategory.City && MathF.Abs(Vector2.Dot(p.P0 - origin, n)) < DiamondC + 1f
                    && MathF.Abs(Vector2.Dot(p.P2 - origin, n)) < DiamondC + 1f) bridge = p;
            if (bridge == null) return true;
            foreach (var p in pieces)
            {
                if (!p.Type.IsHighway) continue;
                float a = Vector2.Dot(p.P0 - origin, u), b = Vector2.Dot(p.P2 - origin, u);
                if (MathF.Min(a, b) > 0f || MathF.Max(a, b) < 0f) continue; // kreuzt die Querachse nicht
                float t = MathF.Abs(b - a) > 1e-3f ? -a / (b - a) : 0.5f;
                var rp = RoadBuilder.Plan(roads, p.P0, 0f, p.Control, p.P2, 0f, p.Type, p.H0, p.H2, p.Flags, p.FollowTerrain);
                if (!rp.IsValid) return false;
                float road = rp.HeightAt(t);
                var cross = Bezier.Evaluate(p.P0, p.Control, p.P2, t);
                float s = Vector2.Dot(cross - bridge.P0, bridge.P2 - bridge.P0) / MathF.Max(1e-3f, (bridge.P2 - bridge.P0).LengthSquared());
                float deck = bridge.H0 + (bridge.H2 - bridge.H0) * Math.Clamp(s, 0f, 1f);
                if (deck - road < RoadBuilder.Clearance + 0.5f) return false;
            }
            return true;
        }

        /// <summary>
        /// Plant eine Richtungsfahrbahn vom Ende der Anschlussstelle <paramref name="from"/> in Richtung <paramref name="d"/>
        /// bis an den Kartenrand, in Stücke geteilt (Stückgrenzen abseits vom Wasser, Höhen auf einer geglätteten,
        /// steigungsbegrenzten Gradiente). <paramref name="outward"/>: Fahrtrichtung zum Rand (sonst vom Rand herein).
        /// </summary>
        static bool PlanCarriageway(HighwayPlan plan, RoadNetwork roads, Vector2 from, Vector2 d, float edge, bool outward, out Vector2 end)
        {
            var ground = roads.Ground;
            end = default;
            float length = ExitDistance(from, d, edge);
            if (length < 80f || length > 5000f) return false;
            end = from + d * length;
            // Ende (und das Stück davor) auf Land – kein Autobahnende im Meer
            if (ground.IsWater(end) || ground.IsWater(from + d * (length - 60f))) return false;

            // Abtastung + Gradiente
            const float ds = 4f;
            int count = (int)MathF.Ceiling(length / ds) + 1;
            var water = new bool[count];
            var raw = new float[count];
            for (int k = 0; k < count; k++)
            {
                var p = from + d * MathF.Min(length, k * ds);
                water[k] = ground.IsWater(p);
                raw[k] = water[k] ? ground.WaterLevel + Profiles.WaterClearance : ground.HeightAt(p);
            }
            int win = Math.Max(1, (int)(GatewayRules.GradeSmoothing / ds));
            var smooth = BoxSmooth(raw, win);
            var grade = new float[count];
            grade[0] = ground.HeightAt(from); // Anschluss an die Vorlage (ebenerdig)
            float g = GatewayRules.GradeSlope * ds;
            for (int k = 1; k < count; k++) grade[k] = Math.Clamp(smooth[k], grade[k - 1] - g, grade[k - 1] + g);

            // Stückgrenzen: alle ~260 m, mindestens 150 m vom Wasser entfernt
            int clear = (int)(150f / ds);
            bool Dry(int k)
            {
                for (int j = Math.Max(0, k - clear); j <= Math.Min(count - 1, k + clear); j++) if (water[j]) return false;
                return true;
            }
            var cuts = new List<int> { 0 };
            int last = count - 1;
            int step = (int)(GatewayRules.ChunkLength / ds), maxStep = (int)(GatewayRules.MaxChunkLength / ds), minRest = (int)(60f / ds);
            int cur = 0;
            while (last - cur > step * 3 / 2)
            {
                int found = -1;
                for (int k = cur + step; k <= Math.Min(cur + maxStep, last - minRest); k += 2)
                    if (Dry(k)) { found = k; break; }
                if (found < 0)
                    for (int k = cur + step - 2; k > cur + minRest; k -= 2)
                        if (Dry(k)) { found = k; break; }
                if (found < 0)
                {
                    if (last - cur <= maxStep) break; // Rest in einem Stück (über das Wasser)
                    return false;
                }
                cuts.Add(found);
                cur = found;
            }
            cuts.Add(last);

            for (int i = 0; i < cuts.Count - 1; i++)
            {
                var a = from + d * MathF.Min(length, cuts[i] * ds);
                var b = from + d * MathF.Min(length, cuts[i + 1] * ds);
                float ha = grade[cuts[i]], hb = grade[cuts[i + 1]];
                // Fahrtrichtung: hinaus = von der Anschlussstelle zum Rand, herein = umgekehrt
                var piece = outward
                    ? new Piece { P0 = a, P2 = b, H0 = ha, H2 = hb }
                    : new Piece { P0 = b, P2 = a, H0 = hb, H2 = ha };
                piece.Control = (piece.P0 + piece.P2) * 0.5f;
                piece.Type = RoadCatalog.Highway2;
                piece.FollowTerrain = true;
                piece.Flags = RoadSegmentFlags.Locked;
                var rp = RoadBuilder.Plan(roads, piece.P0, 0f, piece.Control, piece.P2, 0f, piece.Type, piece.H0, piece.H2, piece.Flags, true);
                if (!rp.IsValid) return false;
                plan.Pieces.Add(piece);
            }
            return true;
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

        // ------------------------------------------------------------ Bahnkorridor

        /// <summary>
        /// Lage einer Bahntrasse quer durch die Karte (gerade, freigehalten): Achse mit dem besten Bezug zu den Zielen,
        /// mindestens 45° gegen die Autobahn verdreht, mit Abstand zur Anschlussstelle; Enden auf Land.
        /// </summary>
        internal static bool PlanRail(SimulationWorld world, List<GatewayTarget> targets, float highwayAngle, Vector2 interchange,
            out Vector2 a, out Vector2 b, out float angle)
        {
            a = b = default;
            angle = 0f;
            var ground = world.Roads.Ground;
            float half = world.Settings.SizeMeters * 0.5f;
            float edge = half - GatewayRules.EdgeInset;
            var hu = Dir(highwayAngle);
            foreach (float cand in AxisCandidates(targets))
            {
                float diff = MathF.Abs(((cand - highwayAngle) % 180f + 180f) % 180f);
                if (diff < 45f || diff > 135f) continue;
                var u = Dir(cand);
                var n = new Vector2(-u.Y, u.X);
                // Linie durch einen Punkt auf der Autobahnachse, weit genug von der Anschlussstelle (beide Seiten probieren)
                foreach (float along in new[] { 620f, -620f, 800f, -800f, 450f, -450f })
                {
                    var through = interchange + hu * along;
                    if (!Inside(through, half - 100f)) continue;
                    float sp = ExitDistance(through, u, edge), sm = ExitDistance(through, -u, edge);
                    var pa = through + u * sp;
                    var pb = through - u * sm;
                    if (ground.IsWater(pa) || ground.IsWater(pb)) continue;
                    // Abstand zur Anschlussstelle (Mitte der Vorlage) von der Linie
                    float dist = MathF.Abs(Vector2.Dot(interchange - through, n));
                    if (dist < GatewayRules.RailClearanceFromInterchange) continue;
                    // höchstens ein Viertel über Wasser (Fluss ja, Meer nein)
                    int wet = 0, total = 0;
                    for (float s = -sm; s <= sp; s += 16f) { total++; if (ground.IsWater(through + u * s)) wet++; }
                    if (wet * 4 > total) continue;
                    a = pa; b = pb; angle = cand;
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------ Wasserstraßen

        /// <summary>Wasser am Kartenrand: Lauf entlang einer Kante (Mitte, Breite, Tiefe, Richtung nach außen, Meer?).</summary>
        internal struct WaterRun
        {
            public Vector2 Center, Outward;
            public float Width, Depth;
            public bool Sea;
        }

        internal static List<WaterRun> WaterAtEdges(SimulationWorld world)
        {
            var result = new List<WaterRun>();
            var ground = world.Roads.Ground;
            float half = world.Settings.SizeMeters * 0.5f;
            float e = half - 4f;
            var edges = new[]
            {
                (new Vector2(-e, e), new Vector2(1, 0), new Vector2(0, 1)),   // Nord
                (new Vector2(-e, -e), new Vector2(1, 0), new Vector2(0, -1)), // Süd
                (new Vector2(e, -e), new Vector2(0, 1), new Vector2(1, 0)),   // Ost
                (new Vector2(-e, -e), new Vector2(0, 1), new Vector2(-1, 0)), // West
            };
            const float step = 4f;
            int count = (int)(2f * e / step) + 1;
            WaterRun bestSea = default;
            bool haveSea = false;
            foreach (var (start, along, outward) in edges)
            {
                WaterRun bestRiver = default;
                bool haveRiver = false;
                int runStart = -1;
                float minH = 0f;
                for (int k = 0; k <= count; k++)
                {
                    bool wet = k < count && ground.IsWater(start + along * (k * step));
                    if (wet)
                    {
                        float h = ground.HeightAt(start + along * (k * step));
                        if (runStart < 0) { runStart = k; minH = h; }
                        else minH = MathF.Min(minH, h);
                        continue;
                    }
                    if (runStart < 0) continue;
                    float width = (k - runStart) * step;
                    var center = start + along * ((runStart + k - 1) * 0.5f * step);
                    var run = new WaterRun
                    {
                        Center = center, Outward = outward, Width = width, Depth = MathF.Max(0f, ground.WaterLevel - minH),
                        Sea = width >= 300f,
                    };
                    if (run.Sea) { if (!haveSea || run.Width > bestSea.Width) { bestSea = run; haveSea = true; } }
                    else if (width >= 12f && (!haveRiver || width > bestRiver.Width)) { bestRiver = run; haveRiver = true; }
                    runStart = -1;
                }
                if (haveRiver) result.Add(bestRiver);
            }
            if (haveSea) result.Add(bestSea);
            return result;
        }
    }
}
