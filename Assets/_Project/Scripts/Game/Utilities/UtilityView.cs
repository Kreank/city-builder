using System.Collections.Generic;
using CityBuilder.Game.Environment;
using CityBuilder.Simulation.Utilities;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Utilities
{
    /// <summary>
    /// Zeichnet Versorger-Bauwerke als einfache Platzhalter aus Grundkörpern (Kraftwerkshallen, Schornsteine,
    /// Kühlturm, Windrad mit drehendem Rotor, Solarfelder, Pumpe, Wasserturm), Stromleitungen mit Masten (Standorte aus
    /// der Simulation) und durchhängenden Seilen sowie Wasserrohre (nur in der Unterirdisch-Ansicht sichtbar).
    /// </summary>
    public sealed class UtilityView : MonoBehaviour
    {
        SimulationRunner runner;
        Material baseMaterial, wireMaterial;
        GroundView ground;
        Mesh cube, cylinder, sphere;
        Transform facilityRoot, lineRoot, pipeRoot;
        readonly Dictionary<int, GameObject> facilityObjects = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> lineObjects = new Dictionary<int, GameObject>();
        /// <summary>Revision je Leitung (neu trassiert → neu zeichnen).</summary>
        readonly Dictionary<int, int> lineRevisions = new Dictionary<int, int>();
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        readonly List<Transform> rotors = new List<Transform>();
        int builtVersion = -1;

        public void Init(SimulationRunner r, Material lit, GroundView groundView)
        {
            runner = r;
            baseMaterial = lit;
            ground = groundView;
            r.WorldReplaced += () =>
            {
                // Speichern & Laden: Objekte gehören zu Ids der alten Welt
                foreach (var o in facilityObjects.Values) if (o != null) Destroy(o);
                foreach (var o in lineObjects.Values) if (o != null) Destroy(o);
                facilityObjects.Clear();
                lineObjects.Clear();
                lineRevisions.Clear();
                builtVersion = -1;
            };
        }

        void Start()
        {
            cube = PrimitiveMesh(PrimitiveType.Cube);
            cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
            sphere = PrimitiveMesh(PrimitiveType.Sphere);
            facilityRoot = NewRoot("Utility Facilities");
            lineRoot = NewRoot("Power Lines");
            pipeRoot = NewRoot("Water Pipes");
            wireMaterial = Mat(new Color(0.12f, 0.12f, 0.13f));
        }

        Transform NewRoot(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(transform, false);
            return t;
        }

        static Mesh PrimitiveMesh(PrimitiveType type)
        {
            var tmp = GameObject.CreatePrimitive(type);
            var mesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            return mesh;
        }

        void OnDestroy()
        {
            foreach (var m in materials.Values) if (m != null) Destroy(m);
        }

        void LateUpdate()
        {
            if (runner == null || runner.World == null || baseMaterial == null || cube == null) return;
            var u = runner.World.Utilities;
            if (u.StructureVersion != builtVersion)
            {
                builtVersion = u.StructureVersion;
                Sync(u);
            }
            if (pipeRoot != null) pipeRoot.gameObject.SetActive(ground != null && ground.Underground);
            // --- Abwasser (G3 erweitert): Hausanschlüsse (Zufahrt → Kanal), nur unterirdisch sichtbar
            if (u.StructureVersion != builtConnections || runner.World.City.Version != builtConnectionsCity)
            {
                builtConnections = u.StructureVersion;
                builtConnectionsCity = runner.World.City.Version;
                SyncHouseConnections(u);
            }
            if (connectionRoot != null) connectionRoot.gameObject.SetActive(ground != null && ground.Underground);
            // --- Ende
            // Nur Darstellung: Rotoren drehen sich in Verkehrszeit (begrenzt, Zeitmodell) – die Simulation kennt keine Rotorstellung
            float spin = Time.deltaTime * 60f * runner.AnimationTimeScale;
            foreach (var r in rotors) if (r != null) r.Rotate(0f, 0f, spin, Space.Self);
        }

        // --- Abwasser (G3 erweitert): Hausanschlüsse nach ConnectionRules (Zufahrt → nächster Punkt des Kanals)
        Transform connectionRoot;
        int builtConnections = -1, builtConnectionsCity = -1;

        void SyncHouseConnections(UtilitySystem u)
        {
            if (connectionRoot == null) connectionRoot = NewRoot("House Connections");
            for (int i = connectionRoot.childCount - 1; i >= 0; i--) Destroy(connectionRoot.GetChild(i).gameObject);
            var color = new Color(0.5f, 0.36f, 0.2f);
            float depth = UtilityTool.PipeDepth + 1.5f; // wie die Abwasserkanäle
            foreach (var b in runner.World.City.Buildings)
            {
                if (!b.IsConstructed) continue;
                var line = u.ConnectedLine(b, UtilityKind.Sewage);
                if (line == null || !ConnectionRules.HouseConnection(line, b, out var from, out var to)) continue;
                var a = SimConvert.ToWorld(from, Ground(from) - depth * 0.6f);
                var c = SimConvert.ToWorld(to, Ground(to) - depth);
                var delta = c - a;
                if (delta.sqrMagnitude < 1e-4f) continue;
                Part(connectionRoot, cylinder, (a + c) * 0.5f, Quaternion.FromToRotation(Vector3.up, delta.normalized),
                    new Vector3(0.5f, delta.magnitude * 0.5f, 0.5f), color, local: false);
            }
        }
        // --- Ende

        // ------------------------------------------------------------ Abgleich mit der Simulation

        void Sync(UtilitySystem u)
        {
            var aliveF = new HashSet<int>();
            foreach (var f in u.Facilities)
            {
                aliveF.Add(f.Id);
                if (!facilityObjects.ContainsKey(f.Id)) facilityObjects[f.Id] = BuildFacility(f);
            }
            RemoveDead(facilityObjects, aliveF);
            rotors.RemoveAll(r => r == null);

            var aliveL = new HashSet<int>();
            foreach (var l in u.Lines)
            {
                aliveL.Add(l.Id);
                if (lineObjects.TryGetValue(l.Id, out var old) && lineRevisions.TryGetValue(l.Id, out int rev) && rev != l.Revision)
                {
                    if (old != null) Destroy(old);
                    lineObjects.Remove(l.Id);
                }
                if (!lineObjects.ContainsKey(l.Id))
                {
                    lineObjects[l.Id] = l.Kind == UtilityKind.Power ? BuildPowerLine(l) : BuildPipe(l);
                    lineRevisions[l.Id] = l.Revision;
                }
            }
            RemoveDead(lineObjects, aliveL);
            var deadRevisions = new List<int>();
            foreach (int id in lineRevisions.Keys) if (!aliveL.Contains(id)) deadRevisions.Add(id);
            foreach (int id in deadRevisions) lineRevisions.Remove(id);
        }

        static void RemoveDead(Dictionary<int, GameObject> objects, HashSet<int> alive)
        {
            var dead = new List<int>();
            foreach (var kv in objects) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead)
            {
                if (objects[id] != null) Destroy(objects[id]);
                objects.Remove(id);
            }
        }

        float Ground(SimVector2 p) => runner.World.Terrain.HeightAt(p);

        // ------------------------------------------------------------ Bauwerke

        GameObject BuildFacility(UtilityFacility f)
        {
            var root = new GameObject($"{f.Type.DisplayName} #{f.Id}").transform;
            root.SetParent(facilityRoot, false);
            root.SetPositionAndRotation(SimConvert.ToWorld(f.Position, f.Height), Quaternion.LookRotation(new Vector3(f.Front.X, 0f, f.Front.Y)));

            // Eigenes Blender-Modell, falls vorhanden (OwnModelCatalog), sonst Platzhalter aus Grundkörpern
            if (TryBuildModel(f, root)) return root.gameObject;

            var concrete = new Color(0.62f, 0.61f, 0.58f);
            float w = f.Type.Width, d = f.Type.Depth;
            Box(root, new Vector3(0f, 0.15f, 0f), new Vector3(w, 0.3f, d), concrete * 0.9f);

            switch (f.Type.Id)
            {
                case "power_coal":
                    Box(root, new Vector3(-6f, 7f, 4f), new Vector3(28f, 14f, 18f), new Color(0.55f, 0.35f, 0.28f));
                    Box(root, new Vector3(13f, 11f, 4f), new Vector3(12f, 22f, 14f), new Color(0.5f, 0.5f, 0.52f));
                    Cyl(root, new Vector3(16f, 25f, -10f), 2.4f, 50f, new Color(0.7f, 0.68f, 0.66f));
                    Cyl(root, new Vector3(16f, 49f, -10f), 2.6f, 2f, new Color(0.75f, 0.15f, 0.12f));
                    Cyl(root, new Vector3(-13f, 9f, -8f), 9.5f, 18f, new Color(0.78f, 0.77f, 0.74f)); // Kühlturm
                    Part(root, sphere, new Vector3(4f, 1f, -11f), Quaternion.identity, new Vector3(12f, 4f, 9f), new Color(0.12f, 0.12f, 0.12f)); // Kohle
                    break;
                case "power_gas":
                    Box(root, new Vector3(-4f, 5f, 3f), new Vector3(22f, 10f, 14f), new Color(0.6f, 0.62f, 0.66f));
                    Cyl(root, new Vector3(10f, 15f, 6f), 1.6f, 30f, new Color(0.72f, 0.72f, 0.72f));
                    Part(root, sphere, new Vector3(-8f, 5f, -8f), Quaternion.identity, new Vector3(9f, 9f, 9f), new Color(0.9f, 0.9f, 0.88f));
                    Part(root, sphere, new Vector3(6f, 5f, -8f), Quaternion.identity, new Vector3(9f, 9f, 9f), new Color(0.9f, 0.9f, 0.88f));
                    break;
                case "power_wind":
                {
                    var white = new Color(0.93f, 0.93f, 0.93f);
                    Cyl(root, new Vector3(0f, 20f, 0f), 1.1f, 40f, white);
                    Box(root, new Vector3(0f, 40.5f, 0.8f), new Vector3(2.4f, 2.4f, 5f), white);
                    var rotor = new GameObject("Rotor").transform;
                    rotor.SetParent(root, false);
                    rotor.localPosition = new Vector3(0f, 40.5f, -2f);
                    for (int i = 0; i < 3; i++)
                    {
                        var blade = new GameObject("Blade").transform;
                        blade.SetParent(rotor, false);
                        blade.localRotation = Quaternion.Euler(0f, 0f, i * 120f);
                        Box(blade, new Vector3(0f, 9f, 0f), new Vector3(1.2f, 18f, 0.3f), white);
                    }
                    rotors.Add(rotor);
                    break;
                }
                case "power_solar":
                {
                    var panel = new Color(0.1f, 0.16f, 0.35f);
                    for (int row = 0; row < 5; row++)
                    for (int col = 0; col < 6; col++)
                    {
                        var pos = new Vector3(-w * 0.5f + 3.5f + col * (w - 7f) / 5f, 1.6f, -d * 0.5f + 3.5f + row * (d - 7f) / 4f);
                        Part(root, cube, pos, Quaternion.Euler(-25f, 0f, 0f), new Vector3(5.5f, 0.15f, 3.2f), panel);
                        Box(root, pos - new Vector3(0f, 0.9f, 0f), new Vector3(0.2f, 1.4f, 0.2f), concrete);
                    }
                    break;
                }
                case "water_pump":
                    Box(root, new Vector3(0f, 3f, -2f), new Vector3(10f, 6f, 8f), new Color(0.75f, 0.78f, 0.82f));
                    Cyl(root, new Vector3(0f, 2f, 4f), 2.5f, 4f, new Color(0.2f, 0.45f, 0.8f));
                    Part(root, cylinder, new Vector3(0f, 0.8f, 9f), Quaternion.Euler(90f, 0f, 0f), new Vector3(1.2f, 5f, 1.2f), new Color(0.3f, 0.35f, 0.4f));
                    break;
                case "water_tower":
                {
                    var steel = new Color(0.55f, 0.58f, 0.62f);
                    for (int i = 0; i < 4; i++)
                    {
                        float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                        Cyl(root, new Vector3(Mathf.Cos(a) * 3.2f, 8f, Mathf.Sin(a) * 3.2f), 0.35f, 16f, steel);
                    }
                    Cyl(root, new Vector3(0f, 19f, 0f), 4.5f, 6f, new Color(0.35f, 0.6f, 0.9f));
                    Part(root, sphere, new Vector3(0f, 22f, 0f), Quaternion.identity, new Vector3(9f, 2.5f, 9f), new Color(0.3f, 0.5f, 0.8f));
                    Box(root, new Vector3(4f, 1.2f, -4f), new Vector3(3f, 2.4f, 3f), concrete); // Brunnenhaus
                    break;
                }
                // --- Atom/Abwasser (G3 erweitert): Platzhalter, bis NuclearPlant/SewagePlant.fbx im Katalog sind
                case "power_nuclear":
                {
                    var tower = new Color(0.82f, 0.81f, 0.78f);
                    Cyl(root, new Vector3(-18f, 14f, 10f), 12f, 28f, tower); // Kühltürme
                    Cyl(root, new Vector3(6f, 14f, 10f), 12f, 28f, tower);
                    Part(root, sphere, new Vector3(16f, 10f, -12f), Quaternion.identity, new Vector3(18f, 20f, 18f), new Color(0.88f, 0.88f, 0.86f)); // Reaktorkuppel
                    Box(root, new Vector3(-10f, 6f, -14f), new Vector3(26f, 12f, 16f), new Color(0.6f, 0.62f, 0.66f));
                    break;
                }
                case "sewage_plant":
                {
                    var basin = new Color(0.35f, 0.32f, 0.22f);
                    for (int i = 0; i < 3; i++)
                        Cyl(root, new Vector3(-12f + i * 12f, 1.2f, 6f), 5f, 2.4f, basin); // Klärbecken
                    Box(root, new Vector3(0f, 3f, -9f), new Vector3(20f, 6f, 10f), new Color(0.7f, 0.72f, 0.7f));
                    break;
                }
                case "sewage_outlet":
                    Box(root, new Vector3(0f, 1f, 0f), new Vector3(4f, 2f, 4f), new Color(0.45f, 0.42f, 0.38f));
                    Part(root, cylinder, new Vector3(0f, 0.6f, 3.5f), Quaternion.Euler(90f, 0f, 0f), new Vector3(1.4f, 2.5f, 1.4f), new Color(0.4f, 0.3f, 0.2f));
                    break;
                // --- Ende
                default:
                    Box(root, new Vector3(0f, 5f, 0f), new Vector3(w * 0.8f, 10f, d * 0.8f), concrete);
                    break;
            }
            return root.gameObject;
        }

        /// <summary>
        /// Setzt das Blender-Modell des Bauwerks ein: gemeinsames Paletten-Material, Kindobjekt „Rotor“ dreht sich.
        /// Weicht die Grundfläche des Modells um mehr als Faktor 5 vom Katalog ab (falsche Import-Einheit), wird es
        /// gleichmäßig auf die Grundfläche skaliert.
        /// </summary>
        bool TryBuildModel(UtilityFacility f, Transform root)
        {
            var catalog = CityBuilder.Game.Assets.OwnModelCatalog.Instance;
            var prefab = catalog != null ? catalog.Facility(f.Type.Id) : null;
            if (prefab == null) return false;

            var go = Instantiate(prefab, root, false);
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

            // Grundfläche prüfen (lokal zum Bauwerk)
            bool any = false;
            var b = new Bounds();
            foreach (var r in renderers)
            {
                var rb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = rb.center + Vector3.Scale(rb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = root.InverseTransformPoint(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (any)
            {
                // Nur grobe Einheitenfehler (cm statt m o. ä.) korrigieren – überstehende Teile wie Rotorblätter
                // sind gewollt breiter als die Grundfläche (Windrad: 32 m Rotor auf 12 m Grundfläche).
                float fit = Mathf.Min(f.Type.Width / Mathf.Max(0.01f, b.size.x), f.Type.Depth / Mathf.Max(0.01f, b.size.z));
                if (fit < 0.2f || fit > 5f) go.transform.localScale = Vector3.one * fit;
            }

            var rotor = FindChild(go.transform, "Rotor");
            if (rotor != null) rotors.Add(rotor);
            return true;
        }

        static Transform FindChild(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindChild(c, name);
                if (r != null) return r;
            }
            return null;
        }

        // ------------------------------------------------------------ Stromleitungen: Masten + Seile

        /// <summary>
        /// Masten an den Standorten aus der Simulation (<see cref="UtilityLine.Poles"/> – nie auf Straße/Wasser),
        /// Seile mit Durchhang von Mast zu Mast; liegt ein Leitungsende abseits des Endmasts (Anschluss auf
        /// Straße/Bauwerk), führt ein schräges Anschlussseil dorthin.
        /// </summary>
        GameObject BuildPowerLine(UtilityLine l)
        {
            var root = new GameObject($"Stromleitung #{l.Id}").transform;
            root.SetParent(lineRoot, false);
            var dir = l.B - l.A;
            var forward = new Vector3(dir.X, 0f, dir.Y).normalized;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            var rot = Quaternion.LookRotation(forward);
            var right = rot * Vector3.right;
            var steel = new Color(0.45f, 0.46f, 0.48f);
            var concrete = new Color(0.6f, 0.59f, 0.56f);
            float h = UtilityTool.WireHeight;
            var poles = l.Poles;
            var tops = new Vector3[poles.Count];

            for (int i = 0; i < poles.Count; i++)
            {
                var p = poles[i];
                var basePos = SimConvert.ToWorld(p, Mathf.Max(Ground(p), runner.World.Terrain.WaterLevel));
                var pylon = new GameObject("Mast").transform;
                pylon.SetParent(root, false);
                pylon.SetPositionAndRotation(basePos, rot);
                // Fundament = belegte Grundfläche (Platzverbrauch sichtbar machen)
                float foot = UtilitySystem.PoleSize;
                Box(pylon, new Vector3(0f, 0.15f, 0f), new Vector3(foot, 0.3f, foot), concrete);
                Box(pylon, new Vector3(0f, h * 0.5f, 0f), new Vector3(0.6f, h, 0.6f), steel);
                Box(pylon, new Vector3(0f, h - 0.6f, 0f), new Vector3(6f, 0.35f, 0.35f), steel);
                tops[i] = basePos + Vector3.up * (h - 0.8f);
            }
            if (poles.Count == 0) return root.gameObject;

            // Anschlusspunkte der Enden (auf Straße/Bauwerk/Gebäude) – tiefer als die Mastspitze
            Vector3 EndPoint(SimVector2 p) => SimConvert.ToWorld(p, Mathf.Max(Ground(p), runner.World.Terrain.WaterLevel) + h * 0.5f);
            bool startDrop = SimVector2.Distance(l.A, poles[0]) > 1f;
            bool endDrop = SimVector2.Distance(l.B, poles[poles.Count - 1]) > 1f;

            // Zwei Seile links/rechts am Querträger, mit Durchhang
            foreach (float side in new[] { -2.6f, 2.6f })
            {
                var points = new List<Vector3>();
                if (startDrop) points.Add(EndPoint(l.A));
                for (int i = 0; i < tops.Length; i++) points.Add(tops[i] + right * side);
                if (endDrop) points.Add(EndPoint(l.B));
                if (points.Count < 2) continue;

                var go = new GameObject("Seil");
                go.transform.SetParent(root, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = wireMaterial;
                lr.useWorldSpace = true;
                lr.widthMultiplier = 0.12f;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                const int perSpan = 8;
                lr.positionCount = (points.Count - 1) * perSpan + 1;
                int k = 0;
                for (int s = 0; s < points.Count - 1; s++)
                {
                    var a = points[s];
                    var b = points[s + 1];
                    float sag = Vector3.Distance(a, b) * 0.03f;
                    for (int j = 0; j < perSpan; j++)
                    {
                        float t = j / (float)perSpan;
                        lr.SetPosition(k++, Vector3.Lerp(a, b, t) + Vector3.down * (sag * 4f * t * (1f - t)));
                    }
                }
                lr.SetPosition(k, points[points.Count - 1]);
            }
            return root.gameObject;
        }

        // ------------------------------------------------------------ Wasserrohre (unterirdisch)

        GameObject BuildPipe(UtilityLine l)
        {
            var root = new GameObject($"{UtilityCatalog.LineName(l.Kind)} #{l.Id}").transform;
            root.SetParent(pipeRoot, false);
            var color = l.Kind == UtilityKind.Sewage ? new Color(0.5f, 0.36f, 0.2f) : new Color(0.2f, 0.5f, 0.95f); // Abwasser braun
            int n = Mathf.Max(1, Mathf.CeilToInt(l.Length / 8f));
            Vector3 Point(int i)
            {
                var p = SimVector2.Lerp(l.A, l.B, i / (float)n);
                return SimConvert.ToWorld(p, Ground(p) - UtilityTool.PipeDepth - (l.Kind == UtilityKind.Sewage ? 1.5f : 0f)); // Kanal tiefer als Wasser
            }
            var prev = Point(0);
            for (int i = 1; i <= n; i++)
            {
                var next = Point(i);
                var mid = (prev + next) * 0.5f;
                var delta = next - prev;
                if (delta.sqrMagnitude > 1e-4f)
                    Part(root, cylinder, mid, Quaternion.FromToRotation(Vector3.up, delta.normalized),
                        new Vector3(1.2f, delta.magnitude * 0.5f + 0.1f, 1.2f), color, local: false);
                prev = next;
            }
            return root.gameObject;
        }

        // ------------------------------------------------------------ Grundkörper

        void Box(Transform parent, Vector3 localPos, Vector3 size, Color c) => Part(parent, cube, localPos, Quaternion.identity, size, c);

        /// <summary>Zylinder mit Radius und Höhe (Unity-Zylinder ist 2 Einheiten hoch).</summary>
        void Cyl(Transform parent, Vector3 localCenter, float radius, float height, Color c) =>
            Part(parent, cylinder, localCenter, Quaternion.identity, new Vector3(radius * 2f, height * 0.5f, radius * 2f), c);

        void Part(Transform parent, Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, Color c, bool local = true)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            if (local) { go.transform.localPosition = pos; go.transform.localRotation = rot; }
            else go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Mat(c);
        }

        Material Mat(Color c)
        {
            if (materials.TryGetValue(c, out var m)) return m;
            m = new Material(baseMaterial) { name = "Utility_" + ColorUtility.ToHtmlStringRGB(c) };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            materials[c] = m;
            return m;
        }
    }
}
