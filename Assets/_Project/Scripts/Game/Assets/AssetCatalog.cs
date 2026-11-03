using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBuilder.Game.Assets
{
    /// <summary>
    /// Ein aufbereitetes Modell: alle Teile zu einem Mesh zusammengefasst (ein Submesh je Material),
    /// ausgerichtet (Front +Z, Unterkante auf 0, mittig) und auf reale Größe skaliert.
    /// </summary>
    [Serializable]
    public sealed class ModelEntry
    {
        public string Name;
        public Mesh Mesh;
        public Material[] Materials;
        public Vector3 Size;
    }

    /// <summary>
    /// Katalog der Store-Modelle, die das Spiel benutzt. Wird vom Editor-Befehl
    /// „City Builder → Third Party → Build Asset Catalog“ in Assets/ThirdParty/_Catalog/Resources erzeugt
    /// (nicht im Git). Fehlt er, zeigt das Spiel Platzhalter.
    /// </summary>
    public sealed class AssetCatalog : ScriptableObject
    {
        public ModelEntry[] Cars = new ModelEntry[0];
        public ModelEntry[] Trucks = new ModelEntry[0];
        public ModelEntry[] Buses = new ModelEntry[0];
        public ModelEntry[] Trams = new ModelEntry[0];
        public ModelEntry[] StreetLamps = new ModelEntry[0];
        public ModelEntry[] TrafficLights = new ModelEntry[0];
        public ModelEntry[] Trees = new ModelEntry[0];

        // Gebäude je Zonenart (Grundfläche normiert, beim Zeichnen ans Grundstück angepasst)
        public ModelEntry[] ResidentialLow = new ModelEntry[0];
        public ModelEntry[] ResidentialMedium = new ModelEntry[0];
        public ModelEntry[] ResidentialHigh = new ModelEntry[0];
        public ModelEntry[] Mixed = new ModelEntry[0];
        public ModelEntry[] Retail = new ModelEntry[0];
        public ModelEntry[] Office = new ModelEntry[0];
        public ModelEntry[] Industry = new ModelEntry[0];

        public ModelEntry[] Buildings(CityBuilder.Simulation.Zoning.ZoneType zone)
        {
            switch (zone)
            {
                case CityBuilder.Simulation.Zoning.ZoneType.ResidentialLow: return ResidentialLow;
                case CityBuilder.Simulation.Zoning.ZoneType.ResidentialMedium: return ResidentialMedium;
                case CityBuilder.Simulation.Zoning.ZoneType.ResidentialHigh: return ResidentialHigh;
                case CityBuilder.Simulation.Zoning.ZoneType.Mixed: return Mixed;
                case CityBuilder.Simulation.Zoning.ZoneType.Retail: return Retail;
                case CityBuilder.Simulation.Zoning.ZoneType.Office: return Office;
                case CityBuilder.Simulation.Zoning.ZoneType.Industry: return Industry;
                default: return null;
            }
        }

        static AssetCatalog cached;
        static bool loaded;

        public static AssetCatalog Instance
        {
            get
            {
                if (!loaded)
                {
                    cached = Resources.Load<AssetCatalog>("AssetCatalog");
                    loaded = true;
                }
                return cached;
            }
        }

        public static bool Has(ModelEntry[] list) => list != null && list.Length > 0 && list[0] != null && list[0].Mesh != null;

        public static ModelEntry Pick(ModelEntry[] list, int variant) =>
            Has(list) ? list[(variant % list.Length + list.Length) % list.Length] : null;
    }

    /// <summary>
    /// Zeichnet viele Kopien von Katalog-Modellen per GPU-Instancing (ohne GameObjects).
    /// Entweder jedes Frame neu befüllen (Fahrzeuge) oder einmal befüllen und nur zeichnen (Laternen, Bäume).
    /// </summary>
    public sealed class InstancedModels
    {
        const int Batch = 1023;
        readonly Dictionary<ModelEntry, List<Matrix4x4>> instances = new Dictionary<ModelEntry, List<Matrix4x4>>();
        static readonly Dictionary<Material, Material> instancingCopies = new Dictionary<Material, Material>();
        readonly Matrix4x4[] buffer = new Matrix4x4[Batch];

        public ShadowCastingMode Shadows = ShadowCastingMode.On;

        public void Clear()
        {
            foreach (var l in instances.Values) l.Clear();
        }

        public void Add(ModelEntry model, Matrix4x4 matrix)
        {
            if (!instances.TryGetValue(model, out var list)) instances[model] = list = new List<Matrix4x4>();
            list.Add(matrix);
        }

        public int Count
        {
            get { int n = 0; foreach (var l in instances.Values) n += l.Count; return n; }
        }

        public void Render()
        {
            foreach (var kv in instances)
            {
                var model = kv.Key;
                var list = kv.Value;
                if (list.Count == 0 || model.Mesh == null) continue;
                int subs = model.Mesh.subMeshCount;
                for (int sub = 0; sub < subs; sub++)
                {
                    var mat = Instanced(model.Materials != null && model.Materials.Length > 0
                        ? model.Materials[Mathf.Min(sub, model.Materials.Length - 1)] : null);
                    if (mat == null) continue;
                    var rp = new RenderParams(mat) { shadowCastingMode = Shadows, receiveShadows = true };
                    for (int start = 0; start < list.Count; start += Batch)
                    {
                        int n = Mathf.Min(Batch, list.Count - start);
                        list.CopyTo(start, buffer, 0, n);
                        Graphics.RenderMeshInstanced(rp, model.Mesh, sub, buffer, n);
                    }
                }
            }
        }

        /// <summary>Instancing braucht Materialien mit aktiviertem GPU-Instancing → bei Bedarf eine Kopie.</summary>
        static Material Instanced(Material m)
        {
            if (m == null || m.enableInstancing) return m;
            if (!instancingCopies.TryGetValue(m, out var copy))
            {
                copy = new Material(m) { enableInstancing = true, name = m.name + " (Instanced)" };
                instancingCopies[m] = copy;
            }
            return copy;
        }
    }
}
