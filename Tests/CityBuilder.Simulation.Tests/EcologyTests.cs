using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Services.Content;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.World;
using CityBuilder.Simulation.Zoning;
using ResourceIds = CityBuilder.Simulation.World.ResourceIds;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Umwelt (Etappe 6): Luft (Quellen, Wind, Abbau durch Wald/Parks), Gegenmaßnahmen (Filter, Bäume, Umweltzone),
    /// Boden/Grundwasser (Ernte, Brunnen), Wirkungen (Gesundheit, Bodenwert, Schlagzeilen), Klima (Wind, Schnee, Dürre),
    /// Spielstand/Determinismus, Info Industrie- vs. grüne Stadt.
    /// </summary>
    public class EcologyTests
    {
        readonly ITestOutputHelper output;
        public EcologyTests(ITestOutputHelper output) => this.output = output;

        static void Road(SimulationWorld w, Vector2 a, Vector2 b)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        static void Run(SimulationWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        /// <summary>
        /// Stadt an einer Straße (0,0)→(1020,0) bis zum Kartenrand: Kraftwerk West (Kohle oder Windrad) mit Hochspannung/Umspannwerk,
        /// Erdkabel entlang der Straße, Mehrfamilienhäuser (gesetzt) nördlich, Industriezone südlich, Läden.
        /// </summary>
        static SimulationWorld Town(bool coal = true, int seed = 12345, bool enable = true, int days = 4, bool industry = true)
        {
            var w = SimulationWorld.CreateDefault(null, seed);
            w.Utilities.AbandonmentEnabled = false;
            Road(w, new Vector2(0, 0), new Vector2(1020, 0));
            GridKit.PowerChainWest(w, coal ? UtilityCatalog.CoalPlant : UtilityCatalog.WindTurbine);
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(0, 0), new Vector2(900, 0));
            w.City.BuildHouseRow(w.City.PlanHouseRow(HouseCatalog.Medium, new Vector2(60, 14), new Vector2(560, 14)));
            if (industry) w.City.Paint(new Vector2(200, -40), 120f, ZoneType.Industry);
            w.City.Paint(new Vector2(640, 30), 50f, ZoneType.Retail);
            w.Ecology.Enabled = enable;
            Run(w, SimulationClock.TicksPerDay * days);
            return w;
        }

        /// <summary>Leere flache Welt mit eingeschalteter Umwelt (für Physik-Tests mit eingespeistem Abgas).</summary>
        static SimulationWorld Empty(int seed = 12345, int terrainSeed = 4711)
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { TerrainSeed = terrainSeed }, seed);
            w.Ecology.Enabled = true;
            return w;
        }

        /// <summary>Speist jeden Tick Abgas an einer Stelle ein (wie ein Schornstein) und lässt die Welt laufen.</summary>
        static void RunWithStack(SimulationWorld w, Vector2 p, int ticks, float perTick = 0.02f)
        {
            for (int i = 0; i < ticks; i++)
            {
                w.Environment.AirPollution.AddRadial(p, 24f, perTick);
                w.Step();
            }
        }

        static float Sum(GridLayer l) => l.RawValues.Sum();

        // ------------------------------------------------------------ Grundsätzliches

        [Fact]
        public void Off_ByDefault_TrafficAndHeatingDoNotPollute()
        {
            Assert.False(SimulationWorld.CreateDefault().Ecology.Enabled);
            var off = Town(coal: false, enable: false, days: 3);
            Assert.True(off.Traffic.Stats.Spawned > 0);
            Assert.Equal(0f, off.Environment.AirPollution.MaxValue()); // wie früher: nur Kraftwerke/Verbrennung machen Abgas
            Assert.Equal(0f, off.Ecology.Soil.MaxValue());
            Assert.Equal(0.0, off.Ecology.WeeklyUpkeep());
            var on = Town(coal: false, enable: true, days: 3);
            var e = on.Ecology;
            output.WriteLine($"Ohne Kraftwerk, Umwelt an: max {on.Environment.AirPollution.MaxValue():0.000}, an Wohnungen Ø {e.HomeAirAverage:0.000}, " +
                             $"gestern Verkehr {e.EmissionYesterday(EmissionSource.Traffic):0.0}, Heizen {e.EmissionYesterday(EmissionSource.Heating):0.0}, " +
                             $"Industrie {e.EmissionYesterday(EmissionSource.Industry):0.0}");
            Assert.True(on.Environment.AirPollution.MaxValue() > 0.01f);
            Assert.True(e.EmissionYesterday(EmissionSource.Traffic) > 0f, "Verkehr stößt aus");
            Assert.True(e.EmissionYesterday(EmissionSource.Heating) > 0f, "Heizen im Januar");
            Assert.True(e.EmissionYesterday(EmissionSource.Industry) > 0f, "Industrie nach Rezept");
        }

        [Fact]
        public void Legacy_CoalPlantOnly_SameAsBefore()
        {
            // Alte Luftrechnung (Umwelt aus): Kraftwerk addiert jeden Durchlauf seinen Katalogwert, klingt mit 8 %/10 % ab
            var w = SimulationWorld.CreateDefault();
            var plant = w.Utilities.TryBuildFacility(UtilityCatalog.CoalPlant, new Vector2(300, 300));
            Run(w, 5);
            var reference = new GridLayer("ref", w.Environment.AirPollution.Origin, w.Environment.AirPollution.Width,
                w.Environment.AirPollution.Height, w.Environment.AirPollution.CellSize);
            reference.AddRadial(plant.Position, plant.Type.PollutionRadius, plant.Type.Pollution);
            reference.Diffuse(0.1f);
            reference.Decay(0.08f);
            Assert.Equal(reference.RawValues, w.Environment.AirPollution.RawValues);
            Assert.True(w.Utilities.pollutionActive);
        }

        // ------------------------------------------------------------ Wind, Wald, Inversion

        [Fact]
        public void Wind_ShiftsPollutionDownwind()
        {
            var w = Empty();
            var p = new Vector2(0, 0);
            int downwindDays = 0, windyDays = 0;
            for (int day = 0; day < 12; day++)
            {
                RunWithStack(w, p, SimulationClock.TicksPerDay);
                var wind = w.Climate.WindOn(w.Clock.Day - 1);
                if (wind.Strength < 0.3f) continue;
                windyDays++;
                // gemessen am Tagesende (Wind des abgelaufenen Tages hat die Fahne geformt)
                float down = w.Environment.AirPollution.Sample(p + wind.Toward * 160f);
                float up = w.Environment.AirPollution.Sample(p - wind.Toward * 160f);
                output.WriteLine($"Tag {day}: Wind aus {wind.FromName} {wind.MetersPerSecond:0.0} m/s – Lee {down:0.0000}, Luv {up:0.0000}");
                if (down > up * 1.5f) downwindDays++;
            }
            Assert.True(windyDays >= 4, $"zu wenige windige Tage: {windyDays}");
            Assert.True(downwindDays >= windyDays - 1, $"nur an {downwindDays} von {windyDays} windigen Tagen liegt das Abgas im Lee");
        }

        [Fact]
        public void Forest_BreaksDownPollution()
        {
            var bare = Empty();
            var forest = Empty();
            var wood = forest.Resources.Layer(ResourceIds.Wood);
            for (int i = 0; i < wood.RawValues.Length; i++) wood.RawValues[i] = 0.9f;
            wood.MarkChanged();
            var bareWood = bare.Resources.Layer(ResourceIds.Wood);
            for (int i = 0; i < bareWood.RawValues.Length; i++) bareWood.RawValues[i] = 0f;
            bareWood.MarkChanged();
            RunWithStack(bare, Vector2.Zero, SimulationClock.TicksPerDay * 2);
            RunWithStack(forest, Vector2.Zero, SimulationClock.TicksPerDay * 2);
            float a = Sum(bare.Environment.AirPollution), b = Sum(forest.Environment.AirPollution);
            output.WriteLine($"Abgas gesamt ohne Wald {a:0.00}, mit Wald {b:0.00} ({b / a:P0})");
            Assert.True(b < a * 0.7f, "Wald filtert deutlich");
        }

        [Fact]
        public void Park_BreaksDownPollution_Locally()
        {
            var bare = Empty();
            var park = Empty();
            Assert.NotNull(park.Services.Build(park.Services.Plan(ServiceCatalog.ParkLarge, new Vector2(0, 0))));
            RunWithStack(bare, Vector2.Zero, SimulationClock.TicksPerDay);
            RunWithStack(park, Vector2.Zero, SimulationClock.TicksPerDay);
            float a = bare.Environment.AirPollution.Sample(Vector2.Zero), b = park.Environment.AirPollution.Sample(Vector2.Zero);
            output.WriteLine($"Abgas an der Quelle ohne Park {a:0.000}, im Park {b:0.000}");
            Assert.True(b < a * 0.92f);
        }

        [Fact]
        public void Climate_WindSnowDroughtInversion()
        {
            var temperate = new ClimateModel(ClimateParams.Default, 99);
            float strength = 0f, east = 0f;
            int inversions = 0;
            for (int d = 0; d < 360; d++)
            {
                var wind = temperate.WindOn(d);
                Assert.InRange(wind.Strength, 0f, 1f);
                Assert.InRange(wind.Toward.Length(), 0.99f, 1.01f);
                strength += wind.Strength;
                east += wind.Toward.X;
                if (temperate.InversionOn(d) > 0.5f) inversions++;
            }
            output.WriteLine($"Gemäßigt: Wind Ø {strength / 360f:0.00}, Ostanteil Ø {east / 360f:0.00}, Inversionstage {inversions}");
            Assert.InRange(strength / 360f, 0.3f, 0.6f);
            Assert.True(east / 360f > 0.2f, "Westwindzone: Luft zieht im Mittel nach Osten");
            Assert.InRange(inversions, 1, 60);
            Assert.True(new ClimateModel(new ClimateParams { Latitude = 15f }, 99).WindOn(10).Toward.X < 0.9f); // Passat (aus Ost) möglich

            var cold = new ClimateModel(new ClimateParams { MeanTemperature = -2f, Amplitude = 14f, AnnualPrecipitation = 600f }, 7);
            float janSnow = Enumerable.Range(20, 20).Average(d => cold.SnowCoverOn(d));
            float julSnow = Enumerable.Range(190, 20).Average(d => cold.SnowCoverOn(d));
            output.WriteLine($"Kalt: Schnee Januar {janSnow:0.00}, Juli {julSnow:0.00}; gemäßigt Januar {Enumerable.Range(20, 20).Average(d => temperate.SnowCoverOn(d)):0.00}");
            Assert.True(janSnow > 0.4f);
            Assert.Equal(0f, julSnow);
            Assert.Equal(cold.SnowCoverOn(30), new ClimateModel(new ClimateParams { MeanTemperature = -2f, Amplitude = 14f, AnnualPrecipitation = 600f }, 7).SnowCoverOn(30));

            var arid = new ClimateModel(new ClimateParams { MeanTemperature = 24f, Amplitude = 8f, AnnualPrecipitation = 220f, Latitude = 28f }, 7);
            float dryArid = Enumerable.Range(150, 60).Average(d => arid.GrassDrynessOn(d));
            float dryTemp = Enumerable.Range(150, 60).Average(d => temperate.GrassDrynessOn(d));
            output.WriteLine($"Gras trocken Sommer: arid {dryArid:0.00}, gemäßigt {dryTemp:0.00}; Hitze arid {Enumerable.Range(150, 60).Max(d => arid.HeatStressOn(d)):0.00}");
            Assert.True(dryArid > dryTemp + 0.2f);
        }

        // ------------------------------------------------------------ Gegenmaßnahmen

        [Fact]
        public void Filter_LowersEmission_AndCostsFirmsAndCity()
        {
            SimulationWorld Make(int level)
            {
                var w = Town(coal: true, days: 4);
                foreach (var f in w.Goods.Firms.Where(f => f.Recipe.IsIndustry).OrderBy(f => f.BuildingId))
                    Assert.True(w.Goods.Restructure(f, RecipeCatalog.Get("steelworks")));
                w.Ecology.SetRegulation(level);
                Run(w, SimulationClock.TicksPerDay * 2);
                return w;
            }
            var none = Make(0);
            var strict = Make(2);
            var steelA = none.Goods.Firms.First(f => f.Recipe.Id == "steelworks" && f.Workers > 0);
            var steelB = strict.Goods.FirmAt(steelA.BuildingId);
            float ea = none.Ecology.EmissionYesterday(EmissionSource.Industry), eb = strict.Ecology.EmissionYesterday(EmissionSource.Industry);
            float pa = none.Ecology.EmissionYesterday(EmissionSource.Power), pb = strict.Ecology.EmissionYesterday(EmissionSource.Power);
            output.WriteLine($"Industrie gestern ohne Auflagen {ea:0.0}, streng {eb:0.0}; Kraftwerk {pa:0.0} → {pb:0.0}; " +
                             $"Stahlwerk Umweltkosten {steelB.Yesterday.Environment:0} €/Tag, Gewinn {steelA.Yesterday.Profit:0} → {steelB.Yesterday.Profit:0}; " +
                             $"Stadt Unterhalt {none.Ecology.WeeklyUpkeep():0} → {strict.Ecology.WeeklyUpkeep():0} €/Woche; Firmen gesamt {strict.Ecology.FirmCostPerDay():0} €/Tag");
            Assert.True(eb < ea * 0.6f, "Filter senken die Industrie-Emission");
            Assert.True(pb < pa * 0.6f, "Filter senken die Kraftwerks-Emission");
            Assert.Equal(0.0, steelA.Yesterday.Environment);
            Assert.True(steelB.Yesterday.Environment > 0, "Auflagen kosten die Firma");
            Assert.Equal(steelB.Jobs * EcologyRules.FirmCostPerJobDay(2) * EcologyRules.FirmWeight("steelworks"), strict.Ecology.FirmCost(steelB), 6);
            Assert.True(strict.Ecology.WeeklyUpkeep() > none.Ecology.WeeklyUpkeep(), "Filter an den städtischen Kraftwerken kosten Unterhalt");
            Assert.Equal(0.0, none.Ecology.WeeklyUpkeep());
            Assert.True(strict.Ecology.HomeAirAverage < none.Ecology.HomeAirAverage);
            // Die Auflagen erscheinen in der Gewinnrechnung (Gewinn = … − Umweltkosten) und damit in der Gewerbesteuer
            var l = steelB.Yesterday;
            Assert.Equal(l.Revenue + l.Subsidy - l.InputCost - l.Wages - l.Transport - l.Environment, l.Profit, 6);
        }

        [Fact]
        public void LowEmissionZone_TrucksEmitLess_FirmsInZonePay()
        {
            var w = Town(coal: false, days: 3);
            var e = w.Ecology;
            var center = e.CityCenter();
            e.SetLowEmissionZone(true, center, 600f);
            Assert.True(e.InZone(center));
            Assert.False(e.InZone(center + new Vector2(2000, 0)));
            var shop = w.Goods.Firms.FirstOrDefault(f => f.Role == FirmRole.Retail && e.InZone(w.City.GetBuilding(f.BuildingId).Position));
            Assert.NotNull(shop);
            Assert.Equal(shop.Jobs * EcologyRules.ZoneCostPerJobDay, e.FirmCost(shop), 6);
            Assert.True(e.WeeklyUpkeep() >= EcologyRules.ZoneUpkeepPerWeek);
            e.SetLowEmissionZone(false, center, 600f);
            Assert.Equal(0.0, e.FirmCost(shop));
        }

        [Fact]
        public void PlantTrees_CostsMoney_GrowsAndFilters()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { UnlimitedFunds = false, StartingFunds = 20000 });
            w.Ecology.Enabled = true;
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            var wood = w.Resources.Layer(ResourceIds.Wood);
            for (int i = 0; i < wood.RawValues.Length; i++) wood.RawValues[i] = 0f;
            wood.MarkChanged();

            var plan = w.Ecology.PlanTrees(new Vector2(0, 0), 60f);
            Assert.True(plan.IsValid, plan.Error.ToString());
            // Auf der Straße wird nicht gepflanzt
            Assert.DoesNotContain(plan.Cells, i => MathF.Abs(w.Environment.AirPollution.CellCenter(i % 128, i / 128).Y) < 6f);
            double before = w.Economy.Balance;
            Assert.True(w.Ecology.PlantTrees(plan));
            output.WriteLine($"{plan.Cells.Count} Felder, {plan.Trees:0} Bäume, {plan.Cost:0} €");
            Assert.Equal(before - plan.Cost, w.Economy.Balance, 3);
            Assert.Equal(plan.Cost, w.Economy.CurrentWeek.Amount(BudgetCategory.Environment), 3);
            Assert.Equal(plan.Cells.Count, w.Ecology.PlantedCells);
            var p = w.Environment.AirPollution.CellCenter(plan.Cells[0] % 128, plan.Cells[0] / 128);
            float d0 = w.Resources.DepositAt(ResourceIds.Wood, p);
            Run(w, SimulationClock.TicksPerDay * 30);
            float d30 = w.Resources.DepositAt(ResourceIds.Wood, p);
            Run(w, SimulationClock.TicksPerDay * 50);
            float d80 = w.Resources.DepositAt(ResourceIds.Wood, p);
            output.WriteLine($"Waldraster: Setzlinge {d0:0.00} → 30 Tage {d30:0.00} → 80 Tage {d80:0.00}; Pflege {w.Ecology.WeeklyUpkeep():0} €/Woche");
            Assert.InRange(d0, 0.04f, 0.06f);
            Assert.True(d30 > 0.3f && d80 >= 0.79f);
            Assert.True(w.Ecology.WeeklyUpkeep() > 0);
            // Schon bewaldet → nichts mehr frei; zu wenig Geld → Fehler
            Assert.Equal(TreePlantError.NothingFree, w.Ecology.PlanTrees(new Vector2(0, 30), 10f).Error);
            w.Ecology.Budget = new BrokeBudget();
            Assert.Equal(TreePlantError.InsufficientFunds, w.Ecology.PlanTrees(new Vector2(500, 500), 80f).Error);
            w.Ecology.Budget = w.Economy;
            // Eine neue Straße durch die Aufforstung beendet sie dort
            Road(w, new Vector2(0, -100), new Vector2(0, 100));
            Run(w, SimulationClock.TicksPerDay);
            Assert.True(w.Ecology.PlantedCells < plan.Cells.Count);
            Assert.True(w.Resources.DepositAt(ResourceIds.Wood, new Vector2(0, 30)) < 0.1f);
        }

        sealed class BrokeBudget : IConstructionBudget
        {
            public bool CanAfford(double cost) => false;
            public void Spend(BudgetCategory category, double amount) { }
            public void Refund(double amount) { }
        }

        // ------------------------------------------------------------ Boden & Grundwasser

        [Fact]
        public void SoilPollution_LowersFertility_AndHarvest()
        {
            SimulationWorld Make(bool polluted)
            {
                var w = Town(coal: false, days: 3);
                foreach (var f in w.Goods.Firms.Where(f => f.Recipe.IsIndustry).OrderBy(f => f.BuildingId))
                    Assert.True(w.Goods.Restructure(f, RecipeCatalog.Get("farm")));
                if (polluted)
                    for (int i = 0; i < w.Ecology.Soil.RawValues.Length; i++) w.Ecology.Soil.RawValues[i] = 0.6f;
                Run(w, SimulationClock.TicksPerDay * 2);
                return w;
            }
            var clean = Make(false);
            var dirty = Make(true);
            var farm = clean.Goods.Firms.First(f => f.Recipe.Id == "farm" && f.Workers > 0);
            var farm2 = dirty.Goods.FirmAt(farm.BuildingId);
            var pos = clean.City.GetBuilding(farm.BuildingId).Position;
            float qa = clean.Goods.DepositQualityAt(ResourceIds.Fertility, pos), qb = dirty.Goods.DepositQualityAt(ResourceIds.Fertility, pos);
            output.WriteLine($"Fruchtbarkeit sauber {qa:0.00}, belastet {qb:0.00} (Boden {dirty.Ecology.SoilAt(pos):0.00}); Produktivität {farm.Productivity:0.00} → {farm2.Productivity:0.00}; " +
                             $"Ernte gestern {farm.Yesterday.Produced:0} → {farm2.Yesterday.Produced:0}");
            Assert.True(qb < qa * 0.7f);
            Assert.True(farm2.Productivity < farm.Productivity);
            Assert.True(farm2.Yesterday.Produced < farm.Yesterday.Produced);
            // Umwelt aus → Fruchtbarkeit unverändert (Hook liefert den alten Wert)
            dirty.Ecology.Enabled = false;
            Assert.Equal(dirty.Resources.DepositAt(ResourceIds.Fertility, pos), dirty.Goods.DepositAt(ResourceIds.Fertility, pos));
        }

        [Fact]
        public void HeatAndDrought_LowerHarvest()
        {
            var w = Town(coal: false, days: 1, industry: false);
            var c = w.Climate;
            float worst = 1f;
            int worstDay = 0;
            for (int d = 0; d < 360; d++)
            {
                float s = w.Ecology.HarvestStress(d);
                Assert.InRange(s, 0f, 1f);
                if (s < worst) { worst = s; worstDay = d; }
            }
            output.WriteLine($"Gemäßigt: schlechtester Erntetag {worstDay} – Faktor {worst:0.00} (Dürre {c.DroughtOn(worstDay):0.00}, Hitze {c.HeatStressOn(worstDay):0.00})");
            Assert.True(worst < 0.95f, "auch im gemäßigten Klima gibt es trockene Wochen");
            var hot = new ClimateModel(new ClimateParams { MeanTemperature = 26f, Amplitude = 9f, AnnualPrecipitation = 300f, Latitude = 30f }, 3);
            float hotMin = Enumerable.Range(150, 60).Min(d => (1f - EcologyRules.HeatHarvestLoss * hot.HeatStressOn(d)) * (1f - EcologyRules.DroughtHarvestLoss * hot.DroughtOn(d)));
            output.WriteLine($"Heiß/trocken: Sommer-Minimum {hotMin:0.00}");
            Assert.True(hotMin < 0.7f);
            // Umwelt an: die Ernte der Höfe (GoodsSystem.HarvestFactor) trägt den Stress
            int day = w.Clock.Day;
            Assert.Equal(BaseHarvest(w, day) * w.Ecology.HarvestStress(day), w.Goods.HarvestFactor(day), 4);
            w.Ecology.Enabled = false;
            Assert.Equal(BaseHarvest(w, day), w.Goods.HarvestFactor(day), 5);
        }

        static float BaseHarvest(SimulationWorld w, int day)
        {
            float annual = MathF.Max(0.05f, w.Climate.AnnualHarvestFactor);
            float scale = Math.Clamp(annual / GoodsRules.HarvestReference, 0.4f, 1.2f);
            return (GoodsRules.HarvestFloor + (1f - GoodsRules.HarvestFloor) * w.Climate.HarvestFactor(day) / annual) * scale;
        }

        [Fact]
        public void Groundwater_FromLandfill_LowersWellQuality()
        {
            var w = SimulationWorld.CreateDefault();
            w.Ecology.Enabled = true;
            w.Utilities.AbandonmentEnabled = false;
            Road(w, new Vector2(0, 0), new Vector2(600, 0));
            GridKit.PowerChainWest(w);
            GridKit.Lay(w, UtilityCatalog.UndergroundCable, new Vector2(0, 0), new Vector2(600, 0));
            var well = w.Utilities.TryBuildFacility(UtilityCatalog.WaterTower, new Vector2(614, 0), 270f);
            Assert.NotNull(well);
            GridKit.Lay(w, UtilityCatalog.WaterPipe, new Vector2(0, 0), new Vector2(606, 0));
            var row = w.City.BuildHouseRow(w.City.PlanHouseRow(HouseCatalog.Low, new Vector2(100, 14), new Vector2(420, 14)));
            Assert.True(row.Count > 5);
            Run(w, SimulationClock.TicksPerDay);
            Assert.Equal(1f, w.WaterQuality.PumpQuality(well));
            var landfill = w.Services.Build(w.Services.PlanAligned(ServiceCatalog.Landfill, new Vector2(560, -40)));
            Assert.NotNull(landfill);
            Run(w, SimulationClock.TicksPerDay * 40);
            float gw = w.Ecology.GroundwaterAt(well.Position), q = w.WaterQuality.PumpQuality(well);
            var house = w.City.Buildings.First(b => b.IsPlayerPlaced && b.Residents > 0);
            output.WriteLine($"Nach 40 Tagen Deponie: Grundwasser am Brunnen {gw:0.000}, Brunnen {q:0.000}, Trinkwasser {w.WaterQuality.DrinkingWaterQuality(house):0.000}, " +
                             $"Boden an der Deponie {w.Ecology.SoilAt(landfill.Position):0.000}");
            Assert.True(gw > 0.01f);
            Assert.True(q < 0.99f);
            Assert.True(w.WaterQuality.DrinkingWaterQuality(house) < 0.99f);
            // Direkt gesetzt: stark belastetes Grundwasser → schlechtes Trinkwasser
            for (int i = 0; i < w.Ecology.Groundwater.RawValues.Length; i++) w.Ecology.Groundwater.RawValues[i] = 0.5f;
            w.WaterQuality.ForceTick();
            Assert.Equal(0.5f, w.WaterQuality.PumpQuality(well), 3);
            w.Ecology.Enabled = false;
            w.WaterQuality.ForceTick();
            Assert.Equal(1f, w.WaterQuality.PumpQuality(well));
        }

        // ------------------------------------------------------------ Wirkungen

        [Fact]
        public void Smog_LowersHealth_OldAndYoungMore()
        {
            SimulationWorld Make(bool smog)
            {
                var w = Town(coal: false, days: 3, industry: false);
                var center = new Vector2(310, 14);
                for (int i = 0; i < SimulationClock.TicksPerDay * 14; i++)
                {
                    if (smog) w.Environment.AirPollution.AddRadial(center, 400f, 0.012f);
                    w.Step();
                }
                return w;
            }
            var clean = Make(false);
            var smoggy = Make(true);
            var ids = clean.Citizens.Citizens.Where(c => !c.IsCommuterIn).Select(c => c.Id).ToHashSet();
            var a = clean.Citizens.Citizens.Where(c => ids.Contains(c.Id)).ToDictionary(c => c.Id);
            var pairs = smoggy.Citizens.Citizens.Where(c => a.ContainsKey(c.Id)).Select(c => (c, before: a[c.Id])).ToList();
            float Drop(Func<Citizen, bool> who)
            {
                var list = pairs.Where(p => who(p.c)).ToList();
                return list.Count == 0 ? float.NaN : list.Average(p => p.before.Health - p.c.Health);
            }
            float all = Drop(c => true), adults = Drop(c => c.Age >= 18 && c.Age < 60), old = Drop(c => c.Age >= 60), young = Drop(c => c.Age < 15);
            output.WriteLine($"Smog: Luft Wohnungen Ø {smoggy.Ecology.HomeAirAverage:0.00}, Exposition max {smoggy.Ecology.Exposure.MaxValue():0.00}, Alarm {smoggy.Ecology.SmogAlert}; " +
                             $"Gesundheit −{all:0.000} (Erwachsene −{adults:0.000}, Ältere −{old:0.000}, Kinder −{young:0.000}, Paare {pairs.Count})");
            Assert.True(all > 0.05f, "Smog senkt die Gesundheit");
            if (!float.IsNaN(old)) Assert.True(old > adults, "Ältere leiden stärker");
            if (!float.IsNaN(young)) Assert.True(young > adults * 0.9f, "Kinder leiden mindestens ähnlich");
            Assert.True(smoggy.Ecology.SmogAlert);
            Assert.True(smoggy.Citizens.Citizens.Count(c => c.Memories.Strength(MemoryKind.Smog) > 0f) > clean.Citizens.Citizens.Count(c => c.Memories.Strength(MemoryKind.Smog) > 0f));
            Assert.True(smoggy.Citizens.Stats.AverageSatisfaction < clean.Citizens.Stats.AverageSatisfaction);
        }

        [Fact]
        public void SoilPollution_LowersLandValue()
        {
            var w = Town(coal: false, days: 2, industry: false);
            var p = new Vector2(300, 14);
            Assert.True(w.LandValue.Explain(p).Contamination > -0.005f); // nur etwas Ablagerung aus der Luft
            for (int i = 0; i < w.Ecology.Soil.RawValues.Length; i++) w.Ecology.Soil.RawValues[i] = 0.5f;
            var f = w.LandValue.Explain(p);
            output.WriteLine($"Bodenwert-Abzug Altlasten: {f.Contamination:0.000}");
            Assert.Equal(-EcologyRules.ContaminationPenalty * 0.5f, f.Contamination, 4);
            Assert.Contains(f.Contributions(), c => c.label.StartsWith("Altlasten"));
        }

        [Fact]
        public void Headlines_SmogAlarm_FishKill()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { GenerateTerrain = true });
            w.Ecology.Enabled = true;
            var river = Enumerable.Range(-60, 120).Select(i => new Vector2(i * 8f, 0)).FirstOrDefault(q => w.Terrain.IsWater(q));
            Assert.True(w.Terrain.IsWater(river), "Fluss erwartet");
            var town = Town(coal: false, days: 2, industry: false);
            Assert.NotNull(town.Services.Build(town.Services.PlanAligned(ServiceCatalog.Newspaper, new Vector2(300, -30))));
            Run(town, 20);
            for (int i = 0; i < SimulationClock.TicksPerDay; i++)
            {
                town.Environment.AirPollution.AddRadial(new Vector2(310, 14), 400f, 0.08f);
                town.Step();
            }
            output.WriteLine($"Smog-Episoden {town.Ecology.SmogEpisodes}, Schlagzeilen: {string.Join(" | ", town.Media.Recent.Select(h => h.Title))}");
            Assert.True(town.Ecology.SmogEpisodes >= 1);
            Assert.Contains(town.Media.Recent, h => h.Topic == HeadlineTopic.Smog);
            // Fischsterben: stark verschmutzter Fluss
            w.WaterQuality.Pollution.AddRadial(river, 30f, 1f);
            Run(w, SimulationClock.TicksPerDay);
            Assert.Equal(1, w.Ecology.FishKills);
        }

        // ------------------------------------------------------------ Spielstand

        [Fact]
        public void SaveLoad_MidPass_StaysBitIdentical()
        {
            var a = Town(coal: true, days: 3);
            a.Ecology.SetRegulation(1);
            a.Ecology.SetLowEmissionZone(true, a.Ecology.CityCenter(), 500f);
            Assert.True(a.Ecology.PlantTrees(a.Ecology.PlanTrees(new Vector2(300, 90), 50f)));
            Run(a, SimulationClock.TicksPerDay + 3); // mitten im Luft-Durchlauf (Verkehr gezählt, noch nicht ausgestoßen)
            for (int i = 0; i < 41; i++) a.Substep();
            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            Assert.True(b.Ecology.Enabled);
            Assert.Equal(1, b.Ecology.Regulation);
            for (int i = 0; i < SimulationClock.TicksPerDay + 17; i++) { a.Step(); b.Step(); }
            var ha = SaveGame.SectionHashes(a);
            var hb = SaveGame.SectionHashes(b);
            var diff = ha.Zip(hb, (x, y) => (x.Key, same: x.Value == y.Value)).Where(x => !x.same).Select(x => x.Key).ToList();
            Assert.True(diff.Count == 0, "abweichend: " + string.Join(", ", diff));
            Assert.Equal(a.Environment.AirPollution.RawValues, b.Environment.AirPollution.RawValues);
            Assert.Equal(a.Ecology.Soil.RawValues, b.Ecology.Soil.RawValues);
            Assert.Equal(a.Ecology.HomeAirAverage, b.Ecology.HomeAirAverage);
        }

        [Fact]
        public void Migration_WithoutSection_IsOffAndClean()
        {
            var a = Town(coal: true, days: 2);
            var c = SaveGame.Capture(a);
            Assert.True(c.Remove("ecology"));
            var b = SaveGame.Load(c.ToBytes());
            Assert.False(b.Ecology.Enabled);
            Assert.Equal(0f, b.Ecology.Soil.MaxValue());
            Assert.Equal(0, b.Ecology.Regulation);
            Run(b, 20); // alte Luftrechnung läuft weiter
            Assert.True(b.Environment.AirPollution.MaxValue() > 0f);
        }

        [Fact]
        public void Deterministic_SameSeedSameAir()
        {
            string Run2()
            {
                var w = Town(coal: true, days: 3, seed: 4242);
                return $"{Sum(w.Environment.AirPollution):R}|{Sum(w.Ecology.Soil):R}|{Sum(w.Ecology.Exposure):R}|{w.Ecology.HomeAirAverage:R}";
            }
            Assert.Equal(Run2(), Run2());
        }

        // ------------------------------------------------------------ Info: Industriestadt vs. grüne Stadt

        [Fact]
        public void Info_IndustrialVsGreenCity()
        {
            var ind = Town(coal: true, days: 14);
            var green = Town(coal: false, days: 0, industry: false);
            green.Ecology.SetRegulation(2);
            green.Ecology.PlantTrees(green.Ecology.PlanTrees(new Vector2(300, 80), 60f));
            Run(green, SimulationClock.TicksPerDay * 14);
            foreach (var (name, w) in new[] { ("Industrie", ind), ("Grün", green) })
            {
                var e = w.Ecology;
                var res = w.Citizens.Citizens.Where(c => !c.IsCommuterIn).ToList();
                var homes = w.City.Buildings.Where(b => b.Residents > 0).ToList();
                output.WriteLine($"{name}: Einwohner {res.Count}, Luft an Wohnungen Ø {e.HomeAirAverage:0.000} max {e.HomeAirMax:0.000}, Smog {e.SmogBuildings}/{e.HomeBuildings} " +
                                 $"(Episoden {e.SmogEpisodes}), Quellen gestern V {e.ShareYesterday(EmissionSource.Traffic):P0} K {e.ShareYesterday(EmissionSource.Power):P0} " +
                                 $"I {e.ShareYesterday(EmissionSource.Industry):P0} H {e.ShareYesterday(EmissionSource.Heating):P0} (gesamt {e.TotalEmissionYesterday:0}), " +
                                 $"Boden max {e.Soil.MaxValue():0.000}, Grundwasser max {e.Groundwater.MaxValue():0.000}, Gesundheit Ø {res.Average(c => c.Health):0.000}, " +
                                 $"Zufriedenheit {w.Citizens.Stats.AverageSatisfaction:0.000}, Bodenwert Wohnungen Ø {homes.Average(b => w.LandValue.ValueOf(b)):0.000}, " +
                                 $"Tourismus {e.TourismFactor:0.00}, Firmen-Umweltkosten {e.FirmCostPerDay():0} €/Tag, Unterhalt {e.WeeklyUpkeep():0} €/Woche");
            }
            Assert.True(ind.Ecology.HomeAirAverage > green.Ecology.HomeAirAverage * 1.5f);
            Assert.True(ind.Citizens.Citizens.Where(c => !c.IsCommuterIn).Average(c => c.Health) <
                        green.Citizens.Citizens.Where(c => !c.IsCommuterIn).Average(c => c.Health));
            Assert.True(ind.Ecology.Soil.MaxValue() > green.Ecology.Soil.MaxValue());
            // Rechenzeit (danach, verändert den Zustand): ein voller Luft-Durchlauf (alle Quellen, Wind, Abbau, Statistik) + Tagesteil
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 50; i++) ind.Ecology.Tick(ind); // Uhr steht auf einer Tagesgrenze → Durchlauf + Tagesteil
            sw.Stop();
            output.WriteLine($"Rechenzeit Umwelt: {sw.Elapsed.TotalMilliseconds / 50:0.00} ms je Durchlauf inkl. Tagesteil (128×128 Zellen)");
        }
    }
}
