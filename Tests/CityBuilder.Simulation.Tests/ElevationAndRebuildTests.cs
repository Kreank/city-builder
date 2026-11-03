using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Roads;
using Xunit;
using static CityBuilder.Simulation.Tests.RoadNetworkTests;

namespace CityBuilder.Simulation.Tests
{
    public class ElevationAndRebuildTests
    {
        static RoadPlan PlanH(RoadNetwork net, Vector2 a, float ha, Vector2 b, float hb, RoadType type = null) =>
            RoadBuilder.Plan(net, a, 8f, null, b, 8f, type ?? RoadCatalog.CityTwoLane, ha, hb);

        [Fact]
        public void Bridge_OverRoad_IsGradeSeparated()
        {
            var net = new RoadNetwork();
            Build(net, new Vector2(-200, 0), new Vector2(200, 0));

            RoadBuilder.Build(net, PlanH(net, new Vector2(0, -200), 0f, new Vector2(0, -100), 8f));
            var bridge = PlanH(net, new Vector2(0, -100), 8f, new Vector2(0, 100), 8f);
            Assert.True(bridge.IsValid, bridge.Error.ToString());
            Assert.Empty(bridge.Crossings);
            Assert.Equal(1, bridge.GradeSeparations);

            RoadBuilder.Build(net, bridge);
            Assert.Equal(1, net.Segments.Count(s => s.Start.Position.Y == 0 || s.End.Position.Y == 0)); // Straße unten nicht zerteilt
        }

        [Fact]
        public void LowBridge_FailsClearance_AndSteepRampIsRejected()
        {
            var net = new RoadNetwork();
            Build(net, new Vector2(-200, 0), new Vector2(200, 0));
            Assert.Equal(RoadBuildError.InsufficientClearance,
                PlanH(net, new Vector2(0, -100), 3f, new Vector2(0, 100), 3f).Error);
            Assert.Equal(RoadBuildError.TooSteep,
                PlanH(net, new Vector2(500, 0), 0f, new Vector2(500, 50), 10f).Error);
            Assert.Equal(RoadBuildError.TooSteep, // Autobahn max. 6 %
                PlanH(net, new Vector2(800, 0), 0f, new Vector2(800, 100), 8f, RoadCatalog.Highway2).Error);
        }

        [Fact]
        public void Tunnel_UnderRoad_IsGradeSeparated_SilentAndExpensive()
        {
            var world = SimulationWorld.CreateDefault();
            var net = world.Roads;
            Build(net, new Vector2(-300, 0), new Vector2(300, 0));

            // Rampe hinunter (8 %), Tunnel unter der Straße, Rampe wieder hinauf
            RoadBuilder.Build(net, PlanH(net, new Vector2(0, -300), 0f, new Vector2(0, -200), -8f));
            var tunnel = PlanH(net, new Vector2(0, -200), -8f, new Vector2(0, 200), -8f);
            Assert.True(tunnel.IsValid, tunnel.Error.ToString());
            Assert.Empty(tunnel.Crossings);
            Assert.Equal(1, tunnel.GradeSeparations);
            var ground = PlanH(net, new Vector2(500, -200), 0f, new Vector2(500, 200), 0f);
            Assert.True(tunnel.Cost > ground.Cost * 2f);
            RoadBuilder.Build(net, tunnel);
            RoadBuilder.Build(net, PlanH(net, new Vector2(0, 200), -8f, new Vector2(0, 300), 0f));

            var tunnelSeg = net.Segments.Single(s => s.Start.Height < -7f && s.End.Height < -7f);
            Assert.True(tunnelSeg.IsUnderground);
            Assert.True(tunnelSeg.IsCoveredAt(0.5f));

            for (int i = 0; i < 10; i++) world.Step();
            Assert.Equal(0f, world.Environment.Noise.Sample(new Vector2(0, -120)), 3); // nur Tunnel in der Nähe

            // Verkehr fährt durch den Tunnel
            for (int i = 0; i < 3000; i++) world.Step();
            var st = world.Traffic.Stats;
            Assert.True(st.Arrived > st.Spawned * 0.6f, $"{st.Arrived}/{st.Spawned}");
            Assert.Equal(0, st.Despawned);
        }

