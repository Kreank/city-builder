using CityBuilder.Game.Aviation;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Aviation;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>
    /// Bauleiste, Kategorie „Flughafen“ (Taste 9): Regionalflugplatz, Verkehrsflughafen, Terminal ausbauen, Abriss; Werkzeugzeile
    /// mit Drehen/Wind/Terminal-Seite/Nachtflugverbot/Einflugschneisen; Warnchip „Flughafen“ (außer Betrieb, Schneise gesperrt).
    /// </summary>
    public sealed partial class GameHud
    {
        static readonly Color AirportSwatch = BuildLabels.Hex(0x5B6FB8);
        AirportTool airportTool;

        AirportTool Airports => airportTool != null ? airportTool : airportTool = tool.GetComponent<AirportTool>();

        Category? AirportCategory() => Airports != null && Airports.Current == AirportTool.AirportAction.Remove ? Category.Bulldoze : Category.Airport;

        void SelectAirportCategory()
        {
            if (Airports != null) Airports.Select(Airports.Current == AirportTool.AirportAction.Remove ? AirportTool.AirportAction.Airfield : Airports.Current);
            else tool.SetMode(RoadTool.Mode.Airport);
        }

        void BuildAirportPanel()
        {
            Header("Flughafen", "Mauszeiger = Bahnmitte · , . drehen (⇧ 1°) · Enter Terminal-Seite · Einflugschneisen (ansteigende Flächen) müssen frei von Hochbauten bleiben · Esc/RMB zurück");
            var at = Airports;
            var av = runner.World.Aviation;
            if (at == null || av == null) { panelBody.Add(Info(() => "Flughafen-Werkzeug fehlt.")); return; }

            var tools = Container("cb-tool-row");
            tools.Add(PBtn("Nach Wind", () => at.AlignToWind(), () => false,
                $"Bahn in die Hauptwindrichtung drehen (Start und Landung gegen den Wind) – hier Wind meist aus {AviationRules.WindText(av.PrevailingWind)}"));
            tools.Add(PBtn("Terminal-Seite", () => at.FlipSide(), () => false, "Terminal und Vorfeld auf die andere Seite der Bahn (Enter)"));
            tools.Add(PBtn("Nachtflugverbot", () => av.SetNightBan(!av.NightBan), () => av.NightBan,
                "22–6 Uhr keine Starts und Landungen: nachts still, aber weniger Flüge (ein Teil der Nachfrage weicht auf den Tag aus)"));
            tools.Add(PBtn("Einflugschneisen", () => AirportView.ShowCorridors = !AirportView.ShowCorridors, () => AirportView.ShowCorridors,
                "Einflugschneisen und Hindernisse immer zeigen (im Flughafen-Werkzeug ohnehin sichtbar)"));
            panelBody.Add(tools);

            var tiles = Container("cb-tiles");
            foreach (var type in AirportCatalog.All)
            {
                var t = type;
                var action = t.Class == AirportClass.Airfield ? AirportTool.AirportAction.Airfield : AirportTool.AirportAction.Airport;
                float len = t.RunwayLengthFor(runner.World.Settings.SizeMeters);
                tiles.Add(Tile(t.DisplayName, BuildLabels.Money((float)t.Cost), AirportSwatch, HudIconKind.Plane,
                    () => at.Select(action),
                    () => at.Active && at.Current == action,
                    $"{t.DisplayName} · Bahn {len:0} m" + (len < t.NominalRunwayLength ? $" (echt {t.NominalRunwayLength:0} m – auf dieser Karte verkürzt)" : "") +
                    $" × {t.RunwayWidth:0} m · Anflugfläche {t.ApproachSlope * 100f:0} % Neigung, {t.ApproachLength / 1000f:0.#} km · " +
                    $"{t.MovementsPerHour:0} Flugbewegungen/h · Terminal {t.TerminalPaxPerHour[0]:0} Fluggäste/h, {t.Stands[0]} Positionen (Ausbau bis {t.Stands[t.MaxLevel - 1]}) · " +
                    (t.CargoLoadsPerDay > 0 ? $"Luftfracht {t.CargoLoadsPerDay} Ladungen/Tag (Elektronik) · " : "") +
                    $"Unterhalt {BuildLabels.Money((float)t.UpkeepPerWeek)}/Woche · Gelände wird eingeebnet (Erdarbeiten kosten)"));
            }
            tiles.Add(Tile("Terminal ausbauen", "Ausbau", AirportSwatch, HudIconKind.LandValue,
                () => at.Select(AirportTool.AirportAction.Expand),
                () => at.Active && at.Current == AirportTool.AirportAction.Expand,
                "Flughafen anklicken: mehr Terminal-Module, Abstellpositionen und Fluggäste je Stunde"));
            panelBody.Add(tiles);

            panelBody.Add(Info(() =>
            {
                string hint = at.Active && !string.IsNullOrEmpty(at.Label) ? at.Label + "  ·  " : "";
                int inService = 0;
                foreach (var a in av.Airports) if (a.InService) inService++;
                string warn = av.Warning;
                return hint + $"{av.Airports.Count} Flughäfen ({inService} in Betrieb) · {av.AllAircraft.Count} Flugzeuge sichtbar · " +
                       $"{av.VisitorsPresent:0} Besucher in der Stadt" + (av.NightBan ? " · Nachtflugverbot" : "") + (warn != null ? " · " + warn : "") + " · F12 Übersicht";
            }));
        }

        void AddAirportBulldozeTile(VisualElement tiles)
        {
            if (Airports == null) return;
            tiles.Add(Tile("Flughafen", "Flughafen", DangerSwatch, HudIconKind.Plane,
                () => Airports.Select(AirportTool.AirportAction.Remove),
                () => Airports.Active && Airports.Current == AirportTool.AirportAction.Remove,
                "Flughafen anklicken – 25 % zurück (Flugzeuge verschwinden)"));
        }

        // --- Warnchip: Flughafen außer Betrieb oder Einflugschneise gesperrt; Klick zeigt die Schneisen
        VisualElement AirportChip()
        {
            var chip = El("cb-chip-warn", "cb-hidden");
            chip.Add(Icon(HudIconKind.Plane));
            var text = Text("", "cb-chip-warn__text");
            chip.Add(text);
            string warn = null;
            OnClick(chip, () => AirportView.ShowCorridors = true);
            slowRefresh.Add(() =>
            {
                warn = runner.World.Aviation?.Warning;
                chip.EnableInClassList("cb-hidden", warn == null);
                text.text = "Flughafen";
            });
            Tip(chip, () => (warn ?? "") + " · Klick: Einflugschneisen zeigen");
            return chip;
        }
    }
}
