using System;
using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Environment;
using CityBuilder.Simulation.Roads;
using Xunit;
using Xunit.Abstractions;

namespace CityBuilder.Simulation.Tests
{
    /// <summary>Automatische Trassierung ebenerdiger Straßen: einebnen, untertunneln, überbrücken.</summary>
    public class ProfileTests
    {
        readonly ITestOutputHelper output;
        public ProfileTests(ITestOutputHelper output) => this.output = output;

        /// <summary>Flach auf 10 m mit einer Erhebung (Gauß) der Höhe <paramref name="bump"/> bei x = 0.</summary>
        static TerrainMap Bump(float bump, float width = 60f)
        {
            var t = TerrainMap.Flat(new Vector2(-1024), 2048f, 4f, 10f);
            var field = typeof(TerrainMap).GetField("current", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var nat = typeof(TerrainMap).GetField("natural", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var cur = (float[])field.GetValue(t);
            var n = (float[])nat.GetValue(t);
            for (int y = 0; y < t.Resolution; y++)
            for (int x = 0; x < t.Resolution; x++)
            {
                var p = t.PointAt(x, y);
                float h = 10f + bump * MathF.Exp(-(p.X * p.X) / (2f * width * width));
                cur[y * t.Resolution + x] = n[y * t.Resolution + x] = h;
            }
            return t;
        }

        static (RoadNetwork net, TerrainMap terrain, RoadSegment[] segs) BuildAcross(TerrainMap terrain, float halfLength = 300f)
        {
            var net = new RoadNetwork { Ground = terrain };
            net.SegmentAdded += terrain.ConformTo;
            var a = new Vector2(-halfLength, 0);
            var b = new Vector2(halfLength, 0);
            var plan = RoadBuilder.Plan(net, a, 0f, null, b, 0f, RoadCatalog.CityTwoLane, terrain.HeightAt(a), terrain.HeightAt(b), followTerrain: true);
            Assert.True(plan.IsValid, plan.Error.ToString());
            return (net, terrain, RoadBuilder.Build(net, plan).ToArray());
        }

        [Fact]
        public void GentleHill_RoadFollowsAndTerrainIsLeveled()
        {
            var (net, terrain, segs) = BuildAcross(Bump(5f));
            var s = segs.Single();
            Assert.True(s.FollowsTerrain);
            Assert.InRange(s.HeightAt(0.5f), 12f, 15.5f);          // fährt über die Kuppe (geglättet)
            // Gelände unter der Straße liegt auf Fahrbahnhöhe – nirgends zugedeckt, keine Brücke
            for (int i = 0; i <= 20; i++)
            {
                float e = s.ElevationAt(i / 20f);
                Assert.InRange(e, -0.3f, 0.6f);
            }
            Assert.True(s.MaxSlope <= RoadCatalog.CityTwoLane.MaxSlope + 1e-3f);
        }

        [Fact]
        public void Mountain_IsTunneled()
        {
            var (net, terrain, segs) = BuildAcross(Bump(45f));
            var s = segs.Single();
            output.WriteLine($"Mitte: Straße {s.HeightAt(0.5f):0.0} m, Gelände {terrain.HeightAt(s.Evaluate(0.5f)):0.0} m");
            Assert.True(s.IsCoveredAt(0.5f), "Berg in der Mitte sollte untertunnelt sein");
            Assert.InRange(s.HeightAt(0.5f), 9f, 16f);              // bleibt nahe der direkten Linie
            Assert.True(s.ElevationAt(0.02f) > -1f);                // an den Enden ebenerdig
        }

        [Fact]
        public void Valley_IsBridged()
        {
            var (net, terrain, segs) = BuildAcross(Bump(-25f));
            var s = segs.Single();
            output.WriteLine($"Mitte: Straße {s.HeightAt(0.5f):0.0} m, Gelände {terrain.HeightAt(s.Evaluate(0.5f)):0.0} m");
            Assert.True(s.ElevationAt(0.5f) > 10f, "Tal in der Mitte sollte überbrückt sein");
            Assert.True(s.IsElevated);
        }

        [Fact]
        public void Split_KeepsProfile()
        {
            var (net, terrain, segs) = BuildAcross(Bump(5f));
            var s = segs.Single();
            float mid = s.HeightAt(0.25f);
            net.SplitSegment(s, 0.5f);
            var left = net.Segments.First(x => x.Start.Position.X < -100f);
            Assert.True(left.FollowsTerrain);
            Assert.Equal(mid, left.HeightAt(0.5f), 1);
        }

        [Fact]
        public void SteepMountainRoad_TooSteepWhenEndsUnreachable()
        {
            var net = new RoadNetwork { Ground = TerrainMap.Flat(new Vector2(-1024), 2048f, 4f, 0f) };
            // 20 m Höhenunterschied auf 100 m = 20 % > 10 %
            var plan = RoadBuilder.Plan(net, new Vector2(0, 0), 0f, null, new Vector2(100, 0), 0f, RoadCatalog.CityTwoLane, 0f, 20f, followTerrain: true);
            Assert.Equal(RoadBuildError.TooSteep, plan.Error);
        }
    }
}
