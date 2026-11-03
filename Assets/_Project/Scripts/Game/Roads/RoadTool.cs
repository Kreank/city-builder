using System.Collections.Generic;
using CityBuilder.Game.UI;
using CityBuilder.Simulation.Roads;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Roads
{
    /// <summary>
    /// Straßen bauen wie in Cities: Skylines.
    ///   Gerade:       Klick Start → Klick Ende (danach geht es direkt am Ende weiter).
    ///   Kurve:        Klick Start → Klick Kontrollpunkt → Klick Ende.
    ///   Kreisverkehr: Klick Mittelpunkt → Maus = Radius → Klick bauen.
    /// Alle Prüfungen und das eigentliche Bauen macht der <see cref="RoadBuilder"/> der Simulation.
    /// </summary>
    public sealed class RoadTool : MonoBehaviour
    {
        public enum Mode { Straight, Curve, Roundabout, Bulldoze }

        /// <summary>Alles, was Hilfslinien und Beschriftungen zum aktuellen Bauvorgang wissen müssen.</summary>
        public struct GuideState
        {
            public bool Active;
            public bool IsCurve;
            public bool PlacingControl;
            public bool Valid;
            public string Error;
            public SimVector2 Start, Control, End;
            public float Length;
            public float Cost;
            public RoadType Type;

            public bool HasStartReference;
            public SimVector2 StartReference;
            public SimVector2 StartDirection;
            public float StartAngle;
            public float Bearing;

            public bool HasEndReference;
            public SimVector2 EndReference;
            public SimVector2 EndDirection;
            public float EndAngle;

            public bool IsRoundabout;
            public SimVector2 Center;
            public float Radius;
        }

        public const float AngleSnapStep = 15f;
        public const float LengthSnapStep = 8f;
        public const float RadiusSnapStep = 2f;

        [SerializeField] SimulationRunner runner;
        [SerializeField] Camera cam;
        [SerializeField] Material previewMaterial;
        [SerializeField] Material invalidMaterial;
        [SerializeField] float snapDistance = 10f;
        [SerializeField] float previewHeight = 0.3f;

        enum Step { Start, Control, End }

        Step step = Step.Start;
        SimVector2 startPoint;
        SimVector2 controlPoint;
        RoadPlan currentPlan;
        RoundaboutPlan currentRoundabout;
        Mesh previewMesh;
        MeshRenderer previewRenderer;
        readonly List<(SimVector2, SimVector2, SimVector2)> previewCurves = new List<(SimVector2, SimVector2, SimVector2)>();

        public Mode CurrentMode { get; private set; } = Mode.Straight;
        public RoadType SelectedType { get; private set; } = RoadCatalog.CityTwoLane;
        public bool HasCursor { get; private set; }
        public SimVector2 Cursor { get; private set; }
        public RoadAnchor CursorAnchor { get; private set; }
        public bool SnapEnabled { get; private set; } = true;
        public bool SnapActive { get; private set; }
        public GuideState Guide { get; private set; }
        public float PreviewLength => Guide.Active ? Guide.Length : 0f;
        public Camera Camera => cam;

        /// <summary>Kreisverkehre brauchen eine Einbahn-Stadtstraße; sonst Standardtyp.</summary>
        public RoadType RoundaboutType =>
            SelectedType.IsOneWay && SelectedType.Category == RoadCategory.City ? SelectedType : RoadCatalog.DefaultRoundabout;

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

            var guides = GetComponent<RoadGuidesView>();
            if (guides == null) guides = gameObject.AddComponent<RoadGuidesView>();
            guides.Init(this, previewMaterial);

            // Bauleiste gehört ebenfalls zum Werkzeug – so muss die Szene nicht neu erzeugt werden.
            var toolbar = GetComponent<RoadToolbar>();
            if (toolbar == null) toolbar = gameObject.AddComponent<RoadToolbar>();
            toolbar.Init(this);
        }

        void OnDestroy()
        {
            if (previewMesh != null) Destroy(previewMesh);
        }

        // ------------------------------------------------------------ öffentliche Steuerung (Toolbar)

        public void SetMode(Mode mode)
        {
            CurrentMode = mode;
            Cancel();
        }

        public void SelectType(RoadType type)
        {
            if (type == null) return;
            SelectedType = type;
            if (CurrentMode == Mode.Bulldoze) SetMode(Mode.Straight);
        }

        public void Cancel()
        {
            step = Step.Start;
            HidePreview();
        }

        // ------------------------------------------------------------ Update

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (kb != null)
            {
                if (kb.rKey.wasPressedThisFrame) SetMode(CurrentMode == Mode.Straight ? Mode.Curve : Mode.Straight);
                if (kb.kKey.wasPressedThisFrame) SetMode(CurrentMode == Mode.Roundabout ? Mode.Straight : Mode.Roundabout);
                if (kb.bKey.wasPressedThisFrame) SetMode(CurrentMode == Mode.Bulldoze ? Mode.Straight : Mode.Bulldoze);
                if (kb.tabKey.wasPressedThisFrame) CycleType(kb.shiftKey.isPressed ? -1 : 1);
                if (kb.gKey.wasPressedThisFrame) SnapEnabled = !SnapEnabled;
                if (kb.escapeKey.wasPressedThisFrame) Cancel();
            }
            SnapActive = SnapEnabled && !(kb != null && kb.ctrlKey.isPressed);
            if (mouse.rightButton.wasPressedThisFrame) Cancel();

            Vector2 screen = mouse.position.ReadValue();
            bool overUi = UiBlocker.IsPointerOverUi(screen);
            HasCursor = TryGetGroundPoint(screen, out var raw) && !overUi;
            if (!HasCursor)
            {
                if (previewRenderer != null) previewRenderer.enabled = false;
                return;
            }
            bool clicked = mouse.leftButton.wasPressedThisFrame;
            var roads = runner.World.Roads;

            switch (CurrentMode)
            {
                case Mode.Bulldoze:
                    Cursor = raw;
                    CursorAnchor = RoadAnchor.Free(raw);
                    UpdateBulldoze(roads, raw, clicked);
                    break;
                case Mode.Roundabout:
                    UpdateRoundabout(roads, raw, clicked);
                    break;
                default:
                    UpdateRoad(roads, raw, clicked);
                    break;
            }
        }

        void CycleType(int dir)
        {
            var all = RoadCatalog.All;
            int i = System.Array.IndexOf(all, SelectedType);
            SelectType(all[((i + dir) % all.Length + all.Length) % all.Length]);
        }

        // ------------------------------------------------------------ Straße (gerade/Kurve)

        void UpdateRoad(RoadNetwork roads, SimVector2 raw, bool clicked)
        {
            bool curve = CurrentMode == Mode.Curve;
            var anchor = RoadBuilder.ResolveAnchor(roads, raw, snapDistance);
            CursorAnchor = anchor;

            // Zielpunkt bestimmen: Anschluss hat Vorrang, sonst Winkel-/Längenraster.
            SimVector2 target = anchor.Position;
            float targetSnap = 0.5f;
            if (anchor.Kind == AnchorKind.Free)
            {
                targetSnap = 0f;
                if (step != Step.Start && SnapActive)
                {
                    bool curveEnd = curve && step == Step.End;
                    SimVector2 origin = curveEnd ? controlPoint : startPoint;
                    SimVector2 reference;
                    if (curveEnd) reference = controlPoint - startPoint;
                    else if (!RoadGeometry.TryGetReferenceDirection(roads, roads.FindNearestNode(startPoint, 0.01f),
                                 raw - startPoint, out reference, out _))
                        reference = SimVector2.UnitY;
                    target = RoadGeometry.Snap(origin, raw, reference, AngleSnapStep, curveEnd ? 0f : LengthSnapStep);
                }
            }
            Cursor = target;

            if (step == Step.Start)
            {
                HidePreview();
                if (clicked && runner.World.Contains(target))
                {
                    startPoint = target;
                    step = curve ? Step.Control : Step.End;
                }
                return;
            }

            if (step == Step.Control)
            {
                // Richtung/Länge des ersten Kurvenschenkels zeigen
                currentPlan = RoadBuilder.Plan(roads, startPoint, 0.5f, null, target, targetSnap, SelectedType);
                ShowPlan(roads, currentPlan, placingControl: true);
                if (clicked && SimVector2.Distance(startPoint, target) >= RoadNetwork.MinSegmentLength)
                {
                    controlPoint = target;
                    step = Step.End;
                }
                return;
            }

            SimVector2? control = curve ? controlPoint : (SimVector2?)null;
            currentPlan = RoadBuilder.Plan(roads, startPoint, 0.5f, control, target, targetSnap, SelectedType);
            ShowPlan(roads, currentPlan, placingControl: false);

            if (clicked && currentPlan.IsValid && runner.World.Contains(currentPlan.End))
            {
                var built = RoadBuilder.Build(roads, currentPlan);
                if (built.Count > 0)
                {
                    // Weiterbauen am Ende, wie man es aus Cities: Skylines kennt.
                    startPoint = built[built.Count - 1].End.Position;
                    step = curve ? Step.Control : Step.End;
                }
            }
        }

        void ShowPlan(RoadNetwork roads, RoadPlan plan, bool placingControl)
        {
            var g = new GuideState
            {
                Active = true,
                IsCurve = CurrentMode == Mode.Curve,
                PlacingControl = placingControl,
                Start = plan.Start,
                Control = plan.Control,
                End = plan.End,
                Length = plan.Length,
                Cost = plan.Cost,
                Type = plan.Type,
                Valid = placingControl || plan.IsValid,
                Error = placingControl || plan.IsValid ? null : ErrorText(plan.Error),
            };

            g.StartDirection = SafeNormalize(plan.Control - plan.Start, plan.End - plan.Start);
            var startNode = plan.StartAnchor.Kind == AnchorKind.Node ? plan.StartAnchor.Node : null;
            g.HasStartReference = RoadGeometry.TryGetReferenceDirection(roads, startNode, g.StartDirection, out g.StartReference, out g.StartAngle);
            if (!g.HasStartReference && plan.StartAnchor.Kind == AnchorKind.Segment)
                g.HasStartReference = SegmentReference(plan.StartAnchor, g.StartDirection, out g.StartReference, out g.StartAngle);
            g.Bearing = RoadGeometry.Bearing(g.StartDirection);

            if (!placingControl)
            {
                g.EndDirection = SafeNormalize(plan.Control - plan.End, plan.Start - plan.End);
                if (plan.EndAnchor.Kind == AnchorKind.Node && plan.EndAnchor.Node != startNode)
                    g.HasEndReference = RoadGeometry.TryGetReferenceDirection(roads, plan.EndAnchor.Node, g.EndDirection, out g.EndReference, out g.EndAngle);
                else if (plan.EndAnchor.Kind == AnchorKind.Segment)
                    g.HasEndReference = SegmentReference(plan.EndAnchor, g.EndDirection, out g.EndReference, out g.EndAngle);
            }
            Guide = g;

            previewCurves.Clear();
            previewCurves.Add((plan.Start, plan.Control, plan.End));
            ShowPreviewMesh(plan.Type.Width, g.Valid);
        }

        /// <summary>Anschluss mitten auf einer Straße: Referenz ist die näher liegende der beiden Fahrtrichtungen.</summary>
        static bool SegmentReference(RoadAnchor anchor, SimVector2 direction, out SimVector2 reference, out float angle)
        {
            var tangent = anchor.Segment.Tangent(anchor.T);
            float a1 = RoadGeometry.Angle(tangent, direction);
            float a2 = RoadGeometry.Angle(-tangent, direction);
            reference = a1 <= a2 ? tangent : -tangent;
            angle = Mathf.Min(a1, a2);
            return true;
        }

        // ------------------------------------------------------------ Kreisverkehr

        void UpdateRoundabout(RoadNetwork roads, SimVector2 raw, bool clicked)
        {
            if (step == Step.Start)
            {
                // Mittelpunkt rastet an Knoten ein – so wird aus einer Kreuzung schnell ein Kreisverkehr.
                var node = roads.FindNearestNode(raw, snapDistance);
                Cursor = node != null ? node.Position : raw;
                CursorAnchor = node != null ? RoadAnchor.AtNode(node) : RoadAnchor.Free(raw);
                HidePreview();
                if (clicked && runner.World.Contains(Cursor))
                {
                    startPoint = Cursor;
                    step = Step.End;
                }
                return;
            }

            float radius = SimVector2.Distance(startPoint, raw);
            if (SnapActive) radius = Mathf.Round(radius / RadiusSnapStep) * RadiusSnapStep;
            radius = Mathf.Clamp(radius, RoadBuilder.MinRoundaboutRadius, RoadBuilder.MaxRoundaboutRadius);
            Cursor = raw;
            CursorAnchor = RoadAnchor.Free(raw);

            currentRoundabout = RoadBuilder.PlanRoundabout(roads, startPoint, radius, RoundaboutType);
            var type = RoundaboutType;
            Guide = new GuideState
            {
                Active = true,
                IsRoundabout = true,
                Center = startPoint,
                Radius = radius,
                Type = type,
                Valid = currentRoundabout.IsValid,
                Error = currentRoundabout.IsValid ? null : ErrorText(currentRoundabout.Error),
                Cost = currentRoundabout.Cost,
                Length = Mathf.PI * 2f * radius,
                Start = startPoint,
                End = startPoint + new SimVector2(radius, 0f),
            };

            previewCurves.Clear();
            int arcs = RoadBuilder.RoundaboutArcCount(radius);
            float sweep = Mathf.PI * 2f / arcs;
            for (int i = 0; i < arcs; i++)
            {
                RoadBuilder.GetArc(startPoint, radius, sweep * i, sweep, out var p0, out var c, out var p2);
                previewCurves.Add((p0, c, p2));
            }
            ShowPreviewMesh(type.Width, currentRoundabout.IsValid);

            if (clicked && currentRoundabout.IsValid)
            {
                RoadBuilder.BuildRoundabout(roads, currentRoundabout);
                Cancel();
            }
        }

        // ------------------------------------------------------------ Abriss

        void UpdateBulldoze(RoadNetwork roads, SimVector2 point, bool clicked)
        {
            Guide = default;
            var segment = roads.FindNearestSegment(point, 2f);
            if (segment == null)
            {
                previewRenderer.enabled = false;
                return;
            }

            previewCurves.Clear();
            previewCurves.Add((segment.Start.Position, segment.Control, segment.End.Position));
            ShowPreviewMesh(segment.Type.Width + 1f, false);

            if (clicked)
            {
                roads.RemoveSegment(segment.Id);
                previewRenderer.enabled = false;
            }
        }

        // ------------------------------------------------------------ Hilfen

        void ShowPreviewMesh(float width, bool valid)
        {
            RoadMeshBuilder.BuildRibbons(previewCurves, width, previewHeight, previewMesh);
            previewRenderer.sharedMaterial = valid ? previewMaterial : invalidMaterial;
            previewRenderer.enabled = true;
        }

        void HidePreview()
        {
            if (previewRenderer != null) previewRenderer.enabled = false;
            Guide = default;
        }

        public static string ErrorText(RoadBuildError error)
        {
            switch (error)
            {
                case RoadBuildError.TooShort: return "Zu kurz";
                case RoadBuildError.SameNode: return "Start und Ende sind derselbe Punkt";
                case RoadBuildError.Duplicate: return "Diese Straße gibt es schon";
                case RoadBuildError.AngleTooSharp: return $"Winkel zu spitz (min. {RoadBuilder.MinJunctionAngle:0}°)";
                case RoadBuildError.HighwayCrossing: return "Autobahnen kreuzen nicht ebenerdig (Brücken folgen)";
                case RoadBuildError.IncompatibleConnection: return "Autobahn nur an Autobahn oder Auf-/Abfahrt anschließen";
                case RoadBuildError.TooCloseToCrossing: return "Zu dicht an einer anderen Kreuzung";
                case RoadBuildError.InvalidRadius: return $"Radius {RoadBuilder.MinRoundaboutRadius:0}–{RoadBuilder.MaxRoundaboutRadius:0} m";
                case RoadBuildError.InvalidType: return "Kreisverkehr braucht eine Einbahn-Stadtstraße";
                default: return null;
            }
        }

        static SimVector2 SafeNormalize(SimVector2 v, SimVector2 fallback)
        {
            if (v.LengthSquared() > 1e-6f) return SimVector2.Normalize(v);
            return fallback.LengthSquared() > 1e-6f ? SimVector2.Normalize(fallback) : SimVector2.UnitY;
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
