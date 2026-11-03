using System;

namespace CityBuilder.Simulation.Core
{
    /// <summary>
    /// Zeitmodell der Simulation – <b>alle Zeitkonstanten an einem Ort</b> (s. CLAUDE.md „Zeitmodell“).
    /// <para>
    /// Zwei Zeitachsen, fest miteinander verzahnt (deterministisch, nur Ganzzahl-Schritte):
    /// <list type="bullet">
    /// <item><b>Kalender</b> (Spieluhr, <see cref="SimulationClock"/>): 1 Tick = <see cref="GameMinutesPerTick"/> Spielminuten,
    /// <see cref="TicksPerDay"/> Ticks je Tag. Tagesabläufe, Produktion, Abrechnungen, Bedürfnisse rechnen in Ticks.</item>
    /// <item><b>Verkehrszeit</b> (<c>TrafficSystem.Time</c>, physikalische Sekunden): Fahrzeuge, Busse, Fußwege. Je Kalender-Tick
    /// laufen <see cref="TrafficStepsPerTick"/> Verkehrsschritte à <see cref="TrafficStepSeconds"/> s, also
    /// <see cref="TrafficSecondsPerTick"/> s Verkehrszeit.</item>
    /// </list>
    /// <b>Zeitdehnung</b> (<see cref="Dilation"/>): 1 Verkehrssekunde = 8 Spielsekunden. Eine Autofahrt von 3 Minuten
    /// Verkehrszeit dauert damit 24 Spielminuten – plausibel im Kalender, ohne dass der Spieltag in Echtzeit endlos wird
    /// (Prinzip wie Cities: Skylines: Bewegung läuft „langsamer“ als der Kalender). Der Tagesablauf bleibt tolerant
    /// (Arbeitsbeginn-Fenster, Ankunft beendet erst die Fahrt).
    /// </para>
    /// <para>
    /// Echtzeit (nur Unity-Seite, <c>SimulationRunner</c>): Tempo 1/2/3 = <see cref="TicksPerRealSecond"/> × 1/3/6
    /// (1 Spieltag = 36 / 12 / 6 Minuten).
    /// Die Verkehrsschritte eines Ticks werden über die Frames verteilt (<c>SimulationWorld.Substep</c>).
    /// </para>
    /// </summary>
    public static class TimeModel
    {
        // ------------------------------------------------------------ Kalender

        /// <summary>Spielminuten je Kalender-Tick.</summary>
        public const int GameMinutesPerTick = 6;
        /// <summary>Kalender-Ticks je Spielstunde.</summary>
        public const int TicksPerHour = 60 / GameMinutesPerTick;
        /// <summary>Kalender-Ticks je Spieltag (240).</summary>
        public const int TicksPerDay = 24 * TicksPerHour;
        /// <summary>Spielsekunden je Kalender-Tick.</summary>
        public const float GameSecondsPerTick = GameMinutesPerTick * 60f;

        // ------------------------------------------------------------ Verkehrszeit

        /// <summary>Länge eines Verkehrsschritts (IDM-Fahrzeugfolge, Ampeln …) in Sekunden Verkehrszeit.</summary>
        public const float TrafficStepSeconds = 0.25f;
        /// <summary>Verkehrsschritte je Kalender-Tick (Substeps).</summary>
        public const int TrafficStepsPerTick = 180;
        /// <summary>Verkehrszeit je Kalender-Tick (45 s).</summary>
        public const float TrafficSecondsPerTick = TrafficStepSeconds * TrafficStepsPerTick;
        /// <summary>Zeitdehnung: Spielsekunden je Verkehrssekunde (8).</summary>
        public const float Dilation = GameSecondsPerTick / TrafficSecondsPerTick;
        /// <summary>Verkehrszeit je Spieltag (10 800 s = 3 h).</summary>
        public const float TrafficSecondsPerDay = TrafficSecondsPerTick * TicksPerDay;

        // ------------------------------------------------------------ Gehen, Rad, Platzhalter (Verkehrszeit)

