using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Xml;
using PatentOffice.Shared;
using PatentOffice.PowerPoint;
using Xunit;

namespace PatentOffice.PowerPoint.CodeTests
{
    /// <summary>
    /// G10 (R-008 / R-024) L1 harness for PowerPoint. The production <c>PaletteForm</c>,
    /// <c>PresentationBinding</c> and <c>DictionaryReader</c> run unmodified; only the PowerPoint
    /// COM boundary is replaced with in-memory objects. The test project does not reference
    /// System.Windows.Forms, so the form and its controls are driven through reflection.
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

    /// <summary>A CustomXMLPart stand-in: the product only reads <c>XML</c> and deletes parts.</summary>
    public sealed class G10CustomXmlPart
    {
        public G10CustomXmlPart(G10CustomXmlParts owner, string xml)
        {
            Owner = owner;
            Xml = xml;
        }

        public G10CustomXmlParts Owner { get; private set; }
        public string Xml { get; set; }

        public string XML { get { return Xml; } }

        public void Delete()
        {
            Owner.Remove(this);
        }
    }

    public sealed class G10CustomXmlParts
    {
        private readonly List<G10CustomXmlPart> _parts = new List<G10CustomXmlPart>();

        public int Count { get { return _parts.Count; } }

        public G10CustomXmlPart Item(int index) { return _parts[index - 1]; }

        public G10CustomXmlParts SelectByNamespace(string namespaceUri)
        {
            G10CustomXmlParts selected = new G10CustomXmlParts();
            for (int i = 0; i < _parts.Count; i++)
            {
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                document.LoadXml(_parts[i].Xml);
                if (document.DocumentElement != null &&
                    string.Equals(document.DocumentElement.NamespaceURI, namespaceUri, StringComparison.Ordinal))
                    selected._parts.Add(_parts[i]);
            }
            return selected;
        }

        public G10CustomXmlPart Add(string xml, object loadOption)
        {
            G10CustomXmlPart part = new G10CustomXmlPart(this, xml);
            _parts.Add(part);
            return part;
        }

        public void Remove(G10CustomXmlPart part)
        {
            _parts.Remove(part);
        }
    }

    public sealed class G10TagCollection
    {
        private readonly List<KeyValuePair<string, string>> _tags = new List<KeyValuePair<string, string>>();

        public int Count { get { return _tags.Count; } }

        public void Add(string name, string value)
        {
            _tags.Add(new KeyValuePair<string, string>(name, value));
        }

        public string Name(int index) { return _tags[index - 1].Key; }

        public string Value(int index) { return _tags[index - 1].Value; }
    }

    public sealed class G10Shape
    {
        public G10Shape()
        {
            GroupItems = new G10ShapeCollection();
            Tags = new G10TagCollection();
        }

        public int Type { get; set; }
        public string Name { get; set; }
        public string VisibleText { get; set; }
        public G10ShapeCollection GroupItems { get; private set; }
        public G10TagCollection Tags { get; private set; }

        public G10TextFrame TextFrame { get { return new G10TextFrame(this); } }

        // Line-related members the product touches while creating an annotation.
        public float Left { get; set; }
        public float Top { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }
        public float Rotation { get; set; }
        public bool HorizontalFlip { get; set; }
        public bool VerticalFlip { get; set; }
        public G10LineFormat Line { get { return new G10LineFormat(); } }
        public G10FillFormat Fill { get { return new G10FillFormat(); } }
        public object Parent { get; set; }
    }

    public sealed class G10LineFormat
    {
        public int BeginArrowheadStyle { get; set; }
        public int EndArrowheadStyle { get; set; }
        public int Visible { get; set; }
    }

    public sealed class G10FillFormat
    {
        public int Visible { get; set; }
    }
    /// <summary>Shape.TextFrame.TextRange.Text, the way a real text box exposes its visible text.</summary>
    public sealed class G10TextFrame
    {
        public G10TextFrame(G10Shape owner) { TextRange = new G10TextRange(owner); }
        public G10TextRange TextRange { get; private set; }

