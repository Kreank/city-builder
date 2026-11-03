namespace CityBuilder.Simulation.Goods
{
    /// <summary>
    /// Alle Stellschrauben der Warenketten an einem Ort (Platzhalter fürs Balancing).
    /// Geld in €, Mengen in Einheiten, Zeit in Spieltagen bzw. Ticks (1 Tick = 6 Spielminuten).
    /// </summary>
    public static class GoodsRules
    {
        // ------------------------------------------------------------ Takt
        /// <summary>Das Warensystem läuft alle 10 Ticks (= 1 Spielstunde): Produktion, Bestellungen, Lieferungen.</summary>
        public const int TickInterval = 10;
        public const int StepsPerDay = 24;

        // ------------------------------------------------------------ Lager
        /// <summary>Eingangslager fasst so viele Tage Verbrauch (bei voller Belegschaft).</summary>
        public const float InputStorageDays = 3f;
        /// <summary>Ausgangslager fasst so viele Tage Produktion.</summary>
        public const float OutputStorageDays = 3f;
        /// <summary>Nachbestellen, wenn Eingang + unterwegs unter diesen Anteil der Kapazität fällt.</summary>
        public const float ReorderLevel = 0.5f;
        /// <summary>Höchstens so viele Ladungen je Gut und Firma und Stunde.</summary>
        public const int MaxLoadsPerOrder = 3;
        /// <summary>Kleinste sinnvolle Liefermenge (Einheiten).</summary>
        public const float MinShipment = 0.5f;
        /// <summary>Neue Firmen starten mit diesem Anteil gefüllter Eingangslager/Ladenregale (Erstausstattung).</summary>
        public const float InitialInputFill = 0.5f;

        // ------------------------------------------------------------ Markt
        /// <summary>Zielbestand = so viele Tage Bedarf (Ausgangs- + Eingangslager aller Firmen der Stadt).</summary>
        public const float TargetStockDays = 3f;
        /// <summary>Preisreaktion auf Knappheit: Zielfaktor = 1 + Empfindlichkeit × (1 − Bestand/Ziel).</summary>
        public const float PriceSensitivity = 0.6f;
        public const float MinPriceFactor = 0.5f;
        public const float MaxPriceFactor = 1.8f;
        /// <summary>Trägheit: Anteil, um den der lokale Preis je Tag zum Zielpreis läuft.</summary>
        public const float PriceInertia = 0.25f;
        /// <summary>Weltmarkt schwankt um ± diesen Anteil um den Grundpreis (deterministisches Rauschen).</summary>
        public const float WorldVolatility = 0.10f;
        /// <summary>Periode des Weltmarkt-Rauschens in Tagen.</summary>
        public const int WorldNoisePeriodDays = 15;
        /// <summary>
        /// Paritätsband: Der Weltmarkt begrenzt den lokalen Preis (über Import/Export immer erreichbar) auf
        /// Welt × (1 − Untergrenze) … Welt × (1 + Obergrenze) – Export- bzw. Importparität inkl. Fracht und Spielraum.
        /// </summary>
        public const float ParityBelow = 0.1f, ParityAbove = 0.07f;
        /// <summary>Glättung der Tageswerte (Bedarf, Produktion …): Gewicht des neuen Tages.</summary>
        public const float EmaWeight = 0.3f;

        // ------------------------------------------------------------ Handel/Transport
        /// <summary>Lkw-Ladung: Volumen in m³ und Gewicht in t.</summary>
        public const float TruckVolume = 25f;
        public const float TruckWeight = 20f;
        /// <summary>Transportkosten = Zeit × Kostensatz: fester Anteil je Ladung + € je Verkehrssekunde.</summary>
        public const double TruckFixedCost = 12.0;
        public const double TruckCostPerSecond = 0.12;
        /// <summary>Schätzung der Fahrzeit (Verkehrssekunden) aus der Luftlinie: Umweg 1,3, Ø 11 m/s, +20 s.</summary>
        public const float DetourFactor = 1.3f;
        public const float AverageTruckSpeed = 11f;
        /// <summary>Fracht außerhalb der Karte (Weltmarkt-Strecke) als Anteil des Warenwerts.</summary>
        public const double WorldFreightShare = 0.08;
        /// <summary>Ohne Außenanschluss: Umlandversorgung (Platzhalter ohne Fahrzeug), teurere Fracht und angenommene Strecke.</summary>
        public const double HinterlandFreightShare = 0.15;
        public const float HinterlandDistance = 1500f;
        /// <summary>Export, wenn das Ausgangslager mindestens so voll ist …</summary>
        public const float ExportFill = 0.5f;
        /// <summary>… und der Welt-Nettoerlös mindestens so viel vom lokalen Preis bringt – ab <see cref="DumpFill"/> immer.</summary>
        public const float ExportMinPriceRatio = 0.9f;
        public const float DumpFill = 0.9f;
        /// <summary>Exportiert wird bis herunter auf diesen Füllstand.</summary>
        public const float ExportKeepFill = 0.3f;

        // ------------------------------------------------------------ Lieferverkehr
        /// <summary>
        /// Anteil der Lieferungen, die als echter Lkw fahren (× <c>TrafficSystem.Demand</c>), Rest = Platzhalter. Seit dem
        /// Zeitmodell (45 s Verkehrszeit je Tick, wenig gleichzeitige Fahrzeuge) 0,8 statt 0,35.
        /// </summary>
        public const float TruckShare = 0.8f;
        /// <summary>Höchstens dieser Anteil von <c>TrafficSystem.MaxVehicles</c> sind Lieferwagen (Bürger: 70 %).</summary>
        public const float MaxTruckShare = 0.15f;
        /// <summary>
        /// Platzhalter-Lieferungen (Zeitmodell): Fahrzeit in Verkehrszeit wie ein Lkw – Umweg 1,3 × Luftlinie bei
        /// <see cref="Core.TimeModel.TruckPlaceholderSpeed"/> –, umgerechnet in Ticks (30 s Verkehrszeit je Tick).
        /// </summary>
        public static int PlaceholderTicks(float distance, bool outside) =>
            Core.TimeModel.TicksFor(distance * 1.3f / Core.TimeModel.TruckPlaceholderSpeed + (outside ? Core.TimeModel.OutsideTrafficSeconds : 0f));
        /// <summary>Sicherheitsnetz: Lkw-Fahrt ohne Rückmeldung gilt nach 3 Tagen als angekommen.</summary>
        public const long TruckTimeoutTicks = 3L * Core.TimeModel.TicksPerDay;

        // ------------------------------------------------------------ Firmen
        /// <summary>Startkapital je Arbeitsplatz.</summary>
        public const double StartCashPerJob = 400.0;
        /// <summary>Lohn je Beschäftigtem und Tag, wenn das Bürgersystem keine Löhne liefert.</summary>
        public const double DefaultWage = 85.0;
        /// <summary>Firmen dürfen ihr Konto bis −(so viele Tage Lohn) überziehen, danach kaufen sie nichts mehr.</summary>
        public const double CreditDaysOfWages = 10.0;
        /// <summary>Pleite: so viele Tage am Stück Verlust (geglättet) und Konto unter dem Kreditrahmen → Gebäude wird verlassen.</summary>
        public const int BankruptAfterLossDays = 21;
        /// <summary>Erst ab diesem Alter (Tage) zählt eine Firma für die Nachfrage der Zone.</summary>
        public const int EstablishedAfterDays = 3;
        /// <summary>Produktivität ohne Strom/Wasser (wenn die Art in der Stadt schon verlangt wird).</summary>
        public const float UnsuppliedProductivity = 0.2f;
        /// <summary>Fehlende Fachkräfte senken die Produktivität höchstens um diesen Anteil.</summary>
        public const float SkillPenalty = 0.5f;
        /// <summary>Einzelhandel: Aufschlag auf den lokalen Großhandelspreis.</summary>
        public const float RetailMarkup = 1.45f;
        /// <summary>Büro/Dienstleistung: Erlös je Beschäftigtem und Tag bei normaler Nachfrage.</summary>
        public const double OfficeRevenuePerWorker = 185.0;

        // ------------------------------------------------------------ Vorkommen (Bindung an world.Resources)
        /// <summary>
        /// Entnahme aus der Bodenkarte je geförderter Einheit (Einheit der Karte, <c>ResourceKind.Unit</c>).
        /// Erz/Kohle in t = Gewicht der Ware, Erdöl/Sand in m³ = Volumen der Ware (1:1 physisch). Holz und Boden
        /// im Spielmaßstab: die Karte entnimmt nur im 3×3-Zellen-Fenster (~48 × 48 m) um den Betrieb, ein Forst bzw.
        /// Hof bewirtschaftet aber viel mehr Fläche – physisch (1,5 m³ Holz je Einheit) wäre der Wald in Stunden weg.
        /// </summary>
        public const float WoodPerUnit = 0.01f;       // m³ Waldbestand je Einheit Holz
        public const float FertilityPerUnit = 0.001f; // Bodenpunkte je Einheit Ernte (Getreide/Vieh)
        /// <summary>Ernte: Jahresmittel des Klimas wird auf diesen Bezug normiert (gemäßigt ≈ 1), begrenzt 0,4–1,2.</summary>
        public const float HarvestReference = 0.5f;
        /// <summary>
        /// Grundertrag unabhängig von der Jahreszeit (Vieh, Lager, Gewächshaus): Ernte = Grund + (1 − Grund) × Jahresgang.
        /// Ohne ihn stünde jeder Hof den ganzen Winter still und ginge über die 21-Tage-Verlustregel pleite.
        /// </summary>
        public const float HarvestFloor = 0.6f;

        // ------------------------------------------------------------ Haushalte
        /// <summary>Ein Einkauf deckt so viele Tage Verbrauch (das Bedürfnis „Einkauf“ erreicht 1 nach ~3 Tagen).</summary>
        public const float DaysPerShopping = 3f;
        /// <summary>Anteil des Einkommens, der für Konsum ausgegeben wird.</summary>
        public const float SpendShare = 0.5f;
        /// <summary>Komfort ab diesem Haushaltseinkommen je Tag und dieser Zufriedenheit.</summary>
        public const float ComfortIncome = 110f;
        public const float ComfortSatisfaction = 0.45f;
        /// <summary>Luxus ab diesem Haushaltseinkommen je Tag und dieser Zufriedenheit.</summary>
        public const float LuxuryIncome = 200f;
        public const float LuxurySatisfaction = 0.55f;
        /// <summary>Gewichte der Stufen für die Erfüllung eines Einkaufs.</summary>
        public const float BasicWeight = 0.6f, ComfortWeight = 0.25f, LuxuryWeight = 0.15f;

        // ------------------------------------------------------------ Nachfrage der Zonen (Hook CitySystem)
        /// <summary>Höchster Zuschlag/Abzug der Zonen-Nachfrage aus dem Gewinn der Firmen.</summary>
        public const float ProfitDemandRange = 25f;
        /// <summary>Gewinnmarge, bei der die Wirkung ~76 % erreicht (tanh).</summary>
        public const float ProfitDemandMargin = 0.15f;
        /// <summary>Zuschlag Industrie, wenn die Stadt viel importiert (Anteil Import am Bedarf × Wert).</summary>
        public const float ImportDemandBonus = 12f;
        /// <summary>Zuschlag Einzelhandel, wenn Haushalte nichts bekommen (Anteil unerfüllter Einkäufe × Wert).</summary>
        public const float UnmetShoppingBonus = 20f;
    }
}
