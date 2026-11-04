using System;
using System.Collections.Generic;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Handel 2 – Regierungen &amp; Bündnisse: Handelsbündnisse und Zollunionen (Binnenzoll 0, Zollunion mit gemeinsamem
    /// Außenzoll), Beitritt per Antrag (Abstimmung der Mitglieder, ⅔-Mehrheit) oder Einladung, Austritt, Sanktionen per Abstimmung
    /// (einfache Mehrheit: Zoll 50 % und kein neuer Handel mit dem Ziel, laufende Verträge enden ohne Gebühr), Aufhebung.
    /// NPC-Länder stimmen und handeln per Utility AI (<see cref="DecideDiplomacy"/>); der Spieler nutzt dieselben Commands.
    /// Beim Erzeugen der Welt entstehen bis zu <see cref="DiplomacyRules.InitialBlocs"/> Bündnisse aus den Grundbeziehungen.
    /// </summary>
    public sealed partial class TradeSystem
    {
        readonly List<TradeBloc> blocs = new List<TradeBloc>();
        readonly List<BlocVote> votes = new List<BlocVote>();
        readonly List<BlocInvitation> invitations = new List<BlocInvitation>();
        int nextBlocId = 1, nextVoteId = 1, nextInvitationId = 1;
        int[,] lastDiplomacy;

        public IReadOnlyList<TradeBloc> Blocs => blocs;
        public IReadOnlyList<BlocVote> Votes => votes;
        public IReadOnlyList<BlocInvitation> Invitations => invitations;

        public TradeBloc Bloc(int id)
        {
            foreach (var b in blocs) if (b.Id == id) return b;
            return null;
        }

        public BlocVote Vote(int id)
        {
            foreach (var v in votes) if (v.Id == id) return v;
            return null;
        }

        public BlocInvitation Invitation(int id)
        {
            foreach (var i in invitations) if (i.Id == id) return i;
            return null;
        }

        /// <summary>Bündnis, dem ein Land angehört (auch im Aufbau), sonst null.</summary>
        public TradeBloc BlocOf(int n)
        {
            if (n < 0) return null;
            foreach (var b in blocs) if (b.IsMember(n)) return b;
            return null;
        }

        /// <summary>Beide im selben wirksamen Bündnis?</summary>
        public bool SameBloc(int a, int b)
        {
            if (a == b) return false;
            var x = BlocOf(a);
            return x != null && x.Active && x.IsMember(b);
        }

        /// <summary>Sanktionen zwischen a und b (das Bündnis von a sanktioniert b oder umgekehrt)?</summary>
        public bool IsSanctioned(int a, int b)
        {
            if (a == b || a < 0 || b < 0) return false;
            return (BlocOf(a)?.Sanctions(b) ?? false) || (BlocOf(b)?.Sanctions(a) ?? false);
        }

        public string SanctionText(int a, int b)
        {
            var x = BlocOf(a);
            if (x != null && x.Sanctions(b)) return $"Sanktionen von {x.Name} gegen {nations[b].Country.Name}: kein Handel";
            var y = BlocOf(b);
            return y != null ? $"Sanktionen von {y.Name} gegen {nations[a].Country.Name}: kein Handel" : "Sanktionen";
        }

        /// <summary>Grundzoll auf Weltmarkt-Importe (Zollunion: gemeinsamer Außenzoll).</summary>
        public double GeneralTariff(int n)
        {
            var b = BlocOf(n);
            if (b != null && b.Active && b.Kind == BlocKind.CustomsUnion) return b.ExternalTariff;
            return nations[n].Tariffs.General;
        }

        /// <summary>Zoll aus der Bündnislage (null = eigener Satz gilt).</summary>
        double? BlocTariff(int importer, int exporter)
        {
            var b = BlocOf(importer);
            if (b == null || !b.Active) return null;
            if (b.sanctioned.Contains(exporter)) return DiplomacyRules.SanctionTariff;
            if (b.members.Contains(exporter)) return 0.0;
            if (b.Kind == BlocKind.CustomsUnion)
            {
                var own = nations[importer].Tariffs;
                return own.HasOverride(exporter) ? Math.Max(b.ExternalTariff, own.Override(exporter)) : b.ExternalTariff;
            }
            return null;
        }

        // ------------------------------------------------------------ Aufbau

        void AllocateStage2(int n)
        {
            lastDiplomacy = new int[n, n];
            for (int a = 0; a < n; a++) for (int b = 0; b < n; b++) lastDiplomacy[a, b] = int.MinValue / 2;
            AllocateTourism(n);
            InitBlocs();
        }

        static readonly string[] AllianceWords = { "Handelsbund", "Liga", "Pakt" };

        string DefaultBlocName(BlocKind kind, int founder, int salt)
        {
            var c = nations[founder].Country;
            string place = c.CapitalName ?? c.Name;
            return kind == BlocKind.CustomsUnion ? $"Zollunion von {place}" : $"{AllianceWords[salt % AllianceWords.Length]} von {place}";
        }

        /// <summary>Bündnisse beim Erzeugen: Gründer mit den meisten guten Grundbeziehungen, Mitglieder mit Beziehung ≥ 18 zum Gründer.</summary>
        void InitBlocs()
        {
            int n = nations.Count;
            var assigned = new bool[n];
            for (int i = 0; i < n; i++) if (IsPlayer(i)) assigned[i] = true;
            int made = 0;
            for (int attempt = 0; attempt < n && made < DiplomacyRules.InitialBlocs; attempt++)
            {
                int founder = -1;
                float best = 0f;
                for (int a = 0; a < n; a++)
                {
                    if (assigned[a]) continue;
                    float s = 0f;
                    for (int b = 0; b < n; b++)
                        if (b != a && !assigned[b]) s += Math.Max(0f, relationBase[a, b] - DiplomacyRules.InitialBlocRelation);
                    if (s > best) { best = s; founder = a; }
                }
                if (founder < 0) break;
                assigned[founder] = true;
                var members = new List<int> { founder };
                var cands = new List<int>();
                for (int b = 0; b < n; b++)
                    if (!assigned[b] && relationBase[founder, b] >= DiplomacyRules.InitialBlocRelation) cands.Add(b);
                cands.Sort((x, y) => relationBase[founder, y] != relationBase[founder, x] ? relationBase[founder, y].CompareTo(relationBase[founder, x]) : x.CompareTo(y));
                foreach (int c in cands)
                {
                    if (members.Count >= DiplomacyRules.InitialBlocMaxMembers) break;
                    bool ok = true;
                    foreach (int m in members) if (relationBase[m, c] < DiplomacyRules.InitialBlocMinPair) { ok = false; break; }
                    if (ok) members.Add(c);
                }
                if (members.Count < 2) continue;
                var kind = made == 0 ? BlocKind.CustomsUnion : BlocKind.TradeAlliance;
                var bloc = NewBloc(kind, DefaultBlocName(kind, founder, made), founder);
                foreach (int m in members)
                {
                    assigned[m] = true;
                    if (!bloc.members.Contains(m)) bloc.members.Add(m);
                }
                bloc.members.Sort();
                bloc.ExternalTariff = CommonTariff(bloc);
                made++;
            }
        }

        TradeBloc NewBloc(BlocKind kind, string name, int founder)
        {
            var b = new TradeBloc { Id = nextBlocId++, Name = name, Kind = kind, Founder = founder, FoundedDay = Day };
            b.members.Add(founder);
            b.ExternalTariff = Math.Round(Math.Max(0.0, nations[founder].Tariffs.General), 2);
            blocs.Add(b);
            return b;
        }

        /// <summary>Gemeinsamer Außenzoll = Mittel der Grundzölle der Mitglieder (auf Prozent gerundet).</summary>
        double CommonTariff(TradeBloc b)
        {
            double s = 0;
            foreach (int m in b.members) s += nations[m].Tariffs.General;
            return b.members.Count > 0 ? Math.Round(s / b.members.Count, 2) : 0.0;
        }

        int ActiveBlocCount()
        {
            int k = 0;
            foreach (var b in blocs) if (!b.Dissolved) k++;
            return k;
        }

        // ------------------------------------------------------------ Commands

        internal TradeResult DoFoundBloc(int party, BlocKind kind, string name, int partner)
        {
            if (BlocOf(party) != null) return TradeResult.Fail($"schon Mitglied: {BlocOf(party).Name}");
            if (partner < 0 || partner >= nations.Count || partner == party) return TradeResult.Fail("unbekanntes Partnerland");
            if (BlocOf(partner) != null) return TradeResult.Fail($"{nations[partner].Country.Name} ist schon in {BlocOf(partner).Name}");
            if (IsSanctioned(party, partner)) return TradeResult.Fail(SanctionText(party, partner));
            if (ActiveBlocCount() >= DiplomacyRules.MaxBlocs) return TradeResult.Fail($"Höchstens {DiplomacyRules.MaxBlocs} Bündnisse in der Welt");
            var b = NewBloc(kind, string.IsNullOrWhiteSpace(name) ? DefaultBlocName(kind, party, nextBlocId) : name.Trim(), party);
            var inv = DoInvite(party, b.Id, partner);
            return new TradeResult(true, $"{b.Name} gegründet – {nations[partner].Country.Name} eingeladen" + (inv.Ok ? "" : $" ({inv.Message})"), b.Id);
        }

        internal TradeResult DoApply(int party, int blocId)
        {
            var b = Bloc(blocId);
            if (b == null || b.Dissolved) return TradeResult.Fail("Bündnis gibt es nicht (mehr)");
            if (b.IsMember(party)) return TradeResult.Fail("schon Mitglied");
            if (BlocOf(party) != null) return TradeResult.Fail($"erst aus {BlocOf(party).Name} austreten");
            if (b.sanctioned.Contains(party)) return TradeResult.Fail($"{b.Name} hat Sanktionen gegen uns verhängt");
            if (b.members.Count >= DiplomacyRules.MaxBlocMembers) return TradeResult.Fail($"{b.Name} ist voll ({DiplomacyRules.MaxBlocMembers} Mitglieder)");
            foreach (var v in votes)
                if (v.IsOpen && v.BlocId == blocId && v.Kind == VoteKind.Admit && v.Subject == party) return TradeResult.Fail("Antrag läuft bereits");
            var vote = NewVote(b, VoteKind.Admit, party, party);
            return new TradeResult(true, $"Aufnahmeantrag bei {b.Name} gestellt – Abstimmung bis Tag {vote.CloseDay}", vote.Id);
        }

        internal TradeResult DoInvite(int party, int blocId, int target)
        {
            var b = Bloc(blocId);
            if (b == null || b.Dissolved) return TradeResult.Fail("Bündnis gibt es nicht (mehr)");
            if (!b.IsMember(party)) return TradeResult.Fail("nur Mitglieder laden ein");
            if (target < 0 || target >= nations.Count || b.IsMember(target)) return TradeResult.Fail("unbekanntes Land bzw. schon Mitglied");
            if (BlocOf(target) != null) return TradeResult.Fail($"{nations[target].Country.Name} ist schon in {BlocOf(target).Name}");
            if (b.sanctioned.Contains(target) || IsSanctioned(party, target)) return TradeResult.Fail("Sanktionen bestehen");
            if (b.members.Count >= DiplomacyRules.MaxBlocMembers) return TradeResult.Fail($"{b.Name} ist voll ({DiplomacyRules.MaxBlocMembers} Mitglieder)");
            foreach (var x in invitations)
                if (x.IsOpen && x.BlocId == blocId && x.To == target) return TradeResult.Fail("Einladung läuft bereits");
            var inv = new BlocInvitation
            {
                Id = nextInvitationId++, BlocId = blocId, From = party, To = target, CreatedDay = Day,
                ExpiresDay = Day + DiplomacyRules.InvitationValidDays, AnswerDay = inDayStep ? Day + 1 : Day, Status = OfferStatus.Open,
            };
            invitations.Add(inv);
            if (IsPlayer(target)) Report(TradeNewsKind.BlocInvitation, party, 0, b.Name);
            return new TradeResult(true, $"{nations[target].Country.Name} in {b.Name} eingeladen", inv.Id);
        }

        internal TradeResult DoAnswerInvitation(int party, int invitationId, bool accept, string reason)
        {
            var inv = Invitation(invitationId);
            if (inv == null || !inv.IsOpen) return TradeResult.Fail("Einladung nicht mehr offen");
            if (inv.To != party) return TradeResult.Fail("Einladung gilt einem anderen Land");
            var b = Bloc(inv.BlocId);
            if (b == null || b.Dissolved) { inv.Status = OfferStatus.Expired; return TradeResult.Fail("Bündnis gibt es nicht mehr"); }
            if (!accept)
            {
                inv.Status = OfferStatus.Rejected;
                inv.Reason = reason ?? "abgelehnt";
                return new TradeResult(true, $"Einladung in {b.Name} abgelehnt", inv.Id);
            }
            if (BlocOf(party) != null) return TradeResult.Fail($"erst aus {BlocOf(party).Name} austreten");
            if (b.members.Count >= DiplomacyRules.MaxBlocMembers) { inv.Status = OfferStatus.Expired; return TradeResult.Fail($"{b.Name} ist inzwischen voll"); }
            inv.Status = OfferStatus.Accepted;
            inv.Reason = reason ?? "angenommen";
            JoinBloc(b, party);
            return new TradeResult(true, $"{nations[party].Country.Name} ist {b.Name} beigetreten", b.Id);
        }

        internal TradeResult DoLeave(int party, int blocId)
        {
            var b = Bloc(blocId);
            if (b == null || !b.IsMember(party)) return TradeResult.Fail("nicht Mitglied");
            b.members.Remove(party);
            foreach (int m in b.members)
            {
                AddRelation(party, m, -DiplomacyRules.RelationLeave);
                nations[m].Remember(new TradeMemory(TradeMemoryKind.BlocLeft, party, Day, 1f));
            }
            bool playerInvolved = IsPlayer(party) || b.members.Contains(PlayerId);
            if (playerInvolved) Report(TradeNewsKind.BlocLeft, IsPlayer(party) ? (b.members.Count > 0 ? b.members[0] : party) : party, b.members.Count, b.Name);
            if (b.members.Count < 2) Dissolve(b, "zu wenige Mitglieder");
            else if (b.Founder == party && b.members.Count > 0) b.Founder = b.members[0];
            return new TradeResult(true, $"aus {b.Name} ausgetreten", b.Id);
        }

        internal TradeResult DoProposeVote(int party, int blocId, VoteKind kind, int subject)
        {
            var b = Bloc(blocId);
            if (b == null || b.Dissolved) return TradeResult.Fail("Bündnis gibt es nicht (mehr)");
            if (!b.IsMember(party)) return TradeResult.Fail("nur Mitglieder beantragen Abstimmungen");
            if (subject < 0 || subject >= nations.Count || subject == party) return TradeResult.Fail("unbekanntes Land");
            switch (kind)
            {
                case VoteKind.Sanction:
                    if (!b.Active) return TradeResult.Fail("Bündnis noch im Aufbau");
                    if (b.IsMember(subject)) return TradeResult.Fail("keine Sanktionen gegen Mitglieder");
                    if (b.sanctioned.Contains(subject)) return TradeResult.Fail("Sanktionen bestehen bereits");
                    break;
                case VoteKind.LiftSanction:
                    if (!b.sanctioned.Contains(subject)) return TradeResult.Fail("keine Sanktionen gegen dieses Land");
                    break;
                default:
                    if (b.IsMember(subject) || BlocOf(subject) != null) return TradeResult.Fail("Land ist schon in einem Bündnis");
                    break;
            }
            foreach (var v in votes)
                if (v.IsOpen && v.BlocId == blocId && v.Kind == kind && v.Subject == subject) return TradeResult.Fail("Abstimmung läuft bereits");
            var vote = NewVote(b, kind, subject, party);
            vote.ballots[party] = VoteChoice.Yes;
            return new TradeResult(true, $"Abstimmung „{vote.KindText}: {nations[subject].Country.Name}“ in {b.Name} bis Tag {vote.CloseDay}", vote.Id);
        }

        internal TradeResult DoCastVote(int party, int voteId, VoteChoice choice)
        {
            var v = Vote(voteId);
            if (v == null || !v.IsOpen) return TradeResult.Fail("Abstimmung nicht offen");
            var b = Bloc(v.BlocId);
            if (b == null || !b.IsMember(party)) return TradeResult.Fail("nur Mitglieder stimmen ab");
            if (party == v.Subject) return TradeResult.Fail("betroffenes Land stimmt nicht mit");
            if (v.ballots.ContainsKey(party)) return TradeResult.Fail("schon abgestimmt");
            v.ballots[party] = choice;
            return new TradeResult(true, $"Stimme abgegeben: {ChoiceText(choice)}", v.Id);
        }

        internal TradeResult DoSetBlocTariff(int party, int blocId, double rate)
        {
            var b = Bloc(blocId);
            if (b == null || b.Dissolved) return TradeResult.Fail("Bündnis gibt es nicht (mehr)");
            if (b.Kind != BlocKind.CustomsUnion) return TradeResult.Fail("nur Zollunionen haben einen Außenzoll");
            if (b.Founder != party) return TradeResult.Fail("nur der Vorsitz setzt den Außenzoll");
            if (double.IsNaN(rate)) return TradeResult.Fail("Satz fehlt");
            double before = PlayerId >= 0 && !b.IsMember(PlayerId) ? TariffRate(b.Founder, PlayerId) : 0.0;
            b.ExternalTariff = Math.Round(Math.Clamp(rate, 0.0, TradeRules.MaxTariff), 3);
            if (PlayerId >= 0 && !b.IsMember(PlayerId) && TariffRate(b.Founder, PlayerId) > before + 1e-9)
                Report(TradeNewsKind.TariffRaised, party, TariffRate(b.Founder, PlayerId), null);
            return new TradeResult(true, $"Außenzoll {b.Name}: {b.ExternalTariff * 100:0} %", b.Id);
        }

        public static string ChoiceText(VoteChoice c) => c == VoteChoice.Yes ? "Ja" : c == VoteChoice.No ? "Nein" : "Enthaltung";

        // ------------------------------------------------------------ Ablauf

        BlocVote NewVote(TradeBloc b, VoteKind kind, int subject, int proposer)
        {
            var v = new BlocVote
            {
                Id = nextVoteId++, BlocId = b.Id, Kind = kind, Subject = subject, Proposer = proposer, OpenedDay = Day,
                FirstVoteDay = inDayStep ? Day + 1 : Day, CloseDay = Day + DiplomacyRules.VoteDays, Status = VoteStatus.Open,
            };
            votes.Add(v);
            return v;
        }

        void JoinBloc(TradeBloc b, int n)
        {
            foreach (int m in b.members)
            {
                AddRelation(n, m, DiplomacyRules.RelationJoin);
                nations[m].Remember(new TradeMemory(TradeMemoryKind.BlocJoined, n, Day, 1f));
            }
            b.members.Add(n);
            b.members.Sort();
            // offene Anträge/Einladungen des Landes erledigen sich
            foreach (var x in invitations) if (x.IsOpen && x.To == n) { x.Status = OfferStatus.Expired; x.Reason = $"beigetreten: {b.Name}"; }
            foreach (var v in votes)
                if (v.IsOpen && v.Kind == VoteKind.Admit && v.Subject == n) { v.Status = VoteStatus.Failed; v.Result = $"beigetreten: {b.Name}"; }
            if (IsPlayer(n) || b.members.Contains(PlayerId)) Report(TradeNewsKind.BlocJoined, IsPlayer(n) ? b.Founder : n, b.members.Count, b.Name);
        }

        void Dissolve(TradeBloc b, string why)
        {
            if (b.Dissolved) return;
            b.DissolvedDay = Day;
            b.sanctioned.Clear();
            foreach (var v in votes) if (v.IsOpen && v.BlocId == b.Id) { v.Status = VoteStatus.Failed; v.Result = "Bündnis aufgelöst"; }
            foreach (var x in invitations) if (x.IsOpen && x.BlocId == b.Id) { x.Status = OfferStatus.Expired; x.Reason = "Bündnis aufgelöst"; }
            foreach (int m in b.members) if (IsPlayer(m)) Report(TradeNewsKind.BlocLeft, b.Founder, 0, b.Name + " (aufgelöst: " + why + ")");
            b.members.Clear();
        }

        /// <summary>NPC beantworten Einladungen (eine Nacht Bedenkzeit) nach dem Beitritts-Nutzen.</summary>
        void RespondToInvitations()
        {
            for (int i = 0; i < invitations.Count; i++)
            {
                var inv = invitations[i];
                if (!inv.IsOpen || IsPlayer(inv.To) || inv.AnswerDay > Day) continue;
                var b = Bloc(inv.BlocId);
                if (b == null || b.Dissolved) { inv.Status = OfferStatus.Expired; continue; }
                float u = JoinUtility(inv.To, b, out string why) + Noise(inv.To);
                Execute(new AnswerInvitationCommand(inv.To, inv.Id, u > 0f, why));
            }
        }

        /// <summary>Kleine Streuung nach Risiko (Zufall aus dem Handels-RNG).</summary>
        float Noise(int n) => (rng.NextFloat() - 0.5f) * 0.3f * (0.3f + nations[n].Personality.Risk);

        /// <summary>
        /// Nutzen eines Beitritts: 0,9 × Ø Beziehung/100 + 0,5 × (Ø Vertrauen − 0,6) + Freihandel (Zollunion stärker) + 0,08 je
        /// gleicher Regierungsform − 0,1 Trägheit. Begründung = stärkster Baustein.
        /// </summary>
        float JoinUtility(int n, TradeBloc b, out string reason)
        {
            var pers = nations[n].Personality;
            float rel = 0f, trust = 0f, gov = 0f;
            int k = 0;
            foreach (int m in b.members)
            {
                if (m == n) continue;
                rel += Relation(n, m);
                trust += nations[n].Trust[m];
                if (nations[m].Government == nations[n].Government) gov += 1f;
                k++;
            }
            if (k == 0) { reason = "keine Mitglieder"; return -1f; }
            rel /= k; trust /= k;
            float relPart = 0.9f * rel / 100f, trustPart = 0.5f * (trust - TradeRules.TrustStart);
            float openPart = b.Kind == BlocKind.CustomsUnion ? 0.35f * (0.4f - pers.Protectionism) : 0.25f * (0.5f - pers.Protectionism);
            float u = relPart + trustPart + openPart + 0.08f * gov / k - 0.1f;
            if (u > 0f) reason = relPart >= openPart ? "gute Beziehungen zu den Mitgliedern" : "freier Handel lohnt sich";
            else reason = relPart <= openPart && relPart < 0.05f ? $"Beziehung zu den Mitgliedern zu schwach (Ø {rel:+0;−0})"
                : openPart < 0f ? (b.Kind == BlocKind.CustomsUnion ? "wir wollen unsere Zölle selbst bestimmen" : "wir schützen unsere Wirtschaft")
                : "kein Anlass für einen Beitritt";
            return u;
        }

        /// <summary>Stimme eines NPC-Mitglieds (Utility AI).</summary>
        VoteChoice NpcVote(int n, BlocVote v, TradeBloc b)
        {
            var pers = nations[n].Personality;
            int s = v.Subject;
            float rel = Relation(n, s) / 100f;
            float trust = nations[n].Trust[s] - TradeRules.TrustStart;
            float u;
            switch (v.Kind)
            {
                case VoteKind.Admit:
                    u = 0.8f * rel + 1.0f * trust + (nations[s].Government == nations[n].Government ? 0.1f : 0f)
                        - (b.Kind == BlocKind.CustomsUnion ? 0.2f : 0.1f) * pers.Protectionism + 0.08f;
                    break;
                case VoteKind.Sanction:
                    u = -rel - DiplomacyRules.SanctionVoteBias + 0.6f * MemoryOf(n, s, TradeMemoryKind.Breach) + 0.3f * MemoryOf(n, s, TradeMemoryKind.Threat)
                        - 0.1f * ContractsBetween(n, s) - 0.5f * trust;
                    break;
                default:
                    u = rel + 0.15f;
                    break;
            }
            u += Noise(n);
            return Math.Abs(u) < 0.02f ? VoteChoice.Abstain : u > 0f ? VoteChoice.Yes : VoteChoice.No;
        }

        /// <summary>Gab es in diesem Bündnis eine Abstimmung dieser Art (Betroffener −1 = egal) in den letzten Tagen?</summary>
        bool RecentVote(int blocId, VoteKind kind, int subject, int days)
        {
            foreach (var v in votes)
                if (v.BlocId == blocId && v.Kind == kind && (subject < 0 || v.Subject == subject) && Day - v.OpenedDay < days) return true;
            return false;
        }

        float MemoryOf(int n, int partner, TradeMemoryKind kind)
        {
            foreach (var m in nations[n].Memory) if (m.Kind == kind && m.Partner == partner && Day - m.Day <= 120) return 1f;
            return 0f;
        }

        int ContractsBetween(int a, int b)
        {
            int k = 0;
            foreach (var c in contracts) if (c.IsActive && c.Involves(a) && c.Involves(b)) k++;
            return k;
        }

        /// <summary>Täglich: NPC stimmen ab (ab dem Tag nach dem Antrag); Ende, wenn alle gestimmt haben oder die Frist abläuft.</summary>
        void UpdateVotes()
        {
            for (int i = 0; i < votes.Count; i++)
            {
                var v = votes[i];
                if (!v.IsOpen) continue;
                var b = Bloc(v.BlocId);
                if (b == null || b.Dissolved) { v.Status = VoteStatus.Failed; v.Result = "Bündnis aufgelöst"; continue; }
                bool all = true;
                foreach (int m in b.members)
                {
                    if (m == v.Subject || v.ballots.ContainsKey(m)) continue;
                    if (!IsPlayer(m) && Day >= v.FirstVoteDay) v.ballots[m] = NpcVote(m, v, b);
                    else all = false;
                }
                if (all || Day >= v.CloseDay) Resolve(v, b);
            }
        }

        void Resolve(BlocVote v, TradeBloc b)
        {
            // Stimmen von Ländern, die nicht mehr Mitglied sind, zählen nicht
            var gone = new List<int>();
            foreach (var kv in v.ballots) if (!b.IsMember(kv.Key)) gone.Add(kv.Key);
            foreach (int g in gone) v.ballots.Remove(g);
            int yes = v.Count(VoteChoice.Yes), no = v.Count(VoteChoice.No);
            bool passed = v.Kind == VoteKind.Admit ? yes > 0 && yes >= DiplomacyRules.AdmitMajority * (yes + no) - 1e-9 : yes > no;
            v.Status = passed ? VoteStatus.Passed : VoteStatus.Failed;
            v.Result = $"{(passed ? "angenommen" : "abgelehnt")} ({yes} Ja, {no} Nein, {v.Count(VoteChoice.Abstain)} Enthaltung)";
            if (!passed) return;
            switch (v.Kind)
            {
                case VoteKind.Admit:
                    if (BlocOf(v.Subject) == null && b.members.Count < DiplomacyRules.MaxBlocMembers) JoinBloc(b, v.Subject);
                    else { v.Status = VoteStatus.Failed; v.Result += " – Beitritt nicht mehr möglich"; }
                    break;
                case VoteKind.Sanction:
                    ImposeSanction(b, v.Subject);
                    break;
                default:
                    if (b.sanctioned.Remove(v.Subject))
                    {
                        foreach (int m in b.members) AddRelation(m, v.Subject, 4f);
                        if (IsPlayer(v.Subject) || b.members.Contains(PlayerId)) Report(TradeNewsKind.Sanction, v.Subject, 0, $"{b.Name} hebt die Sanktionen auf");
                    }
                    break;
            }
        }

        /// <summary>Sanktion: Ziel in die Liste, Beziehungen sinken, laufende Verträge mit Mitgliedern enden ohne Gebühr, offene Angebote verfallen.</summary>
        void ImposeSanction(TradeBloc b, int s)
        {
            if (b.sanctioned.Contains(s)) return;
            b.sanctioned.Add(s);
            b.sanctioned.Sort();
            foreach (int m in b.members)
            {
                AddRelation(m, s, -DiplomacyRules.RelationSanction);
                nations[s].Remember(new TradeMemory(TradeMemoryKind.Sanction, m, Day, 1f));
                nations[m].Remember(new TradeMemory(TradeMemoryKind.Sanction, s, Day, 1f));
            }
            foreach (var c in contracts)
            {
                if (!c.IsActive || !c.Involves(s) || !b.members.Contains(c.Partner(s))) continue;
                if (c.Kind == ContractKind.Agreement) { agreements[c.Seller, c.Buyer]--; agreements[c.Buyer, c.Seller]--; }
                c.Status = ContractStatus.Cancelled;
                c.CancelledBy = -2; // Sanktion (keine Gebühr)
                c.EndDay = Day;
            }
            foreach (var o in offers)
            {
                if (!o.IsOpen) continue;
                bool between = (o.From == s && (o.To < 0 || b.members.Contains(o.To))) || (o.To == s && b.members.Contains(o.From));
                if (between && o.To >= 0) { o.Status = OfferStatus.Withdrawn; o.Reason = $"Sanktionen von {b.Name}"; }
            }
            if (IsPlayer(s) || b.members.Contains(PlayerId)) Report(TradeNewsKind.Sanction, s, 1, $"{b.Name} verhängt Sanktionen gegen {nations[s].Country.Name}");
        }

        /// <summary>Täglich: Einladungen verfallen, Bündnisse im Aufbau ohne Beitritt lösen sich auf, alte Einträge werden vergessen.</summary>
        void ExpireBlocs()
        {
            foreach (var x in invitations)
                if (x.IsOpen && x.ExpiresDay <= Day) { x.Status = OfferStatus.Expired; x.Reason ??= "abgelaufen"; }
            foreach (var b in blocs)
            {
                if (b.Dissolved || b.members.Count >= 2) continue;
                bool pending = false;
                foreach (var x in invitations) if (x.IsOpen && x.BlocId == b.Id) pending = true;
                foreach (var v in votes) if (v.IsOpen && v.BlocId == b.Id) pending = true;
                if (pending) continue;
                bool invitedBefore = false;
                foreach (var x in invitations) if (x.BlocId == b.Id) invitedBefore = true;
                // im Aufbau: alle Einladungen beantwortet/abgelaufen oder Frist vorbei → auflösen
                if (invitedBefore || Day - b.FoundedDay >= DiplomacyRules.FormingDays) Dissolve(b, "niemand ist beigetreten");
            }
            invitations.RemoveAll(x => !x.IsOpen && Day - x.CreatedDay > 30);
            votes.RemoveAll(v => !v.IsOpen && Day - v.OpenedDay > 60);
            blocs.RemoveAll(b => b.Dissolved && Day - b.DissolvedDay > 60);
        }

        // ------------------------------------------------------------ KI (wöchentlich)

        /// <summary>
        /// Bündnispolitik eines NPC-Landes (höchstens eine Aktion je Woche, Chance 35 %): ohne Bündnis Beitritt beantragen (bester
        /// Beitritts-Nutzen &gt; 0,05) oder mit dem besten Partner (Beziehung ≥ 40) ein Handelsbündnis gründen; im Bündnis austreten
        /// (Ø Beziehung &lt; −10), Sanktionen beantragen (Beziehung &lt; −45) bzw. aufheben (&gt; −15) oder ein Land einladen (Ø Beziehung
        /// der Mitglieder ≥ 32, auch den Spieler). Außerdem: Protest gegen hohe Zölle (≥ 25 %) anderer Länder.
        /// </summary>
        void DecideDiplomacy(int n)
        {
            if (!rng.Chance(DiplomacyRules.DiplomacyChance)) return;
            var me = nations[n];
            var b = BlocOf(n);
            if (b == null)
            {
                TradeBloc bestBloc = null;
                float bestU = 0.1f;
                foreach (var x in blocs)
                {
                    if (!x.Active || x.sanctioned.Contains(n) || x.members.Count >= DiplomacyRules.MaxBlocMembers) continue;
                    if (RecentVote(x.Id, VoteKind.Admit, n, DiplomacyRules.ReapplyDays)) continue; // abgelehnt: erst später wieder
                    float u = JoinUtility(n, x, out _);
                    if (u > bestU) { bestU = u; bestBloc = x; }
                }
                if (bestBloc != null) { Execute(new ApplyToBlocCommand(n, bestBloc.Id)); return; }
                if (ActiveBlocCount() < DiplomacyRules.MaxBlocs)
                {
                    int partner = -1;
                    float bestRel = DiplomacyRules.FoundRelation;
                    for (int m = 0; m < nations.Count; m++)
                    {
                        if (m == n || IsPlayer(m) || BlocOf(m) != null) continue;
                        float r = Relation(n, m);
                        if (r > bestRel) { bestRel = r; partner = m; }
                    }
                    if (partner >= 0)
                    {
                        var kind = me.Personality.Protectionism >= 0.5f ? BlocKind.CustomsUnion : BlocKind.TradeAlliance;
                        Execute(new FoundBlocCommand(n, kind, null, partner));
                        return;
                    }
                }
            }
            else
            {
                float avg = 0f;
                int k = 0;
                foreach (int m in b.members) if (m != n) { avg += Relation(n, m); k++; }
                if (k > 0 && avg / k < -10f) { Execute(new LeaveBlocCommand(n, b.Id)); return; }
                if (b.Active)
                {
                    // Sanktionen beantragen / aufheben
                    int worst = -1;
                    float worstRel = DiplomacyRules.SanctionRelation;
                    for (int m = 0; m < nations.Count; m++)
                    {
                        if (m == n || b.IsMember(m)) continue;
                        float r = Relation(n, m);
                        if (b.sanctioned.Contains(m))
                        {
                            if (r > DiplomacyRules.LiftRelation && !RecentVote(b.Id, VoteKind.LiftSanction, m, DiplomacyRules.SanctionCooldownDays)
                                && Execute(new ProposeVoteCommand(n, b.Id, VoteKind.LiftSanction, m)).Ok) return;
                            continue;
                        }
                        bool cause = r < DiplomacyRules.SanctionWithoutCause
                                     || MemoryOf(n, m, TradeMemoryKind.Breach) > 0 || MemoryOf(n, m, TradeMemoryKind.Threat) > 0;
                        if (r < worstRel && cause) { worstRel = r; worst = m; }
                    }
                    if (worst >= 0 && !RecentVote(b.Id, VoteKind.Sanction, -1, DiplomacyRules.SanctionCooldownDays)
                        && Execute(new ProposeVoteCommand(n, b.Id, VoteKind.Sanction, worst)).Ok) return;
                }
                // Einladen: bestes Land ohne Bündnis mit guter Ø-Beziehung der Mitglieder
                int cand = -1;
                float candRel = DiplomacyRules.InviteRelation;
                for (int m = 0; m < nations.Count; m++)
                {
                    if (b.IsMember(m) || BlocOf(m) != null || b.sanctioned.Contains(m)) continue;
                    float s = 0f;
                    foreach (int x in b.members) s += Relation(x, m);
                    s /= Math.Max(1, b.members.Count);
                    if (s > candRel) { candRel = s; cand = m; }
                }
                if (cand >= 0 && b.members.Count < DiplomacyRules.MaxBlocMembers && Execute(new InviteToBlocCommand(n, b.Id, cand)).Ok) return;
            }
            // Protest gegen hohe Zölle anderer Länder
            if (me.Personality.Protectionism < 0.5f)
                for (int m = 0; m < nations.Count; m++)
                {
                    if (m == n || IsPlayer(m)) continue;
                    if (TariffRate(m, n) >= 0.25 && Day - lastDiplomacy[n, m] >= DiplomacyRules.DiplomacyCooldownDays * 4)
                    {
                        Execute(new DiplomacyCommand(n, m, DiplomacyAction.Protest));
                        return;
                    }
                }
        }

        // ------------------------------------------------------------ Diplomatie

        /// <summary>Tag der letzten Protest-/Drohaktion von a gegen b (sehr klein = nie).</summary>
        public int LastDiplomacyDay(int a, int b) => lastDiplomacy[a, b];

        internal TradeResult DoDiplomacy(int party, int target, DiplomacyAction action, double amount)
        {
            if (target < 0 || target >= nations.Count || target == party) return TradeResult.Fail("unbekanntes Land");
            var t = nations[target];
            var tp = t.Personality;
            string name = t.Country.Name;
            switch (action)
            {
                case DiplomacyAction.Gift:
                {
                    if (!(amount >= DiplomacyRules.MinGift)) return TradeResult.Fail($"Geschenk mindestens {DiplomacyRules.MinGift:N0} €");
                    if (IsPlayer(party))
                    {
                        if (City != null && !City.CanAfford(amount)) return TradeResult.Fail("Zu wenig Geld in der Stadtkasse");
                        City?.Post(CityBuilder.Simulation.Economy.BudgetCategory.Diplomacy, amount);
                    }
                    double prior = 0;
                    foreach (var m in t.Memory)
                        if (m.Kind == TradeMemoryKind.Gift && m.Partner == party && Day - m.Day <= DiplomacyRules.GiftMemoryDays) prior = m.Magnitude;
                    double gain = DiplomacyRules.GiftRelationFactor *
                                  (Math.Log10(1.0 + (prior + amount) / DiplomacyRules.GiftReference) - Math.Log10(1.0 + prior / DiplomacyRules.GiftReference));
                    AddRelation(party, target, (float)gain);
                    t.Trust[party] = Math.Min(1f, t.Trust[party] + 0.01f);
                    t.Remember(new TradeMemory(TradeMemoryKind.Gift, party, Day, (float)(prior + amount)));
                    nations[party].GiftsGiven += amount;
                    return new TradeResult(true, $"Geschenk an {name}: {amount:N0} € · Beziehung {gain:+0.0;−0.0}");
                }
                case DiplomacyAction.Protest:
                {
                    if (Day - lastDiplomacy[party, target] < DiplomacyRules.DiplomacyCooldownDays)
                        return TradeResult.Fail($"Protest erst wieder ab Tag {lastDiplomacy[party, target] + DiplomacyRules.DiplomacyCooldownDays}");
                    lastDiplomacy[party, target] = Day;
                    AddRelation(party, target, -DiplomacyRules.RelationProtest);
                    t.Remember(new TradeMemory(TradeMemoryKind.Protest, party, Day, 1f));
                    var tt = t.Tariffs;
                    if (tt.HasOverride(party) && tt.Override(party) > tt.General + 1e-9 && rng.Chance(0.3 + 0.4 * (1.0 - tp.Protectionism)))
                    {
                        Execute(new SetTariffCommand(target, party, double.NaN));
                        return new TradeResult(true, $"{name} lenkt ein: Sonderzoll gegen uns aufgehoben (Beziehung −{DiplomacyRules.RelationProtest:0})");
                    }
                    return new TradeResult(true, $"{name} weist den Protest zurück (Beziehung −{DiplomacyRules.RelationProtest:0})");
                }
                default:
                {
                    if (Day - lastDiplomacy[party, target] < DiplomacyRules.DiplomacyCooldownDays)
                        return TradeResult.Fail($"Drohung erst wieder ab Tag {lastDiplomacy[party, target] + DiplomacyRules.DiplomacyCooldownDays}");
                    lastDiplomacy[party, target] = Day;
                    double power = Math.Max(1.0, nations[party].Economy.Population) / Math.Max(1.0, t.Economy.Population);
                    double chance = Math.Clamp(0.15 + 0.25 * Math.Log10(power), 0.02, 0.8) * (1.0 - 0.5 * tp.Risk);
                    AddRelation(party, target, -DiplomacyRules.RelationThreat);
                    t.Trust[party] = Math.Max(0f, t.Trust[party] - DiplomacyRules.TrustThreat);
                    t.Remember(new TradeMemory(TradeMemoryKind.Threat, party, Day, 1f));
                    if (rng.Chance(chance))
                    {
                        bool had = t.Tariffs.HasOverride(party);
                        if (had) Execute(new SetTariffCommand(target, party, double.NaN));
                        return new TradeResult(true, $"{name} gibt nach{(had ? ": Sonderzoll gegen uns aufgehoben" : "")} (Chance {chance * 100:0} %)");
                    }
                    if (tp.Protectionism >= 0.3f)
                    {
                        double rate = Math.Min(TradeRules.MaxTariff, t.Tariffs.RateFor(party) + DiplomacyRules.ThreatRetaliation);
                        Execute(new SetTariffCommand(target, party, rate));
                        return new TradeResult(true, $"{name} antwortet mit Gegenzoll {rate * 100:0} % (Chance zum Nachgeben war {chance * 100:0} %)");
                    }
                    return new TradeResult(true, $"{name} lässt sich nicht beeindrucken (Chance {chance * 100:0} %)");
                }
            }
        }
    }
}
