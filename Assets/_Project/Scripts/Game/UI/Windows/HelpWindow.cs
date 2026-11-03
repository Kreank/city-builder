using CityBuilder.Game.Environment;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Hilfe &amp; Tasten (mittiges Fenster, F1 oder „?“ in der oberen Leiste): alle Tastenkürzel plus die
    /// Entwickler-Statistik des früheren IMGUI-<see cref="DebugHud"/> (Straßen/Knoten, Verkehr, Stadt).
    /// </summary>
    public sealed class HelpWindow : GameWindow
    {
        bool open;
        EnvironmentOverlayView envOverlay;

        public HelpWindow() : base(WindowSlot.Center) { }

        public void Toggle() => open = !open;
        public bool IsOpen => open;

        public override float OpenScore => open ? 1f : 0f;
        public override void Close() => open = false;

        public override void Poll()
        {
            var kb = GameKeyboard.Current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) open = !open;
        }

        public override void DrawHead(Ui ui) => Head(ui, "Hilfe (F1)", Tone.Info, "Tasten & Statistik", "Esc oder F1 schließt");

        public override void DrawBody(Ui ui)
        {
            // --- Außenanbindungen: der erste Schritt jeder Stadt
            ui.Section("Start");
            ui.Note("Eine Autobahn führt schon durch die Karte. Baue deine erste Straße an den Zubringer der Anschlussstelle " +
                    "(„hier anschließen“) – erst dann ziehen Leute zu, und Handel/Lkw laufen über die Autobahn. Autobahn und " +
                    "Anschlussstelle gehören der Welt (nicht abreißbar); Bahntrasse und Wasserwege sind vorbereitet und folgen später.");
            ui.Section("Bauen");
            using (ui.Fields())
            {
                ui.Field("LMB / RMB · Esc", "bauen / abbrechen");
                ui.Field("R · K", "Gerade/Kurve · Kreisverkehr");
                ui.Field("U · I · B", "Ausbauen · Richtung umdrehen · Abriss");
                ui.Field("Tab · H", "Straßentyp wechseln · Doppelbau (Autobahn)");
                ui.Field("Bild↑ / Bild↓", "Bauhöhe (0 = automatisch trassieren: Tunnel/Brücke/einebnen)");
                ui.Field("G · Strg", "Einrasten an/aus · halten = frei");
                ui.Field("F9 · F10", "Parallelstraßen: Reihen 4→3→2→1→aus · Blockraster");
                ui.Field(", .", "Vorlage/Bauwerk drehen");
                ui.Field("Z · [ ]", "Zonen · Pinselgröße");
                ui.Field("L", "Gelände formen");
                ui.Field("J", "Versorgung (Strom, Wasser)");
                ui.Field("4 · ⇧4", "Dienste · Abdeckungs-Ansicht durchschalten");
                ui.Field("5 · F6", "ÖPNV (Haltestelle, Linie zeichnen, Busdepot) · Linienübersicht");
            }
            ui.Section("Ansehen");
            using (ui.Fields())
            {
                ui.Field("Esc · Rechtsklick", "Vorgang abbrechen, sonst zurück zum Auswählen (Standard: Linksklick wählt aus)");
                ui.Field("C · F", "Stadtübersicht · Fahrzeug/Bürger verfolgen");
                ui.Field("Y", "Ausgewählten Dienst/Versorger/Depot verschieben (, . drehen, Klick setzt)");
                ui.Field("T · V", "Verkehrsregeln · Stau-Ansicht");
                ui.Field("N · P · O", "Lärm · Abgas · Strom/Wasser-Ansicht");
                ui.Field("X", "Unterirdisch-Ansicht");
                ui.Field("F2 · F3 · F4", "Bodenwert · Rohstoffe durchschalten · Weltkarte");
                ui.Field("M", "Stadtkasse");
                ui.Field("F5", "Wirtschaft (Güter, Preise, Handel, Firmen)");
                ui.Field("F11", "Handel & Politik (Länder, Weltpreise, Angebote, Verträge, Zölle)"); // Etappe 8
                ui.Field("F1", "diese Hilfe");
            }
            ui.Section("Spiel");
            using (ui.Fields())
            {
                ui.Field("Esc", "Spielmenü (wenn nichts anderes offen ist): Speichern, Laden, Neues Spiel, Autosave");
                ui.Field("Strg+S · Strg+L", "Schnell speichern · Schnell laden");
            }
            ui.Section("Zeit & Kamera");
            using (ui.Fields())
            {
                ui.Field("Leertaste · 1/2/3", "Pause · Tempo");
                ui.Field("WASD (Shift)", "bewegen (schnell)");
                ui.Field("Q/E · Mitteltaste", "drehen");
                ui.Field("Mausrad", "zoomen");
            }

            var w = World;
            var c = w.Clock;
            var s = w.Traffic.Stats;
            if (envOverlay == null) envOverlay = Object.FindAnyObjectByType<EnvironmentOverlayView>();
            ui.Section("Entwickler-Statistik");
            using (ui.Fields())
            {
                ui.Field("Zeit", $"Jahr {c.Year + 1}, Tag {c.DayOfYear + 1} · {c.Hour:00}:{c.Minute:00} · " +
                                 (Runner.IsPaused ? "Pause" : $"Tempo {Runner.SpeedIndex} (Sim {Runner.LastFrameSimMilliseconds:0.0} ms/Frame)"));
                string snap = !Tool.SnapEnabled ? "aus" : Tool.SnapActive ? $"{RoadTool.AngleSnapStep:0}° / {RoadTool.LengthSnapStep:0} m" : "frei (Strg)";
                ui.Field("Straßen / Knoten", $"{w.Roads.SegmentCount} / {w.Roads.NodeCount} · Einrasten {snap}");
                ui.Field("Lärm am Cursor", Tool.HasCursor ? $"{w.Environment.Noise.Sample(Tool.Cursor) * 100f:0} %" : "–");
                ui.Field("Overlay", envOverlay != null ? envOverlay.Current.ToString() : "–");
                ui.Field("Verkehr", $"{s.Active} Fahrzeuge · Ø {s.AverageSpeedKmh:0} km/h · {s.Waiting} warten");
                ui.Field("Fahrten", $"angekommen {s.Arrived} · hängen geblieben {s.Despawned}", s.Despawned > 0 ? Tone.Accent : Tone.None);
                ui.Field("Stadt", $"{w.City.Stats.Population} Einwohner · {w.City.Stats.OccupiedHouseholds}/{w.City.Stats.Households} Haushalte");
                ui.Field("Jobs / Gebäude", $"{w.City.Stats.Workers}/{w.City.Stats.Jobs} · {w.City.Stats.Buildings} (+{w.City.Stats.UnderConstruction} im Bau)");
                ui.Field("Arbeit", $"arbeitslos {w.Citizens.Stats.Unemployed} · Auspendler {w.Citizens.Stats.CommutersOut} · Einpendler {w.Citizens.Stats.CommutersIn}");
            }
        }
    }
}
