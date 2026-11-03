using System.Linq;
using CityBuilder.Game.Transit;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Transit;
using static CityBuilder.Game.Inspect.InspectorTexts;

namespace CityBuilder.Game.Inspect
{
    /// <summary>ÖPNV im Inspektor: Haltestelle (Linien, Wartende), Depot (Busse), Linienbus (Linie, Fahrgäste), Bürger im Bus.</summary>
    public sealed partial class InspectorWindow
    {
        bool TransitHead(Ui ui, SimulationWorld world, Selection s)
        {
            var t = world.Transit;
            if (t == null) return false;
            if (s.Kind == SelectionKind.TransitStop)
            {
                var stop = t.GetStop(s.Id);
                if (stop == null) return false;
                StandardHead(ui, "Haltestelle", Tone.Info, stop.Name,
                    $"{(stop.Side == StopSide.Both ? "beide Straßenseiten" : "eine Straßenseite")} · {t.LinesAt(stop.Id).Count} Linien · {stop.WaitingCount} warten", false);
                return true;
            }
            if (s.Kind == SelectionKind.TransitDepot)
            {
                var d = t.GetDepot(s.Id);
                if (d == null) return false;
                StandardHead(ui, "ÖPNV", Tone.Info, $"{d.Type.DisplayName} #{d.Id}",
                    $"{d.VehiclesOut}/{d.Type.VehicleCapacity} Busse im Einsatz · {d.Type.Width:0}×{d.Type.Depth:0} m", false);
                return true;
            }
            return false;
        }

        bool TransitBody(Ui ui, SimulationWorld world, Selection s)
        {
            var t = world.Transit;
            if (t == null) return false;
            if (s.Kind == SelectionKind.TransitStop)
            {
                var stop = t.GetStop(s.Id);
                if (stop == null) return false;
                using (ui.Grid(2))
                {
                    ui.Stat("Wartende", stop.WaitingCount.ToString());
                    ui.Stat("Eingestiegen", stop.BoardedYesterday.ToString(), $"gestern (heute {stop.BoardedToday})");
                }
                ui.Section("Linien");
                var lines = t.LinesAt(stop.Id);
                if (lines.Count == 0) ui.Note("Keine Linie hält hier.");
                foreach (var l in lines)
                    ui.ListItem(l.Id.ToString(), TransitView.LineColor(l), l.Name,
                        $"{stop.WaitingFor(l.Id)} warten · Takt {(float.IsInfinity(l.HeadwaySeconds) ? "–" : $"{l.HeadwaySeconds:0} s")}",
                        $"{l.ActiveVehicles} Busse", l.ActiveVehicles > 0 ? Tone.Good : Tone.Warn);
                using (ui.Fields())
                    ui.Field("Straße", $"Segment {stop.SegmentId}");
                return true;
            }
            if (s.Kind == SelectionKind.TransitDepot)
            {
                var d = t.GetDepot(s.Id);
                if (d == null) return false;
                using (ui.Card())
                    ui.Meter("Busse im Einsatz", $"{d.VehiclesOut}/{d.Type.VehicleCapacity}", d.VehiclesOut / (float)System.Math.Max(1, d.Type.VehicleCapacity), Tone.Info);
                using (ui.Fields())
                {
                    ui.Field("Zufahrt", d.HasAccess ? $"Segment {d.AccessSegmentId}" : "keine", d.HasAccess ? Tone.None : Tone.Bad);
                    ui.Field("Baukosten", BuildLabels.Money((float)d.Type.Cost));
                    ui.Field("Unterhalt", $"{BuildLabels.Money((float)d.Type.UpkeepPerWeek)}/Woche + {BuildLabels.Money((float)TransitRules.BusUpkeepPerWeek)} je Bus");
                }
                MoveAction(ui, SelectionKind.TransitDepot, d.Id, (float)d.Type.Cost); // UX: Verschieben
                ui.Section("Busse");
                foreach (var b in t.Buses.Where(x => x.DepotId == d.Id))
                {
                    var l = t.GetLine(b.LineId);
                    ui.Meter($"Bus #{b.Id} · {l?.Name}", $"{b.Passengers}/{b.Capacity}", b.Load, b.Load >= 1f ? Tone.Bad : Tone.Good);
                }
                return true;
            }
            return false;
        }

        string BusSubtitle(SimulationWorld world, Vehicle v)
        {
            var bus = world.Transit?.BusOf(v);
            if (bus == null) return null;
            var l = world.Transit.GetLine(bus.LineId);
            return $"{l?.Name ?? "Linienbus"} · {bus.Passengers}/{bus.Capacity} Fahrgäste";
        }

