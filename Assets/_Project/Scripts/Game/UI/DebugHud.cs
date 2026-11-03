using CityBuilder.Game.Environment;
using CityBuilder.Game.Roads;
using UnityEngine;

namespace CityBuilder.Game.UI
{
    /// <summary>
    /// Provisorische Anzeige per IMGUI. Wird später durch echte UI (UI Toolkit) ersetzt.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        [SerializeField] SimulationRunner runner;
        [SerializeField] RoadTool roadTool;
        [SerializeField] EnvironmentOverlayView overlay;

        GUIStyle box;

        void OnGUI()
        {
            if (runner == null || runner.World == null) return;
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, richText = true };
                box.padding = new RectOffset(10, 10, 8, 8);
            }

            var w = runner.World;
            var c = w.Clock;
            string speed = runner.IsPaused ? "<color=#ff8080>PAUSE</color>" : $"{runner.SpeedMultiplier}x";

            string tool = roadTool.CurrentMode switch
            {
                RoadTool.Mode.Straight => $"Straße gerade – {roadTool.CurrentType.DisplayName}",
                RoadTool.Mode.Curve => $"Straße Kurve – {roadTool.CurrentType.DisplayName}",
                _ => "<color=#ff8080>Abriss</color>",
            };

            string noise = roadTool.HasCursor ? $"{w.Environment.Noise.Sample(roadTool.Cursor) * 100f:0}%" : "-";
            string length = roadTool.PreviewLength > 0f
                ? $"\nLänge: {roadTool.PreviewLength:0} m   Kosten: {roadTool.PreviewLength * roadTool.CurrentType.CostPerMeter:0}"
                : "";

            string snap = !roadTool.SnapEnabled ? "<color=#ff8080>aus</color>"
                : roadTool.SnapActive ? $"{RoadTool.AngleSnapStep:0}° / {RoadTool.LengthSnapStep:0} m"
                : "<color=#ffd060>frei (Strg)</color>";

            string text =
                $"<b>Jahr {c.Year + 1}, Tag {c.DayOfYear + 1}  {c.Hour:00}:{c.Minute:00}</b>   Tempo: {speed}\n" +
                $"Werkzeug: {tool}   Einrasten: {snap}{length}\n" +
                $"Straßen: {w.Roads.SegmentCount}   Knoten: {w.Roads.NodeCount}   Lärm am Cursor: {noise}   Overlay: {overlay.Current}\n\n" +
                "<size=12>LMB bauen · RMB/Esc abbrechen · R gerade/Kurve · Tab Straßentyp · B Abriss\n" +
                "G Einrasten an/aus · Strg halten = frei bauen\n" +
                "N Lärm-Overlay · P Abgas-Overlay · Leertaste Pause · 1/2/3 Tempo\n" +
                "WASD bewegen (Shift schneller) · Q/E oder Mitteltaste drehen · Mausrad zoomen</size>";

            GUI.Box(new Rect(10, 10, 600, 170), text, box);
        }
    }
}