        /// <summary>Gehtempo der Fußgänger (m/s Verkehrszeit) – Fußwege, Zugang zur Haltestelle, Parkwege.</summary>
        public const float WalkSpeed = 1.3f;
        /// <summary>Umwegfaktor Gehweg gegenüber Luftlinie.</summary>
        public const float WalkDetour = 1.25f;
        /// <summary>Bis zu dieser Luftlinie gehen Platzhalter-Fahrten zu Fuß, darüber Rad/Sonstiges.</summary>
        public const float WalkMaxMeters = 900f;
        /// <summary>Tempo der Platzhalter „Rad/Sonstiges“ (m/s Verkehrszeit, ≈ 16 km/h).</summary>
        public const float BikeSpeed = 4.5f;
        /// <summary>Lkw-Platzhalter: Umweg 1,3 × Luftlinie bei diesem Tempo (m/s Verkehrszeit).</summary>
        public const float TruckPlaceholderSpeed = 9f;
        /// <summary>Zusätzliche Verkehrszeit außerhalb der Karte (Pendler, Import/Export): 3 Ticks = 18 Spielminuten.</summary>
        public const float OutsideTrafficSeconds = 3 * TrafficSecondsPerTick;

        // ------------------------------------------------------------ Echtzeit (Unity-Seite, nur Darstellung/Takt)

        /// <summary>Echtzeit je Spieltag bei Tempo 1 (36 Minuten; Entscheidung des Spielers 2026-11-03: Verkehr sichtbar
        /// nur ~5× schneller als echt statt 30×).</summary>
        public const float RealSecondsPerDayAtSpeed1 = 2160f;
        /// <summary>Kalender-Ticks je Echtzeitsekunde bei Tempo 1 (240 / 2160 s = 0,111 → 5 s Verkehrszeit je Sekunde).</summary>
        public const float TicksPerRealSecond = TicksPerDay / RealSecondsPerDayAtSpeed1;
        /// <summary>Vervielfacher der Tempostufen (Pause, 1, 2, 3): 1 Tag = 36 / 12 / 6 Minuten, Verkehr 5× / 15× / 30× Echtzeit.</summary>
        public static readonly int[] SpeedMultipliers = { 0, 1, 3, 6 };

        // ------------------------------------------------------------ Umrechnungen

        /// <summary>Verkehrssekunden → Kalender-Ticks (aufgerundet, mindestens 1).</summary>
        public static int TicksFor(double trafficSeconds) =>
            trafficSeconds <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(trafficSeconds / TrafficSecondsPerTick - 1e-9));

        /// <summary>Verkehrssekunden → Spielminuten (Zeitdehnung).</summary>
        public static double GameMinutes(double trafficSeconds) => trafficSeconds * Dilation / 60.0;

        /// <summary>Kalender-Ticks → Verkehrssekunden.</summary>
        public static double TrafficSeconds(long ticks) => ticks * (double)TrafficSecondsPerTick;

        /// <summary>Fußweg (Verkehrssekunden) für eine Luftlinie in Metern: Umweg × Gehtempo.</summary>
        public static float WalkSeconds(float airMeters) => airMeters * WalkDetour / WalkSpeed;

        /// <summary>Platzhalter-Fahrt ohne Auto/Bus: bis <see cref="WalkMaxMeters"/> zu Fuß, sonst Rad/Sonstiges.</summary>
        public static float OtherTripSeconds(float airMeters) =>
            airMeters <= WalkMaxMeters ? WalkSeconds(airMeters) : airMeters * WalkDetour / BikeSpeed;

        /// <summary>Fußweg in Kalender-Ticks (aufgerundet, mindestens 1) – gemeinsame Formel für Bürgersystem und Fußgänger-Darstellung.</summary>
        public static int WalkTicks(float airMeters) => TicksFor(WalkSeconds(airMeters));

        /// <summary>Echtzeit (s) je Spieltag bei einer Tempostufe (1..3).</summary>
        public static float RealSecondsPerDay(int speedIndex)
        {
            int m = SpeedMultipliers[Math.Clamp(speedIndex, 1, SpeedMultipliers.Length - 1)];
            return TicksPerDay / (TicksPerRealSecond * m);
        }
    }
}
