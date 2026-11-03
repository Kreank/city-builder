using System.Collections.Generic;
using System.IO;
using System.Linq;
using CityBuilder.Game.Assets;
using UnityEditor;
using UnityEngine;

namespace CityBuilder.Editor
{
    /// <summary>
    /// Baut den <see cref="AssetCatalog"/> aus den importierten Store-Prefabs:
    /// fasst jedes Modell zu einem Mesh zusammen, richtet es aus (Front +Z, Unterkante 0) und skaliert es.
    ///   Menü: City Builder → Third Party → Build Asset Catalog
    ///   Batch: -executeMethod CityBuilder.Editor.AssetCatalogBuilder.Build
    /// </summary>
    public static class AssetCatalogBuilder
    {
        const string Folder = ThirdPartySetup.Root + "/_Catalog/Resources";
        const string AssetPath = Folder + "/AssetCatalog.asset";

        enum Fit { Length, Height }

        sealed class Spec
        {
            public string Hint;      // Teil des Pfads (Paket/Ordner)
            public string Name;      // Dateiname ohne .prefab
            public Fit Fit;
            public float Target;     // Ziellänge bzw. -höhe in Metern
            public bool Vehicle;     // Längsachse auf +Z, Front erkennen
            public float MaxWidth = float.MaxValue; // Fahrzeuge dürfen nicht breiter als die Spur werden
        }

        static Spec V(string hint, string name, float length, float maxWidth = 2.3f) =>
            new Spec { Hint = hint, Name = name, Fit = Fit.Length, Target = length, Vehicle = true, MaxWidth = maxWidth };

        /// <summary>Vorschau-/Präsentationsteile in Store-Prefabs, die nicht zum Modell gehören.</summary>
        static readonly string[] ExcludedParts = { "Ground", "Railway", "Floor", "Plane", "Shadow", "Interior" };
        static Spec P(string hint, string name, float height) => new Spec { Hint = hint, Name = name, Fit = Fit.Height, Target = height };

        const string SimplePoly = "SimplePoly City - Low Poly Assets/Prefab/Vehicles/Vehicle with Static Wheels";
        const string Nebula = "Nebula - Free low poly car pack/Prefabs/Simple Vehicles";
        const string Vlad = "Street_Vehicles_Pack_Autobus_Tram/Prefabs";
        const string Colorful = "Triangularity/ColorfulCity/Prefabs";
        const string Nature = "Nature Pack - Low Polly Trees & Bushes/Prefabs";
        const string Modular = "ModularLowpolyStreetsFree/Prefabs/Other";

        static readonly Spec[] Cars =
        {
            V(SimplePoly, "Vehicle_Car_color01", 4.4f), V(SimplePoly, "Vehicle_Car_color02", 4.4f), V(SimplePoly, "Vehicle_Car_color03", 4.4f),
            V(SimplePoly, "Vehicle_SUV_color01", 4.7f), V(SimplePoly, "Vehicle_SUV_color02", 4.7f), V(SimplePoly, "Vehicle_SUV_color03", 4.7f),
            V(SimplePoly, "Vehicle_Taxi", 4.5f),
            V(SimplePoly, "Vehicle_Pick up Truck_color01", 5.2f), V(SimplePoly, "Vehicle_Pick up Truck_color02", 5.2f),
            V(Nebula, "Minivan", 4.8f), V(Nebula, "Muscle", 4.6f),
        };
        // SimplePoly-Lkw sind sehr gedrungen → werden auf Spurbreite begrenzt und damit kürzer (Lieferwagen-Größe)
        static readonly Spec[] Trucks =
        {
            V(Nebula, "LorryCargo", 10f, 2.6f), V(Nebula, "LorryCargo", 10f, 2.6f),
            V(SimplePoly, "Vehicle_Truck_color01", 9f, 2.6f), V(SimplePoly, "Vehicle_Truck_color02", 9f, 2.6f),
        };
        static readonly Spec[] Buses = { V(Vlad, "Autobus_Mesh", 12f, 2.6f) };
        static readonly Spec[] Trams = { V(Vlad, "Tram_Mesh", 29f, 2.65f) };
        static readonly Spec[] Lamps = { P(Colorful, "StreetLamp2_a_Single_LP_0", 7f), P(Colorful, "StreetLamp1_d_Single_LP_0", 7f) };
        static readonly Spec[] Lights = { P(Modular, "Pole_traffic_light", 4.8f), P(Modular, "Traffic_light", 4.8f) };
        static readonly Spec[] Trees =
        {
            P(Nature, "tree 1", 9f), P(Nature, "tree 2", 8f), P(Nature, "tree 3", 10f), P(Nature, "tree 4", 7f), P(Nature, "tree 5", 9f),
            P(Nature, "tree 6", 11f), P(Nature, "tree 7", 8f), P(Nature, "tree 8", 9f), P(Nature, "tree 9", 10f), P(Nature, "tree 10", 8f),
            P(Colorful, "Tree5_a_Pine_LP_0", 12f), P(Colorful, "Tree2_a_Spherical_LP_0", 8f), P(Colorful, "Tree9_a_SquarePine_LP_0", 11f),
        };

