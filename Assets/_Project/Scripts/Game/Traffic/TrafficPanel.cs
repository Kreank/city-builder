using System.Collections.Generic;
using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Traffic
{
    /// <summary>
    /// Regel-Fenster (IMGUI) für die im Verkehrs-Modus gewählte Kreuzung bzw. Straße:
    /// Kreuzungsart, Schilder je Zufahrt, Abbiegeverbote, Ampel-Grünzeit; Sperrung, Fahrverbote, Tempolimit.
    /// Zeigt außerdem die Abbiegebeziehungen der Kreuzung als Linien (grün erlaubt, rot verboten).
    /// </summary>
    public sealed class TrafficPanel : MonoBehaviour
    {
        static readonly Color Selected = new Color(0.45f, 0.75f, 1f);
        static readonly float[] SpeedSteps = { 0f, 30f, 50f, 70f, 100f, 130f };

        RoadTool tool;
        Material lineMaterial;
        readonly List<LineRenderer> pool = new List<LineRenderer>();
        Vector2 scroll;
        GUIStyle header, small;

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            lineMaterial = new Material(transparent) { color = Color.white, name = "TurnLines" };
        }

        void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
        }

        TrafficSystem Traffic => tool.Runner.World.Traffic;

        // ------------------------------------------------------------ Abbiegelinien

        void LateUpdate()
        {
            int used = 0;
            if (tool != null && tool.CurrentMode == RoadTool.Mode.Traffic && tool.SelectedNode != null)
            {
                var node = tool.SelectedNode;
                foreach (var c in Traffic.Lanes.GetConnections(node.Id))
                {
                    bool allowed = Traffic.Rules.IsTurnAllowed(node.Id, c.From.Segment.Id, c.To.Segment.Id, VehicleClass.Car);
                    var lr = Next(ref used);
                    lr.startColor = lr.endColor = allowed ? new Color(0.2f, 1f, 0.3f, 0.85f) : new Color(1f, 0.2f, 0.2f, 0.85f);
                    const int n = 10;
                    lr.positionCount = n + 1;
                    for (int i = 0; i <= n; i++)
                    {
                        var p = Bezier.Evaluate(c.P0, c.Control, c.P2, i / (float)n);
                        lr.SetPosition(i, SimConvert.ToWorld(p, node.Height + 0.5f));
                    }
                }
            }
            for (int i = used; i < pool.Count; i++) pool[i].enabled = false;
        }

        LineRenderer Next(ref int used)
        {
            if (used == pool.Count)
            {
                var go = new GameObject("Turn Line");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = lineMaterial;
                lr.useWorldSpace = true;
                lr.widthMultiplier = 0.5f;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                pool.Add(lr);
            }
            var r = pool[used++];
            r.enabled = true;
            return r;
        }

        // ------------------------------------------------------------ Fenster

        void OnGUI()
        {
            if (tool == null || tool.CurrentMode != RoadTool.Mode.Traffic) return;
            if (header == null)
            {
                header = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, richText = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
            }

            var area = new Rect(Screen.width - 430f, 10f, 420f, Mathf.Min(Screen.height - 170f, 620f));
            UiBlocker.Register(area);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f));
            scroll = GUILayout.BeginScrollView(scroll);

            if (tool.SelectedNode != null) NodePanel(tool.SelectedNode);
            else if (tool.SelectedSegment != null) SegmentPanel(tool.SelectedSegment);
            else
            {
                GUILayout.Label("Verkehrsregeln", header);
                GUILayout.Label("Kreuzung anklicken: Vorfahrt, Schilder, Ampel, Abbiegeverbote.\n" +
                                "Straße anklicken: Sperrung, Lkw-Verbot, Busspur-Regeln, Tempolimit.\n\n" +
                                "Schilder: <color=#ffd31a>gelb</color> Vorfahrtstraße · weiß Vorfahrt gewähren · <color=#e02020>rot</color> Stopp.\n" +
                                "V = Stau-Ansicht.", small);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            DrawApproachLetters();
        }

        void NodePanel(RoadNode node)
        {
            var rules = Traffic.Rules;
            var info = Traffic.Junctions.Get(node);
            var roads = tool.Runner.World.Roads;
            GUILayout.Label($"Kreuzung #{node.Id} · {info.Approaches.Count} Zufahrten", header);

            var current = rules.ControlAt(node.Id);
            GUILayout.BeginHorizontal();
            if (Button("Automatisch", current == JunctionControl.Auto)) rules.SetControl(node.Id, JunctionControl.Auto);
            if (Button("Rechts vor links", current == JunctionControl.RightBeforeLeft)) rules.SetControl(node.Id, JunctionControl.RightBeforeLeft);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (Button("Schilder", current == JunctionControl.Signs)) rules.SetControl(node.Id, JunctionControl.Signs);
            if (Button("Ampel", current == JunctionControl.TrafficLights)) rules.SetControl(node.Id, JunctionControl.TrafficLights);
            GUILayout.EndHorizontal();

            if (current == JunctionControl.TrafficLights)
            {
                float green = rules.GetNode(node.Id)?.GreenSeconds ?? 20f;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Grünphase: {green:0} s ({info.Phases.Count} Phasen)", GUILayout.Width(200f));
                if (GUILayout.Button("−5 s")) rules.SetGreenSeconds(node.Id, green - 5f);
                if (GUILayout.Button("+5 s")) rules.SetGreenSeconds(node.Id, green + 5f);
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6f);
            var exits = Traffic.Lanes.OutgoingLanes(node).Select(l => l.Segment).Distinct().ToList();
            for (int i = 0; i < info.Approaches.Count; i++)
            {
                int approach = info.Approaches[i];
                var seg = roads.GetSegment(approach);
                GUILayout.Label($"<b>Zufahrt {Letter(i)}</b> – {seg.Type.DisplayName}, von {Compass(seg.Other(node).Position - node.Position)}", small);

                GUILayout.BeginHorizontal();
                var sign = info.SignOf(approach);
                GUILayout.Label("Vorfahrt: " + SignName(sign, current), small, GUILayout.Width(190f));
                if (current == JunctionControl.Signs && GUILayout.Button("ändern", GUILayout.Width(70f)))
                    rules.SetSign(node.Id, approach, NextSign(sign));
                GUILayout.EndHorizontal();

                // Abbiegebeziehungen dieser Zufahrt
                var targets = Traffic.Lanes.GetConnections(node.Id).Where(c => c.From.Segment.Id == approach)
                    .Select(c => (c.To.Segment, c.Turn)).Distinct().ToList();
                foreach (var (target, turn) in targets)
                {
                    var ban = rules.GetTurnBan(node.Id, approach, target.Id);
                    int j = info.Approaches.IndexOf(target.Id);
                    string to = j >= 0 ? $"Richtung {Letter(j)}" : $"nach {Compass(target.Other(node).Position - node.Position)}";
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"   {TurnName(turn)} {to}", small, GUILayout.Width(190f));
                    if (GUILayout.Button(BanName(ban), GUILayout.Width(150f)))
                        rules.SetTurnBan(node.Id, approach, target.Id, NextBan(ban));
                    GUILayout.EndHorizontal();
                }
                GUILayout.Space(4f);
            }
        }

        void SegmentPanel(RoadSegment seg)
        {
            var rules = Traffic.Rules;
            var r = rules.GetSegment(seg.Id);
            GUILayout.Label($"{seg.Type.DisplayName} #{seg.Id}", header);
            GUILayout.Label($"Länge {seg.Length:0} m · {(seg.IsElevated ? $"Brücke {Mathf.Max(seg.Start.Height, seg.End.Height):0} m · " : "")}" +
                            $"Auslastung {(1f - Traffic.Lanes.GetLanes(seg.Id).Select(l => l.SpeedRatio).DefaultIfEmpty(1f).Min()) * 100f:0} %", small);

            bool closed = r?.Closed ?? false;
            var banned = r?.Banned ?? VehicleClass.None;
            float limit = r?.SpeedLimitKmh ?? 0f;

            if (Button(closed ? "Gesperrt (Umleitung aktiv)" : "Straße sperren", closed)) rules.SetClosed(seg.Id, !closed);
            bool noTrucks = (banned & VehicleClass.Truck) != 0;
            if (Button(noTrucks ? "Lkw-Verbot: an" : "Lkw-Verbot: aus", noTrucks)) rules.SetBanned(seg.Id, banned ^ VehicleClass.Truck);
            bool busOnly = (banned & VehicleClass.Car) != 0;
            if (Button(busOnly ? "Nur Bus/Tram/Lkw-frei: an" : "Pkw-Verbot (nur ÖPNV): aus", busOnly))
                rules.SetBanned(seg.Id, busOnly ? banned & ~(VehicleClass.Car | VehicleClass.Truck) : banned | VehicleClass.Car | VehicleClass.Truck);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Tempolimit: {(limit > 0f ? $"{limit:0} km/h" : $"Standard ({seg.Type.SpeedLimitKmh:0} km/h)")}", small, GUILayout.Width(230f));
            if (GUILayout.Button("ändern")) rules.SetSpeedLimit(seg.Id, NextSpeed(limit));
            GUILayout.EndHorizontal();
        }

        void DrawApproachLetters()
        {
            var node = tool.SelectedNode;
            if (node == null || tool.Camera == null) return;
            var info = Traffic.Junctions.Get(node);
            for (int i = 0; i < info.Approaches.Count; i++)
            {
                var lanes = info.IncomingLanes[info.Approaches[i]];
                var lane = lanes[0];
                lane.Sample(Mathf.Max(0f, lane.Length - 15f), 0f, out var p, out float h, out _);
                var sp = tool.Camera.WorldToScreenPoint(SimConvert.ToWorld(p, h + 2f));
                if (sp.z <= 0f) continue;
                GUI.Box(new Rect(sp.x - 14f, Screen.height - sp.y - 12f, 28f, 24f), Letter(i));
            }
        }

        // ------------------------------------------------------------ Texte

        static bool Button(string text, bool active)
        {
            var old = GUI.backgroundColor;
            if (active) GUI.backgroundColor = Selected;
            bool c = GUILayout.Button(text, GUILayout.Height(24f));
            GUI.backgroundColor = old;
            return c;
        }

        static string Letter(int i) => ((char)('A' + i)).ToString();

        static string Compass(SimVector2 dir)
        {
            float b = RoadGeometry.Bearing(dir);
            string[] names = { "Norden", "Nordosten", "Osten", "Südosten", "Süden", "Südwesten", "Westen", "Nordwesten" };
            return names[Mathf.RoundToInt(b / 45f) % 8];
        }

        static string SignName(ApproachSign s, JunctionControl c)
        {
            if (c == JunctionControl.TrafficLights) return "Ampel";
            if (c == JunctionControl.RightBeforeLeft) return "rechts vor links";
            switch (s)
            {
                case ApproachSign.Priority: return "<color=#ffd31a>Vorfahrtstraße</color>";
                case ApproachSign.Yield: return "Vorfahrt gewähren";
                case ApproachSign.Stop: return "<color=#ff4040>Stopp</color>";
                default: return "rechts vor links";
            }
        }

        static ApproachSign NextSign(ApproachSign s) =>
            s == ApproachSign.Priority ? ApproachSign.Yield : s == ApproachSign.Yield ? ApproachSign.Stop : ApproachSign.Priority;

        static string TurnName(TurnType t) =>
            t == TurnType.Left ? "↰ links" : t == TurnType.Right ? "↱ rechts" : t == TurnType.UTurn ? "↶ wenden" : "↑ geradeaus";

        static string BanName(VehicleClass b) =>
            b == VehicleClass.None ? "erlaubt" : b == VehicleClass.Truck ? "Lkw verboten" : "VERBOTEN";

        static VehicleClass NextBan(VehicleClass b) =>
            b == VehicleClass.None ? VehicleClass.All : b == VehicleClass.All ? VehicleClass.Truck : VehicleClass.None;

        static float NextSpeed(float current)
        {
            int i = System.Array.IndexOf(SpeedSteps, current);
            return SpeedSteps[(i + 1) % SpeedSteps.Length];
        }
    }
}
