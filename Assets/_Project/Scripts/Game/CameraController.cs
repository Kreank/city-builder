using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game
{
    /// <summary>
    /// Städtebau-Kamera: kreist um einen Fokuspunkt auf dem Boden.
    /// WASD/Pfeile = verschieben, Q/E oder mittlere Maustaste = drehen, Mausrad = zoomen.
    /// </summary>
    public sealed class CameraController : MonoBehaviour
    {
        [SerializeField] Vector3 focus = Vector3.zero;
        [SerializeField] float yaw = 0f;
        [SerializeField, Range(15f, 89f)] float pitch = 50f;
        [SerializeField] float distance = 400f;

        [Header("Limits")]
        [SerializeField] float minDistance = 20f;
        [SerializeField] float maxDistance = 1800f;
        [SerializeField] float worldHalfSize = 1024f;

        [Header("Speeds")]
        [SerializeField] float panSpeed = 1.2f;       // relativ zur Zoomstufe
        [SerializeField] float rotateSpeed = 90f;     // Grad/s per Tastatur
        [SerializeField] float mouseRotateSpeed = 0.25f;
        [SerializeField] float zoomStep = 0.12f;      // Anteil pro Mausrad-Raste
        [SerializeField] float smoothing = 12f;

        float targetDistance;
        float groundHeight;
        SimulationRunner runner;

        void Awake() => targetDistance = distance;

        /// <summary>
        /// Inspektor: Fokus folgt diesem Punkt (null = frei). Wird jedes Frame neu gesetzt; Verschieben per Tastatur
        /// hebt das Folgen auf (setzt null) – der Setzende erkennt das daran.
        /// </summary>
        public Vector3? FollowTarget { get; set; }

        void Start() => runner = FindAnyObjectByType<SimulationRunner>();

        void LateUpdate()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            float dt = Time.unscaledDeltaTime;

            // Verschieben (in Blickrichtung, unabhängig vom Neigungswinkel)
            Vector2 move = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
                if (kb.qKey.isPressed) yaw += rotateSpeed * dt;
                if (kb.eKey.isPressed) yaw -= rotateSpeed * dt;
            }

            if (move != Vector2.zero) FollowTarget = null;
            else if (FollowTarget.HasValue)
            {
                var t = FollowTarget.Value;
                float k = 1f - Mathf.Exp(-8f * dt);
                focus.x = Mathf.Lerp(focus.x, t.x, k);
                focus.z = Mathf.Lerp(focus.z, t.z, k);
            }

            if (move != Vector2.zero)
            {
                Quaternion flatRot = Quaternion.Euler(0f, yaw, 0f);
                Vector3 dir = flatRot * new Vector3(move.x, 0f, move.y).normalized;
                float fast = kb != null && kb.leftShiftKey.isPressed ? 2.5f : 1f;
                focus += dir * (panSpeed * distance * fast * dt);
            }

            if (mouse != null)
            {
                if (mouse.middleButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    yaw += delta.x * mouseRotateSpeed;
                    pitch = Mathf.Clamp(pitch - delta.y * mouseRotateSpeed, 15f, 89f);
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    targetDistance *= 1f - Mathf.Sign(scroll) * zoomStep;
            }

            targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);
            distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-smoothing * dt));

            focus.x = Mathf.Clamp(focus.x, -worldHalfSize, worldHalfSize);
            focus.z = Mathf.Clamp(focus.z, -worldHalfSize, worldHalfSize);
            // Fokuspunkt folgt dem Gelände (weich, damit die Kamera nicht springt)
            if (runner != null && runner.World != null)
            {
                float g = Mathf.Max(runner.World.Terrain.HeightAt(SimConvert.ToSim(focus)), runner.World.Terrain.WaterLevel);
                groundHeight = Mathf.Lerp(groundHeight, g, 1f - Mathf.Exp(-4f * dt));
            }
            focus.y = groundHeight;

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = focus - rot * Vector3.forward * distance;
            transform.rotation = rot;
        }
    }
}
