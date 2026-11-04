using System.Collections.Generic;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Aviation;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Aviation
{
    /// <summary>
    /// Flughafen-Werkzeug (<b>Taste 9</b>, Modus <see cref="RoadTool.Mode.Airport"/>; 9 war frei – 1–3 Tempo, 4 Dienste, 5 ÖPNV,
    /// 6 Bauen, 7 Bahn, 8 Hafen):
    /// <list type="bullet">
    /// <item><b>Regionalflugplatz / Verkehrsflughafen</b>: Mauszeiger = Bahnmitte, <c>, .</c> drehen (5°, mit Shift 1°), Enter = Terminal
    ///   auf die andere Seite, „Wind“ richtet die Bahn nach der Hauptwindrichtung aus. Vorschau: Bahn, Gelände, Terminal, die beiden
    ///   Einflugschneisen als ansteigende Flächen (grün frei, gelb eingeschränkt, rot gesperrt), Lärmkorridor, Hindernisse als rote Säulen.</item>
    /// <item><b>Terminal ausbauen</b>: Flughafen anklicken.</item>
    /// <item><b>Abriss</b>: Flughafen anklicken.</item>
    /// </list>
    /// </summary>
    public sealed class AirportTool : MonoBehaviour
    {
        public enum AirportAction { Airfield, Airport, Expand, Remove }

        RoadTool tool;
        Material okMaterial, badMaterial, warnMaterial, noiseMaterial, siteMaterial;
        GameObject runwayGhost, terminalGhost;
        MeshRenderer runwayRenderer, terminalRenderer;
        LineRenderer siteLine, siteLine2;
        readonly LineRenderer[] approachLines = new LineRenderer[2];
        readonly LineRenderer[] noiseLines = new LineRenderer[2];
        readonly List<GameObject> markers = new List<GameObject>();
        string label;
        bool labelError;
        Vector3 labelWorld;
        float angle = float.NaN; // Grad, 0 = Ost (mathematisch)
        int side = 1;

        public AirportAction Current { get; private set; } = AirportAction.Airfield;
        public bool Active => tool != null && tool.CurrentMode == RoadTool.Mode.Airport;
        public string Label => label;
        public bool LabelIsError => labelError;
        /// <summary>Letzter Plan der Vorschau (für Fenster/HUD), null außerhalb des Werkzeugs.</summary>
        public AirportPlan LastPlan { get; private set; }

        AviationSystem Aviation => tool.Runner.World.Aviation;

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            okMaterial = new Material(transparent) { name = "AirportPreviewOk", color = new Color(0.3f, 0.95f, 0.5f, 0.5f) };
            badMaterial = new Material(transparent) { name = "AirportPreviewBad", color = new Color(1f, 0.25f, 0.2f, 0.55f) };
            warnMaterial = new Material(transparent) { name = "AirportPreviewWarn", color = new Color(1f, 0.82f, 0.2f, 0.55f) };
            noiseMaterial = new Material(transparent) { name = "AirportNoiseCorridor", color = new Color(0.85f, 0.3f, 0.9f, 0.16f) };
            siteMaterial = new Material(transparent) { name = "AirportSite", color = new Color(1f, 1f, 1f, 0.6f) };
        }

        void Start()
        {
            runwayGhost = Ghost("Runway Preview", out runwayRenderer);
            terminalGhost = Ghost("Terminal Preview", out terminalRenderer);
            siteLine = NewLine("Airport Site", 3f, siteMaterial, true);
            siteLine2 = NewLine("Airport Terminal Area", 3f, siteMaterial, true);
            for (int i = 0; i < 2; i++)
            {
                approachLines[i] = NewLine("Approach Surface " + i, 4f, okMaterial, true);
                noiseLines[i] = NewLine("Noise Corridor " + i, 1f, noiseMaterial, false);
            }
        }

        GameObject Ghost(string name, out MeshRenderer r)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(transform, false);
            r = g.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.SetActive(false);
            return g;
        }

        LineRenderer NewLine(string name, float width, Material m, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<LineRenderer>();
            l.useWorldSpace = true;
            l.widthMultiplier = width;
            l.loop = loop;
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.sharedMaterial = m;
            l.enabled = false;
            return l;
        }

        void OnDestroy()
        {
            foreach (var m in new[] { okMaterial, badMaterial, warnMaterial, noiseMaterial, siteMaterial }) if (m != null) Destroy(m);
        }

        // ------------------------------------------------------------ Steuerung

        public void Select(AirportAction action)
        {
            Current = action;
            if (!Active) tool.SetMode(RoadTool.Mode.Airport);
        }

        /// <summary>Bahn nach der Hauptwindrichtung ausrichten (Start und Landung gegen den Wind).</summary>
        public void AlignToWind()
        {
            var w = tool.Runner.World.Aviation.WindAlignedDirection;
            angle = Mathf.Atan2(w.Y, w.X) * Mathf.Rad2Deg;
        }

        public void FlipSide() => side = -side;
        public float AngleDegrees => float.IsNaN(angle) ? 0f : angle;

        void Update()
        {
            if (tool == null) return;
            var kb = GameKeyboard.Current;
            if (kb == null) return;
            if (kb.digit9Key.wasPressedThisFrame && !kb.shiftKey.isPressed) tool.ToggleMode(RoadTool.Mode.Airport); // erneut 9 = Auswählen
            if (!Active) return;
            if (float.IsNaN(angle) && tool.Runner?.World?.Aviation != null) AlignToWind();
            float step = kb.shiftKey.isPressed ? 1f : 5f;
            if (kb.commaKey.wasPressedThisFrame) angle -= step;
            if (kb.periodKey.wasPressedThisFrame) angle += step;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) FlipSide();
        }

        void LateUpdate()
        {
            label = null;
            LastPlan = null;
            if (tool == null || !Active || !tool.HasCursor || tool.Runner.World?.Aviation == null) { Hide(); return; }
            var mouse = Mouse.current;
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame && !CityBuilder.Game.UI.UiBlocker.IsPointerOverUi(mouse.position.ReadValue());
            var p = tool.Cursor;
            switch (Current)
            {
                case AirportAction.Airfield: UpdatePlace(AirportCatalog.Airfield, p, clicked); break;
                case AirportAction.Airport: UpdatePlace(AirportCatalog.Airport, p, clicked); break;
                default: UpdatePick(p, clicked); break;
            }
        }

        void Hide()
        {
            if (runwayGhost != null) runwayGhost.SetActive(false);
            if (terminalGhost != null) terminalGhost.SetActive(false);
            if (siteLine != null) siteLine.enabled = false;
            if (siteLine2 != null) siteLine2.enabled = false;
            for (int i = 0; i < 2; i++)
            {
                if (approachLines[i] != null) approachLines[i].enabled = false;
                if (noiseLines[i] != null) noiseLines[i].enabled = false;
            }
            foreach (var m in markers) if (m != null) m.SetActive(false);
        }

        float GroundAt(SimVector2 p) => tool.Runner.World.Terrain.HeightAt(p);

        // ------------------------------------------------------------ Platzieren

        void UpdatePlace(AirportType type, SimVector2 p, bool clicked)
        {
            var av = Aviation;
            float a = AngleDegrees * Mathf.Deg2Rad;
            var dir = new SimVector2(Mathf.Cos(a), Mathf.Sin(a));
            var plan = av.PlanAirport(type, p, dir, side);
            LastPlan = plan;
            var l = plan.Layout;
            float h = plan.Error == AirportBuildError.OutOfWorld || plan.Height == 0f ? GroundAt(p) : plan.Height;
            ShowPreview(l, h, plan.IsValid, plan.CorridorA, plan.CorridorB);
            ShowObstacles(plan.Obstacles, h);
            string wind = AviationRules.WindText(av.PrevailingWind);
            string info;
            if (plan.IsValid)
            {
                info = $"{type.DisplayName} · Bahn {plan.RunwayLength:0} m ({AviationRules.RunwayDesignator(dir)}) · {BuildLabelsMoney(plan.Cost)}" +
                       (plan.EarthworkCost > 1 ? $" + Erdarbeiten {BuildLabelsMoney(plan.EarthworkCost)} ({plan.EarthVolume / 1000f:0} Tsd. m³)" : "") +
                       $" · Wind aus {wind}" +
                       (plan.CorridorA != CorridorState.Clear || plan.CorridorB != CorridorState.Clear
                           ? $" · Einflugschneise {(plan.CorridorA == CorridorState.Closed || plan.CorridorB == CorridorState.Closed ? "gesperrt" : "eingeschränkt")}: {plan.Obstacles.Count} Hindernisse, Kapazität {plan.CapacityFactor * 100f:0} %"
                           : " · Einflugschneisen frei") +
                       (plan.Displaced > 0 ? $" · {plan.Displaced} Gebäude weichen" : "") +
                       (plan.HasRoadAccess ? " · Straße am Terminal" : " · Straße vor das Terminal bauen (sonst außer Betrieb)");
            }
            else info = AviationSystem.ErrorText(plan);
            info += "  ·  , . drehen · Enter Terminal-Seite";
            SetLabel(info, !plan.IsValid, SimConvert.ToWorld(l.Terminal.FrontMid, h + 30f));
            if (!clicked || !plan.IsValid) return;
            var built = av.BuildAirport(plan);
            if (built != null)
                CityBuilder.Game.UI.Hud.HudMessages.Info(built.Name, $"{built.Type.DisplayName} · Bahn {built.Designator}, {built.RunwayLength:0} m" +
                    (built.HasAccess ? " · in Betrieb" : " · Straße vor das Terminal bauen"), CityBuilder.Game.UI.Hud.HudIconKind.Plane, "airport-built");
        }

        static string BuildLabelsMoney(double v) => CityBuilder.Game.UI.Hud.BuildLabels.Money((float)v);

        void ShowPreview(in AirportLayout l, float h, bool valid, CorridorState ca, CorridorState cb)
        {
            var dir3 = new Vector3(l.Dir.X, 0f, l.Dir.Y);
            var rot = Quaternion.LookRotation(dir3);
            runwayGhost.SetActive(true);
            runwayGhost.transform.SetPositionAndRotation(SimConvert.ToWorld(l.Center, h + 0.6f), rot);
            runwayGhost.transform.localScale = new Vector3(l.Type.RunwayWidth, 1f, l.Length);
            runwayRenderer.sharedMaterial = valid ? okMaterial : badMaterial;
            var t = l.Terminal;
            terminalGhost.SetActive(true);
            terminalGhost.transform.SetPositionAndRotation(SimConvert.ToWorld(t.Center, h + 8f), Quaternion.LookRotation(new Vector3(t.Front.X, 0f, t.Front.Y)));
            terminalGhost.transform.localScale = new Vector3(t.Width, 16f, t.Depth);
            terminalRenderer.sharedMaterial = valid ? okMaterial : badMaterial;
            // Gelände
            var corners = l.RunwayArea.Corners();
            siteLine.positionCount = 4;
            for (int i = 0; i < 4; i++) siteLine.SetPosition(i, SimConvert.ToWorld(corners[i], h + 1f));
            siteLine.enabled = true;
            corners = l.TerminalArea.Corners();
            siteLine2.positionCount = 4;
            for (int i = 0; i < 4; i++) siteLine2.SetPosition(i, SimConvert.ToWorld(corners[i], h + 1f));
            siteLine2.enabled = true;
            // Einflugschneisen (ansteigende Fläche) + Lärmkorridor
            for (int e = 0; e < 2; e++)
            {
                var state = e == 0 ? ca : cb;
                var outward = e == 0 ? -l.Dir : l.Dir;
                var start = (e == 0 ? l.EndA : l.EndB) + outward * AviationRules.ApproachStart;
                float len = l.Type.ApproachLength, w0 = l.Type.ApproachInnerHalfWidth, w1 = w0 + AviationRules.Divergence * len;
                var a0 = start + l.Normal * w0; var a1 = start - l.Normal * w0;
                var b0 = start + outward * len + l.Normal * w1; var b1 = start + outward * len - l.Normal * w1;
                float hEnd = h + l.Type.ApproachSlope * len;
                var line = approachLines[e];
                line.positionCount = 4;
                line.SetPosition(0, SimConvert.ToWorld(a0, h + 1f));
                line.SetPosition(1, SimConvert.ToWorld(b0, hEnd));
                line.SetPosition(2, SimConvert.ToWorld(b1, hEnd));
                line.SetPosition(3, SimConvert.ToWorld(a1, h + 1f));
                line.sharedMaterial = state == CorridorState.Closed ? badMaterial : state == CorridorState.Limited ? warnMaterial : okMaterial;
                line.enabled = true;
                var noise = noiseLines[e];
                const int n = 12;
                noise.positionCount = n + 1;
                var curve = new AnimationCurve();
                for (int i = 0; i <= n; i++)
                {
                    float d = AviationRules.NoiseLength * i / n;
                    var p = (e == 0 ? l.EndA : l.EndB) + outward * d;
                    noise.SetPosition(i, SimConvert.ToWorld(p, h + 1.5f));
                    curve.AddKey(i / (float)n, AviationSystem.NoiseRadiusAt(d) * 2f);
                }
                noise.widthCurve = curve;
                noise.widthMultiplier = 1f;
                noise.enabled = true;
            }
        }

        void ShowObstacles(List<Obstacle> obstacles, float h)
        {
            int k = 0;
            foreach (var o in obstacles)
            {
                if (k >= 24) break;
                if (k >= markers.Count)
                {
                    var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Destroy(g.GetComponent<Collider>());
                    g.name = "Hindernis";
                    g.transform.SetParent(transform, false);
                    g.GetComponent<MeshRenderer>().sharedMaterial = badMaterial;
                    markers.Add(g);
                }
                var m = markers[k++];
                m.SetActive(true);
                float top = Mathf.Max(4f, o.Top);
                m.transform.position = SimConvert.ToWorld(o.Position, h + top * 0.5f);
                m.transform.localScale = new Vector3(8f, top * 0.5f, 8f);
                m.GetComponent<MeshRenderer>().sharedMaterial = o.Core ? badMaterial : warnMaterial;
            }
            for (int i = k; i < markers.Count; i++) markers[i].SetActive(false);
        }

        // ------------------------------------------------------------ Ausbau / Abriss

        void UpdatePick(SimVector2 p, bool clicked)
        {
            var av = Aviation;
            var a = av.AirportAt(p, 4f);
            foreach (var m in markers) if (m != null) m.SetActive(false);
            if (a == null)
            {
                Hide();
                SetLabel(Current == AirportAction.Expand ? "Flughafen anklicken: Terminal ausbauen" : "Flughafen anklicken: abreißen", false, SimConvert.ToWorld(p, GroundAt(p) + 6f));
                return;
            }
            ShowPreview(a.Layout, a.Height, Current == AirportAction.Expand, a.CorridorA, a.CorridorB);
            if (Current == AirportAction.Expand)
            {
                bool can = av.CanExpand(a);
                string text = a.Level >= a.Type.MaxLevel ? $"{a.Name}: Terminal voll ausgebaut (Stufe {a.Level})"
                    : $"{a.Name}: Terminal auf Stufe {a.Level + 1} ausbauen ({a.Type.Stands[a.Level]} Positionen, {a.Type.TerminalPaxPerHour[a.Level]:0} Fluggäste/h) · {BuildLabelsMoney(a.Type.ExpandCost)}" +
                      (can ? "" : " · zu wenig Geld");
                SetLabel(text, !can, SimConvert.ToWorld(a.Layout.Terminal.FrontMid, a.Height + 30f));
                if (clicked && can) av.ExpandTerminal(a.Id);
                return;
            }
            SetLabel($"{a.Name} abreißen (+{BuildLabelsMoney((a.Type.Cost + (a.Level - 1) * a.Type.ExpandCost) * Simulation.Economy.EconomyRules.DemolitionRefundShare)})", true,
                SimConvert.ToWorld(a.Layout.Terminal.FrontMid, a.Height + 30f));
            if (clicked) av.DemolishAirport(a.Id);
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
