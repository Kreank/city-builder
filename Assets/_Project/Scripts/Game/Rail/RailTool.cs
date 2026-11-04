using System.Collections.Generic;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Rail;
using CityBuilder.Simulation.Roads;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Rail
{
    /// <summary>
    /// Bahn-Werkzeug (<b>Taste 7</b>, Modus <see cref="RoadTool.Mode.Rail"/>; 7 war frei – 1–3 Tempo, 4 Dienste, 5 ÖPNV, 6 Wohnhäuser):
    /// <list type="bullet">
    /// <item><b>Gleis</b>: Start anklicken, Ende anklicken (Kette läuft weiter). Beginnt das Gleis an einem vorhandenen Gleis,
    ///   schließt es tangential an (Bogen ab dem Anschluss, Weiche mitten auf dem Gleis). Bauhöhe 0 = automatische Trassierung.</item>
    /// <item><b>Gleisbogen</b>: Start, Kontrollpunkt, Ende (Radius ≥ 150 m).</item>
    /// <item><b>Personen-/Güterbahnhof</b>: rastet am nächsten Gleis ein, Vorderseite zur Straße.</item>
    /// <item><b>Abriss</b>: Gleisstück oder Bahnhof anklicken (Hauptstrecke nicht, belegte Gleise nicht).</item>
    /// </list>
    /// </summary>
    public sealed class RailTool : MonoBehaviour
    {
        public enum RailAction { Track, Curve, PassengerStation, FreightStation, Remove }

        RoadTool tool;
        Material okMaterial, badMaterial;
        LineRenderer line;
        GameObject ghost;
        MeshRenderer ghostRenderer;
        bool hasStart, hasControl;
        SimVector2 start, control;
        string label;
        bool labelError;
        Vector3 labelWorld;

        public RailAction Current { get; private set; } = RailAction.Track;
        public bool Active => tool != null && tool.CurrentMode == RoadTool.Mode.Rail;
        /// <summary>Bauhöhe über Gelände (0 = automatische Trassierung mit Tunnel/Brücke, sonst feste Höhe: Brücke/Tunnel).</summary>
        public float HeightOffset { get; private set; }
        public string Label => label;
        public bool LabelIsError => labelError;
        public bool Drawing => hasStart;

        RailSystem Rail => tool.Runner.World.Rail;

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            if (tool.Runner != null) tool.Runner.WorldReplaced += Cancel;
            tool.RegisterOperation(() => Active && hasStart, Cancel, Cancel); // UX: Esc/Rechtsklick bricht das Gleis ab
            okMaterial = new Material(transparent) { name = "RailPreviewOk", color = new Color(0.3f, 0.95f, 0.5f, 0.55f) };
            badMaterial = new Material(transparent) { name = "RailPreviewBad", color = new Color(1f, 0.25f, 0.2f, 0.6f) };
        }

        void Start()
        {
            ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghost.name = "Rail Preview";
            Destroy(ghost.GetComponent<Collider>());
            ghost.transform.SetParent(transform, false);
            ghostRenderer = ghost.GetComponent<MeshRenderer>();
            ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghost.SetActive(false);
            var go = new GameObject("Rail Track Preview");
            go.transform.SetParent(transform, false);
            line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = 4.5f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.sharedMaterial = okMaterial;
            line.enabled = false;
        }

        void OnDestroy()
        {
            if (okMaterial != null) Destroy(okMaterial);
            if (badMaterial != null) Destroy(badMaterial);
        }

        // ------------------------------------------------------------ Steuerung

        public void Select(RailAction action)
        {
            Cancel();
            Current = action;
            if (!Active) tool.SetMode(RoadTool.Mode.Rail);
        }

        public void Cancel() { hasStart = hasControl = false; }
        public void ChangeHeight(float delta) => HeightOffset = Mathf.Clamp(HeightOffset + delta, RoadBuilder.MinHeight, RoadBuilder.MaxHeight);

        void Update()
        {
            if (tool == null) return;
            var kb = GameKeyboard.Current;
            if (kb == null) return;
            if (kb.digit7Key.wasPressedThisFrame && !kb.shiftKey.isPressed) tool.ToggleMode(RoadTool.Mode.Rail); // erneut 7 = Auswählen
            if (!Active) { Cancel(); return; }
            if (kb.pageUpKey.wasPressedThisFrame) ChangeHeight(5f);
            if (kb.pageDownKey.wasPressedThisFrame) ChangeHeight(-5f);
        }

        void LateUpdate()
        {
            label = null;
            if (tool == null || !Active || !tool.HasCursor || tool.Runner.World?.Rail == null) { Hide(); return; }
            var mouse = Mouse.current;
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            var p = tool.Cursor;
            switch (Current)
            {
                case RailAction.Track:
                case RailAction.Curve: UpdateTrack(p, clicked); ghost.SetActive(false); break;
                case RailAction.PassengerStation: UpdateStation(StationCatalog.Passenger, p, clicked); line.enabled = false; break;
                case RailAction.FreightStation: UpdateStation(StationCatalog.Freight, p, clicked); line.enabled = false; break;
                default: UpdateRemove(p, clicked); break;
            }
        }

        void Hide()
        {
            if (ghost != null) ghost.SetActive(false);
            if (line != null) line.enabled = false;
        }

        float GroundAt(SimVector2 p) => tool.Runner.World.Terrain.HeightAt(p);

        // ------------------------------------------------------------ Gleis

        void UpdateTrack(SimVector2 p, bool clicked)
        {
            var rail = Rail;
            var cursorAnchor = RoadBuilder.ResolveAnchor(rail.Tracks, p, 8f);
            var cur = cursorAnchor.Position;
            if (!hasStart)
            {
                line.enabled = false;
                SetLabel(cursorAnchor.Kind == AnchorKind.Free ? "Gleisanfang setzen (an ein Gleis klicken = Weiche/Anschluss)"
                    : cursorAnchor.Kind == AnchorKind.Segment ? "Weiche: Gleis zweigt hier tangential ab" : "an Gleisende anschließen",
                    false, SimConvert.ToWorld(cur, GroundAt(cur) + 6f));
                if (clicked) { start = cur; hasStart = true; hasControl = false; }
                return;
            }
            bool curve = Current == RailAction.Curve;
            if (curve && !hasControl)
            {
                // Kontrollpunkt wählen: Vorschau als Gerade bis zum Mauszeiger
                ShowLine(start, (start + cur) * 0.5f, cur, true, h => GroundAt(Bezier(start, (start + cur) * 0.5f, cur, h)) + 0.8f);
                SetLabel("Bogen: Kontrollpunkt setzen", false, SimConvert.ToWorld(cur, GroundAt(cur) + 6f));
                if (clicked) { control = cur; hasControl = true; }
                return;
            }
            SimVector2? ctrl = curve ? control : TangentControl(rail, start, cur);
            bool auto = Mathf.Abs(HeightOffset) < 0.01f;
            var plan = rail.PlanTrack(start, ctrl, cur, 8f, GroundAt(start) + HeightOffset, GroundAt(cur) + HeightOffset, auto);
            ShowLine(plan.Start, plan.Control, plan.End, plan.IsValid, t => plan.HeightAt(t) + 0.8f);
            float radius = RailSystem.MinRadius(plan.Start, plan.Control, plan.End);
            string info = plan.IsValid
                ? $"Gleis {plan.Length:0} m · {plan.Cost:0} € · Radius {(float.IsInfinity(radius) ? "gerade" : $"{radius:0} m")}" +
                  (auto ? "" : $" · Bauhöhe {HeightOffset:+0;-0} m") + (plan.GradeSeparations > 0 ? $" · {plan.GradeSeparations} Kreuzungen über/unter" : "") +
                  (plan.Crossings.Count > 0 ? $" · {plan.Crossings.Count} Weichen/Kreuzungen" : "")
                : RailSystem.ErrorText(plan.Error);
            // --- Wasserstraße: Gleisbrücke über die Fahrrinne – lichte Höhe prüfen
            var bridge = tool.Runner.World.Shipping != null ? tool.Runner.World.Shipping.CheckBridge(plan, rail: true) : default;
            if (plan.IsValid && bridge.Text != null) info += " · " + bridge.Text.Replace("oder Klappbrücke (Hafen-Werkzeug 8)", "(Gleise haben keine Klappbrücke)");
            // --- Ende Wasserstraße
            SetLabel(info, !plan.IsValid, SimConvert.ToWorld(cur, plan.HeightAt(1f) + 6f));
            if (!clicked || !plan.IsValid) return;
            var built = rail.BuildTrack(plan);
            if (built.Count == 0) return;
            start = plan.End; // Kette: weiter am Ende
            hasControl = false;
        }

        /// <summary>Gerade ab einem Gleis: tangential anschließen (Kontrollpunkt auf der Tangente am Anschluss).</summary>
        static SimVector2? TangentControl(RailSystem rail, SimVector2 from, SimVector2 to)
        {
            var a = RoadBuilder.ResolveAnchor(rail.Tracks, from, 1f);
            SimVector2 dir;
            if (a.Kind == AnchorKind.Segment) dir = a.Segment.Tangent(a.T);
            else if (a.Kind == AnchorKind.Node && a.Node.SegmentIds.Count > 0)
            {
                var seg = rail.Tracks.GetSegment(a.Node.SegmentIds[0]);
                dir = -RoadGeometry.OutgoingDirection(seg, a.Node); // Fortsetzung über das Gleisende hinaus
            }
            else return null;
            var d = to - from;
            float along = SimVector2.Dot(d, dir);
            if (along < 0f) { dir = -dir; along = -along; }
            if (along < 1f) return null;
            return from + dir * (along * 0.5f);
        }

        static SimVector2 Bezier(SimVector2 a, SimVector2 c, SimVector2 b, float t) => Simulation.Roads.Bezier.Evaluate(a, c, b, t);

        void ShowLine(SimVector2 a, SimVector2 c, SimVector2 b, bool ok, System.Func<float, float> height)
        {
            const int n = 32;
            var pts = new Vector3[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                pts[i] = SimConvert.ToWorld(Bezier(a, c, b, t), height(t));
            }
            line.positionCount = pts.Length;
            line.SetPositions(pts);
            line.sharedMaterial = ok ? okMaterial : badMaterial;
            line.enabled = true;
        }

        // ------------------------------------------------------------ Bahnhof

        void UpdateStation(StationType type, SimVector2 p, bool clicked)
        {
            var rail = Rail;
            var plan = rail.PlanStation(type, p);
            ghost.SetActive(true);
            ghost.transform.SetPositionAndRotation(SimConvert.ToWorld(plan.Position, plan.Height + 5f), Quaternion.LookRotation(new Vector3(plan.Front.X, 0f, plan.Front.Y)));
            ghost.transform.localScale = new Vector3(type.Width, 10f, type.Depth);
            ghostRenderer.sharedMaterial = plan.IsValid ? okMaterial : badMaterial;
            string info = plan.IsValid
                ? $"{type.DisplayName} · {plan.PlatformTracks} Bahnsteiggleise · {plan.Cost:0} € (+{type.UpkeepPerWeek:0} €/Woche)" +
                  (plan.HasRoadAccess ? " · Straße an der Vorderseite" : " · noch keine Straße vor dem Gebäude (außer Betrieb)")
                : StationErrorText(plan);
            SetLabel(info, !plan.IsValid, SimConvert.ToWorld(plan.Position, plan.Height + 14f));
            if (clicked && plan.IsValid)
            {
                var s = rail.BuildStation(plan);
                if (s != null) CityBuilder.Game.UI.Hud.HudMessages.Info(s.Name, $"{s.Type.DisplayName} · {s.Platforms.Count} Gleise" +
                    (s.InService ? " · in Betrieb" : s.HasAccess ? " · Bahnsteig nicht an der Hauptstrecke" : " · Straße vor dem Gebäude bauen"),
                    CityBuilder.Game.UI.Hud.HudIconKind.Train, "rail-station");
            }
        }

        public static string StationErrorText(StationPlan plan)
        {
            switch (plan.Error)
            {
                case StationBuildError.NoTrack: return $"Bahnhof rastet an einem Gleis ein (≤ {RailRules.StationSnapRadius:0} m, ebenerdig, Bahnsteig parallel)";
                case StationBuildError.OutOfWorld: return "Außerhalb der Karte";
                case StationBuildError.TileNotOwned: return CityBuilder.Simulation.Roads.BuildArea.NotOwnedText; // Kacheln
                case StationBuildError.OnWater: return "Nicht im Wasser bauen";
                case StationBuildError.TooSteep: return $"Zu steil (max. {RailRules.StationMaxSlope:0} m Höhenunterschied)";
                case StationBuildError.OverlapsRoad: return "Steht auf einer Straße";
                case StationBuildError.OverlapsTrack: return "Steht auf einem Gleis";
                case StationBuildError.OverlapsStructure: return plan.BlockedBy != null ? $"Überschneidet {plan.BlockedBy.Name}" : "Überschneidet ein Bauwerk";
                case StationBuildError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                default: return plan.Error.ToString();
            }
        }

        // ------------------------------------------------------------ Abriss

        void UpdateRemove(SimVector2 p, bool clicked)
        {
            var rail = Rail;
            var st = rail.StationAt(p, 2f);
            if (st != null)
            {
                line.enabled = false;
                ghost.SetActive(true);
                ghost.transform.SetPositionAndRotation(SimConvert.ToWorld(st.Position, st.Height + 6f), Quaternion.LookRotation(new Vector3(st.Front.X, 0f, st.Front.Y)));
                ghost.transform.localScale = new Vector3(st.Type.Width + 1f, 12f, st.Type.Depth + 1f);
                ghostRenderer.sharedMaterial = badMaterial;
                SetLabel($"{st.Name} abreißen (+{st.Type.Cost * Simulation.Economy.EconomyRules.DemolitionRefundShare:0} €)", true,
                    SimConvert.ToWorld(st.Position, st.Height + 16f));
                if (clicked) rail.DemolishStation(st.Id);
                return;
            }
            ghost.SetActive(false);
            var seg = rail.TrackNear(p, 3f);
            if (seg == null) { line.enabled = false; SetLabel("Gleis oder Bahnhof anklicken", false, SimConvert.ToWorld(p, GroundAt(p) + 6f)); return; }
            ShowLine(seg.Start.Position, seg.Control, seg.End.Position, false, t => seg.HeightAt(t) + 1f);
            string reason = rail.DemolishBlockReason(seg.Id);
            SetLabel(reason ?? $"Gleis abreißen (+{RoadBuilder.DemolishRefund(seg):0} €)", true, SimConvert.ToWorld(p, seg.HeightAt(seg.ClosestT(p)) + 6f));
            if (clicked && reason == null) rail.DemolishTrack(seg.Id);
        }

        void SetLabel(string text, bool error, Vector3 world)
        {
            label = text;
            labelError = error;
            labelWorld = world;
        }

        // ------------------------------------------------------------ Beschriftung

        void OnGUI()
        {
            if (CityBuilder.Game.UI.Panels.WorldLabels.Enabled)
            {
                if (Event.current.type != EventType.Layout) return;
                CityBuilder.Game.UI.Panels.WorldLabels.Begin(this);
                if (Active && !string.IsNullOrEmpty(label))
                    CityBuilder.Game.UI.Panels.WorldLabels.Add(this, labelWorld, label,
                        labelError ? CityBuilder.Game.UI.Panels.WorldLabelStyle.Error : CityBuilder.Game.UI.Panels.WorldLabelStyle.Normal, -6f, above: true);
                return;
            }
            if (!Active || string.IsNullOrEmpty(label) || tool.Camera == null) return;
            var sp = tool.Camera.WorldToScreenPoint(labelWorld);
            if (sp.z <= 0f) return;
            var content = new GUIContent(labelError ? $"<color=#ff7060>{label}</color>" : label);
            var style = new GUIStyle(GUI.skin.box) { fontSize = 12, richText = true, alignment = TextAnchor.MiddleCenter };
            var size = style.CalcSize(content);
            GUI.Box(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y - 6f, size.x + 8f, size.y + 4f), content, style);
        }
    }
}
