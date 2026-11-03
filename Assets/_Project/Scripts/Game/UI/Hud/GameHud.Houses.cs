using CityBuilder.Game.Roads;
using CityBuilder.Game.Zoning;
using CityBuilder.Simulation.Zoning;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>
    /// Bauleiste, Kategorie „Bauen“ (Taste 6, Anno-Stil; „Setzen statt Zonen“): Reiter Wohnen / Gewerbe / Industrie, je Bauart
    /// eine Kachel mit Nachfrage-Balken ihrer Art, Drehen, Abriss-Kachel. LMB halten und ziehen setzt eine Reihe
    /// (<see cref="HouseTool"/>). Eigener Block, damit <c>GameHud.BuildBar.cs</c> nur Einhängepunkte bekommt. Die
    /// Anforderungen am Mauszeiger zeigt das Haus-Fenster (<c>HouseWindow</c>, rechts) und die Hinweiszeile.
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

        /// <summary>Hat der Spielstand gemalte Zonen (alter Stand)? Dann bleibt die Kategorie „Zonen“ sichtbar.</summary>
        bool HasOldZones()
        {
            var c = runner != null && runner.World != null ? runner.World.City : null;
            return c != null && (c.Stats.ZonedCells > 0 || currentCategory == Category.Zones);
        }

        /// <summary>Hinweiszeile im Bau-Werkzeug (Kosten, Fehler, was am Bauplatz fehlt, Reihe). false = nichts zu sagen.</summary>
        bool HouseHint(out string text, out bool error)
        {
            text = null;
            error = false;
            if (tool.CurrentMode != RoadTool.Mode.House || Houses == null || !tool.HasCursor || string.IsNullOrEmpty(Houses.HintText)) return false;
            text = Houses.HintText;
            error = Houses.HintError;
            return true;
        }

        static Color GroupSwatch(PlaceableGroup g) =>
            g == PlaceableGroup.Residential ? HouseSwatch : g == PlaceableGroup.Commercial ? BuildLabels.ZoneColor(ZoneType.Retail) : BuildLabels.ZoneColor(ZoneType.Industry);

        void BuildHousePanel()
        {
            Header("Bauen", "Klick setzt ein Gebäude · LMB halten und entlang der Straße ziehen = Reihe · , und . drehen · RMB/Esc bricht die Reihe ab");
            var ht = Houses;
            if (ht == null) { panelBody.Add(Info(() => "Bau-Werkzeug fehlt.")); return; }
            var city = runner.World.City;

            var tools = Container("cb-tool-row");
            var tabs = Container("cb-segment");
            foreach (PlaceableGroup group in System.Enum.GetValues(typeof(PlaceableGroup)))
            {
                var g = group;
                tabs.Add(PBtn(HouseCatalog.GroupName(g), () => ht.SelectType(HouseCatalog.InGroup(g)[0]),
                    () => ht.SelectedType.Group == g,
                    g == PlaceableGroup.Residential ? "Wohnhäuser: bevölkern sich, steigen mit Laden, Schule, Polizei … in Reichweite auf"
                    : g == PlaceableGroup.Commercial ? "Laden, Wohn- und Geschäftshaus, Bürohaus – ein Händler/Dienstleister zieht ein"
                    : "Gewerbe- und Industriehallen – welcher Betrieb einzieht, entscheidet die Simulation (Vorkommen, Nachfrage, Schwerpunkt)",
                    "cb-seg-btn"));
            }
            tools.Add(tabs);
            tools.Add(PLabel("Drehung"));
            tools.Add(PBtn("−15°", () => ht.Rotate(-15f), null, "Gebäude drehen (,) – nur Einzelbau; Reihen stehen immer mit der Vorderseite zur Straße"));
            tools.Add(PValue(() => $"{ht.Rotation:0}°"));
            tools.Add(PBtn("+15°", () => ht.Rotate(15f), null, "Gebäude drehen (.)"));
            tools.Add(Container("cb-spacer"));
            tools.Add(PValue(() => ht.Preview != null && ht.Active ? $"Hier erreichbar: Stufe {ht.Preview.ReachableLevel} von {ht.Preview.MaxLevel}" : ""));
            panelBody.Add(tools);

            var tiles = Container("cb-tiles");
            foreach (var type in HouseCatalog.All)
            {
                var t = type;
                var kind = DemandReport.KindOf(t.Zone);
                string sub = t.HasHomes && t.JobsAtLevel(1) > 0 ? $"{t.HouseholdsAtLevel(1)} Whg. · {t.JobsAtLevel(1)} Jobs"
                    : t.HasHomes ? $"{t.HouseholdsAtLevel(1)}–{t.HouseholdsAtLevel(HouseRules.MaxLevelFor(t.Zone))} Whg."
                    : $"{t.JobsAtLevel(1)}–{t.JobsAtLevel(HouseRules.MaxLevelFor(t.Zone))} Jobs";
                var tile = Tile(t.DisplayName, sub, GroupSwatch(t.Group), t.Group == PlaceableGroup.Industrial ? HudIconKind.Briefcase : HudIconKind.LandValue,
                    () => ht.SelectType(t),
                    () => ht.Active && ht.Current == HouseTool.HouseAction.Place && ht.SelectedType == t,
                    () => $"{t.DisplayName}: {t.Description} · Grundstück {t.Width:0}×{t.Depth:0} m · Baukosten {BuildLabels.Money(t.Cost)} · " +
                          $"Nachfrage {DemandReport.NameOf(kind)} {Mathf.Clamp(DemandReport.ValueOf(city, kind), 0f, 100f):0} %");
                // Nachfrage ihrer Art als Balken (wie früher die Zonenkacheln)
                var bar = Container("cb-bar");
                var fill = Container("cb-bar__fill");
                fill.style.backgroundColor = BuildLabels.ZoneColor(t.Zone);
                bar.Add(fill);
                tile.Add(bar);
                panelSlowRefresh.Add(() => fill.style.width = Length.Percent(Mathf.Clamp(DemandReport.ValueOf(city, kind), 0f, 100f)));
                panelFastRefresh.Add(() => tile.style.display = ht.SelectedType.Group == t.Group ? DisplayStyle.Flex : DisplayStyle.None);
                tiles.Add(tile);
            }
            tiles.Add(Container("cb-tile-sep"));
            tiles.Add(Tile("Abreißen", "25 % zurück", DangerSwatch, HudIconKind.Bulldoze,
                () => ht.Select(HouseTool.HouseAction.Remove),
                () => ht.Active && ht.Current == HouseTool.HouseAction.Remove,
                "Gesetztes Gebäude anklicken – 25 % der Baukosten zurück (Zonengebäude alter Stände: Entzonen)"));
            panelBody.Add(tiles);

            panelBody.Add(Info(() =>
            {
                var g = ht.SelectedType.Group;
                string rules = g == PlaceableGroup.Residential
                    ? $"Stufe 2: Strom, Wasser, Laden in {HouseRules.ShopWalkRadius:0} m, Grundschule · Stufe 3: + Polizei, Gesundheit, Abwasser, Park, Oberschule/Bibliothek"
                    : g == PlaceableGroup.Commercial
                        ? "Laden: Kundschaft im Umkreis, Strom/Wasser, dann Abwasser + Polizei · Büro: Fachkräfte mit Studium, dann Polizei + Bibliothek"
                        : "Halle: Strom/Wasser + Autobahn in der Nähe, dann Abwasser, Feuerwehr, Facharbeiter · Betrieb nach Vorkommen/Nachfrage – lenken mit Z";
                return $"{city.PlayerHouseCount} gesetzte Gebäude · {rules}";
            }));
        }

        void AddHouseBulldozeTile(VisualElement tiles)
        {
            if (Houses == null) return;
            tiles.Add(Tile("Gebäude", "gesetzt", DangerSwatch, HudIconKind.LandValue,
                () => Houses.Select(HouseTool.HouseAction.Remove),
                () => Houses.Active && Houses.Current == HouseTool.HouseAction.Remove,
                "Vom Spieler gesetztes Gebäude (Wohnhaus, Laden, Büro, Halle) anklicken – 25 % der Baukosten zurück"));
        }
    }
}
