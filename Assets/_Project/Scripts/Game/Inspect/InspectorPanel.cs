using System.Collections.Generic;
using System.Linq;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI;
using CityBuilder.Game.Zoning;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.Inspect
{
    /// <summary>
    /// Inspektor im Info-Modus (Taste C = <see cref="RoadTool.Mode.Inspect"/>): Klick wählt Gebäude, Fahrzeug oder
    /// Versorgungsbauwerk; Bürger über die Listen in Gebäude/Fahrzeug. Auswahl wird hervorgehoben (Umriss/Ring,
    /// bei Bürgern zusätzlich Wohnung und Arbeitsplatz), Fahrzeuge/Bürger können verfolgt werden (F).
    /// Ohne Auswahl: Stadtübersicht der Bürger. Liest nur – Änderungen laufen über die Simulation.
    /// <para>
    /// Werte, die erst mit dem Bedürfnismodell entstehen (Gesundheit, Zufriedenheit, Gemüt, Gebäudezustand), sind
    /// als <see cref="Pending"/> markiert und zeigen bewusst keine Zahl.
    /// </para>
    /// </summary>
    public sealed class InspectorPanel : MonoBehaviour
    {
        /// <summary>Anzeige für Werte, die es noch nicht gibt.</summary>
        const string Pending = "<color=#9aa4b0>— (kommt mit Bedürfnismodell)</color>";
        const int PageSize = 25;
        const int MaxHistory = 30;

        static readonly Color PrimaryColor = new Color(1f, 0.85f, 0.2f, 0.95f);
        static readonly Color HomeColor = new Color(0.45f, 0.95f, 0.45f, 0.9f);
        static readonly Color WorkColor = new Color(0.45f, 0.75f, 1f, 0.9f);
        static readonly Color AgeColor = new Color(0.45f, 0.75f, 1f);
        static readonly Color EduColor = new Color(0.55f, 0.9f, 0.55f);
        static readonly Color NeedColor = new Color(1f, 0.7f, 0.3f);

        RoadTool tool;
        BuildingView buildingView;
        CameraController cameraController;
        readonly List<Material> materials = new List<Material>();
        Marker primary, home, work;

        Selection selection;
        readonly List<Selection> history = new List<Selection>();
        int tab;
        int page;
        Vector2 scroll;
        bool follow;
        bool followWasSet;
        /// <summary>Bürger des zuletzt gezeigten Fahrzeugs – kommt das Auto an, springt die Auswahl zum Bürger.</summary>
        int vehicleCitizenId;
        GUIStyle header, small, link, sub;
        /// <summary>Klicks im Fenster (Verweise, Zurück …) wirken erst zum nächsten Layout-Durchlauf – sonst bricht IMGUI ab.</summary>
        System.Action deferred;

        public Selection Current => selection;

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            buildingView = GetComponent<BuildingView>();
            primary = new Marker(transform, NewMaterial(transparent, PrimaryColor, "InspectPrimary"), true);
            home = new Marker(transform, NewMaterial(transparent, HomeColor, "InspectHome"), false);
            work = new Marker(transform, NewMaterial(transparent, WorkColor, "InspectWork"), false);
        }

        Material NewMaterial(Material transparent, Color c, string name)
        {
            var m = new Material(transparent) { color = c, name = name };
            materials.Add(m);
            return m;
        }

        void OnDestroy()
        {
            foreach (var m in materials) if (m != null) Destroy(m);
        }

        bool Active => tool != null && tool.CurrentMode == RoadTool.Mode.Inspect && tool.Runner != null && tool.Runner.World != null;
        SimulationWorld World => tool.Runner.World;

        // ------------------------------------------------------------ Auswahl

        public void Select(Selection s)
        {
            if (s.Equals(selection)) return;
            if (!selection.IsNone)
            {
                history.Add(selection);
                if (history.Count > MaxHistory) history.RemoveAt(0);
            }
            selection = s;
            tab = 0;
            page = 0;
            scroll = Vector2.zero;
            if (s.Kind != SelectionKind.Vehicle && s.Kind != SelectionKind.Citizen) follow = false;
        }

        void Back()
        {
            if (history.Count == 0) { selection = Selection.None; return; }
            selection = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            tab = 0;
            page = 0;
        }

        void ClearSelection()
        {
            selection = Selection.None;
            history.Clear();
            follow = false;
        }

        void Update()
        {
            if (cameraController == null && tool != null && tool.Camera != null) cameraController = tool.Camera.GetComponent<CameraController>();
            if (!Active)
            {
                HideMarkers();
                StopFollowing();
                return;
            }
            var world = World;
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse != null)
            {
                var screen = mouse.position.ReadValue();
                bool overUi = UiBlocker.IsPointerOverUi(screen);
                if (mouse.leftButton.wasPressedThisFrame && !overUi && tool.Camera != null) PickAt(screen);
                if (mouse.rightButton.wasPressedThisFrame && !overUi) ClearSelection();
            }
            if (kb != null && kb.fKey.wasPressedThisFrame && CanFollow) follow = !follow;

            Validate(world);
            UpdateMarkers(world);
            UpdateFollow(world);
        }

        void PickAt(Vector2 screen)
        {
            var world = World;
            var ray = tool.Camera.ScreenPointToRay(screen);
            var hit = InspectorPicking.Pick(ray, world, tool.Runner.TickAlpha,
                b => buildingView != null ? buildingView.VisualHeight(b) : 8f);
            if (hit.IsNone && tool.HasCursor)
            {
                // Rückfall: Geländepunkt unter der Maus
                var b = world.City.BuildingAt(tool.Cursor);
                var f = b == null ? world.Utilities.FacilityAt(tool.Cursor) : null;
                var sf = b == null && f == null ? world.Services.FacilityAt(tool.Cursor) : null; // G4
                if (b != null) hit = new Selection(SelectionKind.Building, b.Id);
                else if (f != null) hit = new Selection(SelectionKind.Facility, f.Id);
                else if (sf != null) hit = new Selection(SelectionKind.Service, sf.Id);
            }
            if (hit.IsNone) ClearSelection();
            else Select(hit);
        }

        /// <summary>Ausgewähltes Objekt noch da? Fahrzeug weg → zum Bürger wechseln, sonst Auswahl aufheben.</summary>
        void Validate(SimulationWorld world)
        {
            switch (selection.Kind)
            {
                case SelectionKind.Building:
                    if (world.City.GetBuilding(selection.Id) == null) selection = Selection.None;
                    break;
                case SelectionKind.Facility:
                    if (world.Utilities.GetFacility(selection.Id) == null) selection = Selection.None;
                    break;
                case SelectionKind.Service:
                    if (world.Services.GetFacility(selection.Id) == null) selection = Selection.None;
                    break;
                case SelectionKind.Citizen:
                    if (world.Citizens.Get(selection.Id) == null) selection = Selection.None;
                    break;
                case SelectionKind.Vehicle:
                {
                    var v = FindVehicle(world, selection.Id);
                    if (v != null)
                    {
                        var c = world.Citizens.CitizenForTrip(v.TripId);
                        vehicleCitizenId = c != null ? c.Id : 0;
                        break;
                    }
                    // Fahrt beendet: weiter beim Fahrer (die Verfolgung bleibt erhalten)
                    var driver = vehicleCitizenId != 0 ? world.Citizens.Get(vehicleCitizenId) : null;
                    selection = driver != null ? new Selection(SelectionKind.Citizen, driver.Id) : Selection.None;
                    vehicleCitizenId = 0;
                    tab = 0;
                    break;
                }
            }
            if (selection.IsNone) follow = false;
        }

        static Vehicle FindVehicle(SimulationWorld world, int id)
        {
            foreach (var v in world.Traffic.ActiveVehicles) if (v.Id == id) return v;
            return null;
        }

        bool CanFollow => selection.Kind == SelectionKind.Vehicle || selection.Kind == SelectionKind.Citizen;

        // ------------------------------------------------------------ Hervorhebung & Verfolgen

        void HideMarkers()
        {
            primary?.Hide();
            home?.Hide();
            work?.Hide();
        }

        void UpdateMarkers(SimulationWorld world)
        {
            HideMarkers();
            float width = tool.Camera != null ? Mathf.Clamp(Vector3.Distance(tool.Camera.transform.position, FocusPoint(world) ?? Vector3.zero) * 0.004f, 0.3f, 3f) : 0.6f;
            switch (selection.Kind)
            {
                case SelectionKind.Building:
                    MarkBuilding(primary, world.City.GetBuilding(selection.Id), width);
                    break;
                case SelectionKind.Facility:
                {
                    var f = world.Utilities.GetFacility(selection.Id);
                    if (f != null) primary.Rect(f.Corners(), f.Height + 0.3f, f.Height + InspectorPicking.FacilityHeight(f) + 0.5f, width);
                    break;
                }
                case SelectionKind.Service:
                {
                    var f = world.Services.GetFacility(selection.Id);
                    if (f != null) primary.Rect(f.Corners(), f.Height + 0.3f, f.Height + InspectorPicking.ServiceHeight(f) + 0.5f, width);
                    break;
                }
                case SelectionKind.Vehicle:
                {
                    var v = FindVehicle(world, selection.Id);
                    if (v != null) primary.Ring(InspectorPicking.VehiclePosition(v, tool.Runner.TickAlpha), Mathf.Clamp(v.Length * 0.7f, 3f, 12f), width * 0.6f);
                    var c = world.Citizens.CitizenForTrip(v?.TripId ?? 0);
                    if (c != null && c.Destination.IsBuilding) MarkBuilding(work, world.City.GetBuilding(c.Destination.BuildingId), width * 0.7f);
                    break;
                }
                case SelectionKind.Citizen:
                {
                    var c = world.Citizens.Get(selection.Id);
                    if (c == null) break;
                    var v = world.Citizens.VehicleOf(c);
                    if (v != null) primary.Ring(InspectorPicking.VehiclePosition(v, tool.Runner.TickAlpha), Mathf.Clamp(v.Length * 0.7f, 3f, 12f), width * 0.6f);
                    else if (c.Location.IsBuilding) MarkBuilding(primary, world.City.GetBuilding(c.Location.BuildingId), width);
                    if (c.Home.IsBuilding && c.Home.BuildingId != c.Location.BuildingId) MarkBuilding(home, world.City.GetBuilding(c.Home.BuildingId), width * 0.7f);
                    if (c.WorkplaceId != 0 && c.WorkplaceId != c.Location.BuildingId) MarkBuilding(work, world.City.GetBuilding(c.WorkplaceId), width * 0.7f);
                    break;
                }
            }
        }

        void MarkBuilding(Marker m, Building b, float width)
        {
            if (b == null) return;
            var side = b.Side * (b.Width * 0.5f);
            var front = b.Front * (b.Depth * 0.5f);
            var c = b.Position;
            var corners = new[] { c + side + front, c - side + front, c - side - front, c + side - front };
            float h = buildingView != null ? buildingView.VisualHeight(b) : 8f;
            m.Rect(corners, b.Height + 0.35f, b.Height + h + 0.6f, width);
        }

        /// <summary>Punkt, dem die Kamera beim Verfolgen folgt (bzw. Bezug für die Linienbreite).</summary>
        Vector3? FocusPoint(SimulationWorld world)
        {
            switch (selection.Kind)
            {
                case SelectionKind.Vehicle:
                {
                    var v = FindVehicle(world, selection.Id);
                    return v != null ? InspectorPicking.VehiclePosition(v, tool.Runner.TickAlpha) : (Vector3?)null;
                }
                case SelectionKind.Citizen:
                {
                    var c = world.Citizens.Get(selection.Id);
                    if (c == null) return null;
                    var v = world.Citizens.VehicleOf(c);
                    if (v != null) return InspectorPicking.VehiclePosition(v, tool.Runner.TickAlpha);
                    var b = c.Location.IsBuilding ? world.City.GetBuilding(c.Location.BuildingId) : null;
                    return b != null ? SimConvert.ToWorld(b.Position, b.Height) : (Vector3?)null;
                }
                case SelectionKind.Building:
                {
                    var b = world.City.GetBuilding(selection.Id);
                    return b != null ? SimConvert.ToWorld(b.Position, b.Height) : (Vector3?)null;
                }
                case SelectionKind.Facility:
                {
                    var f = world.Utilities.GetFacility(selection.Id);
                    return f != null ? SimConvert.ToWorld(f.Position, f.Height) : (Vector3?)null;
                }
                case SelectionKind.Service:
                {
                    var f = world.Services.GetFacility(selection.Id);
                    return f != null ? SimConvert.ToWorld(f.Position, f.Height) : (Vector3?)null;
                }
                default: return null;
            }
        }

        void UpdateFollow(SimulationWorld world)
        {
            if (cameraController == null) return;
            // Spieler hat die Kamera verschoben → Verfolgen aus
            if (followWasSet && !cameraController.FollowTarget.HasValue) follow = false;
            var target = follow && CanFollow ? FocusPoint(world) : null;
            if (target.HasValue)
            {
                cameraController.FollowTarget = target;
                followWasSet = true;
            }
            else StopFollowing();
        }

        void StopFollowing()
        {
            if (followWasSet && cameraController != null) cameraController.FollowTarget = null;
            followWasSet = false;
        }

        // ------------------------------------------------------------ Fenster

        void OnGUI()
        {
            if (!Active) return;
            if (Event.current.type == EventType.Layout && deferred != null)
            {
                var a = deferred;
                deferred = null;
                a();
            }
            if (header == null)
            {
                header = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, richText = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
                sub = new GUIStyle(small) { fontStyle = FontStyle.Bold };
                link = new GUIStyle(GUI.skin.button) { fontSize = 12, richText = true, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            }

            var area = new Rect(Screen.width - 450f, HudLayout.TopInset, 440f, Mathf.Min(Screen.height - HudLayout.TopInset - HudLayout.BottomInset, 720f));
            UiBlocker.Register(area);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 10f, area.y + 8f, area.width - 20f, area.height - 16f));

            if (!selection.IsNone)
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = history.Count > 0;
                if (GUILayout.Button("← Zurück", GUILayout.Width(80f))) deferred = Back;
                GUI.enabled = true;
                if (GUILayout.Button("Übersicht", GUILayout.Width(80f))) deferred = ClearSelection;
                if (CanFollow)
                {
                    bool f = GUILayout.Toggle(follow, " Verfolgen (F)", GUILayout.Width(120f));
                    if (f != follow) follow = f;
                }
                GUILayout.EndHorizontal();
            }

            scroll = GUILayout.BeginScrollView(scroll);
            var world = World;
            switch (selection.Kind)
            {
                case SelectionKind.Building: BuildingPanel(world, world.City.GetBuilding(selection.Id)); break;
                case SelectionKind.Facility: FacilityPanel(world, world.Utilities.GetFacility(selection.Id)); break;
                case SelectionKind.Service: ServicePanel(world, world.Services.GetFacility(selection.Id)); break;
                case SelectionKind.Vehicle: VehiclePanel(world, FindVehicle(world, selection.Id)); break;
                case SelectionKind.Citizen: CitizenDetail(world, world.Citizens.Get(selection.Id)); break;
                default: CityPanel(world); break;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        int Tabs(params string[] names)
        {
            if (tab >= names.Length) tab = 0;
            int t = GUILayout.Toolbar(tab, names);
            if (t != tab) deferred = () => { tab = t; page = 0; scroll = Vector2.zero; };
            return tab;
        }

        void Line(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, small, GUILayout.Width(150f));
            GUILayout.Label(value, small);
            GUILayout.EndHorizontal();
        }

        void Section(string title)
        {
            GUILayout.Space(6f);
            GUILayout.Label(title, sub);
        }

        /// <summary>Anklickbarer Verweis auf ein anderes Objekt.</summary>
        void Link(string label, Selection target)
        {
            if (GUILayout.Button(label, link)) deferred = () => Select(target);
        }

        void PlaceLine(SimulationWorld world, string label, Place p)
        {
            if (p.IsBuilding)
            {
                var b = world.City.GetBuilding(p.BuildingId);
                GUILayout.BeginHorizontal();
                GUILayout.Label(label, small, GUILayout.Width(150f));
                if (b != null) Link(BuildingName(b), new Selection(SelectionKind.Building, b.Id));
                else GUILayout.Label($"Gebäude #{p.BuildingId} (abgerissen)", small);
                GUILayout.EndHorizontal();
            }
            else Line(label, p.IsOutside ? $"außerhalb (Außenanschluss {p.ConnectionNodeId})" : "–");
        }

        /// <summary>Blättern in langen Listen. Liefert den ersten Index der Seite.</summary>
        int Pager(int count)
        {
            int pages = Mathf.Max(1, (count + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            if (pages > 1)
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = page > 0;
                if (GUILayout.Button("‹", GUILayout.Width(30f))) deferred = () => page--;
                GUI.enabled = true;
                GUILayout.Label($"Seite {page + 1}/{pages}", small, GUILayout.Width(90f));
                GUI.enabled = page < pages - 1;
                if (GUILayout.Button("›", GUILayout.Width(30f))) deferred = () => page++;
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            return page * PageSize;
        }

        // ------------------------------------------------------------ Gebäude

        void BuildingPanel(SimulationWorld world, Building b)
        {
            if (b == null) return;
            var citizens = world.Citizens;
            GUILayout.Label($"{Zones.DisplayName(b.Zone)} #{b.Id}", header);
            GUILayout.Label($"{UseName(b.Zone)} · {(b.IsConstructed ? "fertig" : $"<color=#ffd060>im Bau {b.Construction * 100f:0} %</color>")}", small);

            var tabs = new List<string> { "Übersicht" };
            if (b.Households > 0) tabs.Add("Bewohner");
            if (b.Jobs > 0) tabs.Add("Arbeit");
            tabs.Add("Umfeld");
            string current = tabs[Tabs(tabs.ToArray())];

            switch (current)
            {
                case "Übersicht":
                {
                    Section("Gebäude");
                    Line("Grundstück", $"{b.WidthCells}×{b.DepthCells} Zellen ({b.Width:0}×{b.Depth:0} m)");
                    Line("Höhe (sichtbar)", buildingView != null ? $"≈ {buildingView.VisualHeight(b):0} m" : "–");
                    Line("Stufe", b.Level.ToString());
                    Line("Baufortschritt", $"{b.Construction * 100f:0} %");
                    if (b.Households > 0) Line("Wohnungen", $"{b.OccupiedHouseholds}/{b.Households} belegt · {b.Residents} Bewohner");
                    if (b.Jobs > 0)
                    {
                        var ws = citizens.WorkersAt(b.Id);
                        int inbound = ws.Count(w => w.IsCommuterIn);
                        Line("Arbeitsplätze", $"{b.Workers}/{b.Jobs} besetzt · {ws.Count - inbound} Einheimische, {inbound} Einpendler");
                    }
                    Line("Gerade anwesend", citizens.PresentIn(b.Id).ToString());
                    var seg = world.Roads.GetSegment(b.FrontSegmentId);
                    Line("Zufahrt", seg != null ? $"{seg.Type.DisplayName} (Segment {seg.Id})" : "<color=#ff9080>keine</color>");

                    Section("Versorgung");
                    UtilityLine(world, b, UtilityKind.Power);
                    UtilityLine(world, b, UtilityKind.Water);

                    Section("Zustand & Zufriedenheit");
                    Line("Zustand", Pending);
                    if (b.Households > 0) Line("Zufriedenheit Bewohner", Pending);
                    if (b.Jobs > 0) Line("Zufriedenheit Mitarbeiter", Pending);
                    Line("Zufriedenheit Umfeld", Pending);
                    break;
                }
                case "Bewohner": ResidentsTab(world, b); break;
                case "Arbeit": WorkersTab(world, b); break;
                default: SurroundingsTab(world, b); break;
            }
        }

        void UtilityLine(SimulationWorld world, Building b, UtilityKind kind)
        {
            var u = world.Utilities;
            var s = u.GetStatus(b);
            string name = UtilityCatalog.KindName(kind);
            float demand = UtilityCatalog.Demand(b, kind);
            string need = $"Bedarf {demand:0.#} {UtilityCatalog.Unit(kind)}";
            if (!b.IsConstructed) { Line(name, "– (im Bau, noch kein Bedarf)"); return; }
            if (s.Has(kind))
            {
                var net = u.NetworkOf(b, kind);
                Line(name, $"<color=#80ff90>versorgt</color> · {need}{(net != null ? $" · Netz {net.Id}" : "")}");
                return;
            }
            if (!u.IsRequired(kind))
            {
                Line(name, $"<color=#c0c0c0>nicht versorgt – noch kein Versorger in der Stadt (folgenlos)</color> · {need}");
                return;
            }
            int without = s.TicksWithout(kind);
            string left = u.AbandonmentEnabled
                ? $" · verlassen in ≈ {Duration(Mathf.Max(0, UtilitySystem.AbandonAfterTicks - without))}"
                : "";
            Line(name, $"<color=#ff9080>fehlt seit {Duration(without)}</color>{left} · {need}");
        }

        void ResidentsTab(SimulationWorld world, Building b)
        {
            var hs = world.Citizens.HouseholdsIn(b.Id);
            Section($"Haushalte {hs.Count}/{b.Households} · {b.Residents} Bewohner");
            var kinds = hs.GroupBy(h => h.Kind).OrderBy(g => g.Key).Select(g => $"{HouseholdKinds.DisplayName(g.Key)} {g.Count()}");
            if (hs.Count > 0) GUILayout.Label(string.Join(" · ", kinds), small);
            if (hs.Count == 0) { GUILayout.Label("Noch niemand eingezogen.", small); return; }

            int start = Pager(hs.Count);
            for (int i = start; i < Mathf.Min(hs.Count, start + PageSize); i++)
            {
                var h = hs[i];
                GUILayout.Label($"<b>Haushalt {h.Id}</b> · {HouseholdKinds.DisplayName(h.Kind)} · seit Tag {h.MovedInDay}", small);
                foreach (var m in h.Members) Link("   " + Describe(m), new Selection(SelectionKind.Citizen, m.Id));
            }
        }

        void WorkersTab(SimulationWorld world, Building b)
        {
            var ws = world.Citizens.WorkersAt(b.Id);
            int inbound = ws.Count(w => w.IsCommuterIn);
            Section($"Beschäftigte {ws.Count}/{b.Jobs}");
            GUILayout.Label($"Einheimische {ws.Count - inbound} · Einpendler {inbound} · offene Stellen {Mathf.Max(0, b.Jobs - ws.Count)}", small);
            var edu = new int[4];
            foreach (var w in ws) edu[(int)w.Education]++;
            GUILayout.Label($"Bildung: ohne {edu[0]} · Grund {edu[1]} · mittel {edu[2]} · hoch {edu[3]}", small);
            int present = ws.Count(w => w.Activity == Activity.Work && w.Location.BuildingId == b.Id);
            GUILayout.Label($"Gerade bei der Arbeit: {present}", small);
            if (ws.Count == 0) return;

            GUILayout.Space(4f);
            int start = Pager(ws.Count);
            for (int i = start; i < Mathf.Min(ws.Count, start + PageSize); i++)
            {
                var w = ws[i];
                string from = w.IsCommuterIn ? "Einpendler" : $"wohnt in #{w.Home.BuildingId}";
                Link($"Bürger #{w.Id} · {w.Age} J., {EducationName(w.Education)} · {from} · {ActivityName(w)}", new Selection(SelectionKind.Citizen, w.Id));
            }
        }

        void SurroundingsTab(SimulationWorld world, Building b)
        {
            var env = world.Environment;
            Section("Umwelt am Gebäude");
            Line("Lärm", $"{env.Noise.Sample(b.Position) * 100f:0} % (Straßenseite {env.Noise.Sample(b.Entrance) * 100f:0} %)");
            Line("Abgas", $"{env.AirPollution.Sample(b.Position):0.00} (Rasterwert, ohne Einheit)");

            Section("Gelände");
            var terrain = world.Terrain;
            float natural = terrain.NaturalHeightAt(b.Position);
            Line("Bodenplatte", $"{b.Height:0.0} m (Wasserspiegel {terrain.WaterLevel:0} m)");
            Line("Ursprüngliches Gelände", $"{natural:0.0} m ({(b.Height - natural >= 0 ? "+" : "")}{b.Height - natural:0.0} m eingeebnet)");

            // G4: Abdeckung je Dienstart (0..1, sinkt mit Fahrzeit und Überlastung)
            Section("Dienste in Reichweite");
            var services = world.Services;
            foreach (var k in ServiceKinds.All)
            {
                float c = services.Coverage(k, b);
                string col = "#" + ColorUtility.ToHtmlStringRGB(Color.Lerp(CityBuilder.Game.Services.ServiceOverlayView.CoverageColor(c), Color.white, 0.25f));
                string extra = k == ServiceKinds.Education
                    ? $" (Grundschule {services.Coverage(ServiceKinds.EducationPrimary, b) * 100f:0} %, Oberschule {services.Coverage(ServiceKinds.EducationSecondary, b) * 100f:0} %)"
                    : "";
                Line(ServiceKinds.DisplayName(k), $"<color={col}>{c * 100f:0} %</color>{extra}");
            }
            Line("Müll am Gebäude", $"{services.GarbageAt(b):0.##} t ({services.GarbageLevel(b) * 100f:0} % voll)");
            Line("Brandrisiko", $"{services.FireRisk(b) * 100f:0} % (Kennwert, noch ohne Brände)");

            Section("Später");
            Line("Bodenwert", "<color=#9aa4b0>— (kommt mit G6)</color>");
            Line("Zufriedenheit Umfeld", Pending);
        }

        // ------------------------------------------------------------ Bürger

        void CitizenDetail(SimulationWorld world, Citizen c)
        {
            if (c == null) return;
            GUILayout.Label($"Bürger #{c.Id}", header);
            GUILayout.Label($"{c.Age} Jahre · {StageName(c)} · {ActivityName(c)}", small);

            switch (Tabs("Person", "Alltag", "Befinden"))
            {
                case 0:
                {
                    Section("Person");
                    Line("Alter", $"{c.Age} J. (Geburtstag: Tag {c.BirthDayOfYear + 1} des Jahres)");
                    Line("Bildung", EducationName(c.Education) + (c.Age < Citizen.AdultAge ? " (Abschluss mit 18)" : ""));
                    Line("Familienstand", "<color=#9aa4b0>— (eigenes Merkmal fehlt noch; siehe Haushalt)</color>");

                    Section("Haushalt & Wohnung");
                    if (c.Household == null)
                    {
                        Line("Haushalt", "keiner – wohnt außerhalb (Einpendler)");
                        PlaceLine(world, "Zuhause", c.Home);
                        break;
                    }
                    var h = c.Household;
                    Line("Haushaltsart", $"{HouseholdKinds.DisplayName(h.Kind)} ({h.Members.Count} Personen, Haushalt {h.Id})");
                    PlaceLine(world, "Wohnung", c.Home);
                    Line("Eingezogen", $"Tag {h.MovedInDay}");
                    foreach (var m in h.Members)
                    {
                        if (m == c) continue;
                        Link("   " + Describe(m), new Selection(SelectionKind.Citizen, m.Id));
                    }
                    break;
                }
                case 1:
                {
                    Section("Arbeit");
                    if (c.WorkplaceId != 0) PlaceLine(world, "Arbeitsplatz", c.Workplace);
                    else if (c.WorksOutside) Line("Arbeitsplatz", $"außerhalb – Auspendler über Anschluss {c.OutsideWorkConnectionId}");
                    else if (c.IsWorkingAge) Line("Arbeitsplatz", $"<color=#ff9080>arbeitslos</color> seit {c.UnemployedDays} Tagen");
                    else Line("Arbeitsplatz", c.Stage == LifeStage.Senior ? "Rente" : c.Stage == LifeStage.Student ? "Schüler (bleibt vorerst zu Hause, Schulen kommen mit G4)" : "Kind");
                    if (c.IsEmployed) Line("Arbeitszeit", $"ab {Clock(c.WorkStart)} Uhr, {CitizenSystem.WorkTicks * 6 / 60} h");
                    if (c.IsCommuterIn) Line("Pendeln", "Einpendler (wohnt außerhalb)");
                    else if (c.WorksOutside) Line("Pendeln", "Auspendler");

                    Section("Gerade");
                    Line("Tätigkeit", ActivityName(c));
                    if (c.Activity != Activity.Travelling) PlaceLine(world, "Ort", c.Location);
                    else
                    {
                        Line("Zweck", PurposeName(c.Purpose));
                        PlaceLine(world, "Ziel", c.Destination);
                        Line("Unterwegs seit", Duration(world.Clock.Tick - c.TripStartTick) + $" (ab {Clock(c.TripStartTick)} Uhr)");
                        if (c.Mode == TravelMode.Car)
                        {
                            var v = world.Citizens.VehicleOf(c);
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("Verkehrsmittel", small, GUILayout.Width(150f));
                            if (v != null) Link($"Auto – {VehicleClasses.DisplayName(v.Class)} #{v.Id}", new Selection(SelectionKind.Vehicle, v.Id));
                            else GUILayout.Label("Auto (gleich am Ziel)", small);
                            GUILayout.EndHorizontal();
                        }
                        else
                        {
                            Line("Verkehrsmittel", "zu Fuß/Rad/ÖPNV <color=#9aa4b0>(Platzhalter, Luftlinie 15 km/h)</color>");
                            long eta = c.PlannedArrivalTick - world.Clock.Tick;
                            Line("Ankunft", eta > 0 ? $"≈ {Clock(c.PlannedArrivalTick)} Uhr (in {Duration(eta)})" : "gleich");
                        }
                    }

                    Section("Bedürfnisse (vorhanden)");
                    NeedBar("Einkaufen", c.ShoppingNeed);
                    break;
                }
                default:
                {
                    Section("Befinden");
                    Line("Gesundheit", Pending);
                    Line("Zufriedenheit", Pending);
                    Line("Gemüt", Pending);

                    Section("Einflüsse (Rohwerte, noch ohne Bewertung)");
                    var hb = c.Home.IsBuilding ? world.City.GetBuilding(c.Home.BuildingId) : null;
                    if (hb != null)
                    {
                        var env = world.Environment;
                        Line("Lärm an der Wohnung", $"{env.Noise.Sample(hb.Position) * 100f:0} %");
                        Line("Abgas an der Wohnung", $"{env.AirPollution.Sample(hb.Position):0.00}");
                        var s = world.Utilities.GetStatus(hb);
                        Line("Wohnung Strom/Wasser", $"{(s.HasPower ? "ja" : "nein")} / {(s.HasWater ? "ja" : "nein")}");
                    }
                    else Line("Wohnung", "außerhalb – keine Werte");
                    if (c.IsWorkingAge) Line("Arbeit", c.IsEmployed ? "beschäftigt" : $"arbeitslos seit {c.UnemployedDays} Tagen");
                    Line("Einkaufsbedürfnis", $"{c.ShoppingNeed:0.00}");
                    GUILayout.Label("<color=#9aa4b0>Diese Werte gibt es schon; wie sie Gesundheit, Zufriedenheit und Gemüt beeinflussen, " +
                                    "legt das Bedürfnismodell fest.</color>", small);
                    break;
                }
            }
        }

        void NeedBar(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, small, GUILayout.Width(150f));
            var r = GUILayoutUtility.GetRect(160f, 14f, GUILayout.Width(160f));
            if (Event.current.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, 0.12f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = NeedColor;
                GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(value), r.height), Texture2D.whiteTexture);
                GUI.color = old;
            }
            GUILayout.Label(value >= 1f ? $"{value:0.00} <color=#ffb060>dringend</color>" : $"{value:0.00}", small);
            GUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------ Fahrzeug

        void VehiclePanel(SimulationWorld world, Vehicle v)
        {
            if (v == null) return;
            var traffic = world.Traffic;
            GUILayout.Label($"{VehicleClasses.DisplayName(v.Class)} #{v.Id}", header);

            Section("Fahrt");
            Line("Geschwindigkeit", $"{v.Speed * 3.6f:0} km/h (max. {v.MaxSpeed * 3.6f:0} km/h)");
            if (v.WaitTime > 0.5f) Line("Steht seit", $"{v.WaitTime:0} s");
            Line("Fahrtdauer", $"{FormatSeconds(traffic.Time - v.StartTime)} Verkehrszeit");
            if (v.InJunction) Line("Ort", "in einer Kreuzung");
            else if (v.Lane != null) Line("Ort", $"{v.Lane.Segment.Type.DisplayName} (Segment {v.Lane.Segment.Id})");
            Line("Restliche Abschnitte", v.RemainingEdges.ToString());

            var c = world.Citizens.CitizenForTrip(v.TripId);
            Section("Ziel & Fahrer");
            if (c != null)
            {
                PlaceLine(world, "Ziel", c.Destination);
                Line("Zweck", PurposeName(c.Purpose));
                GUILayout.BeginHorizontal();
                GUILayout.Label("Fahrer", small, GUILayout.Width(150f));
                Link($"Bürger #{c.Id} · {c.Age} J. · {(c.IsCommuterIn ? "Einpendler" : "Einwohner")}", new Selection(SelectionKind.Citizen, c.Id));
                GUILayout.EndHorizontal();
                Line("Unterwegs seit", $"{Duration(world.Clock.Tick - c.TripStartTick)} Spielzeit");
            }
            else
            {
                Line("Ziel", v.HasSegmentDestination ? $"Zufahrt an Segment {v.DestinationSegmentId}"
                    : v.Destination != null ? $"Knoten {v.Destination.Id} (Außenanschluss/Kartenrand)" : "–");
                Line("Fahrer", v.TripId != 0 ? "Bürgerfahrt (beendet)" : "Durchgangs-/Hintergrundverkehr (kein Bürger)");
            }
            GUILayout.Space(4f);
            GUILayout.Label("<color=#9aa4b0>Verkehr läuft mit 0,25 s je Tick, die Uhr mit 6 min je Tick – Autofahrten dauern daher " +
                            "in Spielzeit oft Stunden.</color>", small);
        }

        // ------------------------------------------------------------ Versorgungsbauwerk

        void FacilityPanel(SimulationWorld world, UtilityFacility f)
        {
            if (f == null) return;
            var t = f.Type;
            string unit = UtilityCatalog.Unit(f.Kind);
            GUILayout.Label($"{t.DisplayName} #{f.Id}", header);
            GUILayout.Label($"Versorgung {UtilityCatalog.KindName(f.Kind)}", small);

            Section("Leistung");
            Line("Aktuell", $"{f.Output:0.#} {unit} von {t.Capacity:0.#} {unit}");
            var nets = world.Utilities.Networks(f.Kind);
            var net = f.NetworkId >= 0 && f.NetworkId < nets.Count ? nets[f.NetworkId] : null;
            if (net != null)
            {
                Line("Netz", $"{net.Id}: Erzeugung {net.Production:0.#} / Bedarf {net.Demand:0.#} {unit}" +
                             (net.HasShortage ? " <color=#ff9080>Unterdeckung</color>" : ""));
                Line("Im Netz", $"{net.Facilities} Bauwerke · {net.SuppliedBuildings}/{net.Buildings} Gebäude versorgt · {net.Lines} Leitungen");
            }
            else Line("Netz", "<color=#ff9080>nicht angeschlossen</color>");

            Section("Bauwerk");
            Line("Größe", $"{t.Width:0}×{t.Depth:0} m · Boden {f.Height:0.0} m");
            Line("Kosten / Unterhalt", $"{t.Cost:0} / {t.UpkeepPerDay:0} je Tag (nur Anzeige, noch keine Stadtkasse)");
            if (t.NoiseLevel > 0f) Line("Lärm", $"{t.NoiseLevel * 100f:0} % im Umkreis {t.NoiseRadius:0} m");
            if (t.Pollution > 0f) Line("Abgas", $"{t.Pollution:0.##} im Umkreis {t.PollutionRadius:0} m");
            if (t.NeedsWater) Line("Standort", "braucht Wasser in Reichweite");
            Line("Zustand", "<color=#9aa4b0>— (Verschleiß/Wartung später)</color>");
        }

        // ------------------------------------------------------------ Dienstgebäude (G4)

        void ServicePanel(SimulationWorld world, ServiceFacility f)
        {
            if (f == null) return;
            var t = f.Type;
            GUILayout.Label($"{t.DisplayName} #{f.Id}", header);
            GUILayout.Label($"Dienst {ServiceKinds.DisplayName(t.Channel)}", small);

            Section("Leistung");
            Line("Kapazität", $"{t.Capacity:0} {t.CapacityUnit} je Tag" + (f.IsFull ? " <color=#ff9080>(voll – nimmt nichts mehr an)</color>" : ""));
            string load = f.EffectiveCapacity > 0f ? $"{f.Utilization * 100f:0} %" : "—";
            string over = f.Utilization > 1f ? $" <color=#ffb040>überlastet → Wirkung × {f.LoadFactor:0.00}</color>" : "";
            Line("Auslastung", $"{load} · Last {f.Load:0.#} {t.CapacityUnit}{over}");
            Line("Versorgt", $"{f.ServedBuildings} Gebäude");
            Line("Reichweite", t.Walking
                ? $"Fußweg {t.Range:0} m (Luftlinie)"
                : $"{t.Range:0.#} min Fahrzeit übers Straßennetz (voll bis {ServiceSystem.FullEffectShare * 100f:0} %)" +
                  (f.HasAccess ? "" : " <color=#ff9080>keine Zufahrt</color>"));
            if (t.Storage > 0f) Line("Lager", $"{f.Stored:0} / {t.Storage:0} t");
            if (f.Kind == ServiceKinds.Garbage) Line("Abgeholt", $"{f.CollectedPerDay:0.#} t/Tag");

            Section("Gebäude");
            Line("Größe", $"{t.Width:0}×{t.Depth:0} m · Boden {f.Height:0.0} m");
            Line("Kosten / Unterhalt", $"{t.Cost:0} € / {t.UpkeepPerWeek:0} € je Woche (Stadtkasse, Kategorie Dienste)");
            if (t.NoiseLevel > 0f) Line("Lärm", $"{t.NoiseLevel * 100f:0} % im Umkreis {t.NoiseRadius:0} m");
            if (t.Pollution > 0f) Line("Abgas", $"{t.Pollution:0.##} im Umkreis {t.PollutionRadius:0} m");
        }

        // ------------------------------------------------------------ Stadtübersicht

        void CityPanel(SimulationWorld world)
        {
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
            GUILayout.Label("Klick auf Gebäude, Fahrzeug oder Kraftwerk/Pumpe zeigt Details; Bewohner und Beschäftigte sind dort " +
                            "anklickbar. F: Fahrzeug/Bürger verfolgen. Rechtsklick: Auswahl aufheben. C: Info schließen.", small);
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

        // ------------------------------------------------------------ Texte

        static string BuildingName(Building b) => $"{Zones.DisplayName(b.Zone)} #{b.Id}";

        static string UseName(ZoneType z)
        {
            switch (z)
            {
                case ZoneType.ResidentialLow:
                case ZoneType.ResidentialMedium:
                case ZoneType.ResidentialHigh: return "Wohngebäude";
                case ZoneType.Mixed: return "Wohnen + Läden";
                case ZoneType.Retail: return "Geschäft";
                case ZoneType.Office: return "Bürogebäude";
                case ZoneType.Industry: return "Industriebetrieb";
                default: return "Gebäude";
            }
        }

        static string Describe(Citizen c)
        {
            string job = c.WorkplaceId != 0 ? $"arbeitet in #{c.WorkplaceId}" : c.WorksOutside ? "Auspendler" :
                c.IsWorkingAge ? "<color=#ff9080>arbeitslos</color>" : StageName(c);
            return $"Bürger #{c.Id} · {c.Age} J. · {job} · {ActivityName(c)}";
        }

        static string StageName(Citizen c)
        {
            switch (c.Stage)
            {
                case LifeStage.Child: return "Kind";
                case LifeStage.Student: return "Schüler";
                case LifeStage.Senior: return "Rente";
                default: return "Erwachsen";
            }
        }

        static string ActivityName(Citizen c)
        {
            switch (c.Activity)
            {
                case Activity.Home: return "zu Hause";
                case Activity.Work: return "bei der Arbeit";
                case Activity.Shopping: return "einkaufen";
                case Activity.Travelling: return c.Mode == TravelMode.Car ? "im Auto unterwegs" : "unterwegs";
                default: return "außerhalb";
            }
        }

        static string PurposeName(TripPurpose p)
        {
            switch (p)
            {
                case TripPurpose.ToWork: return "zur Arbeit";
                case TripPurpose.ToShop: return "zum Einkaufen";
                case TripPurpose.ToHome: return "nach Hause";
                default: return "–";
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

        /// <summary>Dauer in Ticks (1 Tick = 6 Spielminuten) als Text.</summary>
        static string Duration(long ticks)
        {
            if (ticks < 0) ticks = 0;
            long minutes = ticks * 6;
            if (minutes < 60) return $"{minutes} min";
            if (minutes < 60 * 24) return $"{minutes / 60} h {minutes % 60:00} min";
            return $"{minutes / (60 * 24)} T {minutes / 60 % 24} h";
        }

        /// <summary>Uhrzeit eines Ticks bzw. eines Ticks im Tag.</summary>
        static string Clock(long tick)
        {
            long tod = ((tick % SimulationClock.TicksPerDay) + SimulationClock.TicksPerDay) % SimulationClock.TicksPerDay;
            long minutes = tod * 6;
            return $"{minutes / 60:00}:{minutes % 60:00}";
        }

        static string FormatSeconds(float s)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(s));
            return total < 60 ? $"{total} s" : $"{total / 60} min {total % 60:00} s";
        }

        // ------------------------------------------------------------ Marker

        /// <summary>Hervorhebung aus zwei Linienzügen: Rechteck am Boden + auf Dachhöhe bzw. Ring um ein Fahrzeug.</summary>
        sealed class Marker
        {
            const int RingPoints = 24;
            readonly LineRenderer lower, upper;

            public Marker(Transform parent, Material material, bool withUpper)
            {
                lower = Create(parent, material, "Inspect Marker");
                if (withUpper) upper = Create(parent, material, "Inspect Marker (oben)");
            }

            static LineRenderer Create(Transform parent, Material material, string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = material;
                lr.useWorldSpace = true;
                lr.loop = true;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.enabled = false;
                return lr;
            }

            public void Hide()
            {
                lower.enabled = false;
                if (upper != null) upper.enabled = false;
            }

            /// <summary>Viereck (Ecken in Simulationskoordinaten) unten und – falls vorhanden – oben.</summary>
            public void Rect(System.Numerics.Vector2[] corners, float bottom, float top, float width)
            {
                SetRect(upper ?? lower, corners, top, width);
                if (upper != null) SetRect(lower, corners, bottom, width * 0.7f);
            }

            static void SetRect(LineRenderer lr, System.Numerics.Vector2[] corners, float h, float width)
            {
                lr.positionCount = corners.Length;
                for (int i = 0; i < corners.Length; i++) lr.SetPosition(i, SimConvert.ToWorld(corners[i], h));
                lr.widthMultiplier = width;
                lr.enabled = true;
            }

            public void Ring(Vector3 center, float radius, float width)
            {
                SetRing(lower, center + Vector3.down * 0.8f, radius, width);
                if (upper != null) SetRing(upper, center + Vector3.up * 3.5f, radius * 0.35f, width);
            }

            static void SetRing(LineRenderer lr, Vector3 center, float radius, float width)
            {
                lr.positionCount = RingPoints;
                for (int i = 0; i < RingPoints; i++)
                {
                    float a = i * Mathf.PI * 2f / RingPoints;
                    lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
                }
                lr.widthMultiplier = width;
                lr.enabled = true;
            }
        }
    }
}
