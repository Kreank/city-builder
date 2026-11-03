using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CityBuilder.Editor
{
    /// <summary>
    /// Fenster „Store-Pakete“: zeigt alle im Asset-Store-Cache heruntergeladenen Pakete (der Cache gilt für ALLE
    /// Unity-Projekte), man hakt an, welche zu diesem Projekt gehören (gespeichert in <see cref="SelectionFile"/>, im Git),
    /// und importiert die noch nicht importierten ohne Dialog. Danach: Organize (neue Ordner → ThirdParty), Convert,
    /// Build Asset Catalog und eine Inventurliste aller Prefabs mit Maßen (<see cref="InventoryFile"/>) zum Einsortieren.
    /// Die Import-Warteschlange liegt im SessionState, damit sie Domain-Reloads zwischen den Paketen übersteht.
    /// </summary>
    [InitializeOnLoad]
    public sealed class StorePackageImporter : EditorWindow
    {
        /// <summary>Pakete dieses Projekts (Dateinamen ohne Pfad), eine Zeile je Paket – liegt im Git.</summary>
        const string SelectionFile = "Assets/_Project/Config/StorePackages.txt";
        /// <summary>Auf diesem Rechner bereits importierte Pakete (ThirdParty ist nicht im Git).</summary>
        const string ImportedFile = ThirdPartySetup.Root + "/_Catalog/ImportedPackages.txt";
        const string InventoryFile = ThirdPartySetup.Root + "/_Catalog/Inventory.txt";

        const string QueueKey = "CityBuilder.StoreImport.Queue";
        const string FinishKey = "CityBuilder.StoreImport.Finish";

        static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", "Asset Store-5.x");

        sealed class Entry
        {
            public string Path, File, Publisher;
            public long Bytes;
            public DateTime Downloaded;
            public bool Selected, Imported;
        }

        List<Entry> entries = new List<Entry>();
        Vector2 scroll;

        static StorePackageImporter()
        {
            AssetDatabase.importPackageCompleted += name => { MarkImported(name); Continue(); };
            AssetDatabase.importPackageFailed += (name, error) => { Debug.LogError($"[StoreImport] {name}: {error}"); Continue(); };
            AssetDatabase.importPackageCancelled += _ => Continue();
            // Nach einem Domain-Reload mitten im Ablauf weitermachen
            if (!string.IsNullOrEmpty(SessionState.GetString(QueueKey, "")) || SessionState.GetBool(FinishKey, false))
                Continue();
        }

        [MenuItem("City Builder/Third Party/Store-Pakete …")]
        public static void Open() => GetWindow<StorePackageImporter>("Store-Pakete").Refresh();

        void OnEnable() => Refresh();

        void Refresh()
        {
            var selected = ReadLines(SelectionFile);
            var imported = ReadLines(ImportedFile);
            entries = !Directory.Exists(CacheRoot) ? new List<Entry>() :
                Directory.GetFiles(CacheRoot, "*.unitypackage", SearchOption.AllDirectories)
                    .Select(p =>
                    {
                        var fi = new FileInfo(p);
                        var rel = p.Substring(CacheRoot.Length).TrimStart('\\', '/');
                        string file = fi.Name;
                        return new Entry
                        {
                            Path = p, File = file, Bytes = fi.Length, Downloaded = fi.LastWriteTime,
                            Publisher = rel.Split('\\', '/')[0],
                            Selected = selected.Contains(file), Imported = imported.Contains(file),
                        };
                    })
                    .OrderBy(e => e.Selected ? 0 : 1).ThenByDescending(e => e.Downloaded).ToList();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Der Download-Ordner gilt für alle Unity-Projekte. Hake an, was zu City Builder gehört – " +
                "die Auswahl wird in " + SelectionFile + " gespeichert. „Importieren“ holt alle angehakten, noch nicht importierten " +
                "Pakete, räumt sie nach ThirdParty und baut den Katalog neu. Neue Pakete müssen danach noch im Katalog " +
                "einsortiert werden (Inventurliste: " + InventoryFile + ").", MessageType.Info);
            if (!Directory.Exists(CacheRoot)) { EditorGUILayout.HelpBox("Cache nicht gefunden: " + CacheRoot, MessageType.Warning); return; }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var e in entries)
            {
                EditorGUILayout.BeginHorizontal();
                bool sel = EditorGUILayout.ToggleLeft(Path.GetFileNameWithoutExtension(e.File), e.Selected, GUILayout.MinWidth(260));
                if (sel != e.Selected) { e.Selected = sel; SaveSelection(); }
                GUILayout.Label(e.Publisher, EditorStyles.miniLabel, GUILayout.Width(140));
                GUILayout.Label($"{e.Bytes / 1048576f:0.#} MB", EditorStyles.miniLabel, GUILayout.Width(60));
                GUILayout.Label(e.Downloaded.ToString("dd.MM.yy"), EditorStyles.miniLabel, GUILayout.Width(60));
                GUILayout.Label(e.Imported ? "importiert" : e.Selected ? "wird importiert" : "–", EditorStyles.miniBoldLabel, GUILayout.Width(90));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            var todo = entries.Where(e => e.Selected && !e.Imported).ToList();
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(todo.Count == 0 || !string.IsNullOrEmpty(SessionState.GetString(QueueKey, ""))))
                if (GUILayout.Button($"Importieren und einsortieren ({todo.Count})", GUILayout.Height(28))) StartImport(todo.Select(t => t.Path).ToList());
            if (GUILayout.Button("Nur Katalog + Inventur neu bauen", GUILayout.Height(28))) Finish();
            if (GUILayout.Button("Aktualisieren", GUILayout.Width(100), GUILayout.Height(28))) Refresh();
            EditorGUILayout.EndHorizontal();
        }

        void SaveSelection()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SelectionFile));
            var lines = entries.Where(e => e.Selected).Select(e => e.File).OrderBy(s => s).ToArray();
            File.WriteAllLines(SelectionFile, lines, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(SelectionFile);
        }

        static void StartImport(List<string> paths)
        {
            Debug.Log($"[StoreImport] importiere {paths.Count} Pakete …");
            SessionState.SetString(QueueKey, string.Join("|", paths));
            SessionState.SetBool(FinishKey, true);
            Continue();
        }

        static void Continue() => EditorApplication.delayCall += Step;

        static void Step()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { Continue(); return; }
            var queue = SessionState.GetString(QueueKey, "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (queue.Count > 0)
            {
                var next = queue[0];
                queue.RemoveAt(0);
                SessionState.SetString(QueueKey, string.Join("|", queue));
                Debug.Log($"[StoreImport] {Path.GetFileName(next)}");
                AssetDatabase.ImportPackage(next, false); // Abschluss → importPackageCompleted → Continue
                return;
            }
            if (!SessionState.GetBool(FinishKey, false)) return;
            SessionState.SetBool(FinishKey, false);
            Finish();
        }

        static void Finish()
        {
            Debug.Log("[StoreImport] Organize And Convert, Build Asset Catalog, Inventur …");
            ThirdPartySetup.OrganizeAndConvert();
            AssetCatalogBuilder.Build();
            WriteInventory();
            Debug.Log("[StoreImport] fertig. Neue Pakete jetzt im Katalog einsortieren (Inventur: " + InventoryFile + ").");
            foreach (var w in Resources.FindObjectsOfTypeAll<StorePackageImporter>()) { w.Refresh(); w.Repaint(); }
        }

        /// <summary>Merkt importierte Pakete (Name ohne Endung, so meldet ihn Unity) anhand der Cache-Dateinamen.</summary>
        static void MarkImported(string packageName)
        {
            if (!Directory.Exists(CacheRoot)) return;
            var file = Directory.GetFiles(CacheRoot, "*.unitypackage", SearchOption.AllDirectories)
                .Select(Path.GetFileName)
                .FirstOrDefault(f => string.Equals(Path.GetFileNameWithoutExtension(f), packageName, StringComparison.OrdinalIgnoreCase));
            if (file == null) return;
            var set = ReadLines(ImportedFile);
            if (!set.Add(file)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ImportedFile));
            File.WriteAllLines(ImportedFile, set.OrderBy(s => s), new UTF8Encoding(false));
        }

        static HashSet<string> ReadLines(string path) =>
            File.Exists(path)
                ? new HashSet<string>(File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Alle Prefabs unter ThirdParty mit Maßen (Breite × Höhe × Tiefe in Prefab-Einheiten) – Grundlage fürs Einsortieren.</summary>
        [MenuItem("City Builder/Third Party/Write Inventory")]
        public static void WriteInventory()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Inventur der Store-Prefabs: Pfad | Breite x Höhe x Tiefe (Prefab-Einheiten) | Meshes");
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ThirdPartySetup.Root }).OrderBy(AssetDatabase.GUIDToAssetPath))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/_Catalog/")) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                bool any = false;
                var b = new Bounds();
                int meshes = 0;
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    meshes++;
                    var m = go.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    var mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        var p = m.MultiplyPoint3x4(c);
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                    }
                }
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (smr.sharedMesh != null) meshes++;
                var s = b.size;
                sb.AppendLine(any ? $"{path} | {s.x:0.##} x {s.y:0.##} x {s.z:0.##} | {meshes}" : $"{path} | – | {meshes}");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(InventoryFile));
            File.WriteAllText(InventoryFile, sb.ToString(), new UTF8Encoding(false));
            Debug.Log($"[StoreImport] Inventur geschrieben: {InventoryFile}");
        }
    }
}
