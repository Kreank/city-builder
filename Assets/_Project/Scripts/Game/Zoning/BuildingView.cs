using System.Collections.Generic;
using CityBuilder.Game.Assets;
using CityBuilder.Simulation.Zoning;
using UnityEngine;

namespace CityBuilder.Game.Zoning
{
    /// <summary>
    /// Zeichnet die Gebäude der Stadt: Store-Modell passend zur Zonenart, ans Grundstück angepasst
    /// und zur Straße gedreht; ohne Katalog farbige Platzhalter-Quader. Im Bau wachsen sie aus dem Boden.
    /// </summary>
    public sealed class BuildingView : MonoBehaviour
    {
        SimulationRunner runner;
        Material baseMaterial;
        Mesh cube;
        readonly InstancedModels models = new InstancedModels();
        readonly Dictionary<ZoneType, Material> placeholderMaterials = new Dictionary<ZoneType, Material>();
        readonly Dictionary<ZoneType, List<Matrix4x4>> placeholders = new Dictionary<ZoneType, List<Matrix4x4>>();
        readonly Matrix4x4[] buffer = new Matrix4x4[1023];
        int builtVersion = -1;
        bool anyUnderConstruction;

        public void Init(SimulationRunner r, Material lit)
        {
            runner = r;
            baseMaterial = lit;
        }

        void Start()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }

        void OnDestroy()
        {
            foreach (var m in placeholderMaterials.Values) if (m != null) Destroy(m);
        }

        void LateUpdate()
        {
            if (runner == null || runner.World == null || cube == null) return;
            var city = runner.World.City;
            // Neu befüllen, wenn sich Gebäude geändert haben (im Bau: laufend, damit sie wachsen)
            if (city.Version != builtVersion || anyUnderConstruction)
            {
                builtVersion = city.Version;
                Rebuild(city);
            }
            models.Render();
            foreach (var kv in placeholders)
            {
                if (kv.Value.Count == 0) continue;
                var rp = new RenderParams(PlaceholderMaterial(kv.Key)) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On, receiveShadows = true };
                for (int start = 0; start < kv.Value.Count; start += buffer.Length)
                {
                    int n = Mathf.Min(buffer.Length, kv.Value.Count - start);
                    kv.Value.CopyTo(start, buffer, 0, n);
                    Graphics.RenderMeshInstanced(rp, cube, 0, buffer, n);
                }
            }
        }

        void Rebuild(CitySystem city)
        {
            models.Clear();
            foreach (var l in placeholders.Values) l.Clear();
            anyUnderConstruction = false;
            var catalog = AssetCatalog.Instance;

            foreach (var b in city.Buildings)
            {
                if (!b.IsConstructed) anyUnderConstruction = true;
                float grow = Mathf.Clamp(b.Construction, 0.08f, 1f);
                var front = new Vector3(b.Front.X, 0f, b.Front.Y);
                var basePos = SimConvert.ToWorld(b.Position, b.Height);
                var model = catalog != null ? AssetCatalog.Pick(catalog.Buildings(b.Zone), b.Variant) : null;

                if (model != null)
                {
                    // Modell ans Grundstück anpassen: um 0° oder 90° gedreht, je nachdem was besser passt
                    float w = b.Width * 0.92f, d = b.Depth * 0.92f;
                    float s0 = Mathf.Min(w / model.Size.x, d / model.Size.z);
                    float s90 = Mathf.Min(w / model.Size.z, d / model.Size.x);
                    bool turn = s90 > s0 * 1.1f;
                    float s = Mathf.Min(turn ? s90 : s0, 1.6f);
                    var rot = Quaternion.LookRotation(front) * (turn ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity);
                    models.Add(model, Matrix4x4.TRS(basePos, rot, new Vector3(s, s * grow, s)));
                }
                else
                {
                    float h = PlaceholderHeight(b) * grow;
                    var size = new Vector3(b.Width * 0.85f, h, b.Depth * 0.85f);
                    var pos = basePos + Vector3.up * (h * 0.5f);
                    if (!placeholders.TryGetValue(b.Zone, out var list)) placeholders[b.Zone] = list = new List<Matrix4x4>();
                    list.Add(Matrix4x4.TRS(pos, Quaternion.LookRotation(front), size));
                }
            }
        }

        static float PlaceholderHeight(Building b)
        {
            float jitter = 0.8f + (b.Variant % 100) / 250f;
            switch (b.Zone)
            {
                case ZoneType.ResidentialLow: return 6f * jitter;
                case ZoneType.ResidentialMedium: return 14f * jitter;
                case ZoneType.ResidentialHigh: return 40f * jitter;
                case ZoneType.Mixed: return 16f * jitter;
                case ZoneType.Retail: return 7f * jitter;
                case ZoneType.Office: return 32f * jitter;
                default: return 10f * jitter;
            }
        }

        Material PlaceholderMaterial(ZoneType z)
        {
            if (placeholderMaterials.TryGetValue(z, out var m)) return m;
            m = new Material(baseMaterial) { name = "Building_" + z, enableInstancing = true };
            var c = Color.Lerp(ZoneView.ZoneColor(z), Color.white, 0.35f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            placeholderMaterials[z] = m;
            return m;
        }
    }
}
