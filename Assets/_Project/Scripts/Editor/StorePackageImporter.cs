using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CityBuilder.Editor
{
    /// <summary>
    /// Fenster „Store-Pakete“: zeigt alle im Asset-Store-Cache heruntergeladenen Pakete (der Cache gilt für ALLE
    /// Unity-Projekte), man hakt an, welche zu diesem Projekt gehören (gespeichert in <see cref="SelectionFile"/>, im Git),
    /// und importiert die fehlenden ohne Dialog. Danach: Organize (Paketordner → ThirdParty), Convert,
    /// Build Asset Catalog und eine Inventurliste aller Prefabs mit Maßen (<see cref="InventoryFile"/>) zum Einsortieren.
    ///
    /// Robust gegen Domain-Reloads: Ob ein Paket importiert ist, wird aus seiner Inhaltsliste geprüft (liegen seine
    /// Dateien im Projekt – am Originalort oder unter ThirdParty?), nicht aus Import-Rückmeldungen. Aus derselben
    /// Liste kommen die Paketordner, die Organize verschieben darf (<see cref="ThirdPartySetup.RecordedRootsFile"/>).
    /// </summary>
    [InitializeOnLoad]
    public sealed class StorePackageImporter : EditorWindow
    {
        /// <summary>Pakete dieses Projekts (Dateinamen ohne Pfad), eine Zeile je Paket – liegt im Git.</summary>
        const string SelectionFile = "Assets/_Project/Config/StorePackages.txt";
        const string InventoryFile = ThirdPartySetup.Root + "/_Catalog/Inventory.txt";
        /// <summary>Zwischenspeicher der Paket-Inhaltslisten (lokal, nicht im Git).</summary>
        const string IndexDir = "Library/CityBuilderPackageIndex";

        const string FinishKey = "CityBuilder.StoreImport.Finish";
        const string IdleSinceKey = "CityBuilder.StoreImport.IdleSince";
        /// <summary>So lange muss Unity nach dem Import ruhig sein (kein Import, kein Kompilieren), bevor einsortiert wird.</summary>
        const double IdleSeconds = 4.0;

        static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", "Asset Store-5.x");

        sealed class Entry
        {
            public string Path, File, Publisher;
            public long Bytes;
            public DateTime Downloaded;
            public bool Selected;
            public float ImportedShare = -1f; // -1 = noch nicht geprüft
            public bool Imported => ImportedShare >= 0.9f;
        }

        List<Entry> entries = new List<Entry>();
        Vector2 scroll;

        static StorePackageImporter()
        {
            EditorApplication.update += WatchFinish;
        }

        [MenuItem("City Builder/Third Party/Store-Pakete …")]
        public static void Open() => GetWindow<StorePackageImporter>("Store-Pakete").Refresh(true);

        void OnEnable() => Refresh(false);

        void Refresh(bool check)
        {
            var selected = ReadLines(SelectionFile);
            entries = !Directory.Exists(CacheRoot) ? new List<Entry>() :
                Directory.GetFiles(CacheRoot, "*.unitypackage", SearchOption.AllDirectories)
                    .Select(p =>
                    {
                        var fi = new FileInfo(p);
                        var rel = p.Substring(CacheRoot.Length).TrimStart('\\', '/');
                        return new Entry
                        {
                            Path = p, File = fi.Name, Bytes = fi.Length, Downloaded = fi.LastWriteTime,
                            Publisher = rel.Split('\\', '/')[0], Selected = selected.Contains(fi.Name),
                        };
                    })
                    .OrderBy(e => e.Selected ? 0 : 1).ThenByDescending(e => e.Downloaded).ToList();
            if (check) CheckImported(entries.Where(e => e.Selected).ToList());
        }

        void CheckImported(List<Entry> list)
        {
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Store-Pakete", "Prüfe " + list[i].File, (float)i / Math.Max(1, list.Count));
                    list[i].ImportedShare = ImportedShare(PackagePaths(list[i].Path));
                }
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Der Download-Ordner gilt für alle Unity-Projekte. Hake an, was zu City Builder gehört – " +
                "die Auswahl wird in " + SelectionFile + " gespeichert. „Importieren“ holt alle angehakten Pakete, deren Dateien " +
                "noch fehlen, räumt sie nach ThirdParty und baut den Katalog neu. Neue Pakete müssen danach noch im Katalog " +
                "einsortiert werden (Inventurliste: " + InventoryFile + ").", MessageType.Info);
            if (!Directory.Exists(CacheRoot)) { EditorGUILayout.HelpBox("Cache nicht gefunden: " + CacheRoot, MessageType.Warning); return; }
            if (SessionState.GetBool(FinishKey, false))
                EditorGUILayout.HelpBox("Import läuft – einsortiert wird automatisch, sobald Unity fertig ist.", MessageType.None);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var e in entries)
            {
                EditorGUILayout.BeginHorizontal();
                bool sel = EditorGUILayout.ToggleLeft(Path.GetFileNameWithoutExtension(e.File), e.Selected, GUILayout.MinWidth(260));
                if (sel != e.Selected) { e.Selected = sel; SaveSelection(); if (sel) CheckImported(new List<Entry> { e }); }
                GUILayout.Label(e.Publisher, EditorStyles.miniLabel, GUILayout.Width(140));
                GUILayout.Label($"{e.Bytes / 1048576f:0.#} MB", EditorStyles.miniLabel, GUILayout.Width(60));
                GUILayout.Label(e.Downloaded.ToString("dd.MM.yy"), EditorStyles.miniLabel, GUILayout.Width(60));
                string state = !e.Selected ? "–" : e.ImportedShare < 0 ? "?" : e.Imported ? "importiert" :
                    e.ImportedShare > 0.01f ? $"teilweise ({e.ImportedShare:P0})" : "fehlt";
                GUILayout.Label(state, EditorStyles.miniBoldLabel, GUILayout.Width(110));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            var todo = entries.Where(e => e.Selected && e.ImportedShare >= 0 && !e.Imported).ToList();
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(todo.Count == 0 || SessionState.GetBool(FinishKey, false)))
                if (GUILayout.Button($"Importieren und einsortieren ({todo.Count})", GUILayout.Height(28))) StartImport(todo);
            if (GUILayout.Button("Nur einsortieren + Katalog", GUILayout.Height(28))) Finish();
            if (GUILayout.Button("Prüfen", GUILayout.Width(80), GUILayout.Height(28))) Refresh(true);
            EditorGUILayout.EndHorizontal();
        }

        void SaveSelection()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SelectionFile));
            var lines = entries.Where(e => e.Selected).Select(e => e.File).OrderBy(s => s).ToArray();
            File.WriteAllLines(SelectionFile, lines, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(SelectionFile);
        }

        void StartImport(List<Entry> todo)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("Store-Pakete", "Bitte zuerst den Play-Modus beenden.", "OK");
                return;
            }
            // Paketordner schon vor dem Import festhalten – Organize verschiebt genau diese
            var roots = new HashSet<string>(ReadLines(ThirdPartySetup.RecordedRootsFile));
            foreach (var e in todo)
                foreach (var p in PackagePaths(e.Path))
                {
                    var parts = p.Split('/');
                    if (parts.Length >= 3 && parts[0] == "Assets" && parts[1] != "_Project" && parts[1] != "ThirdParty") roots.Add("Assets/" + parts[1]);
                }
            Directory.CreateDirectory(Path.GetDirectoryName(ThirdPartySetup.RecordedRootsFile));
            File.WriteAllLines(ThirdPartySetup.RecordedRootsFile, roots.OrderBy(s => s), new UTF8Encoding(false));

            Debug.Log($"[StoreImport] importiere {todo.Count} Pakete: {string.Join(", ", todo.Select(t => t.File))}");
            SessionState.SetBool(FinishKey, true);
            SessionState.SetString(IdleSinceKey, "");
            // Alle auf einmal übergeben – Unity arbeitet sie nacheinander ab (keine eigene Kette, die ein Reload abreißen kann)
