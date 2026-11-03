using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Traffic
{
    /// <summary>Ein Fahrzeug im Verkehr. Fährt entweder auf einer Spur oder über eine Kreuzungsverbindung.</summary>
    public sealed class Vehicle
    {
        public int Id { get; internal set; }
        public VehicleClass Class { get; internal set; }
        public float Length { get; internal set; }
        public float MaxSpeed { get; internal set; }
        public float Acceleration { get; internal set; }
        /// <summary>Für die Darstellung (Farbvariante).</summary>
        public int Variant { get; internal set; }

        public Lane Lane { get; internal set; }
        public LaneConnection Connection { get; internal set; }
        public bool InJunction => Connection != null;

        /// <summary>Gefahrene Strecke auf der aktuellen Spur bzw. Verbindung (Front des Fahrzeugs).</summary>
        public float S { get; internal set; }
        public float Speed { get; internal set; }

        internal List<EdgeKey> Route = new List<EdgeKey>();
        internal int RouteIndex;
        public RoadNode Destination { get; internal set; }
        internal LaneConnection Planned;

        /// <summary>Fahrt eines Bürgers (0 = Hintergrund-/Durchgangsverkehr). Wird bei <see cref="TrafficSystem.TripEnded"/> zurückgemeldet.</summary>
        public int TripId { get; internal set; }
        /// <summary>Ziel mitten auf einem Segment (Gebäudezufahrt); -1 = Ziel ist <see cref="Destination"/>.</summary>
        public int DestinationSegmentId { get; internal set; } = -1;
        /// <summary>Segment-Parameter t der Zufahrt.</summary>
        public float DestinationT { get; internal set; }
        public bool HasSegmentDestination => DestinationSegmentId >= 0;
        /// <summary>Verkehrszeit (<see cref="TrafficSystem.Time"/>, s) beim Start – für die Fahrtdauer im Inspektor.</summary>
        public double StartTime { get; internal set; }

        /// <summary>Wartezeit am Stück (s) – für Deadlock-Auflösung und Umleitung.</summary>
        public float WaitTime { get; internal set; }
        internal float StopLineWait;
        internal float LaneChangeCooldown;
        /// <summary>Seitlicher Versatz beim Spurwechsel (klingt ab).</summary>
        public float Lateral { get; internal set; }
        internal long LastTick;
        internal int Reroutes;

        // Darstellung: Position am Ende des letzten und vorletzten Ticks (für Interpolation)
        public Vector2 Position { get; internal set; }
        public float Height { get; internal set; }
        public Vector2 Direction { get; internal set; }
        public Vector2 PrevPosition { get; internal set; }
        public float PrevHeight { get; internal set; }
        public Vector2 PrevDirection { get; internal set; }

        // --- ÖPNV: Linienfahrzeuge (Bus), gesteuert vom TransitSystem (s. TrafficSystem.Scheduled.cs)
        /// <summary>Linienfahrzeug: verschwindet am Ziel nicht, sondern hält (Haltestelle) und bekommt das nächste Ziel.</summary>
        public bool IsScheduled { get; internal set; }
        /// <summary>Id des Busses im <c>TransitSystem</c> (0 = kein Linienfahrzeug).</summary>
        public int TransitId { get; internal set; }
        /// <summary>Steht gerade an einer Haltestelle (Ein-/Aussteigen).</summary>
        public bool IsAtStop => AtStop;
        internal bool AtStop;
        /// <summary>Verkehrszeit, bis zu der das Fahrzeug an der Haltestelle stehen bleibt.</summary>
        internal double HoldUntil;
        /// <summary>Zielrichtung auf dem Zielsegment: 0 = beliebig, 1 = in Segmentrichtung, 2 = dagegen.</summary>
        internal byte DestinationDirection;
        // --- Ende ÖPNV

        public bool IsOnFinalEdge => RouteIndex >= Route.Count - 1;
        public EdgeKey CurrentEdge => Route[RouteIndex];
        public EdgeKey? NextEdge => RouteIndex + 1 < Route.Count ? Route[RouteIndex + 1] : (EdgeKey?)null;
        public EdgeKey? EdgeAfterNext => RouteIndex + 2 < Route.Count ? Route[RouteIndex + 2] : (EdgeKey?)null;
        public int RemainingEdges => Route.Count - RouteIndex;
        /// <summary>Abschnitte der ganzen Route (lesend, für die Anzeige).</summary>
        public int RouteEdges => Route.Count;
        /// <summary>Fortschritt der Route 0..1 nach Abschnitten (lesend, für die Anzeige; Umleitung kann ihn zurücksetzen).</summary>
        public float RouteProgress => Route.Count == 0 ? 0f : System.Math.Min(1f, System.Math.Max(0f, RouteIndex / (float)Route.Count));

        public override string ToString() => $"{VehicleClasses.DisplayName(Class)} #{Id}";
    }
}
