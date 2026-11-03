using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityBuilder.Editor
{
    /// <summary>
    /// Importiert die im Asset-Store-Cache heruntergeladenen Store-Pakete nacheinander ohne Dialog und führt danach
    /// „Organize And Convert“ und „Build Asset Catalog“ aus. Menü: City Builder → Third Party → Import Downloaded Store Packages.
    /// Die Warteschlange liegt im SessionState, damit sie Domain-Reloads zwischen den Paketen übersteht.
    /// </summary>
    [InitializeOnLoad]
    public static class StorePackageImporter
    {
        /// <summary>Dateinamen (Teilstring) der verwendeten Pakete im Cache (%APPDATA%\Unity\Asset Store-5.x).</summary>
        static readonly string[] PackageNames =
        {
            "SimplePoly City",
            "City Town Lowpoly Pack",
            "Colorful City",
            "Modular Lowpoly Streets",
            "Low Poly City Objects",
            "Road props for games",
            "Nature Pack",
            "Free low poly car pack",
            "StreetVehiclesPackAutobusTram",
        };

        const string QueueKey = "CityBuilder.StoreImport.Queue";
        const string FinishKey = "CityBuilder.StoreImport.Finish";

        static StorePackageImporter()
        {
            AssetDatabase.importPackageCompleted += _ => Continue();
            AssetDatabase.importPackageFailed += (name, error) => { Debug.LogError($"[StoreImport] {name}: {error}"); Continue(); };
            AssetDatabase.importPackageCancelled += _ => Continue();
            // Nach einem Domain-Reload mitten im Ablauf weitermachen
            if (!string.IsNullOrEmpty(SessionState.GetString(QueueKey, "")) || SessionState.GetBool(FinishKey, false))
                Continue();
        }

        static string CacheRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Unity", "Asset Store-5.x");

        [MenuItem("City Builder/Third Party/Import Downloaded Store Packages")]
        public static void ImportAll()
        {
            if (!Directory.Exists(CacheRoot)) { Debug.LogError($"[StoreImport] Cache nicht gefunden: {CacheRoot}"); return; }
            var files = Directory.GetFiles(CacheRoot, "*.unitypackage", SearchOption.AllDirectories);
            var queue = new List<string>();
            foreach (var name in PackageNames)
            {
                var file = files.FirstOrDefault(f => Path.GetFileName(f).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (file == null) Debug.LogWarning($"[StoreImport] nicht heruntergeladen: {name} (Package Manager → My Assets → Download)");
                else queue.Add(file);
            }
            if (queue.Count == 0) return;
            Debug.Log($"[StoreImport] importiere {queue.Count} Pakete …");
            SessionState.SetString(QueueKey, string.Join("|", queue));
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
            Debug.Log("[StoreImport] alle Pakete importiert → Organize And Convert, Build Asset Catalog");
            ThirdPartySetup.OrganizeAndConvert();
            AssetCatalogBuilder.Build();
            Debug.Log("[StoreImport] fertig.");
        }
    }
}
