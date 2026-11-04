namespace CityBuilder.Simulation.Economy
{
    /// <summary>
    /// Buchungskategorie der Stadtkasse. Einnahmen stehen vor <see cref="RoadConstruction"/>,
    /// Ausgaben ab dort (<see cref="Budget.IsIncome"/>); Ausnahme: die hinten angefügten Handelskategorien
    /// (Etappe 8), deren Richtung <see cref="Budget.IsIncome"/> einzeln kennt. Neue Kategorien hinten anfügen
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
        /// <summary>ÖPNV: Fahrgeld je Einstieg in einen Linienbus.</summary>
        TransitFares,

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
        /// <summary>ÖPNV: Bau von Haltestellen und Depots.</summary>
        TransitConstruction,

        // --- Etappe 8 Handel (hinten angefügt, damit bestehende Werte gleich bleiben; Einnahme/Ausgabe s. Budget.IsIncome)
        /// <summary>Einnahme: Zölle auf Importe (Weltmarkt und Vertragsware).</summary>
        Tariffs,
        /// <summary>Einnahme: Verkauf von Vertragsware an Firmen bzw. Erlös aus Exportverträgen.</summary>
        TradeSales,
        /// <summary>Einnahme: Vertragsstrafen anderer Länder.</summary>
        PenaltiesReceived,
        /// <summary>Ausgabe: Einkauf von Vertragsware bzw. Bezahlung der Firmen für Exportware.</summary>
        TradePurchases,
        /// <summary>Ausgabe: eigene Vertragsstrafen und Kündigungsgebühren.</summary>
        PenaltiesPaid,
        // --- Ende Etappe 8
        /// <summary>Ausgabe: Bau/Umzug einzeln gesetzter Wohnhäuser (Anno-Stil).</summary>
        HouseConstruction,

        // --- Produktion lenken (hinten angefügt)
        /// <summary>Ausgabe: Wirtschaftsförderung je Branche (Geld je Beschäftigtem an geförderte Firmen).</summary>
        IndustrySubsidies,
        // --- Ende Produktion lenken

        // --- Bahn (Stufe 2, hinten angefügt; Einnahme/Ausgabe s. Budget.IsIncome)
        /// <summary>Ausgabe: Bau von Gleisen und Bahnhöfen.</summary>
        RailConstruction,
        /// <summary>Einnahme: Stationsentgelte (Halt Fern-/Güterzug), Fahrgeldanteil je Ein-/Aussteiger, Umschlag je Ladung.</summary>
        RailFares,
        // --- Ende Bahn

        // --- Wasserstraße & Hafen (Stufe 3, hinten angefügt; Einnahme/Ausgabe s. Budget.IsIncome)
        /// <summary>Ausgabe: Bau von Häfen und Klappbrücken (Mechanik; die Fahrbahn bucht der Straßenbau).</summary>
        HarborConstruction,
        /// <summary>Einnahme: Hafengeld je Schiffsanlauf und Umschlag je Ladung.</summary>
        HarborFees,
        // --- Ende Wasserstraße & Hafen

        // --- Flughafen (Stufe 4, hinten angefügt; Einnahme/Ausgabe s. Budget.IsIncome)
        /// <summary>Ausgabe: Bau und Ausbau von Flughäfen (inkl. Einebnen des Geländes).</summary>
        AirportConstruction,
        /// <summary>Einnahme: Landeentgelte, Passagierentgelte, Umschlag Luftfracht.</summary>
        AirportFees,
        /// <summary>Einnahme: Tourismus (Kurtaxe, Eintritt in Freizeitanlagen/Sehenswürdigkeiten).</summary>
        TourismIncome,
        // --- Ende Flughafen
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
        public const int Count = (int)BudgetCategory.TourismIncome + 1; // letzte Kategorie (Wohnhäuser, Produktion lenken, Bahn, Hafen, dann Flughafen)
        public const int TaxKinds = (int)TaxKind.Industry + 1;

        public static bool IsIncome(BudgetCategory c) =>
            c < BudgetCategory.RoadConstruction
            || c == BudgetCategory.Tariffs || c == BudgetCategory.TradeSales || c == BudgetCategory.PenaltiesReceived // Etappe 8
            || c == BudgetCategory.RailFares  // Bahn
            || c == BudgetCategory.HarborFees  // Wasserstraße & Hafen
            || c == BudgetCategory.AirportFees || c == BudgetCategory.TourismIncome; // Flughafen

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
                case BudgetCategory.TransitFares: return "Fahrgeld ÖPNV";
                case BudgetCategory.TransitConstruction: return "Bau ÖPNV";
                // --- Etappe 8 Handel
                case BudgetCategory.Tariffs: return "Zölle";
                case BudgetCategory.TradeSales: return "Handel: Verkauf";
                case BudgetCategory.PenaltiesReceived: return "Vertragsstrafen (erhalten)";
                case BudgetCategory.TradePurchases: return "Handel: Einkauf";
                case BudgetCategory.PenaltiesPaid: return "Vertragsstrafen (gezahlt)";
                // --- Ende Etappe 8
                case BudgetCategory.HouseConstruction: return "Bau Gebäude (gesetzt)"; // Setzen statt Zonen: Wohnhäuser, Läden, Büros, Hallen
                case BudgetCategory.IndustrySubsidies: return "Wirtschaftsförderung"; // Produktion lenken
                case BudgetCategory.RailConstruction: return "Bau Bahn";              // Bahn
                case BudgetCategory.RailFares: return "Bahn: Entgelte & Fahrgeld";   // Bahn
                case BudgetCategory.HarborConstruction: return "Bau Hafen & Klappbrücken"; // Wasserstraße & Hafen
                case BudgetCategory.HarborFees: return "Hafen: Hafengeld & Umschlag";      // Wasserstraße & Hafen
                case BudgetCategory.AirportConstruction: return "Bau Flughafen";                   // Flughafen
                case BudgetCategory.AirportFees: return "Flughafen: Lande- & Passagierentgelte"; // Flughafen
                case BudgetCategory.TourismIncome: return "Tourismus: Kurtaxe & Eintritt";       // Flughafen (Tourismus)
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
