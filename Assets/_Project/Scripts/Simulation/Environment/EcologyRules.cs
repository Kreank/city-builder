using System;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>Emissionsquellen der Luft (Statistik/Inspektor).</summary>
    public enum EmissionSource { Traffic, Power, Industry, Heating, Waste }

    /// <summary>
    /// Umwelt (Etappe 6): alle Zahlen des <see cref="EcologySystem"/> an einem Ort (Spielmaßstab, Platzhalter fürs Balancing).
    /// Einheit der Luft wie bisher: Rasterwert ohne Einheit, 0 = sauber, ab ~0,4 Smog (Bürger: Ruhe −1,6 × Wert).
    /// Ein Durchlauf = 30 Spielminuten (5 Ticks).
    /// </summary>
    public static class EcologyRules
    {
        // ------------------------------------------------------------ Luft: Quellen (je Durchlauf)

        /// <summary>Abgas je Fahrzeug, das im Mittel des Durchlaufs in einer Zelle steht/fährt (Pkw = 1).</summary>
        public const float TrafficPerVehicle = 0.06f;
        /// <summary>Stop-and-go/Stau (unter 2 m/s) stößt mehr aus.</summary>
        public const float SlowFactor = 1.6f, SlowSpeed = 2f;

        public static float VehicleWeight(VehicleClass c)
        {
            switch (c)
            {
                case VehicleClass.Car: return 1f;
                case VehicleClass.Truck: return 3.5f; // Diesel-Lkw
                case VehicleClass.Bus: return 2.2f;
                default: return 0f;                    // Tram elektrisch
            }
        }

        /// <summary>Umweltzone: nur saubere Fahrzeuge – Lkw stoßen dort so viel aus, Pkw so viel.</summary>
        public const float ZoneTruckFactor = 0.4f, ZoneCarFactor = 0.85f;
        /// <summary>Umweltzone: Nachrüstung/neuere Lieferfahrzeuge je Arbeitsplatz und Tag (Firmen in der Zone).</summary>
        public const double ZoneCostPerJobDay = 1.5;
        /// <summary>Umweltzone: Schilder, Kontrolle (Stadtkasse, je Woche).</summary>
        public const double ZoneUpkeepPerWeek = 400.0;
        public const float ZoneMinRadius = 200f, ZoneMaxRadius = 1200f;

        /// <summary>Heizen: Abgas je bewohnter Wohnung bei Heizbedarf 1 (Klima, 0 ab 15 °C).</summary>
        public const float HeatingPerHousehold = 0.0007f;
        /// <summary>Heizen: Läden/Büros/Hallen je besetztem Arbeitsplatz.</summary>
        public const float HeatingPerJob = 0.00015f;

        /// <summary>Bauart: Einfamilienhäuser haben mehr Außenfläche je Wohnung, Blöcke weniger.</summary>
        public static float HeatingBuildingFactor(Building b)
        {
            float f;
            switch (b.Zone)
            {
                case ZoneType.ResidentialLow: f = 1.3f; break;
                case ZoneType.ResidentialMedium: f = 1f; break;
                case ZoneType.ResidentialHigh: f = 0.8f; break;
                case ZoneType.Mixed: f = 0.9f; break;
                default: f = 1f; break;
            }
            // Ausbaustufen = Modernisierung (Dämmung, neue Heizung)
            return f * (b.Level >= 3 ? 0.8f : b.Level == 2 ? 0.9f : 1f);
        }

        /// <summary>
        /// Industrie: Abgas je Arbeitsplatz und Durchlauf bei voller Produktivität, nach Rezept (Stahl/Raffinerie/Zement stark,
        /// Fertigung schwach). 60 Arbeitsplätze im Stahlwerk ≈ ein Kohlekraftwerk.
        /// </summary>
        public static float AirPerJob(string recipeId)
        {
            switch (recipeId)
            {
                case "steelworks": return 0.0010f;
                case "refinery": return 0.0009f;
                case "cementworks": return 0.0008f;
                case "glassworks": return 0.0006f;
                case "building_materials": return 0.0004f;
                case "coal_mine": return 0.0004f;
                case "oil_well": return 0.0004f;
                case "ore_mine": return 0.0003f;
                case "quarry": return 0.0003f;   // Staub
                case "sawmill": return 0.00025f;
                case "textile": return 0.00025f;
                case "vehicle_plant": return 0.00025f;
                case "food_factory": return 0.00015f;
                case "mill": return 0.00015f;
                case "furniture": return 0.00015f;
                case "electronics": return 0.0001f;
                case "farm": return 0.0001f;      // Ammoniak, Maschinen
                case "forestry": return 0.00005f;
                default: return 0f;
            }
        }

        /// <summary>Industrie: Boden-/Grundwasserbelastung je Arbeitsplatz und Tag bei voller Produktivität (Altlasten, Leckagen).</summary>
        public static void GroundPerJob(string recipeId, out float soil, out float groundwater)
        {
            switch (recipeId)
            {
                case "steelworks": soil = 0.00004f; groundwater = 0.00001f; return;
                case "refinery": soil = 0.00005f; groundwater = 0.00003f; return;
                case "oil_well": soil = 0.00004f; groundwater = 0.00003f; return;
                case "coal_mine": soil = 0.00003f; groundwater = 0.00002f; return; // saure Grubenwässer
                case "ore_mine": soil = 0.00003f; groundwater = 0.00002f; return;
                case "glassworks": soil = 0.00001f; groundwater = 0f; return;
                case "cementworks": soil = 0.00002f; groundwater = 0f; return;
                case "textile": soil = 0.00001f; groundwater = 0.00001f; return;  // Färberei
                case "electronics": soil = 0.00001f; groundwater = 0.00001f; return;
                case "farm": soil = 0.000005f; groundwater = 0.000008f; return;   // Dünger/Nitrat
                default: soil = 0f; groundwater = 0f; return;
            }
        }

        /// <summary>Kraftwerke ohne Abnehmer laufen im Bereitschaftsbetrieb (Anteil der Emission).</summary>
        public const float StandbyLoad = 0.15f;
        /// <summary>Ausbreitungsradius Industrie (Schornsteine).</summary>
        public const float IndustryRadius = 64f;

        // ------------------------------------------------------------ Luft: Ausbreitung und Abbau (je Durchlauf)

        public const float BaseDecay = 0.05f;
        /// <summary>Wind verdünnt: zusätzlicher Abbau × Stärke.</summary>
        public const float WindDecay = 0.05f;
        /// <summary>Regen wäscht aus: × min(1, Niederschlag/6 mm).</summary>
        public const float RainDecay = 0.05f, RainFullMillimeters = 6f;
        /// <summary>Wald filtert Feinstaub: zusätzlicher Abbau × Waldraster (0..1).</summary>
        public const float ForestDecay = 0.06f;
        /// <summary>Parks: zusätzlicher Abbau auf der Parkfläche (+ 16 m Rand).</summary>
        public const float ParkDecay = 0.04f;
        public const float Diffusion = 0.1f;
        /// <summary>Verschiebung mit dem Wind: höchstens so viele Zellen je Durchlauf (bei Stärke 1).</summary>
        public const float MaxAdvection = 0.6f;
        /// <summary>Inversion: Abbau, Ausbreitung und Wind nur noch zu diesen Anteilen.</summary>
        public const float InversionDecayFactor = 0.4f, InversionDiffusionFactor = 0.5f, InversionAdvectionFactor = 0.3f;

        // ------------------------------------------------------------ Gegenmaßnahmen: Umweltauflagen (Filter)

        public const int MaxRegulation = 3;
        /// <summary>Emission von Kraftwerken, Industrie und Müllverbrennung je Auflagen-Stufe (Filter, Entstickung, Entschwefelung).</summary>
        public static float FilterFactor(int level) => level <= 0 ? 1f : level == 1 ? 0.65f : level == 2 ? 0.4f : 0.22f;
        /// <summary>Mehrkosten städtischer Kraftwerke/Verbrennung als Anteil ihres Unterhalts je Stufe.</summary>
        public static double PlantUpkeepShare(int level) => level <= 0 ? 0 : level == 1 ? 0.2 : level == 2 ? 0.45 : 0.8;
        /// <summary>Mehrkosten der Industrie je Arbeitsplatz und Tag bei Gewicht 1 (Stahlwerk) je Stufe.</summary>
        public static double FirmCostPerJobDay(int level) => level <= 0 ? 0 : level == 1 ? 5 : level == 2 ? 12 : 22;
        /// <summary>Gewicht einer Firma für die Auflagen-Kosten (Emission je Job relativ zum Stahlwerk, 0..1,3).</summary>
        public static float FirmWeight(string recipeId)
        {
            GroundPerJob(recipeId, out float soil, out float gw);
            return Math.Clamp(AirPerJob(recipeId) / 0.001f + (soil + gw) / 0.00005f * 0.3f, 0f, 1.3f);
        }

        public static string RegulationName(int level)
        {
            switch (level)
            {
                case 0: return "keine";
                case 1: return "Grundauflagen (Staubfilter)";
                case 2: return "streng (Filter + Entstickung)";
                default: return "sehr streng (beste verfügbare Technik)";
            }
        }

        // ------------------------------------------------------------ Bäume pflanzen

        /// <summary>Kosten je gepflanztem Baum; ein 16-m-Feld mit voller Aufforstung hat ~6 Bäume.</summary>
        public const double CostPerTree = 30.0;
        public const float TreesPerCell = 6f;
        /// <summary>Ziel-Dichte einer Aufforstung (Waldraster) und Startwert der Setzlinge.</summary>
        public const float PlantedDensity = 0.8f, SeedlingDensity = 0.05f;
        /// <summary>Wachstum je Tag (Setzling → Wald in ~2 Monaten Spielzeit).</summary>
        public const float PlantGrowthPerDay = 0.012f;
        /// <summary>Pflege je aufgeforstetem Feld und Woche.</summary>
        public const double CareUpkeepPerCellWeek = 1.0;
        public const float MaxPlantRadius = 80f;

        // ------------------------------------------------------------ Boden & Grundwasser (je Tag)

        /// <summary>Ablagerung aus der Luft: Boden + so viel × Tagesmittel der Luft.</summary>
        public const float SoilFromAir = 0.003f;
        /// <summary>Müllhalde: Boden auf der Fläche (+ 30 m) und Sickerwasser ins Grundwasser (Radius 60 m), × (0,3 + 0,7 × Füllstand).</summary>
        public const float LandfillSoil = 0.006f, LandfillGroundwater = 0.008f, LandfillGroundwaterRadius = 60f;
        /// <summary>Müllverbrennung: Asche/Schlacke auf dem Gelände.</summary>
        public const float IncineratorSoil = 0.002f;
        /// <summary>Abwasser-Auslass: Uferboden (Radius 40 m) × min(1, Einleitung/500 m³/h).</summary>
        public const float OutletSoil = 0.002f, OutletRadius = 40f;
        /// <summary>Erholung: Boden 0,4 % je Tag (mit Wald × 1,5), Grundwasser 0,3 %.</summary>
        public const float SoilRecovery = 0.004f, ForestSoilBonus = 1.5f, GroundwaterRecovery = 0.003f;
        /// <summary>Grundwasser: Ausbreitung je Tag und Auswaschung aus belastetem Boden.</summary>
        public const float GroundwaterSpread = 0.03f, Leaching = 0.001f;

        // ------------------------------------------------------------ Wirkungen

        /// <summary>Fruchtbarkeit × (1 − so viel × Bodenbelastung).</summary>
        public const float SoilFertilityLoss = 0.75f;
        /// <summary>Ernte: Ozon/Smog schädigt Pflanzen – Fruchtbarkeit × (1 − so viel × Luft).</summary>
        public const float AirCropLoss = 0.3f;
        /// <summary>Ernte bei Hitze/Dürre: × (1 − 0,5 × Hitze) × (1 − 0,45 × Dürre).</summary>
        public const float HeatHarvestLoss = 0.5f, DroughtHarvestLoss = 0.45f;
        /// <summary>Brunnen: Qualität = 1 − so viel × Grundwasserbelastung am Standort.</summary>
        public const float WellPollutionFactor = 1f;
        /// <summary>Chronische Belastung: Tagesmittel der Luft, geglättet (≈ 10 Tage Gedächtnis).</summary>
        public const float ExposureSmoothing = 0.1f;
        /// <summary>Gesundheit: Zielwert − so viel × chronische Belastung × Empfindlichkeit; Krankheitschance × (1 + 2 × …).</summary>
        public const float ChronicHealthLoss = 0.4f, ChronicIllnessFactor = 2f;
        /// <summary>Bodenwert: Altlasten-Abzug bei Bodenbelastung 1.</summary>
        public const float ContaminationPenalty = 0.12f;

        /// <summary>Empfindlichkeit der Atemwege nach Alter (Kleinkinder, Kinder, Ältere stärker).</summary>
        public static float Vulnerability(int age) =>
            age < 6 ? 1.6f : age < 15 ? 1.25f : age >= 75 ? 1.8f : age >= 60 ? 1.4f : 1f;

        // ------------------------------------------------------------ Smog-Alarm, Schlagzeilen

        /// <summary>Smog an einer Wohnung ab diesem Luftwert.</summary>
        public const float SmogThreshold = 0.3f;
        /// <summary>Alarm, wenn so viele Wohngebäude (mind. <see cref="SmogMinBuildings"/>) bzw. dieser Anteil im Smog liegen.</summary>
        public const int SmogMinBuildings = 5;
        public const float SmogShare = 0.1f;
        /// <summary>Fischsterben ab dieser Wasserverschmutzung (WaterQuality).</summary>
        public const float FishKillThreshold = 0.6f;
    }
}
