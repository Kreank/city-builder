using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;

namespace CityBuilder.Simulation.Rail
{
    /// <summary>
    /// Alle Zahlen der Bahn (Stufe 2) an einem Ort – Platzhalter fürs Balancing. Zeiten in Sekunden Verkehrszeit
    /// (<see cref="TrafficSystem.Time"/>: Züge fahren wie Autos in physikalischen m/s), Geld in €.
    /// </summary>
    public static class RailRules
    {
        // --- Trassierung (strenger als Straße)
        /// <summary>Gleisabstand der zweigleisigen Hauptstrecke (Gleismitte ↔ Gleismitte).</summary>
        public const float TrackSpacing = 4.5f;
        /// <summary>Kleinster Bogenhalbmesser für Spielergleise (m) – Straßen kennen keinen.</summary>
        public const float MinRadius = 150f;
        /// <summary>Weiche/Anschluss: neues Gleis darf höchstens so steil vom Bestandsgleis abzweigen (Grad).</summary>
        public const float MaxSwitchAngle = 12f;
        /// <summary>Fahrweg: Züge biegen an einem Knoten höchstens um so viel ab (Grad) – sonst keine Verbindung.</summary>
        public const float MaxDeflection = 20f;
        /// <summary>Kreuzung Gleis/Gleis ebenerdig (Kreuzungsweiche): mindestens so steiler Winkel.</summary>
        public const float MinDiamondAngle = 25f;
        /// <summary>Gradiente der Hauptstrecke (geglättetes Gelände, steigungsbegrenzt).</summary>
        public const float MainlineGrade = 0.02f;
        /// <summary>Glättung des Geländes für die Gradiente der Hauptstrecke (Halbbreite in m).</summary>
        public const float MainlineSmoothing = 160f;
        /// <summary>Stückgrenzen der Hauptstrecke (= Blockabschnitte): Ziel- und Höchstlänge (m).</summary>
        public const float MainlineChunk = 260f, MainlineMaxChunk = 700f;

        // --- Kreuzung Straße ↔ Gleis
        /// <summary>Lichte Höhe: Gleis über Straße (Brücke) bzw. Straße über Gleis (Oberleitung).</summary>
        public const float RailOverRoadClearance = 5f, RoadOverRailClearance = 6.5f;
        /// <summary>Bahnübergang: Mindestwinkel Straße ↔ Gleis (Grad).</summary>
        public const float MinCrossingAngle = 35f;
        /// <summary>Bahnübergang: Abstand zum nächsten Straßenknoten (Kreuzung/Ende) bzw. zur nächsten Weiche (m).</summary>
        public const float CrossingClearFromRoadNode = 20f, CrossingClearFromSwitch = 25f;
        /// <summary>Schranke schließt, wenn ein Zug in so vielen Sekunden da wäre (+ fester Puffer in m).</summary>
        public const float CrossingWarnSeconds = 28f, CrossingWarnMeters = 40f;
        /// <summary>Schranke öffnet so viele Sekunden, nachdem das Zugende den Übergang verlassen hat.</summary>
        public const float CrossingOpenDelay = 3f;
        /// <summary>Haltelinie vor dem Gleis: Abstand vom Gleisrand (m).</summary>
        public const float StopLineGap = 3.5f;

        // --- Fahren
        /// <summary>Signal vor dem besetzten Block: der Zug hält so weit davor (Schutzstrecke, m).</summary>
        public const float SignalMargin = 10f;
        /// <summary>Reservierungs-Vorschau über den Bremsweg hinaus (m).</summary>
        public const float SightMargin = 200f;
        /// <summary>Zug wartet höchstens so lange vor der Karte auf einen freien ersten Block, sonst fällt er aus (s).</summary>
        public const float SpawnPatience = 900f;

        // --- Fahrplan (Spielstunden)
        /// <summary>Fernzüge je Richtung stündlich von <see cref="FirstIntercityHour"/> bis <see cref="LastIntercityHour"/> Uhr.</summary>
        public const int FirstIntercityHour = 6, LastIntercityHour = 22;
        /// <summary>Güterzüge je Richtung alle so viele Spielstunden (rund um die Uhr).</summary>
        public const int FreightEveryHours = 2;

