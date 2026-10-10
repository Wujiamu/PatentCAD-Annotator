using System.Collections.Generic;
using PatentOffice.Visio;
using Xunit;

namespace PatentOffice.Visio.CodeTests
{
    public sealed class AnnotationScanTests
    {
        [Fact]
        public void ScanDocument_CountsValidPairsOnForegroundPagesOnly()
        {
            FakeVisioPage foreground = new FakeVisioPage { Background = 0 };
            foreground.Shapes.Add(new FakeVisioShape { OneD = 1 });
            AddAnnotation(foreground, "pair-1", "1342A", true, null);
            AddAnnotation(foreground, "pair-1", "1342A", false, " 1342a \r");
            AddAnnotation(foreground, "pair-2", "1342a", true, null);
            AddAnnotation(foreground, "pair-2", "1342a", false, "1342a");
            AddAnnotation(foreground, "orphan-line", "2", true, null);
            AddAnnotation(foreground, "orphan-label", "3", false, "3");
            AddAnnotation(foreground, "mismatched-pair", "4", true, null);
            AddAnnotation(foreground, "mismatched-pair", "5", false, "5");

            FakeVisioPage background = new FakeVisioPage { Background = 1 };
            AddAnnotation(background, "background-pair", "99", true, null);
            AddAnnotation(background, "background-pair", "99", false, "99");

            FakeVisioDocument document = new FakeVisioDocument();
            document.Pages.Add(foreground);
            document.Pages.Add(background);

            AnnotationScanResult result = VisioAnnotations.ScanDocument((dynamic)document);

            Assert.Single(result.MarkedNumbers);
            Assert.Contains("1342a", result.MarkedNumbers);
            Assert.DoesNotContain("99", result.MarkedNumbers);
            Assert.Equal(3, result.MalformedCount);
        }

        [Fact]
        public void ScanDocument_DoesNotPairPartsOnDifferentPages()
        {
            FakeVisioPage first = new FakeVisioPage { Background = 0 };
            AddAnnotation(first, "shared-id", "1", true, null);
            FakeVisioPage second = new FakeVisioPage { Background = 0 };
            AddAnnotation(second, "shared-id", "1", false, "1");

            FakeVisioDocument document = new FakeVisioDocument();
            document.Pages.Add(first);
            document.Pages.Add(second);

            AnnotationScanResult result = VisioAnnotations.ScanDocument((dynamic)document);

            Assert.Empty(result.MarkedNumbers);
            Assert.Equal(2, result.MalformedCount);
        }

        private static void AddAnnotation(FakeVisioPage page, string id, string number, bool isLine, string text)
        {
            FakeVisioShape shape = new FakeVisioShape
            {
                OneD = isLine ? 1 : 0,
                Text = text ?? ""
            };
            AddUserValue(shape, "PatentMarkerProduct", "PATMARKER_VISIO");
            AddUserValue(shape, "PatentMarkerRole", isLine ? "leader" : "label");
            AddUserValue(shape, "PatentMarkerAnnotationId", id);
            AddUserValue(shape, "PatentMarkerNumber", number);
            AddUserValue(shape, "PatentMarkerSchema", "1");
            page.Shapes.Add(shape);
        }

        private static void AddUserValue(FakeVisioShape shape, string row, string value)
        {
            shape.Cells.Add("User." + row + ".Value", new FakeVisioCell { FormulaU = ShapeSheetString.Encode(value) });
        }
    }

    public sealed class FakeVisioDocument
    {
        public FakeVisioCollection<FakeVisioPage> Pages { get; private set; }

        public FakeVisioDocument()
        {
            Pages = new FakeVisioCollection<FakeVisioPage>();
        }
    }

    public sealed class FakeVisioPage
    {
        public int Background { get; set; }
        public FakeVisioCollection<FakeVisioShape> Shapes { get; private set; }

        public FakeVisioPage()
        {
            Shapes = new FakeVisioCollection<FakeVisioShape>();
        }
    }

    public sealed class FakeVisioShape
    {
        public int OneD { get; set; }
        public string Text { get; set; }
        public FakeVisioCollection<FakeVisioShape> Shapes { get; private set; }
        public Dictionary<string, FakeVisioCell> Cells { get; private set; }

        public FakeVisioShape()
        {
            Shapes = new FakeVisioCollection<FakeVisioShape>();
            Cells = new Dictionary<string, FakeVisioCell>();
        }

        public int CellExistsU(string name, int flags)
        {
            return Cells.ContainsKey(name) ? 1 : 0;
        }

        public FakeVisioCell CellsU(string name)
        {
            return Cells[name];
        }
    }

    public sealed class FakeVisioCell
    {
        public string FormulaU { get; set; }
    }

    public sealed class FakeVisioCollection<T>
    {
        private readonly List<T> _items = new List<T>();

        public int Count { get { return _items.Count; } }

        public T Item(int index)
        {
            return _items[index - 1];
        }

        public void Add(T item)
        {
            _items.Add(item);
        }
    }
}
