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
            Assert.True(points.Count > 19);
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
            var points = PatBraceGeometry.BuildPoints(updated);
            Assert.Equal(updated.Bottom, points[points.Count - 1]);
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
            Assert.True(rightPoints.Count > 19);
            Assert.True(rightPoints[8].X > 0.0);
            Assert.True(rightPoints[rightPoints.Count / 2].X > rightPoints[8].X);

            PatBraceDefinition leftTip = PatBraceGeometry.FromPoints(
                new Point3d(0, 10, 0),
                new Point3d(0, 0, 0),
                new Point3d(-4, 5, 0));
            var leftPoints = PatBraceGeometry.BuildPoints(leftTip);

            Assert.Equal(-1, leftTip.Side);
            Assert.True(leftPoints.Count > 19);
            Assert.True(leftPoints[8].X < 0.0);
            Assert.True(leftPoints[leftPoints.Count / 2].X < leftPoints[8].X);
        }

        [Fact]
        public void HorizontalEndpointsSupportUpAndDownCenterTips()
        {
            PatBraceDefinition upTip = PatBraceGeometry.FromPoints(
                new Point3d(0, 0, 0),
                new Point3d(10, 0, 0),
                new Point3d(5, 4, 0));
            var upPoints = PatBraceGeometry.BuildPoints(upTip);
            Assert.True(upPoints.Count > 19);
            Assert.True(upPoints[upPoints.Count / 2].Y > upPoints[8].Y);

            PatBraceDefinition downTip = PatBraceGeometry.FromPoints(
                new Point3d(0, 0, 0),
                new Point3d(10, 0, 0),
                new Point3d(5, -4, 0));
            var downPoints = PatBraceGeometry.BuildPoints(downTip);
            Assert.True(downPoints.Count > 19);
            Assert.True(downPoints[downPoints.Count / 2].Y < downPoints[8].Y);
        }

        [Fact]
        public void CenterTipIsSharpAndStemsStayOnOneSideOfAxis()
        {
            PatBraceDefinition definition = PatBraceGeometry.Create(
                new Point3d(0, 100, 0),
                new Point3d(0, 0, 0),
                20.0, 1);
            var points = PatBraceGeometry.BuildPoints(definition);
            int tipIndex = 0;
            double maxX = double.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].X > maxX)
                {
                    maxX = points[i].X;
                    tipIndex = i;
                }
            }

            Assert.True(tipIndex > 0 && tipIndex < points.Count - 1);
            Assert.Equal(20.0, maxX, 6);
            double incomingX = points[tipIndex].X - points[tipIndex - 1].X;
            double incomingY = points[tipIndex].Y - points[tipIndex - 1].Y;
            double outgoingX = points[tipIndex + 1].X - points[tipIndex].X;
            double outgoingY = points[tipIndex + 1].Y - points[tipIndex].Y;
            double cross = incomingX * outgoingY - incomingY * outgoingX;
            Assert.True(Math.Abs(cross) > 0.0001);
            for (int i = 0; i < points.Count; i++)
            {
                if (i != tipIndex)
                    Assert.True(points[i].X >= -0.000001
                        && points[i].X <= 20.000001);
            }
        }
    }
}
