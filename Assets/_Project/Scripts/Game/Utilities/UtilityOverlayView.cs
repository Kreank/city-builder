using System.Collections.Generic;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.Utilities
{
    /// <summary>
    /// Info-Ansicht Strom, Wasser bzw. Abwasser (O schaltet Strom → Wasser → Abwasser → aus): über jedem fertigen Gebäude ein Marker,
    /// grün = versorgt, rot = unversorgt (je länger unversorgt, desto größer), dazu eine farbige Bodenplatte.
    /// Bauwerke der gewählten Art bekommen einen blauen Marker. Liest nur die Simulation.
    /// </summary>
    public sealed class UtilityOverlayView : MonoBehaviour
    {
        public enum Overlay { None, Power, Water, Sewage } // Sewage: Abwasser (G3 erweitert)

        static readonly Color Ok = new Color(0.2f, 0.9f, 0.3f);
        static readonly Color Bad = new Color(0.95f, 0.15f, 0.1f);
        static readonly Color Source = new Color(0.25f, 0.6f, 1f);

        SimulationRunner runner;
        Material okMarker, badMarker, sourceMarker, okPlate, badPlate;
        Mesh cube;
        readonly List<Matrix4x4> okList = new List<Matrix4x4>(), badList = new List<Matrix4x4>(), sourceList = new List<Matrix4x4>();
        readonly List<Matrix4x4> okPlates = new List<Matrix4x4>(), badPlates = new List<Matrix4x4>();
        readonly Matrix4x4[] buffer = new Matrix4x4[1023];
        int shownStatus = -1, shownCity = -1, shownStructure = -1;
        Overlay shownOverlay;

        public Overlay Current { get; private set; } = Overlay.None;
        public UtilityKind Kind => Current == Overlay.Water ? UtilityKind.Water : Current == Overlay.Sewage ? UtilityKind.Sewage : UtilityKind.Power;

        public void Init(SimulationRunner r, Material lit, Material transparent)
        {
            runner = r;
            okMarker = Colored(lit, Ok, "UtilityOk");
            badMarker = Colored(lit, Bad, "UtilityBad");
            sourceMarker = Colored(lit, Source, "UtilitySource");
            okPlate = Colored(transparent, new Color(Ok.r, Ok.g, Ok.b, 0.45f), "UtilityOkPlate");
            badPlate = Colored(transparent, new Color(Bad.r, Bad.g, Bad.b, 0.5f), "UtilityBadPlate");
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
            foreach (var m in new[] { okMarker, badMarker, sourceMarker, okPlate, badPlate }) if (m != null) Destroy(m);
        }

        public void Toggle(Overlay overlay) => Current = Current == overlay ? Overlay.None : overlay;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.oKey.wasPressedThisFrame)
                Current = Current == Overlay.None ? Overlay.Power : Current == Overlay.Power ? Overlay.Water : Current == Overlay.Water ? Overlay.Sewage : Overlay.None;
        }

        void LateUpdate()
        {
            if (Current == Overlay.None || runner == null || runner.World == null || cube == null) return;
            var w = runner.World;
            var u = w.Utilities;
            if (u.StatusVersion != shownStatus || w.City.Version != shownCity || u.StructureVersion != shownStructure || Current != shownOverlay)
            {
                shownStatus = u.StatusVersion;
                shownCity = w.City.Version;
                shownStructure = u.StructureVersion;
                shownOverlay = Current;
                Rebuild(w.City, u);
            }
            Draw(okList, okMarker, true);
            Draw(badList, badMarker, true);
            Draw(sourceList, sourceMarker, true);
            Draw(okPlates, okPlate, false);
            Draw(badPlates, badPlate, false);
        }

        void Rebuild(CitySystem city, UtilitySystem u)
        {
            okList.Clear(); badList.Clear(); sourceList.Clear(); okPlates.Clear(); badPlates.Clear();
            var kind = Kind;
            var rot45 = Quaternion.Euler(45f, 0f, 45f);
            foreach (var b in city.Buildings)
            {
                if (!b.IsConstructed) continue;
                var s = u.GetStatus(b);
                bool ok = s.Has(kind);
                // Unversorgt: Marker wächst mit der Dauer (bis zum Verlassen)
                float size = ok ? 2.5f : 2.5f + 2.5f * Mathf.Clamp01(s.TicksWithout(kind) / (float)(kind == UtilityKind.Sewage ? UtilitySystem.SewageAbandonAfterTicks : UtilitySystem.AbandonAfterTicks));
                var top = SimConvert.ToWorld(b.Position, b.Height + EstimatedHeight(b) + 4f + size);
                (ok ? okList : badList).Add(Matrix4x4.TRS(top, rot45, Vector3.one * size));
                var plate = SimConvert.ToWorld(b.Position, b.Height + 0.25f);
                var front = new Vector3(b.Front.X, 0f, b.Front.Y);
                (ok ? okPlates : badPlates).Add(Matrix4x4.TRS(plate, Quaternion.LookRotation(front), new Vector3(b.Width * 0.95f, 0.3f, b.Depth * 0.95f)));
            }
            foreach (var f in u.Facilities)
            {
                if (f.Kind != kind) continue;
                sourceList.Add(Matrix4x4.TRS(SimConvert.ToWorld(f.Position, f.Height + 30f), rot45, Vector3.one * 5f));
            }
        }

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

        /// <summary>Ungefähre Gebäudehöhe je Zone (wie die Platzhalter in BuildingView), damit der Marker darüber schwebt.</summary>
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
