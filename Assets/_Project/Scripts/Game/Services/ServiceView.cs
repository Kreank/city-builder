using System.Collections.Generic;
using CityBuilder.Simulation.Services;
using UnityEngine;

namespace CityBuilder.Game.Services
{
    /// <summary>
    /// Zeichnet Dienstgebäude: eigenes Blender-Modell aus dem <see cref="CityBuilder.Game.Assets.OwnModelCatalog"/>
    /// (FBX unter <c>Assets/_Project/Models/Services/&lt;Name&gt;.fbx</c>), sonst Platzhalter aus Grundkörpern.
    /// Dazu Müllsäcke an Gebäuden, an denen sich Müll staut (Menge aus der Simulation). Liest nur.
    /// </summary>
    public sealed class ServiceView : MonoBehaviour
    {
        SimulationRunner runner;
        Material baseMaterial, garbageMaterial;
        Mesh cube, cylinder, sphere;
        Transform root;
        readonly Dictionary<int, GameObject> objects = new Dictionary<int, GameObject>();
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        readonly List<Matrix4x4> garbageBags = new List<Matrix4x4>();
        readonly Matrix4x4[] buffer = new Matrix4x4[1023];
        int builtVersion = -1, garbageVersion = -1, garbageCity = -1;

        public void Init(SimulationRunner r, Material lit)
        {
            runner = r;
            baseMaterial = lit;
            r.WorldReplaced += () =>
            {
                // Speichern & Laden: Objekte gehören zu Ids der alten Welt
                foreach (var o in objects.Values) if (o != null) Destroy(o);
                objects.Clear();
                builtVersion = garbageVersion = garbageCity = -1;
            };
        }

        void Start()
        {
            cube = PrimitiveMesh(PrimitiveType.Cube);
            cylinder = PrimitiveMesh(PrimitiveType.Cylinder);
            sphere = PrimitiveMesh(PrimitiveType.Sphere);
            root = new GameObject("Service Facilities").transform;
            root.SetParent(transform, false);
            if (baseMaterial != null)
            {
                garbageMaterial = new Material(baseMaterial) { name = "ServiceGarbage", enableInstancing = true };
                var c = new Color(0.18f, 0.22f, 0.16f);
                if (garbageMaterial.HasProperty("_BaseColor")) garbageMaterial.SetColor("_BaseColor", c); else garbageMaterial.color = c;
            }
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
            if (garbageMaterial != null) Destroy(garbageMaterial);
        }

        void LateUpdate()
        {
            if (runner == null || runner.World == null || baseMaterial == null || cube == null) return;
            var s = runner.World.Services;
            if (s.StructureVersion != builtVersion)
            {
                builtVersion = s.StructureVersion;
                Sync(s);
            }
            if (s.CoverageVersion != garbageVersion || runner.World.City.Version != garbageCity)
            {
                garbageVersion = s.CoverageVersion;
                garbageCity = runner.World.City.Version;
                RebuildGarbage(s);
            }
            if (garbageBags.Count > 0 && garbageMaterial != null)
            {
                var rp = new RenderParams(garbageMaterial) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true };
                for (int start = 0; start < garbageBags.Count; start += buffer.Length)
                {
                    int n = Mathf.Min(buffer.Length, garbageBags.Count - start);
                    garbageBags.CopyTo(start, buffer, 0, n);
                    Graphics.RenderMeshInstanced(rp, sphere, 0, buffer, n);
                }
            }
        }

        void Sync(ServiceSystem s)
        {
            var alive = new HashSet<int>();
            foreach (var f in s.Facilities)
            {
                alive.Add(f.Id);
                if (!objects.ContainsKey(f.Id)) objects[f.Id] = Build(f);
            }
            var dead = new List<int>();
            foreach (var kv in objects) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (int id in dead)
            {
                if (objects[id] != null) Destroy(objects[id]);
                objects.Remove(id);
            }
        }

        /// <summary>Müllsäcke an der Straßenseite: einer je 25 % <see cref="ServiceSystem.GarbageLevel"/>.</summary>
        void RebuildGarbage(ServiceSystem s)
        {
            garbageBags.Clear();
            foreach (var b in runner.World.City.Buildings)
            {
                if (!b.IsConstructed) continue;
                int bags = Mathf.FloorToInt(s.GarbageLevel(b) * 4f + 0.5f);
                if (bags <= 0) continue;
                for (int i = 0; i < bags; i++)
                {
                    var p = b.Entrance - b.Front * 1.2f + b.Side * (b.Width * 0.5f - 1.2f - i * 1.1f);
                    garbageBags.Add(Matrix4x4.TRS(SimConvert.ToWorld(p, b.Height + 0.45f), Quaternion.identity, new Vector3(1f, 0.9f, 1f)));
                }
            }
        }

        // ------------------------------------------------------------ Gebäude

