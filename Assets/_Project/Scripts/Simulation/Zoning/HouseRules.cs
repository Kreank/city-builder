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
        // --- Setzen statt Zonen: Anforderungen für Läden, Büros und Hallen
        /// <summary>Kundschaft: Einwohner in <see cref="HouseRules.CustomerRadius"/> (Luftlinie) ≥ <see cref="HouseNeed.Threshold"/>.</summary>
        Customers,
        /// <summary>Fachkräfte: Anteil der Beschäftigten mit passendem Abschluss ≥ Schwelle (Büro: Studium, sonst mittlerer Abschluss).</summary>
        Skilled,
        /// <summary>Güteranschluss: Autobahn/Zubringer in der Nähe (<c>LandValue.HighwayAccessAt</c> ≥ Schwelle).</summary>
        Freight,
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

        /// <summary>Stufenregeln der Wohnhäuser ab Stufe 2 (Index 0 = Aufstieg 1 → 2). Gilt auch fürs Wohn- und Geschäftshaus.</summary>
        public static readonly HouseLevelRule[] Levels =
        {
            new HouseLevelRule(2, "gehoben", 0.55f, 0.75f, 3, Power, Water, Shop, PrimarySchool),
            new HouseLevelRule(3, "wohlhabend", 0.62f, 0.75f, 5, Police, Health, Sewage, Park, HigherEducation),
        };

        // --- Setzen statt Zonen: Anforderungen für gesetzte Läden, Büros und Hallen (datengetrieben wie die Wohnhäuser).
        // Weiche Bedingungen: Zufriedenheit der Beschäftigten, Besetzung der Arbeitsplätze.
        /// <summary>Einzugsgebiet für „Kundschaft“ (Luftlinie Zufahrt ↔ Zufahrt der Wohngebäude).</summary>
        public const float CustomerRadius = 600f;

        public static readonly HouseNeed CustomersFew = new HouseNeed("customers_150", HouseNeedKind.Customers, "Kundschaft",
            "150 Einwohner im Umkreis von 600 m", 150f);
        public static readonly HouseNeed CustomersMany = new HouseNeed("customers_500", HouseNeedKind.Customers, "viel Kundschaft",
            "500 Einwohner im Umkreis von 600 m", 500f);
        public static readonly HouseNeed OfficeStaff = new HouseNeed("skilled_office_30", HouseNeedKind.Skilled, "Fachkräfte",
            "30 % der Beschäftigten mit Studium", 0.3f);
        public static readonly HouseNeed OfficeStaffMany = new HouseNeed("skilled_office_55", HouseNeedKind.Skilled, "viele Fachkräfte",
            "55 % der Beschäftigten mit Studium", 0.55f);
        public static readonly HouseNeed IndustryStaff = new HouseNeed("skilled_industry_40", HouseNeedKind.Skilled, "Facharbeiter",
            "40 % der Beschäftigten mit mittlerem Abschluss", 0.4f);
        public static readonly HouseNeed FreightAccess = new HouseNeed("freight", HouseNeedKind.Freight, "Güteranschluss",
            "Autobahn in der Nähe (Güteranschluss)", 0.2f);
        public static readonly HouseNeed Fire = new HouseNeed("fire", HouseNeedKind.Service, "Feuerwehr",
            "Feuerwehr in Reichweite", ServiceThreshold, ServiceKinds.Fire);
        public static readonly HouseNeed Library = new HouseNeed("library", HouseNeedKind.Service, "Bibliothek/Oberschule",
            "Bibliothek oder Oberschule in Reichweite", ServiceThreshold, ServiceKinds.Library, ServiceKinds.EducationSecondary);

        /// <summary>Laden: Stufe 2 „belebt“ Versorgung + Kundschaft, Stufe 3 „gefragt“ viel Kundschaft, Abwasser, Polizei.</summary>
        public static readonly HouseLevelRule[] ShopLevels =
        {
            new HouseLevelRule(2, "belebt", 0.5f, 0.75f, 3, Power, Water, CustomersFew),
            new HouseLevelRule(3, "gefragt", 0.58f, 0.75f, 5, Sewage, Police, CustomersMany),
        };

        /// <summary>Büro: Stufe 2 „modern“ Versorgung + Fachkräfte, Stufe 3 „repräsentativ“ + Abwasser, Polizei, Bibliothek, viele Fachkräfte.</summary>
        public static readonly HouseLevelRule[] OfficeLevels =
        {
            new HouseLevelRule(2, "modern", 0.5f, 0.75f, 3, Power, Water, OfficeStaff),
            new HouseLevelRule(3, "repräsentativ", 0.6f, 0.75f, 5, Sewage, Police, Library, OfficeStaffMany),
        };

        /// <summary>Halle: Stufe 2 „ausgebaut“ Versorgung + Güteranschluss, Stufe 3 „Großbetrieb“ + Abwasser, Feuerwehr, Facharbeiter.</summary>
        public static readonly HouseLevelRule[] IndustryLevels =
        {
            new HouseLevelRule(2, "ausgebaut", 0.45f, 0.75f, 3, Power, Water, FreightAccess),
            new HouseLevelRule(3, "Großbetrieb", 0.55f, 0.75f, 5, Sewage, Fire, IndustryStaff),
        };

        /// <summary>Stufenregeln einer Zonenart (Wohnen und Mischgebiet: <see cref="Levels"/>).</summary>
        public static HouseLevelRule[] LevelsFor(ZoneType zone)
        {
            switch (zone)
            {
                case ZoneType.Retail: return ShopLevels;
                case ZoneType.Office: return OfficeLevels;
                case ZoneType.Industry: return IndustryLevels;
                default: return Levels;
            }
        }

        public static int MaxLevelFor(ZoneType zone) => LevelsFor(zone).Length + 1;

        public static string LevelName(ZoneType zone, int level)
        {
            if (level <= 1) return "einfach";
            var r = RuleFor(zone, level);
            return r?.Name ?? $"Stufe {level}";
        }

        public static HouseLevelRule RuleFor(ZoneType zone, int targetLevel)
        {
            var levels = LevelsFor(zone);
            int i = targetLevel - 2;
            return i >= 0 && i < levels.Length ? levels[i] : null;
        }

        public static List<HouseNeed> NeedsFor(ZoneType zone, int level)
        {
            var list = new List<HouseNeed>();
            for (int l = 2; l <= level; l++)
            {
                var r = RuleFor(zone, l);
                if (r != null) list.AddRange(r.Needs);
            }
            return list;
        }

        public static List<HouseNeed> AllNeeds(ZoneType zone) => NeedsFor(zone, MaxLevelFor(zone));

        public static int LevelOf(ZoneType zone, HouseNeed need)
        {
            foreach (var r in LevelsFor(zone)) if (Array.IndexOf(r.Needs, need) >= 0) return r.Level;
            return 0;
        }

        public static int ReachableLevel(ZoneType zone, IReadOnlyList<HouseNeedStatus> statuses)
        {
            int level = 1;
            for (int l = 2; l <= MaxLevelFor(zone); l++)
            {
                var r = RuleFor(zone, l);
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
        // --- Ende Setzen statt Zonen

        /// <summary>Höchste Stufe der Wohnhäuser (1 + Anzahl Regeln).</summary>
        public static int MaxLevel => Levels.Length + 1;

        public static string LevelName(int level) => LevelName(ZoneType.ResidentialLow, level);

        /// <summary>Wohnhaus-Regel für den Aufstieg auf <paramref name="targetLevel"/> (null = gibt es nicht).</summary>
        public static HouseLevelRule RuleFor(int targetLevel) => RuleFor(ZoneType.ResidentialLow, targetLevel);

        /// <summary>Alle harten Anforderungen eines Wohnhauses für Stufe <paramref name="level"/> (kumuliert, feste Reihenfolge).</summary>
        public static List<HouseNeed> NeedsFor(int level) => NeedsFor(ZoneType.ResidentialLow, level);

        /// <summary>Alle Anforderungen aller Wohnhaus-Stufen (für Vorschau/Overlay).</summary>
        public static List<HouseNeed> AllNeeds() => NeedsFor(MaxLevel);

        /// <summary>Stufe, in der eine Anforderung (Wohnhaus) dazukommt.</summary>
        public static int LevelOf(HouseNeed need) => LevelOf(ZoneType.ResidentialLow, need);

        /// <summary>Netto-Fortschritt: alles erfüllt +1; harte Anforderung fehlt −2 (nicht unter 0); nur Weiches fehlt: unverändert.</summary>
        public static int NextProgress(int days, bool hardMet, bool softMet) =>
            !hardMet ? Math.Max(0, days - 2) : softMet ? days + 1 : days;

        /// <summary>Höchste Wohnhaus-Stufe, deren harte Anforderungen alle erfüllt sind (Vorschau „erreichbare Stufe“).</summary>
        public static int ReachableLevel(IReadOnlyList<HouseNeedStatus> statuses) => ReachableLevel(ZoneType.ResidentialLow, statuses);

        /// <summary>„Es fehlt: Grundschule in Reichweite, Laden mit Ware in Fußweite“ (leer = nichts fehlt).</summary>
        public static string MissingText(IEnumerable<HouseNeedStatus> statuses)
        {
            var parts = new List<string>();
            foreach (var s in statuses) if (!s.Met) parts.Add(s.Need.Label);
            return parts.Count == 0 ? "" : "Es fehlt: " + string.Join(", ", parts);
        }
    }
}
