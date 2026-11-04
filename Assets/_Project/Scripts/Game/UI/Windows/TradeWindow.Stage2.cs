using System.Collections.Generic;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Trade;
using CityBuilder.Simulation.World;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Handel 2 im Handelsfenster (F11): Reiter <b>Verhandlung</b> (Gegenangebote mit Vorschlag „was würde … annehmen?“,
    /// Verläufe), <b>Bündnisse</b> (eigenes Bündnis, Einladungen, Abstimmungen, Aufnahme beantragen, gründen, Sanktionen),
    /// <b>Diplomatie</b> (Geschenk, Protest, Drohung) und <b>Tourismus</b> (Besucher der Stadt nach Herkunft und Anreise,
    /// Tourismus-Handel der Länder). Alle Aktionen als Commands an das <see cref="TradeSystem"/>.
    /// </summary>
    public sealed partial class TradeWindow
    {
        int draftCounterOf;
        BlocKind draftBlocKind = BlocKind.TradeAlliance;
        double draftGift = 50000;
        readonly List<TradeOffer> chainBuffer = new List<TradeOffer>();

        // ------------------------------------------------------------ Verhandlung

        /// <summary>Entwurf aus einem Angebot an uns füllen (Seite gedreht, Ziel = Anbieter) und zur Verhandlung wechseln.</summary>
        void StartCounter(TradeSystem t, TradeOffer o)
        {
            if (o?.Terms?.Good == null) return;
            draftCounterOf = o.Id;
            draftTarget = o.From;
            draftSide = o.Side == TradeSide.Sell ? TradeSide.Buy : TradeSide.Sell;
            var goods = GoodsCatalog.All;
            for (int i = 0; i < goods.Count; i++) if (goods[i] == o.Terms.Good) draftGood = i;
            draftUnits = o.Terms.UnitsPerWeek;
            draftWeeks = o.Terms.Weeks;
            draftMode = o.Terms.PriceMode;
            double world = t.WorldPrice(o.Terms.Good);
            draftPriceFactor = o.Terms.PriceMode == PriceMode.Fixed ? (world > 0 ? t.UnitPrice(o.Terms) / world : 1.0) : o.Terms.Price;
            draftPenalty = o.Terms.PenaltyShare;
            tab = Negotiation;
            ContentVersion++;
        }

        ContractTerms DraftTerms(TradeSystem t)
        {
            var goods = GoodsCatalog.All;
            var g = goods[UnityEngine.Mathf.Clamp(draftGood, 0, goods.Count - 1)];
            double price = t.WorldPrice(g) * draftPriceFactor;
            return new ContractTerms
            {
                Kind = ContractKind.Supply, Good = g, UnitsPerWeek = draftUnits, Weeks = draftWeeks, PriceMode = draftMode,
                Price = draftMode == PriceMode.Fixed ? System.Math.Round(price, 2) : System.Math.Round(draftPriceFactor, 3), PenaltyShare = draftPenalty,
            };
        }

        void LoadDraft(TradeSystem t, ContractTerms s)
        {
            draftUnits = s.UnitsPerWeek;
            draftWeeks = s.Weeks;
            draftPenalty = s.PenaltyShare;
            double world = t.WorldPrice(s.Good);
            draftPriceFactor = s.PriceMode == PriceMode.Fixed ? (world > 0 ? s.Price / world : 1.0) : s.Price;
            ContentVersion++;
        }

        void DrawNegotiation(Ui ui, TradeSystem t)
        {
            var o = draftCounterOf > 0 ? t.Offer(draftCounterOf) : null;
            if (o != null && o.IsOpen && o.To == Player)
            {
                var from = t.Nation(o.From).Country.Name;
                ui.Section($"Gegenangebot an {from}", $"auf #{o.Id} · Runde {o.Round + 1} von höchstens {DiplomacyRules.MaxNegotiationRounds}");
                using (ui.Fields())
                {
                    ui.Field("Ihr Angebot", o.Terms.Describe(), Tone.Muted);
                    var terms = DraftTerms(t);
                    ui.Field("Wir", draftSide == TradeSide.Sell ? $"verkaufen {terms.Good.Name}" : $"kaufen {terms.Good.Name}");
                    ui.Stepper("Menge je Woche", $"{draftUnits:0}", () => draftUnits = System.Math.Max(1, draftUnits - Step(draftUnits)), () => draftUnits += Step(draftUnits));
                    ui.Stepper("Laufzeit", $"{draftWeeks} Wochen", () => draftWeeks = System.Math.Max(TradeRules.MinWeeks, draftWeeks - 1),
                        () => draftWeeks = System.Math.Min(TradeRules.MaxWeeks, draftWeeks + 1));
                    ui.Stepper("Preis", draftMode == PriceMode.Fixed ? $"{t.UnitPrice(terms):0.0} € fest" : $"Welt × {draftPriceFactor:0.00}",
                        () => draftPriceFactor = System.Math.Max(0.3, draftPriceFactor - 0.01), () => draftPriceFactor = System.Math.Min(3.0, draftPriceFactor + 0.01),
                        $"ihr Preis {t.UnitPrice(o.Terms):0.0} €", Tone.Muted);
                    ui.Stepper("Strafe bei Nichtlieferung", $"{draftPenalty * 100:0} %", () => draftPenalty = System.Math.Max(0, draftPenalty - 0.05),
                        () => draftPenalty = System.Math.Min(1, draftPenalty + 0.05));
                    ui.Field("Wert je Woche", Money(t.UnitPrice(terms) * draftUnits));
                }
                // Einschätzung und Vorschlag: dieselbe Utility AI wie bei der Antwort (ohne Zufall)
                var probe = t.PreviewOffer(Player, o.From, draftSide, DraftTerms(t));
                if (probe != null)
                {
                    var ev = t.Evaluate(o.From, probe);
                    ui.Note($"Einschätzung {from}: {(ev.Probability >= 0.5f ? "wahrscheinlich ja" : "eher nein")} ({ev.Probability * 100:0} %) – {ev.Reason}");
                    var suggestion = t.SuggestCounter(o.From, probe);
                    using (ui.Actions())
                    {
                        ui.ActionButton("Gegenangebot senden", () => { Run(new CounterOfferCommand(Player, o.Id, DraftTerms(t))); draftCounterOf = 0; }, ActionStyle.Primary);
                        ui.ActionButton("Vorschlag übernehmen", () => LoadDraft(t, suggestion), ActionStyle.Secondary, false, suggestion != null,
                            suggestion != null ? $"{from} würde wohl annehmen: {suggestion.Describe()}" : "kein Vorschlag (Gegengrund lässt sich nicht über Bedingungen lösen)");
                        ui.ActionButton("Abbrechen", () => { draftCounterOf = 0; ContentVersion++; });
                    }
                }
            }
            else
            {
                draftCounterOf = 0;
                ui.Note("Ein Angebot an uns im Reiter „Angebote“ mit „Gegenangebot …“ öffnen. Lehnen NPC-Länder unsere Angebote ab, " +
                        "machen sie oft selbst ein Gegenangebot (Geduld je Persönlichkeit, höchstens " + DiplomacyRules.MaxNegotiationRounds + " Runden).");
            }

            ui.Section("Verhandlungen", "mit uns, neueste zuerst");
            int shown = 0;
            for (int i = t.Offers.Count - 1; i >= 0 && shown < 8; i--)
            {
                var x = t.Offers[i];
                if (!x.IsCounter || (x.From != Player && x.To != Player)) continue;
                // nur das letzte Glied einer Kette zeigen
                bool hasNext = false;
                foreach (var y in t.Offers) if (y.CounterOf == x.Id) { hasNext = true; break; }
                if (hasNext) continue;
                shown++;
                int other = x.From == Player ? x.To : x.From;
                using (ui.Card(x.IsOpen ? Tone.Info : x.Status == OfferStatus.Accepted ? Tone.Good : Tone.Muted))
                {
                    ui.Text($"{t.Nation(other).Country.Name}: {x.Terms.Good?.Name} · {StatusText(x)}");
                    t.NegotiationChain(x, chainBuffer);
                    using (ui.Fields())
                        foreach (var step in chainBuffer)
                            ui.Field($"Runde {step.Round}: {(step.From == Player ? "wir" : t.Nation(step.From).Country.Name)}",
                                $"{step.Terms.UnitsPerWeek:0.#}/Woche · {t.UnitPrice(step.Terms):0.0} € · {step.Terms.Weeks} W.", step.From == Player ? Tone.None : Tone.Info);
                    if (x.IsOpen && x.To == Player)
                    {
                        var open = x;
                        using (ui.Actions())
                        {
                            ui.ActionButton("Annehmen", () => Run(new AcceptOfferCommand(Player, open.Id)), ActionStyle.Primary);
                            ui.ActionButton("Gegenangebot …", () => StartCounter(t, open), ActionStyle.Secondary, false, open.Round < DiplomacyRules.MaxNegotiationRounds);
                        }
                    }
                }
            }
            if (shown == 0) ui.Note("Noch keine Verhandlungen.");
        }

        // ------------------------------------------------------------ Bündnisse

        void DrawBlocs(Ui ui, TradeSystem t)
        {
            var mine = t.BlocOf(Player);
            // Einladungen an uns
            foreach (var inv in t.Invitations)
            {
                if (!inv.IsOpen || inv.To != Player) continue;
                var b = t.Bloc(inv.BlocId);
                if (b == null) continue;
                using (ui.Card(Tone.Accent))
                {
                    ui.Text($"Einladung: {t.Nation(inv.From).Country.Name} lädt uns in {b.Name} ein ({b.KindText})");
                    ui.Note(BlocEffect(b));
                    var id = inv.Id;
                    using (ui.Actions())
                    {
                        ui.ActionButton("Beitreten", () => Run(new AnswerInvitationCommand(Player, id, true)), ActionStyle.Primary, false, mine == null,
                            mine != null ? $"erst aus {mine.Name} austreten" : null);
                        ui.ActionButton("Ablehnen", () => Run(new AnswerInvitationCommand(Player, id, false, "vom Spieler abgelehnt")), ActionStyle.Danger);
                    }
                }
            }

            if (mine != null)
            {
                ui.Section($"Unser Bündnis: {mine.Name}", $"{mine.KindText}{(mine.Active ? "" : " · im Aufbau (wirksam ab 2 Mitgliedern)")} · seit Tag {mine.FoundedDay}");
                using (ui.Fields())
                {
                    ui.Field("Vorsitz", mine.Founder == Player ? "wir" : t.Nation(mine.Founder).Country.Name);
                    foreach (int m in mine.Members)
                        if (m != Player) ui.Field("Mitglied", $"{t.Nation(m).Country.Name} · Beziehung {t.Relation(Player, m):+0;−0}", RelationTone(t.Relation(Player, m)));
                    if (mine.Kind == BlocKind.CustomsUnion)
                    {
                        double ext = mine.ExternalTariff;
                        if (mine.Founder == Player)
                            ui.Stepper("Gemeinsamer Außenzoll", $"{ext * 100:0} %",
                                () => Run(new SetBlocTariffCommand(Player, mine.Id, System.Math.Max(0, ext - 0.01))),
                                () => Run(new SetBlocTariffCommand(Player, mine.Id, System.Math.Min(TradeRules.MaxTariff, ext + 0.01))), "gilt für alle Mitglieder", Tone.Muted);
                        else ui.Field("Gemeinsamer Außenzoll", $"{ext * 100:0} % (setzt der Vorsitz)");
                    }
                    foreach (int s in mine.Sanctioned) ui.Field("Sanktionen gegen", t.Nation(s).Country.Name, Tone.Bad);
                }
                using (ui.Actions())
                {
                    int sel = selected;
                    bool selOk = sel >= 0 && sel != Player && sel < t.Nations.Count && !mine.IsMember(sel);
                    string selName = selOk ? t.Nation(sel).Country.Name : "Land im Reiter „Länder“ wählen";
                    ui.ActionButton($"Einladen: {selName}", () => Run(new InviteToBlocCommand(Player, mine.Id, sel)), ActionStyle.Secondary, false, selOk);
                    bool sanctioned = selOk && mine.Sanctions(sel);
                    ui.ActionButton(sanctioned ? $"Sanktionen aufheben: {selName}" : $"Sanktionen beantragen: {selName}",
                        () => Run(new ProposeVoteCommand(Player, mine.Id, sanctioned ? VoteKind.LiftSanction : VoteKind.Sanction, sel)), ActionStyle.Secondary, false,
                        selOk && mine.Active, "Abstimmung der Mitglieder (einfache Mehrheit): Zoll 50 %, kein neuer Handel, laufende Verträge enden");
                    ui.ActionButton("Austreten", () => Run(new LeaveBlocCommand(Player, mine.Id)), ActionStyle.Danger, false, true, "Beziehung zu allen Mitgliedern sinkt");
                }
            }

            // Abstimmungen, die uns betreffen oder in denen wir stimmen
            bool header = false;
            foreach (var v in t.Votes)
            {
                var b = t.Bloc(v.BlocId);
                if (b == null) continue;
                bool member = b.IsMember(Player);
                if (!member && v.Subject != Player) continue;
                if (!v.IsOpen && t.Day - v.CloseDay > 14) continue;
                if (!header) { ui.Section("Abstimmungen", "offen und zuletzt entschieden"); header = true; }
                using (ui.Card(v.IsOpen ? Tone.Info : v.Status == VoteStatus.Passed ? Tone.Good : Tone.Muted))
                {
                    ui.Text($"{b.Name}: {v.KindText} – {t.Nation(v.Subject).Country.Name} (Antrag {(v.Proposer == Player ? "von uns" : t.Nation(v.Proposer).Country.Name)})");
                    ui.Note(v.IsOpen ? $"offen bis Tag {v.CloseDay} · {v.Count(VoteChoice.Yes)} Ja, {v.Count(VoteChoice.No)} Nein" : v.Result);
                    if (v.IsOpen && member && v.Subject != Player && !v.Ballots.ContainsKey(Player))
                    {
                        int id = v.Id;
                        using (ui.Actions())
                        {
                            ui.ActionButton("Ja", () => Run(new CastVoteCommand(Player, id, VoteChoice.Yes)), ActionStyle.Primary);
                            ui.ActionButton("Nein", () => Run(new CastVoteCommand(Player, id, VoteChoice.No)), ActionStyle.Danger);
                            ui.ActionButton("Enthaltung", () => Run(new CastVoteCommand(Player, id, VoteChoice.Abstain)));
                        }
                    }
                }
            }

            ui.Section("Bündnisse der Welt", "Handelsbündnis: zollfrei untereinander · Zollunion: zusätzlich gemeinsamer Außenzoll");
            foreach (var b in t.Blocs)
            {
                if (b.Dissolved) continue;
                var names = new System.Text.StringBuilder();
                foreach (int m in b.Members) { if (names.Length > 0) names.Append(", "); names.Append(m == Player ? "wir" : t.Nation(m).Country.Name); }
                using (ui.Card(b.IsMember(Player) ? Tone.Good : Tone.None))
                {
                    ui.Text($"{b.Name} · {b.KindText}{(b.Kind == BlocKind.CustomsUnion ? $" · Außenzoll {b.ExternalTariff * 100:0} %" : "")}");
                    ui.Note(names.ToString() + (b.Sanctioned.Count > 0 ? $" · Sanktionen gegen {b.Sanctioned.Count} Land/Länder" : ""));
                    if (mine == null && b.Active)
                    {
                        int id = b.Id;
                        ui.ActionButton("Aufnahme beantragen", () => Run(new ApplyToBlocCommand(Player, id)), ActionStyle.Secondary, false, !b.Sanctions(Player),
                            "Abstimmung der Mitglieder (Zweidrittelmehrheit) – Beziehung und Vertrauen zählen");
                    }
                }
            }

            if (mine == null)
            {
                ui.Section("Bündnis gründen", "mit dem im Reiter „Länder“ gewählten Land");
                int sel = selected;
                bool ok = sel >= 0 && sel != Player && sel < t.Nations.Count && t.BlocOf(sel) == null;
                using (ui.Fields())
                {
                    ui.Stepper("Art", draftBlocKind == BlocKind.CustomsUnion ? "Zollunion" : "Handelsbündnis",
                        () => draftBlocKind = draftBlocKind == BlocKind.CustomsUnion ? BlocKind.TradeAlliance : BlocKind.CustomsUnion,
                        () => draftBlocKind = draftBlocKind == BlocKind.CustomsUnion ? BlocKind.TradeAlliance : BlocKind.CustomsUnion);
                    ui.Field("Partner", ok ? t.Nation(sel).Country.Name : sel >= 0 && sel < t.Nations.Count && sel != Player ? "schon in einem Bündnis" : "Land wählen", ok ? Tone.None : Tone.Muted);
                }
                ui.ActionButton("Gründen und einladen", () => Run(new FoundBlocCommand(Player, draftBlocKind, null, sel)), ActionStyle.Primary, false, ok);
            }
        }

        static string BlocEffect(TradeBloc b) => b.Kind == BlocKind.CustomsUnion
            ? $"Zollfrei mit den Mitgliedern; unser Grundzoll wird durch den gemeinsamen Außenzoll ({b.ExternalTariff * 100:0} %) ersetzt."
            : "Zollfrei mit den Mitgliedern; eigene Zölle gegen Dritte bleiben.";

        // ------------------------------------------------------------ Diplomatie

        void DrawDiplomacy(Ui ui, TradeSystem t)
        {
            int sel = selected;
            if (sel < 0 || sel == Player || sel >= t.Nations.Count)
            {
                ui.Note("Land im Reiter „Länder“ wählen.");
                return;
            }
            var n = t.Nation(sel);
            float rel = t.Relation(Player, sel);
            ui.Section(n.Country.Name, $"{Governments.DisplayName(n.Government)} · {n.Personality.Describe()}");
            using (ui.Grid(3))
            {
                ui.Stat("Beziehung", $"{rel:+0;−0}", null, RelationTone(rel));
                ui.Stat("Vertrauen in uns", $"{n.Trust[Player] * 100:0}", "%");
                ui.Stat("Zoll gegen uns", $"{t.TariffRate(sel, Player) * 100:0}", "%");
            }
            using (ui.Fields())
            {
                ui.Stepper("Geschenk", Money(draftGift), () => draftGift = System.Math.Max(DiplomacyRules.MinGift, draftGift / 2),
                    () => draftGift = System.Math.Min(5e6, draftGift * 2), "aus der Stadtkasse", Tone.Muted);
            }
            int last = t.LastDiplomacyDay(Player, sel);
            bool cooled = t.Day - last >= DiplomacyRules.DiplomacyCooldownDays;
            string wait = cooled ? null : $"erst wieder ab Tag {last + DiplomacyRules.DiplomacyCooldownDays}";
            using (ui.Actions())
            {
                double amount = draftGift;
                ui.ActionButton("Schenken", () => Run(new DiplomacyCommand(Player, sel, DiplomacyAction.Gift, amount)), ActionStyle.Primary, false, true,
                    "Beziehung steigt (mehrere Geschenke in 30 Tagen wirken schwächer), Vertrauen +1");
                ui.ActionButton("Protest gegen Zölle", () => Run(new DiplomacyCommand(Player, sel, DiplomacyAction.Protest)), ActionStyle.Secondary, false, cooled,
                    wait ?? "Beziehung −3; manchmal hebt das Land seinen Sonderzoll gegen uns auf");
                ui.ActionButton("Drohen", () => Run(new DiplomacyCommand(Player, sel, DiplomacyAction.Threat)), ActionStyle.Danger, false, cooled,
                    wait ?? "Beziehung −8, Vertrauen −5; Erfolg nach Macht (Einwohner) – sonst droht ein Gegenzoll");
            }
            var k = World.Economy;
            if (k != null) ui.Field("Diplomatie diese Woche", "−" + Money(k.CurrentWeek.Amount(BudgetCategory.Diplomacy)), Tone.Muted);
            bool any = false;
            foreach (var m in n.Memory)
            {
                if (m.Partner != Player) continue;
                if (!any) { ui.Section("Gedächtnis", "was sie über uns denken"); any = true; }
                ui.Text($"Tag {m.Day}: {m.Text("Wir")}", m.Kind == TradeMemoryKind.Threat || m.Kind == TradeMemoryKind.Sanction || m.Kind == TradeMemoryKind.Breach ? Tone.Warn : Tone.None);
            }
        }

        // ------------------------------------------------------------ Tourismus

        void DrawTourism(Ui ui, TradeSystem t)
        {
            ui.Section("Besucher der Stadt", "gestern, aus dem Tourismus-Handel der Länder");
            using (ui.Grid(3))
            {
                ui.Stat("Besucher/Tag", $"{t.PlayerVisitorsYesterday:0.0}");
                ui.Stat("Potenzial", $"{t.PlayerTourismPotentialYesterday:0.0}", "/Tag");
                ui.Stat("Marktfaktor", $"{t.PlayerTourismMarket:0.00}", "×", t.PlayerTourismMarket < 0.8 ? Tone.Warn : t.PlayerTourismMarket > 1.1 ? Tone.Good : Tone.None);
            }
            using (ui.Fields())
            {
                ui.Field("Straße", $"{t.VisitorsByMode(TourismMode.Road):0.0}");
                ui.Field("Bahn", $"{t.VisitorsByMode(TourismMode.Rail):0.0}");
                ui.Field("Schiff", $"{t.VisitorsByMode(TourismMode.Ship):0.0}");
                ui.Field("Flugzeug", $"{t.VisitorsByMode(TourismMode.Air):0.0}");
                double lost = t.VisitorsByMode(TourismMode.Lost);
                ui.Field("ohne passende Anreise", $"{lost:0.0}", lost > 0.5 ? Tone.Warn : Tone.Muted);
                if (World.Aviation != null) ui.Field("Besucher in der Stadt", $"{World.Aviation.VisitorsPresent:0}");
            }
            ui.Note("Potenzial = Tourismus-Stärke × Attraktivität (Freizeit, Wahrzeichen) × Stadtgröße × Umwelt. Marktfaktor = Beziehungen, " +
                    "Preise und Bündnisse der Herkunftsländer. Bis 1 500 km über Land kommen Gäste per Bahn (Personenbahnhof) oder Straße, " +
                    "weiter nur per Flugzeug (Flughafen) – Übersee ohne Flughafen halb per Schiff (Hafen).");

            ui.Section("Herkunft", "Besucher gestern je Land");
            var list = new List<(int id, double v)>();
            foreach (var n in t.Nations) if (!n.IsPlayer && t.PlayerVisitorsFrom(n.Id) > 0.01) list.Add((n.Id, t.PlayerVisitorsFrom(n.Id)));
            list.Sort((a, b) => b.v.CompareTo(a.v));
            using (ui.Card())
            {
                if (list.Count == 0) ui.Note("noch keine Besucher");
                for (int i = 0; i < list.Count && i < 8; i++)
                    ui.Columns(t.Nation(list[i].id).Country.Name, $"{list[i].v:0.0}/Tag", t.Geography.Route(list[i].id, Player)?.ModeText ?? "");
            }

            ui.Section("Tourismus weltweit", "Einreisen gestern (Kategorie-Einheiten) · Wert seit Beginn");
            var dest = new List<NationTrade>(t.Nations);
            dest.Sort((a, b) => b.TourismInYesterday.CompareTo(a.TourismInYesterday));
            using (ui.Card())
            {
                ui.Columns("Land", "Einreise/Tag", "Export seit Beginn", header: true);
                for (int i = 0; i < dest.Count && i < 8; i++)
                    ui.Columns(dest[i].IsPlayer ? "wir" : dest[i].Country.Name, $"{dest[i].TourismInYesterday:N0}", Money(dest[i].TourismExportValue));
            }
        }
    }
}
