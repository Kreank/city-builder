namespace CityBuilder.Simulation.Core
{
    /// <summary>
    /// Ein Teilsystem der Simulation (Umwelt, Wirtschaft, Bevölkerung, Handel ...).
    /// Systeme werden in der Reihenfolge ausgeführt, in der sie registriert wurden.
    /// </summary>
    public interface ISimulationSystem
    {
        /// <summary>Alle wie viele Ticks das System läuft (1 = jeden Tick).</summary>
        int TickInterval { get; }

        void Tick(SimulationWorld world);
    }
}
