namespace CityBuilder.Simulation.Trade
{
    // Handel 2: Commands für Verhandlung, Bündnisse, Abstimmungen und Diplomatie – gleich für Spieler und NPC-KI.

    /// <summary>
    /// Gegenangebot auf ein offenes, an dieses Land gerichtetes Angebot: lehnt das Angebot ab und schickt dem Anbieter neue
    /// Bedingungen (Seite gedreht: auf „verkaufe“ folgt „kaufe“). Höchstens <see cref="DiplomacyRules.MaxNegotiationRounds"/> Runden.
    /// </summary>
    public sealed class CounterOfferCommand : TradeCommand
    {
        public int OfferId { get; }
        public ContractTerms Terms { get; }
        public string Reason { get; }
        public CounterOfferCommand(int party, int offerId, ContractTerms terms, string reason = null) : base(party)
        {
            OfferId = offerId; Terms = terms?.Clone(); Reason = reason;
        }
        internal override TradeResult Apply(TradeSystem t) => t.DoCounter(Party, OfferId, Terms, Reason);
        public override string Describe() => $"{Party}: Gegenangebot auf #{OfferId}: {Terms?.Describe()}";
    }

    /// <summary>Bündnis gründen (Gründer = Vorsitz) und ein Partnerland einladen. Wirksam, sobald der Partner beitritt.</summary>
    public sealed class FoundBlocCommand : TradeCommand
    {
        public BlocKind Kind { get; }
        public string Name { get; }
        public int Partner { get; }
        public FoundBlocCommand(int party, BlocKind kind, string name, int partner) : base(party) { Kind = kind; Name = name; Partner = partner; }
        internal override TradeResult Apply(TradeSystem t) => t.DoFoundBloc(Party, Kind, Name, Partner);
        public override string Describe() => $"{Party} gründet {Name} ({Kind}) mit {Partner}";
    }

    /// <summary>Aufnahme in ein Bündnis beantragen (startet eine Abstimmung der Mitglieder).</summary>
    public sealed class ApplyToBlocCommand : TradeCommand
    {
        public int BlocId { get; }
        public ApplyToBlocCommand(int party, int blocId) : base(party) { BlocId = blocId; }
        internal override TradeResult Apply(TradeSystem t) => t.DoApply(Party, BlocId);
        public override string Describe() => $"{Party} beantragt Aufnahme in Bündnis #{BlocId}";
    }

    /// <summary>Mitglied lädt ein Land ein; nimmt es an, tritt es bei.</summary>
    public sealed class InviteToBlocCommand : TradeCommand
    {
        public int BlocId { get; }
        public int Target { get; }
        public InviteToBlocCommand(int party, int blocId, int target) : base(party) { BlocId = blocId; Target = target; }
        internal override TradeResult Apply(TradeSystem t) => t.DoInvite(Party, BlocId, Target);
        public override string Describe() => $"{Party} lädt {Target} in Bündnis #{BlocId} ein";
    }

    /// <summary>Einladung annehmen (Beitritt) oder ablehnen.</summary>
    public sealed class AnswerInvitationCommand : TradeCommand
    {
        public int InvitationId { get; }
        public bool Accept { get; }
        public string Reason { get; }
        public AnswerInvitationCommand(int party, int invitationId, bool accept, string reason = null) : base(party)
        {
            InvitationId = invitationId; Accept = accept; Reason = reason;
        }
        internal override TradeResult Apply(TradeSystem t) => t.DoAnswerInvitation(Party, InvitationId, Accept, Reason);
        public override string Describe() => $"{Party} {(Accept ? "nimmt" : "lehnt")} Einladung #{InvitationId} {(Accept ? "an" : "ab")}";
    }

    /// <summary>Aus dem Bündnis austreten (Beziehung zu den Mitgliedern sinkt).</summary>
    public sealed class LeaveBlocCommand : TradeCommand
    {
        public int BlocId { get; }
        public LeaveBlocCommand(int party, int blocId) : base(party) { BlocId = blocId; }
        internal override TradeResult Apply(TradeSystem t) => t.DoLeave(Party, BlocId);
        public override string Describe() => $"{Party} tritt aus Bündnis #{BlocId} aus";
    }

    /// <summary>Abstimmung beantragen (Sanktionen gegen ein Land bzw. aufheben); der Antragsteller stimmt mit Ja.</summary>
    public sealed class ProposeVoteCommand : TradeCommand
    {
        public int BlocId { get; }
        public VoteKind Kind { get; }
        public int Subject { get; }
        public ProposeVoteCommand(int party, int blocId, VoteKind kind, int subject) : base(party) { BlocId = blocId; Kind = kind; Subject = subject; }
        internal override TradeResult Apply(TradeSystem t) => t.DoProposeVote(Party, BlocId, Kind, Subject);
        public override string Describe() => $"{Party}: Abstimmung {Kind} gegen {Subject} in Bündnis #{BlocId}";
    }

    /// <summary>Stimme in einer offenen Abstimmung abgeben (nur Mitglieder, einmal).</summary>
    public sealed class CastVoteCommand : TradeCommand
    {
        public int VoteId { get; }
        public VoteChoice Choice { get; }
        public CastVoteCommand(int party, int voteId, VoteChoice choice) : base(party) { VoteId = voteId; Choice = choice; }
        internal override TradeResult Apply(TradeSystem t) => t.DoCastVote(Party, VoteId, Choice);
        public override string Describe() => $"{Party} stimmt {Choice} in Abstimmung #{VoteId}";
    }

    /// <summary>Gemeinsamen Außenzoll der Zollunion setzen (nur der Vorsitz).</summary>
    public sealed class SetBlocTariffCommand : TradeCommand
    {
        public int BlocId { get; }
        public double Rate { get; }
        public SetBlocTariffCommand(int party, int blocId, double rate) : base(party) { BlocId = blocId; Rate = rate; }
        internal override TradeResult Apply(TradeSystem t) => t.DoSetBlocTariff(Party, BlocId, Rate);
        public override string Describe() => $"{Party}: Außenzoll Bündnis #{BlocId} = {Rate * 100:0} %";
    }

    /// <summary>Diplomatie: Geschenk (Betrag in €), Protest gegen Zölle, Drohung.</summary>
    public sealed class DiplomacyCommand : TradeCommand
    {
        public int Target { get; }
        public DiplomacyAction Action { get; }
        public double Amount { get; }
        public DiplomacyCommand(int party, int target, DiplomacyAction action, double amount = 0) : base(party) { Target = target; Action = action; Amount = amount; }
        internal override TradeResult Apply(TradeSystem t) => t.DoDiplomacy(Party, Target, Action, Amount);
        public override string Describe() => $"{Party} → {Target}: {Action}{(Action == DiplomacyAction.Gift ? $" {Amount:0} €" : "")}";
    }
}
