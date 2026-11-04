using CityBuilder.Game.Roads;
using CityBuilder.Game.Transit;
using CityBuilder.Simulation.Transit;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>Bauleiste, Kategorie „ÖPNV“ (Taste 5): Haltestelle, Linie zeichnen, Busdepot, Abriss – eigener Block, damit
    /// <c>GameHud.BuildBar.cs</c> nur Einhängepunkte bekommt.</summary>
    public sealed partial class GameHud
    {
        static readonly Color TransitSwatch = BuildLabels.Hex(0xE0A21B);

        Category? TransitCategory() =>
            transitTool != null && transitTool.Current == TransitTool.TransitAction.Remove ? Category.Bulldoze : Category.Transit;

        void SelectTransitCategory()
        {
            if (transitTool != null)
                transitTool.Select(transitTool.Current == TransitTool.TransitAction.Remove ? TransitTool.TransitAction.Stop : transitTool.Current);
            else tool.SetMode(RoadTool.Mode.Transit);
        }

        void BuildTransitPanel()
        {
            Header("ÖPNV", "Haltestelle rastet am Straßenrand ein (⇧ beide Seiten) · Linie: Halte anklicken, Enter fertig, RMB zurück, Esc verwerfen");
            if (transitTool == null || runner.World.Transit == null) { panelBody.Add(Info(() => "ÖPNV-Werkzeug fehlt.")); return; }
            var t = runner.World.Transit;
            var tt = transitTool;

            var tools = Container("cb-tool-row");
            tools.Add(PBtn("Beide Seiten", () => tt.BothSides = !tt.BothSides, () => tt.BothSides, "Haltestellen als Paar auf beiden Straßenseiten setzen (⇧ kehrt um)"));
            var kind = Container("cb-segment");
            kind.Add(PBtn("Rundlinie", () => tt.DrawLoop = true, () => tt.DrawLoop, "A → B → C → A", "cb-seg-btn"));
            kind.Add(PBtn("Pendel", () => tt.DrawLoop = false, () => !tt.DrawLoop, "A → B → C → B → A", "cb-seg-btn"));
            tools.Add(kind);
            tools.Add(PLabel("Busse"));
            tools.Add(PBtn("−", () => tt.SetNewLineVehicles(tt.NewLineVehicles - 1), null, "Weniger Busse für die neue Linie"));
            tools.Add(PValue(() => tt.NewLineVehicles.ToString()));
            tools.Add(PBtn("+", () => tt.SetNewLineVehicles(tt.NewLineVehicles + 1), null, "Mehr Busse (dichterer Takt)"));
            tools.Add(PBtn("Fertig", () => tt.FinishLine(), () => tt.DrawingStops.Count >= 2, "Gezeichnete Linie anlegen (Enter)"));
            tools.Add(PBtn("Verwerfen", tt.CancelLine, null, "Gezeichnete Halte verwerfen (Esc)"));
            tools.Add(Container("cb-spacer"));
            tools.Add(PBtn("Linien zeigen", () => TransitView.ShowLines = !TransitView.ShowLines, () => TransitView.ShowLines,
                "Linienverläufe auch außerhalb des ÖPNV-Werkzeugs zeigen"));
            panelBody.Add(tools);

            var tiles = Container("cb-tiles");
            tiles.Add(ThumbTile("Haltestelle", $"{TransitRules.StopCost:0} €", TransitSwatch, HudIconKind.Bus,
                "tr:bus_stop", ModelThumbnails.Transport("bus_stop"),
                () => tt.Select(TransitTool.TransitAction.Stop),
                () => tt.Active && tt.Current == TransitTool.TransitAction.Stop,
                $"Haltestelle am Fahrbahnrand einer Stadtstraße · Seite des Mauszeigers (⇧ beide Seiten) · {TransitRules.StopUpkeepPerWeek:0} €/Woche"));
            tiles.Add(ThumbTile("Linie zeichnen", "Halte anklicken", TransitSwatch, HudIconKind.Road, null, null,
                () => tt.Select(TransitTool.TransitAction.Line),
                () => tt.Active && tt.Current == TransitTool.TransitAction.Line,
                "Haltestellen nacheinander anklicken (freie Straße setzt eine neue) · Klick auf die erste schließt die Rundlinie · Enter fertig"));
            var d = TransitCatalog.BusDepot;
            tiles.Add(ThumbTile(d.DisplayName, BuildLabels.Money((float)d.Cost), TransitSwatch, HudIconKind.Bus,
                "tr:bus_depot", ModelThumbnails.Transport("bus_depot"),
                () => tt.Select(TransitTool.TransitAction.Depot),
                () => tt.Active && tt.Current == TransitTool.TransitAction.Depot,
                $"{d.DisplayName} · {d.Width:0}×{d.Depth:0} m · richtet sich zur Straße aus (Strg frei, , . drehen) · Baukosten {BuildLabels.Money((float)d.Cost)}, " +
                $"Unterhalt {BuildLabels.Money((float)d.UpkeepPerWeek)}/Woche + {BuildLabels.Money((float)TransitRules.BusUpkeepPerWeek)} je Bus"));
            panelBody.Add(tiles);

            panelBody.Add(Info(() =>
            {
                string draw = tt.Active && tt.Current == TransitTool.TransitAction.Line && tt.DrawingStops.Count > 0
                    ? $"Zeichne: {tt.DrawingStops.Count} Halte  ·  " : "";
                string hint = tt.Active && !string.IsNullOrEmpty(tt.Label) ? tt.Label + "  ·  " : "";
                return hint + draw + $"{t.Lines.Count} Linien · {t.Buses.Count}/{t.VehicleCapacityTotal} Busse · {t.Stops.Count} Haltestellen · " +
                       $"{t.Depots.Count} Depots · Unterhalt {BuildLabels.Money((float)t.WeeklyUpkeep())}/Woche · F6 Linienübersicht";
            }));
        }

        void AddTransitBulldozeTile(VisualElement tiles)
        {
            if (transitTool == null) return;
            tiles.Add(Tile("Haltestelle/Depot", "ÖPNV", DangerSwatch, HudIconKind.Bus,
                () => transitTool.Select(TransitTool.TransitAction.Remove),
                () => transitTool.Active && transitTool.Current == TransitTool.TransitAction.Remove,
                "Haltestelle oder Busdepot anklicken – 25 % der Baukosten zurück"));
        }
    }
}
