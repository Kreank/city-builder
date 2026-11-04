using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.UI
{
    /// <summary>
    /// Bau-Vorschau für frei platzierte Bauwerke (Dienste, Versorger, Depots, Verschieben): durchsichtiger Quader plus
    /// <b>Vorderseiten-Markierung</b> – Streifen an der Vorderkante am Boden und Pfeil auf dem Dach, der zur Zufahrt zeigt.
    /// Auch für die Hervorhebung eines Bauwerks, das eine geplante Straße blockiert (ohne Pfeil).
    /// </summary>
    public sealed class PlacementGhost
    {
        readonly GameObject box, stripe;
        readonly MeshRenderer boxRenderer, stripeRenderer;
        readonly LineRenderer arrow;

        public PlacementGhost(Transform parent, string name)
        {
            box = Primitive(parent, name, out boxRenderer);
            stripe = Primitive(parent, name + " (Vorderseite)", out stripeRenderer);
            var go = new GameObject(name + " (Pfeil)");
            go.transform.SetParent(parent, false);
            arrow = go.AddComponent<LineRenderer>();
            arrow.useWorldSpace = true;
            arrow.loop = false;
            arrow.positionCount = 5;
            arrow.widthMultiplier = 0.9f;
            arrow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            arrow.receiveShadows = false;
            arrow.enabled = false;
        }

        static GameObject Primitive(Transform parent, string name, out MeshRenderer renderer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            go.SetActive(false);
            return go;
        }

        /// <summary>Zeigt die Vorschau. <paramref name="showFront"/> = Vorderseite markieren (Streifen + Pfeil).</summary>
        public void Show(SimVector2 pos, SimVector2 front, float width, float depth, float height, float tall, Material material, bool showFront = true)
        {
            if (front.LengthSquared() < 1e-6f) front = SimVector2.UnitY;
            var rot = Quaternion.LookRotation(new Vector3(front.X, 0f, front.Y));
            box.SetActive(true);
            box.transform.SetPositionAndRotation(SimConvert.ToWorld(pos, height + tall * 0.5f), rot);
            box.transform.localScale = new Vector3(width, tall, depth);
            boxRenderer.sharedMaterial = material;

            stripe.SetActive(showFront);
            arrow.enabled = showFront;
            if (!showFront) return;
            var frontMid = pos + front * (depth * 0.5f);
            stripe.transform.SetPositionAndRotation(SimConvert.ToWorld(frontMid - front * 0.8f, height + 0.25f), rot);
            stripe.transform.localScale = new Vector3(width * 0.9f, 0.5f, 1.6f);
            stripeRenderer.sharedMaterial = material;

            // Pfeil vom Dach über die Vorderkante hinaus (zeigt zur Zufahrt)
            float roof = height + tall + 0.6f;
            float len = Mathf.Clamp(depth * 0.5f + 4f, 6f, 30f);
            var tail = pos - front * (depth * 0.15f);
            var tip = tail + front * len;
            var side = new SimVector2(-front.Y, front.X);
            float head = Mathf.Clamp(width * 0.15f, 1.5f, 5f);
            arrow.sharedMaterial = material;
            arrow.widthMultiplier = Mathf.Clamp(width * 0.04f, 0.6f, 1.6f);
            arrow.SetPosition(0, SimConvert.ToWorld(tail, roof));
            arrow.SetPosition(1, SimConvert.ToWorld(tip, roof));
            arrow.SetPosition(2, SimConvert.ToWorld(tip - front * head + side * head, roof));
            arrow.SetPosition(3, SimConvert.ToWorld(tip, roof));
            arrow.SetPosition(4, SimConvert.ToWorld(tip - front * head - side * head, roof));
        }

        // ------------------------------------------------------------ echtes Modell als Geist

        readonly System.Collections.Generic.Dictionary<GameObject, GameObject> modelGhosts =
            new System.Collections.Generic.Dictionary<GameObject, GameObject>();
        readonly System.Collections.Generic.Dictionary<GameObject, float> modelHeights =
            new System.Collections.Generic.Dictionary<GameObject, float>();
        GameObject activeModel;
        Material activeModelMaterial;

        /// <summary>
        /// Zeigt das <b>echte Modell</b> des Bauwerks durchsichtig (grün/rot über <paramref name="material"/>) in der
        /// Drehung, in der es gebaut würde – plus Vorderseiten-Streifen und Pfeil. Ohne Modell → wie <see cref="Show"/> (Quader).
        /// Die Instanz wird je Modell einmal erzeugt und wiederverwendet (keine Skripte, keine Schatten).
        /// </summary>
        public void ShowModel(GameObject model, SimVector2 pos, SimVector2 front, float width, float depth, float height,
                              float fallbackTall, Material material, bool showFront = true, int units = 1)
        {
            if (model == null) { HideModel(); Show(pos, front, width, depth, height, fallbackTall, material, showFront); return; }
            if (front.LengthSquared() < 1e-6f) front = SimVector2.UnitY;

            if (!modelGhosts.TryGetValue(model, out var inst) || inst == null)
            {
                inst = Object.Instantiate(model, box.transform.parent, false);
                inst.name = model.name + " (Vorschau)";
                foreach (var c in inst.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
                foreach (var b in inst.GetComponentsInChildren<MonoBehaviour>(true)) Object.Destroy(b);
                FitLikeViews(inst, width, depth, units);
                float top = 0f;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    top = Mathf.Max(top, r.bounds.max.y - inst.transform.position.y);
                }
                modelGhosts[model] = inst;
                modelHeights[model] = top > 0.5f ? top : fallbackTall;
                activeModelMaterial = null;
            }
            if (activeModel != null && activeModel != inst) activeModel.SetActive(false);
            activeModel = inst;
            inst.SetActive(true);
            inst.transform.SetPositionAndRotation(SimConvert.ToWorld(pos, height), Quaternion.LookRotation(new Vector3(front.X, 0f, front.Y)));

            if (activeModelMaterial != material)
            {
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                    for (int i = 0; i < mats.Length; i++) mats[i] = material;
                    r.sharedMaterials = mats;
                }
                activeModelMaterial = material;
            }

            // Quader aus, Vorderseiten-Markierung bleibt (Pfeil über dem echten Dach)
            Show(pos, front, width, depth, height, modelHeights[model], material, showFront);
            box.SetActive(false);
        }

        /// <summary>
        /// Wie UtilityView/ServiceView: grobe Einheitenfehler (cm statt m) an die Grundfläche anpassen und Bauwerke mit mehreren
        /// Anlagen (Offshore-Windpark) im Raster vervielfachen – sonst wäre die Vorschau riesig/winzig bzw. zeigte nur ein Windrad.
        /// </summary>
        static void FitLikeViews(GameObject inst, float width, float depth, int units)
        {
            var t = inst.transform;
            t.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rootScale = t.localScale;
            t.localScale = Vector3.one;
            // Kinder unter einen Zwischenknoten (trägt die Prefab-Skalierung), damit Skalierung und Kopien das Wurzelobjekt
            // (Lage/Drehung je Frame) nicht stören
            var children = new System.Collections.Generic.List<Transform>();
            foreach (Transform c in t) children.Add(c);
            var model = new GameObject(inst.name + " Teile").transform;
            model.SetParent(t, false);
            model.localScale = rootScale;
            foreach (var c in children) c.SetParent(model, false);
            // Renderer direkt an der Wurzel (Ein-Mesh-Prefab): als Kopie unter den Zwischenknoten
            var rootFilter = inst.GetComponent<MeshFilter>();
            var rootRenderer = inst.GetComponent<MeshRenderer>();
            if (rootFilter != null && rootRenderer != null)
            {
                var copy = new GameObject("Mesh");
                copy.transform.SetParent(model, false);
                copy.AddComponent<MeshFilter>().sharedMesh = rootFilter.sharedMesh;
                copy.AddComponent<MeshRenderer>().sharedMaterials = rootRenderer.sharedMaterials;
                rootRenderer.enabled = false;
                Object.Destroy(rootRenderer);
                Object.Destroy(rootFilter);
            }

            bool any = false;
            var b = new Bounds();
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            if (any)
            {
                float fit = Mathf.Min(width / Mathf.Max(0.01f, b.size.x), depth / Mathf.Max(0.01f, b.size.z));
                if (fit < 0.2f || fit > 5f) model.localScale = rootScale * fit;
            }
            if (units <= 1) return;
            int cols = Mathf.CeilToInt(Mathf.Sqrt(units)), rows = Mathf.CeilToInt(units / (float)cols);
            for (int i = 0; i < units; i++)
            {
                int cx = i % cols, cz = i / cols;
                var at = new Vector3((cx - (cols - 1) * 0.5f) * width / cols, 0f, (cz - (rows - 1) * 0.5f) * depth / rows);
                var unit = i == 0 ? model : Object.Instantiate(model.gameObject, t, false).transform;
                unit.localPosition = at;
            }
        }

        void HideModel()
        {
            if (activeModel != null) activeModel.SetActive(false);
            activeModel = null;
        }

        public void Hide()
        {
            if (box != null) box.SetActive(false);
            if (stripe != null) stripe.SetActive(false);
            if (arrow != null) arrow.enabled = false;
            HideModel();
        }
    }
}
