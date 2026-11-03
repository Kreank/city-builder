namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Handelsaktion eines Landes (Spieler oder NPC – dieselben Klassen). Zustand ändert sich nur über Commands
    /// (Multiplayer/Replays): <see cref="TradeSystem.Submit"/> reiht ein (ausgeführt im nächsten Tick in Eingangsreihenfolge),
    /// <see cref="TradeSystem.Execute"/> führt sofort aus. Ausgeführte Commands stehen im <see cref="TradeSystem.CommandLog"/>.
    /// </summary>
    public abstract class TradeCommand
    {
        /// <summary>Handelndes Land (Country-Id).</summary>
        public int Party { get; }
        protected TradeCommand(int party) { Party = party; }
        internal abstract TradeResult Apply(TradeSystem trade);
        public abstract string Describe();
    }

    /// <summary>Angebot (verkaufen) bzw. Nachfrage (kaufen) oder Handelsabkommen an ein Land (−1 = an alle) stellen.</summary>
    public sealed class ProposeContractCommand : TradeCommand
    {
        public int To { get; }
        public TradeSide Side { get; }
        public ContractTerms Terms { get; }
        public int ValidDays { get; }

        public ProposeContractCommand(int party, int to, TradeSide side, ContractTerms terms, int validDays = TradeRules.OfferValidDays) : base(party)
        {
            To = to; Side = side; Terms = terms?.Clone(); ValidDays = validDays;
        }

        internal override TradeResult Apply(TradeSystem t) => t.DoPropose(Party, To, Side, Terms, ValidDays);
        public override string Describe() => $"{Party} → {(To < 0 ? "alle" : To.ToString())}: {(Side == TradeSide.Sell ? "verkauft" : "kauft")} {Terms?.Describe()}";
    }

    /// <summary>Offenes Angebot annehmen (macht daraus einen Vertrag).</summary>
    public sealed class AcceptOfferCommand : TradeCommand
    {
        public int OfferId { get; }
        public AcceptOfferCommand(int party, int offerId) : base(party) { OfferId = offerId; }
        internal override TradeResult Apply(TradeSystem t) => t.DoAccept(Party, OfferId, null);
        public override string Describe() => $"{Party} nimmt Angebot #{OfferId} an";
    }

    /// <summary>Offenes Angebot ablehnen (mit Grund).</summary>
    public sealed class RejectOfferCommand : TradeCommand
    {
        public int OfferId { get; }
        public string Reason { get; }
        public RejectOfferCommand(int party, int offerId, string reason = null) : base(party) { OfferId = offerId; Reason = reason; }
        internal override TradeResult Apply(TradeSystem t) => t.DoReject(Party, OfferId, Reason ?? "abgelehnt");
        public override string Describe() => $"{Party} lehnt Angebot #{OfferId} ab";
    }

    /// <summary>Eigenes offenes Angebot zurückziehen.</summary>
    public sealed class WithdrawOfferCommand : TradeCommand
    {
        public int OfferId { get; }
        public WithdrawOfferCommand(int party, int offerId) : base(party) { OfferId = offerId; }
        internal override TradeResult Apply(TradeSystem t) => t.DoWithdraw(Party, OfferId);
        public override string Describe() => $"{Party} zieht Angebot #{OfferId} zurück";
    }

    /// <summary>Vertrag kündigen (sofort; unterwegs befindliche Ware kommt noch an). Kostet eine Gebühr und Vertrauen.</summary>
    public sealed class CancelContractCommand : TradeCommand
    {
        public int ContractId { get; }
        public CancelContractCommand(int party, int contractId) : base(party) { ContractId = contractId; }
        internal override TradeResult Apply(TradeSystem t) => t.DoCancel(Party, ContractId);
        public override string Describe() => $"{Party} kündigt Vertrag #{ContractId}";
    }

    /// <summary>Zollsatz setzen: Grundzoll (<see cref="Target"/> = −1) oder Satz gegen ein Lieferland (NaN = wieder Grundzoll).</summary>
    public sealed class SetTariffCommand : TradeCommand
    {
        public int Target { get; }
        public double Rate { get; }
        public SetTariffCommand(int party, int target, double rate) : base(party) { Target = target; Rate = rate; }
        internal override TradeResult Apply(TradeSystem t) => t.DoSetTariff(Party, Target, Rate);
        public override string Describe() => $"{Party}: Zoll {(Target < 0 ? "allgemein" : "gegen " + Target)} = {(double.IsNaN(Rate) ? "Grundzoll" : $"{Rate * 100:0} %")}";
    }
}