        GameObject Build(ServiceFacility f)
        {
            var t = new GameObject($"{f.Type.DisplayName} #{f.Id}").transform;
            t.SetParent(root, false);
            t.SetPositionAndRotation(SimConvert.ToWorld(f.Position, f.Height), Quaternion.LookRotation(new Vector3(f.Front.X, 0f, f.Front.Y)));
            if (TryBuildModel(f, t)) return t.gameObject;

            float w = f.Type.Width, d = f.Type.Depth;
            var paving = new Color(0.66f, 0.65f, 0.62f);
            var grass = new Color(0.33f, 0.58f, 0.27f);
            var white = new Color(0.92f, 0.92f, 0.9f);
            bool park = f.Type.Walking;
            Box(t, new Vector3(0f, 0.15f, 0f), new Vector3(w, 0.3f, d), park ? grass : paving);

            switch (f.Type.Id)
            {
                case "police_station":
                    Box(t, new Vector3(0f, 4f, -2f), new Vector3(w * 0.7f, 8f, d * 0.55f), new Color(0.82f, 0.84f, 0.88f));
                    Box(t, new Vector3(0f, 8.4f, -2f), new Vector3(w * 0.72f, 0.8f, d * 0.57f), new Color(0.12f, 0.25f, 0.6f));
                    Box(t, new Vector3(w * 0.3f, 1.2f, d * 0.32f), new Vector3(2f, 1.4f, 4.4f), new Color(0.15f, 0.3f, 0.7f)); // Streifenwagen
                    break;
                case "fire_station":
                    Box(t, new Vector3(-3f, 5f, -1f), new Vector3(w * 0.65f, 10f, d * 0.6f), new Color(0.75f, 0.2f, 0.15f));
                    for (int i = 0; i < 3; i++)
                        Box(t, new Vector3(-3f - w * 0.22f + i * w * 0.22f, 3f, -1f + d * 0.3f + 0.05f), new Vector3(w * 0.17f, 5.5f, 0.2f), white); // Tore
                    Box(t, new Vector3(w * 0.36f, 9f, -d * 0.2f), new Vector3(3.5f, 18f, 3.5f), new Color(0.7f, 0.18f, 0.14f)); // Schlauchturm
                    break;
                case "clinic":
                    Box(t, new Vector3(0f, 4.5f, -1f), new Vector3(w * 0.8f, 9f, d * 0.7f), white);
                    Box(t, new Vector3(0f, 9.4f, -1f), new Vector3(4f, 0.6f, 1.2f), new Color(0.85f, 0.1f, 0.1f)); // Kreuz
                    Box(t, new Vector3(0f, 9.4f, -1f), new Vector3(1.2f, 0.6f, 4f), new Color(0.85f, 0.1f, 0.1f));
                    break;
                case "hospital":
                    Box(t, new Vector3(0f, 6f, -3f), new Vector3(w * 0.85f, 12f, d * 0.5f), white);
                    Box(t, new Vector3(-w * 0.15f, 14f, -3f), new Vector3(w * 0.35f, 16f, d * 0.4f), new Color(0.88f, 0.9f, 0.93f));
                    Cyl(t, new Vector3(w * 0.25f, 12.3f, -3f), 4f, 0.4f, new Color(0.3f, 0.3f, 0.32f)); // Hubschrauberlandeplatz
                    Box(t, new Vector3(0f, 22.3f, -3f), new Vector3(5f, 0.6f, 1.4f), new Color(0.85f, 0.1f, 0.1f));
                    Box(t, new Vector3(0f, 22.3f, -3f), new Vector3(1.4f, 0.6f, 5f), new Color(0.85f, 0.1f, 0.1f));
                    break;
                case "school_primary":
                case "school_high":
                {
                    bool high = f.Type.Id == "school_high";
                    var brick = high ? new Color(0.62f, 0.38f, 0.3f) : new Color(0.9f, 0.75f, 0.45f);
                    Box(t, new Vector3(-w * 0.15f, high ? 6f : 4f, -d * 0.15f), new Vector3(w * 0.6f, high ? 12f : 8f, d * 0.45f), brick);
                    Box(t, new Vector3(w * 0.3f, 3f, -d * 0.15f), new Vector3(w * 0.3f, 6f, d * 0.3f), brick * 0.9f); // Turnhalle
                    Box(t, new Vector3(0f, 0.32f, d * 0.27f), new Vector3(w * 0.8f, 0.05f, d * 0.35f), new Color(0.55f, 0.5f, 0.45f)); // Pausenhof
                    break;
                }
                case "library":
                    Box(t, new Vector3(0f, 4f, -1f), new Vector3(w * 0.8f, 8f, d * 0.7f), new Color(0.85f, 0.82f, 0.74f));
                    for (int i = 0; i < 4; i++)
                        Cyl(t, new Vector3(-w * 0.3f + i * w * 0.2f, 3.5f, d * 0.27f), 0.5f, 7f, white); // Säulen
                    Box(t, new Vector3(0f, 8.5f, -1f), new Vector3(w * 0.85f, 1f, d * 0.75f), new Color(0.5f, 0.45f, 0.4f));
                    break;
                case "park_small":
                case "park_large":
                {
                    var tree = new Color(0.2f, 0.45f, 0.2f);
                    var trunk = new Color(0.4f, 0.28f, 0.18f);
                    Box(t, new Vector3(0f, 0.32f, 0f), new Vector3(2.2f, 0.05f, d * 0.95f), new Color(0.78f, 0.72f, 0.6f)); // Weg
                    Box(t, new Vector3(0f, 0.32f, 0f), new Vector3(w * 0.95f, 0.05f, 2.2f), new Color(0.78f, 0.72f, 0.6f));
                    int trees = f.Type.Id == "park_large" ? 14 : 4;
                    for (int i = 0; i < trees; i++)
                    {
                        // feste Streuung aus der Id (nur Darstellung)
                        float a = (i * 137.5f + f.Id * 31f) * Mathf.Deg2Rad;
                        float r = (0.25f + 0.2f * ((i * 7 + f.Id) % 5) / 4f) * Mathf.Min(w, d);
                        var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                        p.x = Mathf.Clamp(p.x, -w * 0.42f, w * 0.42f);
                        p.z = Mathf.Clamp(p.z, -d * 0.42f, d * 0.42f);
                        if (Mathf.Abs(p.x) < 2f) p.x += 3f;
                        if (Mathf.Abs(p.z) < 2f) p.z += 3f;
                        Cyl(t, p + new Vector3(0f, 1.5f, 0f), 0.3f, 3f, trunk);
                        Part(t, sphere, p + new Vector3(0f, 4.2f, 0f), Quaternion.identity, Vector3.one * 4f, tree);
                    }
                    if (f.Type.Id == "park_large") Cyl(t, new Vector3(w * 0.25f, 0.35f, -d * 0.25f), 5f, 0.1f, new Color(0.3f, 0.55f, 0.85f)); // Teich
                    break;
                }
                case "landfill":
                    Part(t, sphere, new Vector3(-w * 0.12f, 0f, -d * 0.1f), Quaternion.identity, new Vector3(w * 0.6f, 10f, d * 0.55f), new Color(0.42f, 0.38f, 0.3f));
                    Part(t, sphere, new Vector3(w * 0.2f, 0f, -d * 0.2f), Quaternion.identity, new Vector3(w * 0.35f, 6f, d * 0.3f), new Color(0.36f, 0.34f, 0.27f));
                    Box(t, new Vector3(w * 0.35f, 2f, d * 0.35f), new Vector3(6f, 4f, 5f), new Color(0.6f, 0.6f, 0.58f)); // Waage/Büro
                    break;
                case "incinerator":
                    Box(t, new Vector3(-2f, 7f, -1f), new Vector3(w * 0.6f, 14f, d * 0.6f), new Color(0.55f, 0.56f, 0.6f));
                    Cyl(t, new Vector3(w * 0.3f, 20f, -d * 0.25f), 1.8f, 40f, new Color(0.72f, 0.7f, 0.68f));
                    Cyl(t, new Vector3(w * 0.3f, 39f, -d * 0.25f), 1.9f, 2f, new Color(0.75f, 0.15f, 0.12f));
                    break;
                default:
                    Box(t, new Vector3(0f, 4f, 0f), new Vector3(w * 0.8f, 8f, d * 0.8f), paving);
                    break;
            }
            return t.gameObject;
        }

