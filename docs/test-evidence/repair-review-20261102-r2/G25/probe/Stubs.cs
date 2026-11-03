// G25 / R-034 (补修) probe stubs.
//
// Replaces ONLY the palette neighbours that are not under repair and that would
// otherwise drag in AutoCAD host types:
//
//   * PatPaletteCommand  -> not needed by the probe; DictPaletteControl only
//     reads its static style flags and calls RequestPanelCommand/RequestMark.
//   * DictPaletteCadService -> recording stub. "The drawing must not be touched"
//     is asserted through Calls.Count, so a forbidden call is a FAIL line, never
//     a silently tolerated write.
//   * PasteRecognizeDialog -> only has to exist so DictPaletteControl compiles.
//
// The real DictPaletteControl / ArbitrateDialog / DictPaletteWorkflow are
// compiled from the snapshot under test. DictLoader, DictWriter, ConfigLoader,
// RuntimeHost, DictConflict, Strings and the view renderer come from the live
// tree in BOTH builds.

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
        internal static readonly List<string> Log = new List<string>();
        internal static void RawLog(string msg) { Log.Add(msg); }
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

    /// <summary>Recording stub: every call is appended, in order.</summary>
    public static class DictPaletteCadService
    {
        public static readonly List<string> Calls = new List<string>();
        public static readonly Queue<DictPaletteRenameResult> Queued = new Queue<DictPaletteRenameResult>();
        public static readonly Queue<Exception> Throws = new Queue<Exception>();

        /// <summary>Changed count of the last completed call (-1 when none ran).</summary>
        public static int LastChanged = -1;

        public static void Reset()
        {
            Calls.Clear();
            Queued.Clear();
            Throws.Clear();
            LastChanged = -1;
        }

        public static void Enqueue(DictPaletteRenameResult result) { Queued.Enqueue(result); }
        public static void EnqueueThrow(Exception error) { Throws.Enqueue(error); }

        public static DictPaletteRenameResult RenameNumber(
            Autodesk.AutoCAD.ApplicationServices.Document doc, string oldNumber, string newNumber)
        {
            Calls.Add(oldNumber + ">" + newNumber);
            LastChanged = -1;
            if (Throws.Count > 0) throw Throws.Dequeue();
            DictPaletteRenameResult result = Queued.Count == 0
                ? new DictPaletteRenameResult { Matched = 0, Committed = true }
                : Queued.Dequeue();
            LastChanged = result.Changed;
            return result;
        }

        public static DictPaletteDeleteResult DeleteAll(Autodesk.AutoCAD.ApplicationServices.Document doc)
        {
            return new DictPaletteDeleteResult();
        }
    }

    /// <summary>Not part of R-034; only needs to exist so DictPaletteControl compiles.</summary>
    public class PasteRecognizeDialog : Form
    {
        public PasteRecognizeDialog(DictModel model, string dictPath, object document, string identity) { }
    }
}
