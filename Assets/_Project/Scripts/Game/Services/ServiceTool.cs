using System.Collections.Generic;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Services;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Services
{
    /// <summary>
    /// Dienste-Werkzeug (Taste 4): Polizei, Feuerwehr, Gesundheit, Schulen, Bibliothek, Parks, Müll platzieren
    /// (, und . drehen), Abriss. Vorschau zeigt die <b>Reichweite</b>: erreichte Straßen werden nach Fahrzeit eingefärbt
    /// (grün = volle Wirkung → rot = Rand der Reichweite), bei Parks ein Ring um den Fußweg-Radius.
    /// Aktiv im Modus <see cref="RoadTool.Mode.Service"/> – das RoadTool liefert den Cursor, die Simulation
    /// (<see cref="ServiceSystem"/>) prüft und baut.
    /// <para>Taste 4 statt eines Buchstabens: frei war nur Y, das das Input System nach US-Lage liest (auf QWERTZ = Taste „Z“).</para>
    /// </summary>
    public sealed class ServiceTool : MonoBehaviour
    {
        /// <summary>Eigene Assets: <see cref="NoiseBarrier"/> = Lärmschutzwand als Kette ziehen (Linienbauwerk).</summary>
        public enum ServiceAction { Place, Remove, NoiseBarrier }

        const int Buckets = 6;
        const float PieceLength = 6f;
        static readonly Color Selected = new Color(0.45f, 0.75f, 1f);

        RoadTool tool;
        Material okMaterial, badMaterial;
        readonly Material[] reachMaterials = new Material[Buckets];
        readonly List<Matrix4x4>[] reachPieces = new List<Matrix4x4>[Buckets];
        readonly Matrix4x4[] buffer = new Matrix4x4[1023];
        Mesh cube;
        GameObject ghost;
        MeshRenderer ghostRenderer;
        LineRenderer ring;
        GUIStyle infoStyle, labelStyle;

        string label;
        bool labelError;
        Vector3 labelWorld;

        // Vorschau-Cache: Reichweite nur neu rechnen, wenn sich Lage, Typ oder Netz geändert haben
        SimVector2 cachedPos = new SimVector2(float.NaN);
        float cachedRotation = float.NaN;
        ServiceType cachedType;
        int cachedRoadVersion = -1, cachedStructureVersion = -1;
        ServicePlan cachedPlan;

        public ServiceAction Current { get; private set; } = ServiceAction.Place;
        public ServiceType SelectedType { get; private set; } = ServiceCatalog.PoliceStation;
        public float Rotation { get; private set; }
        public bool Active => tool != null && tool.CurrentMode == RoadTool.Mode.Service;

        ServiceSystem Services => tool.Runner.World.Services;

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            if (tool.Runner != null) tool.Runner.WorldReplaced += () => cachedRoadVersion = cachedStructureVersion = -1; // Speichern & Laden
            // Eigene Assets: laufende Wandkette – Esc/Rechtsklick beendet nur die Kette
            tool.RegisterOperation(() => Active && Current == ServiceAction.NoiseBarrier && barrierChain, () => barrierChain = false, () => barrierChain = false);
            okMaterial = new Material(transparent) { name = "ServicePreviewOk", color = new Color(0.3f, 0.9f, 1f, 0.45f) };
            badMaterial = new Material(transparent) { name = "ServicePreviewBad", color = new Color(1f, 0.25f, 0.2f, 0.5f) };
            for (int i = 0; i < Buckets; i++)
            {
                var c = ServiceOverlayView.CoverageColor(1f - i / (float)(Buckets - 1));
                c.a = 0.75f;
                var m = new Material(transparent) { name = "ServiceReach" + i, enableInstancing = true };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
                reachMaterials[i] = m;
                reachPieces[i] = new List<Matrix4x4>();
            }
        }

        void Start()
        {
            ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghost.name = "Service Preview";
            Destroy(ghost.GetComponent<Collider>());
            ghost.transform.SetParent(transform, false);
            ghostRenderer = ghost.GetComponent<MeshRenderer>();
            ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghost.SetActive(false);
            cube = ghost.GetComponent<MeshFilter>().sharedMesh;

            var ringGo = new GameObject("Service Range Ring");
            ringGo.transform.SetParent(transform, false);
            ring = ringGo.AddComponent<LineRenderer>();
            ring.useWorldSpace = true;
            ring.loop = true;
            ring.widthMultiplier = 1.2f;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ring.enabled = false;
        }

        void OnDestroy()
        {
            if (okMaterial != null) Destroy(okMaterial);
            if (badMaterial != null) Destroy(badMaterial);
            foreach (var m in reachMaterials) if (m != null) Destroy(m);
        }

        // ------------------------------------------------------------ Steuerung (Bauleiste)

        public void Select(ServiceAction action)
        {
            if (action != ServiceAction.NoiseBarrier || Current != ServiceAction.NoiseBarrier) barrierChain = false; // Eigene Assets
            Current = action;
            if (!Active) tool.SetMode(RoadTool.Mode.Service);
        }

        public void SelectType(ServiceType type)
        {
            SelectedType = type;
            Select(ServiceAction.Place);
        }

        public void Rotate(float degrees) => Rotation = Mathf.Repeat(Rotation + degrees, 360f);

        // ------------------------------------------------------------ Update

        void Update()
        {
            if (tool == null) return;
            var kb = GameKeyboard.Current;
            if (kb == null) return;
            if (kb.digit4Key.wasPressedThisFrame && !kb.shiftKey.isPressed)
                tool.ToggleMode(RoadTool.Mode.Service); // UX: erneut 4 = Auswählen
            if (!Active) return;
            if (kb.commaKey.wasPressedThisFrame) Rotate(-15f);
            if (kb.periodKey.wasPressedThisFrame) Rotate(15f);
        }

        void LateUpdate()
        {
            label = null;
            if (tool == null) return;
            var overlay = GetComponent<ServiceOverlayView>();
            // Wohnhäuser setzen: im Haus-Werkzeug bestimmt das HouseTool die Abdeckungs-Ansicht
            if (overlay != null && tool.CurrentMode != RoadTool.Mode.House) overlay.ToolKind = Active && Current == ServiceAction.Place ? SelectedType.Kind : null;
            if (!Active || !tool.HasCursor || tool.Runner.World == null) { Hide(); return; }

            var mouse = Mouse.current;
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (Current == ServiceAction.Place) UpdatePlace(tool.Cursor, clicked);
            else if (Current == ServiceAction.NoiseBarrier) UpdateBarrier(tool.Cursor, clicked); // Eigene Assets
            else UpdateRemove(tool.Cursor, clicked);
            DrawReach();
        }

        void Hide()
        {
            if (ghost != null) ghost.SetActive(false);
            frontMarker?.Hide();
            if (ring != null) ring.enabled = false;
            foreach (var l in reachPieces) l?.Clear();
            cachedPlan = null;
        }

        void UpdatePlace(SimVector2 p, bool clicked)
        {
            var s = Services;
            var roads = tool.Runner.World.Roads;
            bool moved = SimVector2.DistanceSquared(p, cachedPos) > 0.25f || !Mathf.Approximately(Rotation, cachedRotation) ||
                         cachedType != SelectedType || cachedRoadVersion != roads.Version || cachedStructureVersion != s.StructureVersion;
            if (moved || cachedPlan == null)
            {
                cachedPos = p;
                cachedRotation = Rotation;
                cachedType = SelectedType;
                cachedRoadVersion = roads.Version;
                cachedStructureVersion = s.StructureVersion;
                cachedPlan = s.PlanAligned(SelectedType, p, Rotation); // UX: Vorderseite zur Straße, Rotation = Zusatzdrehung
                BuildReachPreview(cachedPlan);
            }
            var plan = cachedPlan;
            var t = SelectedType;
            // Vorschau mit dem echten Modell in Baudrehung (sonst Quader)
            var catalog = CityBuilder.Game.Assets.OwnModelCatalog.Instance;
            ShowGhost(plan.Position, plan.Front, t.Width, t.Depth, plan.Height, 5f, plan.IsValid, t.NeedsRoadFront,
                catalog != null ? catalog.Service(t.Id) : null);

            if (t.Walking)
            {
                ring.enabled = true;
                ring.sharedMaterial = plan.IsValid ? okMaterial : badMaterial;
                const int n = 64;
                ring.positionCount = n;
                float r = t.Range + Mathf.Max(t.Width, t.Depth) * 0.5f;
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    var q = p + new SimVector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    ring.SetPosition(i, SimConvert.ToWorld(q, tool.Runner.World.Terrain.HeightAt(q) + 1f));
                }
            }
            else ring.enabled = false;

            string range = t.Broadcast ? $"Funkradius {t.Range:0} m" : t.Walking ? $"Fußweg {t.Range:0} m" : $"Reichweite {t.Range:0.#} min Fahrzeit (≈ {t.RangeGameMinutes:0} Spielmin.)"; // G4 erw.: Funk
            string turn = plan.Aligned ? (Rotation != 0f ? $"zur Straße + {Rotation:0}°" : "zur Straße ausgerichtet") : $"{Rotation:0}°"; // UX
            string info = $"{t.DisplayName} · {t.Capacity:0} {t.CapacityUnit} · {range} · {t.Cost:0} € (+{t.UpkeepPerWeek:0} €/Woche) · {turn}";
            if (plan.Displaced.Count > 0) info += $" · reißt {plan.Displaced.Count} Gebäude ab";
            SetLabel(plan.IsValid ? info : plan.ObstacleNote ?? ErrorText(plan.Error), !plan.IsValid, SimConvert.ToWorld(p, plan.Height + 10f)); // Flughafen: Hinweis zur Einflugschneise
            if (clicked && plan.IsValid)
            {
                s.Build(plan);
                cachedPlan = null;
            }
        }

        void UpdateRemove(SimVector2 p, bool clicked)
        {
            ring.enabled = false;
            foreach (var l in reachPieces) l.Clear();
            cachedPlan = null;
            var f = Services.FacilityAt(p, 2f);
            if (f == null && TryRemoveBarrier(p, clicked)) return; // Eigene Assets: Lärmschutzwand abreißen
            if (f == null) { ghost.SetActive(false); frontMarker?.Hide(); return; }
            ShowGhost(f.Position, f.Front, f.Type.Width + 1f, f.Type.Depth + 1f, f.Height, 7f, false, false);
            SetLabel($"{f.Type.DisplayName} abreißen (+{f.Type.Cost * Simulation.Economy.EconomyRules.DemolitionRefundShare:0} €)", true, SimConvert.ToWorld(f.Position, f.Height + 12f));
            if (clicked) Services.Demolish(f.Id); // Abrisserlös 25 %
        }

        // ------------------------------------------------------------ Eigene Assets: Lärmschutzwand

        bool barrierChain;
        SimVector2 barrierStart;

        /// <summary>Wand gerade in Arbeit (Startpunkt gesetzt)?</summary>
        public bool BarrierChainActive => barrierChain;

        void UpdateBarrier(SimVector2 p, bool clicked)
        {
            ring.enabled = false;
            foreach (var l in reachPieces) l.Clear();
            cachedPlan = null;
            frontMarker?.Hide();
            var world = tool.Runner.World;
            var nb = world.NoiseBarriers;
            if (nb == null) { ghost.SetActive(false); return; }
            var kb = GameKeyboard.Current;
            bool free = kb != null && kb.ctrlKey.isPressed;
            if (!free && nb.SnapToRoad(p, out var snapped, out _)) p = snapped;
            float h = world.Terrain.HeightAt(p);
            if (!barrierChain)
            {
                ShowGhost(p, SimVector2.UnitY, 1.2f, 1.2f, h, NoiseBarrierSystem.WallHeight, true, false);
                SetLabel("Lärmschutzwand: Startpunkt setzen (rastet neben der Straße ein, Strg = frei) · " +
                         $"{NoiseBarrierSystem.CostPerMeter:0} €/m", false, SimConvert.ToWorld(p, h + 8f));
                if (clicked) { barrierStart = p; barrierChain = true; }
                return;
            }
            var plan = nb.Plan(barrierStart, p);
            var mid = (barrierStart + p) * 0.5f;
            var dir = p - barrierStart;
            var normal = dir.LengthSquared() > 1e-4f ? SimVector2.Normalize(new SimVector2(-dir.Y, dir.X)) : SimVector2.UnitY;
            ShowGhost(mid, normal, Mathf.Max(0.5f, plan.Length), NoiseBarrierSystem.Thickness, world.Terrain.HeightAt(mid),
                NoiseBarrierSystem.WallHeight, plan.IsValid, false);
            string info = plan.IsValid
                ? $"Lärmschutzwand {plan.Length:0} m · {plan.Cost:0} € · dämpft den Straßenlärm dahinter · Klick = setzen, weiter ziehen · RMB/Esc = fertig"
                : BarrierErrorText(plan.Error);
            SetLabel(info, !plan.IsValid, SimConvert.ToWorld(mid, world.Terrain.HeightAt(mid) + 8f));
            if (clicked && plan.IsValid && nb.Build(plan) != null) barrierStart = p; // Kette: nächstes Stück beginnt hier
        }

        bool TryRemoveBarrier(SimVector2 p, bool clicked)
        {
            var nb = tool.Runner.World.NoiseBarriers;
            var wall = nb?.BarrierNear(p, 2f);
            if (wall == null) return false;
            var mid = wall.Mid;
            float h = tool.Runner.World.Terrain.HeightAt(mid);
            ShowGhost(mid, wall.Normal, wall.Length + 1f, NoiseBarrierSystem.Thickness + 1f, h, NoiseBarrierSystem.WallHeight + 1f, false, false);
            SetLabel($"Lärmschutzwand ({wall.Length:0} m) abreißen (+{NoiseBarrierSystem.CostOf(wall.Length) * Simulation.Economy.EconomyRules.DemolitionRefundShare:0} €)",
                true, SimConvert.ToWorld(mid, h + 8f));
            if (clicked) nb.Demolish(wall.Id);
            return true;
        }

        public static string BarrierErrorText(NoiseBarrierError e)
        {
            switch (e)
            {
                case NoiseBarrierError.TooShort: return $"Zu kurz (mind. {NoiseBarrierSystem.MinLength:0} m)";
                case NoiseBarrierError.TooLong: return $"Zu lang (höchstens {NoiseBarrierSystem.MaxLength:0} m je Stück – Zwischenpunkt setzen)";
                case NoiseBarrierError.OutOfWorld: return "Außerhalb der Karte";
                case NoiseBarrierError.OnWater: return "Nicht im Wasser bauen";
                case NoiseBarrierError.OnRoad: return "Liegt auf Fahrbahn/Gehweg – neben die Straße setzen";
                case NoiseBarrierError.OverlapsStructure: return "Kreuzt ein Bauwerk";
                case NoiseBarrierError.OverlapsBuilding: return "Kreuzt ein Gebäude (wird nicht abgerissen)";
                case NoiseBarrierError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                default: return null;
            }
        }
        // ------------------------------------------------------------ Ende Lärmschutzwand

        /// <summary>Erreichte Straßen in Stücken einfärben (Farbe nach Entfernungswirkung, aus der Simulation).</summary>
        void BuildReachPreview(ServicePlan plan)
        {
            foreach (var l in reachPieces) l.Clear();
            var reach = Services.PreviewReach(plan);
            if (reach == null) return;
            foreach (var s in tool.Runner.World.Roads.Segments)
            {
                if (!reach.Nodes.ContainsKey(s.Start.Id) && !reach.Nodes.ContainsKey(s.End.Id) && s.Id != reach.StartSegmentId) continue;
                int pieces = Mathf.Max(1, Mathf.CeilToInt(s.Length / PieceLength));
                for (int i = 0; i < pieces; i++)
                {
                    float t = (i + 0.5f) / pieces;
                    float factor = reach.Factor(s, t);
                    if (factor <= 0f) continue;
                    int bucket = Mathf.Clamp(Mathf.RoundToInt((1f - factor) * (Buckets - 1)), 0, Buckets - 1);
                    var pos = SimConvert.ToWorld(s.Evaluate(t), s.HeightAt(t) + 0.45f);
                    var tan = s.Tangent(t);
                    var rot = Quaternion.LookRotation(new Vector3(tan.X, 0f, tan.Y));
                    reachPieces[bucket].Add(Matrix4x4.TRS(pos, rot, new Vector3(s.Type.Width * 0.6f, 0.2f, s.Length / pieces * 0.9f)));
                }
            }
        }

        void DrawReach()
        {
            if (cube == null) return;
            for (int b = 0; b < Buckets; b++)
            {
                var list = reachPieces[b];
                if (list.Count == 0) continue;
                var rp = new RenderParams(reachMaterials[b]) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                for (int start = 0; start < list.Count; start += buffer.Length)
                {
                    int n = Mathf.Min(buffer.Length, list.Count - start);
                    list.CopyTo(start, buffer, 0, n);
                    Graphics.RenderMeshInstanced(rp, cube, 0, buffer, n);
                }
            }
        }

        void ShowGhost(SimVector2 pos, SimVector2 front, float width, float depth, float height, float tall, bool valid, bool showFront,
                       GameObject model = null)
        {
            if (frontMarker == null) frontMarker = new CityBuilder.Game.UI.PlacementGhost(transform, "Service Front");
            if (model != null)
            {
                ghost.SetActive(false);
                frontMarker.ShowModel(model, pos, front, width, depth, height, tall, valid ? okMaterial : badMaterial, showFront);
                return;
            }
            ghost.SetActive(true);
            ghost.transform.SetPositionAndRotation(SimConvert.ToWorld(pos, height + tall * 0.5f),
                Quaternion.LookRotation(new Vector3(front.X, 0f, front.Y)));
            ghost.transform.localScale = new Vector3(width, tall, depth);
            ghostRenderer.sharedMaterial = valid ? okMaterial : badMaterial;
            // UX: Vorderseite (Zufahrt) als Streifen + Pfeil
            if (showFront) frontMarker.Show(pos, front, width, depth, height, tall, valid ? okMaterial : badMaterial);
            else frontMarker.Hide();
        }

        CityBuilder.Game.UI.PlacementGhost frontMarker;

        void SetLabel(string text, bool error, Vector3 world)
        {
            label = text;
            labelError = error;
            labelWorld = world;
        }

        public static string ErrorText(ServiceBuildError e)
        {
            switch (e)
            {
                case ServiceBuildError.OutOfWorld: return "Außerhalb der Karte";
                case ServiceBuildError.OnWater: return "Nicht im Wasser bauen";
                case ServiceBuildError.TooSteep: return $"Zu steil (max. {ServiceSystem.MaxSlope:0} m Höhenunterschied)";
                case ServiceBuildError.OverlapsRoad: return "Steht auf einer Straße";
                case ServiceBuildError.OverlapsFacility: return "Überschneidet ein anderes Bauwerk";
                case ServiceBuildError.OverlapsPole: return "Strommast im Weg";
                case ServiceBuildError.NoRoadAccess: return $"Keine Zufahrt an der Vorderseite (Pfeil) – zur Stadtstraße drehen, höchstens {ServiceSystem.RoadReach:0} m";
                case ServiceBuildError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                case ServiceBuildError.AlreadyBuilt: return "Gibt es nur einmal je Stadt – steht schon";
                case ServiceBuildError.ObstacleClearance: return "Zu hoch für die Einflugschneise des Flughafens – niedriger oder woanders bauen"; // Flughafen
                default: return null;
            }
        }

        // ------------------------------------------------------------ IMGUI

        void OnGUI()
        {
            if (CityBuilder.Game.UI.Panels.WorldLabels.Enabled)
            {
                // Neue UI: Beschriftung über der Vorschau als UI-Toolkit-Label melden
                if (Event.current.type != EventType.Layout) return;
                CityBuilder.Game.UI.Panels.WorldLabels.Begin(this);
                if (Active && !string.IsNullOrEmpty(label))
                    CityBuilder.Game.UI.Panels.WorldLabels.Add(this, labelWorld, label, labelError ? CityBuilder.Game.UI.Panels.WorldLabelStyle.Error : CityBuilder.Game.UI.Panels.WorldLabelStyle.Normal,
                        -6f, above: true);
                return;
            }
            if (!Active || string.IsNullOrEmpty(label) || tool.Camera == null) return;
            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.box) { fontSize = 12, richText = true, alignment = TextAnchor.MiddleCenter };
            var sp = tool.Camera.WorldToScreenPoint(labelWorld);
            if (sp.z <= 0f) return;
            var content = new GUIContent(labelError ? $"<color=#ff7060>{label}</color>" : label);
            var size = labelStyle.CalcSize(content);
            GUI.Box(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y - 6f, size.x + 8f, size.y + 4f), content, labelStyle);
        }

        /// <summary>
        /// Zeilen der Bauleiste (aufgerufen von <c>RoadToolbar</c> in deren Layout-Bereich; die neue UI bindet diese
        /// Methode später an).
        /// </summary>
        public void DrawToolbarRows()
        {
            if (infoStyle == null) infoStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
            var overlay = GetComponent<ServiceOverlayView>();

            GUILayout.BeginHorizontal();
            foreach (var t in ServiceCatalog.All)
                if (Button(ShortName(t), Active && Current == ServiceAction.Place && SelectedType == t, 0f)) SelectType(t);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (Button("Abriss", Active && Current == ServiceAction.Remove, 70f)) Select(ServiceAction.Remove);
            if (Button("Lärmschutzwand", Active && Current == ServiceAction.NoiseBarrier, 110f)) Select(ServiceAction.NoiseBarrier); // Eigene Assets
            if (GUILayout.Button("⟲ 15°", GUILayout.Width(55f), GUILayout.Height(24f))) Rotate(-15f);
            if (GUILayout.Button("⟳ 15°", GUILayout.Width(55f), GUILayout.Height(24f))) Rotate(15f);
            GUILayout.Label("Ansicht (⇧4):", GUILayout.Width(85f));
            if (overlay != null)
                foreach (var k in ServiceKinds.All)
                    if (Button(ServiceKinds.DisplayName(k), overlay.Current == k, 0f)) overlay.Toggle(k);
            GUILayout.EndHorizontal();

            var s = Services;
            GUILayout.Label($"{s.FacilityCount} Dienste · Unterhalt {s.WeeklyUpkeep():0} €/Woche · liegender Müll {s.TotalGarbage:0.#} t  ·  " +
                            "Reichweite über das Straßennetz (Fahrzeit, Einbahn beachtet), Parks über Fußweg · Überlastung senkt die Abdeckung",
                infoStyle);
        }

        public static string ShortName(ServiceType t)
        {
            switch (t.Id)
            {
                case "police_station": return "Polizei";
                case "fire_station": return "Feuerwehr";
                case "clinic": return "Klinik";
                case "hospital": return "Krankenhaus";
                case "school_primary": return "Grundschule";
                case "school_high": return "Gymnasium";
                case "library": return "Bibliothek";
                case "park_small": return "Park S";
                case "park_large": return "Park L";
                case "landfill": return "Müllhalde";
                case "incinerator": return "Verbrennung";
                case "media_newspaper": return "Zeitung";
                case "media_radio": return "Radio";
                case "media_tv": return "TV-Turm";
                // Eigene Assets: Freizeit/Wahrzeichen
                case "leisure_playground": return "Spielplatz";
                case "leisure_sports_field": return "Sportplatz";
                case "leisure_tennis": return "Tennis";
                case "leisure_pool": return "Freibad";
                case "leisure_stadium": return "Stadion";
                case "leisure_ferris_wheel": return "Riesenrad";
                case "landmark_town_hall": return "Rathaus";
                case "landmark_bandstand": return "Pavillon";
                default: return t.DisplayName;
            }
        }

        static bool Button(string text, bool active, float width)
        {
            var old = GUI.backgroundColor;
            if (active) GUI.backgroundColor = Selected;
            bool clicked = width > 0f ? GUILayout.Button(text, GUILayout.Width(width), GUILayout.Height(24f))
                                      : GUILayout.Button(text, GUILayout.Height(24f));
            GUI.backgroundColor = old;
            return clicked;
        }
    }
}
