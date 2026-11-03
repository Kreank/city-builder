using CityBuilder.Game.Roads;
using CityBuilder.Game.UI;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Inspect
{
    /// <summary>
    /// Verschieben-Werkzeug (UX) für Dienstgebäude, Versorgungsbauwerke und Busdepots: im Auswählen-Modus ausgewähltes
    /// Bauwerk → Knopf „Verschieben“ im Inspektor bzw. <b>Taste Y</b> (einzige freie Buchstabentaste; gelesen über das
    /// gedruckte Zeichen der aktuellen Tastaturbelegung) → Geistervorschau folgt der Maus mit denselben Platzregeln wie
    /// beim Bau (inkl. Straßenausrichtung, , . drehen), Klick setzt, Esc/Rechtsklick bricht ab. Kosten: Umzugspreis
    /// (10 % der Baukosten) über die Stadtkasse; das Objekt behält Id, Zustand, Lager und Statistik.
    /// Die Simulation prüft und verschiebt (<c>MoveFacility</c>/<c>MoveDepot</c>).
    /// </summary>
    public sealed class FacilityMover : MonoBehaviour
    {
        /// <summary>Umzugspreis als Anteil der Baukosten (Anzeige; maßgeblich sind die Simulationswerte).</summary>
        public const float CostShare = ServiceSystem.MoveCostShare;

        RoadTool tool;
        InspectorPanel inspector;
        Material okMaterial, badMaterial;
        PlacementGhost ghost;
        string label;
        bool labelError;
        Vector3 labelWorld;

        public SelectionKind MovingKind { get; private set; }
        public int MovingId { get; private set; }
        public bool Moving => MovingKind != SelectionKind.None;
        /// <summary>Drehung zur Straße (bzw. absolut ohne Straße in der Nähe), , und . ändern sie.</summary>
        public float Rotation { get; private set; }

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            inspector = GetComponent<InspectorPanel>();
            okMaterial = new Material(transparent) { name = "MovePreviewOk", color = new Color(0.3f, 0.9f, 1f, 0.45f) };
            badMaterial = new Material(transparent) { name = "MovePreviewBad", color = new Color(1f, 0.25f, 0.2f, 0.5f) };
            ghost = new PlacementGhost(transform, "Move Preview");
            // Esc/Rechtsklick brechen das Verschieben ab (statt den Modus zu wechseln)
            tool.RegisterOperation(() => Moving, Cancel);
        }

        void OnDestroy()
        {
            if (okMaterial != null) Destroy(okMaterial);
            if (badMaterial != null) Destroy(badMaterial);
        }

        public static bool CanMove(SelectionKind kind) =>
            kind == SelectionKind.Service || kind == SelectionKind.Facility || kind == SelectionKind.TransitDepot
            || kind == SelectionKind.Building; // Wohnhäuser setzen: nur gesetzte Häuser (Begin prüft)

        /// <summary>Verschieben beginnen (wechselt ins Auswählen, falls nötig).</summary>
        public void Begin(SelectionKind kind, int id)
        {
            if (!CanMove(kind) || tool == null) return;
            // Wohnhäuser setzen: von Gebäuden lassen sich nur vom Spieler gesetzte Häuser verschieben
            if (kind == SelectionKind.Building && !(tool.Runner?.World?.City.GetBuilding(id)?.IsPlayerPlaced ?? false)) return;
            if (!tool.IsSelectMode) tool.SetMode(RoadTool.SelectMode);
            MovingKind = kind;
            MovingId = id;
            Rotation = 0f;
        }

        public void Cancel()
        {
            MovingKind = SelectionKind.None;
            MovingId = 0;
            ghost?.Hide();
            label = null;
        }

        void Update()
        {
            if (tool == null || tool.Runner == null || tool.Runner.World == null) return;
            var kb = GameKeyboard.Current;
            if (kb == null) return;
            var moveKey = kb.FindKeyOnCurrentKeyboardLayout("y") ?? kb.yKey;
            if (moveKey != null && moveKey.wasPressedThisFrame && tool.IsSelectMode)
            {
                if (Moving) Cancel();
                else if (inspector != null && CanMove(inspector.Current.Kind)) Begin(inspector.Current.Kind, inspector.Current.Id);
            }
            if (!Moving) return;
            if (kb.commaKey.wasPressedThisFrame) Rotation = Mathf.Repeat(Rotation - 15f, 360f);
            if (kb.periodKey.wasPressedThisFrame) Rotation = Mathf.Repeat(Rotation + 15f, 360f);
        }

        void LateUpdate()
        {
            label = null;
            if (!Moving) return;
            if (tool == null || !tool.IsSelectMode || tool.Runner.World == null) { Cancel(); return; }
            if (!tool.HasCursor) { ghost.Hide(); return; }
            var mouse = Mouse.current;
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            var w = tool.Runner.World;
            var p = tool.Cursor;
            bool done;
            switch (MovingKind)
            {
                case SelectionKind.Service: done = UpdateService(w.Services, p, clicked); break;
                case SelectionKind.Facility: done = UpdateUtility(w.Utilities, p, clicked); break;
                case SelectionKind.TransitDepot: done = UpdateDepot(w.Transit, p, clicked); break;
                case SelectionKind.Building: done = UpdateHouse(w.City, p, clicked); break; // Wohnhäuser setzen
                default: done = true; break;
            }
            if (done) Cancel();
        }

        // Rückgabe true = fertig (verschoben oder Objekt weg)

        bool UpdateService(ServiceSystem s, SimVector2 p, bool clicked)
        {
            var f = s.GetFacility(MovingId);
            if (f == null) return true;
            var plan = s.PlanAligned(f.Type, p, Rotation, f.Id);
            var t = f.Type;
            Show(plan.Position, plan.Front, t.Width, t.Depth, plan.Height, 6f, plan.IsValid, t.NeedsRoadFront);
            Label(t.DisplayName, plan.Cost, plan.Aligned, plan.Displaced.Count, plan.IsValid ? null : CityBuilder.Game.Services.ServiceTool.ErrorText(plan.Error),
                plan.Position, plan.Height);
            return clicked && plan.IsValid && s.MoveFacility(plan);
        }

        bool UpdateUtility(UtilitySystem u, SimVector2 p, bool clicked)
        {
            var f = u.GetFacility(MovingId);
            if (f == null) return true;
            var plan = u.PlanFacilityAligned(f.Type, p, Rotation, f.Id);
            var t = f.Type;
            Show(plan.Position, plan.Front, t.Width, t.Depth, plan.Height, 5f, plan.IsValid, t.NeedsRoadFront);
            Label(t.DisplayName, plan.Cost, plan.Aligned, plan.Displaced.Count, plan.IsValid ? null : CityBuilder.Game.Utilities.UtilityTool.ErrorText(plan.Error),
                plan.Position, plan.Height);
            return clicked && plan.IsValid && u.MoveFacility(plan);
        }

        bool UpdateDepot(CityBuilder.Simulation.Transit.TransitSystem t, SimVector2 p, bool clicked)
        {
            var d = t?.GetDepot(MovingId);
            if (d == null) return true;
            var pos = p;
            float rot = Rotation;
            bool aligned = t.SnapDepot(d.Type, p, out var snapped, out float snapRot);
            if (aligned) { pos = snapped; rot = snapRot + Rotation; }
            var plan = t.PlanMoveDepot(d.Id, pos, rot);
            Show(plan.Position, plan.Front, d.Type.Width, d.Type.Depth, plan.Height, 8f, plan.IsValid, true);
            Label(d.Type.DisplayName, (float)plan.Cost, aligned, plan.Displaced.Count, plan.IsValid ? null : CityBuilder.Game.Transit.TransitTool.ErrorText(plan.Error),
                plan.Position, plan.Height);
            return clicked && plan.IsValid && t.MoveDepot(plan);
        }

        // --- Wohnhäuser setzen: gesetztes Haus verschieben (Id, Stufe, Bewohner bleiben; 10 % der Baukosten)
        bool UpdateHouse(CityBuilder.Simulation.Zoning.CitySystem city, SimVector2 p, bool clicked)
        {
            var b = city.GetBuilding(MovingId);
            var type = CityBuilder.Simulation.Zoning.HouseCatalog.Of(b);
            if (type == null) return true;
            var plan = city.PlanMoveHouse(b.Id, p, Rotation);
            Show(plan.Position, plan.Front, type.Width, type.Depth, plan.Height, 8f, plan.IsValid, true);
            Label(type.DisplayName, plan.Cost, plan.Aligned, 0, plan.IsValid ? null : CityBuilder.Game.Zoning.HouseTool.ErrorText(plan),
                plan.Position, plan.Height);
            return clicked && plan.IsValid && city.MoveHouse(plan);
        }
        // --- Ende Wohnhäuser

        void Show(SimVector2 pos, SimVector2 front, float width, float depth, float height, float tall, bool valid, bool showFront) =>
            ghost.Show(pos, front, width, depth, height, tall, valid ? okMaterial : badMaterial, showFront);

        void Label(string name, float cost, bool aligned, int displaced, string error, SimVector2 pos, float height)
        {
            labelError = error != null;
            labelWorld = SimConvert.ToWorld(pos, height + 12f);
            if (error != null) { label = error; return; }
            label = $"Verschieben: {name} · Umzug {cost:0} €" + (aligned ? " · zur Straße ausgerichtet" : "") +
                    (Rotation != 0f ? $" · {Rotation:0}°" : "") + (displaced > 0 ? $" · reißt {displaced} Gebäude ab" : "") +
                    " · , . drehen · Klick setzt · Esc bricht ab";
        }

        void OnGUI()
        {
            if (!CityBuilder.Game.UI.Panels.WorldLabels.Enabled) return; // IMGUI-Rückfall: nur die Vorschau
            if (Event.current.type != EventType.Layout) return;
            CityBuilder.Game.UI.Panels.WorldLabels.Begin(this);
            if (Moving && !string.IsNullOrEmpty(label))
                CityBuilder.Game.UI.Panels.WorldLabels.Add(this, labelWorld, label,
                    labelError ? CityBuilder.Game.UI.Panels.WorldLabelStyle.Error : CityBuilder.Game.UI.Panels.WorldLabelStyle.Normal, -6f, above: true);
        }
    }
}
