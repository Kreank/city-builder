using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Game.Transit;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Transit;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Linienübersicht (rechtes Panel, Baukasten): alle Linien mit Farbe, Fahrgästen, Auslastung, Pünktlichkeit und Takt;
    /// gewählte Linie mit Bussen (± ändert den Takt), Rund/Pendel, Haltestellen samt Wartenden, Löschen.
    /// Offen im ÖPNV-Werkzeug (Taste 5) oder über F6.
    /// </summary>
    public sealed class TransitWindow : GameWindow
    {
        bool open;
        int selectedLine;

        public TransitWindow() : base(WindowSlot.Right) { }

        public override float OpenScore
        {
            get
            {
                if (World?.Transit == null) return 0f;
                if (Tool.CurrentMode == RoadTool.Mode.Transit) return 90f;
                return open ? 60f : 0f;
            }
        }

        public override void Close()
        {
            open = false;
            TransitView.HighlightLine = 0;
            base.Close();
        }

        public override bool HandleEscape()
        {
            if (selectedLine != 0) { Select(0); return true; }
            if (Tool.CurrentMode == RoadTool.Mode.Transit) return false; // Esc gehört dem Werkzeug (Linie verwerfen)
            Close();
            return true;
        }

        public override void Poll()
        {
            var kb = GameKeyboard.Current;
            if (kb != null && kb.f6Key.wasPressedThisFrame)
            {
                open = !open;
                Dismissed = false;
                if (!open) TransitView.HighlightLine = 0;
            }
        }

        void Select(int lineId)
        {
            selectedLine = lineId;
            TransitView.HighlightLine = lineId;
            ContentVersion++;
        }

        public override void DrawHead(Ui ui)
        {
            var t = World.Transit;
            var line = t.GetLine(selectedLine);
            if (line != null)
            {
                Head(ui, "ÖPNV · Linie", Tone.Info, line.Name,
                    $"{(line.IsLoop ? "Rundlinie" : "Pendellinie")} · {line.Stops.Count} Halte · {line.LengthMeters / 1000f:0.0} km",
                    back: () => Select(0), chipDot: TransitView.LineColor(line));
                return;
            }
            Head(ui, "ÖPNV (5 · F6)", Tone.Info, "Linienübersicht",
                $"{t.Lines.Count} Linien · {t.Buses.Count} Busse · Unterhalt {BuildLabels.Money((float)t.WeeklyUpkeep())}/Woche");
        }

        public override void DrawBody(Ui ui)
        {
            var world = World;
            var t = world.Transit;
            var line = t.GetLine(selectedLine);
            if (selectedLine != 0 && line == null) selectedLine = 0;
            if (line != null) { LineBody(ui, t, line); return; }

            int passengers = t.Lines.Sum(l => l.PassengersYesterday);
            using (ui.Grid(3))
            {
                ui.Stat("Fahrgäste", BuildLabels.Number(passengers), "gestern");
                ui.Stat("Busse", $"{t.Buses.Count}/{t.VehicleCapacityTotal}", "im Einsatz");
                ui.Stat("Unterwegs", BuildLabels.Number(t.ActivePassengers), "Fahrgäste");
            }
            if (t.Depots.Count == 0) ui.Message("Kein Depot", "Ohne Busdepot fahren keine Busse. Depot in der Bauleiste „ÖPNV“ setzen.", Tone.Warn);

            ui.Section("Linien", "anklicken für Details");
            if (t.Lines.Count == 0) ui.Note("Noch keine Linie – Haltestellen setzen, dann „Linie zeichnen“.");
            foreach (var l in t.Lines)
            {
                var lineRef = l;
                string badge = !l.RouteValid ? "unterbrochen" : l.ActiveVehicles < l.TargetVehicles ? $"{l.ActiveVehicles}/{l.TargetVehicles} Busse" : $"{l.ActiveVehicles} Busse";
                Tone tone = !l.RouteValid ? Tone.Bad : l.ActiveVehicles < l.TargetVehicles ? Tone.Warn : Tone.Good;
                ui.ListItem(l.Id.ToString(), TransitView.LineColor(l), l.Name,
                    $"{l.PassengersYesterday} Fahrgäste gestern · Auslastung {l.AverageLoad * 100f:0} % · pünktlich {l.Punctuality * 100f:0} % · Takt {Headway(l)}",
                    badge, tone, () => Select(lineRef.Id));
            }
            using (ui.Fields())
            {
                ui.Field("Haltestellen", t.Stops.Count.ToString());
                ui.Field("Depots", t.Depots.Count.ToString());
                ui.Field("Einstiege gesamt", $"{BuildLabels.Number(t.TotalBoardings)} einzeln + {BuildLabels.Number(t.StatisticalBoardings)} statistisch");
                ui.Field("Stehen gelassen (voll)", BuildLabels.Number(t.TotalLeftBehind), t.TotalLeftBehind > 0 ? Tone.Warn : Tone.None);
                ui.Field("Aufgegeben", BuildLabels.Number(t.TotalGaveUp), t.TotalGaveUp > 0 ? Tone.Warn : Tone.None);
                ui.Field("Fahrgeld gesamt", BuildLabels.Money((float)t.FaresTotal));
            }
            using (ui.Actions())
                ui.ActionButton(TransitView.ShowLines ? "Linien ausblenden" : "Linien auf der Karte zeigen",
                    () => TransitView.ShowLines = !TransitView.ShowLines, ActionStyle.Secondary, TransitView.ShowLines);
            ui.Note("Busse fahren im normalen Verkehr (Stau trifft sie wie alle). Zeitmodell: 1 s Fahrzeit = " +
                    $"{Simulation.Core.TimeModel.Dilation:0} s Spielzeit – ein Umlauf von 15 min Fahrzeit dauert {Simulation.Core.TimeModel.GameMinutes(900) / 60:0} Spielstunden.");
        }

        void LineBody(Ui ui, TransitSystem t, TransitLine l)
        {
            using (ui.Grid(3))
            {
                ui.Stat("Fahrgäste", BuildLabels.Number(l.PassengersYesterday), $"gestern (heute {l.PassengersToday})");
                ui.Stat("Auslastung", $"{l.AverageLoad * 100f:0}", "%");
                ui.Stat("Pünktlich", $"{l.Punctuality * 100f:0}", "%");
            }
            if (!l.RouteValid)
                ui.Message("Route unterbrochen", l.BrokenLeg >= 0 ? $"Abschnitt {l.BrokenLeg + 1} ist für Busse nicht befahrbar." : "Weniger als 2 Haltestellen.", Tone.Bad);

            ui.Section("Betrieb");
            ui.Stepper("Busse", $"{l.TargetVehicles} (im Einsatz {l.ActiveVehicles})",
                () => t.SetLineVehicles(l.Id, l.TargetVehicles - 1), () => t.SetLineVehicles(l.Id, l.TargetVehicles + 1),
                $"Takt {Headway(l)}", Tone.Info);
            using (ui.Fields())
            {
                ui.Field("Umlauf", $"{l.CycleSeconds:0} s Verkehrszeit = {Simulation.Core.TimeModel.GameMinutes(l.CycleSeconds):0} Spielminuten (Fahrplan)");
                ui.Field("Länge", $"{l.LengthMeters / 1000f:0.00} km");
                ui.Field("Stehen gelassen heute", l.LeftBehindToday.ToString(), l.LeftBehindToday > 0 ? Tone.Warn : Tone.None);
                ui.Field("Fahrgäste gesamt", BuildLabels.Number(l.PassengersTotal));
                ui.Field("Unterhalt", $"{BuildLabels.Money((float)(l.ActiveVehicles * TransitRules.BusUpkeepPerWeek))}/Woche (Busse)");
            }
            using (ui.Actions())
            {
                ui.ActionButton("Rundlinie", () => t.SetLineLoop(l.Id, true), ActionStyle.Secondary, l.IsLoop);
                ui.ActionButton("Pendel", () => t.SetLineLoop(l.Id, false), ActionStyle.Secondary, !l.IsLoop);
                ui.ActionButton("Linie löschen", () => { t.RemoveLine(l.Id); Select(0); }, ActionStyle.Secondary);
            }

            ui.Section("Busse");
            var buses = t.BusesOf(l.Id).ToList();
            if (buses.Count == 0) ui.Note(t.Depots.Count == 0 ? "Kein Depot – es fahren keine Busse." : "Busse werden aus dem Depot eingesetzt …");
            foreach (var b in buses)
            {
                var next = t.GetStop(b.TargetStopId);
                string state = b.State == BusState.ToLine ? "Anfahrt vom Depot" : b.State == BusState.ToDepot ? "fährt ins Depot"
                    : b.Vehicle != null && b.Vehicle.IsAtStop ? $"hält: {next?.Name}" : $"→ {next?.Name}";
                ui.Meter($"Bus #{b.Id}", $"{b.Passengers}/{b.Capacity} · {state}{(b.LastLegOnTime ? "" : $" · +{b.LastDelaySeconds:0} s")}", b.Load,
                    b.Load >= 1f ? Tone.Bad : b.Load > 0.75f ? Tone.Warn : Tone.Good);
            }

            ui.Section("Haltestellen", "in Fahrtreihenfolge · Wartende dieser Linie");
            using (ui.Card())
                for (int i = 0; i < l.Stops.Count; i++)
                {
                    var s = t.GetStop(l.Stops[i]);
                    if (s == null) continue;
                    int index = i;
                    int waiting = s.WaitingFor(l.Id);
                    using (ui.Row())
                    {
                        ui.Text($"{i + 1}. {s.Name}", waiting > 20 ? Tone.Warn : Tone.None);
                        ui.Spacer();
                        ui.Text($"{waiting} warten · {t.LinesAt(s.Id).Count} Linien", Tone.Muted);
                        if (l.Stops.Count > 2) ui.IconButton(HudIconKind.Close, () => t.RemoveStopFromLine(l.Id, index), false, true, "Halt aus der Linie nehmen");
                    }
                }
            ui.Note(l.IsLoop ? "Rundlinie: nach dem letzten Halt zurück zum ersten." : "Pendel: am Ende wenden und zurück.");
        }

        static string Headway(TransitLine l) => float.IsInfinity(l.HeadwaySeconds) ? "–" : $"{Simulation.Core.TimeModel.GameMinutes(l.HeadwaySeconds):0} Spielmin."; // Zeitmodell
    }
}
