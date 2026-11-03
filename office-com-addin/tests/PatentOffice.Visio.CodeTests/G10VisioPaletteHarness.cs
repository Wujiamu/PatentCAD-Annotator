using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using PatentOffice.Shared;
using PatentOffice.Visio;
using Xunit;

namespace PatentOffice.Visio.CodeTests
{
    /// <summary>
    /// G10 (R-008 / R-024) L1 harness. The production <c>PaletteForm</c>, <c>VisioDocumentBinding</c>
    /// and <c>DictionaryReader</c> run unmodified; only the Visio COM boundary is replaced with
    /// in-memory objects. The test project does not reference System.Windows.Forms, so the form and
    /// its controls are driven through reflection instead of compile-time types.
    /// </summary>
    internal static class G10Sta
    {
        /// <summary>
        /// Runs the body on an STA thread. A bounded join turns a blocked host call (for example a
        /// modal dialog opened by the product's own failure path) into a visible test failure
        /// instead of a hung test run.
        /// </summary>
        public static void Run(Action body)
        {
            ExceptionDispatchInfo failure = null;
            bool completed = false;
            Thread thread = new Thread(delegate()
            {
                try { body(); completed = true; }
                catch (Exception ex) { failure = ExceptionDispatchInfo.Capture(ex); }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            bool finished = thread.Join(TimeSpan.FromSeconds(60));
            if (!finished)
                throw new TimeoutException(
                    "G10 harness: the panel blocked for 60s (likely a modal dialog from a failure path).");
            if (failure != null) failure.Throw();
            Assert.True(completed, "G10 harness: the test body did not complete.");
        }
    }

    public sealed class G10Cell
    {
        public string FormulaU { get; set; }
        public double ResultIU { get; set; }
    }

    /// <summary>Minimal ShapeSheet shape: named rows, cell reads/writes and child shapes.</summary>
    public sealed class G10Shape
    {
        public G10Shape()
        {
            Cells = new Dictionary<string, G10Cell>(StringComparer.OrdinalIgnoreCase);
            Shapes = new FakeVisioCollection<G10Shape>();
        }

        public int OneD { get; set; }
        public int ID { get; set; }
        public string NameU { get; set; }

        private string _text;

        public string Text
        {
            get { return _text; }
            set
            {
                _text = value;
                if (TextChanged != null) TextChanged(value);
            }
        }

        /// <summary>Invoked whenever the product writes the visible label text.</summary>
        public Action<string> TextChanged { get; set; }

        public object ContainingShape { get; set; }
        public object ContainingPage { get; set; }
        public object Document { get; set; }
        public Dictionary<string, G10Cell> Cells { get; private set; }
        public FakeVisioCollection<G10Shape> Shapes { get; private set; }

        public int CellExistsU(string name, int flags)
        {
            return Cells.ContainsKey(name) ? 1 : 0;
        }

        public G10Cell CellsU(string name)
        {
            G10Cell cell;
            if (!Cells.TryGetValue(name, out cell))
            {
                cell = new G10Cell { FormulaU = "0" };
                Cells.Add(name, cell);
            }
            return cell;
        }

        public void AddNamedRow(int section, string rowName, int tag)
        {
            CellsU("User." + rowName + ".Value");
        }
    }

    public sealed class G10Page
    {
        public G10Page()
        {
            Shapes = new FakeVisioCollection<G10Shape>();
            NameU = "Page-1";
            ID = 1;
        }

        public int ID { get; set; }
        public int Background { get; set; }
        public string NameU { get; set; }
        public object Document { get; set; }
        public int RectangleCreations { get; private set; }
        public FakeVisioCollection<G10Shape> Shapes { get; private set; }

        public G10Shape DrawRectangle(double x1, double y1, double x2, double y2)
        {
            RectangleCreations++;
            G10Shape label = new G10Shape
            {
                OneD = 0,
                ID = 9000 + RectangleCreations,
                NameU = "Label." + RectangleCreations,
                ContainingPage = this,
                Document = Document
            };
            label.TextChanged = delegate(string text) { LastAnnotationNumber = text; };
            Shapes.Add(label);
            return label;
        }

        /// <summary>The number actually written onto the last created label, or null if none.</summary>
        public string LastAnnotationNumber { get; set; }
    }

    public sealed class G10Selection
    {
        public G10Selection() { Count = 1; }
        public int Count { get; set; }
        public G10Shape Selected { get; set; }
        public G10Shape Item(int index) { return Selected; }
    }

    public sealed class G10Window
    {
        public G10Window() { Selection = new G10Selection(); }
        public G10Selection Selection { get; private set; }
    }

    public sealed class G10VisioApplication
    {
        public G10VisioApplication()
        {
            Documents = new G10VisioDocumentCollection();
            Window = new G10Window();
            Page = new G10Page();
        }

        public G10VisioDocumentCollection Documents { get; private set; }
        public G10VisioDocument ActiveDocument { get; set; }
        public G10Window Window { get; private set; }
        public G10Page Page { get; private set; }

        /// <summary>Number of times the panel reached into the host UI to create an annotation.</summary>
        public int MarkAttempts { get; private set; }

        public object ActiveWindow
        {
            get
            {
                MarkAttempts++;
                return Window;
            }
        }

        public object ActivePage { get { return Page; } }

        public int BeginUndoScope(string name) { return 1; }
        public void EndUndoScope(int scopeId, bool commit) { }
    }

    public sealed class G10VisioDocumentCollection
    {
        private readonly List<G10VisioDocument> _documents = new List<G10VisioDocument>();

        public int Count { get { return _documents.Count; } }
        public G10VisioDocument Item(int index) { return _documents[index - 1]; }
        public void Add(G10VisioDocument document) { _documents.Add(document); }
    }

    public sealed class G10VisioDocument
    {
        public G10VisioDocument(string fullName, G10VisioApplication application)
        {
            FullName = fullName;
            Name = Path.GetFileName(fullName);
            Application = application;
            DocumentSheet = new G10Shape();
            Pages = new FakeVisioCollection<G10Page>();
            Saved = true;
        }

        public string FullName { get; set; }
        public string Name { get; set; }
        public bool Saved { get; set; }
        public G10VisioApplication Application { get; private set; }
        public G10Shape DocumentSheet { get; private set; }
        public FakeVisioCollection<G10Page> Pages { get; private set; }
    }

    internal sealed class G10VisioPalette : IDisposable
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const int VisSectionUser = 242;
        private static readonly string[] BindingRows =
        {
            "User.PatentMarkerDictPath.Value",
            "User.PatentMarkerDictKind.Value",
            "User.PatentMarkerBindingVersion.Value"
        };

        private readonly string _root;
        private readonly G10VisioApplication _application;
        private readonly object _form;
        private readonly Type _paletteType;
        private readonly string _logPath;
        private readonly string _previousLogPath;

        private G10VisioPalette(string root)
        {
            _root = root;
            _paletteType = typeof(VisioAddIn).Assembly.GetType("PatentOffice.Visio.PaletteForm", true);
            _application = new G10VisioApplication();

            Page = _application.Page;
            Page.Document = null;
            Document = new G10VisioDocument(Path.Combine(root, "drawing.vsdx"), _application);
            Page.Document = Document;
            Document.Pages.Add(Page);
            File.WriteAllBytes(Document.FullName, new byte[0]);
            _application.Documents.Add(Document);
            _application.ActiveDocument = Document;

            Line = new G10Shape { OneD = 1, ID = 1, NameU = "Line.1", ContainingPage = Page, Document = Document };
            Line.CellsU("BeginX").ResultIU = 1.0;
            Line.CellsU("BeginY").ResultIU = 2.0;
            Line.CellsU("EndX").ResultIU = 3.0;
            Line.CellsU("EndY").ResultIU = 4.0;
            Line.CellsU("EndArrow").FormulaU = "0";
            Page.Shapes.Add(Line);
            _application.Window.Selection.Selected = Line;

            VisioAddIn addIn = new VisioAddIn();
            typeof(VisioAddIn).GetField("_application", Instance).SetValue(addIn, _application);

            // Keep product diagnostics out of the user profile so log assertions stay isolated.
            FieldInfo logField = typeof(OfficeDiagnostics).GetField("_logPath",
                BindingFlags.Static | BindingFlags.NonPublic);
            _previousLogPath = (string)logField.GetValue(null);
            _logPath = Path.Combine(root, "office-visio-g10.tsv");
            logField.SetValue(null, _logPath);

            _form = Activator.CreateInstance(_paletteType, AnyInstance, null, new object[] { addIn }, null);
            // ListView.SelectedItems only tracks selection once the control owns a window handle,
            // so create it without ever showing the form (the product path never shows it here).
            ForceHandle(Field("_entries"));
        }

        private static void ForceHandle(object control)
        {
            Assert.True(control != null, "PaletteForm entry list is missing.");
            control.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.Public)
                .GetValue(control, null);
        }

