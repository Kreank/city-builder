using System.Collections.Generic;
using CityBuilder.Game.Realm;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Realm;
using CityBuilder.Simulation.Trade;
using CityBuilder.Simulation.World;
using UnityEngine;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Etappe 7 Reich (mittiges Fenster, <b>F12</b>, Fahnen-Knopf oben, „Reich“ in der Weltkarte): Reiter <b>Städte</b> (Kennzahlen
    /// aller Städte – detailliert bzw. aggregiert –, „Zur Stadt wechseln“, Umbenennen, „Stadt gründen“ → Weltkarte), <b>Lieferungen</b>
    /// (interne Liefervereinbarungen mit Weg/Fracht, neue Vereinbarung, Beenden), <b>Land</b> (Landesgründung mit Fortschritt und
    /// Bedingungen, Name/Flagge/Hauptstadt; danach Landeskasse, Zuschüsse, Anerkennung) und <b>Meldungen</b>.
    /// Aktionen gehen als <see cref="RealmCommand"/> an das <see cref="RealmSystem"/>; der Wechsel über <see cref="RealmActions.SwitchTo"/>.
    /// </summary>
    public sealed class RealmWindow : GameWindow
    {
        static readonly string[] TabNames = { "Städte", "Lieferungen", "Land", "Meldungen" };
        const int Cities = 0, Deliveries = 1, Nation = 2, News = 3;

        /// <summary>Flaggenfarben zur Auswahl (keine Farben echter Länder nachgebildet – nur Grundtöne).</summary>
        static readonly uint[] FlagColors = { 0xE8C547, 0x2F6FD0, 0xC8323C, 0x2E9E5B, 0x7A4FB5, 0xE07B26, 0x1FA3A3, 0x5A5A66 };
        static readonly string[] FlagNames = { "Gold", "Blau", "Rot", "Grün", "Violett", "Orange", "Türkis", "Schiefer" };

        /// <summary>Zuletzt angemeldetes Reichsfenster (Weltkarte, obere Leiste).</summary>
        public static RealmWindow Current { get; private set; }

        bool open;
        int tab;
        string renameText;
        int renameFor = -1;
        // Entwurf Lieferung
        int draftFrom = -1, draftTo = -1, draftGood;
        double draftUnits = 20;
        int draftWeeks = 8;
        double draftPriceFactor = 1.0;
        // Entwurf Land
        string nationName = "";
        int flagIndex = 1;
        int capital = -1;

        public RealmWindow() : base(WindowSlot.Center) { Current = this; }

        PlayerRealm Realm => World?.Realm;
        RealmSystem Sys => Realm?.System;

        public override float OpenScore => open && Sys != null ? 1f : 0f;
        public override void Close() => open = false;
        public bool IsOpen => open && Sys != null;
        public void Toggle() => open = !open;
        public void Open(int tabIndex = Cities) { open = true; tab = tabIndex; ContentVersion++; }

        public override void Poll()
        {
            var kb = GameKeyboard.Current;
            if (kb != null && kb.f12Key.wasPressedThisFrame) open = !open;
        }

        public override void DrawHead(Ui ui)
        {
            var realm = Realm;
            string sub = realm.IsNation
                ? $"Land · Hauptstadt {realm.CapitalCity?.Name} · {realm.Cities.Count} Städte · {realm.TotalPopulation:N0} Einwohner"
                : $"Stadtstaat · {realm.Cities.Count} {(realm.Cities.Count == 1 ? "Stadt" : "Städte")} · {realm.TotalPopulation:N0} Einwohner";
            Head(ui, "Reich (F12)", realm.IsNation ? Tone.Good : Tone.Info, realm.DisplayName, sub, chipDot: ColorOf(realm.FlagColor));
            ui.Tabs(TabNames, tab, i => { tab = i; ContentVersion++; });
        }

        public override void DrawBody(Ui ui)
        {
            if (World.Planet == null)
            {
                ui.Message("Keine Weltkarte", "Mehrere Städte und ein eigenes Land gibt es nur mit Weltkarte.", Tone.Accent);
                return;
            }
            switch (tab)
            {
                case Cities: DrawCities(ui); break;
                case Deliveries: DrawDeliveries(ui); break;
                case Nation: DrawNation(ui); break;
                default: DrawNews(ui); break;
            }
        }

        // ------------------------------------------------------------ Städte

        void DrawCities(Ui ui)
        {
            var realm = Realm;
            var p = World.Planet;
            ui.Section("Deine Städte", "eine Stadt läuft detailliert (die angezeigte), die anderen aggregiert");
            foreach (var c in realm.Cities)
            {
                var e = c.Economy;
                bool active = c.Id == realm.ActiveCityId;
                string status = active ? "detailliert" : c.HasSnapshot ? "aggregiert" : "neu gegründet";
                using (ui.Card(active ? Tone.Accent : Tone.None))
                {
                    ui.Text($"{c.Name}{(c.Id == realm.CapitalCityId ? " ★ Hauptstadt" : "")} · {status}", active ? Tone.Accent : Tone.None);
                    using (ui.Grid(3))
                    {
                        ui.Stat("Einwohner", $"{e.Population:N0}", e.HousingCapacity > 0 ? $"/ {e.HousingCapacity:N0} Wohnraum" : null);
                        ui.Stat("Zufriedenheit", $"{e.Satisfaction * 100f:0}", "%", e.Satisfaction >= 0.6f ? Tone.Good : e.Satisfaction >= 0.45f ? Tone.Warn : Tone.Bad);
                        ui.Stat("Kasse", $"{(active && World.Economy != null ? World.Economy.Balance : e.Treasury) / 1000:N0}", "T€", e.Treasury >= 0 ? Tone.None : Tone.Bad);
                    }
                    using (ui.Fields())
                    {
                        ui.Field("Region", c.RegionId >= 0 ? $"{p.Regions[c.RegionId].Name} · gegründet Tag {c.FoundedDay + 1}" : "–");
                        ui.Field("Steuern · Unterhalt je Tag", $"{e.TaxesToday:N0} € · {e.UpkeepToday:N0} €");
                        ui.Field("Anschlüsse", $"{(e.RoadConnected ? "Autobahn ✓" : "Autobahn ✗")} · {(e.HasFreightRail ? "Güterbahn ✓" : "Güterbahn –")} · " +
                                               $"{(e.HasHarbor ? "Hafen ✓" : "Hafen –")} · {(e.HasTownHall ? "Rathaus ✓" : "Rathaus –")}");
                        if (!active && realm.ActiveCity != null)
                        {
                            var route = Sys.Route(realm.ActiveCityId, c.Id);
                            ui.Field($"Weg von {realm.ActiveCity.Name}", route != null ? route.ToString() : "kein Weg", route != null ? Tone.None : Tone.Warn);
                        }
                        if (c.HasSnapshot) ui.Field("Eingefroren", $"seit Tag {c.LastDetailedDay + 1} · {c.SnapshotBytes / 1024:N0} KB");
                    }
                    using (ui.Actions())
                    {
                        int id = c.Id;
                        if (!active)
                            ui.ActionButton("Zur Stadt wechseln", () => { open = false; RealmActions.SwitchTo(Runner, id); }, ActionStyle.Primary, false, true,
                                "Aktuelle Stadt wird gespeichert und läuft aggregiert weiter; diese Stadt wird detailliert geladen bzw. neu erzeugt");
                        ui.ActionButton(renameFor == id ? "Umbenennen abbrechen" : "Umbenennen", () => { renameFor = renameFor == id ? -1 : id; renameText = c.Name; });
                    }
                    if (renameFor == c.Id)
                    {
                        int id = c.Id;
                        ui.TextInput("Neuer Name", renameText, v => renameText = v,
                            () => { if (RealmActions.Run(Runner, new RenameCityCommand(id, renameText)).Ok) renameFor = -1; }, PlayerRealm.MaxNameLength);
                    }
                }
            }
            using (ui.Actions())
                ui.ActionButton("Neue Stadt gründen …", () => { open = false; CityBuilder.Game.UI.Windows.WorldMapUiWindow.OpenForFounding(); }, ActionStyle.Primary, false, true,
                    "Auf der Weltkarte (F4) eine freie Nachbarregion oder einen freien Platz in der eigenen Region wählen");
            ui.Note($"Gründen: Mutterstadt mit {RealmRules.FoundMinPopulation:N0} Einwohnern, Rathaus und Autobahnanschluss (bzw. Hafen für Küste ↔ Küste), " +
                    $"{RealmRules.FoundingCost:N0} € aus der Stadtkasse. Die neue Stadt startet mit {RealmRules.NewCityFunds:N0} € und leerer Karte.");
        }

        // ------------------------------------------------------------ Lieferungen

        void DrawDeliveries(Ui ui)
        {
            var realm = Realm;
            var sys = Sys;
            ui.Section("Interne Lieferungen", "zwischen eigenen Städten · ohne Zoll · Fracht zahlt der Sender, Verrechnung bei Ankunft");
            bool any = false;
            foreach (var d in realm.Deliveries)
            {
                if (!d.IsActive && d.WeeksDispatched == 0) continue;
                any = true;
                var a = realm.City(d.From); var b = realm.City(d.To);
                var route = sys.Route(d.From, d.To);
                using (ui.Card(d.IsActive ? Tone.None : Tone.Muted))
                {
                    ui.Text($"#{d.Id} {a?.Name} → {b?.Name}: {d.Terms.UnitsPerWeek:0} {d.Good.Name}/Woche",
                        d.IsActive ? Tone.None : Tone.Muted);
                    using (ui.Fields())
                    {
                        ui.Field("Stand", $"{d.WeeksDispatched}/{d.Terms.Weeks} Wochen · {StatusText(d.Status)}");
                        ui.Field("Verschickt · gefehlt", $"{d.Shipped:0} · {d.Shortfall:0} (letzte Woche {d.LastShipped:0})",
                            d.Shortfall > 0.5 ? Tone.Warn : Tone.None);
                        ui.Field("Verrechnung · Fracht", $"{d.ValueSettled:N0} € · {d.FreightPaid:N0} €");
                        ui.Field("Weg", route != null ? route.ToString() : "kein Weg", route != null ? Tone.None : Tone.Warn);
                        double unit = sys.UnitPrice(d.Terms);
                        ui.Field("Preis heute", $"{unit:0.0} €/E ({(d.Terms.PriceMode == PriceMode.Fixed ? "fest" : $"Welt × {d.Terms.Price:0.00}")})");
                    }
                    if (d.IsActive)
                    {
                        int id = d.Id;
                        using (ui.Actions())
                            ui.ActionButton("Beenden", () => RealmActions.Run(Runner, new CancelDeliveryCommand(id)), ActionStyle.Danger);
                    }
                }
            }
            if (!any) ui.Note("Noch keine Lieferungen.");
            int inTransit = realm.Shipments.Count;
            if (inTransit > 0)
            {
                ui.Section("Unterwegs", $"{inTransit} Sendungen");
                using (ui.Fields())
                    foreach (var s in realm.Shipments)
                        ui.Field($"{realm.City(s.From)?.Name} → {realm.City(s.To)?.Name}", $"{s.Units:0} {s.Good.Name} · Ankunft Tag {s.ArriveDay + 1}");
            }

            if (realm.Cities.Count < 2) { ui.Note("Für Lieferungen braucht es mindestens zwei eigene Städte."); return; }
            ui.Section("Neue Liefervereinbarung", "Sender in der detaillierten Stadt: Firmen liefern per Lkw ins Grenzlager (wie Exportverträge)");
            var cities = realm.Cities;
            if (draftFrom < 0 || realm.City(draftFrom) == null) draftFrom = cities[0].Id;
            if (draftTo < 0 || realm.City(draftTo) == null || draftTo == draftFrom) draftTo = Next(cities, draftFrom, draftFrom);
            var goods = GoodsCatalog.All;
            draftGood = Mathf.Clamp(draftGood, 0, goods.Count - 1);
            var g = goods[draftGood];
            using (ui.Fields())
            {
                ui.Stepper("Von", realm.City(draftFrom).Name, () => draftFrom = Prev(cities, draftFrom, -1), () => draftFrom = Next(cities, draftFrom, -1));
                ui.Stepper("Nach", realm.City(draftTo)?.Name ?? "–", () => draftTo = Prev(cities, draftTo, draftFrom), () => draftTo = Next(cities, draftTo, draftFrom));
                ui.Stepper("Gut", g.Name, () => draftGood = (draftGood + goods.Count - 1) % goods.Count, () => draftGood = (draftGood + 1) % goods.Count);
                ui.Stepper("Menge je Woche", $"{draftUnits:0}", () => draftUnits = System.Math.Max(5, draftUnits - Step(draftUnits)), () => draftUnits += Step(draftUnits));
                ui.Stepper("Laufzeit", $"{draftWeeks} Wochen", () => draftWeeks = System.Math.Max(TradeRules.MinWeeks, draftWeeks - 1),
                    () => draftWeeks = System.Math.Min(TradeRules.MaxWeeks, draftWeeks + 1));
                ui.Stepper("Verrechnungspreis", $"Welt × {draftPriceFactor:0.00}", () => draftPriceFactor = System.Math.Max(0.5, draftPriceFactor - 0.05),
                    () => draftPriceFactor = System.Math.Min(2.0, draftPriceFactor + 0.05));
            }
            var terms = new ContractTerms { Good = g, UnitsPerWeek = draftUnits, Weeks = draftWeeks, PriceMode = PriceMode.WorldIndexed, Price = draftPriceFactor, PenaltyShare = 0 };
            var r = sys.Route(draftFrom, draftTo);
            double unitPrice = sys.UnitPrice(terms);
            using (ui.Fields())
            {
                ui.Field("Weg", r != null ? r.ToString() : "kein Weg (andere Landmasse, keine Küste)", r != null ? Tone.None : Tone.Bad);
                if (r != null)
                    ui.Field("Je Woche", $"Wert {unitPrice * draftUnits:N0} € · Fracht {r.UnitCost(g) * draftUnits:N0} € (Sender)");
            }
            using (ui.Actions())
                ui.ActionButton("Vereinbaren", () => RealmActions.Run(Runner, new ProposeDeliveryCommand(draftFrom, draftTo, terms)), ActionStyle.Primary, false, r != null);
        }

        static int Next(IReadOnlyList<RealmCity> list, int current, int skip)
        {
            int i = IndexOf(list, current);
            for (int k = 1; k <= list.Count; k++)
            {
                var c = list[(i + k + list.Count) % list.Count];
                if (c.Id != skip) return c.Id;
            }
            return current;
        }

        static int Prev(IReadOnlyList<RealmCity> list, int current, int skip)
        {
            int i = IndexOf(list, current);
            for (int k = 1; k <= list.Count; k++)
            {
                var c = list[((i - k) % list.Count + list.Count) % list.Count];
                if (c.Id != skip) return c.Id;
            }
            return current;
        }

        static int IndexOf(IReadOnlyList<RealmCity> list, int id)
        {
            for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return i;
            return 0;
        }

        static double Step(double v) => v < 50 ? 5 : v < 200 ? 10 : v < 1000 ? 50 : 100;

        static string StatusText(RealmDeliveryStatus s) =>
            s == RealmDeliveryStatus.Active ? "läuft" : s == RealmDeliveryStatus.Completed ? "abgeschlossen" : "beendet";

        // ------------------------------------------------------------ Land

        void DrawNation(Ui ui)
        {
            var realm = Realm;
            var sys = Sys;
            if (realm.IsNation) { DrawNationRunning(ui, realm, sys); return; }

            var check = sys.CheckNation(string.IsNullOrWhiteSpace(nationName) ? null : nationName, capital);
            ui.Section("Land gründen", "aus der freien Stadt wird ein eigenes Land – Name, Flagge, Hauptstadt");
            using (ui.Card())
            {
                ui.Progress(check.Progress, check.Ok ? Tone.Good : Tone.Info, "Fortschritt", $"{check.Progress * 100f:0} %");
                using (ui.Fields())
                    foreach (var i in check.Items) ui.Field((i.Ok ? "✓ " : "✗ ") + i.Text, i.Detail ?? "", i.Ok ? Tone.Good : Tone.Warn);
            }
            ui.TextInput("Name des Landes", nationName, v => nationName = v, null, PlayerRealm.MaxNameLength);
            ui.Note("Frei erfunden – keine echten Ländernamen. „Freie Stadt …“ ist reserviert.");
            ui.Section("Flagge", "Farbe des Landes auf der Weltkarte");
            for (int i = 0; i < FlagColors.Length; i++)
            {
                int k = i;
                ui.ListItem("", ColorOf(FlagColors[i]), FlagNames[i], $"#{FlagColors[i]:X6}", i == flagIndex ? "gewählt" : null, Tone.Good, () => flagIndex = k);
            }
            ui.Section("Hauptstadt", "braucht ein Rathaus");
            if (capital < 0 || realm.City(capital) == null) capital = realm.CapitalCityId;
            using (ui.Chips())
                foreach (var c in realm.Cities)
                {
                    int id = c.Id;
                    ui.ChoiceChip(c.Name, id == capital, () => capital = id, true, false, c.Economy.HasTownHall || c.Id == realm.ActiveCityId && World.Services.HasTownHall ? "Rathaus" : null,
                        "Hauptstadt (braucht ein Rathaus)");
                }
            using (ui.Actions())
                ui.ActionButton("Land gründen", () => RealmActions.Run(Runner, new FoundNationCommand(nationName, FlagColors[flagIndex], capital)),
                    ActionStyle.Primary, false, sys.CheckNation(nationName, capital).Ok);
            ui.Note("Danach: Regionen gehören dem Land (Weltkarte färbt um), NPC-Länder reagieren (Beziehung, Anerkennung), " +
                    $"{RealmRules.NationTaxShare * 100:0} % der Steuern jeder Stadt gehen an die Landeskasse, Zölle und Verträge gelten landesweit.");
        }

        void DrawNationRunning(Ui ui, PlayerRealm realm, RealmSystem sys)
        {
            var t = World.Trade;
            using (ui.Grid(3))
            {
                ui.Stat("Landeskasse", $"{realm.NationTreasury / 1000:N0}", "T€");
                ui.Stat("Abgabe gesamt", $"{realm.NationIncomeTotal / 1000:N0}", "T€");
                ui.Stat("Anerkannt", $"{sys.RecognitionCount}", t != null ? $"von {t.Nations.Count - 1}" : null);
            }
            using (ui.Fields())
            {
                ui.Field("Gegründet", $"Tag {realm.NationFoundedDay + 1} · Hauptstadt {realm.CapitalCity?.Name}");
                ui.Field("Landesabgabe", $"{RealmRules.NationTaxShare * 100:0} % der Steuern jeder Stadt");
                ui.Field("Zölle & Verträge", "gelten landesweit (Handel F11) – abgewickelt über das Grenzlager der detaillierten Stadt");
            }
            ui.Section("Zuschüsse", "Landeskasse → Stadtkasse");
            foreach (var c in realm.Cities)
            {
                int id = c.Id;
                using (ui.Actions())
                {
                    ui.ActionButton($"{c.Name}: +50 T€", () => RealmActions.Run(Runner, new TransferFundsCommand(id, 50000)), ActionStyle.Secondary, false, realm.NationTreasury >= 50000);
                    ui.ActionButton($"{c.Name}: +250 T€", () => RealmActions.Run(Runner, new TransferFundsCommand(id, 250000)), ActionStyle.Secondary, false, realm.NationTreasury >= 250000);
                }
            }
            if (t == null) return;
            ui.Section("Anerkennung", "Länder prüfen alle 30 Tage neu – bessere Beziehungen helfen");
            int me = World.Planet.PlayerCountryId;
            using (ui.Fields())
                foreach (var n in t.Nations)
                {
                    if (n.IsPlayer) continue;
                    bool yes = n.Id < realm.Recognized.Length && realm.Recognized[n.Id];
                    ui.Field(n.Country.Name, $"{(yes ? "erkennt an" : "wartet ab")} · Beziehung {t.Relation(n.Id, me):+0;−0}", yes ? Tone.Good : Tone.Muted);
                }
        }

        // ------------------------------------------------------------ Meldungen

        void DrawNews(Ui ui)
        {
            var realm = Realm;
            ui.Section("Meldungen", "Städte, Lieferungen, Land, Anerkennung, Landnahme der NPC-Länder");
            if (realm.News.Count == 0) { ui.Note("Noch nichts passiert."); return; }
            for (int i = realm.News.Count - 1; i >= 0; i--)
            {
                var m = realm.News[i];
                ui.Field($"Tag {m.Day + 1} · {m.Title}", m.Text, m.Important ? Tone.Accent : Tone.None);
            }
            ui.Note(realm.NpcExpansion
                ? $"NPC-Länder beanspruchen langsam freies Land (bisher {realm.RegionsClaimedByNpc} Regionen; höchstens {RealmRules.ExpansionMaxShare * 100:0} % des freien Landes)."
                : "NPC-Expansion ist aus.");
        }

        static Color ColorOf(uint rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    }
}
