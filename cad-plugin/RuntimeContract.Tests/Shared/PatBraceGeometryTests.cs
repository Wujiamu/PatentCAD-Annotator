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
    }
}