        // --- Bahnhof
        /// <summary>Gleismitte ↔ Rückseite des Empfangsgebäudes beim Einrasten (Bahnsteig dazwischen).</summary>
        public const float PlatformGap = 6f;
        /// <summary>Gleise mit Gleismitte in diesem Abstand hinter der Rückseite bekommen eine Bahnsteigkante.</summary>
        public const float PlatformMinOffset = 3f, PlatformMaxOffset = 16f;
        /// <summary>Höchstens so viele Bahnsteiggleise je Bahnhof.</summary>
        public const int MaxPlatforms = 3;
        /// <summary>Bahnhof sucht beim Einrasten Gleise in diesem Umkreis (m).</summary>
        public const float StationSnapRadius = 40f;
        /// <summary>Zufahrt an der Vorderseite (Rand ≤, wie Dienste).</summary>
        public const float StationRoadReach = 8f;
        public const float StationMaxSlope = 8f;
        /// <summary>Haltezeit: Grundzeit + je ein-/aussteigendem Fahrgast (s).</summary>
        public const float DwellBase = 45f, DwellPerPassenger = 0.3f, FreightDwell = 240f;

        // --- Fahrgäste
        /// <summary>Bahnhof ist für Fahrgäste bis zu dieser Luftlinie erreichbar (zu Fuß bis 900 m, darüber Rad/Bus).</summary>
        public const float MaxAccessMeters = 2200f;
        /// <summary>Verkehrszeit „außerhalb“ einer Bahnfahrt (Fernzug ist schneller als das Auto über Land).</summary>
        public const float OutsideSeconds = Core.TimeModel.OutsideTrafficSeconds * 0.6f;
        /// <summary>Wartende am Bahnhof geben nach so vielen Sekunden auf (≥ 2 Takte).</summary>
        public const float StationPatience = 1100f;
        /// <summary>Ankommende warten „draußen“ höchstens so lange auf einen Zug, der hier hält.</summary>
        public const float OutsidePatience = 1400f;
        /// <summary>Durchreisende Fahrgäste je Fernzug beim Einfahren in die Karte (Spielmaßstab, Statistik/Anzeige).</summary>
        public const int ThroughPassengersMin = 120, ThroughPassengersMax = 300;

        // --- Kasse
        /// <summary>Stationsentgelt je Halt eines Fernzugs bzw. Umschlag eines Güterzugs (zahlt der Betreiber an die Stadt).</summary>
        public const double IntercityStopFee = 60.0, FreightStopFee = 90.0;
        /// <summary>Anteil am Fahrpreis je Ein-/Aussteiger am eigenen Bahnhof.</summary>
        public const double PassengerFee = 2.5;
        /// <summary>Umschlagentgelt je Ladung am Güterbahnhof.</summary>
        public const double FreightLoadFee = 6.0;
        /// <summary>Unterhalt Spielergleise je Meter und Woche (Fahrweg, Schwellen, Oberleitung).</summary>
        public const double TrackUpkeepPerMeterWeek = 0.9;

        // --- Güter
        /// <summary>Fracht über die Bahn (Anteil am Warenwert) statt <see cref="Goods.GoodsRules.WorldFreightShare"/> per Lkw.</summary>
        public const double RailFreightShare = 0.045;
        /// <summary>Ladungen (Lkw-Ladungen) je Güterwagen.</summary>
        public const int LoadsPerWagon = 2;
    }

    /// <summary>Art eines Bahnhofs.</summary>
    public enum StationKind
    {
        /// <summary>Personenbahnhof: Fernzüge halten, Fahrgäste steigen ein/aus.</summary>
        Passenger,
        /// <summary>Güterbahnhof: Umschlag Lkw ↔ Zug (Import/Export der Firmen).</summary>
        Freight,
    }