        [MenuItem("City Builder/Third Party/Build Asset Catalog")]
        public static void Build()
        {
            if (!AssetDatabase.IsValidFolder(ThirdPartySetup.Root)) { Debug.LogError("[Catalog] Assets/ThirdParty fehlt – erst Pakete importieren."); return; }
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            AssetDatabase.DeleteAsset(AssetPath);

            var catalog = ScriptableObject.CreateInstance<AssetCatalog>();
            AssetDatabase.CreateAsset(catalog, AssetPath);
            var allPrefabs = AssetDatabase.FindAssets("t:Prefab", new[] { ThirdPartySetup.Root })
                .Select(AssetDatabase.GUIDToAssetPath).ToList();

            catalog.Cars = BakeAll(catalog, allPrefabs, Cars);
            catalog.Trucks = BakeAll(catalog, allPrefabs, Trucks);
            catalog.Buses = BakeAll(catalog, allPrefabs, Buses);
            catalog.Trams = BakeAll(catalog, allPrefabs, Trams);
            catalog.StreetLamps = BakeAll(catalog, allPrefabs, Lamps);
            catalog.TrafficLights = BakeAll(catalog, allPrefabs, Lights).Take(1).ToArray();
            catalog.Trees = BakeAll(catalog, allPrefabs, Trees);

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            foreach (var group in new[] { catalog.Cars, catalog.Trucks, catalog.Buses, catalog.Trams, catalog.StreetLamps, catalog.TrafficLights, catalog.Trees })
            foreach (var e in group)
                Debug.Log($"[Catalog]   {e.Name}: {e.Size.x:0.0} × {e.Size.y:0.0} × {e.Size.z:0.0} m, {e.Mesh.vertexCount} Vertices, {e.Materials.Length} Materialien");
            Debug.Log($"[Catalog] Pkw {catalog.Cars.Length}, Lkw {catalog.Trucks.Length}, Bus {catalog.Buses.Length}, Tram {catalog.Trams.Length}, " +
                      $"Laternen {catalog.StreetLamps.Length}, Ampeln {catalog.TrafficLights.Length}, Bäume {catalog.Trees.Length} → {AssetPath}");
        }

        static ModelEntry[] BakeAll(AssetCatalog catalog, List<string> prefabs, Spec[] specs)
        {
            var result = new List<ModelEntry>();
            foreach (var spec in specs)
            {
                string path = prefabs.FirstOrDefault(p => p.Contains(spec.Hint) && Path.GetFileNameWithoutExtension(p) == spec.Name);
                if (path == null) { Debug.LogWarning($"[Catalog] nicht gefunden: {spec.Hint}/{spec.Name}"); continue; }
                var entry = Bake(AssetDatabase.LoadAssetAtPath<GameObject>(path), spec);
                if (entry == null) continue;
                AssetDatabase.AddObjectToAsset(entry.Mesh, catalog);
                result.Add(entry);
            }
            return result.ToArray();
        }

        static ModelEntry Bake(GameObject prefab, Spec spec)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                inst.transform.position = Vector3.zero;
                inst.transform.rotation = Quaternion.identity;

                // Nur die höchste Detailstufe verwenden
                IEnumerable<Renderer> renderers = inst.GetComponentsInChildren<MeshRenderer>(false);
                var lod = inst.GetComponentInChildren<LODGroup>();
                if (lod != null && lod.GetLODs().Length > 0) renderers = lod.GetLODs()[0].renderers.Where(r => r is MeshRenderer);

