using CityBuilder.Game.Roads;
using CityBuilder.Game.Shipping;
using CityBuilder.Simulation.Shipping;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>Bauleiste, Kategorie „Hafen“ (Taste 8): Güterhafen, Klappbrücke, Abriss, Fahrrinne zeigen – eigener Block; Warnchip „Fahrrinne gesperrt“.</summary>
    public sealed partial class GameHud
    {
        static readonly Color HarborSwatch = BuildLabels.Hex(0x2E7DB5);
        ShippingTool shippingTool;

        ShippingTool Harbors => shippingTool != null ? shippingTool : shippingTool = tool.GetComponent<ShippingTool>();

        Category? HarborCategory() => Harbors != null && Harbors.Current == ShippingTool.HarborAction.Remove ? Category.Bulldoze : Category.Harbor;

        void SelectHarborCategory()
        {
            if (Harbors != null) Harbors.Select(Harbors.Current == ShippingTool.HarborAction.Remove ? ShippingTool.HarborAction.Harbor : Harbors.Current);
            else tool.SetMode(RoadTool.Mode.Harbor);
        }

        void BuildHarborPanel()
        {
            Header("Hafen", "Hafen rastet am Ufer ein (Kai an tiefem Wasser, Landseite zur Straße) · Klappbrücke: zwei Klicks quer über die Fahrrinne · Esc/RMB abbrechen");
            var ht = Harbors;
            var sh = runner.World.Shipping;
            if (ht == null || sh == null) { panelBody.Add(Info(() => "Hafen-Werkzeug fehlt.")); return; }

            var tools = Container("cb-tool-row");
            tools.Add(PBtn("Fahrrinne", () => ShippingView.ShowFairway = !ShippingView.ShowFairway, () => ShippingView.ShowFairway,
                "Fahrrinne immer zeigen (beim Bauen von Straßen/Gleisen ist sie ohnehin sichtbar): rot = Brücke zu niedrig, gelb = Klappbrücke, grün = hoch genug"));
            panelBody.Add(tools);

            var tiles = Container("cb-tiles");
            var type = HarborCatalog.Cargo;
            tiles.Add(ThumbTile(type.DisplayName, BuildLabels.Money((float)type.Cost), HarborSwatch, HudIconKind.Ship,
                "tr:harbor_crane", ModelThumbnails.Transport("harbor_crane"),
                () => ht.Select(ShippingTool.HarborAction.Harbor),
                () => ht.Active && ht.Current == ShippingTool.HarborAction.Harbor,
                $"{type.DisplayName} · Kai {type.Width:0} m, {type.Berths} Liegeplätze · Binnenschiffe am Fluss, Küstenfrachter am tiefen Seeweg · " +
                $"Import/Export der Firmen und Seehandel per Schiff (Fracht {ShippingRules.InlandFreightShare * 100f:0.#} % bzw. {ShippingRules.SeaFreightShare * 100f:0.#} % statt " +
                $"{Simulation.Goods.GoodsRules.WorldFreightShare * 100f:0} % per Lkw) · macht den Wasser-Gateway befahrbar · Unterhalt {BuildLabels.Money((float)type.UpkeepPerWeek)}/Woche"));
            tiles.Add(ThumbTile("Klappbrücke", BuildLabels.Money((float)ShippingRules.DrawbridgeCost) + " + Straße", HarborSwatch, HudIconKind.Road,
                "tr:drawbridge", ModelThumbnails.Transport("drawbridge"),
                () => ht.Select(ShippingTool.HarborAction.Drawbridge),
                () => ht.Active && ht.Current == ShippingTool.HarborAction.Drawbridge,
                $"Niedrige Straßenbrücke ({ShippingRules.DrawbridgeDeck:0} m über Wasser) mit Klappe über der Fahrrinne: öffnet für Schiffe – Ampel rot, Schranken zu, " +
                $"der Verkehr staut (wie am Bahnübergang). Feste Brücken müssen dagegen hoch sein (Binnenwasserstraße {ShippingRules.RiverDesignClearance:0} m, Seeweg {ShippingRules.SeaDesignClearance:0} m lichte Höhe) " +
                $"oder als Tunnel unter dem Fluss durch · Unterhalt {BuildLabels.Money((float)ShippingRules.DrawbridgeUpkeep)}/Woche"));
            panelBody.Add(tiles);

            panelBody.Add(Info(() =>
            {
                string hint = ht.Active && !string.IsNullOrEmpty(ht.Label) ? ht.Label + "  ·  " : "";
                if (!sh.Active) return hint + "Keine schiffbaren Gewässer auf dieser Karte.";
                int inService = 0;
                foreach (var h in sh.Harbors) if (h.InService) inService++;
                string warn = sh.FairwayWarning;
                return hint + $"{sh.Harbors.Count} Häfen ({inService} in Betrieb) · {sh.Ships.Count} Schiffe · {sh.Drawbridges.Count} Klappbrücken · " +
                       $"{sh.ThroughYesterday} Durchgangsschiffe gestern" + (warn != null ? " · " + warn : "") + " · F8 Hafenübersicht";
            }));
        }

        void AddHarborBulldozeTile(VisualElement tiles)
        {
            if (Harbors == null) return;
            tiles.Add(Tile("Hafen/Klappe", "Hafen", DangerSwatch, HudIconKind.Ship,
                () => Harbors.Select(ShippingTool.HarborAction.Remove),
                () => Harbors.Active && Harbors.Current == ShippingTool.HarborAction.Remove,
                "Hafen oder Klappe einer Klappbrücke anklicken – 25 % zurück (liegende Schiffe legen ab)"));
        }

        // --- Warnchip: Fahrrinne gesperrt (feste Brücke zu niedrig) bzw. Hafen nicht erreichbar; Klick öffnet die Hafenübersicht
        VisualElement FairwayChip()
        {
            var chip = El("cb-chip-warn", "cb-hidden");
            chip.Add(Icon(HudIconKind.Ship));
            var text = Text("", "cb-chip-warn__text");
            chip.Add(text);
            string warn = null;
            OnClick(chip, () => ShippingView.ShowFairway = true);
            slowRefresh.Add(() =>
            {
                warn = runner.World.Shipping?.FairwayWarning;
                chip.EnableInClassList("cb-hidden", warn == null);
                text.text = "Fahrrinne gesperrt";
            });
            Tip(chip, () => (warn ?? "") + " · Klick: Fahrrinne zeigen");
            return chip;
        }
    }
}
