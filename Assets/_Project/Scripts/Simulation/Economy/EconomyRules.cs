namespace CityBuilder.Simulation.Economy
{
    /// <summary>
    /// Alle Stellschrauben der Stadtkasse an einem Ort (Platzhalter fürs Balancing).
    /// Geld in €, Zeit in Spieltagen (1 Woche = 7 Tage, 1 Tag = 240 Ticks).
    /// </summary>
    public static class EconomyRules
    {
        public const int DaysPerWeek = 7;
        /// <summary>So viele abgeschlossene Wochen hält die Historie (Diagramme).</summary>
        public const int HistoryWeeks = 52;

        // ------------------------------------------------------------ Kredit
        /// <summary>Überziehungsrahmen: so weit darf der Kontostand durch Bauten ins Minus gehen.</summary>
        public const double CreditLimit = 50000.0;
        /// <summary>Zinsen je Tag auf den negativen Kontostand (0,2 % ≈ 1,4 % je Woche).</summary>
        public const double DailyOverdraftInterest = 0.002;
        /// <summary>So viele Tage am Stück im Minus → Baustopp (keine Neubauten, bis der Kontostand wieder ≥ 0 ist).</summary>
        public const int FreezeAfterDaysInDebt = 4 * DaysPerWeek;

        // ------------------------------------------------------------ Steuern
        public const float DefaultTaxRate = 0.10f;
        public const float MinTaxRate = 0f;
        public const float MaxTaxRate = 0.30f;
        public const float TaxStep = 0.01f;

        /// <summary>Steuerbasis je bewohntem Haushalt und Tag (× Satz Wohnen: 10 % → 4 €/Tag).</summary>
        public const double HouseholdTaxBase = 40.0;
        /// <summary>Steuerbasis je besetztem Arbeitsplatz und Tag (× Satz der Zonenart).</summary>
        public const double CommercialJobTaxBase = 45.0;
        public const double OfficeJobTaxBase = 60.0;
        public const double IndustryJobTaxBase = 40.0;
        /// <summary>
        /// G5: Gewerbesteuer aus dem Gewinn = Tagesgewinn × Faktor × Satz (10 % → 25 % des Gewinns; Verlust → 0).
        /// Der Faktor deckt vorerst auch den Lohnsteueranteil ab, den die alte Job-Pauschale enthielt.
        /// </summary>
        public const double ProfitTaxMultiplier = 2.5;

        // --- G6 Bodenwert: Grundsteuer nach Bodenwert (statt pauschal je Haushalt)
        /// <summary>
        /// Bodenwert, bei dem ein bewohnter Haushalt genau <see cref="HouseholdTaxBase"/> zahlt. 0,30 ≈ Bodenwert eines
        /// typischen Wohngebäudes an der Straße ohne Dienste (Grundwert + Straße + etwas Jobs/Dichte − Straßenlärm) →
        /// eine Durchschnittsstadt bringt etwa die bisherigen Beträge; gute Lagen mehr, schlechte weniger.
        /// </summary>
        public const float PropertyTaxReferenceLandValue = 0.30f;
        public const float PropertyTaxMinFactor = 0.25f, PropertyTaxMaxFactor = 2.5f;

        /// <summary>Faktor auf die Grundsteuer-Basis: Bodenwert / Referenz, begrenzt auf 0,25..2,5.</summary>
        public static float PropertyTaxFactor(float landValue) =>
            System.Math.Clamp(landValue / PropertyTaxReferenceLandValue, PropertyTaxMinFactor, PropertyTaxMaxFactor);
        // --- Ende G6

        /// <summary>Nachfrage-Bonus je Prozentpunkt unter dem Standardsatz (0 % → ×1,2).</summary>
        public const float DemandBonusPerPoint = 0.02f;
        /// <summary>Nachfrage-Malus je Prozentpunkt über dem Standardsatz (20 % → ×0,6, 30 % → ×0,2).</summary>
        public const float DemandPenaltyPerPoint = 0.04f;
        public const float MinDemandFactor = 0.1f;

        // ------------------------------------------------------------ Unterhalt
        /// <summary>Unterhalt Straßen je Tag als Anteil des Neubaupreises (inkl. Brücken-/Tunnelaufschlag).</summary>
        public const double RoadUpkeepShare = 0.005;
        /// <summary>Unterhalt freier Leitungen/Rohre je Tag als Anteil des Baupreises.</summary>
        public const double LineUpkeepShare = 0.005;

        // ------------------------------------------------------------ Bau
        /// <summary>Anteil der Baukosten, der beim Abriss zurückkommt.</summary>
        public const double DemolitionRefundShare = 0.25;
        /// <summary>Erdarbeiten je bewegtem m³ (Terraforming).</summary>
        public const double TerraformCostPerCubicMeter = 0.5;
    }
}
