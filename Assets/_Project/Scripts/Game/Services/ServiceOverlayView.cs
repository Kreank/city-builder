using System.Collections.Generic;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Zoning;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.Services
{
    /// <summary>
    /// Info-Ansicht je Dienstart: über jedem fertigen Gebäude ein Marker mit Bodenplatte, Farbe nach Abdeckung
    /// (grün = 1 → rot = 0). Müll-Ansicht: Marker wächst mit dem liegenden Müll. Dienste der Art bekommen einen
    /// blauen Marker. Umschalten: ⇧4 (aus → Polizei → … → Müll → aus) oder Knöpfe in der Bauleiste; im
    /// Dienste-Werkzeug zeigt sie automatisch die Art des gewählten Gebäudes. Liest nur die Simulation.
    /// </summary>
    public sealed class ServiceOverlayView : MonoBehaviour
    {
        const int Buckets = 6;
        static readonly Color Good = new Color(0.2f, 0.9f, 0.3f);
        static readonly Color Mid = new Color(1f, 0.85f, 0.2f);
        static readonly Color Bad = new Color(0.95f, 0.15f, 0.1f);
        static readonly Color Source = new Color(0.25f, 0.6f, 1f);

        SimulationRunner runner;
        readonly Material[] markers = new Material[Buckets], plates = new Material[Buckets];
        Material sourceMarker;
        Mesh cube;
        readonly List<Matrix4x4>[] markerLists = new List<Matrix4x4>[Buckets], plateLists = new List<Matrix4x4>[Buckets];
        readonly List<Matrix4x4> sourceList = new List<Matrix4x4>();
        readonly Matrix4x4[] buffer = new Matrix4x4[1023];
        int shownCoverage = -1, shownCity = -1, shownStructure = -1, shownSafety = -1;
        string shownKind;

        /// <summary>Vom Spieler gewählte Ansicht (null = aus).</summary>
        public string Selected { get; private set; }
        /// <summary>Vom Werkzeug gesetzt: Art des gewählten Gebäudes beim Platzieren (hat Vorrang).</summary>
        public string ToolKind { get; set; }
        /// <summary>Tatsächlich angezeigte Art (null = aus).</summary>
        public string Current => ToolKind ?? Selected;

        /// <summary>Abdeckung 0..1 → Farbe rot → gelb → grün.</summary>
        public static Color CoverageColor(float coverage)
        {
            coverage = Mathf.Clamp01(coverage);
            return coverage < 0.5f ? Color.Lerp(Bad, Mid, coverage * 2f) : Color.Lerp(Mid, Good, (coverage - 0.5f) * 2f);
        }

        public void Init(SimulationRunner r, Material lit, Material transparent)
        {
            runner = r;
            r.WorldReplaced += () => shownCoverage = shownCity = shownStructure = shownSafety = -1; // Speichern & Laden: neu aufbauen
            for (int i = 0; i < Buckets; i++)
            {
                var c = CoverageColor(i / (float)(Buckets - 1));
                markers[i] = Colored(lit, c, "ServiceMarker" + i);
                plates[i] = Colored(transparent, new Color(c.r, c.g, c.b, 0.45f), "ServicePlate" + i);
                markerLists[i] = new List<Matrix4x4>();
                plateLists[i] = new List<Matrix4x4>();
            }
            sourceMarker = Colored(lit, Source, "ServiceSource");
        }

        static Material Colored(Material source, Color c, string name)
        {
            var m = new Material(source) { name = name, enableInstancing = true };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            return m;
        }

        void Start()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }

        void OnDestroy()
        {
            foreach (var m in markers) if (m != null) Destroy(m);
            foreach (var m in plates) if (m != null) Destroy(m);
            if (sourceMarker != null) Destroy(sourceMarker);
        }

        public void Toggle(string kind) => Selected = Selected == kind ? null : kind;

        /// <summary>Nächste Ansicht: aus → Polizei → … → Müll → aus (⇧4, HUD-Knopf „Dienste-Abdeckung“).</summary>
        public void Cycle()
        {
            int i = Selected == null ? -1 : System.Array.IndexOf(ServiceKinds.All, Selected);
            Selected = i + 1 < ServiceKinds.All.Length ? ServiceKinds.All[i + 1] : null;
        }

        void Update()
        {
            var kb = GameKeyboard.Current;
            if (kb != null && kb.digit4Key.wasPressedThisFrame && kb.shiftKey.isPressed) Cycle();
        }

        void LateUpdate()
        {
            var kind = Current;
            if (kind == null || runner == null || runner.World == null || cube == null || markers[0] == null) return;
            var w = runner.World;
            var s = w.Services;
            int safetyVersion = CityBuilder.Simulation.Safety.SafetyOverlays.IsSafety(kind) && w.Safety != null ? w.Safety.Version : 0; // Sicherheit
            if (s.CoverageVersion != shownCoverage || w.City.Version != shownCity || s.StructureVersion != shownStructure || kind != shownKind ||
                safetyVersion != shownSafety)
            {
                shownCoverage = s.CoverageVersion;
                shownCity = w.City.Version;
                shownStructure = s.StructureVersion;
                shownSafety = safetyVersion;
                shownKind = kind;
                if (CityBuilder.Simulation.Safety.SafetyOverlays.IsSafety(kind)) RebuildSafety(w, kind);
                else Rebuild(w.City, s, kind);
            }
            for (int i = 0; i < Buckets; i++)
            {
                Draw(markerLists[i], markers[i], true);
                Draw(plateLists[i], plates[i], false);
            }
            Draw(sourceList, sourceMarker, true);
        }

        void Rebuild(CitySystem city, ServiceSystem s, string kind)
        {
            foreach (var l in markerLists) l.Clear();
            foreach (var l in plateLists) l.Clear();
            sourceList.Clear();
            var rot45 = Quaternion.Euler(45f, 0f, 45f);
            bool garbage = kind == ServiceKinds.Garbage;
            foreach (var b in city.Buildings)
            {
                if (!b.IsConstructed) continue;
                float cov = s.Coverage(kind, b);
                int bucket = Mathf.Clamp(Mathf.RoundToInt(cov * (Buckets - 1)), 0, Buckets - 1);
                // Müll: Marker wächst mit dem liegenden Müll
                float size = garbage ? 2.5f + 3f * s.GarbageLevel(b) : 2.5f;
                var top = SimConvert.ToWorld(b.Position, b.Height + EstimatedHeight(b) + 4f + size);
                markerLists[bucket].Add(Matrix4x4.TRS(top, rot45, Vector3.one * size));
                var plate = SimConvert.ToWorld(b.Position, b.Height + 0.25f);
                plateLists[bucket].Add(Matrix4x4.TRS(plate, Quaternion.LookRotation(new Vector3(b.Front.X, 0f, b.Front.Y)),
                    new Vector3(b.Width * 0.95f, 0.3f, b.Depth * 0.95f)));
            }
            foreach (var f in s.Facilities)
                if (f.Kind == kind)
                    sourceList.Add(Matrix4x4.TRS(SimConvert.ToWorld(f.Position, f.Height + 28f), rot45, Vector3.one * 5f));
        }

        // --- Sicherheit: Ansichten „Brandgefahr“ und „Kriminalität“ (grün = gering → rot = hoch, Marker wächst mit dem Wert;
        // brennende Gebäude/Tatorte der letzten Stunden als große rote Marker, Feuerwachen/Polizei als blaue Quelle)
        void RebuildSafety(CityBuilder.Simulation.Core.SimulationWorld w, string kind)
        {
            foreach (var l in markerLists) l.Clear();
            foreach (var l in plateLists) l.Clear();
            sourceList.Clear();
            var safety = w.Safety;
            if (safety == null) return;
            bool fire = kind == CityBuilder.Simulation.Safety.SafetyOverlays.FireRisk;
            var rot45 = Quaternion.Euler(45f, 0f, 45f);
            foreach (var b in w.City.Buildings)
            {
                if (!b.IsConstructed) continue;
                // 0 = gut (grün) … 1 = schlecht (rot): Brandgefahr Faktor 0,5 → 0, 3 → 1; Kriminalität Rate 0 → 0, 0,45 → 1
                float bad = fire ? Mathf.Clamp01((safety.FireRiskOf(b).Factor - 0.5f) / 2.5f) : Mathf.Clamp01(safety.CrimeRate(b) / 0.45f);
                if (fire && safety.FireAt(b.Id) != null) bad = 1f;
                int bucket = Mathf.Clamp(Mathf.RoundToInt((1f - bad) * (Buckets - 1)), 0, Buckets - 1);
                float size = 2f + 3.5f * bad;
                var top = SimConvert.ToWorld(b.Position, b.Height + EstimatedHeight(b) + 4f + size);
                markerLists[bucket].Add(Matrix4x4.TRS(top, rot45, Vector3.one * size));
                plateLists[bucket].Add(Matrix4x4.TRS(SimConvert.ToWorld(b.Position, b.Height + 0.25f),
                    Quaternion.LookRotation(new Vector3(b.Front.X, 0f, b.Front.Y)), new Vector3(b.Width * 0.95f, 0.3f, b.Depth * 0.95f)));
            }
            if (!fire)
            {
                long since = w.Clock.Tick - 6 * CityBuilder.Simulation.Core.TimeModel.TicksPerHour;
                foreach (var c in safety.RecentCrimes)
                {
                    if (c.Tick < since) break;
                    markerLists[0].Add(Matrix4x4.TRS(SimConvert.ToWorld(c.Position, 34f), rot45, Vector3.one * 6.5f));
                }
            }
            string source = fire ? ServiceKinds.Fire : ServiceKinds.Police;
            foreach (var f in w.Services.Facilities)
                if (f.Kind == source) sourceList.Add(Matrix4x4.TRS(SimConvert.ToWorld(f.Position, f.Height + 28f), rot45, Vector3.one * 5f));
        }
        // --- Ende Sicherheit

        void Draw(List<Matrix4x4> list, Material material, bool shadows)
        {
            if (list.Count == 0) return;
            var rp = new RenderParams(material)
            {
                shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = shadows,
            };
            for (int start = 0; start < list.Count; start += buffer.Length)
            {
                int n = Mathf.Min(buffer.Length, list.Count - start);
                list.CopyTo(start, buffer, 0, n);
                Graphics.RenderMeshInstanced(rp, cube, 0, buffer, n);
            }
        }

        /// <summary>Ungefähre Gebäudehöhe je Zone (wie in der Versorgungs-Ansicht), damit der Marker darüber schwebt.</summary>
        static float EstimatedHeight(Building b)
        {
            switch (b.Zone)
            {
                case ZoneType.ResidentialLow: return 7f;
                case ZoneType.ResidentialMedium: return 16f;
                case ZoneType.ResidentialHigh: return 46f;
                case ZoneType.Mixed: return 18f;
                case ZoneType.Retail: return 9f;
                case ZoneType.Office: return 38f;
                default: return 12f;
            }
        }
    }
}
