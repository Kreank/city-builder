using System.Collections.Generic;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Shipping;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Shipping
{
    /// <summary>
    /// Zeichnet die Wasserstraße: Fahrrinne als Overlay (beim Bauen von Straßen, Gleisen, Häfen – Brücken rot = zu niedrig,
    /// gelb = Klappbrücke, grün = hoch genug), Tonnen am Rand der Rinne (Modelle „Buoy_Red/Green“), Häfen (Kai-Kacheln „Quay“,
    /// Kräne „HarborCrane“/„ContainerCrane“, Speicher „HarborWarehouse“, sonst Platzhalter), Klappbrücken (Modell
    /// „Infrastructure/Drawbridge“ mit Klappen „LeafA/LeafB“, Ampel rot bei gesperrter Straße; sonst Platzhalter) und Schiffe
    /// (Modelle „CargoShip“, „Ferry“, „Sailboat“, Binnenschiff als Platzhalter-Rumpf), interpoliert zwischen zwei Verkehrsschritten.
    /// Liest nur.
    /// </summary>
    public sealed class ShippingView : MonoBehaviour
    {
        /// <summary>Aktive Darstellung (Inspektor: Schiff anklicken/verfolgen).</summary>
        public static ShippingView Active { get; private set; }
        /// <summary>Fahrrinne immer zeigen (Fenster F8 / Bauleiste), sonst nur beim Bauen.</summary>
        public static bool ShowFairway;

        SimulationRunner runner;
        Material baseMaterial, overlayMaterial;
        Mesh cube;
        RoadTool tool;
        Transform root, overlayRoot, buoyRoot, harborRoot, bridgeRoot, shipRoot;
        readonly Dictionary<int, GameObject> harborObjects = new Dictionary<int, GameObject>();
        readonly Dictionary<int, ShipVisual> shipObjects = new Dictionary<int, ShipVisual>();
        readonly List<BridgeVisual> bridgeObjects = new List<BridgeVisual>();
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        readonly Dictionary<Color, Material> overlayMaterials = new Dictionary<Color, Material>();
        int builtWaterways = -1, builtVersion = -1;

        sealed class ShipVisual
        {
            public GameObject Root;
            public float Length;
            public Vector3 Center;
        }

        sealed class BridgeVisual
        {
            public GameObject Root;
            public int SegmentId;
            public SimVector2 Position;
            public readonly List<(Transform t, Vector3 pos, Quaternion rot, float sign)> Leaves = new List<(Transform, Vector3, Quaternion, float)>();
            public readonly List<GameObject> RedLights = new List<GameObject>();
            public float Lift;
        }

        public void Init(SimulationRunner r, Material lit, Material transparent)
        {
            runner = r;
            baseMaterial = lit;
            overlayMaterial = transparent;
            Active = this;
            tool = GetComponent<RoadTool>();
            r.WorldReplaced += () =>
            {
                foreach (var o in harborObjects.Values) if (o != null) Destroy(o);
                foreach (var v in shipObjects.Values) if (v.Root != null) Destroy(v.Root);
                foreach (var b in bridgeObjects) if (b.Root != null) Destroy(b.Root);
                harborObjects.Clear(); shipObjects.Clear(); bridgeObjects.Clear();
                ClearChildren(overlayRoot);
                ClearChildren(buoyRoot);
                builtWaterways = builtVersion = -1;
            };
        }

        void Start()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            root = new GameObject("Wasserstraße").transform;
            root.SetParent(transform, false);
            overlayRoot = Child("Fahrrinne"); buoyRoot = Child("Tonnen"); harborRoot = Child("Häfen"); bridgeRoot = Child("Klappbrücken"); shipRoot = Child("Schiffe");
        }

        Transform Child(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        static void ClearChildren(Transform t)
        {
            if (t == null) return;
            for (int i = t.childCount - 1; i >= 0; i--) Destroy(t.GetChild(i).gameObject);
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            foreach (var m in materials.Values) if (m != null) Destroy(m);
            foreach (var m in overlayMaterials.Values) if (m != null) Destroy(m);
        }

        void LateUpdate()
        {
            if (runner == null || runner.World?.Shipping == null || cube == null || baseMaterial == null) return;
            var world = runner.World;
            var sh = world.Shipping;
            if (!sh.Active) { overlayRoot.gameObject.SetActive(false); return; }
            if (sh.WaterwayVersion != builtWaterways) { builtWaterways = sh.WaterwayVersion; BuildBuoys(world, sh); }
            if (sh.LayoutVersion != builtVersion)
            {
                builtVersion = sh.LayoutVersion;
                BuildOverlay(world, sh);
                SyncHarbors(world, sh);
                SyncBridges(world, sh);
            }
            overlayRoot.gameObject.SetActive(OverlayWanted);
            AnimateBridges(sh);
            DrawShips(world, sh);
        }

        bool OverlayWanted
        {
            get
            {
                if (ShowFairway) return true;
                if (tool == null) return false;
                switch (tool.CurrentMode)
                {
                    case RoadTool.Mode.Straight:
                    case RoadTool.Mode.Curve:
                    case RoadTool.Mode.Upgrade:
                    case RoadTool.Mode.Roundabout:
                    case RoadTool.Mode.Template:
                    case RoadTool.Mode.Rail:
                    case RoadTool.Mode.Harbor:
                    case RoadTool.Mode.Terraform:
                        return true;
                    default: return false;
                }
            }
        }

        // ------------------------------------------------------------ Fahrrinne + Tonnen

        void BuildOverlay(SimulationWorld world, ShippingSystem sh)
        {
            ClearChildren(overlayRoot);
            float y = world.Terrain.WaterLevel + 0.35f;
            foreach (var w in sh.Waterways)
            {
                var go = new GameObject(w.ToString());
                go.transform.SetParent(overlayRoot, false);
                var l = go.AddComponent<LineRenderer>();
                l.useWorldSpace = true;
                l.widthMultiplier = w.Kind == WaterwayKind.Through ? 7f : 5f;
                l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                l.sharedMaterial = OverlayMat(w.Kind == WaterwayKind.Through ? new Color(0.25f, 0.8f, 1f, 0.45f) : new Color(0.4f, 0.95f, 0.85f, 0.45f));
                var pts = new List<Vector3>();
                foreach (var p in w.Path) pts.Add(SimConvert.ToWorld(p, y));
                l.positionCount = pts.Count;
                l.SetPositions(pts.ToArray());
                // Brücken-Querungen: rot = zu niedrig für die Rinne, gelb = Klappbrücke, grün = hoch genug
                foreach (var x in w.Crossings)
                {
                    var c = x.IsDrawbridge ? new Color(1f, 0.85f, 0.2f, 0.8f) : x.Clearance < w.DesignClearance ? new Color(1f, 0.2f, 0.15f, 0.85f) : new Color(0.3f, 1f, 0.45f, 0.7f);
                    var m = new GameObject("Querung");
                    m.transform.SetParent(go.transform, false);
                    w.Sample(x.S, out var p, out var d);
                    m.transform.SetPositionAndRotation(SimConvert.ToWorld(p, y + 0.2f), Quaternion.LookRotation(new Vector3(d.X, 0f, d.Y)));
                    m.transform.localScale = new Vector3(w.ChannelHalfWidth(x.S) * 2f, 0.3f, 6f);
                    m.AddComponent<MeshFilter>().sharedMesh = cube;
                    m.AddComponent<MeshRenderer>().sharedMaterial = OverlayMat(c);
                }
            }
        }

        void BuildBuoys(SimulationWorld world, ShippingSystem sh)
        {
            ClearChildren(buoyRoot);
            float y = world.Terrain.WaterLevel;
            var placed = new List<SimVector2>();
            foreach (var w in sh.Waterways)
            {
                for (float s = 60f; s < w.Length - 40f; s += 160f)
                {
                    w.Sample(s, out var p, out var d);
                    var n = new SimVector2(-d.Y, d.X);
                    float half = w.ChannelHalfWidth(s);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        var q = p + n * (half * side);
                        if (!world.Contains(q) || !world.Terrain.IsWater(q)) continue;
                        bool near = false;
                        foreach (var o in placed) if (SimVector2.Distance(o, q) < 40f) { near = true; break; }
                        if (near) continue;
                        placed.Add(q);
                        var t = new GameObject(side > 0 ? "Tonne rot" : "Tonne grün").transform;
                        t.SetParent(buoyRoot, false);
                        t.position = SimConvert.ToWorld(q, y);
                        if (!TryModel(side > 0 ? "buoy_red" : "buoy_green", t, 2f, 2f))
                        {
                            var col = side > 0 ? new Color(0.85f, 0.12f, 0.1f) : new Color(0.1f, 0.6f, 0.2f);
                            Box(t, new Vector3(0f, 0.8f, 0f), new Vector3(1.2f, 2.2f, 1.2f), Mat(col), Quaternion.identity);
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------ Häfen

        void SyncHarbors(SimulationWorld world, ShippingSystem sh)
        {
            var alive = new HashSet<int>();
            foreach (var h in sh.Harbors)
            {
                alive.Add(h.Id);
                if (harborObjects.ContainsKey(h.Id)) continue;
                harborObjects[h.Id] = BuildHarbor(world, h);
            }
            var dead = new List<int>();
            foreach (var kv in harborObjects) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead) { if (harborObjects[id] != null) Destroy(harborObjects[id]); harborObjects.Remove(id); }
        }

        GameObject BuildHarbor(SimulationWorld world, Harbor h)
        {
            var tr = new GameObject(h.Name).transform;
            tr.SetParent(harborRoot, false);
            var water = -h.Front; // Coast-Modelle: Wasserseite = +Z
            tr.SetPositionAndRotation(SimConvert.ToWorld(h.Position, h.Height), Quaternion.LookRotation(new Vector3(water.X, 0f, water.Y)));
            float w = h.Type.Width, d = h.Type.Depth;
            // Kaifläche (Pflaster) + Kaimauer
            Box(tr, new Vector3(0f, -0.45f, 0f), new Vector3(w, 1f, d), Mat(new Color(0.62f, 0.61f, 0.58f)), Quaternion.identity);
            int tiles = Mathf.Max(1, Mathf.RoundToInt(w / 16f));
            bool quayModel = false;
            for (int i = 0; i < tiles; i++)
            {
                var t = new GameObject("Kai").transform;
                t.SetParent(tr, false);
                t.localPosition = new Vector3((i + 0.5f) * w / tiles - w * 0.5f, -ShippingRules.QuayHeight + 1.0f, d * 0.5f - 4f);
                quayModel |= TryModel("harbor_quay", t, 16f, 8f);
            }
            if (!quayModel)
                Box(tr, new Vector3(0f, -3f, d * 0.5f - 0.5f), new Vector3(w, 6f, 1f), Mat(new Color(0.5f, 0.5f, 0.48f)), Quaternion.identity);
            // Kräne an den Liegeplätzen
            bool coaster = h.ShipClass == ShipCatalog.Coaster;
            for (int b = 0; b < h.Type.Berths; b++)
            {
                float along = ((b + 0.5f) / h.Type.Berths - 0.5f) * w;
                var t = new GameObject("Kran").transform;
                t.SetParent(tr, false);
                t.localPosition = new Vector3(along, 0f, d * 0.5f - (coaster ? 10f : 6f));
                if (!TryModel(coaster ? "harbor_container_crane" : "harbor_crane", t, coaster ? 18f : 10f, coaster ? 30f : 12f))
                {
                    var red = Mat(new Color(0.8f, 0.2f, 0.12f));
                    Box(t, new Vector3(0f, 9f, 0f), new Vector3(1.2f, 18f, 1.2f), red, Quaternion.identity);
                    Box(t, new Vector3(0f, 17.5f, 6f), new Vector3(1f, 1f, 18f), red, Quaternion.identity);
                }
            }
            // Speicher an der Landseite + Containerstapel
            for (int k = -1; k <= 1; k += 2)
            {
                var t = new GameObject("Speicher").transform;
                t.SetParent(tr, false);
                t.localPosition = new Vector3(k * w * 0.27f, 0f, -d * 0.5f + 10f);
                if (!TryModel("harbor_warehouse", t, 24f, 12f))
                    Box(t, new Vector3(0f, 7f, 0f), new Vector3(24f, 14f, 12f), Mat(new Color(0.62f, 0.3f, 0.22f)), Quaternion.identity);
            }
            var cols = new[] { new Color(0.75f, 0.2f, 0.15f), new Color(0.2f, 0.4f, 0.75f), new Color(0.9f, 0.6f, 0.15f), new Color(0.2f, 0.55f, 0.3f) };
            for (int i = 0; i < 6; i++)
                for (int lvl = 0; lvl < 1 + (i * 7 + h.Id) % 3; lvl++)
                    Box(tr, new Vector3(-w * 0.15f + (i % 3) * 7f, 1.3f + lvl * 2.6f, (i / 3) * 3f + 2f), new Vector3(6.06f, 2.55f, 2.44f), Mat(cols[(i + lvl + h.Id) % cols.Length]), Quaternion.identity);
            return tr.gameObject;
        }

        // ------------------------------------------------------------ Klappbrücken

        const float ModelSpan = 40f, ModelDeck = 3f;

        void SyncBridges(SimulationWorld world, ShippingSystem sh)
        {
            var old = new List<BridgeVisual>(bridgeObjects);
            bridgeObjects.Clear();
            foreach (var d in sh.Drawbridges)
            {
                BridgeVisual keep = null;
                foreach (var o in old) if (o.Root != null && o.SegmentId == d.SegmentId && SimVector2.Distance(o.Position, d.Position) < 1f) { keep = o; break; }
                if (keep != null) { old.Remove(keep); bridgeObjects.Add(keep); continue; }
                var seg = world.Roads.GetSegment(d.SegmentId);
                if (seg == null) continue;
                bridgeObjects.Add(BuildBridge(world, d, seg));
            }
            foreach (var o in old) if (o.Root != null) Destroy(o.Root);
        }

        BridgeVisual BuildBridge(SimulationWorld world, Drawbridge d, Simulation.Roads.RoadSegment seg)
        {
            var v = new BridgeVisual { SegmentId = d.SegmentId, Position = d.Position };
            var tan = seg.Tangent(d.RoadT);
            var axis = new Vector3(tan.X, 0f, tan.Y).normalized;
            var tr = new GameObject("Klappbrücke").transform;
            tr.SetParent(bridgeRoot, false);
            // Mitte der Klappe (Mitte des Stücks) – die Klappe liegt mittig über der Fahrrinne
            var mid = seg.Evaluate(0.5f);
            tr.SetPositionAndRotation(SimConvert.ToWorld(mid, d.Height - ModelDeck), Quaternion.LookRotation(axis));
            v.Root = tr.gameObject;
            float roadW = seg.Type.Width;
            float span = seg.Length;
            var deckMat = Mat(new Color(0.36f, 0.36f, 0.38f));
            bool model = TryModel("drawbridge", tr, 14f, ModelSpan, out var go);
            if (model)
            {
                // Modell liegt entlang lokal Z? Sonst um 90° drehen (Straßenachse = längste Seite)
                var b = LocalBounds(tr, go);
                if (b.size.x > b.size.z) go.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                foreach (var name in new[] { "LeafA", "LeafB" })
                {
                    var leaf = FindDeep(go.transform, name);
                    if (leaf != null) v.Leaves.Add((leaf, leaf.localPosition, leaf.localRotation, LiftSign(leaf, tr.right)));
                }
            }
            else
            {
                // Platzhalter: zwei Pfeiler, zwei Klappen (Drehpunkt am Pfeiler)
                var pier = Mat(new Color(0.45f, 0.45f, 0.43f));
                for (int k = -1; k <= 1; k += 2)
                {
                    Box(tr, new Vector3(0f, ModelDeck - 4f, k * 16f), new Vector3(roadW + 2f, 8f, 8f), pier, Quaternion.identity);
                    var pivot = new GameObject(k < 0 ? "LeafA" : "LeafB").transform;
                    pivot.SetParent(tr, false);
                    pivot.localPosition = new Vector3(0f, ModelDeck, k * 12f);
                    Box(pivot, new Vector3(0f, -0.3f, -k * 6f), new Vector3(roadW - 0.4f, 0.6f, 11.9f), deckMat, Quaternion.identity);
                    v.Leaves.Add((pivot, pivot.localPosition, pivot.localRotation, LiftSign(pivot, tr.right)));
                }
            }
            // feste Fahrbahnstücke zwischen Modell und Enden der Klappe
            float rest = (span - ModelSpan) * 0.5f;
            if (rest > 0.5f)
                for (int k = -1; k <= 1; k += 2)
                    Box(tr, new Vector3(0f, ModelDeck - 0.35f, k * (ModelSpan * 0.5f + rest * 0.5f)), new Vector3(roadW, 0.7f, rest + 0.2f), deckMat, Quaternion.identity);
            // Ampeln an beiden Enden (rot, solange die Straße gesperrt ist)
            var red = Glow(new Color(1f, 0.12f, 0.08f));
            var post = Mat(new Color(0.2f, 0.2f, 0.22f));
            for (int k = -1; k <= 1; k += 2)
            {
                float z = k * (span * 0.5f + 2f);
                Box(tr, new Vector3(roadW * 0.5f + 0.6f, ModelDeck + 1.6f, z), new Vector3(0.2f, 3.2f, 0.2f), post, Quaternion.identity);
                var lamp = Box(tr, new Vector3(roadW * 0.5f + 0.6f, ModelDeck + 3.1f, z), new Vector3(0.5f, 0.5f, 0.5f), red, Quaternion.identity);
                v.RedLights.Add(lamp);
                var bar = Box(tr, new Vector3(0f, ModelDeck + 1.0f, z - k * 1.2f), new Vector3(roadW, 0.15f, 0.15f), Mat(new Color(0.9f, 0.1f, 0.1f)), Quaternion.identity);
                v.RedLights.Add(bar);
            }
            return v;
        }

        static Bounds LocalBounds(Transform parent, GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = new Bounds();
            bool any = false;
            foreach (var r in rs)
            {
                var c = parent.InverseTransformPoint(r.bounds.center);
                var e = parent.InverseTransformVector(r.bounds.extents);
                var rb = new Bounds(c, new Vector3(Mathf.Abs(e.x), Mathf.Abs(e.y), Mathf.Abs(e.z)) * 2f);
                if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
            }
            return b;
        }

        /// <summary>Drehsinn um die Querachse, der die Klappe anhebt (Spitze nach oben).</summary>
        static float LiftSign(Transform pivot, Vector3 axis)
        {
            var rs = pivot.GetComponentsInChildren<Renderer>(true);
            Vector3 c = pivot.position;
            if (rs.Length > 0) { var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds); c = b.center; }
            var arm = c - pivot.position;
            arm.y = 0f;
            if (arm.sqrMagnitude < 1e-4f) return 1f;
            return (Quaternion.AngleAxis(10f, axis) * arm).y >= 0f ? 1f : -1f;
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name.StartsWith(name)) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        void AnimateBridges(ShippingSystem sh)
        {
            if (bridgeObjects.Count == 0) return;
            foreach (var v in bridgeObjects)
            {
                var d = sh.DrawbridgeOf(v.SegmentId);
                if (d == null || v.Root == null) continue;
                // Klappe folgt dem Zustand (Lift 0..1 aus der Simulation), weich nachgeführt
                v.Lift = Mathf.MoveTowards(v.Lift, d.Lift, Time.deltaTime * Mathf.Max(0.5f, runner.AnimationTimeScale) / ShippingRules.DrawbridgeRaise);
                foreach (var (t, pos, rot, sign) in v.Leaves)
                {
                    if (t == null) continue;
                    t.localPosition = pos;
                    t.localRotation = rot;
                    t.RotateAround(t.position, v.Root.transform.right, sign * 78f * v.Lift);
                }
                foreach (var g in v.RedLights) if (g != null && g.activeSelf != d.RoadClosed) g.SetActive(d.RoadClosed);
            }
        }

        // ------------------------------------------------------------ Schiffe

        void DrawShips(SimulationWorld world, ShippingSystem sh)
        {
            float alpha = runner.TickAlpha;
            float y = world.Terrain.WaterLevel;
            float half = world.Settings.SizeMeters * 0.5f + 60f;
            var alive = new HashSet<int>();
            foreach (var s in sh.Ships)
            {
                if (s.State == ShipState.Waiting) continue;
                alive.Add(s.Id);
                if (!shipObjects.TryGetValue(s.Id, out var v)) shipObjects[s.Id] = v = BuildShip(s);
                float bow = Mathf.Lerp(s.PrevS, s.S, alpha);
                sh.Sample(s, bow, out var fp, out var fdir);
                sh.Sample(s, bow - s.Class.Length, out var bp, out _);
                var front = SimConvert.ToWorld(fp, y);
                var back = SimConvert.ToWorld(bp, y);
                var center = (front + back) * 0.5f;
                var dir = front - back;
                if (s.State == ShipState.Moored)
                {
                    // am Kai parallel zur Kaikante
                    var h = sh.GetHarbor(s.HarborId);
                    if (h != null)
                    {
                        var berth = h.Berth(Mathf.Max(0, s.Berth), s.Class);
                        center = SimConvert.ToWorld(berth, y);
                        var side = new Vector3(h.Side.X, 0f, h.Side.Y);
                        dir = Vector3.Dot(side, new Vector3(fdir.X, 0f, fdir.Y)) >= 0f ? side : -side;
                    }
                }
                bool inside = Mathf.Abs(center.x) < half && Mathf.Abs(center.z) < half;
                if (v.Root.activeSelf != inside) v.Root.SetActive(inside);
                if (dir.sqrMagnitude < 1e-4f) continue;
                v.Root.transform.SetPositionAndRotation(center, Quaternion.LookRotation(dir.normalized));
                v.Center = center + Vector3.up * 4f;
            }
            var dead = new List<int>();
            foreach (var kv in shipObjects) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead) { if (shipObjects[id].Root != null) Destroy(shipObjects[id].Root); shipObjects.Remove(id); }
        }

        ShipVisual BuildShip(Ship s)
        {
            var c = s.Class;
            var v = new ShipVisual { Root = new GameObject($"{c.DisplayName} #{s.Id}"), Length = c.Length };
            v.Root.transform.SetParent(shipRoot, false);
            var t = v.Root.transform;
            if (c.ModelId != null && TryModel(c.ModelId, t, c.Beam, c.Length)) return v;
            // Platzhalter: Rumpf, Lukendeckel, Steuerhaus am Heck (Binnenschiff)
            var hull = Mat(new Color(0.16f, 0.2f, 0.28f));
            Box(t, new Vector3(0f, 0.4f, 0f), new Vector3(c.Beam, 2.4f, c.Length * 0.92f), hull, Quaternion.identity);
            Box(t, new Vector3(0f, 0.4f, c.Length * 0.47f), new Vector3(c.Beam * 0.6f, 2.4f, c.Length * 0.06f), hull, Quaternion.identity);
            var hatch = Mat(new Color(0.55f, 0.32f, 0.12f));
            int n = Mathf.Max(1, Mathf.RoundToInt(c.Length / 14f) - 1);
            for (int i = 0; i < n; i++)
                Box(t, new Vector3(0f, 1.9f, -c.Length * 0.32f + c.Length * 0.68f * (i + 0.5f) / n + 3f), new Vector3(c.Beam * 0.85f, 0.6f, c.Length * 0.68f / n - 1f), hatch, Quaternion.identity);
            var white = Mat(new Color(0.92f, 0.92f, 0.9f));
            Box(t, new Vector3(0f, 3.0f, -c.Length * 0.42f), new Vector3(c.Beam * 0.8f, 2.6f, 5f), white, Quaternion.identity);
            Box(t, new Vector3(0f, 4.9f, -c.Length * 0.42f), new Vector3(c.Beam * 0.5f, 1.4f, 3f), Mat(new Color(0.25f, 0.35f, 0.45f)), Quaternion.identity);
            return v;
        }

        /// <summary>Schiff unter dem Strahl, 0 = keins.</summary>
        public int PickShip(Ray ray, float maxDistance = 4000f)
        {
            int best = 0;
            float bestD = maxDistance;
            foreach (var kv in shipObjects)
            {
                var v = kv.Value;
                if (v.Root == null || !v.Root.activeInHierarchy) continue;
                var to = v.Center - ray.origin;
                float along = Vector3.Dot(to, ray.direction);
                if (along < 0f || along > bestD) continue;
                if ((to - ray.direction * along).magnitude > Mathf.Max(5f, v.Length * 0.45f)) continue;
                bestD = along;
                best = kv.Key;
            }
            return best;
        }

        public bool TryGetShipPosition(int shipId, out Vector3 position)
        {
            position = default;
            if (!shipObjects.TryGetValue(shipId, out var v) || v.Root == null) return false;
            position = v.Center;
            return true;
        }

        // ------------------------------------------------------------ Modelle / Grundkörper

        bool TryModel(string id, Transform parent, float width, float depth) => TryModel(id, parent, width, depth, out _);

        /// <summary>Eigenes Blender-Modell einsetzen (Paletten-Material, grobe Einheitenfehler korrigieren – wie RailView).</summary>
        bool TryModel(string id, Transform parent, float width, float depth, out GameObject go)
        {
            go = null;
            var catalog = CityBuilder.Game.Assets.OwnModelCatalog.Instance;
            var prefab = catalog != null ? catalog.TransportModel(id) : null;
            if (prefab == null) return false;
            go = Instantiate(prefab, parent, false);
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
            var b = LocalBounds(parent, go);
            float fit = Mathf.Min(width / Mathf.Max(0.01f, Mathf.Max(b.size.x, 0.01f)), depth / Mathf.Max(0.01f, b.size.z));
            if (fit < 0.2f || fit > 5f) go.transform.localScale = Vector3.one * fit;
            return true;
        }

        GameObject Box(Transform parent, Vector3 localPos, Vector3 size, Material m, Quaternion rot)
        {
            var go = new GameObject("Box");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        Material Mat(Color c)
        {
            if (materials.TryGetValue(c, out var m)) return m;
            m = new Material(baseMaterial) { name = "Ship_" + ColorUtility.ToHtmlStringRGB(c) };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            materials[c] = m;
            return m;
        }

        Material Glow(Color c)
        {
            var key = new Color(c.r, c.g, c.b, 0.999f);
            if (materials.TryGetValue(key, out var m)) return m;
            m = new Material(baseMaterial) { name = "ShipGlow_" + ColorUtility.ToHtmlStringRGB(c) };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * 2f);
            materials[key] = m;
            return m;
        }

        Material OverlayMat(Color c)
        {
            if (overlayMaterials.TryGetValue(c, out var m)) return m;
            var src = overlayMaterial != null ? overlayMaterial : baseMaterial;
            m = new Material(src) { name = "Fairway_" + ColorUtility.ToHtmlStringRGBA(c) };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            overlayMaterials[c] = m;
            return m;
        }
    }
}
