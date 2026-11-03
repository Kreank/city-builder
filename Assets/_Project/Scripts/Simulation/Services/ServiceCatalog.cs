using System;

namespace CityBuilder.Simulation.Services
{
    /// <summary>
    /// Dienstarten als String-Konstanten – verbindliche Schlüssel der Abfrage-API
    /// (<see cref="ServiceSystem.Coverage(string, Zoning.Building)"/>). Andere Systeme (Bedürfnisse, Budget, UI)
    /// docken genau über diese Werte an.
    /// </summary>
    public static class ServiceKinds
    {
        public const string Police = "police", Fire = "fire", Health = "health", Education = "education", Library = "library",
            Park = "park", Garbage = "garbage";

        /// <summary>
        /// Teilkanäle der Bildung (zusätzlich abfragbar). <see cref="Education"/> = Mittel aus beiden
        /// (nur Grundschulen in Reichweite → höchstens 0,5).
        /// </summary>
        public const string EducationPrimary = "education_primary", EducationSecondary = "education_secondary";

        /// <summary>Die sieben Hauptarten (Reihenfolge = Anzeige).</summary>
        public static readonly string[] All = { Police, Fire, Health, Education, Library, Park, Garbage };

        public static string DisplayName(string kind)
        {
            switch (kind)
            {
                case Police: return "Polizei";
                case Fire: return "Feuerwehr";
                case Health: return "Gesundheit";
                case Education: return "Bildung";
                case EducationPrimary: return "Grundschule";
                case EducationSecondary: return "Oberschule";
                case Library: return "Bibliothek";
                case Park: return "Parks";
                case Garbage: return "Müllabfuhr";
                default: return kind;
            }
        }
    }

    /// <summary>Gebäudetyp eines Dienstes. Werte sind Platzhalter fürs Balancing.</summary>
    public sealed class ServiceType
    {
        public string Id { get; }
        public string DisplayName { get; }
        /// <summary>Dienstart (<see cref="ServiceKinds"/>).</summary>
        public string Kind { get; }
        /// <summary>Kanal der Abdeckung: = <see cref="Kind"/>, bei Schulen Grund- bzw. Oberschule.</summary>
        public string Channel { get; }
        public float Width { get; }
        public float Depth { get; }
        /// <summary>
        /// Kapazität je Spieltag: versorgte Personen (Polizei, Feuerwehr, Gesundheit, Bibliothek, Park),
        /// Schüler (Schulen) bzw. Tonnen Müll (Müllhalde, Verbrennung).
        /// </summary>
        public float Capacity { get; }
        public string CapacityUnit { get; }
        /// <summary>
        /// Reichweite: Fahrzeit in Minuten über das Straßennetz – bei <see cref="Walking"/> Fußweg-Radius in Metern (Luftlinie).
        /// </summary>
        public float Range { get; }
        /// <summary>Wirkt über Fußweg-Radius (Luftlinie) statt über das Straßennetz (Parks).</summary>
        public bool Walking { get; }
        /// <summary>Baukosten in € (die Stadtkasse bucht ab – nicht dieses System).</summary>
        public float Cost { get; }
        /// <summary>Unterhalt in € pro Spielwoche (7 Spieltage).</summary>
        public float UpkeepPerWeek { get; }
        /// <summary>Lärm 0..1 und Radius in m (Sirenen, Pausenhof, Müllfahrzeuge …).</summary>
        public float NoiseLevel { get; }
        public float NoiseRadius { get; }
        /// <summary>Abgas je Durchlauf (Verbrennungsanlage).</summary>
        public float Pollution { get; }
        public float PollutionRadius { get; }
        /// <summary>Fassungsvermögen in t (Müllhalde: läuft voll, dann nimmt sie nichts mehr an). 0 = unbegrenzt.</summary>
        public float Storage { get; }

        public float UpkeepPerDay => UpkeepPerWeek / 7f;
        /// <summary>Reichweite in Sekunden Fahrzeit (nur Straßendienste).</summary>
        public float RangeSeconds => Range * 60f;

