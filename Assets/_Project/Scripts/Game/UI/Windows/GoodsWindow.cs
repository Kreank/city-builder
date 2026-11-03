using System.Collections.Generic;
using CityBuilder.Game.Goods;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// G5 Wirtschaft (mittiges Fenster, Taste F5 oder Knopf in der oberen Leiste): Reiter Güter (lokaler Preis/Weltmarkt,
    /// Bestand/Ziel, Produktion/Verbrauch, Haushalte, Import/Export), Firmen (Zustand, Gewinn, Kasse, Nachfrage-Zuschlag)
    /// und Lieferungen. Zustand (offen/zu, Reiter, Taste) bleibt im <see cref="GoodsPanel"/>, das sonst als IMGUI-Rückfall
    /// zeichnet. Liest nur die Abfrage-API des <see cref="GoodsSystem"/>.
    /// </summary>
    public sealed class GoodsWindow : GameWindow
    {
        static readonly string[] TabNames = { "Güter", "Firmen", "Lieferungen" };
        const int MaxRows = 60;

        readonly GoodsPanel panel;
        int firmPage, shipmentPage;

        public GoodsWindow(GoodsPanel panel) : base(WindowSlot.Center) { this.panel = panel; }

        GoodsSystem Goods => World?.Goods;

        public override float OpenScore => panel != null && panel.Visible && Goods != null ? 1f : 0f;
        public override void Close() => panel.Visible = false;
        public bool IsOpen => panel != null && panel.Visible;
        public void Toggle() { if (panel != null) panel.Visible = !panel.Visible; }

        public override void DrawHead(Ui ui)
        {
            var g = Goods;
            var t = g.TradeYesterday;
            Head(ui, "Wirtschaft (F5)", t.Net >= 0 ? Tone.Good : Tone.Warn, "Warenketten & Handel",
                $"gestern Import {Money(t.ImportValue)} · Export {Money(t.ExportValue)} · Bilanz {Signed(t.Net)}");
            ui.Tabs(TabNames, panel.Tab, i => { panel.Tab = i; ContentVersion++; });
        }

        public override void DrawBody(Ui ui)
        {
            var g = Goods;
            var s = g.Stats;
            using (ui.Grid(3))
            {
                ui.Stat("Firmen", s.Firms.ToString());
                ui.Stat("Gewinn gestern", Signed(s.TotalProfitYesterday), null, s.TotalProfitYesterday >= 0 ? Tone.Good : Tone.Warn);
                ui.Stat("Einkäufe gelungen", s.PurchasesYesterday > 0 ? $"{100f * s.SuccessfulPurchasesYesterday / s.PurchasesYesterday:0} %" : "–", null,
                    s.PurchasesYesterday > 0 && s.SuccessfulPurchasesYesterday < s.PurchasesYesterday * 0.8f ? Tone.Warn : Tone.None);
            }
            switch (panel.Tab)
            {
                case 0: DrawGoods(ui, g); break;
                case 1: DrawFirms(ui, g); break;
                default: DrawShipments(ui, g); break;
            }
        }

        void DrawGoods(Ui ui, GoodsSystem g)
        {
            GoodTier? tier = null;
            foreach (var m in g.Markets)
            {
                var good = m.Good;
                if (tier != good.Tier)
                {
                    tier = good.Tier;
                    ui.Section(GoodsCatalog.DisplayName(good.Tier) + "e", tier == GoodTier.Raw ? "Preis lokal (Welt) · Import/Export gestern" : null);
                    ui.Columns("Gut", "€ lokal (Welt)", "Import / Export", header: true);
                }
                var y = m.Yesterday;
                Tone priceTone = m.PriceIndex > 1.03f ? Tone.Warn : m.PriceIndex < 0.97f ? Tone.Good : Tone.None;
                string name = good.IsConsumerGood ? $"{good.Name} · {GoodsCatalog.DisplayName(good.Consumer)}" : good.Name;
                ui.Columns(name, $"{m.LocalPrice:0.0} ({m.WorldPrice:0.0})", $"+{y.Imported:0.#} / −{y.Exported:0.#}", priceTone,
                    y.Imported > y.Exported ? Tone.Warn : Tone.None);
                string hh = good.IsConsumerGood ? $" · Haushalte {y.HouseholdBought:0.#}/{y.HouseholdWanted:0.#}" : "";
                ui.Columns("   Bestand/Ziel · erzeugt/verbraucht", $"{m.Stock:0}/{m.TargetStock:0}", $"{y.Produced:0.#}/{y.Consumed:0.#}{hh}", Tone.Muted, Tone.Muted);
            }
            ui.Note("Lokaler Preis aus Bestand gegen Zielbestand (3 Tage Bedarf), träge und durch den Weltmarkt begrenzt " +
                    "(Export-/Importparität). Orange = knapp, grün = Überschuss.");
        }

        void DrawFirms(Ui ui, GoodsSystem g)
        {
            var s = g.Stats;
            ui.Section("Übersicht");
            using (ui.Fields())
            {
                ui.Field("Gewinnung · Verarbeitung · Fertigung", $"{s.Extraction} · {s.Processing} · {s.Manufacturing}");
                ui.Field("Einzelhandel · Büro", $"{s.Retail} · {s.Office}");
                ui.Field("Pleiten (gesamt)", s.Bankruptcies.ToString(), s.Bankruptcies > 0 ? Tone.Warn : Tone.None);
                ui.Field("Nachfrage-Zuschlag Industrie", Offset(g.DemandOffset(ZoneType.Industry)), OffsetTone(g.DemandOffset(ZoneType.Industry)));
                ui.Field("Nachfrage-Zuschlag Handel", Offset(g.DemandOffset(ZoneType.Retail)), OffsetTone(g.DemandOffset(ZoneType.Retail)));
                ui.Field("Nachfrage-Zuschlag Büro", Offset(g.DemandOffset(ZoneType.Office)), OffsetTone(g.DemandOffset(ZoneType.Office)));
                ui.Field("Erntefaktor heute", $"×{g.HarvestToday:0.00}");
            }
            ui.Section("Firmen", "Gewinn gestern · Kasse");
            ui.Columns("Firma · Zustand", "Gewinn", "Kasse", header: true);
            var firms = g.Firms;
            int pages = (firms.Count + MaxRows - 1) / MaxRows;
            firmPage = System.Math.Min(firmPage, System.Math.Max(0, pages - 1));
            for (int i = firmPage * MaxRows; i < firms.Count && i < (firmPage + 1) * MaxRows; i++)
            {
                var f = firms[i];
                double p = f.Yesterday.Profit;
                ui.Columns($"{f.Recipe.Name} #{f.BuildingId} · {Firm.StatusText(f.Status)} · {f.Workers}/{f.Jobs}",
                    Signed(p), Money(f.Cash), p >= 0 ? Tone.Good : Tone.Bad, f.Cash < 0 ? Tone.Warn : Tone.None);
            }
            if (pages > 1) ui.Pager(firmPage, pages, i => firmPage = i);
        }

        readonly List<Shipment> active = new List<Shipment>();

        void DrawShipments(Ui ui, GoodsSystem g)
        {
            var s = g.Stats;
            ui.Section("Lieferverkehr");
            using (ui.Fields())
            {
                ui.Field("Unterwegs", $"{s.ActiveShipments} (davon Lkw {s.ActiveTrucks})");
                ui.Field("Gestern abgeschickt", $"{s.ShipmentsYesterday} · Lkw {s.TrucksYesterday} · Platzhalter {s.PlaceholderYesterday}");
                ui.Field("Lkw ohne Platz/Weg → Platzhalter", s.TruckFailedYesterday.ToString(), s.TruckFailedYesterday > 0 ? Tone.Muted : Tone.None);
                ui.Field("Verloren (Ziel abgerissen)", s.LostShipments.ToString(), s.LostShipments > 0 ? Tone.Warn : Tone.None);
            }
            active.Clear();
            foreach (var sh in g.Shipments) if (!sh.IsDone) active.Add(sh);
            ui.Section("Lieferungen", "Art · Gut · Menge · Weg");
            ui.Columns("Lieferung", "Wert", "Transport", header: true);
            int pages = (active.Count + MaxRows - 1) / MaxRows;
            shipmentPage = System.Math.Min(shipmentPage, System.Math.Max(0, pages - 1));
            for (int i = shipmentPage * MaxRows; i < active.Count && i < (shipmentPage + 1) * MaxRows; i++)
            {
                var sh = active[i];
                string kind = sh.Kind == ShipmentKind.Import ? "Import" : sh.Kind == ShipmentKind.Export ? "Export" : "lokal";
                string from = sh.FromBuildingId != 0 ? $"#{sh.FromBuildingId}" : "außen";
                string to = sh.ToBuildingId != 0 ? $"#{sh.ToBuildingId}" : "außen";
                ui.Columns($"{kind} · {sh.Amount:0.#} {sh.Good.Name} · {from}→{to}{(sh.IsTruck ? " · Lkw" : "")}", Money(sh.Value), Money(sh.TransportCost));
            }
            if (pages > 1) ui.Pager(shipmentPage, pages, i => shipmentPage = i);
        }

        static string Offset(float v) => $"{v:+0.0;−0.0;0}";
        static Tone OffsetTone(float v) => v > 1f ? Tone.Good : v < -1f ? Tone.Warn : Tone.None;
        static string Money(double v) => $"{v:N0} €";
        static string Signed(double v) => (v >= 0 ? "+" : "−") + Money(System.Math.Abs(v));
    }
}