        // The product also sets these while creating a label; the fake accepts and records them.
        public float VerticalAnchor { get; set; }
        public float MarginLeft { get; set; }
        public float MarginRight { get; set; }
        public float MarginTop { get; set; }
        public float MarginBottom { get; set; }
    }

    public sealed class G10TextRange
    {
        private readonly G10Shape _owner;

        public G10TextRange(G10Shape owner) { _owner = owner; }

        public string Text
        {
            get { return _owner.VisibleText ?? ""; }
            set { _owner.VisibleText = value; }
        }

        public G10Font Font { get { return new G10Font(_owner); } }
        public G10ParagraphFormat ParagraphFormat { get { return new G10ParagraphFormat(_owner); } }
    }

    public sealed class G10Font
    {
        public G10Font(G10Shape owner) { Owner = owner; }
        public G10Shape Owner { get; private set; }
        public float Size { get; set; }
    }

    public sealed class G10ParagraphFormat
    {
        public G10ParagraphFormat(G10Shape owner) { Owner = owner; }
        public G10Shape Owner { get; private set; }
        public float Alignment { get; set; }
    }

    public sealed class G10ShapeCollection
    {
        private readonly List<G10Shape> _shapes = new List<G10Shape>();

        public G10ShapeCollection() { }

        public G10ShapeCollection(G10Slide slide) { Slide = slide; }

        public G10Slide Slide { get; private set; }

        public int Count { get { return _shapes.Count; } }
        public G10Shape Item(int index) { return _shapes[index - 1]; }
        public void Add(G10Shape shape) { _shapes.Add(shape); }

        /// <summary>The product creates the label through Slide.Shapes.AddTextbox.</summary>
        public G10Shape AddTextbox(int orientation, float left, float top, float width, float height)
        {
            G10Shape label = new G10Shape
            {
                Type = 17,
                Name = "Label " + (Count + 1),
                Left = left,
                Top = top,
                Parent = Slide
            };
            Add(label);
            return label;
        }

        /// <summary>The product groups a line with its new label through Shapes.Range(names).</summary>
        public G10ShapeCollection Range(object names)
        {
            object[] requested = (object[])names;
            G10ShapeCollection selected = new G10ShapeCollection(Slide);
            for (int i = 0; i < requested.Length; i++)
            {
                string name = Convert.ToString(requested[i]);
                for (int j = 0; j < _shapes.Count; j++)
                    if (string.Equals(_shapes[j].Name, name, StringComparison.Ordinal)) selected.Add(_shapes[j]);
            }
            return selected;
        }

        public G10Shape Group()
        {
            G10Shape group = new G10Shape { Type = 6, Name = "Group " + (Slide == null ? 0 : Slide.SlideID) };
            for (int i = 0; i < _shapes.Count; i++) group.GroupItems.Add(_shapes[i]);
            if (Slide != null)
            {
                string number = _shapes.Count > 0 ? _shapes[_shapes.Count - 1].VisibleText : null;
                Slide.LastAnnotationNumber = number;
            }
            return group;
        }
    }

    public sealed class G10Slide
    {
        public G10Slide() { Shapes = new G10ShapeCollection(this); }
        public int SlideID { get; set; }
        public object Parent { get; set; }
        public G10ShapeCollection Shapes { get; private set; }

        /// <summary>Records the number the product wrote onto the label of the last annotation.</summary>
        public string LastAnnotationNumber { get; set; }
    }

    public sealed class G10SlideCollection
    {
        private readonly List<G10Slide> _slides = new List<G10Slide>();

        public G10SlideCollection() { }

        public G10SlideCollection(G10Presentation owner) { Owner = owner; }

        public G10Presentation Owner { get; private set; }

        public int Count { get { return _slides.Count; } }
        public G10Slide Item(int index) { return _slides[index - 1]; }

        public void Add(G10Slide slide)
        {
            if (Owner != null) slide.Parent = Owner;
            _slides.Add(slide);
        }
    }

    public sealed class G10Presentation
    {
        public G10Presentation(string fullName)
        {
            FullName = fullName;
            CustomXMLParts = new G10CustomXmlParts();
            Slides = new G10SlideCollection(this);
        }

