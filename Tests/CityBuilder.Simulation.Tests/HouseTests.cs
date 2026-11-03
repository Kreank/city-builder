using System.IO;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.LandValue;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>Wohnhäuser einzeln setzen (Anno-Stil): Planen, Bauen, Sperren, Bevölkern, Aufstieg/Abstieg, Speichern.</summary>
    public class HouseTests
    {
        readonly ITestOutputHelper output;
        public HouseTests(ITestOutputHelper output) => this.output = output;

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, t ?? RoadCatalog.CityTwoLane);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        static void Run(SimulationWorld w, int ticks)
        {
            for (int i = 0; i < ticks; i++) w.Step();
        }

        static SimulationWorld Street(WorldSettings settings = null)
        {
            var w = SimulationWorld.CreateDefault(settings);
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            return w;
        }

        static Building Place(SimulationWorld w, HouseType type, Vector2 cursor)
        {
            var plan = w.City.PlanHouse(type, cursor);
            Assert.True(plan.IsValid, plan.Error.ToString());
            var b = w.City.BuildHouse(plan);
            Assert.NotNull(b);
            return b;
        }

        // ------------------------------------------------------------ Planen & Platzregeln

        [Fact]
        public void Plan_AlignsFrontToRoad_AndChecksPlacement()
        {
            var w = Street();
            var c = w.City;
            var plan = c.PlanHouse(HouseCatalog.Medium, new Vector2(50, 25));
            Assert.True(plan.IsValid, plan.Error.ToString());
            Assert.True(plan.Aligned);
            Assert.True(Vector2.Dot(plan.Front, new Vector2(0, -1)) > 0.99f, $"Vorderseite {plan.Front}");
            // Vorderkante 0,5 m vor dem Gehweg (halbe Straßenbreite)
            Assert.InRange(plan.FrontMid.Y, RoadCatalog.CityTwoLane.HalfWidth + 0.3f, RoadCatalog.CityTwoLane.HalfWidth + 0.7f);
            Assert.True(plan.AccessSegmentId >= 0);
            Assert.Equal(HouseCatalog.Medium.Cost, plan.Cost);

            Assert.Equal(HouseBuildError.NoRoadAccess, c.PlanHouse(HouseCatalog.Low, new Vector2(50, 200)).Error);
            Assert.Equal(HouseBuildError.OverlapsRoad, c.PlanHouseAt(HouseCatalog.Low, new Vector2(50, 3), new Vector2(0, -1)).Error);
            Assert.Equal(HouseBuildError.OutOfWorld, c.PlanHouseAt(HouseCatalog.Low, w.Max - new Vector2(2, 2), Vector2.UnitY).Error);
            // Rückseite zur Straße → keine Zufahrt an der Vorderseite
            Assert.Equal(HouseBuildError.NoRoadAccess, c.PlanHouseAt(HouseCatalog.Low, plan.Position, -plan.Front).Error);

            // Dienstgebäude im Weg → ungültig mit Namen
            var police = w.Services.Build(w.Services.PlanAligned(ServiceCatalog.PoliceStation, new Vector2(150, 20)));
            Assert.NotNull(police);
            var blocked = c.PlanHouse(HouseCatalog.Low, police.Position);
            Assert.Equal(HouseBuildError.OverlapsStructure, blocked.Error);
            Assert.Equal(ServiceCatalog.PoliceStation.DisplayName, blocked.BlockerName);
            // Strommast im Weg
            var line = w.Utilities.TryBuildLine(UtilityKind.Power, new Vector2(-200, 60), new Vector2(-200, 200));
            Assert.NotNull(line);
            var onPole = c.PlanHouseAt(HouseCatalog.Low, line.Poles[1], Vector2.UnitY);
            output.WriteLine($"Mast {line.Poles[1]} → {onPole.Error}");
            Assert.Equal(HouseBuildError.OverlapsStructure, onPole.Error);
        }

        [Fact]
        public void Plan_OverZoneBuilding_IsInvalid_NotDemolished()
        {
            var w = Street();
            w.City.Paint(new Vector2(-100, 20), 60f, ZoneType.ResidentialLow);
            for (int i = 0; i < 400 && !w.City.Buildings.Any(); i++) w.Step();
            var zoneBuilding = w.City.Buildings.First();
            var plan = w.City.PlanHouse(HouseCatalog.Low, zoneBuilding.Position);
            Assert.Equal(HouseBuildError.OverlapsBuilding, plan.Error);
            Assert.Same(zoneBuilding, plan.BlockingBuilding);
            Assert.Null(w.City.BuildHouse(plan));
            Assert.NotNull(w.City.GetBuilding(zoneBuilding.Id));
        }

        [Fact]
        public void Build_ChargesCityBudget_AndDemolishRefundsQuarter()
        {
            var w = Street(new WorldSettings { UnlimitedFunds = false, StartingFunds = 50_000 });
            double before = w.Economy.Balance;
            var b = Place(w, HouseCatalog.High, new Vector2(0, 30));
            Assert.Equal(before - HouseCatalog.High.Cost, w.Economy.Balance, 3);
            Assert.Equal(HouseCatalog.High.Cost, w.Economy.CurrentWeek.Amount(BudgetCategory.HouseConstruction), 3);
            Assert.True(b.IsPlayerPlaced);
            Assert.Equal(BuildingOrigin.Player, b.Origin);
            Assert.Equal(HouseCatalog.High.HouseholdsAtLevel(1), b.Households);
            Assert.Equal(0, b.Jobs);
            Assert.Same(HouseCatalog.High, HouseCatalog.Of(b));

            // Zu teuer (50 000 − 45 000 + 50 000 Kredit < 2 × 45 000 … der dritte Block geht nicht mehr)
            Place(w, HouseCatalog.High, new Vector2(100, 30));
            Assert.Equal(HouseBuildError.InsufficientFunds, w.City.PlanHouse(HouseCatalog.High, new Vector2(200, 30)).Error);

            double mid = w.Economy.Balance;
            Assert.True(w.City.DemolishHouse(b.Id));
            Assert.Null(w.City.GetBuilding(b.Id));
            Assert.Equal(mid + HouseCatalog.High.Cost * EconomyRules.DemolitionRefundShare, w.Economy.Balance, 3);
            Assert.False(w.City.DemolishHouse(b.Id));
        }

        // ------------------------------------------------------------ Sperren

        [Fact]
        public void House_BlocksRoads_LikeServices_AndOtherStructures()
        {
            var w = Street();
            var b = Place(w, HouseCatalog.Medium, new Vector2(0, 30));
            var plan = RoadBuilder.Plan(w.Roads, new Vector2(b.Position.X, 150), 8f, null, new Vector2(b.Position.X, 8), 8f, RoadCatalog.CityTwoLane);
            Assert.Equal(RoadBuildError.OverlapsStructure, plan.Error);
            Assert.Equal("house", plan.BlockedBy.Kind);
            Assert.Equal(b.Id, plan.BlockedBy.Id);
            Assert.Equal(HouseCatalog.Medium.DisplayName, plan.BlockedBy.Name);
            Assert.Empty(RoadBuilder.Build(w.Roads, plan));
            Assert.NotNull(w.City.GetBuilding(b.Id));

            // Dienste, Versorger und Depots reißen gesetzte Häuser nicht ab, ihr Plan ist ungültig
            Assert.Equal(ServiceBuildError.OverlapsFacility, w.Services.Plan(ServiceCatalog.Library, b.Position, b.Front).Error);
            output.WriteLine($"Windrad: {w.Utilities.PlanFacility(UtilityCatalog.WindTurbine, b.Position).Error}");
            Assert.Equal(UtilityBuildError.OverlapsFacility, w.Utilities.PlanFacility(UtilityCatalog.WindTurbine, b.Position).Error);
            Assert.Equal(Transit.TransitBuildError.OverlapsFacility, w.Transit.PlanDepot(Transit.TransitCatalog.BusDepot, new Vector2(b.Position.X, RoadCatalog.CityTwoLane.HalfWidth + 16f), 180f).Error);
        }

        [Fact]
        public void ZoneLogic_DoesNotRemovePlayerHouses()
        {
            var w = Street();
            w.Utilities.AbandonmentEnabled = true;
            var b = Place(w, HouseCatalog.Low, new Vector2(-50, 20));
            // Umwidmen, Zone löschen, Zonen ringsum wachsen lassen
            w.City.Paint(b.Position, 40f, ZoneType.Industry);
            w.City.Paint(b.Position, 40f, ZoneType.None);
            w.City.Paint(b.Position, 60f, ZoneType.ResidentialMedium);
            Run(w, 500);
            Assert.NotNull(w.City.GetBuilding(b.Id));
            Assert.DoesNotContain(w.City.Buildings, o => o.Id != b.Id && b.Contains(o.Position, 4f));
            Assert.Contains(w.City.Buildings, o => !o.IsPlayerPlaced); // daneben wächst es

            // Straße vor dem Haus abreißen: Haus bleibt stehen (ohne Zufahrt), keine Zonen-Abrisse
            foreach (var s in w.Roads.Segments.ToList()) RoadBuilder.Demolish(w.Roads, s);
            Run(w, 10);
            var still = w.City.GetBuilding(b.Id);
            Assert.NotNull(still);
            Assert.False(w.City.HasRoadAccess(still));
        }

        // ------------------------------------------------------------ Bevölkern

        [Fact]
        public void PlacedHouse_IsPopulated_ByCitizenSystem()
        {
            var w = Street();
            var b = Place(w, HouseCatalog.Medium, new Vector2(0, 30));
            for (int i = 0; i < 3000 && b.OccupiedHouseholds < 3; i++) w.Step();
            output.WriteLine($"Haushalte {b.OccupiedHouseholds}/{b.Households}, Bewohner {b.Residents}");
            Assert.True(b.IsConstructed);
            Assert.True(b.OccupiedHouseholds >= 3);
            Assert.True(b.Residents > 0);
            Assert.True(w.Citizens.HouseholdsIn(b.Id).Any());
        }

        // ------------------------------------------------------------ Aufstieg/Abstieg (Anno-Regeln)

        static SimulationWorld HappyHouse(out Building b, bool needsMet = true)
        {
            var w = Street();
            b = Place(w, HouseCatalog.Low, new Vector2(0, 20));
            Run(w, 60); // fertig bauen
            Assert.True(b.IsConstructed);
            w.LandValue.FactsProvider = x => new BuildingFacts { LandValue = 0.1f, Satisfaction = 0.8f, Supplied = true, Occupancy = 1f };
            bool met = needsMet;
            w.LandValue.HouseNeedOverride = (x, n) => met;
            return w;
        }

        [Fact]
        public void Upgrade_WhenAllNeedsMet_IgnoresLandValue_AndRaisesCapacity()
        {
            var w = HappyHouse(out var b);
            var lv = w.LandValue;
            var rule = HouseRules.RuleFor(2);
            for (int d = 1; d < rule.Days; d++) { lv.ForceEvaluateDay(); Assert.Equal(1, b.Level); }
            lv.ForceEvaluateDay();
            Assert.Equal(2, b.Level);
            Assert.Equal(LandValueRules.HouseholdsFor(ZoneType.ResidentialLow, 4, 2), b.Households);
            Assert.True(b.Households > HouseCatalog.Low.HouseholdsAtLevel(1));
            for (int d = 0; d < HouseRules.RuleFor(3).Days; d++) lv.ForceEvaluateDay();
            Assert.Equal(3, b.Level);
            var info = lv.LevelInfo(b);
            Assert.True(info.PlayerHouse);
            Assert.Equal(HouseRules.MaxLevel, info.MaxLevel);
            Assert.Equal("höchste Stufe", info.MissingText);
        }

        [Fact]
        public void MissingNeed_IsNamed_AndBlocksUpgrade_SoftConditionPauses()
        {
            var w = HappyHouse(out var b);
            var lv = w.LandValue;
            lv.HouseNeedOverride = (x, n) => n != HouseRules.PrimarySchool;
            for (int d = 0; d < 10; d++) lv.ForceEvaluateDay();
            Assert.Equal(1, b.Level);
            var info = lv.LevelInfo(b);
            Assert.True((info.Missing & LevelGap.Needs) != 0);
            Assert.Equal("Es fehlt: Grundschule in Reichweite", info.MissingText);
            Assert.Contains(info.Needs, s => s.Need == HouseRules.PrimarySchool && !s.Met);
            Assert.Contains(info.Needs, s => s.Need == HouseRules.Shop && s.Met);
            Assert.Equal(HouseRules.NeedsFor(2).Count, info.Needs.Count);

            // Weiche Bedingung: alles da, aber unzufrieden → Fortschritt pausiert (kein Aufstieg, kein Verlust)
            lv.HouseNeedOverride = (x, n) => true;
            lv.ForceEvaluateDay();
            Assert.Equal(1, lv.LevelInfo(b).ProgressDays);
            lv.FactsProvider = x => new BuildingFacts { LandValue = 0.1f, Satisfaction = 0.3f, Supplied = true, Occupancy = 1f };
            for (int d = 0; d < 10; d++) lv.ForceEvaluateDay();
            info = lv.LevelInfo(b);
            Assert.Equal(1, b.Level);
            Assert.Equal(1, info.ProgressDays);
            Assert.StartsWith("Es fehlt: Zufriedenheit der Bewohner", info.MissingText);
        }

        [Fact]
        public void Downgrade_AfterDays_WhenOwnLevelNeedGoesMissing()
        {
            var w = HappyHouse(out var b);
            var lv = w.LandValue;
            for (int d = 0; d < HouseRules.RuleFor(2).Days; d++) lv.ForceEvaluateDay();
            Assert.Equal(2, b.Level);
            // Laden schließt → Stufe 2 nicht mehr versorgt
            lv.HouseNeedOverride = (x, n) => n != HouseRules.Shop;
            for (int d = 1; d < HouseRules.DeclineDays; d++) lv.ForceEvaluateDay();
            Assert.Equal(2, b.Level);
            var info = lv.LevelInfo(b);
            Assert.True(info.OwnNeedsMissing);
            Assert.Equal(HouseRules.DeclineDays - 1, info.DeclineDays);
            lv.ForceEvaluateDay();
            Assert.Equal(1, b.Level);
            Assert.Equal(HouseCatalog.Low.HouseholdsAtLevel(1), b.Households);
        }

        [Fact]
        public void ZoneBuildings_KeepLandValueRules()
        {
            var w = Street();
            w.City.Paint(new Vector2(-100, 20), 60f, ZoneType.ResidentialLow);
            for (int i = 0; i < 400 && !w.City.Buildings.Any(x => x.IsConstructed); i++) w.Step();
            var z = w.City.Buildings.First(x => x.IsConstructed);
            Assert.False(z.IsPlayerPlaced);
            // Bodenwert zu niedrig: Anforderungen der Häuser dürfen nicht helfen
            w.LandValue.FactsProvider = x => new BuildingFacts { LandValue = 0.1f, Satisfaction = 0.8f, Supplied = true, Occupancy = 1f, Skill = 1f };
            w.LandValue.HouseNeedOverride = (x, n) => true;
            for (int d = 0; d < 20; d++) w.LandValue.ForceEvaluateDay();
            Assert.Equal(1, z.Level);
            var info = w.LandValue.LevelInfo(z);
            Assert.False(info.PlayerHouse);
            Assert.True((info.Missing & LevelGap.LandValue) != 0);
        }

        // ------------------------------------------------------------ Echte Anforderungen

        [Fact]
        public void RealNeeds_SchoolCoverage_ShopWithStock_AndPreview()
        {
            var w = Street();
            var near = Place(w, HouseCatalog.Low, new Vector2(-40, 20));
            var lv = w.LandValue;
            var noSchool = lv.PreviewHouse(w.City.PlanHouse(HouseCatalog.Low, new Vector2(40, 20)));
            Assert.Contains(noSchool.Needs, s => s.Need == HouseRules.PrimarySchool && !s.Met);
            Assert.Equal(1, noSchool.ReachableLevel);

            var school = w.Services.Build(w.Services.PlanAligned(ServiceCatalog.PrimarySchool, new Vector2(60, 25)));
            Assert.NotNull(school);
            w.Services.ForceTick();
            var st = lv.EvaluateNeed(near, HouseRules.PrimarySchool);
            output.WriteLine($"Grundschule: {st.Detail}");
            Assert.True(st.Met, st.Detail);
            Assert.False(lv.EvaluateNeed(near, HouseRules.Police).Met);
            var preview = lv.PreviewHouse(w.City.PlanHouse(HouseCatalog.Low, new Vector2(120, 20)));
            Assert.Contains(preview.Needs, s => s.Need == HouseRules.PrimarySchool && s.Met);
            Assert.Equal(HouseRules.AllNeeds().Count, preview.Needs.Count);

            // Laden: Einzelhandel wächst an der Straße → in Fußweite
            Assert.False(lv.EvaluateNeed(near, HouseRules.Shop).Met);
            w.City.Paint(new Vector2(-150, -20), 50f, ZoneType.Retail);
            for (int i = 0; i < 600 && !w.City.Buildings.Any(x => x.Zone == ZoneType.Retail && x.IsConstructed); i++) w.Step();
            var shop = lv.EvaluateNeed(near, HouseRules.Shop);
            output.WriteLine($"Laden: {shop.Detail}");
            Assert.True(w.City.Buildings.Any(x => x.Zone == ZoneType.Retail && x.IsConstructed));
            bool stocked = w.Goods.ShopHasStock(w.City.Buildings.First(x => x.Zone == ZoneType.Retail && x.IsConstructed).Id);
            Assert.Equal(stocked, shop.Met);
        }

        // ------------------------------------------------------------ Verschieben

        [Fact]
        public void Move_KeepsIdLevelAndResidents_ChargesTenPercent()
        {
            var w = Street(new WorldSettings { UnlimitedFunds = false, StartingFunds = 100_000 });
            var b = Place(w, HouseCatalog.Medium, new Vector2(0, 30));
            for (int i = 0; i < 2000 && b.OccupiedHouseholds == 0; i++) w.Step();
            int households = b.OccupiedHouseholds;
            Assert.True(households > 0);
            double before = w.Economy.Balance;
            var plan = w.City.PlanMoveHouse(b.Id, new Vector2(150, -30));
            Assert.True(plan.IsValid, plan.Error.ToString());
            Assert.Equal(HouseCatalog.Medium.Cost * CitySystem.HouseMoveCostShare, plan.Cost, 3);
            // Eigene alte Fläche zählt nicht: Verschieben um wenige Meter geht
            Assert.True(w.City.PlanMoveHouse(b.Id, b.Position + new Vector2(4, 0)).IsValid);
            Assert.True(w.City.MoveHouse(plan));
            Assert.Same(b, w.City.GetBuilding(b.Id));
            Assert.True(b.Position.Y < 0);
            Assert.Same(b, w.City.BuildingAt(b.Position));
            Assert.Equal(before - plan.Cost, w.Economy.Balance, 3);
            Assert.Equal(households, w.Citizens.HouseholdsIn(b.Id).Count());
            var road = RoadBuilder.Plan(w.Roads, new Vector2(b.Position.X, -150), 8f, null, new Vector2(b.Position.X, -8), 8f, RoadCatalog.CityTwoLane);
            Assert.Equal(RoadBuildError.OverlapsStructure, road.Error);
        }

        // ------------------------------------------------------------ Speichern

        [Fact]
        public void SaveLoad_KeepsOrigin_AndStaysDeterministic()
        {
            var a = Street();
            a.City.Paint(new Vector2(-150, 20), 60f, ZoneType.ResidentialLow);
            var h = Place(a, HouseCatalog.Medium, new Vector2(60, 30));
            Run(a, 300);
            var b = SaveGame.Load(SaveGame.SaveToBytes(a));
            var hb = b.City.GetBuilding(h.Id);
            Assert.NotNull(hb);
            Assert.True(hb.IsPlayerPlaced);
            Assert.Equal(1, b.City.PlayerHouseCount);
            Assert.Contains(b.Roads.Structures, s => s.Kind == "house" && s.Id == h.Id);
            Assert.Contains(b.City.Buildings, x => !x.IsPlayerPlaced);
            for (int i = 0; i < 200; i++) { a.Step(); b.Step(); }
            Assert.Equal(SaveGame.SectionHashes(a), SaveGame.SectionHashes(b));
        }

        [Fact]
        public void Migration_CityV1_AllBuildingsFromZones()
        {
            var a = Street();
            a.City.Paint(new Vector2(-150, 20), 60f, ZoneType.ResidentialLow);
            Run(a, 200);
            Assert.Contains(a.City.Buildings, x => !x.IsPlayerPlaced);
            byte[] v1;
            using (var ms = new MemoryStream())
            {
                var wr = new SaveWriter(ms);
                a.City.WriteLegacyV1 = true;
                try { a.City.Write(wr); } finally { a.City.WriteLegacyV1 = false; }
                wr.Flush();
                v1 = ms.ToArray();
            }
            var c = SaveGame.Capture(a);
            c.Set("city", 1, v1);
            var b = SaveGame.Load(c.ToBytes());
            Assert.Equal(a.City.Buildings.Count(), b.City.Buildings.Count());
            Assert.All(b.City.Buildings, x => Assert.Equal(BuildingOrigin.Zone, x.Origin));
            Assert.Equal(0, b.City.PlayerHouseCount);
        }
    }
}
