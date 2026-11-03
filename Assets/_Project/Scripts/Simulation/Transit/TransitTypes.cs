using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Transit
{
    /// <summary>
    /// Alle Stellschrauben des ÖPNV an einem Ort (Platzhalter fürs Balancing). Zeiten in Sekunden Verkehrszeit
    /// (<see cref="TrafficSystem.Time"/>), Geld in €.
    /// </summary>
    public static class TransitRules
    {
        /// <summary>Plätze je Bus (Sitz- und Stehplätze, Spielmaßstab).</summary>
        public const int BusCapacity = 60;
        /// <summary>Fahrgeld je Einstieg (Umsteigen zahlt erneut – Spielmaßstab, klein).</summary>
        public const double Fare = 1.5;
        /// <summary>Unterhalt je Bus im Einsatz und Woche (Fahrer, Diesel, Wartung).</summary>
        public const double BusUpkeepPerWeek = 420.0;
        public const double StopCost = 600.0;
        public const double StopUpkeepPerWeek = 15.0;

        /// <summary>Grundhaltezeit (Türen, Anfahren) und Zeit je ein- bzw. aussteigendem Fahrgast.</summary>
        public const float DwellBaseSeconds = 4f;
        public const float SecondsPerBoarding = 1.2f;
        public const float SecondsPerAlighting = 0.8f;
        /// <summary>Geplante Haltezeit je Halt für den Fahrplan.</summary>
        public const float PlannedDwellSeconds = 8f;
        /// <summary>Fahrplantempo als Anteil des Tempolimits (Anfahren, Kreuzungen) und Zuschlag je Kreuzung.</summary>
        public const float ScheduleSpeedFactor = 0.75f;
        public const float ScheduleJunctionSeconds = 4f;
        /// <summary>Pünktlich = Abschnitt höchstens so viel langsamer als geplant (Faktor + Puffer).</summary>
        public const float LateFactor = 1.35f;
        public const float LateSlackSeconds = 20f;

        /// <summary>Fußweg zur/von der Haltestelle: Tempo und größte Entfernung (Luftlinie).</summary>
        public const float WalkSpeed = 1.3f;
        public const float MaxWalkMeters = 600f;
        /// <summary>Bewertung: Fußweg und Warten zählen gefühlt mehr als Fahren (Faktor).</summary>
        public const float WalkWeight = 1.3f;
        public const float WaitWeight = 1.5f;
        /// <summary>Gefühlter Aufwand je Umstieg (höchstens 1 Umstieg wird geplant).</summary>
        public const float TransferPenaltySeconds = 120f;
        /// <summary>Wartende geben auf nach max(<see cref="MinPatienceSeconds"/>, Faktor × Takt) oder nach so vielen vollen Bussen.</summary>
        public const float MinPatienceSeconds = 150f;
        public const float PatienceHeadways = 2.5f;
        public const int MaxFullBusesBeforeGivingUp = 2;

        /// <summary>Haltestellen: Abstand zu Kreuzungen (ab 3 Straßen) und zur nächsten Haltestelle derselben Seite.</summary>
        public const float MinDistanceToJunction = 15f;
        public const float MinStopSpacing = 30f;
        /// <summary>Haltestelle rastet an Stadtstraßen ein, deren Rand höchstens so weit vom Klick entfernt ist.</summary>
        public const float SnapDistance = 12f;
        /// <summary>Depot hängt an Stadtstraßen, deren Rand höchstens so weit entfernt ist (wie Dienste G4).</summary>
        public const float DepotRoadReach = 8f;
        public const float DepotMaxSlope = 8f;
    }

    /// <summary>Bauwerkstyp eines Depots (Busdepot; später Tram/Taxi).</summary>
    public sealed class DepotType
    {
        public string Id { get; }
        public string DisplayName { get; }
        public float Width { get; }
        public float Depth { get; }
        public double Cost { get; }
        public double UpkeepPerWeek { get; }
        /// <summary>So viele Fahrzeuge stellt das Depot höchstens (Fahrzeugpool).</summary>
        public int VehicleCapacity { get; }
        public VehicleClass VehicleClass { get; }

        public DepotType(string id, string name, float width, float depth, double cost, double upkeepPerWeek, int vehicles, VehicleClass cls)
        {
            Id = id;
            DisplayName = name;
            Width = width;
            Depth = depth;
            Cost = cost;
            UpkeepPerWeek = upkeepPerWeek;
            VehicleCapacity = vehicles;
            VehicleClass = cls;
        }

        public override string ToString() => DisplayName;
    }

    public static class TransitCatalog
    {
        /// <summary>Busdepot: Halle + Hof, 16 Busse. Baukosten enthalten die Fahrzeuge.</summary>
        public static readonly DepotType BusDepot = new DepotType("bus_depot", "Busdepot", 40f, 30f, 45000, 1500, 16, VehicleClass.Bus);
        public static readonly IReadOnlyList<DepotType> Depots = new[] { BusDepot };
    }

    public enum TransitBuildError
    {
        None,
        OutOfWorld,
        OnWater,
        TooSteep,
        OverlapsRoad,
        /// <summary>Anderes Bauwerk (Depot, Dienst, Versorger) im Weg.</summary>
        OverlapsFacility,
        OverlapsPole,
        /// <summary>Keine Stadtstraße am Depot (Rand ≤ <see cref="TransitRules.DepotRoadReach"/>).</summary>
        NoRoadAccess,
        InsufficientFunds,
        /// <summary>Haltestelle: keine Straße in der Nähe.</summary>
        NoRoad,
        /// <summary>Haltestellen nur an Stadtstraßen (nicht Autobahn/Rampe).</summary>
        NotCityRoad,
        /// <summary>Haltestellen nicht auf Brücken/in Tunneln.</summary>
        Elevated,
        TooCloseToJunction,
        TooCloseToStop,
        /// <summary>Diese Straßenseite hat keine Fahrspur (Einbahnstraße).</summary>
        NoLaneOnSide,
    }

    /// <summary>Straßenseite einer Haltestelle: beide Seiten (Haltestellenpaar) oder nur eine Fahrtrichtung.</summary>
    public enum StopSide : byte
    {
        Both = 0,
        /// <summary>Rechte Seite in Segmentrichtung (Rechtsverkehr: Fahrzeuge in Segmentrichtung).</summary>
        Along = 1,
        /// <summary>Rechte Seite gegen die Segmentrichtung.</summary>
        Against = 2,
    }

    /// <summary>Bushaltestelle am Fahrbahnrand einer Stadtstraße (ein Halt je Fahrtrichtung bzw. Straßenseite).</summary>
    public sealed class BusStop
    {
        public int Id { get; internal set; }
        public string Name { get; internal set; }
        public int SegmentId { get; internal set; }
        public float T { get; internal set; }
        public StopSide Side { get; internal set; }
        /// <summary>Punkt auf der Straßenmitte (Haltepositionen liegen am Rand, s. <see cref="TransitSystem.PlatformPosition"/>).</summary>
        public Vector2 Position { get; internal set; }
        internal readonly List<Passenger> Waiting = new List<Passenger>();
        /// <summary>Segment wurde entfernt, Ersatz steht noch aus (wird im nächsten Tick aufgelöst).</summary>
        internal bool Orphaned;

        /// <summary>Wartende Fahrgäste (alle Linien).</summary>
        public int WaitingCount => Waiting.Count;
        /// <summary>Eingestiegene Fahrgäste heute bzw. am Vortag.</summary>
        public int BoardedToday { get; internal set; }
        public int BoardedYesterday { get; internal set; }
        /// <summary>Richtungscode für den Verkehr (0 = beliebig, 1 = in Segmentrichtung, 2 = dagegen).</summary>
        public byte Direction => (byte)Side;
        public bool ServesAlong => Side != StopSide.Against;
        public bool ServesAgainst => Side != StopSide.Along;

        public int WaitingFor(int lineId)
        {
            int n = 0;
            foreach (var p in Waiting) if (p.CurrentLineId == lineId) n++;
            return n;
        }

        public override string ToString() => $"Haltestelle {Name} #{Id}";
    }

    /// <summary>
    /// Buslinie: geordnete Haltestellen, Rundlinie (A→B→C→A) oder Pendel (A→B→C→B→A). Route über das Straßennetz
    /// per <see cref="Pathfinder"/> je Abschnitt; Takt ergibt sich aus Umlaufzeit / Fahrzeugen.
    /// </summary>
    public sealed class TransitLine
    {
        public int Id { get; internal set; }
        public string Name { get; internal set; }
        /// <summary>Farbe (Index in die Linienpalette der Darstellung).</summary>
        public int ColorIndex { get; internal set; }
        public bool IsLoop { get; internal set; }
        internal readonly List<int> StopList = new List<int>();
        public IReadOnlyList<int> Stops => StopList;
        /// <summary>Gewünschte Zahl Busse (bestimmt den Takt).</summary>
        public int TargetVehicles { get; internal set; }

        // --- berechnete Route (je Umlauf: Halt k → Halt k+1)
        internal int[] Visits = Array.Empty<int>();
        internal float[] LegSeconds = Array.Empty<float>();
        internal float[] LegMeters = Array.Empty<float>();
        internal List<EdgeKey>[] LegRoutes = Array.Empty<List<EdgeKey>>();
        /// <summary>Haltestellen in Umlaufreihenfolge (Pendel: hin und zurück).</summary>
        public IReadOnlyList<int> VisitStops => Visits;
        /// <summary>Abschnitte als Folge gerichteter Segmente (null = Abschnitt nicht befahrbar) – für Vorschau/Darstellung.</summary>
        public IReadOnlyList<List<EdgeKey>> Legs => LegRoutes;
        /// <summary>Geplante Umlaufzeit inkl. Haltezeiten (s) und Länge (m).</summary>
        public float CycleSeconds { get; internal set; }
        public float LengthMeters { get; internal set; }
        /// <summary>Alle Abschnitte befahrbar und mindestens 2 Haltestellen.</summary>
        public bool RouteValid { get; internal set; }
        /// <summary>Erster nicht befahrbarer Abschnitt (-1 = keiner).</summary>
        public int BrokenLeg { get; internal set; } = -1;

        // --- Betrieb
        /// <summary>Busse dieser Linie im Verkehr (inkl. Anfahrt vom Depot).</summary>
        public int ActiveVehicles { get; internal set; }
        /// <summary>Takt in Sekunden (∞ ohne Busse).</summary>
        public float HeadwaySeconds => ActiveVehicles > 0 ? CycleSeconds / ActiveVehicles : float.PositiveInfinity;
        internal float NextSpawnTime;

        // --- Statistik
        public int PassengersToday { get; internal set; }
        public int PassengersYesterday { get; internal set; }
        public int PassengersTotal { get; internal set; }
        /// <summary>Anteil pünktlicher Ankünfte (gleitend über Tage, 1 = alle pünktlich).</summary>
        public float Punctuality { get; internal set; } = 1f;
        /// <summary>Mittlere Auslastung der Busse 0..1 (gleitend).</summary>
        public float AverageLoad { get; internal set; }
        /// <summary>Fahrgäste, die wegen voller Busse stehen blieben (heute).</summary>
        public int LeftBehindToday { get; internal set; }
        internal int ArrivalsToday, OnTimeToday, LoadSamples;
        internal float LoadSum;

        /// <summary>Fahrzeit laut Fahrplan von einer Haltestelle zur anderen (s, kürzeste Richtung; ∞ = nicht auf der Linie).</summary>
        public float RideSeconds(int fromStop, int toStop)
        {
            float best = float.PositiveInfinity;
            for (int k = 0; k < Visits.Length; k++)
                if (Visits[k] == fromStop) best = MathF.Min(best, RideSecondsFromVisit(k, toStop));
            return best;
        }

        /// <summary>Fahrzeit vom Halt Nummer <paramref name="visit"/> bis zur nächsten Ankunft an <paramref name="toStop"/>.</summary>
        public float RideSecondsFromVisit(int visit, int toStop)
        {
            int n = Visits.Length;
            if (n < 2 || visit < 0 || visit >= n) return float.PositiveInfinity;
            float sum = 0f;
            for (int i = 1; i <= n; i++)
            {
                sum += LegSeconds[(visit + i - 1) % n];
                if (Visits[(visit + i) % n] == toStop) return sum;
            }
            return float.PositiveInfinity;
        }

        public bool Serves(int stopId) => StopList.Contains(stopId);

        public override string ToString() => Name;
    }

    public enum BusState : byte
    {
        /// <summary>Vom Depot zum ersten Halt.</summary>
        ToLine,
        InService,
        /// <summary>Zurück ins Depot (Linie verkleinert/gelöscht).</summary>
        ToDepot,
    }

    /// <summary>Ein Bus im Liniendienst (das Fahrzeug selbst fährt im <see cref="TrafficSystem"/>).</summary>
    public sealed class Bus
    {
        public int Id { get; internal set; }
        public int LineId { get; internal set; }
        public int DepotId { get; internal set; }
        public BusState State { get; internal set; }
        public Vehicle Vehicle { get; internal set; }
        public int Capacity { get; internal set; }
        /// <summary>Nächster Halt im Umlauf (Index in <see cref="TransitLine.VisitStops"/>) und dessen Haltestelle.</summary>
        public int VisitIndex { get; internal set; }
        public int TargetStopId { get; internal set; }
        internal readonly List<Passenger> Riders = new List<Passenger>();
        public int Passengers => Riders.Count;
        public float Load => Capacity > 0 ? Riders.Count / (float)Capacity : 0f;
        internal float LegStartTime;
        internal bool MeasureLeg;
        /// <summary>Letzter Abschnitt pünktlich? Verspätung in s (≥ 0).</summary>
        public bool LastLegOnTime { get; internal set; } = true;
        public float LastDelaySeconds { get; internal set; }
        public int StopsServed { get; internal set; }

        public override string ToString() => $"Bus #{Id}";
    }

    public enum RideOutcome : byte
    {
        /// <summary>Am Ziel-Halt ausgestiegen.</summary>
        Arrived,
        /// <summary>Zu viele volle Busse – gibt auf (geht zu Fuß weiter).</summary>
        GaveUpFull,
        /// <summary>Zu lange gewartet.</summary>
        GaveUpWaiting,
        /// <summary>Linie/Haltestelle/Bus weggefallen.</summary>
        Stranded,
        /// <summary>Vom Bürgersystem abgebrochen (Zeitüberschreitung, Umzug, Wegzug).</summary>
        Cancelled,
    }

    /// <summary>Geplante ÖPNV-Verbindung (höchstens 1 Umstieg).</summary>
    public struct TransitOption
    {
        public bool Valid;
        public int Line1, Board, Alight1;
        /// <summary>Zweite Linie nach dem Umstieg an <see cref="Alight1"/> (0 = direkt).</summary>
        public int Line2, Alight2;
        public float AccessMeters, EgressMeters;
        public float WaitSeconds, RideSeconds;
        public int Transfers;
        /// <summary>Gefühlte Reisezeit (Fußweg und Warten gewichtet, + Umstiegsaufwand).</summary>
        public float PerceivedSeconds;
        /// <summary>Tatsächliche geplante Reisezeit (Fußweg + Warten + Fahrt).</summary>
        public float TotalSeconds;
        public int FinalStop => Line2 != 0 ? Alight2 : Alight1;
    }

    /// <summary>Ein Fahrgast (Bürger) auf seiner ÖPNV-Fahrt: wartet an einer Haltestelle oder sitzt im Bus.</summary>
    public sealed class Passenger
    {
        public int Id { get; internal set; }
        public int CitizenId { get; internal set; }
        public TransitOption Plan { get; internal set; }
        /// <summary>0 = erste Linie, 1 = nach dem Umstieg.</summary>
        public int Leg { get; internal set; }
        public int CurrentLineId => Leg == 0 ? Plan.Line1 : Plan.Line2;
        public int AlightStopId => Leg == 0 ? Plan.Alight1 : Plan.Alight2;
        public bool HasTransfer => Plan.Line2 != 0;
        /// <summary>Haltestelle, an der gewartet wird (0 = sitzt im Bus bzw. fertig).</summary>
        public int WaitingAtStop { get; internal set; }
        public Bus Bus { get; internal set; }
        public bool IsActive { get; internal set; } = true;
        public Vector2 Destination { get; internal set; }
        /// <summary>Wo die Fahrt endete (Haltestelle bzw. Bus) – der Rest ist Fußweg.</summary>
        public Vector2 EndPosition { get; internal set; }
        public RideOutcome Outcome { get; internal set; }
        internal float WaitSince, StartTime, BoardTime, PlannedRide;
        public float WaitedSeconds { get; internal set; }
        public int FullBusesSeen { get; internal set; }
        /// <summary>Fahrt deutlich langsamer als geplant (Stau, Verspätung).</summary>
        public bool WasLate { get; internal set; }
    }

    /// <summary>Gebautes Depot (frei platziert und gedreht wie die Dienste G4).</summary>
    public sealed class TransitDepot
    {
        public int Id { get; internal set; }
        public DepotType Type { get; internal set; }
        public Vector2 Position { get; internal set; }
        public Vector2 Front { get; internal set; }
        public float Height { get; internal set; }
        public int AccessSegmentId { get; internal set; } = -1;
        public float AccessT { get; internal set; }
        /// <summary>Fahrzeuge dieses Depots im Einsatz.</summary>
        public int VehiclesOut { get; internal set; }
        public bool HasAccess => AccessSegmentId >= 0;
        public int FreeVehicles => Math.Max(0, Type.VehicleCapacity - VehiclesOut);

        public bool Contains(Vector2 p, float margin = 0f) => ServiceGeometry.InRect(p, Position, Front, Type.Width, Type.Depth, margin);
        public Vector2[] Corners() => ServiceGeometry.Corners(Position, Front, Type.Width, Type.Depth);
        public override string ToString() => $"{Type.DisplayName} #{Id}";
    }

    public sealed class DepotPlan
    {
        public DepotType Type;
        public Vector2 Position;
        public Vector2 Front;
        public float Height;
        public double Cost;
        public TransitBuildError Error;
        public int AccessSegmentId = -1;
        public float AccessT;
        public readonly List<Building> Displaced = new List<Building>();
        public bool IsValid => Error == TransitBuildError.None;
    }

    public sealed class StopPlan
    {
        public int SegmentId = -1;
        public float T;
        public StopSide Side;
        public Vector2 Position;
        public double Cost;
        public TransitBuildError Error;
        public bool IsValid => Error == TransitBuildError.None;
    }

    /// <summary>Vorschau einer Linie (Werkzeug „Linie zeichnen“): Route je Abschnitt, Länge, Umlaufzeit.</summary>
    public sealed class LinePlan
    {
        public readonly List<int> Stops = new List<int>();
        public bool IsLoop;
        public int[] Visits = Array.Empty<int>();
        public List<EdgeKey>[] Legs = Array.Empty<List<EdgeKey>>();
        public float[] LegSeconds = Array.Empty<float>();
        public float[] LegMeters = Array.Empty<float>();
        public float LengthMeters, CycleSeconds;
        /// <summary>Erster nicht befahrbarer Abschnitt (-1 = alle befahrbar).</summary>
        public int BrokenLeg = -1;
        public bool IsValid => Stops.Count >= 2 && BrokenLeg < 0;
    }
}
