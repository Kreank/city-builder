using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Aviation;
using CityBuilder.Simulation.Core;

namespace CityBuilder.Game.Inspect
{
    /// <summary>Flughafen im Inspektor: Flugzeug (Muster, Flug, Ziel, Zustand, Fluggäste) und Flughafen (Bahn, Terminal, Schneisen, Betrieb).</summary>
    public sealed partial class InspectorWindow
    {
        bool AviationHead(Ui ui, SimulationWorld world, Selection s)
        {
            var av = world.Aviation;
            if (av == null) return false;
            switch (s.Kind)
            {
                case SelectionKind.Aircraft:
                {
                    var x = av.GetAircraft(s.Id);
                    if (x == null) return false;
                    StandardHead(ui, "Flugzeug", Tone.Info, $"{x.FlightNumber} · {x.Type.DisplayName}", av.StateText(x), true);
                    return true;
                }
                case SelectionKind.Airport:
                {
                    var a = av.GetAirport(s.Id);
                    if (a == null) return false;
                    StandardHead(ui, "Flughafen", a.InService ? Tone.Good : Tone.Warn, a.Name,
                        $"{a.Type.DisplayName} · Bahn {a.Designator} · {(a.InService ? "in Betrieb" : !a.HasAccess ? "keine Straße vor dem Terminal" : "Einflugschneisen gesperrt")}", false);
                    return true;
                }
            }
            return false;
        }

