namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Alle Stellschrauben des Handels zwischen Ländern (Etappe 8) an einem Ort – Platzhalter fürs Balancing.
    /// Geld in €, Mengen in Wareneinheiten des G5-Katalogs (Länder rechnen intern in Kategorie-Einheiten,
    /// s. <see cref="TradeGoods.CategoryUnitsPerGood"/>), Zeit in Spieltagen.
    /// </summary>
    public static class TradeRules
    {
        // ------------------------------------------------------------ Preise
        /// <summary>Länderpreis je Gut streut um ± diesen Anteil um den Kategorie-Index (stabiler Hash je Land und Gut).</summary>
        public const double GoodPriceSpread = 0.06;
        /// <summary>Gewicht der Produktion im Weltpreis neben dem Handelsvolumen (sonst wäre er ohne Handel undefiniert).</summary>
        public const double ProductionWeight = 0.02;
        /// <summary>Glättung des Handelsvolumens je Land und Kategorie (Gewicht des neuen Tages).</summary>
        public const double TradeVolumeEma = 0.1;
        /// <summary>Die Stadt sieht den Weltpreis nur in diesem Band um den G5-Grundpreis (Schutz des G5-Balancings).</summary>
        public const float CityWorldPriceMin = 0.5f, CityWorldPriceMax = 2.0f;

        // ------------------------------------------------------------ Transport
        /// <summary>Umwegfaktor gegenüber dem Großkreis: Landweg (Straße/Bahn) bzw. Seeweg.</summary>
        public const double LandDetour = 1.35, SeaDetour = 1.25;
        /// <summary>€ je Frachttonne und km: Lkw/Bahn bzw. Schiff (Schiff ~7× billiger je km).</summary>
        public const double LandCostPerTonKm = 0.03, SeaCostPerTonKm = 0.004;
        /// <summary>Umschlag je Hafen (€ je Frachttonne).</summary>
        public const double PortHandlingPerTon = 4.0;
        /// <summary>Tagesleistung: Landweg bzw. Seeweg (km je Tag), dazu 1 Tag Abfertigung und 1 Tag je Hafen.</summary>
        public const double LandKmPerDay = 600.0, SeaKmPerDay = 500.0;
        /// <summary>Frachttonnen je Einheit = max(Gewicht, Volumen × Faktor) – sperrige Güter kosten nach Raum.</summary>
        public const float VolumeTonsPerCubicMeter = 0.4f;

        // ------------------------------------------------------------ Verträge
        /// <summary>Lieferrhythmus: eine Lieferung je so viele Tage.</summary>
        public const int DaysPerWeek = 7;
        /// <summary>Offene Angebote verfallen nach so vielen Tagen.</summary>
        public const int OfferValidDays = 7;
        public const int MinWeeks = 1, MaxWeeks = 52;
        /// <summary>Standard-Strafe: Anteil des Werts der fehlenden Menge.</summary>
        public const double DefaultPenaltyShare = 0.3;
        /// <summary>Kündigung: Gebühr = Restwochen (höchstens so viele) × Wochenwert × Strafanteil × 0,5.</summary>
        public const int CancelFeeMaxWeeks = 4;
        /// <summary>Handelsabkommen (Zollfreiheit) laufen standardmäßig so viele Wochen.</summary>
        public const int AgreementWeeks = 26;

        // ------------------------------------------------------------ Beziehungen & Vertrauen
        public const float RelationMin = -100f, RelationMax = 100f;
        /// <summary>Vertrauen eines Landes in ein anderes (0..1), Start und Erholungsziel.</summary>
        public const float TrustStart = 0.6f;
        /// <summary>Pünktliche, volle Lieferung: Vertrauen + / Beziehung +.</summary>
        public const float TrustOnTime = 0.01f, RelationOnTime = 0.4f;
        /// <summary>Vertragsbruch (Fehlmenge): Vertrauen − (Grund + Anteil × Fehlquote), Beziehung − (Grund + Anteil × Fehlquote).</summary>
        public const float TrustBreachBase = 0.05f, TrustBreachShare = 0.3f;
        public const float RelationBreachBase = 4f, RelationBreachShare = 12f;
        /// <summary>Kündigung: Vertrauen und Beziehung sinken.</summary>
        public const float TrustCancel = 0.1f, RelationCancel = 6f;
        /// <summary>Abgelehnte Angebote verärgern kaum, beendete Verträge freuen.</summary>
        public const float RelationCompleted = 2f;
        /// <summary>Wöchentliche Drift der Beziehung zum Grundwert und des Vertrauens zum Startwert.</summary>
        public const float RelationDrift = 0.02f, TrustDrift = 0.01f;
        /// <summary>Zölle über diesem Satz verärgern das betroffene Land (je Woche × Überschuss × Faktor).</summary>
        public const float TariffAnnoyanceFrom = 0.1f, TariffAnnoyance = 10f;
        /// <summary>Bestehendes Handelsabkommen: Beziehung + je Woche.</summary>
        public const float RelationAgreementWeekly = 0.5f;
        /// <summary>Unter dieser Beziehung handelt ein Land gar nicht.</summary>
        public const float RelationEmbargo = -50f;
        /// <summary>Höchstzoll.</summary>
        public const double MaxTariff = 0.5;

        // ------------------------------------------------------------ NPC (Utility AI)
        /// <summary>Zielreichweite des Lagers in Tagen (wie <c>NationEconomy.TargetStockDays</c>) und „Überschuss ab“.</summary>
        public const double TargetCoverDays = 20.0, SurplusCoverDays = 35.0;
        /// <summary>Höchstens so viele offene eigene Angebote je Land, an den Spieler höchstens eins.</summary>
        public const int MaxOpenOffers = 3;
        /// <summary>Höchstens so viele offene NPC-Angebote an den Spieler insgesamt.</summary>
        public const int MaxOffersToPlayer = 6;
        /// <summary>Angebote je Wochenentscheidung höchstens.</summary>
        public const int OffersPerDecision = 2;
        /// <summary>Nutzwert der Option „nichts tun“ (Trägheit).</summary>
        public const float IdleScore = 0.15f;
        /// <summary>Annahme: Wahrscheinlichkeit = logistisch((Nutzen + Neigung) / Temperatur).</summary>
        public const float AcceptBias = 0.1f;
        /// <summary>Restverkauf: Vertragsware im Grenzlager über 2 Wochen Vertragsmenge geht zu diesem Anteil des Weltpreises weg.</summary>
        public const double DepotDumpPriceShare = 0.85;
    }
}