        /// <summary>Linienbus: Linie, Fahrgäste, nächster Halt. true = Inhalt gezeichnet.</summary>
        bool BusFields(Ui ui, SimulationWorld world, Vehicle v)
        {
            var t = world.Transit;
            var bus = t?.BusOf(v);
            if (bus == null) return false;
            var l = t.GetLine(bus.LineId);
            var next = t.GetStop(bus.TargetStopId);
            using (ui.Card())
                ui.Meter("Fahrgäste", $"{bus.Passengers}/{bus.Capacity}", bus.Load, bus.Load >= 1f ? Tone.Bad : bus.Load > 0.75f ? Tone.Warn : Tone.Good);
            using (ui.Fields())
            {
                if (l != null) ui.Field("Linie", $"{l.Name} ({(l.IsLoop ? "Rundlinie" : "Pendel")})");
                ui.Field("Zustand", bus.State == BusState.ToLine ? "Anfahrt vom Depot" : bus.State == BusState.ToDepot ? "fährt ins Depot"
                    : v.IsAtStop ? $"hält an {next?.Name}" : $"unterwegs nach {next?.Name}");
                ui.Field("Letzter Abschnitt", bus.LastLegOnTime ? "pünktlich" : $"+{bus.LastDelaySeconds:0} s verspätet", bus.LastLegOnTime ? Tone.Good : Tone.Warn);
                ui.Field("Halte bedient", bus.StopsServed.ToString());
                ui.Field("Geschwindigkeit", $"{v.Speed * 3.6f:0} km/h");
            }
            ui.Section("Fahrgäste");
            int shown = 0;
            foreach (var c in world.Citizens.Citizens)
            {
                if (world.Citizens.BusOf(c) != bus) continue;
                if (shown++ >= 8) { ui.Note($"… und {bus.Passengers - 8} weitere"); break; }
                var who = c;
                var ride = world.Citizens.RideOf(c);
                var stop = ride != null ? t.GetStop(ride.AlightStopId) : null;
                ui.ListItem(Ui.Initials(c.FullName), Ui.AvatarColor(c.Id), c.FullName, $"steigt aus: {stop?.Name ?? "–"}", null, Tone.None,
                    () => Go(SelectionKind.Citizen, who.Id));
            }
            if (bus.Passengers == 0) ui.Note("Leer.");
            using (ui.Actions())
                ui.ActionButton(panel.Following ? "Folgen aus" : "Folgen", () => panel.Following = !panel.Following,
                    ActionStyle.Secondary, panel.Following, true, "Kamera folgt dem Bus (F)");
            return true;
        }

        /// <summary>Bürger auf Busfahrt: wartet an Haltestelle bzw. sitzt in Bus (anklickbar).</summary>
        void CitizenTransitFields(Ui ui, SimulationWorld world, Citizen c)
        {
            var t = world.Transit;
            var ride = world.Citizens.RideOf(c);
            if (ride == null || t == null)
            {
                ui.Field("Verkehrsmittel", "Bus (statistisch gefahren bzw. Fußweg vom Halt)");
                long eta = c.PlannedArrivalTick - world.Clock.Tick;
                ui.Field("Ankunft", eta > 0 ? $"≈ {Clock(c.PlannedArrivalTick)} Uhr (in {Duration(eta)})" : "gleich");
                return;
            }
            var line = t.GetLine(ride.CurrentLineId);
            if (ride.Bus != null && ride.Bus.Vehicle != null)
            {
                var v = ride.Bus.Vehicle;
                ui.FieldLink("Verkehrsmittel", $"Bus – {line?.Name} #{ride.Bus.Id}", () => Go(SelectionKind.Vehicle, v.Id));
            }
            else
            {
                var stop = t.GetStop(ride.WaitingAtStop);
                ui.Field("Verkehrsmittel", $"wartet auf {line?.Name} an {stop?.Name}");
                if (ride.FullBusesSeen > 0) ui.Field("Volle Busse", ride.FullBusesSeen.ToString(), Tone.Warn);
            }
            var alight = t.GetStop(ride.AlightStopId);
            ui.Field("Aussteigen", alight?.Name ?? "–");
            if (ride.HasTransfer && ride.Leg == 0)
                ui.Field("Umstieg", $"in {t.GetLine(ride.Plan.Line2)?.Name} bis {t.GetStop(ride.Plan.Alight2)?.Name}");
        }
    }
}
