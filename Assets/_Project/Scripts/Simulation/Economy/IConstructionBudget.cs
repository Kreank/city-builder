namespace CityBuilder.Simulation.Economy
{
    /// <summary>
    /// Was Bau-Code (Straßen, Versorgung, Terraforming) von der Stadtkasse braucht: prüfen und abbuchen.
    /// Die Bausysteme kennen nur diese Schnittstelle; ist sie nicht gesetzt (null), ist Bauen kostenlos –
    /// so laufen reine Netz-/Geländetests ohne Wirtschaft.
    /// </summary>
    public interface IConstructionBudget
    {
        /// <summary>Darf jetzt so viel ausgegeben werden (Kontostand + Kreditrahmen, kein Baustopp)?</summary>
        bool CanAfford(double cost);

        /// <summary>Bucht eine Ausgabe (Baukosten). Prüft nicht – vorher <see cref="CanAfford"/>.</summary>
        void Spend(BudgetCategory category, double amount);

        /// <summary>Bucht einen Abrisserlös (Einnahme <see cref="BudgetCategory.Demolition"/>).</summary>
        void Refund(double amount);
    }

    /// <summary>
    /// Laufender Kostenträger (Dienste G4: Polizei, Schule …). Systeme melden ihre Objekte mit
    /// <see cref="EconomySystem.RegisterUpkeep"/> an; die Kasse bucht täglich ein Siebtel von
    /// <see cref="WeeklyUpkeep"/> unter <see cref="BudgetCategory.Services"/> ab und weist es je <see cref="Name"/> aus.
    /// </summary>
    public interface IUpkeepSource
    {
        /// <summary>Anzeigename in der Bilanz (z. B. „Polizei“). Gleiche Namen werden zusammengefasst.</summary>
        string Name { get; }

        /// <summary>Aktueller Unterhalt in € je Spielwoche (≥ 0). Darf sich ändern (Ausbau, Budgetregler).</summary>
        double WeeklyUpkeep();
    }
}
