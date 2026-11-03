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

        [Fact]
        public void Formula_IsHalfWidthsPlusTwiceDepth()
        {
            Assert.Equal(64f, ParallelGuides.OptimalEdgeGap());
            Assert.Equal(6.5f + 6.5f + 64f, ParallelGuides.OptimalAxisDistance(RoadCatalog.CityTwoLane, RoadCatalog.CityTwoLane), 3);
            Assert.Equal(10f + 6.5f + 64f, ParallelGuides.OptimalAxisDistance(RoadCatalog.CityFourLane, RoadCatalog.CityTwoLane), 3);
            Assert.Equal(5f + 5f + 40f, ParallelGuides.OptimalAxisDistance(RoadCatalog.CityOneWay1, RoadCatalog.CityOneWay1, 1), 3);
            foreach (var t in RoadCatalog.All.Where(t => t.Category == RoadCategory.City))
                output.WriteLine($"{t.Id,-18} halbe Breite {t.HalfWidth,5:0.00} m  optimal zu city_2: {ParallelGuides.OptimalAxisDistance(t, RoadCatalog.CityTwoLane):0.00} m, zu sich selbst: {ParallelGuides.OptimalAxisDistance(t, t):0.00} m");
        }

        [Theory]
        [MemberData(nameof(TypePairs))]
        public void OptimalDistance_GivesGaplessNonOverlappingZones(string idA, string idB)
        {
            var a = RoadCatalog.Get(idA);
            var b = RoadCatalog.Get(idB);
            float d = ParallelGuides.OptimalAxisDistance(a, b);
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(0, 0), new Vector2(200, 0), a);
            Road(w, new Vector2(0, d), new Vector2(200, d), b);
            w.City.Grid.Update();

            var cells = CellsBetween(w.City.Grid, d);
            // 25 Spalten × 4 Reihen je Straße auf der zugewandten Seite
            Assert.Equal(2 * 25 * ZoneGrid.Depth, cells.Count);
            // Reihen liegen genau im 8-m-Takt von Rand zu Rand: lückenlos, keine Überlappung
            var rows = cells.Select(c => MathF.Round(c.cell.Center.Y, 2)).Distinct().OrderBy(y => y).ToList();
            Assert.Equal(2 * ZoneGrid.Depth, rows.Count);
            for (int i = 0; i < rows.Count; i++)
                Assert.Equal(a.HalfWidth + ZoneGrid.CellSize * (i + 0.5f), rows[i], 2);
            Assert.Equal(d - b.HalfWidth - ZoneGrid.CellSize * 0.5f, rows[rows.Count - 1], 2);
            // je Straße 4 Reihen
            Assert.Equal(25 * ZoneGrid.Depth, cells.Count(c => c.block.Segment.Type == a && MathF.Abs(c.block.Segment.Evaluate(0.5f).Y) < 0.1f));

            var info = ParallelGuides.Evaluate(a, b, d);
            Assert.Equal(ParallelFit.Optimal, info.Fit);
            Assert.Equal(4, info.ExistingRows);
            Assert.Equal(4, info.PlannedRows);
            Assert.Equal(0f, info.Unused, 3);
            Assert.Equal(0f, info.Overlap, 3);
        }

        [Fact]
        public void RowModel_MatchesZoneGrid_ForManyGaps()
        {
            var t = RoadCatalog.CityTwoLane;
            for (float gap = 20f; gap <= 76f; gap += 1.3f)
            {
                float d = gap + 2f * t.HalfWidth;
                var w = SimulationWorld.CreateDefault();
                Road(w, new Vector2(0, 0), new Vector2(200, 0), t);
                Road(w, new Vector2(0, d), new Vector2(200, d), t);
                w.City.Grid.Update();
                var cells = CellsBetween(w.City.Grid, d);
                int rowsA = cells.Where(c => c.block.Segment.Start.Position.Y < 1f).Select(c => MathF.Round(c.cell.Center.Y, 1)).Distinct().Count();
                int rowsB = cells.Where(c => c.block.Segment.Start.Position.Y > 1f).Select(c => MathF.Round(c.cell.Center.Y, 1)).Distinct().Count();

                var info = ParallelGuides.Evaluate(t, t, d);
                output.WriteLine($"Rand {gap:0.0} m: Raster {rowsA}+{rowsB}, Modell {info.ExistingRows}+{info.PlannedRows}, ungenutzt {info.Unused:0.0}, Überlappung {info.Overlap:0.0} · {ParallelGuides.Describe(info)}");
                Assert.Equal(rowsA, info.ExistingRows);
                Assert.Equal(rowsB, info.PlannedRows);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public void Stages_GiveNewRoadExactlyItsRows_WithoutWaste(int rows)
        {
            var a = RoadCatalog.CityFourLane;
            var b = RoadCatalog.CityTwoLane;
            float d = ParallelGuides.OptimalAxisDistance(a, b, rows);
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(0, 0), new Vector2(200, 0), a);
            Road(w, new Vector2(0, d), new Vector2(200, d), b);
            w.City.Grid.Update();

            var cells = CellsBetween(w.City.Grid, d);
            Assert.Equal(25 * (ZoneGrid.Depth + rows), cells.Count);
            var info = ParallelGuides.Evaluate(a, b, d, rows);
            Assert.Equal(ParallelFit.Optimal, info.Fit);
            Assert.Equal(rows, info.PlannedRows);
            Assert.Equal(0f, info.Unused, 3);
        }

        [Fact]
        public void TooClose_And_TooFar_AreReported()
        {
            var t = RoadCatalog.CityTwoLane;
            float opt = ParallelGuides.OptimalAxisDistance(t, t);

            var tight = ParallelGuides.Evaluate(t, t, opt - 4f);
            Assert.Equal(ParallelFit.Overlap, tight.Fit);
            Assert.Equal(3, tight.PlannedRows);          // eine Reihe geht verloren …
            Assert.Equal(4f, tight.Unused, 3);           // … und 4 m bleiben leer
            Assert.Contains("zu eng", ParallelGuides.Describe(tight));

            var slightly = ParallelGuides.Evaluate(t, t, opt - 1f);
            Assert.Equal(ParallelFit.Overlap, slightly.Fit);
            Assert.Equal(4, slightly.PlannedRows);        // Raster duldet bis 1,2 m
            Assert.Equal(1f, slightly.Overlap, 3);
            Assert.Contains("1,0 m Überlappung", ParallelGuides.Describe(slightly));

            var wide = ParallelGuides.Evaluate(t, t, opt + 3f);
            Assert.Equal(ParallelFit.Gap, wide.Fit);
            Assert.Equal(3f, wide.Unused, 3);
            Assert.Contains("3,0 m Lücke", ParallelGuides.Describe(wide));
            Assert.Contains("optimal", ParallelGuides.Describe(ParallelGuides.Evaluate(t, t, opt + 0.1f)));
        }

        [Fact]
        public void Snap_LandsOnGuide_BothSides_AndOnlyNearCityRoads()
        {
            var w = SimulationWorld.CreateDefault();
            var a = RoadCatalog.CityTwoLane;
            var s = Road(w, new Vector2(0, 0), new Vector2(200, 0), a);
            float d = ParallelGuides.OptimalAxisDistance(a, a);

            Assert.True(ParallelGuides.TrySnap(w.Roads, new Vector2(60, d + 3f), a, 4, 6f, out var up));
            Assert.Equal(new Vector2(60, d).X, up.Point.X, 3);
            Assert.Equal(d, up.Point.Y, 3);
            Assert.Equal(s.Id, up.Segment.Id);

            Assert.True(ParallelGuides.TrySnap(w.Roads, new Vector2(120, -d + 2f), a, 4, 6f, out var down));
            Assert.Equal(-d, down.Point.Y, 3);
            Assert.NotEqual(up.Side, down.Side);

            // Verlängerung gerader Straßen über das Ende hinaus
            Assert.True(ParallelGuides.TrySnap(w.Roads, new Vector2(250, d - 1f), a, 4, 6f, out var ext));
            Assert.Equal(d, ext.Point.Y, 3);
            Assert.True(ext.T > 1f);

            // außerhalb des Radius nicht
            Assert.False(ParallelGuides.TrySnap(w.Roads, new Vector2(60, d + 10f), a, 4, 6f, out _));
            // Stufe 2 Reihen: Linie 16 m näher
            Assert.True(ParallelGuides.TrySnap(w.Roads, new Vector2(60, d - 15f), a, 2, 6f, out var two));
            Assert.Equal(d - 16f, two.Point.Y, 3);
            // Autobahn bekommt keine Hilfslinien (kein Zonenraster) und eine geplante Autobahn sucht keine
            Assert.False(ParallelGuides.TrySnap(w.Roads, new Vector2(60, d), RoadCatalog.Highway2, 4, 6f, out _));
            var w2 = SimulationWorld.CreateDefault();
            Road(w2, new Vector2(0, 0), new Vector2(200, 0), RoadCatalog.Highway2);
            Assert.False(ParallelGuides.TrySnap(w2.Roads, new Vector2(60, d), a, 4, 6f, out _));
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
        public void SnappedParallelRoad_GivesFullGrid()
        {
            // Spielablauf nachgestellt: Start und Ende per Hilfslinie eingerastet, Ende parallel
            var w = SimulationWorld.CreateDefault();
            var a = RoadCatalog.CityTram;
            var b = RoadCatalog.CityOneWay2;
            Road(w, new Vector2(0, 0), new Vector2(200, 0), a);
            Assert.True(ParallelGuides.TrySnap(w.Roads, new Vector2(1.3f, 85f), b, 4, 8f, out var start));
            Assert.True(RoadGeometry.SnapParallel(start.Point, new Vector2(199f, 88f), start.Tangent, 8f, 8f, out var end));
            Road(w, start.Point, end, b);
            w.City.Grid.Update();
            float d = ParallelGuides.OptimalAxisDistance(a, b);
            var cells = CellsBetween(w.City.Grid, d);
            var rows = cells.Select(c => MathF.Round(c.cell.Center.Y, 1)).Distinct().Count();
            Assert.Equal(2 * ZoneGrid.Depth, rows);
            Assert.Equal(ParallelFit.Optimal, ParallelGuides.Evaluate(a, b, end.Y).Fit);
        }

        [Fact]
        public void Curve_GuideIsOffsetCurve_AndParallelControlFollowsIt()
        {
            var w = SimulationWorld.CreateDefault();
            var a = RoadCatalog.CityTwoLane;
            // weite Kurve (Viertelkreis-ähnlich, Radius ~ 300 m)
            var s = Road(w, new Vector2(0, 0), new Vector2(300, 300), a, new Vector2(300, 0));
            float d = ParallelGuides.OptimalAxisDistance(a, a);

            // Außen- und Innenseite: Punkte nahe der Offset-Kurve rasten exakt im Achsabstand ein
            foreach (float t in new[] { 0.2f, 0.5f, 0.8f })
            foreach (int side in new[] { -1, 1 })
            {
                var near = s.Evaluate(t) + s.Right(t) * side * (d + 2.5f);
                Assert.True(ParallelGuides.TrySnap(w.Roads, near, a, 4, 6f, out var hit));
                Assert.Equal(side, hit.Side);
                Assert.Equal(d, s.DistanceTo(hit.Point), 1);
            }

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
            Assert.True(worst < 1.0f, $"Abweichung {worst} m"); // Zonenraster duldet 1,2 m
            var plan = RoadBuilder.Plan(w.Roads, h0.Point, 0.5f, control, h1.Point, 0.5f, a);
            Assert.True(plan.IsValid, plan.Error.ToString());

            RoadBuilder.Build(w.Roads, plan);
            w.City.Grid.Update();
            // Zwischen beiden Kurven: keine überlappenden Zellen und fast alles belegt
            var between = new List<ZoneCell>();
            foreach (var b in w.City.Grid.Blocks)
            foreach (var c in b.Cells)
                if (c.Valid && s.DistanceTo(c.Center) < d) between.Add(c);
            for (int i = 0; i < between.Count; i++)
            for (int j = i + 1; j < between.Count; j++)
                Assert.True(Vector2.Distance(between[i].Center, between[j].Center) >= ZoneGrid.CellSpacing - 1e-3f);
            output.WriteLine($"Kurve: {between.Count} Zellen zwischen den Straßen");
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