        [Fact]
        public void Height_BelowMinimum_IsRejected()
        {
            var net = new RoadNetwork();
            Assert.Equal(RoadBuildError.InvalidHeight,
                PlanH(net, new Vector2(0, 0), -40f, new Vector2(500, 0), -40f).Error);
        }

        [Fact]
        public void Anchors_OnlySnapOnSameLevel()
        {
            var net = new RoadNetwork();
            Build(net, new Vector2(-200, 0), new Vector2(200, 0));
            var high = RoadBuilder.ResolveAnchor(net, new Vector2(50, 0), 10f, 10f);
            Assert.Equal(AnchorKind.Free, high.Kind);
            var ground = RoadBuilder.ResolveAnchor(net, new Vector2(50, 0), 10f, 0f);
            Assert.Equal(AnchorKind.Segment, ground.Kind);
        }

        [Fact]
        public void Split_InterpolatesHeight()
        {
            var net = new RoadNetwork();
            var seg = RoadBuilder.Build(net, PlanH(net, new Vector2(0, 0), 0f, new Vector2(100, 0), 8f))[0];
            var node = net.SplitSegment(seg, 0.5f);
            Assert.Equal(4f, node.Height, 2);
        }

        [Fact]
        public void Replace_And_Reverse()
        {
            var net = new RoadNetwork();
            var seg = Build(net, new Vector2(0, 0), new Vector2(100, 0), RoadCatalog.CityOneWay2)[0];
            Build(net, new Vector2(100, 0), new Vector2(200, 50));

            var upgraded = RoadBuilder.Replace(net, seg, RoadCatalog.CityFourLane);
            Assert.NotNull(upgraded);
            Assert.Same(RoadCatalog.CityFourLane, upgraded.Type);
            Assert.Equal(RoadBuildError.IncompatibleConnection, RoadBuilder.CheckReplace(net, upgraded, RoadCatalog.Highway2));

            var reversed = net.Reverse(upgraded);
            Assert.Equal(new Vector2(100, 0), reversed.Start.Position);
            Assert.Equal(new Vector2(0, 0), reversed.End.Position);
            Assert.Equal(2, net.SegmentCount);
        }

        [Fact]
        public void DualCarriageway_BuildsTwoOppositeOneWays()
        {
            var net = new RoadNetwork();
            var plan = RoadBuilder.PlanDualCarriageway(net, new Vector2(0, 0), null, new Vector2(400, 0), RoadCatalog.Highway3, 8f);
            Assert.True(plan.IsValid, plan.Error.ToString());
            var built = RoadBuilder.BuildDualCarriageway(net, plan, 8f);
            Assert.Equal(2, built.Count);
            var right = built.Single(s => s.Start.Position.X < s.End.Position.X);
            var left = built.Single(s => s.Start.Position.X > s.End.Position.X);
            Assert.True(right.Start.Position.Y < 0f);  // Rechtsverkehr: +x-Fahrbahn liegt rechts (−y)
            Assert.True(left.Start.Position.Y > 0f);
        }

        [Theory]
        [InlineData(RoadTemplateKind.DiamondInterchange, 6 + 3 + 4)]
        [InlineData(RoadTemplateKind.Cloverleaf, 20 + 4 * 5 + 4 * 2)]
        [InlineData(RoadTemplateKind.Triangle, 3 + 3 + 2 + 2 + 2 * 5 + 2 * 2)]
        public void Templates_BuildCompletelyOnEmptyMap(RoadTemplateKind kind, int expectedPieces)
        {
            var net = new RoadNetwork();
            var plan = RoadTemplates.Plan(net, kind, new Vector2(10, -20), 30f);
            Assert.True(plan.IsValid, plan.Error.ToString());
            Assert.Equal(expectedPieces, plan.Pieces.Count);
            var built = RoadTemplates.Build(net, plan);
            Assert.Equal(expectedPieces, built.Count);
            Assert.All(net.Segments, s => Assert.True(s.Slope <= s.Type.MaxSlope + 1e-3f, $"{s} zu steil: {s.Slope:P1}"));
        }
    }
}
