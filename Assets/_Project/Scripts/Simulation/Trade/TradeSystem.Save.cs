using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Spielstand des Handels zwischen Ländern: Länderzustand (Vertrauen, Zölle, Gedächtnis, Volumen, Summen), Beziehungen
    /// und Abkommen, Weltpreise, Angebote, laufende und beendete Verträge mit Lieferungen, Ware unterwegs, Command-Warteschlange
    /// und Command-Log (Replays), Lagebild der KI vom letzten Handelstag, Grenzlager der Stadt, Zufall und Zähler.
    /// Wege (<see cref="TradeGeography"/>), Persönlichkeiten und Preisstreuung entstehen aus Planet + Seed neu.
    /// </summary>
    public sealed partial class TradeSystem : Persistence.ISaveable
    {
        public string SaveId => "trade";
        /// <summary>v2 = Handel 2 (Gegenangebote, Bündnisse, Abstimmungen, Einladungen, Diplomatie, Tourismus).</summary>
        public int SaveVersion => 2;

        public void Write(SaveWriter w)
        {
            rng.WriteState(w);
            w.Int(Day); w.Int(Version); w.Bool(AiEnabled);
            w.Int(nextOfferId); w.Int(nextContractId); w.Int(nextDeliveryId);
            WriteResult(w, LastResult);

            w.Int(nations.Count);
            foreach (var n in nations) n.WriteState(w);
            Write2D(w, relation); Write2D(w, relationBase); Write2D(w, agreements); Write2D(w, priceOffset);
            w.Doubles(worldPrice); w.Doubles(worldPriceWeekAgo);
            foreach (var a in new[] { need, surplus, exportCap, importNeed, cover, contractedIn, contractedOut }) Write2D(w, a);

            w.Int(offers.Count);
            foreach (var o in offers) WriteOffer(w, o);

            // Lieferungen: Tabelle über alle Verträge + unterwegs (gleiche Objekte)
            var deliveries = new List<TradeDelivery>();
            var seen = new HashSet<TradeDelivery>();
            foreach (var c in contracts) foreach (var d in c.Deliveries) if (seen.Add(d)) deliveries.Add(d);
            foreach (var c in history) foreach (var d in c.Deliveries) if (seen.Add(d)) deliveries.Add(d);
            foreach (var d in inTransit) if (seen.Add(d)) deliveries.Add(d);
            w.Int(deliveries.Count);
            foreach (var d in deliveries)
            {
                w.Int(d.Id); w.Int(d.ContractId); w.Int(d.Week); w.Double(d.Units); w.Double(d.UnitPrice);
                w.Int(d.DispatchDay); w.Int(d.ArriveDay); w.Bool(d.Arrived);
            }
            w.Int(contracts.Count);
            foreach (var c in contracts) WriteContract(w, c);
            w.Int(history.Count);
            foreach (var c in history) WriteContract(w, c);
            w.Int(inTransit.Count);
            foreach (var d in inTransit) w.Int(d.Id);

            w.Int(queue.Count);
            foreach (var cmd in queue) WriteCommand(w, cmd);
            w.Int(log.Count);
            foreach (var l in log) { w.Int(l.Day); WriteCommand(w, l.Command); WriteResult(w, l.Result); }
            w.Int(aiLog.Count);
            foreach (var s in aiLog) w.String(s);

            w.Bool(City != null);
            City?.WriteState(w);
            WriteStage2(w); // Handel 2 (v2)
        }

        public void Read(SaveReader r, int version)
        {
            rng.ReadState(r);
            Day = r.Int(); Version = r.Int(); AiEnabled = r.Bool();
            nextOfferId = r.Int(); nextContractId = r.Int(); nextDeliveryId = r.Int();
            LastResult = ReadResult(r);

            int nc = r.Int();
            if (nc != nations.Count) throw new SaveFormatException($"{nc} Handelsländer gespeichert, die Welt hat {nations.Count}.");
            foreach (var n in nations) n.ReadState(r);
            Read2D(r, relation); Read2D(r, relationBase); Read2D(r, agreements); Read2D(r, priceOffset);
            r.DoublesInto(worldPrice); r.DoublesInto(worldPriceWeekAgo);
            foreach (var a in new[] { need, surplus, exportCap, importNeed, cover, contractedIn, contractedOut }) Read2D(r, a);

            offers.Clear(); offerById.Clear();
            int oc = r.Count(20);
            for (int i = 0; i < oc; i++) { var o = ReadOffer(r); offers.Add(o); offerById[o.Id] = o; }

            var deliveries = new Dictionary<int, TradeDelivery>();
            int dc = r.Count(30);
            for (int i = 0; i < dc; i++)
            {
                var d = new TradeDelivery
                {
                    Id = r.Int(), ContractId = r.Int(), Week = r.Int(), Units = r.Double(), UnitPrice = r.Double(),
                    DispatchDay = r.Int(), ArriveDay = r.Int(), Arrived = r.Bool(),
                };
                deliveries[d.Id] = d;
            }
            TradeDelivery FindD(int id) => deliveries.TryGetValue(id, out var d) ? d : throw new SaveFormatException($"Lieferung {id} fehlt.");
            contracts.Clear(); contractById.Clear(); history.Clear(); inTransit.Clear();
            int cc = r.Count(40);
            for (int i = 0; i < cc; i++) { var c = ReadContract(r, FindD); contracts.Add(c); contractById[c.Id] = c; }
            int hc = r.Count(40);
            for (int i = 0; i < hc; i++) { var c = ReadContract(r, FindD); history.Add(c); if (!contractById.ContainsKey(c.Id)) contractById[c.Id] = c; }
            int tc = r.Count(4);
            for (int i = 0; i < tc; i++) inTransit.Add(FindD(r.Int()));

            queue.Clear();
            int qc = r.Count(5);
            for (int i = 0; i < qc; i++) { var cmd = ReadCommand(r); if (cmd != null) queue.Enqueue(cmd); }
            log.Clear();
            int lc = r.Count(10);
            for (int i = 0; i < lc; i++)
            {
                int day = r.Int();
                var cmd = ReadCommand(r);
                var res = ReadResult(r);
                if (cmd != null) log.Add(new LoggedCommand(day, cmd, res));
            }
            aiLog.Clear();
            int al = r.Count(1);
            for (int i = 0; i < al; i++) aiLog.Add(r.String());

            if (r.Bool())
            {
                if (City == null) throw new SaveFormatException("Grenzlager gespeichert, aber keine Stadt angebunden.");
                City.ReadState(r);
            }
            if (version >= 2) ReadStage2(r);
            // v1 (vor Handel 2): Bündnisse wie beim Erzeugen der Welt (Konstruktor), keine Abstimmungen/Einladungen,
            // Tourismus beginnt mit dem nächsten Handelstag
        }

        // ------------------------------------------------------------ Bausteine

        static void Write2D(SaveWriter w, float[,] a)
        {
            w.Int(a.GetLength(0)); w.Int(a.GetLength(1));
            foreach (var v in a) w.Float(v);
        }

        static void Write2D(SaveWriter w, double[,] a)
        {
            w.Int(a.GetLength(0)); w.Int(a.GetLength(1));
            foreach (var v in a) w.Double(v);
        }

        static void Write2D(SaveWriter w, int[,] a)
        {
            w.Int(a.GetLength(0)); w.Int(a.GetLength(1));
            foreach (var v in a) w.Int(v);
        }

        static void Dims(SaveReader r, Array a)
        {
            int x = r.Int(), y = r.Int();
            if (x != a.GetLength(0) || y != a.GetLength(1)) throw new SaveFormatException($"Tabelle {x}×{y} passt nicht ({a.GetLength(0)}×{a.GetLength(1)}).");
        }

        static void Read2D(SaveReader r, float[,] a)
        {
            Dims(r, a);
            for (int i = 0; i < a.GetLength(0); i++) for (int j = 0; j < a.GetLength(1); j++) a[i, j] = r.Float();
        }

        static void Read2D(SaveReader r, double[,] a)
        {
            Dims(r, a);
            for (int i = 0; i < a.GetLength(0); i++) for (int j = 0; j < a.GetLength(1); j++) a[i, j] = r.Double();
        }

        static void Read2D(SaveReader r, int[,] a)
        {
            Dims(r, a);
            for (int i = 0; i < a.GetLength(0); i++) for (int j = 0; j < a.GetLength(1); j++) a[i, j] = r.Int();
        }

        static void WriteResult(SaveWriter w, TradeResult res) { w.Bool(res.Ok); w.String(res.Message); w.Int(res.Id); }
        static TradeResult ReadResult(SaveReader r) { bool ok = r.Bool(); string m = r.String(); return new TradeResult(ok, m, r.Int()); }

        static void WriteTerms(SaveWriter w, ContractTerms t)
        {
            w.Bool(t != null);
            if (t == null) return;
            w.Name(t.Kind); w.String(t.Good?.Id); w.Double(t.UnitsPerWeek); w.Int(t.Weeks); w.Name(t.PriceMode); w.Double(t.Price); w.Double(t.PenaltyShare);
        }

        static ContractTerms ReadTerms(SaveReader r)
        {
            if (!r.Bool()) return null;
            var t = new ContractTerms { Kind = r.Name<ContractKind>() };
            string good = r.String();
            t.Good = good == null ? null : GoodsCatalog.Get(good) ?? throw new SaveFormatException($"Unbekanntes Gut „{good}“.");
            t.UnitsPerWeek = r.Double(); t.Weeks = r.Int(); t.PriceMode = r.Name<PriceMode>(); t.Price = r.Double(); t.PenaltyShare = r.Double();
            return t;
        }

        static void WriteOffer(SaveWriter w, TradeOffer o)
        {
            w.Int(o.Id); w.Int(o.From); w.Int(o.To); w.Name(o.Side); WriteTerms(w, o.Terms);
            w.Int(o.CreatedDay); w.Int(o.ExpiresDay); w.Int(o.AnswerDay); w.Name(o.Status); w.String(o.Reason);
            w.Int(o.RespondedBy); w.Int(o.ContractId);
        }

        static TradeOffer ReadOffer(SaveReader r) => new TradeOffer
        {
            Id = r.Int(), From = r.Int(), To = r.Int(), Side = r.Name<TradeSide>(), Terms = ReadTerms(r),
            CreatedDay = r.Int(), ExpiresDay = r.Int(), AnswerDay = r.Int(), Status = r.Name<OfferStatus>(), Reason = r.String(),
            RespondedBy = r.Int(), ContractId = r.Int(),
        };

        void WriteContract(SaveWriter w, TradeContract c)
        {
            w.Int(c.Id); WriteTerms(w, c.Terms); w.Int(c.Seller); w.Int(c.Buyer); w.Int(c.OfferId);
            w.Int(c.StartDay); w.Int(c.NextDispatchDay); w.Int(c.WeeksDispatched); w.Name(c.Status);
            w.Bool(c.Route != null);
            w.Double(c.UnitsDelivered); w.Double(c.UnitsShort); w.Double(c.ValueDelivered); w.Double(c.Penalties);
            w.Int(c.WeeksOnTime); w.Int(c.WeeksShort); w.Int(c.CancelledBy); w.Int(c.EndDay);
            w.Int(c.Deliveries.Count);
            foreach (var d in c.Deliveries) w.Int(d.Id);
        }

        TradeContract ReadContract(SaveReader r, Func<int, TradeDelivery> findDelivery)
        {
            var c = new TradeContract
            {
                Id = r.Int(), Terms = ReadTerms(r), Seller = r.Int(), Buyer = r.Int(), OfferId = r.Int(),
                StartDay = r.Int(), NextDispatchDay = r.Int(), WeeksDispatched = r.Int(), Status = r.Name<ContractStatus>(),
            };
            bool hasRoute = r.Bool();
            if (hasRoute) c.Route = Geography.Route(c.Seller, c.Buyer); // Wege entstehen aus der Weltkarte neu
            c.UnitsDelivered = r.Double(); c.UnitsShort = r.Double(); c.ValueDelivered = r.Double(); c.Penalties = r.Double();
            c.WeeksOnTime = r.Int(); c.WeeksShort = r.Int(); c.CancelledBy = r.Int(); c.EndDay = r.Int();
            int n = r.Count(4);
            for (int i = 0; i < n; i++) c.Deliveries.Add(findDelivery(r.Int()));
            return c;
        }

        static void WriteCommand(SaveWriter w, TradeCommand cmd)
        {
            switch (cmd)
            {
                case ProposeContractCommand p: w.String("propose"); w.Int(p.Party); w.Int(p.To); w.Name(p.Side); WriteTerms(w, p.Terms); w.Int(p.ValidDays); break;
                case AcceptOfferCommand a: w.String("accept"); w.Int(a.Party); w.Int(a.OfferId); break;
                case RejectOfferCommand j: w.String("reject"); w.Int(j.Party); w.Int(j.OfferId); w.String(j.Reason); break;
                case WithdrawOfferCommand x: w.String("withdraw"); w.Int(x.Party); w.Int(x.OfferId); break;
                case CancelContractCommand c: w.String("cancel"); w.Int(c.Party); w.Int(c.ContractId); break;
                case SetTariffCommand t: w.String("tariff"); w.Int(t.Party); w.Int(t.Target); w.Double(t.Rate); break;
                // --- Handel 2
                case CounterOfferCommand k: w.String("counter"); w.Int(k.Party); w.Int(k.OfferId); WriteTerms(w, k.Terms); w.String(k.Reason); break;
                case FoundBlocCommand f: w.String("foundbloc"); w.Int(f.Party); w.Name(f.Kind); w.String(f.Name); w.Int(f.Partner); break;
                case ApplyToBlocCommand a: w.String("applybloc"); w.Int(a.Party); w.Int(a.BlocId); break;
                case InviteToBlocCommand i: w.String("invitebloc"); w.Int(i.Party); w.Int(i.BlocId); w.Int(i.Target); break;
                case AnswerInvitationCommand an: w.String("answerinv"); w.Int(an.Party); w.Int(an.InvitationId); w.Bool(an.Accept); w.String(an.Reason); break;
                case LeaveBlocCommand l: w.String("leavebloc"); w.Int(l.Party); w.Int(l.BlocId); break;
                case ProposeVoteCommand pv: w.String("proposevote"); w.Int(pv.Party); w.Int(pv.BlocId); w.Name(pv.Kind); w.Int(pv.Subject); break;
                case CastVoteCommand cv: w.String("castvote"); w.Int(cv.Party); w.Int(cv.VoteId); w.Name(cv.Choice); break;
                case SetBlocTariffCommand bt: w.String("bloctariff"); w.Int(bt.Party); w.Int(bt.BlocId); w.Double(bt.Rate); break;
                case DiplomacyCommand d: w.String("diplomacy"); w.Int(d.Party); w.Int(d.Target); w.Name(d.Action); w.Double(d.Amount); break;
                // --- Ende Handel 2
                default: w.String(null); break; // unbekannter Command-Typ: nicht speicherbar (neue Typen hier ergänzen)
            }
        }

        static TradeCommand ReadCommand(SaveReader r)
        {
            switch (r.String())
            {
                case "propose": { int party = r.Int(), to = r.Int(); var side = r.Name<TradeSide>(); var terms = ReadTerms(r); return new ProposeContractCommand(party, to, side, terms, r.Int()); }
                case "accept": { int party = r.Int(); return new AcceptOfferCommand(party, r.Int()); }
                case "reject": { int party = r.Int(), offer = r.Int(); return new RejectOfferCommand(party, offer, r.String()); }
                case "withdraw": { int party = r.Int(); return new WithdrawOfferCommand(party, r.Int()); }
                case "cancel": { int party = r.Int(); return new CancelContractCommand(party, r.Int()); }
                case "tariff": { int party = r.Int(), target = r.Int(); return new SetTariffCommand(party, target, r.Double()); }
                // --- Handel 2 (unbekannte Enum-Werte → Command entfällt)
                case "counter": { int party = r.Int(), offer = r.Int(); var terms = ReadTerms(r); return new CounterOfferCommand(party, offer, terms, r.String()); }
                case "foundbloc": { int party = r.Int(); bool ok = r.TryName<BlocKind>(out var kind); string name = r.String(); int partner = r.Int(); return ok ? new FoundBlocCommand(party, kind, name, partner) : null; }
                case "applybloc": { int party = r.Int(); return new ApplyToBlocCommand(party, r.Int()); }
                case "invitebloc": { int party = r.Int(), bloc = r.Int(); return new InviteToBlocCommand(party, bloc, r.Int()); }
                case "answerinv": { int party = r.Int(), inv = r.Int(); bool acc = r.Bool(); return new AnswerInvitationCommand(party, inv, acc, r.String()); }
                case "leavebloc": { int party = r.Int(); return new LeaveBlocCommand(party, r.Int()); }
                case "proposevote": { int party = r.Int(), bloc = r.Int(); bool ok = r.TryName<VoteKind>(out var kind); int subject = r.Int(); return ok ? new ProposeVoteCommand(party, bloc, kind, subject) : null; }
                case "castvote": { int party = r.Int(), vote = r.Int(); bool ok = r.TryName<VoteChoice>(out var choice); return ok ? new CastVoteCommand(party, vote, choice) : null; }
                case "bloctariff": { int party = r.Int(), bloc = r.Int(); return new SetBlocTariffCommand(party, bloc, r.Double()); }
                case "diplomacy": { int party = r.Int(), target = r.Int(); bool ok = r.TryName<DiplomacyAction>(out var action); double amount = r.Double(); return ok ? new DiplomacyCommand(party, target, action, amount) : null; }
                // --- Ende Handel 2
                case null: return null;
                default: throw new SaveFormatException("Unbekannter Handels-Command.");
            }
        }
    }

    public sealed partial class NationTrade
    {
        internal void WriteState(SaveWriter w)
        {
            w.Int(Id);
            w.Floats(Trust);
            w.Doubles(TradeVolume);
            w.Doubles(TradedToday);
            w.Double(ExportValue); w.Double(ImportValue); w.Double(TariffIncome); w.Double(PenaltiesPaid); w.Double(PenaltiesReceived);
            Tariffs.WriteState(w);
            w.Int(memory.Count);
            foreach (var m in memory) { w.Name(m.Kind); w.Int(m.Partner); w.Int(m.Day); w.Float(m.Magnitude); }
        }

        internal void ReadState(SaveReader r)
        {
            int id = r.Int();
            if (id != Id) throw new SaveFormatException($"Handelsland {id} erwartet {Id}.");
            r.FloatsInto(Trust);
            r.DoublesInto(TradeVolume);
            r.DoublesInto(TradedToday);
            ExportValue = r.Double(); ImportValue = r.Double(); TariffIncome = r.Double(); PenaltiesPaid = r.Double(); PenaltiesReceived = r.Double();
            Tariffs.ReadState(r);
            memory.Clear();
            int n = r.Count(13);
            for (int i = 0; i < n; i++)
            {
                bool known = r.TryName<TradeMemoryKind>(out var kind);
                int partner = r.Int(), day = r.Int();
                float mag = r.Float();
                if (known) memory.Add(new TradeMemory(kind, partner, day, mag));
            }
        }
    }

    public sealed partial class TariffSchedule
    {
        internal void WriteState(SaveWriter w)
        {
            w.Double(General);
            w.Doubles(overrides);
        }

        internal void ReadState(SaveReader r)
        {
            General = r.Double();
            r.DoublesInto(overrides);
        }
    }

    public sealed partial class CityTradeDepot
    {
        internal void WriteState(SaveWriter w)
        {
            foreach (var a in new[] { ImportStock, ImportCost, ImportTariff, ExportStock, ExportEnRoute }) WriteGoods(w, a);
            w.Int(LastImportDay.Length);
            for (int i = 0; i < LastImportDay.Length; i++) { w.String(GoodsCatalog.All[i].Id); w.Int(LastImportDay[i]); }
            w.Double(TariffsTotal); w.Double(SalesToFirms); w.Double(PaidToFirms); w.Double(DumpedValue); w.Double(FreightPaid);
        }

        internal void ReadState(SaveReader r)
        {
            foreach (var a in new[] { ImportStock, ImportCost, ImportTariff, ExportStock, ExportEnRoute }) ReadGoods(r, a);
            int n = r.Count(5);
            for (int i = 0; i < n; i++)
            {
                var g = GoodsCatalog.Get(r.String());
                int day = r.Int();
                if (g != null) LastImportDay[g.Index] = day;
            }
            TariffsTotal = r.Double(); SalesToFirms = r.Double(); PaidToFirms = r.Double(); DumpedValue = r.Double(); FreightPaid = r.Double();
        }

        /// <summary>Je Gut über die Güter-Id (Katalog darf wachsen).</summary>
        static void WriteGoods(SaveWriter w, double[] a)
        {
            w.Int(a.Length);
            for (int i = 0; i < a.Length; i++) { w.String(GoodsCatalog.All[i].Id); w.Double(a[i]); }
        }

        static void ReadGoods(SaveReader r, double[] a)
        {
            Array.Clear(a, 0, a.Length);
            int n = r.Count(9);
            for (int i = 0; i < n; i++)
            {
                var g = GoodsCatalog.Get(r.String());
                double v = r.Double();
                if (g != null) a[g.Index] = v;
            }
        }
    }
}
