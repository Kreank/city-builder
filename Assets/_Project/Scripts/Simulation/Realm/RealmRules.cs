namespace CityBuilder.Simulation.Realm
{
    /// <summary>
    /// Alle Stellschrauben von Etappe 7 „Reich“ an einem Ort (Platzhalter fürs Balancing): Städte gründen, aggregierte
    /// Stadt-Volkswirtschaft, interne Lieferungen, Land gründen, NPC-Expansion. Geld in €, Zeit in Spieltagen.
    /// </summary>
    public static class RealmRules
    {
        // ------------------------------------------------------------ Städte gründen
        /// <summary>Höchstzahl eigener Städte.</summary>
        public const int MaxCities = 8;
        /// <summary>Gründungskosten aus der Stadtkasse der Mutterstadt (die gerade detailliert läuft).</summary>
        public const double FoundingCost = 250000.0;
        /// <summary>So viele Einwohner braucht die Mutterstadt, bevor sie Siedler aussenden kann.</summary>
        public const int FoundMinPopulation = 1500;
        /// <summary>Mindestabstand (Weltzellen, Chebyshev) zwischen zwei Städten derselben Region.</summary>
        public const int MinCellSpacing = 2;

        // ------------------------------------------------------------ Aggregierte Stadt (LOD „fern“)
        /// <summary>Höchstes Wachstum je Tag (Anteil der Bevölkerung) bei sehr hoher bzw. Schrumpfen bei sehr niedriger Zufriedenheit.</summary>
        public const double MaxGrowthPerDay = 0.004;
        /// <summary>Zufriedenheit, bei der die Bevölkerung stabil bleibt, und die Spanne bis zum vollen Wachstum.</summary>
        public const float NeutralSatisfaction = 0.55f, SatisfactionSpan = 0.2f;
        /// <summary>Zufriedenheit nähert sich täglich um diesen Anteil ihrem Zielwert.</summary>
        public const float SatisfactionRate = 0.05f;
        /// <summary>Abzug am Zielwert der Zufriedenheit, solange die Stadtkasse im Minus ist (Dienste werden gekürzt).</summary>
        public const float DebtSatisfactionPenalty = 0.08f;
        /// <summary>Abzug je fehlendem Anteil Grundversorgung (Lebensmittel/Konsum aus eigener Produktion, Lager oder Lieferung).</summary>
        public const float ShortageSatisfactionPenalty = 0.05f;
        /// <summary>Höchstlager je Kategorie in Tagen Bedarf; darüber geht der Überschuss an den Weltmarkt.</summary>
        public const double MaxStockDays = 30.0;
        /// <summary>Weltmarkt: Ausfuhr zu diesem Anteil des Weltpreises, Einfuhr zu diesem Aufschlag (Fracht, Händler).</summary>
        public const double ExportPriceShare = 0.85, ImportPriceFactor = 1.1;
        /// <summary>Anteil des Außenhandelssaldos, der als Steuer in der Stadtkasse ankommt (Gewerbesteuer der Händler).</summary>
        public const double TradeTaxShare = 0.04;
        /// <summary>Kasse einer neu gegründeten Stadt (Anschub; der Rest der Gründungskosten sind Land und Erschließung).</summary>
        public const double NewCityFunds = 100000.0;

        // ------------------------------------------------------------ Interne Lieferungen
        /// <summary>Bahn statt Lkw (beide Städte mit Güterbahnhof): Kosten- und Zeitfaktor (wie 4,5 % statt 8 % Fracht in G5).</summary>
        public const double RailCostFactor = 0.56, RailSpeedFactor = 1.4;
        /// <summary>Vorlauf zum Hafen außerhalb, wenn eine Küstenstadt keinen eigenen Hafen hat (km).</summary>
        public const double FeederKmWithoutHarbor = 60.0;
        /// <summary>Mindeststrecke zwischen zwei Städten (auch in derselben Weltzelle), km.</summary>
        public const double MinDistanceKm = 40.0;

        // ------------------------------------------------------------ Land gründen
        public const int MinCitiesForNation = 3;
        /// <summary>So viele Städte brauchen ein Rathaus (die Hauptstadt immer).</summary>
        public const int MinTownHallsForNation = 2;
        /// <summary>Einwohner aller Städte zusammen (detailliert + aggregiert).</summary>
        public const int MinPopulationForNation = 6000;
        /// <summary>Landesabgabe: Anteil der täglichen Steuereinnahmen jeder Stadt an die Landeskasse.</summary>
        public const double NationTaxShare = 0.10;
        /// <summary>Umland je Stadt-Region in der Länder-Volkswirtschaft des Spielers (wie der Stadtstaat).</summary>
        public const double UmlandPerRegion = 5000.0;

        // ------------------------------------------------------------ Reaktion der NPC-Länder
        /// <summary>Beziehung nach der Landesgründung: Höflichkeit, Abzug für Nachbarn und Protektionismus.</summary>
        public const float FoundingCourtesy = 3f, FoundingNeighborConcern = 6f, FoundingProtectionismConcern = 4f;
        /// <summary>Anerkennung: Neigung = Beziehung + Offenheit × (1 − Protektionismus) − Nachbar-Abzug; ab 0 erkennt das Land an.</summary>
        public const float RecognitionOpenness = 15f, RecognitionNeighborPenalty = 10f;
        /// <summary>Wer noch nicht anerkannt hat, prüft alle so viele Tage neu.</summary>
        public const int RecognitionReviewDays = 30;
        /// <summary>Anerkennung verbessert die Beziehung einmalig.</summary>
        public const float RecognitionBonus = 2f;
        /// <summary>Gebietsanspruch (Stadt gegründet) verärgert die Länder an dieser Region.</summary>
        public const float ClaimNeighborConcern = 2f;

        // ------------------------------------------------------------ NPC-Expansion
        /// <summary>Wöchentliche Grundchance je NPC-Land, eine freie Nachbarregion zu beanspruchen (× Persönlichkeit).</summary>
        public const double ExpansionWeeklyChance = 0.03;
        /// <summary>Nach einem Anspruch wartet ein Land so viele Tage.</summary>
        public const int ExpansionCooldownDays = 120;
        /// <summary>Die NPC-Länder nehmen höchstens so viel des anfangs freien Landes (der Rest bleibt für den Spieler).</summary>
        public const double ExpansionMaxShare = 0.45;
        /// <summary>Anspruch direkt an einer Spielerregion: Beziehung zum Spieler sinkt („Grenzstreit“), zu anderen Nachbarn etwas.</summary>
        public const float ExpansionPlayerConcern = 6f, ExpansionNeighborConcern = 2f;
        /// <summary>Utility: Abschlag für Regionen am Spielergebiet (vorsichtige Länder meiden Streit).</summary>
        public const float ExpansionAvoidPlayer = 0.4f;

        /// <summary>Höchstens so viele Reichs-Meldungen werden behalten.</summary>
        public const int NewsCapacity = 40;
        /// <summary>Command-Log (Replays): die letzten so vielen.</summary>
        public const int LogCapacity = 100;
    }
}
