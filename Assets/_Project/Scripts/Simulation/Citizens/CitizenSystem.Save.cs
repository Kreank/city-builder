using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// Spielstand der Bürger: jeder Bürger mit allen Feldern (Alter, Bildung, Job, Tagesablauf, laufende Fahrt,
    /// Name, Persönlichkeit, Bedürfnisse, Zufriedenheit/Gemüt/Gesundheit, Beziehungen, Erinnerungen), Haushalte
    /// (Wohnung, Mitglieder in Reihenfolge, Einkommen), Belegschaften je Gebäude (Reihenfolge zählt), Autofahrten,
    /// Umfeld-Cache der laufenden Bewertungsrunde, Gebäudelisten des letzten Durchlaufs (werden nur alle 5 Ticks neu
    /// gesammelt), Ereignis-Ticker, Statistik, Zufall und Zähler. Bereits entfernte, noch nicht aufgeräumte Bürger
    /// (<c>gone</c>) bleiben an ihrer Listenposition – die Bedürfnis-Staffel zählt sie mit.
    /// </summary>
    public sealed partial class CitizenSystem : ISaveable, IAfterLoad
    {
        public string SaveId => "citizens";
        /// <summary>v2 (Eigene Assets): Umfeld-Cache mit Freizeit-/Sport-Abdeckung und Wahrzeichen-Stolz.</summary>
        public int SaveVersion => 2;

        public void AfterLoad(SimulationWorld w)
        {
            world = w; // sonst erst ab dem ersten Tick gesetzt (Umfeld: Lärm, Abgas, Versorgung)
            // ÖPNV: Fahrgäste gehören dem TransitSystem (später gelesen) → erst jetzt zuordnen
            rides.Clear();
            foreach (var (citizenId, passengerId) in pendingRides)
                rides[citizenId] = Transit?.FindPassenger(passengerId) ?? CityBuilder.Simulation.Transit.TransitSystem.DetachedPassenger(passengerId, citizenId);
            pendingRides.Clear();
        }

        readonly List<(int citizen, int passenger)> pendingRides = new List<(int, int)>();

        public void Write(SaveWriter w)
        {
            rng.WriteState(w);
            w.Float(CarShare); w.Float(CommuterCarShare); w.Float(MaxVehicleShare);
            w.Int(MaxNewCommutersPerRun); w.Int(MaxJobSearchesPerRun);
            w.Float(LifeEventRate); w.Float(LeaveConsiderRate); w.Float(TransitRealShare);
            w.Int(nextCitizenId); w.Int(nextHouseholdId); w.Int(nextTripId); w.Int(householdCount);
            w.Bool(freeHousingDirty);
            w.Int(needsCursor); w.Int(needsCycle);
            WriteStats(w);

            WriteBuildingList(w, jobBuildings);
            WriteBuildingList(w, shops);
            WriteBuildingList(w, housing);
            WriteBuildingList(w, freeHousing);

            // Bürger in Listenreihenfolge
            w.Int(citizens.Count);
            foreach (var c in citizens) WriteCitizen(w, c);

            // Haushalte: alle erreichbaren (Wohnungslisten zuerst, dann über Bürger), je einmal
            var households = new List<Household>();
            var seen = new HashSet<Household>();
            var homeKeys = SaveWriter.SortedKeys(homes);
            foreach (int key in homeKeys) foreach (var h in homes[key]) if (seen.Add(h)) households.Add(h);
            foreach (var c in citizens) if (c.Household != null && seen.Add(c.Household)) households.Add(c.Household);
            w.Int(households.Count);
            foreach (var h in households)
            {
                w.Int(h.Id);
                w.Int(h.HomeId);
                w.Int(h.MovedInDay);
                w.Float(h.Income);
                w.Float(h.Satisfaction);
                w.Int(h.MemberList.Count);
                foreach (var m in h.MemberList) w.Int(m.Id);
            }
            // Haushalte je Gebäude, Belegschaften, Fahrten, Umfeld (Nachschlage-Tabellen: kanonisch nach Schlüssel;
            // die Reihenfolge der Listen selbst zählt und bleibt erhalten)
            w.Int(homes.Count);
            foreach (int key in homeKeys)
            {
                w.Int(key);
                w.Int(homes[key].Count);
                foreach (var h in homes[key]) w.Int(h.Id);
            }
            w.Int(staff.Count);
            foreach (int key in SaveWriter.SortedKeys(staff))
            {
                w.Int(key);
                w.Int(staff[key].Count);
                foreach (var c in staff[key]) w.Int(c.Id);
            }
            w.IntDictionary(trips, c => w.Int(c.Id));
            w.IntDictionary(rides, p => w.Int(p.Id)); // ÖPNV: Bürger → Fahrgast (Objekt im TransitSystem)

            w.Int(contexts.Count);
            foreach (int key in SaveWriter.SortedKeys(contexts))
            {
                w.Int(key);
                var x = contexts[key];
                w.Int(x.Cycle);
                w.Float(x.Noise); w.Float(x.Air);
                w.Float(x.Police); w.Float(x.Fire); w.Float(x.Health); w.Float(x.Education); w.Float(x.EducationPrimary);
                w.Float(x.EducationSecondary); w.Float(x.Library); w.Float(x.Park); w.Float(x.Garbage); w.Float(x.GarbageLevel);
                w.Bool(x.Supplied); w.Bool(x.PowerOut); w.Bool(x.WaterOut);
                w.Float(x.DrinkingWater); w.Float(x.Nuclear); w.Float(x.Media); w.Bool(x.SewageOut);
                w.Float(x.Leisure); w.Float(x.Sport); w.Float(x.Pride); // v2 Eigene Assets: Freizeit, Sport, Wahrzeichen
            }
            w.IntDictionary(residentSatisfaction, w.Float);
            w.IntDictionary(workerSatisfaction, w.Float);

            // Ereignis-Ticker (Ringpuffer)
            w.Int(recentStart);
            w.Int(recentCount);
            for (int i = 0; i < recentCount; i++)
            {
                var e = recentEvents[(recentStart + i) % MaxRecentEvents];
                w.Name(e.Kind);
                w.Int(e.Day);
                w.Int(e.CitizenId);
                w.String(e.Text);
            }
        }

        static void WriteBuildingList(SaveWriter w, List<Building> list)
        {
            w.Int(list.Count);
            foreach (var b in list) w.Int(b.Id);
        }

        void WriteCitizen(SaveWriter w, Citizen c)
        {
            w.Int(c.Id);
            w.Bool(gone.Contains(c));
            w.Int(c.Age);
            w.Int(c.BirthDayOfYear);
            w.Name(c.Education);
            w.Int(c.Household?.Id ?? 0);
            w.Int(c.CommuterConnectionId);
            w.Int(c.WorkplaceId);
            w.Int(c.OutsideWorkConnectionId);
            w.Int(c.WorkStart);
            w.Int(c.UnemployedDays);
            w.Float(c.ShoppingNeed);
            w.Name(c.Activity);
            WritePlace(w, c.Location);
            w.Name(c.Purpose);
            w.Name(c.Mode);
            WritePlace(w, c.Destination);
            w.Int(c.TripId);
            w.Long(c.TripStartTick);
            w.Long(c.ArriveTick);
            w.Long(c.BusyUntil);
            w.Int(c.LastWorkDay);
            w.Name(c.Gender);
            w.Int(c.FirstNameIndex);
            w.Int(c.LastNameIndex);
            var p = c.Personality;
            w.Int(p.Archetype);
            w.Int(Personality.AllTraits.Length);
            foreach (var t in Personality.AllTraits) { w.Name(t); w.Float(p[t]); }
            w.NamedFloats<NeedKind>(c.NeedValues);
            w.Float(c.Satisfaction);
            w.Float(c.Mood);
            w.Float(c.Health);
            w.Bool(c.IsIll);
            w.Name(c.Relationship);
            w.Int(c.PartnerId);
            w.Int(c.ParentAId);
            w.Int(c.ParentBId);
            w.Bool(c.MemoryStore != null);
            c.MemoryStore?.WriteState(w);
            w.Float(c.TripDistance);
            w.Float(c.CommuteMeters);
            w.Int(c.LastLeisureDay);
            w.Bool(c.LeisureTrip);
            w.Int(c.LeisureParkId);
            w.Int(c.ArrivedDay);
        }

        static void WritePlace(SaveWriter w, Place p)
        {
            w.Int(p.BuildingId);
            w.Int(p.ConnectionNodeId);
        }

        static Place ReadPlace(SaveReader r)
        {
            int b = r.Int(), n = r.Int();
            return b != 0 ? Place.Building(b) : n != 0 ? Place.Outside(n) : Place.None;
        }

        void WriteStats(SaveWriter w)
        {
            var s = Stats;
            w.Int(s.Population); w.Int(s.Households); w.Int(s.Children); w.Int(s.Students); w.Int(s.Adults); w.Int(s.Seniors);
            w.Ints(s.AgeGroups); w.Ints(s.ByEducation);
            w.Int(s.WorkingAge); w.Int(s.Employed); w.Int(s.Unemployed); w.Int(s.WorkingLocal); w.Int(s.CommutersOut); w.Int(s.CommutersIn);
            w.Int(s.AtHome); w.Int(s.AtWork); w.Int(s.Shopping); w.Int(s.Travelling); w.Int(s.TravellingByCar); w.Int(s.TravellingByTransit);
            w.Int(s.TransitTrips); w.Int(s.TransitArrivals); w.Int(s.TransitGaveUp); w.Int(s.TransitStatistical);
            w.Int(s.MovedIn); w.Int(s.MovedOut); w.Int(s.Died); w.Int(s.CarTrips); w.Int(s.OtherTrips); w.Int(s.CarArrivals); w.Int(s.CarTripsFailed);
            w.Int(s.ExternalConnections);
            w.Float(s.AverageSatisfaction); w.Float(s.AverageMood); w.Int(s.Ill); w.Int(s.AtLeisure);
            w.NamedFloats<NeedKind>(s.AverageNeeds);
            w.Int(s.Born); w.Int(s.Partnerships); w.Int(s.Weddings); w.Int(s.Separations); w.Int(s.MovedOutOfParents);
            w.Int(s.MovedWithinCity); w.Int(s.LeftCity); w.Int(s.JobChanges); w.Int(s.JobsLost);
            w.Int(s.LeisureHome); w.Int(s.LeisurePark); w.Int(s.LeisureRealPark); w.Int(s.LeisureStroll);
        }

        void ReadStats(SaveReader r)
        {
            var s = Stats;
            s.Population = r.Int(); s.Households = r.Int(); s.Children = r.Int(); s.Students = r.Int(); s.Adults = r.Int(); s.Seniors = r.Int();
            r.IntsInto(s.AgeGroups); r.IntsInto(s.ByEducation);
            s.WorkingAge = r.Int(); s.Employed = r.Int(); s.Unemployed = r.Int(); s.WorkingLocal = r.Int(); s.CommutersOut = r.Int(); s.CommutersIn = r.Int();
            s.AtHome = r.Int(); s.AtWork = r.Int(); s.Shopping = r.Int(); s.Travelling = r.Int(); s.TravellingByCar = r.Int(); s.TravellingByTransit = r.Int();
            s.TransitTrips = r.Int(); s.TransitArrivals = r.Int(); s.TransitGaveUp = r.Int(); s.TransitStatistical = r.Int();
            s.MovedIn = r.Int(); s.MovedOut = r.Int(); s.Died = r.Int(); s.CarTrips = r.Int(); s.OtherTrips = r.Int(); s.CarArrivals = r.Int(); s.CarTripsFailed = r.Int();
            s.ExternalConnections = r.Int();
            s.AverageSatisfaction = r.Float(); s.AverageMood = r.Float(); s.Ill = r.Int(); s.AtLeisure = r.Int();
            r.NamedFloatsInto<NeedKind>(s.AverageNeeds);
            s.Born = r.Int(); s.Partnerships = r.Int(); s.Weddings = r.Int(); s.Separations = r.Int(); s.MovedOutOfParents = r.Int();
            s.MovedWithinCity = r.Int(); s.LeftCity = r.Int(); s.JobChanges = r.Int(); s.JobsLost = r.Int();
            s.LeisureHome = r.Int(); s.LeisurePark = r.Int(); s.LeisureRealPark = r.Int(); s.LeisureStroll = r.Int();
        }

        public void Read(SaveReader r, int version)
        {
            citizens.Clear(); byId.Clear(); homes.Clear(); staff.Clear(); trips.Clear(); gone.Clear();
            contexts.Clear(); residentSatisfaction.Clear(); workerSatisfaction.Clear();

            rng.ReadState(r);
            CarShare = r.Float(); CommuterCarShare = r.Float(); MaxVehicleShare = r.Float();
            MaxNewCommutersPerRun = r.Int(); MaxJobSearchesPerRun = r.Int();
            LifeEventRate = r.Float(); LeaveConsiderRate = r.Float(); TransitRealShare = r.Float();
            nextCitizenId = r.Int(); nextHouseholdId = r.Int(); nextTripId = r.Int(); householdCount = r.Int();
            freeHousingDirty = r.Bool();
            needsCursor = r.Int(); needsCycle = r.Int();
            ReadStats(r);

            ReadBuildingList(r, jobBuildings);
            ReadBuildingList(r, shops);
            ReadBuildingList(r, housing);
            ReadBuildingList(r, freeHousing);

            int n = r.Count(100);
            var householdOf = new Dictionary<Citizen, int>();
            for (int i = 0; i < n; i++)
            {
                var c = ReadCitizen(r, out bool isGone, out int householdId);
                if (byId.ContainsKey(c.Id)) throw new SaveFormatException($"Bürger {c.Id} doppelt.");
                citizens.Add(c);
                byId[c.Id] = c;
                if (isGone) gone.Add(c);
                if (householdId != 0) householdOf[c] = householdId;
            }
            Citizen FindCitizen(int id) => byId.TryGetValue(id, out var c) ? c : throw new SaveFormatException($"Bürger {id} fehlt.");

            int hn = r.Count(24);
            var households = new Dictionary<int, Household>();
            for (int i = 0; i < hn; i++)
            {
                var h = new Household { Id = r.Int(), HomeId = r.Int(), MovedInDay = r.Int(), Income = r.Float(), Satisfaction = r.Float() };
                int m = r.Count(4);
                for (int k = 0; k < m; k++) h.MemberList.Add(FindCitizen(r.Int()));
                if (households.ContainsKey(h.Id)) throw new SaveFormatException($"Haushalt {h.Id} doppelt.");
                households[h.Id] = h;
            }
            foreach (var kv in householdOf)
                kv.Key.Household = households.TryGetValue(kv.Value, out var h) ? h : throw new SaveFormatException($"Haushalt {kv.Value} fehlt.");

            int homeCount = r.Count(8);
            for (int i = 0; i < homeCount; i++)
            {
                int building = r.Int();
                int m = r.Count(4);
                var list = new List<Household>(m);
                for (int k = 0; k < m; k++)
                    list.Add(households.TryGetValue(r.Int(), out var h) ? h : throw new SaveFormatException("Haushalt einer Wohnung fehlt."));
                homes[building] = list;
            }
            int staffCount = r.Count(8);
            for (int i = 0; i < staffCount; i++)
            {
                int building = r.Int();
                int m = r.Count(4);
                var list = new List<Citizen>(m);
                for (int k = 0; k < m; k++) list.Add(FindCitizen(r.Int()));
                staff[building] = list;
            }
            int tripCount = r.Count(8);
            for (int i = 0; i < tripCount; i++)
            {
                int id = r.Int();
                trips[id] = FindCitizen(r.Int());
            }
            rides.Clear();
            pendingRides.Clear();
            int rideCount = r.Count(8);
            for (int i = 0; i < rideCount; i++) { int cid = r.Int(); pendingRides.Add((cid, r.Int())); }

            int ctx = r.Count(60);
            for (int i = 0; i < ctx; i++)
            {
                int id = r.Int();
                var x = new HomeContext { Cycle = r.Int(), Noise = r.Float(), Air = r.Float() };
                x.Police = r.Float(); x.Fire = r.Float(); x.Health = r.Float(); x.Education = r.Float(); x.EducationPrimary = r.Float();
                x.EducationSecondary = r.Float(); x.Library = r.Float(); x.Park = r.Float(); x.Garbage = r.Float(); x.GarbageLevel = r.Float();
                x.Supplied = r.Bool(); x.PowerOut = r.Bool(); x.WaterOut = r.Bool();
                x.DrinkingWater = r.Float(); x.Nuclear = r.Float(); x.Media = r.Float(); x.SewageOut = r.Bool();
                if (version >= 2) { x.Leisure = r.Float(); x.Sport = r.Float(); x.Pride = r.Float(); } // v1: keine solchen Bauwerke → 0
                contexts[id] = x;
            }
            r.IntDictionaryInto(residentSatisfaction, r.Float, 4);
            r.IntDictionaryInto(workerSatisfaction, r.Float, 4);

            recentStart = r.Int();
            recentCount = r.Int();
            if (recentStart < 0 || recentStart >= MaxRecentEvents || recentCount < 0 || recentCount > MaxRecentEvents)
                throw new SaveFormatException("Ereignis-Ticker ungültig.");
            Array.Clear(recentEvents, 0, recentEvents.Length);
            for (int i = 0; i < recentCount; i++)
            {
                var kind = r.Name<Content.LifeEventKind>();
                int day = r.Int(), id = r.Int();
                recentEvents[(recentStart + i) % MaxRecentEvents] = new LifeEventRecord(kind, day, id, r.String());
            }
        }

        void ReadBuildingList(SaveReader r, List<Building> list)
        {
            list.Clear();
            int n = r.Count(4);
            for (int i = 0; i < n; i++)
            {
                int id = r.Int();
                // Abgerissene Gebäude bleiben bis zum nächsten Durchlauf in der Liste (Abfragen prüfen GetBuilding) –
                // ein Platzhalter mit derselben Id hält Reihenfolge und Verhalten gleich.
                list.Add(city.GetBuilding(id) ?? new Building { Id = id });
            }
        }

        Citizen ReadCitizen(SaveReader r, out bool isGone, out int householdId)
        {
            var c = new Citizen { Id = r.Int() };
            isGone = r.Bool();
            c.Age = r.Int();
            c.BirthDayOfYear = r.Int();
            c.Education = r.Name<Education>();
            householdId = r.Int();
            c.CommuterConnectionId = r.Int();
            c.WorkplaceId = r.Int();
            c.OutsideWorkConnectionId = r.Int();
            c.WorkStart = r.Int();
            c.UnemployedDays = r.Int();
            c.ShoppingNeed = r.Float();
            c.Activity = r.Name<Activity>();
            c.Location = ReadPlace(r);
            c.Purpose = r.Name<TripPurpose>();
            c.Mode = r.Name<TravelMode>();
            c.Destination = ReadPlace(r);
            c.TripId = r.Int();
            c.TripStartTick = r.Long();
            c.ArriveTick = r.Long();
            c.BusyUntil = r.Long();
            c.LastWorkDay = r.Int();
            c.Gender = r.Name<Gender>();
            c.FirstNameIndex = r.Int();
            c.LastNameIndex = r.Int();
            var p = new Personality { Archetype = r.Int() };
            foreach (var t in Personality.AllTraits) p[t] = 0.5f;
            int traits = r.Count(5);
            for (int i = 0; i < traits; i++)
            {
                bool ok = r.TryName<Trait>(out var t);
                float v = r.Float();
                if (ok) p[t] = v;
            }
            c.Personality = p;
            r.NamedFloatsInto<NeedKind>(c.NeedValues);
            c.Satisfaction = r.Float();
            c.Mood = r.Float();
            c.Health = r.Float();
            c.IsIll = r.Bool();
            c.Relationship = r.Name<Relationship>();
            c.PartnerId = r.Int();
            c.ParentAId = r.Int();
            c.ParentBId = r.Int();
            if (r.Bool())
            {
                c.MemoryStore = new MemoryList();
                c.MemoryStore.ReadState(r);
            }
            c.TripDistance = r.Float();
            c.CommuteMeters = r.Float();
            c.LastLeisureDay = r.Int();
            c.LeisureTrip = r.Bool();
            c.LeisureParkId = r.Int();
            c.ArrivedDay = r.Int();
            return c;
        }
    }

    public sealed partial class MemoryList
    {
        internal void WriteState(SaveWriter w)
        {
            w.Byte((byte)Count);
            for (int i = 0; i < Count; i++)
            {
                var m = items[i];
                w.Name(m.Kind);
                w.Byte(m.Template);
                w.Name(m.Zone);
                w.Float(m.Strength);
                w.Int(m.Day);
                w.Int(m.Street);
                w.Int(m.Building);
                w.Int(m.Facility);
                w.Int(m.OtherId);
                w.Int(m.OtherName);
            }
        }

        internal void ReadState(SaveReader r)
        {
            int n = r.Byte();
            if (n > items.Length) throw new SaveFormatException("Zu viele Erinnerungen.");
            int kept = 0;
            for (int i = 0; i < n; i++)
            {
                bool known = r.TryName<MemoryKind>(out var kind);
                var m = new Memory
                {
                    Kind = kind,
                    Template = r.Byte(),
                    Zone = r.Name<ZoneType>(),
                    Strength = r.Float(),
                    Day = r.Int(),
                    Street = r.Int(),
                    Building = r.Int(),
                    Facility = r.Int(),
                    OtherId = r.Int(),
                    OtherName = r.Int(),
                };
                if (known) items[kept++] = m; // Erinnerungsart dieser Version unbekannt → vergessen
            }
            Count = kept;
        }
    }

    public sealed partial class SimRandom
    {
        /// <summary>Zustand schreiben (4 × 32 Bit).</summary>
        public void WriteState(SaveWriter w)
        {
            w.UInt(s0); w.UInt(s1); w.UInt(s2); w.UInt(s3);
        }

        public void ReadState(SaveReader r)
        {
            s0 = r.UInt(); s1 = r.UInt(); s2 = r.UInt(); s3 = r.UInt();
            if ((s0 | s1 | s2 | s3) == 0) throw new SaveFormatException("Zufallszustand ungültig (alles 0).");
        }
    }
}
