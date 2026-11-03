using System;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Commands;
using PatentMarker.I18n;
using Xunit;

namespace PatentMarker.RuntimeContractTests
{
    public sealed class G13IndependentCommandTests
    {
        private static readonly Point3d Top = new Point3d(0, 0, 2);
        private static readonly Point3d Bottom = new Point3d(0, 100, 2);
        private static readonly Point3d OtherBottom = new Point3d(0, 100, 5);
        private static readonly Point3d Width = new Point3d(50, 50, 2);

        [Fact]
        public void UserCreateRejectsNonPlanarBeforeAnyAppend()
        {
            using (SimulationFixture f = new SimulationFixture())
            {
                QueueCreate(f, Top, OtherBottom, Width);
                new PatBraceCommand().Create();
                Assert.Empty(f.Database.CommittedEntities);
                Assert.DoesNotContain(f.Database.Trace, x => x.StartsWith("append:"));
                Assert.Contains(f.Editor.Messages, x => x.Contains("same elevation"));
            }
        }

        [Fact]
        public void UserEndpointEditRejectsAndPreservesAllVerticesAndRecord()
        {
            using (SimulationFixture f = new SimulationFixture())
            {
                Polyline poly = CreateExisting(f);
                Point3d[] vertices = Vertices(poly);
                string[] record = Record(f.Database, poly);
                f.Editor.Messages.Clear();
                f.Editor.EnqueueSelection(poly.ObjectId);
                f.Editor.EnqueueKeyword(Strings.PatBrace_EditPoints);
                f.Editor.EnqueuePoint(PromptStatus.OK, Top);
                f.Editor.EnqueuePoint(PromptStatus.OK, OtherBottom);
                f.Editor.EnqueuePoint(PromptStatus.None, new Point3d());
                new PatBraceCommand().Edit();
                Assert.Contains(f.Editor.Messages, x => x.Contains("same elevation"));
                Assert.Equal(vertices, Vertices(poly));
                Assert.Equal(2, poly.Elevation);
                Assert.Equal(record, Record(f.Database, poly));
                Assert.Single(f.Database.CommittedEntities);
            }
        }

        [Fact]
        public void UserSizeEditCannotFlattenLegacyOrModifyItsRecord()
        {
            using (SimulationFixture f = new SimulationFixture())
            {
                Polyline poly = CreateExisting(f);
                SeedLegacyV1(f.Database, poly);
                Point3d[] vertices = Vertices(poly);
                string[] record = Record(f.Database, poly);
                f.Editor.Messages.Clear();
                f.Editor.EnqueueSelection(poly.ObjectId);
                f.Editor.EnqueueKeyword(Strings.PatBrace_EditSize);
                f.Editor.EnqueueDouble(120);
                f.Editor.EnqueueDouble(60);
                new PatBraceCommand().Edit();
                Assert.Contains(f.Editor.Messages, x => x.Contains("same elevation"));
                Assert.Equal(vertices, Vertices(poly));
                Assert.Equal(record, Record(f.Database, poly));
                Assert.Single(f.Database.CommittedEntities);
            }
        }

