using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityBuilder.Editor
{
    /// <summary>
    /// Ordnet importierte Asset-Store-Pakete nach Assets/ThirdParty (nicht im Git) und wandelt
    /// Built-in-Materialien auf URP um. Menü: City Builder → Third Party → …
    ///   Batch: Unity.exe -batchmode -quit -projectPath . -executeMethod CityBuilder.Editor.ThirdPartySetup.OrganizeAndConvert
    /// </summary>
    public static class ThirdPartySetup
    {
        public const string Root = "Assets/ThirdParty";

        /// <summary>Wurzelordner der verwendeten Pakete (so wie sie importiert werden).</summary>
        static readonly string[] PackageRoots =
        {
            "Assets/SimplePoly City - Low Poly Assets",
            "Assets/Pandazole_Ultimate_Pack",
            "Assets/Triangularity",
            "Assets/ModularLowpolyStreetsFree",
            "Assets/Guddies",
            "Assets/Road props for games",
            "Assets/Downtown Game Studio",
            "Assets/Nebula - Free low poly car pack",
            "Assets/Vladislav Simakov",
            "Assets/Loading Games",          // Toon City Pack
            "Assets/Buildings",              // Lowpoly Modern City Buildings Set
            "Assets/Simple Vehicle Pack",
        };

        /// <summary>Mitgelieferte Skripte, die wir nicht wollen (eigene Kamera etc.).</summary>
        static readonly string[] UnwantedFiles =
        {
            "Triangularity/ColorfulCity/Scripts/SimpleCameraController.cs",
            // Vorkompilierter Inspector-Helfer, nutzt in Unity 6 entfernte API (PrefabStage) → Fehler bei jedem Start
            "Triangularity/ColorfulCity/Scripts/libs/Editor",
            // Demo-Skripte (alte Input-API, eigene Ampel-/Lichtlogik)
            "Loading Games/Toon City Pack/Demo/Scripts",
        };

        [MenuItem("City Builder/Third Party/Organize And Convert")]
        public static void OrganizeAndConvert()
        {
            Organize();
            ConvertMaterials();
            Report();
        }

        /// <summary>Ordner direkt unter Assets/, die zum Projekt gehören und nie verschoben werden.</summary>
        static readonly string[] ProjectFolders =
        {
            "_Project", "ThirdParty", "Scenes", "Settings", "Plugins", "Resources", "StreamingAssets", "Editor", "Gizmos",
            "TextMesh Pro", "UI Toolkit",
        };

        [MenuItem("City Builder/Third Party/Organize")]
        public static void Organize()
        {
            if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "ThirdParty");
            // Bekannte Paketordner plus jeder andere neue Ordner direkt unter Assets/ (neu importierte Store-Pakete)
            var roots = PackageRoots.Concat(AssetDatabase.GetSubFolders("Assets")
                    .Where(f => !ProjectFolders.Contains(Path.GetFileName(f), System.StringComparer.OrdinalIgnoreCase)))
                .Distinct().ToList();
            foreach (var src in roots)
            {
                if (!AssetDatabase.IsValidFolder(src)) continue;
                string dst = $"{Root}/{Path.GetFileName(src)}";
                if (AssetDatabase.IsValidFolder(dst)) { Debug.LogWarning($"[ThirdParty] Ziel existiert schon: {dst}"); continue; }
                string err = AssetDatabase.MoveAsset(src, dst);
                Debug.Log(string.IsNullOrEmpty(err) ? $"[ThirdParty] verschoben: {src} → {dst}" : $"[ThirdParty] Fehler {src}: {err}");
            }
            foreach (var f in UnwantedFiles)
            {
                string path = $"{Root}/{f}";
                if (AssetDatabase.DeleteAsset(path)) Debug.Log($"[ThirdParty] entfernt: {path}");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        [MenuItem("City Builder/Third Party/Convert Materials To URP")]
        public static void ConvertMaterials()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            int converted = 0, skipped = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null) continue;
                string s = m.shader.name;
                if (s.StartsWith("Universal Render Pipeline")) { skipped++; continue; }
                bool isUnlit = s.StartsWith("Unlit/");
                Convert(m, isUnlit ? unlit : lit, isUnlit);
                EditorUtility.SetDirty(m);
                converted++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ThirdParty] Materialien umgewandelt: {converted}, schon URP: {skipped}");
        }

        /// <summary>Überträgt die üblichen Built-in-Eigenschaften (Standard, Legacy, Mobile) auf URP Lit/Unlit.</summary>
        static void Convert(Material m, Shader target, bool unlit)
        {
            Texture main = Tex(m, "_MainTex");
            var scale = m.HasProperty("_MainTex") ? m.GetTextureScale("_MainTex") : Vector2.one;
            var offset = m.HasProperty("_MainTex") ? m.GetTextureOffset("_MainTex") : Vector2.zero;
            Color color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
            Texture bump = Tex(m, "_BumpMap");
            Texture metalMap = Tex(m, "_MetallicGlossMap");
            Texture emissionMap = Tex(m, "_EmissionMap");
            Texture occlusion = Tex(m, "_OcclusionMap");
            float metallic = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0f;
            float smooth = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : m.HasProperty("_Shininess") ? m.GetFloat("_Shininess") : 0.2f;
            Color emission = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            bool emissive = m.IsKeywordEnabled("_EMISSION") && emission.maxColorComponent > 0.01f;
            float mode = m.HasProperty("_Mode") ? m.GetFloat("_Mode") : 0f; // 0 opak, 1 Ausschnitt, 2/3 transparent
            float cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f;
            bool legacyTransparent = m.shader.name.Contains("Transparent");
            if (m.shader.name.Contains("Cutout")) mode = 1f;
            else if (legacyTransparent) mode = 3f;

            m.shader = target;
            m.SetTexture("_BaseMap", main);
            m.SetTextureScale("_BaseMap", scale);
            m.SetTextureOffset("_BaseMap", offset);
            m.SetColor("_BaseColor", color);

            if (!unlit)
            {
                if (bump != null) { m.SetTexture("_BumpMap", bump); m.EnableKeyword("_NORMALMAP"); }
                if (metalMap != null) { m.SetTexture("_MetallicGlossMap", metalMap); m.EnableKeyword("_METALLICSPECGLOSSMAP"); }
                if (occlusion != null) { m.SetTexture("_OcclusionMap", occlusion); m.EnableKeyword("_OCCLUSIONMAP"); }
                m.SetFloat("_Metallic", metallic);
                m.SetFloat("_Smoothness", Mathf.Clamp01(smooth));
                if (emissive)
                {
                    m.SetColor("_EmissionColor", emission);
                    if (emissionMap != null) m.SetTexture("_EmissionMap", emissionMap);
                    m.EnableKeyword("_EMISSION");
                    m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
            }

            if (mode >= 1f && mode < 2f)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", cutoff);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            else if (mode >= 2f)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.SetOverrideTag("RenderType", "Transparent");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            else
            {
                m.SetFloat("_Surface", 0f);
                m.renderQueue = -1;
            }
            m.enableInstancing = true;
        }

        static Texture Tex(Material m, string prop) => m.HasProperty(prop) ? m.GetTexture(prop) : null;

        [MenuItem("City Builder/Third Party/Report")]
        public static void Report()
        {
            var nonUrp = new List<string>();
            int materials = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Root }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                materials++;
                if (m != null && m.shader != null && !m.shader.name.StartsWith("Universal Render Pipeline"))
                    nonUrp.Add($"{path} ({m.shader.name})");
            }
            int prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { Root }).Length;
            int models = AssetDatabase.FindAssets("t:Model", new[] { Root }).Length;
            Debug.Log($"[ThirdParty] Bericht: {prefabs} Prefabs, {models} Modelle, {materials} Materialien, davon nicht URP: {nonUrp.Count}");
            foreach (var n in nonUrp.Take(30)) Debug.LogWarning($"[ThirdParty] nicht URP: {n}");
        }
    }
}
