using System;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Safety
{
    /// <summary>
    /// Alle Zahlen für Brände und Kriminalität an einem Ort (Platzhalter fürs Balancing, rein deterministisch).
    /// Zeiten in Spielstunden (1 Tick = 0,1 h), Wahrscheinlichkeiten je Tick bzw. Stunde.
    /// </summary>
    public static class SafetyRules
    {
        // ------------------------------------------------------------ Brand: Entstehung
        /// <summary>
        /// Brände je Gebäude und Tag bei Brandgefahr-Faktor 1 (≈ 5 % je Spieljahr; real ~1 %, fürs Spiel verdichtet). Eine gepflegte
        /// Stadt mit 1 000 Gebäuden (Ø Faktor ≈ 1,3) hat so etwa alle 5 Spieltage einen Brand (Balance-Info-Test).
        /// </summary>
        public const float IgnitionsPerDay = 0.00015f;
        /// <summary>Faktor je Bauart (1 = Einfamilienhaus).</summary>
        public static float ZoneFactor(ZoneType z)
        {
            switch (z)
            {
                case ZoneType.ResidentialLow: return 1.0f;
                case ZoneType.ResidentialMedium: return 1.15f;
                case ZoneType.ResidentialHigh: return 1.3f;
                case ZoneType.Retail: return 1.1f;
                case ZoneType.Mixed: return 1.2f;
                case ZoneType.Office: return 0.8f;
                case ZoneType.Industry: return 1.8f;
                default: return 1f;
            }
        }

        /// <summary>Faktor je Produktionsrezept (Raffinerie, Ölförderung, Sägewerk … brennen eher).</summary>
        public static float RecipeFactor(string recipeId)
        {
            switch (recipeId)
            {
                case "refinery": return 2.2f;
                case "oil_well": return 1.8f;
                case "sawmill": return 1.6f;
                case "coal_mine": return 1.5f;
                case "furniture":
                case "textile": return 1.4f;
                case "steelworks":
                case "glassworks": return 1.3f;
                case "food_factory":
                case "mill": return 1.2f;
                default: return 1f;
            }
        }

        /// <summary>Wie stark ein Brand wächst (Brandlast) – Industrie und Holz/Öl schneller.</summary>
        public static float Flammability(ZoneType z, string recipeId) =>
            (z == ZoneType.Industry ? 1.25f : z == ZoneType.Office ? 0.85f : 1f) * (recipeId == "refinery" || recipeId == "oil_well" ? 1.3f : recipeId == "sawmill" ? 1.2f : 1f);

        /// <summary>Alter/Zustand: × (1 + so viel × (1 − Zustand)).</summary>
        public const float ConditionWeight = 1.2f;
        /// <summary>Liegender Müll: × (1 + so viel × Müllstand).</summary>
        public const float GarbageWeight = 0.8f;
        /// <summary>Stromausfall (Kerzen, Notstrom): × so viel.</summary>
        public const float PowerOutageFactor = 1.25f;
        /// <summary>Brandschutz (Begehungen der Feuerwehr): × (1 − so viel × Abdeckung).</summary>
        public const float PreventionWeight = 0.35f;
        /// <summary>Größe: × (0,6 + 0,1 × Zellen) – 2×2 = 1, 4×4 = 2,2.</summary>
        public static float SizeFactor(Building b) => 0.6f + 0.1f * b.WidthCells * b.DepthCells;

        // ------------------------------------------------------------ Brand: Verlauf
        /// <summary>Stärke beim Entstehen.</summary>
        public const float StartIntensity = 0.1f;
        /// <summary>Wachstumsrate je Stunde (logistisch: r × I × (1 − I) + Grundwachstum) – ohne Feuerwehr Vollbrand nach ≈ 3 h.</summary>
        public const float GrowthPerHour = 1.5f;
        public const float BaseGrowthPerHour = 0.03f;
        /// <summary>Löschleistung je Löschfahrzeug vor Ort und Stunde (Stärke), × Wasser-Faktor.</summary>
        public const float SuppressionPerTruckHour = 0.9f;
        /// <summary>Ohne Wasser am Gebäude (kein Hydrant/Wassernetz) nur Tankwasser: Löschleistung × so viel.</summary>
        public const float TankWaterFactor = 0.35f;
        /// <summary>Schaden je Stunde bei Stärke 1 (Totalschaden ohne Feuerwehr nach ≈ 5 h).</summary>
        public const float DamagePerHour = 0.28f;
        /// <summary>Zustand sinkt um so viel je Schadenspunkt (Gebäudezustand G6), nie unter <see cref="MinCondition"/>.</summary>
        public const float ConditionLossPerDamage = 1.1f;
        public const float MinCondition = 0.05f;
        /// <summary>Unter dieser Stärke gilt der Brand als gelöscht.</summary>
        public const float OutBelow = 0.01f;
        /// <summary>Entstehungsbrand (Stärke darunter) mit Menschen im Haus: Chance je Tick, dass sie ihn selbst löschen.</summary>
        public const float SelfExtinguishBelow = 0.16f;
        public const float SelfExtinguishPerTick = 0.06f;
        /// <summary>Notruf: Ticks bis ein Brand bemerkt wird (mit Menschen im Gebäude bzw. leer).</summary>
        public const int DetectTicksOccupied = 1, DetectTicksEmpty = 3;
        /// <summary>Alarmierung bis Ausrücken (Ticks) im Rückfall ohne Dienstfahrzeuge.</summary>
        public const int FallbackAlarmTicks = 1;
        /// <summary>Löschfahrzeuge je Brand: ab dieser Stärke 2 bzw. 3.</summary>
        public const float SecondTruckAt = 0.35f, ThirdTruckAt = 0.6f;

        // ------------------------------------------------------------ Brand: Übergreifen
        /// <summary>Abstand (Kante zu Kante), bis zu dem ein Brand übergreifen kann.</summary>
        public const float SpreadDistance = 12f;
        /// <summary>Ab dieser Stärke kann der Brand übergreifen.</summary>
        public const float SpreadFrom = 0.45f;
        /// <summary>Chance je Tick und Nachbar bei Vollbrand direkt daneben (Reihenhaus) – ≈ 25 % je Stunde.</summary>
        public const float SpreadPerTick = 0.03f;
        /// <summary>Feuerwehr vor Ort kühlt die Nachbarn: Übergreifen × so viel.</summary>
        public const float SpreadWithTrucks = 0.3f;

        // ------------------------------------------------------------ Brand: Folgen
        /// <summary>Ab diesem Schaden ist das Gebäude unbewohnbar (Sanierung, Bewohner ziehen vorübergehend aus).</summary>
        public const float SevereDamage = 0.4f;
        /// <summary>Sanierung: Tage = Grund + Faktor × Schaden.</summary>
        public const float RepairBaseDays = 1f, RepairDaysPerDamage = 6f;
        /// <summary>Wiederaufbau einer Ruine (gesetztes Gebäude): Anteil der Baukosten (Rest trägt die Versicherung) und Dauer.</summary>
        public const float RebuildCostShare = 0.6f;
        public const float RebuildDays = 3f;
        /// <summary>Verletzte: Chance je anwesender Person = Grund + Faktor × Stärke, höchstens so viele je Brand.</summary>
        public const float InjuryBase = 0.01f, InjuryPerIntensity = 0.12f;
        public const int MaxInjuredPerFire = 4;
        /// <summary>Gesundheit sinkt um Grund + Faktor × Stärke.</summary>
        public const float InjurySeverityBase = 0.25f, InjurySeverityPerIntensity = 0.2f;
        /// <summary>Nachbarn im Umkreis bekommen eine (schwache) Brand-Erinnerung.</summary>
        public const float NeighborMemoryRadius = 80f;
        /// <summary>Bodenwert: Wirkung frischer Brände (Radius, Abklingen in Tagen).</summary>
        public const float ScarRadius = 120f, ScarDays = 6f, ScarMaxAgeDays = 20f;
        /// <summary>Lager der Firma brennt anteilig mit (Schaden × so viel).</summary>
        public const float StockLossPerDamage = 1f;

        // ------------------------------------------------------------ Kriminalität
        /// <summary>Rasterweite für Viertel-Werte (Arbeitslosigkeit, Zufriedenheit, Bildung, Dichte, Streifenpräsenz).</summary>
        public const float CrimeCell = 96f;
        /// <summary>Gewichte der Faktoren (Druck = Grund + Σ Gewicht × Faktor).</summary>
        public const float CrimeBase = 0.06f, UnemploymentWeight = 0.30f, DissatisfactionWeight = 0.22f, EducationWeight = 0.12f,
                           DensityWeight = 0.10f, LowLandValueWeight = 0.12f, ShopWeight = 0.05f;
        /// <summary>Polizeiabdeckung senkt um bis zu so viel, Streifenpräsenz zusätzlich um bis zu so viel.</summary>
        public const float PoliceReduction = 0.6f, PatrolReduction = 0.35f;
        /// <summary>Normierung: Arbeitslosenquote 25 % = 1; Zufriedenheit 0,7 → 0, 0,2 → 1; Anteil ohne Abschluss 20 % → 0, 70 % → 1; Dichte.</summary>
        public const float UnemploymentFull = 0.25f, SatisfactionNeutral = 0.7f, LowEducationNeutral = 0.2f, DensityFull = 600f;
        /// <summary>Straftaten je Gebäude und Tag bei Rate 1 und Größe 1.</summary>
        public const float CrimesPerDay = 0.03f;
        /// <summary>Streifenpräsenz: je Streifen-Halt +1 in der Zelle (Nachbarzellen +0,4), Abklingen je Stunde, Präsenz = 1 − e^(−x/2).</summary>
        public const float PatrolDecayPerHour = 0.8f, PatrolNeighborShare = 0.4f, PatrolScale = 2f;
        /// <summary>Anzeige bei der Polizei (Chance je Art).</summary>
        public static float ReportChance(CrimeKind k) => k == CrimeKind.Burglary ? 0.85f : k == CrimeKind.Shoplifting ? 0.5f : 0.6f;
        /// <summary>Grund-Aufklärungsquote je Art (× (0,5 + 0,5 × Abdeckung) + schnelle Ankunft).</summary>
        public static float SolveBase(CrimeKind k) => k == CrimeKind.Burglary ? 0.25f : k == CrimeKind.Shoplifting ? 0.45f : 0.15f;
        /// <summary>Bonus für schnelle Ankunft (voll bei 0 min, 0 ab 45 Spielminuten).</summary>
        public const float FastResponseBonus = 0.25f, FastResponseMinutes = 45f;
        /// <summary>Ohne Streifenwagen am Tatort: nur spätere Ermittlung.</summary>
        public const float LateInvestigationShare = 0.5f;
        /// <summary>Zellen je Polizeiwache (Platzhalter bis zum Gefängnis) und täglicher Abgang (Gericht/Überstellung).</summary>
        public const int HoldingCellsPerStation = 20;
        public const float CustodyReleasePerDay = 0.3f;
        /// <summary>Schlagzeile „Einbruchserie“ ab so vielen Taten an einem Tag rund um eine Straße (Zelle 3×3).</summary>
        public const int CrimeWaveCount = 6;

        /// <summary>
        /// Kriminalitätsrate aus den Faktoren: Druck = Grund + Σ Gewicht × Faktor (Arbeitslosigkeit, Unzufriedenheit, Bildung,
        /// Dichte, niedriger Bodenwert, Läden), dann × (1 − 0,6 × Polizei) × (1 − 0,35 × Streife). Setzt auch <c>Pressure</c>.
        /// </summary>
        public static float CrimeRate(ref CrimeFactors f)
        {
            f.Pressure = CrimeBase + UnemploymentWeight * f.Unemployment + DissatisfactionWeight * f.Dissatisfaction
                         + EducationWeight * f.LowEducation + DensityWeight * f.Density + LowLandValueWeight * f.LowLandValue + ShopWeight * f.Shops;
            return Math.Clamp(f.Pressure * (1f - PoliceReduction * Math.Clamp(f.Police, 0f, 1f)) * (1f - PatrolReduction * Math.Clamp(f.Patrol, 0f, 1f)), 0f, 1f);
        }

        /// <summary>Tageszeit-Gewicht der Straftaten (nachts mehr), Mittel ≈ 1.</summary>
        public static float NightWeight(int hour)
        {
            if (hour < 5) return 1.8f;
            if (hour < 8) return 0.6f;
            if (hour < 18) return 0.7f;
            if (hour < 22) return 1.1f;
            return 1.6f;
        }

        /// <summary>Größe für Straftaten: mehr Wohnungen/Arbeitsplätze = mehr Gelegenheiten.</summary>
        public static float CrimeSize(Building b) => Math.Clamp(0.4f + (b.Households + b.Jobs) / 15f, 0.4f, 3f);

        // ------------------------------------------------------------ Texte

        public static string RiskText(float factor) =>
            factor >= 3f ? "sehr hoch" : factor >= 2f ? "hoch" : factor >= 1.2f ? "erhöht" : factor >= 0.7f ? "normal" : "gering";

        public static string CrimeText(float rate) =>
            rate >= 0.45f ? "sehr hoch" : rate >= 0.3f ? "hoch" : rate >= 0.18f ? "mittel" : rate >= 0.08f ? "gering" : "sehr gering";

        /// <summary>Stärke als Wort.</summary>
        public static string IntensityText(float i) =>
            i >= 0.8f ? "Vollbrand" : i >= 0.5f ? "Großbrand" : i >= 0.25f ? "Mittelbrand" : "Entstehungsbrand";

        /// <summary>Deterministischer Würfel 0..1 aus Ids (verbraucht keinen Zufallsstrom).</summary>
        public static double Roll(int a, long b, int salt) =>
            Citizens.SimRandom.Hash(unchecked(a * 31 + salt), unchecked((int)b ^ (int)(b >> 32) * 7919)) / (double)uint.MaxValue;

        public static int TicksForDays(float days) => (int)MathF.Ceiling(days * TimeModel.TicksPerDay);
    }
}
