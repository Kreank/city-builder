using System.Numerics;
using CityBuilder.Simulation.Roads;
using Xunit;

namespace CityBuilder.Simulation.Tests
{
    public class RoadNetworkTests
    {
        [Fact]
        public void BuildRoad_CreatesTwoNodesAndOneSegment()
        {
            var net = new RoadNetwork();
            var seg = net.BuildRoad(new Vector2(0, 0), null, new Vector2(100, 0), RoadType.Small);

            Assert.NotNull(seg);
            Assert.Equal(2, net.NodeCount);
            Assert.Equal(1, net.SegmentCount);
            Assert.Equal(100f, seg.Length, 1);
        }

        [Fact]
        public void BuildRoad_SnapsToExistingNode()
        {
            var net = new RoadNetwork();
            net.BuildRoad(new Vector2(0, 0), null, new Vector2(100, 0), RoadType.Small);
            var second = net.BuildRoad(new Vector2(102, 1), null, new Vector2(100, 100), RoadType.Small, snapDistance: 8f);

            Assert.Equal(3, net.NodeCount);
            Assert.Equal(new Vector2(100, 0), second.Start.Position);
            Assert.Equal(2, second.Start.SegmentIds.Count);
        }

        [Fact]
        public void BuildRoad_RejectsTooShortAndDuplicate()
        {
            var net = new RoadNetwork();
            Assert.Null(net.BuildRoad(new Vector2(0, 0), null, new Vector2(1, 0), RoadType.Small));

            net.BuildRoad(new Vector2(0, 0), null, new Vector2(50, 0), RoadType.Small);
            Assert.Null(net.BuildRoad(new Vector2(0, 0), null, new Vector2(50, 0), RoadType.Small, 8f));
            Assert.Equal(1, net.SegmentCount);
            Assert.Equal(2, net.NodeCount);
        }

        [Fact]
        public void RemoveSegment_RemovesOrphanNodesOnly()
        {
            var net = new RoadNetwork();
            var a = net.BuildRoad(new Vector2(0, 0), null, new Vector2(100, 0), RoadType.Small);
            net.BuildRoad(new Vector2(100, 0), null, new Vector2(100, 100), RoadType.Small, 8f);

            Assert.True(net.RemoveSegment(a.Id));
            Assert.Equal(1, net.SegmentCount);
            Assert.Equal(2, net.NodeCount); // Knoten (100,0) hängt noch am zweiten Segment
        }

        [Fact]
        public void CurvedSegment_IsLongerThanStraightLine()
        {
            var net = new RoadNetwork();
            var seg = net.BuildRoad(new Vector2(0, 0), new Vector2(50, 50), new Vector2(100, 0), RoadType.Small);
            Assert.True(seg.Length > 100f);
            Assert.True(seg.DistanceTo(new Vector2(50, 25)) < 1f); // Scheitelpunkt der Kurve
        }

        [Fact]
        public void FindNearestSegment_UsesRoadWidth()
        {
            var net = new RoadNetwork();
            var seg = net.BuildRoad(new Vector2(0, 0), null, new Vector2(100, 0), RoadType.Small);
            Assert.Equal(seg, net.FindNearestSegment(new Vector2(50, 5), 2f));
            Assert.Null(net.FindNearestSegment(new Vector2(50, 20), 2f));
        }
    }
}
