using System.Numerics;
using CityBuilder.Simulation.Citizens;

namespace CityBuilder.Simulation.People
{
    /// <summary>Was ein Bürger sichtbar gerade tut.</summary>
    public enum PedestrianActivity : byte
    {
        /// <summary>Nicht sichtbar (im Gebäude, im Auto, außerhalb).</summary>
        Hidden,
        Walking,
        Standing,
        /// <summary>Wartet (z. B. an einer Haltestelle, s. <see cref="TransitPose"/>).</summary>
        Waiting,
    }

    /// <summary>Wo der Bürger steht bzw. was für ein Weg es ist.</summary>
    public enum PedestrianSpot : byte { None, Trip, Door, Shop, Park, Stroll, Stop }

    /// <summary>Sichtbarer Zustand eines Bürgers zu einem Zeitpunkt (Simulationszeit in Ticks, mit Bruchteil).</summary>
    public struct PedestrianPose
    {
        public PedestrianActivity Kind;
        public PedestrianSpot Spot;
        public Vector2 Position;
        /// <summary>Absolute Höhe der Füße (inkl. Gehweg-/Fahrbahnbelag).</summary>
        public float Height;
        /// <summary>Blick-/Laufrichtung (normalisiert).</summary>
        public Vector2 Direction;
        /// <summary>Darstellungsgeschwindigkeit in m je Tick (Weglänge / Gehzeit; 1 Tick = 30 s Verkehrszeit).</summary>
        public float MetersPerTick;
        /// <summary>Gehtempo in m/s Verkehrszeit (Zeitmodell: ≈ <see cref="Core.TimeModel.WalkSpeed"/>).</summary>
        public float MetersPerSecond => MetersPerTick / Core.TimeModel.TrafficSecondsPerTick;
        /// <summary>Anteil des Wegs (0..1) bei <see cref="PedestrianActivity.Walking"/>.</summary>
        public float Progress;
        /// <summary>Steht gerade auf einer Fahrbahn (Zebrastreifen).</summary>
        public bool OnCrossing;

        public bool IsVisible => Kind != PedestrianActivity.Hidden;
        public static readonly PedestrianPose Hidden = default;
    }

    public enum TransitPoseKind : byte
    {
        /// <summary>Keine Vorgabe – normale Fußgänger-Logik.</summary>
        None,
        /// <summary>Im Fahrzeug (Bus/Bahn) – nicht als Fußgänger zeichnen.</summary>
        Hidden,
        /// <summary>Steht an <see cref="TransitPose.Position"/> (Haltestellenpunkt).</summary>
        Waiting,
        /// <summary>Geht von <see cref="TransitPose.WalkFrom"/> nach <see cref="TransitPose.WalkTo"/> zwischen WalkStart und WalkEnd.</summary>
        Walking,
    }

    /// <summary>
    /// Erweiterungsstelle für den ÖPNV (Agent „transit“): Vorgabe des sichtbaren Zustands eines Bürgers, z. B.
    /// „wartet an Haltestelle“ oder „geht zur Haltestelle“. Wird über <see cref="PedestrianPlanner.Transit"/> eingehängt.
    /// Orte: <see cref="Place"/> (Gebäude/Außenanschluss) oder – wenn <c>IsNone</c> – der freie Punkt (z. B. Haltestelle).
    /// </summary>
    public struct TransitPose
    {
        public TransitPoseKind Kind;
        public Vector2 Position;
        public float Height;
        public Vector2 Facing;
        public Place WalkFrom, WalkTo;
        public Vector2 WalkFromPoint, WalkToPoint;
        public double WalkStart, WalkEnd;

        public static readonly TransitPose None = default;
        public static TransitPose InVehicle => new TransitPose { Kind = TransitPoseKind.Hidden };

        public static TransitPose WaitingAt(Vector2 stop, float height, Vector2 facing) =>
            new TransitPose { Kind = TransitPoseKind.Waiting, Position = stop, Height = height, Facing = facing };

        /// <summary>Weg zwischen zwei Punkten bzw. Orten (Place.None = Punkt benutzen).</summary>
        public static TransitPose Walk(Place from, Vector2 fromPoint, Place to, Vector2 toPoint, double start, double end) =>
            new TransitPose
            {
                Kind = TransitPoseKind.Walking, WalkFrom = from, WalkFromPoint = fromPoint, WalkTo = to, WalkToPoint = toPoint,
                WalkStart = start, WalkEnd = end,
            };
    }
}
