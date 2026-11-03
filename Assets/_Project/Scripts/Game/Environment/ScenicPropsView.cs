using System.Collections.Generic;
using CityBuilder.Game.Assets;
using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Roads;
using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Environment
{
    /// <summary>
    /// Deko aus dem Asset-Katalog: Straßenlaternen auf den Gehwegen der Stadtstraßen und Bäume
    /// in der Landschaft (werden beim Straßenbau entfernt). Alles per GPU-Instancing.
    /// </summary>
    public sealed class ScenicPropsView : MonoBehaviour
    {
        const float LampSpacing = 32f;
        const float TreeCell = 14f;

        SimulationRunner runner;
        readonly InstancedModels lamps = new InstancedModels();
        readonly InstancedModels trees = new InstancedModels();
        int roadsVersion = -1, terrainVersion = -1;

        struct Tree { public SimVector2 Pos; public float Scale, Yaw; public int Variant; public bool Alive; }
        Tree[] treeList;
        bool treesDirty = true;

        public void Init(SimulationRunner r) => runner = r;

        void Start()
        {
            var catalog = AssetCatalog.Instance;
            if (runner == null || catalog == null) { enabled = false; return; }
            if (AssetCatalog.Has(catalog.Trees)) ScatterTrees();
            runner.World.Roads.SegmentAdded += ClearTreesAround;
        }

        // ------------------------------------------------------------ Bäume

        void ScatterTrees()
        {
            var terrain = runner.World.Terrain;
            var min = runner.World.Min;
            float size = runner.World.Settings.SizeMeters;
            int n = Mathf.CeilToInt(size / TreeCell);
            var list = new List<Tree>();
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Waldflecken: niederfrequentes Rauschen entscheidet über die Dichte
                float density = Mathf.PerlinNoise(x * 0.045f + 13.7f, y * 0.045f + 7.3f);
                float roll = Hash(x, y, 1);
                if (roll > (density - 0.35f) * 1.6f) continue;

                var p = min + new SimVector2((x + Hash(x, y, 2)) * TreeCell, (y + Hash(x, y, 3)) * TreeCell);
                if (terrain.HeightAt(p) < terrain.WaterLevel + 0.8f) continue;
                if (p.Length() < 180f) continue; // Startgebiet frei lassen
                list.Add(new Tree
                {
                    Pos = p,
                    Scale = 0.7f + Hash(x, y, 4) * 0.6f,
                    Yaw = Hash(x, y, 5) * 360f,
                    Variant = (int)(Hash(x, y, 6) * 1000f),
                    Alive = true,
                });
            }
            treeList = list.ToArray();
            treesDirty = true;
        }

        void ClearTreesAround(RoadSegment s)
        {
            if (treeList == null) return;
            Bezier.Bounds(s.Start.Position, s.Control, s.End.Position, out var bmin, out var bmax);
            float r = s.Type.HalfWidth + 4f;
            for (int i = 0; i < treeList.Length; i++)
            {
                ref var t = ref treeList[i];
                if (!t.Alive) continue;
                if (t.Pos.X < bmin.X - r || t.Pos.Y < bmin.Y - r || t.Pos.X > bmax.X + r || t.Pos.Y > bmax.Y + r) continue;
                if (s.IsCoveredAt(s.ClosestT(t.Pos))) continue; // über Tunneln bleibt der Wald
                if (s.DistanceTo(t.Pos) < r) { t.Alive = false; treesDirty = true; }
            }
        }

        void RebuildTrees()
        {
            trees.Clear();
            var catalog = AssetCatalog.Instance;
            var terrain = runner.World.Terrain;
            foreach (var t in treeList)
            {
                if (!t.Alive) continue;
                var model = AssetCatalog.Pick(catalog.Trees, t.Variant);
                var pos = SimConvert.ToWorld(t.Pos, terrain.HeightAt(t.Pos) - 0.1f);
                trees.Add(model, Matrix4x4.TRS(pos, Quaternion.Euler(0f, t.Yaw, 0f), Vector3.one * t.Scale));
            }
        }

        // ------------------------------------------------------------ Laternen

        void RebuildLamps()
        {
            lamps.Clear();
            var catalog = AssetCatalog.Instance;
            if (!AssetCatalog.Has(catalog.StreetLamps)) return;
            var roads = runner.World.Roads;
            int k = 0;
            foreach (var s in roads.Segments)
            {
                if (s.Type.Category != RoadCategory.City) continue;
                float trim0 = RoadMeshBuilder.JunctionTrim(roads, s.Start, s);
                float trim1 = RoadMeshBuilder.JunctionTrim(roads, s.End, s);
                foreach (var lane in s.Type.Lanes)
                {
                    if (lane.Kind != LaneKind.Sidewalk) continue;
                    bool rightSide = lane.Offset > 0f;
                    float offset = rightSide ? lane.Right - 0.7f : lane.Left + 0.7f;
                    // versetzt auf beiden Seiten
                    for (float d = trim0 + 6f + (rightSide ? 0f : LampSpacing * 0.5f); d < s.Length - trim1 - 6f; d += LampSpacing)
                    {
                        float t = d / s.Length;
                        if (s.IsCoveredAt(t)) continue;
                        var p = s.Evaluate(t) + s.Right(t) * offset;
                        var toRoad = rightSide ? -s.Right(t) : s.Right(t);
                        var pos = SimConvert.ToWorld(p, s.HeightAt(t) + RoadMeshBuilder.RoadHeight + 0.15f);
                        var rot = Quaternion.LookRotation(new Vector3(toRoad.X, 0f, toRoad.Y));
                        lamps.Add(AssetCatalog.Pick(catalog.StreetLamps, k++ / 7), Matrix4x4.TRS(pos, rot, Vector3.one));
                    }
                }
            }
        }

        // ------------------------------------------------------------ Zeichnen

        void LateUpdate()
        {
            var world = runner.World;
            if (world.Roads.Version != roadsVersion)
            {
                roadsVersion = world.Roads.Version;
                RebuildLamps();
            }
            if (treeList != null && (treesDirty || world.Terrain.Version != terrainVersion))
            {
                treesDirty = false;
                terrainVersion = world.Terrain.Version;
                RebuildTrees();
            }
            lamps.Render();
            trees.Render();
        }

        static float Hash(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093 ^ y * 19349663 ^ salt * 83492791);
                h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }
    }
}
