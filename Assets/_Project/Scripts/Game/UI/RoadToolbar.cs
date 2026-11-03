using System.Collections.Generic;
using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Game.Traffic;
using CityBuilder.Simulation.Roads;
using UnityEngine;

namespace CityBuilder.Game.UI
{
    /// <summary>
    /// Provisorische Bauleiste (IMGUI) am unteren Rand: Bereich, Werkzeug, Straßentyp, Höhe, Verkehr.
    /// Wird später durch eine richtige UI (UI Toolkit) ersetzt – die Logik bleibt im RoadTool.
    /// </summary>
    public sealed class RoadToolbar : MonoBehaviour
    {
        enum Group { City, Highway, Interchanges, Traffic }

        static readonly Color Selected = new Color(0.45f, 0.75f, 1f);

        RoadTool tool;
        Group group = Group.City;
        GUIStyle infoStyle;

        public void Init(RoadTool roadTool) => tool = roadTool;

        static Group GroupOf(RoadType t) => t.Category == RoadCategory.City ? Group.City : Group.Highway;

        void OnGUI()
        {
            if (tool == null) return;
            if (infoStyle == null) infoStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };

            // Bereich folgt dem Werkzeug (z. B. nach Tastendruck)
            if (Event.current.type == EventType.Layout)
            {
                if (tool.CurrentMode == RoadTool.Mode.Traffic) group = Group.Traffic;
                else if (tool.CurrentMode == RoadTool.Mode.Template) group = Group.Interchanges;
                else if (group != Group.Traffic && group != Group.Interchanges) group = GroupOf(tool.SelectedType);
                else if (tool.IsBuildMode) group = GroupOf(tool.SelectedType);
            }

