using System;
using System.Linq;
using System.Numerics;
using System.Text;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Services.Content;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>Abwasser, Wasserqualität, Atomkraftwerk, Medien/Schlagzeilen (G3/G4 erweitert).</summary>
    public class SewageMediaTests
    {
        readonly ITestOutputHelper output;
        public SewageMediaTests(ITestOutputHelper output) => this.output = output;

        // Fluss: Wasserstreifen x 400..440 über die ganze Karte, fließt nach −y (TerrainMap.FlowDirection)
        const float RiverX = 420f;
        static readonly Vector2 OutletPos = new Vector2(385, 15);
        static readonly Vector2 PumpDownstream = new Vector2(385, -250);
        static readonly Vector2 PumpUpstream = new Vector2(385, 300);
        /// <summary>Straßen enden mit Abstand zum Ufer (Trassierung meidet Wasser); Bauwerke am Ufer hängen per Rohr/Kanal an.</summary>
        const float RoadEnd = 340f;

        static UtilityFacility Pump(SimulationWorld w, Vector2 pos)
        {
            var pump = w.Utilities.TryBuildFacility(UtilityCatalog.WaterPump, pos);
            Assert.NotNull(pump);
            var to = pos.Y < 0f && pos.Y > -300f ? new Vector2(RoadEnd, pos.Y) : new Vector2(RoadEnd, 0);
            Assert.NotNull(w.Utilities.TryBuildLine(UtilityKind.Water, pos, to));
            return pump;
        }

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

        static SimulationWorld RiverWorld(WorldSettings settings = null, int seed = 12345)
        {
            var w = SimulationWorld.CreateDefault(settings, seed);
            // flaches Testgelände hat sonst kein Wasser; −1 m, weil Straßen den Boden 0,12 m unter die Fahrbahn legen
            w.Terrain.WaterLevel = -1f;
            w.Terrain.FlattenArea(new Vector2(RiverX, 0), new Vector2(0, 1), 40f, 2040f, -4f);
            Assert.True(w.Terrain.IsWater(new Vector2(RiverX, 0)));
            Assert.False(w.Terrain.IsWater(OutletPos));
            return w;
        }

        /// <summary>Stadt am Fluss: Hauptstraße bis ans Ufer, Uferstraße nach Süden (Pumpe flussab), bewohnt.</summary>
        static SimulationWorld Town(int seed = 12345, WorldSettings settings = null)
        {
            var w = RiverWorld(settings, seed);
            w.Utilities.AbandonmentEnabled = false;
            Road(w, new Vector2(-300, 0), new Vector2(RoadEnd, 0));
            Road(w, new Vector2(RoadEnd, 0), new Vector2(RoadEnd, -300));
            w.City.Paint(new Vector2(-150, 20), 120f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(150, -20), 80f, ZoneType.Industry);
            w.City.Paint(new Vector2(150, 20), 60f, ZoneType.Retail);
            Run(w, SimulationClock.TicksPerDay * 2);
            Assert.True(w.Citizens.Stats.Population > 20);
            return w;
        }

        static Building[] Constructed(SimulationWorld w) => w.City.Buildings.Where(b => b.IsConstructed).OrderBy(b => b.Id).ToArray();

        static UtilityFacility Outlet(SimulationWorld w)
        {
            var outlet = w.Utilities.TryBuildFacility(UtilityCatalog.SewageOutlet, OutletPos);
            Assert.NotNull(outlet);
            SewerMain(w);
            Assert.NotNull(w.Utilities.TryBuildLine(UtilityKind.Sewage, OutletPos, new Vector2(RoadEnd, 0))); // Kanal zum Hauptsammler
            return outlet;
        }

        /// <summary>Abwasser wird selbst verlegt: Hauptsammler unter der Hauptstraße (einmal je Welt).</summary>
        static void SewerMain(SimulationWorld w)
        {
            if (w.Utilities.Lines.Any(l => l.Kind == UtilityKind.Sewage && l.A == new Vector2(-300, 0))) return;
            Assert.NotNull(w.Utilities.TryBuildLine(UtilityKind.Sewage, new Vector2(-300, 0), new Vector2(RoadEnd, 0)));
        }

        /// <summary>Abwasser-Bauwerk südlich der Hauptstraße plus Kanal zum Hauptsammler.</summary>
        static UtilityFacility SewageFacility(SimulationWorld w, UtilityFacilityType type, Vector2 pos)
        {
            SewerMain(w);
            var f = w.Utilities.TryBuildFacility(type, pos);
            if (f == null) return null;
            Assert.NotNull(w.Utilities.TryBuildLine(UtilityKind.Sewage, pos, new Vector2(pos.X, 0)));
            return f;
        }

        [Fact]
        public void Sewage_IsNotCarriedByRoads_HousesNeedSewerInReach()
        {
            var w = Town();
            var u = w.Utilities;
            Assert.NotNull(u.TryBuildFacility(UtilityCatalog.SewagePlant, new Vector2(-50, -28)));
            u.ForceTick();
            // Kläranlage an der Straße, aber ohne Kanal: Straßen führen kein Abwasser mit
            Assert.All(Constructed(w), b => Assert.False(u.IsSupplied(b, UtilityKind.Sewage)));
            Assert.NotNull(u.TryBuildLine(UtilityKind.Sewage, new Vector2(-50, -28), new Vector2(-50, 0)));
            Assert.NotNull(u.TryBuildLine(UtilityKind.Sewage, new Vector2(-50, 0), new Vector2(-150, 0)));
            u.ForceTick();
            var houses = Constructed(w);
            Assert.Contains(houses, b => u.IsSupplied(b, UtilityKind.Sewage));
            foreach (var b in houses)
            {
                bool inReach = u.ConnectedLine(b, UtilityKind.Sewage) != null;
                Assert.Equal(inReach, u.IsSupplied(b, UtilityKind.Sewage));
            }
            Assert.Contains(houses, b => !u.IsSupplied(b, UtilityKind.Sewage)); // weiter östlich: kein Kanal
            Assert.Null(u.ConnectedLine(houses[0], UtilityKind.Water)); // Wasser bleibt (noch) straßengeführt
        }

        static void WaterTicks(SimulationWorld w, int runs)
        {
            w.Utilities.ForceTick();
            for (int i = 0; i < runs; i++) w.WaterQuality.ForceTick();
        }

        static void SetTraits(Citizen c, params (Trait t, float v)[] traits)
        {
            var p = Personality.Neutral;
            foreach (var (t, v) in traits) p[t] = v;
            c.Personality = p;
        }

        // ------------------------------------------------------------ Abwasser-Netz

        [Fact]
        public void Sewage_NetworkBalance_AndStartPhase()
        {
            var w = Town();
            var u = w.Utilities;
            u.ForceTick();
            var houses = Constructed(w);
            // Startphase: ohne Kläranlage/Auslass wird Abwasser nicht verlangt
            Assert.False(u.IsRequired(UtilityKind.Sewage));
            Assert.All(houses, b => Assert.False(u.IsSupplied(b, UtilityKind.Sewage)));

            // Zu kleine Kläranlage → verlangt, aber nicht alle entsorgt → Versorgung nicht erfüllt
            var tiny = new UtilityFacilityType("tiny_sewage", "Mini", UtilityKind.Sewage, 0.05f, 10f, 10f, 0f, 0f);
            Assert.NotNull(SewageFacility(w, tiny, new Vector2(-120, -14)));
            u.ForceTick();
            Assert.True(u.IsRequired(UtilityKind.Sewage));
            Assert.Contains(houses, b => !u.IsSupplied(b, UtilityKind.Sewage) && !u.IsSatisfied(b));

            // Echte Kläranlage: alle entsorgt, Bedarf = 0,9 × Wasserbedarf
            Assert.NotNull(SewageFacility(w, UtilityCatalog.SewagePlant, new Vector2(-50, -28)));
            u.ForceTick();
            houses = Constructed(w); // der Bau hat ggf. Gebäude verdrängt
            Assert.All(houses, b => Assert.True(u.IsSupplied(b, UtilityKind.Sewage), $"{b} ohne Abwasser"));
            // Wasserbedarf wie die Simulation ihn rechnet (seit G5 Industrie nach Rezept über DemandOf)
            float expected = houses.Sum(b => u.DemandOf(b, UtilityKind.Water)) * UtilityCatalog.SewageShareOfWater;
            Assert.Equal(expected, u.TotalDemand(UtilityKind.Sewage), 2);
            Assert.Single(u.Networks(UtilityKind.Sewage).Where(n => n.Facilities > 0));
            w.WaterQuality.ForceTick();
            Assert.Equal(expected, w.WaterQuality.TotalTreated, 2);
            Assert.Equal(0f, w.WaterQuality.TotalUntreated);
        }

        // ------------------------------------------------------------ Wasserqualität

        [Fact]
        public void Outlet_PollutesDownstream_NotUpstream()
        {
            var w = Town();
            var outlet = Outlet(w);
            WaterTicks(w, 300);
            var q = w.WaterQuality;
            Assert.True(q.DischargeOf(outlet) > 0f);
            Assert.True(q.TotalUntreated > 0f);
            float down = q.PollutionAt(new Vector2(RiverX, -250)), up = q.PollutionAt(new Vector2(RiverX, 300));
            output.WriteLine($"Abwasser {q.TotalUntreated:0.0} m³/h · Verschmutzung flussab {down:0.000}, flussauf {up:0.000}");
            Assert.True(down > 0.05f, $"flussab {down}");
            Assert.Equal(0f, up);
            Assert.Equal(0f, q.PollutionAt(new Vector2(0, 0))); // an Land nie
            // Bodenwert-Hook: Ufer flussab sieht verschmutztes Wasser
            Assert.True(q.PollutionNear(new Vector2(385, -250), 80f) > 0.05f);
        }

        [Fact]
        public void PolluteWater_AtPump_LowersHealth_SewagePlantPreventsIt()
        {
            float Health(bool treat, out float drinking)
            {
                var w = Town();
                var u = w.Utilities;
                Pump(w, PumpDownstream);
                Outlet(w);
                if (treat) Assert.NotNull(SewageFacility(w, UtilityCatalog.SewagePlant, new Vector2(-50, -28)));
                WaterTicks(w, 300);
                var home = Constructed(w).First(b => b.Residents > 0 && u.IsSupplied(b, UtilityKind.Water));
                drinking = w.WaterQuality.DrinkingWaterQuality(home);
                output.WriteLine($"treat={treat}: Pumpe {w.WaterQuality.PumpQuality(w.Utilities.Facilities.First(f => f.Type == UtilityCatalog.WaterPump)):0.000}, Trinkwasser {drinking:0.000}, ungeklärt {w.WaterQuality.TotalUntreated:0.0} m³/h");
                for (int i = 0; i < 12; i++) w.Citizens.ForceUpdateNeeds();
                var residents = w.Citizens.Citizens.Where(c => !c.IsCommuterIn && c.Household != null &&
                                                               u.IsSupplied(w.City.GetBuilding(c.Household.HomeId), UtilityKind.Water)).ToList();
                Assert.NotEmpty(residents);
                if (!treat) Assert.Contains(residents, c => c.Memories.Strength(MemoryKind.BadTapWater) > 0f);
                return residents.Average(c => c.Need(NeedKind.Health));
            }

            float dirty = Health(false, out float qDirty);
            float clean = Health(true, out float qClean);
            output.WriteLine($"Trinkwasser ungeklärt {qDirty:0.00} → Gesundheit {dirty:0.000}; mit Kläranlage {qClean:0.00} → {clean:0.000}");
            Assert.True(qDirty < WaterQualitySystem.BadDrinkingWater, $"Qualität {qDirty}");
            Assert.Equal(1f, qClean, 3);
            Assert.True(clean > dirty + 0.02f);
        }

        [Fact]
        public void Pump_Upstream_StaysClean()
        {
            var w = Town();
            var pump = Pump(w, PumpUpstream);
            Assert.NotNull(pump);
            Outlet(w);
            WaterTicks(w, 300);
            Assert.Equal(1f, w.WaterQuality.PumpQuality(pump), 4);
        }

        // ------------------------------------------------------------ Atomkraftwerk

        [Fact]
        public void Nuclear_NeedsWater_WorriesAnxious_AndIncidentShutsDown()
        {
            var w = Town();
            var u = w.Utilities;
            u.IncidentsEnabled = false;
            Assert.Equal(UtilityBuildError.NeedsWater, u.PlanFacility(UtilityCatalog.NuclearPlant, new Vector2(-300, 400)).Error);
            Assert.Equal(UtilityBuildError.OnWater, u.PlanFacility(UtilityCatalog.NuclearPlant, new Vector2(372, 400)).Error); // Ecken im Wasser
            var plant = u.TryBuildFacility(UtilityCatalog.NuclearPlant, new Vector2(352, 400));
            Assert.NotNull(plant);
            Assert.Equal(0f, plant.Type.Pollution);

            var residents = w.Citizens.Citizens.Where(c => !c.IsCommuterIn && c.Household != null && c.Age >= Citizen.AdultAge).ToList();
            Assert.True(residents.Count >= 2);
            var anxious = residents[0];
            var calm = residents[1];
            Assert.True(u.ConcernAt(w.City.GetBuilding(anxious.Household.HomeId).Position) > 0f);
            SetTraits(anxious, (Trait.Anxious, 1f));
            SetTraits(calm, (Trait.Anxious, 0f));
            for (int i = 0; i < 6; i++) w.Citizens.ForceUpdateNeeds();
            Assert.True(anxious.Memories.Strength(MemoryKind.NuclearFear) > 0f);
            Assert.Equal(0f, calm.Memories.Strength(MemoryKind.NuclearFear));
            Assert.Equal(0f, u.ConcernAt(new Vector2(-1000, -1000)));

            // Störfall: abgeschaltet, Leistung 0
            UtilityFacility hit = null;
            u.NuclearIncident += f => hit = f;
            u.ForceIncident(plant.Id, w.Clock.Day);
            u.ForceTick();
            Assert.Same(plant, hit);
            Assert.True(u.IsOffline(plant));
            Assert.Equal(0f, plant.Output);
        }

        [Fact]
        public void Nuclear_Incidents_AreDeterministicAndOff_WithoutPlant()
        {
            var w = RiverWorld();
            w.Utilities.IncidentChancePerDay = 1f; // jeden Tag
            Assert.NotNull(w.Utilities.TryBuildFacility(UtilityCatalog.NuclearPlant, new Vector2(352, 400)));
            Run(w, SimulationClock.TicksPerDay * 2);
            Assert.True(w.Utilities.Incidents >= 1);
            var w2 = RiverWorld();
            w2.Utilities.IncidentChancePerDay = 1f;
            Run(w2, SimulationClock.TicksPerDay * 2);
            Assert.Equal(0, w2.Utilities.Incidents);
        }

        // ------------------------------------------------------------ Medien

        [Fact]
        public void MediaReach_Broadcast_vs_Delivery()
        {
            var w = Town();
            var s = w.Services;
            var houses = Constructed(w).Where(b => b.Residents > 0).ToArray();
            Assert.Equal(0f, w.Citizens.MediaReach(houses[0]));

            // Zeitung: Zustellung übers Straßennetz → braucht Zufahrt; Funk nicht
            var offRoad = new Vector2(-150, 250);
            Assert.Equal(ServiceBuildError.NoRoadAccess, s.Plan(ServiceCatalog.Newspaper, offRoad).Error);
            Assert.True(s.Plan(ServiceCatalog.RadioStation, offRoad).IsValid);

            // Radio weit weg (Funkradius 900 m) erreicht die Stadt nicht
            Assert.NotNull(s.TryBuild(ServiceCatalog.RadioStation, new Vector2(-900, 900)));
            s.ForceTick();
            Assert.All(houses, b => Assert.Equal(0f, s.Coverage(ServiceKinds.Media, b)));

            // Zeitung an der Hauptstraße: alle Häuser an der Straße beliefert
            Assert.NotNull(s.TryBuild(ServiceCatalog.Newspaper, new Vector2(-250, -18)));
            s.ForceTick();
            houses = Constructed(w).Where(b => b.Residents > 0).ToArray(); // Bau verdrängt Gebäude
            Assert.All(houses, b => Assert.True(s.Coverage(ServiceKinds.Media, b) > 0f, $"{b} ohne Zeitung"));
            Assert.True(w.Citizens.MediaReach(houses[0]) > 0f);

            // Radio nahe der Stadt, ohne Straße: per Funk
            var w2 = Town();
            Assert.NotNull(w2.Services.TryBuild(ServiceCatalog.RadioStation, offRoad));
            w2.Services.ForceTick();
            var near = Constructed(w2).Where(b => b.Residents > 0 && Vector2.Distance(b.Position, offRoad) < 300f).ToArray();
            Assert.NotEmpty(near);
            Assert.All(near, b => Assert.True(w2.Services.Coverage(ServiceKinds.Media, b) > 0.9f));
        }

        [Fact]
        public void Headline_FromRealEvents_TaxRaiseAndOpening()
        {
            var w = Town();
            Assert.NotNull(w.Services.TryBuild(ServiceCatalog.Newspaper, new Vector2(-250, -18)));
            w.Services.ForceTick();
            w.Media.Tick(w);
            var opening = w.Media.Recent.FirstOrDefault(h => h.Topic == HeadlineTopic.NewMedia);
            Assert.NotNull(opening);
            Assert.Equal("Stadt-Kurier", opening.Source);

            w.Economy.SetTaxRate(TaxKind.Residential, 0.15f);
            Headline published = null;
            w.Media.Published += h => published = h;
            w.Media.Tick(w);
            Assert.NotNull(published);
            Assert.Equal(HeadlineTopic.TaxRaise, published.Topic);
            Assert.Contains("15 %", published.Title + published.Text);
            Assert.DoesNotContain("{", published.Title + published.Text);
            Assert.True(published.Tone < 0f);
            Assert.True(w.Media.CurrentTone < 0.5f);
            output.WriteLine($"{opening}\n{published}: {published.Text}");

            // Gleiches Ereignis nicht doppelt (Sperrfrist)
            int count = w.Media.PublishedCount;
            w.Media.Tick(w);
            Assert.Equal(count, w.Media.PublishedCount);
        }

        [Fact]
        public void NoMedia_NoHeadlines()
        {
            var w = Town();
            w.Economy.SetTaxRate(TaxKind.Residential, 0.2f);
            Assert.NotNull(w.Services.TryBuild(ServiceCatalog.PrimarySchool, new Vector2(-150, -23)));
            Run(w, 50);
            Assert.Empty(w.Media.Recent);
            Assert.Equal(0, w.Media.PublishedCount);
            Assert.Equal(0f, w.Media.CurrentTone);
        }

        [Fact]
        public void Headline_Templates_HaveTextsForAllTopics()
        {
            Assert.Equal(Enum.GetValues(typeof(HeadlineTopic)).Length, Headlines.TopicCount);
            foreach (HeadlineTopic t in Enum.GetValues(typeof(HeadlineTopic)))
                Assert.True(Headlines.Count(t) >= 2, t.ToString());
        }

        // ------------------------------------------------------------ Kosten

        [Fact]
        public void Costs_AreBookedInCityBudget()
        {
            var w = RiverWorld(new WorldSettings { UnlimitedFunds = false, StartingFunds = 1000000.0 });
            Road(w, new Vector2(-300, 0), new Vector2(RoadEnd, 0));
            var e = w.Economy;
            double b0 = e.Balance;
            Assert.NotNull(w.Utilities.TryBuildFacility(UtilityCatalog.NuclearPlant, new Vector2(352, 400)));
            Assert.NotNull(w.Utilities.TryBuildFacility(UtilityCatalog.SewagePlant, new Vector2(-50, -28)));
            Assert.NotNull(w.Services.TryBuild(ServiceCatalog.Newspaper, new Vector2(-250, -18)));
            double construction = UtilityCatalog.NuclearPlant.Cost + UtilityCatalog.SewagePlant.Cost;
            Assert.Equal(construction, e.CurrentWeek.Amount(BudgetCategory.UtilityConstruction), 2);
            Assert.Equal(ServiceCatalog.Newspaper.Cost, e.CurrentWeek.Amount(BudgetCategory.ServiceConstruction), 2);
            Assert.Equal(b0 - construction - ServiceCatalog.Newspaper.Cost, e.Balance, 2);

            for (int i = 0; i < EconomyRules.DaysPerWeek; i++) e.ForceBillDay();
            var week = e.LastWeek;
            Assert.True(week.Amount(BudgetCategory.UtilityUpkeep) >=
                        (UtilityCatalog.NuclearPlant.UpkeepPerDay + UtilityCatalog.SewagePlant.UpkeepPerDay) * EconomyRules.DaysPerWeek - 1);
            Assert.Equal(ServiceCatalog.Newspaper.UpkeepPerWeek, week.Amount(BudgetCategory.Services), 2);
            Assert.Equal(UtilityBuildError.InsufficientFunds,
                RiverWorld(new WorldSettings { UnlimitedFunds = false, StartingFunds = 1000.0 }).Utilities
                    .PlanFacility(UtilityCatalog.NuclearPlant, new Vector2(352, 400)).Error);
        }

        // ------------------------------------------------------------ Determinismus

        static string Fingerprint(int seed)
        {
            var w = Town(seed);
            var u = w.Utilities;
            u.IncidentChancePerDay = 0.5f;
            Pump(w, PumpDownstream);
            u.TryBuildFacility(UtilityCatalog.NuclearPlant, new Vector2(352, 400));
            Outlet(w);
            w.Services.TryBuild(ServiceCatalog.Newspaper, new Vector2(-250, -18));
            Run(w, SimulationClock.TicksPerDay * 2);
            w.Economy.SetTaxRate(TaxKind.Office, 0.18f);
            Run(w, SimulationClock.TicksPerDay);
            var sb = new StringBuilder();
            foreach (var v in w.WaterQuality.Pollution.RawValues) sb.Append(v.ToString("R")).Append(',');
            foreach (var h in w.Media.Recent) sb.Append(h.Tick).Append(h.Title).Append(h.Text).Append(';');
            foreach (var c in w.Citizens.Citizens) sb.Append(c.Need(NeedKind.Health).ToString("R")).Append(c.Mood.ToString("R"));
            sb.Append(u.Incidents);
            return sb.ToString();
        }

        [Fact]
        public void Deterministic_SameSeedSameResult()
        {
            Assert.Equal(Fingerprint(7), Fingerprint(7));
        }
    }
}
