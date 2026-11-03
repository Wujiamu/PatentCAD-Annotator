// G20 / R-034 (补修) red/green probe.
//
// THE SAME scenario file is compiled against two snapshots of the three files
// G20 may touch (DictPaletteControl / ArbitrateDialog / DictPaletteWorkflow).
// There is no adapter and no re-implementation: both builds drive the real
// production palette entry (DictPaletteControl.BtnArbitrate_Click) and observe
// the real modal ArbitrateDialog through Application.OpenForms, pressing its
// real buttons. The only variable is the fix.
//
// Failure contract (sealed in repair-review-20261102/palette-review-final.txt):
//   valid current JSON + corrupt backup. The pre-G20 palette returned
//   "裁决失败: Backup dictionary cannot be parsed." from BtnArbitrate_Click and
//   never opened the dialog, so the user could not choose "采用 Word 版".
//
// Business rules asserted here (per selected action, not per dialog):
//   current good + backup good -> both actions available
//   current bad  + backup good -> keep unavailable, restore available
//   current good + backup bad  -> keep available, restore unavailable   <-- defect
//   current bad  + backup bad  -> both unavailable, reason shown, "Later" keeps both files
//   "Later" never deletes either file; a corrupt backup is never auto-removed.
//   The current/backup changing while the dialog is open is refused, files kept.
//
// Cell state is read from the ACTUAL strict parse result
// (DictConflict.CaptureSelection(...).CurrentParsed / BackupParsed); nothing is
// preset by a test boolean. If the fixture does not parse the way the cell
// claims, the fixture line fails instead of silently asserting the wrong case.
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

namespace G20Probe
{
    internal static class ActionProbe
    {
        private const string WordVersion =
            "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"extracted_at\": \"2026-11-02T01:23:45\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"301\",\n      \"name\": \"Word 版壳体\"\n    }\n  ],\n  \"warnings\": []\n}";

