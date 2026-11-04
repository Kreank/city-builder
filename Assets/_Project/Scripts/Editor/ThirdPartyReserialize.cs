using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityBuilder.Editor
{
    /// <summary>
    /// Alte Store-Pakete (vor Unity 2019.1) melden bei jedem Import „Serialized files [version 9] … are deprecated“.
    /// Dieses Menü speichert alle Prefabs, Materialien und Szenen unter Assets/ThirdParty im aktuellen Format neu –
    /// danach ist die Warnung dauerhaft weg. Ändert keinen Inhalt.
    /// </summary>
    public static class ThirdPartyReserialize
    {
        [MenuItem("City Builder/Third Party/Pakete neu speichern (alte Formate)")]
        public static void Run()
        {
            var paths = AssetDatabase.FindAssets("t:Prefab t:Material t:Scene t:ScriptableObject", new[] { "Assets/ThirdParty" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => !p.StartsWith("Assets/ThirdParty/_Catalog"))
                .Distinct()
                .ToArray();
            if (paths.Length == 0) { Debug.Log("[ThirdParty] Nichts zu speichern."); return; }
            AssetDatabase.ForceReserializeAssets(paths, ForceReserializeAssetsOptions.ReserializeAssetsAndMetadata);
            AssetDatabase.SaveAssets();
            Debug.Log($"[ThirdParty] {paths.Length} Dateien im aktuellen Format neu gespeichert.");
        }
    }
}
