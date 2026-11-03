using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    public class ServiceTests
    {
        readonly ITestOutputHelper output;
        public ServiceTests(ITestOutputHelper output) => this.output = output;

        static void Road(SimulationWorld w, Vector2 a, Vector2 b, RoadType t = null, float ha = 0f, float hb = 0f)
        {
            var plan = RoadBuilder.Plan(w.Roads, a, 8f, null, b, 8f, t ?? RoadCatalog.CityTwoLane, ha, hb);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.Build(w.Roads, plan);
        }

        static void Grow(SimulationWorld w, Vector2 center, float radius, ZoneType zone, int minBuildings)
        {
            w.City.Paint(center, radius, zone);
            for (int i = 0; i < 3000; i++)
            {
                w.Step();
                if (i % 50 == 0 && w.City.Buildings.Count(b => b.IsConstructed && b.Residents > 0 &&
                        Vector2.Distance(b.Position, center) < radius + 20f) >= minBuildings) break;
            }
            for (int i = 0; i < 60; i++) w.Step();
        }

        static Building[] Inhabited(SimulationWorld w, Vector2 center, float radius) =>
            w.City.Buildings.Where(b => b.IsConstructed && b.Residents > 0 && Vector2.Distance(b.Position, center) < radius)
                .OrderBy(b => b.Id).ToArray();

        /// <summary>Zwei getrennte Straßen mit bewohnten Häusern (West 0..200, Ost 500..700).</summary>
        static SimulationWorld TwoStreets(out Building[] west, out Building[] east)
        {
            var w = SimulationWorld.CreateDefault();
            w.Utilities.AbandonmentEnabled = false;
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            Road(w, new Vector2(500, 0), new Vector2(700, 0));
            Grow(w, new Vector2(100, 20), 80f, ZoneType.ResidentialLow, 4);
            Grow(w, new Vector2(600, 20), 80f, ZoneType.ResidentialLow, 4);
            west = Inhabited(w, new Vector2(100, 20), 110f);
            east = Inhabited(w, new Vector2(600, 20), 110f);
            Assert.True(west.Length >= 3, $"West: {west.Length}");
            Assert.True(east.Length >= 3, $"Ost: {east.Length}");
            return w;
        }

        static ServiceType Custom(string kind, float capacity, float rangeMinutes = 1.5f) =>
            new ServiceType("test_" + kind, "Test " + kind, kind, 24f, 20f, capacity, "Personen", rangeMinutes, 0f, 0f);

        // ------------------------------------------------------------ Reichweite

        [Fact]
        public void Reach_FollowsRoadNetwork_RiverWithoutBridgeSeparates()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { SizeMeters = 1024f });
            var s = w.Services;
            // Fluss: Wasserspiegel −1 m (flache Testwelt hat sonst keins; 0 ginge nicht, weil Boden unter Straßen 0,12 m
            // tiefer liegt), Streifen x 100..130 auf −4 m absenken
            w.Terrain.WaterLevel = -1f;
            for (float y = -240f; y <= 240f; y += 8f)
                for (int i = 0; i < 4; i++)
                    w.Terrain.Terraform(new TerraformBrush(TerraformMode.Level, new Vector2(115f, y), 16f, 5f, 0f, -4f));
            Assert.True(w.Terrain.IsWater(new Vector2(115, 0)));
            Assert.False(w.Terrain.IsWater(new Vector2(60, 0)));

            Road(w, new Vector2(0, -150), new Vector2(0, 150));     // Westufer
            Road(w, new Vector2(230, -150), new Vector2(230, 150)); // Ostufer
            var police = s.TryBuild(ServiceCatalog.PoliceStation, new Vector2(-18, 0), 90f); // Vorderseite zur Straße (Osten)
            Assert.NotNull(police);
            Assert.True(police.HasAccess);
            s.ForceTick();

            var westPoint = new Vector2(10, 40);
            var eastPoint = new Vector2(220, 0); // 240 m Luftlinie – aber kein Weg über den Fluss
            Assert.Equal(1f, s.CoverageAt(ServiceKinds.Police, westPoint), 3);
            Assert.Equal(0f, s.CoverageAt(ServiceKinds.Police, eastPoint));

            // Brücke (5 m über Wasser, Rampen ≤ 10 %) verbindet beide Ufer → Ostufer ist versorgt
            Road(w, new Vector2(0, 0), new Vector2(90, 0), null, 0f, 5f);
            Road(w, new Vector2(90, 0), new Vector2(140, 0), null, 5f, 5f);
            Road(w, new Vector2(140, 0), new Vector2(230, 0), null, 5f, 0f);
            Assert.NotNull(s.GetFacility(police.Id));
            s.ForceTick();
            float east = s.CoverageAt(ServiceKinds.Police, eastPoint);
            output.WriteLine($"Ostufer nach Brückenbau: {east:0.###}");
            Assert.True(east > 0.9f);
            Assert.Equal(0f, s.CoverageAt(ServiceKinds.Fire, eastPoint)); // andere Art: keine Feuerwache gebaut
        }

        [Fact]
        public void Reach_FadesWithDriveTime_AndEndsAtRange()
        {
            var w = SimulationWorld.CreateDefault();
            var s = w.Services;
            Road(w, new Vector2(-1000, 300), new Vector2(1000, 300));
            var police = s.TryBuild(ServiceCatalog.PoliceStation, new Vector2(-1000 + 12, 300 - 18));
            Assert.NotNull(police);
            s.ForceTick();
            float metersPerSecond = 50f / 3.6f * ServiceSystem.SpeedFactor;
            float fullUntil = ServiceCatalog.PoliceStation.RangeSeconds * ServiceSystem.FullEffectShare * metersPerSecond;
            float range = ServiceCatalog.PoliceStation.RangeSeconds * metersPerSecond;
            output.WriteLine($"voll bis {fullUntil:0} m, Reichweite {range:0} m");

            float At(float x) => s.CoverageAt(ServiceKinds.Police, new Vector2(x, 310));
            Assert.Equal(1f, At(-1000 + fullUntil * 0.5f), 3);
            float mid = At(-1000 + (fullUntil + range) * 0.5f);
            Assert.InRange(mid, 0.3f, 0.7f);
            Assert.Equal(0f, At(-1000 + range + 100f));
            Assert.True(At(-1000 + fullUntil + 50f) > At(-1000 + fullUntil + 150f), "Wirkung nimmt mit der Fahrzeit ab");
        }

        [Fact]
        public void Reach_OneWayStreet_OnlyInDrivingDirection()
        {
            var w = SimulationWorld.CreateDefault();
            var s = w.Services;
            // Einbahnstraße von West nach Ost; Wache in der Mitte
            Road(w, new Vector2(0, 0), new Vector2(600, 0), RoadCatalog.CityOneWay1);
            var fire = s.TryBuild(ServiceCatalog.FireStation, new Vector2(300, -22));
            Assert.NotNull(fire);
            s.ForceTick();
            Assert.True(s.CoverageAt(ServiceKinds.Fire, new Vector2(400, 5)) > 0.9f, "in Fahrtrichtung erreichbar");
            Assert.Equal(0f, s.CoverageAt(ServiceKinds.Fire, new Vector2(200, 5))); // gegen die Einbahn: kein Weg
        }

        [Fact]
        public void Parks_UseWalkingRadius_WithoutRoad()
        {
            var w = SimulationWorld.CreateDefault();
            var s = w.Services;
            var park = s.TryBuild(ServiceCatalog.ParkSmall, new Vector2(300, 300));
            Assert.NotNull(park); // Park braucht keine Straße
            s.ForceTick();
            Assert.Equal(1f, s.CoverageAt(ServiceKinds.Park, new Vector2(300, 360)), 3);
            Assert.Equal(0f, s.CoverageAt(ServiceKinds.Park, new Vector2(300, 300 + 8 + ServiceCatalog.ParkSmall.Range + 5f)));
            // Polizei braucht dagegen eine Zufahrt
            Assert.Equal(ServiceBuildError.NoRoadAccess, s.Plan(ServiceCatalog.PoliceStation, new Vector2(-300, -300)).Error);
        }

        // ------------------------------------------------------------ Abdeckung von Gebäuden, Überlastung

        [Fact]
        public void Coverage_PerBuilding_AndOverloadLowersIt()
        {
            var w = TwoStreets(out var west, out var east);
            var s = w.Services;
            var tiny = s.TryBuild(Custom(ServiceKinds.Police, 1f), new Vector2(-18, 0), 90f); // Vorderseite zum Straßenende
            Assert.NotNull(tiny);
            s.ForceTick();
            output.WriteLine($"Belastung {tiny.Load:0.#} bei Kapazität 1, Faktor {tiny.LoadFactor:0.###}");
            Assert.True(tiny.Load > 1f);
            Assert.All(west, b => Assert.InRange(s.Coverage(ServiceKinds.Police, b), 0.0001f, tiny.LoadFactor + 1e-4f));
            Assert.All(east, b => Assert.Equal(0f, s.Coverage(ServiceKinds.Police, b)));

            // Ausreichende Kapazität → volle Abdeckung im Nahbereich
            Assert.True(s.Remove(tiny.Id));
            var real = s.TryBuild(ServiceCatalog.PoliceStation, new Vector2(-18, 0), 90f);
            s.ForceTick();
            Assert.True(real.Utilization < 1f);
            Assert.All(west, b => Assert.True(s.Coverage(ServiceKinds.Police, b) > 0.95f, $"{b}: {s.Coverage(ServiceKinds.Police, b):0.##}"));
            Assert.Equal(west.Length + 0, west.Count(b => s.Coverage(ServiceKinds.Police, b) > 0f));
            Assert.True(s.AverageCoverage(ServiceKinds.Police) > 0f);

            // Halbe Kapazität des Bedarfs → Abdeckung ≈ 0,5
            float load = real.Load;
            Assert.True(s.Remove(real.Id));
            var half = s.TryBuild(Custom(ServiceKinds.Police, load * 0.5f), new Vector2(-18, 0), 90f);
            s.ForceTick();
            Assert.InRange(half.LoadFactor, 0.45f, 0.55f);
            Assert.True(s.Coverage(ServiceKinds.Police, west[0]) <= 0.55f);
        }

        [Fact]
        public void Education_IsMeanOfPrimaryAndSecondary()
        {
            var w = TwoStreets(out var west, out _);
            var s = w.Services;
            Assert.NotNull(s.TryBuild(ServiceCatalog.PrimarySchool, new Vector2(-20, 0), 90f));
            s.ForceTick();
            var b = west[0];
            Assert.True(s.Coverage(ServiceKinds.EducationPrimary, b) > 0.9f);
            Assert.Equal(0f, s.Coverage(ServiceKinds.EducationSecondary, b));
            Assert.Equal(0.5f * s.Coverage(ServiceKinds.EducationPrimary, b), s.Coverage(ServiceKinds.Education, b), 4);
            Assert.Equal(0f, s.Coverage("unbekannt", b));
        }

        // ------------------------------------------------------------ Müll

        [Fact]
        public void Garbage_PilesUpWithoutCollection_AndIsCollectedInReach()
        {
            var w = TwoStreets(out var west, out var east);
            var s = w.Services;
            for (int i = 0; i < 48; i++) s.ForceTick(); // ein Tag ohne Müllabfuhr
            float before = west.Sum(b => s.GarbageAt(b));
            output.WriteLine($"Müll West nach 1 Tag: {before:0.###} t");
            Assert.True(before > 0f);
            Assert.All(west, b => Assert.True(s.GarbageAt(b) > 0f));

            var dump = s.TryBuild(ServiceCatalog.Landfill, new Vector2(-32, 0), 90f);
            Assert.NotNull(dump);
            float eastBefore = east.Sum(b => s.GarbageAt(b));
            for (int i = 0; i < 48 * 3; i++) s.ForceTick();
            float after = west.Sum(b => s.GarbageAt(b));
            float eastAfter = east.Sum(b => s.GarbageAt(b));
            output.WriteLine($"Nach 3 Tagen mit Müllhalde: West {after:0.###} t, Ost {eastBefore:0.###} → {eastAfter:0.###} t, Halde {dump.Stored:0.##} t");
            Assert.True(after < before * 0.2f, "Rückstau im Westen abgebaut");
            Assert.True(eastAfter > eastBefore, "Osten (keine Straßenverbindung) stapelt weiter");
            Assert.True(dump.Stored > 0f);
            Assert.True(s.GarbageLevel(east[0]) > s.GarbageLevel(west[0]));
        }

        [Fact]
        public void FullLandfill_StopsCollecting()
        {
            var w = TwoStreets(out var west, out _);
            var s = w.Services;
            var small = new ServiceType("dump_small", "Mini-Halde", ServiceKinds.Garbage, 30f, 20f, 100f, "t/Tag", 3f, 0f, 0f, storage: 0.01f);
            var dump = s.TryBuild(small, new Vector2(-18, 0), 90f);
            Assert.NotNull(dump);
            for (int i = 0; i < 48 * 2; i++) s.ForceTick();
            Assert.True(dump.IsFull);
            Assert.Equal(0f, dump.LoadFactor);
            Assert.All(west, b => Assert.Equal(0f, s.Coverage(ServiceKinds.Garbage, b)));
        }

        [Fact]
        public void FireStation_LowersFireRisk()
        {
            var w = TwoStreets(out var west, out _);
            var s = w.Services;
            s.ForceTick();
            float without = s.FireRisk(west[0]);
            Assert.NotNull(s.TryBuild(ServiceCatalog.FireStation, new Vector2(-20, 0), 90f));
            s.ForceTick();
            float with = s.FireRisk(west[0]);
            output.WriteLine($"Brandrisiko ohne {without:0.###}, mit Feuerwache {with:0.###}");
            // Grundrisiko Wohnen 0,25 wird um 80 % gesenkt (liegender Müll kommt unverändert dazu)
            Assert.Equal(without - 0.8f * 0.25f * s.Coverage(ServiceKinds.Fire, west[0]), with, 3);
            Assert.True(s.Coverage(ServiceKinds.Fire, west[0]) > 0.9f);
        }

        // ------------------------------------------------------------ Platzregeln, Abriss

        [Fact]
        public void Placement_Rules()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            var s = w.Services;
            Assert.Equal(ServiceBuildError.OverlapsRoad, s.Plan(ServiceCatalog.PoliceStation, new Vector2(100, 5)).Error);
            Assert.Equal(ServiceBuildError.OutOfWorld, s.Plan(ServiceCatalog.Hospital, w.Max - new Vector2(5, 5)).Error);
            Assert.Equal(ServiceBuildError.NoRoadAccess, s.Plan(ServiceCatalog.Library, new Vector2(100, 120)).Error);

            var school = s.TryBuild(ServiceCatalog.PrimarySchool, new Vector2(100, 20), 180f); // Vorderseite zur Straße (Süden)
            Assert.NotNull(school);
            Assert.Equal(ServiceBuildError.OverlapsFacility, s.Plan(ServiceCatalog.Clinic, new Vector2(110, 25)).Error);
            // Versorger dürfen nicht auf Dienstflächen (Hook im UtilitySystem) – und umgekehrt
            Assert.Equal(UtilityBuildError.OverlapsFacility, w.Utilities.PlanFacility(UtilityCatalog.WindTurbine, new Vector2(110, 25)).Error);
            var wind = w.Utilities.TryBuildFacility(UtilityCatalog.WindTurbine, new Vector2(40, 25));
            Assert.NotNull(wind);
            Assert.Equal(ServiceBuildError.OverlapsFacility, s.Plan(ServiceCatalog.Clinic, new Vector2(45, 25)).Error);
            // Mast auf der Dienstfläche geht nicht, Dienst auf einem Mast auch nicht
            Assert.Equal(UtilityBuildError.PoleOnFacility, w.Utilities.CheckPole(school.Position));
            var line = w.Utilities.TryBuildLine(UtilityKind.Power, new Vector2(300, 100), new Vector2(300, 200));
            Assert.NotNull(line);
            Assert.Equal(ServiceBuildError.OverlapsPole, s.Plan(ServiceCatalog.ParkSmall, line.Poles[1]).Error);

            // Grundstücke auf der Dienstfläche bleiben frei, auch wenn gezont
            w.City.Paint(new Vector2(100, 20), 40f, ZoneType.ResidentialLow);
            for (int i = 0; i < 400; i++) w.Step();
            Assert.True(w.City.LotBlocker(school.Position));
            Assert.DoesNotContain(w.City.Buildings, b => school.Contains(b.Position, 2f));
            Assert.NotEmpty(w.City.Buildings); // daneben wird gebaut
        }

        [Fact]
        public void Build_DisplacesBuildings_AndRoadThroughFacility_IsBlocked()
        {
            var w = TwoStreets(out var west, out _);
            var s = w.Services;
            // Bibliothek auf ein bebautes Grundstück (etwas von der Straße weg, damit sie nicht auf der Fahrbahn steht):
            // Gebäude darunter werden abgerissen
            ServicePlan plan = null;
            Building target = null;
            foreach (var b in west)
            {
                for (float off = 0f; off <= 12f && plan == null; off += 2f)
                {
                    var p = s.Plan(ServiceCatalog.Library, b.Position - b.Front * off, b.Front);
                    if (p.IsValid && p.Displaced.Contains(b)) { plan = p; target = b; }
                }
                if (plan != null) break;
            }
            Assert.NotNull(plan);
            Assert.NotNull(s.Build(plan));
            Assert.Null(w.City.GetBuilding(target.Id));

            var hospital = s.TryBuild(ServiceCatalog.Hospital, new Vector2(-24, 0), 90f);
            Assert.NotNull(hospital);
            int removed = 0;
            s.FacilityRemoved += f => { if (f == hospital) removed++; };
            // Neu (UX-Runde): Straße durchs Dienstgebäude → Plan ungültig mit Namen, Gebäude bleibt
            var plan2 = RoadBuilder.Plan(w.Roads, new Vector2(-24, -120), 8f, null, new Vector2(-24, -5), 8f, RoadCatalog.CityTwoLane);
            Assert.Equal(RoadBuildError.OverlapsStructure, plan2.Error);
            Assert.NotNull(plan2.BlockedBy);
            Assert.Equal(hospital.Id, plan2.BlockedBy.Id);
            Assert.Equal(ServiceCatalog.Hospital.DisplayName, plan2.BlockedBy.Name);
            Assert.Empty(RoadBuilder.Build(w.Roads, plan2));
            Assert.NotNull(s.GetFacility(hospital.Id));
            Assert.Equal(0, removed);
        }

        [Fact]
        public void Terraform_IsLockedUnderServices()
        {
            var w = SimulationWorld.CreateDefault(new WorldSettings { SizeMeters = 1024f });
            var park = w.Services.TryBuild(ServiceCatalog.ParkLarge, new Vector2(200, 200));
            Assert.NotNull(park);
            float h0 = w.Terrain.HeightAt(park.Position);
            w.Terrain.Terraform(new TerraformBrush(TerraformMode.Raise, park.Position, 30f, 3f));
            Assert.Equal(h0, w.Terrain.HeightAt(park.Position), 3);
        }

        // ------------------------------------------------------------ Kosten, Determinismus

        [Fact]
        public void WeeklyUpkeep_SumsCatalogValues()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            var s = w.Services;
            Assert.Equal(0.0, s.WeeklyUpkeep());
            Assert.Equal("Dienste", s.UpkeepName);
            s.TryBuild(ServiceCatalog.PoliceStation, new Vector2(50, -20));
            s.TryBuild(ServiceCatalog.ParkSmall, new Vector2(150, 30));
            Assert.Equal(ServiceCatalog.PoliceStation.UpkeepPerWeek + ServiceCatalog.ParkSmall.UpkeepPerWeek, s.WeeklyUpkeep(), 2);
            Assert.All(ServiceCatalog.All, t => Assert.True(t.Cost > 0f && t.UpkeepPerWeek > 0f && t.Capacity > 0f && t.Range > 0f));
            Assert.Equal(ServiceCatalog.All.Length, ServiceCatalog.All.Select(t => t.Id).Distinct().Count());
            Assert.All(ServiceKinds.All, k => Assert.Contains(ServiceCatalog.All, t => t.Kind == k));
        }

        // ------------------------------------------------------------ Stadtkasse

        static SimulationWorld RealFunds(double funds) =>
            SimulationWorld.CreateDefault(new WorldSettings { UnlimitedFunds = false, StartingFunds = funds });

        [Fact]
        public void Upkeep_IsBookedInCityBudget_AsServices()
        {
            var w = RealFunds(500000.0);
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            var s = w.Services;
            Assert.NotNull(s.TryBuild(ServiceCatalog.PoliceStation, new Vector2(50, -20)));
            Assert.NotNull(s.TryBuild(ServiceCatalog.ParkSmall, new Vector2(150, 30)));
            var e = w.Economy;
            for (int i = 0; i < EconomyRules.DaysPerWeek; i++) e.ForceBillDay();
            var week = e.LastWeek;
            Assert.NotNull(week);
            double expected = ServiceCatalog.PoliceStation.UpkeepPerWeek + ServiceCatalog.ParkSmall.UpkeepPerWeek;
            Assert.Equal(expected, week.Amount(BudgetCategory.Services), 2);
            Assert.Equal(expected, week.ServiceUpkeep.ToDictionary(kv => kv.Key, kv => kv.Value)["Dienste"], 2);
        }

        [Fact]
        public void Construction_CostsMoney_NoMoneyNoBuild_DemolishRefunds25Percent()
        {
            var w = RealFunds(100000.0);
            Road(w, new Vector2(0, 0), new Vector2(200, 0));
            var s = w.Services;
            var e = w.Economy;
            double b0 = e.Balance;
            var police = s.TryBuild(ServiceCatalog.PoliceStation, new Vector2(50, -20));
            Assert.NotNull(police);
            Assert.Equal(b0 - ServiceCatalog.PoliceStation.Cost, e.Balance, 2);
            Assert.Equal(ServiceCatalog.PoliceStation.Cost, e.CurrentWeek.Amount(BudgetCategory.ServiceConstruction), 2);

            // Spieler-Abriss: 25 % zurück; Straße durchs Gebäude: nichts
            b0 = e.Balance;
            Assert.True(s.Demolish(police.Id));
            Assert.Equal(b0 + ServiceCatalog.PoliceStation.Cost * EconomyRules.DemolitionRefundShare, e.Balance, 2);

            // Kein Geld (auch nicht im Kreditrahmen) → Plan ungültig, nichts gebaut, nichts gebucht
            var poor = RealFunds(100000.0);
            Road(poor, new Vector2(0, 0), new Vector2(200, 0));
            poor.Economy.Spend(BudgetCategory.RoadConstruction, poor.Economy.Balance + EconomyRules.CreditLimit - 1000.0); // Kasse leeren
            double before = poor.Economy.Balance;
            var plan = poor.Services.Plan(ServiceCatalog.Hospital, new Vector2(100, -30));
            Assert.Equal(ServiceBuildError.InsufficientFunds, plan.Error);
            Assert.Null(poor.Services.Build(plan));
            Assert.Equal(0, poor.Services.FacilityCount);
            Assert.Equal(before, poor.Economy.Balance, 2);
            // Geometriefehler haben Vorrang vor Geld
            Assert.Equal(ServiceBuildError.OverlapsRoad, poor.Services.Plan(ServiceCatalog.Hospital, new Vector2(100, 5)).Error);
        }

        [Fact]
        public void Determinism_SameInputsSameCoverage()
        {
            string Run()
            {
                var w = SimulationWorld.CreateDefault(seed: 99);
                w.Utilities.AbandonmentEnabled = false;
                Road(w, new Vector2(0, 0), new Vector2(300, 0));
                Road(w, new Vector2(150, 0), new Vector2(150, 200));
                w.Services.TryBuild(ServiceCatalog.PoliceStation, new Vector2(-22, 0));
                w.Services.TryBuild(ServiceCatalog.Landfill, new Vector2(330, -40));
                w.Services.TryBuild(ServiceCatalog.ParkSmall, new Vector2(200, 120));
                w.City.Paint(new Vector2(150, 60), 120f, ZoneType.ResidentialLow);
                for (int i = 0; i < 1500; i++) w.Step();
                var sb = new System.Text.StringBuilder();
                foreach (var b in w.City.Buildings.OrderBy(b => b.Id))
                {
                    sb.Append(b.Id).Append(':');
                    foreach (var k in ServiceKinds.All) sb.Append(w.Services.Coverage(k, b).ToString("R")).Append(',');
                    sb.Append(w.Services.GarbageAt(b).ToString("R")).Append(';');
                }
                return sb.ToString();
            }
            string a = Run(), b = Run();
            Assert.True(a.Length > 50);
            Assert.Equal(a, b);
        }
    }
}
