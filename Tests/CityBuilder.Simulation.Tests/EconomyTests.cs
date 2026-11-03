using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    public class EconomyTests
    {
        readonly ITestOutputHelper output;
        public EconomyTests(ITestOutputHelper output) => this.output = output;

        /// <summary>Welt mit echter Kasse (nicht Sandbox).</summary>
        static SimulationWorld World(double funds = 100000.0) =>
            SimulationWorld.CreateDefault(new WorldSettings { UnlimitedFunds = false, StartingFunds = funds });

        static RoadPlan PlanRoad(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null) =>
            RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, t ?? RoadCatalog.CityTwoLane);

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null)
        {
            var plan = PlanRoad(w, a, b, t);
            Assert.True(plan.IsValid, plan.Error.ToString());
            Assert.NotEmpty(RoadBuilder.Build(w.Roads, plan));
        }

        static void Run(SimulationWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        // ------------------------------------------------------------ Steuern

        [Fact]
        public void Taxes_FlowFromOccupiedHomesAndJobs()
        {
            var w = World();
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.City.Paint(new Vector2(-150, 20), 120f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(150, -20), 120f, ZoneType.Industry);
            Run(w, SimulationClock.TicksPerDay * 7);

            var e = w.Economy;
            var week = e.LastWeek;
            Assert.NotNull(week);
            Assert.Equal(7, week.Days);
            output.WriteLine($"Woche 0: Einnahmen {week.Income:0} · Ausgaben {week.Expense:0} · Saldo {week.Net:0} · Kontostand {e.Balance:0}");
            Assert.True(week.Amount(BudgetCategory.ResidentialTax) > 0, "Grundsteuer erwartet");
            Assert.True(week.Amount(BudgetCategory.IndustryTax) > 0, "Gewerbesteuer Industrie erwartet");

            // Formel: bewohnte Haushalte × 40 € × Satz; Gewerbesteuer seit G5 aus dem Gewinn der Firma
            // (Tagesgewinn × Faktor × Satz, Verlust → 0) statt besetzte Jobs × Basis × Satz
            int households = w.City.Buildings.Where(b => b.IsConstructed).Sum(b => b.OccupiedHouseholds);
            double industryProfitTax = w.City.Buildings.Where(b => b.IsConstructed && b.Zone == ZoneType.Industry)
                .Sum(b => System.Math.Max(0, w.Goods.TaxableProfit(b)) * EconomyRules.ProfitTaxMultiplier * 0.10);
            var day = e.EstimateDay();
            Assert.Equal(households * EconomyRules.HouseholdTaxBase * 0.10, day.Amount(BudgetCategory.ResidentialTax), 2);
            Assert.Equal(industryProfitTax, day.Amount(BudgetCategory.IndustryTax), 2);

            // Höherer Satz → mehr Einnahmen bei gleicher Belegung
            e.SetTaxRate(TaxKind.Residential, 0.20f);
            Assert.Equal(households * EconomyRules.HouseholdTaxBase * 0.20, e.EstimateDay().Amount(BudgetCategory.ResidentialTax), 3);
        }

        [Fact]
        public void TaxRate_IsClampedAndRounded()
        {
            var e = World().Economy;
            Assert.Equal(EconomyRules.DefaultTaxRate, e.TaxRate(TaxKind.Office));
            e.SetTaxRate(TaxKind.Office, 0.9f);
            Assert.Equal(EconomyRules.MaxTaxRate, e.TaxRate(TaxKind.Office));
            e.SetTaxRate(TaxKind.Office, -1f);
            Assert.Equal(0f, e.TaxRate(TaxKind.Office));
            e.SetTaxRate(TaxKind.Office, 0.1234f);
            Assert.Equal(0.12f, e.TaxRate(TaxKind.Office), 5);
        }

        // ------------------------------------------------------------ Unterhalt

        [Fact]
        public void Upkeep_IsDeductedDaily()
        {
            var w = World();
            Road(w, new Vector2(-500, 0), new Vector2(500, 0));
            Assert.NotNull(w.Utilities.TryBuildFacility(UtilityCatalog.WaterTower, new Vector2(0, 30)));
            Assert.NotNull(w.Utilities.TryBuildLine(UtilityKind.Water, new Vector2(-100, 100), new Vector2(100, 100)));
            var e = w.Economy;
            double before = e.Balance;

            e.ForceBillDay();

            var seg = w.Roads.Segments.Single();
            double road = RoadBuilder.SegmentValue(seg) * EconomyRules.RoadUpkeepShare;
            double line = 200.0 * UtilityCatalog.LineCostPerMeter(UtilityKind.Water) * EconomyRules.LineUpkeepShare;
            Assert.Equal(road, e.LastDay.Amount(BudgetCategory.RoadUpkeep), 3);
            Assert.Equal(UtilityCatalog.WaterTower.UpkeepPerDay, e.LastDay.Amount(BudgetCategory.UtilityUpkeep), 3);
            Assert.Equal(line, e.LastDay.Amount(BudgetCategory.LineUpkeep), 3);
            Assert.Equal(before - road - UtilityCatalog.WaterTower.UpkeepPerDay - line, e.Balance, 3);
            // 1 km Stadtstraße: 12 000 € Neubauwert → 60 €/Tag
            Assert.Equal(60.0, road, 0);
        }

        [Fact]
        public void BridgesCostMoreUpkeep_ThanGroundRoads()
        {
            var w = World(1e7);
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            var plan = RoadBuilder.Plan(w.Roads, new Vector2(-300, 200), 0f, null, new Vector2(300, 200), 0f, RoadCatalog.CityTwoLane, 10f, 10f);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
            var ground = w.Roads.Segments.First(s => s.Start.Position.Y < 1f);
            var bridge = w.Roads.Segments.First(s => s.Start.Position.Y > 1f);
            Assert.True(RoadBuilder.SegmentValue(bridge) > RoadBuilder.SegmentValue(ground) * 1.3f);
        }

        [Fact]
        public void UpkeepSources_AreBookedAsServices_WithBreakdown()
        {
            var w = World();
            var e = w.Economy;
            var police = new FixedUpkeep("Polizei", 700);
            e.RegisterUpkeep(police);
            e.RegisterUpkeep(police); // doppelt zählt nicht
            e.RegisterUpkeep(new FixedUpkeep("Schule", 1400));
            for (int i = 0; i < EconomyRules.DaysPerWeek; i++) e.ForceBillDay();

            var week = e.LastWeek;
            Assert.Equal(2100.0, week.Amount(BudgetCategory.Services), 6);
            var parts = week.ServiceUpkeep.ToDictionary(kv => kv.Key, kv => kv.Value);
            Assert.Equal(700.0, parts["Polizei"], 6);
            Assert.Equal(1400.0, parts["Schule"], 6);

            Assert.True(e.UnregisterUpkeep(police));
            e.ForceBillDay();
            Assert.Equal(200.0, e.LastDay.Amount(BudgetCategory.Services), 6);
        }

        sealed class FixedUpkeep : IUpkeepSource
        {
            readonly double weekly;
            public FixedUpkeep(string name, double weekly) { Name = name; this.weekly = weekly; }
            public string Name { get; }
            public double WeeklyUpkeep() => weekly;
        }

        // ------------------------------------------------------------ Baukosten

        [Fact]
        public void ConstructionCosts_AreCharged()
        {
            var w = World(1e7);
            var e = w.Economy;

            // Straße: Planpreis
            double b0 = e.Balance;
            var plan = PlanRoad(w, new Vector2(-400, 0), new Vector2(400, 0));
            RoadBuilder.Build(w.Roads, plan);
            Assert.Equal(b0 - plan.Cost, e.Balance, 2);
            Assert.Equal(plan.Cost, e.CurrentWeek.Amount(BudgetCategory.RoadConstruction), 2);

            // Kreuzende Straße (zerteilt die erste): nur ihr eigener Preis
            b0 = e.Balance;
            var cross = PlanRoad(w, new Vector2(0, -200), new Vector2(0, 200));
            Assert.Single(cross.Crossings);
            RoadBuilder.Build(w.Roads, cross);
            Assert.Equal(b0 - cross.Cost, e.Balance, 2);

            // Kreisverkehr: Gesamtpreis genau einmal
            b0 = e.Balance;
            var ring = RoadBuilder.PlanRoundabout(w.Roads, new Vector2(300, 300), 30f, RoadCatalog.CityOneWay2);
            Assert.True(ring.IsValid, ring.Error.ToString());
            Assert.NotEmpty(RoadBuilder.BuildRoundabout(w.Roads, ring));
            Assert.Equal(b0 - ring.Cost, e.Balance, 2);

            // Autobahn-Vorlage: Gesamtpreis genau einmal
            b0 = e.Balance;
            var template = RoadTemplates.Plan(w.Roads, RoadTemplateKind.DiamondInterchange, new Vector2(0, -600), 0f);
            Assert.True(template.IsValid, template.Error.ToString());
            Assert.NotEmpty(RoadTemplates.Build(w.Roads, template));
            Assert.Equal(b0 - template.Cost, e.Balance, 1);

            // Ausbau: halber Neubaupreis des Zieltyps
            b0 = e.Balance;
            var seg = w.Roads.Segments.First(s => s.Type == RoadCatalog.CityTwoLane && !s.IsRoundabout);
            float upgrade = RoadBuilder.ReplaceCost(seg, RoadCatalog.CityFourLane);
            Assert.NotNull(RoadBuilder.Replace(w.Roads, seg, RoadCatalog.CityFourLane));
            Assert.Equal(b0 - upgrade, e.Balance, 2);

            // Versorgung: Bauwerk und Leitung
            b0 = e.Balance;
            Assert.NotNull(w.Utilities.TryBuildFacility(UtilityCatalog.WindTurbine, new Vector2(-200, 60)));
            Assert.Equal(b0 - UtilityCatalog.WindTurbine.Cost, e.Balance, 2);
            b0 = e.Balance;
            var linePlan = w.Utilities.PlanLine(UtilityKind.Water, new Vector2(-300, 150), new Vector2(-100, 150));
            Assert.NotNull(w.Utilities.BuildLine(linePlan));
            Assert.Equal(b0 - linePlan.Cost, e.Balance, 2);

            // Terraforming: Erdmenge × Preis
            b0 = e.Balance;
            var r = w.Terrain.Terraform(new TerraformBrush(TerraformMode.Raise, new Vector2(500, 500), 20f, 1f));
            Assert.True(r.Volume > 0f);
            Assert.Equal(r.Volume * EconomyRules.TerraformCostPerCubicMeter, r.Cost, 3);
            Assert.Equal(b0 - r.Cost, e.Balance, 2);
            Assert.Equal(r.Cost, e.CurrentWeek.Amount(BudgetCategory.Terraforming), 3);
        }

        [Fact]
        public void Demolition_RefundsPartOfTheCost()
        {
            var w = World(1e6);
            var e = w.Economy;
            Road(w, new Vector2(-200, 0), new Vector2(200, 0));
            var seg = w.Roads.Segments.Single();
            double value = RoadBuilder.SegmentValue(seg);
            double b0 = e.Balance;
            Assert.True(RoadBuilder.Demolish(w.Roads, seg));
            Assert.Equal(0, w.Roads.SegmentCount);
            Assert.Equal(b0 + value * EconomyRules.DemolitionRefundShare, e.Balance, 2);
            Assert.Equal(value * EconomyRules.DemolitionRefundShare, e.CurrentWeek.Amount(BudgetCategory.Demolition), 2);

            var f = w.Utilities.TryBuildFacility(UtilityCatalog.SolarPark, new Vector2(0, 100));
            b0 = e.Balance;
            Assert.True(w.Utilities.DemolishFacility(f.Id));
            Assert.Equal(b0 + UtilityCatalog.SolarPark.Cost * EconomyRules.DemolitionRefundShare, e.Balance, 2);

            var l = w.Utilities.TryBuildLine(UtilityKind.Water, new Vector2(0, 300), new Vector2(100, 300));
            b0 = e.Balance;
            Assert.True(w.Utilities.DemolishLine(l.Id));
            Assert.Equal(b0 + 100 * UtilityCatalog.LineCostPerMeter(UtilityKind.Water) * EconomyRules.DemolitionRefundShare, e.Balance, 2);

            // Internes Entfernen (z. B. Kreisverkehr räumt innen) bringt kein Geld
            Road(w, new Vector2(-50, -400), new Vector2(50, -400));
            b0 = e.Balance;
            w.Roads.RemoveSegment(w.Roads.Segments.First().Id);
            Assert.Equal(b0, e.Balance);
        }

        // ------------------------------------------------------------ Bauen ohne Geld

        [Fact]
        public void Build_WithoutMoney_IsInvalid()
        {
            // 1 000 € + 50 000 € Kreditrahmen
            var w = World(1000.0);
            var e = w.Economy;

            // 1 km Stadtstraße (12 000 €) geht – ins Minus, im Rahmen
            Road(w, new Vector2(-500, 0), new Vector2(500, 0));
            Assert.True(e.IsOverdrawn);
            Assert.Equal(1000.0 - 12000.0, e.Balance, 0);

            // Kohlekraftwerk 60 000 € übersteigt den Rahmen
            var coal = w.Utilities.PlanFacility(UtilityCatalog.CoalPlant, new Vector2(0, 100));
            Assert.Equal(UtilityBuildError.InsufficientFunds, coal.Error);
            Assert.Null(w.Utilities.BuildFacility(coal));

            // Autobahn 1 km (45 €/m) übersteigt den Rest
            var hw = PlanRoad(w, new Vector2(-500, 400), new Vector2(500, 400), RoadCatalog.Highway2);
            Assert.Equal(RoadBuildError.InsufficientFunds, hw.Error);
            Assert.Empty(RoadBuilder.Build(w.Roads, hw));

            // Geometriefehler haben Vorrang vor dem Geldfehler
            Assert.Equal(RoadBuildError.TooShort, PlanRoad(w, new Vector2(0, 500), new Vector2(1, 500), RoadCatalog.Highway2).Error);

            // Bis an den Rahmen bauen: −11 000 → −23 000 → −35 000 → −47 000
            var stale = PlanRoad(w, new Vector2(-500, 600), new Vector2(500, 600));
            Assert.True(stale.IsValid);
            foreach (float y in new[] { -200f, -400f, -600f }) Road(w, new Vector2(-500, y), new Vector2(500, y));
            Assert.Equal(-47000.0, e.Balance, 0);

            // Jetzt reicht es nicht mehr: Straße, Kreisverkehr, Ausbau, Leitung
            Assert.Equal(RoadBuildError.InsufficientFunds, PlanRoad(w, new Vector2(-500, 800), new Vector2(500, 800)).Error);
            Assert.Equal(RoadBuildError.InsufficientFunds, RoadBuilder.PlanRoundabout(w.Roads, new Vector2(0, -800), 60f, RoadCatalog.CityOneWay2).Error);
            var seg = w.Roads.Segments.First();
            Assert.Equal(RoadBuildError.InsufficientFunds, RoadBuilder.CheckReplace(w.Roads, seg, RoadCatalog.CityFourLane));
            Assert.Null(RoadBuilder.Replace(w.Roads, seg, RoadCatalog.CityFourLane));
            Assert.Equal(UtilityBuildError.InsufficientFunds, w.Utilities.PlanLine(UtilityKind.Water, new Vector2(-500, 300), new Vector2(500, 300)).Error);

            // Ein früher gültiger Plan baut auch nichts mehr
            double before = e.Balance;
            Assert.Empty(RoadBuilder.Build(w.Roads, stale));
            Assert.Equal(before, e.Balance);

            // Terraforming am Limit: nicht ausgeführt, nichts gebucht
            var r = w.Terrain.Terraform(new TerraformBrush(TerraformMode.Raise, new Vector2(800, 800), 80f, 3f));
            Assert.True(r.InsufficientFunds);
            Assert.False(r.Changed);
            Assert.Equal(before, e.Balance);
            Assert.True(e.Balance >= -e.CreditLimit);
        }

        [Fact]
        public void LongDebt_FreezesConstruction_UntilBalancePositive()
        {
            var w = World(0.0);
            var e = w.Economy;
            Road(w, new Vector2(-500, 0), new Vector2(500, 0)); // −12 000
            Assert.True(e.CanAfford(1000));

            for (int i = 0; i < EconomyRules.FreezeAfterDaysInDebt - 1; i++) e.ForceBillDay();
            Assert.False(e.ConstructionFrozen);
            Assert.Equal(1, e.DaysUntilFreeze);
            Assert.True(e.LastDay.Amount(BudgetCategory.Interest) > 0, "Überziehungszinsen erwartet");

            e.ForceBillDay();
            Assert.True(e.ConstructionFrozen);
            Assert.False(e.CanAfford(1));
            Assert.Equal(RoadBuildError.InsufficientFunds, PlanRoad(w, new Vector2(-50, 100), new Vector2(50, 100)).Error);
            Assert.Equal(0, e.AvailableFunds);

            // Wieder ins Plus (z. B. Abriss/Einnahmen) → Baustopp aufgehoben
            e.Refund(-e.Balance + 1000);
            Assert.True(e.CanAfford(1));
            e.ForceBillDay();
            Assert.False(e.ConstructionFrozen);
            Assert.Equal(0, e.DaysInDebt);
        }

        [Fact]
        public void UnlimitedFunds_BooksButNeverBlocks()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { StartingFunds = 0 }); // Standard: Sandbox
            Assert.True(w.Economy.UnlimitedFunds);
            Road(w, new Vector2(-1000, 0), new Vector2(1000, 0), RoadCatalog.Highway2);
            Assert.True(w.Economy.Balance < -50000);
            Assert.NotNull(w.Utilities.TryBuildFacility(UtilityCatalog.CoalPlant, new Vector2(0, 200)));
            for (int i = 0; i < 40; i++) w.Economy.ForceBillDay();
            Assert.False(w.Economy.ConstructionFrozen);
            Assert.Equal(0, w.Economy.LastWeek.Amount(BudgetCategory.Interest));
        }

        // ------------------------------------------------------------ Nachfrage

        [Fact]
        public void TaxRate_DampensDemand()
        {
            Assert.Equal(1f, EconomySystem.DemandFactorForRate(EconomyRules.DefaultTaxRate));
            Assert.Equal(0.6f, EconomySystem.DemandFactorForRate(0.20f), 4);
            Assert.Equal(1.2f, EconomySystem.DemandFactorForRate(0f), 4);
            Assert.True(EconomySystem.DemandFactorForRate(0.30f) >= EconomyRules.MinDemandFactor);

            SimulationWorld Make(float resTax, float indTax)
            {
                var w = World();
                w.Economy.SetTaxRate(TaxKind.Residential, resTax);
                w.Economy.SetTaxRate(TaxKind.Industry, indTax);
                Road(w, new Vector2(-300, 0), new Vector2(300, 0));
                w.City.Paint(new Vector2(-150, 20), 120f, ZoneType.ResidentialLow);
                w.City.Paint(new Vector2(150, -20), 120f, ZoneType.Industry);
                Run(w, 5); // eine Stadt-Runde, noch keine Bürger
                return w;
            }

            var normal = Make(0.10f, 0.10f);
            var high = Make(0.20f, 0.25f);
            float res = normal.City.Demand[ZoneType.ResidentialLow];
            float ind = normal.City.Demand[ZoneType.Industry];
            output.WriteLine($"Nachfrage Wohnen {res:0.0} → {high.City.Demand[ZoneType.ResidentialLow]:0.0}, Industrie {ind:0.0} → {high.City.Demand[ZoneType.Industry]:0.0}");
            Assert.True(res > 0 && ind > 0);
            Assert.Equal(res * 0.6f, high.City.Demand[ZoneType.ResidentialLow], 2);
            Assert.Equal(ind * EconomySystem.DemandFactorForRate(0.25f), high.City.Demand[ZoneType.Industry], 2);
            // Mischgebiet: Mittel aus Wohnen und Handel
            Assert.Equal((0.6f + 1f) * 0.5f, high.Economy.DemandFactor(ZoneType.Mixed), 4);
        }

        // ------------------------------------------------------------ Bilanz / Historie / Determinismus

        [Fact]
        public void WeeklyBalance_HistoryAndIdentity()
        {
            var w = World();
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.City.Paint(new Vector2(0, 20), 200f, ZoneType.ResidentialLow);
            Run(w, SimulationClock.TicksPerDay * 14);
            var e = w.Economy;
            Assert.Equal(2, e.History.Count);
            Assert.Equal(0, e.History[0].Index);
            Assert.Equal(1, e.History[1].Index);
            Assert.Equal(7, e.History[1].FirstDay);
            foreach (var week in e.History)
                Assert.Equal(week.ClosingBalance - week.OpeningBalance, week.Net, 3);
            Assert.Equal(e.History[1].ClosingBalance, e.History[0].ClosingBalance + e.History[1].Net, 3);
            Assert.Equal(e.LastWeek.Net, e.LastWeekNet);
            Assert.Equal(e.Balance, e.LastWeek.ClosingBalance, 6);
            Assert.Equal(0, e.CurrentWeek.Days);
        }

        [Fact]
        public void Economy_IsDeterministic()
        {
            SimulationWorld Play()
            {
                var w = World();
                Road(w, new Vector2(-300, 0), new Vector2(300, 0));
                Road(w, new Vector2(0, -200), new Vector2(0, 200));
                w.City.Paint(new Vector2(-150, 20), 120f, ZoneType.ResidentialLow);
                w.City.Paint(new Vector2(150, -20), 120f, ZoneType.Industry);
                w.City.Paint(new Vector2(150, 20), 60f, ZoneType.Retail);
                w.Utilities.TryBuildFacility(UtilityCatalog.WindTurbine, new Vector2(-250, -60));
                w.Economy.SetTaxRate(TaxKind.Commercial, 0.14f);
                Run(w, SimulationClock.TicksPerDay * 10);
                return w;
            }

            var a = Play().Economy;
            var b = Play().Economy;
            Assert.Equal(a.Balance, b.Balance);
            Assert.Equal(a.History.Count, b.History.Count);
            for (int i = 0; i < Budget.Count; i++)
            {
                var c = (BudgetCategory)i;
                Assert.Equal(a.LastWeek.Amount(c), b.LastWeek.Amount(c));
                Assert.Equal(a.CurrentWeek.Amount(c), b.CurrentWeek.Amount(c));
            }
            output.WriteLine($"Kontostand nach 10 Tagen: {a.Balance:0} € · letzte Woche {a.LastWeekNet:+0;-0} €");
        }
    }
}
