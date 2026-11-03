using System.Collections.Generic;
using CityBuilder.Game.Assets;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.World;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Environment
{
    /// <summary>
    /// Eigene Assets: Büsche (<c>Models/Nature/Bush_*</c>) und Felsen (<c>Rock_*</c>) als Ergänzung zu den Bäumen des
    /// <see cref="ScenicPropsView"/>. Büsche wachsen am Waldrand (Waldraster der Simulation, mittlere Dichte), vereinzelt auf
    /// Wiesen und in Parks (G4); Felsen an Hängen nach Steigung und im Gebirge nach Höhe. Feste Verteilung aus einem Hash
    /// je Rasterzelle (gleich bei jedem Start), Straßen räumen sie weg, auf Gebäuden/Bauwerken/Wasser stehen keine.
    /// GPU-Instancing mit Entfernungs-LOD (Kacheln, Büsche bis 900 m, Felsen bis 1 600 m).
    /// </summary>
    public sealed class NatureDetailView : MonoBehaviour
    {
        const float Cell = 20f;

        SimulationRunner runner;
        PartsRenderer bushes, rocks;
        Camera cam;
        RoadNetwork boundRoads;

        struct Spot { public SimVector2 Pos; public float Scale, Yaw, Roll, Slope; public int Variant; public bool Rock, Alive; }
        Spot[] spots;
        bool dirty = true;
        int terrainVersion = -1, woodVersion = -1, cityVersion = -1, serviceVersion = -1;
        float lastRebuild = -10f;

        public void Init(SimulationRunner r)
        {
            runner = r;
            r.WorldReplaced += () => { Bind(); spots = null; dirty = true; }; // Speichern & Laden: neues Gelände
        }

        void Start() => Bind();

        void Bind()
        {
            if (boundRoads != null) boundRoads.SegmentAdded -= ClearAround;
            boundRoads = runner?.World?.Roads;
            if (boundRoads != null) boundRoads.SegmentAdded += ClearAround;
        }

        GridLayer Wood => runner.World.Resources?.Layer(ResourceIds.Wood);

        static uint Hash(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return h;
            }
        }

        static float H01(int x, int y, int salt) => (Hash(x, y, salt) & 0xFFFFFF) / (float)0xFFFFFF;

        /// <summary>Kandidaten einmal je Welt: Lage, Größe, Felsen oder Busch (Wald/Hang können sich später ändern → Filter beim Aufbau).</summary>
        void Scatter()
        {
            var w = runner.World;
            var terrain = w.Terrain;
            int n = Mathf.CeilToInt(w.Settings.SizeMeters / Cell);
            var list = new List<Spot>();
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = w.Min + new SimVector2((x + H01(x, y, 2)) * Cell, (y + H01(x, y, 3)) * Cell);
                float h = terrain.HeightAt(p);
                if (h < terrain.WaterLevel + 0.6f) continue;
                float dx = terrain.HeightAt(p + new SimVector2(4f, 0f)) - terrain.HeightAt(p - new SimVector2(4f, 0f));
                float dy = terrain.HeightAt(p + new SimVector2(0f, 4f)) - terrain.HeightAt(p - new SimVector2(0f, 4f));
                float slope = Mathf.Sqrt(dx * dx + dy * dy) / 8f;
                // Felsen: ab ~14° Steigung, mehr im Gebirge (> 40 m)
                float rockChance = Mathf.Clamp01((slope - 0.25f) * 2.5f) * 0.7f + Mathf.Clamp01((h - 40f) / 60f) * 0.25f;
                float roll = H01(x, y, 1);
                bool rock = roll < rockChance;
                list.Add(new Spot
                {
                    Pos = p, Rock = rock, Slope = slope, Roll = H01(x, y, 7),
                    Scale = 0.7f + H01(x, y, 4) * 0.7f, Yaw = H01(x, y, 5) * 360f, Variant = (int)(H01(x, y, 6) * 1000f), Alive = true,
                });
            }
            spots = list.ToArray();
            dirty = true;
        }

        void ClearAround(RoadSegment s)
        {
            if (spots == null) return;
            Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var lo, out var hi);
            float r = s.Type.HalfWidth + 3f;
            for (int i = 0; i < spots.Length; i++)
            {
                ref var t = ref spots[i];
                if (!t.Alive) continue;
                if (t.Pos.X < lo.X - r || t.Pos.Y < lo.Y - r || t.Pos.X > hi.X + r || t.Pos.Y > hi.Y + r) continue;
                if (s.IsCoveredAt(s.ClosestT(t.Pos))) continue;
                if (s.DistanceTo(t.Pos) < r) { t.Alive = false; dirty = true; }
            }
        }

        static ModelParts Pick(IReadOnlyList<OwnModelCatalog.Entry> list, int variant, bool large)
        {
            if (list.Count == 0) return null;
            // Felsen: meist „Large“ an steilen Hängen, sonst „Small“ (falls so benannt); Büsche/Rest nach Variante
            OwnModelCatalog.Entry best = null;
            foreach (var e in list)
            {
                bool isLarge = e.Id.IndexOf("Large", System.StringComparison.OrdinalIgnoreCase) >= 0;
                bool isSmall = e.Id.IndexOf("Small", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if ((large && isLarge) || (!large && isSmall)) { best = e; break; }
            }
            if (best != null && (variant & 3) != 0) return ModelParts.Of(best.Model);
            return ModelParts.Of(list[(variant & 0x7fffffff) % list.Count].Model);
        }

        void Rebuild()
        {
            var w = runner.World;
            var catalog = OwnModelCatalog.Instance;
            bushes.Clear();
            rocks.Clear();
            var bushList = catalog.WithRole(OwnModelRoles.Bush);
            var rockList = catalog.WithRole(OwnModelRoles.Rock);
            var wood = Wood;
            var terrain = w.Terrain;
            foreach (var s in spots)
            {
                if (!s.Alive) continue;
                float h = terrain.HeightAt(s.Pos);
                if (h < terrain.WaterLevel + 0.3f) continue;
                if (w.City.BuildingAt(s.Pos) != null || w.Services.FacilityAt(s.Pos, 2f) != null) continue;
                if (w.Utilities.FacilityAt(s.Pos) != null) continue;
                var at = SimConvert.ToWorld(s.Pos, h - 0.15f);
                var rot = Quaternion.Euler(0f, s.Yaw, 0f);
                if (s.Rock)
                {
                    if (rockList.Count == 0) continue;
                    rocks.Add(Pick(rockList, s.Variant, s.Slope > 0.5f), Matrix4x4.TRS(at, rot, Vector3.one * s.Scale));
                    continue;
                }
                if (bushList.Count == 0) continue;
                // Büsche: Waldrand (mittlere Walddichte) häufig, offene Wiese selten, dichter Wald nicht (Bäume stehen dort)
                float forest = wood != null ? wood.Sample(s.Pos) : 0.2f;
                float edge = Mathf.Clamp01(1f - Mathf.Abs(forest - 0.3f) / 0.3f);
                float chance = 0.55f * edge + 0.03f;
                if (s.Roll > chance) continue;
                bushes.Add(Pick(bushList, s.Variant, false), Matrix4x4.TRS(at, rot, Vector3.one * s.Scale));
            }

            // Parks (G4): Büsche entlang der Ränder, fest aus der Id
            if (bushList.Count > 0)
                foreach (var f in w.Services.Facilities)
                {
                    if (f.Kind != ServiceKinds.Park) continue;
                    int count = Mathf.Clamp(Mathf.RoundToInt((f.Type.Width + f.Type.Depth) / 6f), 4, 24);
                    for (int i = 0; i < count; i++)
                    {
                        float u = H01(f.Id, i, 11), v = H01(f.Id, i, 12);
                        // auf einem Ring 85–95 % der halben Abmessung (nicht auf den Wegen in der Mitte)
                        bool alongWidth = (Hash(f.Id, i, 13) & 1) == 0;
                        float sx = alongWidth ? (u - 0.5f) * 0.9f : ((Hash(f.Id, i, 14) & 1) == 0 ? -0.45f : 0.45f);
                        float sz = alongWidth ? ((Hash(f.Id, i, 15) & 1) == 0 ? -0.45f : 0.45f) : (v - 0.5f) * 0.9f;
                        var p = f.Position + f.Side * (sx * f.Type.Width) + f.Front * (sz * f.Type.Depth);
                        var at = SimConvert.ToWorld(p, f.Height + 0.25f);
                        bushes.Add(Pick(bushList, (int)Hash(f.Id, i, 16), false),
                            Matrix4x4.TRS(at, Quaternion.Euler(0f, H01(f.Id, i, 17) * 360f, 0f), Vector3.one * (0.8f + 0.4f * v)));
                    }
                }
        }

        void LateUpdate()
        {
            if (runner == null || runner.World == null) return;
            var catalog = OwnModelCatalog.Instance;
            if (catalog == null || catalog.Palette == null) return;
            if (catalog.WithRole(OwnModelRoles.Bush).Count == 0 && catalog.WithRole(OwnModelRoles.Rock).Count == 0) return;
            if (bushes == null)
            {
                bushes = new PartsRenderer(catalog.Palette, 256f, 900f);
                rocks = new PartsRenderer(catalog.Palette, 256f, 1600f);
            }
            if (cam == null) cam = Camera.main;
            var w = runner.World;
            if (spots == null) Scatter();
            var wood = Wood;
            bool changed = dirty || w.City.Version != cityVersion || w.Services.StructureVersion != serviceVersion;
            // Gelände/Wald höchstens 1× je Sekunde nachziehen (Terraforming-Pinsel, Holzeinschlag)
            if (Time.unscaledTime - lastRebuild > 1f && (w.Terrain.Version != terrainVersion || (wood != null && wood.Version != woodVersion)))
                changed = true;
            if (changed && Time.unscaledTime - lastRebuild > 0.25f)
            {
                dirty = false;
                lastRebuild = Time.unscaledTime;
                terrainVersion = w.Terrain.Version;
                woodVersion = wood != null ? wood.Version : -1;
                cityVersion = w.City.Version;
                serviceVersion = w.Services.StructureVersion;
                Rebuild();
            }
            bushes.Render(cam);
            rocks.Render(cam);
        }
    }
}
