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

        // Zuordnung Datei → Id (Versorger, Dienste inkl. Freizeit/Wahrzeichen, ÖPNV) steht in OwnModelDescriptions (Ordnerregeln);
        // eine Beschreibungsdatei Name.json neben dem FBX hat Vorrang (docs/Modell-Konvention.md).

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
            // Eigene Assets: auch Beschreibungsdateien (Name.json) lösen den Neubau aus
            bool relevant = imported.Concat(deleted).Concat(moved).Concat(movedFrom)
                .Any(p => p.StartsWith(ModelsRoot + "/") && (p.EndsWith(".fbx") || p.EndsWith(".png") || p.EndsWith(".json")));
            if (!relevant || scheduled) return;
            scheduled = true;
            // Eigener Katalog (Versorger/Dienste) + Asset-Katalog (Gebäude/Bäume/Laternen aus Models/Buildings, Nature, Props)
            EditorApplication.delayCall += () => { scheduled = false; Build(); AssetCatalogBuilder.Build(); };
        }

        // --- Eigene Assets: beim Öffnen des Editors neu bauen, wenn sich Modelle/Beschreibungsdateien seit dem letzten Bau
        // geändert haben (z. B. neue Exporte der Blender-KI, während Unity geschlossen war, oder ein veralteter Katalog im Git)
        [InitializeOnLoadMethod]
        static void RebuildIfStale()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                var catalog = AssetDatabase.LoadAssetAtPath<OwnModelCatalog>(CatalogPath);
                if (catalog != null && catalog.SourceStamp == SourceStamp()) return;
                Debug.Log("[OwnModels] Modelle/Beschreibungen geändert – Kataloge werden neu gebaut.");
                Build();
                AssetCatalogBuilder.Build();
            };
        }

        /// <summary>Stand der Quellen: alle FBX/JSON unter Models mit Größe und Änderungszeit.</summary>
        static string SourceStamp()
        {
            string root = Path.GetFullPath(ModelsRoot);
            if (!Directory.Exists(root)) return "";
            var files = Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f, System.StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder();
            foreach (var f in files)
            {
                var info = new FileInfo(f);
                sb.Append(f.Substring(root.Length)).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append('|');
            }
            unchecked
            {
                ulong h = 14695981039346656037UL;
                foreach (char c in sb.ToString()) { h ^= c; h *= 1099511628211UL; }
                return h.ToString("x16");
            }
        }
        // --- Ende Eigene Assets

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
            var all = OwnModelDescriptions.Scan();
            GameObject Load(OwnModelDescriptions.Desc d) => AssetDatabase.LoadAssetAtPath<GameObject>(d.Path);
            OwnModelCatalog.Entry Make(OwnModelDescriptions.Desc d, string id, GameObject model) => new OwnModelCatalog.Entry
            {
                Id = id, Model = model, Folder = d.Folder, Role = d.Role, Use = d.Use, Placement = d.Placement, Biome = d.Biome,
                Tags = d.Tags ?? new string[0], Size = Bounds(model).size,
            };
            void Use(OwnModelDescriptions.Desc d, string what) => d.UsedBy = d.UsedBy == null ? what : d.UsedBy + ", " + what;
            // Mit JSON beschriebene Modelle zuerst (Vorrang vor den Ordnerregeln), sonst nach Pfad
            var ordered = all.Where(d => d.Json != null).Concat(all.Where(d => d.Json == null)).ToList();

            // --- Versorger (G3)
            var entries = new List<OwnModelCatalog.Entry>();
            foreach (var d in ordered.Where(d => d.Role == OwnModelRoles.Facility))
            {
                if (!OwnModelDescriptions.IsFacilityId(d.Target)) { d.WaitsFor = $"unbekanntes Versorgungsbauwerk '{d.Target}'"; continue; }
                var dup = entries.FirstOrDefault(e => e.Id == d.Target);
                if (dup != null) { d.WaitsFor = $"doppelt – {d.Target} nutzt schon {dup.Model.name}"; continue; }
                var model = Load(d);
                if (model == null) continue;
                entries.Add(Make(d, d.Target, model));
                Use(d, $"Versorgung {d.Target}");
                Debug.Log($"[OwnModels] {d.Target} ← {OwnModelDescriptions.Key(d)}.fbx, Größe {Bounds(model).size}");
            }
            catalog.Facilities = entries.ToArray();

            // --- G4 Dienste inkl. Freizeit/Wahrzeichen: zuerst JSON („service“ = Dienst-Id oder Dienstart), dann die Ordnerregeln
            // (je Id der erste vorhandene Kandidat, z. B. PrimarySchool vor School)
            var services = new List<OwnModelCatalog.Entry>();
            foreach (var d in all.Where(d => d.Json != null && d.Role == OwnModelRoles.Service))
            {
                var ids = OwnModelDescriptions.ServiceIdsFor(d.Target).Where(id => services.All(e => e.Id != id)).ToList();
                if (ids.Count == 0) { d.WaitsFor = $"unbekannter oder schon belegter Dienst '{d.Target}'"; continue; }
                var model = Load(d);
                if (model == null) continue;
                foreach (var id in ids) services.Add(Make(d, id, model));
                Use(d, "Dienst " + string.Join(", ", ids));
            }
            foreach (var kv in OwnModelDescriptions.ServiceFiles)
            {
                if (services.Any(e => e.Id == kv.Key)) continue;
                foreach (var candidate in kv.Value)
                {
                    var d = all.FirstOrDefault(x => x.Json == null && x.Role == OwnModelRoles.Service && OwnModelDescriptions.Key(x) == candidate);
                    var model = d != null ? Load(d) : null;
                    if (model == null) continue;
                    services.Add(Make(d, kv.Key, model));
                    Use(d, "Dienst " + kv.Key);
                    Debug.Log($"[OwnModels] {kv.Key} ← {candidate}.fbx, Größe {Bounds(model).size}");
                    break;
                }
            }
            foreach (var d in all.Where(d => d.Role == OwnModelRoles.Service && d.UsedBy == null && d.WaitsFor == null))
                d.WaitsFor = "Dienst hat schon ein anderes Modell";
            catalog.Services = services.ToArray();

            // --- Fußgänger: Rolle person (Models/People/*.fbx), Id = Dateiname
            var people = new List<OwnModelCatalog.Entry>();
            foreach (var d in all.Where(d => d.Role == OwnModelRoles.Person))
            {
                var model = Load(d);
                if (model == null) continue;
                people.Add(Make(d, d.Name, model));
                Use(d, "Fußgänger");
            }
            people.Sort((x, y) => string.CompareOrdinal(x.Id, y.Id));
            catalog.People = people.ToArray();

            // --- ÖPNV (Depot, Haltestelle); fehlt das Modell, zeichnet TransitView Platzhalter
            var transport = new List<OwnModelCatalog.Entry>();
            foreach (var d in ordered.Where(d => d.Role == OwnModelRoles.Transport))
            {
                if (string.IsNullOrEmpty(d.Target) || transport.Any(e => e.Id == d.Target)) { d.WaitsFor = "ÖPNV-Id fehlt oder schon belegt"; continue; }
                var model = Load(d);
                if (model == null) continue;
                transport.Add(Make(d, d.Target, model));
                Use(d, $"ÖPNV {d.Target}");
            }
            catalog.Transport = transport.ToArray();

            // --- Eigene Assets: Landwirtschaft, Infrastruktur, Requisiten, Büsche, Felsen (Id = Dateiname)
            var extras = new List<OwnModelCatalog.Entry>();
            foreach (var d in ordered)
            {
                string usedBy = null;
                switch (d.Role)
                {
                    case OwnModelRoles.Agriculture:
                        if (OwnModelDescriptions.AgricultureUses.Contains(d.Use ?? "")) usedBy = $"Landwirtschaft ({d.Use}) – FarmView";
                        else d.WaitsFor = $"Landwirtschaft: Verwendung '{d.Use}' unbekannt";
                        break;
                    case OwnModelRoles.Infrastructure:
                        if (OwnModelDescriptions.InfrastructureUses.Contains(d.Use ?? "")) usedBy = $"Infrastruktur ({d.Use})";
                        else d.WaitsFor = $"Infrastruktur: Verwendung '{d.Use}' unbekannt";
                        break;
                    case OwnModelRoles.Prop: usedBy = $"Straßen-/Parkausstattung ({d.Placement}) – StreetFurnitureView"; break;
                    case OwnModelRoles.Bush: usedBy = "Büsche (Waldrand, Parks) – NatureDetailView"; break;
                    case OwnModelRoles.Rock: usedBy = "Felsen (Hänge, Gebirge) – NatureDetailView"; break;
                    default: continue;
                }
                if (usedBy == null) continue;
                if (extras.Any(e => e.Id == d.Name)) { d.WaitsFor = $"doppelter Dateiname {d.Name}"; continue; }
                var model = Load(d);
                if (model == null) continue;
                extras.Add(Make(d, d.Name, model));
                Use(d, usedBy);
            }
            extras.Sort((x, y) => string.CompareOrdinal(x.Id, y.Id));
            catalog.Extras = extras.ToArray();

            // --- Rollen des Asset-Katalogs (AssetCatalogBuilder): Gebäude je Zone, Bäume, Laternen
            foreach (var d in all)
            {
                if (d.Role == OwnModelRoles.Building)
                {
                    if (AssetCatalogBuilder.IsZoneName(d.Zone)) Use(d, $"Gebäude {d.Zone}{(d.Level > 0 ? $" Stufe {d.Level}" : "")} (AssetCatalog)");
                    else d.WaitsFor = $"Gebäude: Zone '{d.Zone}' unbekannt";
                }
                else if (d.Role == OwnModelRoles.Tree) Use(d, "Bäume (AssetCatalog, ScenicPropsView)");
                else if (d.Role == OwnModelRoles.StreetLamp) Use(d, "Straßenlaternen (AssetCatalog, ScenicPropsView)");
                else if (d.UsedBy == null && d.WaitsFor == null) d.WaitsFor = $"Rolle '{d.Role}' ohne Verwendung";
            }
            OwnModelDescriptions.WriteReport(all);

            catalog.SourceStamp = SourceStamp(); // Eigene Assets
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Debug.Log($"[OwnModels] Katalog: {entries.Count} Versorgungsbauwerke, {services.Count} Dienste, {transport.Count} ÖPNV, " +
                      $"{people.Count} Personen, {extras.Count} weitere → {CatalogPath}; ohne Verwendung: {all.Count(d => d.UsedBy == null)} (s. {OwnModelDescriptions.UnusedReport})");
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
