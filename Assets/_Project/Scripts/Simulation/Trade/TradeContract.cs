using System.Collections.Generic;
using CityBuilder.Simulation.Goods;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>Seite eines Angebots aus Sicht des Anbietenden.</summary>
    public enum TradeSide : byte
    {
        /// <summary>„Verkaufe X Einheiten Gut G je Woche zu Preis P“ (Angebot).</summary>
        Sell,
        /// <summary>„Kaufe …“ (Nachfrage).</summary>
        Buy,
    }

    public enum ContractKind : byte
    {
        /// <summary>Liefervertrag: Gut, Menge je Woche, Laufzeit, Preis, Strafe.</summary>
        Supply,
        /// <summary>Handelsabkommen: gegenseitige Zollfreiheit für die Laufzeit (kein Gut).</summary>
        Agreement,
    }

    public enum PriceMode : byte
    {
        /// <summary>Fester Preis je Einheit.</summary>
        Fixed,
        /// <summary>An den Weltpreis gekoppelt: Weltpreis am Liefertag × Faktor.</summary>
        WorldIndexed,
    }

    public enum OfferStatus : byte { Open, Accepted, Rejected, Withdrawn, Expired }

    public enum ContractStatus : byte { Active, Completed, Cancelled }

    /// <summary>Vertragsbedingungen (gleich für Spieler und NPC).</summary>
    public sealed class ContractTerms
    {
        public ContractKind Kind = ContractKind.Supply;
        /// <summary>Gut (null bei Handelsabkommen).</summary>
        public GoodType Good;
        /// <summary>Einheiten je Lieferwoche (Wareneinheiten des G5-Katalogs).</summary>
        public double UnitsPerWeek;
        /// <summary>Laufzeit in Wochen (= Anzahl Lieferungen).</summary>
        public int Weeks = 4;
        public PriceMode PriceMode = PriceMode.Fixed;
        /// <summary>Fest: € je Einheit; an den Weltpreis gekoppelt: Faktor auf den Weltpreis (1 = Weltpreis).</summary>
        public double Price;
        /// <summary>Strafe bei Nichtlieferung als Anteil des Werts der Fehlmenge (0..1).</summary>
        public double PenaltyShare = TradeRules.DefaultPenaltyShare;

        public ContractTerms Clone() => (ContractTerms)MemberwiseClone();

        public string Describe()
        {
            if (Kind == ContractKind.Agreement) return $"Handelsabkommen (Zollfreiheit) · {Weeks} Wochen";
            string price = PriceMode == PriceMode.Fixed ? $"{Price:0.0} €/E fest" : $"Weltpreis × {Price:0.00}";
            return $"{UnitsPerWeek:0.#} {Good?.Name}/Woche · {Weeks} Wochen · {price} · Strafe {PenaltyShare * 100:0} %";
        }
    }

    /// <summary>Angebot bzw. Nachfrage eines Landes an ein anderes (oder an alle: <see cref="To"/> = −1).</summary>
    public sealed class TradeOffer
    {
        public int Id { get; internal set; }
        public int From { get; internal set; }
        /// <summary>Empfänger-Land, −1 = an alle (erstes/bestes annehmendes Land).</summary>
        public int To { get; internal set; }
        public TradeSide Side { get; internal set; }
        public ContractTerms Terms { get; internal set; }
        public int CreatedDay { get; internal set; }
        public int ExpiresDay { get; internal set; }
        /// <summary>Erster Handelstag, an dem der Empfänger antworten kann (eine Nacht Bedenkzeit).</summary>
        public int AnswerDay { get; internal set; }
        public OfferStatus Status { get; internal set; }
        /// <summary>Begründung der Annahme/Ablehnung (Utility AI) bzw. des Rückzugs.</summary>
        public string Reason { get; internal set; }
        /// <summary>Wer angenommen/abgelehnt hat.</summary>
        public int RespondedBy { get; internal set; } = -1;
        public int ContractId { get; internal set; }
        // --- Handel 2: Verhandlung
        /// <summary>Gegenangebot auf dieses Angebot (0 = Erstangebot).</summary>
        public int CounterOf { get; internal set; }
        /// <summary>Verhandlungsrunde (0 = Erstangebot, 1 = erstes Gegenangebot …).</summary>
        public int Round { get; internal set; }
        public bool IsCounter => CounterOf > 0;
        // --- Ende Handel 2

        public bool IsOpen => Status == OfferStatus.Open;
        public override string ToString() => $"#{Id} {From}→{To} {Side} {Terms.Describe()} [{Status}]";
    }

    /// <summary>Eine Lieferung eines Vertrags: unterwegs von Verkäufer zu Käufer (Lieferzeit aus dem Handelsweg).</summary>
    public sealed class TradeDelivery
    {
        public int Id { get; internal set; }
        public int ContractId { get; internal set; }
        public int Week { get; internal set; }
        public double Units { get; internal set; }
        /// <summary>Preis je Einheit am Versandtag (fest oder Weltpreis × Faktor).</summary>
        public double UnitPrice { get; internal set; }
        public int DispatchDay { get; internal set; }
        public int ArriveDay { get; internal set; }
        public bool Arrived { get; internal set; }
        public double Value => Units * UnitPrice;
    }

    /// <summary>
    /// Laufender bzw. beendeter Vertrag. Verkäufer liefert wöchentlich ab <see cref="StartDay"/> (<see cref="NextDispatchDay"/>),
    /// Ware ist <see cref="Route"/>.LeadDays unterwegs; bezahlt wird bei Ankunft. Fehlmenge → Strafe und Vertrauensverlust.
    /// </summary>
    public sealed class TradeContract
    {
        public int Id { get; internal set; }
        public ContractTerms Terms { get; internal set; }
        public int Seller { get; internal set; }
        public int Buyer { get; internal set; }
        public int OfferId { get; internal set; }
        public int StartDay { get; internal set; }
        public int NextDispatchDay { get; internal set; }
        public int WeeksDispatched { get; internal set; }
        public ContractStatus Status { get; internal set; }
        public TradeRoute Route { get; internal set; }
        public double UnitsDelivered { get; internal set; }
        public double UnitsShort { get; internal set; }
        public double ValueDelivered { get; internal set; }
        /// <summary>Strafen, die der Verkäufer gezahlt hat (Fehlmengen) bzw. Kündigungsgebühr.</summary>
        public double Penalties { get; internal set; }
        /// <summary>Wochen mit voller, pünktlicher Lieferung bzw. mit Fehlmenge.</summary>
        public int WeeksOnTime { get; internal set; }
        public int WeeksShort { get; internal set; }
        public int CancelledBy { get; internal set; } = -1;
        public int EndDay { get; internal set; } = -1;
        public readonly List<TradeDelivery> Deliveries = new List<TradeDelivery>();

        public ContractKind Kind => Terms.Kind;
        public GoodType Good => Terms.Good;
        public bool IsActive => Status == ContractStatus.Active;
        /// <summary>Alle Lieferungen verschickt?</summary>
        public bool AllDispatched => WeeksDispatched >= Terms.Weeks;
        /// <summary>Letzter Tag der Laufzeit (Handelsabkommen) bzw. geplanter letzter Versandtag.</summary>
        public int PlannedEndDay => StartDay + Terms.Weeks * TradeRules.DaysPerWeek;
        public bool Involves(int party) => Seller == party || Buyer == party;
        public int Partner(int party) => Seller == party ? Buyer : Seller;
        public override string ToString() => $"V#{Id} {Seller}→{Buyer} {Terms.Describe()} [{Status}]";
    }

    public enum TradeMemoryKind : byte
    {
        OnTime, Breach, Cancelled, Completed, Rejected, Agreement, Tariff,
        // --- Handel 2 (hinten angefügt)
        Sanction, Gift, Protest, Threat, BlocJoined, BlocLeft,
    }

    /// <summary>Erinnerung eines Landes an ein anderes (Gedächtnis der Utility AI; Vertrauen ist der verdichtete Wert).</summary>
    public readonly struct TradeMemory
    {
        public readonly TradeMemoryKind Kind;
        public readonly int Partner;
        public readonly int Day;
        public readonly float Magnitude;
        public TradeMemory(TradeMemoryKind kind, int partner, int day, float magnitude) { Kind = kind; Partner = partner; Day = day; Magnitude = magnitude; }

        public string Text(string partnerName)
        {
            switch (Kind)
            {
                case TradeMemoryKind.OnTime: return $"{partnerName} hat pünktlich geliefert";
                case TradeMemoryKind.Breach: return $"{partnerName} hat {Magnitude * 100:0} % der Menge nicht geliefert (Vertragsbruch)";
                case TradeMemoryKind.Cancelled: return $"{partnerName} hat einen Vertrag gekündigt";
                case TradeMemoryKind.Completed: return $"Vertrag mit {partnerName} erfüllt";
                case TradeMemoryKind.Rejected: return $"{partnerName} hat unser Angebot abgelehnt";
                case TradeMemoryKind.Agreement: return $"Handelsabkommen mit {partnerName}";
                // --- Handel 2
                case TradeMemoryKind.Sanction: return $"Sanktionen zwischen uns und {partnerName}";
                case TradeMemoryKind.Gift: return $"{partnerName} hat uns {Magnitude:N0} € geschenkt";
                case TradeMemoryKind.Protest: return $"{partnerName} hat gegen unsere Zölle protestiert";
                case TradeMemoryKind.Threat: return $"{partnerName} hat uns gedroht";
                case TradeMemoryKind.BlocJoined: return $"{partnerName} ist unserem Bündnis beigetreten";
                case TradeMemoryKind.BlocLeft: return $"{partnerName} hat unser Bündnis verlassen";
                // --- Ende Handel 2
                default: return $"{partnerName} erhebt hohe Zölle ({Magnitude * 100:0} %)";
            }
        }
    }

    public enum TradeNewsKind : byte
    {
        ContractSigned, Embargo, TariffRaised,
        // --- Handel 2 (hinten angefügt): Gegenangebot an uns, Bündnis (Beitritt/Einladung/Austritt), Sanktion
        CounterOffer, BlocJoined, BlocInvitation, BlocLeft, Sanction,
    }

    /// <summary>Nachricht des Handels an den Spieler (Medien, HUD).</summary>
    public readonly struct TradeNews
    {
        public readonly TradeNewsKind Kind;
        public readonly int Country;
        public readonly string CountryName;
        /// <summary>Vertrag: Laufzeit in Wochen; Embargo: Beziehung; Zoll: neuer Satz (0..1).</summary>
        public readonly double Value;
        /// <summary>Vertrag: Gut bzw. „ein Handelsabkommen“.</summary>
        public readonly string Detail;
        public readonly int Day;
        public TradeNews(TradeNewsKind kind, int country, string name, double value, string detail, int day)
        { Kind = kind; Country = country; CountryName = name; Value = value; Detail = detail; Day = day; }
    }

    /// <summary>Ergebnis eines Handels-Commands.</summary>
    public readonly struct TradeResult
    {
        public readonly bool Ok;
        public readonly string Message;
        /// <summary>Id des angelegten Angebots/Vertrags (0 = keins).</summary>
        public readonly int Id;
        public TradeResult(bool ok, string message, int id = 0) { Ok = ok; Message = message; Id = id; }
        public static TradeResult Fail(string message) => new TradeResult(false, message);
        public override string ToString() => (Ok ? "ok: " : "Fehler: ") + Message;
    }
}
