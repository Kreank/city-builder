namespace CityBuilder.Simulation.Economy
{
    /// <summary>
    /// Buchungskategorie der Stadtkasse. Einnahmen stehen vor <see cref="RoadConstruction"/>,
    /// Ausgaben ab dort (<see cref="Budget.IsIncome"/>). Neue Kategorien hinten bei ihrer Gruppe anfügen
    /// und <see cref="Budget.Count"/> anpassen.
    /// </summary>
    public enum BudgetCategory
    {
        // --- Einnahmen
        /// <summary>Grundsteuer Wohnen je bewohntem Haushalt (Wohnzonen + Wohnungen im Mischgebiet).</summary>
        ResidentialTax = 0,
        /// <summary>Gewerbesteuer Einzelhandel/Mischgebiet je besetztem Arbeitsplatz.</summary>
        CommercialTax,
        /// <summary>Gewerbe-/Einkommensteuer Büro je besetztem Arbeitsplatz.</summary>
        OfficeTax,
        /// <summary>Gewerbesteuer Industrie je besetztem Arbeitsplatz.</summary>
        IndustryTax,
        /// <summary>Gebühren (später: ÖPNV-Tickets, Maut, Parken …) – noch keine Quelle.</summary>
        Fees,
        /// <summary>Erlös aus dem Abriss eigener Bauten (Teil der Baukosten).</summary>
        Demolition,

        // --- Ausgaben
        RoadConstruction,
        UtilityConstruction,
        Terraforming,
        RoadUpkeep,
        /// <summary>Unterhalt der Versorgungsbauwerke (aus <c>UtilityCatalog</c>).</summary>
        UtilityUpkeep,
        /// <summary>Unterhalt frei verlegter Leitungen/Rohre.</summary>
        LineUpkeep,
        /// <summary>Weitere Kostenträger über <see cref="IUpkeepSource"/> (Dienste G4 …).</summary>
        Services,
        /// <summary>Überziehungszinsen.</summary>
        Interest,
        /// <summary>Bau von Dienstgebäuden (G4).</summary>
        ServiceConstruction,
    }

    /// <summary>Steuerart mit eigenem, einstellbarem Satz.</summary>
    public enum TaxKind
    {
        Residential = 0,
        Commercial,
        Office,
        Industry,
    }

    public static class Budget
    {
        public const int Count = (int)BudgetCategory.ServiceConstruction + 1;
        public const int TaxKinds = (int)TaxKind.Industry + 1;

        public static bool IsIncome(BudgetCategory c) => c < BudgetCategory.RoadConstruction;

        public static BudgetCategory TaxCategory(TaxKind kind)
        {
            switch (kind)
            {
                case TaxKind.Residential: return BudgetCategory.ResidentialTax;
                case TaxKind.Commercial: return BudgetCategory.CommercialTax;
                case TaxKind.Office: return BudgetCategory.OfficeTax;
                default: return BudgetCategory.IndustryTax;
            }
        }

        public static string DisplayName(BudgetCategory c)
        {
            switch (c)
            {
                case BudgetCategory.ResidentialTax: return "Grundsteuer Wohnen";
                case BudgetCategory.CommercialTax: return "Gewerbesteuer Handel";
                case BudgetCategory.OfficeTax: return "Steuer Büro";
                case BudgetCategory.IndustryTax: return "Gewerbesteuer Industrie";
                case BudgetCategory.Fees: return "Gebühren";
                case BudgetCategory.Demolition: return "Abrisserlös";
                case BudgetCategory.RoadConstruction: return "Straßenbau";
                case BudgetCategory.UtilityConstruction: return "Bau Versorgung";
                case BudgetCategory.Terraforming: return "Erdarbeiten";
                case BudgetCategory.RoadUpkeep: return "Unterhalt Straßen";
                case BudgetCategory.UtilityUpkeep: return "Unterhalt Versorger";
                case BudgetCategory.LineUpkeep: return "Unterhalt Leitungen";
                case BudgetCategory.Services: return "Dienste";
                case BudgetCategory.Interest: return "Zinsen";
                case BudgetCategory.ServiceConstruction: return "Bau Dienste";
                default: return c.ToString();
            }
        }

        public static string DisplayName(TaxKind k)
        {
            switch (k)
            {
                case TaxKind.Residential: return "Wohnen";
                case TaxKind.Commercial: return "Handel";
                case TaxKind.Office: return "Büro";
                default: return "Industrie";
            }
        }
    }
}
