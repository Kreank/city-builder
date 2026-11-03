using System.Collections.Generic;
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
    /// <summary>
    /// Netze werden selbst verlegt (Entscheidung des Spielers): Wasserrohre und Strom mit Spannungsebenen
    /// (Kraftwerk → Hochspannung → Umspannwerk → Ortsnetz als Freileitung oder Erdkabel → Gebäude),
    /// Hausanschluss in 12 m, „Netz entlang Straßen verlegen“, Determinismus.
    /// </summary>
    public class NetworkTests
    {
        readonly ITestOutputHelper output;
        public NetworkTests(ITestOutputHelper output) => this.output = output;

        static readonly Vector2 PlantPos = new Vector2(-150, -150);   // Kohlekraftwerk 48×36: x −174..−126, y −168..−132
        static readonly Vector2 SubPos = new Vector2(-60, -80);       // Umspannwerk 24×20: x −72..−48, y −90..−70

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, t ?? RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        static Building[] Constructed(SimulationWorld w) => w.City.Buildings.Where(b => b.IsConstructed).OrderBy(b => b.Id).ToArray();

        /// <summary>Straßen bauen, beidseits Wohngebiet malen und wachsen lassen.</summary>
        static SimulationWorld Town(int minBuildings, params (Vector2 a, Vector2 b)[] streets) => Town(minBuildings, RoadCatalog.CityTwoLane, streets);

        static SimulationWorld Town(int minBuildings, RoadType type, params (Vector2 a, Vector2 b)[] streets)
        {
            var w = SimulationWorld.CreateDefault();
            w.Utilities.AbandonmentEnabled = false;
            foreach (var (a, b) in streets) Road(w, a, b, type);
            foreach (var (a, b) in streets) w.City.Paint((a + b) * 0.5f, Vector2.Distance(a, b) * 0.5f + 20f, ZoneType.ResidentialLow);
            for (int i = 0; i < 3000; i++)
            {
                w.Step();
                if (i % 50 == 0 && Constructed(w).Length >= minBuildings) break;
            }
            for (int i = 0; i < 60; i++) w.Step();
            Assert.True(Constructed(w).Length >= minBuildings, $"nur {Constructed(w).Length} Gebäude");
            return w;
        }

        /// <summary>Kraftwerk + Hochspannung + Umspannwerk + Erdkabel-Stich vom Umspannwerk zum Punkt <paramref name="to"/>.</summary>
        static (UtilityFacility plant, UtilityFacility sub) Chain(SimulationWorld w, Vector2 to, UtilityFacilityType subType = null)
        {
            var u = w.Utilities;
            var plant = u.TryBuildFacility(UtilityCatalog.CoalPlant, PlantPos);
            Assert.NotNull(plant);
            var sub = u.TryBuildFacility(subType ?? UtilityCatalog.Substation, SubPos);
            Assert.NotNull(sub);
            GridKit.Lay(w, UtilityCatalog.HighVoltageLine, new Vector2(-150, -132), new Vector2(-72, -80)); // Kraftwerk-Nordrand → Umspannwerk-Westrand
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(-60, -70), to);                      // Umspannwerk-Nordrand → Ortsnetz
            return (plant, sub);
        }

        // ------------------------------------------------------------ Wasser

        [Fact]
        public void Water_HouseWithoutPipe_IsUnsupplied_PipeWithin12m_Supplies()
        {
            var w = Town(10, (new Vector2(0, 0), new Vector2(300, 0)));
            var u = w.Utilities;
            Assert.False(ConnectionRules.CarriedByRoads(UtilityKind.Water));
            var tower = u.TryBuildFacility(UtilityCatalog.WaterTower, new Vector2(-40, 40)); // abseits der Straße: x −46..−34, y 34..46
            Assert.NotNull(tower);
            u.ForceTick();
            var houses = Constructed(w);
            Assert.True(u.IsRequired(UtilityKind.Water));
            Assert.All(houses, b => Assert.False(u.IsSupplied(b, UtilityKind.Water), $"{b} hat Wasser ohne Rohr"));
            Assert.All(houses, b => Assert.False(u.IsSatisfied(b)));

            // Rohr vom Turm zum Straßenanfang, dann unter dem Nord-Gehweg bis zur Straßenmitte (x = 150)
            var start = u.ResolveLinePoint(UtilityCatalog.WaterPipe, new Vector2(2, 5), 8f, out var snap);
            Assert.Equal(UtilitySnap.Road, snap);
            var end = u.ResolveLinePoint(UtilityCatalog.WaterPipe, new Vector2(150, 5), 8f, out snap);
            Assert.Equal(start.Y, end.Y, 2);
            GridKit.Lay(w, UtilityCatalog.WaterPipe, new Vector2(-34, 40), start);
            GridKit.Lay(w, UtilityCatalog.WaterPipe, start, end);
            u.ForceTick();

            houses = Constructed(w);
            foreach (var b in houses)
            {
                bool inReach = u.ConnectedLine(b, UtilityKind.Water) != null;
                Assert.Equal(inReach, u.IsSupplied(b, UtilityKind.Water));
            }
            // Beide Straßenseiten hängen am selben Rohr unter dem Gehweg; jenseits x = 150 + 12 m kein Anschluss
            Assert.Contains(houses, b => b.Entrance.Y > 0f && u.IsSupplied(b, UtilityKind.Water));
            Assert.Contains(houses, b => b.Entrance.Y < 0f && u.IsSupplied(b, UtilityKind.Water));
            Assert.Contains(houses, b => b.Entrance.X > 175f && !u.IsSupplied(b, UtilityKind.Water));
            var line = u.ConnectedLine(houses.First(b => u.IsSupplied(b, UtilityKind.Water)), UtilityKind.Water);
            Assert.True(ConnectionRules.HouseConnection(line, houses.First(b => u.IsSupplied(b, UtilityKind.Water)), out var from, out var to));
            Assert.True(Vector2.Distance(from, to) <= ConnectionRules.BuildingReach);
        }

        // ------------------------------------------------------------ Strom: Ebenen

        [Fact]
        public void LocalGrid_WithoutSubstation_IsUnsupplied()
        {
            var w = Town(8, (new Vector2(0, 0), new Vector2(300, 0)));
            var u = w.Utilities;
            Assert.False(ConnectionRules.CarriedByRoads(UtilityKind.Power));
            var plant = u.TryBuildFacility(UtilityCatalog.CoalPlant, PlantPos);
            Assert.NotNull(plant);
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(0, 0), new Vector2(300, 0));
            // Erdkabel direkt ans Kraftwerk: große Kraftwerke speisen nur in die Hochspannung → nichts
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(-126, -150), new Vector2(0, 0));
            u.ForceTick();
            var houses = Constructed(w);
            Assert.All(houses, b => Assert.False(u.IsSupplied(b, UtilityKind.Power)));
            var local = u.NetworkOf(houses[0], UtilityKind.Power);
            Assert.Equal(0f, local.Production);
            Assert.False(local.HasSource);
            Assert.True(local.HasShortage);
            Assert.NotEqual(local, u.NetworkOf(plant));

            // Umspannwerk am Ortsnetz, aber ohne Hochspannung: weiterhin unversorgt
            var sub = u.TryBuildFacility(UtilityCatalog.Substation, SubPos);
            Assert.NotNull(sub);
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(-60, -70), new Vector2(0, 0));
            u.ForceTick();
            Assert.All(houses, b => Assert.False(u.IsSupplied(b, UtilityKind.Power)));
            Assert.Equal(1, u.NetworkOf(sub).Substations);
            Assert.Equal(0f, sub.Load);
            Assert.True(u.IsRequired(UtilityKind.Power));

            // Hochspannung vom Kraftwerk zum Umspannwerk schließt die Kette
            GridKit.Lay(w, UtilityCatalog.HighVoltageLine, new Vector2(-150, -132), new Vector2(-72, -80));
            u.ForceTick();
            Assert.All(houses, b => Assert.True(u.IsSupplied(b, UtilityKind.Power), $"{b} ohne Strom"));
            Assert.Same(u.NetworkOf(plant), u.UpstreamNetworkOf(sub));
            Assert.Equal(GridLevel.HighVoltage, u.NetworkOf(plant).Level);
        }

        [Fact]
        public void HighVoltage_DoesNotConnectBuildings()
        {
            var w = Town(6, (new Vector2(0, 0), new Vector2(300, 0)));
            var u = w.Utilities;
            var plant = u.TryBuildFacility(UtilityCatalog.CoalPlant, PlantPos);
            Assert.NotNull(plant);
            GridKit.Lay(w, UtilityCatalog.HighVoltageLine, new Vector2(-150, -132), new Vector2(0, 0));
            GridKit.Lay(w, UtilityCatalog.HighVoltageLine, new Vector2(0, 0), new Vector2(300, 0)); // Hochspannung längs der Straße
            u.ForceTick();
            foreach (var b in Constructed(w))
            {
                Assert.False(u.IsSupplied(b, UtilityKind.Power));
                Assert.Null(u.ConnectedLine(b, UtilityKind.Power));
            }
            // Große Masten: 4 × 4 m, weite Spannweite
            var hv = u.Lines.First(l => l.Type == UtilityCatalog.HighVoltageLine && l.A == new Vector2(0, 0));
            Assert.True(hv.PoleFootprintHalf > UtilitySystem.PoleFootprintHalf);
            for (int i = 1; i < hv.Poles.Count; i++)
                Assert.True(Vector2.Distance(hv.Poles[i - 1], hv.Poles[i]) <= UtilityCatalog.HighVoltageLine.MaxSpan + 0.01f);
        }

        [Fact]
        public void SmallGenerator_FeedsLocalGridDirectly_WithoutSubstation()
        {
            var w = Town(6, (new Vector2(0, 0), new Vector2(300, 0)));
            var u = w.Utilities;
            var wind = u.TryBuildFacility(UtilityCatalog.WindTurbine, new Vector2(-40, -40)); // x/y −46..−34
            Assert.NotNull(wind);
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(-34, -40), new Vector2(0, 0));
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(0, 0), new Vector2(300, 0));
            u.ForceTick();
            var net = u.NetworkOf(wind);
            Assert.Equal(GridLevel.Distribution, net.Level);
            Assert.Equal(0, net.Substations);
            Assert.True(net.Demand <= wind.Output, $"Bedarf {net.Demand} > {wind.Output}");
            Assert.All(Constructed(w), b => Assert.True(u.IsSupplied(b, UtilityKind.Power)));
        }

        [Fact]
        public void Substation_CapacityLimitsLoad()
        {
            var w = Town(8, (new Vector2(0, 0), new Vector2(300, 0)));
            var u = w.Utilities;
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(0, 0), new Vector2(300, 0));
            // Probelauf mit großem Umspannwerk: Bedarf des Ortsnetzes
            var (_, big) = Chain(w, new Vector2(0, 0));
            u.ForceTick();
            float demand = u.NetworkOf(big).Demand;
            Assert.True(demand > 0f);
            Assert.All(Constructed(w), b => Assert.True(u.IsSupplied(b, UtilityKind.Power)));
            Assert.Equal(demand, big.Load, 2);
            u.RemoveFacility(big.Id);

            // Kleines Umspannwerk mit halbem Bedarf: begrenzt die Last trotz reichlich Kraftwerksleistung
            var small = new UtilityFacilityType("sub_small", "Kleines Umspannwerk", UtilityKind.Power, demand * 0.5f, 24f, 20f, 0f, 0f,
                role: FacilityRole.Substation);
            var s1 = u.TryBuildFacility(small, SubPos);
            Assert.NotNull(s1);
            u.ForceTick();
            var local = u.NetworkOf(s1);
            int supplied = Constructed(w).Count(b => u.IsSupplied(b, UtilityKind.Power));
            output.WriteLine($"Bedarf {demand:0} kW, Umspannwerk {small.Capacity:0} kW → versorgt {supplied}/{Constructed(w).Length}");
            Assert.Equal(small.Capacity, s1.Load, 2);
            Assert.Equal(small.Capacity, local.Imported, 2);
            Assert.True(local.HasShortage);
            Assert.True(supplied > 0 && supplied < Constructed(w).Length);
            Assert.True(local.Delivered <= small.Capacity + 1e-3f);

            // Zweites Umspannwerk am selben Ortsnetz: Last verteilt sich nach Kapazität, alle versorgt
            var s2 = u.TryBuildFacility(small, new Vector2(-100, -80)); // x −112..−88
            Assert.NotNull(s2);
            GridKit.Lay(w, UtilityCatalog.HighVoltageLine, new Vector2(-150, -132), new Vector2(-112, -80));
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(-100, -70), new Vector2(0, 0));
            u.ForceTick();
            Assert.All(Constructed(w), b => Assert.True(u.IsSupplied(b, UtilityKind.Power)));
            Assert.Equal(s1.Load, s2.Load, 2);
            Assert.Equal(demand, s1.Load + s2.Load, 1);
            Assert.Equal(2, u.NetworkOf(s1).Substations);
            Assert.Equal(2f * small.Capacity, u.NetworkOf(s1).TransformerCapacity, 1);
        }

        [Fact]
        public void HighVoltageShortage_IsSharedProportionally()
        {
            var w = Town(12, (new Vector2(0, 0), new Vector2(300, 0)), (new Vector2(0, 300), new Vector2(300, 300)));
            var u = w.Utilities;
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(0, 0), new Vector2(300, 0));
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(0, 300), new Vector2(300, 300));
            var probe = new UtilityFacilityType("probe_hv", "Probe", UtilityKind.Power, 1e6f, 10f, 10f, 0f, 0f); // speist nur in die Hochspannung
            var plant = u.TryBuildFacility(probe, new Vector2(-150, 150));
            Assert.NotNull(plant);
            var south = u.TryBuildFacility(UtilityCatalog.Substation, SubPos);
            var north = u.TryBuildFacility(UtilityCatalog.Substation, new Vector2(-60, 220)); // x −72..−48, y 210..230
            Assert.NotNull(south);
            Assert.NotNull(north);
            GridKit.Lay(w, UtilityCatalog.HighVoltageLine, new Vector2(-145, 150), new Vector2(-72, -80));
            GridKit.Lay(w, UtilityCatalog.HighVoltageLine, new Vector2(-145, 150), new Vector2(-72, 220));
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(-60, -70), new Vector2(0, 0));
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(-60, 230), new Vector2(0, 300));
            u.ForceTick();
            float dS = u.NetworkOf(south).Demand, dN = u.NetworkOf(north).Demand;
            Assert.True(dS > 0f && dN > 0f);
            Assert.NotSame(u.NetworkOf(south), u.NetworkOf(north));
            Assert.Same(u.UpstreamNetworkOf(south), u.UpstreamNetworkOf(north));
            Assert.All(Constructed(w), b => Assert.True(u.IsSupplied(b, UtilityKind.Power)));

            // Gleiches Bauwerk mit halber Leistung: beide Umspannwerke bekommen denselben Anteil
            u.RemoveFacility(plant.Id);
            var weak = new UtilityFacilityType("probe_hv_half", "Probe halb", UtilityKind.Power, (dS + dN) * 0.5f, 10f, 10f, 0f, 0f);
            Assert.NotNull(u.TryBuildFacility(weak, new Vector2(-150, 150)));
            u.ForceTick();
            output.WriteLine($"Süd {south.Load:0}/{dS:0} kW, Nord {north.Load:0}/{dN:0} kW");
            Assert.Equal(dS * 0.5f, south.Load, 1);
            Assert.Equal(dN * 0.5f, north.Load, 1);
            Assert.True(u.UpstreamNetworkOf(south).HasShortage);
            Assert.Contains(Constructed(w), b => !u.IsSupplied(b, UtilityKind.Power));
        }

        // ------------------------------------------------------------ Freileitung vs. Erdkabel

        [Fact]
        public void Overhead_IsCheaperButNeedsSpace_CableCostsMoreButFitsAnywhere()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(0, 0), new Vector2(300, 0));
            var u = w.Utilities;
            var over = u.PlanLine(UtilityCatalog.OverheadLine, new Vector2(20, 0), new Vector2(280, 0));
            var cable = u.PlanLine(UtilityCatalog.UndergroundCable, new Vector2(20, 0), new Vector2(280, 0));
            Assert.True(over.IsValid && cable.IsValid);
            Assert.True(cable.Cost > over.Cost, $"Erdkabel {cable.Cost} ≤ Freileitung {over.Cost}");
            Assert.Equal(260f * UtilityCatalog.OverheadLine.CostPerMeter, over.Cost, 1);
            Assert.Equal(260f * UtilityCatalog.UndergroundCable.CostPerMeter, cable.Cost, 1);
            Assert.NotEmpty(over.Poles);
            Assert.Empty(cable.Poles);
            var o = u.BuildLine(over);
            var c = u.BuildLine(cable);
            Assert.True(w.City.LotBlocker(o.Poles[1]));                      // Masten belegen Grundstücke neben der Straße
            Assert.Empty(c.Poles);

            // Kein Platz neben der Straße (beidseits Bauwerke bis an den Gehweg): Freileitung geht nicht, Erdkabel schon
            Assert.True(u.RemoveLine(o.Id) && u.RemoveLine(c.Id));
            Assert.NotNull(u.TryBuildFacility(UtilityCatalog.SolarPark, new Vector2(150, 23)));  // y 7..39
            Assert.NotNull(u.TryBuildFacility(UtilityCatalog.SolarPark, new Vector2(150, -23))); // y −39..−7
            Assert.False(u.PlanLine(UtilityCatalog.OverheadLine, new Vector2(138, 0), new Vector2(162, 0)).IsValid);
            Assert.True(u.PlanLine(UtilityCatalog.UndergroundCable, new Vector2(138, 4), new Vector2(162, 4)).IsValid);

            // Bestehende Stadt: Freileitung entlang der Straße reißt nie ab und stellt keinen Mast auf ein Gebäude,
            // Erdkabel ist überall möglich und erreicht alle Gebäude
            var town = Town(14, (new Vector2(0, 0), new Vector2(300, 0)));
            int buildings = Constructed(town).Length;
            var overhead = town.Utilities.PlanAlongRoads(UtilityCatalog.OverheadLine);
            var cables = town.Utilities.PlanAlongRoads(UtilityCatalog.UndergroundCable);
            output.WriteLine($"Freileitung: {overhead.ValidCount} gültig, {overhead.Invalid} ungültig ({overhead.FirstInvalid}), " +
                             $"{overhead.Cost:0} € · Erdkabel: {cables.ValidCount} gültig, {cables.Cost:0} €, erreicht {cables.BuildingsReached}");
            Assert.Equal(0, cables.Invalid);
            Assert.True(cables.IsValid);
            Assert.Equal(buildings, cables.BuildingsReached);
            Assert.True(cables.Cost > overhead.Cost);
            Assert.True(overhead.Invalid == 0 || overhead.FirstInvalid == UtilityBuildError.PoleOnBuilding, overhead.FirstInvalid.ToString());
            town.Utilities.BuildAlongRoads(overhead);
            Assert.Equal(buildings, Constructed(town).Length);
            foreach (var l in town.Utilities.Lines)
                foreach (var p in l.Poles)
                    Assert.DoesNotContain(town.City.Buildings, b => b.Contains(p, l.PoleFootprintHalf - 0.01f));
        }

        // ------------------------------------------------------------ Entlang Straßen verlegen

        [Fact]
        public void AlongRoads_SuppliesAnExistingCity_AtNormalCosts()
        {
            // Bestehende Stadt mit Kreuzung und Kurve, noch ohne Leitungen
            var w = SimulationWorld.CreateDefault();
            w.Utilities.AbandonmentEnabled = false;
            Road(w, new Vector2(0, 0), new Vector2(300, 0));
            Road(w, new Vector2(150, -150), new Vector2(150, 150));
            var curve = RoadBuilder.Plan(w.Roads, new Vector2(300, 0), 8f, new Vector2(400, 0), new Vector2(400, 120), 8f, RoadCatalog.CityTwoLane);
            Assert.True(curve.IsValid, curve.Error.ToString());
            RoadBuilder.Build(w.Roads, curve);
            w.City.Paint(new Vector2(150, 0), 200f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(360, 60), 90f, ZoneType.ResidentialLow);
            for (int i = 0; i < 3000 && Constructed(w).Length < 16; i++) w.Step();
            for (int i = 0; i < 60; i++) w.Step();
            var u = w.Utilities;
            Assert.True(Constructed(w).Length >= 12, $"{Constructed(w).Length} Gebäude");

            var (plant, sub) = Chain(w, new Vector2(0, 0));
            var tower = u.TryBuildFacility(UtilityCatalog.WaterTower, new Vector2(-40, 40));
            Assert.NotNull(tower);
            GridKit.Lay(w, UtilityCatalog.WaterPipe, new Vector2(-34, 40), new Vector2(0, 0));
            u.ForceTick();
            Assert.All(Constructed(w), b => Assert.False(u.IsSupplied(b, UtilityKind.Power)));

            // Vorschau: Kosten = Summe der Stücke zu den normalen Meterpreisen, alle Gebäude erreicht
            var plan = u.PlanAlongRoads(UtilityCatalog.UndergroundCable);
            Assert.True(plan.IsValid, plan.Error.ToString());
            Assert.Equal(0, plan.Invalid);
            Assert.Equal(plan.Pieces.Sum(p => p.Length) * UtilityCatalog.UndergroundCable.CostPerMeter, plan.Cost, 0);
            Assert.Equal(Constructed(w).Length, plan.BuildingsReached);
            Assert.Equal(w.Roads.Segments.Count(), plan.SegmentIds.Count);
            double before = w.Economy.Balance;
            int built = u.BuildAlongRoads(plan);
            Assert.Equal(plan.ValidCount, built);
            Assert.Equal(plan.Cost, (float)(before - w.Economy.Balance), 0);

            var water = u.PlanAlongRoads(UtilityCatalog.WaterPipe);
            Assert.True(u.BuildAlongRoads(water) > 0);
            u.ForceTick();
            output.WriteLine($"Erdkabel {plan.Length:0} m / {plan.Cost:0} € in {built} Stücken, Wasser {water.Length:0} m / {water.Cost:0} €");
            Assert.All(Constructed(w), b => Assert.True(u.IsSupplied(b), $"{b}: Strom {u.IsSupplied(b, UtilityKind.Power)}, Wasser {u.IsSupplied(b, UtilityKind.Water)}"));
            Assert.Single(u.Networks(UtilityKind.Power), n => n.Buildings > 0);   // alle Stränge über die Knoten verbunden
            Assert.Single(u.Networks(UtilityKind.Water), n => n.Buildings > 0);

            // Zweimal verlegen kostet nichts: alles schon da
            var again = u.PlanAlongRoads(UtilityCatalog.UndergroundCable);
            Assert.Equal(0, again.ValidCount);
            Assert.True(again.Existing > 0);
            Assert.False(again.IsValid);
            Assert.Equal(0, u.BuildAlongRoads(again));

            // Einzelne Straße: nur dieses Segment
            var one = u.PlanAlongRoad(UtilityCatalog.SewerPipe, w.Roads.Segments.First().Id);
            Assert.Single(one.SegmentIds);
            Assert.True(one.IsValid);
        }

        [Fact]
        public void AlongRoads_WideRoad_LaysCableOnBothSidewalks()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(0, 0), new Vector2(300, 0), RoadCatalog.CityFourLane);
            var narrow = SimulationWorld.CreateDefault();
            Road(narrow, new Vector2(0, 0), new Vector2(300, 0));
            var wide = w.Utilities.PlanAlongRoads(UtilityCatalog.UndergroundCable);
            var small = narrow.Utilities.PlanAlongRoads(UtilityCatalog.UndergroundCable);
            float off4 = RoadCatalog.CityFourLane.HalfWidth - ConnectionRules.SidewalkInset;
            Assert.Contains(wide.Pieces, p => System.MathF.Abs(p.A.Y - off4) < 0.01f && System.MathF.Abs(p.B.Y - off4) < 0.01f);
            Assert.Contains(wide.Pieces, p => System.MathF.Abs(p.A.Y + off4) < 0.01f && System.MathF.Abs(p.B.Y + off4) < 0.01f);
            Assert.True(wide.Length > small.Length * 1.8f);
            // Schmale Straße: ein Strang reicht bis zu den Zufahrten gegenüber (≤ 12 m)
            float off2 = RoadCatalog.CityTwoLane.HalfWidth - ConnectionRules.SidewalkInset;
            Assert.True(RoadCatalog.CityTwoLane.HalfWidth + off2 <= ConnectionRules.BuildingReach);
            Assert.DoesNotContain(small.Pieces, p => p.A.Y > 0.01f && p.B.Y > 0.01f); // nur rechts (in Segmentrichtung = −y)
        }

        // ------------------------------------------------------------ Determinismus

        [Fact]
        public void Networks_AreDeterministic_AndIndependentOfBuildOrder()
        {
            string Run(bool reverse)
            {
                var w = Town(10, (new Vector2(0, 0), new Vector2(300, 0)));
                var u = w.Utilities;
                var small = new UtilityFacilityType("sub_det", "Umspannwerk klein", UtilityKind.Power, 40f, 24f, 20f, 0f, 0f, role: FacilityRole.Substation);
                var steps = new List<System.Action>
                {
                    () => Assert.NotNull(u.TryBuildFacility(UtilityCatalog.CoalPlant, PlantPos)),
                    () => Assert.NotNull(u.TryBuildFacility(small, SubPos)),
                    () => GridKit.Lay(w, UtilityCatalog.HighVoltageLine, new Vector2(-150, -132), new Vector2(-72, -80)),
                    () => GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(-60, -70), new Vector2(0, 0)),
                    () => GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(0, 0), new Vector2(150, 0)),
                    () => GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(150, 0), new Vector2(300, 0)),
                };
                if (reverse) steps.Reverse();
                foreach (var s in steps) s();
                for (int i = 0; i < 20; i++) w.Step();
                u.ForceTick();
                var sub = u.Substations.Single();
                var supplied = string.Join(",", Constructed(w).Where(b => u.IsSupplied(b, UtilityKind.Power)).Select(b => b.Id));
                return $"{sub.Load:0.###}|{u.TotalProduction(UtilityKind.Power):0.###}|{u.TotalDemand(UtilityKind.Power):0.###}|{supplied}";
            }

            string a = Run(false), b = Run(false), c = Run(true);
            output.WriteLine(a);
            Assert.Equal(a, b);
            Assert.Equal(a, c);
            Assert.Contains("|", a);
        }
    }
}
