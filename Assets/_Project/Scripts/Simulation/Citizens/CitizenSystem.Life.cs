using System;
using System.Collections.Generic;
using System.Text;
using CityBuilder.Simulation.Citizens.Content;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>Eintrag im Ereignis-Ticker (Meldungstext beim Entstehen aus der Vorlage erzeugt).</summary>
    public readonly struct LifeEventRecord
    {
        public readonly LifeEventKind Kind;
        public readonly int Day;
        public readonly int CitizenId;
        public readonly string Text;

        public LifeEventRecord(LifeEventKind kind, int day, int citizenId, string text)
        {
            Kind = kind;
            Day = day;
            CitizenId = citizenId;
            Text = text;
        }
    }

    /// <summary>
    /// Beziehungen und Lebensereignisse: Partnerschaft, Hochzeit, Geburt, Auszug erwachsener Kinder, Trennung,
    /// Krankheit/Genesung, Jobverlust, Rente, Tod. Chancen aus den Vorlagen (<see cref="LifeEvents"/>), Wirkung
    /// in Erinnerungen und im Ereignis-Ticker.
    /// </summary>
    public sealed partial class CitizenSystem
    {
        public const int MaxRecentEvents = 40;
        readonly LifeEventRecord[] recentEvents = new LifeEventRecord[MaxRecentEvents];
        int recentStart, recentCount;
        readonly List<Citizen> singles = new List<Citizen>();

        /// <summary>Letzte Lebensereignisse, neueste zuerst.</summary>
        public IEnumerable<LifeEventRecord> RecentEvents
        {
            get
            {
                for (int i = recentCount - 1; i >= 0; i--) yield return recentEvents[(recentStart + i) % MaxRecentEvents];
            }
        }

        public int RecentEventCount => recentCount;

        /// <summary>Dienstfahrzeuge: Bürger ist erkrankt (im täglichen Durchlauf; Empfänger merken sich nur den Einsatz).</summary>
        public event Action<Citizen> BecameIll;

        void Log(LifeEventKind kind, Citizen subject, Citizen other, Building place)
        {
            var t = LifeEvents.Get(kind);
            if (t.News.Length == 0 || subject == null) return;
            int idx = (int)(SimRandom.Hash(subject.Id, clock.Day * 13 + (int)kind) % (uint)t.News.Length);
            string text = null;
            for (int a = 0; a < t.News.Length && text == null; a++) text = FillNews(t.News[(idx + a) % t.News.Length], subject, other, place);
            if (text == null) text = $"{LifeEvents.DisplayName(kind)}: {subject.FullName}";
            var rec = new LifeEventRecord(kind, clock.Day, subject.Id, text);
            if (recentCount < MaxRecentEvents) recentEvents[(recentStart + recentCount++) % MaxRecentEvents] = rec;
            else { recentEvents[recentStart] = rec; recentStart = (recentStart + 1) % MaxRecentEvents; }
        }

        string FillNews(string template, Citizen subject, Citizen other, Building place)
        {
            var sb = new StringBuilder(template.Length + 32);
            int i = 0;
            while (i < template.Length)
            {
                char ch = template[i];
                int end = ch == '{' ? template.IndexOf('}', i) : -1;
                if (end < 0) { sb.Append(ch); i++; continue; }
                string key = template.Substring(i + 1, end - i - 1);
                string v;
                switch (key)
                {
                    case "Name": v = subject.FullName; break;
                    case "Nachname": v = subject.LastName; break;
                    case "Partner":
                    case "Kind": v = other?.FirstName; break;
                    case "Straße":
                        var b = place ?? (subject.Household != null ? city.GetBuilding(subject.Household.HomeId) : null);
                        v = b != null ? PlaceNames.Street(b.FrontSegmentId) : null;
                        break;
                    case "Firma":
                        var job = place ?? (subject.WorkplaceId != 0 ? city.GetBuilding(subject.WorkplaceId) : null);
                        v = job != null ? PlaceNames.Company(job.Id, job.Zone) : null;
                        break;
                    default: v = null; break;
                }
                if (v == null) return null;
                sb.Append(v);
                i = end + 1;
            }
            return sb.ToString();
        }

        bool Roll(LifeEventKind kind, Citizen c, float factor = 1f)
        {
            var t = LifeEvents.Get(kind);
            if (c.Age < t.MinAge || c.Age > t.MaxAge) return false;
            return rng.Chance(t.ChanceFor(c.Personality) * factor * LifeEventRate);
        }

        static float Strength(LifeEventKind k, bool relative)
        {
            var t = LifeEvents.Get(k);
            return relative ? t.RelativeStrength : t.SubjectStrength;
        }

        // ------------------------------------------------------------ Täglich je Bürger

        /// <summary>
        /// Umwelt (Etappe 6, Hook): chronische Luftbelastung an einer Wohnung 0..1 (geglättetes Tagesmittel). Gesetzt von
        /// <c>EcologySystem.Connect</c>; null bzw. 0 = wie bisher.
        /// </summary>
        public Func<Building, float> ChronicAirExposure { get; set; }

        /// <summary>Erinnerungen abklingen, Gesundheit, Lebensereignisse. Läuft im täglichen Durchlauf.</summary>
        void DailyLife(Citizen c)
        {
            c.MemoryStore?.Decay(c.Personality.Anxious);
            if (c.IsCommuterIn) return;
            var home = city.GetBuilding(c.Household.HomeId);
            float air = 0f, healthCov = 0.5f;
            if (home != null) { var ctx = Context(home); air = ctx.Air; healthCov = ctx.Health; }

            // Gesundheit: Grundwert nach Alter, Abgas, Krankheit
            float baseHealth = c.Age < 50 ? 1f : MathF.Max(0.45f, 1f - (c.Age - 50) * 0.012f);
            float target = Math.Clamp(baseHealth - 0.3f * air - (c.IsIll ? 0.45f : 0f) + 0.1f * (healthCov - 0.5f), 0.05f, 1f);
            // --- Umwelt (Etappe 6): chronische Luftbelastung an der Wohnung (Atemwege), Kinder und Ältere stärker (0 = wie bisher)
            float chronic = ChronicAirExposure != null && home != null ? ChronicAirExposure(home) : 0f;
            float airIllness = 1f;
            if (chronic > 0f)
            {
                float load = chronic * Environment.EcologyRules.Vulnerability(c.Age);
                target = Math.Clamp(target - Environment.EcologyRules.ChronicHealthLoss * load, 0.05f, 1f);
                airIllness = 1f + Environment.EcologyRules.ChronicIllnessFactor * load;
                if (load >= 0.25f) Remember(c, MemoryKind.Smog, Math.Min(1f, 0.25f + load));
            }
            // --- Ende Umwelt
            c.Health = Math.Clamp(c.Health + (target - c.Health) * 0.25f, 0f, 1f);

            if (c.IsIll)
            {
                if (Roll(LifeEventKind.Recovery, c, (0.5f + healthCov) * (c.Age > 70 ? 0.6f : 1f)))
                {
                    c.IsIll = false;
                    Remember(c, MemoryKind.Recovered, Strength(LifeEventKind.Recovery, false));
                    Log(LifeEventKind.Recovery, c, null, null);
                }
            }
            else if (Roll(LifeEventKind.Illness, c, (1f + (c.Age > 60 ? (c.Age - 60) * 0.05f : 0f)) * (1f + 2f * air) * (1.5f - healthCov) * airIllness))
            {
                c.IsIll = true;
                BecameIll?.Invoke(c); // Dienstfahrzeuge: Rettungswagen bei schwerer Erkrankung (nur Zustand merken, keine Fahrt im Handler)
                Remember(c, MemoryKind.Illness, Strength(LifeEventKind.Illness, false));
                var partner = PartnerOf(c);
                if (partner != null) Remember(partner, MemoryKind.Illness, Strength(LifeEventKind.Illness, true), other: c);
                Log(LifeEventKind.Illness, c, null, null);
            }

            if (c.WorkplaceId != 0 && c.IsWorkingAge && Roll(LifeEventKind.JobLoss, c))
            {
                var job = city.GetBuilding(c.WorkplaceId);
                Log(LifeEventKind.JobLoss, c, null, job);
                Remember(c, MemoryKind.JobLost, Strength(LifeEventKind.JobLoss, false), building: c.WorkplaceId);
                var partner = PartnerOf(c);
                if (partner != null) Remember(partner, MemoryKind.JobLost, Strength(LifeEventKind.JobLoss, true), other: c);
                Fire(c);
                c.CommuteMeters = 0f;
                Stats.JobsLost++;
            }

            if (c.Age < Citizen.AdultAge) return;
            var h = c.Household;
            var mate = PartnerOf(c);
            bool together = mate != null && mate.Household == h;

            if (mate == null)
            {
                if (c.PartnerId != 0) { c.PartnerId = 0; if (c.Relationship != Relationship.Widowed) c.Relationship = Relationship.Separated; }
                if (!ParentInHousehold(c) || c.Age >= 25) TryAddSingle(c);
            }
            else if (together && c.Id < mate.Id) // Paar nur einmal würfeln
            {
                float avgSat = (c.Satisfaction + mate.Satisfaction) * 0.5f;
                if (c.Relationship == Relationship.Partnered && Roll(LifeEventKind.Wedding, c)) Wedding(c, mate);
                else if (Roll(LifeEventKind.Separation, c, 1f + 3f * MathF.Max(0f, 0.6f - avgSat) + 0.5f * MathF.Max(0f, 0.4f - (c.Mood + mate.Mood) * 0.5f)))
                    Separate(c, mate);
                else TryBirth(c, mate);
            }

            if (ParentInHousehold(c) && Roll(LifeEventKind.MoveOut, c, 1f + (c.Age - 18) * 0.15f)) MoveOut(c);
        }

        bool ParentInHousehold(Citizen c)
        {
            if (c.Household == null) return false;
            foreach (var m in c.Household.MemberList)
                if (m != c && (m.Id == c.ParentAId || m.Id == c.ParentBId)) return true;
            return false;
        }

        void TryAddSingle(Citizen c)
        {
            if (Roll(LifeEventKind.FindPartner, c)) singles.Add(c);
        }

        /// <summary>Nach dem täglichen Durchlauf: Partnersuchende zusammenbringen (nach Alter, möglichst nicht verwandt).</summary>
        void MatchSingles()
        {
            if (singles.Count < 2) { singles.Clear(); return; }
            for (int i = 0; i < singles.Count; i++)
            {
                var a = singles[i];
                if (a == null || gone.Contains(a) || a.PartnerId != 0) continue;
                int best = -1;
                float bestScore = float.MaxValue;
                for (int j = i + 1; j < singles.Count; j++)
                {
                    var b = singles[j];
                    if (b == null || gone.Contains(b) || b.PartnerId != 0 || b.Household == a.Household) continue;
                    if (b.ParentAId == a.ParentAId && a.ParentAId != 0) continue; // Geschwister
                    float score = MathF.Abs(a.Age - b.Age) + (a.Gender == b.Gender ? 8f : 0f) + rng.NextFloat() * 3f;
                    if (score < bestScore) { bestScore = score; best = j; }
                }
                if (best < 0 || bestScore > 14f) continue;
                var mate = singles[best];
                singles[best] = null;
                Couple(a, mate);
            }
            singles.Clear();
        }

        /// <summary>Zwei Singles werden ein Paar und ziehen zusammen (der kleinere Haushalt zieht um, wenn Platz ist).</summary>
        internal bool Couple(Citizen a, Citizen b)
        {
            if (a == null || b == null || a == b || a.IsCommuterIn || b.IsCommuterIn) return false;
            // Wer zieht? Der mit dem kleineren Haushalt; erwachsene Kinder ziehen allein aus dem Elternhaus aus
            var mover = a.Household.MemberList.Count <= b.Household.MemberList.Count ? a : b;
            var stayer = mover == a ? b : a;
            var target = stayer.Household;
            var targetHome = city.GetBuilding(target.HomeId);
            if (targetHome == null) return false;
            bool moverAlone = !ParentInHousehold(mover);
            int moving = moverAlone ? mover.Household.MemberList.Count : 1;
            if (target.MemberList.Count + moving > ComfortableMembers(targetHome.Zone) + 2) return false;

            a.PartnerId = b.Id; b.PartnerId = a.Id;
            a.Relationship = b.Relationship = Relationship.Partnered;
            if (moverAlone)
            {
                var old = mover.Household;
                foreach (var m in old.MemberList.ToArray()) MoveMember(m, target);
                DissolveIfEmpty(old);
            }
            else MoveMember(mover, target);
            Remember(a, MemoryKind.NewPartner, Strength(LifeEventKind.FindPartner, false), other: b);
            Remember(b, MemoryKind.NewPartner, Strength(LifeEventKind.FindPartner, false), other: a);
            Log(LifeEventKind.FindPartner, a, b, null);
            Stats.Partnerships++;
            return true;
        }

        void Wedding(Citizen a, Citizen b)
        {
            a.Relationship = b.Relationship = Relationship.Married;
            if (rng.Chance(0.7f)) b.LastNameIndex = a.LastNameIndex; else a.LastNameIndex = b.LastNameIndex;
            Remember(a, MemoryKind.Wedding, Strength(LifeEventKind.Wedding, false), other: b);
            Remember(b, MemoryKind.Wedding, Strength(LifeEventKind.Wedding, true), other: a);
            Log(LifeEventKind.Wedding, a, b, null);
            Stats.Weddings++;
        }

        /// <summary>Geburt: Paar mit Frau im Alter laut Vorlage, höchstens 4 Kinder, Platz in der Wohnung zählt.</summary>
        void TryBirth(Citizen a, Citizen b)
        {
            var mother = a.Gender == Gender.Female ? a : b.Gender == Gender.Female ? b : null;
            var father = mother == a ? b : a;
            if (mother == null || father.Gender != Gender.Male) return;
            int kids = 0;
            foreach (var m in a.Household.MemberList) if (m.Age < Citizen.AdultAge) kids++;
            if (kids >= 4) return;
            // Chance aus der Vorlage (Familiensinn der Mutter), dazu Familiensinn des Vaters, Platz und Kinderzahl
            float space = mother.Need(NeedKind.Housing);
            float factor = (0.6f + 0.8f * father.Personality.FamilyOriented) * (0.4f + space) * (kids == 0 ? 1.3f : 1f / kids);
            if (!Roll(LifeEventKind.Birth, mother, factor)) return;
            Birth(mother, father);
        }

        internal Citizen Birth(Citizen mother, Citizen father)
        {
            var h = mother.Household;
            if (h == null) return null;
            var child = NewCitizen(0);
            child.Personality = Personality.Mix(mother.Personality, father?.Personality ?? Personality.FromArchetype(Archetypes.Roll(rng), rng), rng);
            child.LastNameIndex = father != null && rng.Chance(0.6f) ? father.LastNameIndex : mother.LastNameIndex;
            child.ParentAId = mother.Id;
            child.ParentBId = father?.Id ?? 0;
            child.ShoppingNeed = 0f;
            child.Household = h;
            child.Location = Place.Building(h.HomeId);
            child.Activity = Activity.Home;
            h.MemberList.Add(child);
            Remember(mother, MemoryKind.Birth, Strength(LifeEventKind.Birth, false), other: child);
            if (father != null) Remember(father, MemoryKind.Birth, Strength(LifeEventKind.Birth, true), other: child);
            Log(LifeEventKind.Birth, mother, child, null);
            Stats.Born++;
            return child;
        }

        /// <summary>Trennung: einer der beiden zieht in eine freie Wohnung oder verlässt die Stadt. Kinder bleiben.</summary>
        internal void Separate(Citizen a, Citizen b)
        {
            a.PartnerId = b.PartnerId = 0;
            a.Relationship = b.Relationship = Relationship.Separated;
            Remember(a, MemoryKind.Separation, Strength(LifeEventKind.Separation, false), other: b);
            Remember(b, MemoryKind.Separation, Strength(LifeEventKind.Separation, true), other: a);
            Log(LifeEventKind.Separation, a, b, null);
            Stats.Separations++;
            var mover = rng.Chance(0.5f) ? a : b;
            if (!MoveIntoOwnHome(mover, out _)) RemoveCitizen(mover, countAsMovedOut: true);
        }

        /// <summary>Erwachsenes Kind zieht aus (mit Partner, falls im selben Haushalt). Ohne freie Wohnung bleibt es.</summary>
        internal bool MoveOut(Citizen c)
        {
            var parents = c.Household;
            var partner = PartnerOf(c);
            if (!MoveIntoOwnHome(c, out var nh)) return false;
            if (partner != null && partner.Household == parents) MoveMember(partner, nh);
            var home = city.GetBuilding(nh.HomeId);
            Remember(c, MemoryKind.OwnFlat, Strength(LifeEventKind.MoveOut, false), street: home?.FrontSegmentId ?? 0, building: nh.HomeId);
            foreach (var m in parents.MemberList)
                if (m.Id == c.ParentAId || m.Id == c.ParentBId) Remember(m, MemoryKind.ChildMovedOut, Strength(LifeEventKind.MoveOut, true), other: c);
            Log(LifeEventKind.MoveOut, c, null, home);
            Stats.MovedOutOfParents++;
            return true;
        }

        /// <summary>Bürger gründet einen eigenen Haushalt in der besten von einigen freien Wohnungen.</summary>
        bool MoveIntoOwnHome(Citizen c, out Household created)
        {
            created = null;
            var old = c.Household;
            if (old == null) return false;
            var oldHome = city.GetBuilding(old.HomeId);
            int n = SampleFreeHomes(old.HomeId, 0);
            if (n == 0) return false;
            // Bewertung als Ein-Personen-Haushalt: vorübergehend allein betrachten
            var probe = new Household { Id = -1, HomeId = old.HomeId };
            probe.MemberList.Add(c);
            for (int i = 0; i < n; i++)
                scores[i] = HomeScore(probe, candidates[i]) - (oldHome != null ? System.Numerics.Vector2.Distance(oldHome.Position, candidates[i].Position) / 20000f : 0f);
            int choice = UtilityChoice.Choose(scores, n, c.Personality.Temperature * 0.5f, rng);
            var target = candidates[choice];
            var h = new Household { Id = nextHouseholdId++, HomeId = target.Id, MovedInDay = clock.Day };
            if (!homes.TryGetValue(target.Id, out var list)) homes[target.Id] = list = new List<Household>();
            list.Add(h);
            householdCount++;
            MoveMember(c, h);
            DissolveIfEmpty(old);
            created = h;
            return true;
        }

        /// <summary>Mitglied wechselt den Haushalt (Ort/Fahrtziel folgen).</summary>
        void MoveMember(Citizen c, Household to)
        {
            var from = c.Household;
            if (from == to) return;
            from?.MemberList.Remove(c);
            c.Household = to;
            to.MemberList.Add(c);
            if (c.Activity != Activity.Travelling && from != null && c.Location.BuildingId == from.HomeId) c.Location = Place.Building(to.HomeId);
            if (c.Activity == Activity.Travelling && c.Purpose == TripPurpose.ToHome) c.Destination = Place.Building(to.HomeId);
            if (c.Activity == Activity.Leisure) { c.Activity = Activity.Home; c.LeisureParkId = 0; c.Location = Place.Building(to.HomeId); }
            RefreshCommute(c);
        }

        void DissolveIfEmpty(Household h)
        {
            if (h == null || h.MemberList.Count > 0) return;
            if (homes.TryGetValue(h.HomeId, out var list) && list.Remove(h))
            {
                { householdCount--; freeHousingDirty = true; }
                HouseholdMovedOut?.Invoke(h);
            }
        }

        /// <summary>Tod: Partner wird verwitwet und erinnert sich.</summary>
        void OnDeath(Citizen c)
        {
            Log(LifeEventKind.Death, c, null, null);
            var partner = PartnerOf(c);
            if (partner == null) return;
            partner.PartnerId = 0;
            partner.Relationship = Relationship.Widowed;
            Remember(partner, MemoryKind.PartnerDied, Strength(LifeEventKind.Death, true), other: c);
        }

        void OnRetired(Citizen c, int workplace)
        {
            Remember(c, MemoryKind.Retired, Strength(LifeEventKind.Retirement, false), building: workplace);
            Log(LifeEventKind.Retirement, c, null, workplace != 0 ? city.GetBuilding(workplace) : null);
        }

        /// <summary>Lohn bzw. Rente je Tag (brutto, ohne Steuern), grobe Platzhalter bis zur Wirtschaft (G5).</summary>
        float DailyIncome(Citizen c)
        {
            if (c.Age < Citizen.AdultAge) return 0f;
            if (c.Age >= Citizen.RetirementAge) return 45f;
            float edu = 60f + 25f * (int)c.Education;
            if (c.WorkplaceId != 0)
            {
                var job = city.GetBuilding(c.WorkplaceId);
                return edu + (job != null && job.Zone == ZoneType.Office ? 20f : 0f);
            }
            if (c.WorksOutside) return edu * 0.9f;
            return 0f;
        }
    }
}
