using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens.Content;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// Utility-AI-Entscheidungen: Optionen bekommen Nutzwerte aus Bedürfnissen, Persönlichkeit und Erinnerungen,
    /// gewählt wird per Softmax mit der Temperatur der Persönlichkeit (<see cref="UtilityChoice"/>):
    /// Freizeitziel, Verkehrsmittel, Umzug in der Stadt, Wegzug, Jobwechsel (und Jobsuche).
    /// </summary>
    public sealed partial class CitizenSystem
    {
        /// <summary>Freizeit 15:00–21:00.</summary>
        const int LeisureFrom = 150, LeisureUntil = 210;
        /// <summary>Haushalte prüfen Umzug/Wegzug alle so viele Tage (gestaffelt nach Id).</summary>
        public const int HouseholdDecisionDays = 5;
        /// <summary>Beschäftigte prüfen einen Jobwechsel alle so viele Tage (gestaffelt nach Id).</summary>
        public const int JobDecisionDays = 10;
        /// <summary>Kandidaten (Wohnungen/Jobs) je Entscheidung – Stichprobe statt alle (Rechenzeit).</summary>
        const int MaxCandidates = 6;
        /// <summary>Grundrate: Anteil der Haushalte, die bei einer Prüfung überhaupt über Wegzug nachdenken.</summary>
        public float LeaveConsiderRate = 0.25f;

        readonly float[] scores = new float[MaxCandidates + 2];
        readonly Building[] candidates = new Building[MaxCandidates + 2];
        readonly List<Household> householdScratch = new List<Household>();

        public enum LeisureChoice : byte { Home, Park, Stroll }

        // ------------------------------------------------------------ Freizeit

        /// <summary>Nutzwerte der Freizeitziele (Index = <see cref="LeisureChoice"/>); Bummeln nur mit erreichbarem Laden.</summary>
        public void LeisureScores(Citizen c, float[] into, bool shopReachable = true)
        {
            var p = c.Personality;
            var m = c.Memories;
            var home = c.Household != null ? city.GetBuilding(c.Household.HomeId) : null;
            float park = 0.5f, air = 0f;
            if (home != null) { var ctx = Context(home); park = ctx.Park; air = ctx.Air; }
            into[0] = 0.35f + 0.3f * (1f - p.Sociable) + 0.15f * (1f - p.Adventurous) + (c.IsIll ? 0.5f : 0f) + (c.Age >= 75 ? 0.1f : 0f);
            into[1] = 0.1f + 0.6f * park + 0.35f * p.NatureLoving + 0.3f * MathF.Min(1f, m.Strength(MemoryKind.NicePark))
                      - 0.4f * air - 0.2f * MathF.Min(1f, m.Strength(MemoryKind.FeelsUnsafe));
            into[2] = shopReachable && c.Age >= Citizen.AdultAge
                ? 0.1f + 0.35f * p.Sociable + 0.25f * (1f - p.Thrifty) + 0.2f * MathF.Min(1f, m.Strength(MemoryKind.NiceShopping))
                : -10f;
        }

        bool TryLeisure(Citizen c, long now, int day)
        {
            c.LastLeisureDay = day;
            LeisureScores(c, scores, shops.Count > 0);
            int choice = UtilityChoice.Choose(scores, 3, c.Personality.Temperature, rng);
            var n = c.NeedValues;
            var home = c.Household != null ? city.GetBuilding(c.Household.HomeId) : null;
            switch ((LeisureChoice)choice)
            {
                case LeisureChoice.Park:
                {
                    float park = home != null ? Context(home).Park : 0.5f;
                    c.Activity = Activity.Leisure;
                    c.BusyUntil = now + 10 + rng.Next(11);
                    n[(int)NeedKind.Leisure] = MathF.Min(1f, n[(int)NeedKind.Leisure] + 0.25f + 0.5f * park);
                    if (park >= 0.6f && rng.Chance(0.35f + 0.3f * c.Personality.NatureLoving))
                        Remember(c, MemoryKind.NicePark, 0.3f + 0.4f * park, building: home?.Id ?? 0);
                    Stats.LeisurePark++;
                    return true;
                }
                case LeisureChoice.Stroll:
                    c.LeisureTrip = true;
                    if (TryShop(c, now)) { Stats.LeisureStroll++; return true; }
                    c.LeisureTrip = false;
                    goto default;
                default:
                    c.BusyUntil = now + 15;
                    n[(int)NeedKind.Leisure] = MathF.Min(1f, n[(int)NeedKind.Leisure] + 0.2f);
                    if (rng.Chance(0.12f)) Remember(c, MemoryKind.QuietEvening, 0.3f, other: PartnerOf(c));
                    Stats.LeisureHome++;
                    return false;
            }
        }

        // ------------------------------------------------------------ Verkehrsmittel

        /// <summary>
        /// Wahrscheinlichkeit, dass eine Fahrt als sichtbares Auto stattfindet. Grundanteil <see cref="CarShare"/> ×
        /// <see cref="TrafficSystem.Demand"/>, verschoben durch einen logistischen Nutzwertvergleich Auto vs. zu Fuß/ÖPNV
        /// (sparsam/naturverbunden → weniger Auto, ehrgeizig/weite Wege → mehr Auto, Stau-Erinnerung → deutlich weniger).
        /// Bei durchschnittlicher Persönlichkeit, ohne Erinnerungen und ~750 m Weg genau der Grundanteil.
        /// </summary>
        public float CarProbability(Citizen c, float distance)
        {
            float share = (c.IsCommuterIn ? CommuterCarShare : CarShare) * traffic.Demand;
            if (share <= 0f) return 0f;
            var p = c.Personality;
            float uCar = -0.5f * (p.Thrifty - 0.5f) + 0.3f * (p.Ambitious - 0.5f) - 0.35f * (p.NatureLoving - 0.5f)
                         + 0.25f * (MathF.Min(1f, distance / 2500f) - 0.3f)
                         - 0.6f * MathF.Min(1f, c.Memories.Strength(MemoryKind.TrafficJam));
            float pCar = UtilityChoice.Probability(uCar, 0f, p.Temperature + 0.15f);
            return Math.Clamp(share * 2f * pCar, 0f, 1f);
        }

        // ------------------------------------------------------------ Wohnung bewerten

        /// <summary>
        /// Nutzwert einer Wohnung für einen Haushalt: Mittel über die Mitglieder (Kinder halb) der ortsabhängigen
        /// Bedürfnisse (Wohnen, Versorgung, Ruhe, Sicherheit, Bildung, Gesundheitsdienst, Mobilität) gewichtet mit
        /// Lebensphase × Persönlichkeit. 0..1.
        /// </summary>
        public float HomeScore(Household h, Building b, int extraMembers = 0)
        {
            if (h == null || b == null) return 0f;
            var ctx = Context(b);
            int members = h.MemberList.Count + extraMembers;
            float housing = HousingTarget(b, ctx, members), calm = CalmTarget(ctx), safety = SafetyTarget(ctx);
            float supply = ctx.Supplied ? 1f : 0.05f;
            float total = 0f, weights = 0f;
            foreach (var m in h.MemberList)
            {
                var stage = m.Stage;
                var p = m.Personality;
                float sum = 0f, ws = 0f;
                void Add(NeedKind k, float v)
                {
                    float w = Needs.StageWeight(stage, k) * p.NeedWeight(k);
                    sum += w * v;
                    ws += w;
                }
                Add(NeedKind.Housing, housing);
                Add(NeedKind.Supply, supply);
                Add(NeedKind.Calm, calm);
                Add(NeedKind.Safety, safety);
                Add(NeedKind.Education, EducationTarget(m, ctx));
                Add(NeedKind.Health, 0.65f * m.Health + 0.35f * ctx.Health);
                if (m.IsEmployed) Add(NeedKind.Mobility, MobilityTarget(m, CommuteFrom(m, b)));
                float weight = m.Age < Citizen.AdultAge ? 0.5f : 1f;
                if (ws > 0f) { total += weight * sum / ws; weights += weight; }
            }
            return weights > 0f ? total / weights : 0f;
        }

        /// <summary>Bis zu <see cref="MaxCandidates"/> zufällige freie Wohnungen (ohne <paramref name="except"/>) in <see cref="candidates"/>.</summary>
        int SampleFreeHomes(int except, int startIndex)
        {
            // Neu sammeln nur, wenn seit dem letzten Mal Wohnungen frei geworden sind oder neue Gebäude dazukamen;
            // inzwischen belegte Einträge überspringt die Stichprobe
            if (freeHousingDirty) RefreshFreeHousing();
            return SampleFrom(freeHousing, except, startIndex);
        }

        int SampleFrom(List<Building> pool, int except, int startIndex)
        {
            int n = startIndex;
            if (pool.Count == 0) return n;
            for (int tries = 0; tries < MaxCandidates * 3 && n < startIndex + MaxCandidates; tries++)
            {
                var b = pool[rng.Next(pool.Count)];
                if (b.Id == except || city.GetBuilding(b.Id) == null || HouseholdsIn(b.Id).Count >= b.Households) continue;
                bool dup = false;
                for (int i = startIndex; i < n; i++) if (candidates[i] == b) { dup = true; break; }
                if (!dup) candidates[n++] = b;
            }
            return n;
        }

        // ------------------------------------------------------------ Haushalte: Umzug und Wegzug (täglich, gestaffelt)

        void HouseholdDecisions(int day)
        {
            householdScratch.Clear();
            foreach (var list in homes.Values) householdScratch.AddRange(list);
            // Reihenfolge der Dictionary-Werte ist für gleiche Abläufe gleich; sortieren macht es robust
            householdScratch.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var h in householdScratch)
            {
                if (h.MemberList.Count == 0 || (h.Id + day) % HouseholdDecisionDays != 0) continue;
                if (!homes.TryGetValue(h.HomeId, out var l) || !l.Contains(h)) continue;
                if (rng.NextDouble() < LeaveProbability(h)) { LeaveCity(h); continue; }
                if (day - h.MovedInDay >= 10) ConsiderMove(h);
            }
        }

        /// <summary>
        /// Wahrscheinlichkeit, dass der Haushalt bei einer Prüfung die Stadt verlässt:
        /// <see cref="LeaveConsiderRate"/> × logistisch(Nutzen Wegzug − Nutzen Bleiben).
        /// Bleiben: Zufriedenheit, Gemüt, Verwurzelung (Jahre hier), Familiensinn. Wegzug: Abenteuerlust,
        /// Langzeitarbeitslosigkeit aller Erwerbsfähigen, Erinnerungen an Ausfälle. Frisch Zugezogene (&lt; 5 Tage) bleiben.
        /// </summary>
        public float LeaveProbability(Household h)
        {
            if (h == null || h.MemberList.Count == 0 || clock.Day - h.MovedInDay < 5) return 0f;
            float sat = 0f, mood = 0f, adv = 0f, fam = 0f, outage = 0f;
            int n = 0, working = 0, longUnemployed = 0;
            foreach (var m in h.MemberList)
            {
                if (m.IsWorkingAge)
                {
                    working++;
                    if (!m.IsEmployed && m.UnemployedDays > 10) longUnemployed++;
                }
                if (m.Age < Citizen.AdultAge && h.MemberList.Count > 1) continue;
                n++;
                sat += m.Satisfaction;
                mood += m.Mood;
                adv += m.Personality.Adventurous;
                fam += m.Personality.FamilyOriented;
                outage += m.Memories.Strength(MemoryKind.PowerOutage) + m.Memories.Strength(MemoryKind.WaterOutage);
            }
            if (n == 0) return 0f;
            sat /= n; mood /= n; adv /= n; fam /= n; outage /= n;
            float years = (clock.Day - h.MovedInDay) / (float)Core.SimulationClock.DaysPerYear;
            float stay = 0.55f + 1.3f * (sat - 0.55f) + 0.4f * (mood - 0.5f) + MathF.Min(0.25f, years * 0.08f) + 0.1f * (fam - 0.5f);
            float leave = 0.2f + 0.3f * (adv - 0.5f) + (working > 0 && longUnemployed == working ? 0.7f : 0f) + 0.15f * MathF.Min(1f, outage);
            return LeaveConsiderRate * UtilityChoice.Probability(leave, stay, 0.12f);
        }

        void LeaveCity(Household h)
        {
            var head = Head(h);
            if (head != null) Log(LifeEventKind.LeaveCity, head, null, null);
            Stats.LeftCity++;
            RemoveHousehold(h);
        }

        /// <summary>Umzug innerhalb der Stadt: aktuelle Wohnung (mit Trägheit) gegen eine Stichprobe freier Wohnungen.</summary>
        void ConsiderMove(Household h)
        {
            var home = city.GetBuilding(h.HomeId);
            if (home == null) return;
            float stay = HomeScore(h, home);
            float noise = 0f, outage = 0f, unsafeMem = 0f, smog = 0f, thrifty = 0f, temp = 0f;
            int adults = 0;
            foreach (var m in h.MemberList)
            {
                var mem = m.Memories;
                noise += mem.Strength(MemoryKind.Noise);
                smog += mem.Strength(MemoryKind.Smog);
                outage += mem.Strength(MemoryKind.PowerOutage) + mem.Strength(MemoryKind.WaterOutage);
                unsafeMem += mem.Strength(MemoryKind.FeelsUnsafe);
                if (m.Age < Citizen.AdultAge) continue;
                adults++;
                thrifty += m.Personality.Thrifty;
                temp += m.Personality.Temperature;
            }
            if (adults == 0) return;
            int count = h.MemberList.Count;
            thrifty /= adults; temp /= adults;
            // Ohne Leidensdruck nur selten umsehen
            float pressure = (1f - stay) + 0.15f * (noise + smog + outage + unsafeMem) / count;
            if (pressure < 0.3f && !rng.Chance(0.1f)) return;

            stay += 0.06f + 0.08f * thrifty - 0.15f * MathF.Min(1f, noise / count) - 0.1f * MathF.Min(1f, outage / count)
                    - 0.1f * MathF.Min(1f, unsafeMem / count) - 0.08f * MathF.Min(1f, smog / count);
            int n = SampleFreeHomes(home.Id, 1);
            if (n <= 1) return;
            scores[0] = stay;
            for (int i = 1; i < n; i++) scores[i] = HomeScore(h, candidates[i]);
            // Umzug ist eine große Entscheidung: halbe Temperatur
            int choice = UtilityChoice.Choose(scores, n, temp * 0.5f, rng);
            if (choice <= 0) return;
            RelocateHousehold(h, candidates[choice]);
            Stats.MovedWithinCity++;
            foreach (var m in h.MemberList) Remember(m, MemoryKind.MovedHome, 0.6f, street: candidates[choice].FrontSegmentId, building: candidates[choice].Id);
            var head = Head(h);
            if (head != null) Log(LifeEventKind.MoveHome, head, null, candidates[choice]);
        }

        /// <summary>Haushalt zieht in ein anderes Gebäude (Belegung, Orte, Pendelweg).</summary>
        void RelocateHousehold(Household h, Building target)
        {
            int old = h.HomeId;
            if (homes.TryGetValue(old, out var list)) list.Remove(h);
            freeHousingDirty = true;
            h.HomeId = target.Id;
            h.MovedInDay = clock.Day;
            if (!homes.TryGetValue(target.Id, out var nl)) homes[target.Id] = nl = new List<Household>();
            nl.Add(h);
            foreach (var m in h.MemberList)
            {
                if (m.Activity != Activity.Travelling && m.Location.BuildingId == old) m.Location = Place.Building(target.Id);
                if (m.Activity == Activity.Travelling && m.Purpose == TripPurpose.ToHome) m.Destination = Place.Building(target.Id);
                RefreshCommute(m);
            }
        }

        /// <summary>Ältestes erwachsenes Mitglied (für Meldungen).</summary>
        static Citizen Head(Household h)
        {
            Citizen best = null;
            foreach (var m in h.MemberList) if (best == null || m.Age > best.Age) best = m;
            return best;
        }

        // ------------------------------------------------------------ Arbeit: Jobsuche und Jobwechsel

        /// <summary>
        /// Nutzwert eines Jobs: Entfernung (gewichtet mit dem Mobilitätsbedürfnis) und Passung zu Bildung/Ehrgeiz.
        /// Ersetzt die frühere Regel „nächster Job, Gebildete +250 m für Nicht-Büro“ (bei durchschnittlichem
        /// Ehrgeiz entspricht die Passung weiterhin ~250 m).
        /// </summary>
        public float JobUtility(Citizen c, Building job, Vector2 from)
        {
            float mobility = c.Personality.NeedWeight(NeedKind.Mobility);
            float dist = Vector2.Distance(from, job.Entrance);
            return -dist / 1000f * (0.5f + 0.5f * mobility) + 0.5f * JobMatch(c, job);
        }

        void JobDecisions(int day)
        {
            int searched = 0;
            foreach (var c in citizens)
            {
                if (gone.Contains(c) || c.IsCommuterIn || !c.IsWorkingAge || !c.IsEmployed) continue;
                if ((c.Id + day) % JobDecisionDays != 0) continue;
                if (searched >= MaxJobSearchesPerRun) break;
                var home = city.GetBuilding(c.Household.HomeId);
                if (home == null) continue;
                float work = c.Need(NeedKind.Work), mob = c.Need(NeedKind.Mobility);
                if (work >= 0.75f && mob >= 0.55f && !rng.Chance(0.2f * c.Personality.Ambitious)) continue;
                searched++;
                ConsiderJobChange(c, home);
            }
        }

        void ConsiderJobChange(Citizen c, Building home)
        {
            int comp = ComponentOf(home);
            if (comp < 0 || jobBuildings.Count == 0) return;
            var current = c.WorkplaceId != 0 ? city.GetBuilding(c.WorkplaceId) : null;
            float stay = current != null ? JobUtility(c, current, home.Entrance)
                                         : -c.CommuteMeters / 1000f - 0.3f; // Auspendler: weiter Weg, lieber hier
            scores[0] = stay + 0.12f + 0.08f * c.Personality.Thrifty;
            int n = 1;
            for (int tries = 0; tries < MaxCandidates * 4 && n < 1 + MaxCandidates; tries++)
            {
                var b = jobBuildings[rng.Next(jobBuildings.Count)];
                if (b == current || city.GetBuilding(b.Id) == null || FreeJobs(b) <= 0 || c.Education < MinEducation(b.Zone) || ComponentOf(b) != comp) continue;
                bool dup = false;
                for (int i = 1; i < n; i++) if (candidates[i] == b) { dup = true; break; }
                if (dup) continue;
                candidates[n] = b;
                scores[n] = JobUtility(c, b, home.Entrance);
                n++;
            }
            if (n <= 1) return;
            int choice = UtilityChoice.Choose(scores, n, c.Personality.Temperature * 0.5f, rng);
            if (choice <= 0) return;
            Employ(c, candidates[choice]);
            Stats.JobChanges++;
            Remember(c, MemoryKind.NewJob, 0.6f, building: candidates[choice].Id);
            Log(LifeEventKind.JobChange, c, null, candidates[choice]);
        }
    }
}
