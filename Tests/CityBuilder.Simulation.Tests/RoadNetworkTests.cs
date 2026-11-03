using System.Linq;
using System.Numerics;
using CityBuilder.Simulation.Roads;
using Xunit;

namespace CityBuilder.Simulation.Tests
{
    public class RoadNetworkTests
    {
        static readonly RoadType City = RoadCatalog.CityTwoLane;

        internal static RoadPlan Plan(RoadNetwork net, Vector2 a, Vector2 b, RoadType type = null, Vector2? control = null, float snap = 8f) =>
            RoadBuilder.Plan(net, a, snap, control, b, snap, type ?? City);

        internal static System.Collections.Generic.List<RoadSegment> Build(RoadNetwork net, Vector2 a, Vector2 b, RoadType type = null, Vector2? control = null, float snap = 8f)
        {
            var plan = Plan(net, a, b, type, control, snap);
            Assert.True(plan.IsValid, $"Plan ungültig: {plan.Error}");
            return RoadBuilder.Build(net, plan);
        }

        [Fact]
        public void Build_CreatesTwoNodesAndOneSegment()
        {
            var net = new RoadNetwork();
            var segs = Build(net, new Vector2(0, 0), new Vector2(100, 0));

            Assert.Single(segs);
            Assert.Equal(2, net.NodeCount);
            Assert.Equal(100f, segs[0].Length, 1);
        }

        [Fact]
        public void Build_SnapsToExistingNode()
        {
            var net = new RoadNetwork();
            Build(net, new Vector2(0, 0), new Vector2(100, 0));
            var second = Build(net, new Vector2(102, 1), new Vector2(100, 100))[0];

            Assert.Equal(3, net.NodeCount);
            Assert.Equal(new Vector2(100, 0), second.Start.Position);
            Assert.Equal(2, second.Start.SegmentIds.Count);
        }

        [Fact]
        public void Plan_RejectsTooShortDuplicateAndSharpAngle()
        {
            var net = new RoadNetwork();
            Assert.Equal(RoadBuildError.TooShort, Plan(net, new Vector2(0, 0), new Vector2(1, 0)).Error);

            Build(net, new Vector2(0, 0), new Vector2(50, 0));
            Assert.Equal(RoadBuildError.Duplicate, Plan(net, new Vector2(0, 0), new Vector2(50, 0)).Error);
            // Fast parallel zur vorhandenen Straße vom selben Knoten weg
            Assert.Equal(RoadBuildError.AngleTooSharp, Plan(net, new Vector2(0, 0), new Vector2(100, 5), snap: 2f).Error);
        }

        [Fact]
        public void Crossing_SplitsBothRoadsIntoFourWayJunction()
        {
            var net = new RoadNetwork();
            Build(net, new Vector2(-100, 0), new Vector2(100, 0));
            var built = Build(net, new Vector2(0, -100), new Vector2(0, 100));

            Assert.Equal(2, built.Count);
            Assert.Equal(4, net.SegmentCount);
            Assert.Equal(5, net.NodeCount);
            var center = net.FindNearestNode(Vector2.Zero, 1f);
            Assert.NotNull(center);
            Assert.Equal(4, center.SegmentIds.Count);
        }

        [Fact]
        public void EndingOnRoad_CreatesTJunction()
        {
            var net = new RoadNetwork();
            Build(net, new Vector2(-100, 0), new Vector2(100, 0));
            Build(net, new Vector2(20, 80), new Vector2(20, 2)); // endet auf der Fahrbahn

            Assert.Equal(3, net.SegmentCount);
            var t = net.FindNearestNode(new Vector2(20, 0), 1f);
            Assert.NotNull(t);
            Assert.Equal(3, t.SegmentIds.Count);
        }

        [Fact]
        public void Split_KeepsGeometryAndDirection()
        {
            var net = new RoadNetwork();
            var seg = Build(net, new Vector2(0, 0), new Vector2(100, 0), RoadCatalog.CityOneWay2, control: new Vector2(50, 60))[0];
            var probe = seg.Evaluate(0.25f);

            var node = net.SplitSegment(seg, 0.5f);
            var parts = net.Segments.ToList();
            Assert.Equal(2, parts.Count);
            // Richtung bleibt Start → Ende (wichtig für Einbahnstraßen)
            Assert.Contains(parts, s => s.Start.Position == new Vector2(0, 0) && s.End == node);
            Assert.Contains(parts, s => s.Start == node && s.End.Position == new Vector2(100, 0));
            // Die Teilkurven liegen exakt auf der alten Kurve
            var first = parts.First(s => s.End == node);
            Assert.True(Vector2.Distance(first.Evaluate(0.5f), probe) < 0.01f);
        }

        [Fact]
        public void CurvedRoad_CrossingStraightRoadTwice_SplitsTwice()
        {
            var net = new RoadNetwork();
            Build(net, new Vector2(-100, 0), new Vector2(100, 0));
            // Bogen von unten nach unten über die Straße hinweg
            Build(net, new Vector2(-50, -40), new Vector2(50, -40), control: new Vector2(0, 120));

            Assert.Equal(6, net.SegmentCount); // Straße in 3 Teile, Bogen in 3 Teile
        }

        [Fact]
        public void Highway_CannotCrossOrConnectToCityRoad()
        {
            var net = new RoadNetwork();
            Build(net, new Vector2(-100, 0), new Vector2(100, 0), RoadCatalog.Highway3);

            Assert.Equal(RoadBuildError.HighwayCrossing,
                Plan(net, new Vector2(0, -100), new Vector2(0, 100)).Error);
            Assert.Equal(RoadBuildError.IncompatibleConnection,
                Plan(net, new Vector2(100, 0), new Vector2(100, 100)).Error);
            Assert.True(Plan(net, new Vector2(100, 0), new Vector2(160, 60), RoadCatalog.Ramp).IsValid);
            Assert.True(Plan(net, new Vector2(100, 0), new Vector2(250, 20), RoadCatalog.Highway3).IsValid);
        }

        [Fact]
        public void RemoveSegment_RemovesOrphanNodesOnly()
        {
            var net = new RoadNetwork();
            var a = Build(net, new Vector2(0, 0), new Vector2(100, 0))[0];
            Build(net, new Vector2(100, 0), new Vector2(100, 100));

            Assert.True(net.RemoveSegment(a.Id));
            Assert.Equal(1, net.SegmentCount);
            Assert.Equal(2, net.NodeCount);
        }

        [Fact]
        public void FindNearestSegment_UsesRoadWidth()
        {
            var net = new RoadNetwork();
            var seg = Build(net, new Vector2(0, 0), new Vector2(100, 0))[0];
            Assert.Equal(seg, net.FindNearestSegment(new Vector2(50, 7), 2f));   // Breite 13 m → Rand bei 6,5 m
            Assert.Null(net.FindNearestSegment(new Vector2(50, 20), 2f));
        }
    }
}
