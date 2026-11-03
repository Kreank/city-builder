using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI;
using CityBuilder.Simulation.Utilities;
using UnityEngine;

namespace CityBuilder.Game.Utilities
{
    /// <summary>
    /// Fenster (IMGUI, oben rechts) mit Erzeugung/Bedarf je Netz – sichtbar im Versorgungs-Werkzeug oder
    /// bei eingeschalteter Strom-/Wasser-Ansicht (nicht im Verkehrsmodus, dort liegt das Regel-Fenster).
    /// </summary>
    public sealed class UtilityPanel : MonoBehaviour
    {
        const int MaxRows = 10;

        RoadTool tool;
        UtilityOverlayView overlay;
        GUIStyle header, small;
        Vector2 scroll;

        public void Init(RoadTool roadTool, UtilityOverlayView overlayView)
        {
            tool = roadTool;
            overlay = overlayView;
        }

        void OnGUI()
        {
            if (tool == null || tool.Runner.World == null || tool.CurrentMode == RoadTool.Mode.Traffic) return;
            bool overlayOn = overlay != null && overlay.Current != UtilityOverlayView.Overlay.None;
            if (tool.CurrentMode != RoadTool.Mode.Utility && !overlayOn) return;
            if (header == null)
            {
                header = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, richText = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
            }

            var area = new Rect(Screen.width - 400f, HudLayout.TopInset, 390f, Mathf.Min(Screen.height - HudLayout.TopInset - HudLayout.BottomInset, 520f));
            UiBlocker.Register(area);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f));
            scroll = GUILayout.BeginScrollView(scroll);

            var u = tool.Runner.World.Utilities;
            if (overlayOn) Section(u, overlay.Kind);
            else { Section(u, UtilityKind.Power); GUILayout.Space(6f); Section(u, UtilityKind.Water); }

            GUILayout.Space(6f);
            GUILayout.Label($"Unterhalt gesamt {u.TotalUpkeepPerDay:0} €/Tag · verlassene Gebäude {u.AbandonedBuildings}\n" +
                            $"Ohne Versorgung länger als {UtilitySystem.AbandonAfterTicks / Simulation.Core.SimulationClock.TicksPerDay} Tage → Gebäude wird verlassen " +
                            "(erst, wenn es mind. einen Versorger dieser Art gibt).", small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void Section(UtilitySystem u, UtilityKind kind)
        {
            string unit = UtilityCatalog.Unit(kind);
            float prod = u.TotalProduction(kind), dem = u.TotalDemand(kind);
            GUILayout.Label($"{UtilityCatalog.KindName(kind)}: {prod:0} / {dem:0} {unit}", header);
            if (!u.IsRequired(kind))
                GUILayout.Label($"<color=#ffd060>Noch kein Versorger gebaut – Gebäude bleiben vorerst ohne {UtilityCatalog.KindName(kind)}.</color>", small);

            var nets = u.Networks(kind).Where(n => n.Facilities > 0 || n.Buildings > 0)
                .OrderByDescending(n => n.Facilities > 0).ThenByDescending(n => n.Demand).ToList();
            int shown = 0;
            foreach (var n in nets)
            {
                if (shown++ >= MaxRows) { GUILayout.Label($"… und {nets.Count - MaxRows} weitere Netze", small); break; }
                string color = n.Facilities == 0 ? "#ff7060" : n.HasShortage ? "#ffb040" : "#7dff8a";
                string state = n.Facilities == 0 ? "kein Versorger angeschlossen" : n.HasShortage ? $"Unterdeckung ({n.Coverage * 100f:0} %)" : "ausreichend";
                GUILayout.Label($"<color={color}><b>Netz {n.Id + 1}</b> · {state}</color>\n" +
                                $"   Erzeugung {n.Production:0} {unit} · Bedarf {n.Demand:0} {unit} · " +
                                $"versorgt {n.SuppliedBuildings}/{n.Buildings} Gebäude · {n.Facilities} Bauwerke · {n.Lines} Leitungen", small);
            }
            if (nets.Count == 0) GUILayout.Label("Keine Netze.", small);
        }
    }
}
