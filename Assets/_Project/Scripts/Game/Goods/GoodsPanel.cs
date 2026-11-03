using System.Collections.Generic;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Zoning;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.Goods
{
    /// <summary>
    /// Fenster „Wirtschaft“ (IMGUI, Taste F3, verschiebbar): Güter mit lokalem Preis, Weltmarkt, Bestand/Ziel,
    /// Produktion/Verbrauch und Import/Export des Vortags; Firmen je Rolle mit Gewinn; Lieferungen (Lkw/Platzhalter).
    /// Liest nur (Abfrage-API des <see cref="GoodsSystem"/>). Begleiter des <see cref="RoadTool"/>.
    /// </summary>
    public sealed class GoodsPanel : MonoBehaviour
    {
        const int WindowId = 0x600D5;

        RoadTool tool;
        Rect window = new Rect(0f, 0f, 760f, 560f);
        bool visible, placed;
        int tab;
        Vector2 scroll;
        GUIStyle header, small, right;
        static readonly string[] Tabs = { "Güter", "Firmen", "Lieferungen" };

        public bool Visible { get => visible; set => visible = value; }

        public void Init(RoadTool roadTool) => tool = roadTool;

        GoodsSystem Goods => tool != null && tool.Runner != null && tool.Runner.World != null ? tool.Runner.World.Goods : null;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame) visible = !visible;
        }

        void OnGUI()
        {
            if (!visible || Goods == null) return;
            if (header == null)
            {
                header = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, richText = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = false };
                right = new GUIStyle(small) { alignment = TextAnchor.UpperRight };
            }
            if (!placed)
            {
                window.x = Mathf.Max(HudLayout.LeftInset, Screen.width * 0.5f - window.width * 0.5f);
                window.y = HudLayout.TopInset + 30f;
                window.height = Mathf.Min(window.height, Screen.height - HudLayout.TopInset - HudLayout.BottomInset - 40f);
                placed = true;
            }
            UiBlocker.Register(window);
            window = GUI.Window(WindowId, window, Draw, "Wirtschaft (F3)");
        }

        void Draw(int id)
        {
            var g = Goods;
            if (g == null) return;
            GUILayout.BeginHorizontal();
            tab = GUILayout.Toolbar(tab, Tabs, GUILayout.Width(330f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(24f))) visible = false;
            GUILayout.EndHorizontal();

            var t = g.TradeYesterday;
            var s = g.Stats;
            GUILayout.Label($"Gestern: Import {Money(t.ImportValue)} · Export {Money(t.ExportValue)} · Bilanz {Signed(t.Net)} · " +
                            $"Firmen {s.Firms} · Gewinn gesamt {Signed(s.TotalProfitYesterday)} · Einkäufe {s.SuccessfulPurchasesYesterday}/{s.PurchasesYesterday}",
                            small);

            scroll = GUILayout.BeginScrollView(scroll);
            switch (tab)
            {
                case 0: DrawGoods(g); break;
                case 1: DrawFirms(g); break;
                default: DrawShipments(g); break;
            }
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        void DrawGoods(GoodsSystem g)
        {
            Row("<b>Gut</b>", "<b>lokal €</b>", "<b>Welt €</b>", "<b>Bestand/Ziel</b>", "<b>prod.</b>", "<b>verbr.</b>", "<b>Haush.</b>", "<b>Import</b>", "<b>Export</b>");
            GoodTier? tier = null;
            foreach (var m in g.Markets)
            {
                var good = m.Good;
                if (tier != good.Tier)
                {
                    tier = good.Tier;
                    GUILayout.Label($"<b>{GoodsCatalog.DisplayName(good.Tier)}e</b>", small);
                }
                var y = m.Yesterday;
                string price = m.PriceIndex > 1.03f ? $"<color=#ffb040>{m.LocalPrice:0.0}</color>" : m.PriceIndex < 0.97f ? $"<color=#7dff8a>{m.LocalPrice:0.0}</color>" : $"{m.LocalPrice:0.0}";
                string name = good.IsConsumerGood ? $"{good.Name} <color=#9fd0ff>({GoodsCatalog.DisplayName(good.Consumer)})</color>" : good.Name;
                Row(name, price, $"{m.WorldPrice:0.0}", $"{m.Stock:0}/{m.TargetStock:0}", $"{y.Produced:0.#}", $"{y.Consumed:0.#}",
                    good.IsConsumerGood ? $"{y.HouseholdBought:0.#}/{y.HouseholdWanted:0.#}" : "–", $"{y.Imported:0.#}", $"{y.Exported:0.#}");
            }
            GUILayout.Label("Preise: lokal aus Bestand gegen Zielbestand (3 Tage Bedarf), träge und durch den Weltmarkt begrenzt. " +
                            "Orange = knapp/teuer, grün = Überschuss.", small);
        }

        void DrawFirms(GoodsSystem g)
        {
            var s = g.Stats;
            GUILayout.Label($"Gewinnung {s.Extraction} · Verarbeitung {s.Processing} · Fertigung {s.Manufacturing} · Einzelhandel {s.Retail} · " +
                            $"Büro {s.Office} · Pleiten {s.Bankruptcies}", small);
            GUILayout.Label($"Nachfrage-Zuschlag: Industrie {g.DemandOffset(ZoneType.Industry):+0.0;-0.0} · " +
                            $"Handel {g.DemandOffset(ZoneType.Retail):+0.0;-0.0} · Büro {g.DemandOffset(ZoneType.Office):+0.0;-0.0}", small);
            Row("<b>Firma</b>", "<b>Zustand</b>", "<b>Besch.</b>", "<b>Produkt.</b>", "<b>Erlös</b>", "<b>Gewinn</b>", "<b>Kasse</b>", "", "");
            foreach (var f in g.Firms)
            {
                var y = f.Yesterday;
                string profit = y.Profit >= 0 ? $"<color=#7dff8a>{y.Profit:N0}</color>" : $"<color=#ff9080>{y.Profit:N0}</color>";
                Row($"{f.Recipe.Name} #{f.BuildingId}", Firm.StatusText(f.Status), $"{f.Workers}/{f.Jobs}", $"{f.Productivity * 100f:0} %",
                    $"{y.Revenue:N0}", profit, $"{f.Cash:N0}", "", "");
            }
        }

        readonly List<Shipment> list = new List<Shipment>();

        void DrawShipments(GoodsSystem g)
        {
            var s = g.Stats;
            GUILayout.Label($"Unterwegs {s.ActiveShipments} (Lkw {s.ActiveTrucks}) · gestern {s.ShipmentsYesterday} Lieferungen, davon Lkw {s.TrucksYesterday}, " +
                            $"ohne Platz/Weg {s.TruckFailedYesterday} · verloren {s.LostShipments}", small);
            Row("<b>Art</b>", "<b>Gut</b>", "<b>Menge</b>", "<b>von</b>", "<b>nach</b>", "<b>Wert</b>", "<b>Transport</b>", "<b>Lkw</b>", "");
            int n = 0;
            foreach (var sh in g.Shipments)
            {
                if (sh.IsDone) continue;
                if (++n > 80) { GUILayout.Label("…", small); break; }
                string kind = sh.Kind == ShipmentKind.Import ? "Import" : sh.Kind == ShipmentKind.Export ? "Export" : "lokal";
                Row(kind, sh.Good.Name, $"{sh.Amount:0.#}", sh.FromBuildingId != 0 ? $"#{sh.FromBuildingId}" : "außen",
                    sh.ToBuildingId != 0 ? $"#{sh.ToBuildingId}" : "außen", $"{sh.Value:N0}", $"{sh.TransportCost:N0}", sh.IsTruck ? "ja" : "–", "");
            }
        }

        static readonly float[] Widths = { 190f, 70f, 60f, 90f, 55f, 55f, 75f, 55f, 55f };

        void Row(params string[] cells)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < cells.Length && i < Widths.Length; i++)
                GUILayout.Label(cells[i], i == 0 ? small : right, GUILayout.Width(Widths[i]));
            GUILayout.EndHorizontal();
        }

        static string Money(double v) => $"{v:N0} €";
        static string Signed(double v) => (v >= 0 ? "+" : "−") + Money(System.Math.Abs(v));
    }
}