    /// <summary>Bauwerkstyp eines Bahnhofs (Empfangsgebäude bzw. Umschlaghalle mit Bahnsteig/Ladegleis dahinter).</summary>
    public sealed class StationType
    {
        public string Id { get; }
        public string DisplayName { get; }
        public StationKind Kind { get; }
        public float Width { get; }
        public float Depth { get; }
        /// <summary>Bahnsteig- bzw. Ladegleislänge (m) – länger als das Gebäude, mittig dahinter.</summary>
        public float PlatformLength { get; }
        public double Cost { get; }
        public double UpkeepPerWeek { get; }

        public StationType(string id, string name, StationKind kind, float width, float depth, float platform, double cost, double upkeep)
        {
            Id = id; DisplayName = name; Kind = kind; Width = width; Depth = depth; PlatformLength = platform; Cost = cost; UpkeepPerWeek = upkeep;
        }

        public override string ToString() => DisplayName;
    }

    public static class StationCatalog
    {
        public static readonly StationType Passenger = new StationType("train_station", "Personenbahnhof", StationKind.Passenger, 44f, 20f, 200f, 120000, 2400);
        public static readonly StationType Freight = new StationType("freight_station", "Güterbahnhof", StationKind.Freight, 60f, 30f, 260f, 150000, 3000);
        public static readonly IReadOnlyList<StationType> All = new[] { Passenger, Freight };

        public static StationType Get(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            return null;
        }
    }

    /// <summary>Wagen eines Zuges (Modelle aus <c>Models/Rail/</c>).</summary>
    public enum CarKind : byte { Locomotive, PassengerCoach, ContainerWagon, TankWagon }

    public static class RailCars
    {
        /// <summary>Länge über Puffer (m) – wie die Blender-Modelle (Lok 19, Reisezugwagen 26, Container 19,6, Kessel 16).</summary>
        public static float Length(CarKind k)
        {
            switch (k)
            {
                case CarKind.Locomotive: return 19f;
                case CarKind.PassengerCoach: return 26f;
                case CarKind.ContainerWagon: return 19.6f;
                default: return 16f;
            }
        }

        /// <summary>Abstand zwischen zwei Wagen (Kupplung).</summary>
        public const float Gap = 0.8f;

        public static string DisplayName(CarKind k)
        {
            switch (k)
            {
                case CarKind.Locomotive: return "Lok";
                case CarKind.PassengerCoach: return "Reisezugwagen";
                case CarKind.ContainerWagon: return "Containertragwagen";
                default: return "Kesselwagen";
            }
        }
    }

    public enum TrainKind { Intercity, Freight }

    /// <summary>Zuggattung: Wagenreihung, Höchstgeschwindigkeit, Anfahren/Bremsen, Plätze.</summary>
    public sealed class TrainType
    {
        public string Id { get; }
        public string DisplayName { get; }
        public TrainKind Kind { get; }
        public IReadOnlyList<CarKind> Cars { get; }
        /// <summary>Höchstgeschwindigkeit (m/s), Beschleunigung und Betriebsbremsung (m/s²).</summary>
        public float MaxSpeed { get; }
        public float Acceleration { get; }
        public float Deceleration { get; }
        public int Capacity { get; }
        public float Length { get; }
        public int Wagons { get; }

        public TrainType(string id, string name, TrainKind kind, CarKind[] cars, float maxKmh, float acc, float dec, int capacity)
        {
            Id = id; DisplayName = name; Kind = kind; Cars = cars; MaxSpeed = maxKmh / 3.6f; Acceleration = acc; Deceleration = dec; Capacity = capacity;
            float len = 0f;
            for (int i = 0; i < cars.Length; i++) { len += RailCars.Length(cars[i]); if (i > 0) len += RailCars.Gap; if (cars[i] != CarKind.Locomotive) Wagons++; }
            Length = len;
        }

        public override string ToString() => DisplayName;
    }

    public static class TrainCatalog
    {
        public static readonly TrainType Intercity = new TrainType("intercity", "Fernzug", TrainKind.Intercity,
            new[] { CarKind.Locomotive, CarKind.PassengerCoach, CarKind.PassengerCoach, CarKind.PassengerCoach, CarKind.PassengerCoach, CarKind.PassengerCoach, CarKind.PassengerCoach },
            160f, 0.45f, 0.7f, 6 * 72);

