namespace CityBuilder.Simulation.Core
{
    /// <summary>
    /// Spielzeit der Simulation. Alles in der Simulation rechnet in Ticks,
    /// niemals in Echtzeit-Sekunden – nur so bleibt sie deterministisch und
    /// unabhängig von Framerate und Spielgeschwindigkeit.
    /// </summary>
    public sealed class SimulationClock
    {
        /// <summary>1 Tick = 6 Spielminuten.</summary>
        public const int TicksPerDay = 240;
        public const int DaysPerYear = 360;

        public long Tick { get; private set; }

        public int Day => (int)(Tick / TicksPerDay);
        public int Year => Day / DaysPerYear;
        public int DayOfYear => Day % DaysPerYear;

        /// <summary>0..1 über den Tag.</summary>
        public float TimeOfDay => (Tick % TicksPerDay) / (float)TicksPerDay;

        public int Hour => (int)(TimeOfDay * 24f);
        public int Minute => (int)(TimeOfDay * 24f * 60f) % 60;

        internal void Advance() => Tick++;

        /// <summary>Spielstand: Uhr auf einen gespeicherten Tick setzen.</summary>
        internal void Restore(long tick) => Tick = tick;
    }
}
