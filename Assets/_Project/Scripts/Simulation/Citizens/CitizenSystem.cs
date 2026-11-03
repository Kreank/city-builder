using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens.Content;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Citizens
{
    public sealed class CitizenStats
    {
        /// <summary>Einwohner (ohne Einpendler).</summary>
        public int Population;
        public int Households;
        public int Children;
        public int Students;
        public int Adults;
        public int Seniors;
        /// <summary>Altersgruppen: 0–5, 6–17, 18–29, 30–49, 50–64, 65–79, 80+.</summary>
        public readonly int[] AgeGroups = new int[7];
        public static readonly string[] AgeGroupNames = { "0–5", "6–17", "18–29", "30–49", "50–64", "65–79", "80+" };
        /// <summary>Erwachsene nach Abschluss (Index = <see cref="Education"/>).</summary>
        public readonly int[] ByEducation = new int[4];

        public int WorkingAge;
        public int Employed;
        public int Unemployed;
        public int WorkingLocal;
        public int CommutersOut;
        public int CommutersIn;
        public float UnemploymentRate => WorkingAge > 0 ? Unemployed / (float)WorkingAge : 0f;

        public int AtHome;
        public int AtWork;
        public int Shopping;
        public int Travelling;
        public int TravellingByCar;

        // kumuliert
        public int MovedIn;
        public int MovedOut;
        public int Died;
        public int CarTrips;
        public int OtherTrips;
        public int CarArrivals;
        /// <summary>Autofahrten ohne reguläre Ankunft (abgebrochen/Zeitüberschreitung) – Bürger kam trotzdem an.</summary>
        public int CarTripsFailed;

        public int ExternalConnections;

        // Bedürfnismodell
        /// <summary>Mittlere Zufriedenheit/Gemüt der Einwohner (0..1).</summary>
        public float AverageSatisfaction = 0.6f;
        public float AverageMood = 0.5f;
        public int Ill;
        public int AtLeisure;
        /// <summary>Mittlere Erfüllung je Bedürfnis (Index = <see cref="NeedKind"/>).</summary>
        public readonly float[] AverageNeeds = new float[Needs.Count];
        // kumuliert
        public int Born;
        public int Partnerships;
        public int Weddings;
        public int Separations;
        public int MovedOutOfParents;
        public int MovedWithinCity;
        /// <summary>Haushalte, die sich zum Wegzug entschieden haben (Unzufriedenheit/Arbeitslosigkeit).</summary>
        public int LeftCity;
        public int JobChanges;
        public int JobsLost;
        public int LeisureHome;
        public int LeisurePark;
        /// <summary>Davon in einem echten Park (G4).</summary>
        public int LeisureRealPark;
        public int LeisureStroll;
    }

    /// <summary>
    /// Bürger als einzelne Agenten (G2): Zuzug in freie Wohnungen gemäß Nachfrage, Jobsuche (nächster passender freier Job
    /// im selben Straßennetz), Einpendler/Auspendler über Außenanschlüsse, Altern/Wegzug und ein Tagesablauf nach Uhrzeit
    /// (morgens zur Arbeit, nach der Arbeit bzw. tagsüber einkaufen, dann nach Hause).
    /// Ein Teil der Fahrten fährt als sichtbares Auto im <see cref="TrafficSystem"/> und meldet die Ankunft zurück,
    /// der Rest gilt als zu Fuß/ÖPNV (Platzhalter mit Fahrzeit aus der Luftlinie).
    /// </summary>
    public sealed partial class CitizenSystem : ISimulationSystem
    {
        public int TickInterval => 1;

        /// <summary>Zuzug, Jobsuche, Statistik alle 5 Ticks (30 Spielminuten).</summary>
        public const int ManageInterval = 5;
        /// <summary>Arbeitszeit 8 h.</summary>
        public const int WorkTicks = 80;
        const int ShopTicks = 8;
        const int ShopFrom = 100, ShopUntil = 200;   // 10:00–20:00
        const int WorkStartMin = 60, WorkStartMax = 95; // 6:00–9:30
        /// <summary>Sicherheitsnetz: Autofahrt ohne Rückmeldung gilt nach 3 Tagen als angekommen.</summary>
        const long CarTripTimeout = 3L * SimulationClock.TicksPerDay;
        /// <summary>Platzhalter-Fahrten: Meter je Tick (≈ 15 km/h im Mittel aus Fuß/Rad/ÖPNV).</summary>
        const float OtherMetersPerTick = 1500f;
        /// <summary>Zusätzliche Fahrzeit außerhalb der Karte.</summary>
        const int OutsideTicks = 3;

        /// <summary>Anteil der Fahrten von Einwohnern, die als sichtbares Auto fahren (× <see cref="TrafficSystem.Demand"/>).</summary>
        public float CarShare = 0.35f;
        /// <summary>Anteil der Einpendler-Fahrten mit Auto (× Demand).</summary>
        public float CommuterCarShare = 0.6f;
        /// <summary>Höchstens dieser Anteil von <see cref="TrafficSystem.MaxVehicles"/> sind Bürgerfahrzeuge.</summary>
        public float MaxVehicleShare = 0.7f;
        /// <summary>Höchstens so viele Einpendler kommen je Durchlauf dazu.</summary>
        public int MaxNewCommutersPerRun = 20;
        /// <summary>Jobsuchende je Durchlauf (Rechenzeit begrenzen).</summary>
        public int MaxJobSearchesPerRun = 300;

        readonly CitySystem city;
        readonly TrafficSystem traffic;
        readonly RoadNetwork network;
        readonly SimulationClock clock;
        /// <summary>Eigener geseedeter RNG (deterministisch, plattformunabhängig).</summary>
        readonly SimRandom rng;
        readonly RoadComponents components;

        readonly List<Citizen> citizens = new List<Citizen>();
        readonly Dictionary<int, Citizen> byId = new Dictionary<int, Citizen>();
        readonly Dictionary<int, List<Household>> homes = new Dictionary<int, List<Household>>();
        readonly Dictionary<int, List<Citizen>> staff = new Dictionary<int, List<Citizen>>();
        readonly Dictionary<int, Citizen> trips = new Dictionary<int, Citizen>();
        readonly HashSet<Citizen> gone = new HashSet<Citizen>();
        static readonly List<Household> NoHouseholds = new List<Household>();
        static readonly List<Citizen> NoCitizens = new List<Citizen>();

        // Pro Durchlauf gesammelte Gebäude
        readonly List<Building> jobBuildings = new List<Building>();
        readonly List<Building> shops = new List<Building>();
        readonly List<Building> housing = new List<Building>();
        /// <summary>Wohngebäude mit freien Wohnungen beim letzten Durchlauf (Stichproben für Umzug/Auszug).</summary>
        readonly List<Building> freeHousing = new List<Building>();
        bool freeHousingDirty = true;

        int nextCitizenId = 1, nextHouseholdId = 1, nextTripId = 1;
        int householdCount;

        public CitizenStats Stats { get; } = new CitizenStats();
        public IReadOnlyList<Citizen> Citizens => citizens;
        public RoadComponents Components => components;
        /// <summary>Laufende Autofahrten von Bürgern.</summary>
        public int ActiveCarTrips => trips.Count;

        public event Action<Household> HouseholdMovedIn;
        public event Action<Household> HouseholdMovedOut;

        public CitizenSystem(CitySystem city, TrafficSystem traffic, RoadNetwork network, SimulationClock clock, int seed = 1)
        {
            this.city = city ?? throw new ArgumentNullException(nameof(city));
            this.traffic = traffic ?? throw new ArgumentNullException(nameof(traffic));
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            rng = new SimRandom(seed * 7919 + 17);
            components = new RoadComponents(network);
            city.BuildingRemoved += OnBuildingRemoved;
            traffic.TripEnded += OnTripEnded;
        }

        public Citizen Get(int id) => byId.TryGetValue(id, out var c) && !gone.Contains(c) ? c : null;
        public IReadOnlyList<Household> HouseholdsIn(int buildingId) => homes.TryGetValue(buildingId, out var l) ? l : NoHouseholds;
        public IReadOnlyList<Citizen> WorkersAt(int buildingId) => staff.TryGetValue(buildingId, out var l) ? l : NoCitizens;

        /// <summary>Bürger, zu dessen Autofahrt die Fahrt-Id gehört (<see cref="Vehicle.TripId"/>); null = Durchgangsverkehr/beendet.</summary>
        public Citizen CitizenForTrip(int tripId) =>
            tripId != 0 && trips.TryGetValue(tripId, out var c) && !gone.Contains(c) ? c : null;

        /// <summary>Fahrzeug der laufenden Autofahrt des Bürgers (null = keine Autofahrt bzw. Fahrzeug schon entfernt).</summary>
        public Vehicle VehicleOf(Citizen c)
        {
            if (c == null || c.TripId == 0) return null;
            foreach (var v in traffic.ActiveVehicles)
                if (v.TripId == c.TripId) return v;
            return null;
        }

        /// <summary>Bürger, die sich gerade im Gebäude aufhalten.</summary>
        public int PresentIn(int buildingId)
        {
            int n = 0;
            foreach (var c in citizens)
                if (c.Activity != Activity.Travelling && c.Location.BuildingId == buildingId && !gone.Contains(c)) n++;
            return n;
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld world)
        {
            this.world = world;
            long now = clock.Tick;
            if (now % ManageInterval == 0) Manage(now);
            if (now % SimulationClock.TicksPerDay == 0) Daily();
            UpdateNeedsSlice();
            Routines(now);
            if (now % ManageInterval == 0) UpdateStats();
        }

        /// <summary>Für Tests/Werkzeuge: Zuzug/Jobsuche/Statistik sofort ausführen.</summary>
        public void ForceManage()
        {
            Manage(clock.Tick);
            UpdateStats();
        }

        void Manage(long now)
        {
            if (gone.Count > 0)
            {
                citizens.RemoveAll(c => gone.Contains(c));
                foreach (var c in gone) byId.Remove(c.Id);
                gone.Clear();
            }
            CollectBuildings();
            ValidateConnections();
            MoveIn();
            AssignJobs();
            FillWithCommuters();
        }

        void CollectBuildings()
        {
            jobBuildings.Clear();
            shops.Clear();
            housing.Clear();
            foreach (var b in city.Buildings)
            {
                if (!b.IsConstructed) continue;
                if (b.Jobs > 0) jobBuildings.Add(b);
                if (b.Zone == ZoneType.Retail || b.Zone == ZoneType.Mixed) shops.Add(b);
                if (b.Households > 0) housing.Add(b);
            }
        }

        /// <summary>Wohngebäude mit freien Wohnungen neu sammeln (täglich und bei leerer Stichprobe).</summary>
        void RefreshFreeHousing()
        {
            freeHousingDirty = false;
            freeHousing.Clear();
            foreach (var b in housing)
                if (city.GetBuilding(b.Id) != null && HouseholdsIn(b.Id).Count < b.Households) freeHousing.Add(b);
        }

        // ------------------------------------------------------------ Zuzug / Wegzug

        void MoveIn()
        {
            // Wie in G1: ein Haushalt je Gebäude und Durchlauf, solange die Stadt anzieht
            if (city.MoveInPull <= 5f) return;
            foreach (var b in housing)
            {
                if (HouseholdsIn(b.Id).Count >= b.Households) continue;
                // Bedürfnismodell: Neue meiden Häuser mit unzufriedenen Bewohnern
                float sat = ResidentSatisfaction(b.Id);
                if (sat >= 0f && sat < 0.45f && rng.Chance((0.45f - sat) * 2f)) continue;
                CreateHousehold(b);
            }
        }

        internal Household CreateHousehold(Building home)
        {
            var h = new Household { Id = nextHouseholdId++, HomeId = home.Id, MovedInDay = clock.Day };
            double roll = rng.NextDouble();
            if (roll < 0.28)
            {
                var single = AddMember(h, rng.Next(18, 41));                                  // Single
                if (single.Age >= 26 && rng.Chance(0.15)) single.Relationship = Relationship.Separated;
            }
            else if (roll < 0.50) { int a = rng.Next(22, 61); AddCouple(h, a, Math.Max(18, a + rng.Next(-5, 6))); } // Paar
            else if (roll < 0.82)
            {
                int a = rng.Next(25, 51);                                                     // Familie
                var (p1, p2) = AddCouple(h, a, Math.Max(18, a + rng.Next(-5, 6)), married: 0.8);
                int kids = rng.Next(1, 4);
                for (int i = 0; i < kids; i++) AddChild(h, rng.Next(0, Math.Min(18, a - 17)), p1, p2);
            }
            else if (roll < 0.92)
            {
                int a = rng.Next(22, 51);                                                     // Alleinerziehend
                var parent = AddMember(h, a);
                parent.Relationship = rng.Chance(0.7) ? Relationship.Separated : Relationship.Single;
                int kids = rng.Next(1, 3);
                for (int i = 0; i < kids; i++) AddChild(h, rng.Next(0, Math.Min(18, a - 17)), parent, null);
            }
            else
            {
                int a = rng.Next(65, 86);                                                     // Senioren
                if (rng.NextDouble() < 0.6) AddCouple(h, a, Math.Max(65, a + rng.Next(-4, 5)), married: 0.9);
                else AddMember(h, a).Relationship = rng.Chance(0.6) ? Relationship.Widowed : Relationship.Single;
            }
            if (!homes.TryGetValue(home.Id, out var list)) homes[home.Id] = list = new List<Household>();
            list.Add(h);
            householdCount++;
            Stats.MovedIn += h.MemberList.Count;
            HouseholdMovedIn?.Invoke(h);
            return h;
        }

        Citizen AddMember(Household h, int age)
        {
            var c = NewCitizen(age);
            c.Household = h;
            c.Location = Place.Building(h.HomeId);
            c.Activity = Activity.Home;
            h.MemberList.Add(c);
            return c;
        }

        /// <summary>Paar (meist Frau und Mann, sonst gleichgeschlechtlich), verheiratet mit Wahrscheinlichkeit <paramref name="married"/>.</summary>
        (Citizen, Citizen) AddCouple(Household h, int ageA, int ageB, double married = 0.55)
        {
            var a = AddMember(h, ageA);
            var b = AddMember(h, ageB);
            b.Gender = rng.Chance(0.93) ? (a.Gender == Gender.Female ? Gender.Male : Gender.Female) : a.Gender;
            a.PartnerId = b.Id;
            b.PartnerId = a.Id;
            a.Relationship = b.Relationship = rng.Chance(married) ? Relationship.Married : Relationship.Partnered;
            if (a.Relationship == Relationship.Married && rng.Chance(0.8)) b.LastNameIndex = a.LastNameIndex;
            return (a, b);
        }

        /// <summary>Kind: Persönlichkeit aus den Eltern gemischt, Nachname eines Elternteils.</summary>
        Citizen AddChild(Household h, int age, Citizen p1, Citizen p2)
        {
            var c = AddMember(h, age);
            c.ParentAId = p1?.Id ?? 0;
            c.ParentBId = p2?.Id ?? 0;
            if (p1 != null)
            {
                c.Personality = Personality.Mix(p1.Personality, p2?.Personality ?? Personality.FromArchetype(Archetypes.Roll(rng), rng), rng);
                c.LastNameIndex = p2 != null && rng.Chance(0.5) ? p2.LastNameIndex : p1.LastNameIndex;
            }
            return c;
        }

        Citizen NewCitizen(int age)
        {
            var c = new Citizen
            {
                Id = nextCitizenId++,
                Age = age,
                BirthDayOfYear = rng.Next(SimulationClock.DaysPerYear),
                WorkStart = rng.Next(WorkStartMin, WorkStartMax + 1),
                ShoppingNeed = age >= Citizen.AdultAge ? (float)rng.NextDouble() : 0f,
            };
            c.Education = age >= Citizen.AdultAge ? RollEducation() : age >= 6 ? Education.Basic : Education.None;
            // Bedürfnismodell: Name, Persönlichkeit (Archetyp + Streuung), Gesundheit
            c.Gender = rng.Chance(0.5) ? Gender.Female : Gender.Male;
            c.FirstNameIndex = rng.Next(1000);
            c.LastNameIndex = rng.Next(1000);
            c.Personality = Personality.FromArchetype(Archetypes.Roll(rng), rng);
            c.Health = age < 50 ? 1f : MathF.Max(0.45f, 1f - (age - 50) * 0.012f);
            c.ArrivedDay = clock.Day;
            citizens.Add(c);
            byId[c.Id] = c;
            return c;
        }

        Education RollEducation()
        {
            double r = rng.NextDouble();
            return r < 0.10 ? Education.None : r < 0.45 ? Education.Basic : r < 0.80 ? Education.Secondary : Education.Higher;
        }

        internal void RemoveHousehold(Household h)
        {
            foreach (var m in h.MemberList.ToArray()) RemoveCitizen(m, countAsMovedOut: true);
            if (homes.TryGetValue(h.HomeId, out var list) && list.Remove(h)) { householdCount--; freeHousingDirty = true; }
            HouseholdMovedOut?.Invoke(h);
        }

        void RemoveCitizen(Citizen c, bool countAsMovedOut)
        {
            if (!gone.Add(c)) return;
            Fire(c);
            if (c.PartnerId != 0)
            {
                var partner = Get(c.PartnerId);
                if (partner != null && partner.PartnerId == c.Id) partner.PartnerId = 0;
            }
            if (c.TripId != 0) { trips.Remove(c.TripId); c.TripId = 0; }
            if (c.Household != null)
            {
                c.Household.MemberList.Remove(c);
                if (countAsMovedOut) Stats.MovedOut++;
            }
        }

        // ------------------------------------------------------------ Täglich: Altern, Bedürfnisse, Wegzug

        void Daily()
        {
            int doy = clock.DayOfYear;
            freeHousingDirty = true;
            foreach (var c in citizens.ToArray())
            {
                if (gone.Contains(c)) continue;
                if (!c.IsCommuterIn && c.Age >= Citizen.AdultAge) c.ShoppingNeed = MathF.Min(1.5f, c.ShoppingNeed + 0.34f);
                if (c.IsWorkingAge && !c.IsCommuterIn && !c.IsEmployed) c.UnemployedDays++;

                // Bedürfnismodell: Erinnerungen, Gesundheit, Lebensereignisse
                DailyLife(c);
                if (gone.Contains(c)) continue;

                bool illDeath = c.IsIll && c.Age >= 70 && rng.Chance(0.01 * LifeEventRate);
                if (c.BirthDayOfYear != doy && !illDeath) continue;
                if (c.BirthDayOfYear == doy)
                {
                    c.Age++;
                    if (c.Age == Citizen.AdultAge) c.Education = RollEducation(); // Schulabschluss
                    if (c.Age >= Citizen.RetirementAge && c.IsEmployed)
                    {
                        if (c.IsCommuterIn) { RemoveCitizen(c, false); continue; }
                        int workplace = c.WorkplaceId;
                        Fire(c); // Rente
                        c.OutsideWorkConnectionId = 0;
                        c.CommuteMeters = 0f;
                        OnRetired(c, workplace);
                    }
                }
                if (illDeath || (c.BirthDayOfYear == doy && c.Age >= 80 && rng.NextDouble() < (c.Age - 79) * 0.12))
                {
                    var h = c.Household;
                    OnDeath(c);
                    RemoveCitizen(c, false);
                    Stats.Died++;
                    if (h != null && h.MemberList.Count == 0 && homes.TryGetValue(h.HomeId, out var list) && list.Remove(h))
                    {
                        { householdCount--; freeHousingDirty = true; }
                        HouseholdMovedOut?.Invoke(h);
                    }
                }
            }
            MatchSingles();

            // Einkommen je Haushalt (ohne Steuern)
            foreach (var list in homes.Values)
                foreach (var h in list)
                {
                    float income = 0f;
                    foreach (var m in h.MemberList) income += DailyIncome(m);
                    h.Income = income;
                }

            // Utility-AI: Wegzug/Umzug (ersetzt die frühere feste Regel „alle Erwerbsfähigen > 10 Tage arbeitslos → 8 %/Tag“,
            // die jetzt als starker Faktor in LeaveProbability steckt) und Jobwechsel
            int day = clock.Day;
            HouseholdDecisions(day);
            JobDecisions(day);
        }

        // ------------------------------------------------------------ Arbeit

        static Education MinEducation(ZoneType zone) => zone == ZoneType.Office ? Education.Secondary : Education.None;

        int FreeJobs(Building b) => b.Jobs - WorkersAt(b.Id).Count;

        void Employ(Citizen c, Building b)
        {
            Fire(c);
            if (!staff.TryGetValue(b.Id, out var list)) staff[b.Id] = list = new List<Citizen>();
            list.Add(c);
            c.WorkplaceId = b.Id;
            c.OutsideWorkConnectionId = 0;
            c.UnemployedDays = 0;
            RefreshCommute(c);
        }

        void Fire(Citizen c)
        {
            if (c.WorkplaceId != 0 && staff.TryGetValue(c.WorkplaceId, out var list)) list.Remove(c);
            c.WorkplaceId = 0;
        }

        int ComponentOf(Building b)
        {
            if (b == null) return -1;
            int comp = components.OfSegment(b.FrontSegmentId);
            if (comp >= 0) return comp;
            var seg = network.FindNearestSegment(b.Entrance, 4f);
            return seg == null ? -1 : components.OfNode(seg.Start.Id);
        }

        void AssignJobs()
        {
            // Arbeitslose zuerst, dann Auspendler (die lieber hier arbeiten würden)
            var seekers = new List<Citizen>();
            foreach (var c in citizens)
                if (!gone.Contains(c) && !c.IsCommuterIn && c.IsWorkingAge && !c.IsEmployed) seekers.Add(c);
            foreach (var c in citizens)
                if (!gone.Contains(c) && !c.IsCommuterIn && c.IsWorkingAge && c.WorksOutside) seekers.Add(c);

            int searched = 0;
            foreach (var c in seekers)
            {
                if (searched++ >= MaxJobSearchesPerRun) break;
                var home = city.GetBuilding(c.Household.HomeId);
                if (home == null) continue;
                int comp = ComponentOf(home);
                var job = FindJob(c, home.Entrance, comp, out var displaced);
                if (job != null)
                {
                    if (displaced != null) RemoveCitizen(displaced, false); // Einpendler verliert den Job an einen Einwohner
                    bool wasUnemployed = !c.IsEmployed;
                    Employ(c, job);
                    if (wasUnemployed) Remember(c, MemoryKind.NewJob, 0.6f, building: job.Id);
                    continue;
                }
                if (c.WorksOutside || c.UnemployedDays < 1) continue;
                // Kein Job in der Stadt: außerhalb arbeiten, wenn ein Außenanschluss erreichbar ist
                var ext = NearestExternal(home.Entrance, comp);
                if (ext != null)
                {
                    c.OutsideWorkConnectionId = ext.Id;
                    RefreshCommute(c);
                }
            }
        }

        /// <summary>
        /// Bester passender Job nach <see cref="JobUtility"/> (Arbeitssuchende nehmen den besten, kein Würfeln);
        /// ohne freien Platz ggf. einer, den ein Einpendler besetzt (<paramref name="displaced"/>).
        /// </summary>
        Building FindJob(Citizen c, Vector2 from, int comp, out Citizen displaced)
        {
            displaced = null;
            if (comp < 0) return null;
            Building best = null, bestTaken = null;
            float bestScore = float.MinValue, bestTakenScore = float.MinValue;
            foreach (var b in jobBuildings)
            {
                if (c.Education < MinEducation(b.Zone)) continue;
                bool free = FreeJobs(b) > 0;
                if (!free && (c.WorksOutside || !HasCommuterIn(b))) continue;
                if (ComponentOf(b) != comp) continue;
                float score = JobUtility(c, b, from);
                if (free) { if (score > bestScore) { bestScore = score; best = b; } }
                else if (score > bestTakenScore) { bestTakenScore = score; bestTaken = b; }
            }
            if (best != null) return best;
            if (bestTaken == null) return null;
            foreach (var w in WorkersAt(bestTaken.Id))
                if (w.IsCommuterIn) { displaced = w; break; }
            return bestTaken;
        }

        bool HasCommuterIn(Building b)
        {
            foreach (var w in WorkersAt(b.Id)) if (w.IsCommuterIn) return true;
            return false;
        }

        RoadNode NearestExternal(Vector2 from, int comp)
        {
            RoadNode best = null;
            float bestD = float.MaxValue;
            foreach (var n in traffic.ExternalConnections)
            {
                if (components.OfNode(n.Id) != comp) continue;
                float d = Vector2.Distance(from, n.Position);
                if (d < bestD) { bestD = d; best = n; }
            }
            return best;
        }

        /// <summary>Freie Jobs ohne einheimische Bewerber besetzen Einpendler von außerhalb.</summary>
        void FillWithCommuters()
        {
            if (traffic.ExternalConnections.Count == 0) return;
            foreach (var c in citizens)
                if (!gone.Contains(c) && !c.IsCommuterIn && c.IsWorkingAge && !c.IsEmployed) return; // Einwohner zuerst

            int added = 0;
            foreach (var b in jobBuildings)
            {
                int free = FreeJobs(b);
                if (free <= 0) continue;
                var ext = NearestExternal(b.Entrance, ComponentOf(b));
                if (ext == null) continue;
                for (int i = 0; i < Math.Min(free, 2) && added < MaxNewCommutersPerRun; i++, added++)
                {
                    var c = NewCitizen(rng.Next(20, 61));
                    if (c.Education < MinEducation(b.Zone)) c.Education = rng.NextDouble() < 0.5 ? Education.Secondary : Education.Higher;
                    c.CommuterConnectionId = ext.Id;
                    c.Location = Place.Outside(ext.Id);
                    c.Activity = Activity.Outside;
                    c.ShoppingNeed = 0f;
                    Employ(c, b);
                }
                if (added >= MaxNewCommutersPerRun) break;
            }
        }

        /// <summary>Weggefallene Außenanschlüsse: Einpendler gehen, Auspendler werden arbeitslos.</summary>
        void ValidateConnections()
        {
            foreach (var c in citizens)
            {
                if (gone.Contains(c)) continue;
                if (c.IsCommuterIn && network.GetNode(c.CommuterConnectionId) == null) { RemoveCitizen(c, false); continue; }
                if (c.WorksOutside && network.GetNode(c.OutsideWorkConnectionId) == null) { c.OutsideWorkConnectionId = 0; c.CommuteMeters = 0f; }
                if (c.Activity != Activity.Travelling && c.Location.IsOutside && network.GetNode(c.Location.ConnectionNodeId) == null)
                    TeleportHome(c);
            }
        }

        // ------------------------------------------------------------ Tagesablauf

        void Routines(long now)
        {
            int tod = (int)(now % SimulationClock.TicksPerDay);
            int day = (int)(now / SimulationClock.TicksPerDay);
            for (int i = 0; i < citizens.Count; i++)
            {
                var c = citizens[i];
                if (gone.Contains(c)) continue;
                if (c.Activity == Activity.Travelling)
                {
                    if (now >= c.ArriveTick) Arrive(c, now);
                    continue;
                }
                if (now < c.BusyUntil) continue;

                switch (c.Activity)
                {
                    case Activity.Home:
                    case Activity.Outside:
                        bool workToday = c.IsEmployed && c.LastWorkDay != day && tod >= c.WorkStart && tod < c.WorkStart + 30;
                        if (workToday)
                        {
                            c.LastWorkDay = day;
                            StartTrip(c, TripPurpose.ToWork, c.Workplace, now);
                        }
                        else if (!c.IsCommuterIn && c.Age >= Citizen.AdultAge && c.ShoppingNeed >= 1f && tod >= ShopFrom && tod < ShopUntil &&
                                 !(c.IsEmployed && c.LastWorkDay != day && tod < c.WorkStart + 30))
                        {
                            TryShop(c, now);
                        }
                        // Bedürfnismodell: Freizeit am Nachmittag/Abend (Utility AI: zu Hause / Park / Bummeln)
                        else if (c.Activity == Activity.Home && c.Age >= 12 && c.LastLeisureDay != day && tod >= LeisureFrom && tod < LeisureUntil &&
                                 c.Need(NeedKind.Leisure) < 0.65f && !(c.IsEmployed && c.LastWorkDay != day && tod < c.WorkStart + 30))
                        {
                            TryLeisure(c, now, day);
                        }
                        break;
                    case Activity.Leisure:
                        c.Activity = Activity.Home;
                        c.LeisureParkId = 0;
                        c.BusyUntil = now + 2;
                        break;
                    case Activity.Work:
                        if (!c.IsCommuterIn && c.ShoppingNeed >= 0.6f && tod >= ShopFrom && tod < ShopUntil && TryShop(c, now)) break;
                        StartTrip(c, TripPurpose.ToHome, c.Home, now);
                        break;
                    case Activity.Shopping:
                        StartTrip(c, TripPurpose.ToHome, c.Home, now);
                        break;
                }
            }
        }

        bool TryShop(Citizen c, long now)
        {
            var from = PositionOf(c.Location);
            int comp = ComponentOfPlace(c.Location);
            Building best = null;
            float bestD = float.MaxValue;
            if (from.HasValue && comp >= 0)
                foreach (var b in shops)
                {
                    if (city.GetBuilding(b.Id) == null || ComponentOf(b) != comp) continue;
                    if (ShopHasStock != null && !ShopHasStock(b.Id)) continue; // --- G5 (Hook): leere Läden meiden
                    float d = Vector2.Distance(from.Value, b.Entrance);
                    if (d < bestD) { bestD = d; best = b; }
                }
            if (best == null)
            {
                c.BusyUntil = now + 20; // in 2 h erneut versuchen
                if (c.ShoppingNeed >= 1.2f && rng.Chance(0.2f)) Remember(c, MemoryKind.NoShop, 0.4f);
                return false;
            }
            StartTrip(c, TripPurpose.ToShop, Place.Building(best.Id), now);
            return true;
        }

        void StartTrip(Citizen c, TripPurpose purpose, Place destination, long now)
        {
            if (destination.IsNone) { c.BusyUntil = now + 10; return; }
            c.Purpose = purpose;
            c.Destination = destination;
            c.TripStartTick = now;
            c.Activity = Activity.Travelling;
            c.Mode = TravelMode.Other;
            if (SamePlace(c.Location, destination)) { c.ArriveTick = now; return; }

            var a = PositionOf(c.Location);
            var b = PositionOf(destination);
            if (!a.HasValue || !b.HasValue) { c.ArriveTick = now + 1; Stats.OtherTrips++; return; }

            float dist = Vector2.Distance(a.Value, b.Value);
            c.TripDistance = dist;
            if (c.Age >= Citizen.AdultAge && WantsCar(c, dist) && TryStartCar(c, destination, now)) return;

            int ticks = Math.Max(1, (int)MathF.Ceiling(dist / OtherMetersPerTick));
            if (c.Location.IsOutside || destination.IsOutside) ticks += OutsideTicks;
            c.ArriveTick = now + ticks;
            Stats.OtherTrips++;
        }

        /// <summary>Verkehrsmittelwahl (Utility AI, s. <see cref="CarProbability"/>).</summary>
        bool WantsCar(Citizen c, float distance)
        {
            float p = CarProbability(c, distance);
            if (p <= 0f || rng.NextDouble() >= p) return false;
            return trips.Count < traffic.MaxVehicles * MaxVehicleShare;
        }

        bool TryStartCar(Citizen c, Place destination, long now)
        {
            int ca = ComponentOfPlace(c.Location), cb = ComponentOfPlace(destination);
            if (ca < 0 || ca != cb) return false;
            if (!TryTripEnd(c.Location, out var from) || !TryTripEnd(destination, out var to)) return false;
            int id = nextTripId++;
            if (nextTripId == int.MaxValue) nextTripId = 1;
            var v = traffic.RequestTrip(from, to, VehicleClass.Car, id);
            if (v == null) return false;
            c.Mode = TravelMode.Car;
            c.TripId = id;
            c.ArriveTick = now + CarTripTimeout;
            trips[id] = c;
            Stats.CarTrips++;
            return true;
        }

        void OnTripEnded(Vehicle v, bool arrived)
        {
            // Läuft während des Verkehrs-Ticks: nur Zustand setzen, keine neuen Fahrten
            if (!trips.TryGetValue(v.TripId, out var c)) return;
            trips.Remove(v.TripId);
            c.TripId = 0;
            c.ArriveTick = 0; // kommt im nächsten Tagesablauf-Durchlauf an
            if (arrived) Stats.CarArrivals++; else Stats.CarTripsFailed++;

            // Bedürfnismodell: Stau erkennen (Verkehrszeit deutlich über freier Fahrt bzw. abgebrochen) → Erinnerung
            float expected = c.TripDistance * 1.3f / 11f + 20f;
            float actual = traffic.Time - v.StartTime;
            float strength = !arrived ? 0.6f : actual > expected * 2.2f + 20f ? Math.Clamp((actual / expected - 2f) / 3f, 0.25f, 0.8f) : 0f;
            if (strength > 0f) Remember(c, MemoryKind.TrafficJam, strength, street: v.Lane?.Segment.Id ?? 0,
                                        building: c.WorkplaceId != 0 ? c.WorkplaceId : 0);
        }

        void Arrive(Citizen c, long now)
        {
            if (c.TripId != 0)
            {
                // Zeitüberschreitung: Auto fährt ggf. weiter, der Bürger gilt als angekommen
                trips.Remove(c.TripId);
                c.TripId = 0;
                Stats.CarTripsFailed++;
            }
            c.Mode = TravelMode.None;
            var dest = c.Destination;
            bool valid = dest.IsBuilding ? city.GetBuilding(dest.BuildingId) != null : dest.IsOutside && network.GetNode(dest.ConnectionNodeId) != null;
            if (!valid) { TeleportHome(c); return; }

            c.Location = dest;
            c.Destination = Place.None;
            int tod = (int)(now % SimulationClock.TicksPerDay);
            switch (c.Purpose)
            {
                case TripPurpose.ToWork:
                    c.Activity = Activity.Work;
                    long end = now - tod + c.WorkStart + WorkTicks;
                    c.BusyUntil = end > now ? end : now + 10; // zu spät angekommen: kurz bleiben
                    if (!SamePlace(dest, c.Workplace)) c.BusyUntil = now + 1; // Job unterwegs verloren → gleich wieder heim
                    else if (c.CommuteMeters > 3000f && rng.Chance(0.15f)) Remember(c, MemoryKind.LongCommute, 0.3f, building: c.WorkplaceId);
                    break;
                case TripPurpose.ToShop:
                    c.Activity = Activity.Shopping;
                    c.BusyUntil = now + ShopTicks;
                    // Einkauf für den ganzen Haushalt
                    // --- G5 Warenketten (Hook): Einkauf gelingt nur so weit, wie der Laden die Güter vorrätig hat
                    float bought = Purchase != null && c.Household != null ?Purchase(c.Household, dest.BuildingId) : 1f;
                    if (bought < 0.5f && rng.Chance(0.3f)) Remember(c, MemoryKind.NoShop, 0.35f, building: dest.BuildingId);
                    if (c.Household != null) foreach (var m in c.Household.MemberList) m.ShoppingNeed = ShoppingNeedAfter(m.ShoppingNeed, bought);
                    c.ShoppingNeed = ShoppingNeedAfter(c.ShoppingNeed, bought);
                    // --- Ende G5
                    if (c.LeisureTrip)
                    {
                        // Freizeitbummel: dauert länger, füllt die Freizeit
                        c.LeisureTrip = false;
                        c.BusyUntil = now + ShopTicks * 2;
                        c.NeedValues[(int)NeedKind.Leisure] = MathF.Min(1f, c.NeedValues[(int)NeedKind.Leisure] + 0.35f);
                        if (rng.Chance(0.3f)) Remember(c, MemoryKind.NiceShopping, 0.35f, building: dest.BuildingId);
                    }
                    break;
                default:
                    c.Activity = c.IsCommuterIn ? Activity.Outside : Activity.Home;
                    c.BusyUntil = now + 2;
                    break;
            }
            c.Purpose = TripPurpose.None;
        }

        void TeleportHome(Citizen c)
        {
            if (c.TripId != 0) { trips.Remove(c.TripId); c.TripId = 0; }
            var home = c.Home;
            bool valid = home.IsBuilding ? city.GetBuilding(home.BuildingId) != null : network.GetNode(home.ConnectionNodeId) != null;
            if (!valid)
            {
                if (c.IsCommuterIn) RemoveCitizen(c, false);
                return; // Haushalt ohne Wohnung wird beim Abriss umgesiedelt bzw. zieht weg
            }
            c.Location = home;
            c.Destination = Place.None;
            c.Purpose = TripPurpose.None;
            c.Mode = TravelMode.None;
            c.Activity = c.IsCommuterIn ? Activity.Outside : Activity.Home;
            c.BusyUntil = 0;
            c.LeisureTrip = false;
            c.LeisureParkId = 0;
        }

        // ------------------------------------------------------------ Orte

        static bool SamePlace(Place a, Place b) => a.BuildingId == b.BuildingId && a.ConnectionNodeId == b.ConnectionNodeId;

        Vector2? PositionOf(Place p)
        {
            if (p.IsBuilding) return city.GetBuilding(p.BuildingId)?.Entrance;
            if (p.IsOutside) return network.GetNode(p.ConnectionNodeId)?.Position;
            return null;
        }

        int ComponentOfPlace(Place p)
        {
            if (p.IsBuilding) return ComponentOf(city.GetBuilding(p.BuildingId));
            if (p.IsOutside) return components.OfNode(p.ConnectionNodeId);
            return -1;
        }

        /// <summary>Anschlusspunkt eines Ortes ans Straßennetz (Zufahrt des Gebäudes bzw. Außenanschluss-Knoten).</summary>
        public bool TryTripEnd(Place p, out TripEnd end)
        {
            end = default;
            if (p.IsOutside)
            {
                var node = network.GetNode(p.ConnectionNodeId);
                if (node == null) return false;
                end = TripEnd.AtNode(node);
                return true;
            }
            var b = p.IsBuilding ? city.GetBuilding(p.BuildingId) : null;
            if (b == null) return false;
            var seg = network.GetSegment(b.FrontSegmentId);
            float t = b.FrontT;
            if (seg == null || Vector2.Distance(seg.Evaluate(t), b.Entrance) > 4f)
            {
                // Segment wurde zerteilt/umgebaut und die Stadt hat noch nicht neu zugeordnet
                seg = network.FindNearestSegment(b.Entrance, 4f);
                if (seg == null) return false;
                t = seg.ClosestT(b.Entrance);
            }
            end = TripEnd.AtSegment(seg.Id, t);
            return true;
        }

        // ------------------------------------------------------------ Abriss

        void OnBuildingRemoved(Building b)
        {
            // Bewohner: in eine freie Wohnung umziehen, sonst wegziehen
            if (homes.TryGetValue(b.Id, out var hs))
            {
                homes.Remove(b.Id);
                foreach (var h in hs)
                {
                    var target = FindFreeHome(b.Position, b.Id);
                    if (target == null)
                    {
                        { householdCount--; freeHousingDirty = true; }
                        foreach (var m in h.MemberList.ToArray()) RemoveCitizen(m, countAsMovedOut: true);
                        HouseholdMovedOut?.Invoke(h);
                        continue;
                    }
                    h.HomeId = target.Id;
                    h.MovedInDay = clock.Day;
                    if (!homes.TryGetValue(target.Id, out var list)) homes[target.Id] = list = new List<Household>();
                    list.Add(h);
                    foreach (var m in h.MemberList)
                    {
                        RefreshCommute(m);
                        Remember(m, MemoryKind.Demolished, 0.7f, street: b.FrontSegmentId, building: target.Id);
                    }
                }
            }
            // Beschäftigte: verlieren den Job (Einpendler gehen)
            if (staff.TryGetValue(b.Id, out var ws))
            {
                staff.Remove(b.Id);
                foreach (var w in ws.ToArray())
                {
                    w.WorkplaceId = 0;
                    w.CommuteMeters = 0f;
                    if (w.IsCommuterIn) RemoveCitizen(w, false);
                    else Remember(w, MemoryKind.JobLost, 0.6f, building: 0);
                }
            }
            contexts.Remove(b.Id);
            residentSatisfaction.Remove(b.Id);
            workerSatisfaction.Remove(b.Id);
            // Wer gerade drin ist, ist jetzt zu Hause
            foreach (var c in citizens)
                if (!gone.Contains(c) && c.Activity != Activity.Travelling && c.Location.BuildingId == b.Id) TeleportHome(c);
        }

        Building FindFreeHome(Vector2 near, int except)
        {
            Building best = null;
            float bestD = float.MaxValue;
            foreach (var b in city.Buildings)
            {
                if (b.Id == except || !b.IsConstructed || b.Households <= HouseholdsIn(b.Id).Count) continue;
                float d = Vector2.Distance(near, b.Position);
                if (d < bestD) { bestD = d; best = b; }
            }
            return best;
        }

        // ------------------------------------------------------------ Statistik

        void UpdateStats()
        {
            var s = Stats;
            s.Population = s.Children = s.Students = s.Adults = s.Seniors = 0;
            s.WorkingAge = s.Employed = s.Unemployed = s.WorkingLocal = s.CommutersOut = s.CommutersIn = 0;
            s.AtHome = s.AtWork = s.Shopping = s.Travelling = s.TravellingByCar = 0;
            Array.Clear(s.AgeGroups, 0, s.AgeGroups.Length);
            Array.Clear(s.ByEducation, 0, s.ByEducation.Length);
            Array.Clear(s.AverageNeeds, 0, s.AverageNeeds.Length);
            int unemployedEducated = 0, unserved = 0;
            float satSum = 0f, moodSum = 0f;
            s.Ill = s.AtLeisure = 0;

            foreach (var c in citizens)
            {
                if (gone.Contains(c)) continue;
                switch (c.Activity)
                {
                    case Activity.Leisure: s.AtLeisure++; break;
                    case Activity.Home: s.AtHome++; break;
                    case Activity.Work: s.AtWork++; break;
                    case Activity.Shopping: s.Shopping++; break;
                    case Activity.Travelling:
                        s.Travelling++;
                        if (c.Mode == TravelMode.Car) s.TravellingByCar++;
                        break;
                }
                if (c.IsCommuterIn) { s.CommutersIn++; continue; }

                s.Population++;
                satSum += c.Satisfaction;
                moodSum += c.Mood;
                if (c.IsIll) s.Ill++;
                for (int i = 0; i < Needs.Count; i++) s.AverageNeeds[i] += c.NeedValues[i];
                switch (c.Stage)
                {
                    case LifeStage.Child: s.Children++; break;
                    case LifeStage.Student: s.Students++; break;
                    case LifeStage.Adult: s.Adults++; break;
                    default: s.Seniors++; break;
                }
                int g = c.Age < 6 ? 0 : c.Age < 18 ? 1 : c.Age < 30 ? 2 : c.Age < 50 ? 3 : c.Age < 65 ? 4 : c.Age < 80 ? 5 : 6;
                s.AgeGroups[g]++;
                if (c.Age >= Citizen.AdultAge) s.ByEducation[(int)c.Education]++;
                if (c.ShoppingNeed >= 1.2f) unserved++;

                if (!c.IsWorkingAge) continue;
                s.WorkingAge++;
                if (c.WorkplaceId != 0) { s.Employed++; s.WorkingLocal++; }
                else if (c.WorksOutside) { s.Employed++; s.CommutersOut++; }
                else
                {
                    s.Unemployed++;
                    if (c.Education >= Education.Secondary) unemployedEducated++;
                }
            }
            s.Households = householdCount;
            s.ExternalConnections = traffic.ExternalConnections.Count;
            s.AverageSatisfaction = s.Population > 0 ? satSum / s.Population : 0.6f;
            s.AverageMood = s.Population > 0 ? moodSum / s.Population : 0.5f;
            for (int i = 0; i < Needs.Count; i++) s.AverageNeeds[i] = s.Population > 0 ? s.AverageNeeds[i] / s.Population : 0.6f;

            // Belegung an die Gebäude, Arbeitsmarkt an die Stadt (Nachfrage); Zufriedenheit je Gebäude
            int vacant = 0, freeHomes = 0;
            foreach (var b in city.Buildings)
            {
                var hs = HouseholdsIn(b.Id);
                int residents = 0;
                float hsat = 0f;
                foreach (var h in hs)
                {
                    float sum = 0f;
                    foreach (var m in h.MemberList) sum += m.Satisfaction;
                    h.Satisfaction = h.MemberList.Count > 0 ? sum / h.MemberList.Count : 0.6f;
                    hsat += sum;
                    residents += h.MemberList.Count;
                }
                if (residents > 0) residentSatisfaction[b.Id] = hsat / residents; else residentSatisfaction.Remove(b.Id);
                var ws = WorkersAt(b.Id);
                if (ws.Count > 0)
                {
                    float wsat = 0f;
                    foreach (var w in ws) wsat += 0.6f * w.NeedValues[(int)NeedKind.Work] + 0.4f * w.NeedValues[(int)NeedKind.Mobility];
                    workerSatisfaction[b.Id] = wsat / ws.Count;
                }
                else workerSatisfaction.Remove(b.Id);
                b.OccupiedHouseholds = hs.Count;
                b.Residents = residents;
                b.Workers = ws.Count;
                if (!b.IsConstructed) continue;
                vacant += Math.Max(0, b.Jobs - b.Workers);
                freeHomes += Math.Max(0, b.Households - b.OccupiedHouseholds);
            }
            var p = city.Population;
            p.Residents = s.Population;
            p.WorkingAge = s.WorkingAge;
            p.Unemployed = s.Unemployed;
            p.UnemployedEducated = unemployedEducated;
            p.CommutersOut = s.CommutersOut;
            p.CommutersIn = s.CommutersIn;
            p.VacantJobs = vacant;
            p.FreeHomes = freeHomes;
            p.UnservedShoppers = unserved;
            p.AverageSatisfaction = s.Population > 0 ? s.AverageSatisfaction : -1f;
        }
    }
}