        public static readonly TrainType Freight = new TrainType("freight", "Güterzug", TrainKind.Freight,
            new[] { CarKind.Locomotive, CarKind.ContainerWagon, CarKind.ContainerWagon, CarKind.TankWagon, CarKind.ContainerWagon, CarKind.ContainerWagon,
                    CarKind.TankWagon, CarKind.TankWagon, CarKind.ContainerWagon, CarKind.ContainerWagon, CarKind.TankWagon },
            100f, 0.25f, 0.45f, 0);

        public static readonly IReadOnlyList<TrainType> All = new[] { Intercity, Freight };

        public static TrainType Get(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            return null;
        }
    }

    public enum StationBuildError
    {
        None,
        OutOfWorld,
        OnWater,
        TooSteep,
        /// <summary>Kein Gleis hinter dem Gebäude (Bahnhof rastet an Gleisen ein).</summary>
        NoTrack,
        OverlapsRoad,
        OverlapsTrack,
        /// <summary>Anderes Bauwerk (Dienst, Versorger, Depot, gesetztes Haus, Bahnhof) im Weg.</summary>
        OverlapsStructure,
        InsufficientFunds,
    }

    /// <summary>Bahnsteigkante eines Bahnhofs an einem Gleis (Mitte des Bahnsteigs auf dem Gleis).</summary>
    public sealed class Platform
    {
        public int SegmentId { get; internal set; }
        /// <summary>Parameter der Bahnsteigmitte auf dem Gleis.</summary>
        public float T { get; internal set; }
        /// <summary>Abstand Gleismitte ↔ Gebäuderückseite (m).</summary>
        public float Offset { get; internal set; }
        /// <summary>Gleisnummer im Bahnhof (1 = am Gebäude).</summary>
        public int Number { get; internal set; }
        public Vector2 Position { get; internal set; }
    }

    /// <summary>Gebauter Bahnhof (Personen- oder Güterbahnhof): Gebäude mit Straßenfront, Bahnsteige an den Gleisen dahinter.</summary>
    public sealed class Station
    {
        public int Id { get; internal set; }
        public StationType Type { get; internal set; }
        public string Name { get; internal set; } = "";
        public Vector2 Position { get; internal set; }
        /// <summary>Vorderseite (Straßenfront, Bahnhofsvorplatz) – die Gleise liegen hinter dem Gebäude (−Front).</summary>
        public Vector2 Front { get; internal set; }
        public float Height { get; internal set; }

        // --- abgeleitet (Straßen-/Gleisänderung)
        public int AccessSegmentId { get; internal set; } = -1;
        public float AccessT { get; internal set; }
        /// <summary>Straßenknoten nahe der Zufahrt: hier „kommen“ Zuzügler per Bahn an (Ort außerhalb im Bürgersystem), 0 = keiner.</summary>
        public int AccessNodeId { get; internal set; }
        internal readonly List<Platform> PlatformList = new List<Platform>();
        public IReadOnlyList<Platform> Platforms => PlatformList;
        /// <summary>Mindestens ein Bahnsteiggleis liegt auf einem Fahrweg der Hauptstrecke (Fernzüge/Güterzüge halten hier).</summary>
        public bool OnMainline { get; internal set; }

        public bool HasAccess => AccessSegmentId >= 0;
        public bool InService => HasAccess && OnMainline;
        public bool IsPassenger => Type.Kind == StationKind.Passenger;

        // --- Statistik
        public int BoardedToday { get; internal set; }
        public int AlightedToday { get; internal set; }
        public int BoardedYesterday { get; internal set; }
        public int AlightedYesterday { get; internal set; }
        public int StopsToday { get; internal set; }
        public int StopsYesterday { get; internal set; }
        public int LoadsToday { get; internal set; }
        public int LoadsYesterday { get; internal set; }
        public int TotalPassengers { get; internal set; }
        /// <summary>Güterbahnhof: Ladungen seit dem letzten Güterzug (werden beim nächsten Halt umgeschlagen).</summary>
        public int PendingLoads { get; internal set; }
        internal readonly List<RailPassenger> Waiting = new List<RailPassenger>();
        public int WaitingCount => Waiting.Count;

