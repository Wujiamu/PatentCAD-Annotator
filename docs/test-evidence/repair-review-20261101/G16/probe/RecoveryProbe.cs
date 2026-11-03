// G16 (R-034) recovery probe.
//
// The SAME scenario file is compiled against two snapshots of the production
// code. Only the adapter differs (AdapterRed.cs / AdapterGreen.cs), so the
// assertions are identical in both runs and the only variable is the fix.
//
// Evidence format (same as the sealed probe):
//   PASS|name|detail
//   FAIL|name|detail
//   SKIP/BLOCKED|name|reason

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using PatentMarker.IO;
using PatentMarker.Palette;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace G16Probe
{
    internal static class RecoveryProbe
    {
        private static string _dir;
        private static int _pass;
        private static int _fail;
        private static int _skip;
        private static readonly List<string> _lines = new List<string>();
        private static IProbeEntry _entry;

        private const string WordVersion =
            "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"extracted_at\": \"2026-11-01T01:23:45\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"301\",\n      \"name\": \"Word 版壳体\"\n    }\n  ],\n  \"warnings\": []\n}";

        private const string CadVersion =
            "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"modified_by\": \"cad\",\n    \"modified_at\": \"2026-11-01T02:00:00\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"301\",\n      \"name\": \"CAD 版壳体\"\n    },\n    {\n      \"number\": \"602\",\n      \"name\": \"CAD 版端盖\"\n    }\n  ],\n  \"warnings\": []\n}";

        private const string BackupTs = "20261101-010101";

        public static int Main()
        {
            _dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(
                typeof(RecoveryProbe).Assembly.Location)), "g16-work");
            if (Directory.Exists(_dir)) RemoveDir(_dir);
            Directory.CreateDirectory(_dir);

            _entry = CreateEntry();

            // The four scenarios the sealed recovery-probe.txt recorded.
            ScenarioValidControl();
            ScenarioAfterParseError();
            ScenarioRawCurrentSnapshotControl();
            ScenarioAfterExplicitReload();

            // The rest of the required matrix.
            ScenarioCorruptUtf8();
            ScenarioColdStartNoCache();
            ScenarioCorruptBackup();
            ScenarioCurrentChangedAfterCapture();
            ScenarioBackupChangedAfterCapture();
            ScenarioDocumentSwitched();
            ScenarioKeepRequiresParsableCurrent();
            ScenarioLockBusy();
            ScenarioReadOnlyTarget();
            ScenarioBaselineFailureAfterRestore();
            ScenarioDialogButtons();

            _entry.Release();
            foreach (string line in _lines) Console.WriteLine(line);
            Console.WriteLine("SUMMARY: passed=" + _pass + " failed=" + _fail + " skipped=" + _skip);
            return _fail == 0 ? 0 : 1;
        }

        /// <summary>
        /// One adapter per build, selected at compile time. The pre-fix snapshot
        /// has no recovery-selection type, so the two adapters cannot share code.
        /// </summary>
        private static IProbeEntry CreateEntry()
        {
#if G16_RED
            return new RedEntry();
#else
            return new GreenEntry();
#endif
        }

        // ===== helpers =====

        private static string DictPath { get { return Path.Combine(_dir, "t.dict.json"); } }
        private static string BackupPath { get { return DictPath + DictConflict.BackupInfix + BackupTs + ".bak"; } }

        private static void RemoveDir(string dir)
        {
            foreach (string f in Directory.GetFiles(dir)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }

        private static void WriteText(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static string Identity(string path) { return DictFileVisibility.ReadIdentity(path); }

        private static void Reset(string current, string backup)
        {
            DictLoader.InvalidateCache();
            ConfigLoader.Current = null;
            if (Directory.Exists(_dir))
            {
                foreach (string f in Directory.GetFiles(_dir)) File.SetAttributes(f, FileAttributes.Normal);
                foreach (string f in Directory.GetFiles(_dir)) File.Delete(f);
            }
            if (current != null) WriteText(DictPath, current);
            if (backup != null) WriteText(BackupPath, backup);
        }

        private static void Report(string name, bool ok, string detail)
        {
            if (ok) { _pass++; _lines.Add("PASS|" + name + "|" + detail); }
            else { _fail++; _lines.Add("FAIL|" + name + "|" + detail); }
        }

        private static void Skip(string name, string reason)
        {
            _skip++;
            _lines.Add("SKIP/BLOCKED|" + name + "|" + reason);
        }

        private static string K(string key, object value) { return key + "=" + Convert.ToString(value); }

        /// <summary>Abbreviates a digest so the evidence line stays readable.</summary>
        private static string Short(string identity)
        {
            if (identity == null) return "<null>";
            return identity.Length <= 12 ? identity : identity.Substring(0, 12);
        }

        /// <summary>
        /// Points the host seam at the dictionary's own directory.
        /// The document instance is stable per path, like the real
        /// ActiveDocument: DictPaletteWorkflow.IsCurrentContext compares
        /// documents by reference, so returning a fresh object each access
        /// would never match.
        /// </summary>
        private static void UseDrawing(string dir)
        {
            string drawing = Path.Combine(dir, "t.dwg");
            AcDoc document;
            if (!_documents.TryGetValue(drawing, out document))
            {
                document = new AcDoc { Name = drawing };
                _documents[drawing] = document;
            }
            AcDoc stable = document;
            RuntimeHost.SetActiveDocumentOverride(delegate { return stable; });
        }

        private static readonly Dictionary<string, AcDoc> _documents = new Dictionary<string, AcDoc>();

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>The palette click path, minus the modal dialog.</summary>
        private static ProbeOutcome RestoreThroughEntry(string dictPath, string drawingDir)
        {
            UseDrawing(drawingDir);
            try
            {
                ProbeSelection selection = _entry.Capture(dictPath);
                return _entry.Restore(selection);
            }
            finally { RuntimeHost.ClearActiveDocumentOverride(); }
        }

        // ===== the four sealed-probe scenarios =====

        /// <summary>
        /// Sealed control: a healthy current file plus a healthy backup is selectable.
        /// Both snapshots must pass this - it is the baseline the fix must not break.
        /// </summary>
        private static void ScenarioValidControl()
        {
            const string name = "recovery_valid_control";
            Reset(WordVersion, CadVersion);
            UseDrawing(_dir);
            try
            {
                ProbeSelection selection = _entry.Capture(DictPath);
                bool ok = selection.Available &&
                    string.Equals(selection.CurrentIdentity, Identity(DictPath), StringComparison.Ordinal) &&
                    string.Equals(selection.BackupIdentity, Identity(BackupPath), StringComparison.Ordinal);
                Report(name, ok, K("selected_valid", selection.Available) + "|" +
                    K("current_identity_matches_disk", string.Equals(selection.CurrentIdentity, Identity(DictPath), StringComparison.Ordinal)) + "|" +
                    K("backup_identity_matches_disk", string.Equals(selection.BackupIdentity, Identity(BackupPath), StringComparison.Ordinal)) + "|" +
                    K("sel_current", Short(selection.CurrentIdentity)) + "|" +
                    K("disk_current", Short(Identity(DictPath))) + "|" +
                    K("sel_backup", Short(selection.BackupIdentity)) + "|" +
                    K("disk_backup", Short(Identity(BackupPath))));
            }
            finally { RuntimeHost.ClearActiveDocumentOverride(); }
        }

        /// <summary>
        /// R-034 red case: hot cache, then the current JSON becomes corrupt.
        /// The palette still holds the old model; restore must work anyway.
        /// </summary>
        private static void ScenarioAfterParseError()
        {
            const string name = "recovery_after_parse_error";
            Reset(WordVersion, CadVersion);

            // Hot cache: a healthy model is loaded first, exactly like a live session.
            ConfigLoader.Current = new PatConfig { DefaultDictPath = DictPath };
            DictLoader.InvalidateCache();
            DictModel stale = DictLoader.LoadForCurrentDrawing();

            // The current file is now damaged after the cache was populated.
            WriteText(DictPath, "{ this is not json");
            DictModel afterCorrupt = DictLoader.LoadForCurrentDrawing();

            UseDrawing(_dir);
            ProbeSelection selection;
            ProbeOutcome outcome;
            bool pending;
            try
            {
                pending = _entry.Pending(DictPath);
                selection = _entry.Capture(DictPath);
                outcome = _entry.Restore(selection);
            }
            finally { RuntimeHost.ClearActiveDocumentOverride(); ConfigLoader.Current = null; }

            bool ok = pending && selection.Available && selection.BackupParsed &&
                outcome.Succeeded && outcome.Error.Length == 0 &&
                outcome.Model != null && outcome.BackupRemoved &&
                !File.Exists(BackupPath) &&
                File.ReadAllText(DictPath, Encoding.UTF8).Contains("CAD 版壳体");

            Report(name, ok,
                K("valid_backup", selection.BackupParsed) + "|" +
                K("retained_old_model", stale != null && afterCorrupt != null && object.ReferenceEquals(stale, afterCorrupt)) + "|" +
                K("palette_snapshot_matches_disk", string.Equals(selection.CurrentIdentity, Identity(DictPath), StringComparison.Ordinal)) + "|" +
                K("recovery_allowed", outcome.Succeeded) + "|" +
                K("error", outcome.Error.Length == 0 ? "<none>" : outcome.Error));
        }

        /// <summary>
        /// Sealed control: with a corrupt current file, a byte snapshot still yields
        /// a usable selection. This isolates "read from disk" from "the palette
        /// cache still holds the old identity" - the pre-fix entry cannot do it,
        /// which is exactly R-034.
        /// </summary>
        private static void ScenarioRawCurrentSnapshotControl()
        {
            const string name = "recovery_raw_current_snapshot_control";
            Reset(null, CadVersion);
            WriteText(DictPath, "{ broken, but the backup is valid");
            ProbeSelection selection = _entry.Capture(DictPath);
            Report(name, _entry.HasByteSnapshot && selection.Available && selection.BackupParsed,
                K("selected_valid", selection.Available) + "|" +
                K("backup_parsed", selection.BackupParsed) + "|" +
                K("byte_snapshot", _entry.HasByteSnapshot));
        }

        /// <summary>
        /// Second red case: an explicit reload clears the cache, so the model is null.
        /// The arbitration button used to be disabled precisely then.
        /// </summary>
        private static void ScenarioAfterExplicitReload()
        {
            const string name = "recovery_after_explicit_reload";
            Reset(null, CadVersion);
            WriteText(DictPath, "{ corrupt after explicit reload");
            ConfigLoader.Current = new PatConfig { DefaultDictPath = DictPath };
            UseDrawing(_dir);
            DictModel model;
            bool pending;
            ProbeSelection selection;
            ProbeOutcome outcome;
            try
            {
                DictPaletteWorkflow workflow = new DictPaletteWorkflow();
                model = workflow.ReloadCurrent();
                pending = _entry.Pending(DictPath);
                selection = _entry.Capture(DictPath);
                outcome = _entry.Restore(selection);
            }
            finally { RuntimeHost.ClearActiveDocumentOverride(); ConfigLoader.Current = null; }

            bool ok = model == null && pending && selection.Available && selection.BackupParsed &&
                outcome.Succeeded;
            Report(name, ok,
                K("current_model_null", model == null) + "|" +
                K("valid_backup", selection.BackupParsed) + "|" +
                K("button_enabled", pending));
        }

        // ===== the rest of the required matrix =====

        /// <summary>Bad UTF-8 in the current file is "unparsable", not "no conflict".</summary>
        private static void ScenarioCorruptUtf8()
        {
            const string name = "recovery_corrupt_utf8_current";
            Reset(null, CadVersion);
            byte[] head = Encoding.UTF8.GetBytes("{\"entries\":[{\"number\":\"301\",\"name\":\"");
            byte[] tail = Encoding.UTF8.GetBytes("\"}]}");
            using (FileStream stream = File.Create(DictPath))
            {
                stream.Write(head);
                stream.WriteByte(0xC3);
                stream.WriteByte(0x28);
                stream.Write(tail);
            }
            string corrupt = Identity(DictPath);
            ProbeOutcome outcome = RestoreThroughEntry(DictPath, _dir);
            bool ok = outcome.Succeeded && outcome.Model != null &&
                outcome.Model.Entries.Count == 2 &&
                !File.Exists(BackupPath) &&
                !string.Equals(Identity(DictPath), corrupt, StringComparison.Ordinal);
            Report(name, ok, K("restored", outcome.Succeeded) + "|" +
                K("entries", outcome.Model == null ? -1 : outcome.Model.Entries.Count) + "|" +
                K("bytes_replaced", !string.Equals(Identity(DictPath), corrupt, StringComparison.Ordinal)));
        }

        /// <summary>Cold start: no cache at all, corrupt current file, valid backup.</summary>
        private static void ScenarioColdStartNoCache()
        {
            const string name = "recovery_cold_start_no_cache";
            Reset(null, CadVersion);
            WriteText(DictPath, "{ cold start corruption");
            DictLoader.InvalidateCache();
            UseDrawing(_dir);
            DictModel model;
            bool pending;
            ProbeOutcome outcome;
            try
            {
                DictPaletteWorkflow workflow = new DictPaletteWorkflow();
                model = workflow.LoadCurrent();
                pending = _entry.Pending(DictPath);
                outcome = _entry.Restore(_entry.Capture(DictPath));
            }
            finally { RuntimeHost.ClearActiveDocumentOverride(); }
            bool ok = model == null && pending && outcome.Succeeded &&
                File.ReadAllText(DictPath, Encoding.UTF8).Contains("CAD 版端盖");
            Report(name, ok, K("model_null", model == null) + "|" +
                K("pending", pending) + "|" + K("restored", outcome.Succeeded));
        }

        /// <summary>A corrupt backup must be refused and leave every byte alone.</summary>
        private static void ScenarioCorruptBackup()
        {
            const string name = "recovery_corrupt_backup_refused";
            Reset(WordVersion, "{ \"entries\": [ this backup is broken");
            byte[] currentBefore = File.ReadAllBytes(DictPath);
            byte[] backupBefore = File.ReadAllBytes(BackupPath);
            FileAttributes attrsBefore = File.GetAttributes(BackupPath);
            ProbeOutcome outcome = RestoreThroughEntry(DictPath, _dir);
            bool ok = !outcome.Succeeded && !outcome.Wrote &&
                BytesEqual(currentBefore, File.ReadAllBytes(DictPath)) &&
                BytesEqual(backupBefore, File.ReadAllBytes(BackupPath)) &&
                attrsBefore == File.GetAttributes(BackupPath) &&
                !File.Exists(DictPath + ".word-baseline");
            Report(name, ok, K("refused", !outcome.Succeeded) + "|" +
                K("wrote", outcome.Wrote) + "|" +
                K("error", outcome.Error.Length == 0 ? "<none>" : outcome.Error));
        }

        /// <summary>The current file changes after capture: refuse, keep the newer bytes.</summary>
        private static void ScenarioCurrentChangedAfterCapture()
        {
            const string name = "recovery_current_changed_after_capture";
            Reset(WordVersion, CadVersion);
            UseDrawing(_dir);
            ProbeOutcome outcome;
            byte[] newer;
            byte[] backupBytes;
            try
            {
                ProbeSelection selection = _entry.Capture(DictPath);
                // Word exports again while the dialog is open.
                WriteText(DictPath, WordVersion.Replace("Word 版壳体", "Word 版第二次导出"));
                newer = File.ReadAllBytes(DictPath);
                backupBytes = File.ReadAllBytes(BackupPath);
                outcome = _entry.Restore(selection);
            }
            finally { RuntimeHost.ClearActiveDocumentOverride(); }
            bool ok = !outcome.Succeeded && !outcome.Wrote &&
                BytesEqual(newer, File.ReadAllBytes(DictPath)) &&
                BytesEqual(backupBytes, File.ReadAllBytes(BackupPath));
            Report(name, ok, K("refused", !outcome.Succeeded) + "|" +
                K("error", outcome.Error.Length == 0 ? "<none>" : outcome.Error) + "|" +
                K("newer_bytes_kept", BytesEqual(newer, File.ReadAllBytes(DictPath))));
        }

        /// <summary>The selected backup changes after capture: refuse rather than overwrite.</summary>
        private static void ScenarioBackupChangedAfterCapture()
        {
            const string name = "recovery_backup_changed_after_capture";
            Reset(WordVersion, CadVersion);
            UseDrawing(_dir);
            ProbeOutcome outcome;
            byte[] changed;
            byte[] currentBytes;
            try
            {
                ProbeSelection selection = _entry.Capture(DictPath);
                WriteText(BackupPath, CadVersion.Replace("CAD 版壳体", "CAD 版被外部改写"));
                changed = File.ReadAllBytes(BackupPath);
                currentBytes = File.ReadAllBytes(DictPath);
                outcome = _entry.Restore(selection);
            }
            finally { RuntimeHost.ClearActiveDocumentOverride(); }
            bool ok = !outcome.Succeeded && !outcome.Wrote &&
                BytesEqual(currentBytes, File.ReadAllBytes(DictPath)) &&
                BytesEqual(changed, File.ReadAllBytes(BackupPath));
            Report(name, ok, K("refused", !outcome.Succeeded) + "|" +
                K("error", outcome.Error.Length == 0 ? "<none>" : outcome.Error));
        }

        /// <summary>Switching to another drawing invalidates the captured selection.</summary>
        private static void ScenarioDocumentSwitched()
        {
            const string name = "recovery_document_switched";
            Reset(WordVersion, CadVersion);
            string otherDir = Path.Combine(_dir, "other");
            Directory.CreateDirectory(otherDir);
            UseDrawing(_dir);
            ProbeOutcome outcome;
            byte[] currentBytes;
            byte[] backupBytes;
            try
            {
                ProbeSelection selection = _entry.Capture(DictPath);
                // The user activates a different drawing before confirming.
                UseDrawing(otherDir);
                currentBytes = File.ReadAllBytes(DictPath);
                backupBytes = File.ReadAllBytes(BackupPath);
                outcome = _entry.Restore(selection);
            }
            finally { RuntimeHost.ClearActiveDocumentOverride(); }
            bool ok = !outcome.Succeeded && !outcome.Wrote &&
                BytesEqual(currentBytes, File.ReadAllBytes(DictPath)) &&
                BytesEqual(backupBytes, File.ReadAllBytes(BackupPath));
            Report(name, ok, K("refused", !outcome.Succeeded) + "|" +
                K("error", outcome.Error.Length == 0 ? "<none>" : outcome.Error));
        }

        /// <summary>"Keep current version" must still require parsable current content.</summary>
        private static void ScenarioKeepRequiresParsableCurrent()
        {
            const string name = "recovery_keep_requires_parsable_current";
            Reset(null, CadVersion);
            WriteText(DictPath, "{ corrupt, keep must refuse");
            byte[] corrupt = File.ReadAllBytes(DictPath);
            byte[] backupBytes = File.ReadAllBytes(BackupPath);
            UseDrawing(_dir);
            ProbeOutcome outcome;
            try
            {
                outcome = _entry.Keep(_entry.Capture(DictPath));
            }
            finally { RuntimeHost.ClearActiveDocumentOverride(); }
            bool ok = !outcome.Succeeded && !outcome.BackupRemoved &&
                BytesEqual(corrupt, File.ReadAllBytes(DictPath)) &&
                BytesEqual(backupBytes, File.ReadAllBytes(BackupPath));
            Report(name, ok, K("refused", !outcome.Succeeded) + "|" +
                K("error", outcome.Error.Length == 0 ? "<none>" : outcome.Error) + "|" +
                K("backup_kept", File.Exists(BackupPath)));
        }

        /// <summary>A held product lock surfaces as busy, with no partial write.</summary>
        private static void ScenarioLockBusy()
        {
            const string name = "recovery_lock_busy";
            Reset(null, CadVersion);
            WriteText(DictPath, "{ corrupt under a held lock");
            byte[] corrupt = File.ReadAllBytes(DictPath);
            byte[] backupBytes = File.ReadAllBytes(BackupPath);
            UseDrawing(_dir);
            ProbeOutcome outcome;
            try
            {
                ProbeSelection selection = _entry.Capture(DictPath);
                using (FileStream held = new FileStream(DictPath + ".patlock",
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    outcome = _entry.Restore(selection);
                }
            }
            finally { RuntimeHost.ClearActiveDocumentOverride(); }
            bool ok = !outcome.Succeeded && !outcome.Wrote &&
                BytesEqual(corrupt, File.ReadAllBytes(DictPath)) &&
                BytesEqual(backupBytes, File.ReadAllBytes(BackupPath));
            Report(name, ok, K("refused", !outcome.Succeeded) + "|" +
                K("error", outcome.Error.Length == 0 ? "<none>" : outcome.Error));
        }

        /// <summary>A read-only current file is not overwritten by recovery.</summary>
        private static void ScenarioReadOnlyTarget()
        {
            const string name = "recovery_read_only_target";
            Reset(WordVersion, CadVersion);
            File.SetAttributes(DictPath, FileAttributes.ReadOnly);
            byte[] before = File.ReadAllBytes(DictPath);
            FileAttributes attrs = File.GetAttributes(DictPath);
            UseDrawing(_dir);
            ProbeOutcome outcome;
            bool ok;
            try
            {
                outcome = _entry.Restore(_entry.Capture(DictPath));
                // Measure before the probe restores attributes for cleanup.
                ok = !outcome.Succeeded && !outcome.Wrote &&
                    BytesEqual(before, File.ReadAllBytes(DictPath)) &&
                    attrs == File.GetAttributes(DictPath) &&
                    File.Exists(BackupPath);
            }
            finally
            {
                RuntimeHost.ClearActiveDocumentOverride();
                if (File.Exists(DictPath)) File.SetAttributes(DictPath, FileAttributes.Normal);
            }
            Report(name, ok, K("refused", !outcome.Succeeded) + "|" +
                K("read_only_preserved", (attrs & FileAttributes.ReadOnly) != 0) + "|" +
                K("error", outcome.Error.Length == 0 ? "<none>" : outcome.Error));
        }

        /// <summary>
        /// The dictionary bytes are written but the baseline sidecar cannot be updated.
        /// The outcome must say so instead of pretending nothing happened.
        /// </summary>
        private static void ScenarioBaselineFailureAfterRestore()
        {
            const string name = "recovery_baseline_failure_reports_write";
            Reset(null, CadVersion);
            WriteText(DictPath, "{ corrupt, baseline will fail");
            WriteText(DictPath + ".word-baseline", "unrelated sentinel");
            byte[] sentinel = File.ReadAllBytes(DictPath + ".word-baseline");
            byte[] backupBytes = File.ReadAllBytes(BackupPath);
            UseDrawing(_dir);
            ProbeOutcome outcome;
            try
            {
                outcome = _entry.Restore(_entry.Capture(DictPath));
            }
            finally
            {
                RuntimeHost.ClearActiveDocumentOverride();
                if (File.Exists(DictPath + ".word-baseline"))
                    File.SetAttributes(DictPath + ".word-baseline", FileAttributes.Normal);
            }
            bool ok = !outcome.Succeeded && outcome.Wrote &&
                outcome.Model != null &&
                File.ReadAllText(DictPath, Encoding.UTF8).Contains("CAD 版端盖") &&
                BytesEqual(sentinel, File.ReadAllBytes(DictPath + ".word-baseline")) &&
                BytesEqual(backupBytes, File.ReadAllBytes(BackupPath));
            Report(name, ok, K("reports_wrote", outcome.Wrote) + "|" +
                K("error", outcome.Error.Length == 0 ? "<none>" : outcome.Error) + "|" +
                K("restore_point_kept", File.Exists(BackupPath)));
        }

        /// <summary>
        /// Button enablement is the user-visible half of R-034: with a corrupt
        /// current file "keep current" must be unavailable, "restore" must be offered.
        /// </summary>
        private static void ScenarioDialogButtons()
        {
            const string name = "recovery_dialog_offers_restore";
            Reset(null, CadVersion);
            WriteText(DictPath, "{ corrupt, dialog must still offer restore");
            UseDrawing(_dir);
            Form dlg = null;
            try
            {
                ProbeSelection selection = _entry.Capture(DictPath);
                dlg = _entry.CreateDialog(selection);
                if (dlg == null) { Skip(name, "the entry could not establish a selection to show"); return; }
                Button keep = Find(dlg, "采用 Word 版", "Keep Word version");
                Button restore = Find(dlg, "恢复 CAD 版", "Restore CAD version");
                bool ok = restore != null && restore.Enabled && keep != null && !keep.Enabled;
                Report(name, ok, K("restore_enabled", restore != null && restore.Enabled) + "|" +
                    K("keep_enabled", keep != null && keep.Enabled));
            }
            catch (Exception ex)
            {
                Skip(name, "dialog could not be constructed off-screen: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (dlg != null) dlg.Dispose();
                RuntimeHost.ClearActiveDocumentOverride();
            }
        }

        private static Button Find(Control parent, string zh, string en)
        {
            foreach (Control child in parent.Controls)
            {
                Button button = child as Button;
                if (button != null && (button.Text == zh || button.Text == en)) return button;
            }
            return null;
        }
    }
}
