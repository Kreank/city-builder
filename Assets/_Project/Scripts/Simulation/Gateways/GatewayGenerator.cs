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
        /// <summary>Ausgebaute Zustände der Trassensuche (Autobahn + Bahn, Rechenaufwand).</summary>
        public int Expansions;
        /// <summary>Mittellinie der Autobahn (Rand B → Anschlussstelle → Rand A), nur in der Sitzung der Erzeugung.</summary>
        public List<Vector2> Centerline;
        /// <summary>Verworfene Kandidaten: Trassensuche ohne Weg / Fahrbahnstück nicht baubar (letzter Fehler).</summary>
        public int NoRoute, NotBuildable, TemplateRejected;
        /// <summary>Rechenzeit der Trassierung (Autobahn planen + bauen, Bahn planen), nur Bericht.</summary>
        public long Milliseconds;
        /// <summary>Gründe je verworfenem Kandidaten ohne Trasse (Fehlersuche).</summary>
        public List<string> Log = new List<string>();
        public RoadBuildError LastPieceError;
        public string Failure;
        public override string ToString() =>
            HighwayBuilt ? $"Autobahn {HighwayAngle:0}° · Anschlussstelle ({Interchange.X:0}/{Interchange.Y:0}) · {SegmentsBuilt} Stücke · {Attempts} Versuche · {Expansions} Suchschritte · {Milliseconds} ms"
                         : $"keine Autobahn: {Failure} ({Attempts} Versuche, {TemplateRejected} Anschlussstelle unpassend, {NoRoute} ohne Weg ({Expansions} Suchschritte), {NotBuildable} nicht baubar: {LastPieceError})";
    }

    /// <summary>
    /// Erzeugt beim Kartenstart die Außenanbindung (deterministisch): Ziele aus der Weltkarte (Regionsnachbarn, Hauptstädte,
    /// ohne Welt das Umland), die Autobahnachse mit dem besten Bezug zu den wichtigen Richtungen, darauf eine
    /// Anschlussstelle (Vorlage „Raute“) nahe der Kartenmitte mit Zubringer, geschwungene Richtungsfahrbahnen bis an den
    /// Kartenrand (Trassensuche <see cref="RoutePlanner"/>: weite Bögen, Wasser fast rechtwinklig an schmaler Stelle, Gelände
    /// ausweichen; Höhen über die automatische Trassierung), eine Fernbahntrasse, die die Autobahn einmal fast rechtwinklig
    /// kreuzt, und Wasserstraßen an den Stellen, wo Fluss/Meer den Kartenrand berühren.
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
            int player = planet.PlayerCountryId; // Stadtstaat oder Land der Startregion (newgame)
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
                    // freies Land (Region ohne Land, s. newgame): Umland-Ziel mit Regionsnamen
                    Kind = foreign ? GatewayTargetKind.Country : r.IsFree ? GatewayTargetKind.Hinterland : GatewayTargetKind.Region,
                    Name = foreign ? country.Name : r.IsFree ? $"{r.Name} (freies Land)" : r.Name,
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

        /// <summary>Achsen der Anschlussstelle: erst die 15°-Schritte nach Eignung, dann die Zwischenwinkel (7,5°) nach Eignung.</summary>
        static List<float> HighwayCandidates(List<GatewayTarget> targets)
        {
            var list = AxisCandidates(targets);
            foreach (float a in AxisCandidates(targets, 7.5f))
                if (!list.Contains(a)) list.Add(a);
            return list;
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
            /// <summary>Mittellinie von Rand B über die Anschlussstelle zu Rand A (für die Bahn und Prüfungen).</summary>
            public List<Vector2> Centerline;
            public int Expansions;
            public RoadBuildError LastError;
        }

        const float DiamondE = 400f, DiamondS = 12f, DiamondC = 52f;

        /// <summary>
        /// Baut die Autobahn (beide Richtungsfahrbahnen, Anschlussstelle, Zubringer) und legt die Gateways an.
        /// Achse der Anschlussstelle wie bisher nach den Zielen; von ihren Enden aus sucht <see cref="RoutePlanner"/> je Seite eine
        /// Trasse bis an den Kartenrand (Bögen R ≥ 750 m, Wasser gerade und fast rechtwinklig, Gelände ausweichen).
        /// Kosten trägt die Welt: die Stadtkasse ist während des Baus abgehängt.
        /// </summary>
        internal static GatewayLayoutReport BuildHighway(SimulationWorld world, GatewaySystem gateways, List<GatewayTarget> targets, RouteField field)
        {
            var report = new GatewayLayoutReport();
            var roads = world.Roads;
            float half = world.Settings.SizeMeters * 0.5f;
            if (half < DiamondE + 300f) { report.Failure = "Karte zu klein für Autobahn + Anschlussstelle"; return report; }

            HighwayPlan plan = null;
            foreach (float angle in HighwayCandidates(targets))
            {
                var u = Dir(angle);
                var n = new Vector2(-u.Y, u.X);
                foreach (var (a, b) in OriginOffsets(half))
                {
                    var origin = u * a + n * b;
                    report.Attempts++;
                    plan = TryPlan(world, field, angle, origin, half, report);
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
            report.Centerline = plan.Centerline;
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

        static HighwayPlan TryPlan(SimulationWorld world, RouteField field, float angle, Vector2 origin, float half, GatewayLayoutReport report)
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
            // Die gerade Anschlussstelle selbst quert kein Wasser (sonst längs über den Fluss)
            for (float s = -DiamondE; s <= DiamondE; s += 16f)
                if (field.Shore(origin + u * s) < 20f) { report.TemplateRejected++; return null; }

            var plan = new HighwayPlan { Angle = angle, Origin = origin };
            float edge = half - GatewayRules.EdgeInset;

            // Anschlussstelle (Raute): Autobahn + Rampen + Brücke gehören der Welt, die äußeren Querstraßen-Stücke sind der Zubringer
            var tp = RoadTemplates.Plan(roads, RoadTemplateKind.DiamondInterchange, origin, angle, RoadCatalog.Highway2, RoadCatalog.CityTwoLane);
            if (!tp.IsValid) { report.TemplateRejected++; return null; }
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
            if (!BridgeClears(roads, plan.Pieces, origin, u, n)) { report.TemplateRejected++; return null; }
            plan.StubA = origin + n * DiamondE;
            plan.StubB = origin - n * DiamondE;
            // Zubringer-Enden auf Land
            if (ground.IsWater(plan.StubA) || ground.IsWater(plan.StubB)) { report.TemplateRejected++; return null; }

            // Mittellinie je Seite: Trassensuche von den Enden der Anschlussstelle bis an den Rand
            var centerA = PlanHalf(field, origin + u * DiamondE, u, edge, report);
            if (centerA == null) { report.NoRoute++; report.Log?.Add($"{angle:0}° {origin} A: Ufer {field.Shore(origin + u * DiamondE):0} m"); return null; }
            var centerB = PlanHalf(field, origin - u * DiamondE, -u, edge, report);
            if (centerB == null) { report.NoRoute++; report.Log?.Add($"{angle:0}° {origin} B: Ufer {field.Shore(origin - u * DiamondE):0} m"); return null; }

            // Richtungsfahrbahnen: hinaus rechts der Mittellinie, herein links (Rechtsverkehr)
            if (!PlanCarriageways(plan, roads, centerA, edge, out plan.ExitA, out plan.EntryA)) { report.NotBuildable++; report.LastPieceError = plan.LastError; return null; }
            if (!PlanCarriageways(plan, roads, centerB, edge, out plan.ExitB, out plan.EntryB)) { report.NotBuildable++; report.LastPieceError = plan.LastError; return null; }

            var line = new List<Vector2>();
            var sb = RoutePlanner.Sample(ReverseAll(centerB), 24f);
            line.AddRange(sb);
            line.AddRange(RoutePlanner.Sample(centerA, 24f));
            plan.Centerline = line;
            return plan;
        }

        static List<RoutePiece> ReverseAll(List<RoutePiece> pieces)
        {
            var list = new List<RoutePiece>(pieces.Count);
            for (int i = pieces.Count - 1; i >= 0; i--) list.Add(RoutePlanner.Reverse(pieces[i]));
            return list;
        }

        /// <summary>Mittellinie einer Autobahnseite (Ende der Anschlussstelle → Kartenrand) als Stücke; null = keine Trasse.</summary>
        static List<RoutePiece> PlanHalf(RouteField field, Vector2 start, Vector2 d, float edge, GatewayLayoutReport report)
        {
            float length = ExitDistance(start, d, edge);
            if (length < 80f || length == float.MaxValue) return null;
            var spec = new RouteSpec
            {
                Radius = RouteRules.HighwayRadius, MaxGrade = GatewayRules.GradeSlope, WaterCost = RouteRules.HighwayWaterCost,
                StraightNearWater = RouteRules.HighwayStraightNearWater, Edge = edge, IdealExit = start + d * length,
            };
            var steps = RoutePlanner.Plan(field, spec, start, MathF.Atan2(d.Y, d.X), out int expansions);
            report.Expansions += expansions;
            if (steps == null) return null;
            // Stückgrenzen nur abseits vom Wasser (Brücke + Rampen in einem Stück)
            return RoutePlanner.ToPieces(steps, GatewayRules.ChunkLength, GatewayRules.MaxChunkLength, 15f,
                p => field.Shore(p) >= RouteRules.HighwayCutClearance);
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
        /// Beide Richtungsfahrbahnen einer Seite aus der Mittellinie (Stücke von der Anschlussstelle zum Rand): hinaus rechts,
        /// herein links im Abstand <see cref="DiamondS"/>, Enden genau am Rand, Höhen der Stückgrenzen auf einer geglätteten,
        /// steigungsbegrenzten Gradiente; jedes Stück wird vorab geprüft.
        /// </summary>
        static bool PlanCarriageways(HighwayPlan plan, RoadNetwork roads, List<RoutePiece> center, float edge, out Vector2 exit, out Vector2 entry)
        {
            exit = entry = default;
            var outward = new List<RoutePiece>();
            var inward = new List<RoutePiece>(); // noch in Richtung hinaus geordnet
            foreach (var p in center)
            {
                outward.Add(RoutePlanner.Offset(p, -DiamondS));
                inward.Add(RoutePlanner.Offset(p, DiamondS));
            }
            RoutePlanner.ClampEndToEdge(outward, edge, true);
            RoutePlanner.ClampEndToEdge(inward, edge, true);
            var hOut = Gradient(roads.Ground, outward);
            var hIn = Gradient(roads.Ground, inward);
            for (int i = 0; i < outward.Count; i++)
                if (!AddPiece(plan, roads, outward[i].P0, outward[i].C, outward[i].P2, hOut[i], hOut[i + 1])) return false;
            for (int i = inward.Count - 1; i >= 0; i--)
                if (!AddPiece(plan, roads, inward[i].P2, inward[i].C, inward[i].P0, hIn[i + 1], hIn[i])) return false;
            exit = outward[outward.Count - 1].P2;
            entry = inward[inward.Count - 1].P2;
            return true;
        }

        static bool AddPiece(HighwayPlan plan, RoadNetwork roads, Vector2 a, Vector2 c, Vector2 b, float ha, float hb)
        {
            var piece = new Piece { P0 = a, Control = c, P2 = b, H0 = ha, H2 = hb, Type = RoadCatalog.Highway2, FollowTerrain = true, Flags = RoadSegmentFlags.Locked };
            var rp = RoadBuilder.Plan(roads, piece.P0, 0f, piece.Control, piece.P2, 0f, piece.Type, piece.H0, piece.H2, piece.Flags, true);
            if (!rp.IsValid) { plan.LastError = rp.Error; return false; }
            plan.Pieces.Add(piece);
            return true;
        }

        /// <summary>Höhen an den Stückgrenzen: geglättetes Gelände (über Wasser die Mindesthöhe der Fahrrinne), Steigung begrenzt.</summary>
        static float[] Gradient(IGround ground, List<RoutePiece> pieces)
        {
            var starts = new List<int>();
            var pts = RoutePlanner.Sample(pieces, 4f, starts);
            int count = pts.Count;
            var raw = new float[count];
            for (int k = 0; k < count; k++)
                raw[k] = ground.IsWater(pts[k]) ? Profiles.WaterFloor(ground, pts[k]) : ground.HeightAt(pts[k]); // Wasserstraße: Fahrrinne verlangt lichte Höhe
            var smooth = BoxSmooth(raw, Math.Max(1, (int)(GatewayRules.GradeSmoothing / 4f)));
            var grade = new float[count];
            grade[0] = ground.HeightAt(pts[0]); // Anschluss an die Vorlage (ebenerdig)
            for (int k = 1; k < count; k++)
            {
                float g = GatewayRules.GradeSlope * Vector2.Distance(pts[k - 1], pts[k]);
                grade[k] = Math.Clamp(smooth[k], grade[k - 1] - g, grade[k - 1] + g);
            }
            var h = new float[pieces.Count + 1];
            for (int i = 0; i <= pieces.Count; i++) h[i] = grade[Math.Min(count - 1, starts[i])];
            return h;
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

        // ------------------------------------------------------------ Bahn

        /// <summary>Geplante Fernbahn: Mittellinie von Rand A nach Rand B, Kreuzungspunkt mit der Autobahn.</summary>
        internal sealed class RailRoute
        {
            public List<RoutePiece> Pieces;
            public Vector2 A, B, Through;
            public float Angle;
            public int Expansions;
            public string Report = "";
        }

        /// <summary>
        /// Trasse der Fernbahn: kreuzt die Autobahn einmal, fast rechtwinklig (≤ 30° schief) und mit Abstand zur Anschlussstelle,
        /// Richtung nach den Zielen; von dort je Seite Trassensuche bis an den Rand (R ≥ 1 000 m, 2 %, Wasser gerade und fast
        /// rechtwinklig, Abstand zur Autobahn). Ohne Autobahn quer durch die Mitte.
        /// </summary>
        internal static RailRoute PlanRail(SimulationWorld world, RouteField field, List<GatewayTarget> targets, IReadOnlyList<Vector2> highwayLine,
            Vector2 interchange)
        {
            float half = world.Settings.SizeMeters * 0.5f;
            float edge = half - GatewayRules.EdgeInset;
            bool hasHighway = highwayLine != null && highwayLine.Count >= 2;
            var axis = hasHighway ? highwayLine : new List<Vector2> { new Vector2(-half, 0f), new Vector2(half, 0f) };
            if (hasHighway)
            {
                field.AddNearLine(highwayLine, RouteRules.RailHighwayClearance, RouteRules.RailHighwayCost);
                field.AddNearPoint(interchange, RouteRules.RailInterchangeClearance, RouteRules.RailInterchangeCost);
            }
            var result = new RailRoute();
            int searched = 0, noRoute = 0, recrossing = 0, noStation = 0;
            // Bezug der Achsen zu den Zielen (Maßstab für die Richtung der Bahn)
            float bestAxis = 1e-6f;
            for (int k = 0; k < 36; k++) bestAxis = MathF.Max(bestAxis, AxisScore(targets, Dir(k * 5f)));
            // Kandidaten: Kreuzungspunkt entlang der Autobahn × Richtung (senkrecht ± 10/20/30°), bewertet nach Bezug zu den
            // Zielen, Rechtwinkligkeit und Nähe zur Kartenmitte (Bahnhof nahe der Startkachel)
            var candidates = new List<(Vector2 through, float angle, float along, float score, int order)>();
            int order = 0;
            foreach (float along in new[] { 620f, -620f, 800f, -800f, 1000f, -1000f })
            {
                var through = PointAlong(axis, interchange, along, out var tangent);
                if (!Inside(through, half - 100f) || field.Shore(through) < RouteRules.RailStraightNearWater) continue;
                float perp = MathF.Atan2(tangent.Y, tangent.X) * 180f / MathF.PI + 90f;
                foreach (float off in new[] { 0f, 10f, -10f, 20f, -20f, 30f, -30f })
                {
                    float a = ((perp + off) % 180f + 180f) % 180f;
                    var u = Dir(a);
                    var n = new Vector2(-u.Y, u.X);
                    if (MathF.Abs(Vector2.Dot(interchange - through, n)) < GatewayRules.RailClearanceFromInterchange) continue;
                    float centerDist = MathF.Abs(Vector2.Dot(through, n)); // Abstand der Geraden zur Kartenmitte
                    float score = AxisScore(targets, u) / bestAxis - 0.5f * MathF.Abs(off) / RouteRules.RailCrossingMaxSkew
                                  - 0.6f * MathF.Min(1f, centerDist / MathF.Min(half, 1500f));
                    candidates.Add((through, a, along, score, order++));
                }
            }
            candidates.Sort((x, y) => MathF.Abs(x.score - y.score) > 1e-4f ? y.score.CompareTo(x.score) : x.order.CompareTo(y.order));
            // bevorzugt Trassen mit Platz für einen Bahnhof; findet sich keine, die beste ohne
            RailRoute fallback = null;
            foreach (var c in candidates)
            {
                var u = Dir(c.angle);
                searched++;
                var plus = PlanRailHalf(field, c.through, u, edge, ref result.Expansions);
                var minus = plus == null ? null : PlanRailHalf(field, c.through, -u, edge, ref result.Expansions);
                if (minus == null) { noRoute++; continue; }
                var pieces = ReverseAll(plus);
                pieces.AddRange(minus);
                // genau eine Kreuzung mit der Autobahn
                if (hasHighway && Crossings(RoutePlanner.Sample(pieces, 16f), highwayLine) != 1) { recrossing++; continue; }
                string what = $"Bahn {c.angle:0}° bei {c.along:0} m entlang der Autobahn";
                if (!HasStationSpot(field, pieces, c.through))
                {
                    noStation++;
                    if (fallback == null) fallback = new RailRoute { Pieces = pieces, Through = c.through, Angle = c.angle, Report = what };
                    continue;
                }
                result.Pieces = pieces;
                result.Through = c.through;
                result.Angle = c.angle;
                result.Report = $"{what} · {searched} Kandidaten ({noStation} ohne Bahnhofsplatz), {result.Expansions} Suchschritte";
                break;
            }
            if (result.Pieces == null && fallback != null)
            {
                result.Pieces = fallback.Pieces;
                result.Through = fallback.Through;
                result.Angle = fallback.Angle;
                result.Report = $"{fallback.Report} · ohne geraden ebenen Abschnitt für einen Bahnhof · {searched} Kandidaten, {result.Expansions} Suchschritte";
            }
            if (result.Pieces == null)
            {
                result.Report = $"keine Bahntrasse: {searched} Kandidaten gesucht, {noRoute} ohne Weg, {recrossing} kreuzen die Autobahn mehrfach";
                return result;
            }
            result.A = result.Pieces[0].P0;
            result.B = result.Pieces[result.Pieces.Count - 1].P2;
            return result;
        }

        /// <summary>
        /// Gibt es einen geraden, ebenen Abschnitt für einen Bahnhof (≥ 260 m, Gelände dort ≤ 2 % und kaum Wellen, weg vom
        /// Wasser und von der Rampe der Brücke über die Autobahn)?
        /// </summary>
        static bool HasStationSpot(RouteField field, List<RoutePiece> pieces, Vector2 through)
        {
            const float need = 260f;
            float ramp = (Rail.RailRules.RailOverRoadClearance + 1.2f) / Rail.RailRules.MainlineGrade + 120f;
            int i = 0;
            while (i < pieces.Count)
            {
                if (pieces[i].Turn != 0) { i++; continue; }
                int j = i;
                while (j + 1 < pieces.Count && pieces[j + 1].Turn == 0) j++;
                var a = pieces[i].P0; var b = pieces[j].P2;
                float len = Vector2.Distance(a, b);
                var d = len > 1e-3f ? (b - a) / len : Vector2.UnitX;
                for (float s = 0f; s + need <= len; s += 40f)
                {
                    var p0 = a + d * s; var p1 = p0 + d * need;
                    if (Vector2.Distance((p0 + p1) * 0.5f, through) < ramp + need * 0.5f) continue; // Rampe der Brücke über die Autobahn
                    float h0 = field.Height(p0), h1 = field.Height(p1);
                    if (MathF.Abs(h1 - h0) > need * Rail.RailRules.MainlineGrade) continue;
                    bool ok = true;
                    for (float q = 0f; q <= need && ok; q += 20f)
                    {
                        var p = p0 + d * q;
                        float lin = h0 + (h1 - h0) * q / need;
                        if (MathF.Abs(field.Height(p) - lin) > 2.5f || field.Shore(p) < 60f) ok = false;
                    }
                    if (ok) return true;
                }
                i = j + 1;
            }
            return false;
        }

        static List<RoutePiece> PlanRailHalf(RouteField field, Vector2 start, Vector2 d, float edge, ref int expansions)
        {
            float length = ExitDistance(start, d, edge);
            if (length < RouteRules.RailStraightAtCrossing + 80f || length == float.MaxValue) return null;
            var spec = new RouteSpec
            {
                Radius = RouteRules.RailRadius, MaxGrade = Rail.RailRules.MainlineGrade, WaterCost = RouteRules.RailWaterCost,
                StraightNearWater = RouteRules.RailStraightNearWater, Edge = edge, IdealExit = start + d * length,
                StraightStart = RouteRules.RailStraightAtCrossing,
            };
            var steps = RoutePlanner.Plan(field, spec, start, MathF.Atan2(d.Y, d.X), out int e);
            expansions += e;
            if (steps == null) return null;
            return RoutePlanner.ToPieces(steps, Rail.RailRules.MainlineChunk, Rail.RailRules.MainlineMaxChunk, 15f, p => field.Shore(p) >= 40f);
        }

        /// <summary>Punkt auf dem Linienzug, <paramref name="along"/> m (Bogenlänge) vom Punkt nächst <paramref name="from"/>, und Richtung dort.</summary>
        static Vector2 PointAlong(IReadOnlyList<Vector2> line, Vector2 from, float along, out Vector2 tangent)
        {
            // Bogenlänge des nächsten Punkts
            float acc = 0f, s0 = 0f, bestD = float.MaxValue;
            var cum = new float[line.Count];
            for (int i = 0; i < line.Count - 1; i++)
            {
                var a = line[i]; var ab = line[i + 1] - a;
                float l = ab.Length();
                cum[i] = acc;
                float t = l < 1e-6f ? 0f : Math.Clamp(Vector2.Dot(from - a, ab) / (l * l), 0f, 1f);
                float d = Vector2.Distance(from, a + ab * t);
                if (d < bestD) { bestD = d; s0 = acc + t * l; }
                acc += l;
            }
            cum[line.Count - 1] = acc;
            float s = Math.Clamp(s0 + along, 0f, acc);
            for (int i = 0; i < line.Count - 1; i++)
            {
                float l = cum[i + 1] - cum[i];
                if (s <= cum[i + 1] || i == line.Count - 2)
                {
                    tangent = l > 1e-6f ? Vector2.Normalize(line[i + 1] - line[i]) : Vector2.UnitX;
                    return line[i] + (line[i + 1] - line[i]) * (l > 1e-6f ? Math.Clamp((s - cum[i]) / l, 0f, 1f) : 0f);
                }
            }
            tangent = Vector2.UnitX;
            return line[0];
        }

        /// <summary>Anzahl der Schnittpunkte zweier Linienzüge.</summary>
        public static int Crossings(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
        {
            // Schnittpunkte sammeln; Treffer an gemeinsamen Stützpunkten (doppelt gezählt) fallen zusammen
            var points = new List<Vector2>();
            for (int i = 0; i < a.Count - 1; i++)
                for (int j = 0; j < b.Count - 1; j++)
                    if (Bezier.LineSegmentsIntersect(a[i], a[i + 1], b[j], b[j + 1], out float s, out _))
                    {
                        var p = a[i] + (a[i + 1] - a[i]) * s;
                        bool known = false;
                        foreach (var q in points) if (Vector2.Distance(p, q) < 30f) { known = true; break; }
                        if (!known) points.Add(p);
                    }
            return points.Count;
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
