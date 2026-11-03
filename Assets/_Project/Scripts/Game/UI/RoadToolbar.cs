using System.Collections.Generic;
using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Roads;
using UnityEngine;

namespace CityBuilder.Game.UI
{
    /// <summary>
    /// Provisorische Bauleiste (IMGUI) am unteren Rand: Werkzeug, Kategorie, Straßentyp.
    /// Wird später durch eine richtige UI (UI Toolkit) ersetzt – die Logik bleibt im RoadTool.
    /// </summary>
    public sealed class RoadToolbar : MonoBehaviour
    {
        enum Group { City, Highway }

        static readonly Color Selected = new Color(0.45f, 0.75f, 1f);

        RoadTool tool;
        Group group = Group.City;
        GUIStyle infoStyle;

        static readonly (RoadTool.Mode mode, string label)[] Modes =
        {
            (RoadTool.Mode.Straight, "Gerade (R)"),
            (RoadTool.Mode.Curve, "Kurve (R)"),
            (RoadTool.Mode.Roundabout, "Kreisverkehr (K)"),
            (RoadTool.Mode.Bulldoze, "Abriss (B)"),
        };

        public void Init(RoadTool roadTool) => tool = roadTool;

        static Group GroupOf(RoadType t) => t.Category == RoadCategory.City ? Group.City : Group.Highway;

        void OnGUI()
        {
            if (tool == null) return;
            if (infoStyle == null) infoStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };

            // Kategorie folgt der Auswahl (z. B. nach Tab)
            if (Event.current.type == EventType.Layout) group = GroupOf(tool.SelectedType);

            const float width = 900f, height = 128f;
            var area = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 10f, width, height);
            UiBlocker.Register(area);

            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 8f, area.y + 6f, area.width - 16f, area.height - 12f));

            // Werkzeuge
            GUILayout.BeginHorizontal();
            foreach (var (mode, label) in Modes)
                if (Button(label, tool.CurrentMode == mode, 150f)) tool.SetMode(mode);
            GUILayout.FlexibleSpace();
            GUILayout.Label(tool.SnapEnabled ? "Einrasten: an (G)" : "Einrasten: aus (G)", GUILayout.Width(130f));
            GUILayout.EndHorizontal();

            // Kategorien
            GUILayout.BeginHorizontal();
            if (Button("Stadtstraßen", group == Group.City, 120f)) SelectGroup(Group.City);
            if (Button("Autobahn", group == Group.Highway, 120f)) SelectGroup(Group.Highway);
            GUILayout.EndHorizontal();

            // Typen der Kategorie
            GUILayout.BeginHorizontal();
            foreach (var type in TypesOf(group))
                if (Button(ShortName(type), tool.SelectedType == type, 0f)) tool.SelectType(type);
            GUILayout.EndHorizontal();

            GUILayout.Label(Describe(tool.CurrentMode == RoadTool.Mode.Roundabout ? tool.RoundaboutType : tool.SelectedType), infoStyle);
            GUILayout.EndArea();
        }

        void SelectGroup(Group g)
        {
            group = g;
            var first = TypesOf(g).FirstOrDefault();
            if (first != null && GroupOf(tool.SelectedType) != g) tool.SelectType(first);
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
            return $"<b>{t.DisplayName}</b>  –  {dir}{extras} · Breite {t.Width:0.#} m · {t.SpeedLimitKmh:0} km/h · {t.CostPerMeter:0} €/m";
        }
    }
}
