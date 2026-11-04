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
    /// Liest nur – Änderungen laufen über die Simulation.
    /// <para>
    /// Diese Komponente enthält die <b>Logik</b> (Auswahl, Verlauf, Picking, Hervorhebung, Verfolgen, Reiter/Seite).
    /// Die Darstellung ist seit Phase 2 das rechte Panel <see cref="InspectorWindow"/> (UI Toolkit); die frühere
    /// IMGUI-Darstellung wurde entfernt (s. <see cref="CityBuilder.Game.UI.Panels.UiPanels"/>).
    /// </para>
    /// </summary>
    public sealed class InspectorPanel : MonoBehaviour
    {
        public const int PageSize = 25;
        const int MaxHistory = 30;

        static readonly Color PrimaryColor = new Color(1f, 0.85f, 0.2f, 0.95f);
        static readonly Color HomeColor = new Color(0.45f, 0.95f, 0.45f, 0.9f);
        static readonly Color WorkColor = new Color(0.45f, 0.75f, 1f, 0.9f);

        RoadTool tool;
        BuildingView buildingView;
        CameraController cameraController;
        readonly List<Material> materials = new List<Material>();
        Marker primary, home, work;

        Selection selection;
        readonly List<Selection> history = new List<Selection>();
        bool follow;
        bool followWasSet;
        /// <summary>Bürger des zuletzt gezeigten Fahrzeugs – kommt das Auto an, springt die Auswahl zum Bürger.</summary>
        int vehicleCitizenId;

        public Selection Current => selection;

        /// <summary>Gewählter Reiter (0 = erster) – springt bei neuer Auswahl auf 0.</summary>
        public int Tab { get; set; }

        /// <summary>Seite in langen Listen (Bewohner, Beschäftigte).</summary>
        public int Page { get; set; }

        /// <summary>Zählt jede Änderung der Auswahl (Fenster scrollt dann nach oben).</summary>
        public int SelectionVersion { get; private set; }

        public bool CanGoBack => history.Count > 0;

        /// <summary>Kamera folgt dem gewählten Fahrzeug/Bürger (Taste F).</summary>
        public bool Following
        {
            get => follow;
            set => follow = value && CanFollow;
        }

        public BuildingView BuildingView => buildingView;

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            if (tool.Runner != null) tool.Runner.WorldReplaced += ClearSelection; // Speichern & Laden: Auswahl zeigt in die alte Welt
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

        /// <summary>Info-Modus an und Simulation bereit.</summary>
        public bool Active => tool != null && tool.CurrentMode == RoadTool.Mode.Inspect && tool.Runner != null && tool.Runner.World != null;
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
            SetSelection(s);
            if (s.Kind != SelectionKind.Vehicle && s.Kind != SelectionKind.Citizen) follow = false;
        }

        void SetSelection(Selection s)
        {
            selection = s;
            Tab = 0;
            Page = 0;
            SelectionVersion++;
        }

        /// <summary>„← Zurück“: vorige Auswahl im Verlauf.</summary>
        public void Back()
        {
            if (history.Count == 0) { SetSelection(Selection.None); return; }
            var prev = history[history.Count - 1];
            history.RemoveAt(history.Count - 1);
            SetSelection(prev);
        }

        /// <summary>„Übersicht“/Rechtsklick: Auswahl und Verlauf leeren.</summary>
        public void ClearSelection()
        {
            if (!selection.IsNone) SetSelection(Selection.None);
            history.Clear();
            follow = false;
        }

        /// <summary>Reiter wählen (Seite zurück auf 1).</summary>
        public void SelectTab(int tab)
        {
            if (tab == Tab) return;
            Tab = tab;
            Page = 0;
            SelectionVersion++;
        }

        /// <summary>
        /// UX: Stadtübersicht ausdrücklich angefordert (Info-Knopf/C). Ohne Auswahl und ohne Übersicht bleibt das Panel zu –
        /// im Auswählen-Modus (Standard) wird nichts aufgedrängt.
        /// </summary>
        public bool ShowOverview { get; set; }

        /// <summary>Soll das Inspektor-Panel offen sein? (Auswahl vorhanden oder Übersicht angefordert)</summary>
        public bool WantsPanel => Active && (!selection.IsNone || ShowOverview);

        FacilityMover mover;
        bool MoverBusy => (mover != null || (mover = GetComponent<FacilityMover>()) != null) && mover.Moving;

        /// <summary>Panel schließen (×): Auswahl und Übersicht weg, der Auswählen-Modus bleibt.</summary>
        public void Close()
        {
            ClearSelection();
            ShowOverview = false;
        }

        void Update()
        {
            if (cameraController == null && tool != null && tool.Camera != null) cameraController = tool.Camera.GetComponent<CameraController>();
            if (!Active)
            {
                ShowOverview = false;
                HideMarkers();
                StopFollowing();
                return;
            }
            var world = World;
            var mouse = Mouse.current;
            var kb = GameKeyboard.Current;
            if (mouse != null)
            {
                var screen = mouse.position.ReadValue();
                bool overUi = UiBlocker.IsPointerOverUi(screen);
                bool moving = MoverBusy; // UX: während Verschieben gehören die Klicks dem FacilityMover
                if (mouse.leftButton.wasPressedThisFrame && !overUi && tool.Camera != null && !moving) PickAt(screen);
                if (mouse.rightButton.wasPressedThisFrame && !overUi && !moving) ClearSelection();
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
            // --- Bahn: Züge (Kugel um die Wagen) vor Gebäuden/Gelände
            int train = Rail.RailView.Active != null ? Rail.RailView.Active.PickTrain(ray) : 0;
            if (train != 0 && (hit.IsNone || hit.Kind == SelectionKind.Building || hit.Kind == SelectionKind.Gateway)) hit = new Selection(SelectionKind.Train, train);
            // --- Ende Bahn
            // --- Wasserstraße: Schiffe (Kugel um den Rumpf) vor Gebäuden/Gelände
            int ship = Shipping.ShippingView.Active != null ? Shipping.ShippingView.Active.PickShip(ray) : 0;
            if (ship != 0 && (hit.IsNone || hit.Kind == SelectionKind.Building || hit.Kind == SelectionKind.Gateway)) hit = new Selection(SelectionKind.Ship, ship);
            // --- Ende Wasserstraße
            // --- Flughafen: Flugzeuge (Kugel um den Rumpf) vor Gebäuden/Gelände
            int plane = Aviation.AirportView.Active != null ? Aviation.AirportView.Active.PickAircraft(ray) : 0;
            if (plane != 0 && (hit.IsNone || hit.Kind == SelectionKind.Building || hit.Kind == SelectionKind.Gateway)) hit = new Selection(SelectionKind.Aircraft, plane);
            // --- Ende Flughafen
            if (hit.IsNone && tool.HasCursor)
            {
                // Rückfall: Geländepunkt unter der Maus
                var b = world.City.BuildingAt(tool.Cursor);
                var f = b == null ? world.Utilities.FacilityAt(tool.Cursor) : null;
                var sf = b == null && f == null ? world.Services.FacilityAt(tool.Cursor) : null; // G4
                if (b != null) hit = new Selection(SelectionKind.Building, b.Id);
                else if (f != null) hit = new Selection(SelectionKind.Facility, f.Id);
                else if (sf != null) hit = new Selection(SelectionKind.Service, sf.Id);
                else if (world.Transit?.DepotAt(tool.Cursor) is var td && td != null) hit = new Selection(SelectionKind.TransitDepot, td.Id); // ÖPNV
                else if (world.Rail?.StationAt(tool.Cursor, 1f) is var rs && rs != null) hit = new Selection(SelectionKind.Station, rs.Id); // Bahn
                else if (world.Shipping?.HarborAt(tool.Cursor, 1f) is var hb && hb != null) hit = new Selection(SelectionKind.Harbor, hb.Id); // Wasserstraße
                else if (world.Shipping?.DrawbridgeNear(tool.Cursor, 12f) is var db && db != null) hit = new Selection(SelectionKind.Drawbridge, db.SegmentId); // Wasserstraße
                else if (world.Aviation?.AirportAt(tool.Cursor, 1f) is var ap && ap != null) hit = new Selection(SelectionKind.Airport, ap.Id); // Flughafen
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
                    if (world.City.GetBuilding(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.Facility:
                    if (world.Utilities.GetFacility(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.Service:
                    if (world.Services.GetFacility(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.Citizen:
                    if (world.Citizens.Get(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.TransitStop: // ÖPNV
                    if (world.Transit?.GetStop(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.TransitDepot: // ÖPNV
                    if (world.Transit?.GetDepot(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.Gateway: // Außenanbindung
                    if (world.Gateways?.Get(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.Train: // Bahn
                    if (world.Rail?.GetTrain(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.Station: // Bahn
                    if (world.Rail?.GetStation(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.Ship: // Wasserstraße
                    if (world.Shipping?.GetShip(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.Harbor: // Wasserstraße
                    if (world.Shipping?.GetHarbor(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.Drawbridge: // Wasserstraße
                    if (world.Shipping?.DrawbridgeOf(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.Aircraft: // Flughafen
                    if (world.Aviation?.GetAircraft(selection.Id) == null) SetSelection(Selection.None);
                    break;
                case SelectionKind.Airport: // Flughafen
                    if (world.Aviation?.GetAirport(selection.Id) == null) SetSelection(Selection.None);
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
                    SetSelection(driver != null ? new Selection(SelectionKind.Citizen, driver.Id) : Selection.None);
                    vehicleCitizenId = 0;
                    break;
                }
            }
            if (selection.IsNone) follow = false;
        }

        public static Vehicle FindVehicle(SimulationWorld world, int id)
        {
            foreach (var v in world.Traffic.ActiveVehicles) if (v.Id == id) return v;
            return null;
        }

        public bool CanFollow => selection.Kind == SelectionKind.Vehicle || selection.Kind == SelectionKind.Citizen || selection.Kind == SelectionKind.Train // Bahn: Zug
            || selection.Kind == SelectionKind.Ship // Wasserstraße: Schiff
            || selection.Kind == SelectionKind.Aircraft; // Flughafen: Flugzeug

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
                case SelectionKind.TransitDepot: // ÖPNV
                {
                    var d = world.Transit?.GetDepot(selection.Id);
                    if (d != null) primary.Rect(d.Corners(), d.Height + 0.3f, d.Height + 9.5f, width);
                    break;
                }
                case SelectionKind.TransitStop: // ÖPNV
                {
                    var s = world.Transit?.GetStop(selection.Id);
                    if (s != null) primary.Ring(SimConvert.ToWorld(s.Position, world.Terrain.HeightAt(s.Position) + 0.6f), 8f, width * 0.6f);
                    break;
                }
                case SelectionKind.Gateway: // Außenanbindung
                {
                    var g = world.Gateways?.Get(selection.Id);
                    if (g != null && g.HasLocation)
                    {
                        var p = InspectorPicking.GatewayPoint(world, g);
                        primary.Ring(new Vector3(p.x, p.y - 7f, p.z), 30f, width);
                    }
                    break;
                }
                case SelectionKind.Train: // Bahn
                    if (Rail.RailView.Active != null && Rail.RailView.Active.TryGetTrainPosition(selection.Id, out var tp)) primary.Ring(tp, 14f, width * 0.6f);
                    break;
                case SelectionKind.Station: // Bahn
                {
                    var st = world.Rail?.GetStation(selection.Id);
                    if (st != null) primary.Rect(st.Footprint.Corners(), st.Height + 0.3f, st.Height + 12f, width);
                    break;
                }
                // --- Wasserstraße
                case SelectionKind.Ship:
                    if (Shipping.ShippingView.Active != null && Shipping.ShippingView.Active.TryGetShipPosition(selection.Id, out var sp)) primary.Ring(sp, 30f, width * 0.6f);
                    break;
                case SelectionKind.Harbor:
                {
                    var hb = world.Shipping?.GetHarbor(selection.Id);
                    if (hb != null) primary.Rect(hb.Footprint.Corners(), hb.Height + 0.3f, hb.Height + 14f, width);
                    break;
                }
                case SelectionKind.Drawbridge:
                {
                    var db = world.Shipping?.DrawbridgeOf(selection.Id);
                    if (db != null) primary.Ring(SimConvert.ToWorld(db.Position, db.Height + 1f), 24f, width * 0.6f);
                    break;
                }
                // --- Ende Wasserstraße
                // --- Flughafen
                case SelectionKind.Aircraft:
                    if (Aviation.AirportView.Active != null && Aviation.AirportView.Active.TryGetAircraftPosition(selection.Id, out var apos)) primary.Ring(apos, 30f, width * 0.6f);
                    break;
                case SelectionKind.Airport:
                {
                    var ap = world.Aviation?.GetAirport(selection.Id);
                    if (ap != null) primary.Rect(ap.Site.Corners(), ap.Height + 0.3f, ap.Height + 12f, width);
                    break;
                }
                // --- Ende Flughafen
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
                    // --- Fußgänger: Ring um die sichtbare Person
                    else if (People.PeopleView.Active != null && People.PeopleView.Active.TryGetPosition(c, out var walker))
                        primary.Ring(walker, 1.6f, width * 0.4f);
                    // --- Ende Fußgänger
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
                    if (People.PeopleView.Active != null && People.PeopleView.Active.TryGetPosition(c, out var walker)) return walker; // Fußgänger
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
                case SelectionKind.TransitStop: // ÖPNV
                {
                    var s = world.Transit?.GetStop(selection.Id);
                    return s != null ? SimConvert.ToWorld(s.Position, world.Terrain.HeightAt(s.Position)) : (Vector3?)null;
                }
                case SelectionKind.TransitDepot: // ÖPNV
                {
                    var d = world.Transit?.GetDepot(selection.Id);
                    return d != null ? SimConvert.ToWorld(d.Position, d.Height) : (Vector3?)null;
                }
                case SelectionKind.Gateway: // Außenanbindung
                {
                    var g = world.Gateways?.Get(selection.Id);
                    return g != null && g.HasLocation ? InspectorPicking.GatewayPoint(world, g) : (Vector3?)null;
                }
                case SelectionKind.Train: // Bahn
                    return Rail.RailView.Active != null && Rail.RailView.Active.TryGetTrainPosition(selection.Id, out var tp) ? tp : (Vector3?)null;
                case SelectionKind.Station: // Bahn
                {
                    var st = world.Rail?.GetStation(selection.Id);
                    return st != null ? SimConvert.ToWorld(st.Position, st.Height) : (Vector3?)null;
                }
                // --- Wasserstraße
                case SelectionKind.Ship:
                    return Shipping.ShippingView.Active != null && Shipping.ShippingView.Active.TryGetShipPosition(selection.Id, out var sp) ? sp : (Vector3?)null;
                case SelectionKind.Harbor:
                {
                    var hb = world.Shipping?.GetHarbor(selection.Id);
                    return hb != null ? SimConvert.ToWorld(hb.Position, hb.Height) : (Vector3?)null;
                }
                case SelectionKind.Drawbridge:
                {
                    var db = world.Shipping?.DrawbridgeOf(selection.Id);
                    return db != null ? SimConvert.ToWorld(db.Position, db.Height) : (Vector3?)null;
                }
                // --- Ende Wasserstraße
                // --- Flughafen
                case SelectionKind.Aircraft:
                    return Aviation.AirportView.Active != null && Aviation.AirportView.Active.TryGetAircraftPosition(selection.Id, out var apos) ? apos : (Vector3?)null;
                case SelectionKind.Airport:
                {
                    var ap = world.Aviation?.GetAirport(selection.Id);
                    return ap != null ? SimConvert.ToWorld(ap.Layout.Terminal.Center, ap.Height) : (Vector3?)null;
                }
                // --- Ende Flughafen
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
