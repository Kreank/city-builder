using System.Collections.Generic;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Gateways;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Gateways
{
    /// <summary>
    /// Außenanbindungen (Begleiter am RoadTool): Schilder an den Gateways am Kartenrand („A 7 · Richtung Valmora“) als
    /// Welt-Beschriftung, Bahntrasse als dezente gestrichelte Linie mit Hinweis „Bahn folgt“, Wasserstraßen beschriftet,
    /// „Hier anschließen“ an den Zubringer-Enden und eine Meldung, solange die Stadt nicht angeschlossen ist.
    /// Liest nur die Simulation.
    /// </summary>
    public sealed class GatewayView : MonoBehaviour
    {
        /// <summary>Schilder stehen so weit innerhalb des Kartenrands (sonst wären sie am Bildrand kaum zu sehen).</summary>
        const float SignInset = 90f;
        const float DashLength = 14f, GapLength = 10f, DashWidth = 2.2f;
        static readonly Color RailColor = new Color(0.92f, 0.86f, 0.72f, 0.55f);

        RoadTool tool;
        Material baseMaterial, railMaterial;
        GameObject railObject;
        Mesh railMesh;
        SimulationWorld builtFor;
        string lastHint;
        float hintShownAt = -1000f;
        readonly List<(Vector3 world, string text, WorldLabelStyle style, bool above)> labels = new List<(Vector3, string, WorldLabelStyle, bool)>();
        GUIStyle fallbackStyle;

        public void Init(RoadTool roadTool, Material material)
        {
            tool = roadTool;
            baseMaterial = material;
            if (tool.Runner != null) tool.Runner.WorldReplaced += () => { builtFor = null; lastHint = null; };
        }

        void OnDestroy()
        {
            if (railMaterial != null) Destroy(railMaterial);
            if (railMesh != null) Destroy(railMesh);
        }

        SimulationWorld World => tool != null && tool.Runner != null ? tool.Runner.World : null;

        void LateUpdate()
        {
            var world = World;
            labels.Clear();
            if (world?.Gateways == null) return;
            if (builtFor != world) BuildRail(world);
            var gw = world.Gateways;

            foreach (var g in gw.All)
            {
                if (!g.HasLocation) continue;
                var p = g.Position - g.Direction * SignInset;
                float h = world.Terrain.HeightAt(p);
                if (g.IsRoad)
                {
                    var seg = world.Roads.GetNode(g.EntryNodeId);
                    if (seg != null) h = Mathf.Max(h, seg.Height);
                }
                string text = SignText(g);
                labels.Add((SimConvert.ToWorld(p, h + 14f), text, WorldLabelStyle.Normal, true));
            }
            // --- Bahn: liegt die Hauptstrecke, zeichnet RailView die Gleise – gestrichelte Trasse aus, Hinweis „Bahnhof setzen“
            bool tracks = world.Rail != null && world.Rail.HasMainline;
            if (railObject != null && railObject.activeSelf == tracks) railObject.SetActive(!tracks && gw.HasRailCorridor);
            if (gw.HasRailCorridor)
            {
                // Hinweis nicht genau auf der Autobahn: etwas Richtung Ende verschoben
                var at = SimVector2.Lerp(gw.RailStart, gw.RailEnd, 0.3f);
                string railText = !tracks ? "Bahntrasse · Züge folgen (freigehalten)"
                    : world.Rail.HasPassengerService ? null : "Fernbahn · Bahnhof an die Gleise setzen (Bahn, 7)";
                if (railText != null)
                    labels.Add((SimConvert.ToWorld(at, world.Terrain.HeightAt(at) + 9f), railText, WorldLabelStyle.Normal, true));
            }
            // --- Ende Bahn

            // „Hier anschließen“ an den Zubringer-Enden, solange etwas fehlt
            string hint = gw.ConnectionHint;
            if (hint != null)
                foreach (var end in gw.FeederEnds)
                    labels.Add((SimConvert.ToWorld(end, world.Terrain.HeightAt(end) + 8f), "Zubringer · hier anschließen", WorldLabelStyle.Error, true));

            // Meldung bei Änderung bzw. alle 90 s (Echtzeit), solange nicht angeschlossen
            if (hint != lastHint || (hint != null && Time.unscaledTime - hintShownAt > 90f))
            {
                if (hint != null)
                {
                    HudMessages.Warning("Außenanbindung fehlt", hint, HudIconKind.Highway, "gateway-connect");
                    hintShownAt = Time.unscaledTime;
                }
                else if (lastHint != null)
                    HudMessages.Info("Stadt angeschlossen", "Neue Einwohner kommen über die Autobahn.", HudIconKind.Highway, "gateway-connect");
                lastHint = hint;
            }

            if (WorldLabels.Enabled)
            {
                WorldLabels.Begin(this);
                foreach (var (w, text, style, above) in labels) WorldLabels.Add(this, w, text, style, 0f, above);
            }
        }

        /// <summary>Schildtext: „A 7 · Richtung Valmora“, Bahn/Wasser mit Status.</summary>
        public static string SignText(Gateway g)
        {
            switch (g.Kind)
            {
                case GatewayKind.Highway:
                case GatewayKind.Road:
                    return $"{g.Route} · Richtung {g.TargetName}";
                case GatewayKind.Rail:
                    return $"Bahn · Richtung {g.TargetName}" + (g.IsOperational ? "" : g.RailBuilt ? " (Bahnhof fehlt)" : " (folgt)"); // Bahn
                case GatewayKind.Water:
                    return $"{g.Name} ({(g.Navigable ? "schiffbar, Hafen folgt" : "nicht schiffbar")})";
                default:
                    return g.Name;
            }
        }

        // ------------------------------------------------------------ Bahntrasse (gestrichelt)

        void BuildRail(SimulationWorld world)
        {
            builtFor = world;
            if (railMesh == null) railMesh = new Mesh { name = "Rail Corridor" };
            railMesh.Clear();
            var gw = world.Gateways;
            if (railObject == null)
            {
                railObject = new GameObject("Rail Corridor (Gateways)");
                railObject.transform.SetParent(transform, false);
                railObject.AddComponent<MeshFilter>().sharedMesh = railMesh;
                var r = railObject.AddComponent<MeshRenderer>();
                railMaterial = baseMaterial != null ? new Material(baseMaterial) { color = RailColor, name = "RailCorridor" } : null;
                r.sharedMaterial = railMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            railObject.SetActive(gw.HasRailCorridor);
            if (!gw.HasRailCorridor) return;

            var a = gw.RailStart;
            var b = gw.RailEnd;
            float length = SimVector2.Distance(a, b);
            var dir = SimVector2.Normalize(b - a);
            var side = new SimVector2(-dir.Y, dir.X);
            var verts = new List<Vector3>();
            var tris = new List<int>();
            // zwei Striche (Gleisachsen), versetzt um ±3,5 m
            foreach (float offset in new[] { -3.5f, 3.5f })
                for (float s = 0f; s < length; s += DashLength + GapLength)
                {
                    float e = Mathf.Min(length, s + DashLength);
                    for (float u = s; u < e - 0.01f; u += 4f)
                    {
                        float v = Mathf.Min(e, u + 4f);
                        var p0 = a + dir * u + side * offset;
                        var p1 = a + dir * v + side * offset;
                        int i = verts.Count;
                        foreach (var (p, w) in new[] { (p0, -1f), (p0, 1f), (p1, -1f), (p1, 1f) })
                        {
                            var q = p + side * (w * DashWidth * 0.5f);
                            float h = world.Terrain.HeightAt(q);
                            if (world.Terrain.IsWater(q)) h = world.Terrain.WaterLevel;
                            verts.Add(SimConvert.ToWorld(q, h + 0.35f));
                        }
                        tris.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3 });
                    }
                }
            railMesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            railMesh.SetVertices(verts);
            railMesh.SetTriangles(tris, 0);
            railMesh.RecalculateNormals();
            railMesh.RecalculateBounds();
        }

        // ------------------------------------------------------------ IMGUI-Rückfall

        void OnGUI()
        {
            if (WorldLabels.Enabled || tool == null || tool.Camera == null || labels.Count == 0) return;
            if (fallbackStyle == null) fallbackStyle = new GUIStyle(GUI.skin.box) { fontSize = 12 };
            foreach (var (w, text, _, _) in labels)
            {
                var sp = tool.Camera.WorldToScreenPoint(w);
                if (sp.z <= 0f) continue;
                var size = fallbackStyle.CalcSize(new GUIContent(text));
                GUI.Box(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y, size.x, size.y), text, fallbackStyle);
            }
        }
    }
}
