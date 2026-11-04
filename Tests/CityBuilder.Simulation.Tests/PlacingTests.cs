using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.LandValue;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>
    /// Setzen statt Zonen: Reihen ziehen (gerade/Kurve/Knick), eine Buchung je Reihe, Läden/Büros/Hallen mit Betrieb aus der
    /// Standortwahl, Aufstieg nach Anforderungen, kein Wachstum ohne Zonen, alte Stände mit Zonen laufen weiter, Nachfrage-Anzeige.
    /// </summary>
    public class PlacingTests
    {
        readonly ITestOutputHelper output;
        public PlacingTests(ITestOutputHelper output) => this.output = output;

        static List<RoadSegment> Road(SimulationWorld w, Vector2 a, Vector2 b, Vector2? control = null, RoadType t = null)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, control, b, 8f, t ?? RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            return RoadBuilder.Build(w.Roads, plan);
        }

        static SimulationWorld Street(WorldSettings settings = null)
        {
            var w = SimulationWorld.CreateDefault(settings);
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            return w;
        }

        static void Run(SimulationWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        static void AssertNoOverlaps(IReadOnlyList<HousePlan> items)
        {
            for (int i = 0; i < items.Count; i++)
            for (int j = i + 1; j < items.Count; j++)
            {
                var a = items[i];
                var b = items[j];
                Assert.False(ServiceGeometry.RectsOverlap(a.Position, a.Front, a.Type.Width, a.Type.Depth, b.Position, b.Front, b.Type.Width, b.Type.Depth),
                    $"Platz {i} und {j} überlappen");
            }
        }

        sealed class CountingBudget : IConstructionBudget
        {
            public int Spends;
            public double Spent;
            public double Limit = double.MaxValue;
            public bool CanAfford(double cost) => Spent + cost <= Limit;
            public void Spend(BudgetCategory category, double amount) { Spends++; Spent += amount; }
            public void Refund(double amount) { }
        }

        // ------------------------------------------------------------ Reihe planen

        [Fact]
        public void Row_Straight_GaplessFrontToRoad_UpToTheMouse()
        {
            var w = Street();
            var t = HouseCatalog.Low;
            var row = w.City.PlanHouseRow(t, new Vector2(0, 15), new Vector2(100, 15));
            output.WriteLine(string.Join(" | ", row.Items.Select(i => $"{i.Position.X:0.00}/{i.Position.Y:0.00} {i.Error}")));
            Assert.True(row.Aligned);
            Assert.Equal(7, row.Items.Count); // Mitten 0, 16, … 96 (die Maus bei 100 liegt im 7. Haus)
            Assert.All(row.Items, i => Assert.True(i.IsValid, i.Error.ToString()));
            for (int i = 1; i < row.Items.Count; i++)
                Assert.Equal(t.Width + CitySystem.RowGap, row.Items[i].Position.X - row.Items[i - 1].Position.X, 2);
            Assert.All(row.Items, i => Assert.True(Vector2.Dot(i.Front, -Vector2.UnitY) > 0.999f));
            Assert.All(row.Items, i => Assert.Equal(RoadCatalog.CityTwoLane.HalfWidth + RoadFront.SnapGap + t.Depth * 0.5f, i.Position.Y, 2));
            Assert.Equal(7 * t.Cost, row.TotalCost, 3);
            AssertNoOverlaps(row.Items);

            // Ziehen nach links, andere Straßenseite
            var left = w.City.PlanHouseRow(t, new Vector2(0, -15), new Vector2(-60, -15));
            Assert.Equal(4, left.ValidCount);
            Assert.True(left.Items.Last().Position.X < -40f);
            Assert.All(left.Items, i => Assert.True(Vector2.Dot(i.Front, Vector2.UnitY) > 0.999f));

            // Einzelklick = genau ein Gebäude wie PlanHouse
            var single = w.City.PlanHouseRow(t, new Vector2(20, 15), new Vector2(20, 15));
            Assert.Single(single.Items);
            var plan = w.City.PlanHouse(t, new Vector2(20, 15));
            Assert.Equal(plan.Position.X, single.Items[0].Position.X, 3);
            Assert.Equal(plan.Position.Y, single.Items[0].Position.Y, 3);

            // ohne Straße: ein Platz (frei setzen – gültig, wartet aber auf eine Straße)
            var none = w.City.PlanHouseRow(t, new Vector2(0, 200), new Vector2(80, 200));
            Assert.False(none.Aligned);
            Assert.Single(none.Items);
            Assert.True(none.Items[0].IsValid, none.Items[0].Error.ToString());
            Assert.False(none.Items[0].HasRoadAccess);
        }

        [Fact]
        public void Row_FollowsCurve_AndBend_WithoutOverlaps()
        {
            var w = SimulationWorld.CreateDefault();
            // Bogen ~90°, R ≈ 150 m, danach Knick (Grad-2-Knoten) in eine Gerade
            var curve = Road(w, new Vector2(0, 0), new Vector2(150, 150), new Vector2(150, 0));
            Road(w, new Vector2(150, 150), new Vector2(150, 400));
            var seg = curve[curve.Count - 1];
            var t = HouseCatalog.Medium;
            foreach (int side in new[] { 1, -1 })
            {
                var start = seg.Evaluate(0.05f) + seg.Right(0.05f) * side * 12f;
                var end = new Vector2(150, 330) + new Vector2(side > 0 ? 12f : -12f, 0f) * (seg.Right(0.95f).X > 0 ? 1 : -1);
                var row = w.City.PlanHouseRow(t, start, end);
                output.WriteLine($"Seite {side}: {row.Items.Count} Plätze, {row.ValidCount} gültig, Segmente {string.Join(",", row.SegmentIds)} · " +
                                 string.Join(" ", row.Items.Select(i => i.Error == HouseBuildError.None ? "✓" : i.Error.ToString())));
                Assert.True(row.SegmentIds.Count >= 2, "Reihe folgt über den Knick in die Gerade");
                Assert.True(row.ValidCount >= 8, $"gültig {row.ValidCount}");
                AssertNoOverlaps(row.Items);
                // jede Vorderseite zeigt zur Straße (Zufahrt gefunden)
                Assert.All(row.ValidItems, i => Assert.True(i.AccessSegmentId >= 0));
                // dichte Reihe: Abstand benachbarter Mitten höchstens ~ Breite + Kurvenausgleich
                for (int i = 1; i < row.Items.Count; i++)
                    Assert.True(Vector2.Distance(row.Items[i].Position, row.Items[i - 1].Position) < t.Width * 1.25f);
            }
        }

        [Fact]
        public void Row_InvalidPlacesSkipped_BuildChargesOnce()
        {
            var w = Street();
            var budget = new CountingBudget();
            w.City.Budget = budget;
            var police = w.Services.Build(w.Services.PlanAligned(ServiceCatalog.PoliceStation, new Vector2(50, 25)));
            Assert.NotNull(police);
            var t = HouseCatalog.Low;
            var row = w.City.PlanHouseRow(t, new Vector2(-40, 15), new Vector2(140, 15));
            int invalid = row.Items.Count(i => !i.IsValid);
            output.WriteLine($"{row.Items.Count} Plätze, {invalid} ungültig: {string.Join(",", row.Items.Where(i => !i.IsValid).Select(i => i.Error))}");
            Assert.True(invalid >= 1);
            Assert.Contains(row.Items, i => i.Error == HouseBuildError.OverlapsStructure);
            Assert.Equal(row.ValidCount * t.Cost, row.TotalCost, 3);

            var built = w.City.BuildHouseRow(row);
            Assert.Equal(row.ValidCount, built.Count);
            Assert.Equal(1, budget.Spends);                 // eine Buchung
            Assert.Equal(row.TotalCost, budget.Spent, 3);
            Assert.All(built, b => Assert.True(b.IsPlayerPlaced));
            Assert.Equal(built.Count, w.City.PlayerHouseCount);
        }

        [Fact]
        public void Row_Budget_IsCumulative_AndCityKassaBooksOnce()
        {
            var w = Street(new WorldSettings { UnlimitedFunds = false, StartingFunds = 10_000 });
            double before = w.Economy.Balance;
            var t = HouseCatalog.Medium; // 16 000 €, Rahmen 10 000 + 50 000 Kredit → 3 passen
            var row = w.City.PlanHouseRow(t, new Vector2(-200, 20), new Vector2(0, 20));
            output.WriteLine(string.Join(",", row.Items.Select(i => i.Error)));
            Assert.Equal(3, row.ValidCount);
            Assert.All(row.Items.Skip(3), i => Assert.Equal(HouseBuildError.InsufficientFunds, i.Error));
            var built = w.City.BuildHouseRow(row);
            Assert.Equal(3, built.Count);
            Assert.Equal(before - 3 * t.Cost, w.Economy.Balance, 3);
            Assert.Equal(3 * t.Cost, w.Economy.CurrentWeek.Amount(BudgetCategory.HouseConstruction), 3);
        }

        // ------------------------------------------------------------ Gewerbe & Industrie

        [Fact]
        public void PlacedShopOfficeHall_GetJobsAndFirms_ChosenBySimulation()
        {
            var w = Street();
            Building Put(HouseType t, float x)
            {
                var plan = w.City.PlanHouse(t, new Vector2(x, 20));
                Assert.True(plan.IsValid, $"{t.DisplayName}: {plan.Error}");
                return w.City.BuildHouse(plan);
            }
            var shop = Put(HouseCatalog.Shop, -200);
            var mixed = Put(HouseCatalog.MixedUse, -150);
            var office = Put(HouseCatalog.OfficeBuilding, -100);
            var small = Put(HouseCatalog.SmallHall, 0);
            var large = Put(HouseCatalog.LargeHall, 100);
            Assert.Equal(HouseCatalog.Shop.JobsAtLevel(1), shop.Jobs);
            Assert.True(shop.Jobs > 0 && shop.Households == 0);
            Assert.True(mixed.Jobs > 0 && mixed.Households > 0);
            Assert.True(large.Jobs > small.Jobs);
            Assert.Same(HouseCatalog.LargeHall, HouseCatalog.Of(large));
            Assert.Same(HouseCatalog.SmallHall, HouseCatalog.Of(small));
            w.Goods.ForceStep();
            Assert.Equal(FirmRole.Retail, w.Goods.FirmAt(shop.Id).Role);
            Assert.Equal(FirmRole.Retail, w.Goods.FirmAt(mixed.Id).Role);
            Assert.Equal(FirmRole.Office, w.Goods.FirmAt(office.Id).Role);
            Assert.True(w.Goods.FirmAt(small.Id).Recipe.IsIndustry);
            Assert.True(w.Goods.FirmAt(large.Id).Recipe.IsIndustry);
        }

        static float OilEverywhere(string id, Vector2 p) => id == ResourceIds.Oil ? 0.6f : GoodsSystem.DefaultDeposit(id, p);

        [Fact]
        public void PlacedHalls_Siting_FollowsFocus()
        {
            int Count(bool focus)
            {
                var w = SimulationWorld.CreateDefault();
                w.Goods.DepositAt = OilEverywhere;
                w.City.Population.Residents = 3000;
                Road(w, new Vector2(-300, 0), new Vector2(300, 0));
                if (focus) w.Policy.Paint(new Vector2(0, 40), 250f, IndustryFocus.OilGas);
                var row = w.City.PlanHouseRow(HouseCatalog.LargeHall, new Vector2(-150, 25), new Vector2(150, 25));
                var halls = w.City.BuildHouseRow(row);
                Assert.True(halls.Count >= 8);
                w.Goods.ForceStep();
                int primary = halls.Count(b => IndustryBranches.HomeOf(w.Goods.FirmAt(b.Id).Recipe) == IndustryFocus.OilGas);
                output.WriteLine($"Schwerpunkt {(focus ? "Öl/Gas" : "automatisch")}: {primary} von {halls.Count} Hallen Öl/Gas · " +
                                 string.Join(", ", halls.Select(b => w.Goods.FirmAt(b.Id).Recipe.Name)));
                return primary * 100 / halls.Count;
            }
            int without = Count(false), with = Count(true);
            Assert.True(with > without, $"mit {with} %, ohne {without} %");
            Assert.True(with >= 30, $"mit Schwerpunkt nur {with} %"); // Bedarf zählt weiter: zu viele Ölförderungen senken den Score
        }

        [Fact]
        public void PlacedHall_Bankruptcy_NewTenant_BuildingStays()
        {
            var w = Street();
            var hall = w.City.BuildHouse(w.City.PlanHouse(HouseCatalog.SmallHall, new Vector2(0, 25)));
            Run(w, 60);
            w.Goods.ForceStep();
            var f = w.Goods.FirmAt(hall.Id);
            Assert.NotNull(f);
            f.AgeDays = 30;
            f.Cash = -1e7;
            f.ProfitEma = -1000;
            f.LossDays = GoodsRules.BankruptAfterLossDays + 5;
            int before = w.Goods.Stats.Bankruptcies;
            w.Goods.ForceCloseDay();
            Assert.Equal(before + 1, w.Goods.Stats.Bankruptcies);
            Assert.NotNull(w.City.GetBuilding(hall.Id));         // Hülle bleibt
            var g = w.Goods.FirmAt(hall.Id);
            Assert.NotNull(g);
            Assert.True(g.Cash > 0);                               // neuer Betreiber mit Startkapital
            Assert.True(g.Recipe.IsIndustry);
        }

        [Fact]
        public void CommercialLevels_UseOwnRules_DataDriven()
        {
            Assert.Equal(3, HouseRules.MaxLevelFor(ZoneType.Retail));
            Assert.Contains(HouseRules.CustomersFew, HouseRules.NeedsFor(ZoneType.Retail, 2));
            Assert.Contains(HouseRules.OfficeStaff, HouseRules.NeedsFor(ZoneType.Office, 2));
            Assert.Contains(HouseRules.FreightAccess, HouseRules.NeedsFor(ZoneType.Industry, 2));
            Assert.Contains(HouseRules.Shop, HouseRules.NeedsFor(ZoneType.Mixed, 2)); // Mischgebiet wie Wohnen
            Assert.DoesNotContain(HouseRules.Shop, HouseRules.NeedsFor(ZoneType.Retail, 3));

            var w = Street();
            var shop = w.City.BuildHouse(w.City.PlanHouse(HouseCatalog.Shop, new Vector2(0, 20)));
            Run(w, 60);
            Assert.True(shop.IsConstructed);
            var lv = w.LandValue;
            lv.FactsProvider = x => new BuildingFacts { LandValue = 0.1f, Satisfaction = 0.8f, Supplied = true, Occupancy = 1f, Skill = 1f };
            // Versorgung gegeben, Kundschaft echt ausgewertet: noch niemand wohnt in der Nähe
            lv.HouseNeedOverride = (b, n) => n.Kind == HouseNeedKind.Customers ? (bool?)null : true;
            var info = lv.LevelInfo(shop);
            Assert.True(info.PlayerHouse);
            Assert.Contains(info.Needs, s => s.Need == HouseRules.CustomersFew && !s.Met);
            Assert.Contains("Einwohner", info.MissingText);
            for (int d = 0; d < 10; d++) lv.ForceEvaluateDay();
            Assert.Equal(1, shop.Level);

            // Kundschaft: Wohnblock in der Nähe mit 200 Bewohnern
            var block = w.City.BuildHouse(w.City.PlanHouse(HouseCatalog.High, new Vector2(100, 30)));
            block.Residents = 200;
            Assert.True(lv.EvaluateNeed(shop, HouseRules.CustomersFew).Met);
            Assert.False(lv.EvaluateNeed(shop, HouseRules.CustomersMany).Met);
            for (int d = 0; d < HouseRules.RuleFor(ZoneType.Retail, 2).Days; d++) lv.ForceEvaluateDay();
            Assert.Equal(2, shop.Level);
            Assert.Equal(HouseCatalog.Shop.JobsAtLevel(2), shop.Jobs);
            Assert.Equal("belebt", HouseRules.LevelName(ZoneType.Retail, 2));
        }

        [Fact]
        public void IndustryLevel_NeedsFreightAccess_OfficeNeedsSkilledStaff()
        {
            var w = Street();
            var hall = w.City.BuildHouse(w.City.PlanHouse(HouseCatalog.LargeHall, new Vector2(-150, 25)));
            var office = w.City.BuildHouse(w.City.PlanHouse(HouseCatalog.OfficeBuilding, new Vector2(150, 25)));
            w.LandValue.ForceRecalculate();
            var freight = w.LandValue.EvaluateNeed(hall, HouseRules.FreightAccess);
            output.WriteLine($"ohne Autobahn: {freight.Detail}");
            Assert.False(freight.Met);
            // Autobahn 200 m entfernt
            Road(w, new Vector2(-400, -200), new Vector2(400, -200), null, RoadCatalog.Highway2);
            w.LandValue.ForceRecalculate();
            freight = w.LandValue.EvaluateNeed(hall, HouseRules.FreightAccess);
            output.WriteLine($"mit Autobahn: {freight.Detail}");
            Assert.True(freight.Met);

            w.LandValue.FactsProvider = x => new BuildingFacts { Satisfaction = 0.8f, Supplied = true, Occupancy = 1f, Skill = 0.1f };
            var staff = w.LandValue.EvaluateNeed(office, HouseRules.OfficeStaff);
            Assert.False(staff.Met);
            Assert.Contains("Studium", staff.Detail);
            w.LandValue.FactsProvider = x => new BuildingFacts { Satisfaction = 0.8f, Supplied = true, Occupancy = 1f, Skill = 0.4f };
            Assert.True(w.LandValue.EvaluateNeed(office, HouseRules.OfficeStaff).Met);

            // Vorschau am Bauplatz nennt die Anforderungen der Bauart
            var preview = w.LandValue.PreviewHouse(w.City.PlanHouse(HouseCatalog.SmallHall, new Vector2(0, 25)));
            Assert.Contains(preview.Needs, s => s.Need == HouseRules.FreightAccess);
            Assert.DoesNotContain(preview.Needs, s => s.Need == HouseRules.PrimarySchool);
            Assert.Equal(3, preview.MaxLevel);
        }

        // ------------------------------------------------------------ Zonen: neues Spiel ohne, alte Stände mit

        [Fact]
        public void NewGame_WithoutZones_NothingGrows_DemandStillComputed()
        {
            var w = Street();
            Road(w, new Vector2(0, -200), new Vector2(0, 200));
            Run(w, 2 * SimulationClock.TicksPerDay);
            Assert.Empty(w.City.Buildings);
            Assert.Equal(0, w.City.Stats.ZonedCells);
            Assert.True(w.City.Demand[ZoneType.ResidentialLow] > 0f);
            // gesetzte Gebäude laufen ohne Zonen normal
            var row = w.City.PlanHouseRow(HouseCatalog.Low, new Vector2(40, 15), new Vector2(150, 15));
            w.City.BuildHouseRow(row);
            Run(w, 2 * SimulationClock.TicksPerDay);
            Assert.All(w.City.Buildings, b => Assert.True(b.IsPlayerPlaced));
            Assert.Contains(w.City.Buildings, b => b.Residents > 0);
        }

        [Fact]
        public void OldSaveWithZones_KeepsGrowing_AfterLoad_Deterministic()
        {
            var a = Street();
            a.City.Paint(new Vector2(-150, 20), 70f, ZoneType.ResidentialLow);
            a.City.Paint(new Vector2(150, 20), 50f, ZoneType.Industry);
            Run(a, 120);
            int grown = a.City.Buildings.Count(b => !b.IsPlayerPlaced);
            Assert.True(grown > 0);
            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            Assert.True(b.City.Stats.ZonedCells > 0);
            for (int i = 0; i < 400; i++) { a.Step(); b.Step(); }
            Assert.True(b.City.Buildings.Count(x => !x.IsPlayerPlaced) > grown, "Zonen wachsen nach dem Laden weiter");
            Assert.Equal(SaveGame.SectionHashes(a), SaveGame.SectionHashes(b));
        }

        [Fact]
        public void SaveLoad_PlacedCommercialRow_Deterministic()
        {
            var a = Street();
            a.City.BuildHouseRow(a.City.PlanHouseRow(HouseCatalog.Low, new Vector2(-250, 15), new Vector2(-120, 15)));
            a.City.BuildHouseRow(a.City.PlanHouseRow(HouseCatalog.Shop, new Vector2(-100, 15), new Vector2(-40, 15)));
            a.City.BuildHouseRow(a.City.PlanHouseRow(HouseCatalog.SmallHall, new Vector2(60, 20), new Vector2(200, 20)));
            Run(a, 300);
            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            Assert.Equal(a.City.PlayerHouseCount, b.City.PlayerHouseCount);
            foreach (var x in a.City.Buildings)
                Assert.Same(HouseCatalog.Of(x), HouseCatalog.Of(b.City.GetBuilding(x.Id)));
            for (int i = 0; i < 200; i++) { a.Step(); b.Step(); }
            Assert.Equal(SaveGame.SectionHashes(a), SaveGame.SectionHashes(b));
        }

        // ------------------------------------------------------------ Nachfrage-Anzeige

        [Fact]
        public void DemandReport_ExplainsWhy()
        {
            var w = Street();
            w.City.BuildHouseRow(w.City.PlanHouseRow(HouseCatalog.High, new Vector2(-250, 25), new Vector2(-50, 25)));
            Run(w, SimulationClock.TicksPerDay);
            foreach (var k in DemandReport.All)
            {
                var e = DemandReport.Explain(w, k);
                output.WriteLine(e.Tooltip);
                Assert.InRange(e.Value, 0f, 100f);
                Assert.NotEmpty(e.Reasons);
                Assert.Equal(DemandReport.ValueOf(w.City, k), e.Value);
            }
            Assert.Contains("Ladenjobs", DemandReport.Explain(w, DemandKind.Shop).Tooltip);
            Assert.Equal(DemandKind.Shop, DemandReport.KindOf(ZoneType.Mixed));
            Assert.Equal(DemandKind.Residential, DemandReport.KindOf(ZoneType.ResidentialHigh));
        }
    }
}
