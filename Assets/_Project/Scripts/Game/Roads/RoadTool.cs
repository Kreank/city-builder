using System.Collections.Generic;
using CityBuilder.Game.Environment;
using CityBuilder.Game.Traffic;
using CityBuilder.Game.UI;
using CityBuilder.Game.Zoning;
using CityBuilder.Simulation.Zoning;
using CityBuilder.Simulation.Roads;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Roads
{
    /// <summary>
    /// Straßen- und Verkehrswerkzeug (wie in Cities: Skylines).
    ///   Gerade/Kurve: Klick Start → (Kontrollpunkt) → Ende; Bild↑/↓ = Bauhöhe (Brücken).
    ///   Kreisverkehr: Mittelpunkt → Radius. Vorlagen: Autobahnknoten platzieren (, und . drehen).
    ///   Ausbauen/Richtung: Straße anklicken. Verkehr: Kreuzung/Straße wählen und Regeln setzen.
    /// Alle Prüfungen und das eigentliche Bauen macht die Simulation (<see cref="RoadBuilder"/>).
    /// </summary>
    public sealed class RoadTool : MonoBehaviour
    {
        public enum Mode { Straight, Curve, Roundabout, Upgrade, Reverse, Template, Traffic, Bulldoze, Zone, Terraform, Utility, Inspect, Service, Transit }

        /// <summary>Alles, was Hilfslinien und Beschriftungen zum aktuellen Bauvorgang wissen müssen.</summary>
        public struct GuideState
        {
            public bool Active;
            public bool IsCurve;
            public bool PlacingControl;
            public bool Valid;
            public string Error;
            public string Info;
            public SimVector2 Start, Control, End;
            public float StartHeight, EndHeight;
            public float Length;
            public float Cost;
            public RoadType Type;

            public bool HasStartReference;
            public SimVector2 StartReference;
            public SimVector2 StartDirection;
            public float StartAngle;
            public float Bearing;

            public bool HasEndReference;
            public SimVector2 EndReference;
            public SimVector2 EndDirection;
            public float EndAngle;

            public bool IsRoundabout;
            public SimVector2 Center;
            public float Radius;

            /// <summary>Nur Beschriftung (Ausbauen, Vorlagen …), keine Winkelhilfen.</summary>
            public bool LabelOnly;
        }

        public const float AngleSnapStep = 15f;
        public const float LengthSnapStep = 8f;
        public const float RadiusSnapStep = 2f;
        public const float HeightStep = 2f;

        [SerializeField] SimulationRunner runner;
        [SerializeField] Camera cam;
        [SerializeField] Material previewMaterial;
        [SerializeField] Material invalidMaterial;
        [SerializeField] float snapDistance = 10f;
        [SerializeField] float previewHeight = 0.3f;

        enum Step { Start, Control, End }

        Step step = Step.Start;
        SimVector2 startPoint;
        float startHeight;
        SimVector2 controlPoint;
        RoundaboutPlan currentRoundabout;
        TemplatePlan currentTemplate;
        SimVector2 templatePlannedAt;
        float templatePlannedRotation = float.NaN;
        int templatePlannedVersion = -1;
        Mesh previewMesh;
        MeshRenderer previewRenderer;
        readonly List<RoadMeshBuilder.PreviewCurve> previewCurves = new List<RoadMeshBuilder.PreviewCurve>();

        /// <summary>UX: Start im Auswählen-Modus (<see cref="SelectMode"/>) – nichts „in der Maus“.</summary>
        public Mode CurrentMode { get; private set; } = Mode.Inspect;
        public RoadType SelectedType { get; private set; } = RoadCatalog.CityTwoLane;
        public RoadTemplateKind TemplateKind { get; private set; } = RoadTemplateKind.DiamondInterchange;
        public float TemplateRotation { get; private set; }
        public bool DualCarriageway { get; private set; } = true;
        public float BuildHeight { get; private set; }
        public bool HasCursor { get; private set; }
        public SimVector2 Cursor { get; private set; }
        public RoadAnchor CursorAnchor { get; private set; }
        public bool SnapEnabled { get; private set; } = true;
        public bool SnapActive { get; private set; }
        public GuideState Guide { get; private set; }
        public float PreviewLength => Guide.Active ? Guide.Length : 0f;
        public Camera Camera => cam;
        public SimulationRunner Runner => runner;

        /// <summary>Gewählte Kreuzung bzw. Straße im Verkehrsregel-Modus.</summary>
        public RoadNode SelectedNode { get; private set; }
        public RoadSegment SelectedSegment { get; private set; }

        public bool IsBuildMode => CurrentMode == Mode.Straight || CurrentMode == Mode.Curve || CurrentMode == Mode.Roundabout;
        public bool UsesDualCarriageway => DualCarriageway && SelectedType.IsHighway && IsBuildMode && CurrentMode != Mode.Roundabout;

        /// <summary>Kreisverkehre brauchen eine Einbahn-Stadtstraße; sonst Standardtyp.</summary>
        public RoadType RoundaboutType =>
            SelectedType.IsOneWay && SelectedType.Category == RoadCategory.City ? SelectedType : RoadCatalog.DefaultRoundabout;

        void Start()
        {
            if (cam == null) cam = Camera.main;

            var go = new GameObject("Road Preview");
            go.transform.SetParent(transform, false);
            previewMesh = new Mesh();
            go.AddComponent<MeshFilter>().sharedMesh = previewMesh;
            previewRenderer = go.AddComponent<MeshRenderer>();
            previewRenderer.sharedMaterial = previewMaterial;
            previewRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            previewRenderer.enabled = false;

            // --- Speichern & Laden: neue Welt → laufenden Bau abbrechen, Auswahl/Vorlage verwerfen
            runner.WorldReplaced += () => { Cancel(); ClearSelection(); currentTemplate = null; templatePlannedVersion = -1; };
            // --- Ende Speichern & Laden

            // Zugehörige Anzeigen hängen am Werkzeug – so muss die Szene nicht neu erzeugt werden.
            Ensure<RoadGuidesView>().Init(this, previewMaterial);
            Ensure<TrafficPanel>().Init(this, previewMaterial);

            var networkView = GetComponent<RoadNetworkView>();
            var litBase = networkView != null ? networkView.BaseMaterial : null;
            if (litBase != null)
            {
                Ensure<VehicleView>().Init(runner, litBase);
                Ensure<TrafficSignsView>().Init(runner, litBase);
            }
            Ensure<TrafficOverlayView>().Init(runner, previewMaterial);

            // Boden mit Löchern für Tröge/Tunneleinfahrten (ersetzt die flache Platzhalter-Plane)
            var groundGo = GameObject.Find("Ground");
            var groundRenderer = groundGo != null ? groundGo.GetComponent<MeshRenderer>() : null;
            if (groundRenderer != null)
            {
                ground = Ensure<GroundView>();
                ground.Init(runner, groundRenderer, previewMaterial);
            }
            Ensure<ScenicPropsView>().Init(runner);
            // --- Eigene Assets: Büsche/Felsen, Straßen-/Parkausstattung, Brückenpfeiler/Tunnelportale/Schilderbrücken/Baustellen,
            // Bauernhöfe (Felder je Jahreszeit), Lärmschutzwände – alles aus dem OwnModelCatalog (fehlt ein Modell: wie bisher)
            Ensure<CityBuilder.Game.Environment.NatureDetailView>().Init(runner);
            Ensure<CityBuilder.Game.Environment.StreetFurnitureView>().Init(runner);
            Ensure<InfrastructureDecoView>().Init(runner);
            Ensure<CityBuilder.Game.Zoning.FarmView>().Init(runner);
            if (litBase != null) Ensure<CityBuilder.Game.Environment.NoiseBarrierView>().Init(runner, litBase);
            // --- Ende Eigene Assets

            // Zonen und Gebäude
            zoneView = Ensure<ZoneView>();
            zoneView.Init(runner, previewMaterial);
            if (litBase != null) Ensure<BuildingView>().Init(runner, litBase);
            // --- Fußgänger: sichtbare Bürger (zu Fuß, Park, Laden, Haustür) per GPU-Instancing
            if (litBase != null) Ensure<CityBuilder.Game.People.PeopleView>().Init(runner, litBase);
            // --- Ende Fußgänger

            // Info-Modus (C): Inspektor für Gebäude, Fahrzeuge, Versorgungsbauwerke und Bürger (+ Stadtübersicht)
            Ensure<CityBuilder.Game.Inspect.InspectorPanel>().Init(this, previewMaterial);
            Ensure<CityBuilder.Game.Inspect.FacilityMover>().Init(this, previewMaterial); // UX: Bauwerke verschieben (Y)

            // Terraforming-Werkzeug (Modus Terraform, Taste L)
            Ensure<CityBuilder.Game.Terrain.TerraformTool>().Init(this, previewMaterial);

            // --- G3 Strom & Wasser: Werkzeug (Modus Utility), Bauwerke/Leitungen, Info-Ansicht, Netz-Fenster
            Ensure<CityBuilder.Game.Utilities.UtilityTool>().Init(this, previewMaterial);
            var utilityOverlay = Ensure<CityBuilder.Game.Utilities.UtilityOverlayView>();
            if (litBase != null)
            {
                Ensure<CityBuilder.Game.Utilities.UtilityView>().Init(runner, litBase, ground);
                utilityOverlay.Init(runner, litBase, previewMaterial);
            }
            Ensure<CityBuilder.Game.Utilities.UtilityPanel>().Init(this, utilityOverlay);
            Ensure<CityBuilder.Game.Utilities.UtilityAlerts>().Init(runner); // Meldungen (Toasts) zur Versorgung
            Ensure<CityBuilder.Game.Citizens.CitizenAlerts>().Init(runner);  // Bedürfnismodell: Geburten/Hochzeiten als Tagesmeldung
            Ensure<CityBuilder.Game.Services.MediaAlerts>().Init(runner);    // G4 erweitert: Schlagzeilen der Medien als Meldung
            // --- Ende G3

            // --- G4 Dienste: Werkzeug (Modus Service, Taste 4), Gebäude, Abdeckungs-Ansicht (⇧4), Fenster
            Ensure<CityBuilder.Game.Services.ServiceTool>().Init(this, previewMaterial);
            var serviceOverlay = Ensure<CityBuilder.Game.Services.ServiceOverlayView>();
            if (litBase != null)
            {
                Ensure<CityBuilder.Game.Services.ServiceView>().Init(runner, litBase);
                serviceOverlay.Init(runner, litBase, previewMaterial);
            }
            Ensure<CityBuilder.Game.Services.ServicePanel>().Init(this, serviceOverlay);
            // --- Ende G4

            // --- ÖPNV: Werkzeug (Modus Transit, Taste 5), Haltestellen/Depots/Linien/Busfarben, Linienfenster (F6)
            Ensure<CityBuilder.Game.Transit.TransitTool>().Init(this, previewMaterial);
            if (litBase != null) Ensure<CityBuilder.Game.Transit.TransitView>().Init(runner, litBase, previewMaterial);
            // --- Ende ÖPNV

            // --- Stadtkasse: Fenster (Taste M)
            Ensure<CityBuilder.Game.Economy.BudgetPanel>().Init(this);
            // --- Ende Stadtkasse

            // --- Welt (Etappe 5): Weltkarte (F4); Ressourcen-Ansicht (F3) hängt am EnvironmentOverlayView
            Ensure<CityBuilder.Game.WorldMap.WorldMapWindow>().Init(this);
            // --- Ende Welt

            // --- Außenanbindungen (Gateways): Schilder am Kartenrand, Bahnkorridor, Hinweis „anschließen“
            Ensure<CityBuilder.Game.Gateways.GatewayView>().Init(this, previewMaterial);
            // --- Ende Gateways

            // --- G5 Warenketten: Fenster „Wirtschaft“ (Taste F5; IMGUI-Rückfall, neue UI: GoodsWindow)
            Ensure<CityBuilder.Game.Goods.GoodsPanel>().Init(this);
            // --- Ende G5

            // --- Speichern & Laden: Spielmenü (Esc), Strg+S/Strg+L, Autosave
            Ensure<CityBuilder.Game.Persistence.SaveManager>().Init(this);
            // --- Ende Speichern & Laden

            // Spiel-UI (UI Toolkit) zuletzt – sie greift auf die Begleiter oben zu. Fehlen Theme/USS in Resources,
            // bleibt die alte IMGUI-Bauleiste als Rückfall.
            if (!Ensure<CityBuilder.Game.UI.Hud.GameHud>().Init(this)) Ensure<RoadToolbar>().Init(this);
        }

        /// <summary>Unterirdisch-Ansicht erzwingen (G3: Wasserrohre verlegen).</summary>
        public bool ForceUnderground { get; set; }

        ZoneView zoneView;

        /// <summary>Gewählte Zone im Zonen-Werkzeug (<see cref="ZoneType.None"/> = Zonen entfernen).</summary>
        public ZoneType SelectedZone { get; private set; } = ZoneType.ResidentialLow;
        public float BrushRadius { get; private set; } = 12f;

        public void SelectZone(ZoneType zone)
        {
            SelectedZone = zone;
            SetMode(Mode.Zone);
        }

        public void SetBrush(float radius) => BrushRadius = Mathf.Clamp(radius, 4f, 60f);

        void UpdateZone(SimVector2 raw)
        {
            Cursor = raw;
            CursorAnchor = RoadAnchor.Free(raw, GroundAt(raw));
            HidePreview();
            var mouse = Mouse.current;
            var city = runner.World.City;
            // Malen mit gedrückter linker, Löschen mit gedrückter rechter Maustaste
            if (mouse.leftButton.isPressed) city.Paint(raw, BrushRadius, SelectedZone);
            else if (mouse.rightButton.isPressed) city.Paint(raw, BrushRadius, ZoneType.None);
        }

        GroundView ground;

        /// <summary>Unterirdisch-Ansicht aktiv (umgeschaltet mit X oder automatisch beim Bauen unter 0 m)?</summary>
        public bool UndergroundView => ground != null && ground.Underground;
        public void ToggleUndergroundView() { if (ground != null) ground.ToggleUnderground(); }

        T Ensure<T>() where T : Component
        {
            var c = GetComponent<T>();
            return c != null ? c : gameObject.AddComponent<T>();
        }

        void OnDestroy()
        {
            if (previewMesh != null) Destroy(previewMesh);
        }

        // ------------------------------------------------------------ öffentliche Steuerung (Toolbar)

        public void SetMode(Mode mode)
        {
            CurrentMode = mode;
            if (mode != Mode.Traffic) ClearSelection();
            Cancel();
        }

        // --- UX: Auswählen-Modus als Standard und Rückkehrziel
        // Entscheidung: der vorhandene Mode.Inspect IST der Auswählen-Modus (Linksklick wählt im Inspektor) – kein neuer
        // Enum-Wert, damit Inspektor, Picking, BuildingView und die Werkzeuge anderer Agenten unverändert passen.

        /// <summary>Auswählen: keine Bauvorschau, Linksklick wählt Gebäude/Fahrzeug/Bürger/Dienst/Versorger/Depot.</summary>
        public const Mode SelectMode = Mode.Inspect;
        public bool IsSelectMode => CurrentMode == SelectMode;

        /// <summary>Zurück zum Auswählen (Esc/Rechtsklick ohne laufenden Bauvorgang, Knopf „Auswählen“).</summary>
        public void ReturnToSelect()
        {
            if (IsSelectMode) { Cancel(); return; }
            SetMode(SelectMode);
        }

        /// <summary>Werkzeugtaste: Werkzeug an – erneut gedrückt zurück zum Auswählen.</summary>
        public void ToggleMode(Mode mode) => SetMode(CurrentMode == mode ? SelectMode : mode);

        struct Operation { public System.Func<bool> Busy; public System.Action Escape, RightClick; }
        readonly List<Operation> operations = new List<Operation>();

        /// <summary>
        /// Begleit-Werkzeuge melden laufende Vorgänge (Leitung in Kette, Buslinie zeichnen, Verschieben …): Solange
        /// <paramref name="busy"/> true ist, brechen Esc bzw. Rechtsklick nur diesen Vorgang ab (<paramref name="rightClick"/>
        /// = eigene Rechtsklick-Bedeutung, sonst wie Esc); sonst geht es zurück zum Auswählen.
        /// </summary>
        public void RegisterOperation(System.Func<bool> busy, System.Action escape, System.Action rightClick = null)
        {
            if (busy != null && escape != null) operations.Add(new Operation { Busy = busy, Escape = escape, RightClick = rightClick });
        }

        /// <summary>Läuft gerade ein Bauvorgang (Straße begonnen, Leitung in Kette, …)?</summary>
        public bool IsBusy
        {
            get
            {
                if (step != Step.Start) return true;
                foreach (var op in operations) if (op.Busy()) return true;
                return false;
            }
        }

        /// <summary>In diesen Werkzeugen hat Rechtsklick eine eigene Bedeutung (Zonen löschen, Gelände umgekehrt).</summary>
        public bool RightClickHasOwnMeaning => CurrentMode == Mode.Zone || CurrentMode == Mode.Terraform;

        /// <summary>Hinweis für die Hinweiszeile (null im Auswählen-Modus).</summary>
        public string ReturnHint => IsSelectMode ? null
            : IsBusy ? "Esc/Rechtsklick: Vorgang abbrechen"
            : RightClickHasOwnMeaning ? "Esc: zurück zum Auswählen" : "Esc/Rechtsklick: zurück zum Auswählen";

        /// <summary>Esc/Rechtsklick: erst laufenden Vorgang abbrechen, dann zurück zum Auswählen (läuft nach allen Updates).</summary>
        void HandleCancelInput()
        {
            var kb = GameKeyboard.Current;
            var mouse = Mouse.current;
            bool esc = kb != null && kb.escapeKey.wasPressedThisFrame &&
                       CityBuilder.Game.UI.Panels.WindowHost.EscapeConsumedFrame != Time.frameCount; // Fenster haben Vorrang
            bool rmb = mouse != null && mouse.rightButton.wasPressedThisFrame && !UiBlocker.IsPointerOverUi(mouse.position.ReadValue());
            if (!esc && !rmb) return;
            // Speichern & Laden: Esc öffnet das Spielmenü nur, wenn hier nichts abzubrechen/zurückzusetzen ist
            if (esc && (IsBusy || !IsSelectMode || SelectedNode != null || SelectedSegment != null)) GameKeyboard.ConsumeEscape();
            foreach (var op in operations)
            {
                if (!op.Busy()) continue;
                if (rmb && !esc) (op.RightClick ?? op.Escape)();
                else op.Escape();
                return;
            }
            if (step != Step.Start) { Cancel(); return; }
            if (CurrentMode == Mode.Traffic && (SelectedNode != null || SelectedSegment != null)) { ClearSelection(); return; }
            if (IsSelectMode) return; // Auswahl aufheben macht der Inspektor
            if (rmb && !esc && RightClickHasOwnMeaning) return;
            SetMode(SelectMode);
        }

        void LateUpdate()
        {
            HandleCancelInput();
            ShowBlocker(pendingBlocker);
            pendingBlocker = null;
        }

        // Bauwerks-Sperre: blockierendes Bauwerk in der Vorschau hervorheben
        PlacementGhost blockerGhost;
        StructureBlock pendingBlocker;

        void ShowBlocker(StructureBlock b)
        {
            if (blockerGhost == null)
            {
                if (b == null) return;
                blockerGhost = new PlacementGhost(transform, "Road Blocker Highlight");
            }
            if (b == null) { blockerGhost.Hide(); return; }
            var fp = b.Footprint;
            blockerGhost.Show(fp.Center, fp.Front, fp.Width + 2f, fp.Depth + 2f, b.Height, 10f, invalidMaterial, showFront: false);
        }

        /// <summary>Fehlertext mit Namen des Bauwerks („Straße überschneidet Polizeiwache“).</summary>
        public static string ErrorText(RoadBuildError error, StructureBlock blockedBy) =>
            error == RoadBuildError.OverlapsStructure && blockedBy != null
                ? $"Straße überschneidet {blockedBy.Name} – anders führen oder Gebäude verschieben (Auswählen → Verschieben)"
                : ErrorText(error);
        // --- Ende UX

        public void SelectType(RoadType type)
        {
            if (type == null) return;
            SelectedType = type;
            if (CurrentMode == Mode.Bulldoze || CurrentMode == Mode.Traffic || CurrentMode == Mode.Template || CurrentMode == Mode.Inspect) SetMode(Mode.Straight);
            if (CurrentMode == Mode.Roundabout && type.Category != RoadCategory.City) SetMode(Mode.Straight);
        }

        public void SelectTemplate(RoadTemplateKind kind)
        {
            TemplateKind = kind;
            SetMode(Mode.Template);
        }

        public void RotateTemplate(float degrees) => TemplateRotation = Mathf.Repeat(TemplateRotation + degrees, 360f);
        public void ToggleDualCarriageway() { DualCarriageway = !DualCarriageway; Cancel(); }
        public void ToggleSnap() => SnapEnabled = !SnapEnabled;
        public void ChangeHeight(float delta) => BuildHeight = Mathf.Clamp(BuildHeight + delta, RoadBuilder.MinHeight, RoadBuilder.MaxHeight);

        public void Cancel()
        {
            step = Step.Start;
            startGuide = null;
            HidePreview();
        }

        public void ClearSelection()
        {
            SelectedNode = null;
            SelectedSegment = null;
        }

        // ------------------------------------------------------------ Update

        void Update()
        {
            var kb = GameKeyboard.Current;
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (kb != null) HandleKeys(kb);
            SnapActive = SnapEnabled && !(kb != null && kb.ctrlKey.isPressed);
            if (ground != null)
                ground.UndergroundForced = ForceUnderground || IsBuildMode &&
                    (BuildHeight < -0.1f || (step != Step.Start && startHeight - GroundAt(startPoint) < -0.1f));
            // Esc/Rechtsklick: HandleCancelInput (LateUpdate) – abbrechen bzw. zurück zum Auswählen

            // Auswahl darf nicht auf gelöschte Objekte zeigen
            var roads = runner.World.Roads;
            if (SelectedNode != null && !roads.Contains(SelectedNode)) SelectedNode = null;
            if (SelectedSegment != null && !roads.Contains(SelectedSegment)) SelectedSegment = null;

            Vector2 screen = mouse.position.ReadValue();
            bool overUi = UiBlocker.IsPointerOverUi(screen);
            HasCursor = TryGetTerrainPoint(screen, IsBuildMode ? BuildHeight : 0f, out var raw) && !overUi;
            if (!HasCursor)
            {
                if (previewRenderer != null) previewRenderer.enabled = false;
                return;
            }
            bool clicked = mouse.leftButton.wasPressedThisFrame;
            var ray = cam.ScreenPointToRay(screen);

            if (zoneView != null) zoneView.Visible = CurrentMode == Mode.Zone;

            switch (CurrentMode)
            {
                case Mode.Zone: UpdateZone(raw); break;
                case Mode.Inspect: Cursor = raw; CursorAnchor = RoadAnchor.Free(raw, GroundAt(raw)); HidePreview(); break; // Klick wählt Objekt (InspectorPanel)
                case Mode.Terraform: Cursor = raw; CursorAnchor = RoadAnchor.Free(raw, GroundAt(raw)); HidePreview(); break; // TerraformTool
                case Mode.Utility: Cursor = raw; CursorAnchor = RoadAnchor.Free(raw, GroundAt(raw)); HidePreview(); break; // G3: UtilityTool übernimmt
                case Mode.Service: Cursor = raw; CursorAnchor = RoadAnchor.Free(raw, GroundAt(raw)); HidePreview(); break; // G4: ServiceTool übernimmt
                case Mode.Transit: Cursor = raw; CursorAnchor = RoadAnchor.Free(raw, GroundAt(raw)); HidePreview(); break; // ÖPNV: TransitTool übernimmt
                case Mode.Bulldoze: UpdateBulldoze(roads, ray, raw, clicked); break;
                case Mode.Upgrade: UpdateUpgrade(roads, ray, raw, clicked); break;
                case Mode.Reverse: UpdateReverse(roads, ray, raw, clicked); break;
                case Mode.Traffic: UpdateTrafficSelection(roads, ray, raw, clicked); break;
                case Mode.Template: UpdateTemplate(roads, raw, clicked); break;
                case Mode.Roundabout: UpdateRoundabout(roads, raw, clicked); break;
                default: UpdateRoad(roads, raw, clicked); break;
            }
        }

        void HandleKeys(Keyboard kb)
        {
            // R schaltet Gerade ↔ Kurve (aus anderen Werkzeugen: Gerade); die übrigen Werkzeugtasten erneut = Auswählen
            if (kb.rKey.wasPressedThisFrame) SetMode(CurrentMode == Mode.Straight ? Mode.Curve : Mode.Straight);
            if (kb.kKey.wasPressedThisFrame) ToggleMode(Mode.Roundabout);
            if (kb.bKey.wasPressedThisFrame) ToggleMode(Mode.Bulldoze);
            if (kb.uKey.wasPressedThisFrame) ToggleMode(Mode.Upgrade);
            if (kb.iKey.wasPressedThisFrame) ToggleMode(Mode.Reverse);
            if (kb.tKey.wasPressedThisFrame) ToggleMode(Mode.Traffic);
            if (kb.hKey.wasPressedThisFrame) ToggleDualCarriageway();
            if (kb.xKey.wasPressedThisFrame) ToggleUndergroundView();
            // Zonen über das gedruckte Zeichen „Z“ der aktuellen Tastaturbelegung (Input System liest sonst die
            // US-Lage: auf QWERTZ läge „Zonen“ auf der Taste Y)
            var zoneKey = kb.FindKeyOnCurrentKeyboardLayout("z") ?? kb.zKey;
            if (zoneKey.wasPressedThisFrame) ToggleMode(Mode.Zone);
            if (kb.cKey.wasPressedThisFrame) ToggleCityInfo(); // UX: C = Stadtübersicht (im Auswählen-Modus)
            if (CurrentMode == Mode.Zone)
            {
                if (kb.leftBracketKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame) SetBrush(BrushRadius - 4f);
                if (kb.rightBracketKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) SetBrush(BrushRadius + 4f);
            }
            if (kb.tabKey.wasPressedThisFrame) CycleType(kb.shiftKey.isPressed ? -1 : 1);
            if (kb.gKey.wasPressedThisFrame) ToggleSnap();
            if (kb.f9Key.wasPressedThisFrame) CycleParallelRows();    // Bauhilfe Parallelabstand: Reihen 4 → 3 → 2 → 1 → aus
            if (kb.f10Key.wasPressedThisFrame) ToggleBlockGrid();      // Bauhilfe: Blockraster (Querstraßen-Abstände)
            if (kb.pageUpKey.wasPressedThisFrame) ChangeHeight(HeightStep);
            if (kb.pageDownKey.wasPressedThisFrame) ChangeHeight(-HeightStep);
            if (kb.commaKey.wasPressedThisFrame) RotateTemplate(-15f);
            if (kb.periodKey.wasPressedThisFrame) RotateTemplate(15f);
            // Esc: HandleCancelInput (LateUpdate, nach den Fenstern)
        }

        /// <summary>Stadtübersicht im Inspektor ein/aus (Taste C, Info-Knopf oben) – wechselt dafür ins Auswählen.</summary>
        public void ToggleCityInfo()
        {
            var inspector = GetComponent<CityBuilder.Game.Inspect.InspectorPanel>();
            if (!IsSelectMode) { SetMode(SelectMode); if (inspector != null) inspector.ShowOverview = true; return; }
            if (inspector != null) inspector.ShowOverview = !inspector.ShowOverview;
        }

        void CycleType(int dir)
        {
            var all = RoadCatalog.All;
            int i = System.Array.IndexOf(all, SelectedType);
            SelectType(all[((i + dir) % all.Length + all.Length) % all.Length]);
        }

        // ------------------------------------------------------------ Straße (gerade/Kurve, einzeln oder doppelt)

        void UpdateRoad(RoadNetwork roads, SimVector2 raw, bool clicked)
        {
            bool curve = CurrentMode == Mode.Curve;
            SnappedGuide = null;   // Bauhilfe Parallelabstand: je Frame neu
            lastGuideHit = null;

            if (step == Step.Start)
            {
                // Start: erst gleiche Ebene, dann jede Ebene (z. B. an einer Brücke weiterbauen)
                var a = RoadBuilder.ResolveAnchor(roads, raw, snapDistance, AbsHeight(raw));
                if (a.Kind == AnchorKind.Free) a = RoadBuilder.ResolveAnchor(roads, raw, snapDistance);
                if (a.Kind == AnchorKind.Free || UsesDualCarriageway) a = RoadAnchor.Free(raw, AbsHeight(raw));
                // --- Bauhilfe Parallelabstand: freier Start rastet auf die Hilfslinie ein
                bool onGuide = false;
                GuideHit startHit = default;
                if (a.Kind == AnchorKind.Free && SnapActive && ShowParallelGuides &&
                    ParallelGuides.TrySnap(roads, raw, SelectedType, ParallelRows, ParallelSnapRadius, out startHit))
                {
                    a = RoadAnchor.Free(startHit.Point, AbsHeight(startHit.Point));
                    onGuide = true;
                    SnappedGuide = startHit;
                }
                // --- Ende Bauhilfe
                CursorAnchor = a;
                Cursor = a.Position;
                HidePreview();
                ShowParallelLabel(roads, a.Position);
                if (clicked && runner.World.Contains(a.Position))
                {
                    startGuide = onGuide ? startHit : (GuideHit?)null;
                    startPoint = a.Position;
                    startHeight = a.Height;
                    // Weiter auf der Höhe des Anschlusses bauen (z. B. an einer Brücke)
                    BuildHeight = Mathf.Round((startHeight - GroundAt(startPoint)) * 10f) / 10f;
                    step = curve ? Step.Control : Step.End;
                }
                return;
            }

            var anchor = UsesDualCarriageway ? RoadAnchor.Free(raw, AbsHeight(raw)) : RoadBuilder.ResolveAnchor(roads, raw, snapDistance, AbsHeight(raw));
            SimVector2? parallelControl = null; // Bauhilfe: Kurve folgt einer gekrümmten Hilfslinie
            CursorAnchor = anchor;
            SimVector2 target = anchor.Position;
            float targetSnap = 0.5f;
            if (anchor.Kind == AnchorKind.Free)
            {
                targetSnap = 0f;
                if (SnapActive)
                {
                    bool curveEnd = curve && step == Step.End;
                    SimVector2 origin = curveEnd ? controlPoint : startPoint;
                    SimVector2 reference;
                    if (curveEnd) reference = controlPoint - startPoint;
                    else if (!RoadGeometry.TryGetReferenceDirection(roads, roads.FindNearestNode(startPoint, 0.01f, startHeight),
                                 raw - startPoint, out reference, out _))
                        reference = SimVector2.UnitY;
                    target = RoadGeometry.Snap(origin, raw, reference, AngleSnapStep, curveEnd ? 0f : LengthSnapStep);
                    if (BlockGrid && !curve) target = SnapToBlockLength(startPoint, target);           // Bauhilfe Blöcke
                    if (ShowParallelGuides) target = SnapToParallelGuides(roads, raw, target, curve, out parallelControl);
                }
            }
            Cursor = target;

            if (step == Step.Control)
            {
                var dirPlan = RoadBuilder.Plan(roads, startPoint, 0.5f, null, target, targetSnap, SelectedType, startHeight, AbsHeight(target),
                    followTerrain: AutoTrace);
                ShowPlan(roads, dirPlan, placingControl: true);
                if (clicked && SimVector2.Distance(startPoint, target) >= RoadNetwork.MinSegmentLength)
                {
                    controlPoint = target;
                    step = Step.End;
                }
                return;
            }

            SimVector2? control = curve ? parallelControl ?? controlPoint : (SimVector2?)null;

            if (UsesDualCarriageway)
            {
                var dual = RoadBuilder.PlanDualCarriageway(roads, startPoint, control, target, SelectedType, snapDistance, startHeight, AbsHeight(target),
                    followTerrain: AutoTrace);
                ShowDual(roads, dual);
                if (clicked && dual.IsValid && runner.World.Contains(target))
                {
                    RoadBuilder.BuildDualCarriageway(roads, dual, snapDistance);
                    startHeight = AbsHeight(target);
                    startPoint = target;
                    step = curve ? Step.Control : Step.End;
                }
                return;
            }

            var plan = RoadBuilder.Plan(roads, startPoint, 0.5f, control, target, targetSnap, SelectedType, startHeight, AbsHeight(target),
                followTerrain: AutoTrace);
            ShowPlan(roads, plan, placingControl: false);

            if (clicked && plan.IsValid && runner.World.Contains(plan.End))
            {
                var built = RoadBuilder.Build(roads, plan);
                if (built.Count > 0)
                {
                    var last = built[built.Count - 1];
                    startPoint = last.End.Position;
                    startHeight = last.End.Height;
                    step = curve ? Step.Control : Step.End;
                    startGuide = lastGuideHit; // Bauhilfe: Kette bleibt auf der Hilfslinie
                }
            }
        }

        void ShowPlan(RoadNetwork roads, RoadPlan plan, bool placingControl)
        {
            var g = new GuideState
            {
                Active = true,
                IsCurve = CurrentMode == Mode.Curve,
                PlacingControl = placingControl,
                Start = plan.Start,
                Control = plan.Control,
                End = plan.End,
                StartHeight = plan.StartHeight,
                EndHeight = plan.EndHeight,
                Length = plan.Length,
                Cost = plan.Cost,
                Type = plan.Type,
                Valid = placingControl || plan.IsValid,
                Error = placingControl || plan.IsValid ? null : ErrorText(plan.Error, plan.BlockedBy),
                Info = HeightInfo(roads, plan),
            };
            string parallel = ParallelInfo(roads, plan); // Bauhilfe Parallelabstand
            if (parallel != null) g.Info = parallel + " · " + g.Info;

            g.StartDirection = SafeNormalize(plan.Control - plan.Start, plan.End - plan.Start);
            var startNode = plan.StartAnchor.Kind == AnchorKind.Node ? plan.StartAnchor.Node : null;
            g.HasStartReference = RoadGeometry.TryGetReferenceDirection(roads, startNode, g.StartDirection, out g.StartReference, out g.StartAngle);
            if (!g.HasStartReference && plan.StartAnchor.Kind == AnchorKind.Segment)
                g.HasStartReference = SegmentReference(plan.StartAnchor, g.StartDirection, out g.StartReference, out g.StartAngle);
            g.Bearing = RoadGeometry.Bearing(g.StartDirection);

            if (!placingControl)
            {
                g.EndDirection = SafeNormalize(plan.Control - plan.End, plan.Start - plan.End);
                if (plan.EndAnchor.Kind == AnchorKind.Node && plan.EndAnchor.Node != startNode)
                    g.HasEndReference = RoadGeometry.TryGetReferenceDirection(roads, plan.EndAnchor.Node, g.EndDirection, out g.EndReference, out g.EndAngle);
                else if (plan.EndAnchor.Kind == AnchorKind.Segment)
                    g.HasEndReference = SegmentReference(plan.EndAnchor, g.EndDirection, out g.EndReference, out g.EndAngle);
            }
            Guide = g;
            if (!placingControl) pendingBlocker = plan.BlockedBy; // UX: Bauwerks-Sperre hervorheben

            previewCurves.Clear();
            previewCurves.Add(new RoadMeshBuilder.PreviewCurve(plan.Start, plan.Control, plan.End, plan.Type.Width, plan.StartHeight, plan.EndHeight, plan.Profile));
            ShowPreviewMesh(g.Valid);
        }

        void ShowDual(RoadNetwork roads, DualCarriagewayPlan dual)
        {
            var main = dual.Right ?? dual.Left;
            if (main == null)
            {
                Guide = new GuideState { Active = true, LabelOnly = true, Start = startPoint, End = Cursor, Type = SelectedType, Error = ErrorText(dual.Error) };
                previewRenderer.enabled = false;
                return;
            }
            ShowPlan(roads, main, placingControl: false);
            var g = Guide;
            g.Valid = dual.IsValid;
            g.Error = dual.IsValid ? null : ErrorText(dual.Error, dual.BlockedBy);
            pendingBlocker = dual.BlockedBy;
            g.Cost = dual.Cost;
            g.Info = "Doppelbau (H) · " + g.Info;
            Guide = g;

            previewCurves.Clear();
            foreach (var p in new[] { dual.Right, dual.Left })
                if (p != null) previewCurves.Add(new RoadMeshBuilder.PreviewCurve(p.Start, p.Control, p.End, p.Type.Width, p.StartHeight, p.EndHeight, p.Profile));
            ShowPreviewMesh(dual.IsValid);
        }

        /// <summary>Beschreibung der Lage (bezogen aufs Gelände): ebenerdig/Damm, Brücke, Trog, Tunnel, Steigung.</summary>
        static string HeightInfo(RoadNetwork roads, RoadPlan plan)
        {
            RoadBuilder.ElevationRange(roads.Ground, plan, out float low, out float high);
            float limit = plan.Profile != null ? CityBuilder.Simulation.Environment.TerrainMap.MaxCutFill
                                               : CityBuilder.Simulation.Environment.TerrainMap.ConformRange;
            string kind = low <= -RoadBuilder.TunnelDepth ? "Tunnel"
                : low < -limit ? "Trog"
                : high > limit ? "Brücke"
                : null;
            float e0 = plan.StartHeight - roads.Ground.HeightAt(plan.Start);
            float e1 = plan.EndHeight - roads.Ground.HeightAt(plan.End);
            string s;
            if (plan.Profile != null)
                s = kind == null ? "folgt dem Gelände (wird eingeebnet)" : $"automatisch: {kind} + Gelände eingeebnet";
            else
                s = kind == null ? (Mathf.Abs(e0) < 0.3f && Mathf.Abs(e1) < 0.3f ? "ebenerdig" : "Damm/Einschnitt")
                                 : $"{kind} · {e0:0}→{e1:0} m über Gelände";
            float dh = plan.EndHeight - plan.StartHeight;
            if (plan.Length > 1f && Mathf.Abs(dh) > 0.1f) s += $" · {(dh > 0 ? "↗" : "↘")} {Mathf.Abs(dh) / plan.Length * 100f:0.#} %";
            if (plan.GradeSeparations > 0) s += $" · {plan.GradeSeparations}× kreuzungsfrei";
            return s;
        }

        float GroundAt(SimVector2 p) => runner.World.Roads.Ground.HeightAt(p);

        /// <summary>Bauhöhe 0 = automatische Trassierung (folgt Gelände, Tunnel durch Berge, Brücken über Täler).</summary>
        public bool AutoTrace => Mathf.Abs(BuildHeight) < 0.05f;

        /// <summary>Absolute Bauhöhe an einem Punkt: Gelände + eingestellte Höhe über Gelände.</summary>
        float AbsHeight(SimVector2 p) => GroundAt(p) + BuildHeight;

        /// <summary>Absolute Höhe der Cursor-Position (für Hilfslinien).</summary>
        public float CursorHeight => HasCursor ? (IsBuildMode ? AbsHeight(Cursor) : GroundAt(Cursor)) : 0f;

        /// <summary>Anschluss mitten auf einer Straße: Referenz ist die näher liegende der beiden Fahrtrichtungen.</summary>
        static bool SegmentReference(RoadAnchor anchor, SimVector2 direction, out SimVector2 reference, out float angle)
        {
            var tangent = anchor.Segment.Tangent(anchor.T);
            float a1 = RoadGeometry.Angle(tangent, direction);
            float a2 = RoadGeometry.Angle(-tangent, direction);
            reference = a1 <= a2 ? tangent : -tangent;
            angle = Mathf.Min(a1, a2);
            return true;
        }

        // ------------------------------------------------------------ Bauhilfe Parallelabstand (ParallelGuides, RoadGuidesView)

        /// <summary>Einrastradius der Parallel-Hilfslinien (m).</summary>
        public const float ParallelSnapRadius = 6f;
        /// <summary>Bis zu dieser Abweichung (Grad) wird eine gerade Straße auf ihrer Hilfslinie exakt parallel gestellt.</summary>
        public const float ParallelSnapAngle = 8f;
        /// <summary>In diesem Umkreis um Cursor/Ende nennt die Hinweiszeile den Abstand zur Nachbarstraße (m).</summary>
        public const float ParallelInfoRadius = 24f;

        GuideHit? startGuide;
        GuideHit? lastGuideHit;

        /// <summary>Reihen, die die neue Straße neben einer bestehenden bekommen soll (1–4); 0 = Hilfslinien aus (F9).</summary>
        public int ParallelRows { get; private set; } = ZoneGrid.Depth;
        /// <summary>Blockraster (F10): Länge gerader Straßen rastet auf Querstraßen-Abstände ohne Rest ein.</summary>
        public bool BlockGrid { get; private set; }
        public bool ShowParallelGuides => ParallelRows > 0 && (CurrentMode == Mode.Straight || CurrentMode == Mode.Curve)
            && SelectedType.Category == RoadCategory.City && !UsesDualCarriageway;
        /// <summary>Hilfslinie, auf der der Start der gerade gezogenen Straße liegt.</summary>
        public GuideHit? StartGuide => step == Step.Start ? null : startGuide;
        /// <summary>In diesem Frame eingerastete Hilfslinie (Start, Kontrollpunkt oder Ende).</summary>
        public GuideHit? SnappedGuide { get; private set; }

        public void CycleParallelRows() => ParallelRows = ParallelRows == 0 ? ZoneGrid.Depth : ParallelRows - 1;
        public void ToggleBlockGrid() => BlockGrid = !BlockGrid;

        /// <summary>Nächste Länge, bei der Querstraßen gleichen Typs an beiden Enden den Block ohne Rest füllen.</summary>
        public float SnapBlockLength(float length)
        {
            float first = ParallelGuides.BlockLength(SelectedType, SelectedType, 1);
            int cells = Mathf.Max(1, Mathf.RoundToInt((length - first) / ZoneGrid.CellSize) + 1);
            return ParallelGuides.BlockLength(SelectedType, SelectedType, cells);
        }

        SimVector2 SnapToBlockLength(SimVector2 origin, SimVector2 target)
        {
            var d = target - origin;
            float len = d.Length();
            return len < 0.01f ? target : origin + d / len * SnapBlockLength(len);
        }

        SimVector2 SnapToParallelGuides(RoadNetwork roads, SimVector2 raw, SimVector2 target, bool curve, out SimVector2? parallelControl)
        {
            parallelControl = null;
            lastGuideHit = null;
            // Weiter auf derselben geraden Hilfslinie: exakt parallel (auch der Kontrollpunkt einer Kurve → tangential)
            if (startGuide.HasValue && startGuide.Value.Straight && (!curve || step == Step.Control) &&
                RoadGeometry.SnapParallel(startPoint, raw, startGuide.Value.Tangent, ParallelSnapAngle, 0f, out var along))
            {
                float len = SimVector2.Distance(startPoint, along);
                len = BlockGrid && !curve ? SnapBlockLength(len) : Mathf.Max(LengthSnapStep, Mathf.Round(len / LengthSnapStep) * LengthSnapStep);
                var p = startPoint + SimVector2.Normalize(along - startPoint) * len;
                if (ParallelGuides.TryEvaluate(startGuide.Value.Segment, p, startGuide.Value.Offset, out var h)) lastGuideHit = h;
                SnappedGuide = lastGuideHit ?? startGuide;
                return p;
            }
            if (ParallelGuides.TrySnap(roads, raw, SelectedType, ParallelRows, ParallelSnapRadius, out var hit))
            {
                lastGuideHit = hit;
                SnappedGuide = hit;
                // Kurve zwischen zwei Punkten derselben gekrümmten Hilfslinie: Kontrollpunkt folgt der Parallelen
                if (curve && step == Step.End && startGuide.HasValue && !hit.Straight &&
                    startGuide.Value.Segment == hit.Segment && startGuide.Value.Side == hit.Side)
                    parallelControl = ParallelGuides.FitParallelControl(hit.Segment, hit.Side * hit.Offset, startGuide.Value.T, hit.T);
                return hit.Point;
            }
            return target;
        }

        /// <summary>Start-Schritt: Abstand zur nächsten Stadtstraße als Hinweis (Beschriftung + HUD-Hinweiszeile).</summary>
        void ShowParallelLabel(RoadNetwork roads, SimVector2 p)
        {
            if (!ShowParallelGuides) return;
            var hits = ParallelGuides.FindGuides(roads, p, SelectedType, ParallelRows, ParallelInfoRadius);
            if (hits.Count == 0) return;
            var h = hits[0];
            var info = ParallelGuides.Evaluate(h.Segment.Type, SelectedType, h.Distance, ParallelRows);
            float height = AbsHeight(p);
            string block = BlockGrid ? " · F10 Blöcke an" : "";
            Guide = new GuideState
            {
                Active = true,
                LabelOnly = true,
                Start = p,
                End = p,
                StartHeight = height,
                EndHeight = height,
                Type = SelectedType,
                Valid = true,
                Info = $"Parallele: {ParallelGuides.Describe(info)} · Ziel {ParallelGuides.FormatMeters(h.Offset)} · " +
                       $"F9 Reihen ({ParallelRows}) · G Einrasten" + block,
            };
        }

        /// <summary>Ziehen: Abstand des Straßenendes (sonst Starts) zur nächsten parallelen Stadtstraße.</summary>
        string ParallelInfo(RoadNetwork roads, RoadPlan plan)
        {
            string block = BlockGrid ? "Blöcke (F10)" : null;
            if (!ShowParallelGuides || plan.Type.Category != RoadCategory.City) return block;
            GuideHit? best = null;
            foreach (var p in new[] { plan.End, plan.Start })
            {
                var hits = ParallelGuides.FindGuides(roads, p, plan.Type, ParallelRows, ParallelInfoRadius);
                if (hits.Count > 0) { best = hits[0]; break; }
            }
            if (best == null) return block;
            var h = best.Value;
            var info = ParallelGuides.Evaluate(h.Segment.Type, plan.Type, h.Distance, ParallelRows);
            string text = "Parallele: " + ParallelGuides.Describe(info);
            if (ParallelGuides.TryEvaluate(h.Segment, plan.Start, h.Offset, out var hs) && hs.Side == h.Side &&
                Mathf.Abs(hs.Distance - h.Distance) > 0.5f && hs.Distance < h.Offset * 1.5f)
                text += " · am Start " + ParallelGuides.FormatMeters(hs.Distance);
            if (block != null) text += " · " + block;
            return text;
        }

        // --- Ende Bauhilfe Parallelabstand

        // ------------------------------------------------------------ Kreisverkehr

        void UpdateRoundabout(RoadNetwork roads, SimVector2 raw, bool clicked)
        {
            if (step == Step.Start)
            {
                var node = roads.FindNearestNode(raw, snapDistance, AbsHeight(raw));
                Cursor = node != null ? node.Position : raw;
                CursorAnchor = node != null ? RoadAnchor.AtNode(node) : RoadAnchor.Free(raw, AbsHeight(raw));
                HidePreview();
                if (clicked && runner.World.Contains(Cursor))
                {
                    startPoint = Cursor;
                    startHeight = node != null ? node.Height : AbsHeight(raw);
                    step = Step.End;
                }
                return;
            }

            float radius = SimVector2.Distance(startPoint, raw);
            if (SnapActive) radius = Mathf.Round(radius / RadiusSnapStep) * RadiusSnapStep;
            radius = Mathf.Clamp(radius, RoadBuilder.MinRoundaboutRadius, RoadBuilder.MaxRoundaboutRadius);
            Cursor = raw;
            CursorAnchor = RoadAnchor.Free(raw);

            var type = RoundaboutType;
            currentRoundabout = RoadBuilder.PlanRoundabout(roads, startPoint, radius, type, startHeight, AutoTrace);
            Guide = new GuideState
            {
                Active = true,
                IsRoundabout = true,
                Center = startPoint,
                Radius = radius,
                Type = type,
                StartHeight = startHeight,
                EndHeight = startHeight,
                Valid = currentRoundabout.IsValid,
                Error = currentRoundabout.IsValid ? null : ErrorText(currentRoundabout.Error, currentRoundabout.BlockedBy),
                Cost = currentRoundabout.Cost,
                Length = Mathf.PI * 2f * radius,
                Start = startPoint,
                End = startPoint + new SimVector2(radius, 0f),
            };

            previewCurves.Clear();
            int arcs = RoadBuilder.RoundaboutArcCount(radius);
            float sweep = Mathf.PI * 2f / arcs;
            for (int i = 0; i < arcs; i++)
            {
                RoadBuilder.GetArc(startPoint, radius, sweep * i, sweep, out var p0, out var c, out var p2);
                previewCurves.Add(new RoadMeshBuilder.PreviewCurve(p0, c, p2, type.Width, startHeight, startHeight));
            }
            ShowPreviewMesh(currentRoundabout.IsValid);
            pendingBlocker = currentRoundabout.BlockedBy;

            if (clicked && currentRoundabout.IsValid)
            {
                RoadBuilder.BuildRoundabout(roads, currentRoundabout);
                Cancel();
            }
        }

        // ------------------------------------------------------------ Vorlagen (Autobahnknoten)

        void UpdateTemplate(RoadNetwork roads, SimVector2 raw, bool clicked)
        {
            Cursor = raw;
            CursorAnchor = RoadAnchor.Free(raw);
            var highway = SelectedType.IsHighway ? SelectedType : RoadCatalog.Highway2;
            var cross = SelectedType.Category == RoadCategory.City ? SelectedType : RoadCatalog.CityTwoLane;
            if (cross.IsOneWay) cross = RoadCatalog.CityTwoLane;

            // Planen ist teuer (viele Teilstücke) → nur neu planen, wenn sich etwas geändert hat
            bool moved = SimVector2.Distance(raw, templatePlannedAt) > 2f;
            if (currentTemplate == null || moved || templatePlannedRotation != TemplateRotation ||
                templatePlannedVersion != roads.Version || currentTemplate.Kind != TemplateKind)
            {
                currentTemplate = RoadTemplates.Plan(roads, TemplateKind, raw, TemplateRotation, highway, cross);
                templatePlannedAt = raw;
                templatePlannedRotation = TemplateRotation;
                templatePlannedVersion = roads.Version;
            }

            previewCurves.Clear();
            foreach (var p in currentTemplate.Pieces)
                previewCurves.Add(new RoadMeshBuilder.PreviewCurve(p.P0, p.Control, p.P2, p.Type.Width, p.H0, p.H2));
            ShowPreviewMesh(currentTemplate.IsValid);
            pendingBlocker = currentTemplate.BlockedBy;

            Guide = new GuideState
            {
                Active = true,
                LabelOnly = true,
                Start = raw,
                End = raw,
                Type = highway,
                Valid = currentTemplate.IsValid,
                Error = currentTemplate.IsValid ? null : ErrorText(currentTemplate.Error, currentTemplate.BlockedBy),
                Cost = currentTemplate.Cost,
                Info = $"{RoadTemplates.DisplayName(TemplateKind)} · {TemplateRotation:0}° (, und . drehen) · {currentTemplate.Cost:0} €",
            };

            if (clicked && currentTemplate.IsValid)
            {
                RoadTemplates.Build(roads, currentTemplate);
                currentTemplate = null;
            }
        }

        // ------------------------------------------------------------ Ausbauen / Richtung / Abriss

        void UpdateUpgrade(RoadNetwork roads, Ray ray, SimVector2 raw, bool clicked)
        {
            Cursor = raw;
            CursorAnchor = RoadAnchor.Free(raw);
            var segment = PickSegment(roads, ray);
            if (segment == null) { HidePreview(); return; }

            var error = RoadBuilder.CheckReplace(roads, segment, SelectedType);
            bool valid = error == RoadBuildError.None;
            ShowSegmentHighlight(segment, SelectedType.Width, valid);
            var blocker = error == RoadBuildError.OverlapsStructure ? RoadBuilder.ReplaceBlocker(roads, segment, SelectedType) : null;
            pendingBlocker = blocker;
            Guide = new GuideState
            {
                Active = true,
                LabelOnly = true,
                Start = segment.Evaluate(0.5f),
                End = segment.Evaluate(0.5f),
                StartHeight = segment.HeightAt(0.5f),
                EndHeight = segment.HeightAt(0.5f),
                Type = SelectedType,
                Valid = valid,
                Error = valid ? null : error == RoadBuildError.Duplicate ? "Ist bereits dieser Typ" : ErrorText(error, blocker),
                Info = $"Ausbau: {segment.Type.DisplayName} → {SelectedType.DisplayName} · {RoadBuilder.ReplaceCost(segment, SelectedType):0} €",
            };
            if (clicked && valid) RoadBuilder.Replace(roads, segment, SelectedType);
        }

        void UpdateReverse(RoadNetwork roads, Ray ray, SimVector2 raw, bool clicked)
        {
            Cursor = raw;
            CursorAnchor = RoadAnchor.Free(raw);
            var segment = PickSegment(roads, ray);
            if (segment == null) { HidePreview(); return; }

            string locked = RoadBuilder.LockedReason(segment); // Gateways: Fernstraße der Welt
            ShowSegmentHighlight(segment, segment.Type.Width + 1f, locked == null);
            Guide = new GuideState
            {
                Active = true,
                LabelOnly = true,
                Start = segment.Evaluate(0.5f),
                End = segment.Evaluate(0.5f),
                StartHeight = segment.HeightAt(0.5f),
                EndHeight = segment.HeightAt(0.5f),
                Type = segment.Type,
                Valid = locked == null,
                Error = locked,
                Info = segment.Type.IsOneWay ? "Fahrtrichtung umdrehen" : "Richtung tauschen (Spuren spiegeln)",
            };
            if (clicked && locked == null) RoadBuilder.Reverse(roads, segment);
        }

        void UpdateBulldoze(RoadNetwork roads, Ray ray, SimVector2 raw, bool clicked)
        {
            Cursor = raw;
            CursorAnchor = RoadAnchor.Free(raw);
            Guide = default;
            var segment = PickSegment(roads, ray);
            if (segment == null) { previewRenderer.enabled = false; return; }

            ShowSegmentHighlight(segment, segment.Type.Width + 1f, false);
            // --- Gateways: vorgebaute Autobahn/Anschlussstelle gehört der Welt → Hinweis statt Abriss (nur der Zubringer geht)
            string locked = RoadBuilder.LockedReason(segment);
            if (locked != null)
            {
                Guide = new GuideState
                {
                    Active = true, LabelOnly = true,
                    Start = segment.Evaluate(0.5f), End = segment.Evaluate(0.5f),
                    StartHeight = segment.HeightAt(0.5f), EndHeight = segment.HeightAt(0.5f),
                    Type = segment.Type, Valid = false, Error = locked,
                };
                return;
            }
            // --- Ende Gateways
            if (clicked)
            {
                RoadBuilder.Demolish(roads, segment); // Stadtkasse: Abrisserlös
                previewRenderer.enabled = false;
            }
        }

        void UpdateTrafficSelection(RoadNetwork roads, Ray ray, SimVector2 raw, bool clicked)
        {
            Cursor = raw;
            HidePreview();
            var node = PickNode(roads, ray, junctionsOnly: true);
            var segment = node == null ? PickSegment(roads, ray) : null;
            CursorAnchor = node != null ? RoadAnchor.AtNode(node) : RoadAnchor.Free(raw);
            if (segment != null) ShowSegmentHighlight(segment, segment.Type.Width + 1f, true);

            if (clicked)
            {
                SelectedNode = node;
                SelectedSegment = segment;
            }
        }

        void ShowSegmentHighlight(RoadSegment s, float width, bool valid)
        {
            previewCurves.Clear();
            previewCurves.Add(new RoadMeshBuilder.PreviewCurve(s.Start.Position, s.Control, s.End.Position, width, s.Start.Height, s.End.Height));
            ShowPreviewMesh(valid);
        }

        // ------------------------------------------------------------ Auswahl im Raum (auch Brücken)

        /// <summary>Straße, deren Mittellinie dem Mausstrahl am nächsten liegt (3D, damit Brücken treffbar sind).</summary>
        public static RoadSegment PickSegment(RoadNetwork roads, Ray ray)
        {
            RoadSegment best = null;
            float bestScore = float.MaxValue;
            foreach (var s in roads.Segments)
            {
                const int n = 16;
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    var p = SimConvert.ToWorld(s.Evaluate(t), s.HeightAt(t));
                    float d = DistanceToRay(ray, p);
                    float limit = s.Type.HalfWidth + 2f + s.Length / n * 0.5f;
                    if (d < limit && d / limit < bestScore)
                    {
                        // genauer prüfen
                        float tt = s.ClosestT(SimConvert.ToSim(ClosestOnRay(ray, p)));
                        var q = SimConvert.ToWorld(s.Evaluate(tt), s.HeightAt(tt));
                        float dq = DistanceToRay(ray, q);
                        if (dq < s.Type.HalfWidth + 2f && dq / (s.Type.HalfWidth + 2f) < bestScore)
                        {
                            bestScore = dq / (s.Type.HalfWidth + 2f);
                            best = s;
                        }
                    }
                }
            }
            return best;
        }

        public static RoadNode PickNode(RoadNetwork roads, Ray ray, bool junctionsOnly)
        {
            RoadNode best = null;
            float bestDist = float.MaxValue;
            foreach (var n in roads.Nodes)
            {
                if (junctionsOnly && n.SegmentIds.Count < 3) continue;
                float r = 4f;
                foreach (int id in n.SegmentIds) r = Mathf.Max(r, roads.GetSegment(id).Type.HalfWidth + 2f);
                float d = DistanceToRay(ray, SimConvert.ToWorld(n.Position, n.Height));
                if (d < r && d < bestDist) { bestDist = d; best = n; }
            }
            return best;
        }

        static float DistanceToRay(Ray ray, Vector3 p) => Vector3.Cross(ray.direction, p - ray.origin).magnitude;
        static Vector3 ClosestOnRay(Ray ray, Vector3 p) => ray.origin + ray.direction * Mathf.Max(0f, Vector3.Dot(p - ray.origin, ray.direction));

        // ------------------------------------------------------------ Hilfen

        void ShowPreviewMesh(bool valid)
        {
            RoadMeshBuilder.BuildRibbons(previewCurves, previewHeight, previewMesh);
            previewRenderer.sharedMaterial = valid ? previewMaterial : invalidMaterial;
            previewRenderer.enabled = true;
        }

        void HidePreview()
        {
            if (previewRenderer != null) previewRenderer.enabled = false;
            Guide = default;
        }

        public static string ErrorText(RoadBuildError error)
        {
            switch (error)
            {
                case RoadBuildError.TooShort: return "Zu kurz";
                case RoadBuildError.SameNode: return "Start und Ende sind derselbe Punkt";
                case RoadBuildError.Duplicate: return "Diese Straße gibt es schon";
                case RoadBuildError.AngleTooSharp: return $"Winkel zu spitz (min. {RoadBuilder.MinJunctionAngle:0}°)";
                case RoadBuildError.HighwayCrossing: return "Autobahn kreuzt nicht ebenerdig – Bauhöhe ändern (Bild↑/↓)";
                case RoadBuildError.IncompatibleConnection: return "Autobahn nur an Autobahn oder Auf-/Abfahrt anschließen";
                case RoadBuildError.TooCloseToCrossing: return "Zu dicht an einer anderen Kreuzung";
                case RoadBuildError.InvalidRadius: return $"Radius {RoadBuilder.MinRoundaboutRadius:0}–{RoadBuilder.MaxRoundaboutRadius:0} m";
                case RoadBuildError.InvalidType: return "Für diesen Straßentyp nicht möglich";
                case RoadBuildError.TooSteep: return "Zu steil – länger bauen oder Höhe anpassen";
                case RoadBuildError.InsufficientClearance: return $"Zu wenig Durchfahrtshöhe (min. {RoadBuilder.Clearance:0.#} m)";
                case RoadBuildError.InvalidHeight: return $"Höhe {RoadBuilder.MinHeight:0} bis {RoadBuilder.MaxHeight:0} m über Gelände";
                case RoadBuildError.OverWater: return "Über Wasser nur als Brücke – Bauhöhe erhöhen (Bild↑)";
                case RoadBuildError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                case RoadBuildError.OverlapsStructure: return "Straße überschneidet ein Bauwerk – anders führen oder Gebäude verschieben";
                case RoadBuildError.Locked: return "Gehört zum Fernstraßennetz – nicht abreißbar/umbaubar (am Zubringer anschließen)"; // Gateways
                default: return null;
            }
        }

        static SimVector2 SafeNormalize(SimVector2 v, SimVector2 fallback)
        {
            if (v.LengthSquared() > 1e-6f) return SimVector2.Normalize(v);
            return fallback.LengthSquared() > 1e-6f ? SimVector2.Normalize(fallback) : SimVector2.UnitY;
        }

        /// <summary>
        /// Schnitt des Mausstrahls mit der Geländeoberfläche, um <paramref name="elevation"/> angehoben
        /// (Bauhöhe). Schrittweise entlang des Strahls, dann per Bisektion verfeinert.
        /// </summary>
        bool TryGetTerrainPoint(Vector2 screenPos, float elevation, out SimVector2 point)
        {
            point = default;
            if (cam == null) return false;
            var ray = cam.ScreenPointToRay(screenPos);
            var ground = runner.World.Roads.Ground;
            float Above(float d)
            {
                var p = ray.GetPoint(d);
                return p.y - (ground.HeightAt(SimConvert.ToSim(p)) + elevation);
            }

            const float step = 6f, maxDistance = 8000f;
            float prev = 0f;
            if (Above(prev) < 0f) return false;
            for (float d = step; d < maxDistance; d += step)
            {
                if (Above(d) > 0f) { prev = d; continue; }
                float lo = prev, hi = d;
                for (int i = 0; i < 20; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    if (Above(mid) > 0f) lo = mid; else hi = mid;
                }
                point = SimConvert.ToSim(ray.GetPoint(hi));
                return true;
            }
            return false;
        }
    }
}
