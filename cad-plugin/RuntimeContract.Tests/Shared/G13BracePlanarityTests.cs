using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Commands;
using Xunit;

namespace PatentMarker.RuntimeContractTests
{
    public sealed class G13BracePlanarityTests
    {
        private static readonly Point3d Top = new Point3d(0, 0, 2);
        private static readonly Point3d CoplanarBottom = new Point3d(0, 100, 2);
        private static readonly Point3d OtherBottom = new Point3d(0, 100, 5);
        private static readonly Point3d WidthPoint = new Point3d(50, 50, 2);

        [Fact]
        public void CreateFromDifferentElevationsIsRejected()
        {
            Assert.Throws<ArgumentException>(() => PatBraceGeometry.FromPoints(Top, OtherBottom, WidthPoint));
        }

        [Fact]
        public void EndpointEditWithDifferentElevationsIsRejected()
        {
            PatBraceDefinition current = PatBraceGeometry.FromPoints(Top, CoplanarBottom, WidthPoint);
            Assert.Throws<ArgumentException>(() => PatBraceGeometry.WithEndpoints(current, Top, OtherBottom));
        }

        [Fact]
        public void SizeEditCannotSilentlyFlattenLegacyDefinition()
        {
            PatBraceDefinition legacy = PatBraceGeometry.Create(Top, OtherBottom, 50, 1);
            Assert.Throws<ArgumentException>(() => PatBraceGeometry.WithSize(legacy, 120, 60));
        }

        [Fact]
        public void NewEntityRejectsNonPlanarDefinition()
        {
            Database database = new Database();
            PatBraceDefinition invalid = PatBraceGeometry.Create(Top, OtherBottom, 50, 1);
            Assert.Throws<ArgumentException>(() => PatBraceEntity.CreatePolyline(database, invalid));
        }

        [Fact]
        public void RejectedGeometryLeavesExistingVerticesAndElevationUntouched()
        {
            Database database = new Database();
            PatBraceDefinition original = PatBraceGeometry.FromPoints(Top, CoplanarBottom, WidthPoint);
            Polyline polyline = PatBraceEntity.CreatePolyline(database, original);
            int count = polyline.NumberOfVertices;
            Point3d first = polyline.GetPoint3dAt(0);
            Point3d last = polyline.GetPoint3dAt(count - 1);
            PatBraceDefinition invalid = PatBraceGeometry.Create(Top, OtherBottom, 50, 1);

            Assert.Throws<ArgumentException>(() => PatBraceEntity.ApplyGeometry(polyline, invalid));

            Assert.Equal(count, polyline.NumberOfVertices);
            Assert.Equal(first, polyline.GetPoint3dAt(0));
            Assert.Equal(last, polyline.GetPoint3dAt(count - 1));
            Assert.Equal(2, polyline.Elevation);
        }

        [Fact]
        public void RejectedDefinitionDoesNotCreateExtensionDictionary()
        {
            Database database = new Database();
            Polyline polyline = PatBraceEntity.CreatePolyline(database,
                PatBraceGeometry.FromPoints(Top, CoplanarBottom, WidthPoint));
            PatBraceDefinition invalid = PatBraceGeometry.Create(Top, OtherBottom, 50, 1);
            using (Transaction tr = database.TransactionManager.StartTransaction())
            {
                Assert.Throws<ArgumentException>(() => PatBraceEntity.WriteDefinition(polyline, invalid, tr));
                Assert.True(polyline.ExtensionDictionary.IsNull);
            }
        }

        [Fact]
        public void RejectedReplacementPreservesGeometryAndStoredDefinition()
        {
            Database database = new Database();
            PatBraceDefinition original = PatBraceGeometry.FromPoints(Top, CoplanarBottom, WidthPoint);
            Polyline polyline = PatBraceEntity.CreatePolyline(database, original);
            using (Transaction tr = database.TransactionManager.StartTransaction())
            {
                PatBraceEntity.WriteDefinition(polyline, original, tr);
                Point3d first = polyline.GetPoint3dAt(0);
                Point3d last = polyline.GetPoint3dAt(polyline.NumberOfVertices - 1);
                PatBraceDefinition invalid = PatBraceGeometry.Create(Top, OtherBottom, 50, 1);

                Assert.Throws<ArgumentException>(() => PatBraceEntity.ReplaceGeometry(polyline, invalid, tr));

                Assert.Equal(first, polyline.GetPoint3dAt(0));
                Assert.Equal(last, polyline.GetPoint3dAt(polyline.NumberOfVertices - 1));
                PatBraceDefinition stored;
                Assert.True(PatBraceEntity.TryReadDefinition(polyline, tr, out stored));
                Assert.Equal(original.Bottom, stored.Bottom);
            }
        }

        [Fact]
        public void EqualElevationRemainsExactDuringCreationAndSizeEdit()
        {
            foreach (double z in new[] { 0.0, 2.0, -8.5 })
            {
                Point3d top = new Point3d(0, 0, z);
                Point3d bottom = new Point3d(0, 100, z);
                PatBraceDefinition current = PatBraceGeometry.FromPoints(top, bottom, new Point3d(50, 50, z));
                PatBraceDefinition changed = PatBraceGeometry.WithSize(current, 120, 60);
                Assert.Equal(z, changed.Top.Z);
                Assert.Equal(z, changed.Bottom.Z);
                foreach (Point3d point in PatBraceGeometry.BuildPoints(changed)) Assert.Equal(z, point.Z);
                Assert.Equal(z, PatBraceEntity.CreatePolyline(new Database(), changed).Elevation);
            }
        }

        [Fact]
        public void LegacyV1NonPlanarRecordIsReadableAndCanBeCorrectedExplicitly()
        {
            Database database = new Database();
            Polyline polyline = PatBraceEntity.CreatePolyline(database,
                PatBraceGeometry.FromPoints(Top, CoplanarBottom, WidthPoint));
            using (Transaction tr = database.TransactionManager.StartTransaction())
            {
                polyline.CreateExtensionDictionary();
                DBDictionary dictionary = (DBDictionary)tr.GetObject(polyline.ExtensionDictionary, OpenMode.ForWrite);
                Xrecord record = new Xrecord();
                record.Data = new ResultBuffer(new TypedValue(1000, "PATENTMARKER_BRACE_V1"),
                    new TypedValue(1040, 0.0), new TypedValue(1040, 0.0), new TypedValue(1040, 2.0),
                    new TypedValue(1040, 0.0), new TypedValue(1040, 100.0), new TypedValue(1040, 5.0),
                    new TypedValue(1040, 50.0), new TypedValue(1070, 1));
                dictionary.SetAt(PatBraceEntity.ExtensionKey, record);
                tr.AddNewlyCreatedDBObject(record, true);

                PatBraceDefinition legacy;
                Assert.True(PatBraceEntity.TryReadDefinition(polyline, tr, out legacy));
                Assert.Equal(2, legacy.Top.Z);
                Assert.Equal(5, legacy.Bottom.Z);
                PatBraceDefinition corrected = PatBraceGeometry.WithEndpoints(legacy, Top, CoplanarBottom);
                PatBraceEntity.ReplaceGeometry(polyline, corrected, tr);
                PatBraceDefinition reread;
                Assert.True(PatBraceEntity.TryReadDefinition(polyline, tr, out reread));
                Assert.Equal(2, reread.Top.Z);
                Assert.Equal(2, reread.Bottom.Z);
            }
        }
    }
}
