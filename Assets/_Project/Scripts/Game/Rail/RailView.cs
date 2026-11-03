using System.Collections.Generic;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Rail;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Traffic;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Rail
{
    /// <summary>
    /// Zeichnet die Bahn: Gleise als eigenes Mesh je Gleisstück (Schotterbett, Schwellen, Schienen, Fahrdraht; Brücken mit
    /// Tragwerk und Pfeilern), Signale an den Blockgrenzen (rot = Block belegt/reserviert), Bahnhöfe (Modell „train_station“
    /// aus <c>Models/Transport/TrainStation</c>, sonst Platzhalter; Bahnsteige + Dach), Bahnübergänge (Modell
    /// „rail_level_crossing“ mit Schranken „BarrierA“/„BarrierB“, sonst Platzhalter – Schranken senken/heben sich) und Züge
    /// (Lok + Wagen aus <c>Models/Rail</c>, sonst Quader), interpoliert zwischen zwei Verkehrsschritten. Liest nur.
    /// </summary>
    public sealed class RailView : MonoBehaviour
    {
        /// <summary>Aktive Darstellung (Inspektor: Zug anklicken/verfolgen).</summary>
        public static RailView Active { get; private set; }
        /// <summary>Signale an den Blockgrenzen zeigen.</summary>
        public static bool ShowSignals = true;

        SimulationRunner runner;
        Material baseMaterial;
        Mesh cube;
        Transform root, trackRoot, stationRoot, crossingRoot, trainRoot;
        readonly Dictionary<int, GameObject> trackObjects = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> stationObjects = new Dictionary<int, GameObject>();
        readonly Dictionary<int, TrainVisual> trainObjects = new Dictionary<int, TrainVisual>();
        readonly List<CrossingVisual> crossingObjects = new List<CrossingVisual>();
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        Material gravel, sleeper, steel;
        int builtTracks = -1, builtStations = -1, builtCrossings = -1, crossingRoads = -1;
        readonly List<Matrix4x4> signalPosts = new List<Matrix4x4>(), signalRed = new List<Matrix4x4>(), signalGreen = new List<Matrix4x4>();
        readonly Matrix4x4[] buffer = new Matrix4x4[1023];
        Material postMat, redMat, greenMat;

        sealed class TrainVisual
        {
            public GameObject Root;
            public readonly List<(Transform t, float offset, float length)> Cars = new List<(Transform, float, float)>();
            public Vector3 Head;
        }

        sealed class CrossingVisual
        {
            public GameObject Root;
            public SimVector2 Position;
            public Vector3 Axis;
            public readonly List<(Transform t, Vector3 pos, Quaternion rot, float sign)> Barriers = new List<(Transform, Vector3, Quaternion, float)>();
            public float Open = 1f;
        }

        public void Init(SimulationRunner r, Material lit)
        {
            runner = r;
            baseMaterial = lit;
            Active = this;
            r.WorldReplaced += () =>
            {
                // Speichern & Laden: alles gehört zur alten Welt
                foreach (var o in trackObjects.Values) DestroyTrack(o);
                foreach (var o in stationObjects.Values) if (o != null) Destroy(o);
                foreach (var v in trainObjects.Values) if (v.Root != null) Destroy(v.Root);
                foreach (var c in crossingObjects) if (c.Root != null) Destroy(c.Root);
                trackObjects.Clear(); stationObjects.Clear(); trainObjects.Clear(); crossingObjects.Clear();
                builtTracks = builtStations = builtCrossings = crossingRoads = -1;
            };
        }

        void Start()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            root = new GameObject("Rail").transform;
            root.SetParent(transform, false);
            trackRoot = Child("Gleise"); stationRoot = Child("Bahnhöfe"); crossingRoot = Child("Bahnübergänge"); trainRoot = Child("Züge");
            if (baseMaterial == null) return;
            gravel = Mat(new Color(0.46f, 0.43f, 0.40f));
            sleeper = Mat(new Color(0.62f, 0.60f, 0.56f));
            steel = Mat(new Color(0.55f, 0.56f, 0.6f));
            postMat = Instanced(new Color(0.25f, 0.26f, 0.28f));
            redMat = Instanced(new Color(1f, 0.15f, 0.1f));
            greenMat = Instanced(new Color(0.2f, 1f, 0.35f));
        }

        Transform Child(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            foreach (var m in materials.Values) if (m != null) Destroy(m);
            foreach (var o in trackObjects.Values) DestroyTrack(o);
            foreach (var m in new[] { postMat, redMat, greenMat }) if (m != null) Destroy(m);
        }

        void LateUpdate()
        {
            if (runner == null || runner.World?.Rail == null || cube == null || baseMaterial == null) return;
            var world = runner.World;
            var rail = world.Rail;
            if (rail.Tracks.Version != builtTracks) { builtTracks = rail.Tracks.Version; SyncTracks(world, rail); }
            if (rail.Version != builtStations) { builtStations = rail.Version; SyncStations(world, rail); }
            if (rail.Tracks.Version != builtCrossings || world.Roads.Version != crossingRoads)
            {
                builtCrossings = rail.Tracks.Version;
                crossingRoads = world.Roads.Version;
                SyncCrossings(world, rail);
            }
            AnimateCrossings(rail);
            DrawTrains(world, rail);
            DrawSignals(world, rail);
        }

        // ------------------------------------------------------------ Gleise

        void SyncTracks(SimulationWorld world, RailSystem rail)
        {
            var alive = new HashSet<int>();
            foreach (var s in rail.Tracks.Segments)
            {
                alive.Add(s.Id);
                if (trackObjects.ContainsKey(s.Id)) continue;
                trackObjects[s.Id] = BuildTrack(world, s);
            }
            var dead = new List<int>();
            foreach (var kv in trackObjects) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead) { DestroyTrack(trackObjects[id]); trackObjects.Remove(id); }
        }

        void DestroyTrack(GameObject go)
        {
            if (go == null) return;
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(go);
        }

        const float Gauge = 1.435f, RailTop = 0.62f;

        /// <summary>Gleis-Mesh: Schotterbett (Trapez), Schwellen alle 0,6 m, zwei Schienen, Fahrdraht; Brücke mit Tragwerk/Pfeilern.</summary>
        GameObject BuildTrack(SimulationWorld world, RoadSegment s)
        {
            var go = new GameObject($"Gleis {s.Id}");
            go.transform.SetParent(trackRoot, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // Mesh in Weltkoordinaten
            var verts = new List<Vector3>();
            var tri0 = new List<int>(); var tri1 = new List<int>(); var tri2 = new List<int>();
            int n = Mathf.Max(2, Mathf.CeilToInt(s.Length / 2f)) + 1;
            var center = new Vector3[n]; var right = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                var p = s.Evaluate(t);
                var r = s.Right(t);
                center[i] = SimConvert.ToWorld(p, s.HeightAt(t));
                right[i] = new Vector3(r.X, 0f, r.Y);
            }
            // Schotterbett: unten ±2,2 m, oben ±1,5 m, 0,35 m hoch
            Strip(verts, tri0, center, right, -2.2f, 0f, -1.5f, 0.35f);
            Strip(verts, tri0, center, right, -1.5f, 0.35f, 1.5f, 0.35f);
            Strip(verts, tri0, center, right, 1.5f, 0.35f, 2.2f, 0f);
            // Schienen (Kopf 0,08 m breit, Flanken 0,15 m)
            for (int k = -1; k <= 1; k += 2)
            {
                float x = k * Gauge * 0.5f;
                Strip(verts, tri2, center, right, x - 0.04f, RailTop, x + 0.04f, RailTop);
                Strip(verts, tri2, center, right, x - 0.04f, RailTop - 0.15f, x - 0.04f, RailTop);
                Strip(verts, tri2, center, right, x + 0.04f, RailTop, x + 0.04f, RailTop - 0.15f);
            }
            // Fahrdraht (Oberleitung) 5,6 m über Schienenoberkante
            Strip(verts, tri2, center, right, -0.04f, RailTop + 5.6f, 0.04f, RailTop + 5.6f);
            // Schwellen
            int sleepers = Mathf.Max(1, Mathf.FloorToInt(s.Length / 0.6f));
            for (int k = 0; k < sleepers; k++)
            {
                float t = (k + 0.5f) / sleepers;
                var p = s.Evaluate(t);
                var r = s.Right(t);
                var tan = s.Tangent(t);
                var c = SimConvert.ToWorld(p, s.HeightAt(t) + 0.4f);
                var rr = new Vector3(r.X, 0f, r.Y) * 1.3f;
                var ff = new Vector3(tan.X, 0f, tan.Y) * 0.13f;
                int b = verts.Count;
                verts.Add(c - rr - ff); verts.Add(c - rr + ff); verts.Add(c + rr + ff); verts.Add(c + rr - ff);
                tri1.Add(b); tri1.Add(b + 1); tri1.Add(b + 2); tri1.Add(b); tri1.Add(b + 2); tri1.Add(b + 3);
            }
            // Brücke: Tragwerk unter dem Schotterbett
            float maxElev = s.MaxElevation;
            if (maxElev > 1.2f)
            {
                Strip(verts, tri1, center, right, -2.6f, -1.2f, -2.6f, 0.05f);
                Strip(verts, tri1, center, right, 2.6f, 0.05f, 2.6f, -1.2f);
                Strip(verts, tri1, center, right, 2.6f, -1.2f, -2.6f, -1.2f);
            }
            var mesh = new Mesh { name = $"Gleis {s.Id}" };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(tri0, 0); mesh.SetTriangles(tri1, 1); mesh.SetTriangles(tri2, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { gravel, sleeper, steel };

            // Brückenpfeiler alle ~25 m, Oberleitungsmasten alle ~50 m
            var pillarMat = Mat(new Color(0.7f, 0.7f, 0.68f));
            var mastMat = Mat(new Color(0.5f, 0.52f, 0.55f));
            int masts = Mathf.Max(1, Mathf.RoundToInt(s.Length / 50f));
            for (int k = 0; k < masts; k++)
            {
                float t = (k + 0.5f) / masts;
                var p = s.Evaluate(t);
                var r = s.Right(t);
                float h = s.HeightAt(t);
                var mp = SimConvert.ToWorld(p + r * 3f, h);
                Box(go.transform, mp + Vector3.up * 3.6f, new Vector3(0.25f, 7.2f, 0.25f), mastMat, Quaternion.identity, true);
                var arm = SimConvert.ToWorld(p + r * 1.4f, h + 6.5f);
                var rot = Quaternion.LookRotation(new Vector3(r.X, 0f, r.Y));
                Box(go.transform, arm, new Vector3(0.12f, 0.12f, 3.4f), mastMat, rot, true);
            }
            if (maxElev > 1.2f)
            {
                int pillars = Mathf.Max(1, Mathf.RoundToInt(s.Length / 25f));
                for (int k = 0; k <= pillars; k++)
                {
                    float t = k / (float)pillars;
                    float e = s.ElevationAt(t);
                    if (e < 2f) continue;
                    var p = s.Evaluate(t);
                    float top = s.HeightAt(t) - 1.2f;
                    float ground = world.Terrain.HeightAt(p);
                    if (world.Terrain.IsWater(p)) ground = Mathf.Min(ground, world.Terrain.WaterLevel - 2f);
                    float hgt = Mathf.Max(0.5f, top - ground);
                    Box(go.transform, SimConvert.ToWorld(p, ground + hgt * 0.5f), new Vector3(2.2f, hgt, 1.6f), pillarMat,
                        Quaternion.LookRotation(new Vector3(s.Tangent(t).X, 0f, s.Tangent(t).Y)), true);
                }
            }
            return go;
        }

        /// <summary>Streifen entlang der Mittellinie zwischen zwei Querschnittspunkten (x = seitlich, y = Höhe).</summary>
        static void Strip(List<Vector3> verts, List<int> tris, Vector3[] center, Vector3[] right, float x0, float y0, float x1, float y1)
        {
            int b = verts.Count;
            for (int i = 0; i < center.Length; i++)
            {
                verts.Add(center[i] + right[i] * x0 + Vector3.up * y0);
                verts.Add(center[i] + right[i] * x1 + Vector3.up * y1);
            }
            for (int i = 0; i < center.Length - 1; i++)
            {
                int a = b + i * 2;
                tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
            }
        }

        // ------------------------------------------------------------ Bahnhöfe

        void SyncStations(SimulationWorld world, RailSystem rail)
        {
            // Bahnsteige hängen an den Gleisen → bei jeder Änderung neu (wenige Bahnhöfe)
            foreach (var o in stationObjects.Values) if (o != null) Destroy(o);
            stationObjects.Clear();
            foreach (var s in rail.Stations)
            {
                var tr = new GameObject($"{s.Type.DisplayName} {s.Name} #{s.Id}").transform;
                tr.SetParent(stationRoot, false);
                tr.SetPositionAndRotation(SimConvert.ToWorld(s.Position, s.Height), Quaternion.LookRotation(new Vector3(s.Front.X, 0f, s.Front.Y)));
                float w = s.Type.Width, d = s.Type.Depth;
                if (s.IsPassenger)
                {
                    if (!TryModel("train_station", tr, w, d))
                    {
                        Box(tr, new Vector3(0f, 5f, 0f), new Vector3(w * 0.8f, 10f, d * 0.7f), Mat(new Color(0.86f, 0.80f, 0.70f)), Quaternion.identity);
                        Box(tr, new Vector3(0f, 10.4f, 0f), new Vector3(w * 0.84f, 0.8f, d * 0.74f), Mat(new Color(0.55f, 0.25f, 0.2f)), Quaternion.identity);
                        Box(tr, new Vector3(0f, 7.5f, d * 0.36f), new Vector3(w * 0.3f, 2f, 0.2f), Mat(new Color(0.15f, 0.3f, 0.6f)), Quaternion.identity); // Schild
                        Box(tr, new Vector3(-w * 0.32f, 9f, d * 0.2f), new Vector3(3f, 18f, 3f), Mat(new Color(0.8f, 0.74f, 0.64f)), Quaternion.identity); // Uhrturm
                    }
                }
                else
                {
                    Box(tr, new Vector3(0f, 0.15f, 0f), new Vector3(w, 0.3f, d), Mat(new Color(0.45f, 0.45f, 0.47f)), Quaternion.identity); // Hof
                    Box(tr, new Vector3(-w * 0.2f, 5f, -d * 0.1f), new Vector3(w * 0.5f, 10f, d * 0.6f), Mat(new Color(0.7f, 0.72f, 0.74f)), Quaternion.identity); // Halle
                    var colors = new[] { new Color(0.2f, 0.35f, 0.7f), new Color(0.9f, 0.5f, 0.1f), new Color(0.2f, 0.55f, 0.3f), new Color(0.75f, 0.2f, 0.2f) };
                    for (int i = 0; i < 6; i++) // Containerstapel
                        Box(tr, new Vector3(w * 0.22f + (i % 3) * 3.2f, 1.6f + (i / 3) * 2.6f, d * 0.05f), new Vector3(2.5f, 2.6f, 6.1f), Mat(colors[i % colors.Length]), Quaternion.identity);
                    Box(tr, new Vector3(w * 0.3f, 9f, -d * 0.45f), new Vector3(0.6f, 18f, 0.6f), Mat(new Color(0.9f, 0.75f, 0.1f)), Quaternion.identity); // Kran
                    Box(tr, new Vector3(w * 0.3f, 17.5f, -d * 0.6f), new Vector3(0.5f, 0.6f, 14f), Mat(new Color(0.9f, 0.75f, 0.1f)), Quaternion.identity);
                }
                BuildPlatforms(world, s, tr);
                stationObjects[s.Id] = tr.gameObject;
            }
        }

        /// <summary>Bahnsteig zwischen Gebäude und Gleis 1 (mit Dach), weitere Bahnsteige außen an den übrigen Gleisen.</summary>
        void BuildPlatforms(SimulationWorld world, Station s, Transform tr)
        {
            var concrete = Mat(new Color(0.72f, 0.71f, 0.68f));
            var roof = Mat(new Color(0.35f, 0.4f, 0.45f));
            float L = s.Type.PlatformLength, back = -s.Type.Depth * 0.5f;
            float h = 0.76f;
            var edge = Mat(new Color(0.95f, 0.85f, 0.2f));
            for (int i = 0; i < s.Platforms.Count; i++)
            {
                var p = s.Platforms[i];
                float near, far;
                if (i == 0) { near = 0.2f; far = p.Offset - 1.7f; }
                else { near = p.Offset + 1.7f; far = p.Offset + 4.5f; }
                if (far <= near + 0.5f) continue;
                float mid = back - (near + far) * 0.5f, depth = far - near;
                Box(tr, new Vector3(0f, h * 0.5f, mid), new Vector3(L, h, depth), concrete, Quaternion.identity);
                float edgeZ = i == 0 ? back - far + 0.25f : back - near - 0.25f;
                Box(tr, new Vector3(0f, h + 0.01f, edgeZ), new Vector3(L, 0.02f, 0.3f), edge, Quaternion.identity); // Sicherheitslinie
                if (i == 0)
                {
                    Box(tr, new Vector3(0f, 4.2f, mid), new Vector3(L * 0.6f, 0.25f, depth + 0.6f), roof, Quaternion.identity);
                    for (int k = -3; k <= 3; k++) Box(tr, new Vector3(k * L * 0.09f, 2.4f, mid), new Vector3(0.25f, 3.6f, 0.25f), roof, Quaternion.identity);
                }
            }
        }

        // ------------------------------------------------------------ Bahnübergänge

        void SyncCrossings(SimulationWorld world, RailSystem rail)
        {
            var old = new List<CrossingVisual>(crossingObjects);
            crossingObjects.Clear();
            foreach (var c in rail.Crossings)
            {
                CrossingVisual keep = null;
                foreach (var o in old) if (o.Root != null && SimVector2.Distance(o.Position, c.Position) < 1f) { keep = o; break; }
                if (keep != null) { old.Remove(keep); crossingObjects.Add(keep); continue; }
                var seg = rail.Tracks.GetSegment(c.RailSegmentId);
                if (seg == null) continue;
                var tan = seg.Tangent(c.RailT);
                var v = new CrossingVisual { Position = c.Position, Axis = new Vector3(tan.X, 0f, tan.Y).normalized };
                var tr = new GameObject("Bahnübergang").transform;
                tr.SetParent(crossingRoot, false);
                tr.SetPositionAndRotation(SimConvert.ToWorld(c.Position, seg.HeightAt(c.RailT)), Quaternion.LookRotation(v.Axis));
                v.Root = tr.gameObject;
                if (TryModel("rail_level_crossing", tr, 16f, 16f, out var model))
                {
                    foreach (var name in new[] { "BarrierA", "BarrierB" })
                    {
                        var b = FindDeep(model.transform, name);
                        if (b != null) v.Barriers.Add((b, b.localPosition, b.localRotation, LiftSign(b, v.Axis)));
                    }
                }
                else
                {
                    // Platzhalter: Andreaskreuz-Pfosten + zwei Halbschranken quer über die Straße
                    var road = world.Roads.GetSegment(c.RoadSegmentId);
                    float half = road != null ? road.Type.CarriagewayHalfWidth : 3.5f;
                    var red = Mat(new Color(0.85f, 0.1f, 0.1f));
                    var white = Mat(Color.white);
                    for (int k = -1; k <= 1; k += 2)
                    {
                        var pivot = new GameObject(k < 0 ? "BarrierA" : "BarrierB").transform;
                        pivot.SetParent(tr, false);
                        pivot.localPosition = new Vector3(k * (half + 0.6f), 1f, k * 4.6f);
                        Box(tr, pivot.localPosition + Vector3.down * 0.5f, new Vector3(0.5f, 1f, 0.5f), white, Quaternion.identity);
                        Box(tr, new Vector3(k * (half + 0.6f), 1.6f, k * 5.4f), new Vector3(0.12f, 3.2f, 0.12f), white, Quaternion.identity);
                        Box(tr, new Vector3(k * (half + 0.6f), 2.9f, k * 5.4f), new Vector3(1.4f, 0.2f, 0.06f), red, Quaternion.Euler(0f, 0f, 35f));
                        Box(tr, new Vector3(k * (half + 0.6f), 2.9f, k * 5.4f), new Vector3(1.4f, 0.2f, 0.06f), red, Quaternion.Euler(0f, 0f, -35f));
                        Box(pivot, new Vector3(-k * half * 0.5f, 0f, 0f), new Vector3(half, 0.14f, 0.14f), red, Quaternion.identity);
                        v.Barriers.Add((pivot, pivot.localPosition, pivot.localRotation, LiftSign(pivot, v.Axis)));
                    }
                }
                crossingObjects.Add(v);
            }
            foreach (var o in old) if (o.Root != null) Destroy(o.Root);
        }

        /// <summary>Drehsinn um die Gleisachse, der den Schrankenbaum anhebt (aus der Lage der Kinder zum Drehpunkt).</summary>
        static float LiftSign(Transform pivot, Vector3 axis)
        {
            var rs = pivot.GetComponentsInChildren<Renderer>(true);
            Vector3 c = pivot.position;
            if (rs.Length > 0) { var b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds); c = b.center; }
            var arm = c - pivot.position;
            arm.y = 0f;
            if (arm.sqrMagnitude < 1e-4f) return 1f;
            return Vector3.Cross(axis, arm).y >= 0f ? 1f : -1f;
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

        void AnimateCrossings(RailSystem rail)
        {
            if (crossingObjects.Count == 0) return;
            float step = Time.deltaTime * 0.7f * Mathf.Max(1f, runner.AnimationTimeScale);
            foreach (var v in crossingObjects)
            {
                bool closed = false;
                foreach (var c in rail.Crossings) if (SimVector2.Distance(c.Position, v.Position) < 1f) { closed = c.Closed; break; }
                v.Open = Mathf.MoveTowards(v.Open, closed ? 0f : 1f, step);
                foreach (var (t, pos, rot, sign) in v.Barriers)
                {
                    if (t == null) continue;
                    t.localPosition = pos;
                    t.localRotation = rot;
                    t.RotateAround(t.position, v.Axis, sign * 85f * v.Open);
                }
            }
        }

        // ------------------------------------------------------------ Züge

        void DrawTrains(SimulationWorld world, RailSystem rail)
        {
            float alpha = runner.TickAlpha;
            float half = world.Settings.SizeMeters * 0.5f;
            var alive = new HashSet<int>();
            foreach (var t in rail.Trains)
            {
                if (t.State == TrainState.Waiting) continue;
                alive.Add(t.Id);
                if (!trainObjects.TryGetValue(t.Id, out var v)) trainObjects[t.Id] = v = BuildTrain(t);
                float head = Mathf.Lerp(t.PrevHeadS, t.HeadS, alpha);
                for (int i = 0; i < v.Cars.Count; i++)
                {
                    var (car, offset, len) = v.Cars[i];
                    float bogie = Mathf.Max(1f, len * 0.5f - 3f);
                    rail.Sample(t, head, offset + len * 0.5f - bogie, out var fp, out float fh, out _);
                    rail.Sample(t, head, offset + len * 0.5f + bogie, out var bp, out float bh, out _);
                    var front = SimConvert.ToWorld(fp, fh);
                    var backPt = SimConvert.ToWorld(bp, bh);
                    var mid = (front + backPt) * 0.5f;
                    bool inside = Mathf.Abs(mid.x) < half + 2f && Mathf.Abs(mid.z) < half + 2f;
                    if (car.gameObject.activeSelf != inside) car.gameObject.SetActive(inside);
                    var dir = front - backPt;
                    if (dir.sqrMagnitude < 1e-4f) continue;
                    car.SetPositionAndRotation(mid, Quaternion.LookRotation(dir.normalized));
                    if (i == 0) v.Head = mid + Vector3.up * 3f;
                }
            }
            var dead = new List<int>();
            foreach (var kv in trainObjects) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead) { if (trainObjects[id].Root != null) Destroy(trainObjects[id].Root); trainObjects.Remove(id); }
        }

        TrainVisual BuildTrain(Train t)
        {
            var v = new TrainVisual { Root = new GameObject($"{t.Type.DisplayName} #{t.Id}") };
            v.Root.transform.SetParent(trainRoot, false);
            float offset = 0f;
            foreach (var kind in t.Type.Cars)
            {
                float len = RailCars.Length(kind);
                var car = new GameObject(RailCars.DisplayName(kind)).transform;
                car.SetParent(v.Root.transform, false);
                if (!TryModel(CarModelId(kind), car, 3f, len))
                {
                    var color = kind == CarKind.Locomotive ? new Color(0.75f, 0.12f, 0.1f) : kind == CarKind.PassengerCoach ? new Color(0.92f, 0.92f, 0.9f)
                        : kind == CarKind.ContainerWagon ? new Color(0.2f, 0.4f, 0.75f) : new Color(0.85f, 0.85f, 0.85f);
                    Box(car, new Vector3(0f, RailTop + 2.2f, 0f), new Vector3(2.9f, 3.2f, len - 0.4f), Mat(color), Quaternion.identity);
                    Box(car, new Vector3(0f, RailTop + 0.45f, 0f), new Vector3(2.4f, 0.9f, len - 2f), Mat(new Color(0.15f, 0.15f, 0.16f)), Quaternion.identity);
                }
                v.Cars.Add((car, offset, len));
                offset += len + RailCars.Gap;
            }
            return v;
        }

        static string CarModelId(CarKind k)
        {
            switch (k)
            {
                case CarKind.Locomotive: return "rail_locomotive";
                case CarKind.PassengerCoach: return "rail_coach";
                case CarKind.ContainerWagon: return "rail_container_wagon";
                default: return "rail_tank_wagon";
            }
        }

        /// <summary>Zug unter dem Strahl (Kugel um jeden Wagen), 0 = keiner.</summary>
        public int PickTrain(Ray ray, float maxDistance = 4000f)
        {
            int best = 0;
            float bestD = maxDistance;
            foreach (var kv in trainObjects)
                foreach (var (car, _, len) in kv.Value.Cars)
                {
                    if (car == null || !car.gameObject.activeInHierarchy) continue;
                    var c = car.position + Vector3.up * 2f;
                    var to = c - ray.origin;
                    float along = Vector3.Dot(to, ray.direction);
                    if (along < 0f || along > bestD) continue;
                    float dist = (to - ray.direction * along).magnitude;
                    if (dist > Mathf.Max(3f, len * 0.5f)) continue;
                    bestD = along;
                    best = kv.Key;
                }
            return best;
        }

        /// <summary>Lage der Lok eines Zuges (Inspektor: Ring, Verfolgen).</summary>
        public bool TryGetTrainPosition(int trainId, out Vector3 position)
        {
            position = default;
            if (!trainObjects.TryGetValue(trainId, out var v) || v.Cars.Count == 0 || v.Cars[0].t == null) return false;
            position = v.Head;
            return true;
        }

        // ------------------------------------------------------------ Signale

        void DrawSignals(SimulationWorld world, RailSystem rail)
        {
            if (!ShowSignals || postMat == null) return;
            signalPosts.Clear(); signalRed.Clear(); signalGreen.Clear();
            foreach (var s in rail.Tracks.Segments)
            {
                for (int k = 0; k < 2; k++)
                {
                    bool along = k == 0;
                    // Hauptstrecke: nur in Fahrtrichtung des Gleises (Rechtsverkehr)
                    if (s.IsLocked && !along) continue;
                    var node = along ? s.Start : s.End;
                    if (node.SegmentIds.Count < 2 && !s.IsLocked) continue; // freies Gleisende: kein Signal
                    float t = along ? Mathf.Min(0.999f, 6f / Mathf.Max(6f, s.Length)) : Mathf.Max(0.001f, 1f - 6f / Mathf.Max(6f, s.Length));
                    var p = s.Evaluate(t);
                    var r = s.Right(t) * (along ? 1f : -1f);
                    float h = s.HeightAt(t);
                    var basePt = SimConvert.ToWorld(p + r * 2.6f, h);
                    signalPosts.Add(Matrix4x4.TRS(basePt + Vector3.up * 2.2f, Quaternion.identity, new Vector3(0.18f, 4.4f, 0.18f)));
                    bool clear = rail.SignalClear(new EdgeKey(s.Id, along));
                    (clear ? signalGreen : signalRed).Add(Matrix4x4.TRS(basePt + Vector3.up * 4.3f, Quaternion.identity, new Vector3(0.45f, 0.45f, 0.45f)));
                }
            }
            Render(signalPosts, postMat);
            Render(signalRed, redMat);
            Render(signalGreen, greenMat);
        }

        void Render(List<Matrix4x4> list, Material m)
        {
            if (list.Count == 0 || m == null) return;
            var rp = new RenderParams(m) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            for (int start = 0; start < list.Count; start += buffer.Length)
            {
                int n = Mathf.Min(buffer.Length, list.Count - start);
                list.CopyTo(start, buffer, 0, n);
                Graphics.RenderMeshInstanced(rp, cube, 0, buffer, n);
            }
        }

        // ------------------------------------------------------------ Modelle / Grundkörper

        bool TryModel(string id, Transform parent, float width, float depth) => TryModel(id, parent, width, depth, out _);

        /// <summary>Eigenes Blender-Modell einsetzen (Paletten-Material, grobe Einheitenfehler korrigieren – wie TransitView).</summary>
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

        void Box(Transform parent, Vector3 localPos, Vector3 size, Material m, Quaternion rot)
        {
            var go = new GameObject("Box");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
        }

        /// <summary>Würfel an einer Weltposition (Gleis-Zubehör, Kind des Gleisobjekts).</summary>
        void Box(Transform parent, Vector3 worldPos, Vector3 size, Material m, Quaternion worldRot, bool world)
        {
            var go = new GameObject("Box");
            go.transform.SetParent(parent, true);
            go.transform.SetPositionAndRotation(worldPos, worldRot);
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            go.AddComponent<MeshRenderer>().sharedMaterial = m;
        }

        Material Mat(Color c)
        {
            if (materials.TryGetValue(c, out var m)) return m;
            m = new Material(baseMaterial) { name = "Rail_" + ColorUtility.ToHtmlStringRGB(c) };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            materials[c] = m;
            return m;
        }

        Material Instanced(Color c)
        {
            var m = new Material(baseMaterial) { name = "RailSignal_" + ColorUtility.ToHtmlStringRGB(c), enableInstancing = true };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            if (c.g > 0.9f || c.r > 0.9f)
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * 1.5f);
            }
            return m;
        }
    }
}
