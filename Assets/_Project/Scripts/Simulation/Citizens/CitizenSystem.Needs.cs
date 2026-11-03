using System;
using System.Collections.Generic;
using System.Text;
using CityBuilder.Simulation.Citizens.Content;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Citizens
{
    /// <summary>
    /// Bedürfnismodell (Grundlage für G6): Bedürfnisse je Bürger, gestaffelt aktualisiert (1/<see cref="NeedsSpreadTicks"/>
    /// der Bürger je Tick), Zufriedenheit, Gemüt, Erinnerungen und lesende Abfragen für den Inspektor.
    /// </summary>
    public sealed partial class CitizenSystem
    {
        /// <summary>Jeder Bürger wird alle so viele Ticks neu bewertet (24 Ticks = 2,4 Spielstunden, 10× am Tag).</summary>
        public const int NeedsSpreadTicks = 24;

        /// <summary>
        /// Abdeckung eines Gebäudes durch einen Dienst (G4), 0..1. Ids: <see cref="ServiceIds"/>. Standard 0,5 = neutral.
        /// Wird beim Zusammenführen mit dem Dienste-System verbunden.
        /// </summary>
        public Func<string, Building, float> ServiceCoverage { get; set; } = (id, b) => 0.5f;

        /// <summary>Liegender Müll am Gebäude 0..1 (G4, <c>ServiceSystem.GarbageLevel</c>); null = keiner. Senkt Ruhe/Umwelt.</summary>
        public Func<Building, float> GarbageLevel { get; set; }

        /// <summary>
        /// Nächster/bester echter Park für ein Wohngebäude (G4); null bzw. Id 0 = keiner in Reichweite →
        /// Freizeit „Park“ bleibt ein Spaziergang im Viertel.
        /// </summary>
        public Func<Building, ParkSpot> ParkFinder { get; set; }

        // --- Abwasser/Wasserqualität/Atom/Medien (G3/G4 erweitert): lesende Hooks, verbunden in CitizenUtilityBinding.Connect.
        // Alle null = neutral (Tests ohne Bindung, Startphase ohne solche Bauwerke).
        /// <summary>Trinkwasserqualität am Wohngebäude 0..1 (1 = sauber, null = 1). Senkt die Gesundheit.</summary>
        public Func<Building, float> DrinkingWaterQuality { get; set; }
        /// <summary>Sorge „Atomkraftwerk nebenan“ am Wohngebäude 0..1 (null = 0). Wirkt je nach Ängstlichkeit.</summary>
        public Func<Building, float> NuclearConcern { get; set; }
        /// <summary>Medienreichweite am Wohngebäude 0..1 (Zeitung/Radio/TV, null = 0): Freizeit/Bildung leicht +.</summary>
        public Func<Building, float> MediaReach { get; set; }
        /// <summary>Stimmung der aktuellen Schlagzeilen −1..+1 (null = 0): verstärkt das Gemüt der Erreichten ein wenig.</summary>
        public Func<float> NewsTone { get; set; }
        // --- Ende

        // --- Eigene Assets: Freizeitanlagen, Sport, Wahrzeichen (verbunden in CitizenServiceBinding.Connect; null bzw. 0 = neutral,
        // ohne solche Bauwerke ändert sich nichts)
        /// <summary>Abdeckung mit Freizeitanlagen (Spielplatz … Riesenrad) am Wohngebäude 0..1: Freizeit steigt nebenbei.</summary>
        public Func<Building, float> LeisureCoverage { get; set; }
        /// <summary>Abdeckung mit Sportanlagen (Sportplatz, Tennis, Freibad) 0..1: Gesundheits-Ziel steigt.</summary>
        public Func<Building, float> SportCoverage { get; set; }
        /// <summary>Wahrzeichen in der Nähe 0..1 („Stolz“): Zufriedenheit und Gemüt leicht höher.</summary>
        public Func<Building, float> LandmarkPride { get; set; }
        /// <summary>Freizeit je Bewertung bei voller Abdeckung (≈ +0,12 je Tag gegen −0,35 Abklingen).</summary>
        public const float LeisureFacilityBonus = 0.012f;
        /// <summary>Gesundheits-Ziel + so viel bei voller Sport-Abdeckung.</summary>
        public const float SportHealthBonus = 0.1f;
        /// <summary>Zufriedenheit + so viel bei vollem Stolz (Wahrzeichen in der Nähe).</summary>
        public const float PrideSatisfactionBonus = 0.04f;
        /// <summary>Gemüt-Ziel + so viel bei vollem Stolz.</summary>
        public const float PrideMoodBonus = 0.03f;
        // --- Ende Eigene Assets

        /// <summary>Multiplikator auf alle gewürfelten Lebensereignisse (Tests/Balancing).</summary>
        public float LifeEventRate = 1f;

        SimulationWorld world;
        int needsCursor;
        int needsCycle = 1;

        /// <summary>Umfeld eines Wohngebäudes – einmal je Bewertungsrunde berechnet und von allen Bewohnern geteilt.</summary>
        struct HomeContext
        {
            public int Cycle;
            public float Noise, Air;
            public float Police, Fire, Health, Education, EducationPrimary, EducationSecondary, Library, Park, Garbage, GarbageLevel;
            public bool Supplied, PowerOut, WaterOut;
            // G3/G4 erweitert: Trinkwasser (1 = sauber), Atom-Sorge, Medienreichweite, Abwasser fehlt
            public float DrinkingWater, Nuclear, Media;
            public bool SewageOut;
            // Eigene Assets: Freizeitanlagen, Sport, Wahrzeichen-Stolz
            public float Leisure, Sport, Pride;
        }

        readonly Dictionary<int, HomeContext> contexts = new Dictionary<int, HomeContext>();
        readonly Dictionary<int, float> residentSatisfaction = new Dictionary<int, float>();
        readonly Dictionary<int, float> workerSatisfaction = new Dictionary<int, float>();

        HomeContext Context(Building b)
        {
            if (contexts.TryGetValue(b.Id, out var ctx) && ctx.Cycle == needsCycle) return ctx;
            ctx = new HomeContext { Cycle = needsCycle };
            if (world != null)
            {
                ctx.Noise = Math.Clamp(world.Environment.Noise.Sample(b.Position), 0f, 1f);
                ctx.Air = Math.Clamp(world.Environment.AirPollution.Sample(b.Position), 0f, 1f);
            }
            var u = world?.Utilities;
            ctx.Supplied = u == null || u.IsSatisfied(b);
            if (u != null)
            {
                var st = u.GetStatus(b);
                ctx.PowerOut = !st.HasPower && u.IsRequired(UtilityKind.Power);
                ctx.WaterOut = !st.HasWater && u.IsRequired(UtilityKind.Water);
                ctx.SewageOut = !st.HasSewage && u.IsRequired(UtilityKind.Sewage); // G3 erweitert
            }
            // --- G3/G4 erweitert
            ctx.DrinkingWater = DrinkingWaterQuality == null ? 1f : Math.Clamp(DrinkingWaterQuality(b), 0f, 1f);
            ctx.Nuclear = NuclearConcern == null ? 0f : Math.Clamp(NuclearConcern(b), 0f, 1f);
            ctx.Media = MediaReach == null ? 0f : Math.Clamp(MediaReach(b), 0f, 1f);
            // --- Ende
            // --- Eigene Assets
            ctx.Leisure = LeisureCoverage == null ? 0f : Math.Clamp(LeisureCoverage(b), 0f, 1f);
            ctx.Sport = SportCoverage == null ? 0f : Math.Clamp(SportCoverage(b), 0f, 1f);
            ctx.Pride = LandmarkPride == null ? 0f : Math.Clamp(LandmarkPride(b), 0f, 1f);
            // --- Ende Eigene Assets
            var cov = ServiceCoverage;
            ctx.Police = Coverage(cov, ServiceIds.Police, b);
            ctx.Fire = Coverage(cov, ServiceIds.Fire, b);
            ctx.Health = Coverage(cov, ServiceIds.Health, b);
            ctx.Education = Coverage(cov, ServiceIds.Education, b);
            ctx.EducationPrimary = Coverage(cov, ServiceIds.EducationPrimary, b);
            ctx.EducationSecondary = Coverage(cov, ServiceIds.EducationSecondary, b);
            ctx.Library = Coverage(cov, ServiceIds.Library, b);
            ctx.Park = Coverage(cov, ServiceIds.Park, b);
            ctx.Garbage = Coverage(cov, ServiceIds.Garbage, b);
            ctx.GarbageLevel = GarbageLevel == null ? 0f : Math.Clamp(GarbageLevel(b), 0f, 1f);
            contexts[b.Id] = ctx;
            return ctx;
        }

        static float Coverage(Func<string, Building, float> cov, string id, Building b) =>
            cov == null ? 0.5f : Math.Clamp(cov(id, b), 0f, 1f);

        /// <summary>Gestaffelt: je Tick ein 1/<see cref="NeedsSpreadTicks"/>-Anteil der Bürger.</summary>
        void UpdateNeedsSlice()
        {
            int n = citizens.Count;
            if (n == 0) return;
            int batch = (n + NeedsSpreadTicks - 1) / NeedsSpreadTicks;
            for (int k = 0; k < batch; k++)
            {
                if (needsCursor >= citizens.Count) { needsCursor = 0; needsCycle++; }
                var c = citizens[needsCursor++];
                if (!gone.Contains(c)) UpdateNeeds(c);
            }
        }

        /// <summary>Für Leistungstests: eine Staffel wie in einem Tick.</summary>
        internal void ForceUpdateNeedsSliceForTest() => UpdateNeedsSlice();

        /// <summary>Für Tests/Werkzeuge: alle Bürger sofort neu bewerten (Umfeld neu lesen).</summary>
        public void ForceUpdateNeeds()
        {
            needsCycle++;
            foreach (var c in citizens) if (!gone.Contains(c)) UpdateNeeds(c);
        }

        static void Approach(float[] n, NeedKind k, float target, float rate) =>
            n[(int)k] = Math.Clamp(n[(int)k] + (target - n[(int)k]) * rate, 0f, 1f);

        internal void UpdateNeeds(Citizen c)
        {
            var n = c.NeedValues;
            var p = c.Personality;
            var home = c.IsCommuterIn ? null : city.GetBuilding(c.Household.HomeId);
            if (home != null)
            {
                var ctx = Context(home);
                Approach(n, NeedKind.Housing, HousingTarget(home, ctx, c.Household.MemberList.Count), 0.3f);
                Approach(n, NeedKind.Supply, ctx.Supplied ? 1f : 0.05f, 0.6f);
                float calm = CalmTarget(ctx);
                // G3 erweitert: Atomkraftwerk nebenan – nur Ängstliche spüren es deutlich (Ruhe/Umwelt etwas schlechter)
                float nuclearCalm = ctx.Nuclear > 0f ? NuclearCalmPenalty * ctx.Nuclear * p.Anxious : 0f;
                Approach(n, NeedKind.Calm, calm - nuclearCalm, 0.35f);
                float safety = SafetyTarget(ctx);
                Approach(n, NeedKind.Safety, safety, 0.25f);
                Approach(n, NeedKind.Education, EducationTarget(c, ctx), 0.25f);
                // G3 erweitert: schlechtes Trinkwasser (verschmutzter Fluss an der Pumpe) senkt die Gesundheit
                float healthTarget = 0.65f * c.Health + 0.35f * ctx.Health - DrinkingWaterHealthPenalty * (1f - ctx.DrinkingWater);
                if (ctx.Sport > 0f) healthTarget += SportHealthBonus * ctx.Sport; // Eigene Assets: Sportanlagen in Reichweite
                Approach(n, NeedKind.Health, healthTarget, 0.3f);
                // Freizeit sinkt mit der Zeit (≈ 0,35 je Tag), ein Park in Reichweite hilft ein wenig nebenbei (G4 erweitert: Medien auch)
                float leisure = n[(int)NeedKind.Leisure] - 0.035f + 0.008f * ctx.Park + MediaLeisureBonus * ctx.Media;
                if (ctx.Leisure > 0f) leisure += LeisureFacilityBonus * ctx.Leisure; // Eigene Assets: Freizeitanlagen
                n[(int)NeedKind.Leisure] = Math.Clamp(leisure, 0f, 1f);

                // Eindrücke vom Wohnort → Erinnerungen
                int street = home.FrontSegmentId;
                if (ctx.PowerOut) Remember(c, MemoryKind.PowerOutage, 0.35f, street: street, building: home.Id);
                if (ctx.WaterOut) Remember(c, MemoryKind.WaterOutage, 0.35f, street: street, building: home.Id);
                // --- G3 erweitert (ohne Zufall: verbraucht keinen RNG, ändert bestehende Abläufe nicht)
                if (ctx.SewageOut) Remember(c, MemoryKind.SewageBackup, 0.3f, street: street, building: home.Id);
                if (ctx.DrinkingWater < Utilities.WaterQualitySystem.BadDrinkingWater)
                    Remember(c, MemoryKind.BadTapWater, 0.2f + 0.6f * (1f - ctx.DrinkingWater), street: street, building: home.Id);
                if (ctx.Nuclear > 0f && p.Anxious >= NuclearFearAnxious)
                    Remember(c, MemoryKind.NuclearFear, 0.6f * ctx.Nuclear * p.Anxious, street: street);
                // --- Ende
                if (calm < 0.5f && rng.Chance((0.5f - calm) * p.NoiseSensitive * 0.6f))
                    Remember(c, ctx.Air > ctx.Noise ? MemoryKind.Smog : MemoryKind.Noise, 0.3f + (0.5f - calm), street: street);
                if (safety < 0.35f && rng.Chance(p.Anxious * 0.15f)) Remember(c, MemoryKind.FeelsUnsafe, 0.35f, street: street);
            }
            Approach(n, NeedKind.Work, WorkTarget(c, c.WorkplaceId != 0 ? city.GetBuilding(c.WorkplaceId) : null), 0.3f);
            n[(int)NeedKind.Shopping] = c.Age < Citizen.AdultAge ? 0.8f : 1f - Math.Clamp(c.ShoppingNeed / 1.5f, 0f, 1f);
            Approach(n, NeedKind.Mobility, MobilityTarget(c, c.CommuteMeters), 0.3f);

            c.Satisfaction = SatisfactionOf(c);
            // --- Eigene Assets: Stolz auf Wahrzeichen in der Nähe hebt Zufriedenheit und Gemüt ein wenig
            float pride = home != null ? Context(home).Pride : 0f;
            if (pride > 0f) c.Satisfaction = Math.Clamp(c.Satisfaction + PrideSatisfactionBonus * pride, 0f, 1f);
            // --- Ende
            float moodTarget = 0.5f + 0.5f * MathF.Tanh(c.Memories.MoodSum() * 0.8f) + 0.2f * (c.Satisfaction - 0.6f) - (c.IsIll ? 0.1f : 0f);
            if (pride > 0f) moodTarget += PrideMoodBonus * pride; // Eigene Assets
            // G4 erweitert: Medien verstärken die Stimmung leicht (schlechte Nachrichten drücken, gute heben) – nur wer erreicht wird
            if (home != null && NewsTone != null)
            {
                float media = Context(home).Media;
                if (media > 0f) moodTarget += NewsMoodWeight * media * Math.Clamp(NewsTone(), -1f, 1f);
            }
            c.Mood = Math.Clamp(c.Mood + (moodTarget - c.Mood) * 0.4f, 0f, 1f);
        }

        // ------------------------------------------------------------ Zielwerte (auch für Bewertungen fremder Wohnungen)

        /// <summary>Bequem bewohnbar: Personen je Wohnung nach Bauart.</summary>
        static float ComfortableMembers(ZoneType z) =>
            z == ZoneType.ResidentialLow ? 5f : z == ZoneType.ResidentialMedium ? 4f : 3.5f;

        static float HousingTarget(Building home, in HomeContext ctx, int members)
        {
            float space = Math.Clamp(1f - MathF.Max(0f, members - ComfortableMembers(home.Zone)) * 0.18f, 0.1f, 1f);
            return Math.Clamp(0.55f * space + 0.45f * HousingCondition(home, ctx.Supplied), 0f, 1f);
        }

        // --- G6: Wohnqualität aus Stufe, physischem Zustand (Building.Condition, LandValueSystem) und Versorgung.
        // Eine Formel für Wohnbedürfnis und Inspektor; bei neuem Gebäude (Zustand 1) wie vor G6.
        static float HousingCondition(Building b, bool supplied) =>
            Math.Clamp((0.55f + 0.12f * b.Level) * (0.5f + 0.5f * b.Condition), 0f, 1f) - (supplied ? 0f : 0.25f);
        // --- Ende G6

        // --- G3/G4 erweitert: Stärken (Platzhalter fürs Balancing, bewusst vorsichtig)
        /// <summary>Gesundheits-Ziel sinkt um bis zu so viel bei Trinkwasserqualität 0.</summary>
        public const float DrinkingWaterHealthPenalty = 0.4f;
        /// <summary>Ruhe/Umwelt sinkt um bis zu so viel × Ängstlichkeit direkt neben dem Atomkraftwerk.</summary>
        public const float NuclearCalmPenalty = 0.3f;
        /// <summary>Ab dieser Ängstlichkeit entsteht die Erinnerung „Atomkraftwerk nebenan“.</summary>
        public const float NuclearFearAnxious = 0.4f;
        /// <summary>Freizeit je Bewertung bei voller Medienreichweite (≈ +0,04 je Tag gegen −0,35 Abklingen).</summary>
        public const float MediaLeisureBonus = 0.004f;
        /// <summary>Bildung (Erwachsene): Ziel + so viel bei voller Medienreichweite.</summary>
        public const float MediaEducationBonus = 0.08f;
        /// <summary>Gemüt-Ziel ± so viel bei voller Reichweite und eindeutig guten/schlechten Nachrichten.</summary>
        public const float NewsMoodWeight = 0.05f;
        // --- Ende

        static float CalmTarget(in HomeContext ctx) =>
            Math.Clamp(1f - Math.Clamp(ctx.Noise * 1.1f + ctx.Air * 1.6f, 0f, 1f) + (ctx.Garbage - 0.5f) * 0.2f - 0.3f * ctx.GarbageLevel, 0f, 1f);

        static float SafetyTarget(in HomeContext ctx) => 0.15f + 0.85f * (0.6f * ctx.Police + 0.4f * ctx.Fire);

        static float EducationTarget(Citizen c, in HomeContext ctx) =>
            c.Age < 6 ? 0.5f + 0.5f * ctx.EducationPrimary          // Vorschulkind: Grundschule in der Nähe beruhigt die Eltern
            : c.Age < 12 ? 0.1f + 0.9f * ctx.EducationPrimary
            : c.Age < Citizen.AdultAge ? 0.1f + 0.9f * ctx.EducationSecondary
            : Math.Clamp(0.2f + 0.8f * (0.6f * ctx.Library + 0.4f * ctx.Education) + MediaEducationBonus * ctx.Media, 0f, 1f); // G4 erw.: Medien

        float WorkTarget(Citizen c, Building job)
        {
            if (c.Age < Citizen.AdultAge) return 0.7f;
            if (c.Age >= Citizen.RetirementAge) return 0.75f;
            if (c.WorkplaceId != 0)
            {
                float t = 0.8f + 0.15f * JobMatch(c, job);
                return Math.Clamp(t, 0f, 1f);
            }
            if (c.WorksOutside) return 0.65f;
            if (c.IsCommuterIn) return 0.8f;
            return MathF.Max(0.05f, 0.45f - 0.04f * c.UnemployedDays);
        }

        /// <summary>Passt der Job zur Bildung und zum Ehrgeiz? −1..+1 (0 = neutral).</summary>
        static float JobMatch(Citizen c, Building job)
        {
            if (job == null) return 0f;
            float amb = 0.5f + c.Personality.Ambitious;
            if (c.Education >= Education.Secondary && job.Zone != ZoneType.Office) return -0.5f * amb;
            if (c.Education == Education.Higher && job.Zone == ZoneType.Office) return 0.3f * amb;
            return 0f;
        }

        static float MobilityTarget(Citizen c, float commuteMeters)
        {
            float jam = MathF.Min(1f, c.Memories.Strength(MemoryKind.TrafficJam));
            if (commuteMeters <= 0f) return 0.75f - 0.1f * jam;
            float t = 1f - Math.Clamp((commuteMeters - 500f) / 3500f, 0f, 0.7f);
            // ÖPNV: volle/verspätete Busse senken, gute Fahrten heben die Mobilität
            float bus = -0.15f * MathF.Min(1f, c.Memories.Strength(MemoryKind.BusFull)) - 0.1f * MathF.Min(1f, c.Memories.Strength(MemoryKind.BusLate))
                        + 0.1f * MathF.Min(1f, c.Memories.Strength(MemoryKind.BusRide));
            return Math.Clamp(t - 0.25f * jam - 0.15f * MathF.Min(1f, c.Memories.Strength(MemoryKind.LongCommute)) + bus, 0f, 1f);
        }

        /// <summary>Zufriedenheit: gewichteter Mittelwert (Lebensphase × Persönlichkeit).</summary>
        static float SatisfactionOf(Citizen c)
        {
            var stage = c.Stage;
            var p = c.Personality;
            float sum = 0f, wsum = 0f;
            for (int i = 0; i < Needs.Count; i++)
            {
                var k = (NeedKind)i;
                float w = Needs.StageWeight(stage, k) * p.NeedWeight(k);
                sum += w * c.NeedValues[i];
                wsum += w;
            }
            return wsum > 0f ? sum / wsum : 0.6f;
        }

        void RefreshCommute(Citizen c)
        {
            var home = c.Household != null ? city.GetBuilding(c.Household.HomeId) : null;
            c.CommuteMeters = home == null ? 0f : CommuteFrom(c, home);
        }

        /// <summary>Luftlinie vom Wohngebäude zur Arbeit (Auspendler: bis zum Außenanschluss + 2 km).</summary>
        float CommuteFrom(Citizen c, Building home)
        {
            if (c.WorkplaceId != 0)
            {
                var job = city.GetBuilding(c.WorkplaceId);
                return job == null ? 0f : System.Numerics.Vector2.Distance(home.Entrance, job.Entrance);
            }
            if (c.WorksOutside)
            {
                var node = network.GetNode(c.OutsideWorkConnectionId);
                return node == null ? 0f : System.Numerics.Vector2.Distance(home.Entrance, node.Position) + 2000f;
            }
            return 0f;
        }

        // ------------------------------------------------------------ Erinnerungen

        /// <summary>Erinnerung anlegen/auffrischen. Textvorlage per stabilem Hash (verbraucht keinen Zufall).</summary>
        internal void Remember(Citizen c, MemoryKind kind, float strength, int street = 0, int building = 0, Citizen other = null, int facility = 0)
        {
            if (c == null || strength <= 0f) return;
            if (Memories.Valence(kind) < 0f) strength *= 0.75f + 0.5f * c.Personality.Anxious;
            c.MemoryStore ??= new MemoryList();
            var b = building != 0 ? city.GetBuilding(building) : null;
            var m = new Memory
            {
                Kind = kind,
                Strength = strength,
                Day = clock.Day,
                Street = street,
                Building = building,
                Facility = facility,
                Zone = b?.Zone ?? ZoneType.None,
                OtherId = other?.Id ?? 0,
                OtherName = other == null ? 0 : NameKey(other),
                Template = (byte)(SimRandom.Hash(c.Id, clock.Day * 37 + (int)kind) % (uint)Thoughts.Count(kind)),
            };
            c.MemoryStore.Add(m);
        }

        static int NameKey(Citizen c) => (int)c.Gender * 10000 + c.FirstNameIndex + 1;

        static string NameFromKey(int key) => key <= 0 ? null : Names.First((Gender)(key / 10000), key % 10000 - 1);

        /// <summary>Gedankensatz zu einer Erinnerung (Platzhalter ersetzt).</summary>
        public string MemoryText(Citizen c, in Memory m)
        {
            int count = Thoughts.Count(m.Kind);
            for (int attempt = 0; attempt < count; attempt++)
            {
                string text = Fill(Thoughts.Template(m.Kind, m.Template + attempt), c, m);
                if (text != null) return text;
            }
            return Memories.DisplayName(m.Kind);
        }

        /// <summary>Alle Erinnerungstexte (stärkste zuerst) in <paramref name="into"/>.</summary>
        public void MemoryTexts(Citizen c, List<string> into)
        {
            into.Clear();
            if (c == null) return;
            var list = c.Memories;
            var order = new List<int>(list.Count);
            for (int i = 0; i < list.Count; i++) order.Add(i);
            order.Sort((a, b) => list[b].Strength.CompareTo(list[a].Strength));
            foreach (int i in order) into.Add(MemoryText(c, list[i]));
        }

        /// <summary>Ersetzt Platzhalter; null, wenn ein Platzhalter nicht auflösbar ist (dann nächste Vorlage).</summary>
        string Fill(string template, Citizen c, in Memory m)
        {
            if (template.IndexOf('{') < 0) return template;
            var sb = new StringBuilder(template.Length + 24);
            int i = 0;
            while (i < template.Length)
            {
                char ch = template[i];
                if (ch != '{') { sb.Append(ch); i++; continue; }
                int end = template.IndexOf('}', i);
                if (end < 0) { sb.Append(template, i, template.Length - i); break; }
                string key = template.Substring(i + 1, end - i - 1);
                string value = Resolve(key, c, m);
                if (value == null) return null;
                sb.Append(value);
                i = end + 1;
            }
            return sb.ToString();
        }

        string Resolve(string key, Citizen c, in Memory m)
        {
            switch (key)
            {
                case "Name": return c.FirstName;
                case "Nachname": return c.LastName;
                case "Partner":
                case "Kind":
                    if (m.OtherName != 0) return NameFromKey(m.OtherName);
                    if (key == "Partner" && c.PartnerId != 0) { var pa = Get(c.PartnerId); return pa?.FirstName; }
                    return null;
                case "Straße":
                    if (m.Street > 0) return PlaceNames.Street(m.Street);
                    var home = c.Household != null ? city.GetBuilding(c.Household.HomeId) : null;
                    return home != null ? PlaceNames.Street(home.FrontSegmentId) : null;
                case "Firma":
                    if (m.Building > 0) return PlaceNames.Company(m.Building, m.Zone);
                    var job = c.WorkplaceId != 0 ? city.GetBuilding(c.WorkplaceId) : null;
                    return job != null ? PlaceNames.Company(job.Id, job.Zone) : c.WorksOutside ? "der Firma außerhalb" : null;
                case "Park":
                    if (m.Facility > 0) return PlaceNames.ParkFacility(m.Facility);
                    return c.Household != null ? PlaceNames.Park(c.Household.HomeId) : null;
                default: return null;
            }
        }

        // ------------------------------------------------------------ Abfragen für den Inspektor

        /// <summary>
        /// Wohnqualität eines Gebäudes 0..1 wie im Wohnbedürfnis: Stufe × physischer Zustand (G6, <see cref="Building.Condition"/>),
        /// −0,25 ohne Versorgung. Den reinen Bauzustand liefert <c>Building.Condition</c> bzw. <c>LandValueSystem.LevelInfo</c>.
        /// </summary>
        public float BuildingCondition(Building b)
        {
            if (b == null) return 0f;
            bool supplied = world?.Utilities == null || world.Utilities.IsSatisfied(b);
            return HousingCondition(b, supplied);
        }

        /// <summary>
        /// Umfeld eines Gebäudes 0..1 (für alle gleich gewichtet): Mittel aus Ruhe/Umwelt, Sicherheit, Gesundheitsdienst,
        /// Bildung (Erwachsenensicht) und Park. Stand der aktuellen Bewertungsrunde.
        /// </summary>
        public float SurroundingsScore(Building b)
        {
            if (b == null) return 0f;
            var ctx = Context(b);
            return (CalmTarget(ctx) + SafetyTarget(ctx) + ctx.Health + (0.6f * ctx.Library + 0.4f * ctx.Education) + ctx.Park) / 5f;
        }

        /// <summary>Einwohner eines Gebäudes im Alter [minAge, maxAge] (z. B. Schüler für den Dienste-Bedarf).</summary>
        public int CountResidents(int buildingId, int minAge, int maxAge)
        {
            int n = 0;
            foreach (var h in HouseholdsIn(buildingId))
                foreach (var m in h.MemberList)
                    if (m.Age >= minAge && m.Age <= maxAge) n++;
            return n;
        }

        /// <summary>Wo verbringt der Bürger die Freizeit? „im Park ‹Name›“ bzw. „Spaziergang im Viertel“.</summary>
        public static string LeisurePlaceText(Citizen c) =>
            c == null ? "–" : c.LeisureParkId > 0 ? $"im {PlaceNames.ParkFacility(c.LeisureParkId)}" : "Spaziergang im Viertel";

        /// <summary>Partner (null = keiner bzw. weggezogen).</summary>
        public Citizen PartnerOf(Citizen c) => c == null || c.PartnerId == 0 ? null : Get(c.PartnerId);

        /// <summary>Familienstand als Text, z. B. „verheiratet mit Anna“, „ledig (Kind)“, „verwitwet“.</summary>
        public string FamilyStatusText(Citizen c)
        {
            if (c == null) return "–";
            if (c.Age < Citizen.AdultAge) return "ledig (Kind)";
            var partner = PartnerOf(c);
            switch (c.Relationship)
            {
                case Relationship.Married: return partner != null ? $"verheiratet mit {partner.FirstName}" : "verheiratet";
                case Relationship.Partnered: return partner != null ? $"in Partnerschaft mit {partner.FirstName}" : "in Partnerschaft";
                case Relationship.Separated: return "getrennt";
                case Relationship.Widowed: return c.Gender == Gender.Female ? "verwitwet (Witwe)" : "verwitwet (Witwer)";
                default: return "ledig";
            }
        }

        public static string MoodText(float mood) =>
            mood >= 0.8f ? "glücklich" : mood >= 0.62f ? "gut gelaunt" : mood >= 0.42f ? "ausgeglichen" : mood >= 0.25f ? "bedrückt" : "verzweifelt";

        public static string SatisfactionText(float s) =>
            s >= 0.8f ? "sehr zufrieden" : s >= 0.62f ? "zufrieden" : s >= 0.45f ? "geht so" : s >= 0.3f ? "unzufrieden" : "sehr unzufrieden";

        public static string HealthText(Citizen c) =>
            c == null ? "–" : c.IsIll ? "krank" : c.Health >= 0.85f ? "kerngesund" : c.Health >= 0.65f ? "gesund" : c.Health >= 0.45f ? "angeschlagen" : "gebrechlich";

        /// <summary>Mittlere Zufriedenheit der Bewohner (−1 = keine Bewohner). Stand der letzten Statistik.</summary>
        public float ResidentSatisfaction(int buildingId) => residentSatisfaction.TryGetValue(buildingId, out var s) ? s : -1f;

        /// <summary>Zufriedenheit der Beschäftigten mit Arbeit und Arbeitsweg (−1 = keine). Stand der letzten Statistik.</summary>
        public float WorkerSatisfaction(int buildingId) => workerSatisfaction.TryGetValue(buildingId, out var s) ? s : -1f;

        /// <summary>
        /// Wichtigste Gründe für die Zufriedenheit: die zwei am schlechtesten erfüllten Bedürfnisse mit hohem Gewicht,
        /// z. B. „Ruhe/Umwelt schlecht, Mobilität mittel“.
        /// </summary>
        public static string Concerns(Citizen c)
        {
            if (c == null) return "–";
            NeedKind w1 = NeedKind.Housing, w2 = NeedKind.Housing;
            float s1 = float.MaxValue, s2 = float.MaxValue;
            foreach (var k in Needs.All)
            {
                float w = Needs.StageWeight(c.Stage, k) * c.Personality.NeedWeight(k);
                if (w <= 0.05f) continue;
                float score = c.Need(k) - 0.1f * w; // wichtige Bedürfnisse zählen etwas mehr
                if (score < s1) { s2 = s1; w2 = w1; s1 = score; w1 = k; }
                else if (score < s2) { s2 = score; w2 = k; }
            }
            return $"{Needs.DisplayName(w1)} {Needs.Describe(c.Need(w1))}, {Needs.DisplayName(w2)} {Needs.Describe(c.Need(w2))}";
        }
    }
}
