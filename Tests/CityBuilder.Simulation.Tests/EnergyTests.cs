using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Energie erweitert (Eigene Assets 2, Modelle Models/Energy): Biogas (Zufahrt, Rohstoff aus Bauernhöfen), Laufwasserkraftwerk
    /// (nur im Fluss, Leistung aus Abfluss/Fallhöhe, Fluss bleibt durchgängig), Offshore-Windpark (nur im Meer, Hochspannungs-Seekabel),
    /// Batteriespeicher (lädt bei Überschuss, liefert bei Unterdeckung, Hochspannung und Ortsnetz), Kasse, Spielstand.
    /// </summary>
    public class EnergyTests
    {
        readonly ITestOutputHelper output;
        public EnergyTests(ITestOutputHelper output) => this.output = output;

        const float RiverX = 420f;
        static readonly Vector2 SeaCenter = new Vector2(-760, -760);

        static void Road(SimulationWorld w, Vector2 a, Vector2 b)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        /// <summary>
        /// Flaches Testgelände mit Wasserspiegel −6 m: Fluss (x 400..440, Bett −10 m, Ufer 6 m über dem Wasser) über die ganze Karte
        /// und Meer in der Südwest-Ecke (natürliche Höhe −12 m per Terraforming).
        /// </summary>
        static SimulationWorld WaterWorld(WorldSettings settings = null)
        {
            var w = SimulationWorld.CreateDefault(settings);
            w.Terrain.WaterLevel = -6f;
            w.Terrain.FlattenArea(new Vector2(RiverX, 0), new Vector2(0, 1), 40f, 2040f, -10f);
            var budget = w.Terrain.Budget;
            w.Terrain.Budget = null; // Testgelände: Meer kostet nichts
            for (float x = -1000f; x <= -480f; x += 70f)
            for (float y = -1000f; y <= -480f; y += 70f)
                w.Terrain.Terraform(new TerraformBrush(TerraformMode.Level, new Vector2(x, y), 90f, 20f, 0.1f, -12f));
            w.Terrain.Budget = budget;
            Assert.True(w.Terrain.IsWater(new Vector2(RiverX, 0)));
            Assert.True(w.Terrain.IsWater(SeaCenter));
            return w;
        }

        // ------------------------------------------------------------ Wasserkraft

        [Fact]
        public void Hydro_OnlyInRiver_PowerFromFlowAndHead_RiverStaysOpen()
        {
            var w = WaterWorld();
            var u = w.Utilities;
            var t = UtilityCatalog.HydroPlant;
            Assert.Equal(UtilityBuildError.NeedsRiver, u.PlanFacility(t, new Vector2(0, 300)).Error);           // an Land
            Assert.Equal(UtilityBuildError.NeedsRiver, u.PlanFacility(t, SeaCenter).Error);                    // im Meer
            Assert.True(u.IsSea(SeaCenter));
            Assert.False(u.IsSea(new Vector2(RiverX, 0)));

            // Ausrichten: Wehr quer zum Fluss, Mitte im Fluss, Vorderseite flussabwärts (−y)
            var plan = u.PlanFacilityAligned(t, new Vector2(RiverX + 8f, 100f));
            output.WriteLine($"Wehr: Mitte {plan.Position}, Front {plan.Front}, Breite {plan.Hydro.Width:0} m, Tiefe {plan.Hydro.MeanDepth:0.0} m, " +
                             $"Q {plan.Hydro.Flow:0} m³/s, H {plan.Hydro.Head:0.0} m → {plan.Rated:0} kW, Fehler {plan.Error}");
            Assert.True(plan.IsValid, plan.Error.ToString());
            Assert.InRange(plan.Position.X, RiverX - 3f, RiverX + 3f);
            Assert.True(plan.Front.Y < -0.95f, $"Front {plan.Front}");
            Assert.InRange(plan.Hydro.Head, 4.5f, 5.5f);  // Ufer 6 m über dem Wasser − 1 m Freibord
            Assert.InRange(plan.Hydro.Width, 40f, 56f);
            Assert.Equal(UtilitySystem.HydroPower(plan.Hydro.Flow, plan.Hydro.Head), plan.Rated, 3);
            Assert.InRange(plan.Rated, 3000f, 8000f);

            float before = w.Terrain.HeightAt(plan.Position);
            var dam = u.BuildFacility(plan);
            Assert.NotNull(dam);
            Assert.Equal(plan.Rated, dam.Rated);
            // Fluss bleibt durchgängig: Gelände unter dem Wehr unverändert, Wasser bleibt Wasser (auch flussab/-auf)
            Assert.Equal(before, w.Terrain.HeightAt(dam.Position));
            Assert.True(w.Terrain.IsWater(dam.Position));
            Assert.True(w.Terrain.IsWater(dam.Position + dam.Front * 40f));
            Assert.True(w.Terrain.IsWater(dam.Position - dam.Front * 40f));

            u.ForceTick();
            Assert.Equal(dam.Rated, dam.Output, 2);
            // Trockenzeit: Abfluss unter dem Ausbaudurchfluss → weniger Leistung
            u.SetRiverFlowIndex(0.2f);
            u.ForceTick();
            output.WriteLine($"Trocken: Abfluss ×{u.RiverFlowFactor:0.00} → {dam.Output:0} kW ({dam.Availability:P0})");
            Assert.True(dam.Output < dam.Rated * 0.9f);
            Assert.True(dam.Output > 0f);
            // nur Hochspannung
            Assert.False(UtilitySystem.Accepts(dam, UtilityCatalog.OverheadLine));
            Assert.True(UtilitySystem.Accepts(dam, UtilityCatalog.HighVoltageLine));
        }

        [Fact]
        public void Hydro_LowBanks_NoSite()
        {
            var w = SimulationWorld.CreateDefault();
            w.Terrain.WaterLevel = -1.5f; // Ufer nur 1,5 m über dem Wasser → kein Aufstau möglich
            w.Terrain.FlattenArea(new Vector2(RiverX, 0), new Vector2(0, 1), 40f, 2040f, -4f);
            var plan = w.Utilities.PlanFacilityAligned(UtilityCatalog.HydroPlant, new Vector2(RiverX, 0));
            Assert.Equal(UtilityBuildError.NoHydroSite, plan.Error);
            Assert.True(plan.Hydro.Head < UtilitySystem.HydroMinHead);
        }

        // ------------------------------------------------------------ Offshore

        [Fact]
        public void Offshore_OnlyAtSea_HighVoltageOnly_SubseaCableToShore()
        {
            var w = WaterWorld();
            var u = w.Utilities;
            var t = UtilityCatalog.OffshoreWind;
            Assert.Equal(UtilityBuildError.NeedsSea, u.PlanFacility(t, new Vector2(0, 300)).Error);
            Assert.Equal(UtilityBuildError.NeedsSea, u.PlanFacility(t, new Vector2(RiverX, 0)).Error);
            var park = u.TryBuildFacility(t, SeaCenter);
            Assert.NotNull(park);
            Assert.False(UtilitySystem.Accepts(park, UtilityCatalog.OverheadLine));
            Assert.False(UtilitySystem.Accepts(park, UtilityCatalog.UndergroundCable));

            // Umspannwerk an Land, Anschluss: Freileitung findet über dem Meer keinen Maststandort, das Seekabel schon
            var sub = u.TryBuildFacility(UtilityCatalog.Substation, new Vector2(-760, -250));
            Assert.NotNull(sub);
            var from = SeaCenter + new Vector2(0, 88f);
            var to = new Vector2(-760, -258);
            var hv = u.PlanLine(UtilityCatalog.HighVoltageLine, from, to);
            Assert.False(hv.IsValid);
            output.WriteLine($"Freileitung übers Meer: {hv.Error}");
            var cable = GridKit.Lay(w, UtilityCatalog.SubseaCable, from, to);
            Assert.Empty(cable.Poles);
            u.ForceTick();
            Assert.True(park.NetworkId >= 0);
            Assert.Equal(park.NetworkId, sub.UpstreamNetworkId);
            var net = u.NetworkOf(park);
            Assert.Equal(GridLevel.HighVoltage, net.Level);
            Assert.Equal(park.Output, net.Generation, 2);

            // Wind schwankt stündlich, im Mittel um die Hälfte der Nennleistung
            var values = Enumerable.Range(0, 30 * 24).Select(h => UtilitySystem.WindFactor(h * (long)TimeModel.TicksPerHour)).ToArray();
            output.WriteLine($"Wind 30 Tage: Ø {values.Average():0.00}, min {values.Min():0.00}, max {values.Max():0.00}");
            Assert.InRange(values.Average(), 0.35, 0.65);
            Assert.True(values.Max() - values.Min() > 0.5f);
            Assert.Equal(UtilitySystem.WindFactor(1234), UtilitySystem.WindFactor(1234)); // deterministisch
        }

        // ------------------------------------------------------------ Biogas

        [Fact]
        public void Biogas_NeedsRoadAccess_OutputFromFarmsInReach()
        {
            var w = SimulationWorld.CreateDefault();
            var u = w.Utilities;
            Road(w, new Vector2(0, 0), new Vector2(400, 0));
            var t = UtilityCatalog.BiogasPlant;
            Assert.True(u.PlanFacility(t, new Vector2(200, 300)) is var nr1 && nr1.IsValid && nr1.MissingRoad); // Frei setzen: gültig, ohne Zufahrt // Überland ohne Zufahrt
            var a = u.BuildFacility(u.PlanFacilityAligned(t, new Vector2(80, 30)));
            var b = u.BuildFacility(u.PlanFacilityAligned(t, new Vector2(300, 30)));
            Assert.NotNull(a);
            Assert.NotNull(b);

            // Standard-Bindung an das Warensystem: noch keine Bauernhöfe → nur Grundlast (Bioabfall/Gülle)
            u.ForceTick();
            Assert.Equal(t.Capacity * UtilitySystem.BiogasBaseShare, a.Output, 2);

            // 30 Beschäftigte in einem Hof neben a, ein Hof 5 km weg: a bekommt den nahen Hof, b (auch in Reichweite) den Rest (nichts)
            var farms = new List<(Vector2, float)> { (new Vector2(120, 200), 30f), (new Vector2(5000, 0), 100f) };
            u.FarmSources = () => farms;
            u.ForceTick();
            float expected = UtilitySystem.BiogasBaseShare + (1f - UtilitySystem.BiogasBaseShare) * 30f / UtilitySystem.BiogasFullFeedWorkers;
            Assert.Equal(expected, a.Availability, 4);
            Assert.Equal(30f, a.FeedWorkers);
            Assert.Equal(UtilitySystem.BiogasBaseShare, b.Availability, 4);
            farms.Add((new Vector2(320, 150), 60f)); // zweiter Hof: a holt die fehlenden 10 (nächste Höfe zuerst), b den Rest (50 ≥ 40)
            u.ForceTick();
            Assert.Equal(1f, b.Availability, 4);
            Assert.Equal(1f, a.Availability, 4);
            Assert.Equal(40f, a.FeedWorkers);
            Assert.True(u.IsRequired(UtilityKind.Power));
        }

        // ------------------------------------------------------------ Batteriespeicher

        static void Grow(SimulationWorld w, Vector2 center, float radius, ZoneType zone, int minBuildings)
        {
            w.City.Paint(center, radius, zone);
            for (int i = 0; i < 3000; i++)
            {
                w.Step();
                if (i % 50 == 0 && w.City.Buildings.Count(b => b.IsConstructed && Vector2.Distance(b.Position, center) < radius + 20f) >= minBuildings) break;
            }
            for (int i = 0; i < 60; i++) w.Step();
        }

        /// <summary>Weststraße (0,0)→(200,0) bebaut, Kohlekraftwerk → Hochspannung → Umspannwerk → Erdkabel (GridKit).</summary>
        static SimulationWorld GridTown(out Building[] houses, out UtilityFacility plant, out UtilityFacility sub)
        {
            var w = SimulationWorld.CreateDefault();
            w.Utilities.AbandonmentEnabled = false;
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            Grow(w, new Vector2(100, 20), 80f, ZoneType.ResidentialLow, 4);
            houses = w.City.Buildings.Where(b => b.IsConstructed && Vector2.Distance(b.Position, new Vector2(100, 20)) < 110f).OrderBy(b => b.Id).ToArray();
            Assert.True(houses.Length >= 3);
            (plant, sub, _) = GridKit.PowerChainWest(w);
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(0, 0), new Vector2(200, 0));
            w.Utilities.ForceTick();
            foreach (var h in houses) Assert.True(w.Utilities.IsSupplied(h, UtilityKind.Power));
            return w;
        }

        [Fact]
        public void Battery_HighVoltage_ChargesOnSurplus_BridgesOutage()
        {
            var w = GridTown(out var houses, out var plant, out var sub);
            var u = w.Utilities;
            var bat = u.TryBuildFacility(UtilityCatalog.BatteryStorage, new Vector2(-80, -120));
            Assert.NotNull(bat);
            GridKit.Lay(w, UtilityCatalog.HighVoltageLine, new Vector2(-64, -120), new Vector2(-32, -120)); // Speicher → Umspannwerk (Hochspannung)
            u.ForceTick();
            Assert.Equal(GridLevel.HighVoltage, u.NetworkOf(bat).Level);
            Assert.Equal(bat.Type.Capacity, bat.Charging, 1);                         // volle Ladeleistung aus dem Überschuss
            Assert.Equal(bat.Type.Capacity * u.StepHours * UtilitySystem.StorageEfficiency, bat.StoredKwh, 1);
            for (int i = 0; i < 6; i++) u.ForceTick();
            Assert.Equal(bat.Type.StorageKwh, bat.StoredKwh, 1);                      // voll
            Assert.Equal(0f, bat.Charging);

            // Kraftwerk fällt weg → der Speicher liefert (nur so viel wie fehlt)
            u.RemoveFacility(plant.Id);
            Assert.False(u.IsRequired(UtilityKind.Power)); // Speicher/Umspannwerk erzeugen nichts
            u.ForceTick();
            var local = u.NetworkOf(houses[0], UtilityKind.Power);
            output.WriteLine($"Ausfall: Speicher liefert {bat.Output:0.#} kW, Ortsnetz-Bedarf {local.Demand:0.#} kW, Rest {bat.StoredKwh:0} kWh");
            foreach (var h in houses) Assert.True(u.IsSupplied(h, UtilityKind.Power), $"{h} ohne Strom trotz Speicher");
            Assert.Equal(local.Delivered, bat.Output, 1);
            Assert.Equal(bat.Type.StorageKwh - bat.Output * u.StepHours / UtilitySystem.StorageEfficiency, bat.StoredKwh, 0);
            Assert.True(u.NetworkOf(bat).StorageDischarge > 0f);

            // leer → kein Strom mehr
            u.SetStoredEnergy(bat.Id, 0f);
            u.ForceTick();
            Assert.All(houses, h => Assert.False(u.IsSupplied(h, UtilityKind.Power)));
            Assert.Equal(0f, bat.Output);
        }

        [Fact]
        public void Battery_LocalGrid_ChargesThroughSubstation_AndSuppliesLocally()
        {
            var w = GridTown(out var houses, out var plant, out var sub);
            var u = w.Utilities;
            var bat = u.TryBuildFacility(UtilityCatalog.BatteryStorage, new Vector2(60, -90));
            Assert.NotNull(bat);
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(44, -90), new Vector2(-15, -82.5f)); // an das Erdkabel Umspannwerk → Straße
            u.ForceTick();
            var local = u.NetworkOf(bat);
            Assert.Equal(GridLevel.Distribution, local.Level);
            Assert.Equal(local, u.NetworkOf(houses[0], UtilityKind.Power));
            output.WriteLine($"Laden über Umspannwerk: {bat.Charging:0} kW, Umspannwerk-Last {sub.Load:0} kW, Gebäude {local.Delivered:0} kW");
            Assert.Equal(bat.Type.Capacity, bat.Charging, 1);
            Assert.True(sub.Load >= bat.Charging + local.Delivered - 1f);
            Assert.True(sub.Load <= sub.Type.Capacity + 1e-3f);

            u.RemoveFacility(plant.Id);
            u.ForceTick();
            foreach (var h in houses) Assert.True(u.IsSupplied(h, UtilityKind.Power));
            Assert.Equal(local.Delivered, bat.Output, 1);
            Assert.Equal(0f, sub.Load); // Hochspannung ohne Erzeugung
        }

        // ------------------------------------------------------------ Kasse, Katalog, Spielstand

        [Fact]
        public void NewPlants_CostAndUpkeep_ThroughCityBudget()
        {
            var w = WaterWorld(new WorldSettings { UnlimitedFunds = false, StartingFunds = 120_000 });
            double before = w.Economy.Balance;
            var park = w.Utilities.TryBuildFacility(UtilityCatalog.OffshoreWind, SeaCenter);
            Assert.NotNull(park);
            Assert.Equal(before - UtilityCatalog.OffshoreWind.Cost, w.Economy.Balance, 1);
            Assert.Equal(UtilityBuildError.InsufficientFunds, w.Utilities.PlanFacilityAligned(UtilityCatalog.HydroPlant, new Vector2(RiverX, 0)).Error);
            Assert.Contains(UtilityCatalog.BatteryStorage, UtilityCatalog.All);
            Assert.Same(UtilityCatalog.HydroPlant, UtilityCatalog.TypeById("power_hydro"));
            Assert.Same(UtilityCatalog.SubseaCable, UtilityCatalog.LineTypeById("power_hv_cable"));
            Assert.Equal(UtilityCatalog.OffshoreWind.UpkeepPerDay, w.Utilities.TotalUpkeepPerDay, 1);
        }

        [Fact]
        public void SaveLoad_StorageAndSites_Deterministic()
        {
            var a = WaterWorld();
            var u = a.Utilities;
            Road(a, new Vector2(-300, 0), new Vector2(200, 0));
            var dam = u.BuildFacility(u.PlanFacilityAligned(UtilityCatalog.HydroPlant, new Vector2(RiverX, 200)));
            var park = u.TryBuildFacility(UtilityCatalog.OffshoreWind, SeaCenter);
            var sub = u.TryBuildFacility(UtilityCatalog.Substation, new Vector2(-760, -250));
            var bat = u.TryBuildFacility(UtilityCatalog.BatteryStorage, new Vector2(-700, -250));
            var bio = u.BuildFacility(u.PlanFacilityAligned(UtilityCatalog.BiogasPlant, new Vector2(-100, 30)));
            Assert.NotNull(dam); Assert.NotNull(park); Assert.NotNull(sub); Assert.NotNull(bat); Assert.NotNull(bio);
            GridKit.Lay(a, UtilityCatalog.SubseaCable, SeaCenter + new Vector2(0, 88f), new Vector2(-760, -258));
            GridKit.Lay(a, UtilityCatalog.HighVoltageLine, new Vector2(-748, -250), new Vector2(-716, -250));
            for (int i = 0; i < SimulationClock.TicksPerDay / 2; i++) a.Step();
            Assert.True(bat.StoredKwh > 0f, "Speicher lädt aus dem Offshore-Überschuss");

            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            var bb = b.Utilities.GetFacility(bat.Id);
            Assert.Equal(bat.StoredKwh, bb.StoredKwh);
            Assert.Equal(dam.Rated, b.Utilities.GetFacility(dam.Id).Rated);
            Assert.Equal(dam.SiteHead, b.Utilities.GetFacility(dam.Id).SiteHead);
            Assert.Equal(a.Utilities.RiverFlowIndex, b.Utilities.RiverFlowIndex);
            Same(a, b, "direkt nach dem Laden");
            for (int i = 0; i < SimulationClock.TicksPerDay; i++) { a.Step(); b.Step(); }
            Same(a, b, "nach einem Tag");
            Assert.Equal(a.Utilities.GetFacility(bat.Id).StoredKwh, b.Utilities.GetFacility(bat.Id).StoredKwh);
        }

        static void Same(SimulationWorld a, SimulationWorld b, string when)
        {
            var ha = SaveGame.SectionHashes(a);
            var hb = SaveGame.SectionHashes(b);
            var diff = ha.Zip(hb, (x, y) => (x.Key, same: x.Value == y.Value)).Where(x => !x.same).Select(x => x.Key).ToList();
            Assert.True(diff.Count == 0, $"{when}: abweichende Abschnitte: {string.Join(", ", diff)}");
        }
    }
}