        public ServiceType(string id, string name, string kind, float width, float depth, float capacity, string unit, float range,
            float cost, float upkeepPerWeek, float noise = 0f, float noiseRadius = 0f, bool walking = false, string channel = null,
            float pollution = 0f, float pollutionRadius = 0f, float storage = 0f)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = name;
            Kind = kind;
            Channel = channel ?? kind;
            Width = width;
            Depth = depth;
            Capacity = capacity;
            CapacityUnit = unit;
            Range = range;
            Walking = walking;
            Cost = cost;
            UpkeepPerWeek = upkeepPerWeek;
            NoiseLevel = noise;
            NoiseRadius = noiseRadius;
            Pollution = pollution;
            PollutionRadius = pollutionRadius;
            Storage = storage;
        }

        public override string ToString() => DisplayName;
    }

    /// <summary>
    /// Katalog der Dienste (G4). Reichweite in Fahrminuten über das Straßennetz (Stadtstraße 50 km/h, mit
    /// <see cref="ServiceSystem.SpeedFactor"/> ≈ 670 m je Minute minus Kreuzungszuschlag); Parks über Fußweg-Radius.
    /// </summary>
    public static class ServiceCatalog
    {
        public static readonly ServiceType PoliceStation = new ServiceType(
            "police_station", "Polizeiwache", ServiceKinds.Police, 24f, 20f, 4000f, "Einwohner", 1.5f, 40000f, 2800f,
            noise: 0.2f, noiseRadius: 50f);

        public static readonly ServiceType FireStation = new ServiceType(
            "fire_station", "Feuerwache", ServiceKinds.Fire, 28f, 24f, 5000f, "Einwohner", 1.5f, 45000f, 3150f,
            noise: 0.3f, noiseRadius: 60f);

        public static readonly ServiceType Clinic = new ServiceType(
            "clinic", "Klinik (Ärztehaus)", ServiceKinds.Health, 16f, 16f, 1500f, "Patienten", 1f, 25000f, 1750f,
            noise: 0.05f, noiseRadius: 20f);

        public static readonly ServiceType Hospital = new ServiceType(
            "hospital", "Krankenhaus", ServiceKinds.Health, 40f, 32f, 8000f, "Patienten", 2.5f, 120000f, 8400f,
            noise: 0.25f, noiseRadius: 80f);

        public static readonly ServiceType PrimarySchool = new ServiceType(
            "school_primary", "Grundschule", ServiceKinds.Education, 32f, 24f, 600f, "Schüler", 1f, 35000f, 2450f,
            noise: 0.15f, noiseRadius: 40f, channel: ServiceKinds.EducationPrimary);

        public static readonly ServiceType HighSchool = new ServiceType(
            "school_high", "Gymnasium / Oberschule", ServiceKinds.Education, 40f, 32f, 1200f, "Schüler", 2f, 60000f, 4200f,
            noise: 0.15f, noiseRadius: 40f, channel: ServiceKinds.EducationSecondary);

        public static readonly ServiceType Library = new ServiceType(
            "library", "Bibliothek", ServiceKinds.Library, 20f, 16f, 3000f, "Leser", 1.5f, 20000f, 1050f);

        public static readonly ServiceType ParkSmall = new ServiceType(
            "park_small", "Park (klein)", ServiceKinds.Park, 16f, 16f, 800f, "Besucher", 200f, 3000f, 140f, walking: true);

        public static readonly ServiceType ParkLarge = new ServiceType(
            "park_large", "Park (groß)", ServiceKinds.Park, 48f, 40f, 4000f, "Besucher", 450f, 15000f, 560f, walking: true);

        public static readonly ServiceType Landfill = new ServiceType(
            "landfill", "Müllhalde", ServiceKinds.Garbage, 56f, 48f, 60f, "t/Tag", 3f, 20000f, 2100f,
            noise: 0.3f, noiseRadius: 100f, storage: 25000f);

        public static readonly ServiceType Incinerator = new ServiceType(
            "incinerator", "Müllverbrennungsanlage", ServiceKinds.Garbage, 40f, 32f, 120f, "t/Tag", 3f, 70000f, 6300f,
            noise: 0.4f, noiseRadius: 100f, pollution: 0.03f, pollutionRadius: 180f);

        public static readonly ServiceType[] All =
        {
            PoliceStation, FireStation, Clinic, Hospital, PrimarySchool, HighSchool, Library, ParkSmall, ParkLarge, Landfill, Incinerator,
        };

        public static ServiceType Get(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            return null;
        }
    }
}