        public static G10VisioPalette Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "PatentOffice-G10-Visio-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new G10VisioPalette(root);
        }

        public G10VisioDocument Document { get; private set; }

        public G10Page Page { get; private set; }

        public G10Shape Line { get; private set; }

        public G10VisioApplication Application { get { return _application; } }

        public string Root { get { return _root; } }

        public string StatusText { get { return ControlText(Field("_statusLabel")); } }

        public string BindingText { get { return ControlText(Field("_bindingLabel")); } }

        public string PathInputText { get { return ControlText(Field("_pathInput")); } }

        public string BoundPath { get { return (string)Field("_boundPath"); } }

        public bool MarkEnabled { get { return ControlEnabled(Field("_markButton")); } }

        public bool CheckEnabled { get { return ControlEnabled(Field("_checkButton")); } }

        public bool DictionaryCurrent { get { return (bool)Field("_dictionaryCurrent"); } }

        public int EntryCount { get { return CollectionCount(Property(Field("_entries"), "Items")); } }

        public int SelectedCount
        {
            get { return CollectionCount(Property(Field("_entries"), "SelectedItems")); }
        }

        public int MarkedNumberCount { get { return ((HashSet<string>)Field("_markedNumbers")).Count; } }

        public int MarkAttempts { get { return _application.MarkAttempts; } }

