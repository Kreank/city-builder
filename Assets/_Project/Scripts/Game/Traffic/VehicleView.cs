using System.Collections.Generic;
using CityBuilder.Game.Assets;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Traffic;
using UnityEngine;

namespace CityBuilder.Game.Traffic
{
    /// <summary>
    /// Zeichnet alle Fahrzeuge als Platzhalter-Quader per GPU-Instancing (ohne GameObjects).
    /// Positionen werden zwischen zwei Simulationsticks interpoliert.
    /// </summary>
    public sealed class VehicleView : MonoBehaviour
    {
        const int Batch = 1023;

        [SerializeField] SimulationRunner runner;
        [Tooltip("Basis-Material (URP Lit) – Farben werden davon abgeleitet.")]
        [SerializeField] Material baseMaterial;

        static readonly Color[] CarColors =
        {
            new Color(0.80f, 0.12f, 0.10f), new Color(0.15f, 0.30f, 0.75f), new Color(0.90f, 0.90f, 0.88f),
            new Color(0.10f, 0.10f, 0.12f), new Color(0.55f, 0.57f, 0.60f), new Color(0.15f, 0.45f, 0.25f),
            new Color(0.95f, 0.70f, 0.15f), new Color(0.35f, 0.20f, 0.12f),
        };

        Mesh cube;
        readonly Dictionary<(VehicleClass, int), Material> materials = new Dictionary<(VehicleClass, int), Material>();
        readonly Dictionary<Material, List<Matrix4x4>> batches = new Dictionary<Material, List<Matrix4x4>>();
        readonly Matrix4x4[] buffer = new Matrix4x4[Batch];

        public bool Visible = true;

        void Start()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }

        void OnDestroy()
        {
            foreach (var m in materials.Values) if (m != null) Destroy(m);
        }

        Material MaterialFor(Vehicle v)
        {
            int variant = v.Class == VehicleClass.Car ? v.Variant % CarColors.Length : v.Variant % 2;
            var key = (v.Class, variant);
            if (materials.TryGetValue(key, out var mat)) return mat;

            Color c;
            switch (v.Class)
            {
                case VehicleClass.Truck: c = variant == 0 ? new Color(0.85f, 0.85f, 0.82f) : new Color(0.25f, 0.35f, 0.55f); break;
                case VehicleClass.Bus: c = new Color(0.98f, 0.82f, 0.10f); break;
                case VehicleClass.Tram: c = variant == 0 ? new Color(0.85f, 0.15f, 0.15f) : new Color(0.95f, 0.95f, 0.95f); break;
                default: c = CarColors[variant]; break;
            }
            mat = new Material(baseMaterial) { name = $"Vehicle_{v.Class}_{variant}", enableInstancing = true };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c); else mat.color = c;
            materials[key] = mat;
            return mat;
        }

        static Vector3 Size(VehicleClass c)
        {
            switch (c)
            {
                case VehicleClass.Truck: return new Vector3(2.5f, 3.4f, 11.5f);
                case VehicleClass.Bus: return new Vector3(2.55f, 3.1f, 11.8f);
                case VehicleClass.Tram: return new Vector3(2.4f, 3.3f, 29f);
                default: return new Vector3(1.8f, 1.45f, 4.3f);
            }
        }

        void LateUpdate()
        {
            if (!Visible || cube == null || runner == null || runner.World == null) return;
            var traffic = runner.World.Traffic;
            float alpha = runner.TickAlpha;

            foreach (var list in batches.Values) list.Clear();
            models.Clear();
            var catalog = AssetCatalog.Instance;
            foreach (var v in traffic.Vehicles)
            {
                if (v.Lane == null && v.Connection == null) continue;
                var p = System.Numerics.Vector2.Lerp(v.PrevPosition, v.Position, alpha);
                float h = Mathf.Lerp(v.PrevHeight, v.Height, alpha);
                var d = System.Numerics.Vector2.Lerp(v.PrevDirection, v.Direction, alpha);
                if (d.LengthSquared() < 1e-6f) d = v.Direction;
                var rot = Quaternion.LookRotation(new Vector3(d.X, 0f, d.Y));

                // Echtes Modell aus dem Katalog (Unterkante auf Fahrbahn, Front +Z)
                var model = catalog != null ? AssetCatalog.Pick(ModelsFor(catalog, v.Class), v.Variant) : null;
                if (model != null)
                {
                    var center = SimConvert.ToWorld(p, h + RoadMeshBuilder.RoadHeight);
                    // Kurze Tram-Wagen als Doppeltraktion, damit der Zug so lang ist wie in der Simulation
                    if (v.Class == VehicleClass.Tram && model.Size.z < v.Length * 0.7f)
                    {
                        var fwd = rot * Vector3.forward * (model.Size.z * 0.5f + 0.3f);
                        models.Add(model, Matrix4x4.TRS(center + fwd, rot, Vector3.one));
                        models.Add(model, Matrix4x4.TRS(center - fwd, rot * Quaternion.Euler(0f, 180f, 0f), Vector3.one));
                    }
                    else models.Add(model, Matrix4x4.TRS(center, rot, Vector3.one));
                    continue;
                }

                var size = Size(v.Class);
                var pos = SimConvert.ToWorld(p, h + RoadMeshBuilder.RoadHeight + size.y * 0.5f);
                var mat = MaterialFor(v);
                if (!batches.TryGetValue(mat, out var batch)) batches[mat] = batch = new List<Matrix4x4>();
                batch.Add(Matrix4x4.TRS(pos, rot, size));
            }

            foreach (var kv in batches)
            {
                var list = kv.Value;
                if (list.Count == 0) continue;
                var rp = new RenderParams(kv.Key) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true };
                for (int start = 0; start < list.Count; start += Batch)
                {
                    int n = Mathf.Min(Batch, list.Count - start);
                    list.CopyTo(start, buffer, 0, n);
                    Graphics.RenderMeshInstanced(rp, cube, 0, buffer, n);
                }
            }
            models.Render();
        }

        readonly InstancedModels models = new InstancedModels();

        static ModelEntry[] ModelsFor(AssetCatalog c, VehicleClass cls)
        {
            switch (cls)
            {
                case VehicleClass.Truck: return c.Trucks;
                case VehicleClass.Bus: return c.Buses;
                case VehicleClass.Tram: return c.Trams;
                default: return c.Cars;
            }
        }

        public void Init(SimulationRunner r, Material mat)
        {
            runner = r;
            baseMaterial = mat;
        }
    }
}
