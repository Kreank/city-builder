namespace CityBuilder.Simulation.Goods
{
    /// <summary>
    /// Anschluss des Warensystems an den Handel zwischen Ländern (Etappe 8, umgesetzt von
    /// <c>Trade.CityTradeDepot</c>): Vertragsware liegt im <b>Grenzlager</b> am Außenanschluss und wird von Firmen wie
    /// ein Import bezogen (Lkw ab Außenanschluss), Exportverträge werden von Firmen beliefert (Lkw zum Außenanschluss).
    /// Zölle des Spielers verteuern Weltmarkt-Importe. Ohne Gateway (null) verhält sich G5 exakt wie vorher.
    /// </summary>
    public interface ITradeGateway
    {
        /// <summary>Vertragsware im Grenzlager (Einheiten), die Firmen beziehen können.</summary>
        float ImportAvailable(GoodType g);

        /// <summary>Preis je Einheit Vertragsware für Firmen (Vertragspreis + Zoll gegen das Lieferland).</summary>
        double ImportUnitPrice(GoodType g);

        /// <summary>Entnimmt Vertragsware aus dem Grenzlager und bucht den Erlös in der Stadtkasse; liefert den Preis, den die Firma zahlt.</summary>
        double TakeImport(GoodType g, float amount);

        /// <summary>Wie viele Einheiten die Exportverträge noch brauchen (offene Menge der nächsten Lieferwoche abzgl. Grenzlager und unterwegs).</summary>
        float ExportWanted(GoodType g);

        /// <summary>Vertragspreis je Einheit, den Firmen für Exportware bekommen.</summary>
        double ExportUnitPrice(GoodType g);

        /// <summary>Firma schickt Exportware los (Stadtkasse bezahlt die Firma); liefert den gezahlten Betrag.</summary>
        double ExportDispatched(GoodType g, float amount);

        /// <summary>Exportware ist am Außenanschluss angekommen → Grenzlager.</summary>
        void ExportArrived(GoodType g, float amount);

        /// <summary>Zollsatz des Spielers auf Weltmarkt-Importe dieses Gutes (0..1).</summary>
        double SpotTariffRate(GoodType g);

        /// <summary>Bucht gezahlten Zoll (Weltmarkt-Import) in der Stadtkasse.</summary>
        void BookTariff(double amount);
    }
}