            const float width = 980f, height = 132f;
            var area = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 10f, width, height);
            UiBlocker.Register(area);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 6f, area.width - 16f, area.height - 12f));

            GUILayout.BeginHorizontal();
            if (Button("Stadtstraßen", group == Group.City, 120f)) SelectGroup(Group.City);
            if (Button("Autobahn", group == Group.Highway, 120f)) SelectGroup(Group.Highway);
            if (Button("Autobahnknoten", group == Group.Interchanges, 130f)) SelectGroup(Group.Interchanges);
            if (Button("Verkehr (T)", group == Group.Traffic, 110f)) SelectGroup(Group.Traffic);
            GUILayout.FlexibleSpace();
            GUILayout.Label(tool.SnapEnabled ? "Einrasten: an (G)" : "Einrasten: aus (G)", GUILayout.Width(125f));
            GUILayout.EndHorizontal();

            switch (group)
            {
                case Group.City:
                case Group.Highway: RoadsRows(group); break;
                case Group.Interchanges: InterchangeRows(); break;
                case Group.Traffic: TrafficRows(); break;
            }
            GUILayout.EndArea();
        }

        void SelectGroup(Group g)
        {
            group = g;
            switch (g)
            {
                case Group.Interchanges: tool.SelectTemplate(tool.TemplateKind); break;
                case Group.Traffic: tool.SetMode(RoadTool.Mode.Traffic); break;
                default:
                    var first = TypesOf(g).FirstOrDefault();
                    if (first != null && GroupOf(tool.SelectedType) != g) tool.SelectType(first);
                    if (!tool.IsBuildMode && tool.CurrentMode != RoadTool.Mode.Upgrade && tool.CurrentMode != RoadTool.Mode.Reverse &&
                        tool.CurrentMode != RoadTool.Mode.Bulldoze) tool.SetMode(RoadTool.Mode.Straight);
                    break;
            }
        }

        void RoadsRows(Group g)
        {
            GUILayout.BeginHorizontal();
            ModeButton(RoadTool.Mode.Straight, "Gerade (R)");
            ModeButton(RoadTool.Mode.Curve, "Kurve (R)");
            if (g == Group.City) ModeButton(RoadTool.Mode.Roundabout, "Kreisverkehr (K)");
            ModeButton(RoadTool.Mode.Upgrade, "Ausbauen (U)");
            ModeButton(RoadTool.Mode.Reverse, "Richtung (I)");
            ModeButton(RoadTool.Mode.Bulldoze, "Abriss (B)");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Höhe {tool.BuildHeight:0} m", GUILayout.Width(75f));
            if (GUILayout.Button("▼", GUILayout.Width(26f))) tool.ChangeHeight(-RoadTool.HeightStep);
            if (GUILayout.Button("▲", GUILayout.Width(26f))) tool.ChangeHeight(RoadTool.HeightStep);
            if (Button("Unterirdisch (X)", tool.UndergroundView, 120f)) tool.ToggleUndergroundView();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            foreach (var type in TypesOf(g))
                if (Button(ShortName(type), tool.SelectedType == type, 0f)) tool.SelectType(type);
            if (g == Group.Highway && Button(tool.DualCarriageway ? "Doppelbau: an (H)" : "Doppelbau: aus (H)", tool.DualCarriageway, 150f))
                tool.ToggleDualCarriageway();
            GUILayout.EndHorizontal();

            var t = tool.CurrentMode == RoadTool.Mode.Roundabout ? tool.RoundaboutType : tool.SelectedType;
            GUILayout.Label(Describe(t) + "   ·   Bild↑ Brücke · Bild↓ Trog/Tunnel (ab −7 m)", infoStyle);
        }

        void InterchangeRows()
        {
            GUILayout.BeginHorizontal();
            foreach (RoadTemplateKind kind in System.Enum.GetValues(typeof(RoadTemplateKind)))
                if (Button(RoadTemplates.DisplayName(kind), tool.CurrentMode == RoadTool.Mode.Template && tool.TemplateKind == kind, 210f))
                    tool.SelectTemplate(kind);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("⟲ 15°", GUILayout.Width(60f))) tool.RotateTemplate(-15f);
            if (GUILayout.Button("⟳ 15°", GUILayout.Width(60f))) tool.RotateTemplate(15f);
            GUILayout.EndHorizontal();
            GUILayout.Label($"Drehung {tool.TemplateRotation:0}°  ·  Autobahn-Typ: {(tool.SelectedType.IsHighway ? tool.SelectedType.DisplayName : RoadCatalog.Highway2.DisplayName)}", infoStyle);
            GUILayout.Label("Vorlage platzieren und an den offenen Enden eigene Autobahnen anschließen. , und . drehen.", infoStyle);
        }

        void TrafficRows()
        {
            var traffic = tool.Runner.World.Traffic;
            var overlay = GetComponent<TrafficOverlayView>();
            var vehicles = GetComponent<VehicleView>();

            GUILayout.BeginHorizontal();
            ModeButton(RoadTool.Mode.Traffic, "Regeln bearbeiten (T)");
            if (overlay != null && Button(overlay.Visible ? "Stau-Ansicht: an (V)" : "Stau-Ansicht: aus (V)", overlay.Visible, 170f)) overlay.Toggle();
            if (vehicles != null && Button(vehicles.Visible ? "Fahrzeuge: sichtbar" : "Fahrzeuge: aus", vehicles.Visible, 150f)) vehicles.Visible = !vehicles.Visible;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Verkehrsaufkommen:", GUILayout.Width(130f));
            foreach (var (label, value) in new[] { ("Aus", 0f), ("Wenig", 0.4f), ("Normal", 1f), ("Viel", 2f), ("Extrem", 4f) })
                if (Button(label, Mathf.Approximately(traffic.Demand, value), 80f)) traffic.Demand = value;
            GUILayout.EndHorizontal();
            GUILayout.Label($"Verkehrsquellen (Sackgassen/Kartenrand): {traffic.EndpointCount} · Fahrzeuge: {traffic.Stats.Active}", infoStyle);
        }

        void ModeButton(RoadTool.Mode mode, string label)
        {
            if (Button(label, tool.CurrentMode == mode, 0f)) tool.SetMode(mode);
        }

        static IEnumerable<RoadType> TypesOf(Group g) => RoadCatalog.All.Where(t => GroupOf(t) == g);

        static bool Button(string text, bool active, float width)
        {
            var old = GUI.backgroundColor;
            if (active) GUI.backgroundColor = Selected;
            bool clicked = width > 0f ? GUILayout.Button(text, GUILayout.Width(width), GUILayout.Height(24f))
                                      : GUILayout.Button(text, GUILayout.Height(24f));
            GUI.backgroundColor = old;
            return clicked;
        }

        static string ShortName(RoadType t)
        {
            switch (t.Id)
            {
                case "city_2": return "2-spurig";
                case "city_4": return "4-spurig";
                case "city_oneway_1": return "Einbahn 1";
                case "city_oneway_2": return "Einbahn 2";
                case "city_2_bus": return "Busspuren";
                case "city_oneway_bus": return "Einbahn+Bus";
                case "city_2_tram": return "Tram";
                case "city_oneway_tram": return "Einbahn+Tram";
                case "highway_2": return "Autobahn 2";
                case "highway_3": return "Autobahn 3";
                case "ramp_1": return "Auf-/Abfahrt";
                default: return t.DisplayName;
            }
        }

        static string Describe(RoadType t)
        {
            string dir = t.IsOneWay ? "Einbahn" : $"{t.CarLanesBackward}+{t.CarLanesForward} Spuren";
            string extras = (t.HasBusLanes ? " · Busspur" : "") + (t.HasTram ? " · Tram" : "");
            return $"<b>{t.DisplayName}</b> – {dir}{extras} · {t.Width:0.#} m · {t.SpeedLimitKmh:0} km/h · {t.CostPerMeter:0} €/m · max. {t.MaxSlope * 100f:0} %";
        }
    }
}
