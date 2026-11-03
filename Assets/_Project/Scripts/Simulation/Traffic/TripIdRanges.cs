namespace CityBuilder.Simulation.Traffic
{
    /// <summary>
    /// Spannen der Fahrt-Ids (<see cref="Vehicle.TripId"/>), damit Systeme ihre Fahrten an <see cref="TrafficSystem.TripEnded"/>
    /// erkennen: Bürger &gt; 0 (zählen ab 1 aufwärts), Lkw des Warensystems &lt; 0 (ab −1 abwärts), <b>Dienstfahrzeuge</b>
    /// (Müllwagen, Streifenwagen, Feuerwehr, Rettungswagen) in einer eigenen Spanne weit unten – das Warensystem müsste über
    /// 1,5 Milliarden Lieferungen verschicken, bevor es dort ankommt. Neue Fahrzeugarten bekommen hier eine eigene Spanne.
    /// </summary>
    public static class TripIdRanges
    {
        /// <summary>Erste Fahrt-Id der Dienstfahrzeuge (zählt abwärts).</summary>
        public const int ServiceFirst = -1_500_000_000;
        /// <summary>Letzte Fahrt-Id der Dienstfahrzeuge (danach wieder <see cref="ServiceFirst"/>).</summary>
        public const int ServiceLast = -1_900_000_000;

        /// <summary>Gehört die Fahrt zu einem Dienstfahrzeug?</summary>
        public static bool IsService(int tripId) => tripId <= ServiceFirst && tripId >= ServiceLast;
    }
}
