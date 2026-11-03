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
        bool showHelp = true;

        void OnGUI()
        {
            if (runner == null || runner.World == null) return;
            if (box == null)
            {
                box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };
                box.padding = new RectOffset(10, 10, 8, 8);
            }

            var w = runner.World;
            var c = w.Clock;
            var s = w.Traffic.Stats;
            string speed = runner.IsPaused ? "<color=#ff8080>PAUSE</color>" : $"{runner.SpeedMultiplier}x";
            string snap = !roadTool.SnapEnabled ? "<color=#ff8080>aus</color>"
                : roadTool.SnapActive ? $"{RoadTool.AngleSnapStep:0}° / {RoadTool.LengthSnapStep:0} m"
                : "<color=#ffd060>frei (Strg)</color>";
            string noise = roadTool.HasCursor ? $"{w.Environment.Noise.Sample(roadTool.Cursor) * 100f:0}%" : "-";

            string text =
                $"<b>Jahr {c.Year + 1}, Tag {c.DayOfYear + 1}  {c.Hour:00}:{c.Minute:00}</b>   Tempo: {speed}\n" +
                $"Straßen: {w.Roads.SegmentCount}  Knoten: {w.Roads.NodeCount}   Einrasten: {snap}   Lärm: {noise}   Overlay: {overlay.Current}\n" +
                $"Verkehr: {s.Active} Fahrzeuge · Ø {s.AverageSpeedKmh:0} km/h · {s.Waiting} warten\n" +
                $"   angekommen {s.Arrived} · <color=#ff9080>hängen geblieben {s.Despawned}</color>";

            if (showHelp)
                text += "\n\n<size=11>LMB bauen · RMB/Esc abbrechen · R gerade/Kurve · K Kreisverkehr · U ausbauen · I Richtung · B Abriss\n" +
                        "Tab Straßentyp · H Doppelbau · Bild↑/↓ Bauhöhe · G Einrasten (Strg = frei) · , . Vorlage drehen\n" +
                        "T Verkehrsregeln · V Stau-Ansicht · X Unterirdisch · N Lärm · P Abgas · Leertaste Pause · 1/2/3 Tempo\n" +
                        "WASD (Shift) bewegen · Q/E/Mitteltaste drehen · Mausrad zoomen · F1 Hilfe aus</size>";

            var rect = new Rect(10, 10, 560, showHelp ? 175 : 90);
            UiBlocker.Register(rect);
            GUI.Box(rect, text, box);

            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F1) { showHelp = !showHelp; e.Use(); }
        }
    }
}
