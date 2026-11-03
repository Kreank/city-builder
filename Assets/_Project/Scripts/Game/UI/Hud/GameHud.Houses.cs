using CityBuilder.Game.Roads;
using CityBuilder.Game.Zoning;
using CityBuilder.Simulation.Zoning;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>
    /// Bauleiste, Kategorie „Wohnhäuser“ (Taste 6, Anno-Stil): Bauarten als Kacheln, Drehen, Abriss-Kachel – eigener Block,
    /// damit <c>GameHud.BuildBar.cs</c> nur Einhängepunkte bekommt. Die Anforderungen am Mauszeiger zeigt das Haus-Fenster
    /// (<c>HouseWindow</c>, rechts) und die Hinweiszeile.
    /// </summary>
    public sealed partial class GameHud
    {
        static readonly Color HouseSwatch = BuildLabels.Hex(0x5FA35A);
        HouseTool houseTool;

        HouseTool Houses => houseTool != null ? houseTool : houseTool = tool.GetComponent<HouseTool>();

        Category? HouseCategory() =>
            Houses != null && Houses.Current == HouseTool.HouseAction.Remove ? Category.Bulldoze : Category.Houses;

        void SelectHouseCategory()
        {
            if (Houses != null) Houses.Select(HouseTool.HouseAction.Place);
            else tool.SetMode(RoadTool.Mode.House);
        }

        /// <summary>Hinweiszeile im Haus-Werkzeug (Kosten, Fehler, was am Bauplatz fehlt). false = nichts zu sagen.</summary>
        bool HouseHint(out string text, out bool error)
        {
            text = null;
            error = false;
            if (tool.CurrentMode != RoadTool.Mode.House || Houses == null || !tool.HasCursor || string.IsNullOrEmpty(Houses.HintText)) return false;
            text = Houses.HintText;
            error = Houses.HintError;
            return true;
        }

        void BuildHousePanel()
        {
            Header("Wohnhäuser", "Haus an eine Stadtstraße setzen · , und . drehen · steigt auf, wenn Einrichtungen in Reichweite sind (Fenster rechts)");
            var ht = Houses;
            if (ht == null) { panelBody.Add(Info(() => "Haus-Werkzeug fehlt.")); return; }
            var city = runner.World.City;

            var tools = Container("cb-tool-row");
            tools.Add(PLabel("Drehung"));
            tools.Add(PBtn("−15°", () => ht.Rotate(-15f), null, "Haus drehen (,)"));
            tools.Add(PValue(() => $"{ht.Rotation:0}°"));
            tools.Add(PBtn("+15°", () => ht.Rotate(15f), null, "Haus drehen (.)"));
            tools.Add(Container("cb-spacer"));
            tools.Add(PValue(() => ht.Preview != null && ht.Active ? $"Hier erreichbar: Stufe {ht.Preview.ReachableLevel} von {HouseRules.MaxLevel}" : ""));
            panelBody.Add(tools);

            var tiles = Container("cb-tiles");
            foreach (var type in HouseCatalog.All)
            {
                var t = type;
                tiles.Add(Tile(t.DisplayName, $"{t.HouseholdsAtLevel(1)}–{t.HouseholdsAtLevel(HouseRules.MaxLevel)} Whg.", HouseSwatch, HudIconKind.LandValue,
                    () => ht.SelectType(t),
                    () => ht.Active && ht.Current == HouseTool.HouseAction.Place && ht.SelectedType == t,
                    $"{t.DisplayName} ({Zones.DisplayName(t.Zone)}) · Grundstück {t.Width:0}×{t.Depth:0} m · Baukosten {BuildLabels.Money(t.Cost)} · " +
                    $"Wohnungen je Stufe {t.HouseholdsAtLevel(1)} / {t.HouseholdsAtLevel(2)} / {t.HouseholdsAtLevel(3)}"));
            }
            tiles.Add(Container("cb-tile-sep"));
            tiles.Add(Tile("Abreißen", "25 % zurück", DangerSwatch, HudIconKind.Bulldoze,
                () => ht.Select(HouseTool.HouseAction.Remove),
                () => ht.Active && ht.Current == HouseTool.HouseAction.Remove,
                "Gesetztes Wohnhaus anklicken – 25 % der Baukosten zurück (Zonengebäude: Entzonen)"));
            panelBody.Add(tiles);

            panelBody.Add(Info(() =>
                $"{city.PlayerHouseCount} gesetzte Häuser · Stufe 2: Strom, Wasser, Laden in {HouseRules.ShopWalkRadius:0} m, Grundschule · " +
                "Stufe 3: + Polizei, Gesundheit, Abwasser, Park, Oberschule/Bibliothek · Zonen bleiben zusätzlich (Z)"));
        }

        void AddHouseBulldozeTile(VisualElement tiles)
        {
            if (Houses == null) return;
            tiles.Add(Tile("Wohnhaus", "Wohnhäuser", DangerSwatch, HudIconKind.LandValue,
                () => Houses.Select(HouseTool.HouseAction.Remove),
                () => Houses.Active && Houses.Current == HouseTool.HouseAction.Remove,
                "Vom Spieler gesetztes Wohnhaus anklicken – 25 % der Baukosten zurück"));
        }
    }
}
