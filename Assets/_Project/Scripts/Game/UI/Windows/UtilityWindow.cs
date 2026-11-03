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
            }
            ui.Section("Folgen");
            using (ui.Fields())
            {
                ui.Field("Unterhalt gesamt", $"{BuildLabels.Money(u.TotalUpkeepPerDay)}/Tag");
                ui.Field("Verlassene Gebäude", u.AbandonedBuildings.ToString(), u.AbandonedBuildings > 0 ? Tone.Warn : Tone.None);
            }
            ui.Note($"Ohne Versorgung länger als {UtilitySystem.AbandonAfterTicks / SimulationClock.TicksPerDay} Tage → Gebäude wird verlassen " +
                    "(erst, wenn es mind. einen Versorger dieser Art gibt).");
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

            var nets = u.Networks(kind).Where(n => n.Facilities > 0 || n.Buildings > 0)
                .OrderByDescending(n => n.Facilities > 0).ThenByDescending(n => n.Demand).ToList();
            if (nets.Count == 0) { ui.Note("Keine Netze."); return; }
            using (ui.Card())
            {
                int shown = 0;
                foreach (var n in nets)
                {
                    if (shown++ >= MaxRows) { ui.Note($"… und {nets.Count - MaxRows} weitere Netze"); break; }
                    Tone tone = n.Facilities == 0 ? Tone.Bad : n.HasShortage ? Tone.Warn : Tone.Good;
                    string state = n.Facilities == 0 ? "kein Versorger angeschlossen" : n.HasShortage ? $"Unterdeckung ({n.Coverage * 100f:0} %)" : "ausreichend";
                    ui.Meter($"Netz {n.Id + 1}", state, n.Facilities == 0 ? 0f : n.Coverage, tone);
                    ui.Note($"Erzeugung {n.Production:0} {unit} · Bedarf {n.Demand:0} {unit} · versorgt {n.SuppliedBuildings}/{n.Buildings} Gebäude · " +
                            $"{n.Facilities} Bauwerke · {n.Lines} Leitungen");
                }
            }
        }
    }
}
