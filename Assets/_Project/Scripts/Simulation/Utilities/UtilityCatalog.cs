using System;
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

    /// <summary>
    /// Spannungs-/Netzebene einer Leitung (Netze werden selbst verlegt). Wasser und Abwasser kennen nur
    /// <see cref="Distribution"/>. Strom: <see cref="HighVoltage"/> vom Kraftwerk zum Umspannwerk, <see cref="Distribution"/>
    /// (Ortsnetz, Mittel-/Niederspannung) vom Umspannwerk zu den Gebäuden. Beide Ebenen verbinden sich nur im Umspannwerk.
    /// </summary>
    public enum GridLevel : byte
    {
        /// <summary>Verteilnetz: Ortsnetz (Strom) bzw. Rohre/Kanäle – nur diese Ebene schließt Gebäude an.</summary>
        Distribution = 0,
        /// <summary>Hochspannung (Überlandleitung mit großen Masten): Kraftwerke → Umspannwerke, keine Hausanschlüsse.</summary>
        HighVoltage = 1,
    }

    /// <summary>Rolle eines Versorger-Bauwerks im Netz.</summary>
    public enum FacilityRole : byte
    {
        /// <summary>Erzeugt (Strom/Wasser) bzw. nimmt ab (Abwasser) – <see cref="UtilityFacilityType.Capacity"/> ist die Leistung.</summary>
        Producer = 0,
        /// <summary>
        /// Umspannwerk: verbindet Hochspannung und Ortsnetz, erzeugt nichts; <see cref="UtilityFacilityType.Capacity"/> ist die
        /// größte Last (kW), die es ins Ortsnetz weitergeben kann.
        /// </summary>
        Substation = 1,
        // --- Energie erweitert (Eigene Assets 2026-11-03)
        /// <summary>
        /// Batteriespeicher: erzeugt nichts, lädt bei Überschuss und liefert bei Unterdeckung (Netz-Puffer).
        /// <see cref="UtilityFacilityType.Capacity"/> = Lade-/Entladeleistung (kW), <see cref="UtilityFacilityType.StorageKwh"/> = Energie.
        /// </summary>
        Storage = 2,
        // --- Ende Energie erweitert
    }

    // --- Energie erweitert (Eigene Assets 2026-11-03)
    /// <summary>Wo ein Versorger stehen muss (zusätzlich zu <see cref="UtilityFacilityType.NeedsWater"/> = am Ufer).</summary>
    public enum FacilitySite : byte
    {
        /// <summary>An Land (Standard; mit <see cref="UtilityFacilityType.NeedsWater"/> am Ufer).</summary>
        Land = 0,
        /// <summary>Im Fluss (Wasserkraft): Mitte im fließenden Gewässer, kein Meer; Leistung nach Abfluss und Fallhöhe des Standorts.</summary>
        River = 1,
        /// <summary>Im offenen Meer (Offshore-Wind): ganze Grundfläche im Meer, Anschluss per Hochspannungs-Seekabel an Land.</summary>
        Sea = 2,
    }

    /// <summary>Woher ein Erzeuger seine Leistung bezieht (schwankende Erzeugung, s. UtilitySystem.Energy.cs).</summary>
    public enum OutputModel : byte
    {
        /// <summary>Konstant = Nennleistung (bisherige Kraftwerke, Onshore-Wind/Solar unverändert).</summary>
        Constant = 0,
        /// <summary>Wasserkraft: P = ρ·g·Q·H·η mit Abfluss Q (Querschnitt × Fließgeschwindigkeit × Niederschlagslage) und Fallhöhe H des Standorts.</summary>
        Hydro = 1,
        /// <summary>Offshore-Wind: stündlich schwankendes Windangebot (stetiger Hash-Verlauf, deterministisch).</summary>
        OffshoreWind = 2,
        /// <summary>Biogas: Rohstoffanteil aus Landwirtschaft im Einzugsgebiet (Bauernhöfe) + Grundmenge Bioabfall/Gülle.</summary>
        Biogas = 3,
    }
    // --- Ende Energie erweitert

    /// <summary>
    /// Leitungsart (Wasserrohr, Abwasserkanal, Hochspannung, Freileitung, Erdkabel). Datengetrieben wie die Bauwerke;
    /// <see cref="Id"/> ist der stabile Schlüssel fürs Speichern (<see cref="UtilityCatalog.LineTypeById"/>).
    /// </summary>
    public sealed class UtilityLineType
    {
        public string Id { get; }
        public string DisplayName { get; }
        public UtilityKind Kind { get; }
        public GridLevel Level { get; }
        /// <summary>Baukosten je Meter (Abriss bringt einen Teil zurück, Unterhalt anteilig).</summary>
        public float CostPerMeter { get; }
        /// <summary>Liegt unter der Erde (Rohr, Kanal, Erdkabel): keine Masten, darf unter Straßen liegen, nur unterirdisch sichtbar.</summary>
        public bool Underground { get; }
        /// <summary>Kantenlänge der Mast-Grundfläche (nur oberirdische Leitungen).</summary>
        public float PoleSize { get; }
        /// <summary>Größte Spannweite zwischen zwei Masten (und vom Leitungsende zum ersten Mast).</summary>
        public float MaxSpan { get; }

        public bool HasPoles => !Underground;
        /// <summary>Halbe Mast-Grundfläche + Rand: so weit muss alles Verbotene vom Mastmittelpunkt weg sein.</summary>
        public float PoleFootprintHalf => PoleSize * 0.5f + UtilitySystem.PoleMargin;
        /// <summary>Schließt Gebäude an (Hausanschluss ≤ <see cref="ConnectionRules.BuildingReach"/>) – Hochspannung nicht.</summary>
        public bool ConnectsBuildings => Level == GridLevel.Distribution;

        public UtilityLineType(string id, string name, UtilityKind kind, GridLevel level, float costPerMeter, bool underground,
            float poleSize = UtilitySystem.PoleSize, float maxSpan = UtilitySystem.MaxPoleSpan)
        {
            Id = id;
            DisplayName = name;
            Kind = kind;
            Level = level;
            CostPerMeter = costPerMeter;
            Underground = underground;
            PoleSize = poleSize;
            MaxSpan = maxSpan;
        }

        public override string ToString() => DisplayName;
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
        /// <summary>
        /// Braucht eine Straßenzufahrt an der Vorderseite (Brennstoff, Personal, Wartung) und wird beim Platzieren zur Straße
        /// ausgerichtet. Windrad, Solarpark und Pumpe am Ufer nicht: dort fährt im Alltag niemand hin, die Pumpe richtet sich
        /// nach dem Wasser. Der Netzanschluss (Leitung/Rohr ≤ <see cref="UtilitySystem.RoadReach"/>) ist davon unabhängig.
        /// </summary>
        public bool NeedsRoadFront { get; }

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

        // --- Netze selbst verlegt (Strom mit Spannungsebenen)
        /// <summary>Erzeuger oder Umspannwerk (<see cref="FacilityRole"/>).</summary>
        public FacilityRole Role { get; }
        public bool IsSubstation => Role == FacilityRole.Substation;
        /// <summary>
        /// Kleiner Stromerzeuger (Windrad, Solarpark): darf direkt ins Ortsnetz einspeisen (berührt ein Ortsnetz-Leitungsende
        /// das Bauwerk, speist es dort ein, sonst in die Hochspannung). Große Kraftwerke speisen nur in die Hochspannung.
        /// </summary>
        public bool FeedsLocalGrid { get; }
        // --- Ende

        // --- Energie erweitert (Eigene Assets 2026-11-03)
        /// <summary>Standort: Land (Standard), Fluss (Wasserkraft) oder Meer (Offshore).</summary>
        public FacilitySite Site { get; }
        /// <summary>Erzeugungsmodell (konstant, Wasserkraft, Offshore-Wind, Biogas).</summary>
        public OutputModel Output { get; }
        /// <summary>Batteriespeicher: speicherbare Energie in kWh (0 = kein Speicher).</summary>
        public float StorageKwh { get; }
        /// <summary>
        /// Zufahrt ist Pflicht (nicht nur „Vorderseite zur Straße, falls eine in der Nähe ist“): Biogas braucht Anlieferung
        /// (Mais/Gülle per Traktor/Lkw) – ohne Stadtstraße an der Vorderseite <see cref="UtilityBuildError.NoRoadAccess"/>.
        /// </summary>
        public bool RequiresRoadAccess { get; }
        /// <summary>Darstellung: so viele Anlagen (z. B. Windräder des Offshore-Parks) im Raster auf der Grundfläche.</summary>
        public int Units { get; }
        public bool IsStorage => Role == FacilityRole.Storage;
        /// <summary>Erzeugt selbst Leistung (nicht Umspannwerk, nicht Speicher).</summary>
        public bool IsGenerator => Role == FacilityRole.Producer;
        // --- Ende Energie erweitert

        public UtilityFacilityType(string id, string name, UtilityKind kind, float capacity, float width, float depth, float cost,
            float upkeep, float noise = 0f, float noiseRadius = 0f, float pollution = 0f, float pollutionRadius = 0f, bool needsWater = false,
            bool untreated = false, bool dryFootprint = false, float concernRadius = 0f, bool roadFront = false,
            FacilityRole role = FacilityRole.Producer, bool feedsLocalGrid = false,
            FacilitySite site = FacilitySite.Land, OutputModel output = OutputModel.Constant, float storageKwh = 0f,
            bool requiresRoadAccess = false, int units = 1)
        {
            Site = site;
            Output = output;
            StorageKwh = storageKwh;
            RequiresRoadAccess = requiresRoadAccess;
            Units = Math.Max(1, units);
            Role = role;
            FeedsLocalGrid = feedsLocalGrid;
            NeedsRoadFront = roadFront;
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
            noise: 0.6f, noiseRadius: 140f, pollution: 0.06f, pollutionRadius: 220f, roadFront: true);

        public static readonly UtilityFacilityType GasPlant = new UtilityFacilityType(
            "power_gas", "Gaskraftwerk", UtilityKind.Power, 25000f, 36f, 28f, 45000f, 800f,
            noise: 0.45f, noiseRadius: 100f, pollution: 0.025f, pollutionRadius: 160f, roadFront: true);

        public static readonly UtilityFacilityType WindTurbine = new UtilityFacilityType(
            "power_wind", "Windrad", UtilityKind.Power, 3000f, 12f, 12f, 12000f, 120f,
            noise: 0.25f, noiseRadius: 60f, feedsLocalGrid: true);

        public static readonly UtilityFacilityType SolarPark = new UtilityFacilityType(
            "power_solar", "Solarpark", UtilityKind.Power, 5000f, 40f, 32f, 20000f, 150f, feedsLocalGrid: true);

        // --- Atom (G3 erweitert): sehr große Leistung, sehr teuer, braucht Kühlwasser (Ufer), kein Abgas, Sorge im Umkreis
        public static readonly UtilityFacilityType NuclearPlant = new UtilityFacilityType(
            "power_nuclear", "Atomkraftwerk", UtilityKind.Power, 200000f, 72f, 56f, 400000f, 4500f,
            noise: 0.3f, noiseRadius: 120f, needsWater: true, dryFootprint: true, concernRadius: 1200f);
        // --- Ende Atom

        // --- Umspannwerk (Netze selbst verlegt): Hochspannung → Ortsnetz, Kapazität 20 MW, Zufahrt für Wartung.
        // Modell-Platzhalter; eigenes Modell später über OwnModelCatalog (Datei „Substation“ → Id „power_substation“).
        public static readonly UtilityFacilityType Substation = new UtilityFacilityType(
            "power_substation", "Umspannwerk", UtilityKind.Power, 20000f, 24f, 20f, 18000f, 160f,
            noise: 0.12f, noiseRadius: 35f, roadFront: true, role: FacilityRole.Substation);
        // --- Ende Umspannwerk

        public static readonly UtilityFacilityType WaterPump = new UtilityFacilityType(
            "water_pump", "Wasserpumpe (am Fluss)", UtilityKind.Water, 600f, 16f, 16f, 15000f, 250f,
            noise: 0.2f, noiseRadius: 40f, needsWater: true);

        public static readonly UtilityFacilityType WaterTower = new UtilityFacilityType(
            "water_tower", "Brunnen + Wasserturm", UtilityKind.Water, 80f, 12f, 12f, 6000f, 80f, roadFront: true);

        // --- Abwasser (G3 erweitert)
        public static readonly UtilityFacilityType SewagePlant = new UtilityFacilityType(
            "sewage_plant", "Kläranlage", UtilityKind.Sewage, 500f, 40f, 32f, 45000f, 500f,
            noise: 0.15f, noiseRadius: 50f, roadFront: true); // Klärschlamm-Abfuhr, Personal

        public static readonly UtilityFacilityType SewageOutlet = new UtilityFacilityType(
            "sewage_outlet", "Abwasser-Auslass (ungeklärt ins Gewässer)", UtilityKind.Sewage, 1500f, 8f, 8f, 3000f, 25f,
            needsWater: true, untreated: true);
        // --- Ende Abwasser

        // --- Energie erweitert (Eigene Assets 2026-11-03, Modelle Models/Energy). Werte im Spielmaßstab wie die übrigen
        // Kraftwerke (Windrad 3 MW für 12 000 €), Leistungen realistisch: Biogas-BHKW 2 MW, Laufwasserkraftwerk 3–15 MW je
        // Standort, Offshore-Park 4 × 5 MW, Großbatterie 10 MW / 20 MWh.
        /// <summary>
        /// Biogasanlage (Fermenter + BHKW): 2 MW, braucht Zufahrt (Anlieferung Mais/Gülle), Leistung nach Rohstoff aus den
        /// Bauernhöfen im Umkreis (<see cref="UtilitySystem.BiogasFeedRadius"/>), Grundmenge aus Bioabfall/Gülle. Klein → darf ins Ortsnetz.
        /// </summary>
        public static readonly UtilityFacilityType BiogasPlant = new UtilityFacilityType(
            "power_biogas", "Biogasanlage", UtilityKind.Power, 2000f, 40f, 32f, 16000f, 180f,
            noise: 0.15f, noiseRadius: 50f, pollution: 0.006f, pollutionRadius: 90f, roadFront: true, feedsLocalGrid: true,
            output: OutputModel.Biogas, requiresRoadAccess: true);

        /// <summary>
        /// Laufwasserkraftwerk mit Wehr: nur im Fluss (nicht im Meer), Leistung je Standort aus Abfluss (Querschnitt) und Fallhöhe
        /// (Uferhöhe begrenzt den Aufstau), schwankt mit der Niederschlagslage. Der Fluss bleibt durchgängig (Wasser fließt durch das
        /// Wehr, Gelände wird nicht verändert, Verschmutzung wandert weiter flussabwärts, Fischtreppe/Schleuse). Anschluss: Hochspannung.
        /// <see cref="UtilityFacilityType.Capacity"/> = Richtwert, die echte Nennleistung steht im Bauwerk (<see cref="UtilityFacility.Rated"/>).
        /// </summary>
        public static readonly UtilityFacilityType HydroPlant = new UtilityFacilityType(
            "power_hydro", "Laufwasserkraftwerk (Wehr)", UtilityKind.Power, 8000f, 66f, 56f, 90000f, 450f,
            noise: 0.2f, noiseRadius: 60f, site: FacilitySite.River, output: OutputModel.Hydro);

        /// <summary>
        /// Offshore-Windpark: 4 Anlagen à 5 MW im offenen Meer, Wind schwankt stündlich (im Mittel ~50 % der Nennleistung),
        /// nur Hochspannung – Anschluss an Land per Hochspannungs-Seekabel (<see cref="SubseaCable"/>).
        /// </summary>
        public static readonly UtilityFacilityType OffshoreWind = new UtilityFacilityType(
            "power_offshore_wind", "Offshore-Windpark (4 Anlagen)", UtilityKind.Power, 20000f, 180f, 180f, 150000f, 1100f,
            noise: 0.2f, noiseRadius: 80f, site: FacilitySite.Sea, output: OutputModel.OffshoreWind, units: 4);

        /// <summary>
        /// Batteriespeicher (Container-Park): 10 MW Lade-/Entladeleistung, 20 MWh. Lädt bei Überschuss im eigenen Netz, liefert bei
        /// Unterdeckung (nach den Kraftwerken), 90 % Wirkungsgrad je Richtung. Hängt wie kleine Erzeuger am Ortsnetz oder an der Hochspannung.
        /// </summary>
        public static readonly UtilityFacilityType BatteryStorage = new UtilityFacilityType(
            "power_battery", "Batteriespeicher", UtilityKind.Power, 10000f, 32f, 24f, 30000f, 120f,
            noise: 0.08f, noiseRadius: 25f, roadFront: true, role: FacilityRole.Storage, feedsLocalGrid: true, storageKwh: 20000f);
        // --- Ende Energie erweitert

        public static readonly UtilityFacilityType[] All =
        {
            CoalPlant, GasPlant, WindTurbine, SolarPark, NuclearPlant, Substation, WaterPump, WaterTower, SewagePlant, SewageOutlet,
            BiogasPlant, HydroPlant, OffshoreWind, BatteryStorage, // Energie erweitert (hinten angefügt)
        };

        /// <summary>Versorger zum gespeicherten Schlüssel (null = unbekannt).</summary>
        public static UtilityFacilityType TypeById(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            return null;
        }

        // --- Leitungsarten (Netze selbst verlegt)
        public static readonly UtilityLineType WaterPipe = new UtilityLineType(
            "water_pipe", "Wasserrohr", UtilityKind.Water, GridLevel.Distribution, 10f, underground: true);

        public static readonly UtilityLineType SewerPipe = new UtilityLineType(
            "sewage_pipe", "Abwasserkanal", UtilityKind.Sewage, GridLevel.Distribution, 12f, underground: true);

        /// <summary>Hochspannung: große Masten (4 × 4 m), weite Spannweite, keine Hausanschlüsse.</summary>
        public static readonly UtilityLineType HighVoltageLine = new UtilityLineType(
            "power_hv", "Hochspannungsleitung", UtilityKind.Power, GridLevel.HighVoltage, 30f, underground: false,
            poleSize: 4f, maxSpan: 90f);

        /// <summary>Ortsnetz als Freileitung: billig, kleine Masten (2 × 2 m) neben der Straße – brauchen Platz.</summary>
        public static readonly UtilityLineType OverheadLine = new UtilityLineType(
            "power_overhead", "Freileitung (Ortsnetz)", UtilityKind.Power, GridLevel.Distribution, 15f, underground: false);

        /// <summary>Ortsnetz als Erdkabel: teurer, unterirdisch (unter dem Gehweg), kein Platzverbrauch.</summary>
        public static readonly UtilityLineType UndergroundCable = new UtilityLineType(
            "power_cable", "Erdkabel (Ortsnetz)", UtilityKind.Power, GridLevel.Distribution, 40f, underground: true);

        // --- Energie erweitert: Hochspannung unterirdisch bzw. durchs Wasser (Offshore-Anschluss, auch Erdkabel an Land)
        /// <summary>
        /// Hochspannungs-Erd-/Seekabel: Ebene Hochspannung ohne Masten – kreuzt Wasser (Offshore-Park → Land) und Straßen,
        /// braucht keinen Platz, kostet aber dreimal so viel wie die Freileitung. Keine Hausanschlüsse.
        /// </summary>
        public static readonly UtilityLineType SubseaCable = new UtilityLineType(
            "power_hv_cable", "Hochspannungs-Seekabel/Erdkabel", UtilityKind.Power, GridLevel.HighVoltage, 90f, underground: true);
        // --- Ende Energie erweitert

        public static readonly UtilityLineType[] LineTypes = { WaterPipe, SewerPipe, HighVoltageLine, OverheadLine, UndergroundCable, SubseaCable };

        /// <summary>Leitungsart zum gespeicherten Schlüssel (null = unbekannt).</summary>
        public static UtilityLineType LineTypeById(string id)
        {
            foreach (var t in LineTypes) if (t.Id == id) return t;
            return null;
        }

        /// <summary>Standardart je Versorgungsart (Aufrufe nur mit <see cref="UtilityKind"/>): Strom = Freileitung im Ortsnetz.</summary>
        public static UtilityLineType DefaultLineType(UtilityKind kind) =>
            kind == UtilityKind.Power ? OverheadLine : kind == UtilityKind.Sewage ? SewerPipe : WaterPipe;

        public static string LevelName(GridLevel level) => level == GridLevel.HighVoltage ? "Hochspannung" : "Ortsnetz";
        // --- Ende Leitungsarten

        /// <summary>Kosten je Meter der Standard-Leitungsart (<see cref="DefaultLineType"/>); je Art: <see cref="UtilityLineType.CostPerMeter"/>.</summary>
        public static float LineCostPerMeter(UtilityKind kind) => DefaultLineType(kind).CostPerMeter;

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