#pragma warning disable CS0618 // AssetDatabase.ImportPackage ist in Unity 6.6 als veraltet markiert, funktioniert aber
            foreach (var e in todo) AssetDatabase.ImportPackage(e.Path, false);
#pragma warning restore CS0618
        }

        /// <summary>Wartet nach dem Import, bis Unity eine Weile ruhig ist, und sortiert dann ein (übersteht Domain-Reloads).</summary>
        static void WatchFinish()
        {
            if (!SessionState.GetBool(FinishKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetString(IdleSinceKey, "");
                return;
            }
            double now = EditorApplication.timeSinceStartup;
            var s = SessionState.GetString(IdleSinceKey, "");
            if (s == "" || !double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double since) || since > now)
            {
                SessionState.SetString(IdleSinceKey, now.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return;
            }
            if (now - since < IdleSeconds) return;
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
            foreach (var w in Resources.FindObjectsOfTypeAll<StorePackageImporter>()) { w.Refresh(true); w.Repaint(); }
        }

        // ------------------------------------------------------------ Paket-Inhaltslisten (.unitypackage = tar.gz)

        /// <summary>Zielpfade aller Dateien eines Pakets (aus den „pathname“-Einträgen), zwischengespeichert in Library/.</summary>
        static List<string> PackagePaths(string packageFile)
        {
            var fi = new FileInfo(packageFile);
            string key = $"{fi.Name}_{fi.Length}_{fi.LastWriteTimeUtc.Ticks}".Replace(' ', '_');
            string cache = Path.Combine(IndexDir, key + ".txt");
            if (File.Exists(cache)) return File.ReadAllLines(cache).ToList();

            var paths = new List<string>();
            try
            {
                using (var fs = File.OpenRead(packageFile))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                {
                    var header = new byte[512];
                    while (ReadFull(gz, header, 512))
                    {
                        if (header.All(b => b == 0)) break;
                        string name = Ascii(header, 0, 100);
                        string prefix = Ascii(header, 345, 155);
                        if (prefix.Length > 0) name = prefix + "/" + name;
                        long size = Convert.ToInt64(Ascii(header, 124, 12).Trim().TrimEnd('\0').Trim() is var o && o.Length > 0 ? o : "0", 8);
                        long padded = (size + 511) / 512 * 512;
                        if (name.EndsWith("/pathname") && size > 0 && size < 4096)
                        {
                            var data = new byte[padded];
                            if (!ReadFull(gz, data, (int)padded)) break;
                            var p = Encoding.UTF8.GetString(data, 0, (int)size).Split('\n')[0].Trim();
                            if (p.Length > 0) paths.Add(p);
                        }
                        else Skip(gz, padded);
                    }
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[StoreImport] Inhaltsliste von {fi.Name} nicht lesbar: {ex.Message}"); return paths; }
            Directory.CreateDirectory(IndexDir);
            File.WriteAllLines(cache, paths, new UTF8Encoding(false));
            return paths;
        }

        /// <summary>Anteil der Paketdateien, die im Projekt liegen (Originalort oder unter ThirdParty).</summary>
        static float ImportedShare(List<string> paths)
        {
            var files = paths.Where(p => Path.HasExtension(p)).ToList();
            if (files.Count == 0) return 0f;
            int found = 0;
            foreach (var p in files)
            {
                if (File.Exists(p)) { found++; continue; }
                if (p.StartsWith("Assets/") && File.Exists(ThirdPartySetup.Root + "/" + p.Substring("Assets/".Length))) found++;
            }
            return (float)found / files.Count;
        }

        static string Ascii(byte[] b, int offset, int count)
        {
            int end = offset;
            while (end < offset + count && b[end] != 0) end++;
            return Encoding.ASCII.GetString(b, offset, end - offset);
        }

        static bool ReadFull(Stream s, byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buffer, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        static void Skip(Stream s, long count)
        {
            var buf = new byte[81920];
            while (count > 0)
            {
                int n = s.Read(buf, 0, (int)Math.Min(buf.Length, count));
                if (n <= 0) return;
                count -= n;
            }
        }

        static HashSet<string> ReadLines(string path) =>
            File.Exists(path)
                ? new HashSet<string>(File.ReadAllLines(path).Select(l => l.Trim()).Where(l => l.Length > 0), StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // ------------------------------------------------------------ Inventur

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