        public Footprint Footprint => new Footprint(Position, Front, Type.Width, Type.Depth);
        /// <summary>Bahnsteigfläche hinter dem Gebäude (bis zum entferntesten Bahnsteiggleis) – Straßen dürfen sie nicht queren.</summary>
        public Footprint PlatformArea
        {
            get
            {
                float depth = RailRules.PlatformGap + 3f;
                foreach (var p in PlatformList) depth = MathF.Max(depth, p.Offset + 3f);
                var back = Position - Front * (Type.Depth * 0.5f);
                return new Footprint(back - Front * (depth * 0.5f), Front, Type.PlatformLength, depth);
            }
        }
        public Vector2 FrontMid => Position + Front * (Type.Depth * 0.5f);
        public bool Contains(Vector2 p, float margin = 0f) => Footprint.Contains(p, margin);
        public override string ToString() => $"{Type.DisplayName} {Name} #{Id}";
    }

    public sealed class StationPlan
    {
        public StationType Type;
        public Vector2 Position, Front;
        public float Height;
        public double Cost;
        public StationBuildError Error;
        public StructureBlock BlockedBy;
        /// <summary>Gleise hinter dem Gebäude, die Bahnsteige bekämen (Vorschau).</summary>
        public int PlatformTracks;
        /// <summary>Zufahrt an der Vorderseite gefunden (sonst baubar, aber außer Betrieb bis eine Straße davor liegt).</summary>
        public bool HasRoadAccess;
        public int MovingId;
        public bool IsValid => Error == StationBuildError.None;
    }

    public enum TrainState : byte
    {
        /// <summary>Steht vor der Karte, wartet auf einen freien ersten Block.</summary>
        Waiting,
        Running,
        /// <summary>Hält am Bahnsteig (Ein-/Aussteigen bzw. Umschlag).</summary>
        Dwelling,
    }

    /// <summary>
    /// Ein Zug auf seinem Fahrweg (Folge gerichteter Gleisstücke von Kartenrand zu Kartenrand). Position = Strecke der
    /// Zugspitze ab Fahrwegbeginn (<see cref="HeadS"/>; negativ = noch vor der Karte, &gt; Länge = schon hinaus).
    /// </summary>
    public sealed class Train
    {
        public int Id { get; internal set; }
        public TrainType Type { get; internal set; }
        public TrainState State { get; internal set; }
        public int FromGatewayId { get; internal set; }
        public int ToGatewayId { get; internal set; }
        /// <summary>Fahrweg (gerichtete Gleisstücke) und Beginn jedes Stücks auf dem Fahrweg (m).</summary>
        internal readonly List<EdgeKey> Route = new List<EdgeKey>();
        internal readonly List<float> RouteStart = new List<float>();
        internal readonly List<float> EdgeLength = new List<float>();
        public IReadOnlyList<EdgeKey> RouteEdges => Route;
        public float RouteLength { get; internal set; }
        public float HeadS { get; internal set; }
        /// <summary>Spitze vor dem letzten Verkehrsschritt (Darstellung interpoliert).</summary>
        public float PrevHeadS { get; internal set; }
        public float Speed { get; internal set; }
        /// <summary>Letztes reserviertes Fahrwegstück (Blockabschnitt), −1 = keins.</summary>
        internal int ReservedUntil = -1;
        /// <summary>Erstes noch gehaltenes Fahrwegstück (hinter dem Zugende freigegeben).</summary>
        internal int ReleasedBefore;
        internal int HeadIndex;
        /// <summary>Halte (Bahnhof-Id, Strecke der Zugspitze beim Halt) in Fahrwegreihenfolge.</summary>
        internal readonly List<(int station, float s)> Stops = new List<(int, float)>();
        internal int NextStop;
        internal double DwellUntil;
        internal double SpawnTime, WaitSince;
        /// <summary>Zug, dessen Reservierung die eigene Fahrerlaubnis zuletzt begrenzt hat (0 = keiner).</summary>
        internal int BlockedBy;
        internal readonly List<RailPassenger> Riders = new List<RailPassenger>();
        /// <summary>Durchreisende (nicht einzeln simuliert): Anzeige/Statistik.</summary>
        public int ThroughPassengers { get; internal set; }
        /// <summary>Güterzug: beladene Ladungen (Anzeige; Umschlag am Güterbahnhof).</summary>
        public int Loads { get; internal set; }
        /// <summary>Wartet vor einem Halt zeigenden Signal (Block voraus besetzt).</summary>
        public bool AtSignal { get; internal set; }
        public double DelaySeconds { get; internal set; }

