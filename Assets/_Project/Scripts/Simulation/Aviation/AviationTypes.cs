using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Aviation
{
    /// <summary>
    /// Alle Zahlen des Flughafens (Stufe 4 der Außenanbindungen) an einem Ort – Platzhalter fürs Balancing, angelehnt an
    /// ICAO Annex 14 (Hindernisbegrenzungsflächen) und typische Werte deutscher Regional-/Verkehrsflughäfen.
    /// Zeiten in Verkehrssekunden (s. <c>TimeModel</c>), Strecken in m, Geld in €.
    /// </summary>
    public static class AviationRules
    {
        // ------------------------------------------------------------ Platz
        /// <summary>Streifen (Bauverbot) reicht so weit über die Bahnenden hinaus (ICAO: 60 m).</summary>
        public const float StripEndExtension = 60f;
        /// <summary>Höchster Höhenunterschied des Geländes auf dem Flughafengelände (sonst „zu hügelig“).</summary>
        public const float MaxLevelDifference = 30f;
        /// <summary>Raster der Gelände-Prüfpunkte (Einebnen, Wasser, Erdmenge).</summary>
        public const float SiteSampleStep = 16f;
        /// <summary>Abstand des Flughafens zum Kartenrand (m).</summary>
        public const float EdgeMargin = 10f;
        /// <summary>Straßenzufahrt vor der Landseite des Terminals: Rand der Stadtstraße höchstens so weit entfernt.</summary>
        public const float RoadReach = 12f;

        // ------------------------------------------------------------ Hindernisfreiheit (vereinfachte ICAO-Flächen)
        /// <summary>Anflugfläche beginnt so weit hinter dem Bahnende.</summary>
        public const float ApproachStart = 60f;
        /// <summary>Seitliche Aufweitung der Anflugfläche je Seite (15 %).</summary>
        public const float Divergence = 0.15f;
        /// <summary>Kernzone der Einflugschneise: ein Hindernis hier sperrt den Anflug über dieses Bahnende.</summary>
        public const float CoreLength = 1200f;
        /// <summary>Innere Horizontalfläche: Hindernisse höher als 45 m über dem Flugplatz im Umkreis (Platzrunde, Hindernisfeuer).</summary>
        public const float InnerHorizontalHeight = 45f;
        /// <summary>Kapazitätsfaktor je Hindernis außerhalb der Kernzone (eingeschränkter Anflug, höhere Mindestsicht).</summary>
        public const float LimitedFactor = 0.85f;
        /// <summary>Kapazitätsfaktor, wenn Landungen und Starts gegeneinander laufen müssen (ein Bahnende gesperrt).</summary>
        public const float CounterFlowFactor = 0.55f;
        /// <summary>Kapazitätsfaktor je Hindernis in der inneren Horizontalfläche (Hindernisfeuer, Platzrunde eingeschränkt).</summary>
        public const float InnerHorizontalFactor = 0.97f;
        public const float MinObstacleFactor = 0.3f;
        /// <summary>Gelände-Prüfpunkte entlang der Anflugfläche (Abstand längs, Anzahl quer).</summary>
        public const float TerrainProbeStep = 32f;
        public const int TerrainProbeAcross = 5;

        // ------------------------------------------------------------ Betrieb
        /// <summary>Nachtflugverbot (Spieler-Schalter) gilt von 22 bis 6 Uhr.</summary>
        public const int NightStartHour = 22, NightEndHour = 6;
        /// <summary>Gleitpfad 3° (tan 3° ≈ 0,0524) bis zum Aufsetzpunkt.</summary>
        public const float GlideSlope = 0.0524f;
        /// <summary>Aufsetzpunkt hinter der Schwelle.</summary>
        public const float TouchdownOffset = 300f;
        /// <summary>Flugzeuge erscheinen so weit vor dem Kartenrand bzw. verschwinden so weit dahinter.</summary>
        public const float SpawnMargin = 700f;
        /// <summary>Abrollgeschwindigkeit an der Abrollbahn.</summary>
        public const float ExitSpeed = 14f;
        /// <summary>Rollgeschwindigkeit (Verkehrszeit) und Schiebegeschwindigkeit (Push-back).</summary>
        public const float TaxiSpeed = 9f, PushbackSpeed = 2.5f, TaxiAccel = 1.2f;
        /// <summary>Mindestabstand zweier Bahnbenutzungen (Verkehrssekunden).</summary>
        public const float RunwaySeparation = 60f;
        /// <summary>Ein Abflug rollt nur auf die Bahn, wenn die nächste Landung weiter als so viele Sekunden entfernt ist.</summary>
        public const float LineUpClear = 70f;
        /// <summary>Sichtbare Flugbewegungen je Stunde und Flughafen (der Rest läuft statistisch, wie der Anteil echter Autos).</summary>
        public const int MaxVisibleArrivalsPerHour = 3;
        /// <summary>Haltepunkt vor der Bahn (Abstand zur Bahnmitte = Rollweg).</summary>
        public const float RunwayEndInset = 30f;
        /// <summary>Wartet ein Abflug länger, fällt das sichtbare Flugzeug weg (Flug läuft statistisch zu Ende).</summary>
        public const float MaxHoldSeconds = 900f;

        // ------------------------------------------------------------ Nachfrage (Fluggäste je Richtung und Tag)
        /// <summary>Flugreisen der Einwohner je Kopf und Tag (≈ 1,5 Reisen im Jahr, je Richtung).</summary>
        public const float ResidentTripsPerDay = 0.004f;
        /// <summary>Geschäftsreisen je Büro-Arbeitsplatz bzw. je sonstigem Arbeitsplatz und Tag.</summary>
        public const float BusinessTripsPerOfficeJob = 0.012f, BusinessTripsPerJob = 0.0015f;
        /// <summary>Touristen je Tag bei Tourismus-Stärke 1 ohne Sehenswürdigkeiten (wächst mit Attraktivität und Stadtgröße).</summary>
        public const float TouristBase = 60f;
        /// <summary>Attraktivität (Punkte der Freizeitanlagen/Wahrzeichen), die die Touristenzahl verdoppelt.</summary>
        public const float AttractivenessDoubling = 60f;
        public const float LoadFactorMin = 0.5f, LoadFactorMax = 0.9f;
        /// <summary>Anteil der Nachfrage im Nachtflugverbot, der auf Tagesflüge ausweicht (der Rest fällt weg).</summary>
        public const float NightBanShift = 0.7f;
        /// <summary>Nacht-Nachfrage ohne Verbot (Anteil je Nachtstunde gegenüber einer Tagesstunde).</summary>
        public const float NightDemandShare = 0.2f;
        /// <summary>Zuzug per Flugzeug: Chance je ankommendem Passagierflug, dass ein neuer Haushalt mitkommt (selten).</summary>
        public const float MoveInChancePerArrival = 0.12f;

        // ------------------------------------------------------------ Tourismus (Grundstufe ohne Hotels)
        /// <summary>Aufenthalt der Besucher in Tagen (2–4, nach Hash).</summary>
        public const int MinStay = 2, MaxStay = 4;
        /// <summary>Ausgaben je Besucher und Tag: in Läden (Waren aus dem Regal) und in Freizeit/Sehenswürdigkeiten (Eintritt → Stadtkasse).</summary>
        public const float VisitorShopSpendPerDay = 55f, VisitorLeisureSpendPerDay = 30f;
        /// <summary>Kurtaxe je Besucher und Nacht (Stadtkasse).</summary>
        public const float CityTaxPerNight = 3f;
        /// <summary>Nachfrage-Zuschläge: Besucher → Läden, Geschäftsreisende → Büros (Höchstwerte).</summary>
        public const float MaxRetailBoost = 0.3f, MaxOfficeBoost = 0.25f;

        // ------------------------------------------------------------ Gebühren
        /// <summary>Passagierentgelt je abfliegendem Fluggast (Stadtkasse, Flughafen gehört der Stadt).</summary>
        public const float PassengerFee = 9f;

        // ------------------------------------------------------------ Luftfracht (G5/Handel)
        /// <summary>Fernfracht per Flugzeug in Anteil des Warenwerts: teurer als Schiff/Bahn, aber schnell.</summary>
        public const double AirFreightShare = 0.06;
        /// <summary>Nur hochwertige Güter (Grundpreis je Einheit) fliegen.</summary>
        public const float AirFreightMinValue = 300f;
        /// <summary>Umschlag je Ladung (€) und Mindestmenge für einen Frachtflug.</summary>
        public const float CargoFeePerLoad = 25f;
        public const int FreighterMinLoads = 8;

        // ------------------------------------------------------------ Lärm
        /// <summary>Länge des Lärmkorridors hinter jedem Bahnende (An-/Abflug).</summary>
        public const float NoiseLength = 3200f;
        /// <summary>Breite (Radius) des Lärmstempels an der Bahn bzw. am Ende des Korridors.</summary>
        public const float NoiseRadiusNear = 140f, NoiseRadiusFar = 420f;
        /// <summary>Lärmpegel 0..1: Grundwert bei Betrieb + je gewichteter Flugbewegung der letzten Stunde.</summary>
        public const float NoiseBase = 0.35f, NoisePerMovement = 0.1f, NoiseMax = 1.1f;
        /// <summary>Nachtflüge zählen mehrfach (Lden-Gedanke).</summary>
        public const float NightNoiseWeight = 2.5f;
        /// <summary>Ab diesem Fluglärm am Wohnort zählt ein Einwohner als betroffen (Übersicht, Beschwerden).</summary>
        public const float AffectedNoise = 0.3f;

        // ------------------------------------------------------------ Wind (eigene Annahme, solange agent/environment kein Windmodell liefert)
        /// <summary>Tägliche Schwankung der Windrichtung um die Hauptwindrichtung (± Grad).</summary>
        public const float WindSpreadDegrees = 35f;
        /// <summary>Anteil der Tage mit Wind aus der Gegenrichtung (Ostwetterlage bei Westwind usw.).</summary>
        public const float WindReverseShare = 0.15f;

        /// <summary>
        /// Hauptwindrichtung (Einheitsvektor, aus der der Wind kommt; x = Ost, y = Nord) aus der geografischen Breite:
        /// Passat aus Ost (unter 30°, Nord-/Südost), Westwinde (30–60°, aus West/Südwest bzw. Nordwest), polare Ostwinde (über 60°),
        /// dazu eine feste Abweichung ±20° aus dem Seed. Eigene einfache Annahme bis zum Windmodell von agent/environment.
        /// </summary>
        public static Vector2 PrevailingWindFrom(float latitude, int seed)
        {
            float a = MathF.Abs(latitude);
            bool south = latitude < 0f;
            // Winkel in Grad, 0 = aus Osten, 90 = aus Norden (mathematisch)
            float deg = a < 30f ? (south ? -30f : 30f)            // Nordost- bzw. Südost-Passat
                      : a < 60f ? (south ? 160f : 200f)            // Westwinde: Südwest (Nordhalbkugel) / Nordwest
                      : (south ? -20f : 20f);                      // polare Ostwinde
            deg += (Citizens.SimRandom.Hash(seed, 9001) % 41u) - 20f;
            float r = deg * MathF.PI / 180f;
            return new Vector2(MathF.Cos(r), MathF.Sin(r));
        }

        /// <summary>
        /// Hauptwindrichtung aus dem Windmodell des Klimas (Umwelt, Etappe 6): stärkegewichtetes Mittel der Tageswinde eines Jahres
        /// (Einheitsvektor „aus“). Ersetzt die eigene Annahme <see cref="PrevailingWindFrom"/>, sobald das Klima Wind kennt.
        /// </summary>
        public static Vector2 PrevailingFromClimate(World.ClimateModel climate)
        {
            var sum = Vector2.Zero;
            for (int d = 0; d < Core.SimulationClock.DaysPerYear; d++)
            {
                var w = climate.WindOn(d);
                sum += w.Toward * w.Strength;
            }
            if (sum.LengthSquared() < 1e-8f) return -Vector2.UnitX;
            return -Vector2.Normalize(sum);
        }

        /// <summary>Windrichtung des Tages (eigene Annahme ohne Klima: Hauptwindrichtung, Schwankung, gelegentliche Umkehr nach Hash).</summary>
        public static Vector2 WindOfDay(Vector2 prevailing, int seed, int day)
        {
            uint h = Citizens.SimRandom.Hash(seed * 31 + 7, day);
            float spread = ((h % 1000u) / 999f * 2f - 1f) * WindSpreadDegrees;
            bool reverse = ((h >> 10) % 1000u) < WindReverseShare * 1000f;
            var v = Footprint.Rotate(prevailing, spread);
            return reverse ? -v : v;
        }

        /// <summary>Kompassname einer Windrichtung („West“ = Wind aus Westen).</summary>
        public static string WindText(Vector2 from) => Gateways.GatewayRules.Compass(from);

        /// <summary>Bahnkennung nach Kompass (Ausrichtung in Zehngrad, 01–36), z. B. „09/27“.</summary>
        public static string RunwayDesignator(Vector2 dir)
        {
            // Kurs in Grad (0 = Nord, im Uhrzeigersinn) der Richtung dir und der Gegenrichtung
            float heading = MathF.Atan2(dir.X, dir.Y) * 180f / MathF.PI;
            if (heading < 0f) heading += 360f;
            int a = (int)MathF.Round(heading / 10f) % 36; if (a == 0) a = 36;
            int b = (a + 18) % 36; if (b == 0) b = 36;
            int lo = Math.Min(a, b), hi = Math.Max(a, b);
            return $"{lo:00}/{hi:00}";
        }

        /// <summary>Großkreis-Entfernung (km) zwischen zwei Punkten in Grad.</summary>
        public static float GreatCircleKm(float lat1, float lon1, float lat2, float lon2)
        {
            double r = Math.PI / 180.0;
            double dlat = (lat2 - lat1) * r, dlon = (lon2 - lon1) * r;
            double a = Math.Sin(dlat / 2) * Math.Sin(dlat / 2) + Math.Cos(lat1 * r) * Math.Cos(lat2 * r) * Math.Sin(dlon / 2) * Math.Sin(dlon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0.0, 1 - a)));
            return (float)(6371.0 * c);
        }

        /// <summary>Flugdauer in Stunden: Rollen/Steigen/Sinken 0,5 h + Strecke mit 800 km/h (Umweg 5 %).</summary>
        public static float FlightHours(float km) => 0.5f + km * 1.05f / 800f;
    }

    /// <summary>Größe des Flughafens.</summary>
    public enum AirportClass { Airfield, Airport }

    /// <summary>Bauart eines Flughafens (Katalog). Alle Längen in m, Kapazitäten je Spielstunde.</summary>
    public sealed class AirportType
    {
        public string Id { get; }
        public string DisplayName { get; }
        public AirportClass Class { get; }
        /// <summary>Bahnlänge im echten Maßstab; auf kleinen Karten verkürzt (<see cref="RunwayLengthFor"/>).</summary>
        public float NominalRunwayLength { get; }
        public float MinRunwayLength { get; }
        public float RunwayWidth { get; }
        /// <summary>Halbe Breite des Streifens (Bauverbot neben der Bahn).</summary>
        public float StripHalfWidth { get; }
        /// <summary>Abstand Bahnmitte ↔ Mitte des Parallelrollwegs.</summary>
        public float TaxiwayOffset { get; }
        public float TaxiwayWidth { get; }
        /// <summary>Tiefe des Vorfelds (zwischen Rollweg und Terminal).</summary>
        public float ApronDepth { get; }
        /// <summary>Terminal-Module (64 × 32 m) je Ausbaustufe 1..MaxLevel.</summary>
        public int[] TerminalModules { get; }
        /// <summary>Abstellpositionen je Ausbaustufe.</summary>
        public int[] Stands { get; }
        public int MaxLevel => Stands.Length;
        /// <summary>Abstand der Abstellpositionen entlang des Vorfelds.</summary>
        public float StandSpacing { get; }
        public double Cost { get; }
        public double ExpandCost { get; }
        public double UpkeepPerWeek { get; }
        public double UpkeepPerLevel { get; }
        /// <summary>Flugbewegungen (Starts + Landungen) je Spielstunde mit einer Bahn ohne Hindernisse.</summary>
        public float MovementsPerHour { get; }
        /// <summary>Fluggäste je Stunde (beide Richtungen) je Ausbaustufe.</summary>
        public float[] TerminalPaxPerHour { get; }
        /// <summary>Einzugsgebiet: Fluggäste je Richtung und Tag aus dem Umland (unabhängig von der Stadt).</summary>
        public float Catchment { get; }
        /// <summary>Anflugfläche: Länge, halbe Breite am Anfang, Neigung (ICAO Code 2 Sichtanflug 4 % / Code 4 Präzision 2 %).</summary>
        public float ApproachLength { get; }
        public float ApproachInnerHalfWidth { get; }
        public float ApproachSlope { get; }
        /// <summary>Radius der inneren Horizontalfläche um die Bahn.</summary>
        public float InnerHorizontalRadius { get; }
        /// <summary>Luftfracht: Ladungen je Tag (0 = keine Fracht).</summary>
        public int CargoLoadsPerDay { get; }

        public AirportType(string id, string name, AirportClass cls, float nominalLength, float minLength, float width, float strip,
            float taxiway, float taxiwayWidth, float apron, int[] modules, int[] stands, float standSpacing, double cost, double expand,
            double upkeep, double upkeepPerLevel, float movements, float[] paxPerHour, float catchment, float approachLength,
            float approachHalf, float approachSlope, float innerRadius, int cargo)
        {
            Id = id; DisplayName = name; Class = cls; NominalRunwayLength = nominalLength; MinRunwayLength = minLength; RunwayWidth = width;
            StripHalfWidth = strip; TaxiwayOffset = taxiway; TaxiwayWidth = taxiwayWidth; ApronDepth = apron; TerminalModules = modules;
            Stands = stands; StandSpacing = standSpacing; Cost = cost; ExpandCost = expand; UpkeepPerWeek = upkeep; UpkeepPerLevel = upkeepPerLevel;
            MovementsPerHour = movements; TerminalPaxPerHour = paxPerHour; Catchment = catchment; ApproachLength = approachLength;
            ApproachInnerHalfWidth = approachHalf; ApproachSlope = approachSlope; InnerHorizontalRadius = innerRadius; CargoLoadsPerDay = cargo;
        }

        /// <summary>
        /// Bahnlänge auf einer Karte dieser Größe: echtes Maß, solange es mit Rand in die Karte passt, sonst auf 100 m
        /// abgerundet 90 % der Kartenbreite (nie unter <see cref="MinRunwayLength"/>). Standardkarte 2 048 m:
        /// Regionalflugplatz 1 200 m, Verkehrsflughafen 1 800 m (statt 2 800 m).
        /// </summary>
        public float RunwayLengthFor(float mapSize)
        {
            float fit = MathF.Floor(mapSize * 0.9f / 100f) * 100f;
            return MathF.Max(MinRunwayLength, MathF.Min(NominalRunwayLength, fit));
        }

        public override string ToString() => DisplayName;
    }

    public static class AirportCatalog
    {
        /// <summary>Regionalflugplatz: 1 200 m Bahn, Sichtanflug (Code 2), Kleinflugzeuge und Turboprops, kleines Terminal.</summary>
        public static readonly AirportType Airfield = new AirportType(
            "airfield", "Regionalflugplatz", AirportClass.Airfield, 1200f, 1000f, 30f, 40f,
            75f, 15f, 90f, new[] { 1, 2 }, new[] { 3, 5 }, 45f, 650000, 300000, 6500, 2500, 10f, new[] { 160f, 320f }, 24f,
            2500f, 40f, 0.04f, 2000f, 0);

        /// <summary>
        /// Verkehrsflughafen: 2 800 m Bahn (auf der Standardkarte 1 800 m), Präzisionsanflug (Code 4), Mittelstreckenjets,
        /// Terminal in drei Ausbaustufen, Luftfracht.
        /// </summary>
        public static readonly AirportType Airport = new AirportType(
            "airport", "Verkehrsflughafen", AirportClass.Airport, 2800f, 1700f, 45f, 75f,
            120f, 23f, 130f, new[] { 2, 3, 4 }, new[] { 4, 6, 8 }, 52f, 2400000, 900000, 18000, 6000, 28f, new[] { 600f, 1200f, 2000f }, 260f,
            3000f, 150f, 0.02f, 4000f, 60);

        public static readonly AirportType[] All = { Airfield, Airport };

        public static AirportType Get(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            return null;
        }
    }

    public enum AircraftRole { General, Passenger, Cargo }

    /// <summary>Flugzeugmuster: Maße, Plätze, Leistung (Verkehrszeit), Bahnbedarf, Gebühren, Lärm.</summary>
    public sealed class AircraftType
    {
        public string Id { get; }
        public string DisplayName { get; }
        public AircraftRole Role { get; }
        /// <summary>Modell-Id (<c>OwnModelCatalog.TransportModel</c>), Maßstab des Modells (Turboprop = verkleinerter Jet).</summary>
        public string ModelId { get; }
        public float ModelScale { get; }
        public float Length { get; }
        public float Span { get; }
        public int Seats { get; }
        public int CargoLoads { get; }
        public float ApproachSpeed { get; }
        public float RotateSpeed { get; }
        public float Accel { get; }
        public float Decel { get; }
        public float ClimbGradient { get; }
        public float MinRunway { get; }
        /// <summary>Bodenzeit am Vorfeld in Spielminuten.</summary>
        public float TurnaroundMinutes { get; }
        public float LandingFee { get; }
        /// <summary>Lärmgewicht je Bewegung (Mittelstreckenjet = 1).</summary>
        public float Noise { get; }

        public AircraftType(string id, string name, AircraftRole role, string model, float scale, float length, float span, int seats, int cargo,
            float approach, float rotate, float accel, float decel, float climb, float minRunway, float turnaround, float fee, float noise)
        {
            Id = id; DisplayName = name; Role = role; ModelId = model; ModelScale = scale; Length = length; Span = span; Seats = seats;
            CargoLoads = cargo; ApproachSpeed = approach; RotateSpeed = rotate; Accel = accel; Decel = decel; ClimbGradient = climb;
            MinRunway = minRunway; TurnaroundMinutes = turnaround; LandingFee = fee; Noise = noise;
        }

        /// <summary>Bodenzeit in Verkehrssekunden.</summary>
        public double TurnaroundSeconds => TurnaroundMinutes * 60.0 / Core.TimeModel.Dilation;

        public override string ToString() => DisplayName;
    }

    public static class AircraftCatalog
    {
        public static readonly AircraftType Small = new AircraftType("small", "Kleinflugzeug", AircraftRole.General, "air_small", 1f,
            8f, 11f, 4, 0, 38f, 28f, 1.6f, 1.8f, 0.07f, 600f, 20f, 35f, 0.25f);
        public static readonly AircraftType Regional = new AircraftType("regional", "Regionalflugzeug (Turboprop)", AircraftRole.Passenger, "air_airliner", 0.72f,
            27f, 27f, 70, 0, 58f, 52f, 1.8f, 2.0f, 0.07f, 1100f, 25f, 420f, 0.55f);
        public static readonly AircraftType Jet = new AircraftType("jet", "Mittelstreckenjet", AircraftRole.Passenger, "air_airliner", 1f,
            38f, 34f, 180, 0, 70f, 75f, 2.0f, 2.3f, 0.075f, 1700f, 45f, 1400f, 1f);
        public static readonly AircraftType Freighter = new AircraftType("freighter", "Frachtflugzeug", AircraftRole.Cargo, "air_airliner_red", 1f,
            38f, 34f, 0, 30, 70f, 78f, 1.8f, 2.2f, 0.065f, 1700f, 60f, 1800f, 1.15f);

        public static readonly AircraftType[] All = { Small, Regional, Jet, Freighter };

        public static AircraftType Get(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            return null;
        }
    }

    /// <summary>Ein Flugziel (Hauptstadt eines Landes): Großkreis-Entfernung, Flugdauer, Gewicht nach Bevölkerung und Nähe.</summary>
    public sealed class Destination
    {
        public string City { get; internal set; } = "";
        public string Country { get; internal set; } = "";
        public int CountryId { get; internal set; } = -1;
        public float DistanceKm { get; internal set; }
        public float Hours { get; internal set; }
        public float Weight { get; internal set; }
        /// <summary>Tourismus-Stärke des Herkunftslandes (Rang 0..1) – reiselustige Länder schicken mehr Urlauber.</summary>
        public float Tourism { get; internal set; }
        public string Name => string.IsNullOrEmpty(Country) ? City : $"{City} ({Country})";
        public override string ToString() => $"{Name} · {DistanceKm:0} km · {Hours:0.0} h";
    }

    public enum AirportBuildError
    {
        None,
        OutOfWorld,
        OnWater,
        /// <summary>Gelände zu uneben (mehr als <see cref="AviationRules.MaxLevelDifference"/> Höhenunterschied).</summary>
        TooHilly,
        OverlapsRoad,
        /// <summary>Gesperrte Straße der Welt (Autobahn) – nicht abreißbar.</summary>
        OverlapsHighway,
        OverlapsTrack,
        OverlapsStructure,
        /// <summary>Hochspannungs-/Freileitung (Masten) über dem Gelände.</summary>
        OverlapsPowerLine,
        /// <summary>Anderer Flughafen im Weg.</summary>
        OverlapsAirport,
        /// <summary>Beide Einflugschneisen durch Hindernisse gesperrt – kein Flugbetrieb möglich.</summary>
        ApproachBlocked,
        InsufficientFunds,
    }

    /// <summary>Herkunft eines Hindernisses.</summary>
    public enum ObstacleKind { Building, Service, Utility, PowerLine, Terrain, Structure }

    /// <summary>Welche Hindernisfläche ein Hindernis durchstößt.</summary>
    public enum ObstacleSurface { ApproachA, ApproachB, Transitional, InnerHorizontal }

    /// <summary>Ein Hindernis, das eine Hindernisbegrenzungsfläche durchstößt (abgeleitet, nicht gespeichert).</summary>
    public sealed class Obstacle
    {
        public ObstacleKind Kind;
        public ObstacleSurface Surface;
        public string Name = "";
        public Vector2 Position;
        /// <summary>Höhe der Spitze über dem Flugplatz (m) und erlaubte Höhe dort.</summary>
        public float Top, Allowed;
        /// <summary>Entfernung zum Beginn der Anflugfläche (m; innere Horizontalfläche: 0).</summary>
        public float Distance;
        /// <summary>In der Kernzone (sperrt den Anflug über dieses Bahnende).</summary>
        public bool Core;
        public float Penetration => Top - Allowed;
        public override string ToString() => $"{Name}: {Top:0} m statt höchstens {Allowed:0} m";
    }

    /// <summary>Zustand eines Bahnendes (Einflugschneise).</summary>
    public enum CorridorState { Clear, Limited, Closed }

    /// <summary>Geplanter Flughafen (Vorschau) – erst <see cref="AviationSystem.BuildAirport"/> baut ihn.</summary>
    public sealed class AirportPlan
    {
        public AirportType Type;
        /// <summary>Mitte der Bahn, Bahnrichtung (Einheitsvektor, Ende A → B), Terminal-Seite (+1 links von A→B, −1 rechts).</summary>
        public Vector2 Center;
        public Vector2 Dir = Vector2.UnitX;
        public int Side = 1;
        public float RunwayLength;
        /// <summary>Höhe des eingeebneten Flugplatzes und bewegte Erdmenge (m³).</summary>
        public float Height;
        public float EarthVolume;
        public double Cost, EarthworkCost;
        public AirportBuildError Error;
        public string BlockerName;
        public bool HasRoadAccess;
        /// <summary>Hindernisse in den Hindernisflächen (bestehend) und Zustand je Bahnende.</summary>
        public readonly List<Obstacle> Obstacles = new List<Obstacle>();
        public CorridorState CorridorA, CorridorB;
        public float CapacityFactor = 1f;
        /// <summary>Zonengebäude, die für den Flughafen abgerissen werden.</summary>
        public int Displaced;
        public bool IsValid => Error == AirportBuildError.None;
        public AirportLayout Layout => new AirportLayout(Type, Center, Dir, Side, RunwayLength, 1);
        public double TotalCost => Cost + EarthworkCost;
    }

    /// <summary>
    /// Geometrie eines Flughafens im Bahn-Koordinatensystem (u entlang der Bahn A → B, v quer zur Terminal-Seite): Bahn,
    /// Streifen, Parallelrollweg mit Abrollbahnen, Vorfeld mit Abstellpositionen, Terminal (Landseite zeigt von der Bahn weg),
    /// Tower, Hangar. Rein abgeleitet aus Bauart, Lage, Richtung, Seite, Bahnlänge und Ausbaustufe.
    /// </summary>
    public readonly struct AirportLayout
    {
        public readonly AirportType Type;
        public readonly Vector2 Center, Dir, Normal;
        public readonly float Length;
        public readonly int Level;

        public AirportLayout(AirportType type, Vector2 center, Vector2 dir, int side, float length, int level)
        {
            Type = type; Center = center; Dir = dir; Length = length; Level = Math.Max(1, Math.Min(type.MaxLevel, level));
            Normal = new Vector2(-dir.Y, dir.X) * (side >= 0 ? 1f : -1f);
        }

        public Vector2 At(float u, float v) => Center + Dir * u + Normal * v;
        public float U(Vector2 p) => Vector2.Dot(p - Center, Dir);
        public float V(Vector2 p) => Vector2.Dot(p - Center, Normal);

        public float HalfLength => Length * 0.5f;
        public Vector2 EndA => At(-HalfLength, 0f);
        public Vector2 EndB => At(HalfLength, 0f);
        public float TaxiwayV => Type.TaxiwayOffset;
        public float ApronNear => Type.TaxiwayOffset + Type.TaxiwayWidth * 0.5f + 8f;
        public float ApronFar => ApronNear + Type.ApronDepth;
        /// <summary>Terminal-Rückseite (Vorfeld) und Landseite (Vorfahrt, Straße).</summary>
        public float TerminalBack => ApronFar;
        public float TerminalFront => ApronFar + 32f;
        /// <summary>Gesamtes Gelände bis zur Landseite (für die Straße, die davor verläuft) – v von −Streifen bis Terminal-Front.</summary>
        public float SiteNear => -Type.StripHalfWidth;
        public float SiteFar => TerminalFront;

        public int Modules => Type.TerminalModules[Level - 1];
        public float TerminalWidth => Modules * 64f;
        /// <summary>Vorfeld so lang wie das Terminal der höchsten Stufe + Hangar/Tower (gleich bleibende Gesamtfläche).</summary>
        public float ApronLength => Type.TerminalModules[Type.MaxLevel - 1] * 64f + 160f;
        public int StandCount => Type.Stands[Level - 1];
        public int MaxStands => Type.Stands[Type.MaxLevel - 1];

        /// <summary>Lage der Abstellposition i (Nase zum Terminal = +Normal) – Mitte des Flugzeugs je nach Länge.</summary>
        public Vector2 Stand(int i, float aircraftLength)
        {
            int n = MaxStands;
            float u = (i - (n - 1) * 0.5f) * Type.StandSpacing;
            return At(u, TerminalBack - 8f - aircraftLength * 0.5f);
        }
        public float StandU(int i) => (i - (MaxStands - 1) * 0.5f) * Type.StandSpacing;

        /// <summary>Gesamtfläche (für Sperren, Abriss, Einebnen): Streifen + Rollweg + Vorfeld + Terminal.</summary>
        public Footprint Site
        {
            get
            {
                float len = MathF.Max(Length + 2f * AviationRules.StripEndExtension, ApronLength);
                float depth = SiteFar - SiteNear;
                return new Footprint(At(0f, (SiteNear + SiteFar) * 0.5f), Normal, len, depth);
            }
        }

        /// <summary>Außenkante des Parallelrollwegs (Grenze zwischen Bahn-Bereich und Vorfeld/Terminal-Bereich).</summary>
        public float TaxiwayEdge => Type.TaxiwayOffset + Type.TaxiwayWidth * 0.5f + 5f;

        /// <summary>Bahn-Bereich: Streifen + Bahn + Parallelrollweg über die ganze Länge (+ 60 m an den Enden).</summary>
        public Footprint RunwayArea
        {
            get
            {
                float near = -Type.StripHalfWidth, far = TaxiwayEdge;
                return new Footprint(At(0f, (near + far) * 0.5f), Normal, Length + 2f * AviationRules.StripEndExtension, far - near);
            }
        }

        /// <summary>Vorfeld + Terminal + Tower/Hangar (mittig an der Bahn, bis zur Landseite).</summary>
        public Footprint TerminalArea
        {
            get
            {
                float near = TaxiwayEdge, far = SiteFar;
                return new Footprint(At(0f, (near + far) * 0.5f), Normal, ApronLength, far - near);
            }
        }

        /// <summary>Die beiden Flächen des Flughafens (T-förmiges Gelände statt eines großen Rechtecks).</summary>
        public Footprint[] Areas => new[] { RunwayArea, TerminalArea };

        public bool AreaContains(Vector2 p, float margin = 0f) => RunwayArea.Contains(p, margin) || TerminalArea.Contains(p, margin);

        /// <summary>Landseite des Terminals (Vorderseite = Normal, Straße davor).</summary>
        public Footprint Terminal => new Footprint(At(0f, TerminalBack + 16f), Normal, TerminalWidth, 32f);

        /// <summary>Tower und Hangar neben dem Terminal (Mitte, Tiefenrichtung = Normal).</summary>
        public Vector2 Tower => At(-Type.TerminalModules[Type.MaxLevel - 1] * 32f - 40f, TerminalBack + 10f);
        public Vector2 Hangar => At(Type.TerminalModules[Type.MaxLevel - 1] * 32f + 50f, TerminalBack + 4f);
        public Vector2 Windsock => At(-HalfLength + 120f, -Type.StripHalfWidth + 12f);

        /// <summary>Abrollbahnen/Querverbindungen (u-Werte).</summary>
        public float[] Connectors()
        {
            float h = HalfLength - AviationRules.RunwayEndInset;
            return new[] { -h, -HalfLength * 0.5f, 0f, HalfLength * 0.5f, h };
        }
    }

    public enum AircraftState
    {
        /// <summary>Im Anflug (Gleitpfad bis zum Aufsetzpunkt).</summary>
        Approach,
        /// <summary>Ausrollen auf der Bahn bis zur Abrollbahn.</summary>
        Rollout,
        /// <summary>Rollt zur Abstellposition.</summary>
        TaxiIn,
        /// <summary>Steht am Vorfeld (Abfertigung).</summary>
        Parked,
        /// <summary>Rollt zum Haltepunkt (inkl. Push-back).</summary>
        TaxiOut,
        /// <summary>Wartet am Haltepunkt auf die Startfreigabe.</summary>
        Holding,
        /// <summary>Startlauf und Steigflug bis aus der Karte.</summary>
        Takeoff,
    }

    /// <summary>Ein sichtbares Flugzeug (deterministisch im Verkehrsschritt, gespeichert).</summary>
    public sealed class Aircraft
    {
        public int Id { get; internal set; }
        public AircraftType Type { get; internal set; }
        public int AirportId { get; internal set; }
        public AircraftState State { get; internal set; }
        /// <summary>Flugrichtung auf der Bahn (+1 = A → B) für Landung bzw. Start.</summary>
        public int Direction { get; internal set; }
        public int Stand { get; internal set; } = -1;
        /// <summary>Weg entlang des aktuellen Abschnitts (m) und Wert vor dem letzten Verkehrsschritt (Interpolation).</summary>
        public float S { get; internal set; }
        public float PrevS { get; internal set; }
        public float Speed { get; internal set; }
        /// <summary>Ankunfts-Abschnitt: u der Abrollbahn.</summary>
        public float ExitU { get; internal set; }
        /// <summary>Rotation (Flug), zu der das Flugzeug gehört.</summary>
        public int RotationId { get; internal set; }
        public double StateSince { get; internal set; }
        public double DepartAt { get; internal set; }
        public string FlightNumber { get; internal set; } = "";
        public override string ToString() => $"{Type.DisplayName} {FlightNumber}";
    }

    /// <summary>
    /// Ein Umlauf: Landung → Bodenzeit → Start. Trägt Fluggäste/Fracht in beide Richtungen; sichtbar mit eigenem Flugzeug oder
    /// statistisch (wie der Anteil echter Autos). Alle Zeiten in Verkehrssekunden.
    /// </summary>
    public sealed class Rotation
    {
        public int Id { get; internal set; }
        public int AirportId { get; internal set; }
        public AircraftType Type { get; internal set; }
        public string FlightNumber { get; internal set; } = "";
        public int Destination { get; internal set; }
        public double ArrivalTime { get; internal set; }
        public double DepartureTime { get; internal set; }
        public int ArrivingPax { get; internal set; }
        public int DepartingPax { get; internal set; }
        public int ArrivingLoads { get; internal set; }
        public int DepartingLoads { get; internal set; }
        /// <summary>Anteil Touristen/Geschäftsreisende der ankommenden Fluggäste.</summary>
        public float TouristShare { get; internal set; }
        public float BusinessShare { get; internal set; }
        public bool Visible { get; internal set; }
        public int AircraftId { get; internal set; }
        public bool Arrived { get; internal set; }
        public bool Departed { get; internal set; }
        public bool Night { get; internal set; }
    }

    /// <summary>Ein gebauter Flughafen.</summary>
    public sealed class Airport
    {
        public int Id { get; internal set; }
        public AirportType Type { get; internal set; }
        public string Name { get; internal set; } = "";
        public Vector2 Center { get; internal set; }
        public Vector2 Dir { get; internal set; } = Vector2.UnitX;
        public int Side { get; internal set; } = 1;
        public float RunwayLength { get; internal set; }
        public float Height { get; internal set; }
        public int Level { get; internal set; } = 1;
        public long BuiltTick { get; internal set; }

        // --- Betrieb des Tages (gespeichert)
        public int ArrivalDirection { get; internal set; } = 1;
        public int DepartureDirection { get; internal set; } = 1;
        public Vector2 WindToday { get; internal set; } = -Vector2.UnitX;
        /// <summary>Bruchteil-Übertrag der stündlichen Planung (deterministisch).</summary>
        internal double ScheduleCarry;
        internal int FlightCounter;
        internal double MoveInCarry;

        // --- Statistik (gespeichert)
        public int ArrivalsToday, ArrivalsYesterday, DeparturesToday, DeparturesYesterday;
        public int PaxInToday, PaxInYesterday, PaxOutToday, PaxOutYesterday;
        public int TouristsToday, TouristsYesterday, BusinessToday, BusinessYesterday;
        public int LoadsToday, LoadsYesterday, PendingLoads, TotalLoads;
        public int MoveInsTotal;
        public int TotalMovements;
        /// <summary>Gewichtete Flugbewegungen der letzten Stunde (Lärm) und der laufenden Stunde, getrennt nach Bahnende (A/B).</summary>
        public float NoiseA, NoiseB, NoiseHourA, NoiseHourB;
        public int MovementsThisHour, MovementsLastHour;

        // --- Abgeleitet (nicht gespeichert)
        public bool HasAccess { get; internal set; }
        public int AccessSegmentId { get; internal set; } = -1;
        public int AccessNodeId { get; internal set; }
        public readonly List<Obstacle> Obstacles = new List<Obstacle>();
        public CorridorState CorridorA { get; internal set; }
        public CorridorState CorridorB { get; internal set; }
        public float ObstacleFactor { get; internal set; } = 1f;
        internal int ObstacleSignature = int.MinValue;

        public AirportLayout Layout => new AirportLayout(Type, Center, Dir, Side, RunwayLength, Level);
        public Footprint Site => Layout.Site;
        /// <summary>Kein Anflug möglich (beide Einflugschneisen gesperrt).</summary>
        public bool Blocked => CorridorA == CorridorState.Closed && CorridorB == CorridorState.Closed;
        /// <summary>In Betrieb: Straße vor der Landseite und mindestens eine Einflugschneise frei.</summary>
        public bool InService => HasAccess && !Blocked;
        public bool CounterFlow => ArrivalDirection != DepartureDirection;
        public string Designator => AviationRules.RunwayDesignator(Dir);
        public int Stands => Layout.StandCount;
        public bool Contains(Vector2 p, float margin = 0f) => Layout.AreaContains(p, margin);
        public override string ToString() => Name;
    }
}
