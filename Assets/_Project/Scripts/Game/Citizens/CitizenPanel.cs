using System.Linq;
using System.Text;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Zoning;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.Citizens
{
    /// <summary>
    /// Bürger-Info (IMGUI) im Info-Modus (Taste C): Einwohner, Arbeitsmarkt, Pendler, Altersverteilung, Bildung,
    /// was die Bürger gerade tun. Klick auf ein Gebäude zeigt dessen Bewohner bzw. Beschäftigte.
    /// Liest nur – Änderungen laufen über die Simulation.
    /// </summary>
    public sealed class CitizenPanel : MonoBehaviour
    {
        static readonly Color AgeColor = new Color(0.45f, 0.75f, 1f);
        static readonly Color EduColor = new Color(0.55f, 0.9f, 0.55f);

        RoadTool tool;
        Material lineMaterial;
        LineRenderer outline;
        int selectedId;
        Vector2 scroll;
        GUIStyle header, small;

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            lineMaterial = new Material(transparent) { color = new Color(1f, 0.85f, 0.2f, 0.95f), name = "BuildingOutline" };
            var go = new GameObject("Building Outline");
            go.transform.SetParent(transform, false);
            outline = go.AddComponent<LineRenderer>();
            outline.sharedMaterial = lineMaterial;
            outline.useWorldSpace = true;
            outline.loop = true;
            outline.positionCount = 4;
            outline.widthMultiplier = 0.6f;
            outline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            outline.enabled = false;
        }

        void OnDestroy()
        {
            if (lineMaterial != null) Destroy(lineMaterial);
        }

        bool Active => tool != null && tool.CurrentMode == RoadTool.Mode.Inspect && tool.Runner != null && tool.Runner.World != null;

        // ------------------------------------------------------------ Auswahl

        void Update()
        {
            if (!Active)
            {
                if (outline != null) outline.enabled = false;
                return;
            }
            var city = tool.Runner.World.City;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && tool.HasCursor)
            {
                var b = city.BuildingAt(tool.Cursor);
                selectedId = b != null ? b.Id : 0;
            }
            if (mouse != null && mouse.rightButton.wasPressedThisFrame) selectedId = 0;

            var sel = selectedId != 0 ? city.GetBuilding(selectedId) : null;
            if (sel == null) { selectedId = 0; outline.enabled = false; return; }
            outline.enabled = true;
            var c = sel.Position;
            var side = sel.Side * (sel.Width * 0.5f);
            var front = sel.Front * (sel.Depth * 0.5f);
            float h = sel.Height + 0.6f;
            outline.SetPosition(0, SimConvert.ToWorld(c + side + front, h));
            outline.SetPosition(1, SimConvert.ToWorld(c - side + front, h));
            outline.SetPosition(2, SimConvert.ToWorld(c - side - front, h));
            outline.SetPosition(3, SimConvert.ToWorld(c + side - front, h));
        }

        // ------------------------------------------------------------ Fenster

        void OnGUI()
        {
            if (!Active) return;
            if (header == null)
            {
                header = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, richText = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
            }

            var area = new Rect(Screen.width - 430f, 10f, 420f, Mathf.Min(Screen.height - 170f, 680f));
            UiBlocker.Register(area);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f));
            scroll = GUILayout.BeginScrollView(scroll);

            var world = tool.Runner.World;
            var sel = selectedId != 0 ? world.City.GetBuilding(selectedId) : null;
            if (sel != null) BuildingPanel(sel);
            else CityPanel();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void CityPanel()
        {
            var world = tool.Runner.World;
            var s = world.Citizens.Stats;
            var pop = world.City.Population;
            var traffic = world.Traffic.Stats;

            GUILayout.Label("Bürger", header);
            GUILayout.Label($"<b>{s.Population}</b> Einwohner in {s.Households} Haushalten · " +
                            $"freie Wohnungen {pop.FreeHomes} · Zuzug {world.City.MoveInPull:0} %", small);

            GUILayout.Space(4f);
            GUILayout.Label("<b>Arbeit</b>", small);
            GUILayout.Label($"Erwerbsfähig {s.WorkingAge} · beschäftigt {s.Employed} (hier {s.WorkingLocal})\n" +
                            $"Arbeitslos <color=#ff9080>{s.Unemployed}</color> ({s.UnemploymentRate * 100f:0} %) · freie Jobs {pop.VacantJobs}\n" +
                            $"Auspendler {s.CommutersOut} · Einpendler {s.CommutersIn} · Außenanschlüsse {s.ExternalConnections}", small);
            if (s.ExternalConnections == 0)
                GUILayout.Label("<color=#ffd060>Keine Außenanschlüsse: Straße bis an den Kartenrand bauen, dann kommen Pendler und Durchgangsverkehr.</color>", small);

            GUILayout.Space(4f);
            GUILayout.Label("<b>Alter</b>", small);
            int maxAge = Mathf.Max(1, s.AgeGroups.Max());
            for (int i = 0; i < s.AgeGroups.Length; i++) Bar(CitizenStats.AgeGroupNames[i], s.AgeGroups[i], maxAge, AgeColor);
            GUILayout.Label($"Kinder {s.Children} · Schüler {s.Students} · Erwachsene {s.Adults} · Senioren {s.Seniors}", small);

            GUILayout.Space(4f);
            GUILayout.Label("<b>Bildung (Erwachsene)</b>", small);
            int maxEdu = Mathf.Max(1, s.ByEducation.Max());
            for (int i = 0; i < s.ByEducation.Length; i++) Bar(EducationName((Education)i), s.ByEducation[i], maxEdu, EduColor);

            GUILayout.Space(4f);
            GUILayout.Label("<b>Gerade</b>", small);
            GUILayout.Label($"zu Hause {s.AtHome} · bei der Arbeit {s.AtWork} · einkaufen {s.Shopping}\n" +
                            $"unterwegs {s.Travelling} (davon Auto {s.TravellingByCar}) · Bürgerautos im Verkehr {traffic.CitizenVehicles}", small);
            GUILayout.Label($"Fahrten bisher: Auto {s.CarTrips} (angekommen {s.CarArrivals}, abgebrochen {s.CarTripsFailed}) · " +
                            $"zu Fuß/ÖPNV {s.OtherTrips}\nZugezogen {s.MovedIn} · weggezogen {s.MovedOut} · gestorben {s.Died}", small);

            GUILayout.Space(6f);
            GUILayout.Label("Gebäude anklicken: Bewohner bzw. Beschäftigte. Rechtsklick: Auswahl aufheben. C: Info schließen.", small);
        }

        void BuildingPanel(Building b)
        {
            var citizens = tool.Runner.World.Citizens;
            GUILayout.Label($"{Zones.DisplayName(b.Zone)} #{b.Id}", header);
            GUILayout.Label($"{b.WidthCells}×{b.DepthCells} Zellen · {(b.IsConstructed ? "fertig" : $"im Bau {b.Construction * 100f:0} %")} · " +
                            $"gerade anwesend {citizens.PresentIn(b.Id)}", small);
            if (GUILayout.Button("← Übersicht", GUILayout.Width(110f))) selectedId = 0;

            if (b.Households > 0)
            {
                var hs = citizens.HouseholdsIn(b.Id);
                GUILayout.Space(4f);
                GUILayout.Label($"<b>Wohnungen {hs.Count}/{b.Households}</b> · {b.Residents} Bewohner", small);
                var sb = new StringBuilder();
                int shown = 0;
                foreach (var h in hs)
                {
                    if (shown++ >= 12) { sb.AppendLine($"… und {hs.Count - 12} weitere Haushalte"); break; }
                    sb.Append($"Haushalt {h.Id}: ");
                    sb.AppendLine(string.Join(", ", h.Members.Select(Describe)));
                }
                if (sb.Length > 0) GUILayout.Label(sb.ToString(), small);
            }

            if (b.Jobs > 0)
            {
                var ws = citizens.WorkersAt(b.Id);
                int commuters = ws.Count(w => w.IsCommuterIn);
                GUILayout.Space(4f);
                GUILayout.Label($"<b>Arbeitsplätze {ws.Count}/{b.Jobs}</b> · davon Einpendler {commuters}", small);
                var edu = new int[4];
                foreach (var w in ws) edu[(int)w.Education]++;
                GUILayout.Label($"Bildung: ohne {edu[0]} · Grund {edu[1]} · mittel {edu[2]} · hoch {edu[3]}", small);
                int present = ws.Count(w => w.Activity == Activity.Work && w.Location.BuildingId == b.Id);
                GUILayout.Label($"gerade bei der Arbeit: {present}", small);
            }
        }

        static string Describe(Citizen c)
        {
            string job = c.WorkplaceId != 0 ? $"arbeitet in #{c.WorkplaceId}" : c.WorksOutside ? "Auspendler" :
                c.IsWorkingAge ? "<color=#ff9080>arbeitslos</color>" : c.Stage == LifeStage.Senior ? "Rente" : c.Stage == LifeStage.Student ? "Schüler" : "Kind";
            return $"{c.Age} J. ({job}, {ActivityName(c)})";
        }

        static string ActivityName(Citizen c)
        {
            switch (c.Activity)
            {
                case Activity.Home: return "zu Hause";
                case Activity.Work: return "Arbeit";
                case Activity.Shopping: return "einkaufen";
                case Activity.Travelling: return c.Mode == TravelMode.Car ? "im Auto" : "unterwegs";
                default: return "außerhalb";
            }
        }

        static string EducationName(Education e)
        {
            switch (e)
            {
                case Education.None: return "ohne";
                case Education.Basic: return "Grund";
                case Education.Secondary: return "mittel";
                default: return "hoch";
            }
        }

        void Bar(string label, int value, int max, Color color)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, small, GUILayout.Width(60f));
            var r = GUILayoutUtility.GetRect(240f, 14f, GUILayout.Width(240f));
            if (Event.current.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.12f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = color;
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * value / max, r.height), Texture2D.whiteTexture);
                GUI.color = old;
            }
            GUILayout.Label(value.ToString(), small, GUILayout.Width(50f));
            GUILayout.EndHorizontal();
        }
    }
}
