using System.Collections.Generic;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Trade;
using CityBuilder.Simulation.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Etappe 8 Handel &amp; Politik (mittiges Fenster, <b>F11</b> oder Knopf in der oberen Leiste, „Handel“ in der Weltkarte):
    /// Reiter Länder (Beziehung, Stärken, Persönlichkeit, Vertrauen, Handelsweg, Zölle, Gedächtnis), Weltpreise je Gut,
    /// Angebote (NPC-Angebote annehmen/ablehnen, eigene Angebote mit Antwort + Begründung, neues Angebot erstellen),
    /// Verträge (Lieferstatus, Ware unterwegs, Grenzlager, Kündigen) und Zölle (Grundzoll, Satz je Land, Einnahmen).
    /// Alle Aktionen gehen als <see cref="TradeCommand"/> an das <see cref="TradeSystem"/> – dieselben Klassen wie bei der NPC-KI.
    /// </summary>
    public sealed class TradeWindow : GameWindow
    {
        static readonly string[] TabNames = { "Länder", "Weltpreise", "Angebote", "Verträge", "Zölle" };
        const int Countries = 0, Prices = 1, Offers = 2, Contracts = 3, Tariffs = 4;

        /// <summary>Zuletzt angemeldetes Handelsfenster (für den Knopf „Handel“ in der Weltkarte).</summary>
        public static TradeWindow Current { get; private set; }

        bool open;
        int tab;
        int selected = -1;
        readonly List<TradeOffer> offerBuffer = new List<TradeOffer>();
        readonly List<TradeContract> contractBuffer = new List<TradeContract>();

        // Entwurf „eigenes Angebot“
        int draftGood;
        TradeSide draftSide = TradeSide.Sell;
        int draftTarget = -1;
        double draftUnits = 20;
        int draftWeeks = 4;
        double draftPriceFactor = 1.0;
        PriceMode draftMode = PriceMode.Fixed;
        double draftPenalty = TradeRules.DefaultPenaltyShare;
        string feedback;
        bool feedbackOk;

        public TradeWindow() : base(WindowSlot.Center) { Current = this; }

        TradeSystem Trade => World?.Trade;

        public override float OpenScore => open && Trade != null ? 1f : 0f;
        public override void Close() => open = false;
        public bool IsOpen => open && Trade != null;
        public void Toggle() => open = !open;

        /// <summary>Fenster öffnen und ein Land auswählen (z. B. aus der Weltkarte).</summary>
        public void OpenFor(int country)
        {
            open = true;
            selected = country;
            draftTarget = country;
            tab = Countries;
            ContentVersion++;
        }

        public override void Poll()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f11Key.wasPressedThisFrame) open = !open;
        }

        int Player => Trade.PlayerId;

        public override void DrawHead(Ui ui)
        {
            var t = Trade;
            var me = t.Nation(Player);
            int toMe = 0;
            foreach (var o in t.Offers) if (o.IsOpen && o.To == Player) toMe++;
            int active = 0;
            foreach (var c in t.Contracts) if (c.IsActive && c.Involves(Player)) active++;
            Head(ui, "Handel (F11)", toMe > 0 ? Tone.Accent : Tone.Info, me != null ? $"Handel · {me.Country.Name}" : "Handel",
                $"Tag {t.Day} · {active} Verträge · {toMe} neue Angebote · Grundzoll {(me != null ? me.Tariffs.General * 100 : 0):0} %");
            ui.Tabs(TabNames, tab, i => { tab = i; ContentVersion++; });
        }

        public override void DrawBody(Ui ui)
        {
            var t = Trade;
            if (Player < 0)
            {
                ui.Message("Kein Spielerland", "Ohne Startregion gibt es keinen Handel.", Tone.Accent);
                return;
            }
            if (!string.IsNullOrEmpty(feedback)) ui.Message("Handel", feedback, feedbackOk ? Tone.Good : Tone.Warn);
            switch (tab)
            {
                case Countries: DrawCountries(ui, t); break;
                case Prices: DrawPrices(ui, t); break;
                case Offers: DrawOffers(ui, t); break;
                case Contracts: DrawContracts(ui, t); break;
                default: DrawTariffs(ui, t); break;
            }
        }

        void Run(TradeCommand cmd)
        {
            // Sofort ausführen (auch bei Pause) – protokolliert wie ein eingereihtes Command
            var r = Trade.Execute(cmd);
            feedback = r.Message;
            feedbackOk = r.Ok;
        }

        // ------------------------------------------------------------ Länder

        void DrawCountries(Ui ui, TradeSystem t)
        {
            var order = new List<NationTrade>();
            foreach (var n in t.Nations) if (!n.IsPlayer) order.Add(n);
            order.Sort((a, b) => t.Relation(Player, b.Id).CompareTo(t.Relation(Player, a.Id)));
            if (selected >= 0 && selected != Player && selected < t.Nations.Count) DrawCountry(ui, t, t.Nations[selected]);

            ui.Section("Länder", "Beziehung zu uns · Stärken · Persönlichkeit");
            foreach (var n in order)
            {
                var c = n.Country;
                float rel = t.Relation(Player, n.Id);
                var route = t.Geography.Route(n.Id, Player);
                string sub = $"★ {CountryProfile.DisplayName(c.Profile.Primary)} · ☆ {CountryProfile.DisplayName(c.Profile.Secondary)} · " +
                             $"{n.Personality.Describe()} · {route?.ModeText} {route?.LeadDays} T." + (t.HasAgreement(Player, n.Id) ? " · Abkommen" : "");
                int id = n.Id;
                ui.ListItem(Ui.Initials(c.Name), ColorOf(c.Color), c.Name, sub, $"{rel:+0;−0}", RelationTone(rel), () => { selected = id; draftTarget = id; ContentVersion++; });
            }
        }

        void DrawCountry(Ui ui, TradeSystem t, NationTrade n)
        {
            var c = n.Country;
            float rel = t.Relation(Player, n.Id);
            var route = t.Geography.Route(n.Id, Player);
            ui.Section(c.Name, $"Hauptstadt {c.CapitalName}");
            using (ui.Grid(3))
            {
                ui.Stat("Beziehung", $"{rel:+0;−0}", null, RelationTone(rel));
                ui.Stat("Vertrauen in uns", $"{n.Trust[Player] * 100:0}", "%", n.Trust[Player] < 0.45f ? Tone.Bad : Tone.None);
                ui.Stat("Lieferzeit", route != null ? route.LeadDays.ToString() : "–", "Tage");
            }
            using (ui.Fields())
            {
                ui.Field("Persönlichkeit", n.Personality.Describe());
                ui.Field("Stärken", $"{CountryProfile.DisplayName(c.Profile.Primary)} / {CountryProfile.DisplayName(c.Profile.Secondary)}");
                if (route != null) ui.Field("Handelsweg", route.ToString());
                ui.Field("Zoll gegen uns", $"{t.TariffRate(n.Id, Player) * 100:0} %", t.TariffRate(n.Id, Player) > 0.1 ? Tone.Warn : Tone.None);
                ui.Field("Unser Zoll", $"{t.TariffRate(Player, n.Id) * 100:0} %");
                ui.Field("Handelsabkommen", t.HasAgreement(Player, n.Id) ? "ja (zollfrei)" : "nein", t.HasAgreement(Player, n.Id) ? Tone.Good : Tone.None);
            }
            ui.Section("Lage je Kategorie", "Reichweite Lager · Defizit/Überschuss");
            using (ui.Card())
            {
                ui.Columns("Kategorie", "Reichweite", "Bedarf / Überschuss", header: true);
                for (int k = 0; k < GoodsCategories.Count; k++)
                {
                    var cat = (GoodsCategory)k;
                    if (!TradeGoods.IsTradeable(cat)) continue;
                    double need = t.Need(n.Id, cat), sur = t.Surplus(n.Id, cat);
                    ui.Columns(GoodsCategories.DisplayName(cat), $"{t.Cover(n.Id, cat):0} T.", need > 0.05 ? $"braucht ({need:0.00})" : sur > 0.05 ? $"Überschuss ({sur:0.00})" : "–",
                        Tone.None, need > 0.3 ? Tone.Warn : sur > 0.3 ? Tone.Good : Tone.Muted);
                }
            }
            bool memories = false;
            foreach (var m in n.Memory)
            {
                if (m.Partner != Player) continue;
                if (!memories) { ui.Section("Gedächtnis", "was sie über uns denken"); memories = true; }
                ui.Text($"Tag {m.Day}: {m.Text("Wir")}", m.Kind == TradeMemoryKind.Breach || m.Kind == TradeMemoryKind.Cancelled || m.Kind == TradeMemoryKind.Tariff ? Tone.Warn : Tone.None);
            }
            int id = n.Id;
            using (ui.Actions())
            {
                ui.ActionButton("Angebot erstellen", () => { draftTarget = id; tab = Offers; ContentVersion++; }, ActionStyle.Primary);
                ui.ActionButton(t.HasAgreement(Player, id) ? "Abkommen besteht" : "Handelsabkommen anbieten",
                    () => Run(new ProposeContractCommand(Player, id, TradeSide.Sell,
                        new ContractTerms { Kind = ContractKind.Agreement, Weeks = TradeRules.AgreementWeeks })),
                    ActionStyle.Secondary, false, !t.HasAgreement(Player, id), "Gegenseitige Zollfreiheit für 26 Wochen");
            }
        }

        // ------------------------------------------------------------ Weltpreise

        void DrawPrices(Ui ui, TradeSystem t)
        {
            var goods = World.Goods;
            var depot = t.City;
            GoodsCategory? cat = null;
            foreach (var g in GoodsCatalog.All)
            {
                var c = TradeGoods.CategoryOf(g);
                if (cat != c)
                {
                    cat = c;
                    ui.Section(GoodsCategories.DisplayName(c));
                    ui.Columns("Gut", "Welt € (Woche)", "Stadt lokal · Grenzlager", header: true);
                }
                double w = t.WorldPrice(g), prev = t.WorldPriceWeekAgo(g);
                double trend = prev > 0 ? w / prev - 1.0 : 0.0;
                string local = goods != null ? $"{goods.Market(g).LocalPrice:0.0} €" : "–";
                string stock = depot != null && (depot.ImportStock[g.Index] > 0.05 || depot.ExportStock[g.Index] > 0.05)
                    ? $" · ↓{depot.ImportStock[g.Index]:0} ↑{depot.ExportStock[g.Index]:0}" : "";
                ui.Columns(g.Name, $"{w:0.0} ({(trend >= 0 ? "+" : "−")}{System.Math.Abs(trend) * 100:0.0} %)", local + stock,
                    trend > 0.02 ? Tone.Warn : trend < -0.02 ? Tone.Good : Tone.None, Tone.Muted);
            }
            ui.Note("Weltpreis = handelsgewichtetes Mittel der Länderpreise (Angebot/Nachfrage der Länder). Die Stadt sieht ihn " +
                    "an den Außenanschlüssen (begrenzt auf 0,5–2 × Grundpreis). ↓ Vertragsware im Grenzlager, ↑ Exportware für Verträge.");
        }

        // ------------------------------------------------------------ Angebote

        void DrawOffers(Ui ui, TradeSystem t)
        {
            t.OffersTo(Player, offerBuffer);
            ui.Section("Angebote an uns", offerBuffer.Count == 0 ? "keine offenen" : $"{offerBuffer.Count} offen");
            foreach (var o in offerBuffer)
            {
                var from = t.Nation(o.From);
                using (ui.Card(Tone.Info))
                {
                    string what = o.Terms.Kind == ContractKind.Agreement ? "bietet ein Handelsabkommen an"
                        : o.Side == TradeSide.Sell ? "will uns verkaufen" : "will von uns kaufen";
                    ui.Text($"{from.Country.Name} {what}");
                    using (ui.Fields())
                    {
                        ui.Field("Bedingungen", o.Terms.Describe());
                        if (o.Terms.Good != null)
                        {
                            double unit = t.UnitPrice(o.Terms), world = t.WorldPrice(o.Terms.Good);
                            ui.Field("Preis heute · Weltpreis", $"{unit:0.0} € · {world:0.0} €",
                                (o.Side == TradeSide.Sell) == (unit < world) ? Tone.Good : Tone.Warn);
                            ui.Field("Wert je Woche", Money(unit * o.Terms.UnitsPerWeek));
                            var route = t.Geography.Route(o.Side == TradeSide.Sell ? o.From : Player, o.Side == TradeSide.Sell ? Player : o.From);
                            if (route != null) ui.Field("Lieferzeit", $"{route.LeadDays} Tage ({route.ModeText})");
                        }
                        ui.Field("gültig bis", $"Tag {o.ExpiresDay}");
                    }
                    int id = o.Id;
                    using (ui.Actions())
                    {
                        ui.ActionButton("Annehmen", () => Run(new AcceptOfferCommand(Player, id)), ActionStyle.Primary);
                        ui.ActionButton("Ablehnen", () => Run(new RejectOfferCommand(Player, id, "vom Spieler abgelehnt")), ActionStyle.Danger);
                    }
                }
            }

            ui.Section("Unsere Angebote", "offen bzw. Antwort mit Begründung");
            bool any = false;
            for (int i = t.Offers.Count - 1; i >= 0; i--)
            {
                var o = t.Offers[i];
                if (o.From != Player) continue;
                any = true;
                string to = o.To < 0 ? "alle" : t.Nation(o.To).Country.Name;
                Tone tone = o.Status == OfferStatus.Accepted ? Tone.Good : o.Status == OfferStatus.Rejected ? Tone.Bad : o.Status == OfferStatus.Open ? Tone.Info : Tone.Muted;
                ui.Field($"#{o.Id} → {to}: {(o.Side == TradeSide.Sell ? "verkaufen" : "kaufen")} {o.Terms.Describe()}", StatusText(o), tone);
                if (!string.IsNullOrEmpty(o.Reason) && o.Status != OfferStatus.Open) ui.Note(o.Reason);
                if (o.IsOpen)
                {
                    int id = o.Id;
                    ui.ActionButton("Zurückziehen", () => Run(new WithdrawOfferCommand(Player, id)));
                }
            }
            if (!any) ui.Note("Noch keine eigenen Angebote.");

            DrawDraft(ui, t);
        }

        static string StatusText(TradeOffer o)
        {
            switch (o.Status)
            {
                case OfferStatus.Open: return $"offen bis Tag {o.ExpiresDay}";
                case OfferStatus.Accepted: return $"angenommen → Vertrag #{o.ContractId}";
                case OfferStatus.Rejected: return "abgelehnt";
                case OfferStatus.Withdrawn: return "zurückgezogen";
                default: return "abgelaufen";
            }
        }

        void DrawDraft(Ui ui, TradeSystem t)
        {
            var goods = GoodsCatalog.All;
            draftGood = Mathf.Clamp(draftGood, 0, goods.Count - 1);
            var g = goods[draftGood];
            double world = t.WorldPrice(g);
            double price = world * draftPriceFactor;
            ui.Section("Neues Angebot", "geht als Command an das Zielland; Antwort am nächsten Handelstag");
            using (ui.Fields())
            {
                ui.Stepper("Gut", g.Name, () => draftGood = (draftGood + goods.Count - 1) % goods.Count, () => draftGood = (draftGood + 1) % goods.Count,
                    GoodsCategories.DisplayName(TradeGoods.CategoryOf(g)), Tone.Muted);
                ui.Stepper("Wir wollen", draftSide == TradeSide.Sell ? "verkaufen" : "kaufen",
                    () => draftSide = draftSide == TradeSide.Sell ? TradeSide.Buy : TradeSide.Sell,
                    () => draftSide = draftSide == TradeSide.Sell ? TradeSide.Buy : TradeSide.Sell);
                ui.Stepper("An", draftTarget < 0 ? "alle Länder" : t.Nation(draftTarget).Country.Name,
                    () => draftTarget = NextTarget(t, draftTarget, -1), () => draftTarget = NextTarget(t, draftTarget, +1));
                ui.Stepper("Menge je Woche", $"{draftUnits:0}", () => draftUnits = System.Math.Max(5, draftUnits - Step(draftUnits)), () => draftUnits += Step(draftUnits));
                ui.Stepper("Laufzeit", $"{draftWeeks} Wochen", () => draftWeeks = System.Math.Max(TradeRules.MinWeeks, draftWeeks - 1),
                    () => draftWeeks = System.Math.Min(TradeRules.MaxWeeks, draftWeeks + 1));
                ui.Stepper("Preis", draftMode == PriceMode.Fixed ? $"{price:0.0} € fest" : $"Welt × {draftPriceFactor:0.00}",
                    () => draftPriceFactor = System.Math.Max(0.3, draftPriceFactor - 0.02), () => draftPriceFactor = System.Math.Min(3.0, draftPriceFactor + 0.02),
                    $"{(draftPriceFactor - 1) * 100:+0;−0;0} % zum Weltpreis", draftPriceFactor > 1.0 == (draftSide == TradeSide.Sell) ? Tone.Good : Tone.Warn);
                ui.Stepper("Preisart", draftMode == PriceMode.Fixed ? "fest" : "an Weltpreis gekoppelt",
                    () => draftMode = draftMode == PriceMode.Fixed ? PriceMode.WorldIndexed : PriceMode.Fixed,
                    () => draftMode = draftMode == PriceMode.Fixed ? PriceMode.WorldIndexed : PriceMode.Fixed);
                ui.Stepper("Strafe bei Nichtlieferung", $"{draftPenalty * 100:0} %", () => draftPenalty = System.Math.Max(0, draftPenalty - 0.05),
                    () => draftPenalty = System.Math.Min(1, draftPenalty + 0.05), "des fehlenden Werts", Tone.Muted);
                ui.Field("Wert je Woche", Money(price * draftUnits));
                if (draftSide == TradeSide.Sell && World.Goods != null)
                {
                    var m = World.Goods.Market(g);
                    ui.Field("Stadt: Produktion · Export gestern", $"{m.SupplyPerDay * 7:0}/Woche · {m.Yesterday.Exported:0.#}", Tone.Muted);
                }
            }
            if (draftTarget >= 0 && draftTarget != Player)
            {
                // Vorschau der NPC-Bewertung (ohne Zufall): gleiche Utility AI wie bei der echten Antwort
                var ev = PreviewEvaluation(t, g, price);
                if (ev.HasValue)
                    ui.Note($"Einschätzung {t.Nation(draftTarget).Country.Name}: {(ev.Value.Probability >= 0.5f ? "wahrscheinlich ja" : "eher nein")} " +
                            $"({ev.Value.Probability * 100:0} %) – {ev.Value.Reason}");
            }
            using (ui.Actions())
                ui.ActionButton("Angebot senden", () => Run(new ProposeContractCommand(Player, draftTarget, draftSide, new ContractTerms
                {
                    Kind = ContractKind.Supply, Good = g, UnitsPerWeek = draftUnits, Weeks = draftWeeks, PriceMode = draftMode,
                    Price = draftMode == PriceMode.Fixed ? System.Math.Round(price, 2) : System.Math.Round(draftPriceFactor, 3), PenaltyShare = draftPenalty,
                })), ActionStyle.Primary);
        }

        TradeSystem.OfferEvaluation? PreviewEvaluation(TradeSystem t, GoodType g, double price)
        {
            var o = t.PreviewOffer(Player, draftTarget, draftSide, new ContractTerms
            {
                Good = g, UnitsPerWeek = draftUnits, Weeks = draftWeeks, PriceMode = draftMode,
                Price = draftMode == PriceMode.Fixed ? price : draftPriceFactor, PenaltyShare = draftPenalty,
            });
            return o == null ? (TradeSystem.OfferEvaluation?)null : t.Evaluate(draftTarget, o);
        }

        int NextTarget(TradeSystem t, int current, int dir)
        {
            int n = t.Nations.Count;
            int i = current;
            for (int k = 0; k <= n; k++)
            {
                i += dir;
                if (i < -1) i = n - 1;
                if (i >= n) i = -1;
                if (i == -1 || i != Player) return i;
            }
            return -1;
        }

        static double Step(double v) => v < 50 ? 5 : v < 200 ? 10 : v < 1000 ? 50 : 100;

        // ------------------------------------------------------------ Verträge

        void DrawContracts(Ui ui, TradeSystem t)
        {
            t.ContractsOf(Player, contractBuffer);
            ui.Section("Laufende Verträge", contractBuffer.Count == 0 ? "keine" : $"{contractBuffer.Count}");
            foreach (var c in contractBuffer)
            {
                int partner = c.Partner(Player);
                string name = t.Nation(partner).Country.Name;
                bool weSell = c.Seller == Player;
                using (ui.Card(c.IsActive ? Tone.None : Tone.Muted))
                {
                    string title = c.Kind == ContractKind.Agreement ? $"#{c.Id} Handelsabkommen mit {name}"
                        : $"#{c.Id} {(weSell ? "Export an" : "Import von")} {name}: {c.Good.Name}";
                    ui.Text(title);
                    using (ui.Fields())
                    {
                        ui.Field("Bedingungen", c.Terms.Describe());
                        if (c.Kind == ContractKind.Supply)
                        {
                            ui.Field("Lieferungen", $"{c.WeeksDispatched}/{c.Terms.Weeks} verschickt · pünktlich {c.WeeksOnTime} · Fehlmenge {c.WeeksShort}",
                                c.WeeksShort > 0 ? Tone.Warn : Tone.None);
                            if (c.IsActive && !c.AllDispatched) ui.Field("Nächster Versand", $"Tag {c.NextDispatchDay}");
                            foreach (var d in c.Deliveries)
                                if (!d.Arrived) ui.Field($"   unterwegs: {d.Units:0.#} E", $"Ankunft Tag {d.ArriveDay} ({c.Route.ModeText})", Tone.Info);
                            ui.Field("Geliefert · Wert", $"{c.UnitsDelivered:0.#} E · {Money(c.ValueDelivered)}");
                            if (c.Penalties > 0) ui.Field(weSell ? "Strafen gezahlt" : "Strafen erhalten", Money(c.Penalties), weSell ? Tone.Bad : Tone.Good);
                            if (weSell && c.IsActive && t.City != null)
                                ui.Field("Grenzlager / noch gebraucht", $"{t.City.ExportStock[c.Good.Index]:0.#} / {t.City.ExportWanted(c.Good):0.#} E",
                                    t.City.ExportWanted(c.Good) > 0.5f && c.NextDispatchDay - t.Day <= 2 ? Tone.Warn : Tone.None);
                        }
                        else ui.Field("Läuft bis", $"Tag {c.PlannedEndDay}");
                    }
                    if (c.IsActive)
                    {
                        int id = c.Id;
                        ui.ActionButton("Kündigen", () => Run(new CancelContractCommand(Player, id)), ActionStyle.Danger, false, true,
                            "Gebühr bis 4 Wochenwerte × Strafanteil × 0,5; Vertrauen und Beziehung sinken");
                    }
                    else ui.Note(c.Status == ContractStatus.Cancelled ? "gekündigt – Ware unterwegs kommt noch an" : "erfüllt");
                }
            }

            if (t.City != null)
            {
                ui.Section("Grenzlager", "Vertragsware am Außenanschluss");
                bool any = false;
                using (ui.Fields())
                    foreach (var g in GoodsCatalog.All)
                    {
                        double i = t.City.ImportStock[g.Index], e = t.City.ExportStock[g.Index];
                        if (i < 0.05 && e < 0.05) continue;
                        any = true;
                        ui.Field(g.Name, $"Import {i:0.#} E ({t.City.ImportUnitPrice(g):0.0} €/E inkl. Zoll) · Export {e:0.#} E");
                    }
                if (!any) ui.Note("leer");
            }

            ui.Section("Beendet", "letzte 10");
            int shown = 0;
            for (int i = t.History.Count - 1; i >= 0 && shown < 10; i--)
            {
                var c = t.History[i];
                if (!c.Involves(Player)) continue;
                shown++;
                ui.Field($"#{c.Id} {t.Nation(c.Partner(Player)).Country.Name} · {c.Terms.Describe()}",
                    c.Status == ContractStatus.Cancelled ? "gekündigt" : c.WeeksShort > 0 ? $"erfüllt, {c.WeeksShort}× Fehlmenge" : "erfüllt",
                    c.WeeksShort > 0 || c.Status == ContractStatus.Cancelled ? Tone.Warn : Tone.Good);
            }
            if (shown == 0) ui.Note("noch keine");
        }

        // ------------------------------------------------------------ Zölle

        void DrawTariffs(Ui ui, TradeSystem t)
        {
            var me = t.Nation(Player);
            double general = me.Tariffs.General;
            ui.Section("Grundzoll", "auf Weltmarkt-Importe und Vertragsware ohne eigenen Satz");
            using (ui.Fields())
                ui.Stepper("Grundzoll", $"{general * 100:0} %",
                    () => Run(new SetTariffCommand(Player, -1, System.Math.Max(0, general - 0.01))),
                    () => Run(new SetTariffCommand(Player, -1, System.Math.Min(TradeRules.MaxTariff, general + 0.01))),
                    general > TradeRules.TariffAnnoyanceFrom ? "verärgert Handelspartner" : "", Tone.Warn);
            ui.Note("Zoll verteuert Importe der Firmen (Weltmarkt: Grundzoll; Vertragsware: Satz gegen das Lieferland) und geht an die " +
                    "Stadtkasse. Über 10 % sinken die Beziehungen wöchentlich; protektionistische Länder antworten mit Gegenzöllen. " +
                    "Handelsabkommen setzen beide Seiten auf 0.");

            var k = World.Economy;
            if (k != null)
            {
                ui.Section("Stadtkasse: Handel", "diese Woche · letzte Woche");
                using (ui.Card())
                {
                    var cats = new[] { BudgetCategory.Tariffs, BudgetCategory.TradeSales, BudgetCategory.PenaltiesReceived, BudgetCategory.TradePurchases, BudgetCategory.PenaltiesPaid };
                    foreach (var c in cats)
                    {
                        bool income = Budget.IsIncome(c);
                        double a = k.CurrentWeek.Amount(c), b = k.LastWeek?.Amount(c) ?? 0;
                        ui.Columns(Budget.DisplayName(c), (income ? "+" : "−") + Money(a), (income ? "+" : "−") + Money(b), income ? Tone.Good : Tone.Warn, Tone.Muted);
                    }
                }
            }

            if (selected >= 0 && selected != Player && selected < t.Nations.Count)
            {
                var n = t.Nations[selected];
                int id = n.Id;
                double rate = me.Tariffs.RateFor(id);
                ui.Section($"Zoll gegen {n.Country.Name}", me.Tariffs.HasOverride(id) ? "eigener Satz" : "Grundzoll gilt");
                using (ui.Fields())
                {
                    ui.Stepper("Satz", $"{rate * 100:0} %",
                        () => Run(new SetTariffCommand(Player, id, System.Math.Max(0, rate - 0.01))),
                        () => Run(new SetTariffCommand(Player, id, System.Math.Min(TradeRules.MaxTariff, rate + 0.01))),
                        t.HasAgreement(Player, id) ? "Abkommen: zollfrei" : $"deren Zoll gegen uns {t.TariffRate(id, Player) * 100:0} %", Tone.Muted);
                }
                if (me.Tariffs.HasOverride(id))
                    ui.ActionButton("Grundzoll verwenden", () => Run(new SetTariffCommand(Player, id, double.NaN)));
            }
            else ui.Note("Land im Reiter „Länder“ wählen, um einen eigenen Satz gegen dieses Land zu setzen.");
        }

        // ------------------------------------------------------------ Hilfen

        static Tone RelationTone(float rel) => rel >= 30f ? Tone.Good : rel <= -20f ? Tone.Bad : rel < 0f ? Tone.Warn : Tone.None;

        static Color ColorOf(uint rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        static string Money(double v) => $"{v:N0} €";
    }
}
