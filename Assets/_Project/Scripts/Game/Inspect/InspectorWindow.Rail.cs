using System.Linq;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Rail;

namespace CityBuilder.Game.Inspect
{
    /// <summary>Bahn im Inspektor: Zug (Gattung, Zustand, Tempo, Halte, Fahrgäste/Ladung), Bahnhof (Betrieb, Bahnsteige, Fahrgäste), Bürger im Zug.</summary>
    public sealed partial class InspectorWindow
    {
        bool RailHead(Ui ui, SimulationWorld world, Selection s)
        {
            var r = world.Rail;
            if (r == null) return false;
            if (s.Kind == SelectionKind.Train)
            {
                var t = r.GetTrain(s.Id);
                if (t == null) return false;
                StandardHead(ui, "Bahn", Tone.Info, $"{t.Type.DisplayName} #{t.Id}", TrainStateText(r, t), true);
                return true;
            }
            if (s.Kind == SelectionKind.Station)
            {
                var st = r.GetStation(s.Id);
                if (st == null) return false;
                StandardHead(ui, "Bahn", st.InService ? Tone.Good : Tone.Warn, st.Name,
                    $"{st.Type.DisplayName} · {st.Platforms.Count} Gleise · {(st.InService ? "in Betrieb" : StationProblem(st))}", false);
                return true;
            }
            return false;
        }

        static string TrainStateText(RailSystem r, Train t)
        {
            var next = r.GetStation(t.NextStationId);
            switch (t.State)
            {
                case CityBuilder.Simulation.Rail.TrainState.Waiting: return "wartet vor der Karte auf freie Strecke";
                case CityBuilder.Simulation.Rail.TrainState.Dwelling: return $"hält in {next?.Name}";
                default: return t.AtSignal ? "hält am Signal (Block voraus belegt)" : $"{t.Speed * 3.6f:0} km/h" + (next != null ? $" · nächster Halt {next.Name}" : " · ohne Halt");
            }
        }

        static string StationProblem(Station st) =>
            !st.HasAccess ? "keine Straße vor dem Gebäude" : !st.OnMainline ? "kein Bahnsteig an der Hauptstrecke (Weiche/Gleis fehlt)" : "–";