        public float Length => Type.Length;
        public int Passengers => Riders.Count + ThroughPassengers;
        public int CityPassengers => Riders.Count;
        public bool IsOnMap => State != TrainState.Waiting && HeadS > 0f && HeadS - Length < RouteLength;
        public int NextStationId => NextStop < Stops.Count ? Stops[NextStop].station : 0;
        public override string ToString() => $"{Type.DisplayName} #{Id}";
    }

    /// <summary>Bahnübergang: ebenerdige Kreuzung einer Stadtstraße mit einem Gleis (abgeleitet aus beiden Netzen).</summary>
    public sealed class LevelCrossing
    {
        public int RoadSegmentId { get; internal set; }
        public float RoadT { get; internal set; }
        public int RailSegmentId { get; internal set; }
        public float RailT { get; internal set; }
        public Vector2 Position { get; internal set; }
        public float Height { get; internal set; }
        /// <summary>Winkel zwischen Straße und Gleis (Grad, 0..90).</summary>
        public float Angle { get; internal set; }
        /// <summary>Halbe Breite der Sperrzone entlang der Straße (Gleis + Haltelinie, schräg verlängert).</summary>
        public float HalfSpan { get; internal set; }
        public bool Closed { get; internal set; }
        internal double OpenAt;
        /// <summary>Wie oft heute geschlossen (Statistik).</summary>
        public int ClosuresToday { get; internal set; }
        public override string ToString() => $"Bahnübergang Straße {RoadSegmentId} / Gleis {RailSegmentId}";
    }

    public enum RailRideState : byte
    {
        /// <summary>Geht zum Bahnhof bzw. wartet dort (abfahrend).</summary>
        AtStation,
        /// <summary>Wartet außerhalb auf einen Zug, der am Bahnhof hält (ankommend).</summary>
        Outside,
        OnTrain,
    }

    /// <summary>Ein Bürger auf einer Bahnfahrt (Fernzug von/nach außerhalb über einen eigenen Bahnhof).</summary>
    public sealed class RailPassenger
    {
        public int Id { get; internal set; }
        public int CitizenId { get; internal set; }
        /// <summary>true = fährt aus der Stadt hinaus; false = kommt an.</summary>
        public bool Outbound { get; internal set; }
        public int StationId { get; internal set; }
        public RailRideState State { get; internal set; }
        public int TrainId { get; internal set; }
        /// <summary>Ziel in der Stadt (ankommend) für den Fußweg vom Bahnhof.</summary>
        public Vector2 Destination { get; internal set; }
        internal double StartTime, ReadyTime, WaitSince;
        internal float ExpectedSeconds;
        public bool IsActive { get; internal set; } = true;
        public bool IsWaitingAtStation(double now) => State == RailRideState.AtStation && now >= ReadyTime;
    }

    /// <summary>Angebot einer Bahnfahrt für die Verkehrsmittelwahl (Bürgersystem).</summary>
    public struct RailOffer
    {
        public bool Valid;
        public int StationId;
        public float AccessMeters;
        /// <summary>Zugangsweg zum Bahnhof (s, zu Fuß bzw. Rad), Wartezeit (halber Takt) und Fahrt bis zum Kartenrand.</summary>
        public float AccessSeconds, WaitSeconds, RideSeconds;
        /// <summary>Gefühlte Reisezeit (Zugang 1,3-fach, Warten 1,5-fach) + außerhalb.</summary>
        public float PerceivedSeconds;
        public float TotalSeconds;
    }

    /// <summary>Ergebnis einer Bahnfahrt für das Bürgersystem.</summary>
    public enum RailRideResult : byte { Arrived, ArrivedLate, GaveUp }
}
