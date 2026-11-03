using System.IO;
using CityBuilder.Game;
using CityBuilder.Game.Environment;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityBuilder.Editor
{
    /// <summary>
    /// Baut die Spielszene und Platzhalter-Materialien per Code auf.
    /// Menü: City Builder → Create Main Scene. Auch per Batchmode aufrufbar:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod CityBuilder.Editor.ProjectSetup.CreateMainScene
    /// </summary>
    public static class ProjectSetup
    {
        const string Root = "Assets/_Project";
        const string MaterialDir = Root + "/Materials";
        const string ScenePath = Root + "/Scenes/Main.unity";
        const float WorldSize = 2048f;

        [MenuItem("City Builder/Create Main Scene")]
        public static void CreateMainScene()
        {
            if (!Application.isBatchMode && File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Main Scene", "Main.unity existiert schon. Neu erzeugen und überschreiben?", "Überschreiben", "Abbrechen"))
                return;

            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            AssetDatabase.Refresh();

            var ground = LitMaterial("Ground", new Color(0.36f, 0.52f, 0.28f));
            var road = LitMaterial("Road", new Color(0.22f, 0.22f, 0.24f));
            var preview = TransparentMaterial("RoadPreview", new Color(0.3f, 0.7f, 1f, 0.55f));
            var invalid = TransparentMaterial("RoadInvalid", new Color(1f, 0.25f, 0.2f, 0.55f));
            var overlay = TransparentMaterial("EnvironmentOverlay", Color.white);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Kamera
            var cam = Camera.main;
            cam.farClipPlane = 6000f;
            cam.nearClipPlane = 0.5f;
            cam.gameObject.AddComponent<CameraController>();

            // Licht etwas schräg für schönere Schatten
            foreach (var light in Object.FindObjectsByType<Light>())
                if (light.type == LightType.Directional) light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            // Boden (Platzhalter, später echtes Terrain)
            var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Ground";
            plane.transform.localScale = new Vector3(WorldSize / 10f, 1f, WorldSize / 10f);
            plane.GetComponent<MeshRenderer>().sharedMaterial = ground;

            // Simulation + Systeme
            var sim = new GameObject("Simulation");
            var runner = sim.AddComponent<SimulationRunner>();

            var roadsGo = new GameObject("Roads");
            var view = roadsGo.AddComponent<RoadNetworkView>();
            var tool = roadsGo.AddComponent<RoadTool>();
            SetRefs(view, ("runner", runner), ("roadMaterial", road));
            SetRefs(tool, ("runner", runner), ("cam", cam), ("previewMaterial", preview), ("invalidMaterial", invalid));

            var envGo = new GameObject("Environment Overlay");
            var overlayView = envGo.AddComponent<EnvironmentOverlayView>();
            SetRefs(overlayView, ("runner", runner), ("overlayMaterial", overlay));

            var hud = new GameObject("Debug HUD").AddComponent<DebugHud>();
            SetRefs(hud, ("runner", runner), ("roadTool", tool), ("overlay", overlayView));

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[CityBuilder] Szene erstellt: {ScenePath}");
        }

        static void SetRefs(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in refs)
            {
                var prop = so.FindProperty(field);
                if (prop == null) { Debug.LogError($"Feld '{field}' nicht gefunden auf {target.GetType().Name}"); continue; }
                prop.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Material LitMaterial(string name, Color color)
        {
            var mat = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.1f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Transparentes, unbeleuchtetes Material (Sprites/Default rendert auch in URP).</summary>
        static Material TransparentMaterial(string name, Color color)
        {
            var mat = LoadOrCreate(name, "Sprites/Default");
            mat.color = color;
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material LoadOrCreate(string name, string shaderName)
        {
            string path = $"{MaterialDir}/{name}.mat";
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new System.Exception($"Shader '{shaderName}' nicht gefunden.");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            else mat.shader = shader;
            return mat;
        }
    }
}
