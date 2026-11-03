using CityBuilder.Simulation.Roads;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Roads
{
    /// <summary>
    /// Straßen bauen wie in Cities: Skylines.
    ///   Gerade: Klick Start → Klick Ende (danach geht es direkt am Ende weiter).
    ///   Kurve:  Klick Start → Klick Kontrollpunkt → Klick Ende.
    /// R = Gerade/Kurve, Tab = Straßentyp, B = Abriss, Rechtsklick/Esc = abbrechen,
    /// G = Einrasten an/aus (15°-Winkel, 8-m-Längen), Strg halten = vorübergehend frei.
    /// </summary>
    public sealed class RoadTool : MonoBehaviour
    {
        public enum Mode { Straight, Curve, Bulldoze }

        /// <summary>Alles, was die Hilfslinien/Beschriftungen zum aktuellen Bauvorgang wissen müssen.</summary>
        public struct GuideState
        {
            public bool Active;
            public bool IsCurve;
            public bool PlacingControl;     // Kurve: gerade wird der Kontrollpunkt gesetzt
            public bool Valid;
            public SimVector2 Start, Control, End;
            public float Length;
            public float Width;

            public bool HasStartReference;  // am Start hängt schon eine Straße
            public SimVector2 StartReference; // deren Abgangsrichtung
            public SimVector2 StartDirection; // Abgangsrichtung der neuen Straße
            public float StartAngle;
            public float Bearing;           // Kompassrichtung, falls keine Straße am Start

            public bool HasEndReference;
            public SimVector2 EndReference;
            public SimVector2 EndDirection;
            public float EndAngle;
        }

        public const float AngleSnapStep = 15f;
        public const float LengthSnapStep = 8f;

        [SerializeField] SimulationRunner runner;
        [SerializeField] Camera cam;
        [SerializeField] Material previewMaterial;
        [SerializeField] Material invalidMaterial;
        [SerializeField] float snapDistance = 10f;
        [SerializeField] float previewHeight = 0.1f;

        enum Step { Start, Control, End }

        Step step = Step.Start;
        SimVector2 startPoint;
        SimVector2 controlPoint;
        int roadTypeIndex;
        Mesh previewMesh;
        MeshRenderer previewRenderer;

        public Mode CurrentMode { get; private set; } = Mode.Straight;
        public RoadType CurrentType => RoadType.All[roadTypeIndex];
        public bool HasCursor { get; private set; }
        public SimVector2 Cursor { get; private set; }
        public float PreviewLength => Guide.Active ? Guide.Length : 0f;
        public bool SnapEnabled { get; private set; } = true;
        public bool SnapActive { get; private set; }
        public GuideState Guide { get; private set; }
        public Camera Camera => cam;

        void Start()
        {
            if (cam == null) cam = Camera.main;

            var go = new GameObject("Road Preview");
            go.transform.SetParent(transform, false);
            previewMesh = new Mesh();
            go.AddComponent<MeshFilter>().sharedMesh = previewMesh;
            previewRenderer = go.AddComponent<MeshRenderer>();
            previewRenderer.sharedMaterial = previewMaterial;
            previewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            previewRenderer.enabled = false;

            // Hilfslinien gehören fest zum Werkzeug – automatisch anhängen, falls nicht in der Szene.
            var guides = GetComponent<RoadGuidesView>();
            if (guides == null) guides = gameObject.AddComponent<RoadGuidesView>();
            guides.Init(this, previewMaterial);
        }

        void OnDestroy()
        {
            if (previewMesh != null) Destroy(previewMesh);
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (kb != null)
            {
                if (kb.rKey.wasPressedThisFrame) SetMode(CurrentMode == Mode.Straight ? Mode.Curve : Mode.Straight);
                if (kb.bKey.wasPressedThisFrame) SetMode(CurrentMode == Mode.Bulldoze ? Mode.Straight : Mode.Bulldoze);
                if (kb.tabKey.wasPressedThisFrame) roadTypeIndex = (roadTypeIndex + 1) % RoadType.All.Length;
                if (kb.gKey.wasPressedThisFrame) SnapEnabled = !SnapEnabled;
                if (kb.escapeKey.wasPressedThisFrame) Cancel();
            }
            SnapActive = SnapEnabled && !(kb != null && kb.ctrlKey.isPressed);
            if (mouse.rightButton.wasPressedThisFrame) Cancel();

            var roads = runner.World.Roads;
            HasCursor = TryGetGroundPoint(mouse.position.ReadValue(), out var raw);
            if (!HasCursor)
            {
                HidePreview();
                return;
            }

            if (CurrentMode == Mode.Bulldoze)
            {
                Cursor = raw;
                UpdateBulldoze(roads, raw, mouse.leftButton.wasPressedThisFrame);
                return;
            }

            var cursorNode = roads.FindNearestNode(raw, snapDistance);
            Cursor = ResolveCursor(roads, raw, cursorNode);

            if (mouse.leftButton.wasPressedThisFrame) OnClick(roads);
            UpdatePreview(roads, step == Step.End ? roads.FindNearestNode(Cursor, 0.01f) : null);
        }

        /// <summary>Wo landet der Klick? Vorrang: vorhandener Knoten, dann Winkel-/Längenraster, sonst frei.</summary>
        SimVector2 ResolveCursor(RoadNetwork roads, SimVector2 raw, RoadNode cursorNode)
        {
            if (cursorNode != null) return cursorNode.Position;
            if (step == Step.Start || !SnapActive) return raw;

            bool curveEnd = CurrentMode == Mode.Curve && step == Step.End;
            SimVector2 origin = curveEnd ? controlPoint : startPoint;
            SimVector2 reference;
            if (curveEnd)
                reference = controlPoint - startPoint; // Winkel relativ zur Starttangente
            else if (!RoadGeometry.TryGetReferenceDirection(roads, roads.FindNearestNode(startPoint, 0.01f), raw - startPoint, out reference, out _))
                reference = SimVector2.UnitY; // kein Anschluss → an Nord/Ost ausrichten

            return RoadGeometry.Snap(origin, raw, reference, AngleSnapStep, curveEnd ? 0f : LengthSnapStep);
        }

        void SetMode(Mode mode)
        {
            CurrentMode = mode;
            Cancel();
        }

        void Cancel()
        {
            step = Step.Start;
            HidePreview();
        }

        void HidePreview()
        {
            if (previewRenderer != null) previewRenderer.enabled = false;
            Guide = default;
        }

        void OnClick(RoadNetwork roads)
        {
            if (!runner.World.Contains(Cursor)) return;

            switch (step)
            {
                case Step.Start:
                    startPoint = Cursor;
                    step = CurrentMode == Mode.Curve ? Step.Control : Step.End;
                    break;

                case Step.Control:
                    if (SimVector2.Distance(startPoint, Cursor) < RoadNetwork.MinSegmentLength * 0.5f) return;
                    controlPoint = Cursor;
                    step = Step.End;
                    break;

                case Step.End:
                    SimVector2? control = CurrentMode == Mode.Curve ? controlPoint : (SimVector2?)null;
                    var segment = roads.BuildRoad(startPoint, control, Cursor, CurrentType, snapDistance);
                    if (segment != null)
                    {
                        // Weiterbauen am Ende, wie man es aus Cities: Skylines kennt.
                        startPoint = segment.End.Position;
                        step = CurrentMode == Mode.Curve ? Step.Control : Step.End;
                    }
                    break;
            }
        }

        void UpdatePreview(RoadNetwork roads, RoadNode endNode)
        {
            if (step == Step.Start)
            {
                HidePreview();
                return;
            }

            bool curve = CurrentMode == Mode.Curve;
            bool placingControl = step == Step.Control;
            SimVector2 control = curve && !placingControl ? controlPoint : (startPoint + Cursor) * 0.5f;

            var g = new GuideState
            {
                Active = true,
                IsCurve = curve,
                PlacingControl = placingControl,
                Start = startPoint,
                Control = control,
                End = Cursor,
                Width = CurrentType.Width,
                Length = Bezier.ApproximateLength(startPoint, control, Cursor),
            };
            g.Valid = placingControl || g.Length >= RoadNetwork.MinSegmentLength;

            // Winkel am Start
            g.StartDirection = SafeNormalize(control - startPoint, Cursor - startPoint);
            var startNode = roads.FindNearestNode(startPoint, 0.01f);
            g.HasStartReference = RoadGeometry.TryGetReferenceDirection(roads, startNode, g.StartDirection, out g.StartReference, out g.StartAngle);
            g.Bearing = RoadGeometry.Bearing(g.StartDirection);

            // Winkel am Ende, wenn dort an eine vorhandene Straße angeschlossen wird
            if (!placingControl && endNode != null && endNode != startNode)
            {
                g.EndDirection = SafeNormalize(control - Cursor, startPoint - Cursor);
                g.HasEndReference = RoadGeometry.TryGetReferenceDirection(roads, endNode, g.EndDirection, out g.EndReference, out g.EndAngle);
            }

            Guide = g;

            RoadMeshBuilder.BuildSegment(startPoint, control, Cursor, CurrentType.Width, previewHeight, previewMesh);
            previewRenderer.sharedMaterial = g.Valid ? previewMaterial : invalidMaterial;
            previewRenderer.enabled = g.Length > 0.01f;
        }

        static SimVector2 SafeNormalize(SimVector2 v, SimVector2 fallback)
        {
            if (v.LengthSquared() > 1e-6f) return SimVector2.Normalize(v);
            return fallback.LengthSquared() > 1e-6f ? SimVector2.Normalize(fallback) : SimVector2.UnitY;
        }

        void UpdateBulldoze(RoadNetwork roads, SimVector2 point, bool clicked)
        {
            Guide = default;
            var segment = roads.FindNearestSegment(point, 2f);
            if (segment == null)
            {
                previewRenderer.enabled = false;
                return;
            }

            RoadMeshBuilder.BuildSegment(segment.Start.Position, segment.Control, segment.End.Position,
                segment.Type.Width + 1f, previewHeight, previewMesh);
            previewRenderer.sharedMaterial = invalidMaterial;
            previewRenderer.enabled = true;

            if (clicked)
            {
                roads.RemoveSegment(segment.Id);
                previewRenderer.enabled = false;
            }
        }

        /// <summary>Schnitt des Mausstrahls mit der Bodenebene (y = 0). Später: Raycast aufs Terrain.</summary>
        bool TryGetGroundPoint(Vector2 screenPos, out SimVector2 point)
        {
            point = default;
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(screenPos);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float enter)) return false;
            point = SimConvert.ToSim(ray.GetPoint(enter));
            return true;
        }
    }
}
