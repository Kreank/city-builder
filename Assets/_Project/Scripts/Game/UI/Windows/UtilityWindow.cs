using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Game.Utilities;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Utilities;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Versorgungsnetze (rechtes Panel): Erzeugung/Bedarf je Netz – offen im Versorgungs-Werkzeug (J) oder bei
    /// eingeschalteter Strom-/Wasser-Ansicht (O). Gleiche Inhalte wie das frühere IMGUI-Fenster <see cref="UtilityPanel"/>.
    /// </summary>
    public sealed class UtilityWindow : GameWindow
    {
        const int MaxRows = 10;
        UtilityOverlayView overlay;

        public UtilityWindow() : base(WindowSlot.Right) { }

        bool OverlayOn => overlay != null && overlay.Current != UtilityOverlayView.Overlay.None;

        public override float OpenScore
        {
            get
            {
                if (overlay == null) overlay = Tool.GetComponent<UtilityOverlayView>();
                if (Tool.CurrentMode == RoadTool.Mode.Utility) return 90f;
                return OverlayOn ? 50f : 0f;
            }
        }

        /// <summary>Esc bleibt beim Werkzeug (bricht dort die Leitung ab).</summary>
        public override bool HandleEscape() => false;

        public override void DrawHead(Ui ui)
        {
            var u = World.Utilities;
            string title = OverlayOn ? UtilityCatalog.KindName(overlay.Kind) + "netz" : "Strom & Wasser";
            Head(ui, "Versorgung (J · O)", Tone.Accent, title, $"Unterhalt {BuildLabels.Money(u.TotalUpkeepPerDay)}/Tag · verlassen {u.AbandonedBuildings}");
        }

        public override void DrawBody(Ui ui)
        {
            var u = World.Utilities;
            if (OverlayOn) Section(ui, u, overlay.Kind);
            else
            {
                Section(ui, u, UtilityKind.Power);
                Section(ui, u, UtilityKind.Water);
                Section(ui, u, UtilityKind.Sewage); // G3 erweitert
            }
            ui.Section("Folgen");
            using (ui.Fields())
            {
                ui.Field("Unterhalt gesamt", $"{BuildLabels.Money(u.TotalUpkeepPerDay)}/Tag");
                ui.Field("Verlassene Gebäude", u.AbandonedBuildings.ToString(), u.AbandonedBuildings > 0 ? Tone.Warn : Tone.None);
            }
            ui.Note($"Ohne Versorgung länger als {UtilitySystem.AbandonAfterTicks / SimulationClock.TicksPerDay} Tage → Gebäude wird verlassen " +
                    "(erst, wenn es mind. einen Versorger dieser Art gibt).");
            ui.Note($"Netze selbst verlegen: Straßen führen nichts mit. Ein Gebäude ist angeschlossen, wenn Ortsnetz/Rohr/Kanal ≤ " +
                    $"{ConnectionRules.BuildingReach:0} m an seiner Zufahrt vorbeiführt – „Entlang Straßen“ in der Bauleiste verlegt ganze Straßen auf einmal.");
        }

        static void Section(Ui ui, UtilitySystem u, UtilityKind kind)
        {
            string unit = UtilityCatalog.Unit(kind);
            float prod = u.TotalProduction(kind), dem = u.TotalDemand(kind);
            ui.Section(UtilityCatalog.KindName(kind), $"{BuildLabels.Number(prod)} / {BuildLabels.Number(dem)} {unit}");
            using (ui.Grid(2))
            {
                ui.Stat("Erzeugung", BuildLabels.Number(prod), unit);
                ui.Stat("Bedarf", BuildLabels.Number(dem), unit, dem > prod + 0.001f ? Tone.Warn : Tone.None);
            }
            if (!u.IsRequired(kind))
                ui.Message("Noch kein Versorger gebaut", $"Gebäude bleiben vorerst ohne {UtilityCatalog.KindName(kind)} (folgenlos).", Tone.Accent);
            if (kind == UtilityKind.Power)
            {
                float cap = 0f, load = 0f;
                int count = 0;
                foreach (var s in u.Substations) { cap += s.Type.Capacity; load += s.Load; count++; }
                ui.Note(count == 0
                    ? "Kein Umspannwerk: Kraftwerk → Hochspannung → Umspannwerk → Ortsnetz → Gebäude (Wind/Solar dürfen direkt ins Ortsnetz)."
                    : $"{count} Umspannwerke · Last {BuildLabels.Number(load / 1000f)} von {BuildLabels.Number(cap / 1000f)} MW");
            }

            var nets = u.Networks(kind).Where(n => n.Facilities > 0 || n.Buildings > 0 || n.Substations > 0)
                .OrderByDescending(n => n.Level == GridLevel.HighVoltage).ThenByDescending(n => n.HasSource).ThenByDescending(n => n.Demand).ToList();
            if (nets.Count == 0) { ui.Note("Keine Netze."); return; }
            using (ui.Card())
            {
                int shown = 0;
                foreach (var n in nets)
                {
                    if (shown++ >= MaxRows) { ui.Note($"… und {nets.Count - MaxRows} weitere Netze"); break; }
                    Tone tone = !n.HasSource ? Tone.Bad : n.HasShortage ? Tone.Warn : Tone.Good;
                    ui.Meter(NetName(n), NetState(n), n.HasSource ? n.Coverage : 0f, tone);
                    ui.Note(NetDetails(n, unit));
                }
            }
        }

        /// <summary>„Netz 3“ bzw. beim Strom „Hochspannung 1“ / „Ortsnetz 3“.</summary>
        public static string NetName(UtilityNetwork n) =>
            n.Kind == UtilityKind.Power ? $"{UtilityCatalog.LevelName(n.Level)} {n.Id + 1}" : $"Netz {n.Id + 1}";

        public static string NetState(UtilityNetwork n)
        {
            if (!n.HasSource)
                return n.Kind == UtilityKind.Power && n.Level == GridLevel.Distribution
                    ? "kein Umspannwerk/Erzeuger angeschlossen"
                    : "kein Versorger angeschlossen";
            return n.HasShortage ? $"Unterdeckung ({n.Coverage * 100f:0} %)" : "ausreichend";
        }

        public static string NetDetails(UtilityNetwork n, string unit)
        {
            if (n.Kind == UtilityKind.Power && n.Level == GridLevel.HighVoltage)
                return $"Erzeugung {n.Generation:0} {unit} · an Umspannwerke {n.Delivered:0} von {n.Demand:0} {unit} angefordert · " +
                       $"{n.Substations} Umspannwerke ({BuildLabels.Number(n.TransformerCapacity / 1000f)} MW) · {n.Facilities} Kraftwerke · {n.Lines} Leitungen";
            if (n.Kind == UtilityKind.Power)
                return $"Verfügbar {n.Production:0} {unit} (eigene Erzeugung {n.Generation:0}, über Umspannwerke {n.Imported:0} von " +
                       $"{n.TransformerCapacity:0}) · Bedarf {n.Demand:0} {unit} · versorgt {n.SuppliedBuildings}/{n.Buildings} Gebäude · {n.Lines} Leitungen";
            return $"Erzeugung {n.Production:0} {unit} · Bedarf {n.Demand:0} {unit} · versorgt {n.SuppliedBuildings}/{n.Buildings} Gebäude · " +
                   $"{n.Facilities} Bauwerke · {n.Lines} Leitungen";
        }
    }
}