        bool RailBody(Ui ui, SimulationWorld world, Selection s)
        {
            var r = world.Rail;
            if (r == null) return false;
            if (s.Kind == SelectionKind.Train)
            {
                var t = r.GetTrain(s.Id);
                if (t == null) return false;
                if (t.Type.Kind == TrainKind.Intercity)
                    using (ui.Card())
                        ui.Meter("Fahrgäste", $"{t.Passengers}/{t.Type.Capacity}", t.Passengers / (float)System.Math.Max(1, t.Type.Capacity),
                            t.Passengers >= t.Type.Capacity ? Tone.Bad : Tone.Good);
                using (ui.Fields())
                {
                    ui.Field("Gattung", $"{t.Type.DisplayName} · {t.Type.Cars.Count} Fahrzeuge · {t.Length:0} m · max. {t.Type.MaxSpeed * 3.6f:0} km/h");
                    ui.Field("Zustand", TrainStateText(r, t), t.AtSignal ? Tone.Warn : Tone.None);
                    ui.Field("Von → nach", $"{world.Gateways?.Get(t.FromGatewayId)?.TargetName ?? "–"} → {world.Gateways?.Get(t.ToGatewayId)?.TargetName ?? "–"}");
                    ui.Field("Fahrweg", $"{t.RouteEdges.Count} Blockabschnitte · {t.RouteLength:0} m · {t.HeadS / System.Math.Max(1f, t.RouteLength) * 100f:0} % gefahren");
                    if (t.DelaySeconds > 1) ui.Field("Einfahrt verzögert", $"{TimeModel.GameMinutes(t.DelaySeconds):0} Spielminuten", Tone.Warn);
                    if (t.Type.Kind == TrainKind.Intercity)
                        ui.Field("Fahrgäste", $"{t.ThroughPassengers} Durchreisende · {t.CityPassengers} von/nach hier");
                    else ui.Field("Ladung", $"{t.Loads} Lkw-Ladungen ({t.Type.Wagons} Wagen)");
                }
                ui.Section("Halte");
                var stops = r.StopsOf(t);
                if (stops.Count == 0) ui.Note("Durchfahrt ohne Halt.");
                foreach (var (stationId, at) in stops)
                {
                    var st = r.GetStation(stationId);
                    bool passed = t.HeadS > at + 1f;
                    ui.Field(st?.Name ?? $"Bahnhof #{stationId}", passed ? "erledigt" : $"in {at - t.HeadS:0} m", passed ? Tone.Muted : Tone.None);
                }
                if (t.CityPassengers > 0)
                {
                    ui.Section("Fahrgäste aus der Stadt");
                    int shown = 0;
                    foreach (var p in r.RidersOf(t))
                    {
                        var c = world.Citizens.Get(p.CitizenId);
                        if (c == null) continue;
                        if (shown++ >= 8) { ui.Note($"… und {t.CityPassengers - 8} weitere"); break; }
                        var who = c;
                        ui.ListItem(Ui.Initials(c.FullName), Ui.AvatarColor(c.Id), c.FullName,
                            p.Outbound ? "fährt aus der Stadt" : $"steigt aus in {r.GetStation(p.StationId)?.Name ?? "–"}", null, Tone.None,
                            () => Go(SelectionKind.Citizen, who.Id));
                    }
                }
                using (ui.Actions())
                    ui.ActionButton(panel.Following ? "Folgen aus" : "Folgen", () => panel.Following = !panel.Following,
                        ActionStyle.Secondary, panel.Following, true, "Kamera folgt dem Zug (F)");
                return true;
            }
            if (s.Kind == SelectionKind.Station)
            {
                var st = r.GetStation(s.Id);
                if (st == null) return false;
                using (ui.Grid(2))
                {
                    if (st.IsPassenger)
                    {
                        ui.Stat("Fahrgäste", (st.AlightedYesterday + st.BoardedYesterday).ToString(), $"gestern ({st.AlightedYesterday} aus, {st.BoardedYesterday} ein)");
                        ui.Stat("Wartende", st.WaitingCount.ToString(), $"{st.StopsYesterday} Halte gestern");
                    }
                    else
                    {
                        ui.Stat("Umschlag", st.LoadsYesterday.ToString(), $"Ladungen gestern (heute {st.LoadsToday}/{r.FreightCapacityPerDay})");
                        ui.Stat("Güterzüge", st.StopsYesterday.ToString(), "Halte gestern");
                    }
                }
                using (ui.Fields())
                {
                    ui.Field("Betrieb", st.InService ? "in Betrieb" : StationProblem(st), st.InService ? Tone.Good : Tone.Bad);
                    ui.Field("Straßenzufahrt", st.HasAccess ? $"Straße {st.AccessSegmentId}" : "keine (Straße vor die Vorderseite bauen)", st.HasAccess ? Tone.None : Tone.Bad);
                    foreach (var p in st.Platforms)
                    {
                        int owner = r.OwnerOf(p.SegmentId);
                        ui.Field($"Gleis {p.Number}", owner != 0 ? $"belegt (Zug #{owner})" : "frei", owner != 0 ? Tone.Accent : Tone.None);
                    }
                    ui.Field("Baukosten", BuildLabels.Money((float)st.Type.Cost));
                    ui.Field("Unterhalt", $"{BuildLabels.Money((float)st.Type.UpkeepPerWeek)}/Woche");
                    if (st.IsPassenger)
                        ui.Field("Entgelte", $"{RailRules.IntercityStopFee:0} € je Halt + {RailRules.PassengerFee:0.00} € je Fahrgast");
                    else ui.Field("Entgelte", $"{RailRules.FreightStopFee:0} € je Güterzug + {RailRules.FreightLoadFee:0} € je Ladung · Fracht {RailRules.RailFreightShare * 100f:0.#} % statt Lkw");
                }
                return true;
            }
            return false;
        }

        /// <summary>Bürger auf Bahnfahrt: geht zum Bahnhof/wartet, wartet draußen auf den Zug bzw. sitzt im Zug (anklickbar).</summary>
        void CitizenRailFields(Ui ui, SimulationWorld world, Citizen c)
        {
            var r = world.Rail;
            var p = r.RideOf(c.Id);
            var st = r.GetStation(p.StationId);
            switch (p.State)
            {
                case RailRideState.OnTrain:
                    var t = r.GetTrain(p.TrainId);
                    if (t != null) ui.FieldLink("Verkehrsmittel", $"Zug – {t.Type.DisplayName} #{t.Id}", () => Go(SelectionKind.Train, t.Id));
                    else ui.Field("Verkehrsmittel", "Zug");
                    break;
                case RailRideState.Outside:
                    ui.Field("Verkehrsmittel", $"kommt mit dem nächsten Fernzug nach {st?.Name}");
                    break;
                default:
                    ui.Field("Verkehrsmittel", p.IsWaitingAtStation(world.Traffic.Time) ? $"wartet am Bahnsteig in {st?.Name}" : $"auf dem Weg zum Bahnhof {st?.Name}");
                    break;
            }
            ui.Field("Richtung", p.Outbound ? "aus der Stadt hinaus" : "in die Stadt");
        }
    }
}
