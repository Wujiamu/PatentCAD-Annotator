// G25 / R-034 (补修) red/green probe.
//
// THE SAME scenario file is compiled against two snapshots of the palette files
// G25 may touch. There is no adapter and no re-implementation: both builds drive
// the REAL palette entry (DictPaletteControl.BtnArbitrate_Click), observe the
// REAL modal ArbitrateDialog through Application.OpenForms and press its REAL
// buttons. The only variable is the fix.
//
// Failure contract (sealed in repair-review-20261102-r2/palette-matrix-final.txt):
//   current 301 + valid backup 201. The restore has ALREADY been written to disk
//   (DictLoader.NotifySelfWrite made the cache identity equal the new file), but
//   a helper step fails afterwards - the .word-baseline path is a directory, or
//   the backup cannot be deleted. The user then presses "Later" (or presses
//   restore once more, fails again, then "Later"). The palette kept showing 301
//   while disk and loader were already 201, because it refreshed only when the
//   dialog returned OK/Yes, and because a second failed attempt overwrites the
//   earlier Wrote flag in the dialog's single Outcome field.
//
// Business rules asserted here:
//   * after the dialog closes, the view equals the REAL current file - for the
//     normal success, the pre-write failure, the post-write baseline failure and
//     the post-write backup-deletion failure, with one click and with two;
//   * failure / backup-pending information is preserved (backup retained, the
//     arbitrate entry stays reachable) and "Later" or a partial write is never
//     reported as a fully successful arbitration;
//   * the session keeps the fact that the dictionary WAS written even when the
//     last attempt reported Wrote=false (never judge by the last Outcome alone);
//   * two further polls do not undo the refresh (NotifySelfWrite makes
//     IsFileChanged() false, so the poll alone can never fix a stale view);
//   * the refresh must not show the arbitrated drawing's model on another
//     active drawing.
//
// Control cells that must stay green (G20 four-cell matrix, six drift classes,
// the untouched older backup, the pre-write read-only refusal) are re-run here.
//
// Evidence format:
//   PASS|name|detail
//   FAIL|name|detail
//   SKIP/BLOCKED|name|reason

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using PatentMarker.IO;
using PatentMarker.I18n;
using PatentMarker.Palette;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace G25Probe
{
    internal static class Probe
    {
        private const string CurrentJson =
            "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"extracted_at\": \"2026-11-02T01:23:45\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"301\",\n      \"name\": \"当前版壳体\"\n    }\n  ],\n  \"warnings\": []\n}";

        private const string BackupJson =
            "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"modified_by\": \"cad\",\n    \"modified_at\": \"2026-11-02T02:00:00\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"201\",\n      \"name\": \"CAD 版壳体\"\n    }\n  ],\n  \"warnings\": []\n}";

        private const string BadCurrent = "{ this current file is not json";
        private const string BadBackup = "{ this backup file is not json";
        private const string BackupTs = "20261102-010101";

        private static readonly List<string> Lines = new List<string>();
        private static int _pass, _fail, _skip;
        private static string _root;

        private static readonly BindingFlags Private =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        [STAThread]
        private static int Main()
        {
            _root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(
                typeof(Probe).Assembly.Location)), "g25-work");
            if (Directory.Exists(_root)) RemoveTree(_root);
            Directory.CreateDirectory(_root);

            Console.WriteLine("snapshot=" + Snapshot() + "|entry=production|dialog=modal");

            // --- controls that must stay green in both builds -----------------
            ActionMatrix();        // A1: four cells x three actions
            DriftMatrix();         // A2: six drift classes x keep/restore
            OlderBackupRetained(); // A4

            // --- the defect cells (sealed ids) --------------------------------
            A3("none", 1);
            A3("readonly", 1);
            A3("baseline", 1);
            A3("delete-backup", 1);
            A3("readonly-repeat", 2);
            A3("baseline-repeat", 2);
            A3("delete-backup-repeat", 2);

            // --- session fact: never judge by the last Outcome alone ----------
            SessionFact();

            // --- refresh must not cross drawings ------------------------------
            CrossDrawing();

            foreach (string line in Lines) Console.WriteLine(line);
            Console.WriteLine("SUMMARY: passed=" + _pass + " failed=" + _fail + " skipped=" + _skip);
            return _fail == 0 ? 0 : 1;
        }

        private static string Snapshot()
        {
#if G25_RED
            return "red";
#else
            return "green";
#endif
        }

        // ===== A1: availability / action matrix (G20 control) =====

        private static void ActionMatrix()
        {
            bool[] flags = new bool[] { false, true };
            string[] actions = new string[] { "keep", "restore", "later" };
            foreach (bool wantCurrent in flags)
            {
                foreach (bool wantBackup in flags)
                {
                    foreach (string action in actions)
                    {
                        string cell = wantCurrent + "" + wantBackup;
                        string id = cell + action;
                        using (Fixture f = new Fixture(
                            wantCurrent ? CurrentJson : BadCurrent,
                            wantBackup ? BackupJson : BadBackup))
                        {
                            // Buttons are read from the REAL dialog, and the cell
                            // state comes from the production strict parse.
                            bool keepEnabled, restoreEnabled, laterEnabled;
                            using (ArbitrateDialog dlg = new ArbitrateDialog(f.Selection))
                            {
                                keepEnabled = Button(dlg, "_btnKeepWord") != null && Button(dlg, "_btnKeepWord").Enabled;
                                restoreEnabled = Button(dlg, "_btnRestoreCad") != null && Button(dlg, "_btnRestoreCad").Enabled;
                                laterEnabled = Button(dlg, "_btnLater") != null && Button(dlg, "_btnLater").Enabled;
                            }
                            Report("A1-buttons-" + id,
                                f.CurrentParsed == wantCurrent && f.BackupParsed == wantBackup &&
                                keepEnabled == wantCurrent && restoreEnabled == wantBackup && laterEnabled,
                                K("parsed", f.CurrentParsed + "/" + f.BackupParsed) + "|" +
                                K("keep", keepEnabled) + "|" + K("restore", restoreEnabled) + "|" +
                                K("later", laterEnabled));

                            string before = DictFileVisibility.ReadIdentity(f.DictPath);
                            if (action == "later")
                            {
                                Report("A1-later-" + id,
                                    string.Equals(before, DictFileVisibility.ReadIdentity(f.DictPath), StringComparison.Ordinal) &&
                                    File.Exists(f.BackupPath),
                                    K("current_kept", true) + "|" + K("backup_present", File.Exists(f.BackupPath)));
                            }
                            else
                            {
                                DictConflict.RecoveryOutcome o = action == "keep"
                                    ? DictConflict.KeepSelectedVersion(f.Selection, DictPaletteWorkflow.ContextGuard(f.Selection))
                                    : DictConflict.RestoreSelectedBackup(f.Selection, DictPaletteWorkflow.ContextGuard(f.Selection));
                                bool allowed = action == "keep" ? wantCurrent : wantBackup;
                                bool ok = o.Succeeded == allowed &&
                                    (allowed
                                        ? !File.Exists(f.BackupPath)
                                        : string.Equals(before, DictFileVisibility.ReadIdentity(f.DictPath), StringComparison.Ordinal) &&
                                          File.Exists(f.BackupPath));
                                Report("A1-action-" + id, ok,
                                    K("success", o.Succeeded) + "|" + K("allowed", allowed) + "|" +
                                    K("error", o.Error.Length == 0 ? "<none>" : o.Error));
                            }
                        }
                    }
                }
            }
        }

        // ===== A2: six drift classes (G16/G20 control) =====

        private static void DriftMatrix()
        {
            string[] drifts = new string[] { "current-change", "backup-change", "current-delete", "backup-delete", "document", "path" };
            string[] actions = new string[] { "keep", "restore" };
            foreach (string drift in drifts)
            {
                foreach (string action in actions)
                {
                    using (Fixture f = new Fixture(CurrentJson, BackupJson))
                    {
                        if (drift == "current-change") WriteText(f.DictPath, CurrentJson.Replace("当前版壳体", "外部改写"));
                        if (drift == "backup-change") WriteText(f.BackupPath, BackupJson.Replace("CAD 版壳体", "外部改写"));
                        if (drift == "current-delete") File.Delete(f.DictPath);
                        if (drift == "backup-delete") File.Delete(f.BackupPath);
                        if (drift == "document")
                        {
                            // Another Document object is active.
                            f.SwitchActive(f.OtherDrawing("b"));
                        }
                        if (drift == "path")
                        {
                            // Same Document object, but it now points at another drawing.
                            f.Active.Name = f.OtherDrawing("b").Name;
                        }

                        string before = DictFileVisibility.ReadIdentity(f.DictPath);
                        DictConflict.RecoveryOutcome o = action == "keep"
                            ? DictConflict.KeepSelectedVersion(f.Selection, DictPaletteWorkflow.ContextGuard(f.Selection))
                            : DictConflict.RestoreSelectedBackup(f.Selection, DictPaletteWorkflow.ContextGuard(f.Selection));
                        bool ok = !o.Succeeded && !o.Wrote &&
                            string.Equals(before, DictFileVisibility.ReadIdentity(f.DictPath), StringComparison.Ordinal);
                        Report("A2-" + drift + "-" + action, ok,
                            K("success", o.Succeeded) + "|" + K("wrote", o.Wrote) + "|" +
                            K("error", o.Error.Length == 0 ? "<none>" : o.Error));
                    }
                }
            }
        }

        private static void OlderBackupRetained()
        {
            using (Fixture f = new Fixture(CurrentJson, BackupJson))
            {
                string older = f.DictPath + ".word-20261101-120000.bak";
                WriteText(older, BackupJson.Replace("201", "111"));
                DictConflict.RecoveryOutcome o = DictConflict.KeepSelectedVersion(
                    f.Selection, DictPaletteWorkflow.ContextGuard(f.Selection));
                Report("A4-other-backup-retained", o.Succeeded && File.Exists(older),
                    K("success", o.Succeeded) + "|" + K("older_present", File.Exists(older)));
            }
        }

        // ===== A3: the defect cells =====
        //
        // baseFault:
        //   none          - normal restore (write, baseline and backup cleanup all succeed)
        //   readonly      - current file read-only: refused BEFORE any disk change
        //   baseline      - write succeeded, then the .word-baseline path is a directory
        //   delete-backup - write succeeded, then the backup cannot be deleted (held open)
        // clicks: 1 = press restore once then Later; 2 = press restore twice then Later.

        private static void A3(string fault, int clicks)
        {
            string baseFault = fault.EndsWith("-repeat", StringComparison.Ordinal)
                ? fault.Substring(0, fault.Length - "-repeat".Length) : fault;
            using (Fixture f = new Fixture(CurrentJson, BackupJson))
            {
                f.SelectActive();
                FileStream held = null;
                ClickLog log = new ClickLog();
                try
                {
                    if (baseFault == "baseline") Directory.CreateDirectory(f.DictPath + ".word-baseline");
                    if (baseFault == "delete-backup") held = new FileStream(f.BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    if (baseFault == "readonly") File.SetAttributes(f.DictPath, FileAttributes.ReadOnly);

                    Observation o = f.Run(delegate(ArbitrateDialog dlg)
                    {
                        Press(dlg, "_btnRestoreCad");
                        log.RecordFirst(dlg);
                        for (int i = 1; i < clicks; i++)
                        {
                            Press(dlg, "_btnRestoreCad");
                            log.RecordLast(dlg);
                        }
                        if (dlg.DialogResult == DialogResult.None) Press(dlg, "_btnLater");
                    });
                    if (held != null) { held.Dispose(); held = null; }
                    if (baseFault == "readonly") File.SetAttributes(f.DictPath, FileAttributes.Normal);

                    // Two further polls: NotifySelfWrite made IsFileChanged() false,
                    // so the poll alone can never correct a stale view.
                    f.Poll();
                    f.Poll();

                    string expectDisk = baseFault == "readonly" ? "301" : "201";
                    string disk = f.DiskNumber(expectDisk);
                    string loaded = f.LoadedNumber();
                    string panel = f.PanelNumber();
                    bool expectBackup = baseFault != "none";
                    bool expectSuccess = baseFault == "none";
                    bool expectWrote = baseFault != "readonly";

                    Report("A3-open-" + fault, o.Opened, K("opened", o.Opened));
                    Report("A3-result-" + fault,
                        o.Opened && log.FirstWrote == expectWrote && log.FirstSucceeded == expectSuccess,
                        K("success", log.FirstSucceeded) + "|" + K("wrote", log.FirstWrote) + "|" +
                        K("expected_wrote", expectWrote) + "|" +
                        K("last_wrote", log.LastWrote) + "|" +
                        K("last_error", log.LastError.Length == 0 ? "<none>" : log.LastError));
                    Report("A3-disk-" + fault, disk == expectDisk,
                        K("disk", disk) + "|" + K("expected", expectDisk));
                    Report("A3-loader-" + fault, loaded == expectDisk,
                        K("loader", loaded) + "|" + K("expected", expectDisk));
                    // The sealed assertion: the panel must show the REAL current file.
                    Report("A3-panel-matches-disk-" + fault, panel == disk,
                        K("panel", panel) + "|" + K("disk", disk));
                    // Material is retained; the pending arbitration entry stays reachable.
                    Report("A3-backup-retained-" + fault,
                        File.Exists(f.BackupPath) == expectBackup && f.ArbitrateEnabled() == expectBackup,
                        K("backup_present", File.Exists(f.BackupPath)) + "|" +
                        K("expected", expectBackup) + "|" +
                        K("arbitrate_enabled", f.ArbitrateEnabled()));
                    // Failure semantics: a partial write is never reported as a full
                    // success, and a failed attempt is not silently dropped.
                    bool truthful = expectSuccess
                        ? o.Status == Strings.Conflict_RestoreOk
                        : (o.Status != Strings.Conflict_RestoreOk &&
                           o.Status != Strings.Conflict_KeepWordOk &&
                           o.Status.StartsWith(FailurePrefix(), StringComparison.Ordinal));
                    Report("A3-status-truthful-" + fault, truthful,
                        K("status", o.Status) + "|" + K("expect_success", expectSuccess));
                    if (expectWrote && !expectSuccess)
                    {
                        Report("A3-status-tells-write-" + fault, MentionsWrite(o.Status),
                            K("status", o.Status));
                    }
                }
                finally
                {
                    if (held != null) held.Dispose();
                    try { if (File.Exists(f.DictPath)) File.SetAttributes(f.DictPath, FileAttributes.Normal); }
                    catch { }
                }
            }
        }

        /// <summary>Per-click record, so a later failed attempt cannot hide an earlier write.</summary>
        private sealed class ClickLog
        {
            public bool FirstWrote, FirstSucceeded, LastWrote, LastSucceeded;
            public string FirstError = "", LastError = "";

            public void RecordFirst(ArbitrateDialog dlg)
            {
                FirstWrote = dlg.Outcome != null && dlg.Outcome.Wrote;
                FirstSucceeded = dlg.Outcome != null && dlg.Outcome.Succeeded;
                FirstError = dlg.Outcome == null ? "" : dlg.Outcome.Error;
                LastWrote = FirstWrote;
                LastSucceeded = FirstSucceeded;
                LastError = FirstError;
            }

            public void RecordLast(ArbitrateDialog dlg)
            {
                LastWrote = dlg.Outcome != null && dlg.Outcome.Wrote;
                LastSucceeded = dlg.Outcome != null && dlg.Outcome.Succeeded;
                LastError = dlg.Outcome == null ? "" : dlg.Outcome.Error;
            }
        }

        /// <summary>
        /// A partial write must be reported as a failure that mentions the write;
        /// the write itself must never be hidden behind "Later" or a later refusal.
        /// </summary>
        private static bool MentionsWrite(string status)
        {
            if (status == null) return false;
            string baseline = DictPaletteWorkflow.DescribeWriteError(DictFileVisibility.BaselineError);
            if (status.IndexOf(baseline, StringComparison.Ordinal) >= 0) return true;
            return status.IndexOf("restored", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FailurePrefix()
        {
            return string.Format(Strings.Conflict_Failed, "");
        }

        // ===== session fact: the write survives a later failed attempt =====

        private static void SessionFact()
        {
            using (Fixture f = new Fixture(CurrentJson, BackupJson))
            {
                f.SelectActive();
                try { Directory.CreateDirectory(f.DictPath + ".word-baseline"); }
                catch { }
                ClickLog log = new ClickLog();
                Observation o = f.Run(delegate(ArbitrateDialog dlg)
                {
                    Press(dlg, "_btnRestoreCad");
                    log.RecordFirst(dlg);
                    Press(dlg, "_btnRestoreCad");
                    log.RecordLast(dlg);
                    if (dlg.DialogResult == DialogResult.None) Press(dlg, "_btnLater");
                });
                f.Poll();
                f.Poll();

                object fact = f.SessionFact(o.Dialog);
                bool hasFact = fact != null;
                bool anyWrote = hasFact && Bool(fact, "AnyWrote");
                bool anySucceeded = hasFact && Bool(fact, "AnySucceeded");
                int attempts = hasFact ? Int(fact, "Attempts") : 0;
                string writeError = hasFact ? Str(fact, "WriteError") : "";

                Report("A6-session-fact-present", hasFact, K("present", hasFact));
                Report("A6-session-reports-write-after-later",
                    anyWrote && !log.LastWrote,
                    K("session_any_wrote", anyWrote) + "|" + K("last_outcome_wrote", log.LastWrote));
                Report("A6-session-reports-no-full-success",
                    hasFact && !anySucceeded && attempts >= 2 && writeError.Length > 0,
                    K("any_succeeded", anySucceeded) + "|" + K("attempts", attempts) + "|" +
                    K("write_error", writeError.Length == 0 ? "<none>" : writeError));
                Report("A6-panel-matches-disk", f.PanelNumber() == f.LoadedNumber(),
                    K("panel", f.PanelNumber()) + "|" + K("disk", f.LoadedNumber()));
            }
        }

        // ===== the refresh must not cross drawings =====

        private static void CrossDrawing()
        {
            using (Fixture f = new Fixture(CurrentJson, BackupJson))
            {
                f.SelectActive();
                AcDoc other = f.OtherDrawing("b");
                f.WriteOther(other, "501");
                try { Directory.CreateDirectory(f.DictPath + ".word-baseline"); }
                catch { }
                Observation o = f.Run(delegate(ArbitrateDialog dlg)
                {
                    Press(dlg, "_btnRestoreCad");
                    // The user switches drawing while the modal is still open.
                    f.SwitchActive(other);
                    if (dlg.DialogResult == DialogResult.None) Press(dlg, "_btnLater");
                });

                string panelAtClose = f.PanelNumber();
                f.Poll();
                f.Poll();
                string panelAfterPolls = f.PanelNumber();

                Report("A5-drawing-a-written", f.LoadedNumber() == "201", K("a_disk", f.LoadedNumber()));
                // The hazard: showing the arbitrated drawing's restored model on B.
                Report("A5-refresh-does-not-show-arbitrated-drawing-on-other",
                    panelAtClose != "201" && panelAfterPolls != "201",
                    K("panel_at_close", panelAtClose) + "|" + K("panel_after_polls", panelAfterPolls) + "|" +
                    K("a_restored", "201"));
                // Either the view is left as it was, or it shows B's own current file.
                Report("A5-panel-is-unchanged-or-active-drawing",
                    panelAtClose == "301" || panelAtClose == "501",
                    K("panel_at_close", panelAtClose) + "|" + K("allowed", "301|501"));
                Report("A5-status-not-success", o.Status != Strings.Conflict_RestoreOk,
                    K("status", o.Status));

                // The normal activation path still shows the active drawing.
                f.SelectActive();
                Report("A5-active-drawing-shown-after-activation", f.PanelNumber() == "501",
                    K("panel", f.PanelNumber()));
                Report("A5-other-drawing-number-intact", f.LoadedNumberFor(other) == "501",
                    K("b_disk", f.LoadedNumberFor(other)));
            }
        }

        // ===== reflection helpers for the session contract =====

        private static bool Bool(object target, string name)
        {
            object value = FieldOrProperty(target, name);
            return value is bool && (bool)value;
        }

        private static int Int(object target, string name)
        {
            object value = FieldOrProperty(target, name);
            return value is int ? (int)value : 0;
        }

        private static string Str(object target, string name)
        {
            object value = FieldOrProperty(target, name);
            return value == null ? "" : Convert.ToString(value);
        }

        private static object FieldOrProperty(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            FieldInfo field = type.GetField(name, Private);
            if (field != null) return field.GetValue(target);
            PropertyInfo property = type.GetProperty(name, Private);
            return property == null ? null : property.GetValue(target);
        }

        // ===== harness =====

        private sealed class Observation
        {
            public bool Opened;
            public DialogResult DialogResult;
            public string Status = "";
            public string MessageText = "";
            public ArbitrateDialog Dialog;
        }

        private sealed class Fixture : IDisposable
        {
            private readonly string _dir;
            private readonly Form _host;
            private readonly DictPaletteControl _control;

            public readonly string DictPath;
            public readonly string BackupPath;
            public bool CurrentParsed;
            public bool BackupParsed;
            public DictConflict.RecoverySelection Selection;

            private AcDoc _active;

            public Fixture(string current, string backup)
            {
                _dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_dir);
                DictPath = Path.Combine(_dir, "t.dict.json");
                BackupPath = DictPath + DictConflict.BackupInfix + BackupTs + ".bak";
                WriteText(DictPath, current);
                WriteText(BackupPath, backup);

                DictLoader.InvalidateCache();
                ConfigLoader.Current = null;
                AcDoc document = new AcDoc { Name = Path.Combine(_dir, "t.dwg") };
                _active = document;
                RuntimeHost.SetActiveDocumentOverride(delegate { return _active; });

                // Cell state comes from the production strict parse, not a test flag.
                Selection = DictConflict.CaptureSelection(DictPath, BackupPath);
                if (Selection != null)
                {
                    CurrentParsed = Selection.CurrentParsed;
                    BackupParsed = Selection.BackupParsed;
                    Selection.Document = document;
                }

                _host = new Form();
                _host.StartPosition = FormStartPosition.Manual;
                _host.Location = new Point(-4000, -4000);
                _host.ShowInTaskbar = false;
                _host.ClientSize = new Size(900, 700);
                _control = new DictPaletteControl();
                _control.Dock = DockStyle.Fill;
                _host.Controls.Add(_control);
                _host.Show();
                Application.DoEvents();
                StopPoll();
                Invoke("UpdateConflictState");
            }

            public AcDoc Active { get { return _active; } }
            public Control Control { get { return _control; } }

            public void SwitchActive(AcDoc document) { _active = document; }

            public AcDoc OtherDrawing(string name)
            {
                return new AcDoc { Name = Path.Combine(_dir, name + ".dwg") };
            }

            public void WriteOther(AcDoc document, string number)
            {
                WriteText(Path.ChangeExtension(document.Name, ".dict.json"),
                    "{\"metadata\":{},\"entries\":[{\"number\":\"" + number + "\",\"name\":\"other\"}],\"warnings\":[]}");
            }

            /// <summary>The palette's activation path (what a real document activation does).</summary>
            public void SelectActive()
            {
                DictLoader.InvalidateCache();
                DictModel model = DictLoader.LoadForCurrentDrawing();
                if (model == null) _control.ShowNoDict();
                else _control.LoadDict(model, false);
            }

            /// <summary>One palette poll, exactly as the 2-second timer would run it.</summary>
            public void Poll()
            {
                Invoke("AutoRefreshTimer_Tick", null, EventArgs.Empty);
            }

            /// <summary>First entry number straight off the disk, or "&lt;other&gt;".</summary>
            public string DiskNumber(string expected)
            {
                string text = ReadTextSafe(DictPath);
                return text.Contains("\"" + expected + "\"", StringComparison.Ordinal) ? expected : "<other>";
            }

            public string LoadedNumber() { return NumberOf(DictLoader.Load(DictPath)); }

            public string LoadedNumberFor(AcDoc document)
            {
                return NumberOf(DictLoader.Load(Path.ChangeExtension(document.Name, ".dict.json")));
            }

            public string PanelNumber()
            {
                object model = typeof(DictPaletteControl).GetProperty("_currentDict", Private).GetValue(_control);
                return NumberOf(model as DictModel);
            }

            public bool ArbitrateEnabled()
            {
                FieldInfo info = typeof(DictPaletteControl).GetField("_btnArbitrate", Private);
                Button button = info == null ? null : info.GetValue(_control) as Button;
                return button != null && button.Enabled;
            }

            public object SessionFact(ArbitrateDialog dialog)
            {
                if (dialog == null) return null;
                PropertyInfo property = typeof(ArbitrateDialog).GetProperty("SessionFact", Private);
                return property == null ? null : property.GetValue(dialog);
            }

            private static string NumberOf(DictModel model)
            {
                if (model == null || model.Entries == null || model.Entries.Count == 0) return "<none>";
                return model.Entries[0].Number;
            }

            public Observation Run(Action<ArbitrateDialog> work)
            {
                Observation o = new Observation();
                ArbitrateDialog seen = null;
                int ticks = 0;
                System.Windows.Forms.Timer pump = new System.Windows.Forms.Timer();
                pump.Interval = 40;
                pump.Tick += delegate
                {
                    ticks++;
                    if (ticks > 250) { pump.Stop(); return; }
                    ArbitrateDialog dlg = FindDialog();
                    if (dlg == null) return;
                    pump.Stop();
                    seen = dlg;
                    o.Opened = true;
                    o.Dialog = dlg;
                    work(dlg);
                    if (dlg.DialogResult == DialogResult.None) Press(dlg, "_btnLater");
                    o.DialogResult = dlg.DialogResult;
                    o.MessageText = InfoText(dlg);
                };
                pump.Start();
                try { Invoke("BtnArbitrate_Click", null, EventArgs.Empty); }
                finally { pump.Stop(); pump.Dispose(); }
                if (seen == null)
                {
                    o.Opened = false;
                    o.DialogResult = DialogResult.Cancel;
                }
                o.Status = Status();
                return o;
            }

            private static ArbitrateDialog FindDialog()
            {
                foreach (Form form in Application.OpenForms)
                {
                    ArbitrateDialog dialog = form as ArbitrateDialog;
                    if (dialog != null) return dialog;
                }
                return null;
            }

            private static string InfoText(ArbitrateDialog dlg)
            {
                FieldInfo field = typeof(ArbitrateDialog).GetField("_lblInfo", Private);
                Label label = field == null ? null : field.GetValue(dlg) as Label;
                return label == null ? "" : label.Text;
            }

            private void StopPoll()
            {
                FieldInfo field = typeof(DictPaletteControl).GetField("_autoRefreshTimer", Private);
                System.Windows.Forms.Timer timer =
                    field == null ? null : field.GetValue(_control) as System.Windows.Forms.Timer;
                if (timer != null) timer.Stop();
            }

            private string Status()
            {
                FieldInfo field = typeof(DictPaletteControl).GetField("_lblStatus", Private);
                Label label = field == null ? null : field.GetValue(_control) as Label;
                return label == null ? "" : label.Text;
            }

            private void Invoke(string name, params object[] args)
            {
                MethodInfo method = null;
                foreach (MethodInfo candidate in typeof(DictPaletteControl).GetMethods(Private))
                {
                    if (candidate.Name == name && candidate.GetParameters().Length == args.Length)
                    { method = candidate; break; }
                }
                if (method == null) throw new MissingMethodException("DictPaletteControl", name);
                method.Invoke(_control, args);
            }

            public void Dispose()
            {
                try
                {
                    StopPoll();
                    _host.Hide();
                    _host.Dispose();
                }
                catch { }
                RuntimeHost.ClearActiveDocumentOverride();
                DictLoader.InvalidateCache();
                ConfigLoader.Current = null;
                try
                {
                    foreach (string file in Directory.GetFiles(_dir)) File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(_dir, true);
                }
                catch { }
            }
        }

        private static Button Button(ArbitrateDialog dlg, string field)
        {
            FieldInfo info = typeof(ArbitrateDialog).GetField(field, Private);
            return info == null ? null : info.GetValue(dlg) as Button;
        }

        private static string ReadTextSafe(string path)
        {
            try { return File.ReadAllText(path, Encoding.UTF8); }
            catch { return ""; }
        }

        private static void RemoveTree(string dir)
        {
            foreach (string file in Directory.GetFiles(dir)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }

        private static void WriteText(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        /// <summary>
        /// Presses a real button of the real dialog. A disabled button is left
        /// alone on purpose: that IS the assertion ("not available").
        /// </summary>
        private static void Press(ArbitrateDialog dlg, string field)
        {
            Button button = Button(dlg, field);
            if (button == null || !button.Enabled) return;
            button.PerformClick();
        }

        private static void Report(string name, bool ok, string detail)
        {
            if (ok) { _pass++; Lines.Add("PASS|" + name + "|" + detail); }
            else { _fail++; Lines.Add("FAIL|" + name + "|" + detail); }
        }

        private static string K(string key, object value) { return key + "=" + Convert.ToString(value); }
    }
}
