using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.LandValue
{
    /// <summary>
    /// Rohe Standortfaktoren einer Zelle, jeweils 0..1 (1 = viel davon). Gesammelt vom <see cref="LandValueSystem"/>,
    /// bewertet von <see cref="LandValueRules.Evaluate"/>.
    /// </summary>
    public struct LandValueInputs
    {
        /// <summary>Nähe zur nächsten Stadtstraße (1 bis 24 m, 0 ab 96 m).</summary>
        public float RoadAccess;
        /// <summary>Arbeitsplätze in Reichweite (gewichtet nach Entfernung, Sättigung) – „Nähe Zentrum/Jobs“.</summary>
        public float JobAccess;
        /// <summary>Nähe zum nächsten Außenanschluss (Luftlinie).</summary>
        public float ExternalAccess;
        /// <summary>Nähe zur Autobahn/Rampe (nur für Industrie-Standortwahl, nicht im Bodenwert).</summary>
        public float HighwayAccess;
        public float Police, Fire, Health, Education, Library, Park;
        /// <summary>Eigene Assets: Wahrzeichen in der Nähe (Radius × Stärke, 0..1).</summary>
        public float Landmark;
        /// <summary>Wasserlage (Ufer in der Nähe).</summary>
        public float Shore;
        /// <summary>G3 erweitert: Verschmutzung des nahen Gewässers 0..1 (Abwasser-Auslass) – macht die Wasserlage zum Abzug.</summary>
        public float WaterPollution;
        /// <summary>Aussichtslage (höher als die Umgebung).</summary>
        public float View;
        /// <summary>Bebauungsdichte (Wohnungen + Arbeitsplätze in der Nähe, gesättigt).</summary>
        public float Density;
        /// <summary>Zufriedenheit der Nachbarschaft 0..1; −1 = unbekannt (keine Bewohner/Beschäftigten in der Nähe).</summary>
        public float Neighborhood;
        public float Noise, Air, Garbage;
        /// <summary>Sicherheit: frische Brände in der Nähe + Kriminalität 0..1 (0 = ruhig).</summary>
        public float Safety;
        /// <summary>Umwelt (Etappe 6): Bodenbelastung/Altlasten 0..1.</summary>
        public float Contamination;
        /// <summary>Zelle liegt im Wasser → Bodenwert 0.</summary>
        public bool IsWater;
    }

    /// <summary>Gewichtete Beiträge der Faktoren zum Bodenwert (für Inspektor/HUD) und das Ergebnis.</summary>
    public struct LandValueFactors
    {
        public float Base, RoadAccess, JobAccess, ExternalAccess, Services, Park, Water, View, Density, Neighborhood;
        /// <summary>Eigene Assets: Beitrag der Wahrzeichen.</summary>
        public float Landmark;
        /// <summary>Abzüge (≤ 0).</summary>
        public float Noise, Air, Garbage;
        /// <summary>Sicherheit: Abzug für Brände/Kriminalität (≤ 0).</summary>
        public float Safety;
        /// <summary>Umwelt (Etappe 6): Abzug für Altlasten (≤ 0).</summary>
        public float Contamination;
        /// <summary>Summe, auf 0..1 begrenzt (Zielwert, ungeglättet).</summary>
        public float Target;
        /// <summary>Aktueller (träge geglätteter) Bodenwert der Zelle; −1 = noch nicht berechnet.</summary>
        public float Current;
        public bool IsWater;

        /// <summary>Beiträge als (Bezeichnung, Wert) – nur die nennenswerten (|Wert| ≥ 0,005), Reihenfolge fest.</summary>
        public List<(string label, float contribution)> Contributions()
        {
            var list = new List<(string, float)>(13);
            void Add(string label, float v) { if (MathF.Abs(v) >= 0.005f) list.Add((label, v)); }
            Add("Grundwert", Base);
            Add("Straßenanbindung", RoadAccess);
            Add("Nähe zu Arbeitsplätzen", JobAccess);
            Add("Außenanschluss", ExternalAccess);
            Add("Dienste", Services);
            Add("Parks", Park);
            Add("Wahrzeichen", Landmark);
            Add("Wasserlage", Water);
            Add("Aussicht", View);
            Add("Dichte", Density);
            Add("Nachbarschaft", Neighborhood);
            Add("Lärm", Noise);
            Add("Abgas", Air);
            Add("Müll", Garbage);
            Add("Brände/Kriminalität", Safety); // Sicherheit
            Add("Altlasten (Boden)", Contamination); // Umwelt
            return list;
        }
    }

    /// <summary>Was einem Gebäude zum Aufstieg fehlt (mehrere möglich).</summary>
    [Flags]
    public enum LevelGap
    {
        None = 0,
        LandValue = 1,
        Satisfaction = 2,
        Supply = 4,
        Education = 8,
        Occupancy = 16,
        Condition = 32,
        MaxLevel = 64,
        UnderConstruction = 128,
        /// <summary>Gesetztes Wohnhaus: Anforderungen (Einrichtungen in Reichweite, <see cref="Zoning.HouseRules"/>) fehlen.</summary>
        Needs = 256,
    }

    /// <summary>Werte eines Gebäudes, aus denen Aufstieg, Abstieg und Zustand folgen (täglich gesammelt).</summary>
    public struct BuildingFacts
    {
        /// <summary>Bodenwert des Grundstücks 0..1 (−1 = noch unbekannt).</summary>
        public float LandValue;
        /// <summary>Zufriedenheit der Bewohner (Wohnen/Mischgebiet) bzw. Beschäftigten 0..1; −1 = niemand da.</summary>
        public float Satisfaction;
        /// <summary>Strom/Wasser vorhanden (oder noch nicht verlangt).</summary>
        public bool Supplied;
        /// <summary>Belegung 0..1 (Wohnungen bzw. Arbeitsplätze).</summary>
        public float Occupancy;
        /// <summary>
        /// Bildungs-/Fachkräfteanteil 0..1: Wohnen = Erwachsene mit mind. mittlerem Abschluss; Büro = Beschäftigte mit
        /// Hochschulabschluss; Handel/Industrie = Beschäftigte mit mind. mittlerem Abschluss.
        /// </summary>
        public float Skill;
        /// <summary>Liegender Müll 0..1.</summary>
        public float Garbage;
    }

    /// <summary>Anforderungen für den Aufstieg auf eine Stufe.</summary>
    public struct LevelRequirement
    {
        public float LandValue, Satisfaction, Skill, Occupancy, Condition;
        /// <summary>So viele Tage müssen die Anforderungen (netto) erfüllt sein.</summary>
        public int Days;
    }

    /// <summary>
    /// G6: alle Formeln und Stellschrauben für Bodenwert, Ausbaustufen und Gebäudezustand an einem Ort
    /// (Platzhalter fürs Balancing, rein deterministisch, ohne Zufall).
    /// <para><b>Bodenwert</b> (0..1) = Grundwert + Σ Gewicht × Faktor − Abzüge, begrenzt auf 0..1:</para>
    /// <list type="bullet">
    /// <item>Grundwert 0,10; Straßenanbindung 0,18; Arbeitsplätze in Reichweite 0,14; Außenanschluss 0,04.</item>
    /// <item>Dienste 0,25 gesamt (Polizei 0,06, Feuerwehr 0,05, Gesundheit 0,05, Bildung 0,06, Bibliothek 0,03); Parks 0,12;
    /// Wahrzeichen 0,10 (Eigene Assets).</item>
    /// <item>Wasserlage 0,08, Aussicht 0,05, Dichte 0,06, Nachbarschaft ±0,10 (Zufriedenheit 0,6 = neutral, 1,0 = +0,10, 0,2 = −0,10).</item>
    /// <item>Abzüge: Lärm 0,25 × Lärm, Abgas 0,35 × Abgas, Müll 0,20 × Müllstand. Wasserzellen = 0.</item>
    /// </list>
    /// Der Zellwert läuft je Berechnung (≈ einmal am Spieltag) um <see cref="Smoothing"/> zum Zielwert (träge).
    /// </summary>
    public static class LandValueRules
    {
        // ------------------------------------------------------------ Bodenwert
        public const float BaseValue = 0.10f;
        public const float RoadWeight = 0.18f, JobWeight = 0.14f, ExternalWeight = 0.04f;
        public const float PoliceWeight = 0.06f, FireWeight = 0.05f, HealthWeight = 0.05f, EducationWeight = 0.06f, LibraryWeight = 0.03f;
        public const float ParkWeight = 0.12f;
        /// <summary>Eigene Assets: Wahrzeichen (Rathaus, Kirche, Museum, Burg …) im Umkreis, voll bei Abdeckung 1.</summary>
        public const float LandmarkWeight = 0.10f;
        public const float ShoreWeight = 0.08f, ViewWeight = 0.05f, DensityWeight = 0.06f;
        /// <summary>G3 erweitert: Wasserlage × (1 − Faktor × Verschmutzung); 1,5 → bei voller Verschmutzung −50 % der Uferprämie.</summary>
        public const float PollutedShoreFactor = 1.5f;
        /// <summary>Nachbarschaft: ± so viel bei Zufriedenheit 1,0 bzw. 0,2 (neutral 0,6).</summary>
        public const float NeighborhoodWeight = 0.10f;
        public const float NeighborhoodNeutral = 0.6f;
        public const float NoisePenalty = 0.25f, AirPenalty = 0.35f, GarbagePenalty = 0.20f;
        /// <summary>Sicherheit: Abzug bei vollem Brand-/Kriminalitätswert (frischer Großbrand nebenan bzw. Rate 1).</summary>
        public const float SafetyPenalty = 0.15f;

        /// <summary>Anteil, um den der Zellwert je Neuberechnung zum Ziel läuft.</summary>
        public const float Smoothing = 0.35f;

        // Reichweiten der Faktoren (m)
        public const float RoadFullDistance = 24f, RoadZeroDistance = 96f;
        public const float JobRadius = 800f;
        /// <summary>Arbeitsplätze (entfernungsgewichtet) für JobAccess ≈ 0,63 (Sättigung 1 − e^(−Jobs/Wert)).</summary>
        public const float JobSaturation = 250f;
        public const float ExternalZeroDistance = 2500f;
        public const float HighwayZeroDistance = 640f;
        public const float DensityRadius = 160f;
        public const float DensitySaturation = 60f;
        public const float NeighborhoodRadius = 160f;
        public const float GarbageRadius = 96f;
        public const float ShoreFullDistance = 8f, ShoreZeroDistance = 80f;
        public const float ViewRingDistance = 150f;
        /// <summary>Höhenunterschied (m) über dem Umgebungsmittel für volle Aussicht (die ersten 2 m zählen nicht).</summary>
        public const float ViewFullHeight = 20f;

        /// <summary>Bewertet die rohen Faktoren einer Zelle.</summary>
        public static LandValueFactors Evaluate(in LandValueInputs i)
        {
            var f = new LandValueFactors { Current = -1f, IsWater = i.IsWater };
            if (i.IsWater) return f;
            f.Base = BaseValue;
            f.RoadAccess = RoadWeight * C(i.RoadAccess);
            f.JobAccess = JobWeight * C(i.JobAccess);
            f.ExternalAccess = ExternalWeight * C(i.ExternalAccess);
            f.Services = PoliceWeight * C(i.Police) + FireWeight * C(i.Fire) + HealthWeight * C(i.Health)
                         + EducationWeight * C(i.Education) + LibraryWeight * C(i.Library);
            f.Park = ParkWeight * C(i.Park);
            f.Landmark = LandmarkWeight * C(i.Landmark); // Eigene Assets
            // G3 erweitert: verschmutztes Ufer → aus +0,08 wird bis zu −0,04 (stinkender Fluss statt Uferlage)
            f.Water = ShoreWeight * C(i.Shore) * (1f - PollutedShoreFactor * C(i.WaterPollution));
            f.View = ViewWeight * C(i.View);
            f.Density = DensityWeight * C(i.Density);
            f.Neighborhood = i.Neighborhood < 0f ? 0f
                : NeighborhoodWeight * Math.Clamp((i.Neighborhood - NeighborhoodNeutral) / 0.4f, -1f, 1f);
            f.Noise = -NoisePenalty * C(i.Noise);
            f.Air = -AirPenalty * C(i.Air);
            f.Garbage = -GarbagePenalty * C(i.Garbage);
            float sum = f.Base + f.RoadAccess + f.JobAccess + f.ExternalAccess + f.Services + f.Park + f.Water + f.View
                        + f.Density + f.Neighborhood + f.Noise + f.Air + f.Garbage;
            if (f.Landmark > 0f) sum += f.Landmark; // Eigene Assets (ohne Wahrzeichen bitgleich wie vorher)
            f.Safety = -SafetyPenalty * C(i.Safety);  // Sicherheit (ohne Brände/Kriminalität bitgleich wie vorher)
            if (f.Safety < 0f) sum += f.Safety;
            f.Contamination = -Environment.EcologyRules.ContaminationPenalty * C(i.Contamination); // Umwelt (ohne Altlasten bitgleich)
            if (f.Contamination < 0f) sum += f.Contamination;
            f.Target = C(sum);
            return f;
        }

        static float C(float v) => Math.Clamp(v, 0f, 1f);

        /// <summary>Linear 1 bis <paramref name="full"/>, 0 ab <paramref name="zero"/>.</summary>
        public static float Falloff(float distance, float full, float zero) =>
            distance <= full ? 1f : distance >= zero ? 0f : 1f - (distance - full) / (zero - full);

        /// <summary>Sättigung 1 − e^(−x/scale).</summary>
        public static float Saturate(float x, float scale) => x <= 0f ? 0f : 1f - MathF.Exp(-x / scale);

        /// <summary>
        /// Bauherren-Vorliebe für ein Grundstück 0..1 (G6-Hook im <see cref="CitySystem"/>): Wohnen, Handel und Büro wollen
        /// hohen Bodenwert; Industrie mag günstiges Land (1 − Bodenwert) und Autobahnnähe (je etwa zur Hälfte).
        /// </summary>
        public static float GrowthPreference(ZoneType zone, float landValue, float highwayAccess)
        {
            landValue = C(landValue);
            if (zone == ZoneType.Industry) return C(0.55f * (1f - landValue) + 0.45f * C(highwayAccess));
            return landValue;
        }

        /// <summary>Gewicht bei der Grundstückswahl: kleiner Sockel, damit auch schwache Lagen wachsen, sonst quadratisch.</summary>
        public static float GrowthWeight(float preference) => 0.05f + preference * preference;

        public static string ValueText(float v) =>
            v < 0f ? "unbekannt" : v >= 0.75f ? "sehr hoch" : v >= 0.55f ? "hoch" : v >= 0.35f ? "mittel" : v >= 0.2f ? "niedrig" : "sehr niedrig";

        // ------------------------------------------------------------ Ausbaustufen
        public const int MaxLevel = 3;

        /// <summary>Kapazitätsfaktor je Stufe (Wohnungen/Arbeitsplätze): 1 → 1,0; 2 → 1,4; 3 → 1,8.</summary>
        public static float CapacityFactor(int level) => 1f + 0.4f * (Math.Clamp(level, 1, MaxLevel) - 1);

        /// <summary>Wohnungen eines Grundstücks auf einer Stufe (Stufe 1 = Formel aus dem <see cref="CitySystem"/>).</summary>
        public static int HouseholdsFor(ZoneType zone, int cells, int level)
        {
            float per = Zones.HouseholdsPerCell(zone);
            return per > 0f ? Math.Max(1, (int)MathF.Round(per * cells * CapacityFactor(level))) : 0;
        }

        public static int JobsFor(ZoneType zone, int cells, int level) =>
            (int)MathF.Round(Zones.JobsPerCell(zone) * cells * CapacityFactor(level));

        /// <summary>Anforderungen für den Aufstieg auf <paramref name="targetLevel"/> (2 oder 3).</summary>
        public static LevelRequirement RequirementFor(ZoneType zone, int targetLevel)
        {
            bool top = targetLevel >= 3;
            var r = new LevelRequirement { Occupancy = 0.75f, Condition = 0.5f, Days = top ? 8 : 5 };
            switch (zone)
            {
                case ZoneType.Industry:
                    // Industrie braucht keine teure Lage, aber Fachkräfte
                    r.LandValue = top ? 0.40f : 0.25f; r.Satisfaction = top ? 0.64f : 0.56f; r.Skill = top ? 0.45f : 0.25f; break;
                case ZoneType.Office:
                    r.LandValue = top ? 0.62f : 0.45f; r.Satisfaction = top ? 0.66f : 0.58f; r.Skill = top ? 0.50f : 0.30f; break;
                case ZoneType.Retail:
                    r.LandValue = top ? 0.58f : 0.40f; r.Satisfaction = top ? 0.66f : 0.58f; r.Skill = top ? 0.35f : 0.20f; break;
                default: // Wohnen, Mischgebiet
                    r.LandValue = top ? 0.60f : 0.40f; r.Satisfaction = top ? 0.66f : 0.58f; r.Skill = top ? 0.55f : 0.35f; break;
            }
            return r;
        }

        /// <summary>Was fehlt für den Aufstieg? <see cref="LevelGap.None"/> = alles erfüllt.</summary>
        public static LevelGap Missing(ZoneType zone, int level, float condition, in BuildingFacts f)
        {
            if (level >= MaxLevel) return LevelGap.MaxLevel;
            var r = RequirementFor(zone, level + 1);
            var gap = LevelGap.None;
            if (f.LandValue < r.LandValue) gap |= LevelGap.LandValue;
            if (f.Satisfaction < r.Satisfaction) gap |= LevelGap.Satisfaction; // −1 (niemand da) fehlt immer
            if (!f.Supplied) gap |= LevelGap.Supply;
            if (f.Skill < r.Skill) gap |= LevelGap.Education;
            if (f.Occupancy < r.Occupancy) gap |= LevelGap.Occupancy;
            if (condition < r.Condition) gap |= LevelGap.Condition;
            return gap;
        }

        /// <summary>Netto-Fortschritt je Tag: erfüllt +1, sonst −2 (nicht unter 0) – kurze Ausrutscher kosten, löschen aber nicht alles.</summary>
        public static int NextProgress(int days, bool met) => met ? days + 1 : Math.Max(0, days - 2);

        /// <summary>So viele schlechte Tage (netto) führen zum Abstieg um eine Stufe.</summary>
        public const int DeclineDays = 10;

        /// <summary>
        /// Schlechter Tag für ein Gebäude ab Stufe 2: Bodenwert deutlich (0,12) unter der Anforderung der eigenen Stufe,
        /// Zufriedenheit &lt; 0,4 oder keine Versorgung.
        /// </summary>
        public static bool IsBadDay(ZoneType zone, int level, in BuildingFacts f)
        {
            if (level <= 1) return false;
            var own = RequirementFor(zone, level);
            return (f.LandValue >= 0f && f.LandValue < own.LandValue - 0.12f) || (f.Satisfaction >= 0f && f.Satisfaction < 0.4f) || !f.Supplied;
        }

        public static string LevelGapText(LevelGap g)
        {
            if (g == LevelGap.None) return "alles erfüllt";
            if ((g & LevelGap.MaxLevel) != 0) return "höchste Stufe";
            if ((g & LevelGap.UnderConstruction) != 0) return "im Bau";
            var parts = new List<string>(6);
            if ((g & LevelGap.LandValue) != 0) parts.Add("Bodenwert");
            if ((g & LevelGap.Satisfaction) != 0) parts.Add("Zufriedenheit");
            if ((g & LevelGap.Supply) != 0) parts.Add("Strom/Wasser");
            if ((g & LevelGap.Education) != 0) parts.Add("Bildung/Fachkräfte");
            if ((g & LevelGap.Occupancy) != 0) parts.Add("Belegung");
            if ((g & LevelGap.Condition) != 0) parts.Add("Zustand");
            if ((g & LevelGap.Needs) != 0) parts.Add("Einrichtungen in Reichweite");
            return string.Join(", ", parts);
        }

        // ------------------------------------------------------------ Zustand (0..1, 1 = neu)
        /// <summary>Alterung je Tag (≈ 500 Tage von neu bis verfallen ohne Instandhaltung).</summary>
        public const float AgeDecayPerDay = 0.002f;
        public const float NoSupplyDecayPerDay = 0.04f;
        /// <summary>Bei Müllstand 1.</summary>
        public const float GarbageDecayPerDay = 0.03f;
        public const float EmptyDecayPerDay = 0.01f;
        /// <summary>Leerstand zählt erst ab so vielen Tagen (neue Gebäude füllen sich erst).</summary>
        public const int EmptyGraceDays = 10;
        public const float UnhappyDecayPerDay = 0.01f;
        /// <summary>Instandhaltung je Tag, wenn es dem Gebäude gut geht (versorgt, kaum Müll, belegt, zufrieden, Lage ≥ 0,25).</summary>
        public const float MaintenancePerDay = 0.012f;

        /// <summary>Tägliche Änderung des Zustands.</summary>
        public static float ConditionDelta(in BuildingFacts f, int emptyDays)
        {
            float d = -AgeDecayPerDay;
            if (!f.Supplied) d -= NoSupplyDecayPerDay;
            d -= GarbageDecayPerDay * C(f.Garbage);
            bool empty = f.Occupancy <= 0f;
            if (empty && emptyDays > EmptyGraceDays) d -= EmptyDecayPerDay;
            if (f.Satisfaction >= 0f && f.Satisfaction < 0.35f) d -= UnhappyDecayPerDay;
            bool cared = f.Supplied && f.Garbage < 0.3f && !empty && (f.Satisfaction < 0f || f.Satisfaction >= 0.55f) && f.LandValue >= 0.25f;
            if (cared) d += MaintenancePerDay;
            return d;
        }

        public static string ConditionText(float c) =>
            c >= 0.85f ? "neuwertig" : c >= 0.65f ? "gepflegt" : c >= 0.45f ? "abgenutzt" : c >= 0.2f ? "heruntergekommen" : "verfallen";
    }
}
