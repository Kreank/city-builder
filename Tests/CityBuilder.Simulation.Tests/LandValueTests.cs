using System.Diagnostics;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.LandValue;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>G6: Bodenwert, Ausbaustufen, Gebäudezustand, Grundsteuer, Standortwahl.</summary>
    public class LandValueTests
    {
        readonly ITestOutputHelper output;
        public LandValueTests(ITestOutputHelper output) => this.output = output;

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

        /// <summary>Straße mit wachsendem Wohngebiet (Bürger ziehen ein).</summary>
        static SimulationWorld Town(int seed = 12345, int days = 3)
        {
            var w = SimulationWorld.CreateDefault(null, seed);
            w.Utilities.AbandonmentEnabled = false;
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.City.Paint(new Vector2(-120, 20), 110f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(150, -20), 90f, ZoneType.Industry);
            Run(w, SimulationClock.TicksPerDay * days);
            return w;
        }

        static readonly Vector2 Probe = new Vector2(0, 30);

        // ------------------------------------------------------------ Bodenwert

        [Fact]
        public void Rules_PositiveFactorsRaise_PollutionLowers()
        {
            var plain = new LandValueInputs { RoadAccess = 1f, Neighborhood = -1f };
            float baseline = LandValueRules.Evaluate(plain).Target;
            var park = plain; park.Park = 1f;
            var services = plain; services.Police = services.Fire = services.Health = services.Education = services.Library = 1f;
            var noisy = plain; noisy.Noise = 1f;
            var dirty = plain; dirty.Garbage = 1f;
            var happy = plain; happy.Neighborhood = 1f;
            var water = plain; water.IsWater = true;
            Assert.Equal(LandValueRules.BaseValue + LandValueRules.RoadWeight, baseline, 4);
            Assert.True(LandValueRules.Evaluate(park).Target > baseline);
            Assert.True(LandValueRules.Evaluate(services).Target > baseline + 0.2f);
            Assert.True(LandValueRules.Evaluate(noisy).Target < baseline);
            Assert.True(LandValueRules.Evaluate(dirty).Target < baseline);
            Assert.True(LandValueRules.Evaluate(happy).Target > baseline);
            Assert.Equal(0f, LandValueRules.Evaluate(water).Target);
            // Beiträge summieren sich zum Ziel (ohne Begrenzung)
            var f = LandValueRules.Evaluate(services);
            Assert.Equal(f.Target, f.Contributions().Sum(c => c.contribution), 4);
        }

        [Fact]
        public void Park_RaisesLandValue()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.LandValue.ForceRecalculate();
            float before = w.LandValue.ValueAt(Probe);
            Assert.InRange(before, 0.2f, 0.6f);

            Assert.NotNull(w.Services.TryBuild(ServiceCatalog.ParkSmall, new Vector2(0, 70)));
            w.Services.ForceTick();
            w.LandValue.ForceRecalculate();
            float after = w.LandValue.ValueAt(Probe);
            output.WriteLine($"Bodenwert ohne Park {before:0.000}, mit Park {after:0.000}");
            Assert.True(after > before + 0.05f, $"{before} → {after}");
            Assert.True(w.LandValue.Explain(Probe).Park > 0.05f);
        }

        [Fact]
        public void Services_RaiseLandValue()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.LandValue.ForceRecalculate();
            float before = w.LandValue.ValueAt(Probe);
            Assert.NotNull(w.Services.TryBuild(ServiceCatalog.PoliceStation, new Vector2(-200, -22)));
            Assert.NotNull(w.Services.TryBuild(ServiceCatalog.Clinic, new Vector2(200, -22)));
            w.Services.ForceTick();
            w.LandValue.ForceRecalculate();
            float after = w.LandValue.ValueAt(Probe);
            output.WriteLine($"ohne Dienste {before:0.000}, mit Polizei + Klinik {after:0.000}");
            Assert.True(after > before + 0.06f, $"{before} → {after}");
            Assert.True(w.LandValue.Explain(Probe).Services > 0.06f);
        }

        [Fact]
        public void Noise_And_Garbage_LowerLandValue()
        {
            var w = Town();
            var lv = w.LandValue;
            var house = w.City.Buildings.Where(b => b.IsConstructed && b.Zone == ZoneType.ResidentialLow).OrderBy(b => b.Id).First();
            lv.ForceRecalculate();
            float before = lv.ValueOf(house);

            // Lärm: kräftige Quelle am Haus (bleibt bis zum nächsten Lärm-Durchlauf liegen)
            w.Environment.Noise.StampRadialMax(house.Position, 60f, 1f);
            lv.ForceRecalculate();
            float noisy = lv.ValueOf(house);

            // Müll: überall Müllstau
            w.Environment.Noise.Clear();
            w.Citizens.GarbageLevel = b => 1f;
            lv.ForceRecalculate();
            float dirty = lv.ValueOf(house);
            output.WriteLine($"ruhig {before:0.000} · Lärm {noisy:0.000} · Müll {dirty:0.000}");
            Assert.True(noisy < before - 0.1f, $"Lärm: {before} → {noisy}");
            Assert.True(dirty < before - 0.1f, $"Müll: {before} → {dirty}");
            Assert.True(lv.Explain(house).Garbage < -0.1f);
        }

        [Fact]
        public void Value_IsSmoothed_AndStaggered()
        {
            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            // Nach einem Tag ist jede Zelle einmal berechnet
            Run(w, SimulationClock.TicksPerDay);
            Assert.True(w.LandValue.Cycles >= 1);
            float before = w.LandValue.ValueAt(Probe);
            Assert.True(before > 0f);

            Assert.NotNull(w.Services.TryBuild(ServiceCatalog.ParkLarge, new Vector2(0, 80)));
            w.Services.ForceTick();
            float target = w.LandValue.Explain(Probe).Target;
            Run(w, SimulationClock.TicksPerDay);
            float oneDay = w.LandValue.ValueAt(Probe);
            output.WriteLine($"vorher {before:0.000}, Ziel {target:0.000}, nach 1 Tag {oneDay:0.000}");
            Assert.True(oneDay > before, "steigt");
            Assert.True(oneDay < target - 0.01f, "träge: noch nicht am Ziel");
        }

        // ------------------------------------------------------------ Ausbaustufen

        static BuildingFacts Good() => new BuildingFacts
        {
            LandValue = 0.8f, Satisfaction = 0.8f, Supplied = true, Occupancy = 1f, Skill = 0.8f, Garbage = 0f,
        };

        [Fact]
        public void Upgrade_AfterMinimumDays_WithGoodValues()
        {
            var w = Town();
            var lv = w.LandValue;
            var house = w.City.Buildings.Where(b => b.IsConstructed && b.Zone == ZoneType.ResidentialLow).OrderBy(b => b.Id).First();
            int cells = house.WidthCells * house.DepthCells;
            int households = house.Households;
            int version = w.City.Version;
            Assert.Equal(1, house.Level);

            lv.FactsProvider = b => Good();
            int days = LandValueRules.RequirementFor(house.Zone, 2).Days;
            for (int d = 0; d < days - 1; d++) lv.ForceEvaluateDay();
            Assert.Equal(1, house.Level); // Mindestdauer noch nicht erreicht
            var info = lv.LevelInfo(house);
            Assert.Equal(LevelGap.None, info.Missing);
            Assert.Equal(days - 1, info.ProgressDays);
            Assert.True(info.Progress > 0.5f && info.Progress < 1f);

            lv.ForceEvaluateDay();
            Assert.Equal(2, house.Level);
            Assert.Equal(LandValueRules.HouseholdsFor(house.Zone, cells, 2), house.Households);
            Assert.True(house.Households > households, "höhere Stufe = mehr Wohnungen");
            Assert.True(w.City.Version > version, "Darstellung erfährt die Änderung");
            Assert.True(lv.Upgrades >= 1); // der Hook gilt für alle Gebäude

            // Stufe 3 braucht länger, danach ist Schluss
            for (int d = 0; d < 20; d++) lv.ForceEvaluateDay();
            Assert.Equal(LandValueRules.MaxLevel, house.Level);
            Assert.Equal(LevelGap.MaxLevel, lv.LevelInfo(house).Missing);
        }

        [Fact]
        public void NoUpgrade_WhenSomethingMissing_AndInfoNamesIt()
        {
            var w = Town();
            var lv = w.LandValue;
            var house = w.City.Buildings.Where(b => b.IsConstructed && b.Zone == ZoneType.ResidentialLow).OrderBy(b => b.Id).First();
            lv.FactsProvider = b => { var f = Good(); f.Skill = 0.1f; f.LandValue = 0.2f; return f; };
            for (int d = 0; d < 30; d++) lv.ForceEvaluateDay();
            Assert.Equal(1, house.Level);
            var info = lv.LevelInfo(house);
            Assert.Equal(LevelGap.LandValue | LevelGap.Education, info.Missing);
            Assert.Contains("Bodenwert", info.MissingText);
            Assert.Contains("Bildung", info.MissingText);
            Assert.Equal(0, info.ProgressDays);
        }

        [Fact]
        public void Downgrade_AfterBadDays_ThenDecayDemolishes()
        {
            var w = Town();
            var lv = w.LandValue;
            var house = w.City.Buildings.Where(b => b.IsConstructed && b.Zone == ZoneType.ResidentialLow).OrderBy(b => b.Id).First();
            lv.FactsProvider = b => Good();
            for (int d = 0; d < 5; d++) lv.ForceEvaluateDay();
            Assert.Equal(2, house.Level);

            // Dauerhaft schlecht: kein Strom/Wasser, unzufrieden, schlechte Lage
            lv.FactsProvider = b => new BuildingFacts { LandValue = 0.1f, Satisfaction = 0.2f, Supplied = false, Occupancy = 0.5f, Garbage = 1f };
            for (int d = 0; d < LandValueRules.DeclineDays - 1; d++) lv.ForceEvaluateDay();
            Assert.Equal(2, house.Level);
            Assert.True(lv.LevelInfo(house).Declining);
            lv.ForceEvaluateDay();
            Assert.Equal(1, house.Level);
            Assert.True(lv.Downgrades >= 1);
            Assert.True(house.Condition < 0.5f, $"Zustand {house.Condition}");

            int removed = 0;
            w.City.BuildingRemoved += b => { if (b == house) removed++; };
            for (int d = 0; d < 30 && w.City.GetBuilding(house.Id) != null; d++) lv.ForceEvaluateDay();
            Assert.Null(w.City.GetBuilding(house.Id));
            Assert.Equal(1, removed);
            Assert.True(lv.DecayedBuildings >= 1);
        }

        [Fact]
        public void Condition_AgesSlowly_MaintenanceKeepsGoodHouses()
        {
            var good = Good();
            var neglected = Good(); neglected.Supplied = false;
            var dirty = Good(); dirty.Garbage = 1f;
            var empty = Good(); empty.Occupancy = 0f; empty.Satisfaction = -1f;
            Assert.True(LandValueRules.ConditionDelta(good, 0) > 0f, "gepflegt: Instandhaltung überwiegt");
            Assert.True(LandValueRules.ConditionDelta(neglected, 0) < -0.03f);
            Assert.True(LandValueRules.ConditionDelta(dirty, 0) < -0.02f);
            Assert.True(LandValueRules.ConditionDelta(empty, LandValueRules.EmptyGraceDays + 1) < LandValueRules.ConditionDelta(empty, 0));
            Assert.Equal(-LandValueRules.AgeDecayPerDay, LandValueRules.ConditionDelta(empty, 0), 5);

            // Bürger spüren den Zustand im Wohnbedürfnis/Inspektor (neu = wie vor G6)
            var w = Town();
            var house = w.City.Buildings.Where(b => b.IsConstructed && b.Zone == ZoneType.ResidentialLow).OrderBy(b => b.Id).First();
            float fresh = w.Citizens.BuildingCondition(house);
            house.Condition = 0.2f;
            Assert.True(w.Citizens.BuildingCondition(house) < fresh - 0.2f);
        }

        // ------------------------------------------------------------ Grundsteuer

        [Fact]
        public void PropertyTax_FollowsLandValue()
        {
            Assert.Equal(1f, EconomyRules.PropertyTaxFactor(EconomyRules.PropertyTaxReferenceLandValue), 4);
            Assert.True(EconomyRules.PropertyTaxFactor(0.7f) > 1.9f);
            Assert.Equal(EconomyRules.PropertyTaxMinFactor, EconomyRules.PropertyTaxFactor(0f));

            var w = Town(days: 4);
            var e = w.Economy;
            w.LandValue.ForceRecalculate();
            double expected = w.City.Buildings.Where(b => b.IsConstructed && b.OccupiedHouseholds > 0)
                .Sum(b => b.OccupiedHouseholds * EconomyRules.HouseholdTaxBase * EconomyRules.PropertyTaxFactor(w.LandValue.ValueOf(b)) * 0.10);
            double tax = e.EstimateDay().Amount(BudgetCategory.ResidentialTax);
            int households = w.City.Buildings.Where(b => b.IsConstructed).Sum(b => b.OccupiedHouseholds);
            Assert.True(households > 0);
            Assert.Equal(expected, tax, 2);
            output.WriteLine($"{households} Haushalte: Grundsteuer {tax:0.0} €/Tag (Pauschale wäre {households * 4.0:0.0})");
            // Durchschnittsstadt ≈ bisherige Pauschale (±40 %)
            Assert.InRange(tax / (households * EconomyRules.HouseholdTaxBase * 0.10), 0.6, 1.4);

            // Park + Dienste → höherer Bodenwert → mehr Grundsteuer bei gleicher Belegung
            Assert.NotNull(w.Services.TryBuild(ServiceCatalog.ParkLarge, new Vector2(-120, 75)));
            Assert.NotNull(w.Services.TryBuild(ServiceCatalog.PoliceStation, new Vector2(-280, -22)));
            w.Services.ForceTick();
            w.LandValue.ForceRecalculate();
            double higher = e.EstimateDay().Amount(BudgetCategory.ResidentialTax);
            output.WriteLine($"mit Park + Polizei: {higher:0.0} €/Tag");
            Assert.True(higher > tax * 1.1, $"{tax} → {higher}");
        }

        // ------------------------------------------------------------ Wachstum

        [Fact]
        public void Growth_PrefersHighLandValue()
        {
            var w = SimulationWorld.CreateDefault();
            w.Citizens.LifeEventRate = 0f;
            // Zwei gleiche Straßen; an der Nordstraße liegen Parks und Polizei
            Road(w, new Vector2(-400, 300), new Vector2(400, 300));
            Road(w, new Vector2(-400, -300), new Vector2(400, -300));
            Assert.NotNull(w.Services.TryBuild(ServiceCatalog.ParkLarge, new Vector2(-200, 360)));
            Assert.NotNull(w.Services.TryBuild(ServiceCatalog.ParkLarge, new Vector2(200, 360)));
            Assert.NotNull(w.Services.TryBuild(ServiceCatalog.PoliceStation, new Vector2(0, 278)));
            w.Services.ForceTick();
            w.City.Paint(new Vector2(-200, 320), 160f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(200, 320), 160f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(-200, -280), 160f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(200, -280), 160f, ZoneType.ResidentialLow);
            w.LandValue.ForceRecalculate();
            float north = w.LandValue.ValueAt(new Vector2(0, 320)), south = w.LandValue.ValueAt(new Vector2(0, -280));
            Assert.True(north > south + 0.1f, $"Nord {north}, Süd {south}");

            // Viele Bauentscheidungen zählen: jedes neue Gebäude wird notiert und wieder entfernt
            // (sonst bremst der Leerstand ohne Bürger die Nachfrage nach dem ersten Haus)
            int n = 0, s = 0;
            for (int i = 0; i < 3000 && n + s < 80; i++)
            {
                w.City.ForceTick();
                foreach (var b in w.City.Buildings.ToList())
                {
                    if (b.Position.Y > 0) n++; else s++;
                    w.City.RemoveBuilding(b);
                }
            }
            output.WriteLine($"Bodenwert Nord {north:0.00} / Süd {south:0.00} → neue Gebäude Nord {n}, Süd {s}");
            Assert.True(n + s >= 40, $"zu wenig Bauentscheidungen: {n + s}");
            Assert.True(n > s * 1.4f, $"Nord {n}, Süd {s}");
        }

        [Fact]
        public void Industry_PrefersCheapLand_NearHighway()
        {
            Assert.True(LandValueRules.GrowthPreference(ZoneType.Industry, 0.2f, 1f) > LandValueRules.GrowthPreference(ZoneType.Industry, 0.7f, 0f));
            Assert.True(LandValueRules.GrowthPreference(ZoneType.ResidentialLow, 0.7f, 0f) > LandValueRules.GrowthPreference(ZoneType.ResidentialLow, 0.2f, 1f));

            var w = SimulationWorld.CreateDefault();
            Road(w, new Vector2(-600, 0), new Vector2(600, 0), RoadCatalog.Highway2);
            w.LandValue.ForceRecalculate();
            Assert.True(w.LandValue.HighwayAccessAt(new Vector2(0, 100)) > 0.7f);
            Assert.Equal(0f, w.LandValue.HighwayAccessAt(new Vector2(0, 900)));
        }

        // ------------------------------------------------------------ Determinismus, Leistung

        [Fact]
        public void Deterministic_SameSeedSameResult()
        {
            static (float[] values, int[] levels) RunOnce()
            {
                var w = Town(seed: 99, days: 2);
                w.LandValue.FactsProvider = b => Good();
                Run(w, SimulationClock.TicksPerDay * 6);
                return ((float[])w.LandValue.Layer.RawValues.Clone(),
                    w.City.Buildings.OrderBy(b => b.Id).Select(b => b.Level * 1000 + (int)(b.Condition * 999)).ToArray());
            }
            var a = RunOnce();
            var b = RunOnce();
            Assert.Equal(a.values, b.values);
            Assert.Equal(a.levels, b.levels);
            Assert.Contains(a.levels, l => l >= 2000); // es gab Aufstiege
        }

        [Fact]
        public void Info_SliceCost()
        {
            var w = Town(days: 2);
            var sw = Stopwatch.StartNew();
            w.LandValue.ForceRecalculate();
            sw.Stop();
            output.WriteLine($"Alle {w.LandValue.Layer.RawValues.Length} Zellen: {sw.Elapsed.TotalMilliseconds:0.0} ms " +
                             $"(je Durchlauf ≈ 1/48 davon), {w.City.Buildings.Count()} Gebäude");
            var house = w.City.Buildings.Where(b => b.IsConstructed && b.Households > 0).OrderBy(b => b.Id).First();
            foreach (var (label, c) in w.LandValue.Explain(house).Contributions()) output.WriteLine($"  {label}: {c:+0.000;-0.000}");
            output.WriteLine($"  Bodenwert {w.LandValue.ValueOf(house):0.000}, Stufe {w.LandValue.LevelInfo(house).Level}, fehlt: {w.LandValue.LevelInfo(house).MissingText}");
        }
    }
}
