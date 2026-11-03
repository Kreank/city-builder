using CityBuilder.Game.Roads;
using CityBuilder.Game.UI;
using CityBuilder.Simulation.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Utilities
{
    /// <summary>
    /// Versorgungs-Werkzeug (J): Kraftwerke/Pumpen platzieren (, und . drehen), Stromleitungen und Wasserrohre
    /// Punkt für Punkt verlegen (Klick Start → Klick Ende, weiter in Kette, RMB beendet), Abriss.
    /// Aktiv, wenn das RoadTool im Modus <see cref="RoadTool.Mode.Utility"/> ist – das RoadTool liefert den Cursor.
    /// Alle Prüfungen und das Bauen macht die Simulation (<see cref="UtilitySystem"/>).
    /// </summary>
    public sealed class UtilityTool : MonoBehaviour
    {
        public enum UtilityAction { Facility, PowerLine, WaterPipe, Remove, SewagePipe } // SewagePipe: Abwasser (G3 erweitert)

        /// <summary>Höhe der Leiterseile über Gelände (Vorschau) bzw. Rohrtiefe darunter.</summary>
        public const float WireHeight = 12f;
        public const float PipeDepth = 2.5f;
        const float SnapDistance = 8f;

        static readonly Color Selected = new Color(0.45f, 0.75f, 1f);

        RoadTool tool;
        Material okMaterial, badMaterial;
        GameObject ghost;
        MeshRenderer ghostRenderer;
        LineRenderer lineRenderer;
        /// <summary>Vorschau der geplanten Masten (grün/blau = gültig, rot = kein zulässiger Standort).</summary>
        readonly System.Collections.Generic.List<GameObject> poleGhosts = new System.Collections.Generic.List<GameObject>();
        GUIStyle infoStyle, labelStyle;

        bool placingLine;
        SimVector2 lineStart;
        string label;
        bool labelError;
        Vector3 labelWorld;

        public UtilityAction Current { get; private set; } = UtilityAction.Facility;
        public UtilityFacilityType SelectedType { get; private set; } = UtilityCatalog.CoalPlant;
        public float Rotation { get; private set; }
        public bool Active => tool != null && tool.CurrentMode == RoadTool.Mode.Utility;

        UtilitySystem Utilities => tool.Runner.World.Utilities;

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            // UX: Esc/Rechtsklick beenden die Leitungskette (sonst zurück zum Auswählen)
            tool.RegisterOperation(() => Active && placingLine, () => placingLine = false);
            okMaterial = new Material(transparent) { name = "UtilityPreviewOk", color = new Color(0.3f, 0.9f, 1f, 0.45f) };
            badMaterial = new Material(transparent) { name = "UtilityPreviewBad", color = new Color(1f, 0.25f, 0.2f, 0.5f) };
        }

        void Start()
        {
            ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghost.name = "Utility Preview";
            Destroy(ghost.GetComponent<Collider>());
            ghost.transform.SetParent(transform, false);
            ghostRenderer = ghost.GetComponent<MeshRenderer>();
            ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghost.SetActive(false);

            var lineGo = new GameObject("Utility Line Preview");
            lineGo.transform.SetParent(transform, false);
            lineRenderer = lineGo.AddComponent<LineRenderer>();
            lineRenderer.useWorldSpace = true;
            lineRenderer.widthMultiplier = 0.8f;
            lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lineRenderer.enabled = false;
        }

        void OnDestroy()
        {
            if (okMaterial != null) Destroy(okMaterial);
            if (badMaterial != null) Destroy(badMaterial);
        }

        // ------------------------------------------------------------ Steuerung (Toolbar)

        public void Select(UtilityAction action)
        {
            Current = action;
            placingLine = false;
            if (!Active) tool.SetMode(RoadTool.Mode.Utility);
        }

        public void SelectFacility(UtilityFacilityType type)
        {
            SelectedType = type;
            Select(UtilityAction.Facility);
        }

        public void Rotate(float degrees) => Rotation = Mathf.Repeat(Rotation + degrees, 360f);

        // ------------------------------------------------------------ Update

        void Update()
        {
            if (tool == null) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.jKey.wasPressedThisFrame) tool.ToggleMode(RoadTool.Mode.Utility); // UX: erneut J = Auswählen
            if (!Active) return;
            if (kb.commaKey.wasPressedThisFrame) Rotate(-15f);
            if (kb.periodKey.wasPressedThisFrame) Rotate(15f);
            // Esc/Rechtsklick: über RoadTool.RegisterOperation (beendet die Kette, sonst zurück zum Auswählen)
        }

        void LateUpdate()
        {
            label = null;
            if (tool == null) return;
            tool.ForceUnderground = Active && (Current == UtilityAction.WaterPipe || Current == UtilityAction.SewagePipe);
            if (!Active || !tool.HasCursor || tool.Runner.World == null) { Hide(); placingLine &= Active; return; }

            var mouse = Mouse.current;
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            var p = tool.Cursor;
            switch (Current)
            {
                case UtilityAction.Facility: UpdateFacility(p, clicked); break;
                case UtilityAction.PowerLine: UpdateLine(UtilityKind.Power, p, clicked); break;
                case UtilityAction.WaterPipe: UpdateLine(UtilityKind.Water, p, clicked); break;
                case UtilityAction.SewagePipe: UpdateLine(UtilityKind.Sewage, p, clicked); break;
                case UtilityAction.Remove: UpdateRemove(p, clicked); break;
            }
        }

        void Hide()
        {
            if (ghost != null) ghost.SetActive(false);
            frontMarker?.Hide();
            if (lineRenderer != null) lineRenderer.enabled = false;
            HidePoles();
        }

        void HidePoles(int from = 0)
        {
            for (int i = from; i < poleGhosts.Count; i++) if (poleGhosts[i] != null) poleGhosts[i].SetActive(false);
        }

        /// <summary>Zeigt die von der Simulation geplanten Masten; ungültige rot.</summary>
        void ShowPoles(LinePlan plan)
        {
            int n = plan.Kind == UtilityKind.Power ? plan.Poles.Count : 0;
            while (poleGhosts.Count < n)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Pole Preview";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                poleGhosts.Add(go);
            }
            for (int i = 0; i < n; i++)
            {
                var pole = plan.Poles[i];
                var go = poleGhosts[i];
                go.SetActive(true);
                float baseH = Mathf.Max(Ground(pole.Position), tool.Runner.World.Terrain.WaterLevel);
                go.transform.SetPositionAndRotation(SimConvert.ToWorld(pole.Position, baseH + WireHeight * 0.5f), Quaternion.identity);
                // Grundfläche des Masts (inkl. Rand) – so viel Platz belegt er
                float foot = UtilitySystem.PoleFootprintHalf * 2f;
                go.transform.localScale = new Vector3(foot, WireHeight, foot);
                go.GetComponent<MeshRenderer>().sharedMaterial = pole.IsValid ? okMaterial : badMaterial;
            }
            HidePoles(n);
        }

        float Ground(SimVector2 p) => tool.Runner.World.Terrain.HeightAt(p);

        void UpdateFacility(SimVector2 p, bool clicked)
        {
            lineRenderer.enabled = false;
            HidePoles();
            var plan = Utilities.PlanFacilityAligned(SelectedType, p, Rotation); // UX: Kraftwerke/Wasserturm zur Straße
            ShowGhost(plan.Position, plan.Front, SelectedType.Width, SelectedType.Depth, plan.Height, 4f, plan.IsValid, SelectedType.NeedsRoadFront);
            var t = SelectedType;
            string turn = plan.Aligned ? (Rotation != 0f ? $"zur Straße + {Rotation:0}°" : "zur Straße ausgerichtet") : $"{Rotation:0}°";
            string info = $"{t.DisplayName} · {t.Capacity:0} {UtilityCatalog.Unit(t.Kind)} · {t.Cost:0} € (+{t.UpkeepPerDay:0} €/Tag) · {turn}";
            if (plan.Displaced.Count > 0) info += $" · reißt {plan.Displaced.Count} Gebäude ab";
            SetLabel(plan.IsValid ? info : ErrorText(plan.Error), !plan.IsValid, SimConvert.ToWorld(p, plan.Height + 8f));
            if (clicked && plan.IsValid) Utilities.BuildFacility(plan);
        }

        void UpdateLine(UtilityKind kind, SimVector2 raw, bool clicked)
        {
            ghost.SetActive(false);
            var p = Utilities.ResolveLinePoint(kind, raw, SnapDistance, out var snap);
            float offset = kind == UtilityKind.Power ? WireHeight : -PipeDepth;
            if (!placingLine)
            {
                HidePoles();
                // Nur Markierung am Cursor
                lineRenderer.enabled = true;
                lineRenderer.sharedMaterial = okMaterial;
                lineRenderer.positionCount = 2;
                lineRenderer.SetPosition(0, SimConvert.ToWorld(p, Ground(p)));
                lineRenderer.SetPosition(1, SimConvert.ToWorld(p, Ground(p) + offset));
                string hint = kind == UtilityKind.Power
                    ? " · Stadtstraßen führen Strom schon mit – Leitung nur für Lücken/Überland"
                    : kind == UtilityKind.Sewage
                        ? $" · Kanäle selbst verlegen (Straßen führen keinen mit) – entlang der Straße einrasten, Gebäude ≤ {ConnectionRules.BuildingReach:0} m sind angeschlossen"
                        : " · Stadtstraßen führen Wasser schon mit – Rohr nur für Lücken";
                SetLabel($"{UtilityCatalog.LineName(kind)}: Startpunkt wählen{SnapText(snap)}{hint}", false, SimConvert.ToWorld(p, Ground(p) + WireHeight));
                if (clicked) { lineStart = p; placingLine = true; }
                return;
            }

            var plan = Utilities.PlanLine(kind, lineStart, p);
            lineRenderer.enabled = true;
            lineRenderer.sharedMaterial = plan.IsValid ? okMaterial : badMaterial;
            ShowPoles(plan);
            if (kind == UtilityKind.Power && plan.Poles.Count > 0)
            {
                // Seilverlauf über die geplanten Masten (Enden = Anschlusspunkte)
                lineRenderer.positionCount = plan.Poles.Count + 2;
                lineRenderer.SetPosition(0, SimConvert.ToWorld(lineStart, Ground(lineStart) + offset));
                for (int i = 0; i < plan.Poles.Count; i++)
                {
                    var q = plan.Poles[i].Position;
                    lineRenderer.SetPosition(i + 1, SimConvert.ToWorld(q, Ground(q) + offset));
                }
                lineRenderer.SetPosition(plan.Poles.Count + 1, SimConvert.ToWorld(p, Ground(p) + offset));
            }
            else
            {
                const int n = 16;
                lineRenderer.positionCount = n + 1;
                for (int i = 0; i <= n; i++)
                {
                    var q = SimVector2.Lerp(lineStart, p, i / (float)n);
                    lineRenderer.SetPosition(i, SimConvert.ToWorld(q, Ground(q) + offset));
                }
            }
            string poles = kind == UtilityKind.Power ? $" · {plan.Poles.Count} Masten" : "";
            string text = plan.IsValid
                ? $"{UtilityCatalog.LineName(kind)} · {plan.Length:0} m{poles} · {plan.Cost:0} €{SnapText(snap)} · RMB beenden"
                : ErrorText(plan.Error);
            SetLabel(text, !plan.IsValid, SimConvert.ToWorld(p, Ground(p) + WireHeight));
            if (clicked && plan.IsValid)
            {
                Utilities.BuildLine(plan);
                lineStart = p; // in Kette weiterbauen
            }
        }

        void UpdateRemove(SimVector2 p, bool clicked)
        {
            HidePoles();
            var f = Utilities.FacilityAt(p, 2f);
            if (f != null)
            {
                lineRenderer.enabled = false;
                ShowGhost(f.Position, f.Front, f.Type.Width + 1f, f.Type.Depth + 1f, f.Height, 6f, false, false);
                SetLabel($"{f.Type.DisplayName} abreißen", true, SimConvert.ToWorld(f.Position, f.Height + 10f));
                if (clicked) Utilities.DemolishFacility(f.Id);
                return;
            }
            ghost.SetActive(false);
            var line = Utilities.LineNear(p, 6f);
            if (line == null) { lineRenderer.enabled = false; return; }
            float offset = line.Kind == UtilityKind.Power ? WireHeight : -PipeDepth;
            lineRenderer.enabled = true;
            lineRenderer.sharedMaterial = badMaterial;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, SimConvert.ToWorld(line.A, Ground(line.A) + offset));
            lineRenderer.SetPosition(1, SimConvert.ToWorld(line.B, Ground(line.B) + offset));
            SetLabel($"{UtilityCatalog.LineName(line.Kind)} abreißen ({line.Length:0} m)", true, SimConvert.ToWorld(p, Ground(p) + WireHeight));
            if (clicked) Utilities.DemolishLine(line.Id);
        }

        void ShowGhost(SimVector2 pos, SimVector2 front, float width, float depth, float height, float tall, bool valid, bool showFront)
        {
            ghost.SetActive(true);
            ghost.transform.SetPositionAndRotation(SimConvert.ToWorld(pos, height + tall * 0.5f),
                Quaternion.LookRotation(new Vector3(front.X, 0f, front.Y)));
            ghost.transform.localScale = new Vector3(width, tall, depth);
            ghostRenderer.sharedMaterial = valid ? okMaterial : badMaterial;
            // UX: Vorderseite (Zufahrt) als Streifen + Pfeil – nur bei Bauwerken mit Straßenfront
            if (frontMarker == null) frontMarker = new PlacementGhost(transform, "Utility Front");
            if (showFront) frontMarker.Show(pos, front, width, depth, height, tall, valid ? okMaterial : badMaterial);
            else frontMarker.Hide();
        }

        PlacementGhost frontMarker;

        void SetLabel(string text, bool error, Vector3 world)
        {
            label = text;
            labelError = error;
            labelWorld = world;
        }

        static string SnapText(UtilitySnap s)
        {
            switch (s)
            {
                case UtilitySnap.LineEnd: return " · an Leitungsende";
                case UtilitySnap.Line: return " · an Leitung";
                case UtilitySnap.Facility: return " · an Bauwerk";
                case UtilitySnap.Road: return " · an Straße";
                case UtilitySnap.Building: return " · an Gebäude";
                default: return "";
            }
        }

        public static string ErrorText(UtilityBuildError e)
        {
            switch (e)
            {
                case UtilityBuildError.OutOfWorld: return "Außerhalb der Karte";
                case UtilityBuildError.OverlapsRoad: return "Steht auf einer Straße";
                case UtilityBuildError.OverlapsFacility: return "Überschneidet ein anderes Bauwerk";
                case UtilityBuildError.OnWater: return "Nicht im Wasser bauen";
                case UtilityBuildError.NeedsWater: return "Muss am Ufer stehen (Wasser in Reichweite)";
                case UtilityBuildError.TooSteep: return $"Zu steil (max. {UtilitySystem.MaxFacilitySlope:0} m Höhenunterschied)";
                case UtilityBuildError.TooShort: return "Zu kurz";
                case UtilityBuildError.Duplicate: return "Diese Leitung gibt es schon";
                case UtilityBuildError.PoleOnRoad:
                    return $"Kein Maststandort: Masten nie auf Fahrbahn/Gehweg, Straße überspannen geht nur bis {UtilitySystem.MaxPoleSpan:0} m";
                case UtilityBuildError.PoleOnWater: return $"Kein Maststandort: Wasser breiter als {UtilitySystem.MaxPoleSpan:0} m Spannweite";
                case UtilityBuildError.PoleOnFacility: return "Kein Maststandort: Bauwerk im Weg";
                case UtilityBuildError.PoleOnBuilding: return "Kein Maststandort: Gebäude im Weg (wird nicht abgerissen) – andere Trasse wählen";
                case UtilityBuildError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                case UtilityBuildError.NoRoadAccess: return $"Keine Zufahrt an der Vorderseite (Pfeil) – zur Stadtstraße drehen, höchstens {UtilitySystem.RoadReach:0} m";
                default: return null;
            }
        }

        // ------------------------------------------------------------ IMGUI

        void OnGUI()
        {
            if (CityBuilder.Game.UI.Panels.WorldLabels.Enabled)
            {
                // Neue UI: Beschriftung über der Vorschau als UI-Toolkit-Label melden
                if (Event.current.type != EventType.Layout) return;
                CityBuilder.Game.UI.Panels.WorldLabels.Begin(this);
                if (Active && !string.IsNullOrEmpty(label))
                    CityBuilder.Game.UI.Panels.WorldLabels.Add(this, labelWorld, label, labelError ? CityBuilder.Game.UI.Panels.WorldLabelStyle.Error : CityBuilder.Game.UI.Panels.WorldLabelStyle.Normal,
                        -6f, above: true);
                return;
            }
            if (!Active || string.IsNullOrEmpty(label) || tool.Camera == null) return;
            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.box) { fontSize = 12, richText = true, alignment = TextAnchor.MiddleCenter };
            var sp = tool.Camera.WorldToScreenPoint(labelWorld);
            if (sp.z <= 0f) return;
            var content = new GUIContent(labelError ? $"<color=#ff7060>{label}</color>" : label);
            var size = labelStyle.CalcSize(content);
            GUI.Box(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y - 6f, size.x + 8f, size.y + 4f), content, labelStyle);
        }

        /// <summary>Zeilen der Bauleiste (aufgerufen von <see cref="RoadToolbar"/> in deren Layout-Bereich).</summary>
        public void DrawToolbarRows()
        {
            if (infoStyle == null) infoStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
            var overlay = GetComponent<UtilityOverlayView>();

            GUILayout.BeginHorizontal();
            foreach (var t in UtilityCatalog.All)
                if (Button(ShortName(t), Active && Current == UtilityAction.Facility && SelectedType == t, 0f)) SelectFacility(t);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (Button("Stromleitung", Active && Current == UtilityAction.PowerLine, 110f)) Select(UtilityAction.PowerLine);
            if (Button("Wasserrohr", Active && Current == UtilityAction.WaterPipe, 110f)) Select(UtilityAction.WaterPipe);
            if (Button("Abwasserkanal", Active && Current == UtilityAction.SewagePipe, 110f)) Select(UtilityAction.SewagePipe); // G3 erweitert
            if (Button("Abriss", Active && Current == UtilityAction.Remove, 80f)) Select(UtilityAction.Remove);
            if (GUILayout.Button("⟲ 15°", GUILayout.Width(55f), GUILayout.Height(24f))) Rotate(-15f);
            if (GUILayout.Button("⟳ 15°", GUILayout.Width(55f), GUILayout.Height(24f))) Rotate(15f);
            GUILayout.FlexibleSpace();
            if (overlay != null)
            {
                if (Button("Strom-Ansicht (O)", overlay.Current == UtilityOverlayView.Overlay.Power, 135f)) overlay.Toggle(UtilityOverlayView.Overlay.Power);
                if (Button("Wasser-Ansicht (O)", overlay.Current == UtilityOverlayView.Overlay.Water, 140f)) overlay.Toggle(UtilityOverlayView.Overlay.Water);
            }
            if (Button("Unterirdisch (X)", tool.UndergroundView, 120f)) tool.ToggleUndergroundView();
            GUILayout.EndHorizontal();

            var u = Utilities;
            string Balance(UtilityKind k)
            {
                float prod = u.TotalProduction(k), dem = u.TotalDemand(k);
                string c = dem > prod + 0.001f ? "#ff7060" : "#7dff8a";
                return $"<color={c}>{UtilityCatalog.KindName(k)} {prod:0}/{dem:0} {UtilityCatalog.Unit(k)}</color>";
            }
            GUILayout.Label($"{Balance(UtilityKind.Power)}  ·  {Balance(UtilityKind.Water)}  ·  Unterhalt {u.TotalUpkeepPerDay:0} €/Tag  ·  " +
                            "Stadtstraßen führen Strom + Wasser automatisch mit · freie Leitungen nur für Lücken/Überland " +
                            $"(Masten neben der Straße, ≤ {UtilitySystem.MaxPoleSpan:0} m Abstand, belegen Platz) · Rohre liegen unterirdisch", infoStyle);
        }

        static string ShortName(UtilityFacilityType t)
        {
            switch (t.Id)
            {
                case "power_coal": return "Kohle";
                case "power_gas": return "Gas";
                case "power_wind": return "Wind";
                case "power_solar": return "Solar";
                case "water_pump": return "Pumpe (Ufer)";
                case "water_tower": return "Wasserturm";
                case "power_nuclear": return "Atom";
                case "sewage_plant": return "Kläranlage";
                case "sewage_outlet": return "Auslass (Ufer)";
                default: return t.DisplayName;
            }
        }

        static bool Button(string text, bool active, float width)
        {
            var old = GUI.backgroundColor;
            if (active) GUI.backgroundColor = Selected;
            bool clicked = width > 0f ? GUILayout.Button(text, GUILayout.Width(width), GUILayout.Height(24f))
                                      : GUILayout.Button(text, GUILayout.Height(24f));
            GUI.backgroundColor = old;
            return clicked;
        }
    }
}
