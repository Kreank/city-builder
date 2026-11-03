using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Services;

namespace CityBuilder.Simulation.Zoning
{
    /// <summary>Art einer Anforderung eines gesetzten Wohnhauses.</summary>
    public enum HouseNeedKind
    {
        /// <summary>Strom am Haus (Ortsnetz versorgt).</summary>
        Power,
        Water,
        Sewage,
        /// <summary>Laden (Einzelhandel/Mischgebiet) mit Ware in Fußweite (<see cref="HouseRules.ShopWalkRadius"/>, Luftlinie).</summary>
        Shop,
        /// <summary>Dienst-Abdeckung (<c>world.Services.Coverage</c>) einer der <see cref="HouseNeed.ServiceKinds"/> ≥ Schwelle.</summary>
        Service,
    }

    /// <summary>Eine Anforderung (Bedürfnis) eines gesetzten Wohnhauses, z. B. „Grundschule in Reichweite“.</summary>
    public sealed class HouseNeed
    {
        /// <summary>Stabiler Schlüssel (Tests, Anzeige).</summary>
        public string Id { get; }
        public HouseNeedKind Kind { get; }
        /// <summary>Text für „Es fehlt: …“ und die ✓/✗-Liste.</summary>
        public string Label { get; }
        /// <summary>Kurzname (Inspektor-Spalte, Vorschau).</summary>
        public string ShortName { get; }
        /// <summary>Bei <see cref="HouseNeedKind.Service"/>: Abdeckung der besten dieser Arten zählt (z. B. Oberschule oder Bibliothek).</summary>
        public string[] ServiceKinds { get; }
        /// <summary>Mindestabdeckung 0..1 (Service).</summary>
        public float Threshold { get; }

        public HouseNeed(string id, HouseNeedKind kind, string shortName, string label, float threshold = 0f, params string[] serviceKinds)
        {
            Id = id;
            Kind = kind;
            ShortName = shortName;
            Label = label;
            Threshold = threshold;
            ServiceKinds = serviceKinds ?? new string[0];
        }

        public override string ToString() => Label;
    }

    /// <summary>Was für den Aufstieg auf <see cref="Level"/> neu dazukommt (die Anforderungen der Stufen davor gelten weiter).</summary>
    public sealed class HouseLevelRule
    {
        public int Level { get; }
        public string Name { get; }
        /// <summary>Neue harte Anforderungen dieser Stufe.</summary>
        public HouseNeed[] Needs { get; }
        /// <summary>Weiche Bedingung: Zufriedenheit der Bewohner (fehlt sie, pausiert der Fortschritt, kein Abstieg).</summary>
        public float Satisfaction { get; }
        /// <summary>Weiche Bedingung: Belegung (Wohnungen bewohnt).</summary>
        public float Occupancy { get; }
        /// <summary>Netto-Tage mit allem erfüllt bis zum Aufstieg.</summary>
        public int Days { get; }

        public HouseLevelRule(int level, string name, float satisfaction, float occupancy, int days, params HouseNeed[] needs)
        {
            Level = level;
            Name = name;
            Satisfaction = satisfaction;
            Occupancy = occupancy;
            Days = days;
            Needs = needs ?? new HouseNeed[0];
        }
    }

    /// <summary>Stand einer Anforderung an einem Haus bzw. Bauplatz.</summary>
    public struct HouseNeedStatus
    {
        public HouseNeed Need;
        public bool Met;
        /// <summary>Messwert: Abdeckung 0..1, Laden-Entfernung in m (−1 = keiner), Versorgung 1/0.</summary>
        public float Value;
        /// <summary>Erläuterung, z. B. „Abdeckung 45 % (nötig 25 %)“ oder „Laden in 320 m“.</summary>
        public string Detail;
        public string Mark => Met ? "✓" : "✗";
    }

    /// <summary>
    /// Anno-Regeln für gesetzte Wohnhäuser (datengetrieben): je Stufe eine Liste von Anforderungen über Einrichtungen in
    /// Reichweite. Aufstieg nach <see cref="HouseLevelRule.Days"/> netto erfüllten Tagen (+1 je Tag, fehlt eine harte
    /// Anforderung −2, fehlt nur Weiches: Pause). Fehlt eine Anforderung der <b>eigenen</b> Stufe, zählen schlechte Tage
    /// (<see cref="DeclineDays"/> netto → Abstieg). Kapazität je Stufe wie G6 (<see cref="LandValue.LandValueRules.CapacityFactor"/>).
    /// Weitere Stufen: einfach eine Regel an <see cref="Levels"/> anhängen.
    /// Zonengebäude bleiben bei den G6-Regeln (Bodenwert).
    /// </summary>
    public static class HouseRules
    {
        /// <summary>„Fußweite“ zum Laden (Luftlinie Zufahrt ↔ Ladenzufahrt).</summary>
        public const float ShopWalkRadius = 500f;
        /// <summary>Netto schlechte Tage (Anforderung der eigenen Stufe fehlt) bis zum Abstieg – schneller als G6, Anno-Gefühl.</summary>
        public const int DeclineDays = 6;
        /// <summary>Mindestabdeckung eines Dienstes für „in Reichweite“.</summary>
        public const float ServiceThreshold = 0.25f;

