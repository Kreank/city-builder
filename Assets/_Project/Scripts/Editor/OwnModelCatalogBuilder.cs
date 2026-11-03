using System.Collections.Generic;
using System.IO;
using System.Linq;
using CityBuilder.Game.Assets;
using UnityEditor;
using UnityEngine;

namespace CityBuilder.Editor
{
    /// <summary>
    /// Baut <see cref="OwnModelCatalog"/> (Assets/_Project/Resources/OwnModels.asset) aus den Blender-Exporten unter
    /// Assets/_Project/Models – automatisch, sobald dort ein Modell importiert wird, oder per Menü.
    /// Stellt außerdem die Paletten-Textur auf Punktfilter ohne Mipmaps (sonst laufen die Farbfelder ineinander)
    /// und legt das gemeinsame Paletten-Material an.
    /// </summary>
    public sealed class OwnModelCatalogBuilder : AssetPostprocessor
    {
        const string ModelsRoot = "Assets/_Project/Models";
        const string PalettePng = ModelsRoot + "/Palette/CB_Palette.png";
        const string PaletteMat = ModelsRoot + "/Palette/CB_Palette.mat";
        const string CatalogPath = "Assets/_Project/Resources/OwnModels.asset";

        /// <summary>Dateiname (ohne .fbx) unter Models/Utilities → Id des Versorgungsbauwerks.</summary>
        static readonly Dictionary<string, string> FacilityFiles = new Dictionary<string, string>
        {
            { "CoalPlant", "power_coal" }, { "GasPlant", "power_gas" }, { "WindTurbine", "power_wind" },
            { "SolarPark", "power_solar" }, { "WaterPump", "water_pump" }, { "WaterTower", "water_tower" },
        };

        /// <summary>G4: Dateiname (ohne .fbx) unter Models/Services → Id des Dienstgebäudes (<c>ServiceCatalog</c>).</summary>
        static readonly Dictionary<string, string> ServiceFiles = new Dictionary<string, string>
        {
            { "PoliceStation", "police_station" }, { "FireStation", "fire_station" }, { "Clinic", "clinic" }, { "Hospital", "hospital" },
            { "PrimarySchool", "school_primary" }, { "HighSchool", "school_high" }, { "Library", "library" },
            { "ParkSmall", "park_small" }, { "ParkLarge", "park_large" }, { "Landfill", "landfill" }, { "Incinerator", "incinerator" },
        };

        static bool scheduled;

        void OnPreprocessTexture()
        {
            if (assetPath != PalettePng) return;
            var ti = (TextureImporter)assetImporter;
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.wrapMode = TextureWrapMode.Clamp;
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool relevant = imported.Concat(deleted).Concat(moved)
                .Any(p => p.StartsWith(ModelsRoot + "/") && (p.EndsWith(".fbx") || p.EndsWith(".png")));
            if (!relevant || scheduled) return;
            scheduled = true;
            EditorApplication.delayCall += () => { scheduled = false; Build(); };
        }

        [MenuItem("City Builder/Build Own Model Catalog")]
        public static void Build()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<OwnModelCatalog>(CatalogPath);
            if (catalog == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CatalogPath));
                catalog = ScriptableObject.CreateInstance<OwnModelCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.Palette = EnsurePaletteMaterial();
            var entries = new List<OwnModelCatalog.Entry>();
            foreach (var kv in FacilityFiles)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsRoot}/Utilities/{kv.Key}.fbx");
                if (model == null) continue;
                entries.Add(new OwnModelCatalog.Entry { Id = kv.Value, Model = model });
                Debug.Log($"[OwnModels] {kv.Value} ← {kv.Key}.fbx, Größe {Bounds(model).size}");
            }
            catalog.Facilities = entries.ToArray();

            // --- G4 Dienste
            var services = new List<OwnModelCatalog.Entry>();
            foreach (var kv in ServiceFiles)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsRoot}/Services/{kv.Key}.fbx");
                if (model == null) continue;
                services.Add(new OwnModelCatalog.Entry { Id = kv.Value, Model = model });
                Debug.Log($"[OwnModels] {kv.Value} ← Services/{kv.Key}.fbx, Größe {Bounds(model).size}");
            }
            catalog.Services = services.ToArray();
            // --- Ende G4

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OwnModels] Katalog: {entries.Count} Versorgungsbauwerke, {services.Count} Dienste → {CatalogPath}");
        }

        static Material EnsurePaletteMaterial()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePng);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(PaletteMat);
            if (mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) { Debug.LogError("[OwnModels] URP/Lit nicht gefunden."); return null; }
                mat = new Material(shader) { name = "CB_Palette", enableInstancing = true };
                mat.SetFloat("_Smoothness", 0.15f);
                AssetDatabase.CreateAsset(mat, PaletteMat);
            }
            if (tex != null && mat.GetTexture("_BaseMap") != tex)
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetColor("_BaseColor", Color.white);
                EditorUtility.SetDirty(mat);
            }
            return mat;
        }

        static Bounds Bounds(GameObject model)
        {
            var b = new Bounds();
            bool any = false;
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                var m = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }
    }
}
