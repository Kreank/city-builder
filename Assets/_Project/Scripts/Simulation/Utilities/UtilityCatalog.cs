using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>Versorgungsart. Jede Art hat ein eigenes Netz (Strom: Leitungen, Wasser: Rohre).</summary>
    public enum UtilityKind : byte
    {
        Power = 0,
        Water = 1,
    }

    /// <summary>Bauwerkstyp eines Versorgers (Kraftwerk, Pumpe ...). Werte sind Platzhalter fürs Balancing.</summary>
    public sealed class UtilityFacilityType
    {
        public string Id { get; }
        public string DisplayName { get; }
        public UtilityKind Kind { get; }
        /// <summary>Leistung: Strom in kW, Wasser in m³/h.</summary>
        public float Capacity { get; }
        public float Width { get; }
        public float Depth { get; }
        /// <summary>Baukosten in € (noch ohne Stadtkasse – nur Anzeige).</summary>
        public float Cost { get; }
        /// <summary>Unterhalt in € pro Spieltag (nur Anzeige).</summary>
        public float UpkeepPerDay { get; }
        /// <summary>Lärm 0..1 und Radius in m (wird ins Lärm-Raster gestempelt).</summary>
        public float NoiseLevel { get; }
        public float NoiseRadius { get; }
        /// <summary>Abgas je Durchlauf (wird ins Abgas-Raster addiert).</summary>
        public float Pollution { get; }
        public float PollutionRadius { get; }
        /// <summary>Muss am Wasser stehen (Pumpe am Fluss).</summary>
        public bool NeedsWater { get; }

        public UtilityFacilityType(string id, string name, UtilityKind kind, float capacity, float width, float depth, float cost,
            float upkeep, float noise = 0f, float noiseRadius = 0f, float pollution = 0f, float pollutionRadius = 0f, bool needsWater = false)
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

        public static readonly UtilityFacilityType WaterPump = new UtilityFacilityType(
            "water_pump", "Wasserpumpe (am Fluss)", UtilityKind.Water, 600f, 16f, 16f, 15000f, 250f,
            noise: 0.2f, noiseRadius: 40f, needsWater: true);

        public static readonly UtilityFacilityType WaterTower = new UtilityFacilityType(
            "water_tower", "Brunnen + Wasserturm", UtilityKind.Water, 80f, 12f, 12f, 6000f, 80f);

        public static readonly UtilityFacilityType[] All =
        {
            CoalPlant, GasPlant, WindTurbine, SolarPark, WaterPump, WaterTower,
        };

        /// <summary>Kosten je Meter frei verlegter Leitung/Rohr.</summary>
        public static float LineCostPerMeter(UtilityKind kind) => kind == UtilityKind.Power ? 15f : 10f;

        public static string KindName(UtilityKind kind) => kind == UtilityKind.Power ? "Strom" : "Wasser";
        public static string LineName(UtilityKind kind) => kind == UtilityKind.Power ? "Stromleitung" : "Wasserrohr";
        public static string Unit(UtilityKind kind) => kind == UtilityKind.Power ? "kW" : "m³/h";

        /// <summary>
        /// Verbrauch eines fertigen Gebäudes. Gerechnet mit der Kapazität (Haushalte/Arbeitsplätze), nicht mit der
        /// Belegung – so ändert sich der Bedarf nicht bei jedem Einzug (stabiler, gut planbar).
        /// </summary>
        public static float Demand(Building b, UtilityKind kind)
        {
            if (b == null || !b.IsConstructed) return 0f;
            bool industry = b.Zone == ZoneType.Industry;
            float v = kind == UtilityKind.Power
                ? b.Households * 2f + b.Jobs * (industry ? 5f : 2f)
                : b.Households * 0.2f + b.Jobs * (industry ? 0.4f : 0.1f);
            return v > 0f ? v : (kind == UtilityKind.Power ? 1f : 0.1f);
        }
    }
}