        public string FullName { get; set; }
        public G10CustomXmlParts CustomXMLParts { get; private set; }
        public G10SlideCollection Slides { get; private set; }
    }

    public sealed class G10PresentationCollection
    {
        private readonly List<G10Presentation> _presentations = new List<G10Presentation>();

        public int Count { get { return _presentations.Count; } }
        public G10Presentation Item(int index) { return _presentations[index - 1]; }
        public void Add(G10Presentation presentation) { _presentations.Add(presentation); }
    }

    public sealed class G10PptApplication
    {
        public G10PptApplication()
        {
            Presentations = new G10PresentationCollection();
        }

        public G10PresentationCollection Presentations { get; private set; }
        public G10Presentation ActivePresentation { get; set; }

        /// <summary>Number of times the panel reached into the host UI to create an annotation.</summary>
        public int MarkAttempts { get; private set; }

        public object ActiveWindow
        {
            get
            {
                MarkAttempts++;
                return new G10PptWindow(this);
            }
        }
    }

    /// <summary>The active window/selection the product needs to place a label on the current line.</summary>
    public sealed class G10PptWindow
    {
        private readonly G10PptApplication _application;

        public G10PptWindow(G10PptApplication application)
        {
            _application = application;
            Selection = new G10PptSelection(application);
            View = new G10PptView(application);
        }

        public G10PptSelection Selection { get; private set; }
        public G10PptView View { get; private set; }
    }

    public sealed class G10PptView
    {
        private readonly G10PptApplication _application;

        public G10PptView(G10PptApplication application) { _application = application; }

        public G10Slide Slide
        {
            get { return (G10Slide)_application.ActivePresentation.Slides.Item(1); }
        }
    }

    public sealed class G10PptSelection
    {
        private readonly G10PptApplication _application;

        public G10PptSelection(G10PptApplication application) { _application = application; }

        public int Type { get { return 2; } }  // ppSelectionShapes

        public G10ShapeCollection ShapeRange
        {
            get
            {
                G10Slide slide = (G10Slide)_application.ActivePresentation.Slides.Item(1);
                G10ShapeCollection range = new G10ShapeCollection(slide);
                for (int i = 1; i <= slide.Shapes.Count; i++)
                {
                    G10Shape shape = slide.Shapes.Item(i);
                    if (shape.Type == 9) range.Add(shape);
                }
                return range;
            }
        }
    }

    internal sealed class G10PptPalette : IDisposable
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly string _root;
        private readonly G10PptApplication _application;
        private readonly object _form;
        private readonly Type _paletteType;
        private readonly string _logPath;
        private readonly string _previousLogPath;

