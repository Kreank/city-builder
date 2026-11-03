using CityBuilder.Game.Environment;
using CityBuilder.Game.Roads;
using UnityEngine;

namespace CityBuilder.Game.UI
{
    /// <summary>
    /// Provisorische Entwickler-Anzeige per IMGUI (Statistik + Tastenhilfe, F1). Datum, Tempo, Einwohner und
    /// Arbeitslose zeigt inzwischen die obere Leiste der Spiel-UI (<see cref="Hud.GameHud"/>).
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
                $"   angekommen {s.Arrived} · <color=#ff9080>hängen geblieben {s.Despawned}</color>\n" +
                $"Stadt: {w.City.Stats.Population} Einwohner · {w.City.Stats.OccupiedHouseholds}/{w.City.Stats.Households} Haushalte · " +
                $"{w.City.Stats.Workers}/{w.City.Stats.Jobs} Jobs · {w.City.Stats.Buildings} Gebäude (+{w.City.Stats.UnderConstruction} im Bau)\n" +
                $"   arbeitslos {w.Citizens.Stats.Unemployed} · Auspendler {w.Citizens.Stats.CommutersOut} · Einpendler {w.Citizens.Stats.CommutersIn} (C Bürger-Info)";

            if (showHelp)
                text += "\n\n<size=11>LMB bauen · RMB/Esc abbrechen · R gerade/Kurve · K Kreisverkehr · U ausbauen · I Richtung · B Abriss\n" +
                        "Tab Straßentyp · H Doppelbau · Bild↑/↓ Bauhöhe · G Einrasten (Strg = frei) · , . Vorlage drehen\n" +
                        "T Verkehrsregeln · V Stau-Ansicht · X Unterirdisch · Z Zonen ([ ] Pinsel) · C Bürger-Info · N Lärm · P Abgas\n" +
                        "L Gelände · J Versorgung · O Strom/Wasser-Ansicht\n" +
                        "Leertaste Pause · 1/2/3 Tempo · Bauhöhe 0 = automatisch trassieren (Tunnel/Brücke/einebnen)\n" +
                        "WASD (Shift) bewegen · Q/E/Mitteltaste drehen · Mausrad zoomen · F1 Hilfe aus</size>";

            // Unter der oberen Leiste / neben der Ansichtsleiste der neuen UI (Platz für den Ansicht-Hinweis darüber)
            float top = HudLayout.TopInset > 11f ? HudLayout.TopInset + 46f : 10f;
            var rect = new Rect(HudLayout.LeftInset, top, 600, showHelp ? 228 : 128);
            UiBlocker.Register(rect);
            GUI.Box(rect, text, box);

            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F1) { showHelp = !showHelp; e.Use(); }
        }
    }
}
