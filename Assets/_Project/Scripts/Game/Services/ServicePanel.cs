using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI;
using CityBuilder.Simulation.Services;
using UnityEngine;

namespace CityBuilder.Game.Services
{
    /// <summary>
    /// Kleines Fenster (IMGUI, oben rechts): je Dienstart Anzahl, Kapazität/Belastung und mittlere Abdeckung;
    /// bei gewählter Ansicht die einzelnen Gebäude mit Auslastung. Sichtbar im Dienste-Werkzeug oder bei
    /// eingeschalteter Dienste-Ansicht (nicht im Verkehrsmodus).
    /// </summary>
    public sealed class ServicePanel : MonoBehaviour
    {
        const int MaxRows = 10;

        RoadTool tool;
        ServiceOverlayView overlay;
        GUIStyle header, small;
        Vector2 scroll;

        public void Init(RoadTool roadTool, ServiceOverlayView overlayView)
        {
            tool = roadTool;
            overlay = overlayView;
        }

        void OnGUI()
        {
            if (tool == null || tool.Runner.World == null || tool.CurrentMode == RoadTool.Mode.Traffic) return;
            string kind = overlay != null ? overlay.Current : null;
            if (tool.CurrentMode != RoadTool.Mode.Service && kind == null) return;
            if (header == null)
            {
                header = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, richText = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
            }

            // Neben dem Versorgungsfenster, falls dessen Ansicht gleichzeitig an ist
            var utilityOverlay = GetComponent<CityBuilder.Game.Utilities.UtilityOverlayView>();
            bool besideUtility = utilityOverlay != null && utilityOverlay.Current != CityBuilder.Game.Utilities.UtilityOverlayView.Overlay.None;
            var area = new Rect(Screen.width - (besideUtility ? 800f : 400f), 10f, 390f, Mathf.Min(Screen.height - 170f, 520f));
            UiBlocker.Register(area);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f));
            scroll = GUILayout.BeginScrollView(scroll);

            var s = tool.Runner.World.Services;
            GUILayout.Label($"Dienste · {s.FacilityCount} Gebäude · {s.WeeklyUpkeep():0} €/Woche", header);
            foreach (var k in ServiceKinds.All)
            {
                var list = s.Facilities.Where(f => f.Kind == k).ToList();
                float avg = s.AverageCoverage(k);
                string color = "#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(ServiceOverlayView.CoverageColor(avg), Color.white, 0.25f));
                string load = list.Count == 0 ? "keine gebaut"
                    : $"{list.Count}× · Last {list.Sum(f => f.Load):0}/{list.Sum(f => f.EffectiveCapacity):0} {list[0].Type.CapacityUnit}";
                string bold = k == kind ? "<b>" : "";
                string boldEnd = k == kind ? "</b>" : "";
                GUILayout.Label($"{bold}<color={color}>{ServiceKinds.DisplayName(k)}: Ø {avg * 100f:0} %</color> · {load}{boldEnd}", small);
            }
            GUILayout.Label($"Liegender Müll {s.TotalGarbage:0.#} t", small);

            if (kind != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label(ServiceKinds.DisplayName(kind), header);
                var list = s.Facilities.Where(f => f.Kind == kind).OrderBy(f => f.Id).ToList();
                int shown = 0;
                foreach (var f in list)
                {
                    if (shown++ >= MaxRows) { GUILayout.Label($"… und {list.Count - MaxRows} weitere", small); break; }
                    float u = f.Utilization;
                    string c = !f.HasAccess && !f.Type.Walking ? "#ff7060" : u > 1f ? "#ffb040" : "#7dff8a";
                    string state = !f.HasAccess && !f.Type.Walking ? "keine Zufahrt" : f.IsFull ? "voll" : $"Auslastung {u * 100f:0} %";
                    string extra = f.Type.Storage > 0f ? $" · Lager {f.Stored:0}/{f.Type.Storage:0} t" : "";
                    if (f.Kind == ServiceKinds.Garbage) extra += $" · abgeholt {f.CollectedPerDay:0.#} t/Tag";
                    GUILayout.Label($"<color={c}><b>{f.Type.DisplayName} #{f.Id}</b> · {state}</color>\n" +
                                    $"   {f.ServedBuildings} Gebäude · Last {f.Load:0} / {f.EffectiveCapacity:0} {f.Type.CapacityUnit}{extra}", small);
                }
                if (list.Count == 0) GUILayout.Label("Noch keins gebaut.", small);
                GUILayout.Label("Abdeckung sinkt mit der Fahrzeit (voll bis 40 % der Reichweite) und bei Überlastung (Kapazität < Bedarf).", small);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}