        public static readonly HouseNeed Power = new HouseNeed("power", HouseNeedKind.Power, "Strom", "Stromanschluss");
        public static readonly HouseNeed Water = new HouseNeed("water", HouseNeedKind.Water, "Wasser", "Wasseranschluss");
        public static readonly HouseNeed Sewage = new HouseNeed("sewage", HouseNeedKind.Sewage, "Abwasser", "Abwasseranschluss");
        public static readonly HouseNeed Shop = new HouseNeed("shop", HouseNeedKind.Shop, "Laden", "Laden mit Ware in Fußweite");
        public static readonly HouseNeed PrimarySchool = new HouseNeed("school_primary", HouseNeedKind.Service, "Grundschule",
            "Grundschule in Reichweite", ServiceThreshold, ServiceKinds.EducationPrimary);
        public static readonly HouseNeed Police = new HouseNeed("police", HouseNeedKind.Service, "Polizei",
            "Polizei in Reichweite", ServiceThreshold, ServiceKinds.Police);
        public static readonly HouseNeed Health = new HouseNeed("health", HouseNeedKind.Service, "Gesundheit",
            "Klinik/Krankenhaus in Reichweite", ServiceThreshold, ServiceKinds.Health);
        public static readonly HouseNeed Park = new HouseNeed("park", HouseNeedKind.Service, "Park",
            "Park in Fußweite", ServiceThreshold, ServiceKinds.Park);
        public static readonly HouseNeed HigherEducation = new HouseNeed("higher_education", HouseNeedKind.Service, "Oberschule/Bibliothek",
            "Oberschule oder Bibliothek in Reichweite", ServiceThreshold, ServiceKinds.EducationSecondary, ServiceKinds.Library);

        /// <summary>Stufenregeln ab Stufe 2 (Index 0 = Aufstieg 1 → 2).</summary>
        public static readonly HouseLevelRule[] Levels =
        {
            new HouseLevelRule(2, "gehoben", 0.55f, 0.75f, 3, Power, Water, Shop, PrimarySchool),
            new HouseLevelRule(3, "wohlhabend", 0.62f, 0.75f, 5, Police, Health, Sewage, Park, HigherEducation),
        };

        /// <summary>Höchste Stufe (1 + Anzahl Regeln).</summary>
        public static int MaxLevel => Levels.Length + 1;

        public static string LevelName(int level)
        {
            if (level <= 1) return "einfach";
            var r = RuleFor(level);
            return r?.Name ?? $"Stufe {level}";
        }

        /// <summary>Regel für den Aufstieg auf <paramref name="targetLevel"/> (null = gibt es nicht).</summary>
        public static HouseLevelRule RuleFor(int targetLevel)
        {
            int i = targetLevel - 2;
            return i >= 0 && i < Levels.Length ? Levels[i] : null;
        }

        /// <summary>Alle harten Anforderungen für Stufe <paramref name="level"/> (kumuliert über alle Regeln bis dahin, feste Reihenfolge).</summary>
        public static List<HouseNeed> NeedsFor(int level)
        {
            var list = new List<HouseNeed>();
            for (int l = 2; l <= level; l++)
            {
                var r = RuleFor(l);
                if (r != null) list.AddRange(r.Needs);
            }
            return list;
        }

        /// <summary>Alle Anforderungen aller Stufen (für Vorschau/Overlay).</summary>
        public static List<HouseNeed> AllNeeds() => NeedsFor(MaxLevel);

        /// <summary>Stufe, in der eine Anforderung dazukommt.</summary>
        public static int LevelOf(HouseNeed need)
        {
            foreach (var r in Levels) if (Array.IndexOf(r.Needs, need) >= 0) return r.Level;
            return 0;
        }

        /// <summary>Netto-Fortschritt: alles erfüllt +1; harte Anforderung fehlt −2 (nicht unter 0); nur Weiches fehlt: unverändert.</summary>
        public static int NextProgress(int days, bool hardMet, bool softMet) =>
            !hardMet ? Math.Max(0, days - 2) : softMet ? days + 1 : days;

        /// <summary>Höchste Stufe, deren harte Anforderungen alle erfüllt sind (Vorschau „erreichbare Stufe“).</summary>
        public static int ReachableLevel(IReadOnlyList<HouseNeedStatus> statuses)
        {
            int level = 1;
            for (int l = 2; l <= MaxLevel; l++)
            {
                var r = RuleFor(l);
                bool ok = true;
                foreach (var n in r.Needs)
                {
                    bool met = false;
                    foreach (var s in statuses) if (s.Need == n) { met = s.Met; break; }
                    if (!met) { ok = false; break; }
                }
                if (!ok) break;
                level = l;
            }
            return level;
        }

        /// <summary>„Es fehlt: Grundschule in Reichweite, Laden mit Ware in Fußweite“ (leer = nichts fehlt).</summary>
        public static string MissingText(IEnumerable<HouseNeedStatus> statuses)
        {
            var parts = new List<string>();
            foreach (var s in statuses) if (!s.Met) parts.Add(s.Need.Label);
            return parts.Count == 0 ? "" : "Es fehlt: " + string.Join(", ", parts);
        }
    }
}