        private G10PptPalette(string root)
        {
            _root = root;
            _paletteType = typeof(PowerPointAddIn).Assembly.GetType("PatentOffice.PowerPoint.PaletteForm", true);
            _application = new G10PptApplication();

            Presentation = new G10Presentation(Path.Combine(root, "deck.pptx"));
            File.WriteAllBytes(Presentation.FullName, new byte[0]);
            Slide = new G10Slide { SlideID = 256 };
            Presentation.Slides.Add(Slide);
            _application.Presentations.Add(Presentation);
            _application.ActivePresentation = Presentation;

            // A saved straight line on the slide, so "mark" can complete when the panel allows it.
            G10Shape line = new G10Shape
            {
                Type = 9,
                Name = "Line 1",
                Left = 10f,
                Top = 20f,
                Width = 40f,
                Height = 15f,
                Parent = Slide
            };
            Slide.Shapes.Add(line);

            PowerPointAddIn addIn = new PowerPointAddIn();
            typeof(PowerPointAddIn).GetField("_application", Instance).SetValue(addIn, _application);

            // Keep product diagnostics out of the user profile so log assertions stay isolated.
            FieldInfo logField = typeof(OfficeDiagnostics).GetField("_logPath",
                BindingFlags.Static | BindingFlags.NonPublic);
            _previousLogPath = (string)logField.GetValue(null);
            _logPath = Path.Combine(root, "office-ppt-g10.tsv");
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

        public static G10PptPalette Create()
        {
            string root = Path.Combine(Path.GetTempPath(), "PatentOffice-G10-Ppt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new G10PptPalette(root);
        }

        public G10Presentation Presentation { get; private set; }

        public G10Slide Slide { get; private set; }

        public G10PptApplication Application { get { return _application; } }

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

        /// <summary>The number the panel actually handed to the host for the last annotation.</summary>
        public string LastAnnotationNumber { get { return Slide.LastAnnotationNumber; } }

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
        /// Adds a product annotation group (line + label) carrying a PATNUMBER tag. The label's
        /// visible text matches the tag, which is what the product scanner requires.
        /// </summary>
        public void AddMarkedShape(string number)
        {
            G10Shape group = new G10Shape { Type = 6, Name = "Group " + number };
            group.GroupItems.Add(new G10Shape { Type = 9 });
            group.GroupItems.Add(new G10Shape { Type = 17, VisibleText = number });
            group.Tags.Add("PATMARKER", "PATENTMARKER");
            group.Tags.Add("PATNUMBER", number);
            Slide.Shapes.Add(group);
        }

        /// <summary>
        /// Runs the product's own bind action (the private method both bind entries call), so the
        /// panel state is exactly what a user bind produces rather than a pre-seeded internal state.
        /// </summary>
        public void Bind(string dictionaryPath)
        {
            _application.ActivePresentation = Presentation;
            Invoke("BindDictionaryPath", Presentation, dictionaryPath);
        }

        /// <summary>Binds another presentation through the same product action and leaves it active.</summary>
        public void BindPresentation(G10Presentation presentation, string dictionaryPath)
        {
            _application.ActivePresentation = presentation;
            Invoke("BindDictionaryPath", presentation, dictionaryPath);
        }

        /// <summary>Rewrites only the stored binding: this is what an Undo/Redo of the bind looks like.</summary>
        public void RewriteBinding(string dictionaryPath)
        {
            RemoveBinding();
            Presentation.CustomXMLParts.Add(BindingXml(dictionaryPath), Type.Missing);
        }

        /// <summary>Removes every binding part (Undo of the whole bind action).</summary>
        public void RemoveBinding()
        {
            G10CustomXmlParts selected = Presentation.CustomXMLParts.SelectByNamespace(
                PresentationBinding.NamespaceUri);
            List<G10CustomXmlPart> parts = new List<G10CustomXmlPart>();
            for (int i = 1; i <= selected.Count; i++) parts.Add(selected.Item(i));
            for (int i = 0; i < parts.Count; i++) parts[i].Delete();
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
        /// White-box on purpose: the user path that produced it is a single unrepeatable I/O race.
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

        private string BindingXml(string dictionaryPath)
        {
            string presentationPath = Path.GetFullPath(Presentation.FullName);
            string full = Path.GetFullPath(dictionaryPath);
            string kind;
            string stored;
            if (string.Equals(Path.GetPathRoot(presentationPath), Path.GetPathRoot(full),
                    StringComparison.OrdinalIgnoreCase))
            {
                Uri baseUri = new Uri(Path.GetDirectoryName(presentationPath) + Path.DirectorySeparatorChar);
                stored = Uri.UnescapeDataString(baseUri.MakeRelativeUri(new Uri(full)).ToString())
                    .Replace('/', Path.DirectorySeparatorChar);
                kind = "relative";
            }
            else
            {
                stored = full;
                kind = "absolute";
            }

            XmlDocument document = new XmlDocument();
            document.XmlResolver = null;
            XmlElement root = document.CreateElement("PatentMarkerBinding", PresentationBinding.NamespaceUri);
            root.SetAttribute("version", "1");
            root.SetAttribute("kind", kind);
            root.InnerText = stored;
            document.AppendChild(root);
            return document.OuterXml;
        }

        private object Field(string name)
        {
            FieldInfo field = _paletteType.GetField(name, Instance);
            Assert.True(field != null, "PaletteForm no longer has the field '" + name + "'.");
            return field.GetValue(_form);
        }

        private void SetFieldIfPresent(string name, object value)
        {
            FieldInfo field = _paletteType.GetField(name, Instance);
            if (field != null) field.SetValue(_form, value);
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
