using System.Linq;
using CityBuilder.Game.Aviation;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Aviation;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Gateways;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Flughafenübersicht (rechtes Panel, <b>F12</b> bzw. offen im Flughafen-Werkzeug 9): Luft-Gateway, Flughäfen (Betrieb, Bahn,
    /// Wind/Betriebsrichtung, Einflugschneisen, Hindernisse, Kapazität), Flugplan (nächste Landungen/Starts mit Ziel und Muster),
    /// Ziele weltweit (Entfernung, Flugdauer), Fluggäste (Touristen, Geschäftsreisende, Besucher in der Stadt, Ausgaben), Luftfracht,
    /// Fluglärm (betroffene Einwohner, Beschwerden), Nachtflugverbot, Kasse.
    /// </summary>
    public sealed class AirportWindow : GameWindow
    {
        bool open;
        int tab;
        static readonly string[] Tabs = { "Betrieb", "Flugplan", "Ziele", "Wirkung" };

        public AirportWindow() : base(WindowSlot.Right) { }

        public override float OpenScore
        {
            get
            {
                if (World?.Aviation == null) return 0f;
                if (Tool.CurrentMode == RoadTool.Mode.Airport) return 88f;
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
            if (Tool.CurrentMode == RoadTool.Mode.Airport) return false; // Esc gehört dem Werkzeug
            Close();
            return true;
        }

        public override void Poll()
        {
            var kb = GameKeyboard.Current;
            if (kb != null && kb.f12Key.wasPressedThisFrame) { open = !open; Dismissed = false; }
        }

        public override void DrawHead(Ui ui)
        {
            var av = World.Aviation;
            Head(ui, "Flughafen (9 · F12)", Tone.Info, "Luftverkehr",
                $"{av.Airports.Count} Flughäfen · {av.AllAircraft.Count} Flugzeuge sichtbar · Unterhalt {BuildLabels.Money((float)av.WeeklyUpkeep())}/Woche");
        }

        public override void DrawBody(Ui ui)
        {
            var world = World;
            var av = world.Aviation;
            ui.Tabs(Tabs, tab, i => tab = i, true);
            var warn = av.Warning;
            if (warn != null) ui.Message("Flughafen eingeschränkt", warn, Tone.Bad);
            else if (av.Airports.Count == 0)
                ui.Message("Noch kein Flughafen", "Bauleiste „Flughafen“ (9): Regionalflugplatz oder Verkehrsflughafen auf ebenes Gelände setzen, die Bahn möglichst " +
                    $"in den Wind drehen (hier meist aus {AviationRules.WindText(av.PrevailingWind)}), die Einflugschneisen frei von Hochhäusern, Türmen und Windrädern halten " +
                    "und eine Straße vor das Terminal bauen – dann wird der Luftweg befahrbar.", Tone.Info);
            switch (tab)
            {
                case 0: DrawOperations(ui, world, av); break;
                case 1: DrawSchedule(ui, world, av); break;
                case 2: DrawDestinations(ui, av); break;
                default: DrawEffects(ui, world, av); break;
            }
            using (ui.Actions())
            {
                ui.ActionButton(av.NightBan ? "Nachtflugverbot aufheben" : "Nachtflugverbot (22–6 Uhr)", () => av.SetNightBan(!av.NightBan),
                    ActionStyle.Secondary, av.NightBan, true, "Nachts keine Starts und Landungen: weniger Fluglärm, weniger Kapazität");
                ui.ActionButton(AirportView.ShowCorridors ? "Schneisen ausblenden" : "Schneisen zeigen", () => AirportView.ShowCorridors = !AirportView.ShowCorridors,
                    ActionStyle.Secondary, AirportView.ShowCorridors);
            }
        }

        void DrawOperations(Ui ui, SimulationWorld world, AviationSystem av)
        {
            var air = world.Gateways?.All.FirstOrDefault(g => g.Kind == GatewayKind.Air);
            if (air != null)
                using (ui.Fields()) ui.Field("Luft-Gateway", $"{air.Name} · {air.StatusText}", air.IsOperational ? Tone.Good : Tone.Warn);
            foreach (var a in av.Airports)
            {
                ui.Section(a.Name, $"{a.Type.DisplayName} · Bahn {a.Designator}, {a.RunwayLength:0} × {a.Type.RunwayWidth:0} m · Terminal Stufe {a.Level}/{a.Type.MaxLevel}");
                using (ui.Grid(3))
                {
                    ui.Stat("Landungen", BuildLabels.Number(a.ArrivalsYesterday), $"gestern (heute {a.ArrivalsToday})");
                    ui.Stat("Fluggäste", BuildLabels.Number(a.PaxInYesterday + a.PaxOutYesterday), "gestern an + ab");
                    ui.Stat("Kapazität", $"{av.MovementCapacity(a):0}", "Bewegungen/h");
                }
                using (ui.Fields())
                {
                    ui.Field("Betrieb", a.InService ? "in Betrieb" : !a.HasAccess ? "keine Straße vor dem Terminal" : "gesperrt (beide Schneisen)", a.InService ? Tone.Good : Tone.Bad);
                    string arr = a.ArrivalDirection > 0 ? "über Ende A" : "über Ende B";
                    ui.Field("Wind · Betriebsrichtung", $"aus {AviationRules.WindText(a.WindToday)} · Landung {arr}, Start Richtung {(a.DepartureDirection > 0 ? "B" : "A")}" +
                        (a.CounterFlow ? " (Gegenrichtung)" : ""), a.CounterFlow ? Tone.Warn : Tone.None);
                    ui.Field("Einflugschneise A", AviationSystem.CorridorText(a.CorridorA), Corridor(a.CorridorA));
                    ui.Field("Einflugschneise B", AviationSystem.CorridorText(a.CorridorB), Corridor(a.CorridorB));
                    ui.Field("Abstellpositionen", $"{a.Stands} · Terminal {a.Type.TerminalPaxPerHour[a.Level - 1]:0} Fluggäste/h");
                    ui.Field("Nachfrage", $"{av.DailyDemand(a):0} Fluggäste je Richtung und Tag");
                    ui.Field("Gebühren", $"gesamt {BuildLabels.Money((float)av.FeesTotal)} (Landung, {AviationRules.PassengerFee:0} € je Fluggast, Fracht)");
                }
                if (a.Obstacles.Count > 0)
                {
                    ui.Section("Hindernisse", "durchstoßen die Hindernisbegrenzungsflächen – abreißen oder Gelände abtragen");
                    using (ui.Fields())
                        foreach (var o in a.Obstacles.Take(12))
                            ui.Field(o.Name, $"{o.Top:0} m statt ≤ {o.Allowed:0} m · {SurfaceText(o)}", o.Core ? Tone.Bad : Tone.Warn);
                }
            }
        }

        static Tone Corridor(CorridorState s) => s == CorridorState.Clear ? Tone.Good : s == CorridorState.Limited ? Tone.Warn : Tone.Bad;

        static string SurfaceText(Obstacle o)
        {
            switch (o.Surface)
            {
                case ObstacleSurface.ApproachA: return $"Schneise A, {o.Distance:0} m" + (o.Core ? " (Kernzone)" : "");
                case ObstacleSurface.ApproachB: return $"Schneise B, {o.Distance:0} m" + (o.Core ? " (Kernzone)" : "");
                case ObstacleSurface.Transitional: return "Übergangsfläche neben der Bahn";
                default: return "innere Horizontalfläche (45 m)";
            }
        }

        void DrawSchedule(Ui ui, SimulationWorld world, AviationSystem av)
        {
            double now = world.Traffic?.Time ?? 0.0;
            ui.Section("Nächste Flüge", "Umläufe: Landung → Abfertigung → Start · sichtbar = Flugzeug auf der Karte");
            int shown = 0;
            foreach (var r in av.Rotations.Where(r => !r.Departed).OrderBy(r => r.Arrived ? r.DepartureTime : r.ArrivalTime))
            {
                if (shown++ >= 14) break;
                var d = av.DestinationOf(r);
                bool inbound = !r.Arrived;
                double t = inbound ? r.ArrivalTime : r.DepartureTime;
                string when = $"{ClockText(world, now, t)}";
                string sub = $"{r.Type.DisplayName} · {(inbound ? "aus" : "nach")} {d?.Name ?? "–"} ({d?.Hours ?? 0:0.0} h) · " +
                             (r.Type.Role == AircraftRole.Cargo ? $"{(inbound ? r.ArrivingLoads : r.DepartingLoads)} Ladungen" : $"{(inbound ? r.ArrivingPax : r.DepartingPax)} Fluggäste");
                ui.ListItem(inbound ? "↓" : "↑", inbound ? new UnityEngine.Color(0.25f, 0.55f, 0.35f) : new UnityEngine.Color(0.3f, 0.4f, 0.75f),
                    $"{r.FlightNumber} · {when}", sub, r.Visible ? "sichtbar" : null, Tone.Info);
            }
            if (shown == 0) ui.Note(av.NightBan ? "Keine Flüge geplant (Nachtflugverbot oder kein Flughafen in Betrieb)." : "Keine Flüge geplant.");
            ui.Section("Flugzeuge auf der Karte");
            using (ui.Fields())
                foreach (var x in av.AllAircraft)
                    ui.Field($"{x.FlightNumber} · {x.Type.DisplayName}", av.StateText(x));
        }

        static string ClockText(SimulationWorld world, double now, double t)
        {
            double minutes = TimeModel.GameMinutes(System.Math.Max(0.0, t - now));
            int total = world.Clock.Hour * 60 + world.Clock.Minute + (int)minutes;
            return $"{(total / 60) % 24:00}:{total % 60:00} Uhr";
        }

        void DrawDestinations(Ui ui, AviationSystem av)
        {
            ui.Section("Ziele weltweit", "Hauptstädte · Großkreis-Entfernung · Flugdauer bei 800 km/h");
            foreach (var d in av.Destinations)
                ui.ListItem(d.City.Length > 0 ? d.City.Substring(0, 1) : "?", new UnityEngine.Color(0.35f, 0.4f, 0.7f), d.Name,
                    $"{d.DistanceKm:0} km · {d.Hours:0.0} h Flug · Tourismus {d.Tourism * 100f:0} %", $"{d.Weight:0}", Tone.None);
        }

        void DrawEffects(Ui ui, SimulationWorld world, AviationSystem av)
        {
            int tourists = 0, business = 0, loads = 0, pending = 0, moveIns = 0;
            foreach (var a in av.Airports) { tourists += a.TouristsYesterday; business += a.BusinessYesterday; loads += a.LoadsYesterday; pending += a.PendingLoads; moveIns += a.MoveInsTotal; }
            ui.Section("Fluggäste & Tourismus", $"Tourismus-Stärke des Landes {av.TourismStrength * 100f:0} % · Attraktivität der Stadt {world.Services?.Attractiveness ?? 0f:0} Punkte");
            using (ui.Grid(3))
            {
                ui.Stat("Besucher", $"{av.VisitorsPresent:0}", "in der Stadt (2–4 Tage)");
                ui.Stat("Touristen", BuildLabels.Number(tourists), "angekommen gestern");
                ui.Stat("Geschäftsreisen", BuildLabels.Number(business), "gestern");
            }
            using (ui.Fields())
            {
                ui.Field("Ausgaben der Besucher", $"Läden {BuildLabels.Money((float)av.VisitorShopSpendYesterday)} · Freizeit/Eintritt {BuildLabels.Money((float)av.VisitorLeisureSpendYesterday)} gestern");
                ui.Field("Tourismus-Einnahmen", $"{BuildLabels.Money((float)av.TourismTotal)} gesamt (Kurtaxe {AviationRules.CityTaxPerNight:0} € je Nacht, Eintritt)");
                ui.Field("Nachfrage", $"Läden ×{av.RetailBoost:0.00} (Besucher) · Büros ×{av.OfficeBoost:0.00} (Geschäftsreisende)");
                ui.Field("Zuzug per Flugzeug", $"{moveIns} Haushalte gesamt (selten)");
                ui.Field("Nachfrage je Tag", $"Einwohner {av.DemandResidents:0} · Geschäft {av.DemandBusiness:0} · Touristen {av.DemandTourists:0} (+ Umland)");
            }
            ui.Section("Luftfracht", $"Elektronik u. a. hochwertige Güter · {AviationRules.AirFreightShare * 100:0} % des Warenwerts (teurer als Schiff/Bahn, aber schnell)");
            using (ui.Fields())
                ui.Field("Ladungen", $"{loads} gestern · {pending} warten auf den nächsten Frachtflug");
            ui.Section("Fluglärm", "stark entlang der An-/Abflugschneisen · Lärm-Ansicht (N) zeigt ihn mit dem Straßenlärm");
            using (ui.Grid(2))
            {
                ui.Stat("Betroffene", BuildLabels.Number(av.AffectedResidentsYesterday), $"Einwohner ≥ {AviationRules.AffectedNoise * 100f:0} % Fluglärm (18 Uhr)",
                    av.AffectedResidentsYesterday > 0 ? Tone.Warn : Tone.None);
                ui.Stat("Beschwerden", BuildLabels.Number(av.ComplaintsYesterday), "Bürger erinnern sich an Fluglärm", av.ComplaintsYesterday > 0 ? Tone.Bad : Tone.None);
            }
            if (Tool.HasCursor)
                using (ui.Fields())
                {
                    float limit = av.HeightLimitAt(Tool.Cursor);
                    ui.Field("Am Mauszeiger", $"Fluglärm {av.AircraftNoiseAt(Tool.Cursor) * 100f:0} % · " +
                        (float.IsPositiveInfinity(limit) ? "keine Höhenbeschränkung" : $"Bauhöhe höchstens {System.Math.Max(0f, limit):0} m (Einflugschneise)"));
                }
        }
    }
}
