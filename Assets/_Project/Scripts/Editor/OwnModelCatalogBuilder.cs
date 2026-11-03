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
            // G3 erweitert: Atomkraftwerk; die Kläranlage liegt als Modell unter Models/Services (Schlüssel mit Ordner)
            { "NuclearPlant", "power_nuclear" }, { "Services/SewagePlant", "sewage_plant" },
        };

        /// <summary>G4: Id des Dienstgebäudes (<c>ServiceCatalog</c>) → Dateinamen unter Models/Services (erster vorhandener gilt).</summary>
        static readonly Dictionary<string, string[]> ServiceFiles = new Dictionary<string, string[]>
        {
            { "police_station", new[] { "PoliceStation" } }, { "fire_station", new[] { "FireStation" } },
            { "clinic", new[] { "Clinic" } }, { "hospital", new[] { "Hospital" } },
            { "school_primary", new[] { "PrimarySchool", "School" } }, { "school_high", new[] { "HighSchool", "School" } },
            { "library", new[] { "Library" } },
            { "park_small", new[] { "ParkSmall", "Park" } }, { "park_large", new[] { "ParkLarge", "Park" } },
            { "landfill", new[] { "Landfill" } }, { "incinerator", new[] { "Incinerator" } },
            // G4 erweitert: Medien
            { "media_newspaper", new[] { "Newspaper" } }, { "media_radio", new[] { "RadioStation" } }, { "media_tv", new[] { "TVTower" } },
        };

        /// <summary>ÖPNV: Id → Datei unter Models (mit Unterordner, ohne .fbx; erster vorhandener gilt).</summary>
        static readonly Dictionary<string, string[]> TransportFiles = new Dictionary<string, string[]>
        {
            { "bus_depot", new[] { "Transport/BusDepot" } },
            { "bus_stop", new[] { "Props/BusStop" } },
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
            // Eigener Katalog (Versorger/Dienste) + Asset-Katalog (Gebäude/Bäume/Laternen aus Models/Buildings, Nature, Props)
            EditorApplication.delayCall += () => { scheduled = false; Build(); AssetCatalogBuilder.Build(); };
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
                string file = kv.Key.Contains("/") ? kv.Key : "Utilities/" + kv.Key; // Schlüssel mit Ordner = anderer Unterordner
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsRoot}/{file}.fbx");
                if (model == null) continue;
                entries.Add(new OwnModelCatalog.Entry { Id = kv.Value, Model = model });
                Debug.Log($"[OwnModels] {kv.Value} ← {kv.Key}.fbx, Größe {Bounds(model).size}");
            }
            catalog.Facilities = entries.ToArray();

            // --- G4 Dienste
            var services = new List<OwnModelCatalog.Entry>();
            foreach (var kv in ServiceFiles)
            {
                GameObject model = null;
                string file = null;
                foreach (var candidate in kv.Value)
                {
                    model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsRoot}/Services/{candidate}.fbx");
                    if (model != null) { file = candidate; break; }
                }
                if (model == null) continue;
                services.Add(new OwnModelCatalog.Entry { Id = kv.Key, Model = model });
                Debug.Log($"[OwnModels] {kv.Key} ← Services/{file}.fbx, Größe {Bounds(model).size}");
            }
            catalog.Services = services.ToArray();
            // --- Ende G4

            // --- Fußgänger: alle FBX unter Models/People (Id = Dateiname, s. OwnModelCatalog.People)
            var people = new List<OwnModelCatalog.Entry>();
            if (AssetDatabase.IsValidFolder($"{ModelsRoot}/People"))
                foreach (string guid in AssetDatabase.FindAssets("t:GameObject", new[] { $"{ModelsRoot}/People" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (model == null) continue;
                    people.Add(new OwnModelCatalog.Entry { Id = Path.GetFileNameWithoutExtension(path), Model = model });
                    Debug.Log($"[OwnModels] Person {Path.GetFileNameWithoutExtension(path)} ← {path}, Größe {Bounds(model).size}");
                }
            people.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            catalog.People = people.ToArray();
            // --- Ende Fußgänger

            // --- ÖPNV (Depot, Haltestelle); fehlt das Modell, zeichnet TransitView Platzhalter
            var transport = new List<OwnModelCatalog.Entry>();
            foreach (var kv in TransportFiles)
                foreach (var candidate in kv.Value)
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelsRoot}/{candidate}.fbx");
                    if (model == null) continue;
                    transport.Add(new OwnModelCatalog.Entry { Id = kv.Key, Model = model });
                    Debug.Log($"[OwnModels] {kv.Key} ← {candidate}.fbx, Größe {Bounds(model).size}");
                    break;
                }
            catalog.Transport = transport.ToArray();
            // --- Ende ÖPNV

            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OwnModels] Katalog: {entries.Count} Versorgungsbauwerke, {services.Count} Dienste, {transport.Count} ÖPNV, {people.Count} Personen → {CatalogPath}");
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