                var byMaterial = new Dictionary<Material, List<CombineInstance>>();
                var temporary = new List<Mesh>();
                var toLocal = inst.transform.worldToLocalMatrix;
                foreach (var r in renderers)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null || !r.enabled) continue;
                    if (ExcludedParts.Any(x => r.name.StartsWith(x, System.StringComparison.OrdinalIgnoreCase))) continue;
                    var matrix = toLocal * r.transform.localToWorldMatrix;
                    var source = mf.sharedMesh;
                    // Gespiegelte Teile (negative Skalierung): Dreiecke umdrehen, sonst sind sie innen nach außen gekehrt
                    if (matrix.determinant < 0f)
                    {
                        source = Object.Instantiate(source);
                        for (int sub = 0; sub < source.subMeshCount; sub++)
                        {
                            var tris = source.GetTriangles(sub);
                            for (int t = 0; t < tris.Length; t += 3) (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
                            source.SetTriangles(tris, sub);
                        }
                        temporary.Add(source);
                    }
                    var mats = r.sharedMaterials;
                    for (int sub = 0; sub < source.subMeshCount; sub++)
                    {
                        var mat = mats.Length > 0 ? mats[Mathf.Min(sub, mats.Length - 1)] : null;
                        if (mat == null) continue;
                        if (!byMaterial.TryGetValue(mat, out var list)) byMaterial[mat] = list = new List<CombineInstance>();
                        list.Add(new CombineInstance { mesh = source, subMeshIndex = sub, transform = matrix });
                    }
                }
                if (byMaterial.Count == 0) { Debug.LogWarning($"[Catalog] keine Meshes in {prefab.name}"); return null; }

                var parts = new List<CombineInstance>();
                var materials = new List<Material>();
                foreach (var kv in byMaterial)
                {
                    var part = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    part.CombineMeshes(kv.Value.ToArray(), true, true);
                    parts.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
                    materials.Add(kv.Key);
                }
                var mesh = new Mesh { name = spec.Name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(parts.ToArray(), false, false);
                foreach (var p in parts) Object.DestroyImmediate(p.mesh);
                foreach (var t in temporary) Object.DestroyImmediate(t);

                Normalize(mesh, spec);
                return new ModelEntry { Name = spec.Name, Mesh = mesh, Materials = materials.ToArray(), Size = mesh.bounds.size };
            }
            finally
            {
                Object.DestroyImmediate(inst);
            }
        }

        /// <summary>Längsachse → +Z, Front (niedrigeres Ende = Motorhaube) nach vorn, Größe, Unterkante auf 0.</summary>
        static void Normalize(Mesh mesh, Spec spec)
        {
            var v = mesh.vertices;
            var n = mesh.normals;
            var b = mesh.bounds;
            var rot = Quaternion.identity;

            if (spec.Vehicle)
            {
                if (b.size.x > b.size.z) rot = Quaternion.Euler(0f, 90f, 0f);
                Rotate(v, n, rot, out var rb);

                // Front erkennen: die Motorhaube ist niedriger als das Heck
                float len = rb.size.z;
                float frontY = 0f, backY = 0f; int fc = 0, bc = 0;
                foreach (var p in v)
                {
                    if (p.z > rb.max.z - len * 0.2f) { frontY = Mathf.Max(frontY, p.y); fc++; }
                    else if (p.z < rb.min.z + len * 0.2f) { backY = Mathf.Max(backY, p.y); bc++; }
                }
                if (fc > 0 && bc > 0 && frontY > backY * 1.08f) Rotate(v, n, Quaternion.Euler(0f, 180f, 0f), out _);
            }

            var bounds = Bounds(v);
            float current = spec.Fit == Fit.Length ? bounds.size.z : bounds.size.y;
            float s = current > 1e-4f ? spec.Target / current : 1f;
            if (bounds.size.x * s > spec.MaxWidth) s = spec.MaxWidth / bounds.size.x;
            var offset = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            for (int i = 0; i < v.Length; i++) v[i] = (v[i] + offset) * s;

            mesh.vertices = v;
            if (n.Length == v.Length) mesh.normals = n; else mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }

        static void Rotate(Vector3[] v, Vector3[] n, Quaternion q, out Bounds bounds)
        {
            for (int i = 0; i < v.Length; i++) v[i] = q * v[i];
            for (int i = 0; i < n.Length; i++) n[i] = q * n[i];
            bounds = Bounds(v);
        }

        static Bounds Bounds(Vector3[] v)
        {
            if (v.Length == 0) return new Bounds();
            var b = new Bounds(v[0], Vector3.zero);
            foreach (var p in v) b.Encapsulate(p);
            return b;
        }
    }
}
