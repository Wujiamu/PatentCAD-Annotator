using System;
using System.IO;
using PatentOffice.Shared;
using Xunit;

namespace PatentOffice.Visio.CodeTests
{
    public sealed class AnnotationSelectionTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void MarkSelectedLine_RejectsEmptyOrMultipleSelectionBeforeMutation(int count)
        {
            WithSavedDocument(application =>
            {
                for (int i = 0; i < count; i++)
                    application.ActiveWindow.Selection.Add(new FakeVisioShape { OneD = 1 });
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                {
                    VisioAnnotations.MarkSelectedLine((dynamic)application, Entry());
                });
                Assert.Contains("只选中一条", error.Message);
                Assert.Equal(0, application.UndoScopesStarted);
            });
        }

        [Fact]
        public void MarkSelectedLine_RejectsAnOrdinaryRectangleBeforeMutation()
        {
            WithSavedDocument(application =>
            {
                application.ActiveWindow.Selection.Add(new FakeVisioShape { OneD = 0 });
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                {
                    VisioAnnotations.MarkSelectedLine((dynamic)application, Entry());
                });
                Assert.Contains("不是 Visio 一维线条", error.Message);
                Assert.Equal(0, application.UndoScopesStarted);
            });
        }

        [Fact]
        public void MarkSelectedLine_RejectsAnAlreadyMarkedLineBeforeMutation()
        {
            WithSavedDocument(application =>
            {
                FakeVisioShape line = new FakeVisioShape { OneD = 1 };
                line.Cells.Add("User.PatentMarkerProduct.Value", new FakeVisioCell
                {
                    FormulaU = ShapeSheetString.Encode("PATMARKER_VISIO")
                });
                application.ActiveWindow.Selection.Add(line);
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                {
                    VisioAnnotations.MarkSelectedLine((dynamic)application, Entry());
                });
                Assert.Contains("已是产品标注的一部分", error.Message);
                Assert.Equal(0, application.UndoScopesStarted);
            });
        }

        private static PatentEntry Entry()
        {
            return new PatentEntry { Number = "1", Name = "part" };
        }

        private static void WithSavedDocument(Action<SelectionTestApplication> action)
        {
            // This fixture supplies only the path-exists prerequisite for L1 guards;
            // it is not a real VSDX and provides no host or persistence evidence.
            string path = Path.Combine(Path.GetTempPath(), "PatentOffice-Selection-" +
                Guid.NewGuid().ToString("N") + ".vsdx");
            File.WriteAllBytes(path, new byte[0]);
            try { action(new SelectionTestApplication(path)); }
            finally { File.Delete(path); }
        }
    }

    public sealed class SelectionTestApplication
    {
        public FakeVisioCollection<SelectionTestDocument> Documents { get; private set; }
        public SelectionTestDocument ActiveDocument { get; private set; }
        public SelectionTestWindow ActiveWindow { get; private set; }
        public int UndoScopesStarted { get; private set; }

        public SelectionTestApplication(string path)
        {
            ActiveDocument = new SelectionTestDocument { FullName = path };
            Documents = new FakeVisioCollection<SelectionTestDocument>();
            Documents.Add(ActiveDocument);
            ActiveWindow = new SelectionTestWindow();
        }

        public int BeginUndoScope(string title)
        {
            UndoScopesStarted++;
            throw new InvalidOperationException("Rejected selections must not start mutation.");
        }
    }

    public sealed class SelectionTestDocument
    {
        public string FullName { get; set; }
    }

    public sealed class SelectionTestWindow
    {
        public FakeVisioCollection<FakeVisioShape> Selection { get; private set; }

        public SelectionTestWindow()
        {
            Selection = new FakeVisioCollection<FakeVisioShape>();
        }
    }
}
