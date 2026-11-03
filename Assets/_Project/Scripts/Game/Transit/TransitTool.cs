using System.Collections.Generic;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Transit;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Transit
{
    /// <summary>
    /// ÖPNV-Werkzeug (Taste 5, Modus <see cref="RoadTool.Mode.Transit"/>):
    /// <list type="bullet">
    /// <item><b>Haltestelle</b>: rastet am Rand der nächsten Stadtstraße ein (Seite des Mauszeigers; ⇧ = beide Seiten als Paar).</item>
    /// <item><b>Linie</b>: Haltestellen nacheinander anklicken (Klick auf freie Straße setzt dort gleich eine Haltestelle),
    ///   Vorschau der Route über das Straßennetz; Klick auf die erste Haltestelle schließt eine Rundlinie,
    ///   Enter/„Fertig“ legt sie an (Rund- oder Pendellinie nach Schalter), Rechtsklick nimmt den letzten Halt zurück, Esc verwirft.</item>
    /// <item><b>Depot</b>: richtet sich automatisch zur nächsten Stadtstraße aus und rastet am Rand ein (Anschluss = Mitte der
    ///   Vorderseite); mit Strg frei platzieren, , . drehen.</item>
    /// <item><b>Abriss</b>: Haltestelle oder Depot anklicken (25 % zurück).</item>
    /// </list>
    /// Taste 5: im Werkzeug belegt waren 1–4, F1–F5, F9/F10; 5 ist frei (Linienfenster: F6).
    /// </summary>
    public sealed class TransitTool : MonoBehaviour
    {
        public enum TransitAction { Stop, Line, Depot, Remove }

        RoadTool tool;
        Material okMaterial, badMaterial, routeMaterial;
        GameObject ghost;
        MeshRenderer ghostRenderer;
        LineRenderer route;
        readonly List<int> drawing = new List<int>();
        int previewKey = int.MinValue, previewVersion = -1, previewRoads = -1;
        LinePlan preview;

        string label;
        bool labelError;
        Vector3 labelWorld;

        public TransitAction Current { get; private set; } = TransitAction.Stop;
        public bool Active => tool != null && tool.CurrentMode == RoadTool.Mode.Transit;
        /// <summary>Neue Linie als Rundlinie (sonst Pendel).</summary>
        public bool DrawLoop { get; set; } = true;
        /// <summary>Busse für eine neue Linie.</summary>
        public int NewLineVehicles { get; private set; } = 3;
        /// <summary>Haltestellen standardmäßig auf beiden Straßenseiten (⇧ kehrt um).</summary>
        public bool BothSides { get; set; }
        public float Rotation { get; private set; }
        public IReadOnlyList<int> DrawingStops => drawing;
        public LinePlan Preview => preview;
        /// <summary>Hinweis für die Bauleiste (Fehler/Kosten unter der Maus).</summary>
        public string Label => label;
        public bool LabelIsError => labelError;

        TransitSystem Transit => tool.Runner.World.Transit;

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            // UX: gezeichnete Linie = laufender Vorgang (Esc verwirft, Rechtsklick nimmt den letzten Halt zurück)
            tool.RegisterOperation(() => Active && Current == TransitAction.Line && drawing.Count > 0, () => drawing.Clear(), UndoStop);
            okMaterial = new Material(transparent) { name = "TransitPreviewOk", color = new Color(0.3f, 0.9f, 1f, 0.45f) };
            badMaterial = new Material(transparent) { name = "TransitPreviewBad", color = new Color(1f, 0.25f, 0.2f, 0.5f) };
            routeMaterial = new Material(transparent) { name = "TransitRoutePreview", color = new Color(1f, 0.85f, 0.2f, 0.9f) };
        }

        void Start()
        {
            ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghost.name = "Transit Preview";
            Destroy(ghost.GetComponent<Collider>());
            ghost.transform.SetParent(transform, false);
            ghostRenderer = ghost.GetComponent<MeshRenderer>();
            ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghost.SetActive(false);

            var go = new GameObject("Transit Route Preview");
            go.transform.SetParent(transform, false);
            route = go.AddComponent<LineRenderer>();
            route.useWorldSpace = true;
            route.widthMultiplier = 2.5f;
            route.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            route.sharedMaterial = routeMaterial;
            route.enabled = false;
        }

        void OnDestroy()
        {
            if (okMaterial != null) Destroy(okMaterial);
            if (badMaterial != null) Destroy(badMaterial);
            if (routeMaterial != null) Destroy(routeMaterial);
        }

        // ------------------------------------------------------------ Steuerung (Bauleiste/Tasten)

        public void Select(TransitAction action)
        {
            if (action != TransitAction.Line) drawing.Clear();
            Current = action;
            if (!Active) tool.SetMode(RoadTool.Mode.Transit);
        }

        public void Rotate(float degrees) => Rotation = Mathf.Repeat(Rotation + degrees, 360f);
        public void SetNewLineVehicles(int n) => NewLineVehicles = Mathf.Clamp(n, 1, 30);
        public void CancelLine() => drawing.Clear();
        public void UndoStop() { if (drawing.Count > 0) drawing.RemoveAt(drawing.Count - 1); }

        /// <summary>Gezeichnete Linie anlegen. false = weniger als 2 Halte oder Route nicht befahrbar.</summary>
        public bool FinishLine()
        {
            if (drawing.Count < 2) return false;
            var line = Transit.CreateLine(drawing, DrawLoop, NewLineVehicles);
            if (line == null) return false;
            drawing.Clear();
            CityBuilder.Game.UI.Hud.HudMessages.Info(line.Name, $"{line.Stops.Count} Haltestellen · {line.LengthMeters / 1000f:0.0} km · {line.TargetVehicles} Busse",
                CityBuilder.Game.UI.Hud.HudIconKind.Bus, "transit-line");
            return true;
        }

        // ------------------------------------------------------------ Update

        void Update()
        {
            if (tool == null) return;
            var kb = GameKeyboard.Current;
            if (kb == null) return;
            if (kb.digit5Key.wasPressedThisFrame && !kb.shiftKey.isPressed)
                tool.ToggleMode(RoadTool.Mode.Transit); // UX: erneut 5 = Auswählen
            if (!Active) { drawing.Clear(); return; }
            if (kb.commaKey.wasPressedThisFrame) Rotate(-15f);
            if (kb.periodKey.wasPressedThisFrame) Rotate(15f);
            if (Current == TransitAction.Line)
            {
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) FinishLine();
                // UX: Esc verwirft / Rechtsklick nimmt den letzten Halt zurück – über RoadTool.RegisterOperation, damit
                // ohne gezeichnete Linie dieselben Tasten zum Auswählen zurückführen
            }
        }

        void LateUpdate()
        {
            label = null;
            if (tool == null || !Active || !tool.HasCursor || tool.Runner.World?.Transit == null) { Hide(); return; }
            var mouse = Mouse.current;
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool shift = Keyboard.current != null && Keyboard.current.shiftKey.isPressed;
            var p = tool.Cursor;
            switch (Current)
            {
                case TransitAction.Stop: UpdateStop(p, clicked, BothSides != shift); route.enabled = false; break;
                case TransitAction.Line: UpdateLine(p, clicked, BothSides != shift); break;
                case TransitAction.Depot: UpdateDepot(p, clicked); route.enabled = false; break;
                default: UpdateRemove(p, clicked); route.enabled = false; break;
            }
        }

        void Hide()
        {
            if (ghost != null) ghost.SetActive(false);
            if (route != null) route.enabled = false;
        }

        float GroundAt(SimVector2 p) => tool.Runner.World.Terrain.HeightAt(p);

        void UpdateStop(SimVector2 p, bool clicked, bool both)
        {
            var plan = Transit.PlanStop(p, both);
            ShowStopGhost(plan);
            SetLabel(plan.IsValid ? $"Haltestelle ({SideText(plan.Side)}) · {plan.Cost:0} € (+{TransitRules.StopUpkeepPerWeek:0} €/Woche) · ⇧ beide Seiten" : ErrorText(plan.Error),
                !plan.IsValid, SimConvert.ToWorld(plan.Position, GroundAt(plan.Position) + 6f));
            if (clicked && plan.IsValid) Transit.BuildStop(plan);
        }

        void ShowStopGhost(StopPlan plan)
        {
            var world = tool.Runner.World;
            var seg = world.Roads.GetSegment(plan.SegmentId);
            if (seg == null) { ghost.SetActive(false); return; }
            var right = seg.Right(plan.T);
            var c = plan.Side == StopSide.Both ? plan.Position
                : plan.Position + right * (plan.Side == StopSide.Along ? 1f : -1f) * seg.Type.HalfWidth * 0.7f;
            var tan = seg.Tangent(plan.T);
            ghost.SetActive(true);
            ghost.transform.SetPositionAndRotation(SimConvert.ToWorld(c, seg.HeightAt(plan.T) + 1.3f), Quaternion.LookRotation(new Vector3(tan.X, 0f, tan.Y)));
            ghost.transform.localScale = new Vector3(plan.Side == StopSide.Both ? seg.Type.Width : 3f, 2.6f, 4f);
            ghostRenderer.sharedMaterial = plan.IsValid ? okMaterial : badMaterial;
        }

        void UpdateLine(SimVector2 p, bool clicked, bool both)
        {
            var t = Transit;
            var hover = t.StopNear(p, 14f);
            StopPlan newStop = null;
            if (hover == null)
            {
                newStop = t.PlanStop(p, both);
                ShowStopGhost(newStop);
            }
            else
            {
                ghost.SetActive(true);
                ghost.transform.SetPositionAndRotation(SimConvert.ToWorld(hover.Position, GroundAt(hover.Position) + 2f), Quaternion.identity);
                ghost.transform.localScale = new Vector3(6f, 4f, 6f);
                ghostRenderer.sharedMaterial = okMaterial;
            }

            // Vorschau: bisherige Halte + Halt unter der Maus
            var stops = new List<int>(drawing);
            if (hover != null && (stops.Count == 0 || stops[stops.Count - 1] != hover.Id)) stops.Add(hover.Id);
            int key = Hash(stops) * 31 + (DrawLoop ? 1 : 0);
            if (key != previewKey || previewVersion != t.Version || previewRoads != tool.Runner.World.Roads.Version)
            {
                previewKey = key;
                previewVersion = t.Version;
                previewRoads = tool.Runner.World.Roads.Version;
                preview = t.PreviewLine(stops, DrawLoop);
                var pts = TransitView.RoutePoints(tool.Runner.World, preview.Visits, preview.Legs, 1.2f);
                route.positionCount = pts.Count;
                route.SetPositions(pts.ToArray());
                route.enabled = pts.Count > 1;
            }

            string info;
            bool error = false;
            if (preview != null && preview.Stops.Count >= 2)
            {
                info = $"Linie: {preview.Stops.Count} Halte · {preview.LengthMeters / 1000f:0.00} km · Umlauf {preview.CycleSeconds:0} s · " +
                       $"Takt {preview.CycleSeconds / Mathf.Max(1, NewLineVehicles):0} s bei {NewLineVehicles} Bussen · Enter = fertig";
                if (preview.BrokenLeg >= 0) { info = $"Abschnitt {preview.BrokenLeg + 1} ist für Busse nicht befahrbar"; error = true; }
            }
            else info = drawing.Count == 0 ? "Erste Haltestelle anklicken (freie Straße: setzt eine neue)" : "Nächste Haltestelle anklicken";
            if (hover == null && newStop != null && !newStop.IsValid && newStop.Error != TransitBuildError.NoRoad) { info = ErrorText(newStop.Error); error = true; }
            SetLabel(info, error, SimConvert.ToWorld(p, GroundAt(p) + 8f));

            if (!clicked) return;
            if (hover != null)
            {
                if (drawing.Count >= 2 && hover.Id == drawing[0]) { DrawLoop = true; FinishLine(); return; }
                if (drawing.Count == 0 || drawing[drawing.Count - 1] != hover.Id) drawing.Add(hover.Id);
            }
            else if (newStop != null && newStop.IsValid)
            {
                var s = t.BuildStop(newStop);
                if (s != null) drawing.Add(s.Id);
            }
        }

        static int Hash(List<int> ids)
        {
            int h = 17;
            foreach (int i in ids) h = h * 486187739 + i;
            return h;
        }

        void UpdateDepot(SimVector2 p, bool clicked)
        {
            var t = Transit;
            var type = TransitCatalog.BusDepot;
            bool free = Keyboard.current != null && Keyboard.current.ctrlKey.isPressed;
            var pos = p;
            float rot = Rotation;
            if (!free && t.SnapDepot(type, p, out var snapped, out float snapRot)) { pos = snapped; rot = snapRot; }
            var plan = t.PlanDepot(type, pos, rot);
            ghost.SetActive(true);
            ghost.transform.SetPositionAndRotation(SimConvert.ToWorld(plan.Position, plan.Height + 4f), Quaternion.LookRotation(new Vector3(plan.Front.X, 0f, plan.Front.Y)));
            ghost.transform.localScale = new Vector3(type.Width, 8f, type.Depth);
            ghostRenderer.sharedMaterial = plan.IsValid ? okMaterial : badMaterial;
            string info = $"{type.DisplayName} · {type.VehicleCapacity} Busse · {type.Cost:0} € (+{type.UpkeepPerWeek:0} €/Woche, je Bus +{TransitRules.BusUpkeepPerWeek:0} €)";
            if (plan.Displaced.Count > 0) info += $" · reißt {plan.Displaced.Count} Gebäude ab";
            SetLabel(plan.IsValid ? info : ErrorText(plan.Error), !plan.IsValid, SimConvert.ToWorld(plan.Position, plan.Height + 12f));
            if (clicked && plan.IsValid) t.BuildDepot(plan);
        }

        void UpdateRemove(SimVector2 p, bool clicked)
        {
            var t = Transit;
            var d = t.DepotAt(p, 2f);
            if (d != null)
            {
                ghost.SetActive(true);
                ghost.transform.SetPositionAndRotation(SimConvert.ToWorld(d.Position, d.Height + 5f), Quaternion.LookRotation(new Vector3(d.Front.X, 0f, d.Front.Y)));
                ghost.transform.localScale = new Vector3(d.Type.Width + 1f, 10f, d.Type.Depth + 1f);
                ghostRenderer.sharedMaterial = badMaterial;
                SetLabel($"{d.Type.DisplayName} abreißen (+{d.Type.Cost * Simulation.Economy.EconomyRules.DemolitionRefundShare:0} €) – Busse fahren ein", true,
                    SimConvert.ToWorld(d.Position, d.Height + 14f));
                if (clicked) t.DemolishDepot(d.Id);
                return;
            }
            var s = t.StopNear(p, 14f);
            if (s == null) { ghost.SetActive(false); return; }
            ghost.SetActive(true);
            ghost.transform.SetPositionAndRotation(SimConvert.ToWorld(s.Position, GroundAt(s.Position) + 2f), Quaternion.identity);
            ghost.transform.localScale = new Vector3(8f, 4f, 8f);
            ghostRenderer.sharedMaterial = badMaterial;
            SetLabel($"Haltestelle {s.Name} abreißen · {t.LinesAt(s.Id).Count} Linien halten hier", true, SimConvert.ToWorld(s.Position, GroundAt(s.Position) + 8f));
            if (clicked) t.DemolishStop(s.Id);
        }

        void SetLabel(string text, bool error, Vector3 world)
        {
            label = text;
            labelError = error;
            labelWorld = world;
        }

        public static string SideText(StopSide s) => s == StopSide.Both ? "beide Seiten" : s == StopSide.Along ? "eine Seite" : "eine Seite";

        public static string ErrorText(TransitBuildError e)
        {
            switch (e)
            {
                case TransitBuildError.OutOfWorld: return "Außerhalb der Karte";
                case TransitBuildError.OnWater: return "Nicht im Wasser bauen";
                case TransitBuildError.TooSteep: return $"Zu steil (max. {TransitRules.DepotMaxSlope:0} m Höhenunterschied)";
                case TransitBuildError.OverlapsRoad: return "Steht auf einer Straße";
                case TransitBuildError.OverlapsFacility: return "Überschneidet ein anderes Bauwerk";
                case TransitBuildError.OverlapsPole: return "Strommast im Weg";
                case TransitBuildError.NoRoadAccess: return $"Keine Zufahrt: Stadtstraße höchstens {TransitRules.DepotRoadReach:0} m entfernt nötig";
                case TransitBuildError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                case TransitBuildError.NoRoad: return "Haltestellen an eine Stadtstraße setzen";
                case TransitBuildError.NotCityRoad: return "Haltestellen nur an Stadtstraßen (nicht an Autobahn/Rampe)";
                case TransitBuildError.Elevated: return "Nicht auf Brücken oder in Tunneln";
                case TransitBuildError.TooCloseToJunction: return $"Zu nah an der Kreuzung (mind. {TransitRules.MinDistanceToJunction:0} m)";
                case TransitBuildError.TooCloseToStop: return $"Zu nah an einer anderen Haltestelle (mind. {TransitRules.MinStopSpacing:0} m)";
                case TransitBuildError.NoLaneOnSide: return "Auf dieser Seite fährt kein Bus";
                default: return null;
            }
        }

        // ------------------------------------------------------------ Beschriftung über der Vorschau

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
