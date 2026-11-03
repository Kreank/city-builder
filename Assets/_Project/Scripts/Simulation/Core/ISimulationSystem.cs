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

    /// <summary>
    /// Teilsystem, das zusätzlich in jedem <b>Verkehrsschritt</b> läuft (<see cref="TimeModel.TrafficStepsPerTick"/> je Tick,
    /// <see cref="TimeModel.TrafficStepSeconds"/> Verkehrszeit): Verkehr, Busbetrieb, Abfahrten. Die Verkehrsschritte eines
    /// Ticks laufen <b>vor</b> dem Kalender-Tick (Uhr noch beim alten Tick); Reihenfolge = Registrierung.
    /// </summary>
    public interface ISubstepSystem
    {
        void Substep(SimulationWorld world);
    }
}
