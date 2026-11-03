using System.Numerics;
using CityBuilder.Simulation.Roads;
using Xunit;

namespace CityBuilder.Simulation.Tests
{
    public class RoadGeometryTests
    {
        [Fact]
        public void SignedAngle_IsCounterClockwisePositive()
        {
            Assert.Equal(90f, RoadGeometry.SignedAngle(Vector2.UnitX, Vector2.UnitY), 3);
            Assert.Equal(-90f, RoadGeometry.SignedAngle(Vector2.UnitY, Vector2.UnitX), 3);
            Assert.Equal(180f, RoadGeometry.Angle(Vector2.UnitX, -Vector2.UnitX), 3);
        }

        [Fact]
        public void Bearing_IsCompassStyle()
        {
            Assert.Equal(0f, RoadGeometry.Bearing(Vector2.UnitY), 3);
            Assert.Equal(90f, RoadGeometry.Bearing(Vector2.UnitX), 3);
            Assert.Equal(270f, RoadGeometry.Bearing(-Vector2.UnitX), 3);
        }

        [Fact]
        public void Snap_RoundsAngleAndLength()
        {
            var p = RoadGeometry.Snap(Vector2.Zero, new Vector2(100f, 9f), Vector2.UnitX, 15f, 8f);
            Assert.Equal(104f, p.X, 2); // 100.4 m → 104 m (13 × 8), Winkel ~5° → 0°
            Assert.Equal(0f, p.Y, 2);

            var q = RoadGeometry.Snap(Vector2.Zero, new Vector2(50f, 48f), Vector2.UnitX, 15f, 0f);
            Assert.Equal(45f, RoadGeometry.SignedAngle(Vector2.UnitX, q), 2);
        }

        [Fact]
        public void ReferenceDirection_PointsAwayFromNode()
        {
            var net = new RoadNetwork();
            var seg = RoadNetworkTests.Build(net, new Vector2(0, 0), new Vector2(100, 0))[0];

            // Am Endknoten zeigt die vorhandene Straße nach -X. Geradeaus weiterbauen (+X) = 180°.
            Assert.True(RoadGeometry.TryGetReferenceDirection(net, seg.End, Vector2.UnitX, out var reference, out float angle));
            Assert.Equal(-1f, reference.X, 3);
            Assert.Equal(180f, angle, 2);

            RoadGeometry.TryGetReferenceDirection(net, seg.End, Vector2.UnitY, out _, out float right);
            Assert.Equal(90f, right, 2);
        }
    }
}
