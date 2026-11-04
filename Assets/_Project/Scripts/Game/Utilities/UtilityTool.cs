using System.Collections.Generic;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Utilities;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Utilities
{
    /// <summary>
    /// Versorgungs-Werkzeug (J): Kraftwerke, Umspannwerke, Pumpen platzieren (, und . drehen), Leitungen Punkt für Punkt
    /// verlegen (Klick Start → Klick Ende, weiter in Kette, RMB/Esc beendet), „Entlang Straßen verlegen“, Abriss.
    /// <para>
    /// Netze werden selbst verlegt (Straßen führen nichts mit): Wasserrohr, Abwasserkanal, Hochspannung (große Masten,
    /// Kraftwerk → Umspannwerk), Ortsnetz als Freileitung (kleine Masten neben der Straße, billig) oder Erdkabel (unter dem
    /// Gehweg, teurer). Unterirdische Leitungen rasten unter dem Gehweg ein und schalten die Unterirdisch-Ansicht ein.
    /// </para>
    /// Aktiv, wenn das RoadTool im Modus <see cref="RoadTool.Mode.Utility"/> ist – das RoadTool liefert den Cursor.
    /// Alle Prüfungen und das Bauen macht die Simulation (<see cref="UtilitySystem"/>).
    /// </summary>
    public sealed class UtilityTool : MonoBehaviour
    {
        /// <summary>
        /// Werkzeug. <see cref="PowerLine"/> = Freileitung im Ortsnetz (Name bleibt für bestehende Aufrufer),
        /// SewagePipe: Abwasser (G3 erweitert); HighVoltageLine/PowerCable/AlongRoads: Netze selbst verlegt.
        /// </summary>
        public enum UtilityAction { Facility, PowerLine, WaterPipe, Remove, SewagePipe, HighVoltageLine, PowerCable, AlongRoads, SubseaCable /* Energie erweitert */ }

        /// <summary>Höhe der Leiterseile über Gelände (Vorschau, Freileitung alt) bzw. Rohrtiefe darunter.</summary>
        public const float WireHeight = 12f;
        public const float PipeDepth = 2.5f;
        const float SnapDistance = 8f;

        static readonly Color Selected = new Color(0.45f, 0.75f, 1f);

        /// <summary>Seilhöhe je Leitungsart: Hochspannung 26 m, Freileitung im Ortsnetz 9 m.</summary>
        public static float WireHeightOf(UtilityLineType t) => t == UtilityCatalog.HighVoltageLine ? 26f : 9f;

        /// <summary>Tiefe unter Gelände je unterirdischer Leitungsart (Erdkabel flach, Wasser, Abwasser am tiefsten).</summary>
        public static float DepthOf(UtilityLineType t) =>
            t == UtilityCatalog.UndergroundCable ? 1.2f : t != null && t.Kind == UtilityKind.Sewage ? PipeDepth + 1.5f : PipeDepth;

        /// <summary>Höhe der Leitung relativ zum Gelände (oberirdisch +, unterirdisch −).</summary>
        public static float OffsetOf(UtilityLineType t) => t.Underground ? -DepthOf(t) : WireHeightOf(t);

        /// <summary>Leitungsart eines Leitungs-Werkzeugs (null = kein Leitungswerkzeug).</summary>
        public static UtilityLineType LineTypeOf(UtilityAction a)
        {
            switch (a)
            {
                case UtilityAction.PowerLine: return UtilityCatalog.OverheadLine;
                case UtilityAction.HighVoltageLine: return UtilityCatalog.HighVoltageLine;
                case UtilityAction.PowerCable: return UtilityCatalog.UndergroundCable;
                case UtilityAction.WaterPipe: return UtilityCatalog.WaterPipe;
                case UtilityAction.SewagePipe: return UtilityCatalog.SewerPipe;
                case UtilityAction.SubseaCable: return UtilityCatalog.SubseaCable; // Energie erweitert
                default: return null;
            }
        }

        /// <summary>Arten, die „Entlang Straßen verlegen“ anbietet (Hochspannung nicht – die führt über Land zum Umspannwerk).</summary>
        public static readonly UtilityLineType[] AlongTypes =
        {
            UtilityCatalog.UndergroundCable, UtilityCatalog.OverheadLine, UtilityCatalog.WaterPipe, UtilityCatalog.SewerPipe,
        };

        RoadTool tool;
        Material okMaterial, badMaterial;
        GameObject ghost;
        MeshRenderer ghostRenderer;
        LineRenderer lineRenderer;
        /// <summary>Vorschau der geplanten Masten (grün/blau = gültig, rot = kein zulässiger Standort).</summary>
        readonly List<GameObject> poleGhosts = new List<GameObject>();
        /// <summary>Vorschau „Entlang Straßen“: ein Strich je geplantem Stück.</summary>
        readonly List<LineRenderer> alongRenderers = new List<LineRenderer>();
        GUIStyle infoStyle, labelStyle;

        bool placingLine;
        SimVector2 lineStart;
        string label;
        bool labelError;
        Vector3 labelWorld;

        // „Entlang Straßen“: zwischengespeicherte Vorschau (neu, wenn sich Straßen/Leitungen/Art/Auswahl ändern)
        AlongRoadsPlan alongPlan;
        int alongSegment = -1, alongRoadVersion = -1, alongStructureVersion = -1;
        UtilityLineType alongPlanType;
        bool alongPlanAll;

        public UtilityAction Current { get; private set; } = UtilityAction.Facility;
        public UtilityFacilityType SelectedType { get; private set; } = UtilityCatalog.CoalPlant;
        public float Rotation { get; private set; }
        public bool Active => tool != null && tool.CurrentMode == RoadTool.Mode.Utility;
        /// <summary>Leitungsart für „Entlang Straßen verlegen“.</summary>
        public UtilityLineType AlongType { get; private set; } = UtilityCatalog.UndergroundCable;
        /// <summary>„Entlang Straßen“: alle Stadtstraßen statt der Straße unter dem Mauszeiger.</summary>
        public bool AlongAll { get; private set; }
        /// <summary>Aktuelle Vorschau „Entlang Straßen“ (für Bauleiste/Info), null = keine.</summary>
        public AlongRoadsPlan AlongPreview => Active && Current == UtilityAction.AlongRoads ? alongPlan : null;

        UtilitySystem Utilities => tool.Runner.World.Utilities;

        public void Init(RoadTool roadTool, Material transparent)
        {
            tool = roadTool;
            if (tool.Runner != null) tool.Runner.WorldReplaced += () => { placingLine = false; AlongAll = false; alongPlan = null; }; // Speichern & Laden
            // UX: Esc/Rechtsklick beenden die Leitungskette bzw. „alle Straßen“ (sonst zurück zum Auswählen)
            tool.RegisterOperation(() => Active && (placingLine || (Current == UtilityAction.AlongRoads && AlongAll)),
                () => { placingLine = false; AlongAll = false; });
            okMaterial = new Material(transparent) { name = "UtilityPreviewOk", color = new Color(0.3f, 0.9f, 1f, 0.45f) };
            badMaterial = new Material(transparent) { name = "UtilityPreviewBad", color = new Color(1f, 0.25f, 0.2f, 0.5f) };
        }

        void Start()
        {
            ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ghost.name = "Utility Preview";
            Destroy(ghost.GetComponent<Collider>());
            ghost.transform.SetParent(transform, false);
            ghostRenderer = ghost.GetComponent<MeshRenderer>();
            ghostRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ghost.SetActive(false);

            lineRenderer = NewLineRenderer("Utility Line Preview");
        }

        LineRenderer NewLineRenderer(string name)
        {
            var lineGo = new GameObject(name);
            lineGo.transform.SetParent(transform, false);
            var lr = lineGo.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.widthMultiplier = 0.8f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.enabled = false;
            return lr;
        }

        void OnDestroy()
        {
            if (okMaterial != null) Destroy(okMaterial);
            if (badMaterial != null) Destroy(badMaterial);
        }

        // ------------------------------------------------------------ Steuerung (Toolbar)

        public void Select(UtilityAction action)
        {
            Current = action;
            placingLine = false;
            if (action != UtilityAction.AlongRoads) AlongAll = false;
            if (!Active) tool.SetMode(RoadTool.Mode.Utility);
        }

        public void SelectFacility(UtilityFacilityType type)
        {
            SelectedType = type;
            Select(UtilityAction.Facility);
        }

        /// <summary>„Entlang Straßen verlegen“ mit dieser Leitungsart.</summary>
        public void SelectAlong(UtilityLineType type)
        {
            if (type != null) AlongType = type;
            Select(UtilityAction.AlongRoads);
        }

        /// <summary>„Entlang Straßen“: alle Stadtstraßen (Vorschau mit Gesamtkosten, Klick verlegt) ein/aus.</summary>
        public void ToggleAlongAll()
        {
            bool all = !(Current == UtilityAction.AlongRoads && AlongAll);
            Select(UtilityAction.AlongRoads);
            AlongAll = all;
        }

        /// <summary>Verlegt die aktuelle Vorschau „Entlang Straßen“ (Knopf in der Bauleiste). Liefert die Zahl gebauter Stücke.</summary>
        public int BuildAlongNow()
        {
            var plan = alongPlan;
            if (plan == null || !plan.IsValid) return 0;
            int built = Utilities.BuildAlongRoads(plan);
            if (built > 0)
            {
                CityBuilder.Game.UI.Hud.HudMessages.Info($"{plan.Type.DisplayName} verlegt",
                    $"{built} Stücke · {plan.Length:0} m · {plan.Cost:0} € · {plan.BuildingsReached} Gebäude neu angeschlossen",
                    plan.Type.Kind == UtilityKind.Power ? CityBuilder.Game.UI.Hud.HudIconKind.Power : CityBuilder.Game.UI.Hud.HudIconKind.Pipe,
                    "utility-along");
                AlongAll = false;
            }
            alongPlan = null;
            alongStructureVersion = -1;
            return built;
        }

        public void Rotate(float degrees) => Rotation = Mathf.Repeat(Rotation + degrees, 360f);

        // ------------------------------------------------------------ Update

        void Update()
        {
            if (tool == null) return;
            var kb = GameKeyboard.Current;
            if (kb == null) return;
            if (kb.jKey.wasPressedThisFrame) tool.ToggleMode(RoadTool.Mode.Utility); // UX: erneut J = Auswählen
            if (!Active) return;
            { float turn = GameKeyboard.RotateInput(kb, 15f); if (turn != 0f) Rotate(turn); } // , . oder Strg links + Mausrad
            // Esc/Rechtsklick: über RoadTool.RegisterOperation (beendet die Kette, sonst zurück zum Auswählen)
        }

        bool WantsUnderground()
        {
            if (!Active) return false;
            if (Current == UtilityAction.AlongRoads) return AlongType.Underground;
            var t = LineTypeOf(Current);
            return t != null && t.Underground;
        }

        void LateUpdate()
        {
            label = null;
            if (tool == null) return;
            tool.ForceUnderground = WantsUnderground();
            if (!Active || !tool.HasCursor || tool.Runner.World == null) { Hide(); placingLine &= Active; return; }

            var mouse = Mouse.current;
            bool clicked = mouse != null && mouse.leftButton.wasPressedThisFrame;
            var p = tool.Cursor;
            if (Current != UtilityAction.AlongRoads) HideAlong();
            switch (Current)
            {
                case UtilityAction.Facility: UpdateFacility(p, clicked); break;
                case UtilityAction.Remove: UpdateRemove(p, clicked); break;
                case UtilityAction.AlongRoads: UpdateAlong(p, clicked); break;
                default: UpdateLine(LineTypeOf(Current), p, clicked); break;
            }
        }

        void Hide()
        {
            if (ghost != null) ghost.SetActive(false);
            frontMarker?.Hide();
            if (lineRenderer != null) lineRenderer.enabled = false;
            HidePoles();
            HideAlong();
        }

        void HidePoles(int from = 0)
        {
            for (int i = from; i < poleGhosts.Count; i++) if (poleGhosts[i] != null) poleGhosts[i].SetActive(false);
        }

        void HideAlong(int from = 0)
        {
            for (int i = from; i < alongRenderers.Count; i++) if (alongRenderers[i] != null) alongRenderers[i].enabled = false;
        }

        /// <summary>Zeigt die von der Simulation geplanten Masten; ungültige rot.</summary>
        void ShowPoles(LinePlan plan)
        {
            int n = plan.Type.HasPoles ? plan.Poles.Count : 0;
            while (poleGhosts.Count < n)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Pole Preview";
                Destroy(go.GetComponent<Collider>());
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                poleGhosts.Add(go);
            }
            float h = WireHeightOf(plan.Type);
            for (int i = 0; i < n; i++)
            {
                var pole = plan.Poles[i];
                var go = poleGhosts[i];
                go.SetActive(true);
                float baseH = Mathf.Max(Ground(pole.Position), tool.Runner.World.Terrain.WaterLevel);
                go.transform.SetPositionAndRotation(SimConvert.ToWorld(pole.Position, baseH + h * 0.5f), Quaternion.identity);
                // Grundfläche des Masts (inkl. Rand) – so viel Platz belegt er
                float foot = plan.Type.PoleFootprintHalf * 2f;
                go.transform.localScale = new Vector3(foot, h, foot);
                go.GetComponent<MeshRenderer>().sharedMaterial = pole.IsValid ? okMaterial : badMaterial;
            }
            HidePoles(n);
        }

        float Ground(SimVector2 p) => tool.Runner.World.Terrain.HeightAt(p);

        void UpdateFacility(SimVector2 p, bool clicked)
        {
            lineRenderer.enabled = false;
            HidePoles();
            var plan = Utilities.PlanFacilityAligned(SelectedType, p, Rotation); // UX: Kraftwerke/Wasserturm zur Straße
            // Vorschau mit dem echten Modell in Baudrehung (sonst Quader)
            var catalog = CityBuilder.Game.Assets.OwnModelCatalog.Instance;
            ShowGhost(plan.Position, plan.Front, SelectedType.Width, SelectedType.Depth, plan.Height, 4f, plan.IsValid, SelectedType.NeedsRoadFront,
                catalog != null ? catalog.Facility(SelectedType.Id) : null, SelectedType.Units);
            var t = SelectedType;
            string turn = plan.Aligned ? (Rotation != 0f ? $"zur Straße + {Rotation:0}°" : "zur Straße ausgerichtet") : $"{Rotation:0}°";
            string power = t.IsSubstation ? $"Umspannkapazität {t.Capacity / 1000f:0.#} MW" : $"{t.Capacity:0} {UtilityCatalog.Unit(t.Kind)}";
            string info = $"{t.DisplayName} · {power} · {t.Cost:0} € (+{t.UpkeepPerDay:0} €/Tag) · {turn}";
            // --- Energie erweitert: Standortleistung (Wasserkraft), Speicher, Offshore, Biogas
            if (t.Output == OutputModel.Hydro && plan.Hydro.Width > 0f)
                info += $" · Standort: Fluss {plan.Hydro.Width:0} m breit, Abfluss {plan.Hydro.Flow:0} m³/s, Fallhöhe {plan.Hydro.Head:0.0} m → {plan.Rated / 1000f:0.0} MW";
            if (t.IsStorage) info += $" · {t.StorageKwh / 1000f:0} MWh · lädt bei Überschuss, liefert bei Unterdeckung";
            if (t.Site == FacilitySite.Sea) info += " · nur im Meer · Hochspannungs-Seekabel zum Umspannwerk an Land";
            if (t.Output == OutputModel.Biogas) info += $" · Rohstoff aus Bauernhöfen im Umkreis {UtilitySystem.BiogasFeedRadius / 1000f:0} km";
            // --- Ende Energie erweitert
            if (t.IsSubstation) info += " · Hochspannung an einen Rand, Ortsnetz an einen anderen";
            else if (t.Kind == UtilityKind.Power) info += t.FeedsLocalGrid ? " · speist ins Ortsnetz oder in die Hochspannung" : " · Hochspannung zum Umspannwerk verlegen";
            if (plan.Displaced.Count > 0) info += $" · reißt {plan.Displaced.Count} Gebäude ab";
            if (plan.IsValid && plan.MissingRoad) // Frei setzen: Biogas/Speicher brauchen später eine Straße
                info = $"Noch keine Straße – arbeitet erst mit Stadtstraße vor der Vorderseite (Pfeil), sonst verfällt es nach {UtilitySystem.NoRoadDays} Tagen · " + info;
            SetLabel(plan.IsValid ? info : plan.ObstacleNote ?? ErrorText(plan.Error), !plan.IsValid, SimConvert.ToWorld(p, plan.Height + 8f)); // Flughafen: Hinweis zur Einflugschneise
            if (clicked && plan.IsValid) Utilities.BuildFacility(plan);
        }

        void UpdateLine(UtilityLineType type, SimVector2 raw, bool clicked)
        {
            ghost.SetActive(false);
            var p = Utilities.ResolveLinePoint(type, raw, SnapDistance, out var snap);
            float offset = OffsetOf(type);
            float labelH = WireHeightOf(type);
            if (!placingLine)
            {
                HidePoles();
                // Nur Markierung am Cursor
                lineRenderer.enabled = true;
                lineRenderer.sharedMaterial = okMaterial;
                lineRenderer.positionCount = 2;
                lineRenderer.SetPosition(0, SimConvert.ToWorld(p, Ground(p)));
                lineRenderer.SetPosition(1, SimConvert.ToWorld(p, Ground(p) + offset));
                SetLabel($"{type.DisplayName}: Startpunkt wählen{SnapText(snap)} · {Hint(type)}", false, SimConvert.ToWorld(p, Ground(p) + labelH));
                if (clicked) { lineStart = p; placingLine = true; }
                return;
            }

            var plan = Utilities.PlanLine(type, lineStart, p);
            lineRenderer.enabled = true;
            lineRenderer.sharedMaterial = plan.IsValid ? okMaterial : badMaterial;
            ShowPoles(plan);
            if (type.HasPoles && plan.Poles.Count > 0)
            {
                // Seilverlauf über die geplanten Masten (Enden = Anschlusspunkte)
                lineRenderer.positionCount = plan.Poles.Count + 2;
                lineRenderer.SetPosition(0, SimConvert.ToWorld(lineStart, Ground(lineStart) + offset));
                for (int i = 0; i < plan.Poles.Count; i++)
                {
                    var q = plan.Poles[i].Position;
                    lineRenderer.SetPosition(i + 1, SimConvert.ToWorld(q, Ground(q) + offset));
                }
                lineRenderer.SetPosition(plan.Poles.Count + 1, SimConvert.ToWorld(p, Ground(p) + offset));
            }
            else DrawStraight(lineRenderer, lineStart, p, offset);
            string poles = type.HasPoles ? $" · {plan.Poles.Count} Masten" : "";
            string text = plan.IsValid
                ? $"{type.DisplayName} · {plan.Length:0} m{poles} · {plan.Cost:0} € ({type.CostPerMeter:0} €/m){SnapText(snap)} · RMB beenden"
                : ErrorText(plan.Error);
            SetLabel(text, !plan.IsValid, SimConvert.ToWorld(p, Ground(p) + labelH));
            if (clicked && plan.IsValid)
            {
                Utilities.BuildLine(plan);
                lineStart = p; // in Kette weiterbauen
            }
        }

        void DrawStraight(LineRenderer lr, SimVector2 a, SimVector2 b, float offset)
        {
            const int n = 16;
            lr.positionCount = n + 1;
            for (int i = 0; i <= n; i++)
            {
                var q = SimVector2.Lerp(a, b, i / (float)n);
                lr.SetPosition(i, SimConvert.ToWorld(q, Ground(q) + offset));
            }
        }

        static string Hint(UtilityLineType t)
        {
            float reach = ConnectionRules.BuildingReach;
            if (t == UtilityCatalog.HighVoltageLine)
                return "vom Kraftwerk zum Umspannwerk · große Masten, keine Hausanschlüsse";
            if (t == UtilityCatalog.OverheadLine)
                return $"Ortsnetz vom Umspannwerk entlang der Straße · Masten neben der Straße brauchen Platz · Gebäude ≤ {reach:0} m angeschlossen";
            if (t == UtilityCatalog.SubseaCable) // Energie erweitert
                return "Hochspannung ohne Masten: durchs Meer (Offshore-Windpark → Umspannwerk an Land) oder als Erdkabel unter Straßen · keine Hausanschlüsse";
            if (t == UtilityCatalog.UndergroundCable)
                return $"Ortsnetz unter dem Gehweg (teurer, kein Platzbedarf) · Gebäude ≤ {reach:0} m angeschlossen";
            if (t.Kind == UtilityKind.Sewage)
                return $"Kanäle selbst verlegen – unter dem Gehweg einrasten, Gebäude ≤ {reach:0} m sind angeschlossen";
            return $"Rohr selbst verlegen (Straßen führen kein Wasser mit) – unter dem Gehweg einrasten, Gebäude ≤ {reach:0} m angeschlossen";
        }

        // ------------------------------------------------------------ Entlang Straßen verlegen

        void UpdateAlong(SimVector2 p, bool clicked)
        {
            ghost.SetActive(false);
            frontMarker?.Hide();
            lineRenderer.enabled = false;
            HidePoles();
            var world = tool.Runner.World;
            int seg = -1;
            if (!AlongAll)
            {
                var s = world.Roads.FindNearestSegment(p, 6f);
                if (s != null && s.Type.Category == RoadCategory.City) seg = s.Id;
            }
            if (!AlongAll && seg < 0)
            {
                alongPlan = null;
                HideAlong();
                SetLabel($"Entlang Straßen verlegen ({AlongType.DisplayName}): Stadtstraße anklicken – oder „Alle Stadtstraßen“ in der Bauleiste",
                    false, SimConvert.ToWorld(p, Ground(p) + 6f));
                return;
            }

            // Vorschau nur neu rechnen, wenn sich etwas geändert hat (alle Straßen kann teuer sein)
            if (alongPlan == null || alongPlanType != AlongType || alongPlanAll != AlongAll || (!AlongAll && alongSegment != seg) ||
                alongRoadVersion != world.Roads.Version || alongStructureVersion != world.Utilities.StructureVersion)
            {
                alongPlan = AlongAll ? Utilities.PlanAlongRoads(AlongType) : Utilities.PlanAlongRoad(AlongType, seg);
                alongPlanType = AlongType;
                alongPlanAll = AlongAll;
                alongSegment = seg;
                alongRoadVersion = world.Roads.Version;
                alongStructureVersion = world.Utilities.StructureVersion;
            }
            ShowAlong(alongPlan);
            SetLabel(AlongText(alongPlan, AlongAll) + (alongPlan.IsValid ? " · Klick: verlegen" : ""), !alongPlan.IsValid && alongPlan.Existing == 0,
                SimConvert.ToWorld(p, Ground(p) + 8f));
            if (clicked && alongPlan.IsValid) BuildAlongNow();
        }

        /// <summary>Kurztext der Vorschau (auch für die Infozeile der Bauleiste).</summary>
        public static string AlongText(AlongRoadsPlan plan, bool all)
        {
            if (plan == null) return "";
            string where = all ? $"{plan.SegmentIds.Count} Straßenstücke" : "diese Straße";
            string text = $"{plan.Type.DisplayName} entlang {where}: {plan.Length:0} m · {plan.Cost:0} € · {plan.BuildingsReached} Gebäude neu angeschlossen";
            if (plan.Existing > 0) text += $" · {plan.Existing} Stücke liegen schon";
            if (plan.Invalid > 0) text += $" · {plan.Invalid} Stücke nicht möglich ({ErrorText(plan.FirstInvalid)})";
            if (plan.Error == UtilityBuildError.InsufficientFunds) text += " · " + ErrorText(plan.Error);
            else if (plan.ValidCount == 0) text += " · nichts zu verlegen";
            return text;
        }

        void ShowAlong(AlongRoadsPlan plan)
        {
            int n = plan.Pieces.Count;
            while (alongRenderers.Count < n) alongRenderers.Add(NewLineRenderer("Along Roads Preview"));
            float offset = OffsetOf(plan.Type);
            for (int i = 0; i < n; i++)
            {
                var lr = alongRenderers[i];
                var piece = plan.Pieces[i];
                lr.enabled = true;
                lr.sharedMaterial = piece.IsValid ? okMaterial : badMaterial;
                // Oberirdisch: über die Masten, sonst gerade unter dem Gehweg
                if (plan.Type.HasPoles && piece.Poles.Count > 0)
                {
                    lr.positionCount = piece.Poles.Count + 2;
                    lr.SetPosition(0, SimConvert.ToWorld(piece.A, Ground(piece.A) + offset));
                    for (int j = 0; j < piece.Poles.Count; j++)
                        lr.SetPosition(j + 1, SimConvert.ToWorld(piece.Poles[j].Position, Ground(piece.Poles[j].Position) + offset));
                    lr.SetPosition(piece.Poles.Count + 1, SimConvert.ToWorld(piece.B, Ground(piece.B) + offset));
                }
                else DrawStraight(lr, piece.A, piece.B, offset);
            }
            HideAlong(n);
        }

        // ------------------------------------------------------------ Abriss

        void UpdateRemove(SimVector2 p, bool clicked)
        {
            HidePoles();
            var f = Utilities.FacilityAt(p, 2f);
            if (f != null)
            {
                lineRenderer.enabled = false;
                ShowGhost(f.Position, f.Front, f.Type.Width + 1f, f.Type.Depth + 1f, f.Height, 6f, false, false);
                SetLabel($"{f.Type.DisplayName} abreißen", true, SimConvert.ToWorld(f.Position, f.Height + 10f));
                if (clicked) Utilities.DemolishFacility(f.Id);
                return;
            }
            ghost.SetActive(false);
            var line = Utilities.LineNear(p, 6f);
            if (line == null) { lineRenderer.enabled = false; return; }
            float offset = OffsetOf(line.Type);
            lineRenderer.enabled = true;
            lineRenderer.sharedMaterial = badMaterial;
            lineRenderer.positionCount = 2;
            lineRenderer.SetPosition(0, SimConvert.ToWorld(line.A, Ground(line.A) + offset));
            lineRenderer.SetPosition(1, SimConvert.ToWorld(line.B, Ground(line.B) + offset));
            SetLabel($"{line.Type.DisplayName} abreißen ({line.Length:0} m)", true, SimConvert.ToWorld(p, Ground(p) + WireHeight));
            if (clicked) Utilities.DemolishLine(line.Id);
        }

        void ShowGhost(SimVector2 pos, SimVector2 front, float width, float depth, float height, float tall, bool valid, bool showFront,
                       GameObject model = null, int units = 1)
        {
            if (frontMarker == null) frontMarker = new PlacementGhost(transform, "Utility Front");
            if (model != null)
            {
                ghost.SetActive(false);
                frontMarker.ShowModel(model, pos, front, width, depth, height, tall, valid ? okMaterial : badMaterial, showFront, units);
                return;
            }
            ghost.SetActive(true);
            ghost.transform.SetPositionAndRotation(SimConvert.ToWorld(pos, height + tall * 0.5f),
                Quaternion.LookRotation(new Vector3(front.X, 0f, front.Y)));
            ghost.transform.localScale = new Vector3(width, tall, depth);
            ghostRenderer.sharedMaterial = valid ? okMaterial : badMaterial;
            // UX: Vorderseite (Zufahrt) als Streifen + Pfeil – nur bei Bauwerken mit Straßenfront
            if (showFront) frontMarker.Show(pos, front, width, depth, height, tall, valid ? okMaterial : badMaterial);
            else frontMarker.Hide();
        }

        PlacementGhost frontMarker;

        void SetLabel(string text, bool error, Vector3 world)
        {
            label = text;
            labelError = error;
            labelWorld = world;
        }

        static string SnapText(UtilitySnap s)
        {
            switch (s)
            {
                case UtilitySnap.LineEnd: return " · an Leitungsende";
                case UtilitySnap.Line: return " · an Leitung";
                case UtilitySnap.Facility: return " · an Bauwerk";
                case UtilitySnap.Road: return " · an Straße";
                case UtilitySnap.Building: return " · an Gebäude";
                default: return "";
            }
        }

        public static string ErrorText(UtilityBuildError e)
        {
            switch (e)
            {
                case UtilityBuildError.OutOfWorld: return "Außerhalb der Karte";
                case UtilityBuildError.OverlapsRoad: return "Steht auf einer Straße";
                case UtilityBuildError.OverlapsFacility: return "Überschneidet ein anderes Bauwerk";
                case UtilityBuildError.OnWater: return "Nicht im Wasser bauen";
                case UtilityBuildError.NeedsWater: return "Muss am Ufer stehen (Wasser in Reichweite)";
                case UtilityBuildError.TooSteep: return $"Zu steil (max. {UtilitySystem.MaxFacilitySlope:0} m Höhenunterschied)";
                case UtilityBuildError.TooShort: return "Zu kurz";
                case UtilityBuildError.Duplicate: return "Diese Leitung gibt es schon";
                case UtilityBuildError.PoleOnRoad:
                    return "Kein Maststandort: Masten nie auf Fahrbahn/Gehweg, Straße überspannen nur bis zur Spannweite";
                case UtilityBuildError.PoleOnWater: return "Kein Maststandort: Wasser breiter als die Spannweite";
                case UtilityBuildError.PoleOnFacility: return "Kein Maststandort: Bauwerk im Weg";
                case UtilityBuildError.PoleOnBuilding: return "Kein Maststandort: Gebäude im Weg (wird nicht abgerissen) – Erdkabel oder andere Trasse";
                case UtilityBuildError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                case UtilityBuildError.ObstacleClearance: return "Zu hoch für die Einflugschneise des Flughafens – niedriger oder woanders bauen"; // Flughafen
                case UtilityBuildError.NoRoadAccess: return $"Keine Zufahrt an der Vorderseite (Pfeil) – zur Stadtstraße drehen, höchstens {UtilitySystem.RoadReach:0} m";
                // --- Energie erweitert
                case UtilityBuildError.NeedsRiver: return "Nur im Fluss (Mitte im fließenden Wasser, nicht im Meer) – das Wehr richtet sich quer zum Fluss aus";
                case UtilityBuildError.NeedsSea: return $"Nur im offenen Meer (ganze Fläche im Wasser, mind. {UtilitySystem.SeaMinDepth:0} m tief, weg von der Küste)";
                case UtilityBuildError.NoHydroSite: return $"Kein Wasserkraft-Standort: Ufer zu niedrig für den Aufstau (Fallhöhe mind. {UtilitySystem.HydroMinHead:0} m) oder Fluss zu schmal/breit";
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

        /// <summary>Zeilen der Bauleiste (aufgerufen von <see cref="RoadToolbar"/> in deren Layout-Bereich).</summary>
        public void DrawToolbarRows()
        {
            if (infoStyle == null) infoStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };
            var overlay = GetComponent<UtilityOverlayView>();

            GUILayout.BeginHorizontal();
            foreach (var t in UtilityCatalog.All)
                if (Button(ShortName(t), Active && Current == UtilityAction.Facility && SelectedType == t, 0f)) SelectFacility(t);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (Button("Hochspannung", Active && Current == UtilityAction.HighVoltageLine, 105f)) Select(UtilityAction.HighVoltageLine);
            if (Button("Freileitung", Active && Current == UtilityAction.PowerLine, 90f)) Select(UtilityAction.PowerLine);
            if (Button("Erdkabel", Active && Current == UtilityAction.PowerCable, 80f)) Select(UtilityAction.PowerCable);
            if (Button("Seekabel", Active && Current == UtilityAction.SubseaCable, 80f)) Select(UtilityAction.SubseaCable); // Energie erweitert
            if (Button("Wasserrohr", Active && Current == UtilityAction.WaterPipe, 95f)) Select(UtilityAction.WaterPipe);
            if (Button("Abwasserkanal", Active && Current == UtilityAction.SewagePipe, 110f)) Select(UtilityAction.SewagePipe); // G3 erweitert
            if (Button("Entlang Straßen", Active && Current == UtilityAction.AlongRoads, 115f)) SelectAlong(AlongType);
            if (Button("Abriss", Active && Current == UtilityAction.Remove, 70f)) Select(UtilityAction.Remove);
            if (GUILayout.Button("⟲ 15°", GUILayout.Width(55f), GUILayout.Height(24f))) Rotate(-15f);
            if (GUILayout.Button("⟳ 15°", GUILayout.Width(55f), GUILayout.Height(24f))) Rotate(15f);
            GUILayout.FlexibleSpace();
            if (overlay != null)
            {
                if (Button("Strom-Ansicht (O)", overlay.Current == UtilityOverlayView.Overlay.Power, 135f)) overlay.Toggle(UtilityOverlayView.Overlay.Power);
                if (Button("Wasser-Ansicht (O)", overlay.Current == UtilityOverlayView.Overlay.Water, 140f)) overlay.Toggle(UtilityOverlayView.Overlay.Water);
            }
            if (Button("Unterirdisch (X)", tool.UndergroundView, 120f)) tool.ToggleUndergroundView();
            GUILayout.EndHorizontal();

            if (Active && Current == UtilityAction.AlongRoads)
            {
                GUILayout.BeginHorizontal();
                foreach (var t in AlongTypes)
                    if (Button(t.DisplayName, AlongType == t, 0f)) SelectAlong(t);
                if (Button("Alle Stadtstraßen", AlongAll, 130f)) ToggleAlongAll();
                GUILayout.EndHorizontal();
            }

            var u = Utilities;
            string Balance(UtilityKind k)
            {
                float prod = u.TotalProduction(k), dem = u.TotalDemand(k);
                string c = dem > prod + 0.001f ? "#ff7060" : "#7dff8a";
                return $"<color={c}>{UtilityCatalog.KindName(k)} {prod:0}/{dem:0} {UtilityCatalog.Unit(k)}</color>";
            }
            GUILayout.Label($"{Balance(UtilityKind.Power)}  ·  {Balance(UtilityKind.Water)}  ·  Unterhalt {u.TotalUpkeepPerDay:0} €/Tag  ·  " +
                            "Netze selbst verlegen: Kraftwerk → Hochspannung → Umspannwerk → Ortsnetz (Freileitung/Erdkabel) → Gebäude ≤ " +
                            $"{ConnectionRules.BuildingReach:0} m · Rohre/Kabel unter dem Gehweg", infoStyle);
        }

        static string ShortName(UtilityFacilityType t)
        {
            switch (t.Id)
            {
                case "power_coal": return "Kohle";
                case "power_gas": return "Gas";
                case "power_wind": return "Wind";
                case "power_solar": return "Solar";
                case "water_pump": return "Pumpe (Ufer)";
                case "water_tower": return "Wasserturm";
                case "power_nuclear": return "Atom";
                case "power_substation": return "Umspannwerk";
                case "sewage_plant": return "Kläranlage";
                case "sewage_outlet": return "Auslass (Ufer)";
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
