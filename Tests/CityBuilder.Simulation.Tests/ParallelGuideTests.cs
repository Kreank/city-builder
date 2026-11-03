using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>Bauhilfe „optimaler Straßenabstand“: Formel gegen das echte Zonenraster geprüft.</summary>
    public class ParallelGuideTests
    {
        readonly ITestOutputHelper output;
        public ParallelGuideTests(ITestOutputHelper output) => this.output = output;

        static RoadSegment Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType t, Vector2? control = null)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 0.5f, control, b, 0.5f, t);
            Assert.True(plan.IsValid, plan.Error.ToString());
            var built = RoadBuilder.Build(w.Roads, plan);
            return built[built.Count - 1];
        }

        /// <summary>Gültige Zellen zwischen y = 0 und y = <paramref name="top"/>, nach Straße.</summary>
        static List<(ZoneBlock block, ZoneCell cell)> CellsBetween(ZoneGrid grid, float top)
        {
            var list = new List<(ZoneBlock, ZoneCell)>();
            foreach (var b in grid.Blocks)
            foreach (var c in b.Cells)
                if (c.Valid && c.Center.Y > 0f && c.Center.Y < top) list.Add((b, c));
            return list;
        }

        public static IEnumerable<object[]> TypePairs()
        {
            var types = new[] { RoadCatalog.CityTwoLane, RoadCatalog.CityFourLane, RoadCatalog.CityOneWay1, RoadCatalog.CityTram, RoadCatalog.CityOneWayBus };
            foreach (var a in types)
            foreach (var b in types)
                yield return new object[] { a.Id, b.Id };
        }

        // Setzen statt Zonen: Die Bauhilfe richtet sich nach Gebäudetiefen statt nach dem Zonenraster. Die früheren Tests
        // (Abstand gegen das echte Zonenraster, Reihen-Stufen 1–4, Rasterduldung 1,2 m) prüften ein Modell, das es für neue
        // Städte nicht mehr gibt; sie sind ersetzt durch dieselben Fragen gegen das echte Setzen von Gebäudereihen
        // (CitySystem.PlanHouseRow): passt Reihe B bei optimalem Abstand Rücken an Rücken hinter Reihe A, ohne Lücke?

        /// <summary>Bauart je Tiefe (16/24/32 m).</summary>
        static HouseType TypeOfDepth(float depth) => HouseCatalog.All.First(t => MathF.Abs(t.Depth - depth) < 0.01f);

        /// <summary>Reihe an Straße A (y = 0, Seite +y) und an Straße B (y = d, Seite −y) setzen; liefert die Reihen.</summary>
        static (HouseRowPlan a, HouseRowPlan b) BackToBack(SimulationWorld w, HouseType type, RoadType ta, RoadType tb, float d)
        {
            float ya = ta.HalfWidth + 3f, yb = d - tb.HalfWidth - 3f;
            var rowA = w.City.PlanHouseRow(type, new Vector2(30, ya), new Vector2(170, ya));
            Assert.True(rowA.ValidCount >= 3, $"Reihe A: {string.Join(",", rowA.Items.Select(i => i.Error))}");
            w.City.BuildHouseRow(rowA);
            var rowB = w.City.PlanHouseRow(type, new Vector2(30, yb), new Vector2(170, yb));
            return (rowA, rowB);
        }

        [Fact]
        public void Formula_IsHalfWidthsPlusTwiceBuildingDepth()
        {
            Assert.Equal(2f * (0.5f + 24f), ParallelGuides.OptimalEdgeGap(), 3);
            Assert.Equal(33f, ParallelGuides.OptimalEdgeGap(16f), 3);
            Assert.Equal(65f, ParallelGuides.OptimalEdgeGap(32f), 3);
            Assert.Equal(6.5f + 6.5f + 33f, ParallelGuides.OptimalAxisDistance(RoadCatalog.CityTwoLane, RoadCatalog.CityTwoLane, 16f), 3);
            Assert.Equal(10f + 6.5f + 49f, ParallelGuides.OptimalAxisDistance(RoadCatalog.CityFourLane, RoadCatalog.CityTwoLane, 24f), 3);
            foreach (var t in RoadCatalog.All.Where(t => t.Category == RoadCategory.City))
                output.WriteLine($"{t.Id,-18} halbe Breite {t.HalfWidth,5:0.00} m  optimal zu city_2: 16 m {ParallelGuides.OptimalAxisDistance(t, RoadCatalog.CityTwoLane, 16f):0.0} · " +
                                 $"24 m {ParallelGuides.OptimalAxisDistance(t, RoadCatalog.CityTwoLane, 24f):0.0} · 32 m {ParallelGuides.OptimalAxisDistance(t, RoadCatalog.CityTwoLane, 32f):0.0} m");
        }

        [Theory]
        [MemberData(nameof(TypePairs))]
        public void OptimalDistance_PutsTwoRowsBackToBack(string idA, string idB)
        {
            var a = RoadCatalog.Get(idA);
            var b = RoadCatalog.Get(idB);
            foreach (float depth in new[] { 16f, 32f })
            {
                var type = TypeOfDepth(depth);
                float d = ParallelGuides.OptimalAxisDistance(a, b, depth);
                var w = SimulationWorld.CreateDefault();
                Road(w, new Vector2(0, 0), new Vector2(200, 0), a);
                Road(w, new Vector2(0, d), new Vector2(200, d), b);
                var (rowA, rowB) = BackToBack(w, type, a, b, d);
                // Reihe B passt vollständig (keine Überlappung mit Reihe A) …
                Assert.True(rowB.ValidCount >= 3 && rowB.Items.All(i => i.IsValid || i.Error != HouseBuildError.OverlapsBuilding),
                    $"Reihe B: {string.Join(",", rowB.Items.Select(i => i.Error))}");
                // … und die Rückseiten berühren sich (keine Lücke)
                float backA = rowA.Items[0].Position.Y + depth * 0.5f;
                float backB = rowB.Items[0].Position.Y - depth * 0.5f;
                Assert.True(MathF.Abs(backA - backB) < 0.05f, $"Rückseiten {backA} / {backB}");
                var info = ParallelGuides.Evaluate(a, b, d, depth);
                Assert.Equal(ParallelFit.Optimal, info.Fit);
                Assert.Equal(2, info.Rows);
                Assert.Equal(0f, info.Unused, 3);
                Assert.Equal(0f, info.Overlap, 3);
            }
        }

        [Fact]
        public void RowModel_MatchesPlacement_ForManyGaps()
        {
            // Evaluate().Rows (2 = beide Reihen passen) gegen echtes Setzen über viele Abstände
            var t = RoadCatalog.CityTwoLane;
            var type = HouseCatalog.Medium; // 24 m tief
            for (float gap = 20f; gap <= 76f; gap += 1.7f)
            {
                float d = gap + 2f * t.HalfWidth;
                var w = SimulationWorld.CreateDefault();
                Road(w, new Vector2(0, 0), new Vector2(200, 0), t);
                Road(w, new Vector2(0, d), new Vector2(200, d), t);
                var info = ParallelGuides.Evaluate(t, t, d, type.Depth);
                bool fitsA = info.Rows >= 1;
                var rowA = w.City.PlanHouseRow(type, new Vector2(30, t.HalfWidth + 3f), new Vector2(170, t.HalfWidth + 3f));
                output.WriteLine($"Rand {gap:0.0} m: Modell {info.Rows} Reihen · {ParallelGuides.Describe(info)}");
                if (!fitsA) continue; // dann ragt schon Reihe A in die Gegenstraße
                Assert.True(rowA.ValidCount >= 3);
                w.City.BuildHouseRow(rowA);
                var rowB = w.City.PlanHouseRow(type, new Vector2(30, d - t.HalfWidth - 3f), new Vector2(170, d - t.HalfWidth - 3f));
                bool placedB = rowB.ValidCount > 0;
                Assert.Equal(info.Rows == 2, placedB);
            }
        }

        [Fact]
        public void TooClose_And_TooFar_AreReported()
        {
            var t = RoadCatalog.CityTwoLane;
            float opt = ParallelGuides.OptimalAxisDistance(t, t, 24f);

            var tight = ParallelGuides.Evaluate(t, t, opt - 4f, 24f);
            Assert.Equal(ParallelFit.Overlap, tight.Fit);
            Assert.Equal(1, tight.Rows);                  // nur eine Reihe passt …
            Assert.Equal(4f, tight.Overlap, 3);           // … die zweite würde 4 m überlappen
            Assert.Contains("zu eng", ParallelGuides.Describe(tight));
            Assert.Contains("nur eine Reihe", ParallelGuides.Describe(tight));

            var slightly = ParallelGuides.Evaluate(t, t, opt - 1f, 24f);
            Assert.Equal(ParallelFit.Overlap, slightly.Fit);
            Assert.Equal(1f, slightly.Overlap, 3);
            Assert.Contains("1,0 m Überlappung", ParallelGuides.Describe(slightly));

            var wide = ParallelGuides.Evaluate(t, t, opt + 3f, 24f);
            Assert.Equal(ParallelFit.Gap, wide.Fit);
            Assert.Equal(3f, wide.Unused, 3);
            Assert.Equal(2, wide.Rows);
            Assert.Contains("3,0 m Lücke", ParallelGuides.Describe(wide));
            Assert.Contains("optimal", ParallelGuides.Describe(ParallelGuides.Evaluate(t, t, opt + 0.1f, 24f)));
        }

        [Fact]
        public void Snap_LandsOnGuide_BothSides_AndOnlyNearCityRoads()
        {
            var w = SimulationWorld.CreateDefault();
            var a = RoadCatalog.CityTwoLane;
            var s = Road(w, new Vector2(0, 0), new Vector2(200, 0), a);
            float d = ParallelGuides.OptimalAxisDistance(a, a);

            Assert.True(ParallelGuides.TrySnap(w.Roads, new Vector2(60, d + 3f), a, 24f, 6f, out var up));
            Assert.Equal(new Vector2(60, d).X, up.Point.X, 3);
            Assert.Equal(d, up.Point.Y, 3);
            Assert.Equal(s.Id, up.Segment.Id);

            Assert.True(ParallelGuides.TrySnap(w.Roads, new Vector2(120, -d + 2f), a, 24f, 6f, out var down));
            Assert.Equal(-d, down.Point.Y, 3);
            Assert.NotEqual(up.Side, down.Side);

            // Verlängerung gerader Straßen über das Ende hinaus
            Assert.True(ParallelGuides.TrySnap(w.Roads, new Vector2(250, d - 1f), a, 24f, 6f, out var ext));
            Assert.Equal(d, ext.Point.Y, 3);
            Assert.True(ext.T > 1f);

            // außerhalb des Radius nicht
            Assert.False(ParallelGuides.TrySnap(w.Roads, new Vector2(60, d + 10f), a, 24f, 6f, out _));
            // Tiefe 16 m statt 24 m: Linie 2 × 8 = 16 m näher
            Assert.True(ParallelGuides.TrySnap(w.Roads, new Vector2(60, d - 15f), a, 16f, 6f, out var two));
            Assert.Equal(d - 16f, two.Point.Y, 3);
            // Autobahn bekommt keine Hilfslinien (keine Gebäude an der Autobahn) und eine geplante Autobahn sucht keine
            Assert.False(ParallelGuides.TrySnap(w.Roads, new Vector2(60, d), RoadCatalog.Highway2, 24f, 6f, out _));
            var w2 = SimulationWorld.CreateDefault();
            Road(w2, new Vector2(0, 0), new Vector2(200, 0), RoadCatalog.Highway2);
            Assert.False(ParallelGuides.TrySnap(w2.Roads, new Vector2(60, d), a, 24f, 6f, out _));
        }

        [Fact]
        public void SnapParallel_MakesDirectionExactlyParallel()
        {
            var origin = new Vector2(10, 77);
            Assert.True(RoadGeometry.SnapParallel(origin, new Vector2(108, 80), Vector2.UnitX, 8f, 8f, out var p));
            Assert.Equal(77f, p.Y, 4);
            Assert.Equal(10f + 96f, p.X, 3); // 98 m → auf 8er-Raster 96 m
            Assert.True(RoadGeometry.SnapParallel(origin, new Vector2(-90, 75), Vector2.UnitX, 8f, 0f, out var back));
            Assert.Equal(77f, back.Y, 4);
            Assert.False(RoadGeometry.SnapParallel(origin, new Vector2(60, 120), Vector2.UnitX, 8f, 0f, out _));
        }

        [Fact]
        public void SnappedParallelRoad_FitsTwoRowsBackToBack()
        {
            // Spielablauf nachgestellt: Start und Ende per Hilfslinie eingerastet, Ende parallel, dann zwei Reihen setzen
            var w = SimulationWorld.CreateDefault();
            var a = RoadCatalog.CityTram;
            var b = RoadCatalog.CityOneWay2;
            Road(w, new Vector2(0, 0), new Vector2(200, 0), a);
            float d = ParallelGuides.OptimalAxisDistance(a, b, 24f);
            Assert.True(ParallelGuides.TrySnap(w.Roads, new Vector2(1.3f, d + 2f), b, 24f, 8f, out var start));
            Assert.True(RoadGeometry.SnapParallel(start.Point, new Vector2(199f, d + 3f), start.Tangent, 8f, 8f, out var end));
            Road(w, start.Point, end, b);
            Assert.Equal(d, end.Y, 2);
            Assert.Equal(ParallelFit.Optimal, ParallelGuides.Evaluate(a, b, end.Y, 24f).Fit);
            var (_, rowB) = BackToBack(w, HouseCatalog.Medium, a, b, d);
            Assert.True(rowB.ValidCount >= 3 && rowB.Items.All(i => i.Error != HouseBuildError.OverlapsBuilding),
                string.Join(",", rowB.Items.Select(i => i.Error)));
        }

        [Fact]
        public void Curve_GuideIsOffsetCurve_AndParallelControlFollowsIt()
        {
            var w = SimulationWorld.CreateDefault();
            var a = RoadCatalog.CityTwoLane;
            // weite Kurve (Viertelkreis-ähnlich, Radius ~ 300 m)
            var s = Road(w, new Vector2(0, 0), new Vector2(300, 300), a, new Vector2(300, 0));
            float d = ParallelGuides.OptimalAxisDistance(a, a);
            // Setzen statt Zonen: auf Bögen kommt der Kurvenzuschlag dazu (hintere Ecken der Sehnen-Gebäude + Puffer der Näherung)
            // Außen- und Innenseite: Punkte nahe der Offset-Kurve rasten exakt im Achsabstand ein
            foreach (float t in new[] { 0.2f, 0.5f, 0.8f })
            foreach (int side in new[] { -1, 1 })
            {
                var near = s.Evaluate(t) + s.Right(t) * side * (d + 2.5f);
                Assert.True(ParallelGuides.TrySnap(w.Roads, near, a, 24f, 6f, out var hit));
                Assert.Equal(side, hit.Side);
                Assert.True(hit.Allowance > ParallelGuides.CurveFitMargin && hit.Allowance < 3f, $"Kurvenzuschlag {hit.Allowance}");
                Assert.Equal(d + hit.Allowance, s.DistanceTo(hit.Point), 1);
            }

            d += ParallelGuides.CurveAllowance(24f, ParallelGuides.CurveRadius(s, 0.5f)); // Ziel inkl. Kurvenzuschlag (Mitte)
            // Parallele Kurve auf der Außenseite (weg vom Krümmungsmittelpunkt) von Linienanfang bis -ende
            var mid = s.Evaluate(0.5f);
            var center = new Vector2(0, 300);
            int outer = Vector2.Distance(mid + s.Right(0.5f) * 10f, center) > Vector2.Distance(mid, center) ? 1 : -1;
            Assert.True(ParallelGuides.TryEvaluate(s, s.Evaluate(0f) + s.Right(0f) * outer * d, d, out var h0));
            Assert.True(ParallelGuides.TryEvaluate(s, s.Evaluate(1f) + s.Right(1f) * outer * d, d, out var h1));
            Assert.True(ParallelGuides.TryParallelControl(h0, h1, out var tangentControl));
            var control = ParallelGuides.FitParallelControl(s, outer * d, 0f, 1f);
            float Worst(Vector2 p0, Vector2 c, Vector2 p2)
            {
                float m = 0f;
                for (int i = 0; i <= 64; i++)
                    m = MathF.Max(m, MathF.Abs(s.DistanceTo(Bezier.Evaluate(p0, c, p2, i / 64f)) - d));
                return m;
            }
            float worst = Worst(h0.Point, control, h1.Point);
            output.WriteLine($"Kurve 90°: größte Abweichung vom Achsabstand: Tangentenschnitt {Worst(h0.Point, tangentControl, h1.Point):0.00} m, Ausgleich {worst:0.00} m");
            Assert.True(worst < 1.0f, $"Abweichung {worst} m");
            var plan = RoadBuilder.Plan(w.Roads, h0.Point, 0.5f, control, h1.Point, 0.5f, a);
            Assert.True(plan.IsValid, plan.Error.ToString());
            var built = RoadBuilder.Build(w.Roads, plan);
            var s2 = built[built.Count - 1];

            // Zwischen beiden Kurven zwei Reihen (24 m) Rücken an Rücken: alte Kurve außen, neue Kurve innen
            var type = HouseCatalog.Medium;
            Vector2 OnSide(RoadSegment seg, float t, Vector2 toward) =>
                seg.Evaluate(t) + Vector2.Normalize(toward - seg.Evaluate(t)) * (seg.Type.HalfWidth + 3f);
            var rowA = w.City.PlanHouseRow(type, OnSide(s, 0.25f, s2.Evaluate(0.25f)), OnSide(s, 0.75f, s2.Evaluate(0.75f)));
            w.City.BuildHouseRow(rowA);
            var probe = s2.Evaluate(s2.ClosestT(s.Evaluate(0.5f)));
            var rowB = w.City.PlanHouseRow(type, OnSide(s2, s2.ClosestT(s.Evaluate(0.3f)), s.Evaluate(0.3f)),
                OnSide(s2, s2.ClosestT(s.Evaluate(0.7f)), s.Evaluate(0.7f)));
            int overlapping = rowB.Items.Count(i => i.Error == HouseBuildError.OverlapsBuilding);
            output.WriteLine($"Kurve: Reihe außen {rowA.ValidCount}, Reihe innen {rowB.ValidCount} gültig, {overlapping} überlappen (Probe {probe})");
            Assert.True(rowA.ValidCount >= 5);
            Assert.True(rowB.ValidCount >= 3);
            Assert.True(overlapping <= rowB.Items.Count / 3, $"{overlapping} von {rowB.Items.Count} überlappen");
        }

        [Fact]
        public void BlockLength_FillsBlockBetweenCrossStreets_WithoutRemainder()
        {
            var w = SimulationWorld.CreateDefault();
            var t = RoadCatalog.CityTwoLane;
            float block = ParallelGuides.BlockLength(t, t, 10);
            Road(w, new Vector2(0, 0), new Vector2(400, 0), t);
            Road(w, new Vector2(50, -100), new Vector2(50, 100), t);
            Road(w, new Vector2(50 + block + 0.02f, -100), new Vector2(50 + block + 0.02f, 100), t);
            w.City.Grid.Update();
            var blockSeg = w.City.Grid.Blocks.First(b => MathF.Abs(b.Segment.Evaluate(0.5f).Y) < 0.1f
                && b.Segment.Evaluate(0.5f).X > 50 && b.Segment.Evaluate(0.5f).X < 50 + block);
            Assert.Equal(10, blockSeg.Columns);
            // erste und letzte Spalte schließen genau an die Kreuzungsflächen an (kein Rest)
            float x0 = MathF.Min(blockSeg[0, 0].Center.X, blockSeg[9, 0].Center.X);
            float x1 = MathF.Max(blockSeg[0, 0].Center.X, blockSeg[9, 0].Center.X);
            Assert.Equal(50f + t.HalfWidth + 1f + 4f, x0, 1);
            Assert.Equal(50f + block - t.HalfWidth - 1f - 4f, x1, 1);
        }
    }
}
