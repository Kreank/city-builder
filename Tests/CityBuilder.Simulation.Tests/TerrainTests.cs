using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using Xunit;

namespace CityBuilder.Simulation.Tests
{
    public class TerrainTests
    {
        static SimulationWorld HillWorld()
        {
            var world = new SimulationWorld(new WorldSettings { SizeMeters = 1024f });
            return world;
        }

        [Fact]
        public void GeneratedTerrain_HasHillsAndWater_AndIsDeterministic()
        {
            var a = TerrainMap.Generate(new Vector2(-1024), 2048f, 8f, 42);
            var b = TerrainMap.Generate(new Vector2(-1024), 2048f, 8f, 42);
            float min = float.MaxValue, max = float.MinValue;
            int water = 0;
            for (int y = 0; y < a.Resolution; y++)
            for (int x = 0; x < a.Resolution; x++)
            {
                Assert.Equal(a[x, y], b[x, y]);
                min = System.MathF.Min(min, a[x, y]);
                max = System.MathF.Max(max, a[x, y]);
                if (a[x, y] < a.WaterLevel) water++;
            }
            Assert.True(max > 30f, $"zu flach: {max}");
            Assert.True(min < 0f, "kein Fluss");
            Assert.True(water > 100);
        }

        [Fact]
        public void AtGradeRoad_ShapesTerrain_ButBridgeDoesNot()
        {
            var world = HillWorld();
            var t = world.Terrain;
            // künstlicher Hügel: Gelände auf 3 m anheben, Straße auf 0 m → Einschnitt
            var hilly = TerrainMap.Flat(world.Min, 1024f, 4f, 3f);
            world.Roads.Ground = hilly;
            world.Roads.SegmentAdded += hilly.ConformTo;

            RoadBuilder.Build(world.Roads, RoadBuilder.Plan(world.Roads, new Vector2(-100, 0), 0f, null, new Vector2(100, 0), 0f,
                RoadCatalog.CityTwoLane, 0f, 0f));
            Assert.InRange(hilly.HeightAt(new Vector2(0, 0)), -0.1f, 0.05f);   // unter der Straße abgetragen
            Assert.InRange(hilly.HeightAt(new Vector2(0, 40)), 2.9f, 3.01f);  // weit weg unverändert

            // Brücke 10 m über Gelände: keine Anpassung
            RoadBuilder.Build(world.Roads, RoadBuilder.Plan(world.Roads, new Vector2(-100, 200), 0f, null, new Vector2(100, 200), 0f,
                RoadCatalog.CityTwoLane, 13f, 13f));
            Assert.InRange(hilly.HeightAt(new Vector2(0, 200)), 2.99f, 3.01f);
        }

        [Fact]
        public void Water_RequiresBridge()
        {
            var net = new RoadNetwork();
            var lake = TerrainMap.Flat(new Vector2(-512), 1024f, 4f, 5f);
            lake.WaterLevel = 0f;
            // See in der Mitte: Gelände auf −3 m
            var flatLake = new LakeGround(lake);
            net.Ground = flatLake;

            // knapp über dem Wasserspiegel = durchs Wasser
            var atGrade = RoadBuilder.Plan(net, new Vector2(-200, 0), 0f, null, new Vector2(200, 0), 0f, RoadCatalog.CityTwoLane, 1f, 1f);
            Assert.Equal(RoadBuildError.OverWater, atGrade.Error);
            var bridge = RoadBuilder.Plan(net, new Vector2(-200, 0), 0f, null, new Vector2(200, 0), 0f, RoadCatalog.CityTwoLane, 8f, 8f);
            Assert.True(bridge.IsValid, bridge.Error.ToString());
        }

        [Fact]
        public void Elevation_IsRelativeToTerrain()
        {
            var net = new RoadNetwork { Ground = TerrainMap.Flat(new Vector2(-512), 1024f, 4f, 20f) };
            var plan = RoadBuilder.Plan(net, new Vector2(0, 0), 0f, null, new Vector2(100, 0), 0f, RoadCatalog.CityTwoLane, 20f, 20f);
            Assert.True(plan.IsValid);
            var seg = RoadBuilder.Build(net, plan).Single();
            Assert.False(seg.IsElevated);   // 20 m absolut, aber auf dem Gelände
            Assert.False(seg.IsUnderground);
            // 30 m unter dem Gelände ist erlaubt, 40 m nicht
            Assert.True(RoadBuilder.Plan(net, new Vector2(0, 300), 0f, null, new Vector2(400, 300), 0f, RoadCatalog.CityTwoLane, -10f, -10f).IsValid);
            Assert.Equal(RoadBuildError.InvalidHeight,
                RoadBuilder.Plan(net, new Vector2(0, 500), 0f, null, new Vector2(400, 500), 0f, RoadCatalog.CityTwoLane, -21f, -21f).Error);
        }

        /// <summary>Gelände mit einem See (|x| &lt; 60 m auf −3 m).</summary>
        sealed class LakeGround : IGround
        {
            readonly TerrainMap baseMap;
            public LakeGround(TerrainMap m) => baseMap = m;
            public float HeightAt(Vector2 p) => System.MathF.Abs(p.X) < 60f ? -3f : baseMap.HeightAt(p);
            public float WaterLevel => 0f;
            public bool IsWater(Vector2 p) => HeightAt(p) < WaterLevel - 0.1f;
        }
    }
}
