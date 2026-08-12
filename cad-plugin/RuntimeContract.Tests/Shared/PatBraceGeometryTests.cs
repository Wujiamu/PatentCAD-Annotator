using Autodesk.AutoCAD.Geometry;
using PatentMarker.Commands;
using Xunit;

namespace PatentMarker.RuntimeContractTests
{
    public sealed class PatBraceGeometryTests
    {
        [Fact]
        public void ThreePointsCreateExactEndpointsAndPositiveWidth()
        {
            PatBraceDefinition definition = PatBraceGeometry.FromPoints(
                new Point3d(0, 10, 0),
                new Point3d(0, 0, 0),
                new Point3d(-4, 5, 0));

            Assert.Equal(10.0, definition.Height, 6);
            Assert.Equal(4.0, definition.Width, 6);
            Assert.Equal(-1, definition.Side);

            var points = PatBraceGeometry.BuildPoints(definition);
            Assert.Equal(definition.Top, points[0]);
            Assert.Equal(definition.Bottom, points[points.Count - 1]);
            Assert.Equal(19, points.Count);
        }

        [Fact]
        public void SizeEditKeepsTopAndOrientationWhileRebuildingShape()
        {
            PatBraceDefinition original = PatBraceGeometry.FromPoints(
                new Point3d(2, 20, 0),
                new Point3d(2, 10, 0),
                new Point3d(6, 15, 0));

            PatBraceDefinition updated = PatBraceGeometry.WithSize(
                original, 30.0, 8.0);

            Assert.Equal(original.Top, updated.Top);
            Assert.Equal(30.0, updated.Height, 6);
            Assert.Equal(8.0, updated.Width, 6);
            Assert.Equal(original.Side, updated.Side);
            Assert.Equal(new Point3d(2, -10, 0), updated.Bottom);
        }

        [Fact]
        public void EndpointEditPreservesWidthAndSide()
        {
            PatBraceDefinition original = PatBraceGeometry.FromPoints(
                new Point3d(0, 0, 0),
                new Point3d(10, 0, 0),
                new Point3d(5, -3, 0));

            PatBraceDefinition updated = PatBraceGeometry.WithEndpoints(
                original,
                new Point3d(1, 1, 0),
                new Point3d(1, 21, 0));

            Assert.Equal(original.Width, updated.Width, 6);
            Assert.Equal(original.Side, updated.Side);
            Assert.Equal(20.0, updated.Height, 6);
            Assert.Equal(updated.Top, PatBraceGeometry.BuildPoints(updated)[0]);
            Assert.Equal(updated.Bottom,
                PatBraceGeometry.BuildPoints(updated)[18]);
        }

        [Fact]
        public void WidthPointControlsCenterTipAndOppositeOuterShoulders()
        {
            PatBraceDefinition rightTip = PatBraceGeometry.FromPoints(
                new Point3d(0, 10, 0),
                new Point3d(0, 0, 0),
                new Point3d(4, 5, 0));
            var rightPoints = PatBraceGeometry.BuildPoints(rightTip);

            Assert.Equal(1, rightTip.Side);
            Assert.True(rightPoints[9].X > 0.0);
            Assert.True(rightPoints[5].X < 0.0);
            Assert.True(rightPoints[13].X < 0.0);

            PatBraceDefinition leftTip = PatBraceGeometry.FromPoints(
                new Point3d(0, 10, 0),
                new Point3d(0, 0, 0),
                new Point3d(-4, 5, 0));
            var leftPoints = PatBraceGeometry.BuildPoints(leftTip);

            Assert.Equal(-1, leftTip.Side);
            Assert.True(leftPoints[9].X < 0.0);
            Assert.True(leftPoints[5].X > 0.0);
            Assert.True(leftPoints[13].X > 0.0);
        }

        [Fact]
        public void HorizontalEndpointsSupportUpAndDownCenterTips()
        {
            PatBraceDefinition upTip = PatBraceGeometry.FromPoints(
                new Point3d(0, 0, 0),
                new Point3d(10, 0, 0),
                new Point3d(5, 4, 0));
            var upPoints = PatBraceGeometry.BuildPoints(upTip);
            Assert.True(upPoints[9].Y > 0.0);
            Assert.True(upPoints[5].Y < 0.0);

            PatBraceDefinition downTip = PatBraceGeometry.FromPoints(
                new Point3d(0, 0, 0),
                new Point3d(10, 0, 0),
                new Point3d(5, -4, 0));
            var downPoints = PatBraceGeometry.BuildPoints(downTip);
            Assert.True(downPoints[9].Y < 0.0);
            Assert.True(downPoints[5].Y > 0.0);
        }
    }
}
