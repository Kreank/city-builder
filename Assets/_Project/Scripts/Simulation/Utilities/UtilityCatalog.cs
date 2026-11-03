using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>
    /// Versorgungsart. Jede Art hat ein eigenes Netz (Strom: Leitungen, Wasser: Rohre, Abwasser: Kanäle).
    /// Abwasser ist ein „umgekehrtes“ Netz: Gebäude erzeugen Abwasser (≈ Wasserverbrauch), Kläranlagen und Auslässe
    /// nehmen es ab (Kapazität). Ein Gebäude gilt als „versorgt“, wenn sein Abwasser abgenommen wird.
    /// </summary>
    public enum UtilityKind : byte
    {
        Power = 0,
        Water = 1,
        /// <summary>Abwasser (G3 erweitert): Kanäle wie Wasserrohre, Abnahme durch Kläranlage bzw. Auslass ins Gewässer.</summary>
        Sewage = 2,
    }

    /// <summary>Bauwerkstyp eines Versorgers (Kraftwerk, Pumpe ...). Werte sind Platzhalter fürs Balancing.</summary>
    public sealed class UtilityFacilityType
    {
        public string Id { get; }
        public string DisplayName { get; }
        public UtilityKind Kind { get; }
        /// <summary>Leistung: Strom in kW, Wasser in m³/h, Abwasser: abnehmbare Menge in m³/h.</summary>
        public float Capacity { get; }
        public float Width { get; }
        public float Depth { get; }
        /// <summary>Baukosten in € (abgebucht von der Stadtkasse, Abriss bringt einen Teil zurück).</summary>
        public float Cost { get; }
        /// <summary>Unterhalt in € pro Spieltag (täglich von der Stadtkasse abgebucht).</summary>
        public float UpkeepPerDay { get; }
        /// <summary>Lärm 0..1 und Radius in m (wird ins Lärm-Raster gestempelt).</summary>
        public float NoiseLevel { get; }
        public float NoiseRadius { get; }
        /// <summary>Abgas je Durchlauf (wird ins Abgas-Raster addiert).</summary>
        public float Pollution { get; }
        public float PollutionRadius { get; }
        /// <summary>Muss am Wasser stehen (Pumpe am Fluss, Abwasser-Auslass, Kühlwasser des Atomkraftwerks).</summary>
        public bool NeedsWater { get; }

        // --- Abwasser & Atom (G3 erweitert)
        /// <summary>
        /// Abwasser-Auslass: leitet ungeklärtes Abwasser ins Gewässer (verschmutzt flussabwärts,
        /// <see cref="WaterQualitySystem"/>). Kläranlagen haben false (klären vollständig).
        /// </summary>
        public bool Untreated { get; }
        /// <summary>
        /// Trotz <see cref="NeedsWater"/> eine trockene, eingeebnete Grundfläche (großes Bauwerk am Ufer, z. B. Atomkraftwerk):
        /// keine Ecke im Wasser, Gefälle wie bei normalen Bauwerken, wird eingeebnet.
        /// </summary>
        public bool DryFootprint { get; }
        /// <summary>
        /// Akzeptanz: Radius in m, in dem sich Bürger sorgen („Atomkraftwerk nebenan“, <see cref="UtilitySystem.ConcernAt"/>). 0 = keine Sorge.
        /// </summary>
        public float ConcernRadius { get; }
        // --- Ende Abwasser & Atom

        public UtilityFacilityType(string id, string name, UtilityKind kind, float capacity, float width, float depth, float cost,
            float upkeep, float noise = 0f, float noiseRadius = 0f, float pollution = 0f, float pollutionRadius = 0f, bool needsWater = false,
            bool untreated = false, bool dryFootprint = false, float concernRadius = 0f)
        {
            Id = id;
            DisplayName = name;
            Kind = kind;
            Capacity = capacity;
            Width = width;
            Depth = depth;
            Cost = cost;
            UpkeepPerDay = upkeep;
            NoiseLevel = noise;
            NoiseRadius = noiseRadius;
            Pollution = pollution;
            PollutionRadius = pollutionRadius;
            NeedsWater = needsWater;
            Untreated = untreated;
            DryFootprint = dryFootprint;
            ConcernRadius = concernRadius;
        }

        public override string ToString() => DisplayName;
    }

    /// <summary>Katalog der Versorger, Leitungskosten und Verbrauchswerte je Gebäude.</summary>
    public static class UtilityCatalog
    {
        public static readonly UtilityFacilityType CoalPlant = new UtilityFacilityType(
            "power_coal", "Kohlekraftwerk", UtilityKind.Power, 40000f, 48f, 36f, 60000f, 900f,
            noise: 0.6f, noiseRadius: 140f, pollution: 0.06f, pollutionRadius: 220f);

        public static readonly UtilityFacilityType GasPlant = new UtilityFacilityType(
            "power_gas", "Gaskraftwerk", UtilityKind.Power, 25000f, 36f, 28f, 45000f, 800f,
            noise: 0.45f, noiseRadius: 100f, pollution: 0.025f, pollutionRadius: 160f);

        public static readonly UtilityFacilityType WindTurbine = new UtilityFacilityType(
            "power_wind", "Windrad", UtilityKind.Power, 3000f, 12f, 12f, 12000f, 120f,
            noise: 0.25f, noiseRadius: 60f);

        public static readonly UtilityFacilityType SolarPark = new UtilityFacilityType(
            "power_solar", "Solarpark", UtilityKind.Power, 5000f, 40f, 32f, 20000f, 150f);

        // --- Atom (G3 erweitert): sehr große Leistung, sehr teuer, braucht Kühlwasser (Ufer), kein Abgas, Sorge im Umkreis
        public static readonly UtilityFacilityType NuclearPlant = new UtilityFacilityType(
            "power_nuclear", "Atomkraftwerk", UtilityKind.Power, 200000f, 72f, 56f, 400000f, 4500f,
            noise: 0.3f, noiseRadius: 120f, needsWater: true, dryFootprint: true, concernRadius: 1200f);
        // --- Ende Atom

        public static readonly UtilityFacilityType WaterPump = new UtilityFacilityType(
            "water_pump", "Wasserpumpe (am Fluss)", UtilityKind.Water, 600f, 16f, 16f, 15000f, 250f,
            noise: 0.2f, noiseRadius: 40f, needsWater: true);

        public static readonly UtilityFacilityType WaterTower = new UtilityFacilityType(
            "water_tower", "Brunnen + Wasserturm", UtilityKind.Water, 80f, 12f, 12f, 6000f, 80f);

        // --- Abwasser (G3 erweitert)
        public static readonly UtilityFacilityType SewagePlant = new UtilityFacilityType(
            "sewage_plant", "Kläranlage", UtilityKind.Sewage, 500f, 40f, 32f, 45000f, 500f,
            noise: 0.15f, noiseRadius: 50f);

        public static readonly UtilityFacilityType SewageOutlet = new UtilityFacilityType(
            "sewage_outlet", "Abwasser-Auslass (ungeklärt ins Gewässer)", UtilityKind.Sewage, 1500f, 8f, 8f, 3000f, 25f,
            needsWater: true, untreated: true);
        // --- Ende Abwasser

        public static readonly UtilityFacilityType[] All =
        {
            CoalPlant, GasPlant, WindTurbine, SolarPark, NuclearPlant, WaterPump, WaterTower, SewagePlant, SewageOutlet,
        };

        /// <summary>Kosten je Meter frei verlegter Leitung/Rohr/Kanal.</summary>
        public static float LineCostPerMeter(UtilityKind kind) => kind == UtilityKind.Power ? 15f : kind == UtilityKind.Sewage ? 12f : 10f;

        public static string KindName(UtilityKind kind) => kind == UtilityKind.Power ? "Strom" : kind == UtilityKind.Sewage ? "Abwasser" : "Wasser";
        public static string LineName(UtilityKind kind) => kind == UtilityKind.Power ? "Stromleitung" : kind == UtilityKind.Sewage ? "Abwasserkanal" : "Wasserrohr";
        public static string Unit(UtilityKind kind) => kind == UtilityKind.Power ? "kW" : "m³/h";

        /// <summary>Abwasser je Gebäude als Anteil seines Wasserverbrauchs (Rest versickert/verdunstet).</summary>
        public const float SewageShareOfWater = 0.9f;

        /// <summary>
        /// Verbrauch eines fertigen Gebäudes. Gerechnet mit der Kapazität (Haushalte/Arbeitsplätze), nicht mit der
        /// Belegung – so ändert sich der Bedarf nicht bei jedem Einzug (stabiler, gut planbar).
        /// Abwasser: erzeugte Menge ≈ <see cref="SewageShareOfWater"/> × Wasserverbrauch.
        /// </summary>
        public static float Demand(Building b, UtilityKind kind)
        {
            if (b == null || !b.IsConstructed) return 0f;
            if (kind == UtilityKind.Sewage) return Demand(b, UtilityKind.Water) * SewageShareOfWater;
            bool industry = b.Zone == ZoneType.Industry;
            float v = kind == UtilityKind.Power
                ? b.Households * 2f + b.Jobs * (industry ? 5f : 2f)
                : b.Households * 0.2f + b.Jobs * (industry ? 0.4f : 0.1f);
            return v > 0f ? v : (kind == UtilityKind.Power ? 1f : 0.1f);
        }
    }
}
