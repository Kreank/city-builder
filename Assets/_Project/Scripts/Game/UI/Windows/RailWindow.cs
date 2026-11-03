using System.Linq;
using CityBuilder.Game.Rail;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Rail;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Bahnübersicht (rechtes Panel, Baukasten, <b>F7</b> bzw. offen im Bahn-Werkzeug 7): Hauptstrecke und Rail-Gateways,
    /// Bahnhöfe (Betrieb, Bahnsteige, Fahrgäste, Umschlag), Züge (Zustand, Tempo, nächster Halt, Fahrgäste, Signalhalt),
    /// Bahnübergänge, Kasse.
    /// </summary>
    public sealed class RailWindow : GameWindow
    {
        bool open;

        public RailWindow() : base(WindowSlot.Right) { }

        public override float OpenScore
        {
            get
            {
                if (World?.Rail == null) return 0f;
                if (Tool.CurrentMode == RoadTool.Mode.Rail) return 88f;
                return open ? 58f : 0f;
            }
        }

        public override void Close()
        {
            open = false;
            base.Close();
        }

        public override bool HandleEscape()
        {
            if (Tool.CurrentMode == RoadTool.Mode.Rail) return false; // Esc gehört dem Werkzeug
            Close();
            return true;
        }

        public override void Poll()
        {
            var kb = GameKeyboard.Current;
            if (kb != null && kb.f7Key.wasPressedThisFrame) { open = !open; Dismissed = false; }
        }

        public override void DrawHead(Ui ui)
        {
            var r = World.Rail;
            Head(ui, "Bahn (7 · F7)", Tone.Info, "Bahnübersicht",
                $"{(r.HasMainline ? "Fernbahn-Hauptstrecke" : "keine Hauptstrecke")} · {r.Stations.Count} Bahnhöfe · {r.Trains.Count} Züge · Unterhalt {BuildLabels.Money((float)r.WeeklyUpkeep())}/Woche");
        }

        public override void DrawBody(Ui ui)
        {
            var world = World;
            var r = world.Rail;
            using (ui.Grid(3))
            {
                ui.Stat("Fahrgäste", BuildLabels.Number(r.PassengersYesterday), $"gestern (heute {r.PassengersToday})");
                ui.Stat("Züge", BuildLabels.Number(r.TrainsCompleted), $"durchgefahren · {r.TrainsCancelled} ausgefallen");
                ui.Stat("Entgelte", BuildLabels.Money((float)r.FaresTotal), "gesamt");
            }
            if (!r.HasMainline) ui.Message("Keine Hauptstrecke", "Diese Karte hat keinen Bahnkorridor (Spielstand ohne Außenanbindung).", Tone.Warn);
            else if (!r.HasPassengerService && !r.HasFreightService)
                ui.Message("Bahnhof fehlt", "Die Hauptstrecke liegt schon. Einen Personenbahnhof direkt an die Gleise setzen und eine Straße vor das Gebäude bauen – dann halten Fernzüge, Leute ziehen per Bahn zu.", Tone.Warn);

            ui.Section("Hauptstrecke");
            using (ui.Fields())
            {
                foreach (var g in world.Gateways.All.Where(x => x.Kind == GatewayKind.Rail))
                    ui.Field(g.Name, $"{g.StatusText} · Zuzug {g.TotalMoveIns} · Handel heute {g.Today.Imports + g.Today.Exports}", g.IsOperational ? Tone.Good : Tone.Warn);
                ui.Field("Fahrplan", $"Fernzug stündlich je Richtung {RailRules.FirstIntercityHour}–{RailRules.LastIntercityHour} Uhr · Güterzug alle {RailRules.FreightEveryHours} h");
                ui.Field("Nächster Fernzug", $"in {Simulation.Core.TimeModel.GameMinutes(r.SecondsToNextIntercity()):0} Spielminuten");
            }

            ui.Section("Bahnhöfe");
            if (r.Stations.Count == 0) ui.Note("Noch kein Bahnhof – Bauleiste „Bahn“ (7).");
            foreach (var s in r.Stations)
            {
                string state = s.InService ? "in Betrieb" : !s.HasAccess ? "keine Straße vor dem Gebäude" : "Bahnsteig nicht an der Hauptstrecke";
                string sub = s.IsPassenger
                    ? $"{s.Platforms.Count} Gleise · gestern {s.AlightedYesterday} aus / {s.BoardedYesterday} ein · {s.WaitingCount} warten · {s.StopsYesterday} Halte"
                    : $"{s.Platforms.Count} Ladegleise · Umschlag gestern {s.LoadsYesterday} Ladungen (heute {s.LoadsToday}/{r.FreightCapacityPerDay}) · {s.StopsYesterday} Güterzüge";
                ui.ListItem(s.IsPassenger ? "P" : "G", s.IsPassenger ? new UnityEngine.Color(0.2f, 0.45f, 0.8f) : new UnityEngine.Color(0.85f, 0.55f, 0.15f),
                    s.Name, sub, state, s.InService ? Tone.Good : Tone.Warn);
            }

            ui.Section("Züge", "Blockabschnitte: ein Zug je Gleisstück");
            if (r.Trains.Count == 0) ui.Note("Gerade kein Zug unterwegs.");
            foreach (var t in r.Trains)
            {
                var next = r.GetStation(t.NextStationId);
                string state = t.State == TrainState.Waiting ? "wartet vor der Karte" : t.State == TrainState.Dwelling ? $"hält in {next?.Name}"
                    : t.AtSignal ? "hält am Signal" : $"{t.Speed * 3.6f:0} km/h";
                string sub = t.Type.Kind == TrainKind.Intercity
                    ? $"{t.Passengers} Fahrgäste ({t.CityPassengers} von/nach hier) · " + (next != null ? $"nächster Halt {next.Name}" : "ohne Halt")
                    : $"{t.Loads} Ladungen · " + (next != null ? $"Umschlag in {next.Name}" : "Durchfahrt");
                ui.ListItem(t.Type.Kind == TrainKind.Intercity ? "F" : "G", t.Type.Kind == TrainKind.Intercity ? new UnityEngine.Color(0.85f, 0.15f, 0.15f) : new UnityEngine.Color(0.3f, 0.3f, 0.35f),
                    $"{t.Type.DisplayName} #{t.Id}", sub, state, t.AtSignal ? Tone.Warn : Tone.Good);
            }

            ui.Section("Bahnübergänge");
            using (ui.Fields())
            {
                int closed = r.Crossings.Count(c => c.Closed);
                ui.Field("Anzahl", $"{r.Crossings.Count} · gerade geschlossen {closed} · heute {r.Crossings.Sum(c => c.ClosuresToday)} × geschlossen");
                ui.Field("Fahrgäste unterwegs", $"{r.ActivePassengers} (davon {r.WaitingOutside} kommen gleich an)");
                ui.Field("Aufgegeben heute", r.GaveUpToday.ToString(), r.GaveUpToday > 0 ? Tone.Warn : Tone.None);
            }
            using (ui.Actions())
                ui.ActionButton(RailView.ShowSignals ? "Signale ausblenden" : "Signale zeigen", () => RailView.ShowSignals = !RailView.ShowSignals,
                    ActionStyle.Secondary, RailView.ShowSignals);
        }
    }
}
