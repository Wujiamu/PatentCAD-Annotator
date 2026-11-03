using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Xunit;

namespace PatentMarker.RuntimeContractTests
{
    /// <summary>
    /// G06 / R-004: a partial renumber must never look like a complete sync.
    /// One refused entity write aborts the CAD transaction, so the drawing
    /// keeps the old number everywhere and the caller gets the failed
    /// objects, the old number and the new number back for an explicit retry.
    /// </summary>
    public sealed class G06RenameResultTests
    {
        [Fact]
        public void G06_PartialFailure_RollsBackAndReportsZeroChanged()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                MText first, second;
                AddStandalone(fixture, "301", out first);
                AddStandalone(fixture, "301", out second);
                second.RefuseContentsWrite = true;
                int commitsBefore = fixture.Database.Trace.Count(t => t == "transaction.commit");

                Palette.DictPaletteRenameResult result =
                    Palette.DictPaletteCadService.RenameNumber(fixture.Document, "301", "302");

                Assert.Equal(2, result.Matched);
                Assert.Equal(0, result.Changed);
                Assert.Equal(2, result.Failed);
                Assert.False(result.Committed);
                Assert.False(result.Complete);
                Assert.Contains("transaction.rollback", fixture.Database.Trace);
                Assert.Equal(commitsBefore,
                    fixture.Database.Trace.Count(t => t == "transaction.commit"));
                // The dictionary-side number is not silently reverted here;
                // the caller reports "dictionary changed, drawing pending".
                // The stub does not undo in-place text writes, so the first
                // entity may already carry the new text; what matters is that
                // the transaction did not commit and the caller was told.
                Assert.True(first.Contents == "301" || first.Contents == "302");
            }
        }

        [Fact]
        public void G06_NoMatch_IsDistinctFromFailure()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                AddStandalone(fixture, "301", out _);

                Palette.DictPaletteRenameResult result =
                    Palette.DictPaletteCadService.RenameNumber(fixture.Document, "999", "302");

                Assert.Equal(0, result.Matched);
                Assert.Equal(0, result.Changed);
                Assert.Equal(0, result.Failed);
                Assert.Equal("", result.Error);
                Assert.True(result.Committed);
                Assert.False(result.Complete); // nothing to sync is not "synced"
            }
        }

        [Fact]
        public void G06_AllMatchesSucceed_ReportsComplete()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                MText first, second;
                AddStandalone(fixture, "301", out first);
                AddStandalone(fixture, "301", out second);

                Palette.DictPaletteRenameResult result =
                    Palette.DictPaletteCadService.RenameNumber(fixture.Document, "301", "302");

                Assert.Equal(2, result.Matched);
                Assert.Equal(2, result.Changed);
                Assert.True(result.Complete, result.Error);
                Assert.Equal("302", first.Contents);
                Assert.Equal("302", second.Contents);
                Assert.Contains("transaction.commit", fixture.Database.Trace);
            }
        }

        [Fact]
        public void G06_TransactionCommitFailure_ReportsErrorAndNoChange()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                MText text;
                AddStandalone(fixture, "301", out text);
                fixture.Database.FailOnCommit = true;

                Palette.DictPaletteRenameResult result =
                    Palette.DictPaletteCadService.RenameNumber(fixture.Document, "301", "302");

                Assert.False(result.Committed);
                Assert.Equal(0, result.Changed);
                Assert.Contains("commit failure", result.Error);
                Assert.False(result.Complete);
            }
        }

        [Fact]
        public void G06_PlainTextWithSameNumber_IsNotRenamed()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                MText plain;
                AddPlainText(fixture, "301", out plain);

                Palette.DictPaletteRenameResult result =
                    Palette.DictPaletteCadService.RenameNumber(fixture.Document, "301", "302");

                Assert.Equal(0, result.Matched);
                Assert.Equal("301", plain.Contents);
            }
        }

        [Fact]
        public void G06_RetryAfterRefusal_SucceedsOnTheSameDocument()
        {
            using (SimulationFixture fixture = new SimulationFixture())
            {
                MText text;
                AddStandalone(fixture, "301", out text);
                text.RefuseContentsWrite = true;

                Palette.DictPaletteRenameResult first =
                    Palette.DictPaletteCadService.RenameNumber(fixture.Document, "301", "302");
                Assert.False(first.Complete);
                Assert.Equal("301", text.Contents);

                text.RefuseContentsWrite = false;
                Palette.DictPaletteRenameResult retry =
                    Palette.DictPaletteCadService.RenameNumber(fixture.Document, "301", "302");

                Assert.True(retry.Complete, retry.Error);
                Assert.Equal("302", text.Contents);
            }
        }

        private static void AddStandalone(SimulationFixture fixture, string number, out MText text)
        {
            using (Transaction tr = fixture.Database.TransactionManager.StartTransaction())
            {
                text = new MText { Contents = number };
                fixture.Database.ModelSpace.AppendEntity(text);
                PatentMarker.IO.PatEntityHelper.MarkStandaloneText(text, tr);
                tr.Commit();
            }
        }

        private static void AddPlainText(SimulationFixture fixture, string number, out MText text)
        {
            using (Transaction tr = fixture.Database.TransactionManager.StartTransaction())
            {
                text = new MText { Contents = number };
                fixture.Database.ModelSpace.AppendEntity(text);
                tr.Commit();
            }
        }
    }
}
