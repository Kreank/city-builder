using CityBuilder.Game.Rail;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Rail;
using CityBuilder.Simulation.Roads;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>Bauleiste, Kategorie „Bahn“ (Taste 7): Gleis, Gleisbogen, Personen-/Güterbahnhof, Abriss – eigener Block.</summary>
    public sealed partial class GameHud
    {
        static readonly Color RailSwatch = BuildLabels.Hex(0x9C5B3B);
        RailTool railTool;

        RailTool Rails => railTool != null ? railTool : railTool = tool.GetComponent<RailTool>();

        Category? RailCategory() => Rails != null && Rails.Current == RailTool.RailAction.Remove ? Category.Bulldoze : Category.Rail;

        void SelectRailCategory()
        {
            if (Rails != null) Rails.Select(Rails.Current == RailTool.RailAction.Remove ? RailTool.RailAction.Track : Rails.Current);
            else tool.SetMode(RoadTool.Mode.Rail);
        }

        void BuildRailPanel()
        {
            Header("Bahn", "Gleis: Start/Ende anklicken (am Gleis = Weiche, tangential) · Bild↑/↓ Bauhöhe · Bahnhof rastet am Gleis ein · Esc/RMB abbrechen");
            var rt = Rails;
            var rail = runner.World.Rail;
            if (rt == null || rail == null) { panelBody.Add(Info(() => "Bahn-Werkzeug fehlt.")); return; }

            var tools = Container("cb-tool-row");
            tools.Add(PLabel("Bauhöhe"));
            tools.Add(PBtn("−", () => rt.ChangeHeight(-5f), null, "Tiefer bauen (Tunnel, Bild↓)"));
            tools.Add(PValue(() => Mathf.Abs(rt.HeightOffset) < 0.01f ? "auto" : $"{rt.HeightOffset:+0;-0} m"));
            tools.Add(PBtn("+", () => rt.ChangeHeight(5f), null, "Höher bauen (Brücke, Bild↑) · 0 = automatische Trassierung"));
            tools.Add(Container("cb-spacer"));
            tools.Add(PBtn("Signale", () => RailView.ShowSignals = !RailView.ShowSignals, () => RailView.ShowSignals, "Blocksignale an den Gleisstücken zeigen (rot = Block belegt)"));
            panelBody.Add(tools);

            var tiles = Container("cb-tiles");
            tiles.Add(Tile("Gleis", $"{RoadCatalog.RailTrack.CostPerMeter:0} €/m", RailSwatch, HudIconKind.Train,
                () => rt.Select(RailTool.RailAction.Track),
                () => rt.Active && rt.Current == RailTool.RailAction.Track,
                $"Gleis bauen · max. {RoadType.RailMaxSlope * 100f:0.#} % Steigung · Radius ≥ {RailRules.MinRadius:0} m · Weichen zweigen tangential ab (≤ {RailRules.MaxSwitchAngle:0}°) · " +
                $"Straße queren: Bahnübergang oder Brücke/Tunnel · Unterhalt {RailRules.TrackUpkeepPerMeterWeek:0.0} €/m/Woche"));
            tiles.Add(Tile("Gleisbogen", "3 Klicks", RailSwatch, HudIconKind.Road,
                () => rt.Select(RailTool.RailAction.Curve),
                () => rt.Active && rt.Current == RailTool.RailAction.Curve,
                "Start, Kontrollpunkt, Ende – für Überholgleise, Bahnhofsschleifen und Anschlussgleise"));
            foreach (var type in StationCatalog.All)
            {
                var t = type;
                var action = t.Kind == StationKind.Passenger ? RailTool.RailAction.PassengerStation : RailTool.RailAction.FreightStation;
                tiles.Add(Tile(t.DisplayName, BuildLabels.Money((float)t.Cost), RailSwatch, t.Kind == StationKind.Passenger ? HudIconKind.People : HudIconKind.Briefcase,
                    () => rt.Select(action),
                    () => rt.Active && rt.Current == action,
                    t.Kind == StationKind.Passenger
                        ? $"{t.DisplayName} · {t.Width:0}×{t.Depth:0} m, Bahnsteig {t.PlatformLength:0} m · rastet am Gleis ein, Vorderseite zur Straße · " +
                          $"Fernzüge halten (stündlich je Richtung), Zuzug und Pendler per Bahn · Unterhalt {BuildLabels.Money((float)t.UpkeepPerWeek)}/Woche"
                        : $"{t.DisplayName} · {t.Width:0}×{t.Depth:0} m, Ladegleis {t.PlatformLength:0} m · Import/Export der Firmen per Güterzug " +
                          $"(Fracht {RailRules.RailFreightShare * 100f:0.#} % statt {Simulation.Goods.GoodsRules.WorldFreightShare * 100f:0} % per Lkw) · Unterhalt {BuildLabels.Money((float)t.UpkeepPerWeek)}/Woche"));
            }
            panelBody.Add(tiles);

            panelBody.Add(Info(() =>
            {
                string hint = rt.Active && !string.IsNullOrEmpty(rt.Label) ? rt.Label + "  ·  " : "";
                string main = rail.HasMainline ? "Hauptstrecke liegt" : "keine Hauptstrecke";
                int inService = 0;
                foreach (var s in rail.Stations) if (s.InService) inService++;
                return hint + $"{main} · {rail.Stations.Count} Bahnhöfe ({inService} in Betrieb) · {rail.Trains.Count} Züge · " +
                       $"{rail.PassengersYesterday} Fahrgäste gestern · Unterhalt {BuildLabels.Money((float)rail.WeeklyUpkeep())}/Woche · F7 Bahnübersicht";
            }));
        }

        void AddRailBulldozeTile(VisualElement tiles)
        {
            if (Rails == null) return;
            tiles.Add(Tile("Gleis/Bahnhof", "Bahn", DangerSwatch, HudIconKind.Train,
                () => Rails.Select(RailTool.RailAction.Remove),
                () => Rails.Active && Rails.Current == RailTool.RailAction.Remove,
                "Gleisstück oder Bahnhof anklicken – 25 % zurück (Hauptstrecke gehört der Welt, belegte Gleise warten auf den Zug)"));
        }
    }
}