        public int AnnotationCreations { get { return Page.RectangleCreations; } }

        /// <summary>The number the panel actually handed to the host for the last annotation.</summary>
        public string LastAnnotationNumber { get { return Page.LastAnnotationNumber; } }

        public string[] RowNumbers()
        {
            object items = Property(Field("_entries"), "Items");
            int count = CollectionCount(items);
            string[] numbers = new string[count];
            for (int i = 0; i < count; i++) numbers[i] = ControlText(Item(items, i));
            return numbers;
        }

        public string WriteDictionary(string name, string json)
        {
            string path = Path.Combine(_root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(json));
            return path;
        }

        public static string DictionaryJson(params string[] numbers)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\"metadata\":{\"version\":\"1\"},\"entries\":[");
            for (int i = 0; i < numbers.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append("{\"number\":\"").Append(numbers[i])
                    .Append("\",\"name\":\"part ").Append(numbers[i]).Append("\",\"occurrences\":1}");
            }
            builder.Append("],\"warnings\":[]}");
            return builder.ToString();
        }

        /// <summary>
        /// Runs the product's own bind action (the private method both bind buttons call), so the
        /// panel state is exactly what a user bind produces rather than a pre-seeded internal state.
        /// </summary>
        public void Bind(string dictionaryPath)
        {
            EnsureBindingCells(Document);
            _application.ActiveDocument = Document;
            Invoke("BindDictionaryPath", Document, dictionaryPath);
        }

        /// <summary>Binds another document through the same product action and leaves it active.</summary>
        public void BindDocument(G10VisioDocument document, string dictionaryPath)
        {
            EnsureBindingCells(document);
            _application.ActiveDocument = document;
            Invoke("BindDictionaryPath", document, dictionaryPath);
        }

        /// <summary>Rewrites only the stored binding: this is what an Undo/Redo of the bind looks like.</summary>
        public void RewriteBinding(string dictionaryPath)
        {
            EnsureBindingCells(Document);
            string kind;
            string stored = BindingPath.ToStoredPath(Document.FullName, dictionaryPath, out kind);
            ShapeSheetString.Write(Document.DocumentSheet, "PatentMarkerDictPath", stored, VisSectionUser);
            ShapeSheetString.Write(Document.DocumentSheet, "PatentMarkerDictKind", kind, VisSectionUser);
            ShapeSheetString.Write(Document.DocumentSheet, "PatentMarkerBindingVersion", "1", VisSectionUser);
        }

        /// <summary>Removes the binding rows entirely (Undo of the whole bind action).</summary>
        public void RemoveBinding()
        {
            for (int i = 0; i < BindingRows.Length; i++) Document.DocumentSheet.Cells.Remove(BindingRows[i]);
        }

        public void Refresh()
        {
            Invoke("RefreshHostState");
        }

        public void ClickMark()
        {
            Invoke("MarkButton_Click", _form, EventArgs.Empty);
        }

        public void ClickCheck()
        {
            Invoke("CheckButton_Click", _form, EventArgs.Empty);
        }

        public void SelectFirstEntry()
        {
            object items = Property(Field("_entries"), "Items");
            Assert.True(CollectionCount(items) > 0, "no dictionary rows to select");
            object first = Item(items, 0);
            first.GetType().GetProperty("Selected").SetValue(first, true, null);
        }

        /// <summary>
        /// Recreates the state R-008 leaves behind when a hash was read and the following content
        /// read failed: a failure recorded against bytes that are still on disk and unchanged.
        /// White-box: this reaches the panel's private failure cache on purpose, because the user
        /// path that produced it is a single unrepeatable I/O race. After the fix the cache is gone,
        /// so this becomes a no-op and the test degenerates to "a readable file is read".
        /// </summary>
        public void SeedLegacyFailureCacheForCurrentBytes()
        {
            SetFieldIfPresent("_lastAttemptedHash", DictionaryReader.ComputeHash(BoundPath));
            SetFieldIfPresent("_lastAttemptSucceeded", false);
            SetFieldIfPresent("_dictionaryCurrent", false);
            SetFieldIfPresent("_lastAttemptError", "IOException:G10 injected transient failure");
        }

        /// <summary>Holds the dictionary open with no sharing so a read fails with a real sharing violation.</summary>
        public FileStream LockDictionary(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        public long LogLength()
        {
            return File.Exists(_logPath) ? new FileInfo(_logPath).Length : 0;
        }

        public string ReadLogSince(long offset)
        {
            if (!File.Exists(_logPath)) return "";
            using (FileStream stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (offset >= stream.Length) return "";
                stream.Seek(offset, SeekOrigin.Begin);
                using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false)))
                    return reader.ReadToEnd();
            }
        }

        public static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int index = 0;
            while (true)
            {
                index = text.IndexOf(needle, index, StringComparison.Ordinal);
                if (index < 0) return count;
                count++;
                index += needle.Length;
            }
        }

        public void Dispose()
        {
            try { Invoke("CloseFromHost"); }
            catch { }
            try { ((IDisposable)_form).Dispose(); }
            catch { }
            try
            {
                typeof(OfficeDiagnostics)
                    .GetField("_logPath", BindingFlags.Static | BindingFlags.NonPublic)
                    .SetValue(null, _previousLogPath);
            }
            catch { }
            try { Directory.Delete(_root, true); }
            catch { }
        }

        private void EnsureBindingCells(G10VisioDocument document)
        {
            for (int i = 0; i < BindingRows.Length; i++)
                if (!document.DocumentSheet.Cells.ContainsKey(BindingRows[i]))
                    document.DocumentSheet.Cells.Add(BindingRows[i], new G10Cell { FormulaU = "\"\"" });
        }

        private object Field(string name)
        {
            FieldInfo field = _paletteType.GetField(name, Instance);
            Assert.True(field != null, "PaletteForm no longer has the field '" + name + "'.");
            return field.GetValue(_form);
        }

        private bool SetFieldIfPresent(string name, object value)
        {
            FieldInfo field = _paletteType.GetField(name, Instance);
            if (field == null) return false;
            field.SetValue(_form, value);
            return true;
        }

        private void Invoke(string methodName, params object[] arguments)
        {
            MethodInfo method = _paletteType.GetMethod(methodName, Instance);
            Assert.True(method != null, "PaletteForm no longer has the method '" + methodName + "'.");
            try
            {
                method.Invoke(_form, arguments);
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException(
                    "PaletteForm." + methodName + " threw: " + ex.InnerException, ex.InnerException);
            }
        }

        private static object Property(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name);
            Assert.True(property != null, target.GetType().Name + " no longer has the property '" + name + "'.");
            return property.GetValue(target, null);
        }

        private static int CollectionCount(object collection)
        {
            return Convert.ToInt32(Property(collection, "Count"));
        }

        private static object Item(object collection, int index)
        {
            PropertyInfo indexer = collection.GetType().GetProperty("Item", new[] { typeof(int) });
            Assert.True(indexer != null, collection.GetType().Name + " no longer has an integer indexer.");
            return indexer.GetValue(collection, new object[] { index });
        }

        private static string ControlText(object control)
        {
            return Convert.ToString(Property(control, "Text"));
        }

        private static bool ControlEnabled(object control)
        {
            return Convert.ToBoolean(Property(control, "Enabled"));
        }
    }
}
