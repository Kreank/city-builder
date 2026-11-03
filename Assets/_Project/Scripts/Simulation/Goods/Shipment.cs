using System.Numerics;

namespace CityBuilder.Simulation.Goods
{
    public enum ShipmentKind : byte
    {
        /// <summary>Zwischen zwei Lagern in der Stadt.</summary>
        Local,
        /// <summary>Vom Außenanschluss (bzw. Umland) in ein Lager.</summary>
        Import,
        /// <summary>Aus einem Lager zum Außenanschluss (bzw. Umland).</summary>
        Export,
    }

    /// <summary>
    /// Eine Lieferung (eine Lkw-Ladung): Gut, Menge, Start und Ziel. Fährt entweder als echter Lkw im
    /// <c>TrafficSystem</c> (<see cref="IsTruck"/>, <see cref="TripId"/> &lt; 0) oder als Platzhalter mit Fahrzeit.
    /// Ankunft bucht ins Ziellager.
    /// </summary>
    public sealed class Shipment
    {
        public int Id { get; internal set; }
        public ShipmentKind Kind { get; internal set; }
        public GoodType Good { get; internal set; }
        public float Amount { get; internal set; }
        /// <summary>Absender-Gebäude (0 = außerhalb).</summary>
        public int FromBuildingId { get; internal set; }
        /// <summary>Empfänger-Gebäude (0 = außerhalb).</summary>
        public int ToBuildingId { get; internal set; }
        /// <summary>Außenanschluss (Knoten-Id) bei Import/Export, 0 = Umland ohne Anschluss.</summary>
        public int ConnectionNodeId { get; internal set; }
        public Vector2 From { get; internal set; }
        public Vector2 To { get; internal set; }
        /// <summary>Warenwert (gezahlt an Verkäufer bzw. Weltmarkt).</summary>
        public double Value { get; internal set; }
        /// <summary>Geschätzte Transportkosten beim Abschicken (bei Lkw nach Ankunft mit echter Fahrzeit nachberechnet).</summary>
        public double TransportCost { get; internal set; }
        /// <summary>Wer die Fracht zahlt (Gebäude-Id der Firma).</summary>
        public int PayerBuildingId { get; internal set; }
        public long DispatchTick { get; internal set; }
        /// <summary>Geplante Ankunft (Platzhalter) bzw. Sicherheits-Zeitüberschreitung (Lkw); <c>long.MaxValue</c> = Lkw wartet auf die Abfahrt.</summary>
        public long ArriveTick { get; internal set; }
        /// <summary>Fährt als sichtbarer Lkw.</summary>
        public bool IsTruck { get; internal set; }
        /// <summary>Etappe 8: Vertragsware (Import aus dem Grenzlager bzw. Export in einen Vertrag).</summary>
        public bool IsContract { get; internal set; }
        /// <summary>Fahrt-Id im Verkehr (negativ, damit sie nicht mit Bürgerfahrten kollidiert; 0 = Platzhalter).</summary>
        public int TripId { get; internal set; }
        /// <summary>Verkehrszeit beim Start des Lkw (s) – für die echten Transportkosten.</summary>
        public float StartTrafficTime { get; internal set; }
        // Warteschlange der Lkw-Abfahrt
        internal long LaunchTick;
        internal int FallbackTicks;
        internal CityBuilder.Simulation.Roads.RoadNode FromNode, ToNode;
        /// <summary>Lkw-Anteil der Transportkosten (Schätzung bzw. nach Ankunft die echte Fahrzeit).</summary>
        internal double TruckEstimate;
        internal bool Done;
        /// <summary>Angekommen (bzw. Ziel weggefallen)?</summary>
        public bool IsDone => Done;

        public override string ToString() => $"{Kind} {Amount:0.#} {Good?.Name} #{FromBuildingId}→#{ToBuildingId}";
    }

    /// <summary>Statistik des Warensystems (Gestern-Werte und laufend).</summary>
    public sealed class GoodsStats
    {
        public int Firms, Extraction, Processing, Manufacturing, Retail, Office;
        public int ActiveShipments, ActiveTrucks;
        /// <summary>Gestern: Lieferungen gesamt und davon als Lkw.</summary>
        public int ShipmentsYesterday, TrucksYesterday;
        public int DeliveredYesterday;
        /// <summary>Gestern: Lkw vorgesehen, aber ohne Weg/Platz/Fahrzeuglimit → Platzhalter; bzw. per Anteil/Limit gleich Platzhalter.</summary>
        public int TruckFailedYesterday, PlaceholderYesterday;
        /// <summary>Lieferungen, deren Ziel unterwegs verschwand (Abriss).</summary>
        public int LostShipments;
        public int Bankruptcies;
        /// <summary>Einkäufe der Haushalte gestern: Versuche, erfolgreich (Grundbedarf ≥ 50 %), je Stufe gewünscht/gekauft (Einheiten).</summary>
        public int PurchasesYesterday, SuccessfulPurchasesYesterday;
        public readonly float[] TierWantedYesterday = new float[4];
        public readonly float[] TierBoughtYesterday = new float[4];
        /// <summary>Summe Gewinne/Verluste aller Firmen gestern.</summary>
        public double TotalProfitYesterday;
    }
}
