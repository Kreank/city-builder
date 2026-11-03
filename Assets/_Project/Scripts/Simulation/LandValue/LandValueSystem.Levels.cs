using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.LandValue
{
    /// <summary>Stand der Ausbaustufe eines Gebäudes (für Inspektor/HUD).</summary>
    public struct BuildingLevelInfo
    {
        public int Level, MaxLevel;
        /// <summary>Netto-Tage mit erfüllten Anforderungen und benötigte Tage für den nächsten Aufstieg.</summary>
        public int ProgressDays, DaysNeeded;
        /// <summary>Fortschritt zum Aufstieg 0..1 (1 bei höchster Stufe).</summary>
        public float Progress;
        /// <summary>Was fehlt (None = alles erfüllt, läuft).</summary>
        public LevelGap Missing;
        public string MissingText;
        /// <summary>Anforderungen der nächsten Stufe (bei höchster Stufe die der aktuellen).</summary>
        public LevelRequirement Next;
        /// <summary>Schlechte Tage (netto) bis zum Abstieg.</summary>
        public int DeclineDays, DeclineDaysLimit;
        public bool Declining;
        /// <summary>Zustand 0..1 und als Text.</summary>
        public float Condition;
        public string ConditionText;
        /// <summary>Tage leer stehend (ab Fertigstellung).</summary>
        public int EmptyDays;
        /// <summary>Die Werte, nach denen bewertet wird.</summary>
        public BuildingFacts Facts;

        // --- Wohnhäuser setzen (Anno-Stil): nur bei gesetzten Häusern belegt
        /// <summary>Vom Spieler gesetztes Haus – Stufen nach <see cref="HouseRules"/> statt Bodenwert.</summary>
        public bool PlayerHouse;
        /// <summary>Anforderungen der nächsten Stufe (kumuliert) mit ✓/✗ (bei höchster Stufe: die der eigenen Stufe).</summary>
        public List<HouseNeedStatus> Needs;
        /// <summary>Weiche Bedingungen der nächsten Stufe: nötige Zufriedenheit/Belegung.</summary>
        public float NeedSatisfaction, NeedOccupancy;
        /// <summary>Fehlt eine Anforderung der eigenen Stufe? (Dann laufen die Abstiegs-Tage.)</summary>
        public bool OwnNeedsMissing;
        // --- Ende Wohnhäuser
    }

    public sealed partial class LandValueSystem
    {
        sealed class LevelState
        {
            public int Progress, Decline, EmptyDays;
        }

        readonly Dictionary<int, LevelState> states = new Dictionary<int, LevelState>();
        readonly List<Building> scratch = new List<Building>();

        /// <summary>
        /// Hook für Tests/Szenarien: liefert die Werte eines Gebäudes statt der echten Erhebung (null = echt:
        /// Bodenwert, Bürger-Zufriedenheit, Versorgung, Belegung, Bildung, Müll).
        /// </summary>
        public Func<Building, BuildingFacts> FactsProvider { get; set; }

        /// <summary>Verfallene Gebäude abreißen (Standard an).</summary>
        public bool DemolishDecayed = true;

        /// <summary>Aufstieg (neue Stufe &gt; alte) bzw. Abstieg. Argumente: Gebäude, alte Stufe.</summary>
        public event Action<Building, int> LevelChanged;
        /// <summary>Gebäude ist verfallen und wird gleich abgerissen.</summary>
        public event Action<Building> BuildingDecayed;

        public int Upgrades { get; private set; }
        public int Downgrades { get; private set; }
        public int DecayedBuildings { get; private set; }

        /// <summary>Für Tests/Werkzeuge: einen Tag Stufen/Zustand sofort auswerten.</summary>
        public void ForceEvaluateDay() => EvaluateBuildings();

        void EvaluateBuildings()
        {
            scratch.Clear();
            foreach (var b in world.City.Buildings) if (b.IsConstructed) scratch.Add(b);
            foreach (var b in scratch)
            {
                if (!states.TryGetValue(b.Id, out var st)) states[b.Id] = st = new LevelState();
                var f = FactsOf(b);

                // Zustand
                st.EmptyDays = f.Occupancy <= 0f ? st.EmptyDays + 1 : 0;
                b.Condition = Math.Clamp(b.Condition + LandValueRules.ConditionDelta(f, st.EmptyDays), 0f, 1f);
                if (b.Condition <= 0f && DemolishDecayed && !b.IsPlayerPlaced) // gesetzte Häuser verfallen nicht bis zum Abriss
                {
                    DecayedBuildings++;
                    BuildingDecayed?.Invoke(b);
                    world.City.RemoveBuilding(b);
                    continue;
                }

                // --- Wohnhäuser setzen: gesetzte Häuser steigen nach Anforderungen (HouseRules) auf/ab
                if (b.IsPlayerPlaced) { EvaluateHouse(b, st, f); continue; }
                // --- Ende Wohnhäuser

                // Aufstieg
                var gap = LandValueRules.Missing(b.Zone, b.Level, b.Condition, f);
                if (b.Level < LandValueRules.MaxLevel)
                {
                    st.Progress = LandValueRules.NextProgress(st.Progress, gap == LevelGap.None);
                    if (st.Progress >= LandValueRules.RequirementFor(b.Zone, b.Level + 1).Days)
                    {
                        SetLevel(b, b.Level + 1);
                        b.Condition = 1f; // Ausbau = Modernisierung
                        st.Progress = 0;
                        st.Decline = 0;
                        Upgrades++;
                        continue;
                    }
                }

                // Abstieg
                st.Decline = LandValueRules.NextProgress(st.Decline, LandValueRules.IsBadDay(b.Zone, b.Level, f));
                if (b.Level > 1 && st.Decline >= LandValueRules.DeclineDays)
                {
                    SetLevel(b, b.Level - 1);
                    st.Decline = 0;
                    st.Progress = 0;
                    Downgrades++;
                }
            }
        }

        void SetLevel(Building b, int level)
        {
            int old = b.Level;
            level = Math.Clamp(level, 1, b.IsPlayerPlaced ? HouseRules.MaxLevelFor(b.Zone) : LandValueRules.MaxLevel);
            if (level == old) return;
            b.Level = level;
            int cells = b.WidthCells * b.DepthCells;
            // Mehr Kapazität → mehr Verbrauch (Strom/Wasser/Müll rechnen aus Kapazität bzw. Belegung).
            // Abstieg: wer schon da ist, bleibt vorerst (Überbelegung baut sich über Umzug/Wegzug ab, neue ziehen nicht ein).
            b.Households = LandValueRules.HouseholdsFor(b.Zone, cells, level);
            b.Jobs = LandValueRules.JobsFor(b.Zone, cells, level);
            world.City.NotifyBuildingChanged(b);
            LevelChanged?.Invoke(b, old);
        }

        /// <summary>Werte eines Gebäudes wie in der Tagesauswertung (oder aus <see cref="FactsProvider"/>).</summary>
        public BuildingFacts FactsOf(Building b)
        {
            if (FactsProvider != null) return FactsProvider(b);
            var f = new BuildingFacts { LandValue = ValueOf(b), Satisfaction = -1f, Supplied = true };
            var u = world.Utilities;
            if (u != null) f.Supplied = u.IsSatisfied(b);
            var c = world.Citizens;
            bool homes = b.Households > 0;
            f.Occupancy = homes
                ? (b.Households > 0 ? Math.Min(1f, b.OccupiedHouseholds / (float)b.Households) : 0f)
                : (b.Jobs > 0 ? Math.Min(1f, b.Workers / (float)b.Jobs) : 0f);
            if (c == null) return f;

            float rs = c.ResidentSatisfaction(b.Id), ws = c.WorkerSatisfaction(b.Id);
            f.Satisfaction = homes ? (rs >= 0f ? rs : ws) : ws;
            f.Garbage = c.GarbageLevel == null ? 0f : Math.Clamp(c.GarbageLevel(b), 0f, 1f);

            int skilled = 0, total = 0;
            if (homes)
            {
                foreach (var h in c.HouseholdsIn(b.Id))
                    foreach (var m in h.MemberList)
                    {
                        if (m.Age < Citizen.AdultAge) continue;
                        total++;
                        if (m.Education >= Education.Secondary) skilled++;
                    }
            }
            else
            {
                var min = b.Zone == ZoneType.Office ? Education.Higher : Education.Secondary;
                foreach (var w in c.WorkersAt(b.Id))
                {
                    total++;
                    if (w.Education >= min) skilled++;
                }
            }
            f.Skill = total > 0 ? skilled / (float)total : 0f;
            return f;
        }

        /// <summary>Ausbaustufe, Fortschritt, was fehlt, Zustand – für Inspektor/HUD.</summary>
        public BuildingLevelInfo LevelInfo(Building b)
        {
            var info = new BuildingLevelInfo { MaxLevel = LandValueRules.MaxLevel, DeclineDaysLimit = LandValueRules.DeclineDays };
            if (b == null) return info;
            if (b.IsPlayerPlaced) return HouseLevelInfo(b, info); // Wohnhäuser setzen: Anforderungen statt Bodenwert
            info.Level = b.Level;
            info.Condition = b.Condition;
            info.ConditionText = LandValueRules.ConditionText(b.Condition);
            info.Facts = FactsOf(b);
            states.TryGetValue(b.Id, out var st);
            info.ProgressDays = st?.Progress ?? 0;
            info.DeclineDays = st?.Decline ?? 0;
            info.EmptyDays = st?.EmptyDays ?? 0;
            info.Declining = info.DeclineDays > 0;
            bool top = b.Level >= LandValueRules.MaxLevel;
            info.Next = LandValueRules.RequirementFor(b.Zone, top ? b.Level : b.Level + 1);
            info.DaysNeeded = top ? 0 : info.Next.Days;
            info.Missing = !b.IsConstructed ? LevelGap.UnderConstruction : LandValueRules.Missing(b.Zone, b.Level, b.Condition, info.Facts);
            info.MissingText = LandValueRules.LevelGapText(info.Missing);
            info.Progress = top ? 1f : Math.Clamp(info.ProgressDays / (float)Math.Max(1, info.DaysNeeded), 0f, 1f);
            return info;
        }
    }
}