        [Fact]
        public void UserExplicitlyRepairsLegacyEndpointsAndWritesConsistentDefinition()
        {
            using (SimulationFixture f = new SimulationFixture())
            {
                Polyline poly = CreateExisting(f);
                SeedLegacyV1(f.Database, poly);
                string[] original = Record(f.Database, poly);
                using (Transaction tr = f.Database.TransactionManager.StartTransaction())
                {
                    PatBraceDefinition legacy;
                    Assert.True(PatBraceEntity.TryReadDefinition(poly, tr, out legacy));
                    Assert.Equal(2, legacy.Top.Z);
                    Assert.Equal(5, legacy.Bottom.Z);
                }
                Assert.Equal(original, Record(f.Database, poly));
                f.Editor.Messages.Clear();
                f.Editor.EnqueueSelection(poly.ObjectId);
                f.Editor.EnqueueKeyword(Strings.PatBrace_EditPoints);
                f.Editor.EnqueuePoint(PromptStatus.OK, Top);
                f.Editor.EnqueuePoint(PromptStatus.OK, Bottom);
                f.Editor.EnqueuePoint(PromptStatus.None, new Point3d());
                new PatBraceCommand().Edit();
                Assert.DoesNotContain(f.Editor.Messages, x => x.Contains("same elevation"));
                using (Transaction tr = f.Database.TransactionManager.StartTransaction())
                {
                    PatBraceDefinition repaired;
                    Assert.True(PatBraceEntity.TryReadDefinition(poly, tr, out repaired));
                    Assert.Equal(2, repaired.Top.Z);
                    Assert.Equal(2, repaired.Bottom.Z);
                }
                Assert.All(Vertices(poly), x => Assert.Equal(2, x.Z));
                Assert.Single(f.Database.CommittedEntities);
            }
        }

        [Fact]
        public void UserCreationKeepsZeroAndNonzeroPlaneInEntityAndRecord()
        {
            foreach (double z in new[] { 0.0, 2.0, -8.5 })
            {
                using (SimulationFixture f = new SimulationFixture())
                {
                    QueueCreate(f, new Point3d(0, 0, z), new Point3d(0, 100, z), new Point3d(50, 50, z));
                    new PatBraceCommand().Create();
                    Polyline poly = Assert.IsType<Polyline>(Assert.Single(f.Database.CommittedEntities));
                    Assert.All(Vertices(poly), x => Assert.Equal(z, x.Z));
                    using (Transaction tr = f.Database.TransactionManager.StartTransaction())
                    {
                        PatBraceDefinition stored;
                        Assert.True(PatBraceEntity.TryReadDefinition(poly, tr, out stored));
                        Assert.Equal(z, stored.Top.Z);
                        Assert.Equal(z, stored.Bottom.Z);
                    }
                }
            }
        }

        private static void QueueCreate(SimulationFixture f, Point3d top, Point3d bottom, Point3d width)
        {
            f.Editor.EnqueuePoint(PromptStatus.OK, top);
            f.Editor.EnqueuePoint(PromptStatus.OK, bottom);
            f.Editor.EnqueuePoint(PromptStatus.OK, width);
        }

        private static Polyline CreateExisting(SimulationFixture f)
        {
            QueueCreate(f, Top, Bottom, Width);
            new PatBraceCommand().Create();
            return Assert.IsType<Polyline>(Assert.Single(f.Database.CommittedEntities));
        }

        private static Point3d[] Vertices(Polyline poly)
        {
            return Enumerable.Range(0, poly.NumberOfVertices).Select(poly.GetPoint3dAt).ToArray();
        }

        private static Xrecord GetRecord(Database db, Polyline poly, Transaction tr)
        {
            DBDictionary dict = (DBDictionary)tr.GetObject(poly.ExtensionDictionary, OpenMode.ForRead);
            return (Xrecord)tr.GetObject(dict.GetAt(PatBraceEntity.ExtensionKey), OpenMode.ForRead);
        }

        private static string[] Record(Database db, Polyline poly)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
                return GetRecord(db, poly, tr).Data
                    .Select(x => x.TypeCode.ToString(CultureInfo.InvariantCulture) + ":" +
                        Convert.ToString(x.Value, CultureInfo.InvariantCulture)).ToArray();
        }

        private static void SeedLegacyV1(Database db, Polyline poly)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                GetRecord(db, poly, tr).Data = new ResultBuffer(
                    new TypedValue(1000, "PATENTMARKER_BRACE_V1"),
                    new TypedValue(1040, 0.0), new TypedValue(1040, 0.0), new TypedValue(1040, 2.0),
                    new TypedValue(1040, 0.0), new TypedValue(1040, 100.0), new TypedValue(1040, 5.0),
                    new TypedValue(1040, 50.0), new TypedValue(1070, 1));
                tr.Commit();
            }
        }
    }
}
