using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Verkehrsregeln (rechtes Panel im Verkehrs-Modus T) für die gewählte Kreuzung bzw. Straße: Kreuzungsart,
    /// Schilder je Zufahrt, Abbiegeverbote, Ampel-Grünzeit; Sperrung, Fahrverbote, Tempolimit. Zufahrt-Buchstaben
    /// als Welt-Beschriftungen. Die Abbiegelinien zeichnet weiter <see cref="CityBuilder.Game.Traffic.TrafficPanel"/> (LineRenderer).
    /// Gleiche Funktionen wie das frühere IMGUI-Fenster, Änderungen über <see cref="TrafficRules"/>.
    /// </summary>
    public sealed class TrafficWindow : GameWindow
    {
        static readonly float[] SpeedSteps = { 0f, 30f, 50f, 70f, 100f, 130f };
        static readonly string[] SpeedNames = { "Standard", "30", "50", "70", "100", "130" };

        public TrafficWindow() : base(WindowSlot.Right) { }

        TrafficSystem Traffic => World.Traffic;

        public override float OpenScore => Tool.CurrentMode == RoadTool.Mode.Traffic ? 100f : 0f;

        /// <summary>× beendet den Verkehrs-Modus.</summary>
        public override void Close() => Tool.SetMode(RoadTool.SelectMode); // UX: zurück zum Auswählen

        /// <summary>Esc hebt nur die Auswahl auf (das RoadTool macht dasselbe) – der Modus bleibt.</summary>
        public override bool HandleEscape()
        {
            if (Tool.SelectedNode == null && Tool.SelectedSegment == null) return false;
            Tool.ClearSelection();
            return true;
        }

        public override void Tick()
        {
            WorldLabels.Begin(this);
            var node = Tool.SelectedNode;
            if (node == null) return;
            var info = Traffic.Junctions.Get(node);
            for (int i = 0; i < info.Approaches.Count; i++)
            {
                var lanes = info.IncomingLanes[info.Approaches[i]];
                if (lanes.Count == 0) continue;
                var lane = lanes[0];
                lane.Sample(Mathf.Max(0f, lane.Length - 15f), 0f, out var p, out float h, out _);
                WorldLabels.Add(this, SimConvert.ToWorld(p, h + 2f), Letter(i), WorldLabelStyle.Letter);
            }
        }

        public override void DrawHead(Ui ui)
        {
            if (Tool.SelectedNode != null)
            {
                var node = Tool.SelectedNode;
                var info = Traffic.Junctions.Get(node);
                Head(ui, "Verkehrsregeln (T)", Tone.Info, $"Kreuzung #{node.Id}",
                    $"{info.Approaches.Count} Zufahrten · {ControlName(Traffic.Rules.ControlAt(node.Id))}");
            }
            else if (Tool.SelectedSegment != null)
            {
                var seg = Tool.SelectedSegment;
                float load = 1f - Traffic.Lanes.GetLanes(seg.Id).Select(l => l.SpeedRatio).DefaultIfEmpty(1f).Min();
                Head(ui, "Verkehrsregeln (T)", Tone.Info, $"{seg.Type.DisplayName} #{seg.Id}",
                    $"Länge {seg.Length:0} m · " + (seg.IsElevated ? $"Brücke {Mathf.Max(seg.Start.Height, seg.End.Height):0} m · " : "") +
                    $"Auslastung {load * 100f:0} %");
            }
            else Head(ui, "Verkehrsregeln (T)", Tone.Info, "Verkehr", "Kreuzung oder Straße anklicken");
        }

        public override void DrawBody(Ui ui)
        {
            if (Tool.SelectedNode != null) NodeBody(ui, Tool.SelectedNode);
            else if (Tool.SelectedSegment != null) SegmentBody(ui, Tool.SelectedSegment);
            else EmptyBody(ui);
        }

        void EmptyBody(Ui ui)
        {
            var s = Traffic.Stats;
            using (ui.Grid(3))
            {
                ui.Stat("Fahrzeuge", BuildLabelsNumber(s.Active));
                ui.Stat("Ø Tempo", $"{s.AverageSpeedKmh:0}", "km/h");
                ui.Stat("Warten", BuildLabelsNumber(s.Waiting));
            }
            ui.Section("So geht's");
            ui.Text("Kreuzung anklicken: Vorfahrt, Schilder, Ampel, Abbiegeverbote.");
            ui.Text("Straße anklicken: Sperrung, Lkw-Verbot, Busspur-Regeln, Tempolimit.");
            ui.Section("Schilder");
            using (ui.Chips())
            {
                ui.Chip("Vorfahrtstraße", Tone.Accent, new Color(1f, 0.83f, 0.1f));
                ui.Chip("Vorfahrt gewähren", Tone.None, Color.white);
                ui.Chip("Stopp", Tone.Bad, new Color(0.88f, 0.13f, 0.13f));
            }
            ui.Note("V = Stau-Ansicht · Abbiegelinien an der gewählten Kreuzung: grün erlaubt, rot verboten · RMB/Esc hebt die Auswahl auf.");
        }

        void NodeBody(Ui ui, RoadNode node)
        {
            var rules = Traffic.Rules;
            var info = Traffic.Junctions.Get(node);
            var roads = World.Roads;
            var current = rules.ControlAt(node.Id);

            ui.Section("Steuerung");
            using (ui.Actions())
            {
                ui.ActionButton("Automatisch", () => rules.SetControl(node.Id, JunctionControl.Auto), ActionStyle.Secondary, current == JunctionControl.Auto);
                ui.ActionButton("Rechts vor links", () => rules.SetControl(node.Id, JunctionControl.RightBeforeLeft), ActionStyle.Secondary,
                    current == JunctionControl.RightBeforeLeft);
            }
            using (ui.Actions())
            {
                ui.ActionButton("Schilder", () => rules.SetControl(node.Id, JunctionControl.Signs), ActionStyle.Secondary, current == JunctionControl.Signs);
                ui.ActionButton("Ampel", () => rules.SetControl(node.Id, JunctionControl.TrafficLights), ActionStyle.Secondary,
                    current == JunctionControl.TrafficLights);
            }

            if (current == JunctionControl.TrafficLights)
            {
                float green = rules.GetNode(node.Id)?.GreenSeconds ?? 20f;
                using (ui.Fields())
                    ui.Stepper("Grünphase", $"{green:0} s", () => rules.SetGreenSeconds(node.Id, green - 5f), () => rules.SetGreenSeconds(node.Id, green + 5f),
                        $"{info.Phases.Count} Phasen", Tone.Muted);
            }

            for (int i = 0; i < info.Approaches.Count; i++)
            {
                int approach = info.Approaches[i];
                var seg = roads.GetSegment(approach);
                if (seg == null) continue;
                ui.Section($"Zufahrt {Letter(i)}", $"{seg.Type.DisplayName}, von {Compass(seg.Other(node).Position - node.Position)}");
                using (ui.Fields())
                {
                    var sign = info.SignOf(approach);
                    if (current == JunctionControl.Signs)
                        ui.FieldLink("Vorfahrt", SignName(sign, current) + " · ändern", () => rules.SetSign(node.Id, approach, NextSign(sign)), SignTone(sign, current));
                    else ui.Field("Vorfahrt", SignName(sign, current), SignTone(sign, current));

                    var targets = Traffic.Lanes.GetConnections(node.Id).Where(c => c.From.Segment.Id == approach)
                        .Select(c => (c.To.Segment, c.Turn)).Distinct().ToList();
                    foreach (var (target, turn) in targets)
                    {
                        var ban = rules.GetTurnBan(node.Id, approach, target.Id);
                        int j = info.Approaches.IndexOf(target.Id);
                        string to = j >= 0 ? $"Richtung {Letter(j)}" : $"nach {Compass(target.Other(node).Position - node.Position)}";
                        int targetId = target.Id;
                        ui.FieldLink($"{TurnName(turn)} {to}", BanName(ban), () => rules.SetTurnBan(node.Id, approach, targetId, NextBan(ban)),
                            ban == VehicleClass.None ? Tone.Good : ban == VehicleClass.Truck ? Tone.Accent : Tone.Bad);
                    }
                }
            }
            ui.Note("Klick auf eine Abbiegezeile schaltet: erlaubt → verboten → Lkw verboten.");
        }

        void SegmentBody(Ui ui, RoadSegment seg)
        {
            var rules = Traffic.Rules;
            var r = rules.GetSegment(seg.Id);
            bool closed = r?.Closed ?? false;
            var banned = r?.Banned ?? VehicleClass.None;
            float limit = r?.SpeedLimitKmh ?? 0f;

            ui.Section("Zufahrt & Fahrverbote");
            using (ui.Actions())
                ui.ActionButton(closed ? "Gesperrt (Umleitung aktiv)" : "Straße sperren", () => rules.SetClosed(seg.Id, !closed),
                    closed ? ActionStyle.Danger : ActionStyle.Secondary, closed);
            bool noTrucks = (banned & VehicleClass.Truck) != 0;
            bool busOnly = (banned & VehicleClass.Car) != 0;
            using (ui.Actions())
            {
                ui.ActionButton(noTrucks ? "Lkw-Verbot: an" : "Lkw-Verbot: aus", () => rules.SetBanned(seg.Id, banned ^ VehicleClass.Truck),
                    ActionStyle.Secondary, noTrucks);
                ui.ActionButton(busOnly ? "Nur ÖPNV: an" : "Pkw-Verbot: aus",
                    () => rules.SetBanned(seg.Id, busOnly ? banned & ~(VehicleClass.Car | VehicleClass.Truck) : banned | VehicleClass.Car | VehicleClass.Truck),
                    ActionStyle.Secondary, busOnly, true, "Pkw- und Lkw-Verbot: nur Bus/Tram (Busspur-Regel)");
            }

            ui.Section("Tempolimit", limit > 0f ? $"{limit:0} km/h" : $"Standard ({seg.Type.SpeedLimitKmh:0} km/h)");
            int index = System.Array.IndexOf(SpeedSteps, limit);
            ui.Tabs(SpeedNames, index < 0 ? 0 : index, i => rules.SetSpeedLimit(seg.Id, SpeedSteps[i]), small: true);
        }

        // ------------------------------------------------------------ Texte

        static string BuildLabelsNumber(float v) => Hud.BuildLabels.Number(v);

        static string Letter(int i) => ((char)('A' + i)).ToString();

        static string Compass(SimVector2 dir)
        {
            float b = RoadGeometry.Bearing(dir);
            string[] names = { "Norden", "Nordosten", "Osten", "Südosten", "Süden", "Südwesten", "Westen", "Nordwesten" };
            return names[Mathf.RoundToInt(b / 45f) % 8];
        }

        static string ControlName(JunctionControl c)
        {
            switch (c)
            {
                case JunctionControl.RightBeforeLeft: return "rechts vor links";
                case JunctionControl.Signs: return "Schilder";
                case JunctionControl.TrafficLights: return "Ampel";
                default: return "automatisch";
            }
        }

        static string SignName(ApproachSign s, JunctionControl c)
        {
            if (c == JunctionControl.TrafficLights) return "Ampel";
            if (c == JunctionControl.RightBeforeLeft) return "rechts vor links";
            switch (s)
            {
                case ApproachSign.Priority: return "Vorfahrtstraße";
                case ApproachSign.Yield: return "Vorfahrt gewähren";
                case ApproachSign.Stop: return "Stopp";
                default: return "rechts vor links";
            }
        }

        static Tone SignTone(ApproachSign s, JunctionControl c)
        {
            if (c == JunctionControl.TrafficLights || c == JunctionControl.RightBeforeLeft) return Tone.None;
            return s == ApproachSign.Priority ? Tone.Accent : s == ApproachSign.Stop ? Tone.Bad : Tone.None;
        }

        static ApproachSign NextSign(ApproachSign s) =>
            s == ApproachSign.Priority ? ApproachSign.Yield : s == ApproachSign.Yield ? ApproachSign.Stop : ApproachSign.Priority;

        static string TurnName(TurnType t) =>
            t == TurnType.Left ? "↰ links" : t == TurnType.Right ? "↱ rechts" : t == TurnType.UTurn ? "↶ wenden" : "↑ geradeaus";

        static string BanName(VehicleClass b) =>
            b == VehicleClass.None ? "erlaubt" : b == VehicleClass.Truck ? "Lkw verboten" : "verboten";

        static VehicleClass NextBan(VehicleClass b) =>
            b == VehicleClass.None ? VehicleClass.All : b == VehicleClass.All ? VehicleClass.Truck : VehicleClass.None;
    }
}
