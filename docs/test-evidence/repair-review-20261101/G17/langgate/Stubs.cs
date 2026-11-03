// Minimal stubs so the changed Shared files compile at LangVersion=2.
// Nothing here implements production behaviour; it only satisfies the type
// references those files make, so a C# 3+ construct in the fix is caught.

using System;
using System.Collections.Generic;
using System.Windows.Forms;

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
        public static string Status_NoDictFile { get { return "nofile"; } }
        public static string Status_LoadFailed { get { return "{0}"; } }
        public static string Status_NumberSynced { get { return "{0} {1} {2}"; } }
        public static string Status_NumberSyncedCmd { get { return "{0} {1} {2}"; } }
        public static string Status_NumberSyncFailed { get { return "{0}"; } }
        public static string Status_DictChangedDrawingPending { get { return "{0} {1} {2}"; } }
        public static string Edit_DeleteFailed { get { return "delete failed"; } }
        public static string Edit_TitleAdd { get { return "add"; } }
        public static string Edit_TitleEdit { get { return "edit"; } }
        public static string Edit_Number { get { return "number"; } }
        public static string Edit_Name { get { return "name"; } }
        public static string Edit_BtnSave { get { return "save"; } }
        public static string Edit_BtnDelete { get { return "delete"; } }
        public static string Edit_DeleteConfirm { get { return "confirm"; } }
        public static string Edit_NumberConflict { get { return "{0}"; } }
        public static string Edit_EmptyField { get { return "empty"; } }
        public static string Paste_BtnCancel { get { return "cancel"; } }
    }
}

namespace PatentMarker.IO
{
    public enum DiffStatus { Unchanged, Added, Removed, Changed }

    public class DictDiffEntry
    {
        public DiffStatus Status;
        public DictEntry OldEntry;
        public DictEntry NewEntry;
    }

    public class DictMetadata
    {
        public string SourceFile = "";
        public string ExtractedAt = "";
        public string ModifiedBy;
        public string ModifiedAt;
    }

    public class ConflictInfo
    {
        public string Number = "";
        public List<string> Candidates = new List<string>();
    }

    public class DictEntry
    {
        public string Number = "";
        public string Name = "";
        public int Occurrences;
        public List<ConflictInfo> Conflicts = new List<ConflictInfo>();
    }

    public static class DictDiff
    {
        public static List<DictDiffEntry> Compute(DictModel previous, DictModel current)
        { return new List<DictDiffEntry>(); }
    }

    public class DictModel
    {
        public DictMetadata Metadata = new DictMetadata();
        public List<DictEntry> Entries = new List<DictEntry>();
        public List<string> Warnings = new List<string>();
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
    }

    public static class DictWriter
    {
        public static string Serialize(DictModel model) { return ""; }
        public static bool TryApplyEdit(DictModel model, DictEntry entry, string newNumber,
            string newName, out string conflict) { conflict = null; return true; }
        public static bool TryRemoveEntry(DictModel model, DictEntry entry) { return true; }
        public static bool Write(string path, DictModel model, string expectedIdentity,
            out string error) { error = ""; return true; }
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

namespace PatentMarker.Palette
{
    public sealed class DictPaletteWorkflow
    {
        public static bool IsCurrentContext(object document, string path) { return true; }
        public static string DescribeWriteError(string error) { return error; }
    }
}
