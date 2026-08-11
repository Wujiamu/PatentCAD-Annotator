using Autodesk.AutoCAD.DatabaseServices;
using Xunit;

namespace PatentMarker.RuntimeContractTests
{
    public sealed class DictPaletteCadServiceTests
    {
        [Fact]
        public void RenameNumberChangesOnlyMatchingPatAnnotation()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                Leader patLeader;
                MText patText;
                AddLeader(fixture, "old", "PAT_DIM", out patLeader, out patText);
                Assert.True(patLeader.Annotation.IsNull);

                int changed = Palette.DictPaletteCadService.RenameNumber(
                    fixture.Document, "OLD", "new");

                Assert.Equal(1, changed);
                Assert.Equal("new", patText.Contents);
                Assert.Contains("transaction.commit", fixture.Database.Trace);
                Assert.DoesNotContain("transaction.rollback", fixture.Database.Trace);
            }
        }

        [Fact]
        public void DeleteAllErasesPatLeaderAndAnnotationButSkipsOtherLeader()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                Leader patLeader;
                MText patText;
                AddLeader(fixture, "pat", "PAT_DIM", out patLeader, out patText);
                Assert.True(patLeader.Annotation.IsNull);

                Leader otherLeader;
                MText otherText;
                AddLeader(fixture, "other", "OTHER_DIM", out otherLeader, out otherText);

                Palette.DictPaletteDeleteResult result =
                    Palette.DictPaletteCadService.DeleteAll(fixture.Document);

                Assert.Equal(1, result.Deleted);
                Assert.Equal(1, result.Skipped);
                Assert.True(patLeader.IsErased);
                Assert.True(patText.IsErased);
                Assert.False(otherLeader.IsErased);
                Assert.False(otherText.IsErased);
                Assert.Contains("transaction.commit", fixture.Database.Trace);
            }
        }

        private static void AddLeader(
            SimulationFixture fixture,
            string number,
            string styleName,
            out Leader leader,
            out MText text)
        {
            using (Transaction tr = fixture.Database.TransactionManager.StartTransaction())
            {
                DimStyleTableRecord style = new DimStyleTableRecord { Name = styleName };
                ObjectId styleId = fixture.Database.AllocateId(style);

                text = new MText { Contents = number };
                fixture.Database.ModelSpace.AppendEntity(text);

                leader = new Leader { DimensionStyle = styleId };
                fixture.Database.ModelSpace.AppendEntity(leader);
                if (styleName == "PAT_DIM")
                    PatentMarker.Commands.PatLeaderTextAttachment.LinkText(leader, text, tr);
                else
                    leader.Annotation = text.ObjectId;

                tr.Commit();
            }
        }
    }
}
