using CityBuilder.Game.Goods;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Goods;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>
    /// Produktion lenken: Pinsel-Art „Schwerpunkt“ in der Bauleisten-Kategorie Zonen (Taste Z) – eigener Block, damit
    /// <c>GameHud.BuildBar.cs</c> nur einen Einhängepunkt bekommt. Kachel je Schwerpunkt (Farbe wie die Fläche auf der Karte,
    /// Unterzeile = gemalte Fläche bzw. Förderstufe), „Automatisch“ entfernt den Schwerpunkt; LMB malt, RMB setzt automatisch.
    /// </summary>
    public sealed partial class GameHud
    {
        void BuildFocusSection()
        {
            var policy = runner.World.Policy;
            if (policy == null) return;

            var row = Container("cb-tool-row");
            row.Add(PLabel("Schwerpunkt Industrie"));
            row.Add(Container("cb-spacer"));
            row.Add(PLabel("Pinsel"));
            var presets = Container("cb-segment");
            foreach (var r in new[] { 48f, 96f, 160f })
            {
                float radius = r;
                presets.Add(PBtn($"{r:0} m", () => tool.SetBrush(radius),
                    () => tool.SelectedFocus.HasValue && Mathf.Approximately(tool.BrushRadius, radius),
                    "Großer Pinsel für Schwerpunkt-Gebiete (erst Schwerpunkt wählen; Zonen-Pinsel max. 60 m)", "cb-seg-btn"));
            }
            row.Add(presets);
            panelBody.Add(row);

            var tiles = Container("cb-tiles");
            foreach (var branch in IndustryBranches.Branches) tiles.Add(FocusTile(branch));
            tiles.Add(Container("cb-tile-sep"));
            tiles.Add(FocusTile(IndustryFocus.Auto));
            panelBody.Add(tiles);
            panelBody.Add(Info(() =>
            {
                var w = runner.World;
                if (w == null || w.Policy == null) return "";
                return tool.SelectedFocus.HasValue
                    ? "Schwerpunkt lenkt, welche Industrie hier entsteht – fremde Betriebe stellen mit der Zeit um · Rohstoff-Ansicht zeigt das Vorkommen · Förderung: Wirtschaft (F5) → Lenkung"
                    : "Schwerpunkt: die Simulation wählt die Industrie weiter selbst, du lenkst sie (z. B. Öl/Gas über einem Ölfeld)";
            }));
        }

        VisualElement FocusTile(IndustryFocus f)
        {
            var policy = runner.World.Policy;
            var tile = Tile(IndustryBranches.ShortName(f), null, IndustryFocusView.FocusColor(f),
                f == IndustryFocus.Auto ? HudIconKind.Bulldoze : IndustryBranches.Deposit(f) != null ? HudIconKind.Resource : HudIconKind.Briefcase,
                () => tool.SelectFocus(f),
                () => tool.CurrentMode == RoadTool.Mode.Zone && tool.SelectedFocus == f,
                () =>
                {
                    var p = runner.World.Policy;
                    string tip = $"{IndustryBranches.DisplayName(f)}: {IndustryBranches.Describe(f)}";
                    if (p == null || f == IndustryFocus.Auto) return tip + " · RMB mit jedem Schwerpunkt-Pinsel setzt ebenfalls automatisch";
                    return tip + $" · gemalt {p.Hectares(f):0.#} ha · Förderung {IndustryBranches.LevelName(p.SubsidyLevel(f))}";
                });
            var sub = Text("", "cb-tile__sub");
            tile.Add(sub);
            panelSlowRefresh.Add(() =>
            {
                var p = runner.World.Policy;
                if (p == null) { sub.text = ""; return; }
                if (f == IndustryFocus.Auto) { sub.text = "entfernen"; return; }
                int lvl = p.SubsidyLevel(f);
                float ha = p.Hectares(f);
                sub.text = (ha > 0.05f ? $"{ha:0.#} ha" : "–") + (lvl > 0 ? $" · Förd. {lvl}" : "");
            });
            return tile;
        }
    }
}
