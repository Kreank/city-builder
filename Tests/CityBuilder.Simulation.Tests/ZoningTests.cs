using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    public class ZoningTests
    {
        readonly ITestOutputHelper output;
        public ZoningTests(ITestOutputHelper output) => this.output = output;

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, t ?? RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        [Fact]
        public void Grid_HasFourRowsOnBothSides_OfCityRoad()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            w.City.Grid.Update();
            var blocks = w.City.Grid.Blocks;
            Assert.Equal(2, blocks.Count);
            Assert.All(blocks, b => Assert.Equal(25, b.Columns)); // 200 m / 8 m
            Assert.Equal(2 * 25 * ZoneGrid.Depth, w.City.Grid.ValidCellCount);
            // erste Reihe direkt an der Fahrbahn
            var first = blocks[0][0, 0];
            Assert.Equal(RoadCatalog.CityTwoLane.HalfWidth + 4f, System.MathF.Abs(first.Center.Y), 2);
        }

        [Fact]
        public void Grid_NoZonesAlongHighways_AndCrossingRoadsBlockCells()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(0, -300), new Vector2(400, -300), RoadCatalog.Highway2);
            w.City.Grid.Update();
            Assert.Empty(w.City.Grid.Blocks);

            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            Road(w, new Vector2(100, -100), new Vector2(100, 100));
            w.City.Grid.Update();
            // Keine Zelle darf auf einer Straße liegen
            foreach (var b in w.City.Grid.Blocks)
            foreach (var c in b.Cells.Where(c => c.Valid))
                Assert.Null(w.Roads.FindNearestSegment(c.Center, 3f));
        }

        [Fact]
        public void Zoning_SurvivesRoadSplit()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            int painted = w.City.Paint(new Vector2(50, 15), 30f, ZoneType.ResidentialLow);
            Assert.True(painted > 10);
            Road(w, new Vector2(150, -100), new Vector2(150, 100)); // zerteilt die Straße
            w.City.Grid.Update();
            Assert.Equal(ZoneType.ResidentialLow, w.City.ZoneAt(new Vector2(50, 15)));
        }

        [Fact]
        public void City_Grows_WithResidentialAndIndustry()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            Road(w, new Vector2(-300, 150), new Vector2(300, 150));
            Road(w, new Vector2(-300, -400), new Vector2(-300, 400));
            w.City.Paint(new Vector2(-100, 20), 120f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(150, 20), 80f, ZoneType.ResidentialMedium);
            w.City.Paint(new Vector2(0, 130), 100f, ZoneType.Industry);
            w.City.Paint(new Vector2(200, 130), 60f, ZoneType.Retail);

            for (int i = 0; i < SimulationClock.TicksPerDay * 4; i++) w.Step();

            var s = w.City.Stats;
            output.WriteLine($"Gebäude {s.Buildings} (+{s.UnderConstruction} im Bau), Einwohner {s.Population}, Jobs {s.Jobs}, Arbeiter {s.Workers}");
            foreach (var z in Zones.All) output.WriteLine($"  {Zones.DisplayName(z)}: Nachfrage {w.City.Demand[z]:0}, Gebäude {w.City.Buildings.Count(b => b.Zone == z)}");
            Assert.True(s.Buildings > 10);
            Assert.True(s.Population > 30);
            Assert.True(w.City.Buildings.Any(b => b.Zone == ZoneType.Industry));
            Assert.True(w.City.Buildings.Any(b => b.Zone == ZoneType.ResidentialLow));

            // Gebäude überlappen sich nicht und stehen nicht auf Straßen
            var all = w.City.Buildings.ToList();
            foreach (var b in all)
            {
                Assert.Null(w.Roads.FindNearestSegment(b.Position, 0f));
                Assert.DoesNotContain(all, o => o != b && o.Contains(b.Position));
            }
        }

        [Fact]
        public void Building_IsDemolished_WhenRoadBuiltThrough_OrRezoned()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            w.City.Paint(new Vector2(0, 20), 200f, ZoneType.ResidentialLow);
            for (int i = 0; i < 400; i++) w.Step();
            int before = w.City.Buildings.Count();
            Assert.True(before > 5);

            var victim = w.City.Buildings.First();
            Road(w, victim.Position + new Vector2(0, -60), victim.Position + new Vector2(0, 60));
            Assert.DoesNotContain(w.City.Buildings, b => b.Id == victim.Id);

            var other = w.City.Buildings.First();
            w.City.Paint(other.Position, 6f, ZoneType.Industry);
            Assert.DoesNotContain(w.City.Buildings, b => b.Id == other.Id);
        }
    }
}
