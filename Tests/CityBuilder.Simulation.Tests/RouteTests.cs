using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Rail;
using CityBuilder.Simulation.Roads;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Trassierung der vorgebauten Welt-Infrastruktur (Spielertest „Bahn lief einmal quer über den Fluss“): Autobahn und
    /// Fernbahn queren Wasser fast rechtwinklig an schmaler Stelle (Brücke ≈ Flussbreite), schwingen in weiten Bögen
    /// (Autobahn R ≥ 750 m, Bahn R ≥ 1 000 m), halten die Steigung, kreuzen sich genau einmal fast rechtwinklig, bleiben
    /// deterministisch – auf den Spielwelten (2 km) und der großen Karte (6 km).
    /// </summary>
    public class RouteTests
    {
        readonly ITestOutputHelper output;
        public RouteTests(ITestOutputHelper output) => this.output = output;

        static WorldSettings Settings(int seed, float size = 2048f) => new WorldSettings
        {
            PrebuiltConnections = true, RequireGatewayForMoveIn = true, RequireGatewayForTrade = true, GenerateTerrain = true,
            GenerateWorld = true, WorldSeed = seed, SizeMeters = size,
        };

        /// <summary>Eine Wasserquerung eines Stücks: Länge über Wasser, Flussbreite (kürzeste Sehne), Winkel zur Ufernormalen.</summary>
        sealed class Crossing
        {
            public string Kind;
            public Vector2 Mid;
            public float Length, Width, Skew;
            public override string ToString() => $"{Kind} ({Mid.X:0}/{Mid.Y:0}): Brücke über Wasser {Length:0} m, Gewässer {Width:0} m breit, Winkel {Skew:0}° zur Normalen";
        }

        /// <summary>Kürzeste Wasser-Sehne durch <paramref name="p"/> (Richtungen in 2°-Schritten) und ihre Richtung.</summary>
        static float WaterWidth(IGround ground, Vector2 p, out Vector2 dir)
        {
            float best = float.MaxValue;
            dir = Vector2.UnitX;
            for (int a = 0; a < 180; a += 2)
            {
                var d = new Vector2(MathF.Cos(a * MathF.PI / 180f), MathF.Sin(a * MathF.PI / 180f));
                float w = 0f;
                for (int sgn = -1; sgn <= 1; sgn += 2)
                    for (float s = 1f; s < 1500f; s += 1f)
                        if (!ground.IsWater(p + d * (sgn * s))) { w += s; break; }
                if (w < best) { best = w; dir = d; }
            }
            return best;
        }

        /// <summary>Winkel (Grad) zwischen der Richtung <paramref name="d"/> und der Normalen des Ufers, auf das sie ab <paramref name="from"/> zuläuft.</summary>
        static float BankSkew(IGround ground, Vector2 from, Vector2 d)
        {
            var n = new Vector2(-d.Y, d.X);
            Vector2? Hit(Vector2 start)
            {
                for (float s = 0f; s < 200f; s += 0.5f) if (ground.IsWater(start + d * s)) return start + d * s;
                return null;
            }
            var a = Hit(from - n * 30f); var b = Hit(from + n * 30f);
            if (a == null || b == null) return 0f;
            var bank = Vector2.Normalize(b.Value - a.Value);
            float angleToBank = MathF.Acos(Math.Clamp(MathF.Abs(Vector2.Dot(bank, d)), 0f, 1f)) * 180f / MathF.PI;
            return 90f - angleToBank;
        }

        static List<Crossing> WaterCrossings(SimulationWorld w, IEnumerable<RoadSegment> segments, string kind)
        {
            var list = new List<Crossing>();
            var ground = w.Terrain;
            foreach (var s in segments)
            {
                int n = Math.Max(8, (int)(s.Length / 2f));
                int runStart = -1;
                for (int i = 0; i <= n + 1; i++)
                {
                    bool wet = i <= n && ground.IsWater(s.Evaluate(i / (float)n));
                    if (wet && runStart < 0) runStart = i;
                    if (!wet && runStart >= 0)
                    {
                        float t0 = runStart / (float)n, t1 = (i - 1) / (float)n;
                        float len = 0f;
                        for (int k = runStart; k < i - 1; k++) len += Vector2.Distance(s.Evaluate(k / (float)n), s.Evaluate((k + 1) / (float)n));
                        var mid = s.Evaluate((t0 + t1) * 0.5f);
                        if (len > 6f)
                        {
                            float width = WaterWidth(ground, mid, out _);
                            var tan = Vector2.Normalize(s.Tangent((t0 + t1) * 0.5f));
                            // Winkel gegen die Ufernormale je Ufer: Uferlinie aus zwei Uferpunkten seitlich (±30 m) der Trasse
                            float skew = MathF.Max(BankSkew(ground, s.Evaluate(t0) - tan * 40f, tan), BankSkew(ground, s.Evaluate(t1) + tan * 40f, -tan));
                            list.Add(new Crossing { Kind = kind, Mid = mid, Length = len, Width = width, Skew = skew });
                        }
                        runStart = -1;
                    }
                }
            }
            return list;
        }

        /// <summary>Kleinster Krümmungshalbmesser eines Stücks (quadratische Bézier).</summary>
        static float MinRadius(RoadSegment s)
        {
            var p0 = s.Start.Position; var c = s.Control; var p2 = s.End.Position;
            var dd = 2f * (p0 - 2f * c + p2);
            float best = float.MaxValue;
            for (int i = 0; i <= 16; i++)
            {
                float t = i / 16f;
                var d = Bezier.Derivative(p0, c, p2, t);
                float cross = MathF.Abs(d.X * dd.Y - d.Y * dd.X);
                if (cross < 1e-6f) continue;
                best = MathF.Min(best, MathF.Pow(d.Length(), 3f) / cross);
            }
            return best;
        }

        static List<Vector2> Polyline(IEnumerable<RoadSegment> segs)
        {
            var pts = new List<Vector2>();
            foreach (var s in segs) for (int i = 0; i <= 16; i++) pts.Add(s.Evaluate(i / 16f));
            return pts;
        }

        public static IEnumerable<object[]> Worlds()
        {
            foreach (int seed in new[] { 2024, 74, 333, 592 }) yield return new object[] { seed, 2048f };
            yield return new object[] { 2024, WorldSettings.GameSizeMeters };
            yield return new object[] { 74, WorldSettings.GameSizeMeters };
        }

        [Theory]
        [MemberData(nameof(Worlds))]
        public void GameWorlds_CrossWaterSquarely_WideCurves_GradesHeld_CrossOnce(int seed, float size)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var w = SimulationWorld.CreateDefault(Settings(seed, size));
            sw.Stop();
            output.WriteLine($"Seed {seed}, Karte {size:0} m: Welt erzeugt in {sw.ElapsedMilliseconds} ms");
            output.WriteLine(w.Gateways.LastReport.ToString());
            output.WriteLine(w.Gateways.RailReport);
            output.WriteLine(w.Rail.MainlineReport);
            Assert.True(w.Gateways.LastReport.HighwayBuilt, w.Gateways.LastReport.ToString());
            Assert.True(w.Rail.HasMainline, w.Gateways.RailReport + " / " + w.Rail.MainlineReport);

            var highway = w.Roads.Segments.Where(s => s.Type == RoadCatalog.Highway2).ToList();
            var tracks = w.Rail.Tracks.Segments.ToList();

            // Wasser: fast rechtwinklig, Brücke kaum länger als das Gewässer breit ist
            var crossings = WaterCrossings(w, highway, "Autobahn").Concat(WaterCrossings(w, tracks, "Bahn")).ToList();
            foreach (var c in crossings) output.WriteLine("  " + c);
            foreach (var c in crossings)
            {
                Assert.True(c.Skew <= 25f, c.ToString());
                Assert.True(c.Length <= c.Width * 1.3f + 12f, c.ToString());
            }

            // Radien und Steigung
            float hwR = highway.Min(MinRadius), railR = tracks.Min(MinRadius);
            float hwSlope = highway.Max(s => s.MaxSlope), railSlope = tracks.Max(s => s.MaxSlope);
            int hwCurves = highway.Count(s => MinRadius(s) < 1e5f), railCurves = tracks.Count(s => MinRadius(s) < 1e5f);
            output.WriteLine($"  Autobahn: {highway.Count} Stücke, {hwCurves} im Bogen, kleinster Radius {hwR:0} m, größte Steigung {hwSlope * 100f:0.0} %");
            output.WriteLine($"  Bahn: {tracks.Count} Stücke, {railCurves} im Bogen, kleinster Radius {railR:0} m, größte Steigung {railSlope * 100f:0.00} %");
            // Richtungsfahrbahnen liegen 12 m innen/außen; Bézier-Näherung der Bögen ±3 %
            Assert.True(hwR >= (RouteRules.HighwayRadius - 12f) * 0.97f, $"Autobahn-Radius {hwR:0} m");
            Assert.True(railR >= (RouteRules.RailRadius - 2.25f) * 0.97f, $"Bahn-Radius {railR:0} m");
            Assert.True(railSlope <= RailRules.MainlineGrade + 0.002f, $"Bahn-Steigung {railSlope:0.0000}");
            Assert.True(hwSlope <= RoadCatalog.Highway2.MaxSlope + 0.001f);

            // Autobahn und Bahn kreuzen sich genau einmal, fast rechtwinklig, kreuzungsfrei
            var gw = w.Gateways;
            var railLine = gw.RailPath.ToList();
            int railCrossHw = 0;
            foreach (var s in highway)
            {
                var pts = Polyline(new[] { s });
                if (GatewayGenerator.Crossings(railLine, pts) > 0) railCrossHw++;
            }
            output.WriteLine($"  Bahn kreuzt {railCrossHw} Autobahn-Fahrbahnstücke");
            Assert.Equal(2, railCrossHw); // eine Kreuzung = zwei Richtungsfahrbahnen
            foreach (var s in highway)
            {
                var pts = Polyline(new[] { s });
                if (GatewayGenerator.Crossings(railLine, pts) == 0) continue;
                // Winkel an der Kreuzung
                for (int i = 0; i < railLine.Count - 1; i++)
                    for (int j = 0; j < pts.Count - 1; j++)
                        if (Bezier.LineSegmentsIntersect(railLine[i], railLine[i + 1], pts[j], pts[j + 1], out _, out _))
                        {
                            float a = RoadGeometry.Angle(railLine[i + 1] - railLine[i], pts[j + 1] - pts[j]);
                            if (a > 90f) a = 180f - a;
                            output.WriteLine($"  Kreuzung Bahn/Autobahn: {a:0}°");
                            Assert.True(a >= 90f - RouteRules.RailCrossingMaxSkew - 1f, $"Kreuzungswinkel {a:0}°");
                        }
            }
            // Gerade Abschnitte für Bahnsteige (≥ 250 m) gibt es
            Assert.Contains(tracks, s => MinRadius(s) > 1e5f && s.Length >= 250f);
        }

        [Fact]
        public void Generation_IsDeterministic_SameRoutes()
        {
            var a = SimulationWorld.CreateDefault(Settings(333));
            var b = SimulationWorld.CreateDefault(Settings(333));
            Assert.Equal(SaveGame.SectionHashes(a), SaveGame.SectionHashes(b));
        }

        [Fact]
        public void OldSave_GatewaysV1_KeepsBuiltRoutes_CorridorStraightBetweenEnds()
        {
            var w = SimulationWorld.CreateDefault(Settings(2024));
            var c = SaveGame.Capture(w);
            var gs = c.Find("gateways");
            Assert.Equal(2, gs.Version);
            int n = w.Gateways.RailPath.Count;
            c.Set(gs.Id, 1, gs.Data.Take(gs.Data.Length - 4 - 8 * n).ToArray()); // v1: ohne die Mittellinie der Trasse
            var old = SaveGame.Restore(SaveContainer.FromBytes(c.ToBytes()));
            // Straßen und Gleise stehen im Spielstand – nichts wird neu trassiert
            Assert.Equal(w.Roads.SegmentCount, old.Roads.SegmentCount);
            Assert.Equal(w.Rail.Tracks.SegmentCount, old.Rail.Tracks.SegmentCount);
            Assert.Equal(SaveGame.SectionHashes(w).First(kv => kv.Key == "roads").Value, SaveGame.SectionHashes(old).First(kv => kv.Key == "roads").Value);
            // Korridor alter Stände: gerade zwischen den Enden
            Assert.Equal(2, old.Gateways.RailPath.Count);
            Assert.Equal(w.Gateways.RailStart, old.Gateways.RailPath[0]);
            Assert.Equal(w.Gateways.RailEnd, old.Gateways.RailPath[1]);
            Assert.True(old.Gateways.InRailCorridor((w.Gateways.RailStart + w.Gateways.RailEnd) * 0.5f));
            // neuer Stand: Mittellinie kommt bitgleich zurück
            var again = SaveGame.Restore(SaveContainer.FromBytes(SaveGame.Capture(w).ToBytes()));
            Assert.Equal(w.Gateways.RailPath, again.Gateways.RailPath);
        }

        [Fact]
        public void FlatMap_StaysStraight()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { PrebuiltConnections = true });
            Assert.All(w.Roads.Segments.Where(s => s.Type == RoadCatalog.Highway2), s => Assert.True(MinRadius(s) > 1e5f, s.ToString()));
            Assert.All(w.Rail.Tracks.Segments, s => Assert.True(MinRadius(s) > 1e5f, s.ToString()));
        }
    }
}
