using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    public class UtilityTests
    {
        readonly ITestOutputHelper output;
        public UtilityTests(ITestOutputHelper output) => this.output = output;

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, t ?? RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        /// <summary>Zone malen und die Stadt wachsen lassen, bis genug fertige Gebäude stehen.</summary>
        static void Grow(SimulationWorld w, Vector2 center, float radius, ZoneType zone, int minBuildings)
        {
            w.City.Paint(center, radius, zone);
            for (int i = 0; i < 3000; i++)
            {
                w.Step();
                if (i % 50 == 0 && w.City.Buildings.Count(b => b.IsConstructed &&
                        Vector2.Distance(b.Position, center) < radius + 20f) >= minBuildings) break;
            }
            // fertig bauen lassen
            for (int i = 0; i < 60; i++) w.Step();
        }

        static Building[] BuildingsNear(SimulationWorld w, Vector2 center, float radius) =>
            w.City.Buildings.Where(b => b.IsConstructed && Vector2.Distance(b.Position, center) < radius).OrderBy(b => b.Id).ToArray();

        static SimulationWorld TwoStreets(out Building[] west, out Building[] east)
        {
            var w = SimulationWorld.CreateDefault();
            w.Utilities.AbandonmentEnabled = false;
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            Road(w, new Vector2(500, 0), new Vector2(700, 0));
            Grow(w, new Vector2(100, 20), 80f, ZoneType.ResidentialLow, 4);
            Grow(w, new Vector2(600, 20), 80f, ZoneType.ResidentialLow, 4);
            west = BuildingsNear(w, new Vector2(100, 20), 110f);
            east = BuildingsNear(w, new Vector2(600, 20), 110f);
            Assert.True(west.Length >= 3, $"West: {west.Length}");
            Assert.True(east.Length >= 3, $"Ost: {east.Length}");
            return w;
        }

        [Fact]
        public void Roads_CarryPower_ToBuildings_ButNotAcrossGaps()
        {
            var w = TwoStreets(out var west, out var east);
            var u = w.Utilities;
            // Kraftwerk am Westende der ersten Straße (Rand der Fahrbahn < 8 m entfernt)
            var plant = u.TryBuildFacility(UtilityCatalog.CoalPlant, new Vector2(-35, 0));
            Assert.NotNull(plant);
            u.ForceTick();

            Assert.All(west, b => Assert.True(u.IsSupplied(b, UtilityKind.Power), $"{b} ohne Strom"));
            Assert.All(east, b => Assert.False(u.IsSupplied(b, UtilityKind.Power), $"{b} hat Strom trotz Lücke"));
            Assert.All(west, b => Assert.False(u.IsSupplied(b, UtilityKind.Water)));
            Assert.Equal(u.NetworkOf(west[0], UtilityKind.Power), u.NetworkOf(west[^1], UtilityKind.Power));
            Assert.NotEqual(u.NetworkOf(west[0], UtilityKind.Power), u.NetworkOf(east[0], UtilityKind.Power));
            Assert.Same(u.NetworkOf(west[0], UtilityKind.Power), u.Networks(UtilityKind.Power)[plant.NetworkId]);
        }

        [Fact]
        public void PowerLine_BridgesGap_AndChainsOfLinesConnect()
        {
            var w = TwoStreets(out _, out var east);
            var u = w.Utilities;
            u.TryBuildFacility(UtilityCatalog.CoalPlant, new Vector2(-35, 0));

            // Zwei Leitungsstücke: Straßenende West → Zwischenpunkt → Straßenanfang Ost
            Assert.NotNull(u.TryBuildLine(UtilityKind.Power, new Vector2(200, 0), new Vector2(350, 40)));
            u.ForceTick();
            Assert.All(east, b => Assert.False(u.IsSupplied(b, UtilityKind.Power)));
            Assert.NotNull(u.TryBuildLine(UtilityKind.Power, new Vector2(350, 40), new Vector2(500, 0)));
            u.ForceTick();
            Assert.All(east, b => Assert.True(u.IsSupplied(b, UtilityKind.Power)));
            Assert.Single(u.Networks(UtilityKind.Power), n => n.Buildings > 0);

            // Wasserrohr verbindet kein Stromnetz
            var w2 = TwoStreets(out _, out var east2);
            w2.Utilities.TryBuildFacility(UtilityCatalog.CoalPlant, new Vector2(-35, 0));
            w2.Utilities.TryBuildLine(UtilityKind.Water, new Vector2(200, 0), new Vector2(500, 0));
            w2.Utilities.ForceTick();
            Assert.All(east2, b => Assert.False(w2.Utilities.IsSupplied(b, UtilityKind.Power)));

            // Leitung abreißen trennt wieder
            var line = u.LineNear(new Vector2(420, 20), 5f, UtilityKind.Power);
            Assert.NotNull(line);
            Assert.True(u.RemoveLine(line.Id));
            u.ForceTick();
            Assert.All(east, b => Assert.False(u.IsSupplied(b, UtilityKind.Power)));
        }

        [Fact]
        public void Highways_DoNotCarryUtilities_CityRoadsDo()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            Road(w, new Vector2(250, 0), new Vector2(650, 0), RoadCatalog.Highway2);
            var u = w.Utilities;
            Assert.NotNull(u.TryBuildFacility(UtilityCatalog.WindTurbine, new Vector2(-20, 0)));
            var a = u.TryBuildLine(UtilityKind.Power, new Vector2(200, 0), new Vector2(250, 0));
            var b = u.TryBuildLine(UtilityKind.Power, new Vector2(650, 0), new Vector2(700, 0));
            u.ForceTick();
            Assert.NotEqual(a.NetworkId, b.NetworkId);

            // Dieselbe Strecke als Stadtstraße verbindet beide Leitungen
            var w2 = SimulationWorld.CreateDefault();
            Road(w2, new Vector2(0, 0), new Vector2(200, 0));
            Road(w2, new Vector2(250, 0), new Vector2(650, 0));
            var a2 = w2.Utilities.TryBuildLine(UtilityKind.Power, new Vector2(200, 0), new Vector2(250, 0));
            var b2 = w2.Utilities.TryBuildLine(UtilityKind.Power, new Vector2(650, 0), new Vector2(700, 0));
            w2.Utilities.ForceTick();
            Assert.Equal(a2.NetworkId, b2.NetworkId);
        }

        [Fact]
        public void Shortage_SuppliesOldestFirst_AndNeverMoreThanProduced()
        {
            var w = TwoStreets(out var west, out _);
            var u = w.Utilities;
            var probe = u.TryBuildFacility(new UtilityFacilityType("probe", "Test", UtilityKind.Power, 0.001f, 10f, 10f, 0f, 0f), new Vector2(-15, 0));
            u.ForceTick();
            var net = u.Networks(UtilityKind.Power)[probe.NetworkId];
            float demand = net.Demand;
            output.WriteLine($"Bedarf West: {demand:0.#} kW, {net.Buildings} Gebäude");
            Assert.True(demand > 0f);
            Assert.True(net.HasShortage);
            Assert.All(west, b => Assert.False(u.IsSupplied(b, UtilityKind.Power)));

            u.RemoveFacility(probe.Id);
            var half = u.TryBuildFacility(new UtilityFacilityType("half", "Halb", UtilityKind.Power, demand * 0.5f, 10f, 10f, 0f, 0f), new Vector2(-15, 0));
            u.ForceTick();
            net = u.Networks(UtilityKind.Power)[half.NetworkId];
            int supplied = west.Count(b => u.IsSupplied(b, UtilityKind.Power));
            output.WriteLine($"Versorgt {supplied}/{west.Length}, geliefert {net.Delivered:0.#} von {net.Production:0.#}");
            Assert.True(supplied > 0 && supplied < west.Length);
            Assert.True(net.Delivered <= net.Production + 1e-3f);
            Assert.True(u.IsSupplied(west[0], UtilityKind.Power), "Ältestes Gebäude zuerst");
            Assert.InRange(net.Coverage, 0.49f, 0.51f);
        }

        [Fact]
        public void LongOutage_AbandonsBuildings_OnlyOnceUtilityExists()
        {
            var w = TwoStreets(out var west, out var east);
            var u = w.Utilities;
            u.AbandonmentEnabled = true;

            // Startphase: weder Kraftwerk noch Pumpe → keine Folgen, auch nach langer Zeit
            for (int i = 0; i < UtilitySystem.AbandonAfterTicks / u.TickInterval + 5; i++) u.ForceTick();
            Assert.All(west.Concat(east), b => Assert.NotNull(w.City.GetBuilding(b.Id)));
            Assert.True(u.IsSatisfied(west[0]));

            // Westen bekommt Strom + Wasser, der Osten nichts
            u.TryBuildFacility(UtilityCatalog.CoalPlant, new Vector2(-35, 0));
            u.TryBuildFacility(UtilityCatalog.WaterTower, new Vector2(215, 0));
            u.ForceTick();
            Assert.All(west, b => Assert.True(u.IsSupplied(b), $"{b}: {u.GetStatus(b).HasPower}/{u.GetStatus(b).HasWater}"));
            Assert.False(u.IsSatisfied(east[0]));

            for (int i = 0; i < UtilitySystem.AbandonAfterTicks / u.TickInterval - 2; i++) u.ForceTick();
            Assert.All(east, b => Assert.NotNull(w.City.GetBuilding(b.Id)));
            Assert.True(u.GetStatus(east[0]).TicksWithoutPower > 0);
            for (int i = 0; i < 4; i++) u.ForceTick();
            Assert.All(east, b => Assert.Null(w.City.GetBuilding(b.Id)));
            Assert.All(west, b => Assert.NotNull(w.City.GetBuilding(b.Id)));
            Assert.True(u.AbandonedBuildings >= east.Length);
        }

        [Fact]
        public void Facility_Placement_Rules()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            var u = w.Utilities;
            Assert.Equal(UtilityBuildError.OverlapsRoad, u.PlanFacility(UtilityCatalog.GasPlant, new Vector2(100, 5)).Error);
            Assert.Equal(UtilityBuildError.NeedsWater, u.PlanFacility(UtilityCatalog.WaterPump, new Vector2(100, 60)).Error);
            Assert.Equal(UtilityBuildError.OutOfWorld, u.PlanFacility(UtilityCatalog.CoalPlant, w.Max - new Vector2(5, 5)).Error);
            var gas = u.TryBuildFacility(UtilityCatalog.GasPlant, new Vector2(100, 40));
            Assert.NotNull(gas);
            Assert.Equal(UtilityBuildError.OverlapsFacility, u.PlanFacility(UtilityCatalog.WindTurbine, new Vector2(110, 45)).Error);

            // Auf der Fläche wächst nichts, auch wenn sie gezont ist
            w.City.Paint(new Vector2(100, 30), 40f, ZoneType.ResidentialLow);
            for (int i = 0; i < 400; i++) w.Step();
            Assert.DoesNotContain(w.City.Buildings, b => gas.Contains(b.Position, 2f));

            // Straße durchs Kraftwerk reißt es ab
            Road(w, new Vector2(100, -60), new Vector2(100, 120));
            Assert.Null(u.GetFacility(gas.Id));
        }

        [Fact]
        public void WaterPump_NeedsShore_OnGeneratedTerrain()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { GenerateTerrain = true });
            var t = w.Terrain;
            // Ufer suchen: entlang y = 0 vom Land ins Wasser
            Vector2? shore = null;
            for (float x = w.Min.X + 50f; x < w.Max.X - 50f; x += 2f)
                if (!t.IsWater(new Vector2(x, 0)) && t.IsWater(new Vector2(x + 2f, 0))) { shore = new Vector2(x - 12f, 0); break; }
            Assert.True(shore.HasValue, "Kein Ufer gefunden");
            var plan = w.Utilities.PlanFacility(UtilityCatalog.WaterPump, shore.Value);
            Assert.True(plan.IsValid, plan.Error.ToString());
            Assert.Equal(UtilityBuildError.OnWater, w.Utilities.PlanFacility(UtilityCatalog.WaterPump, shore.Value + new Vector2(20f, 0)).Error);
        }

        [Fact]
        public void CoalPlant_AddsNoiseAndPollution()
        {
            var w = SimulationWorld.CreateDefault();
            var plant = w.Utilities.TryBuildFacility(UtilityCatalog.CoalPlant, new Vector2(300, 300));
            Assert.NotNull(plant);
            for (int i = 0; i < 100; i++) w.Step();
            Assert.True(w.Environment.Noise.Sample(plant.Position) > 0.5f);
            float air = w.Environment.AirPollution.Sample(plant.Position);
            output.WriteLine($"Abgas am Kraftwerk: {air:0.###}");
            Assert.InRange(air, 0.05f, 2f);
            Assert.Equal(0f, w.Environment.AirPollution.Sample(new Vector2(-600, -600)));
        }

        [Fact]
        public void ResolveLinePoint_SnapsToEndsFacilitiesAndRoads()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            var u = w.Utilities;
            var plant = u.TryBuildFacility(UtilityCatalog.WindTurbine, new Vector2(-50, -50));
            u.TryBuildLine(UtilityKind.Power, new Vector2(-50, 100), new Vector2(-100, 100));

            var p = u.ResolveLinePoint(UtilityKind.Power, new Vector2(-48, 102), 6f, out var what);
            Assert.Equal(UtilitySnap.LineEnd, what);
            Assert.Equal(new Vector2(-50, 100), p);

            p = u.ResolveLinePoint(UtilityKind.Power, new Vector2(-50, -42), 6f, out what);
            Assert.Equal(UtilitySnap.Facility, what);
            Assert.True(plant.Contains(p, 0.01f));

            u.ResolveLinePoint(UtilityKind.Water, new Vector2(-50, -42), 6f, out what);
            Assert.NotEqual(UtilitySnap.Facility, what); // Windrad ist kein Wasser-Bauwerk

            p = u.ResolveLinePoint(UtilityKind.Water, new Vector2(100, 8), 6f, out what);
            Assert.Equal(UtilitySnap.Road, what);
            Assert.Equal(0f, p.Y, 2);
        }
    }
}
