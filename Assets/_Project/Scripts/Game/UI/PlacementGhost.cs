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

        public void Hide()
        {
            if (box != null) box.SetActive(false);
            if (stripe != null) stripe.SetActive(false);
            if (arrow != null) arrow.enabled = false;
        }
    }
}
