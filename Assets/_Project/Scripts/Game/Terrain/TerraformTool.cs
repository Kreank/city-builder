using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Environment;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Terrain
{
    /// <summary>
    /// Terraforming-Werkzeug. Aktiv, solange das <see cref="RoadTool"/> im Modus <see cref="RoadTool.Mode.Terraform"/>
    /// ist (Taste L oder Leiste „Gelände“) – andere Werkzeuge schalten es also automatisch ab, wie beim Zonen-Werkzeug.
    /// Linke Maustaste gedrückt halten = Pinsel mit fester Rate anwenden (unabhängig von der Bildrate);
    /// rechte Maustaste kehrt Anheben/Absenken um. Einebnen nimmt die Zielhöhe beim Klick auf.
    /// Die Höhenänderung macht die Simulation (<see cref="TerrainMap.Terraform"/>), dieses Werkzeug liest nur.
    /// </summary>
    public sealed class TerraformTool : MonoBehaviour
    {
        /// <summary>Pinsel-Anwendungen pro Sekunde (Echtzeit).</summary>
        public const float ApplyRate = 20f;
        const int RingSegments = 72;

        static readonly float[] Strengths = { 1f, 3f, 8f };

        RoadTool tool;
        Material ringMaterial;
        Mesh ringMesh;
        MeshRenderer ringRenderer;

        float accumulator;
        bool painting;
        bool inverted;

        public TerraformMode Mode { get; private set; } = TerraformMode.Raise;
        public float Radius { get; private set; } = 24f;
        /// <summary>Höhenänderung in Metern pro Sekunde (in der Pinselmitte).</summary>
        public float Strength { get; private set; } = 3f;
        public float Softness { get; private set; } = 0.5f;
        /// <summary>Beim Klick aufgenommene Zielhöhe für Einebnen.</summary>
        public float TargetHeight { get; private set; }
        public bool HasTarget { get; private set; }
        /// <summary>Bewegte Erdmenge des aktuellen bzw. letzten Pinselstrichs (m³).</summary>
        public float StrokeVolume { get; private set; }
        /// <summary>Pinsel traf zuletzt auf gesperrtes Gelände (Straße/Gebäude).</summary>
        public bool HitLocked { get; private set; }
        /// <summary>Stadtkasse: letzte Anwendung abgelehnt (zu wenig Geld).</summary>
        public bool NoFunds { get; private set; }
        /// <summary>Kosten des aktuellen bzw. letzten Pinselstrichs (€).</summary>
        public double StrokeCost { get; private set; }

        public bool Active => tool != null && tool.CurrentMode == RoadTool.Mode.Terraform;

        public void Init(RoadTool roadTool, Material previewBase)
        {
            tool = roadTool;
            if (previewBase != null) ringMaterial = new Material(previewBase) { name = "TerraformBrush", color = Color.white };
        }

        public void Activate(TerraformMode mode)
        {
            Mode = mode;
            if (tool != null && !Active) tool.SetMode(RoadTool.Mode.Terraform);
        }

        public void SetRadius(float r) => Radius = Mathf.Clamp(r, 4f, 80f);
        public void SetStrength(float s) => Strength = Mathf.Clamp(s, 0.25f, 20f);
        public void SetSoftness(float s) => Softness = Mathf.Clamp01(s);

        void Start()
        {
            var go = new GameObject("Terraform Brush");
            go.transform.SetParent(transform, false);
            ringMesh = new Mesh { name = "Terraform Brush" };
            go.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            ringRenderer = go.AddComponent<MeshRenderer>();
            ringRenderer.sharedMaterial = ringMaterial;
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
            ringRenderer.enabled = false;
        }

        void OnDestroy()
        {
            if (ringMesh != null) Destroy(ringMesh);
            if (ringMaterial != null) Destroy(ringMaterial);
        }

        // LateUpdate: RoadTool hat in Update den Cursor (Schnitt mit dem Gelände, nicht über der UI) gesetzt.
        void LateUpdate()
        {
            if (tool == null || tool.Runner == null || tool.Runner.World == null) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (kb != null && kb.lKey.wasPressedThisFrame)
                tool.SetMode(Active ? RoadTool.Mode.Straight : RoadTool.Mode.Terraform);

            if (!Active || mouse == null)
            {
                painting = false;
                if (ringRenderer != null) ringRenderer.enabled = false;
                return;
            }

            if (kb != null) HandleKeys(kb);

            var terrain = tool.Runner.World.Terrain;
            bool hasCursor = tool.HasCursor;

            // Strich beginnt nur auf dem Gelände (nicht auf der Leiste)
            bool left = mouse.leftButton.wasPressedThisFrame, right = mouse.rightButton.wasPressedThisFrame;
            if ((left || right) && hasCursor)
            {
                painting = true;
                inverted = right;
                StrokeVolume = 0f;
                StrokeCost = 0;
                NoFunds = false;
                HitLocked = false;
                accumulator = 1f / ApplyRate; // sofort einmal anwenden
                if (Mode == TerraformMode.Level && left)
                {
                    TargetHeight = terrain.HeightAt(tool.Cursor);
                    HasTarget = true;
                }
            }
            if (painting && !(inverted ? mouse.rightButton.isPressed : mouse.leftButton.isPressed)) painting = false;

            if (painting && hasCursor)
            {
                accumulator += Time.unscaledDeltaTime;
                float interval = 1f / ApplyRate;
                int n = 0;
                while (accumulator >= interval && n < 4)
                {
                    Apply(terrain, tool.Cursor, interval);
                    accumulator -= interval;
                    n++;
                }
                if (n == 4) accumulator = 0f; // nicht nachholen, wenn der Rechner nicht hinterherkommt
            }
            else accumulator = 0f;

            if (hasCursor) DrawRing(terrain, tool.Cursor);
            else if (ringRenderer != null) ringRenderer.enabled = false;
        }

        void HandleKeys(Keyboard kb)
        {
            if (kb.leftBracketKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) SetRadius(Radius - 4f);
            if (kb.rightBracketKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) SetRadius(Radius + 4f);
        }

        void Apply(TerrainMap terrain, SimVector2 center, float dt)
        {
            var mode = Mode;
            if (inverted && mode == TerraformMode.Raise) mode = TerraformMode.Lower;
            else if (inverted && mode == TerraformMode.Lower) mode = TerraformMode.Raise;
            if (mode == TerraformMode.Level && !HasTarget) return;

            // Glätten braucht mehr Weg pro Schritt, damit es sich ähnlich schnell anfühlt
            float strength = Strength * dt * (mode == TerraformMode.Smooth ? 2f : 1f);
            var result = terrain.Terraform(new TerraformBrush(mode, center, Radius, strength, Softness, TargetHeight));
            StrokeVolume += result.Volume;
            StrokeCost += result.Cost;
            NoFunds = result.InsufficientFunds;
            if (result.LockedPoints > 0) HitLocked = true;
        }

        // ------------------------------------------------------------ Vorschau

        static Color ModeColor(TerraformMode m)
        {
            switch (m)
            {
                case TerraformMode.Raise: return new Color(0.45f, 0.95f, 0.45f, 0.95f);
                case TerraformMode.Lower: return new Color(1f, 0.6f, 0.3f, 0.95f);
                case TerraformMode.Smooth: return new Color(0.5f, 0.8f, 1f, 0.95f);
                default: return new Color(1f, 0.92f, 0.4f, 0.95f);
            }
        }

        /// <summary>Zwei Ringe, die dem Gelände folgen: außen = Radius, innen = Beginn der weichen Kante.</summary>
        void DrawRing(TerrainMap terrain, SimVector2 center)
        {
            if (ringRenderer == null) return;
            ringRenderer.enabled = true;

            var cam = tool.Camera;
            float camDist = cam != null ? Vector3.Distance(cam.transform.position, SimConvert.ToWorld(center)) : 200f;
            float width = Mathf.Max(0.25f, camDist * 0.004f);
            var color = ModeColor(Mode);
            var soft = color; soft.a = 0.45f;
            float inner = Radius * (1f - Softness);

            int ringCount = inner > 1f ? 2 : 1;
            var verts = new Vector3[RingSegments * 2 * ringCount];
            var colors = new Color[verts.Length];
            var tris = new int[RingSegments * 6 * ringCount];
            for (int ring = 0; ring < ringCount; ring++)
            {
                float r = ring == 0 ? Radius : inner;
                float w = ring == 0 ? width : width * 0.6f;
                var c = ring == 0 ? color : soft;
                int vb = ring * RingSegments * 2, tb = ring * RingSegments * 6;
                for (int i = 0; i < RingSegments; i++)
                {
                    float a = i / (float)RingSegments * Mathf.PI * 2f;
                    var dir = new SimVector2(Mathf.Cos(a), Mathf.Sin(a));
                    var pIn = center + dir * Mathf.Max(0f, r - w * 0.5f);
                    var pOut = center + dir * (r + w * 0.5f);
                    verts[vb + i * 2] = SimConvert.ToWorld(pIn, Surface(terrain, pIn));
                    verts[vb + i * 2 + 1] = SimConvert.ToWorld(pOut, Surface(terrain, pOut));
                    colors[vb + i * 2] = colors[vb + i * 2 + 1] = c;

                    int j = (i + 1) % RingSegments;
                    int t = tb + i * 6;
                    tris[t] = vb + i * 2; tris[t + 1] = vb + j * 2; tris[t + 2] = vb + i * 2 + 1;
                    tris[t + 3] = vb + i * 2 + 1; tris[t + 4] = vb + j * 2; tris[t + 5] = vb + j * 2 + 1;
                }
            }
            ringMesh.Clear();
            ringMesh.vertices = verts;
            ringMesh.colors = colors;
            ringMesh.triangles = tris;
            ringMesh.RecalculateBounds();
        }

        static float Surface(TerrainMap terrain, SimVector2 p) => Mathf.Max(terrain.HeightAt(p), terrain.WaterLevel) + 0.4f;

        // ------------------------------------------------------------ Leiste (von RoadToolbar aufgerufen)

        static readonly Color Selected = new Color(0.45f, 0.75f, 1f);

        /// <summary>Zeilen für die Bauleiste (IMGUI, gleicher Stil wie <c>RoadToolbar</c>).</summary>
        public void ToolbarRows(GUIStyle infoStyle)
        {
            GUILayout.BeginHorizontal();
            ModeButton(TerraformMode.Raise, "Anheben");
            ModeButton(TerraformMode.Lower, "Absenken");
            ModeButton(TerraformMode.Smooth, "Glätten");
            ModeButton(TerraformMode.Level, "Einebnen");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Weiche Kante {Softness * 100f:0} %", GUILayout.Width(120f));
            Softness = GUILayout.HorizontalSlider(Softness, 0f, 1f, GUILayout.Width(110f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Pinsel {Radius:0} m ( [ / ] )", GUILayout.Width(140f));
            foreach (var r in new[] { 8f, 16f, 32f, 64f })
                if (Button($"{r:0} m", Mathf.Approximately(Radius, r), 55f)) SetRadius(r);
            GUILayout.Space(20f);
            GUILayout.Label("Stärke", GUILayout.Width(50f));
            string[] names = { "sanft", "mittel", "stark" };
            for (int i = 0; i < Strengths.Length; i++)
                if (Button(names[i], Mathf.Approximately(Strength, Strengths[i]), 65f)) SetStrength(Strengths[i]);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            string info = Mode == TerraformMode.Level
                ? (HasTarget ? $"Zielhöhe {TargetHeight:0.0} m (Klick nimmt neue Höhe auf)" : "Klick nimmt die Zielhöhe auf")
                : "LMB halten = anwenden" + (Mode == TerraformMode.Raise || Mode == TerraformMode.Lower ? " · RMB = umgekehrt" : "");
            info += $"   ·   Erdmenge {StrokeVolume:0} m³ ({StrokeCost:0} €)";
            if (NoFunds) info += "   ·   <color=#ff7060>zu wenig Geld in der Stadtkasse</color>";
            if (HitLocked) info += "   ·   <color=#ffb060>unter Straßen/Gebäuden gesperrt</color>";
            info += "   ·   unter 0 m entsteht Wasser";
            GUILayout.Label(info, infoStyle);
        }

        void ModeButton(TerraformMode m, string label)
        {
            if (Button(label, Active && Mode == m, 0f)) Activate(m);
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
