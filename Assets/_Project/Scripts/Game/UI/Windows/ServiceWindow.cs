using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Game.Services;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Services;

namespace CityBuilder.Game.UI.Windows
{
    /// <summary>
    /// Dienste (rechtes Panel): je Dienstart Anzahl, Last/Kapazität und mittlere Abdeckung; bei gewählter Ansicht die
    /// einzelnen Gebäude mit Auslastung. Offen im Dienste-Werkzeug (4) oder bei eingeschalteter Abdeckungs-Ansicht (⇧4).
    /// Gleiche Inhalte wie das frühere IMGUI-Fenster <see cref="ServicePanel"/>.
    /// </summary>
    public sealed class ServiceWindow : GameWindow
    {
        const int MaxRows = 10;
        ServiceOverlayView overlay;

        public ServiceWindow() : base(WindowSlot.Right) { }

        string Kind => overlay != null ? overlay.Current : null;

        public override float OpenScore
        {
            get
            {
                if (overlay == null) overlay = Tool.GetComponent<ServiceOverlayView>();
                if (Tool.CurrentMode == RoadTool.Mode.Service) return 90f;
                return Kind != null && !CityBuilder.Simulation.Safety.SafetyOverlays.IsSafety(Kind) ? 50f : 0f; // Sicherheit: eigene Ansichten ohne Dienste-Fenster
            }
        }

        public override bool HandleEscape() => false;

        public override void DrawHead(Ui ui)
        {
            var s = World.Services;
            Head(ui, "Dienste (4 · ⇧4)", Tone.Info, Kind != null ? "Abdeckung " + ServiceKinds.DisplayName(Kind) : "Dienste",
                $"{s.FacilityCount} Gebäude · Unterhalt {BuildLabels.Money((float)s.WeeklyUpkeep())}/Woche");
        }

        public override void DrawBody(Ui ui)
        {
            var s = World.Services;
            string kind = Kind;
            ui.Section("Abdeckung je Dienstart", "Ø über alle Gebäude");
            using (ui.Card())
                foreach (var k in ServiceKinds.All)
                {
                    var list = s.Facilities.Where(f => f.Kind == k).ToList();
                    float avg = s.AverageCoverage(k);
                    string load = list.Count == 0 ? "keine gebaut"
                        : $"{list.Count}× · Last {list.Sum(f => f.Load):0}/{list.Sum(f => f.EffectiveCapacity):0} {list[0].Type.CapacityUnit}";
                    ui.Meter((k == kind ? "▸ " : "") + ServiceKinds.DisplayName(k), $"Ø {avg * 100f:0} % · {load}", avg,
                        avg >= 0.6f ? Tone.Good : avg >= 0.3f ? Tone.Accent : Tone.Warn);
                }
            using (ui.Fields()) ui.Field("Liegender Müll", $"{s.TotalGarbage:0.#} t");
            if (overlay != null)
                using (ui.Actions())
                    ui.ActionButton("Ansicht durchschalten (⇧4)", overlay.Cycle, ActionStyle.Secondary, overlay.Selected != null);

            if (kind == null) return;
            ui.Section(ServiceKinds.DisplayName(kind), "Gebäude");
            var items = s.Facilities.Where(f => f.Kind == kind).OrderBy(f => f.Id).ToList();
            if (items.Count == 0) ui.Note("Noch keins gebaut.");
            else
                using (ui.Card())
                {
                    int shown = 0;
                    foreach (var f in items)
                    {
                        if (shown++ >= MaxRows) { ui.Note($"… und {items.Count - MaxRows} weitere"); break; }
                        float u = f.Utilization;
                        bool noAccess = !f.HasAccess && !f.Type.Walking;
                        string state = noAccess ? "keine Zufahrt" : f.IsFull ? "voll" : $"Auslastung {u * 100f:0} %";
                        string extra = f.Type.Storage > 0f ? $" · Lager {f.Stored:0}/{f.Type.Storage:0} t" : "";
                        if (f.Kind == ServiceKinds.Garbage) extra += $" · abgeholt {f.CollectedPerDay:0.#} t/Tag";
                        ui.Meter($"{f.Type.DisplayName} #{f.Id}", state, noAccess ? 0f : UnityEngine.Mathf.Clamp01(u),
                            noAccess ? Tone.Bad : u > 1f || f.IsFull ? Tone.Warn : Tone.Good);
                        ui.Note($"{f.ServedBuildings} Gebäude · Last {f.Load:0} / {f.EffectiveCapacity:0} {f.Type.CapacityUnit}{extra}");
                    }
                }
            ui.Note("Abdeckung sinkt mit der Fahrzeit (voll bis 40 % der Reichweite) und bei Überlastung (Kapazität < Bedarf).");
        }
    }
}
