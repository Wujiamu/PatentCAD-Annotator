using PatentOffice.Visio;
using Xunit;

namespace PatentOffice.Visio.CodeTests
{
    public sealed class G12AnnotationPairTests
    {
        [Fact]
        public void TwoLinesAndOneLabel_ReportsOrphanAndKeepsValidPair()
        {
            AssertPairCounts(2, 1, true, 1);
        }

        [Fact]
        public void OneLineAndTwoLabels_ReportsOrphanAndKeepsValidPair()
        {
            AssertPairCounts(1, 2, true, 1);
        }

        [Fact]
        public void CompleteAndMissingPairs_UseMalformedIdCount()
        {
            AssertPairCounts(1, 1, true, 0);
            AssertPairCounts(1, 0, false, 1);
            AssertPairCounts(2, 2, true, 0);
        }

        [Fact]
        public void SameIdDifferentNumbers_CannotBorrowEachOthersLabels()
        {
            FakeVisioPage page = new FakeVisioPage();
            Add(page, "same", "301", true, null);
            Add(page, "same", "301", false, " 301 ");
            Add(page, "same", "602", true, null);
            Add(page, "same", "603", false, "603");

            AnnotationScanResult result = Scan(page);

            Assert.Contains("301", result.MarkedNumbers);
            Assert.DoesNotContain("602", result.MarkedNumbers);
            Assert.DoesNotContain("603", result.MarkedNumbers);
            Assert.Equal(1, result.MalformedCount);
        }

        [Fact]
        public void SameIdDifferentNumbers_CompletePairsRemainMarked()
        {
            FakeVisioPage page = new FakeVisioPage();
            Add(page, "same", "301", true, null);
            Add(page, "same", "301", false, "301");
            Add(page, "same", "602", true, null);
            Add(page, "same", "602", false, "602");

            AnnotationScanResult result = Scan(page);

            Assert.Equal(2, result.MarkedNumbers.Count);
            Assert.Contains("301", result.MarkedNumbers);
            Assert.Contains("602", result.MarkedNumbers);
            Assert.Equal(0, result.MalformedCount);
        }

        [Fact]
        public void DifferentIdsSameNumber_AreIndependent()
        {
            FakeVisioPage page = new FakeVisioPage();
            Add(page, "first", "S1", true, null);
            Add(page, "first", "S1", false, "s1");
            Add(page, "second", "s1", true, null);
            Add(page, "second", "s1", false, "S1");
            Add(page, "third", "S1", true, null);

            AnnotationScanResult result = Scan(page);

            Assert.Single(result.MarkedNumbers);
            Assert.Contains("s1", result.MarkedNumbers);
            Assert.Equal(1, result.MalformedCount);
        }

        [Fact]
        public void InvalidVisibleText_DoesNotHideValidPairOrExtraPart()
        {
            FakeVisioPage page = new FakeVisioPage();
            Add(page, "same", "301", true, null);
            Add(page, "same", "301", false, "301");
            Add(page, "same", "301", false, "999");

            AnnotationScanResult result = Scan(page);

            Assert.Contains("301", result.MarkedNumbers);
            Assert.Equal(1, result.MalformedCount);
        }

        [Fact]
        public void EmptyVisibleText_DoesNotCompletePair()
        {
            FakeVisioPage page = new FakeVisioPage();
            Add(page, "same", "301", true, null);
            Add(page, "same", "301", false, "");

            AnnotationScanResult result = Scan(page);

            Assert.Empty(result.MarkedNumbers);
            Assert.Equal(1, result.MalformedCount);
        }

        private static void AssertPairCounts(int lines, int labels, bool marked, int malformed)
        {
            FakeVisioPage page = new FakeVisioPage();
            for (int i = 0; i < lines; i++) Add(page, "same", "301", true, null);
            for (int i = 0; i < labels; i++) Add(page, "same", "301", false, "301");
            AnnotationScanResult result = Scan(page);
            Assert.Equal(marked, result.MarkedNumbers.Contains("301"));
            Assert.Equal(malformed, result.MalformedCount);
        }

        private static AnnotationScanResult Scan(FakeVisioPage page)
        {
            FakeVisioDocument document = new FakeVisioDocument();
            document.Pages.Add(page);
            return VisioAnnotations.ScanDocument((dynamic)document);
        }

        private static void Add(FakeVisioPage page, string id, string number, bool isLine, string text)
        {
            FakeVisioShape shape = new FakeVisioShape { OneD = isLine ? 1 : 0, Text = text ?? "" };
            AddCell(shape, "PatentMarkerProduct", "PATMARKER_VISIO");
            AddCell(shape, "PatentMarkerRole", isLine ? "leader" : "label");
            AddCell(shape, "PatentMarkerAnnotationId", id);
            AddCell(shape, "PatentMarkerNumber", number);
            AddCell(shape, "PatentMarkerSchema", "1");
            page.Shapes.Add(shape);
        }

        private static void AddCell(FakeVisioShape shape, string row, string value)
        {
            shape.Cells.Add("User." + row + ".Value", new FakeVisioCell { FormulaU = ShapeSheetString.Encode(value) });
        }
    }
}