        bool AviationBody(Ui ui, SimulationWorld world, Selection s)
        {
            var av = world.Aviation;
            if (av == null) return false;
            switch (s.Kind)
            {
                case SelectionKind.Aircraft:
                {
                    var x = av.GetAircraft(s.Id);
                    if (x == null) return false;
                    var t = x.Type;
                    var r = av.GetRotation(x.RotationId);
                    var d = av.DestinationOf(r);
                    var a = av.GetAirport(x.AirportId);
                    using (ui.Fields())
                    {
                        ui.Field("Muster", $"{t.DisplayName} · {t.Length:0} m lang, {t.Span:0} m Spannweite · " + (t.Role == AircraftRole.Cargo ? $"{t.CargoLoads} Ladungen" : $"{t.Seats} Plätze"));
                        ui.Field("Flug", $"{x.FlightNumber} · {(x.State <= AircraftState.TaxiIn ? "aus" : "nach")} {d?.Name ?? "–"}" + (d != null ? $" · {d.DistanceKm:0} km, {d.Hours:0.0} h" : ""));
                        ui.Field("Zustand", av.StateText(x), x.State == AircraftState.Holding ? Tone.Warn : Tone.None);
                        if (r != null)
                            ui.Field("An Bord", t.Role == AircraftRole.Cargo ? $"{(x.State <= AircraftState.Parked ? r.ArrivingLoads : r.DepartingLoads)} Ladungen"
                                : $"{(x.State <= AircraftState.Parked ? r.ArrivingPax : r.DepartingPax)} Fluggäste · {r.TouristShare * 100f:0} % Touristen, {r.BusinessShare * 100f:0} % Geschäftsreisende");
                        ui.Field("Flughafen", $"{a?.Name ?? "–"} · Position {x.Stand + 1} · Bahnrichtung {(x.Direction > 0 ? "A → B" : "B → A")}");
                        ui.Field("Leistung", $"Anflug {t.ApproachSpeed * 3.6f:0} km/h · Abheben {t.RotateSpeed * 3.6f:0} km/h · Steigflug {t.ClimbGradient * 100f:0} % · Bahn ≥ {t.MinRunway:0} m");
                        ui.Field("Gebühren", $"Landung {t.LandingFee:0} € + {AviationRules.PassengerFee:0} € je abfliegendem Fluggast");
                    }
                    using (ui.Actions())
                        ui.ActionButton(panel.Following ? "Folgen aus" : "Folgen", () => panel.Following = !panel.Following,
                            ActionStyle.Secondary, panel.Following, true, "Kamera folgt dem Flugzeug (F)");
                    return true;
                }
                case SelectionKind.Airport:
                {
                    var a = av.GetAirport(s.Id);
                    if (a == null) return false;
                    using (ui.Grid(3))
                    {
                        ui.Stat("Landungen", BuildLabels.Number(a.ArrivalsYesterday), $"gestern (heute {a.ArrivalsToday})");
                        ui.Stat("Fluggäste", BuildLabels.Number(a.PaxInYesterday + a.PaxOutYesterday), "gestern");
                        ui.Stat("Kapazität", $"{av.MovementCapacity(a):0}", "Bewegungen/h");
                    }
                    using (ui.Fields())
                    {
                        ui.Field("Bahn", $"{a.Designator} · {a.RunwayLength:0} × {a.Type.RunwayWidth:0} m · Höhe {a.Height:0} m" +
                            (a.RunwayLength < a.Type.NominalRunwayLength ? $" (echt {a.Type.NominalRunwayLength:0} m, auf dieser Karte verkürzt)" : ""));
                        ui.Field("Terminal", $"Stufe {a.Level}/{a.Type.MaxLevel} · {a.Layout.Modules} Module · {a.Stands} Positionen · {a.Type.TerminalPaxPerHour[a.Level - 1]:0} Fluggäste/h");
                        ui.Field("Straßenzufahrt", a.HasAccess ? $"Straße {a.AccessSegmentId}" : "keine (Straße vor die Landseite des Terminals bauen)", a.HasAccess ? Tone.None : Tone.Bad);
                        ui.Field("Wind", $"heute aus {AviationRules.WindText(a.WindToday)} · Landung über Ende {(a.ArrivalDirection > 0 ? "A" : "B")}" + (a.CounterFlow ? " · Gegenrichtungsbetrieb" : ""));
                        ui.Field("Einflugschneise A", AviationSystem.CorridorText(a.CorridorA), a.CorridorA == CorridorState.Clear ? Tone.Good : a.CorridorA == CorridorState.Limited ? Tone.Warn : Tone.Bad);
                        ui.Field("Einflugschneise B", AviationSystem.CorridorText(a.CorridorB), a.CorridorB == CorridorState.Clear ? Tone.Good : a.CorridorB == CorridorState.Limited ? Tone.Warn : Tone.Bad);
                        ui.Field("Hindernisse", a.Obstacles.Count == 0 ? "keine" : $"{a.Obstacles.Count} (Übersicht F12)", a.Obstacles.Count == 0 ? Tone.Good : Tone.Warn);
                        ui.Field("Luftfracht", a.Type.CargoLoadsPerDay > 0 ? $"{a.LoadsYesterday} Ladungen gestern · {a.PendingLoads} warten · {a.Type.CargoLoadsPerDay}/Tag" : "keine (nur Verkehrsflughafen)");
                        ui.Field("Fluglärm", $"zuletzt {AviationSystem.LevelFor(a.NoiseA) * 100f:0} % / {AviationSystem.LevelFor(a.NoiseB) * 100f:0} % (Ende A/B) · {av.AffectedResidentsYesterday} Einwohner betroffen");
                        ui.Field("Unterhalt", $"{BuildLabels.Money((float)(a.Type.UpkeepPerWeek + (a.Level - 1) * a.Type.UpkeepPerLevel))}/Woche");
                    }
                    using (ui.Actions())
                    {
                        bool can = av.CanExpand(a);
                        ui.ActionButton(a.Level >= a.Type.MaxLevel ? "Terminal voll ausgebaut" : $"Terminal ausbauen ({BuildLabels.Money((float)a.Type.ExpandCost)})",
                            () => av.ExpandTerminal(a.Id), ActionStyle.Primary, false, can, "Mehr Module, Abstellpositionen und Fluggäste je Stunde");
                    }
                    return true;
                }
            }
            return false;
        }
    }
}
