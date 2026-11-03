using System.Collections.Generic;
using CityBuilder.Game.Assets;
using CityBuilder.Game.Roads;
using CityBuilder.Game.Services;
using CityBuilder.Game.UI;
using CityBuilder.Simulation.LandValue;
using CityBuilder.Simulation.Zoning;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Zoning
{
    /// <summary>
    /// Gebäude setzen (Anno-Stil, <b>Taste 6</b>, Modus <see cref="RoadTool.Mode.House"/>; seit „Setzen statt Zonen“ auch Laden, Mischgebiet,
    /// Büro, Hallen): Bauart wählen, Vorschau mit
    /// dem echten Zonen-Modell (grün/rot) rastet mit der Vorderseite an der Straße ein, <c>,</c>/<c>.</c> drehen, Klick baut.
    /// Am Mauszeiger rechnet die Simulation aus, welche Anforderungen dort erfüllt wären
    /// (<see cref="LandValueSystem.PreviewHouse"/>) – ✓/✗ im Haus-Fenster und in der Beschriftung, dazu die Reichweite des
    /// gewählten bzw. ersten fehlenden Dienstes über die Abdeckungs-Ansicht (Laden: Ring der Fußweite).
    /// <b>Reihe ziehen:</b> LMB halten und entlang der Straße ziehen → Vorschau einer Reihe (gültig grün, ungültig rot und
    /// ausgelassen, <see cref="CitySystem.PlanHouseRow"/>), Loslassen baut alle gültigen mit einer Buchung; RMB/Esc bricht ab.
    /// Abriss: gesetzte Gebäude anklicken (25 % zurück). Esc/Rechtsklick → Auswählen (wie alle Werkzeuge).
    /// Taste 6: frei (Buchstaben sind belegt, 4 = Dienste, 5 = ÖPNV → 6 = Wohnhäuser in derselben Reihe).
    /// </summary>
    public sealed class HouseTool : MonoBehaviour
    {
        public enum HouseAction { Place, Remove }

        RoadTool tool;
        Material okMaterial, badMaterial;
        PlacementGhost ghost;
        LineRenderer ring;
        string label;
        bool labelError;
        Vector3 labelWorld;

        // Vorschau-Cache (Plan + Anforderungen nur neu, wenn sich Lage, Typ oder Stadt geändert haben)
        SimVector2 cachedPos = new SimVector2(float.NaN);
        float cachedRotation = float.NaN;
        HouseType cachedType;
        int cachedRoads = -1, cachedCity = -1, cachedServices = -1, cachedCoverage = -1;
        float cachedAt = -1f;

        // Vorschau-Modelle: je (Bauart, Modell) eine skalierte, inaktive Vorlage für PlacementGhost.ShowModel
        readonly Dictionary<(HouseType, ModelEntry), GameObject> templates = new Dictionary<(HouseType, ModelEntry), GameObject>();
        Transform templateRoot;

        public HouseAction Current { get; private set; } = HouseAction.Place;
        public HouseType SelectedType { get; private set; } = HouseCatalog.Low;
        public float Rotation { get; private set; }
        public bool Active => tool != null && tool.CurrentMode == RoadTool.Mode.House;

        /// <summary>Aktueller Plan am Mauszeiger (null ohne Cursor/außerhalb des Werkzeugs).</summary>
        public HousePlan Plan { get; private set; }
        /// <summary>Anforderungen am Mauszeiger (✓/✗ je Bedürfnis, erreichbare Stufe).</summary>
        public HousePreview Preview { get; private set; }
        /// <summary>Vom Spieler im Fenster gewählte Anforderung, deren Reichweite gezeigt wird (null = erste fehlende).</summary>
        public HouseNeed FocusNeed { get; set; }
        /// <summary>Tatsächlich gezeigte Anforderung (gewählt oder erste fehlende Einrichtung).</summary>
        public HouseNeed ShownNeed { get; private set; }
        /// <summary>Text für die Hinweiszeile der HUD (Kosten, Fehler, was fehlt).</summary>
        public string HintText { get; private set; }
        public bool HintError { get; private set; }
        /// <summary>Haus unter dem Mauszeiger im Abriss-Modus.</summary>
        public Building RemoveTarget { get; private set; }

        // --- Setzen statt Zonen: Reihe ziehen (LMB halten). Einzelklick (ohne Ziehen) = ein Gebäude wie bisher.
        bool dragging, dragMoved;
        SimVector2 dragStart;
        SimVector2 rowCachedEnd = new SimVector2(float.NaN);
        int rowCachedRoads = -1, rowCachedCity = -1;
        HouseType rowCachedType;
        readonly List<PlacementGhost> rowGhosts = new List<PlacementGhost>();

        /// <summary>Gezogene Reihe (null = keine; Vorschau während LMB gehalten wird).</summary>
        public HouseRowPlan Row { get; private set; }
        /// <summary>Wird gerade eine Reihe gezogen?</summary>
        public bool DraggingRow => dragging && dragMoved;

        void CancelRow()
        {
            dragging = dragMoved = false;
            Row = null;
            HideRow();
        }
        // --- Ende Setzen statt Zonen

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            if (tool.Runner != null) tool.Runner.WorldReplaced += () => { cachedRoads = cachedCity = -1; Plan = null; Preview = null; CancelRow(); };
            // Reihe ziehen: Esc/Rechtsklick brechen nur die Reihe ab (wie Leitungskette, Buslinie)
            tool.RegisterOperation(() => dragging, CancelRow);
            okMaterial = new Material(transparent) { name = "HousePreviewOk", color = new Color(0.3f, 0.95f, 0.45f, 0.45f) };
            badMaterial = new Material(transparent) { name = "HousePreviewBad", color = new Color(1f, 0.25f, 0.2f, 0.5f) };
        }

        void Start()
        {
            ghost = new PlacementGhost(transform, "House Preview");
            templateRoot = new GameObject("House Preview Templates").transform;
            templateRoot.SetParent(transform, false);
            templateRoot.gameObject.SetActive(false);
            var ringGo = new GameObject("House Need Ring");
            ringGo.transform.SetParent(transform, false);
            ring = ringGo.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.widthMultiplier = 1.4f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.enabled = false;
        }

        void OnDestroy()
        {
            if (okMaterial != null) Destroy(okMaterial);
            if (badMaterial != null) Destroy(badMaterial);
        }

        // ------------------------------------------------------------ Steuerung (Bauleiste, Fenster)

        public void Select(HouseAction action)
        {
            Current = action;
            if (!Active) tool.SetMode(RoadTool.Mode.House);
        }

        public void SelectType(HouseType type)
        {
            if (type == null) return;
            SelectedType = type;
            CancelRow();
            tool.SetParallelDepth(type.Depth); // Straßen-Abstandshilfe rechnet mit der Tiefe der gewählten Bauart
            Select(HouseAction.Place);
        }

        public void Rotate(float degrees) => Rotation = Mathf.Repeat(Rotation + degrees, 360f);

        void Update()
        {
            if (tool == null) return;
            var kb = GameKeyboard.Current;
            if (kb == null) return;
            if (kb.digit6Key.wasPressedThisFrame && !kb.shiftKey.isPressed) tool.ToggleMode(RoadTool.Mode.House); // erneut 6 = Auswählen
            if (!Active) return;
            if (kb.commaKey.wasPressedThisFrame) Rotate(-15f);
            if (kb.periodKey.wasPressedThisFrame) Rotate(15f);
        }

        void LateUpdate()
        {
            label = null;
            HintText = null;
            HintError = false;
            RemoveTarget = null;
            if (tool == null) return;
            var overlay = GetComponent<ServiceOverlayView>();
            if (!Active || tool.Runner == null || tool.Runner.World == null)
            {
                Hide();
                if (dragging) CancelRow();
                ShownNeed = null;
                return;
            }
            var mouse = Mouse.current;
            if (!tool.HasCursor)
            {
                Hide();
                HideRow();
                if (dragging && mouse != null && mouse.leftButton.wasReleasedThisFrame) CancelRow(); // über der Leiste losgelassen
                if (overlay != null) overlay.ToolKind = ShownKind();
                return;
            }
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (Current == HouseAction.Place) UpdatePlace(tool.Cursor, mouse);
            else { if (dragging) CancelRow(); UpdateRemove(tool.Cursor, clicked); }
            if (overlay != null) overlay.ToolKind = Current == HouseAction.Place ? ShownKind() : null;
        }

        string ShownKind() => ShownNeed != null && ShownNeed.Kind == HouseNeedKind.Service && ShownNeed.ServiceKinds.Length > 0
            ? ShownNeed.ServiceKinds[0] : null;

        void Hide()
        {
            ghost?.Hide();
            if (ring != null) ring.enabled = false;
        }

        void UpdatePlace(SimVector2 p, Mouse mouse)
        {
            var w = tool.Runner.World;
            bool pressed = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool held = mouse != null && mouse.leftButton.isPressed;
            bool released = mouse != null && mouse.leftButton.wasReleasedThisFrame;

            // --- Setzen statt Zonen: LMB drücken merkt den Start; ziehen (> halbe Breite, mind. 4 m) = Reihe
            if (pressed) { dragging = true; dragMoved = false; dragStart = p; Row = null; }
            if (dragging && held && !dragMoved && SimVector2.Distance(p, dragStart) > Mathf.Max(4f, SelectedType.Width * 0.5f)) dragMoved = true;
            if (dragging && dragMoved)
            {
                UpdateRow(w, p, released);
                return;
            }
            HideRow();
            bool clicked = dragging && released; // Einzelklick: beim Loslassen ohne Ziehen
            if (released) dragging = false;
            // --- Ende Setzen statt Zonen
            bool changed = SimVector2.DistanceSquared(p, cachedPos) > 0.25f || !Mathf.Approximately(Rotation, cachedRotation) ||
                           cachedType != SelectedType || cachedRoads != w.Roads.Version || cachedCity != w.City.Version ||
                           cachedServices != w.Services.StructureVersion || cachedCoverage != w.Services.CoverageVersion ||
                           Time.unscaledTime - cachedAt > 1f; // Versorgung/Läden ändern sich ohne eigene Version
            if (changed || Plan == null)
            {
                cachedPos = p;
                cachedRotation = Rotation;
                cachedType = SelectedType;
                cachedRoads = w.Roads.Version;
                cachedCity = w.City.Version;
                cachedServices = w.Services.StructureVersion;
                cachedCoverage = w.Services.CoverageVersion;
                cachedAt = Time.unscaledTime;
                Plan = w.City.PlanHouse(SelectedType, p, Rotation);
                Preview = w.LandValue != null ? w.LandValue.PreviewHouse(Plan) : null;
            }
            var plan = Plan;
            var t = SelectedType;
            ShownNeed = FocusNeed ?? FirstMissingFacility();

            var model = ModelFor(t);
            if (model != null) ghost.ShowModel(model, plan.Position, plan.Front, t.Width, t.Depth, plan.Height, 8f, plan.IsValid ? okMaterial : badMaterial, true);
            else ghost.Show(plan.Position, plan.Front, t.Width, t.Depth, plan.Height, PlaceholderTall(t), plan.IsValid ? okMaterial : badMaterial, true);
            DrawShopRing(w, plan);

            string reach = Preview != null ? $"erreichbare Stufe {Preview.ReachableLevel} von {Preview.MaxLevel}" : null;
            string turn = plan.Aligned ? (Rotation != 0f ? $"zur Straße + {Rotation:0}°" : "zur Straße ausgerichtet") : $"{Rotation:0}°";
            if (plan.IsValid)
            {
                string missing = Preview != null ? Preview.MissingText : "";
                HintText = $"{t.DisplayName} · {Capacity(t)} · Kosten {CityBuilder.Game.UI.Hud.BuildLabels.Money(t.Cost)} · {turn}" +
                           (reach != null ? " · " + reach : "") + (missing.Length > 0 ? " · " + missing : "") + " · ziehen = Reihe";
                label = $"{t.DisplayName} · {t.Cost:0} € · {reach}\n{NeedLine()}";
            }
            else
            {
                HintText = ErrorText(plan);
                HintError = true;
                label = HintText;
            }
            labelError = !plan.IsValid;
            labelWorld = SimConvert.ToWorld(plan.Position, plan.Height + PlaceholderTall(t) + 6f);

            if (clicked && plan.IsValid)
            {
                w.City.BuildHouse(plan);
                Plan = null; // neu planen (Platz ist jetzt belegt)
            }
        }

        /// <summary>„3–5 Wohnungen“ bzw. „12–22 Jobs“ bzw. beides (Mischgebiet).</summary>
        static string Capacity(HouseType t)
        {
            int max = HouseRules.MaxLevelFor(t.Zone);
            string homes = t.HasHomes ? $"{t.HouseholdsAtLevel(1)}–{t.HouseholdsAtLevel(max)} Wohnungen" : null;
            string jobs = t.JobsAtLevel(1) > 0 ? $"{t.JobsAtLevel(1)}–{t.JobsAtLevel(max)} Jobs" : null;
            return homes != null && jobs != null ? homes + ", " + jobs : homes ?? jobs ?? "";
        }

        // ------------------------------------------------------------ Reihe ziehen (Setzen statt Zonen)

        void UpdateRow(CityBuilder.Simulation.Core.SimulationWorld w, SimVector2 p, bool released)
        {
            Hide(); // Einzel-Vorschau aus
            var t = SelectedType;
            if (Row == null || SimVector2.DistanceSquared(p, rowCachedEnd) > 0.25f || rowCachedType != t ||
                rowCachedRoads != w.Roads.Version || rowCachedCity != w.City.Version)
            {
                rowCachedEnd = p;
                rowCachedType = t;
                rowCachedRoads = w.Roads.Version;
                rowCachedCity = w.City.Version;
                Row = w.City.PlanHouseRow(t, dragStart, p);
            }
            var row = Row;
            var model = ModelFor(t);
            for (int i = 0; i < row.Items.Count; i++)
            {
                while (rowGhosts.Count <= i) rowGhosts.Add(new PlacementGhost(transform, $"House Row Preview {rowGhosts.Count}"));
                var it = row.Items[i];
                var mat = it.IsValid ? okMaterial : badMaterial;
                if (model != null) rowGhosts[i].ShowModel(model, it.Position, it.Front, t.Width, t.Depth, it.Height, 8f, mat, i == 0);
                else rowGhosts[i].Show(it.Position, it.Front, t.Width, t.Depth, it.Height, PlaceholderTall(t), mat, i == 0);
            }
            for (int i = row.Items.Count; i < rowGhosts.Count; i++) rowGhosts[i].Hide();

            int bad = row.Items.Count - row.ValidCount;
            string firstError = null;
            foreach (var it in row.Items) if (!it.IsValid) { firstError = ErrorText(it); break; }
            HintText = row.ValidCount > 0
                ? $"Reihe: {row.ValidCount} × {t.DisplayName} · Kosten {CityBuilder.Game.UI.Hud.BuildLabels.Money(row.TotalCost)}" +
                  (bad > 0 ? $" · {bad} ausgelassen (rot: {firstError})" : "") + " · loslassen baut · RMB/Esc bricht ab"
                : $"Reihe: kein Platz frei · {firstError ?? "an eine Stadtstraße ziehen"}";
            HintError = row.ValidCount == 0;
            var last = row.Items[row.Items.Count - 1];
            label = row.ValidCount > 0 ? $"{row.ValidCount} × {t.DisplayName} · {row.TotalCost:0} €" : firstError;
            labelError = row.ValidCount == 0;
            labelWorld = SimConvert.ToWorld(last.Position, last.Height + PlaceholderTall(t) + 6f);
            ShownNeed = null;
            if (ring != null) ring.enabled = false;

            if (released)
            {
                if (row.ValidCount > 0) w.City.BuildHouseRow(row);
                CancelRow();
                Plan = null;
            }
        }

        void HideRow()
        {
            foreach (var g in rowGhosts) g.Hide();
        }

        /// <summary>Kurzzeile ✓/✗ je Anforderung für die Welt-Beschriftung.</summary>
        string NeedLine()
        {
            if (Preview == null) return "";
            var parts = new List<string>(Preview.Needs.Count);
            foreach (var s in Preview.Needs) parts.Add($"{s.Mark} {s.Need.ShortName}");
            return string.Join("  ", parts);
        }

        HouseNeed FirstMissingFacility()
        {
            if (Preview == null) return null;
            foreach (var s in Preview.Needs)
                if (!s.Met && (s.Need.Kind == HouseNeedKind.Service || s.Need.Kind == HouseNeedKind.Shop || s.Need.Kind == HouseNeedKind.Customers)) return s.Need;
            return null;
        }

        void DrawShopRing(CityBuilder.Simulation.Core.SimulationWorld w, HousePlan plan)
        {
            if (ring == null) return;
            bool customers = ShownNeed != null && ShownNeed.Kind == HouseNeedKind.Customers; // Setzen statt Zonen: Einzugsgebiet
            if (ShownNeed == null || (ShownNeed.Kind != HouseNeedKind.Shop && !customers)) { ring.enabled = false; return; }
            float radius = customers ? HouseRules.CustomerRadius : HouseRules.ShopWalkRadius;
            ring.enabled = true;
            bool met = false;
            if (Preview != null) foreach (var s in Preview.Needs) if (s.Need == ShownNeed) met = s.Met;
            ring.sharedMaterial = met ? okMaterial : badMaterial;
            const int n = 96;
            ring.positionCount = n;
            var c = plan.FrontMid;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var q = c + new SimVector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                ring.SetPosition(i, SimConvert.ToWorld(q, w.Terrain.HeightAt(q) + 1.5f));
            }
        }

        void UpdateRemove(SimVector2 p, bool clicked)
        {
            if (ring != null) ring.enabled = false;
            ShownNeed = null;
            var city = tool.Runner.World.City;
            var b = city.BuildingAt(p);
            var type = HouseCatalog.Of(b);
            if (type == null)
            {
                ghost.Hide();
                HintText = b != null ? "Nur vom Spieler gesetzte Gebäude – Zonengebäude alter Stände verschwinden über Entzonen" : "Gesetztes Gebäude anklicken";
                HintError = b != null;
                return;
            }
            RemoveTarget = b;
            ghost.Show(b.Position, b.Front, b.Width + 1f, b.Depth + 1f, b.Height, PlaceholderTall(type) + 2f, badMaterial, false);
            float refund = (float)(type.Cost * CityBuilder.Simulation.Economy.EconomyRules.DemolitionRefundShare);
            HintText = $"{type.DisplayName} abreißen (+{CityBuilder.Game.UI.Hud.BuildLabels.Money(refund)})" + (b.Residents > 0 ? $" · {b.Residents} Bewohner ziehen um oder weg" : b.Workers > 0 ? $" · {b.Workers} Beschäftigte verlieren den Job" : "");
            label = $"{type.DisplayName} abreißen (+{refund:0} €)";
            labelError = true;
            labelWorld = SimConvert.ToWorld(b.Position, b.Height + PlaceholderTall(type) + 6f);
            if (clicked) city.DemolishHouse(b.Id);
        }

        public static string ErrorText(HousePlan plan)
        {
            switch (plan.Error)
            {
                case HouseBuildError.OutOfWorld: return "Außerhalb der Karte";
                case HouseBuildError.OnWater: return "Nicht im Wasser bauen";
                case HouseBuildError.TooSteep: return $"Zu steil (max. {CitySystem.HouseMaxSlope:0} m Höhenunterschied)";
                case HouseBuildError.OverlapsRoad: return "Steht auf einer Straße";
                case HouseBuildError.OverlapsStructure: return $"{plan.BlockerName ?? "Bauwerk"} im Weg";
                case HouseBuildError.OverlapsBuilding:
                    return $"{plan.BlockerName ?? "Gebäude"} im Weg – erst abreißen (Zonengebäude: Entzonen)";
                case HouseBuildError.NoRoadAccess:
                    return $"Keine Zufahrt an der Vorderseite (Pfeil) – an eine Stadtstraße setzen, höchstens {CitySystem.HouseRoadReach:0} m";
                case HouseBuildError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                default: return null;
            }
        }

        // ------------------------------------------------------------ Modell

        static float PlaceholderTall(HouseType t)
        {
            switch (t.Zone)
            {
                case ZoneType.ResidentialHigh: return 40f;
                case ZoneType.ResidentialMedium: return 14f;
                case ZoneType.Mixed: return 16f;
                case ZoneType.Office: return 28f;
                case ZoneType.Industry: return 10f;
                default: return 6f;
            }
        }

        /// <summary>Stufe-1-Modell der Zone (wie <see cref="BuildingView"/> es wählt), aufs Grundstück skaliert – null = Quader.</summary>
        GameObject ModelFor(HouseType t)
        {
            var catalog = AssetCatalog.Instance;
            if (catalog == null || templateRoot == null) return null;
            var entry = BuildingView.PickForLevel(catalog.Buildings(t.Zone), 0, 1);
            if (entry == null || entry.Mesh == null) return null;
            if (templates.TryGetValue((t, entry), out var go) && go != null) return go;

            // wie BuildingView: ans Grundstück anpassen, um 0° oder 90° gedreht, je nachdem was besser passt
            float wdt = t.Width * 0.92f, dpt = t.Depth * 0.92f;
            float s0 = Mathf.Min(wdt / entry.Size.x, dpt / entry.Size.z);
            float s90 = Mathf.Min(wdt / entry.Size.z, dpt / entry.Size.x);
            bool turn = s90 > s0 * 1.1f;
            float s = Mathf.Min(turn ? s90 : s0, 1.6f);
            go = new GameObject($"{t.Id} {entry.Name}");
            go.transform.SetParent(templateRoot, false);
            var child = new GameObject("Mesh");
            child.transform.SetParent(go.transform, false);
            child.transform.localRotation = turn ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
            child.transform.localScale = Vector3.one * s;
            child.AddComponent<MeshFilter>().sharedMesh = entry.Mesh;
            child.AddComponent<MeshRenderer>().sharedMaterials = entry.Materials;
            templates[(t, entry)] = go;
            return go;
        }

        // ------------------------------------------------------------ Beschriftung

        void OnGUI()
        {
            if (!CityBuilder.Game.UI.Panels.WorldLabels.Enabled) return; // IMGUI-Rückfall: nur Vorschau + Hinweis
            if (Event.current.type != EventType.Layout) return;
            CityBuilder.Game.UI.Panels.WorldLabels.Begin(this);
            if (Active && !string.IsNullOrEmpty(label))
                CityBuilder.Game.UI.Panels.WorldLabels.Add(this, labelWorld, label,
                    labelError ? CityBuilder.Game.UI.Panels.WorldLabelStyle.Error : CityBuilder.Game.UI.Panels.WorldLabelStyle.Normal, -6f, above: true);
        }
    }
}
