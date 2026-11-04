using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Game.Shipping;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Gateways;
using CityBuilder.Simulation.Shipping;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Hafenübersicht (rechtes Panel, <b>F8</b> bzw. offen im Hafen-Werkzeug 8): Wasser-Gateways und Fahrrinnen (Tiefe, Breite,
    /// Brücken mit lichter Höhe), Häfen (Betrieb, Umschlag, Anläufe), Klappbrücken (Zustand, Öffnungen, Sperrzeit), Schiffe,
    /// gesperrte Klassen, Kasse.
    /// </summary>
    public sealed class HarborWindow : GameWindow
    {
        bool open;

        public HarborWindow() : base(WindowSlot.Right) { }

        public override float OpenScore
        {
            get
            {
                if (World?.Shipping == null) return 0f;
                if (Tool.CurrentMode == RoadTool.Mode.Harbor) return 88f;
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
            if (Tool.CurrentMode == RoadTool.Mode.Harbor) return false; // Esc gehört dem Werkzeug
            Close();
            return true;
        }

        public override void Poll()
        {
            var kb = GameKeyboard.Current;
            if (kb != null && kb.f8Key.wasPressedThisFrame) { open = !open; Dismissed = false; }
        }

        public override void DrawHead(Ui ui)
        {
            var sh = World.Shipping;
            Head(ui, "Hafen (8 · F8)", Tone.Info, "Wasserstraße & Hafen",
                $"{sh.Waterways.Count} Fahrrinnen · {sh.Harbors.Count} Häfen · {sh.Ships.Count} Schiffe · Unterhalt {BuildLabels.Money((float)sh.WeeklyUpkeep())}/Woche");
        }

        public override void DrawBody(Ui ui)
        {
            var world = World;
            var sh = world.Shipping;
            if (!sh.Active)
            {
                ui.Message("Keine Wasserstraße", "Diese Karte hat keine schiffbaren Gewässer am Kartenrand (oder der Spielstand hat keine Außenanbindung).", Tone.Warn);
                return;
            }
            using (ui.Grid(3))
            {
                ui.Stat("Durchgang", BuildLabels.Number(sh.ThroughYesterday), $"Schiffe gestern (heute {sh.ThroughToday})");
                ui.Stat("Schiffe", BuildLabels.Number(sh.ShipsCompleted), $"gesamt · {sh.ShipsCancelled} umgekehrt");
                ui.Stat("Hafengeld", BuildLabels.Money((float)sh.FeesTotal), "gesamt");
            }
            var warn = sh.FairwayWarning;
            if (warn != null) ui.Message("Fahrrinne gesperrt", warn, Tone.Bad);
            else if (sh.Harbors.Count == 0)
                ui.Message("Hafen fehlt", "Durchgangsschiffe fahren schon. Einen Güterhafen ans Ufer setzen (Kai an tiefem Wasser) und eine Straße an die Landseite bauen – " +
                    "dann laufen Import/Export und Seehandel per Schiff, der Wasser-Gateway wird befahrbar.", Tone.Info);

            ui.Section("Wasser-Gateways");
            using (ui.Fields())
                foreach (var g in world.Gateways.All.Where(x => x.Kind == GatewayKind.Water))
                    ui.Field(g.Name, $"{g.StatusText} · Breite {g.WaterWidth:0} m, Tiefe {g.WaterDepth:0.0} m · Handel heute {g.Today.Imports + g.Today.Exports}",
                        g.IsOperational ? Tone.Good : g.Navigable ? Tone.Warn : Tone.Muted);

            ui.Section("Fahrrinnen", "Tiefe/Breite aus dem Gelände · lichte Höhe der Brücken über der Rinne");
            foreach (var w in sh.Waterways)
            {
                string title = w.Kind == WaterwayKind.Through ? "Durchgang" : $"zum {sh.GetHarbor(w.HarborId)?.Name ?? "Hafen"}";
                var block = w.FirstBlocking(ShipCatalog.Barge);
                string sub = $"{w.Length:0} m · Tiefe ≥ {w.MinDepth:0.0} m · Breite ≥ {w.MinWidth:0} m · ausgelegt für {w.DesignClearance:0} m · {w.Crossings.Count} Brücken";
                ui.ListItem(w.Kind == WaterwayKind.Through ? "D" : "H", new UnityEngine.Color(0.2f, 0.55f, 0.85f), title, sub,
                    block == null ? "frei" : "gesperrt", block == null ? Tone.Good : Tone.Bad);
                foreach (var x in w.Crossings)
                {
                    string kind = sh.CrossingName(x);
                    string state = x.IsDrawbridge ? $"öffnet ({System.Math.Max(0f, x.Clearance):0.0} m geschlossen)" : $"{System.Math.Max(0f, x.Clearance):0.0} m";
                    var tone = x.IsDrawbridge ? Tone.Accent : x.Clearance < w.DesignClearance ? Tone.Bad : Tone.None;
                    using (ui.Fields()) ui.Field("  " + kind, state, tone);
                }
            }
            using (ui.Fields())
                foreach (var c in ShipCatalog.All)
                {
                    int blocked = sh.BlockedYesterday(c) + sh.BlockedToday(c);
                    if (blocked > 0) ui.Field($"{c.DisplayName} gesperrt", $"{blocked} Schiffe kamen nicht durch (Masthöhe {c.AirDraft:0} m)", Tone.Warn);
                }

            ui.Section("Häfen");
            if (sh.Harbors.Count == 0) ui.Note("Noch kein Hafen – Bauleiste „Hafen“ (8).");
            foreach (var h in sh.Harbors)
            {
                string state = h.InService ? "in Betrieb" : !h.HasAccess ? "keine Straße an der Landseite" : h.BlockedReason ?? "keine Fahrrinne";
                ui.ListItem("H", new UnityEngine.Color(0.15f, 0.45f, 0.75f), h.Name,
                    $"{h.ShipClass.DisplayName} · Umschlag gestern {h.LoadsYesterday} (heute {h.LoadsToday}/{sh.CapacityPerDay(h)}) · {h.CallsYesterday} Anläufe · {h.PendingLoads} warten",
                    state, h.InService ? Tone.Good : Tone.Warn);
            }

            ui.Section("Klappbrücken");
            if (sh.Drawbridges.Count == 0) ui.Note("Keine – Klappbrücken lassen niedrige Straßen über die Fahrrinne zu.");
            using (ui.Fields())
                foreach (var d in sh.Drawbridges)
                    ui.Field($"Klappbrücke (Straße {d.SegmentId})", $"{ShippingSystem.StateText(d)} · heute {d.OpeningsToday}× offen, " +
                        $"{Simulation.Core.TimeModel.GameMinutes(d.ClosedSecondsToday):0} Spielminuten gesperrt · {d.ShipsWaiting} Schiffe warten", d.RoadClosed ? Tone.Warn : Tone.None);

            ui.Section("Schiffe");
            if (sh.Ships.Count == 0) ui.Note("Gerade kein Schiff unterwegs.");
            foreach (var s in sh.Ships)
            {
                if (s.State == ShipState.Waiting) continue;
                var h = sh.GetHarbor(s.HarborId);
                string state = s.State == ShipState.Moored ? $"liegt in {h?.Name}" : s.State == ShipState.WaitingBridge ? "wartet vor Brücke"
                    : s.State == ShipState.WaitingBerth ? "wartet auf Liegeplatz" : $"{s.Speed * 3.6f:0.0} km/h";
                string sub = s.Purpose == ShipPurpose.Through ? "Durchfahrt" : s.Purpose == ShipPurpose.Inbound ? $"nach {h?.Name ?? "Hafen"}" : "fährt hinaus";
                sub += s.Class.Role == ShipRole.Cargo ? $" · {s.Loads} Ladungen" : $" · {s.Passengers} an Bord";
                ui.ListItem(s.Class.DisplayName.Substring(0, 1), new UnityEngine.Color(0.25f, 0.3f, 0.4f), $"{s.Class.DisplayName} #{s.Id}", sub, state,
                    s.State == ShipState.WaitingBridge ? Tone.Warn : Tone.Good);
            }
            using (ui.Actions())
                ui.ActionButton(ShippingView.ShowFairway ? "Fahrrinne ausblenden" : "Fahrrinne zeigen", () => ShippingView.ShowFairway = !ShippingView.ShowFairway,
                    ActionStyle.Secondary, ShippingView.ShowFairway);
        }
    }
}
