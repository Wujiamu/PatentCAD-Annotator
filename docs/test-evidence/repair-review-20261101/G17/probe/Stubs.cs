// G17 (R-004) probe stubs.
//
// Replaces ONLY the palette neighbours that are not under repair and that would
// otherwise drag in AutoCAD host types:
//
//   * PatPaletteCommand / DictPaletteCadService  -> counted recording stubs, so
//     the probe can assert "how many times did the renumber service actually run"
//     (that IS the sealed red light: expected_calls=0|actual_calls=1).
//   * PasteRecognizeDialog -> must exist so DictPaletteControl compiles.
//
// The real DictPaletteControl, EditEntryDialog and DictPaletteSession are
// compiled from the snapshot under test. Autodesk ApplicationServices types are
// stubs, exactly as the 2025 test project does.

using System;
using System.Collections.Generic;
using System.Windows.Forms;
using PatentMarker.IO;

namespace Autodesk.AutoCAD.ApplicationServices
{
    public static class Application
    {
        public static DocumentManager DocumentManager { get; } = new DocumentManager();
    }

    public class DocumentManager
    {
        public Document MdiActiveDocument => null;
    }

    public class Document
    {
        public string Name { get; set; } = "";
        public Editor Editor { get; set; } = new Editor();
    }

    public class Editor
    {
        public void WriteMessage(string message) { }
        public void Regen() { }
    }
}

namespace PatentMarker
{
    public static class PatentMarkerApp
    {
        internal static void RawLog(string msg) { }
    }
}

namespace PatentMarker.Palette
{
    public static class PatPaletteCommand
    {
        public static bool HasLeader { get; set; } = true;
        public static bool UnderlineText { get; set; }
        public static bool HasArrowHead { get; set; }
        public static bool IsSplined { get; set; } = true;
        public static bool ThreePointMode { get; set; } = true;
        public static double TextHeight { get; set; } = 3.5;
        public static double ArrowSize { get; set; } = 2.5;

        public static void TryDispatchPendingForCurrentDocument() { }
        public static bool RequestPanelCommand(Autodesk.AutoCAD.ApplicationServices.Document doc, string command) { return true; }
        public static bool RequestMark(Autodesk.AutoCAD.ApplicationServices.Document doc, string number, string name) { return true; }
    }

    public sealed class DictPaletteRenameResult
    {
        public int Matched;
        public int Changed;
        public bool Committed;
        public readonly List<string> Failures = new List<string>();
        public string Error = "";
        public int Failed { get { return Matched - Changed; } }
        public bool Complete { get { return Committed && Matched > 0 && Failed == 0; } }
    }

    public sealed class DictPaletteDeleteResult
    {
        public int Deleted;
        public int Skipped;
    }

    /// <summary>
    /// Recording stub. The probe asserts on <see cref="Calls"/>: a call the
    /// repair must not make is a FAIL line, not a silently tolerated write.
    /// </summary>
    public static class DictPaletteCadService
    {
        public static readonly List<string> Calls = new List<string>();
        public static readonly Queue<DictPaletteRenameResult> Queued = new Queue<DictPaletteRenameResult>();

        public static void Reset()
        {
            Calls.Clear();
            Queued.Clear();
        }

        public static void Enqueue(DictPaletteRenameResult result) { Queued.Enqueue(result); }

        public static DictPaletteRenameResult RenameNumber(
            Autodesk.AutoCAD.ApplicationServices.Document doc, string oldNumber, string newNumber)
        {
            Calls.Add(oldNumber + ">" + newNumber);
            if (Queued.Count == 0) return new DictPaletteRenameResult { Matched = 0, Committed = true };
            return Queued.Dequeue();
        }

        public static DictPaletteDeleteResult DeleteAll(Autodesk.AutoCAD.ApplicationServices.Document doc)
        {
            return new DictPaletteDeleteResult();
        }
    }

    /// <summary>
    /// The paste dialog is not part of R-004; it only needs to exist so
    /// DictPaletteControl compiles. The probe never shows it.
    /// </summary>
    public class PasteRecognizeDialog : Form
    {
        public PasteRecognizeDialog(DictModel model, string dictPath, object document, string identity) { }
    }
}
