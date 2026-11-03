using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Citizens.Content;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>Bedürfnismodell: Bedürfnisse, Persönlichkeit, Utility-AI-Entscheidungen, Gedächtnis, Lebensereignisse.</summary>
    public class NeedsTests
    {
        readonly ITestOutputHelper output;
        public NeedsTests(ITestOutputHelper output) => this.output = output;

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

        /// <summary>Wohngebiet mit Industrie und Läden an einer Straße, nach zwei Tagen bewohnt.</summary>
        static SimulationWorld Town(int seed = 12345, int days = 2)
        {
            var w = SimulationWorld.CreateDefault(seed: seed);
            Road(w, new Vector2(-300, 0), new Vector2(300, 0));
            w.City.Paint(new Vector2(-150, 20), 120f, ZoneType.ResidentialLow);
            w.City.Paint(new Vector2(150, -20), 120f, ZoneType.Industry);
            w.City.Paint(new Vector2(150, 20), 60f, ZoneType.Retail);
            Run(w, SimulationClock.TicksPerDay * days);
            Assert.True(w.Citizens.Stats.Population > 20);
            return w;
        }

        static Citizen AnyResident(SimulationWorld w) => w.Citizens.Citizens.First(c => !c.IsCommuterIn && c.Age >= Citizen.AdultAge);

        static Building HomeOf(SimulationWorld w, Citizen c) => w.City.GetBuilding(c.Household.HomeId);

        static void Settle(SimulationWorld w, int rounds = 12)
        {
            for (int i = 0; i < rounds; i++) w.Citizens.ForceUpdateNeeds();
        }

        static void SetTraits(Citizen c, params (Trait t, float v)[] traits)
        {
            var p = Personality.Neutral;
            foreach (var (t, v) in traits) p[t] = v;
            c.Personality = p;
        }

        // ------------------------------------------------------------ Bedürfnisse reagieren

        [Fact]
        public void Noise_LowersCalm_AndSatisfaction()
        {
            var w = Town();
            var c = AnyResident(w);
            SetTraits(c, (Trait.NoiseSensitive, 1f));
            var home = HomeOf(w, c);

            w.Environment.Noise.Clear();
            w.Environment.AirPollution.Clear();
            Settle(w);
            float calmQuiet = c.Need(NeedKind.Calm), satQuiet = c.Satisfaction;

            w.Environment.Noise.StampRadialMax(home.Position, 60f, 1f);
            Settle(w);
            float calmLoud = c.Need(NeedKind.Calm), satLoud = c.Satisfaction;
            output.WriteLine($"Ruhe still {calmQuiet:0.00} → laut {calmLoud:0.00}, Zufriedenheit {satQuiet:0.00} → {satLoud:0.00}");

            Assert.True(calmQuiet > 0.9f, $"still: {calmQuiet}");
            Assert.True(calmLoud < 0.25f, $"laut: {calmLoud}");
            Assert.True(satLoud < satQuiet - 0.05f, "Lärm muss die Zufriedenheit eines Lärmempfindlichen senken");
        }

        [Fact]
        public void PowerOutage_LowersSupply_AndIsRemembered()
        {
            var w = Town();
            Settle(w);
            var c = AnyResident(w);
            Assert.True(c.Need(NeedKind.Supply) > 0.9f, "ohne Versorger gilt Strom als nicht verlangt (Startphase)");

            // Kraftwerk ohne Anschluss: Strom wird verlangt, aber kein Gebäude ist versorgt
            Assert.NotNull(w.Utilities.TryBuildFacility(UtilityCatalog.CoalPlant, new Vector2(-600, 600)));
            w.Utilities.ForceTick();
            Assert.False(w.Utilities.IsSatisfied(HomeOf(w, c)));
            Settle(w, 4);

            output.WriteLine($"Versorgung {c.Need(NeedKind.Supply):0.00}, Erinnerungen: {string.Join(" | ", Texts(w, c))}");
            Assert.True(c.Need(NeedKind.Supply) < 0.2f, $"Versorgung {c.Need(NeedKind.Supply)}");
            Assert.True(c.Memories.Strength(MemoryKind.PowerOutage) > 0.3f);
            Assert.Contains(Texts(w, c), t => !t.Contains("{"));
        }

        [Fact]
        public void ServiceCoverageDelegate_DrivesSafetyEducationHealth()
        {
            var w = Town();
            var seen = new HashSet<string>();
            w.Citizens.ServiceCoverage = (id, b) => { seen.Add(id); return 1f; };
            Settle(w);
            var c = AnyResident(w);
            float safeHigh = c.Need(NeedKind.Safety), eduHigh = c.Need(NeedKind.Education), healthHigh = c.Need(NeedKind.Health);

            w.Citizens.ServiceCoverage = (id, b) => 0f;
            Settle(w);
            output.WriteLine($"Sicherheit {safeHigh:0.00}→{c.Need(NeedKind.Safety):0.00}, Bildung {eduHigh:0.00}→{c.Need(NeedKind.Education):0.00}, Gesundheit {healthHigh:0.00}→{c.Need(NeedKind.Health):0.00}");

            Assert.True(safeHigh > 0.9f && c.Need(NeedKind.Safety) < 0.25f);
            Assert.True(eduHigh > 0.9f && c.Need(NeedKind.Education) < 0.3f);
            Assert.True(healthHigh > c.Need(NeedKind.Health) + 0.25f);
            Assert.All(ServiceIds.All, id => Assert.Contains(id, seen));
        }

        [Fact]
        public void Services_AreBound_NeutralUntilBuilt_ThenRealCoverage_ParksAndPupils()
        {
            var w = Town();
            var c = AnyResident(w);
            var home = HomeOf(w, c);
            Assert.Equal(0.5f, w.Citizens.ServiceCoverage(ServiceIds.Police, home)); // noch keine Polizei: neutral

            var police = w.Services.TryBuild(Services.ServiceCatalog.PoliceStation, new Vector2(-220, -24));
            Assert.NotNull(police);
            var park = w.Services.TryBuild(Services.ServiceCatalog.ParkSmall, new Vector2(-90, -32));
            Assert.NotNull(park);
            w.Services.ForceTick();
            float real = w.Services.Coverage(ServiceIds.Police, home);
            Assert.Equal(real, w.Citizens.ServiceCoverage(ServiceIds.Police, home));
            Assert.Equal(0.5f, w.Citizens.ServiceCoverage(ServiceIds.Health, home)); // Gesundheit weiter neutral

            // Echte Schülerzahlen für die Schulen
            var withKids = w.City.Buildings.First(b => w.Citizens.CountResidents(b.Id, 6, 11) > 0);
            Assert.Equal(w.Citizens.CountResidents(withKids.Id, 6, 11), w.Services.DemandOf(withKids, Services.ServiceKinds.EducationPrimary));

            // Park als echtes Freizeitziel
            var near = w.City.Buildings.Where(b => w.Citizens.HouseholdsIn(b.Id).Count > 0)
                .OrderBy(b => Vector2.Distance(b.Entrance, park.Position)).First();
            var spot = w.Citizens.ParkFinder(near);
            Assert.True(spot.IsValid && spot.FacilityId == park.Id, $"Park für {near} erwartet");
            Run(w, SimulationClock.TicksPerDay * 3);
            output.WriteLine($"Polizei-Abdeckung {real:0.00}, Parkbesuche {w.Citizens.Stats.LeisureRealPark} von {w.Citizens.Stats.LeisurePark}");
            Assert.True(w.Citizens.Stats.LeisureRealPark > 0, "Bürger müssen den echten Park besuchen");
        }

        // ------------------------------------------------------------ Persönlichkeit und Entscheidungen

        [Fact]
        public void Personality_ChangesDecisions()
        {
            var w = Town();
            var c = AnyResident(w);

            SetTraits(c);
            float neutral = w.Citizens.CarProbability(c, 750f);
            Assert.Equal(w.Citizens.CarShare, neutral, 3);   // Durchschnitt = alter Grundanteil

            SetTraits(c, (Trait.Thrifty, 1f), (Trait.NatureLoving, 1f), (Trait.Ambitious, 0f));
            float eco = w.Citizens.CarProbability(c, 750f);
            SetTraits(c, (Trait.Thrifty, 0f), (Trait.NatureLoving, 0f), (Trait.Ambitious, 1f));
            float driver = w.Citizens.CarProbability(c, 750f);
            output.WriteLine($"Auto: Öko {eco:0.00}, neutral {neutral:0.00}, Karriere {driver:0.00}");
            Assert.True(eco < neutral * 0.6f && driver > neutral * 1.4f);

            // Freizeit: Naturfreund will in den Park, Stubenhocker bleibt zu Hause
            var s = new float[3];
            w.Citizens.ServiceCoverage = (id, b) => id == ServiceIds.Park ? 0.9f : 0.5f;
            w.Citizens.ForceUpdateNeeds();
            SetTraits(c, (Trait.NatureLoving, 1f), (Trait.Sociable, 0.8f), (Trait.Adventurous, 0.6f));
            w.Citizens.LeisureScores(c, s);
            Assert.True(s[(int)CitizenSystem.LeisureChoice.Park] > s[(int)CitizenSystem.LeisureChoice.Home], string.Join(", ", s));
            SetTraits(c, (Trait.NatureLoving, 0f), (Trait.Sociable, 0f), (Trait.Adventurous, 0f));
            w.Citizens.LeisureScores(c, s);
            Assert.True(s[(int)CitizenSystem.LeisureChoice.Home] > s[(int)CitizenSystem.LeisureChoice.Park], string.Join(", ", s));

            // Wohnungswahl: Lärmempfindliche bewerten eine laute Wohnung schlechter als Lärmunempfindliche
            var home = HomeOf(w, c);
            w.Environment.Noise.StampRadialMax(home.Position, 60f, 1f);
            w.Citizens.ForceUpdateNeeds();
            foreach (var m in c.Household.Members) SetTraits(m, (Trait.NoiseSensitive, 1f));
            float sensitive = w.Citizens.HomeScore(c.Household, home);
            foreach (var m in c.Household.Members) SetTraits(m, (Trait.NoiseSensitive, 0f));
            float robust = w.Citizens.HomeScore(c.Household, home);
            Assert.True(sensitive < robust - 0.05f, $"{sensitive} vs {robust}");
        }

        [Fact]
        public void WeightedRandom_IsDeterministic_ForSameSeed()
        {
            var scores = new[] { 0.5f, 0.3f, 0.1f };
            int[] Draw(int seed)
            {
                var rng = new SimRandom(seed);
                var r = new int[2000];
                for (int i = 0; i < r.Length; i++) r[i] = UtilityChoice.Choose(scores, 3, 0.2f, rng);
                return r;
            }
            var a = Draw(42);
            Assert.Equal(a, Draw(42));
            Assert.NotEqual(a, Draw(43));
            // Verteilung folgt Softmax, die beste Option ist häufig, aber nicht immer
            for (int k = 0; k < 3; k++)
            {
                float expected = UtilityChoice.Probability(scores, 3, 0.2f, k);
                float actual = a.Count(x => x == k) / (float)a.Length;
                Assert.InRange(actual, expected - 0.04f, expected + 0.04f);
            }
            Assert.Equal(0, UtilityChoice.Choose(scores, 3, 0f, new SimRandom(1)));

            // Ganze Simulation: gleicher Seed → gleiche Bürger, gleiche Entscheidungen
            string Signature(SimulationWorld w)
            {
                var s = w.Citizens.Stats;
                double sat = w.Citizens.Citizens.Sum(c => (double)c.Satisfaction * c.Id);
                return $"{s.Population}/{s.Households}/{s.CarTrips}/{s.OtherTrips}/{s.LeisurePark}/{s.LeisureHome}/{s.LeisureStroll}/" +
                       $"{s.MovedWithinCity}/{s.LeftCity}/{s.Born}/{s.JobChanges}/{sat:0.0000}/" +
                       string.Join(",", w.Citizens.Citizens.Take(30).Select(c => c.FullName + c.Personality.Archetype));
            }
            var w1 = Town(seed: 99, days: 4);
            var w2 = Town(seed: 99, days: 4);
            string sig = Signature(w1);
            output.WriteLine(sig);
            Assert.Equal(sig, Signature(w2));
        }

        // ------------------------------------------------------------ Gedächtnis

        [Fact]
        public void TrafficJamMemory_LowersCarUse_AndHasText()
        {
            var w = Town();
            var c = AnyResident(w);
            SetTraits(c);
            float before = w.Citizens.CarProbability(c, 1500f);
            int street = w.Roads.Segments.First().Id;
            w.Citizens.Remember(c, MemoryKind.TrafficJam, 0.8f, street: street);
            float after = w.Citizens.CarProbability(c, 1500f);
            output.WriteLine($"Auto vor Stau {before:0.00}, danach {after:0.00}: {Texts(w, c).First()}");
            Assert.True(after < before * 0.6f);
            Assert.True(c.Need(NeedKind.Mobility) >= 0f);
            Assert.All(Texts(w, c), t => Assert.DoesNotContain("{", t));

            // Erinnerungen klingen ab: nach einigen Tagen ist der Stau vergessen, die Wahl normalisiert sich
            Run(w, SimulationClock.TicksPerDay * 12);
            Assert.True(c.Memories.Strength(MemoryKind.TrafficJam) < 0.15f || w.Citizens.Get(c.Id) == null);
        }

        [Fact]
        public void Memories_AreDeduplicated_AndCapped()
        {
            var w = Town();
            var c = AnyResident(w);
            int before = c.Memories.Count;
            for (int i = 0; i < 5; i++) w.Citizens.Remember(c, MemoryKind.Noise, 0.3f, street: 7);
            Assert.Equal(Math.Min(Memories.Capacity, before + 1), c.Memories.Count);
            Assert.True(c.Memories.Strength(MemoryKind.Noise) > 0.5f, "Wiederholung frischt auf statt zu doppeln");
            foreach (MemoryKind k in Enum.GetValues(typeof(MemoryKind))) w.Citizens.Remember(c, k, 0.5f, street: 1000 + (int)k);
            Assert.Equal(Memories.Capacity, c.Memories.Count);
        }

        // ------------------------------------------------------------ Beziehungen und Lebensereignisse

        [Fact]
        public void Births_And_MovingOut_Happen()
        {
            var w = Town(days: 2);
            w.Citizens.LifeEventRate = 25f;
            Run(w, SimulationClock.TicksPerDay * 6);
            var s = w.Citizens.Stats;
            output.WriteLine($"Geburten {s.Born}, Paare {s.Partnerships}, Hochzeiten {s.Weddings}, Trennungen {s.Separations}, Auszüge {s.MovedOutOfParents}, krank {s.Ill}");
            foreach (var e in w.Citizens.RecentEvents.Take(12)) output.WriteLine($"  Tag {e.Day}: {e.Text}");

            Assert.True(s.Born > 0, "Geburten erwartet");
            var baby = w.Citizens.Citizens.First(c => c.Age == 0 && c.ParentAId != 0 && w.Citizens.Get(c.Id) != null);
            Assert.Contains(baby.Household.Members, m => m.Id == baby.ParentAId);
            Assert.Contains(w.Citizens.RecentEvents, e => e.Kind == LifeEventKind.Birth && !e.Text.Contains("{"));
            AssertConsistent(w);
        }

        [Fact]
        public void AdultChild_MovesOut_IntoOwnHousehold()
        {
            var w = Town(days: 3);
            var family = w.Citizens.Citizens.Where(c => !c.IsCommuterIn && c.Age < 18 && c.ParentAId != 0).Select(c => c.Household).First();
            var child = family.Members.First(m => m.Age < 18);
            child.Age = 22;
            // Platz schaffen: einen anderen Haushalt wegziehen lassen
            var other = w.City.Buildings.SelectMany(b => w.Citizens.HouseholdsIn(b.Id)).First(h => h.HomeId != family.HomeId);
            w.Citizens.RemoveHousehold(other);

            int before = w.Citizens.Stats.Households;
            Assert.True(w.Citizens.MoveOut(child));
            Assert.NotSame(family, child.Household);
            Assert.Single(child.Household.Members);
            Assert.DoesNotContain(child, family.Members);
            Assert.True(child.Memories.Strength(MemoryKind.OwnFlat) > 0.3f);
            Assert.Contains(family.Members, m => m.Memories.Strength(MemoryKind.ChildMovedOut) > 0.2f);
            w.Citizens.ForceManage();
            Assert.Equal(before, w.Citizens.Stats.Households); // −1 weggezogen, +1 neu gegründet
            AssertConsistent(w);
        }

        [Fact]
        public void Death_Separation_And_Partners_StayConsistent_OverLongRun()
        {
            var w = Town(days: 2);
            w.Citizens.LifeEventRate = 15f;
            Run(w, SimulationClock.TicksPerDay * 12);
            var s = w.Citizens.Stats;
            output.WriteLine($"Einwohner {s.Population}, Geburten {s.Born}, Paare {s.Partnerships}, Trennungen {s.Separations}, " +
                             $"Auszüge {s.MovedOutOfParents}, Umzüge {s.MovedWithinCity}, Wegzüge {s.LeftCity}, Jobwechsel {s.JobChanges}, " +
                             $"Jobverluste {s.JobsLost}, Zufriedenheit {s.AverageSatisfaction:0.00}, Gemüt {s.AverageMood:0.00}");
            Assert.True(s.Partnerships + s.Separations + s.Weddings > 0);
            AssertConsistent(w);

            var c = w.Citizens.Citizens.First(x => !x.IsCommuterIn && x.Age >= 18 && w.Citizens.Get(x.Id) != null);
            Assert.False(string.IsNullOrEmpty(w.Citizens.FamilyStatusText(c)));
            Assert.False(string.IsNullOrEmpty(c.Personality.Describe(c.Gender)));
            Assert.False(string.IsNullOrEmpty(CitizenSystem.Concerns(c)));
        }

        /// <summary>Partner symmetrisch und im selben Haushalt, Haushalte in existierenden Gebäuden, Statistik stimmt.</summary>
        static void AssertConsistent(SimulationWorld w)
        {
            w.Citizens.ForceManage();
            var alive = w.Citizens.Citizens.Where(c => w.Citizens.Get(c.Id) != null).ToList();
            foreach (var c in alive)
            {
                if (c.PartnerId != 0)
                {
                    var p = w.Citizens.Get(c.PartnerId);
                    Assert.NotNull(p);
                    Assert.Equal(c.Id, p.PartnerId);
                    Assert.Same(c.Household, p.Household);
                }
                if (c.IsCommuterIn) continue;
                Assert.Contains(c, c.Household.Members);
                Assert.NotNull(w.City.GetBuilding(c.Household.HomeId));
                Assert.Contains(c.Household, w.Citizens.HouseholdsIn(c.Household.HomeId));
            }
            Assert.Equal(w.Citizens.Stats.Population, alive.Count(c => !c.IsCommuterIn));
            int households = w.City.Buildings.Sum(b => w.Citizens.HouseholdsIn(b.Id).Count);
            Assert.Equal(households, w.Citizens.Stats.Households);
            Assert.All(w.City.Buildings, b => Assert.True(w.Citizens.HouseholdsIn(b.Id).All(h => h.Members.Count > 0)));
        }

        // ------------------------------------------------------------ Zufriedenheit → Wegzug/Zuzug

        [Fact]
        public void Satisfaction_DrivesLeaving()
        {
            var w = Town(days: 6);
            var h = w.City.Buildings.SelectMany(b => w.Citizens.HouseholdsIn(b.Id)).First(x => x.Members.Any(m => m.IsWorkingAge && m.IsEmployed));
            foreach (var m in h.Members) { m.Satisfaction = 0.9f; m.Mood = 0.6f; }
            float happy = w.Citizens.LeaveProbability(h);
            foreach (var m in h.Members) { m.Satisfaction = 0.25f; m.Mood = 0.3f; }
            float unhappy = w.Citizens.LeaveProbability(h);
            output.WriteLine($"Wegzug je Prüfung: zufrieden {happy:0.0000}, unzufrieden {unhappy:0.0000}");
            Assert.True(unhappy > happy * 10f);

            // Ganze Stadt: ohne Dienste und mit Stromausfall (unverbundenes Kraftwerk, Verlassen abgeschaltet)
            // ziehen deutlich mehr Haushalte weg als mit guten Diensten
            int Left(bool good)
            {
                var t = Town(seed: 5, days: 1);
                t.Citizens.ServiceCoverage = (id, b) => good ? 1f : 0f;
                if (!good)
                {
                    t.Utilities.AbandonmentEnabled = false;
                    Assert.NotNull(t.Utilities.TryBuildFacility(UtilityCatalog.CoalPlant, new Vector2(-600, 600)));
                }
                Run(t, SimulationClock.TicksPerDay * 20);
                output.WriteLine($"{(good ? "gut" : "schlecht")}: Zufriedenheit {t.Citizens.Stats.AverageSatisfaction:0.00}, Gemüt {t.Citizens.Stats.AverageMood:0.00}, " +
                                 $"Wegzüge {t.Citizens.Stats.LeftCity}, Zuzug-Sog {t.City.MoveInPull:0}, Einwohner {t.Citizens.Stats.Population}");
                return t.Citizens.Stats.LeftCity;
            }
            int goodLeft = Left(true), badLeft = Left(false);
            Assert.True(badLeft >= goodLeft + 5, $"schlecht {badLeft} vs. gut {goodLeft}");
        }

        [Fact]
        public void Satisfaction_FeedsMoveInPull()
        {
            var w = Town();
            var pop = w.City.Population;
            Assert.True(pop.AverageSatisfaction > 0f, "Bürgersystem meldet die Zufriedenheit an die Stadt");
            // Arbeitsmarkt neutral setzen, damit nur die Zufriedenheit wirkt
            pop.VacantJobs = pop.Unemployed = pop.CommutersIn = pop.CommutersOut = 0;
            pop.AverageSatisfaction = 0.9f;
            w.City.ForceTick();
            float happy = w.City.MoveInPull;
            pop.AverageSatisfaction = 0.3f;
            w.City.ForceTick();
            float unhappy = w.City.MoveInPull;
            pop.AverageSatisfaction = -1f;
            w.City.ForceTick();
            output.WriteLine($"Zuzug-Sog zufrieden {happy:0.0}, unzufrieden {unhappy:0.0}, ohne Angabe {w.City.MoveInPull:0.0}");
            Assert.Equal(45f + 12f, happy, 1);
            Assert.Equal(45f - 12f, unhappy, 1);
            Assert.Equal(45f, w.City.MoveInPull, 1);
        }

        // ------------------------------------------------------------ Inhalte

        [Fact]
        public void Content_IsComplete()
        {
            Assert.Equal(Enum.GetValues(typeof(MemoryKind)).Length, Thoughts.KindCount);
            foreach (MemoryKind k in Enum.GetValues(typeof(MemoryKind))) Assert.True(Thoughts.Count(k) >= 4, k.ToString());
            Assert.Equal(Enum.GetValues(typeof(LifeEventKind)).Length, LifeEvents.All.Length);
            for (int i = 0; i < LifeEvents.All.Length; i++) Assert.Equal((LifeEventKind)i, LifeEvents.All[i].Kind);
            Assert.True(Names.Female.Length >= 80 && Names.Male.Length >= 80 && Names.Last.Length >= 120);
            Assert.True(Archetypes.All.Length >= 12);
            Assert.All(Archetypes.All, a => Assert.Equal(Personality.TraitCount, a.Traits.Length));
            Assert.NotEqual(PlaceNames.Street(1), PlaceNames.Street(2));
        }

        // ------------------------------------------------------------ Leistung (nur Information)

        [Fact]
        public void Performance_20000Citizens_PerGameDay()
        {
            var w = SimulationWorld.CreateDefault();
            for (int i = 0; i < 5; i++) Road(w, new Vector2(-600, -400 + i * 200), new Vector2(600, -400 + i * 200));
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(-300, -380 + i * 200), 250f, ZoneType.ResidentialMedium);
            for (int i = 0; i < 5; i++) w.City.Paint(new Vector2(350, -380 + i * 200), 200f, ZoneType.Industry);
            w.City.Paint(new Vector2(350, -220), 80f, ZoneType.Retail);
            Run(w, SimulationClock.TicksPerDay * 2);
            var housing = w.City.Buildings.Where(b => b.IsConstructed && b.Households > 0).ToList();
            Assert.NotEmpty(housing);
            int k = 0;
            while (w.Citizens.Citizens.Count < 20000) w.Citizens.CreateHousehold(housing[k++ % housing.Count]);
            w.Citizens.ForceManage();
            Run(w, 10);

            var sw = Stopwatch.StartNew();
            Run(w, SimulationClock.TicksPerDay);
            sw.Stop();
            var citizenSw = Stopwatch.StartNew();
            for (int i = 0; i < SimulationClock.TicksPerDay; i++) w.Citizens.ForceUpdateNeedsSliceForTest();
            citizenSw.Stop();
            var s = w.Citizens.Stats;
            output.WriteLine($"{w.Citizens.Citizens.Count} Bürger ({s.Population} Einwohner, {s.Households} Haushalte, {housing.Count} Wohngebäude), " +
                             $"Fahrzeuge {w.Traffic.Vehicles.Count}");
            output.WriteLine($"Ein Spieltag (240 Ticks, alle Systeme): {sw.Elapsed.TotalMilliseconds:0} ms = {sw.Elapsed.TotalMilliseconds / 240:0.00} ms/Tick");
            output.WriteLine($"Nur Bedürfnis-Staffel (240 Ticks): {citizenSw.Elapsed.TotalMilliseconds:0} ms");
            output.WriteLine($"Zufriedenheit {s.AverageSatisfaction:0.00}, Gemüt {s.AverageMood:0.00}, krank {s.Ill}, Geburten {s.Born}, Wegzüge {s.LeftCity}");
        }

        static List<string> Texts(SimulationWorld w, Citizen c)
        {
            var l = new List<string>();
            w.Citizens.MemoryTexts(c, l);
            return l;
        }
    }
}
