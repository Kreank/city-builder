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
            string snap = !roadTool.SnapEnabled ? "<color=#ff8080>aus</color>"
                : roadTool.SnapActive ? $"{RoadTool.AngleSnapStep:0}° / {RoadTool.LengthSnapStep:0} m"
                : "<color=#ffd060>frei (Strg)</color>";
            string noise = roadTool.HasCursor ? $"{w.Environment.Noise.Sample(roadTool.Cursor) * 100f:0}%" : "-";

            string text =
                $"<b>Jahr {c.Year + 1}, Tag {c.DayOfYear + 1}  {c.Hour:00}:{c.Minute:00}</b>   Tempo: {speed}\n" +
                $"Einrasten: {snap}   Straßen: {w.Roads.SegmentCount}   Knoten: {w.Roads.NodeCount}\n" +
                $"Lärm am Cursor: {noise}   Overlay: {overlay.Current}\n\n" +
                "<size=12>LMB bauen · RMB/Esc abbrechen · R gerade/Kurve · K Kreisverkehr · B Abriss\n" +
                "Tab / Shift+Tab Straßentyp · G Einrasten · Strg halten = frei\n" +
                "N Lärm · P Abgas · Leertaste Pause · 1/2/3 Tempo\n" +
                "WASD (Shift) bewegen · Q/E/Mitteltaste drehen · Mausrad zoomen</size>";

            var rect = new Rect(10, 10, 520, 175);
            UiBlocker.Register(rect);
            GUI.Box(rect, text, box);
        }
    }
}
