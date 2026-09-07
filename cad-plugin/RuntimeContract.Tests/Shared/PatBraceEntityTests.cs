using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Commands;
using Xunit;

namespace PatentMarker.RuntimeContractTests
{
    public sealed class PatBraceEntityTests
    {
        [Fact]
        public void ReplaceGeometryKeepsPolylineValidAndUpdatesDefinition()
        {
            Database database = new Database();
            PatBraceDefinition original = PatBraceGeometry.FromPoints(
                new Point3d(0, 0, 0),
                new Point3d(0, 100, 0),
                new Point3d(50, 50, 0));

            Polyline polyline;
            ObjectId braceId;
            using (Transaction tr = database.TransactionManager.StartTransaction())
            {
                BlockTable blockTable = (BlockTable)tr.GetObject(
                    database.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(
                    blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                polyline = PatBraceEntity.CreatePolyline(database, original);
                braceId = modelSpace.AppendEntity(polyline);
                tr.AddNewlyCreatedDBObject(polyline, true);
                PatBraceEntity.WriteDefinition(polyline, original, tr);
                tr.Commit();
            }

            int originalVertexCount = polyline.NumberOfVertices;
            PatBraceDefinition updated = PatBraceGeometry.WithSize(
                original, 120.0, 60.0);

            using (Transaction tr = database.TransactionManager.StartTransaction())
            {
                Polyline stored = (Polyline)tr.GetObject(
                    braceId, OpenMode.ForWrite);
                PatBraceEntity.ReplaceGeometry(stored, updated, tr);
                tr.Commit();
            }

            Assert.Equal(originalVertexCount, polyline.NumberOfVertices);
            Assert.Equal(updated.Top.X, polyline.GetPointAt(0).X, 6);
            Assert.Equal(updated.Top.Y, polyline.GetPointAt(0).Y, 6);
            Point2d last = polyline.GetPointAt(polyline.NumberOfVertices - 1);
            Assert.Equal(updated.Bottom.X, last.X, 6);
            Assert.Equal(updated.Bottom.Y, last.Y, 6);

            using (Transaction tr = database.TransactionManager.StartTransaction())
            {
                PatBraceDefinition roundTripped;
                Assert.True(PatBraceEntity.TryReadDefinition(
                    polyline, tr, out roundTripped));
                Assert.Equal(updated.Height, roundTripped.Height, 6);
                Assert.Equal(updated.Width, roundTripped.Width, 6);
                Assert.Equal(updated.Side, roundTripped.Side);
            }
        }
    }
}
