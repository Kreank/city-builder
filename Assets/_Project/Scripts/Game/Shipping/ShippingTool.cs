using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Shipping;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Shipping
{
    /// <summary>
    /// Hafen-Werkzeug (<b>Taste 8</b>, Modus <see cref="RoadTool.Mode.Harbor"/>; 8 war frei – 1–3 Tempo, 4 Dienste, 5 ÖPNV,
    /// 6 Bauen, 7 Bahn):
    /// <list type="bullet">
    /// <item><b>Güterhafen</b>: rastet am Ufer ein (Kai an tiefem Wasser, Landseite zur Straße).</item>
    /// <item><b>Klappbrücke</b>: Start- und Endpunkt an den Ufern anklicken – gerade Stadtstraße quer über die Fahrrinne,
    ///   Mittelstück klappt für Schiffe auf.</item>
    /// <item><b>Abriss</b>: Hafen oder Klappe anklicken.</item>
    /// </list>
    /// Die Fahrrinne ist sichtbar (ShippingView), solange gebaut wird (auch im Straßen-/Bahn-Werkzeug).
    /// </summary>
    public sealed class ShippingTool : MonoBehaviour
    {
        public enum HarborAction { Harbor, Drawbridge, Remove }

        RoadTool tool;
        Material okMaterial, badMaterial, spanMaterial;
        LineRenderer line, spanLine;
        GameObject ghost;
        MeshRenderer ghostRenderer;
        bool hasStart;
        SimVector2 start;
        string label;
        bool labelError;
        Vector3 labelWorld;

        public HarborAction Current { get; private set; } = HarborAction.Harbor;
        public bool Active => tool != null && tool.CurrentMode == RoadTool.Mode.Harbor;
        public string Label => label;
        public bool LabelIsError => labelError;

        ShippingSystem Shipping => tool.Runner.World.Shipping;

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            if (tool.Runner != null) tool.Runner.WorldReplaced += Cancel;
            tool.RegisterOperation(() => Active && hasStart, Cancel, Cancel); // Esc/Rechtsklick bricht die Klappbrücke ab
            okMaterial = new Material(transparent) { name = "HarborPreviewOk", color = new Color(0.3f, 0.95f, 0.5f, 0.55f) };
            badMaterial = new Material(transparent) { name = "HarborPreviewBad", color = new Color(1f, 0.25f, 0.2f, 0.6f) };
            spanMaterial = new Material(transparent) { name = "HarborPreviewSpan", color = new Color(1f, 0.85f, 0.2f, 0.75f) };
        }

        void Start()
        {
            ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghost.name = "Harbor Preview";
            Destroy(ghost.GetComponent<Collider>());
            ghost.transform.SetParent(transform, false);
            ghostRenderer = ghost.GetComponent<MeshRenderer>();
            ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghost.SetActive(false);
            line = NewLine("Drawbridge Preview", 8f);
            spanLine = NewLine("Drawbridge Span Preview", 10f);
        }

        LineRenderer NewLine(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<LineRenderer>();
            l.useWorldSpace = true;
            l.widthMultiplier = width;
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.sharedMaterial = okMaterial;
            l.enabled = false;
            return l;
        }

        void OnDestroy()
        {
            foreach (var m in new[] { okMaterial, badMaterial, spanMaterial }) if (m != null) Destroy(m);
        }

        // ------------------------------------------------------------ Steuerung

        public void Select(HarborAction action)
        {
            Cancel();
            Current = action;
            if (!Active) tool.SetMode(RoadTool.Mode.Harbor);
        }

        public void Cancel() { hasStart = false; }

        void Update()
        {
            if (tool == null) return;
            var kb = GameKeyboard.Current;
            if (kb == null) return;
            if (kb.digit8Key.wasPressedThisFrame && !kb.shiftKey.isPressed) tool.ToggleMode(RoadTool.Mode.Harbor); // erneut 8 = Auswählen
            if (!Active) Cancel();
        }

        void LateUpdate()
        {
            label = null;
            if (tool == null || !Active || !tool.HasCursor || tool.Runner.World?.Shipping == null) { Hide(); return; }
            var mouse = Mouse.current;
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            var p = tool.Cursor;
            if (!Shipping.Active)
            {
                Hide();
                SetLabel("Diese Karte hat keine schiffbaren Gewässer (oder keine Außenanbindung)", true, SimConvert.ToWorld(p, GroundAt(p) + 6f));
                return;
            }
            switch (Current)
            {
                case HarborAction.Harbor: UpdateHarbor(p, clicked); line.enabled = spanLine.enabled = false; break;
                case HarborAction.Drawbridge: ghost.SetActive(false); UpdateDrawbridge(p, clicked); break;
                default: UpdateRemove(p, clicked); break;
            }
        }

        void Hide()
        {
            if (ghost != null) ghost.SetActive(false);
            if (line != null) line.enabled = false;
            if (spanLine != null) spanLine.enabled = false;
        }

        float GroundAt(SimVector2 p) => tool.Runner.World.Terrain.HeightAt(p);

        // ------------------------------------------------------------ Hafen

        void UpdateHarbor(SimVector2 p, bool clicked)
        {
            var sh = Shipping;
            var type = HarborCatalog.Cargo;
            var plan = sh.PlanHarbor(type, p);
            ghost.SetActive(true);
            float h = plan.IsValid ? plan.Height : GroundAt(plan.Position);
            ghost.transform.SetPositionAndRotation(SimConvert.ToWorld(plan.Position, h + 4f), Quaternion.LookRotation(new Vector3(plan.Front.X, 0f, plan.Front.Y)));
            ghost.transform.localScale = new Vector3(type.Width, 8f, type.Depth);
            ghostRenderer.sharedMaterial = plan.IsValid ? okMaterial : badMaterial;
            string info = plan.IsValid
                ? $"{type.DisplayName} · {plan.ShipClass.DisplayName} (Tiefe vor dem Kai {plan.BerthDepth:0.0} m) · {plan.Cost:0} € (+{type.UpkeepPerWeek:0} €/Woche)" +
                  (plan.HasRoadAccess ? " · Straße an der Landseite" : " · noch keine Straße an der Landseite (außer Betrieb)")
                : ShippingSystem.ErrorText(plan);
            SetLabel(info, !plan.IsValid, SimConvert.ToWorld(plan.Position, h + 14f));
            if (!clicked || !plan.IsValid) return;
            var built = sh.BuildHarbor(plan);
            if (built != null)
                CityBuilder.Game.UI.Hud.HudMessages.Info(built.Name, $"{built.Type.DisplayName} · {built.ShipClass.DisplayName}" +
                    (built.InService ? " · in Betrieb" : built.HasAccess ? " · " + (built.BlockedReason ?? "keine Fahrrinne") : " · Straße an die Landseite bauen"),
                    CityBuilder.Game.UI.Hud.HudIconKind.Ship, "harbor-built");
        }

        // ------------------------------------------------------------ Klappbrücke

        RoadType DrawbridgeType => tool.SelectedType != null && tool.SelectedType.Category == RoadCategory.City ? tool.SelectedType : RoadCatalog.CityTwoLane;

        void UpdateDrawbridge(SimVector2 p, bool clicked)
        {
            var sh = Shipping;
            var anchor = RoadBuilder.ResolveAnchor(tool.Runner.World.Roads, p, 8f);
            var cur = anchor.Position;
            if (!hasStart)
            {
                line.enabled = spanLine.enabled = false;
                SetLabel("Klappbrücke: Startpunkt am Ufer setzen (gerade quer über die Fahrrinne)", false, SimConvert.ToWorld(cur, GroundAt(cur) + 6f));
                if (clicked) { start = cur; hasStart = true; }
                return;
            }
            var plan = sh.PlanDrawbridge(DrawbridgeType, start, cur);
            float deck = plan.Deck > 0f ? plan.Deck : GroundAt(cur);
            ShowLine(line, plan.Start, plan.End, t => Mathf.Lerp(GroundAt(plan.Start), GroundAt(plan.End), t) + 1f, plan.IsValid ? okMaterial : badMaterial);
            if (plan.SpanStart != plan.SpanEnd) ShowLine(spanLine, plan.SpanStart, plan.SpanEnd, _ => deck + 0.6f, spanMaterial);
            else spanLine.enabled = false;
            string info = plan.IsValid
                ? $"Klappbrücke · {DrawbridgeType.DisplayName} · Klappe {SimVector2.Distance(plan.SpanStart, plan.SpanEnd):0} m, {deck - tool.Runner.World.Terrain.WaterLevel:0.0} m über Wasser · " +
                  $"{plan.Cost:0} € (Mechanik {ShippingRules.DrawbridgeCost:0} €, +{ShippingRules.DrawbridgeUpkeep:0} €/Woche) · öffnet für Schiffe, Verkehr wartet an der Schranke"
                : RoadTool.ErrorText(plan.Error) ?? plan.Error.ToString();
            SetLabel(info, !plan.IsValid, SimConvert.ToWorld(cur, GroundAt(cur) + 8f));
            if (!clicked || !plan.IsValid) return;
            var span = sh.BuildDrawbridge(plan);
            if (span != null)
            {
                CityBuilder.Game.UI.Hud.HudMessages.Info("Klappbrücke gebaut", "Öffnet für Schiffe – die Schranken halten den Verkehr an, Ampel rot", CityBuilder.Game.UI.Hud.HudIconKind.Ship, "harbor-drawbridge");
                hasStart = false;
            }
        }

        void ShowLine(LineRenderer l, SimVector2 a, SimVector2 b, System.Func<float, float> height, Material m)
        {
            const int n = 24;
            var pts = new Vector3[n + 1];
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                pts[i] = SimConvert.ToWorld(SimVector2.Lerp(a, b, t), height(t));
            }
            l.positionCount = pts.Length;
            l.SetPositions(pts);
            l.sharedMaterial = m;
            l.enabled = true;
        }

        // ------------------------------------------------------------ Abriss

        void UpdateRemove(SimVector2 p, bool clicked)
        {
            var sh = Shipping;
            line.enabled = spanLine.enabled = false;
            var h = sh.HarborAt(p, 2f);
            if (h != null)
            {
                ghost.SetActive(true);
                ghost.transform.SetPositionAndRotation(SimConvert.ToWorld(h.Position, h.Height + 5f), Quaternion.LookRotation(new Vector3(h.Front.X, 0f, h.Front.Y)));
                ghost.transform.localScale = new Vector3(h.Type.Width + 1f, 10f, h.Type.Depth + 1f);
                ghostRenderer.sharedMaterial = badMaterial;
                SetLabel($"{h.Name} abreißen (+{h.Type.Cost * Simulation.Economy.EconomyRules.DemolitionRefundShare:0} €)", true, SimConvert.ToWorld(h.Position, h.Height + 16f));
                if (clicked) sh.DemolishHarbor(h.Id);
                return;
            }
            ghost.SetActive(false);
            var d = sh.DrawbridgeNear(p, 30f);
            if (d != null)
            {
                SetLabel("Klappe der Klappbrücke abreißen (Zufahrten bleiben)", true, SimConvert.ToWorld(d.Position, d.Height + 8f));
                if (clicked) sh.DemolishDrawbridge(d.SegmentId);
                return;
            }
            SetLabel("Hafen oder Klappbrücke anklicken", false, SimConvert.ToWorld(p, GroundAt(p) + 6f));
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
