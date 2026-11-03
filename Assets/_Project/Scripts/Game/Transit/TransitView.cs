using System.Collections.Generic;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Transit;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Transit
{
    /// <summary>
    /// Zeichnet den ÖPNV: Haltestellen am Fahrbahnrand (Modell „bus_stop“ aus dem <see cref="CityBuilder.Game.Assets.OwnModelCatalog"/>,
    /// sonst Platzhalter Mast + Schild + Wartehäuschen), Depots (Modell „bus_depot“, sonst Halle + Hof), Linienverläufe in
    /// Linienfarbe (im ÖPNV-Werkzeug bzw. mit <see cref="ShowLines"/>) und eine Farbplatte auf dem Dach jedes Linienbusses.
    /// Die Busse selbst zeichnet die <see cref="Traffic.VehicleView"/> (Modelle aus dem Asset-Katalog, „Buses“). Liest nur.
    /// </summary>
    public sealed class TransitView : MonoBehaviour
    {
        /// <summary>Linienfarben (Index = <see cref="TransitLine.ColorIndex"/>).</summary>
        public static readonly Color[] LineColors =
        {
            new Color(0.90f, 0.20f, 0.20f), new Color(0.15f, 0.45f, 0.90f), new Color(0.20f, 0.70f, 0.30f), new Color(0.95f, 0.65f, 0.10f),
            new Color(0.60f, 0.30f, 0.80f), new Color(0.10f, 0.70f, 0.75f), new Color(0.90f, 0.40f, 0.65f), new Color(0.55f, 0.40f, 0.25f),
            new Color(0.45f, 0.75f, 0.10f), new Color(0.30f, 0.30f, 0.35f),
        };

        public static Color LineColor(TransitLine l) => l == null ? Color.white : LineColors[(l.ColorIndex % LineColors.Length + LineColors.Length) % LineColors.Length];

        /// <summary>Linienverläufe auch außerhalb des ÖPNV-Werkzeugs zeigen (Schalter im Linienfenster).</summary>
        public static bool ShowLines;
        /// <summary>Hervorgehobene Linie (Linienfenster), 0 = keine.</summary>
        public static int HighlightLine;

        SimulationRunner runner;
        Material baseMaterial, lineBase;
        Mesh cube, cylinder;
        Transform root;
        readonly Dictionary<int, GameObject> stopObjects = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> depotObjects = new Dictionary<int, GameObject>();
        readonly Dictionary<int, LineRenderer> lineRenderers = new Dictionary<int, LineRenderer>();
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        readonly Material[] roofMaterials = new Material[LineColors.Length];
        readonly List<Matrix4x4>[] roofs = new List<Matrix4x4>[LineColors.Length];
        readonly Matrix4x4[] buffer = new Matrix4x4[1023];
        int builtVersion = -1, builtRoads = -1;

        public void Init(SimulationRunner r, Material lit, Material transparent)
        {
            runner = r;
            baseMaterial = lit;
            lineBase = transparent;
            r.WorldReplaced += () =>
            {
                // Speichern & Laden: Objekte gehören zu Ids der alten Welt
                foreach (var o in stopObjects.Values) if (o != null) Destroy(o);
                foreach (var o in depotObjects.Values) if (o != null) Destroy(o);
                foreach (var l in lineRenderers.Values) if (l != null) Destroy(l.gameObject);
                stopObjects.Clear();
                depotObjects.Clear();
                lineRenderers.Clear();
                builtVersion = builtRoads = -1;
            };
        }

        void Start()
        {
            cube = PrimitiveMesh(PrimitiveType.Cube);
            cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
            root = new GameObject("Transit").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < LineColors.Length; i++)
            {
                roofs[i] = new List<Matrix4x4>();
                if (baseMaterial == null) continue;
                var m = new Material(baseMaterial) { name = "BusRoof" + i, enableInstancing = true };
                SetColor(m, LineColors[i]);
                roofMaterials[i] = m;
            }
        }

        static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var tmp = GameObject.CreatePrimitive(type);
            var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            return mesh;
        }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
        }

        void OnDestroy()
        {
            foreach (var m in materials.Values) if (m != null) Destroy(m);
            foreach (var m in roofMaterials) if (m != null) Destroy(m);
            foreach (var lr in lineRenderers.Values) if (lr != null && lr.sharedMaterial != null) Destroy(lr.sharedMaterial);
        }

        void LateUpdate()
        {
            if (runner == null || runner.World?.Transit == null || cube == null || baseMaterial == null) return;
            var world = runner.World;
            var t = world.Transit;
            if (t.Version != builtVersion || world.Roads.Version != builtRoads)
            {
                builtVersion = t.Version;
                builtRoads = world.Roads.Version;
                t.EnsureRoutes();
                SyncStops(world, t);
                SyncDepots(world, t);
                SyncLines(world, t);
            }
            var tool = GetComponent<TransitTool>();
            bool show = ShowLines || HighlightLine != 0 || (tool != null && tool.Active);
            foreach (var kv in lineRenderers)
            {
                if (kv.Value == null) continue;
                bool on = show && (HighlightLine == 0 || HighlightLine == kv.Key || ShowLines || (tool != null && tool.Active));
                kv.Value.enabled = on;
                kv.Value.widthMultiplier = HighlightLine == kv.Key ? 3.5f : 2f;
            }
            DrawRoofs(world, t);
        }

        // ------------------------------------------------------------ Haltestellen

        void SyncStops(SimulationWorld world, TransitSystem t)
        {
            foreach (var go in stopObjects.Values) if (go != null) Destroy(go);
            stopObjects.Clear();
            foreach (var s in t.Stops)
            {
                var seg = world.Roads.GetSegment(s.SegmentId);
                if (seg == null) continue;
                var parent = new GameObject($"Haltestelle {s.Name} #{s.Id}").transform;
                parent.SetParent(root, false);
                if (s.ServesAlong) BuildStopSide(world, t, s, seg, true, parent);
                if (s.ServesAgainst) BuildStopSide(world, t, s, seg, false, parent);
                stopObjects[s.Id] = parent.gameObject;
            }
        }

        void BuildStopSide(SimulationWorld world, TransitSystem t, BusStop s, Simulation.Roads.RoadSegment seg, bool along, Transform parent)
        {
            var right = seg.Right(s.T) * (along ? 1f : -1f);
            // Am Gehweg: hinter der Fahrbahnkante (Rand der Straße minus 1 m)
            var pos = seg.Evaluate(s.T) + right * Mathf.Max(seg.Type.HalfWidth - 1.2f, seg.Type.CarriagewayHalfWidth + 0.8f);
            var dir = seg.Tangent(s.T) * (along ? 1f : -1f);
            var side = new GameObject(along ? "Seite +" : "Seite −").transform;
            side.SetParent(parent, false);
            side.SetPositionAndRotation(SimConvert.ToWorld(pos, seg.HeightAt(s.T) + RoadMeshBuilder.RoadHeight),
                Quaternion.LookRotation(new Vector3(-right.X, 0f, -right.Y))); // Vorderseite zur Fahrbahn
            if (TryModel("bus_stop", side, 4f, 2f)) return;
            var post = new Color(0.25f, 0.27f, 0.3f);
            var glass = new Color(0.65f, 0.8f, 0.9f);
            var sign = new Color(0.95f, 0.8f, 0.1f);
            Box(side, new Vector3(0f, 0.05f, 0f), new Vector3(4f, 0.1f, 1.8f), new Color(0.6f, 0.6f, 0.58f));
            Box(side, new Vector3(0f, 1.2f, 0.75f), new Vector3(3.2f, 2.2f, 0.08f), glass);  // Rückwand
            Box(side, new Vector3(0f, 2.35f, 0.2f), new Vector3(3.4f, 0.12f, 1.4f), post);   // Dach
            Box(side, new Vector3(-1.6f, 1.2f, 0.75f), new Vector3(0.1f, 2.4f, 0.1f), post);
            Box(side, new Vector3(1.6f, 1.2f, 0.75f), new Vector3(0.1f, 2.4f, 0.1f), post);
            Box(side, new Vector3(2.2f, 1.4f, -0.4f), new Vector3(0.08f, 2.8f, 0.08f), post); // Mast
            Box(side, new Vector3(2.2f, 2.8f, -0.4f), new Vector3(0.6f, 0.6f, 0.06f), sign);  // H-Schild
        }

        // ------------------------------------------------------------ Depots

        void SyncDepots(SimulationWorld world, TransitSystem t)
        {
            var alive = new HashSet<int>();
            foreach (var d in t.Depots)
            {
                alive.Add(d.Id);
                if (depotObjects.ContainsKey(d.Id)) continue;
                var tr = new GameObject($"{d.Type.DisplayName} #{d.Id}").transform;
                tr.SetParent(root, false);
                tr.SetPositionAndRotation(SimConvert.ToWorld(d.Position, d.Height), Quaternion.LookRotation(new Vector3(d.Front.X, 0f, d.Front.Y)));
                if (!TryModel("bus_depot", tr, d.Type.Width, d.Type.Depth))
                {
                    float w = d.Type.Width, dd = d.Type.Depth;
                    Box(tr, new Vector3(0f, 0.12f, 0f), new Vector3(w, 0.24f, dd), new Color(0.45f, 0.45f, 0.47f));     // Hof
                    Box(tr, new Vector3(0f, 4f, -dd * 0.18f), new Vector3(w * 0.9f, 8f, dd * 0.55f), new Color(0.78f, 0.78f, 0.74f)); // Halle
                    Box(tr, new Vector3(0f, 8.2f, -dd * 0.18f), new Vector3(w * 0.92f, 0.4f, dd * 0.57f), new Color(0.2f, 0.35f, 0.6f));
                    for (int i = 0; i < 4; i++) // Tore zur Straße
                        Box(tr, new Vector3(-w * 0.33f + i * w * 0.22f, 2.5f, dd * 0.1f + 0.05f), new Vector3(w * 0.16f, 5f, 0.1f), new Color(0.3f, 0.3f, 0.32f));
                    Box(tr, new Vector3(w * 0.42f, 3.5f, dd * 0.42f), new Vector3(0.3f, 7f, 0.3f), new Color(0.25f, 0.27f, 0.3f));
                    Box(tr, new Vector3(w * 0.42f, 6.8f, dd * 0.42f), new Vector3(1.6f, 1.6f, 0.1f), new Color(0.95f, 0.8f, 0.1f));
                }
                depotObjects[d.Id] = tr.gameObject;
            }
            var dead = new List<int>();
            foreach (var kv in depotObjects) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead) { if (depotObjects[id] != null) Destroy(depotObjects[id]); depotObjects.Remove(id); }
        }

        // ------------------------------------------------------------ Linien

        void SyncLines(SimulationWorld world, TransitSystem t)
        {
            var alive = new HashSet<int>();
            int index = 0;
            foreach (var l in t.Lines)
            {
                alive.Add(l.Id);
                if (!lineRenderers.TryGetValue(l.Id, out var lr) || lr == null)
                {
                    var go = new GameObject($"Linie {l.Id}");
                    go.transform.SetParent(root, false);
                    lr = go.AddComponent<LineRenderer>();
                    lr.useWorldSpace = true;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    lr.numCornerVertices = 2;
                    lr.sharedMaterial = new Material(lineBase != null ? lineBase : baseMaterial) { name = "TransitLine" + l.Id };
                    lineRenderers[l.Id] = lr;
                }
                var c = LineColor(l);
                c.a = 0.85f;
                SetColor(lr.sharedMaterial, c);
                var pts = RoutePoints(world, l.VisitStops, l.Legs, 0.6f + 0.15f * (index++ % 4));
                lr.positionCount = pts.Count;
                lr.SetPositions(pts.ToArray());
            }
            var dead = new List<int>();
            foreach (var kv in lineRenderers) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead)
            {
                var lr = lineRenderers[id];
                if (lr != null) { if (lr.sharedMaterial != null) Destroy(lr.sharedMaterial); Destroy(lr.gameObject); }
                lineRenderers.Remove(id);
            }
        }

        /// <summary>
        /// Punkte eines Linienverlaufs (Unity-Welt) entlang der Abschnitte: je Kante Abtastung der Fahrbahn, versetzt
        /// in die rechte Fahrspur, auf der ersten/letzten Kante nur zwischen den Haltestellen.
        /// </summary>
        public static List<Vector3> RoutePoints(SimulationWorld world, IReadOnlyList<int> visits, IReadOnlyList<List<EdgeKey>> legs, float lift)
        {
            var pts = new List<Vector3>();
            var t = world.Transit;
            if (legs == null) return pts;
            for (int k = 0; k < legs.Count; k++)
            {
                var leg = legs[k];
                if (leg == null || leg.Count == 0 || visits == null || visits.Count == 0) continue;
                var a = t.GetStop(visits[k % visits.Count]);
                var b = t.GetStop(visits[(k + 1) % visits.Count]);
                for (int i = 0; i < leg.Count; i++)
                {
                    var seg = world.Roads.GetSegment(leg[i].SegmentId);
                    if (seg == null) continue;
                    bool along = leg[i].Along;
                    float from = 0f, to = 1f; // in Fahrtrichtung 0..1
                    if (i == 0 && a != null && a.SegmentId == seg.Id) from = along ? a.T : 1f - a.T;
                    if (i == leg.Count - 1 && b != null && b.SegmentId == seg.Id) to = along ? b.T : 1f - b.T;
                    if (to < from) to = from;
                    int n = Mathf.Max(2, Mathf.CeilToInt(seg.Length * (to - from) / 8f) + 1);
                    float off = seg.Type.CarriagewayHalfWidth > 0f ? seg.Type.CarriagewayHalfWidth * 0.5f : 1.75f;
                    for (int j = 0; j < n; j++)
                    {
                        float u = Mathf.Lerp(from, to, j / (float)(n - 1));
                        float st = along ? u : 1f - u;
                        var p = seg.Evaluate(st) + seg.Right(st) * (along ? off : -off);
                        pts.Add(SimConvert.ToWorld(p, seg.HeightAt(st) + lift));
                    }
                }
            }
            return pts;
        }

        // ------------------------------------------------------------ Busse (Farbplatte)

        void DrawRoofs(SimulationWorld world, TransitSystem t)
        {
            foreach (var l in roofs) l.Clear();
            float alpha = runner.TickAlpha;
            foreach (var bus in t.Buses)
            {
                var v = bus.Vehicle;
                if (v == null || (v.Lane == null && v.Connection == null)) continue;
                var line = t.GetLine(bus.LineId);
                int ci = line != null ? (line.ColorIndex % LineColors.Length + LineColors.Length) % LineColors.Length : LineColors.Length - 1;
                var p = SimVector2.Lerp(v.PrevPosition, v.Position, alpha);
                var d = SimVector2.Lerp(v.PrevDirection, v.Direction, alpha);
                float h = Mathf.Lerp(v.PrevHeight, v.Height, alpha);
                if (d.LengthSquared() < 1e-6f) d = new SimVector2(0f, 1f);
                var rot = Quaternion.LookRotation(new Vector3(d.X, 0f, d.Y));
                roofs[ci].Add(Matrix4x4.TRS(SimConvert.ToWorld(p, h + RoadMeshBuilder.RoadHeight + 3.35f), rot, new Vector3(2.3f, 0.12f, v.Length * 0.85f)));
            }
            for (int c = 0; c < roofs.Length; c++)
            {
                var list = roofs[c];
                if (list.Count == 0 || roofMaterials[c] == null) continue;
                var rp = new RenderParams(roofMaterials[c]) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = true };
                for (int start = 0; start < list.Count; start += buffer.Length)
                {
                    int n = Mathf.Min(buffer.Length, list.Count - start);
                    list.CopyTo(start, buffer, 0, n);
                    Graphics.RenderMeshInstanced(rp, cube, 0, buffer, n);
                }
            }
        }

        // ------------------------------------------------------------ Modelle / Grundkörper

        /// <summary>Eigenes Blender-Modell einsetzen (Paletten-Material, grobe Einheitenfehler korrigieren – wie ServiceView).</summary>
        bool TryModel(string id, Transform parent, float width, float depth)
        {
            var catalog = CityBuilder.Game.Assets.OwnModelCatalog.Instance;
            var prefab = catalog != null ? catalog.TransportModel(id) : null;
            if (prefab == null) return false;
            var go = Instantiate(prefab, parent, false);
            go.name = prefab.name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (catalog.Palette != null)
                foreach (var r in renderers)
                {
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = catalog.Palette;
                    r.sharedMaterials = mats;
                }
            bool any = false;
            var b = new Bounds();
            foreach (var r in renderers)
            {
                var rb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = rb.center + Vector3.Scale(rb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = parent.InverseTransformPoint(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (any)
            {
                float fit = Mathf.Min(width / Mathf.Max(0.01f, b.size.x), depth / Mathf.Max(0.01f, b.size.z));
                if (fit < 0.2f || fit > 5f) go.transform.localScale = Vector3.one * fit;
            }
            return true;
        }

        void Box(Transform parent, Vector3 pos, Vector3 size, Color c)
        {
            var go = new GameObject("Box");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat(c);
        }

        Material Mat(Color c)
        {
            if (materials.TryGetValue(c, out var m)) return m;
            m = new Material(baseMaterial) { name = "Transit_" + ColorUtility.ToHtmlStringRGB(c) };
            SetColor(m, c);
            materials[c] = m;
            return m;
        }
    }
}
