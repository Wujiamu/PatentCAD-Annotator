// G24 / R-004 (补修) language-level gate stubs.
//
// This is the G21 gate stub file with the members G24 additionally references:
// RenameCommitFact / RenameSnapshotRead / RenameSyncContext.DescribeContextDrift
// / DictPaletteWorkflow.ResolveCanonicalPath + ReadSnapshot + ReadCanonicalSnapshot
// / PendingRenameOperation.NeedsReconfirm / PendingRenameRegistry.MarkNeedsReconfirm
// + NeedsReconfirm / RenameRetryPlanner.DecideOnBytes + FindEntry.
//
// Nothing here implements production behaviour; it only satisfies the type
// references the gated files make, so a C# 3+ construct (auto-property, LINQ,
// Func<>, lambda, var, object initializer) or a post-2.0 BCL call added by this
// fix fails here instead of in a legacy edition that cannot be built on this
// machine.
//
// The gated files are the three G24 touched that were ALREADY C# 2.0 clean:
//   DictPaletteSession.cs, EditEntryDialog.cs, DictPaletteWorkflow.cs
// DictPaletteControl.cs is NOT gated: it already contained `var` (C# 3+) before
// this package, in exactly the same count as after (11), so it is covered by the
// real five-edition build instead. See G24.md §7.
//
// DictConflict.cs is compiled here only because DictPaletteWorkflow.cs references
// it and G24 must not change it (G20 finalized it).

using System;
using System.Collections.Generic;

namespace PatentMarker
{
    public static class PatentMarkerApp
    {
        internal static void RawLog(string msg) { }
    }
}

namespace PatentMarker.I18n
{
    public enum Language { Chinese, English }

    public static class Strings
    {
        public static Language Lang = Language.Chinese;
        public static string Conflict_Title { get { return "t"; } }
        public static string Conflict_Msg { get { return "{0}"; } }
        public static string Conflict_BtnKeepWord { get { return "k"; } }
        public static string Conflict_BtnRestoreCad { get { return "r"; } }
        public static string Conflict_BtnLater { get { return "l"; } }
        public static string Conflict_Failed { get { return "{0}"; } }
        public static string Conflict_RestoreOk { get { return "ok"; } }
        public static string Conflict_KeepWordOk { get { return "ok"; } }
        public static string Conflict_StatusDetected { get { return "d"; } }
        public static string Dict_Busy { get { return "busy"; } }
        public static string Dict_Changed { get { return "changed"; } }
        public static string Dict_Pending { get { return "pending"; } }
        public static string Dict_BaselineFailed { get { return "baseline"; } }
        public static string Status_DictLoaded { get { return "loaded"; } }
        public static string Status_Ready { get { return "ready"; } }

        // --- added for G21 (DictPaletteSession / EditEntryDialog) ---
        public static string Status_NumberSyncFailed { get { return "failed: {0}"; } }
        public static string Status_DictChangedDrawingPending { get { return "{0}->{1}: {2}"; } }
        public static string Edit_TitleEdit { get { return "edit"; } }
        public static string Edit_TitleAdd { get { return "add"; } }
        public static string Edit_Number { get { return "number"; } }
        public static string Edit_Name { get { return "name"; } }
        public static string Edit_BtnSave { get { return "save"; } }
        public static string Edit_BtnDelete { get { return "delete"; } }
        public static string Edit_DeleteConfirm { get { return "confirm"; } }
        public static string Edit_DeleteFailed { get { return "delete failed"; } }
        public static string Edit_NumberConflict { get { return "conflict: {0}"; } }
        public static string Edit_EmptyField { get { return "empty"; } }
        public static string Paste_BtnCancel { get { return "cancel"; } }
        public static string Diff_Summary { get { return "diff {0}"; } }
    }
}

namespace PatentMarker.IO
{
    public class DictMetadata
    {
        public string SourceFile = "";
        public string ExtractedAt = "";
        public string ModifiedBy;
        public string ModifiedAt;
    }

    public class DictEntry
    {
        public string Number = "";
        public string Name = "";
        public int Occurrences;
        public List<string> Conflicts;
    }

    public class DictModel
    {
        public DictMetadata Metadata = new DictMetadata();
        public List<DictEntry> Entries = new List<DictEntry>();
        public List<string> Warnings = new List<string>();
    }

    public enum DiffStatus { Added = 0, Removed = 1, Changed = 2, Same = 3 }

    public class DictDiffEntry
    {
        public DiffStatus Status;
        public DictEntry OldEntry;
        public DictEntry NewEntry;
    }

    public static class DictDiff
    {
        public static List<DictDiffEntry> Compute(DictModel previous, DictModel current) { return null; }
    }

    public static class NumberIdentity
    {
        public static bool AreEqual(string a, string b) { return a == b; }
    }

    public static class DictLoader
    {
        public static string CurrentPath { get { return null; } }
        public static DictModel PreviousModel { get { return null; } }
        public static bool IsFileChanged() { return false; }
        public static DictModel LoadForCurrentDrawing() { return null; }
        public static void InvalidateCache() { }
        public static string GetIdentity(DictModel model, string path) { return null; }
        public static void NotifySelfWrite(DictModel model, string path, string identity) { }
        public static DictModel Load(string path) { return null; }
        internal static DictModel LoadBytes(string path, byte[] bytes) { return null; }
        public static string ResolveDictPath() { return null; }
        public static void ReleaseForDrawing(string drawingPath) { }
    }

    public static class DictWriter
    {
        public static string Serialize(DictModel model) { return ""; }
        public static bool Write(string path, DictModel model, string expectedIdentity, out string error)
        { error = ""; return true; }
        public static bool TryApplyEdit(DictModel model, DictEntry entry, string newNumber,
            string newName, out string conflictNumber)
        { conflictNumber = null; return true; }
        public static bool TryRemoveEntry(DictModel model, DictEntry entry) { return true; }
    }

    public static class RuntimeHost
    {
        public static object ActiveDocument { get { return null; } }
    }

    public static class DictFileVisibility
    {
        public const string MissingIdentity = "<missing>";
        public const string BusyError = "PATENTMARKER_LOCK_BUSY";
        public const string ChangedError = "PATENTMARKER_CONTENT_CHANGED";
        public const string PendingError = "PATENTMARKER_CONFLICT_PENDING";
        public const string BaselineError = "PATENTMARKER_BASELINE_FAILED";
        public const string BaselineHeader = "PATENTMARKER_WORD_BASELINE_V1\n";

        public static string Identity(byte[] bytes) { return ""; }
        public static string ReadIdentity(string path) { return ""; }
        public static string DecodeText(byte[] bytes) { return ""; }
        public static string NormalizeForBaseline(string content) { return ""; }
        public static string BaselineDigest(string content) { return ""; }
        public static bool TryWriteBaseline(string path, string json, out string error) { error = ""; return true; }
        public static System.IO.FileStream AcquireLock(string path, out string error) { error = ""; return null; }
        public static bool TryWriteAtomic(string path, string json, out string error) { error = ""; return true; }
        public static bool IsVisible(string path) { return false; }
        public static bool TrySetVisible(string path, bool visible, out string error) { error = ""; return true; }
    }
}