        /// <summary>Blender-Modell einsetzen (Paletten-Material, grobe Einheitenfehler korrigieren – wie bei der Versorgung).</summary>
        bool TryBuildModel(ServiceFacility f, Transform parent)
        {
            var catalog = CityBuilder.Game.Assets.OwnModelCatalog.Instance;
            var prefab = catalog != null ? catalog.Service(f.Type.Id) : null;
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
                float fit = Mathf.Min(f.Type.Width / Mathf.Max(0.01f, b.size.x), f.Type.Depth / Mathf.Max(0.01f, b.size.z));
                if (fit < 0.2f || fit > 5f) go.transform.localScale = Vector3.one * fit;
            }
            return true;
        }

        // ------------------------------------------------------------ Grundkörper

        void Box(Transform parent, Vector3 localPos, Vector3 size, Color c) => Part(parent, cube, localPos, Quaternion.identity, size, c);

        /// <summary>Zylinder mit Radius und Höhe (Unity-Zylinder ist 2 Einheiten hoch).</summary>
        void Cyl(Transform parent, Vector3 localCenter, float radius, float height, Color c) =>
            Part(parent, cylinder, localCenter, Quaternion.identity, new Vector3(radius * 2f, height * 0.5f, radius * 2f), c);

        void Part(Transform parent, Mesh mesh, Vector3 pos, Quaternion rot, Vector3 scale, Color c)
        {
            var go = new GameObject(mesh.name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat(c);
        }

        Material Mat(Color c)
        {
            if (materials.TryGetValue(c, out var m)) return m;
            m = new Material(baseMaterial) { name = "Service_" + ColorUtility.ToHtmlStringRGB(c) };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            materials[c] = m;
            return m;
        }
    }
}