        private const string CadVersion =
            "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"modified_by\": \"cad\",\n    \"modified_at\": \"2026-11-02T02:00:00\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"301\",\n      \"name\": \"CAD 版壳体\"\n    },\n    {\n      \"number\": \"602\",\n      \"name\": \"CAD 版端盖\"\n    }\n  ],\n  \"warnings\": []\n}";

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
                typeof(ActionProbe).Assembly.Location)), "g20-work");
            if (Directory.Exists(_root)) RemoveTree(_root);
            Directory.CreateDirectory(_root);

            Console.WriteLine("snapshot=" + Snapshot() + "|entry=production|dialog=modal");

            // --- the four cells of the required regression matrix -----------
            // Each cell runs the two actions and "Later" on separate fixtures:
            // an action that really writes removes the backup, so the same
            // fixture cannot be reused for the next action.
            Cell("good-current|good-backup", WordVersion, CadVersion);
            Cell("bad-current|good-backup", BadCurrent, CadVersion);
            Cell("good-current|bad-backup", WordVersion, BadBackup);
            Cell("bad-current|bad-backup", BadCurrent, BadBackup);

            // --- the palette must offer the entry in all four cells ----------
            PaletteReachability();

            // --- sealed-record equivalents (same ids as palette-review-final) --
            SealedEquivalents();

            // --- stale selections: changed while the dialog is open ----------
            StaleCurrentChangedAfterOpen();
            StaleBackupChangedAfterOpen();

            foreach (string line in Lines) Console.WriteLine(line);
            Console.WriteLine("SUMMARY: passed=" + _pass + " failed=" + _fail + " skipped=" + _skip);
            return _fail == 0 ? 0 : 1;
        }

        private static string Snapshot()
        {
#if G20_RED
            return "red";
#else
            return "green";
#endif
        }

        // ===== the four cells =====

        private static void Cell(string cell, string current, string backup)
        {
            KeepCell(cell, current, backup);
            RestoreCell(cell, current, backup);
            LaterCell(cell, current, backup);
        }

        /// <summary>
        /// "采用 Word 版" in one cell. Available means: the dialog opens, the
        /// button is enabled, pressing it succeeds, the current bytes are kept
        /// verbatim and the backup is cleaned. Unavailable means: the dialog
        /// still opens and only that button is disabled (the other action and
        /// "Later" are unaffected) - the user is never silently left with no
        /// arbitration at all.
        /// </summary>
        private static void KeepCell(string cell, string current, string backup)
        {
            const string action = "keep";
            using (Fixture f = new Fixture(current, backup))
            {
                bool expectKeep = f.CurrentParsed;
                string currentBefore = f.CurrentIdentity;
                string backupBefore = f.BackupIdentity;
                Observation o = f.Run(delegate(ArbitrateDialog dlg) { Press(dlg, "_btnKeepWord"); });

                bool ok = o.Opened &&
                    o.KeepEnabled == expectKeep &&
                    (expectKeep
                        ? o.DialogResult == DialogResult.OK && o.Outcome.Succeeded &&
                          o.Outcome.BackupRemoved &&
                          string.Equals(f.CurrentIdentity, currentBefore, StringComparison.Ordinal) &&
                          !File.Exists(f.BackupPath) &&
                          o.Status == Strings.Conflict_KeepWordOk
                        : o.DialogResult == DialogResult.Cancel &&
                          string.Equals(f.CurrentIdentity, currentBefore, StringComparison.Ordinal) &&
                          string.Equals(f.BackupIdentity, backupBefore, StringComparison.Ordinal));

                Report("matrix|" + cell + "|" + action, ok,
                    K("parsed", f.CurrentParsed + "/" + f.BackupParsed) + "|" +
                    K("opened", o.Opened) + "|" +
                    K("keep_enabled", o.KeepEnabled) + "|" +
                    K("expected_keep", expectKeep) + "|" +
                    K("result", o.DialogResult) + "|" +
                    K("succeeded", o.Outcome.Succeeded) + "|" +
                    K("current_kept", string.Equals(f.CurrentIdentity, currentBefore, StringComparison.Ordinal)) + "|" +
                    K("backup_present", File.Exists(f.BackupPath)) + "|" +
                    K("status", o.Status));
            }
        }

        /// <summary>
        /// "恢复 CAD 版" in one cell. Available means: the dialog opens, the
        /// button is enabled, pressing it writes the backup content over the
        /// current file and clears the CAD marker. Unavailable means: only that
        /// button is disabled and both files keep their exact bytes.
        /// </summary>
        private static void RestoreCell(string cell, string current, string backup)
        {
            const string action = "restore";
            using (Fixture f = new Fixture(current, backup))
            {
                bool expectRestore = f.BackupParsed;
                string currentBefore = f.CurrentIdentity;
                string backupBefore = f.BackupIdentity;
                Observation o = f.Run(delegate(ArbitrateDialog dlg) { Press(dlg, "_btnRestoreCad"); });

                string onDisk = File.Exists(f.DictPath) ? File.ReadAllText(f.DictPath, Encoding.UTF8) : "";
                bool ok = o.Opened &&
                    o.RestoreEnabled == expectRestore &&
                    (expectRestore
                        ? o.DialogResult == DialogResult.Yes && o.Outcome.Succeeded &&
                          o.Outcome.Wrote && o.Outcome.BackupRemoved &&
                          onDisk.Contains("CAD 版端盖") && !onDisk.Contains("modified_by") &&
                          !File.Exists(f.BackupPath) &&
                          o.Status == Strings.Conflict_RestoreOk
                        : o.DialogResult == DialogResult.Cancel &&
                          string.Equals(f.CurrentIdentity, currentBefore, StringComparison.Ordinal) &&
                          string.Equals(f.BackupIdentity, backupBefore, StringComparison.Ordinal));

                Report("matrix|" + cell + "|" + action, ok,
                    K("parsed", f.CurrentParsed + "/" + f.BackupParsed) + "|" +
                    K("opened", o.Opened) + "|" +
                    K("restore_enabled", o.RestoreEnabled) + "|" +
                    K("expected_restore", expectRestore) + "|" +
                    K("result", o.DialogResult) + "|" +
                    K("succeeded", o.Outcome.Succeeded) + "|" +
                    K("wrote", o.Outcome.Wrote) + "|" +
                    K("current_kept", string.Equals(f.CurrentIdentity, currentBefore, StringComparison.Ordinal)) + "|" +
                    K("backup_present", File.Exists(f.BackupPath)) + "|" +
                    K("status", o.Status));
            }
        }

        /// <summary>
        /// "稍后再说" in one cell: the dialog opens in every cell (including both
        /// unparsable), and choosing Later leaves BOTH files byte-identical -
        /// in particular a corrupt backup is never auto-deleted.
        /// </summary>
        private static void LaterCell(string cell, string current, string backup)
        {
            const string action = "later";
            using (Fixture f = new Fixture(current, backup))
            {
                string currentBefore = f.CurrentIdentity;
                string backupBefore = f.BackupIdentity;
                Observation o = f.Run(delegate(ArbitrateDialog dlg) { Press(dlg, "_btnLater"); });

                bool ok = o.Opened &&
                    o.DialogResult == DialogResult.Cancel &&
                    string.Equals(f.CurrentIdentity, currentBefore, StringComparison.Ordinal) &&
                    string.Equals(f.BackupIdentity, backupBefore, StringComparison.Ordinal) &&
                    File.Exists(f.BackupPath);

                Report("matrix|" + cell + "|" + action, ok,
                    K("parsed", f.CurrentParsed + "/" + f.BackupParsed) + "|" +
                    K("opened", o.Opened) + "|" +
                    K("result", o.DialogResult) + "|" +
                    K("current_kept", string.Equals(f.CurrentIdentity, currentBefore, StringComparison.Ordinal)) + "|" +
                    K("backup_kept", string.Equals(f.BackupIdentity, backupBefore, StringComparison.Ordinal)) + "|" +
                    K("backup_present", File.Exists(f.BackupPath)));
            }
        }

        /// <summary>
        /// "Both unparsable" must still tell the user why, using the product's
        /// own unparsable reasons. Asserted separately so a silent dead dialog
        /// cannot pass as "clear reason".
        /// </summary>
        private static void BothBadReason()
        {
            using (Fixture f = new Fixture(BadCurrent, BadBackup))
            {
                Observation o = f.Run(delegate(ArbitrateDialog dlg) { Press(dlg, "_btnLater"); });
                bool ok = o.Opened && !o.KeepEnabled && !o.RestoreEnabled &&
                    o.ReasonText.Contains(DictConflict.CurrentUnparsableError) &&
                    o.ReasonText.Contains(DictConflict.BackupUnparsableError);
                Report("both-bad-shows-both-reasons", ok,
                    K("opened", o.Opened) + "|" +
                    K("keep_enabled", o.KeepEnabled) + "|" +
                    K("restore_enabled", o.RestoreEnabled) + "|" +
                    K("has_current_reason", o.ReasonText.Contains(DictConflict.CurrentUnparsableError)) + "|" +
                    K("has_backup_reason", o.ReasonText.Contains(DictConflict.BackupUnparsableError)) + "|" +
                    K("reason", o.ReasonText.Replace("\n", " / ")));
            }
        }

        // ===== the palette must OFFER arbitration in every cell =====
        //
        // The user reaches the dialog only if the 裁决 button is enabled. That
        // decision is a disk fact (a .bak is waiting), never a parse verdict on
        // the backup: R-034 was "the dialog cannot be opened at all".

        private static void PaletteReachability()
        {
            Reachability("good-current|good-backup", WordVersion, CadVersion);
            Reachability("bad-current|good-backup", BadCurrent, CadVersion);
            Reachability("good-current|bad-backup", WordVersion, BadBackup);
            Reachability("bad-current|bad-backup", BadCurrent, BadBackup);
        }

        private static void Reachability(string cell, string current, string backup)
        {
            using (Fixture f = new Fixture(current, backup))
            {
                FieldInfo info = typeof(DictPaletteControl).GetField("_btnArbitrate", Private);
                Button button = info == null ? null : info.GetValue(f.Control) as Button;
                bool enabled = button != null && button.Enabled;
                Report("palette|" + cell + "|arbitrate-button-enabled", enabled,
                    K("parsed", f.CurrentParsed + "/" + f.BackupParsed) + "|" +
                    K("button_enabled", enabled) + "|" +
                    K("pending_on_disk", f.BackupIdentity != DictFileVisibility.MissingIdentity));
            }
        }

        // ===== equivalents of the sealed palette-review-final.txt lines =====
        //
        // Same ids, same business judgement as the sealed probe, but driven
        // through the real palette entry instead of a hand-written adapter.

        private static void SealedEquivalents()
        {
            // Line 11: the fixture really is "valid current + invalid backup".
            using (Fixture f = new Fixture(WordVersion, BadBackup))
            {
                Report("valid-current-invalid-backup-fixture",
                    f.CurrentParsed && !f.BackupParsed,
                    K("current", f.CurrentParsed) + "|" + K("backup", f.BackupParsed));
            }

            // Line 12 (R-034 / G20): the defect itself. The palette must not
            // reject the whole arbitration because the backup is corrupt.
            using (Fixture f = new Fixture(WordVersion, BadBackup))
            {
                string backupBefore = f.BackupIdentity;
                Observation o = f.Run(delegate(ArbitrateDialog dlg) { Press(dlg, "_btnKeepWord"); });
                string rejection = string.Format(Strings.Conflict_Failed,
                    DictPaletteWorkflow.DescribeWriteError(DictConflict.BackupUnparsableError));
                bool ok = o.Opened && o.KeepEnabled && o.Status != rejection &&
                    o.DialogResult == DialogResult.OK && o.Outcome.Succeeded;
                Report("valid-current-keep-action-not-rejected-by-bad-backup", ok,
                    K("status", o.Status) + "|" +
                    K("dialog_opened", o.Opened) + "|" +
                    K("keep_enabled", o.KeepEnabled) + "|" +
                    K("backup_still_present_before", backupBefore.Length > 0) + "|" +
                    K("backup_present_after", File.Exists(f.BackupPath)));
            }

            // Line 13: the component underneath was never the problem.
            using (Fixture f = new Fixture(WordVersion, BadBackup))
            {
                DictConflict.RecoverySelection selection =
                    DictConflict.CaptureSelection(f.DictPath, f.BackupPath);
                DictConflict.RecoveryOutcome outcome =
                    DictConflict.KeepSelectedVersion(selection, delegate { return true; });
                Report("valid-current-keep-component-works",
                    outcome.Succeeded && outcome.BackupRemoved,
                    K("success", outcome.Succeeded) + "|" +
                    K("error", outcome.Error.Length == 0 ? "<none>" : outcome.Error));
            }

            // Line 14: healthy control.
            using (Fixture f = new Fixture(WordVersion, CadVersion))
            {
                Observation o = f.Run(delegate(ArbitrateDialog dlg) { Press(dlg, "_btnKeepWord"); });
                Report("valid-valid-keep",
                    o.Opened && o.KeepEnabled && o.DialogResult == DialogResult.OK &&
                    o.Outcome.Succeeded && !File.Exists(f.BackupPath),
                    K("success", o.Outcome.Succeeded) + "|" +
                    K("backup_present", File.Exists(f.BackupPath)));
            }

            // Line 15/16: bad current + good backup still restores, keep disabled.
            using (Fixture f = new Fixture(BadCurrent, CadVersion))
            {
                Observation o = f.Run(delegate(ArbitrateDialog dlg) { Press(dlg, "_btnRestoreCad"); });
                Report("bad-current-valid-backup-keep-disabled",
                    o.Opened && !o.KeepEnabled && f.BackupParsed,
                    K("enabled", o.KeepEnabled));
                Report("bad-current-valid-backup-restore",
                    o.DialogResult == DialogResult.Yes && o.Outcome.Succeeded && o.Outcome.Wrote &&
                    o.Outcome.Model != null,
                    K("success", o.Outcome.Succeeded) + "|" +
                    K("error", o.Outcome.Error.Length == 0 ? "<none>" : o.Outcome.Error));
            }

            // Line 17: both bad -> both actions refuse, both files survive.
            using (Fixture f = new Fixture(BadCurrent, BadBackup))
            {
                string currentBefore = f.CurrentIdentity;
                string backupBefore = f.BackupIdentity;
                Observation o = f.Run(delegate(ArbitrateDialog dlg)
                {
                    Press(dlg, "_btnKeepWord");
                    Press(dlg, "_btnRestoreCad");
                    Press(dlg, "_btnLater");
                });
                Report("both-bad-preserve-files",
                    o.Opened && !o.KeepEnabled && !o.RestoreEnabled &&
                    string.Equals(f.CurrentIdentity, currentBefore, StringComparison.Ordinal) &&
                    string.Equals(f.BackupIdentity, backupBefore, StringComparison.Ordinal),
                    K("keep", o.KeepEnabled) + "|" + K("restore", o.RestoreEnabled) + "|" +
                    K("current_kept", string.Equals(f.CurrentIdentity, currentBefore, StringComparison.Ordinal)) + "|" +
                    K("backup_kept", string.Equals(f.BackupIdentity, backupBefore, StringComparison.Ordinal)));
            }

            BothBadReason();
        }

        // ===== the current/backup changing while the dialog is open =====

        private static void StaleCurrentChangedAfterOpen()
        {
            using (Fixture f = new Fixture(WordVersion, CadVersion))
            {
                string backupBefore = f.BackupIdentity;
                string newer = "";
                Observation o = f.Run(delegate(ArbitrateDialog dlg)
                {
                    // Word exports again while the user is deciding.
                    File.WriteAllText(f.DictPath,
                        WordVersion.Replace("Word 版壳体", "Word 版第二次导出"), new UTF8Encoding(false));
                    newer = DictFileVisibility.ReadIdentity(f.DictPath);
                    Press(dlg, "_btnKeepWord");
                    Press(dlg, "_btnLater");
                });
                bool ok = o.Opened && !o.Outcome.Succeeded && !o.Outcome.Wrote &&
                    string.Equals(f.CurrentIdentity, newer, StringComparison.Ordinal) &&
                    string.Equals(f.BackupIdentity, backupBefore, StringComparison.Ordinal);
                Report("stale-current-changed-after-open-refused", ok,
                    K("succeeded", o.Outcome.Succeeded) + "|" +
                    K("newer_bytes_kept", string.Equals(f.CurrentIdentity, newer, StringComparison.Ordinal)) + "|" +
                    K("backup_kept", string.Equals(f.BackupIdentity, backupBefore, StringComparison.Ordinal)) + "|" +
                    K("dialog_message", o.MessageText.Replace("\n", " / ")));
            }
        }

        private static void StaleBackupChangedAfterOpen()
        {
            using (Fixture f = new Fixture(WordVersion, CadVersion))
            {
                string currentBefore = f.CurrentIdentity;
                string changed = "";
                Observation o = f.Run(delegate(ArbitrateDialog dlg)
                {
                    File.WriteAllText(f.BackupPath,
                        CadVersion.Replace("CAD 版壳体", "CAD 版被外部改写"), new UTF8Encoding(false));
                    changed = DictFileVisibility.ReadIdentity(f.BackupPath);
                    Press(dlg, "_btnRestoreCad");
                    Press(dlg, "_btnLater");
                });
                bool ok = o.Opened && !o.Outcome.Succeeded && !o.Outcome.Wrote &&
                    string.Equals(f.CurrentIdentity, currentBefore, StringComparison.Ordinal) &&
                    string.Equals(f.BackupIdentity, changed, StringComparison.Ordinal);
                Report("stale-backup-changed-after-open-refused", ok,
                    K("succeeded", o.Outcome.Succeeded) + "|" +
                    K("current_kept", string.Equals(f.CurrentIdentity, currentBefore, StringComparison.Ordinal)) + "|" +
                    K("changed_backup_kept", string.Equals(f.BackupIdentity, changed, StringComparison.Ordinal)) + "|" +
                    K("dialog_message", o.MessageText.Replace("\n", " / ")));
            }
        }

        // ===== harness =====

        private sealed class Observation
        {
            public bool Opened;
            public bool KeepEnabled;
            public bool RestoreEnabled;
            public DialogResult DialogResult;
            public string ReasonText = "";
            public string MessageText = "";
            public string Status = "";
            public DictConflict.RecoveryOutcome Outcome = new DictConflict.RecoveryOutcome();
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
                RuntimeHost.SetActiveDocumentOverride(delegate { return document; });

                // The cell state comes from the production strict parse, not
                // from a test flag.
                DictConflict.RecoverySelection probe =
                    DictConflict.CaptureSelection(DictPath, BackupPath);
                if (probe != null) { CurrentParsed = probe.CurrentParsed; BackupParsed = probe.BackupParsed; }

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

                // The 2-second palette poll would rewrite the status label while
                // the modal dialog runs; it is not under test here (G16/G17
                // cover it), so it is stopped to keep the assertion deterministic.
                StopPoll();

                // What the poll would have done before the user can click 裁决.
                Invoke("UpdateConflictState");
            }

            public string CurrentIdentity { get { return DictFileVisibility.ReadIdentity(DictPath); } }
            public string BackupIdentity { get { return DictFileVisibility.ReadIdentity(BackupPath); } }
            public Control Control { get { return _control; } }

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
                    Button keep = Field<Button>(dlg, "_btnKeepWord");
                    Button restore = Field<Button>(dlg, "_btnRestoreCad");
                    o.KeepEnabled = keep != null && keep.Enabled;
                    o.RestoreEnabled = restore != null && restore.Enabled;
                    o.ReasonText = AllLabelText(dlg);
                    work(dlg);
                    // An unavailable action leaves the dialog open (that is the
                    // point). The user's remaining choice is "Later", so close
                    // the modal the way a user would - never by force.
                    if (dlg.DialogResult == DialogResult.None) Press(dlg, "_btnLater");
                    o.DialogResult = dlg.DialogResult;
                    o.MessageText = InfoText(dlg);
                    if (dlg.Outcome != null) o.Outcome = dlg.Outcome;
                };
                pump.Start();
                try { Invoke("BtnArbitrate_Click", null, EventArgs.Empty); }
                finally { pump.Stop(); pump.Dispose(); }
                if (seen == null)
                {
                    // No dialog at all: the palette answered in the status bar.
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

            private static Button Field<T>(object target, string name)
            {
                FieldInfo field = target.GetType().GetField(name, Private);
                return field == null ? null : field.GetValue(target) as Button;
            }

            private static string InfoText(ArbitrateDialog dlg)
            {
                FieldInfo field = typeof(ArbitrateDialog).GetField("_lblInfo", Private);
                Label label = field == null ? null : field.GetValue(dlg) as Label;
                return label == null ? "" : label.Text;
            }

            /// <summary>Every label on the dialog, so the reason line is found
            /// regardless of which label carries it.</summary>
            private static string AllLabelText(Control parent)
            {
                StringBuilder text = new StringBuilder();
                foreach (Control child in parent.Controls)
                {
                    Label label = child as Label;
                    if (label != null && label.Text != null)
                    {
                        if (text.Length > 0) text.Append("\n");
                        text.Append(label.Text);
                    }
                    if (child.Controls.Count > 0)
                    {
                        string inner = AllLabelText(child);
                        if (inner.Length > 0)
                        {
                            if (text.Length > 0) text.Append("\n");
                            text.Append(inner);
                        }
                    }
                }
                return text.ToString();
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

            private static void WriteText(string path, string content)
            {
                File.WriteAllText(path, content, new UTF8Encoding(false));
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

        private static void RemoveTree(string dir)
        {
            foreach (string file in Directory.GetFiles(dir)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }

        /// <summary>
        /// Presses a real button of the real dialog. A disabled button is left
        /// alone on purpose: that IS the assertion ("not available"), and the
        /// probe must not bypass it by invoking the click handler directly.
        /// </summary>
        private static void Press(ArbitrateDialog dlg, string field)
        {
            FieldInfo info = typeof(ArbitrateDialog).GetField(field, Private);
            Button button = info == null ? null : info.GetValue(dlg) as Button;
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
