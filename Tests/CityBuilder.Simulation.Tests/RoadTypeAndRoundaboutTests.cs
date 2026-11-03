using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using Xunit;
using static CityBuilder.Simulation.Tests.RoadNetworkTests;

namespace CityBuilder.Simulation.Tests
{
    public class RoadTypeAndRoundaboutTests
    {
        [Fact]
        public void Catalog_LanesAreLaidOutLeftToRight()
        {
            var t = RoadCatalog.CityTwoLane;
            Assert.Equal(13f, t.Width, 3);
            Assert.False(t.IsOneWay);
            Assert.Equal(1, t.CarLanesForward);
            Assert.Equal(1, t.CarLanesBackward);
            // Rechtsverkehr: Vorwärtsspur rechts von der Mitte
            var fwd = t.Lanes.Single(l => l.Kind == LaneKind.Car && l.Direction == LaneDirection.Forward);
            Assert.True(fwd.Offset > 0f);
            Assert.Equal(3.5f, t.CarriagewayHalfWidth, 3);
        }

        [Fact]
        public void Catalog_FlagsAreConsistent()
        {
            Assert.True(RoadCatalog.CityOneWay2.IsOneWay);
            Assert.True(RoadCatalog.Highway3.IsOneWay);
            Assert.True(RoadCatalog.Ramp.IsOneWay);
            Assert.True(RoadCatalog.CityTram.HasTram);
            Assert.True(RoadCatalog.CityBusLanes.HasBusLanes);
            Assert.Equal(3, RoadCatalog.Highway3.CarLanesForward);
            Assert.All(RoadCatalog.All, t => Assert.Same(t, RoadCatalog.Get(t.Id)));
            Assert.Equal(RoadCatalog.All.Length, RoadCatalog.All.Select(t => t.Id).Distinct().Count());
        }

        [Fact]
        public void Roundabout_OnEmptyMap_BuildsCounterClockwiseRing()
        {
            var net = new RoadNetwork();
            var plan = RoadBuilder.PlanRoundabout(net, Vector2.Zero, 30f, RoadCatalog.DefaultRoundabout);
            Assert.True(plan.IsValid, plan.Error.ToString());

            var ring = RoadBuilder.BuildRoundabout(net, plan);
            int arcs = RoadBuilder.RoundaboutArcCount(30f);
            Assert.Equal(arcs, ring.Count);
            Assert.Equal(arcs, net.NodeCount);
            Assert.All(net.Nodes, n => Assert.Equal(2, n.SegmentIds.Count));

            foreach (var s in ring)
            {
                // Gegen den Uhrzeigersinn: Tangente zeigt links vom Radius-Vektor
                var radial = s.Evaluate(0.5f);
                var tangent = s.Tangent(0.5f);
                Assert.True(RoadGeometry.SignedAngle(radial, tangent) > 0f);
                Assert.InRange(radial.Length(), 29.5f, 30.8f);
            }
        }

        [Fact]
        public void Roundabout_OnCrossroad_ConnectsArmsAndClearsCenter()
        {
            var net = new RoadNetwork();
            Build(net, new Vector2(-150, 0), new Vector2(150, 0));
            Build(net, new Vector2(0, -150), new Vector2(0, 150));
            Assert.Equal(4, net.SegmentCount);

            var plan = RoadBuilder.PlanRoundabout(net, Vector2.Zero, 30f, RoadCatalog.DefaultRoundabout);
            Assert.True(plan.IsValid, plan.Error.ToString());
            RoadBuilder.BuildRoundabout(net, plan);

            Assert.Null(net.FindNearestNode(Vector2.Zero, 25f));            // alte Kreuzung weg
            var ringSegments = net.Segments.Where(s => System.MathF.Abs(s.Evaluate(0.5f).Length() - 30f) < 1.5f).ToList();
            Assert.Equal(4, net.SegmentCount - ringSegments.Count);          // genau die 4 Arme bleiben übrig
            Assert.All(ringSegments, s => Assert.Same(RoadCatalog.DefaultRoundabout, s.Type));
            var armEnds = net.Nodes.Where(n => n.SegmentIds.Count == 3).ToList();
            Assert.Equal(4, armEnds.Count);                                  // jeder Arm hängt am Ring
        }

        [Fact]
        public void Roundabout_RejectsTwoWayTypeAndBadRadius()
        {
            var net = new RoadNetwork();
            Assert.Equal(RoadBuildError.InvalidType, RoadBuilder.PlanRoundabout(net, Vector2.Zero, 30f, RoadCatalog.CityTwoLane).Error);
            Assert.Equal(RoadBuildError.InvalidRadius, RoadBuilder.PlanRoundabout(net, Vector2.Zero, 5f, RoadCatalog.DefaultRoundabout).Error);
        }
    }
}
