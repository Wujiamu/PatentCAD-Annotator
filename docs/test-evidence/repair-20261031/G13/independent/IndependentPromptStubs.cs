using System.Collections;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace Autodesk.AutoCAD.EditorInput
{
    public sealed class PromptKeywordOptions
    {
        public PromptKeywordOptions(string message) { Keywords = new KeywordCollection(); }
        public KeywordCollection Keywords { get; private set; }
    }
    public sealed class KeywordCollection
    {
        public string Default { get; set; }
        public void Add(string value) { }
    }
    public sealed class PromptResult
    {
        public PromptStatus Status { get; set; }
        public string StringResult { get; set; }
    }
    public sealed class PromptDoubleOptions
    {
        public PromptDoubleOptions(string message) { }
        public bool AllowNone { get; set; }
        public double DefaultValue { get; set; }
    }
    public sealed class PromptDoubleResult
    {
        public PromptStatus Status { get; set; }
        public double Value { get; set; }
    }
    public sealed class SelectedObject
    {
        public ObjectId ObjectId { get; set; }
    }
    public sealed class SelectionSet : IEnumerable<SelectedObject>
    {
        private readonly List<SelectedObject> _items = new List<SelectedObject>();
        public SelectionSet(ObjectId id) { _items.Add(new SelectedObject { ObjectId = id }); }
        public int Count { get { return _items.Count; } }
        public IEnumerator<SelectedObject> GetEnumerator() { return _items.GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }
    public sealed class PromptSelectionResult
    {
        public PromptStatus Status { get; set; }
        public SelectionSet Value { get; set; }
    }
    public sealed partial class Editor
    {
        private readonly Queue<PromptResult> _keywords = new Queue<PromptResult>();
        private readonly Queue<PromptDoubleResult> _doubles = new Queue<PromptDoubleResult>();
        private readonly Queue<PromptSelectionResult> _selections = new Queue<PromptSelectionResult>();
        public void EnqueueKeyword(string value) { _keywords.Enqueue(new PromptResult { Status = PromptStatus.OK, StringResult = value }); }
        public void EnqueueDouble(double value) { _doubles.Enqueue(new PromptDoubleResult { Status = PromptStatus.OK, Value = value }); }
        public void EnqueueSelection(ObjectId id) { _selections.Enqueue(new PromptSelectionResult { Status = PromptStatus.OK, Value = new SelectionSet(id) }); }
        public PromptResult GetKeywords(PromptKeywordOptions options) { return _keywords.Dequeue(); }
        public PromptDoubleResult GetDouble(PromptDoubleOptions options) { return _doubles.Dequeue(); }
        public PromptSelectionResult GetSelection() { return _selections.Dequeue(); }
    }
}
