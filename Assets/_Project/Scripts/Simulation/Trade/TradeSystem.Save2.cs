using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Handel 2 – Spielstand (Abschnitt „trade“ v2, hinter den v1-Daten): Verhandlungsrunden der Angebote, Bündnisse (Mitglieder,
    /// Sanktionen, Außenzoll), Abstimmungen mit Stimmen, Einladungen, letzte Diplomatie-Aktionen, Tourismus (Flüsse und Besucher
    /// gestern, Rest, Summen je Land). Regierungsformen und Tourismus-Entfernungen entstehen aus Planet + Seed neu.
    /// </summary>
    public sealed partial class TradeSystem
    {
        void WriteStage2(SaveWriter w)
        {
            // Verhandlung: je Angebot (gleiche Reihenfolge wie oben)
            w.Int(offers.Count);
            foreach (var o in offers) { w.Int(o.Id); w.Int(o.CounterOf); w.Int(o.Round); }

            w.Int(nextBlocId); w.Int(nextVoteId); w.Int(nextInvitationId);
            w.Int(blocs.Count);
            foreach (var b in blocs)
            {
                w.Int(b.Id); w.String(b.Name); w.Name(b.Kind); w.Int(b.Founder); w.Int(b.FoundedDay); w.Int(b.DissolvedDay);
                w.Double(b.ExternalTariff);
                w.Int(b.members.Count); foreach (int m in b.members) w.Int(m);
                w.Int(b.sanctioned.Count); foreach (int s in b.sanctioned) w.Int(s);
            }
            w.Int(votes.Count);
            foreach (var v in votes)
            {
                w.Int(v.Id); w.Int(v.BlocId); w.Name(v.Kind); w.Int(v.Subject); w.Int(v.Proposer); w.Int(v.OpenedDay); w.Int(v.FirstVoteDay);
                w.Int(v.CloseDay); w.Name(v.Status); w.String(v.Result);
                w.Int(v.ballots.Count);
                foreach (var kv in v.ballots) { w.Int(kv.Key); w.Name(kv.Value); }
            }
            w.Int(invitations.Count);
            foreach (var x in invitations)
            {
                w.Int(x.Id); w.Int(x.BlocId); w.Int(x.From); w.Int(x.To); w.Int(x.CreatedDay); w.Int(x.ExpiresDay); w.Int(x.AnswerDay);
                w.Name(x.Status); w.String(x.Reason);
            }
            Write2D(w, lastDiplomacy);

            // Tourismus
            Write2D(w, tourismFlow);
            w.Doubles(playerVisitorsFrom);
            w.Doubles(visitorsByMode);
            w.Double(groundRemainder); w.Double(CityAirTourists); w.Double(PlayerTourismMarket); w.Double(PlayerTourismPotentialYesterday);
            w.Long(GroundVisitorsTotal);
            foreach (var n in nations)
            {
                w.Double(n.TourismInYesterday); w.Double(n.TourismOutYesterday); w.Double(n.TourismExportValue); w.Double(n.TourismImportValue);
                w.Double(n.GiftsGiven);
            }
        }

        void ReadStage2(SaveReader r)
        {
            int oc = r.Count(12);
            for (int i = 0; i < oc; i++)
            {
                int id = r.Int(), counterOf = r.Int(), round = r.Int();
                var o = Offer(id);
                if (o != null) { o.CounterOf = counterOf; o.Round = round; }
            }

            nextBlocId = r.Int(); nextVoteId = r.Int(); nextInvitationId = r.Int();
            blocs.Clear();
            int bc = r.Count(20);
            for (int i = 0; i < bc; i++)
            {
                var b = new TradeBloc { Id = r.Int(), Name = r.String() };
                bool known = r.TryName<BlocKind>(out var kind);
                b.Kind = known ? kind : BlocKind.TradeAlliance;
                b.Founder = r.Int(); b.FoundedDay = r.Int(); b.DissolvedDay = r.Int(); b.ExternalTariff = r.Double();
                int mc = r.Count(4);
                for (int k = 0; k < mc; k++) b.members.Add(r.Int());
                int sc = r.Count(4);
                for (int k = 0; k < sc; k++) b.sanctioned.Add(r.Int());
                blocs.Add(b);
            }
            votes.Clear();
            int vc = r.Count(30);
            for (int i = 0; i < vc; i++)
            {
                var v = new BlocVote { Id = r.Int(), BlocId = r.Int() };
                bool kindOk = r.TryName<VoteKind>(out var vk);
                v.Kind = vk;
                v.Subject = r.Int(); v.Proposer = r.Int(); v.OpenedDay = r.Int(); v.FirstVoteDay = r.Int(); v.CloseDay = r.Int();
                bool statusOk = r.TryName<VoteStatus>(out var st);
                v.Status = statusOk ? st : VoteStatus.Failed;
                v.Result = r.String();
                int n = r.Count(5);
                for (int k = 0; k < n; k++)
                {
                    int party = r.Int();
                    if (r.TryName<VoteChoice>(out var c)) v.ballots[party] = c;
                }
                if (kindOk) votes.Add(v);
            }
            invitations.Clear();
            int ic = r.Count(30);
            for (int i = 0; i < ic; i++)
            {
                var x = new BlocInvitation
                {
                    Id = r.Int(), BlocId = r.Int(), From = r.Int(), To = r.Int(), CreatedDay = r.Int(), ExpiresDay = r.Int(), AnswerDay = r.Int(),
                };
                x.Status = r.TryName<OfferStatus>(out var st) ? st : OfferStatus.Expired;
                x.Reason = r.String();
                invitations.Add(x);
            }
            Read2D(r, lastDiplomacy);

            Read2D(r, tourismFlow);
            r.DoublesInto(playerVisitorsFrom);
            r.DoublesInto(visitorsByMode);
            groundRemainder = r.Double(); CityAirTourists = r.Double(); PlayerTourismMarket = r.Double(); PlayerTourismPotentialYesterday = r.Double();
            GroundVisitorsTotal = r.Long();
            foreach (var n in nations)
            {
                n.TourismInYesterday = r.Double(); n.TourismOutYesterday = r.Double(); n.TourismExportValue = r.Double(); n.TourismImportValue = r.Double();
                n.GiftsGiven = r.Double();
            }
        }
    }
}
