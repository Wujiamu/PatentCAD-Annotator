// G16 (R-034) probe stubs.
//
// These replace ONLY the palette neighbours that are not under repair and that
// would otherwise drag in AutoCAD host types: PatPaletteCommand,
// DictPaletteCadService, EditEntryDialog, PasteRecognizeDialog. The real
// DictPaletteControl, ArbitrateDialog, DictPaletteWorkflow and DictConflict are
// compiled from the snapshot under test.
//
// The Autodesk.ApplicationServices types are stubs, exactly as the 2025 test
// project does, so RuntimeHost and DictLoader resolve a fake Document.

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
    /// <summary>Stub of the command dispatcher; the probe never dispatches commands.</summary>
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

    public static class DictPaletteCadService
    {
        public static DictPaletteRenameResult RenameNumber(
            Autodesk.AutoCAD.ApplicationServices.Document doc, string oldNumber, string newNumber)
        {
            return new DictPaletteRenameResult();
        }

        public static DictPaletteDeleteResult DeleteAll(Autodesk.AutoCAD.ApplicationServices.Document doc)
        {
            return new DictPaletteDeleteResult();
        }
    }

    /// <summary>
    /// The two entry dialogs are not part of R-034; they only need to exist so
    /// DictPaletteControl compiles. The probe never shows them.
    /// </summary>
    public class EditEntryDialog : Form
    {
        public EditEntryDialog(DictModel model, DictEntry entry, string dictPath, object document, string identity) { }
    }

    public class PasteRecognizeDialog : Form
    {
        public PasteRecognizeDialog(DictModel model, string dictPath, object document, string identity) { }
    }
}
