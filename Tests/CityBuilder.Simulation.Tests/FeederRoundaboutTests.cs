using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Spielerbericht: „Kreisverkehr an den Zubringer gesetzt – zählt nicht als Anschluss.“ Kreisverkehr am Zubringer-Ende
    /// (mittig, über dem Ende, knapp davor) muss die Stadt anschließen; der Hinweis „Nicht angeschlossen“ verschwindet sofort
    /// (auch ohne Tick/in der Pause) und schon, bevor Wohnhäuser stehen.
    /// </summary>
    public class FeederRoundaboutTests
    {
        readonly ITestOutputHelper output;
        public FeederRoundaboutTests(ITestOutputHelper output) => this.output = output;

        static WorldSettings Settings(bool terrain) => new WorldSettings
        {
            PrebuiltConnections = true, RequireGatewayForMoveIn = true, RequireGatewayForTrade = true, GenerateTerrain = terrain,
        };

        /// <param name="offset">Mitte des Rings = Zubringer-Ende + offset nach außen (radius + gap = Ende liegt gap vor dem Ring).</param>
        [Theory]
        [InlineData(false, 0f, 30f)]     // Mitte auf dem Ende
        [InlineData(false, 30f, 30f)]    // Ring läuft durch das Ende
        [InlineData(false, 33f, 30f)]    // Ende 3 m vor dem Ring (sieht angeschlossen aus)
        [InlineData(false, 38f, 30f)]    // Ende 8 m vor dem Ring
        [InlineData(false, 0f, 60f)]
        [InlineData(true, 0f, 30f)]
        [InlineData(true, 30f, 30f)]
        [InlineData(true, 32.5f, 30f)]
        [InlineData(true, 0f, 50f)]
        public void RoundaboutAtFeederEnd_ConnectsTheCity(bool terrain, float offset, float radius)
        {
            var w = SimulationWorld.CreateDefault(Settings(terrain));
            var inter = w.Gateways.Interchange;
            var end = w.Gateways.FeederEnds.OrderBy(p => Vector2.Distance(p, Vector2.Zero)).First();
            var outward = Vector2.Normalize(end - inter);
            Assert.NotNull(w.Gateways.ConnectionHint);

            var center = end + outward * offset;
            var plan = RoadBuilder.PlanRoundabout(w.Roads, center, radius, RoadCatalog.DefaultRoundabout, w.Terrain.HeightAt(center), true);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.BuildRoundabout(w.Roads, plan);

            // Der Ring hängt am Netzteil der Autobahn-Einfahrten
            var comps = w.Citizens.Components;
            var ring = w.Roads.Segments.Where(s => s.IsRoundabout).ToList();
            int entryComp = comps.OfNode(w.Gateways.RoadGateways().First().EntryNodeId);
            foreach (var s in ring) Assert.Equal(entryComp, comps.OfSegment(s.Id));
            // kein offenes Zubringer-Ende mehr am Ring
            foreach (var n in w.Roads.Nodes.Where(n => n.SegmentIds.Count == 1))
                Assert.True(Vector2.Distance(n.Position, center) > radius + 12f || Vector2.Distance(n.Position, center) < radius - 12f,
                    $"offenes Ende {n.Position} am Ring");

            // Hinweis verschwindet sofort, auch ohne Tick und ohne Wohnhaus
            output.WriteLine($"Hinweis: {w.Gateways.ConnectionHint ?? "(keiner)"}");
            Assert.Null(w.Gateways.ConnectionHint);

            // Straße vom Ring weg + Wohngebiet → Zuzug
            var a = center + outward * radius;
            var b = center + outward * (radius + 220f);
            var rp = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane, w.Terrain.HeightAt(a), w.Terrain.HeightAt(b), followTerrain: true);
            Assert.True(rp.IsValid, rp.Error.ToString());
            Assert.NotEqual(AnchorKind.Free, rp.StartAnchor.Kind);
            RoadBuilder.Build(w.Roads, rp);
            var side = new Vector2(-outward.Y, outward.X);
            w.City.Paint(center + outward * (radius + 120f) + side * 30f, 60f, ZoneType.ResidentialLow);
            for (int i = 0; i < SimulationClock.TicksPerDay * 2; i++) w.Step();
            output.WriteLine($"Einwohner {w.Citizens.Stats.Population}, Hinweis {w.Gateways.ConnectionHint ?? "(keiner)"}");
            Assert.True(w.Citizens.Stats.Population > 0);
            Assert.Null(w.Gateways.ConnectionHint);
        }

        [Fact]
        public void Hint_UpdatesImmediately_WhenPausedAfterConnecting()
        {
            var w = SimulationWorld.CreateDefault(Settings(false));
            var end = w.Gateways.FeederEnds.OrderBy(p => Vector2.Distance(p, Vector2.Zero)).First();
            var node = w.Roads.FindNearestNode(end, 2f);
            // Haus abseits, ohne Anschluss
            var plan = RoadBuilder.Plan(w.Roads, new Vector2(-200, 400), 8f, null, new Vector2(200, 400), 8f, RoadCatalog.CityTwoLane);
            RoadBuilder.Build(w.Roads, plan);
            var house = w.City.PlanHouse(HouseCatalog.Low, new Vector2(40, 415));
            Assert.True(house.IsValid, house.Error.ToString());
            w.City.BuildHouse(house);
            Assert.Contains("anschließen", w.Gateways.ConnectionHint);
            // anschließen – ohne einen einzigen Tick
            var link = RoadBuilder.Plan(w.Roads, node.Position, 2f, null, new Vector2(0, 400), 8f, RoadCatalog.CityTwoLane);
            Assert.True(link.IsValid, link.Error.ToString());
            RoadBuilder.Build(w.Roads, link);
            Assert.Null(w.Gateways.ConnectionHint);
            Assert.Equal(1, w.Gateways.HousingConnected);
        }
    }
}
