// G21 / R-004 (补修) red/green probe — ONE scenario file, two snapshots.
//
// Failure contract (sealed in repair-review-20261102/palette-review-final.txt):
//   301→302 JSON committed but the drawing sync failed, then on the SAME drawing
//     * 401→402 succeeds      -> the old record is cleared
//     * 401→402 zero match    -> the old record is cleared
//     * 401→402 fails         -> the old record is overwritten with 401→402
//     * first sync refused because the content drifted -> no record at all
//
// Contract after G21 (repair-plan §12.4):
//   * ONE pending record per drawing (no multi-task queue).
//   * While a record exists, EVERY later number change on that drawing is refused
//     BEFORE the second JSON write; the new operation's sync service must not run.
//   * Name edits stay allowed and do not lose the owed operation.
//   * The record is removed only when THAT operation completes or the user
//     explicitly discards it.
//   * A first sync refused by document/path/content drift still registers the
//     operation and asks for reconfirmation - it is never dropped silently.
//   * A/B drawings each keep their own record; closing B does not clear A.
//
// How it is driven (deliberately the formal path):
//   The palette control is real and hosted in a real form. Rows are selected in
//   the real ListView and DictPaletteControl.EditSelectedEntry is invoked, which
//   opens the real modal EditEntryDialog; the probe finds it through
//   Application.OpenForms, types into its real TextBox and presses its real
//   Save/Cancel buttons. JSON is written by the real DictWriter. Only the CAD
//   renumber service is a recording stub (DictPaletteCadService.Calls), so "the
//   second JSON write was blocked" and "the new operation's sync never ran" are
//   observable facts, not assumptions. No pending record is ever preset by the
//   test: every record in every cell is produced by the production sync path.
//
// Evidence format:
//   PASS|name|detail
//   FAIL|name|detail

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using PatentMarker.IO;
using PatentMarker.Palette;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace G21Probe
{
    internal static class PendingProbe
    {
        private static readonly List<string> Lines = new List<string>();
        private static int _pass, _fail;
        private static string _root;

        private static readonly BindingFlags Private =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        [STAThread]
        private static int Main()
        {
            _root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(
                typeof(PendingProbe).Assembly.Location)), "g21-work");
            if (Directory.Exists(_root)) RemoveTree(_root);
            Directory.CreateDirectory(_root);

            Console.WriteLine("snapshot=" + Snapshot()
                + "|entry=production(DictPaletteControl.EditSelectedEntry+EditEntryDialog+DictWriter)"
                + "|cad=recording-stub");

            // ---- the three sealed "unrelated change" cells -----------------
            UnrelatedChange("success", new DictPaletteRenameResult { Matched = 1, Changed = 1, Committed = true });
            UnrelatedChange("no-match", new DictPaletteRenameResult { Matched = 0, Committed = true });
            UnrelatedChange("failure", new DictPaletteRenameResult { Matched = 1, Error = "second injected failure" });

            // ---- the sealed "related follow-up" control -------------------
            RelatedFollowUpBlocked();

            // ---- the rest of the required matrix --------------------------
            NameEditKeepsOwedOperation();
            SameOperationSuccessClears();
            DiscardThenNumberChangeAllowed();
            FirstSyncDrift();
            DriftOnRetryKeepsRecord();
            PartialCommitRecordsPending();
            ServiceExceptionRecordsPending();
            ZeroMatchFirstSyncHasNoPending();
            CompleteFirstSyncHasNoPending();
            TwoDrawingsIndependent();
            CloseDrawingBKeepsDrawingA();

            foreach (string line in Lines) Console.WriteLine(line);
            Console.WriteLine("SUMMARY: passed=" + _pass + " failed=" + _fail);
            return _fail == 0 ? 0 : 1;
        }

        private static string Snapshot()
        {
#if G21_RED
            return "red";
#else
            return "green";
#endif
        }

        // ===== the sealed cells: an unrelated number change while owed =====

        /// <summary>
        /// 301→302 committed, drawing sync failed. The user then renames a
        /// completely unrelated entry (401→402) on the same drawing. Whatever the
        /// CAD service would have answered, the second JSON write must not happen
        /// and the first operation must still be the one owed.
        /// </summary>
        private static void UnrelatedChange(string mode, DictPaletteRenameResult outcome)
        {
            using (Fixture f = new Fixture("301", "401", "777"))
            {
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Error = "first injected CAD failure" });
                Observation owed = f.Edit("301", "302");
                // The first sync must really have been attempted and really failed,
                // otherwise the rest of the cell would assert on an empty state.
                Report("first-sync-failed-recorded|" + mode,
                    owed.Written && f.Pending() == "301>302",
                    "written=" + owed.Written + "|pending=" + f.Pending() + "|calls=" + f.ServiceCalls);
                if (f.Pending() != "301>302")
                {
                    // Do not report the remaining lines as PASS on an empty state.
                    Report("unrelated-" + mode + "-json-blocked", false, "precondition: no owed operation");
                    Report("unrelated-" + mode + "-json-unchanged", false, "precondition: no owed operation");
                    Report("unrelated-" + mode + "-no-service-call", false, "precondition: no owed operation");
                    Report("unrelated-" + mode + "-preserves-first-pending", false, "precondition: no owed operation");
                    return;
                }

                string jsonBefore = f.Identity;
                int callsBefore = f.ServiceCalls;
                DictPaletteCadService.Enqueue(outcome);

                Observation second = f.Edit("401", "402");
                Report("unrelated-" + mode + "-json-blocked", !second.Written,
                    "written=" + second.Written + "|error=" + Short(second.Error));
                Report("unrelated-" + mode + "-json-unchanged",
                    string.Equals(f.Identity, jsonBefore, StringComparison.Ordinal),
                    "unchanged=" + string.Equals(f.Identity, jsonBefore, StringComparison.Ordinal));
                Report("unrelated-" + mode + "-no-service-call",
                    f.ServiceCalls == callsBefore,
                    "calls=" + (f.ServiceCalls - callsBefore));
                Report("unrelated-" + mode + "-preserves-first-pending",
                    f.Pending() == "301>302",
                    "expected=301>302|actual=" + f.Pending());
            }
        }

        // ===== the sealed control: a related follow-up is blocked ==========

        private static void RelatedFollowUpBlocked()
        {
            using (Fixture f = new Fixture("301", "401", "777"))
            {
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Error = "first injected CAD failure" });
                f.Edit("301", "302");
                string jsonBefore = f.Identity;
                int callsBefore = f.ServiceCalls;
                Observation edit = f.Edit("302", "303");
                Report("related-followup-blocked",
                    !edit.Written && f.Pending() == "301>302" &&
                    string.Equals(f.Identity, jsonBefore, StringComparison.Ordinal) &&
                    f.ServiceCalls == callsBefore,
                    "written=" + edit.Written + "|pending=" + f.Pending()
                    + "|calls=" + (f.ServiceCalls - callsBefore));
            }
        }

        // ===== name edits stay allowed and keep the owed operation =========

        private static void NameEditKeepsOwedOperation()
        {
            using (Fixture f = new Fixture("301", "401", "777"))
            {
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Error = "first injected CAD failure" });
                f.Edit("301", "302");
                string jsonBefore = f.Identity;
                // Same number, new name: not a number change, so it must be written.
                Observation edit = f.Edit("302", "302", "renamed part");
                bool changed = !string.Equals(f.Identity, jsonBefore, StringComparison.Ordinal);
                Report("name-edit-allowed-with-pending",
                    edit.Written && changed && f.Pending() == "301>302",
                    "written=" + edit.Written + "|json_changed=" + changed
                    + "|error=" + Short(edit.Error) + "|pending=" + f.Pending());
                Report("name-edit-does-not-lose-task", f.Pending() == "301>302",
                    "pending=" + f.Pending());
            }
        }

        // ===== completing THAT operation clears it =========================

        private static void SameOperationSuccessClears()
        {
            using (Fixture f = new Fixture("301", "401", "777"))
            {
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Error = "first injected CAD failure" });
                f.Edit("301", "302");
                int before = f.ServiceCalls;
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Changed = 1, Committed = true });
                f.Retry();
                Report("same-operation-success-clears",
                    f.Pending() == "none" && f.ServiceCalls == before + 1,
                    "pending=" + f.Pending() + "|retry_calls=" + (f.ServiceCalls - before));
            }
        }

        /// <summary>
        /// "Until it completes OR THE USER EXPLICITLY DISCARDS IT" (repair-plan
        /// §12.4) is only true if the discard is reachable from the panel. An
        /// owed operation the user can never release would freeze every later
        /// number change on that drawing for the rest of the session, so the
        /// discard must be a real, visible control - not a private method with
        /// no caller.
        /// </summary>
        private static void DiscardThenNumberChangeAllowed()
        {
            using (Fixture f = new Fixture("301", "401", "777"))
            {
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Error = "first injected CAD failure" });
                f.Edit("301", "302");

                bool reachable = f.HasDiscardControl();
                Report("discard-control-reachable", reachable,
                    "control=" + (reachable ? "visible" : "missing"));
                if (!reachable)
                {
                    Report("discard-clears-pending", false, "no reachable discard control");
                    Report("discard-then-number-change-allowed", false, "no reachable discard control");
                    return;
                }

                f.Discard();
                Report("discard-clears-pending", f.Pending() == "none", "pending=" + f.Pending());
                Observation edit = f.Edit("401", "402");
                Report("discard-then-number-change-allowed",
                    edit.Written && f.Pending() == "none",
                    "written=" + edit.Written + "|error=" + Short(edit.Error)
                    + "|pending=" + f.Pending());
            }
        }

        // ===== first sync refused by drift is still registered =============

        /// <summary>
        /// The sealed fourth cell. The JSON for 301→302 is really written by the
        /// production edit path, then another writer replaces the dictionary
        /// before the CAD transaction (Word export / second CAD instance). The
        /// palette must refuse the drawing change but still register the owed
        /// operation. Three facts are asserted separately on real bytes:
        /// the JSON really changed, the drawing was really not touched, the
        /// record really exists.
        /// </summary>
        private static void FirstSyncDrift()
        {
            using (Fixture f = new Fixture("301", "401", "777"))
            {
                string beforeEdit = f.Identity;

                // 1. The real edit path writes the JSON: 301 → 302.
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Changed = 1, Committed = true });
                Observation written = f.Edit("301", "302");
                string afterWrite = f.Identity;
                bool jsonWritten = written.Written &&
                    !string.Equals(beforeEdit, afterWrite, StringComparison.Ordinal);
                Report("drift-json-really-written", jsonWritten && f.Pending() == "none",
                    "written=" + written.Written + "|changed=" + !string.Equals(beforeEdit, afterWrite, StringComparison.Ordinal)
                    + "|pending=" + f.Pending());

                // 2. The palette's own snapshot of that write (same call the
                //    palette makes right after the dialog returns).
                RenameSyncContext context = RenameSyncContext.Capture(
                    f.Document, f.DictPath, DictFileVisibility.ReadIdentity(f.DictPath));

                // 3. Another writer replaces the dictionary in the drift window.
                f.WriteExternal("302", "401", "777", "888");
                string afterDrift = f.Identity;
                int callsBefore = f.ServiceCalls;

                // 4. The first drawing sync for 301→302 runs against the stale
                //    snapshot, exactly as the palette does it.
                f.Invoke("RenameLeadersInDrawing", "301", "302", context);

                Report("first-sync-drift-json-changed",
                    !string.Equals(afterWrite, afterDrift, StringComparison.Ordinal),
                    "changed=" + !string.Equals(afterWrite, afterDrift, StringComparison.Ordinal));
                Report("first-sync-drift-no-cad-call", f.ServiceCalls == callsBefore,
                    "calls=" + (f.ServiceCalls - callsBefore));
                Report("first-sync-drift-cad-unchanged",
                    string.Equals(f.Identity, afterDrift, StringComparison.Ordinal),
                    "unchanged=" + string.Equals(f.Identity, afterDrift, StringComparison.Ordinal));
                Report("first-sync-drift-retains-owed-operation", f.Pending() == "301>302",
                    "expected=301>302|actual=" + f.Pending());
                Report("first-sync-drift-requires-reconfirm", f.NeedsReconfirm(),
                    "reconfirm=" + f.NeedsReconfirm());
            }
        }

        /// <summary>
        /// The retry side of the same rule: after the dictionary changes, the
        /// explicit retry keeps the record and asks for confirmation instead of
        /// silently dropping it; the second press (the user's reconfirmation)
        /// then completes it.
        /// </summary>
        private static void DriftOnRetryKeepsRecord()
        {
            using (Fixture f = new Fixture("301", "401", "777"))
            {
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Error = "first injected CAD failure" });
                f.Edit("301", "302");
                f.WriteExternal("302", "401", "777", "888");

                int before = f.ServiceCalls;
                f.Retry();
                Report("drift-on-retry-keeps-record",
                    f.Pending() == "301>302" && f.ServiceCalls == before && f.NeedsReconfirm(),
                    "pending=" + f.Pending() + "|calls=" + (f.ServiceCalls - before)
                    + "|reconfirm=" + f.NeedsReconfirm());

                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Changed = 1, Committed = true });
                f.Retry();
                Report("reconfirm-then-completes", f.Pending() == "none" && f.ServiceCalls == before + 1,
                    "pending=" + f.Pending() + "|calls=" + (f.ServiceCalls - before));
            }
        }

        // ===== the four result shapes of the original operation ============

        private static void PartialCommitRecordsPending()
        {
            using (Fixture f = new Fixture("301", "401", "777"))
            {
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                {
                    Matched = 3,
                    Changed = 1,
                    Committed = true,
                    Error = "1 of 3 PAT annotation(s) could not be updated."
                });
                Observation edit = f.Edit("301", "302");
                Report("partial-commit-records-pending", edit.Written && f.Pending() == "301>302",
                    "written=" + edit.Written + "|pending=" + f.Pending());
            }
        }

        private static void ServiceExceptionRecordsPending()
        {
            using (Fixture f = new Fixture("301", "401", "777"))
            {
                DictPaletteCadService.EnqueueThrow(new InvalidOperationException("injected service crash"));
                Observation edit = f.Edit("301", "302");
                Report("service-exception-records-pending", edit.Written && f.Pending() == "301>302",
                    "written=" + edit.Written + "|pending=" + f.Pending());
            }
        }

        private static void ZeroMatchFirstSyncHasNoPending()
        {
            using (Fixture f = new Fixture("301", "401", "777"))
            {
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult { Matched = 0, Committed = true });
                Observation edit = f.Edit("301", "302");
                Report("zero-match-first-sync-no-pending", edit.Written && f.Pending() == "none",
                    "written=" + edit.Written + "|pending=" + f.Pending());
            }
        }

        private static void CompleteFirstSyncHasNoPending()
        {
            using (Fixture f = new Fixture("301", "401", "777"))
            {
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 2, Changed = 2, Committed = true });
                Observation edit = f.Edit("301", "302");
                Report("complete-first-sync-no-pending", edit.Written && f.Pending() == "none",
                    "written=" + edit.Written + "|pending=" + f.Pending());
            }
        }

        // ===== two drawings keep their own record =========================

        private static void TwoDrawingsIndependent()
        {
            using (Fixture a = new Fixture("301", "401", "777"))
            {
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Error = "a sync failed" });
                a.Edit("301", "302");
                Report("two-drawings-a-owes", a.Pending() == "301>302", "a=" + a.Pending());

                using (Fixture b = new Fixture("501", "601", "888"))
                {
                    // B is a different drawing: its own number change must be allowed.
                    DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                    { Matched = 1, Changed = 1, Committed = true });
                    Observation edit = b.Edit("501", "502");
                    Report("two-drawings-b-not-blocked-by-a",
                        edit.Written && b.Pending() == "none",
                        "written=" + edit.Written + "|b=" + b.Pending());

                    // Going back to A: its own record is still the one owed.
                    a.Activate();
                    Report("two-drawings-independent",
                        a.Pending() == "301>302",
                        "a=" + a.Pending());
                }
            }
        }

        private static void CloseDrawingBKeepsDrawingA()
        {
            using (Fixture a = new Fixture("301", "401", "777"))
            {
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Error = "a sync failed" });
                a.Edit("301", "302");

                using (Fixture b = new Fixture("501", "601", "888"))
                {
                    DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                    { Matched = 1, Error = "b sync failed" });
                    b.Edit("501", "502");
                    Report("two-drawings-b-owes", b.Pending() == "501>502", "b=" + b.Pending());

                    b.CloseDrawing();
                    b.Activate();
                    Report("close-b-clears-b", b.Pending() == "none", "b=" + b.Pending());

                    a.Activate();
                    Report("close-b-keeps-a", a.Pending() == "301>302", "a=" + a.Pending());
                }
            }
        }

        // ===== harness =====

        private sealed class Observation
        {
            public bool Written;
            public string Error = "";
            public string Status = "";
        }

        private sealed class Fixture : IDisposable
        {
            private readonly string _dir;
            private readonly Form _host;
            private readonly DictPaletteControl _control;
            private readonly AcDoc _document;

            public readonly string DictPath;
            public readonly string DrawingPath;

            public Fixture(params string[] numbers)
            {
                _dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_dir);
                DictPath = Path.Combine(_dir, "t.dict.json");
                DrawingPath = Path.Combine(_dir, "t.dwg");
                WriteDict(DictPath, numbers);

                _document = new AcDoc { Name = DrawingPath };
                DictPaletteCadService.Reset();

                _host = new Form();
                _host.StartPosition = FormStartPosition.Manual;
                _host.Location = new Point(-4000, -4000);
                _host.ShowInTaskbar = false;
                _host.ClientSize = new Size(900, 700);
                _control = new DictPaletteControl();
                _control.Dock = DockStyle.Fill;
                _host.Controls.Add(_control);
                _host.Show();
                System.Windows.Forms.Application.DoEvents();
                StopPoll();
                Activate();
            }

            public object Document { get { return _document; } }
            public string Identity { get { return DictFileVisibility.ReadIdentity(DictPath); } }
            public int ServiceCalls { get { return DictPaletteCadService.Calls.Count; } }

            /// <summary>Makes this fixture's drawing the active one and polls it.</summary>
            public void Activate()
            {
                RuntimeHost.SetActiveDocumentOverride(delegate { return _document; });
                DictLoader.InvalidateCache();
                Poll();
            }

            /// <summary>The 2-second poll, performed synchronously.</summary>
            private void Poll()
            {
                DictModel model = DictLoader.LoadForCurrentDrawing();
                if (model == null) Invoke("ShowNoDict");
                else Invoke("LoadDict", model, true);
            }

            /// <summary>
            /// The user's edit through the REAL palette entry: select the row,
            /// invoke EditSelectedEntry, drive the REAL modal EditEntryDialog.
            /// </summary>
            public Observation Edit(string oldNumber, string newNumber)
            {
                return Edit(oldNumber, newNumber, null);
            }

            public Observation Edit(string oldNumber, string newNumber, string newName)
            {
                Observation o = new Observation();
                SelectRow(oldNumber);

                EditEntryDialog seen = null;
                System.Windows.Forms.Timer pump = new System.Windows.Forms.Timer();
                pump.Interval = 40;
                pump.Tick += delegate
                {
                    EditEntryDialog dlg = FindDialog();
                    if (dlg == null) return;
                    pump.Stop();
                    seen = dlg;
                    TextBox number = (TextBox)typeof(EditEntryDialog).GetField("_txtNumber", Private).GetValue(dlg);
                    TextBox name = (TextBox)typeof(EditEntryDialog).GetField("_txtName", Private).GetValue(dlg);
                    Label error = (Label)typeof(EditEntryDialog).GetField("_lblError", Private).GetValue(dlg);
                    Button save = (Button)typeof(EditEntryDialog).GetField("_btnSave", Private).GetValue(dlg);
                    Button cancel = (Button)typeof(EditEntryDialog).GetField("_btnCancel", Private).GetValue(dlg);
                    number.Text = newNumber;
                    if (newName != null) name.Text = newName;
                    save.PerformClick();
                    if (dlg.DialogResult == DialogResult.None)
                    {
                        // Refused: the dialog stays open and explains why.
                        o.Error = error.Text;
                        cancel.PerformClick();
                    }
                    else
                    {
                        o.Written = dlg.DialogResult == DialogResult.OK;
                    }
                };
                pump.Start();
                try { Invoke("EditSelectedEntry"); }
                finally { pump.Stop(); pump.Dispose(); }
                if (seen == null) o.Error = o.Error.Length > 0 ? o.Error : "no dialog opened";
                o.Status = Status();
                Poll();
                return o;
            }

            /// <summary>The real "retry drawing sync" button.</summary>
            public void Retry()
            {
                Button retry = (Button)typeof(DictPaletteControl)
                    .GetField("_btnRenameRetry", Private).GetValue(_control);
                if (retry == null) throw new MissingFieldException("DictPaletteControl", "_btnRenameRetry");
                retry.PerformClick();
                Poll();
            }

            /// <summary>Is there a user-reachable "discard the owed operation" control?</summary>
            public bool HasDiscardControl()
            {
                FieldInfo info = typeof(DictPaletteControl).GetField("_btnRenameDiscard", Private);
                Button discard = info == null ? null : info.GetValue(_control) as Button;
                return discard != null && discard.Visible;
            }

            /// <summary>The user's explicit "discard the owed operation" action.</summary>
            public void Discard()
            {
                Button discard = (Button)typeof(DictPaletteControl)
                    .GetField("_btnRenameDiscard", Private).GetValue(_control) as Button;
                if (discard == null) throw new MissingFieldException("DictPaletteControl", "_btnRenameDiscard");
                discard.PerformClick();
                Poll();
            }

            /// <summary>Simulates closing this drawing.</summary>
            public void CloseDrawing()
            {
                Invoke("ReleasePendingRenameForDrawing", DrawingPath, _document);
            }

            public string Pending()
            {
                PendingRenameOperation pending = _control.CurrentPendingRename;
                return pending == null ? "none" : pending.OldNumber + ">" + pending.NewNumber;
            }

            public bool NeedsReconfirm() { return _control.PendingRenameNeedsReconfirm; }

            /// <summary>Another writer replaces the dictionary on disk.</summary>
            public void WriteExternal(params string[] numbers)
            {
                WriteDict(DictPath, numbers);
                DictLoader.InvalidateCache();
            }

            private void SelectRow(string number)
            {
                ListView list = (ListView)typeof(DictPaletteControl)
                    .GetField("_lstEntries", Private).GetValue(_control);
                if (list == null) throw new MissingFieldException("DictPaletteControl", "_lstEntries");
                list.SelectedItems.Clear();
                foreach (ListViewItem item in list.Items)
                {
                    if (item.SubItems.Count == 0) continue;
                    if (!NumberIdentity.AreEqual(item.SubItems[0].Text, number)) continue;
                    item.Selected = true;
                    item.Focused = true;
                    return;
                }
                throw new InvalidOperationException("row not found in palette: " + number
                    + " (rows: " + RowList(list) + ")");
            }

            private static string RowList(ListView list)
            {
                StringBuilder text = new StringBuilder();
                foreach (ListViewItem item in list.Items)
                {
                    if (item.SubItems.Count == 0) continue;
                    if (text.Length > 0) text.Append(",");
                    text.Append(item.SubItems[0].Text);
                }
                return text.ToString();
            }

            private static EditEntryDialog FindDialog()
            {
                foreach (Form form in System.Windows.Forms.Application.OpenForms)
                {
                    EditEntryDialog dialog = form as EditEntryDialog;
                    if (dialog != null) return dialog;
                }
                return null;
            }

            private void StopPoll()
            {
                System.Windows.Forms.Timer timer = (System.Windows.Forms.Timer)typeof(DictPaletteControl)
                    .GetField("_autoRefreshTimer", Private).GetValue(_control);
                if (timer != null) timer.Stop();
            }

            private string Status()
            {
                Label label = (Label)typeof(DictPaletteControl)
                    .GetField("_lblStatus", Private).GetValue(_control);
                return label == null ? "" : label.Text;
            }

            public void Invoke(string name, params object[] args)
            {
                MethodInfo method = null;
                foreach (MethodInfo candidate in typeof(DictPaletteControl).GetMethods(Private))
                {
                    if (candidate.Name == name && candidate.GetParameters().Length == args.Length)
                    { method = candidate; break; }
                }
                if (method == null) throw new MissingMethodException("DictPaletteControl", name);
                try { method.Invoke(_control, args); }
                catch (TargetInvocationException ex)
                {
                    throw new InvalidOperationException(name + " threw: " + ex.InnerException, ex.InnerException);
                }
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
                try
                {
                    foreach (string file in Directory.GetFiles(_dir)) File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(_dir, true);
                }
                catch { }
            }
        }

        private static void WriteDict(string path, params string[] numbers)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            StringBuilder json = new StringBuilder();
            json.Append("{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n");
            json.Append("    \"extracted_at\": \"2026-11-02T01:23:45\"\n  },\n  \"entries\": [\n");
            for (int i = 0; i < numbers.Length; i++)
            {
                if (i > 0) json.Append(",\n");
                json.Append("    {\n      \"number\": \"").Append(numbers[i])
                    .Append("\",\n      \"name\": \"part ").Append(numbers[i]).Append("\"\n    }");
            }
            json.Append("\n  ],\n  \"warnings\": []\n}");
            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
        }

        private static void RemoveTree(string dir)
        {
            foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            return text.Replace("\r", " ").Replace("\n", " / ");
        }

        private static void Report(string name, bool ok, string detail)
        {
            if (ok) { _pass++; Lines.Add("PASS|" + name + "|" + detail); }
            else { _fail++; Lines.Add("FAIL|" + name + "|" + detail); }
        }
    }
}
