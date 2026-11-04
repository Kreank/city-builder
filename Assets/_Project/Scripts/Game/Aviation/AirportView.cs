using System.Collections.Generic;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Aviation;
using CityBuilder.Simulation.Core;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Aviation
{
    /// <summary>
    /// Zeichnet die Flughäfen: Bahn aus Kacheln (Modelle „Runway“/„Runway_End“, 64 m), Parallelrollweg und Abrollbahnen („Taxiway“),
    /// Vorfeld (Beton), Terminal-Module („Terminal“, Landseite zur Straße), Tower („ControlTower“), Hangar („Hangar“, Tor zum Vorfeld),
    /// Windsack („Windsock“, dreht nach dem Wind des Tages) – sonst Platzhalter. Flugzeuge („Airliner“, „Airliner_Red“ = Fracht,
    /// „SmallPlane“, Turboprop = verkleinerter Airliner) aus den Posen der Simulation, interpoliert zwischen zwei Verkehrsschritten,
    /// Gepäckschlepper („BaggageTrain“) an parkenden Flugzeugen, Einflugschneisen + Hindernisse als Overlay (im Werkzeug bzw. per Knopf).
    /// Liest nur.
    /// </summary>
    public sealed class AirportView : MonoBehaviour
    {
        /// <summary>Aktive Darstellung (Inspektor: Flugzeug anklicken/verfolgen).</summary>
        public static AirportView Active { get; private set; }
        /// <summary>Einflugschneisen + Hindernisse immer zeigen (Fenster F12), sonst nur im Werkzeug.</summary>
        public static bool ShowCorridors;

        SimulationRunner runner;
        Material baseMaterial, overlayMaterial;
        Mesh cube;
        RoadTool tool;
        Transform root, airportRoot, planeRoot, overlayRoot;
        readonly Dictionary<int, AirportVisual> airportObjects = new Dictionary<int, AirportVisual>();
        readonly Dictionary<int, PlaneVisual> planeObjects = new Dictionary<int, PlaneVisual>();
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        readonly Dictionary<Color, Material> overlayMaterials = new Dictionary<Color, Material>();
        int builtLayout = -1, builtOverlay = -1;

        sealed class AirportVisual
        {
            public GameObject Root;
            public Transform Windsock;
            public int Level;
            public readonly List<Transform> Baggage = new List<Transform>();
        }

        sealed class PlaneVisual
        {
            public GameObject Root;
            public float Length;
            public Vector3 Center;
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
                foreach (var v in airportObjects.Values) if (v.Root != null) Destroy(v.Root);
                foreach (var v in planeObjects.Values) if (v.Root != null) Destroy(v.Root);
                airportObjects.Clear(); planeObjects.Clear();
                ClearChildren(overlayRoot);
                builtLayout = builtOverlay = -1;
            };
        }

        void Start()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            root = new GameObject("Flughafen").transform;
            root.SetParent(transform, false);
            airportRoot = Child("Flughäfen"); planeRoot = Child("Flugzeuge"); overlayRoot = Child("Einflugschneisen");
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
            if (runner == null || runner.World?.Aviation == null || cube == null || baseMaterial == null) return;
            var av = runner.World.Aviation;
            if (av.LayoutVersion != builtLayout) { builtLayout = av.LayoutVersion; SyncAirports(av); builtOverlay = -1; }
            bool overlay = ShowCorridors || (tool != null && tool.CurrentMode == RoadTool.Mode.Airport);
            if (overlay && builtOverlay != av.Version) { builtOverlay = av.Version; BuildOverlay(av); }
            overlayRoot.gameObject.SetActive(overlay);
            AnimateAirports(av);
            DrawPlanes(av);
        }

        // ------------------------------------------------------------ Anlagen

        void SyncAirports(AviationSystem av)
        {
            var alive = new HashSet<int>();
            foreach (var a in av.Airports)
            {
                alive.Add(a.Id);
                if (airportObjects.TryGetValue(a.Id, out var v) && v.Level == a.Level) continue;
                if (v != null && v.Root != null) Destroy(v.Root);
                airportObjects[a.Id] = BuildAirport(a);
            }
            var dead = new List<int>();
            foreach (var kv in airportObjects) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead) { if (airportObjects[id].Root != null) Destroy(airportObjects[id].Root); airportObjects.Remove(id); }
        }

        /// <summary>Drehung so, dass die lokale X-Achse entlang <paramref name="along"/> liegt (Kacheln: Bahn entlang X).</summary>
        static Quaternion AlongX(SimVector2 along) => Quaternion.LookRotation(new Vector3(-along.Y, 0f, along.X));
        static Quaternion Facing(SimVector2 front) => Quaternion.LookRotation(new Vector3(front.X, 0f, front.Y));

        AirportVisual BuildAirport(Airport a)
        {
            var l = a.Layout;
            var v = new AirportVisual { Root = new GameObject(a.Name), Level = a.Level };
            var tr = v.Root.transform;
            tr.SetParent(airportRoot, false);
            float y = a.Height;
            var grass = Mat(new Color(0.42f, 0.55f, 0.32f));
            var concrete = Mat(new Color(0.66f, 0.66f, 0.63f));
            var asphalt = Mat(new Color(0.22f, 0.22f, 0.24f));
            var white = Mat(new Color(0.95f, 0.95f, 0.95f));
            var yellow = Mat(new Color(0.95f, 0.8f, 0.15f));

            // Grasstreifen (Bauverbot) und Terminal-Bereich als flache Platten
            foreach (var site in l.Areas)
                Box(tr, SimConvert.ToWorld(site.Center, y + 0.02f), new Vector3(site.Width, 0.04f, site.Depth), grass, Facing(site.Front));

            // Bahn aus 64-m-Kacheln (Enden mit Schwellenmarkierung)
            var along = AlongX(l.Dir);
            int tiles = Mathf.Max(1, Mathf.RoundToInt(l.Length / 64f));
            float tileLen = l.Length / tiles;
            bool runwayModel = false;
            for (int i = 0; i < tiles; i++)
            {
                float u = -l.HalfLength + (i + 0.5f) * tileLen;
                var t = Node(tr, "Bahn", SimConvert.ToWorld(l.At(u, 0f), y), along * (i == 0 ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity));
                bool end = i == 0 || i == tiles - 1;
                runwayModel |= TryModel(end ? "air_runway_end" : "air_runway", t, tileLen, 48f);
            }
            if (!runwayModel)
            {
                Box(tr, SimConvert.ToWorld(l.Center, y + 0.1f), new Vector3(l.Type.RunwayWidth, 0.2f, l.Length), asphalt, Facing(l.Dir));
                for (float u = -l.HalfLength + 60f; u < l.HalfLength - 60f; u += 50f)
                    Box(tr, SimConvert.ToWorld(l.At(u, 0f), y + 0.22f), new Vector3(0.9f, 0.04f, 24f), white, Facing(l.Dir));
                for (int e = -1; e <= 1; e += 2)
                    for (int k = -3; k <= 3; k++)
                        Box(tr, SimConvert.ToWorld(l.At(e * (l.HalfLength - 12f), k * l.Type.RunwayWidth / 8f), y + 0.22f), new Vector3(1.8f, 0.04f, 18f), white, Facing(l.Dir));
            }

            // Parallelrollweg + Abrollbahnen
            int tw = Mathf.Max(1, Mathf.RoundToInt(l.Length / 32f));
            float twLen = l.Length / tw;
            bool taxiModel = false;
            for (int i = 0; i < tw; i++)
            {
                var t = Node(tr, "Rollweg", SimConvert.ToWorld(l.At(-l.HalfLength + (i + 0.5f) * twLen, l.TaxiwayV), y), along);
                taxiModel |= TryModel("air_taxiway", t, twLen, 32f);
            }
            if (!taxiModel) Box(tr, SimConvert.ToWorld(l.At(0f, l.TaxiwayV), y + 0.08f), new Vector3(l.Type.TaxiwayWidth, 0.16f, l.Length), asphalt, Facing(l.Dir));
            var acrossX = AlongX(l.Normal);
            foreach (float u in l.Connectors())
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(l.TaxiwayV / 32f));
                float len = l.TaxiwayV / n;
                bool model = false;
                for (int i = 0; i < n; i++)
                {
                    var t = Node(tr, "Abrollbahn", SimConvert.ToWorld(l.At(u, (i + 0.5f) * len), y), acrossX);
                    model |= TryModel("air_taxiway", t, len, 32f);
                }
                if (!model) Box(tr, SimConvert.ToWorld(l.At(u, l.TaxiwayV * 0.5f), y + 0.08f), new Vector3(l.Type.TaxiwayWidth, 0.16f, l.TaxiwayV), asphalt, Facing(l.Normal));
                Box(tr, SimConvert.ToWorld(l.At(u, l.TaxiwayV * 0.5f), y + 0.2f), new Vector3(0.5f, 0.03f, l.TaxiwayV), yellow, Facing(l.Normal));
            }

            // Vorfeld (Beton) mit Positionsmarken
            var apronCenter = l.At(0f, (l.ApronNear + l.ApronFar) * 0.5f);
            Box(tr, SimConvert.ToWorld(apronCenter, y + 0.06f), new Vector3(l.ApronLength, 0.12f, l.Type.ApronDepth), concrete, Facing(l.Normal));
            for (int i = 0; i < l.StandCount; i++)
            {
                float u = l.StandU(i);
                Box(tr, SimConvert.ToWorld(l.At(u, (l.ApronNear + l.TerminalBack) * 0.5f), y + 0.14f), new Vector3(0.5f, 0.03f, l.TerminalBack - l.ApronNear), yellow, Facing(l.Normal));
            }

            // Terminal-Module (Landseite = Normal)
            var term = l.Terminal;
            for (int k = 0; k < l.Modules; k++)
            {
                float u = (k - (l.Modules - 1) * 0.5f) * 64f;
                var t = Node(tr, "Terminal", SimConvert.ToWorld(l.At(u, l.TerminalBack + 16f), y), Facing(l.Normal));
                if (!TryModel("air_terminal", t, 64f, 32f))
                {
                    Box(t, new Vector3(0f, 7f, 0f), new Vector3(62f, 14f, 30f), Mat(new Color(0.72f, 0.82f, 0.9f)), Quaternion.identity);
                    Box(t, new Vector3(0f, 14.5f, 0f), new Vector3(64f, 1f, 32f), white, Quaternion.identity);
                }
            }
            // Tower, Hangar, Windsack
            var tower = Node(tr, "Tower", SimConvert.ToWorld(l.Tower, y), Facing(l.Normal));
            if (!TryModel("air_tower", tower, 12f, 12f))
            {
                Box(tower, new Vector3(0f, 18f, 0f), new Vector3(5f, 36f, 5f), white, Quaternion.identity);
                Box(tower, new Vector3(0f, 38.5f, 0f), new Vector3(11f, 5f, 11f), Mat(new Color(0.3f, 0.45f, 0.55f)), Quaternion.identity);
            }
            var hangar = Node(tr, "Hangar", SimConvert.ToWorld(l.Hangar, y), Facing(-l.Normal)); // Tor (+Z) zum Vorfeld
            if (!TryModel("air_hangar", hangar, 40f, 32f))
                Box(hangar, new Vector3(0f, 7f, 0f), new Vector3(40f, 14f, 32f), Mat(new Color(0.6f, 0.62f, 0.66f)), Quaternion.identity);
            var sock = Node(tr, "Windsack", SimConvert.ToWorld(l.Windsock, y), Quaternion.identity);
            if (!TryModel("air_windsock", sock, 4f, 4f))
            {
                Box(sock, new Vector3(0f, 3f, 0f), new Vector3(0.2f, 6f, 0.2f), white, Quaternion.identity);
                Box(sock, new Vector3(1.5f, 5.6f, 0f), new Vector3(3f, 0.7f, 0.7f), Mat(new Color(0.95f, 0.45f, 0.1f)), Quaternion.identity);
            }
            v.Windsock = sock;
            return v;
        }

        void AnimateAirports(AviationSystem av)
        {
            foreach (var a in av.Airports)
            {
                if (!airportObjects.TryGetValue(a.Id, out var v) || v.Windsock == null) continue;
                // Windsack zeigt mit dem Wind (lokale +X in Windrichtung = weg von „aus“)
                var to = -a.WindToday;
                v.Windsock.rotation = AlongX(to);
            }
        }

        // ------------------------------------------------------------ Flugzeuge

        void DrawPlanes(AviationSystem av)
        {
            float alpha = runner.TickAlpha;
            var alive = new HashSet<int>();
            foreach (var x in av.AllAircraft)
            {
                float s = Mathf.Lerp(x.PrevS, x.S, alpha);
                if (!av.TryPose(x, s, out var pos, out float alt, out var heading)) continue;
                var a = av.GetAirport(x.AirportId);
                if (a == null) continue;
                alive.Add(x.Id);
                if (!planeObjects.TryGetValue(x.Id, out var v)) planeObjects[x.Id] = v = BuildPlane(x);
                var dir = new Vector3(heading.X, 0f, heading.Y);
                float pitch = 0f;
                if (x.State == AircraftState.Approach) pitch = -3f;
                else if (x.State == AircraftState.Takeoff && alt > 0.5f) pitch = Mathf.Atan(x.Type.ClimbGradient) * Mathf.Rad2Deg * 1.6f;
                var rot = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir.normalized) * Quaternion.Euler(-pitch, 0f, 0f) : Quaternion.identity;
                var p3 = SimConvert.ToWorld(pos, a.Height + alt + 0.2f);
                v.Root.transform.SetPositionAndRotation(p3, rot);
                v.Center = p3 + Vector3.up * 3f;
                if (!v.Root.activeSelf) v.Root.SetActive(true);
                SyncBaggage(x, v, alive);
            }
            var dead = new List<int>();
            foreach (var kv in planeObjects) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead) { if (planeObjects[id].Root != null) Destroy(planeObjects[id].Root); planeObjects.Remove(id); }
        }

        PlaneVisual BuildPlane(Aircraft x)
        {
            var t = x.Type;
            var v = new PlaneVisual { Root = new GameObject($"{t.DisplayName} {x.FlightNumber}"), Length = t.Length };
            v.Root.transform.SetParent(planeRoot, false);
            var body = new GameObject("Modell").transform;
            body.SetParent(v.Root.transform, false);
            body.localScale = Vector3.one * t.ModelScale;
            if (TryModel(t.ModelId, body, t.Span / t.ModelScale, t.Length / t.ModelScale)) return v;
            // Platzhalter: Rumpf, Flügel, Leitwerk
            var col = t.Role == AircraftRole.Cargo ? new Color(0.8f, 0.2f, 0.18f) : new Color(0.93f, 0.94f, 0.96f);
            var m = Mat(col);
            Box(v.Root.transform, new Vector3(0f, t.Length * 0.06f + 1f, 0f), new Vector3(t.Length * 0.1f, t.Length * 0.1f, t.Length), m, Quaternion.identity);
            Box(v.Root.transform, new Vector3(0f, t.Length * 0.05f + 0.8f, 0.05f * t.Length), new Vector3(t.Span, 0.4f, t.Length * 0.16f), m, Quaternion.identity);
            Box(v.Root.transform, new Vector3(0f, t.Length * 0.12f + 1f, -0.45f * t.Length), new Vector3(t.Span * 0.35f, 0.3f, t.Length * 0.08f), m, Quaternion.identity);
            Box(v.Root.transform, new Vector3(0f, t.Length * 0.18f + 1f, -0.45f * t.Length), new Vector3(0.3f, t.Length * 0.14f, t.Length * 0.1f), Mat(new Color(0.2f, 0.35f, 0.7f)), Quaternion.identity);
            return v;
        }

        /// <summary>Gepäckschlepper fährt an einem parkenden Flugzeug hin und her (rein optisch).</summary>
        void SyncBaggage(Aircraft x, PlaneVisual v, HashSet<int> alive)
        {
            var existing = v.Root.transform.Find("Gepäck");
            if (x.State != AircraftState.Parked || x.Type.Role == AircraftRole.General)
            {
                if (existing != null) Destroy(existing.gameObject);
                return;
            }
            if (existing == null)
            {
                existing = new GameObject("Gepäck").transform;
                existing.SetParent(v.Root.transform, false);
                if (!TryModel("air_baggage", existing, 2f, 9f))
                    Box(existing, new Vector3(0f, 0.7f, 0f), new Vector3(1.6f, 1.4f, 9f), Mat(new Color(0.95f, 0.75f, 0.1f)), Quaternion.identity);
            }
            float t = Mathf.PingPong(Time.time * 0.15f + x.Id * 0.37f, 1f);
            existing.localPosition = new Vector3(x.Type.Span * 0.3f, 0f, Mathf.Lerp(-x.Type.Length * 0.3f, x.Type.Length * 0.25f, t));
            existing.localRotation = Quaternion.identity;
        }

        public int PickAircraft(Ray ray, float maxDistance = 4000f)
        {
            int best = 0;
            float bestD = maxDistance;
            foreach (var kv in planeObjects)
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

        public bool TryGetAircraftPosition(int id, out Vector3 position)
        {
            position = default;
            if (!planeObjects.TryGetValue(id, out var v) || v.Root == null) return false;
            position = v.Center;
            return true;
        }

        // ------------------------------------------------------------ Einflugschneisen (Overlay)

        void BuildOverlay(AviationSystem av)
        {
            ClearChildren(overlayRoot);
            foreach (var a in av.Airports)
            {
                var l = a.Layout;
                for (int e = 0; e < 2; e++)
                {
                    var state = e == 0 ? a.CorridorA : a.CorridorB;
                    var col = state == CorridorState.Closed ? new Color(1f, 0.25f, 0.2f, 0.8f) : state == CorridorState.Limited ? new Color(1f, 0.8f, 0.2f, 0.8f) : new Color(0.3f, 0.95f, 0.5f, 0.7f);
                    var outward = e == 0 ? -l.Dir : l.Dir;
                    var start = (e == 0 ? l.EndA : l.EndB) + outward * AviationRules.ApproachStart;
                    float len = l.Type.ApproachLength, w0 = l.Type.ApproachInnerHalfWidth, w1 = w0 + AviationRules.Divergence * len;
                    var go = new GameObject("Schneise");
                    go.transform.SetParent(overlayRoot, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.useWorldSpace = true; lr.loop = true; lr.widthMultiplier = 4f;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.sharedMaterial = OverlayMat(col);
                    float hEnd = a.Height + l.Type.ApproachSlope * len;
                    lr.positionCount = 4;
                    lr.SetPosition(0, SimConvert.ToWorld(start + l.Normal * w0, a.Height + 1f));
                    lr.SetPosition(1, SimConvert.ToWorld(start + outward * len + l.Normal * w1, hEnd));
                    lr.SetPosition(2, SimConvert.ToWorld(start + outward * len - l.Normal * w1, hEnd));
                    lr.SetPosition(3, SimConvert.ToWorld(start - l.Normal * w0, a.Height + 1f));
                }
                foreach (var o in a.Obstacles)
                {
                    var m = new GameObject("Hindernis");
                    m.transform.SetParent(overlayRoot, false);
                    float top = Mathf.Max(4f, o.Top);
                    m.transform.position = SimConvert.ToWorld(o.Position, a.Height + top * 0.5f);
                    m.transform.localScale = new Vector3(6f, top, 6f);
                    m.AddComponent<MeshFilter>().sharedMesh = cube;
                    m.AddComponent<MeshRenderer>().sharedMaterial = OverlayMat(o.Core ? new Color(1f, 0.2f, 0.15f, 0.7f) : new Color(1f, 0.8f, 0.2f, 0.6f));
                }
            }
        }

        // ------------------------------------------------------------ Modelle / Grundkörper

        Transform Node(Transform parent, string name, Vector3 worldPos, Quaternion rot)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.SetPositionAndRotation(worldPos, rot);
            return t;
        }

        /// <summary>Eigenes Blender-Modell einsetzen (Paletten-Material, grobe Einheitenfehler korrigieren – wie ShippingView).</summary>
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
            var b = LocalBounds(parent, go);
            float fit = Mathf.Min(width / Mathf.Max(0.01f, Mathf.Max(b.size.x, 0.01f)), depth / Mathf.Max(0.01f, b.size.z));
            if (fit < 0.2f || fit > 5f) go.transform.localScale = Vector3.one * fit;
            return true;
        }

        static Bounds LocalBounds(Transform parent, GameObject go)
        {
            var b = new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var c = parent.InverseTransformPoint(mf.transform.TransformPoint(mb.center));
                var size = Vector3.Scale(mb.size, mf.transform.lossyScale);
                var local = new Bounds(c, size);
                if (!any) { b = local; any = true; } else b.Encapsulate(local);
            }
            return b;
        }

        void Box(Transform parent, Vector3 pos, Vector3 size, Material m, Quaternion rot)
        {
            var go = new GameObject("Box");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
        }

        Material Mat(Color c)
        {
            if (materials.TryGetValue(c, out var m)) return m;
            m = new Material(baseMaterial) { color = c, name = "Airport " + ColorUtility.ToHtmlStringRGB(c) };
            materials[c] = m;
            return m;
        }

        Material OverlayMat(Color c)
        {
            if (overlayMaterials.TryGetValue(c, out var m)) return m;
            m = new Material(overlayMaterial) { color = c, name = "AirportOverlay " + ColorUtility.ToHtmlStringRGBA(c) };
            overlayMaterials[c] = m;
            return m;
        }
    }
}
