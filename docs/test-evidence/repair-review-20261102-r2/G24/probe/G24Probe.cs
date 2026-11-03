// G24 / R-004 (补修) red/green probe — ONE scenario file, two snapshots.
//
// Failure contract (sealed in repair-review-20261102-r2/palette-matrix-final.txt,
// 20 FAIL assertions across N2/N3/N4):
//
//  (1) COMMIT SNAPSHOT IS REBUILT AFTER THE WRITE
//      DictPaletteControl.EditSelectedEntry reloads the model first and then reads
//      "writtenIdentity" from the *reloaded* model. An external legal JSON, a
//      broken file, a deleted file or a Save-As inside that window is therefore
//      taken as "what this commit wrote": the first sync still runs (calls=1) and
//      no debt is kept.
//
//  (2) RETRY TRUSTS THE CACHED MODEL AND THE CACHED PATH
//      RetryRenameInDrawing plans against _currentDict and a path that may still
//      be the palette cache, so a missing new number or a broken file in a warm
//      cache is retried against stale state (calls=1 where 0 is required), and a
//      Save-As (same Document, new path) retries straight away.
//
//  (3) BUTTONS / CONFIRM PHASE / RELEASE ARE GLOBAL, NOT PER DOCUMENT
//      ShowRenameRetry(bool) is a global flag, so B with its own debt shows no
//      buttons, a debt-free B still shows them, and A's "waiting for
//      reconfirmation" is consumed by B's first retry. ReleaseForDrawing also
//      deletes by historical path even when it was handed a Document, so closing a
//      reopened drawing at A's old path drops the renamed A's debt.
//
// Contract after G24 (repair-plan §13.2):
//  1. The writer saves the commit fact: EditEntryDialog records the context of a
//     successful write (Document + canonical dict path + the identity DictWriter
//     registered through NotifySelfWrite) and the caller reads THAT. No empty
//     identity may degrade into re-reading the current file, and MISSING is never
//     a successful commit.
//  2. First sync and retry both verify the canonical path and the real bytes.
//     Reconfirmation parses the SAME bytes strictly and looks the new number up in
//     that parse, then binds that byte identity. Broken / no-new-number / read
//     failure keeps the task, calls no CAD, and never borrows _currentDict.
//  3. Entry, confirm phase and release are per Document. A's "waiting for
//     reconfirmation" is never B's confirmation.
//
// How it is driven (deliberately the formal path, never an internal helper only):
//   The palette control is real and hosted in a real form. Rows are selected in the
//   real ListView and DictPaletteControl.EditSelectedEntry is invoked, which opens
//   the real modal EditEntryDialog; the probe finds it through Application.OpenForms,
//   types into its real TextBox and presses its real Save/Cancel buttons. JSON is
//   written by the real DictWriter. Only the CAD renumber service is a stub, so
//   "the drawing was not touched" and "the retry service did not run" are facts.
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
using PatentMarker.Palette;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace G24Probe
{
    internal static class G24Probe
    {
        private static readonly List<string> Lines = new List<string>();
        private static int _pass, _fail, _skip;
        private static string _root;

        private static readonly BindingFlags Private =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        [STAThread]
        private static int Main()
        {
            _root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(
                typeof(G24Probe).Assembly.Location)), "g24-work");
            if (Directory.Exists(_root)) RemoveTree(_root);
            Directory.CreateDirectory(_root);

            Console.WriteLine("snapshot=" + Snapshot()
                + "|entry=production(DictPaletteControl.EditSelectedEntry+EditEntryDialog+DictWriter)"
                + "|cad=recording-stub");

            // ===== N1/N5 controls: the G21 contract must not regress ==========
            N1_BlocksNumberChangesWhileOwed();
            N1_NameEditStaysAllowed();
            N1_CompleteThenEditAllowed();
            N1_ExplicitDiscardThenEditAllowed();
            N5_ServiceFailureKeepsTask();
            N5_ZeroMatchEndsAsRealResult();

            // ===== (1) the real edit caller: what did THIS commit write? =====
            RealEditCommit("stable", CommitPerturbation.None);
            RealEditCommit("replacement", CommitPerturbation.LegalExternalReplace);
            RealEditCommit("bad", CommitPerturbation.BrokenFile);
            RealEditCommit("deleted", CommitPerturbation.Deleted);
            RealEditCommit("save-as", CommitPerturbation.SaveAsSameDocument);

            // ===== (2) retry / reconfirm against the real bytes ================
            RetryMatrix();
            SaveAsRetryMatrix();
            PathSwitchMatrix();

            // ===== (3) per-document entry, confirm phase, release =============
            PerDocumentButtons();
            PerDocumentConfirmPhase();
            PerDocumentRelease();
            DiscardReachableWhenJsonUnusable();

            foreach (string line in Lines) Console.WriteLine(line);
            Console.WriteLine("SUMMARY: passed=" + _pass + " failed=" + _fail + " skipped=" + _skip);
            return _fail == 0 && _skip == 0 ? 0 : 1;
        }

        private static string Snapshot()
        {
#if G24_RED
            return "red";
#else
            return "green";
#endif
        }

        // =====================================================================
        // N1 / N5 controls (must stay green in both snapshots)
        // =====================================================================

        private static void N1_BlocksNumberChangesWhileOwed()
        {
            Run("N1-blocks-while-owed", delegate (Fixture f)
            {
                f.Owe("301", "302", "first injected CAD failure");
                string before = f.Identity;
                int callsBefore = f.ServiceCalls;
                Observation related = f.Edit("302", "303");
                Observation unrelated = f.Edit("401", "402");
                Report("N1-related-blocked-before-write", !related.Written,
                    "written=" + related.Written + "|error=" + Short(related.Error));
                Report("N1-unrelated-blocked-before-write", !unrelated.Written,
                    "written=" + unrelated.Written + "|error=" + Short(unrelated.Error));
                Report("N1-json-unchanged",
                    string.Equals(before, f.Identity, StringComparison.Ordinal),
                    "unchanged=" + string.Equals(before, f.Identity, StringComparison.Ordinal));
                Report("N1-no-service-call", f.ServiceCalls == callsBefore,
                    "calls=" + (f.ServiceCalls - callsBefore));
                Report("N1-debt-kept", f.Pending() == "301>302", "pending=" + f.Pending());
            });
        }

        private static void N1_NameEditStaysAllowed()
        {
            Run("N1-name-edit", delegate (Fixture f)
            {
                f.Owe("301", "302", "first injected CAD failure");
                string before = f.Identity;
                Observation edit = f.Edit("302", "302", "renamed part");
                Report("N1-name-edit-allowed",
                    edit.Written && !string.Equals(before, f.Identity, StringComparison.Ordinal),
                    "written=" + edit.Written + "|changed="
                    + !string.Equals(before, f.Identity, StringComparison.Ordinal));
                Report("N1-name-edit-keeps-debt", f.Pending() == "301>302", "pending=" + f.Pending());
            });
        }

        private static void N1_CompleteThenEditAllowed()
        {
            Run("N1-complete", delegate (Fixture f)
            {
                f.Owe("301", "302", "first injected CAD failure");
                int before = f.ServiceCalls;
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Changed = 1, Committed = true });
                f.Retry();
                Report("N1-retry-completes",
                    f.Pending() == "none" && f.ServiceCalls == before + 1,
                    "pending=" + f.Pending() + "|retry_calls=" + (f.ServiceCalls - before));
                Observation edit = f.Edit("401", "402");
                Report("N1-edit-allowed-after-complete", edit.Written,
                    "written=" + edit.Written + "|error=" + Short(edit.Error));
            });
        }

        private static void N1_ExplicitDiscardThenEditAllowed()
        {
            Run("N1-discard", delegate (Fixture f)
            {
                f.Owe("301", "302", "first injected CAD failure");
                Report("N1-discard-control-reachable", f.HasDiscardControl(),
                    "control=" + (f.HasDiscardControl() ? "visible" : "missing"));
                f.Discard();
                Report("N1-discard-clears-debt", f.Pending() == "none", "pending=" + f.Pending());
                Observation edit = f.Edit("401", "402");
                Report("N1-edit-allowed-after-discard", edit.Written,
                    "written=" + edit.Written + "|error=" + Short(edit.Error));
            });
        }

        private static void N5_ServiceFailureKeepsTask()
        {
            Run("N5-failure", delegate (Fixture f)
            {
                f.Owe("301", "302", "first injected CAD failure");
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 2, Changed = 0, Error = "transaction aborted" });
                f.Retry();
                Report("N5-partial-failure-keeps-task", f.Pending() == "301>302", "pending=" + f.Pending());
                DictPaletteCadService.EnqueueThrow(new InvalidOperationException("injected service crash"));
                f.Retry();
                Report("N5-service-exception-keeps-task", f.Pending() == "301>302", "pending=" + f.Pending());
            });
        }

        private static void N5_ZeroMatchEndsAsRealResult()
        {
            Run("N5-zero", delegate (Fixture f)
            {
                f.Owe("301", "302", "first injected CAD failure");
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult { Matched = 0, Committed = true });
                f.Retry();
                Report("N5-zero-match-completes", f.Pending() == "none", "pending=" + f.Pending());
            });
        }

        // =====================================================================
        // (1) the real edit caller: what did THIS commit write?
        // =====================================================================

        private enum CommitPerturbation { None, LegalExternalReplace, BrokenFile, Deleted, SaveAsSameDocument }

        private static void RealEditCommit(string change, CommitPerturbation perturbation)
        {
            Run("N2-real-edit-" + change, delegate (Fixture f)
            {
                bool opened = false, saved = false;
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Changed = 1, Committed = true });

                f.EditSelectedEntry(delegate (EditEntryDialog dlg)
                {
                    opened = true;
                    TextBox number = (TextBox)typeof(EditEntryDialog).GetField("_txtNumber", Private).GetValue(dlg);
                    Button save = (Button)typeof(EditEntryDialog).GetField("_btnSave", Private).GetValue(dlg);
                    number.Text = "302";
                    save.PerformClick();
                    saved = dlg.DialogResult == DialogResult.OK;
                    if (!saved) return;

                    // The bytes THIS commit wrote, read before the window is
                    // perturbed. DictWriter registered exactly this identity.
                    f.WrittenByThisSave = f.Identity;

                    switch (perturbation)
                    {
                        case CommitPerturbation.LegalExternalReplace:
                            f.WriteExternal("999");
                            break;
                        case CommitPerturbation.BrokenFile:
                            File.WriteAllText(f.DictPath, "{broken after save", new UTF8Encoding(false));
                            break;
                        case CommitPerturbation.Deleted:
                            File.Delete(f.DictPath);
                            break;
                        case CommitPerturbation.SaveAsSameDocument:
                            f.SaveAsSameDocument("b", "999");
                            break;
                    }
                });

                Report("N2-real-edit-" + change + "-opened-saved", opened && saved,
                    "opened=" + opened + "|saved=" + saved);
                bool stable = perturbation == CommitPerturbation.None;
                Report("N2-real-edit-" + change + "-sync-calls",
                    f.ServiceCalls == (stable ? 1 : 0),
                    "expected=" + (stable ? 1 : 0) + "|calls=" + f.ServiceCalls);
                if (stable)
                {
                    Report("N2-real-edit-stable-no-debt", f.Pending() == "none", "pending=" + f.Pending());
                    return;
                }
                Report("N2-real-edit-" + change + "-keeps-debt", f.Pending() == "301>302",
                    "pending=" + f.Pending());
                Report("N2-real-edit-" + change + "-commit-identity-is-own-write",
                    string.Equals(f.PendingRecordIdentity(), f.WrittenByThisSave, StringComparison.Ordinal),
                    "record=" + Short(f.PendingRecordIdentity()) + "|own-write=" + Short(f.WrittenByThisSave));
                Report("N2-real-edit-" + change + "-asks-reconfirm", f.NeedsReconfirm(),
                    "reconfirm=" + f.NeedsReconfirm());
                Report("N2-real-edit-" + change + "-discard-reachable", f.HasDiscardControl(),
                    "control=" + (f.HasDiscardControl() ? "visible" : "missing"));
            });
        }

        // =====================================================================
        // (2) retry / reconfirm: canonical path + real bytes
        // =====================================================================

        /// <summary>
        /// Four contents x three cache states. The palette is clicked twice, exactly
        /// like the sealed probe: the first click is a plain retry, the second click
        /// is the user's explicit reconfirmation. Only a legal dictionary that still
        /// contains the new number may reach the CAD service, and only on the second
        /// click.
        /// </summary>
        private static void RetryMatrix()
        {
            foreach (string content in new[] { "has-new", "missing-new", "bad", "read-failure" })
            {
                foreach (string cache in new[] { "stale", "warm", "cold" })
                {
                    string key = "N3-" + content + "-" + cache;
                    string body = content;
                    Run(key, delegate (Fixture f)
                    {
                        f.Owe("301", "302", "first injected CAD failure");
                        string path = f.DictPath;
                        bool readable = true;
                        if (body == "bad")
                            File.WriteAllText(path, "{broken", new UTF8Encoding(false));
                        else if (body == "missing-new")
                            f.WriteExternal("999");
                        else if (body == "has-new")
                            f.WriteExternal("302", "999");
                        else if (body == "read-failure")
                        {
                            File.Delete(path);
                            Directory.CreateDirectory(path);
                            readable = false;
                        }
                        if (cache != "stale") f.Activate(cache == "cold");
                        string before = readable ? f.Identity : "";

                        int callsBefore = f.ServiceCalls;
                        f.Retry();
                        Report(key + "-first-click-refuses", f.ServiceCalls == callsBefore,
                            "calls=" + (f.ServiceCalls - callsBefore) + "|pending=" + f.Pending());
                        Report(key + "-first-click-keeps-debt", f.Pending() == "301>302",
                            "pending=" + f.Pending());
                        // A readable-but-drifting dictionary must ask for an explicit
                        // reconfirmation. When the file cannot be read at all there is
                        // nothing to confirm against, so the requirement is that the
                        // task stays reachable (its discard entry), not that a
                        // reconfirmation is offered.
                        if (body == "read-failure")
                        {
                            Report(key + "-first-click-keeps-task-reachable",
                                f.HasDiscardControl(),
                                "control=" + (f.HasDiscardControl() ? "visible" : "missing"));
                        }
                        else
                        {
                            Report(key + "-first-click-asks-reconfirm", f.NeedsReconfirm(),
                                "reconfirm=" + f.NeedsReconfirm());
                        }

                        DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                        { Matched = 1, Changed = 1, Committed = true });
                        f.Retry();
                        bool allowed = body == "has-new";
                        Report(key + "-confirm-calls",
                            f.ServiceCalls - callsBefore == (allowed ? 1 : 0),
                            "expected=" + (allowed ? 1 : 0) + "|actual=" + (f.ServiceCalls - callsBefore)
                            + "|pending=" + f.Pending());
                        Report(key + "-json-not-written",
                            readable
                                ? string.Equals(before, f.Identity, StringComparison.Ordinal)
                                : !File.Exists(path),
                            "readable=" + readable);
                        Report(key + "-debt",
                            allowed ? f.Pending() == "none" : f.Pending() == "301>302",
                            "expected=" + (allowed ? "none" : "301>302") + "|pending=" + f.Pending());
                        if (allowed)
                        {
                            Report(key + "-maps-the-owed-numbers", f.LastCall() == "301>302",
                                "call=" + f.LastCall());
                            Report(key + "-committed-count", f.LastChanged == 1,
                                "changed=" + f.LastChanged);
                        }
                    });
                }
            }
        }

        /// <summary>Same Document, new path (Save As): the canonical binding moved.</summary>
        private static void SaveAsRetryMatrix()
        {
            foreach (bool containsNew in new[] { false, true })
            {
                foreach (bool refresh in new[] { false, true })
                {
                    string key = "N3-save-as-" + containsNew + refresh;
                    bool withNew = containsNew;
                    bool doRefresh = refresh;
                    Run(key, delegate (Fixture f)
                    {
                        f.Owe("301", "302", "first injected CAD failure");
                        f.SaveAsSameDocument("b", withNew ? new[] { "302", "999" } : new[] { "999" });
                        if (doRefresh) f.Activate(false);

                        int callsBefore = f.ServiceCalls;
                        f.Retry();
                        Report(key + "-first-refuses", f.ServiceCalls == callsBefore,
                            "calls=" + (f.ServiceCalls - callsBefore) + "|pending=" + f.Pending());
                        Report(key + "-first-keeps-debt", f.Pending() == "301>302",
                            "pending=" + f.Pending());
                        f.Retry();
                        Report(key + "-confirm",
                            f.ServiceCalls - callsBefore == (withNew ? 1 : 0),
                            "expected=" + (withNew ? 1 : 0) + "|calls=" + (f.ServiceCalls - callsBefore));
                        Report(key + "-debt", withNew ? f.Pending() == "none" : f.Pending() == "301>302",
                            "pending=" + f.Pending());
                    });
                }
            }
        }

        private static void PathSwitchMatrix()
        {
            Run("N3-path-switch-config-default", delegate (Fixture f)
            {
                f.Owe("301", "302", "first injected CAD failure");
                // The co-located dictionary disappears; the configured default still
                // holds a legal dictionary with the new number. The canonical binding
                // moved, so the retry must refuse first and only complete after an
                // explicit reconfirmation.
                string fallback = Path.Combine(f.Dir, "default.dict.json");
                WriteDict(fallback, "302", "999");
                File.Delete(f.DictPath);
                ConfigLoader.Current = new PatConfig { DefaultDictPath = fallback };
                f.Activate(false);
                int callsBefore = f.ServiceCalls;
                f.Retry();
                Report("N3-path-switch-first-refuses", f.ServiceCalls == callsBefore,
                    "calls=" + (f.ServiceCalls - callsBefore) + "|pending=" + f.Pending());
                f.Retry();
                Report("N3-path-switch-confirm-completes",
                    f.ServiceCalls - callsBefore == 1 && f.Pending() == "none",
                    "calls=" + (f.ServiceCalls - callsBefore) + "|pending=" + f.Pending());
            });

            Run("N3-explicit-reconfirm", delegate (Fixture f)
            {
                f.Owe("301", "302", "first injected CAD failure");
                f.WriteExternal("302", "999");
                f.Activate(false);
                int callsBefore = f.ServiceCalls;
                f.Retry();
                Report("N3-reconfirm-first-refuses", f.ServiceCalls == callsBefore,
                    "calls=" + (f.ServiceCalls - callsBefore));
                Report("N3-reconfirm-asks-user", f.NeedsReconfirm(), "reconfirm=" + f.NeedsReconfirm());
                f.Retry();
                Report("N3-reconfirm-then-completes",
                    f.ServiceCalls - callsBefore == 1 && f.Pending() == "none",
                    "calls=" + (f.ServiceCalls - callsBefore) + "|pending=" + f.Pending());
                Report("N3-reconfirm-maps-real-numbers", f.LastCall() == "301>302",
                    "call=" + f.LastCall());
            });
        }

        // =====================================================================
        // (3) per-document entry, confirm phase, release
        // =====================================================================

        private static void PerDocumentButtons()
        {
            foreach (bool resolveByDiscard in new[] { false, true })
            {
                bool discard = resolveByDiscard;
                Run("N4-b-buttons-" + discard, delegate (Fixture f)
                {
                    AcDoc a = f.Document;
                    f.Owe("301", "302", "a sync failed");
                    AcDoc b = f.NewDrawing("b", "501", "601");
                    f.Activate(b, false);
                    f.Owe("501", "502", "b sync failed");
                    f.Activate(a, false);

                    if (discard) f.Discard();
                    else
                    {
                        DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                        { Matched = 1, Changed = 1, Committed = true });
                        f.Retry();
                    }
                    Report("N4-a-resolved-" + discard, !f.HasPendingFor(a), "a=" + f.PendingFor(a));

                    f.Activate(b, false);
                    Report("N4-b-buttons-after-a-resolved-" + discard,
                        f.HasPendingFor(b) && f.Buttons,
                        "pending=" + f.HasPendingFor(b) + "|buttons=" + f.Buttons);
                    Report("N4-b-debt-intact-" + discard, f.PendingFor(b) == "501>502",
                        "b=" + f.PendingFor(b));
                });
            }

            Run("N4-no-debt-buttons", delegate (Fixture f)
            {
                f.Owe("301", "302", "a sync failed");
                AcDoc b = f.NewDrawing("b", "999");
                f.Activate(b, false);
                Report("N4-no-debt-hides-buttons", !f.HasPendingFor(b) && !f.Buttons,
                    "pending=" + f.HasPendingFor(b) + "|buttons=" + f.Buttons);

                File.Delete(f.DictPathFor(b));
                f.Activate(b, false);                Report("N4-no-dictionary-hides-buttons", !f.Buttons, "buttons=" + f.Buttons);
            });
        }

        private static void PerDocumentConfirmPhase()
        {
            Run("N4-confirm-phase", delegate (Fixture f)
            {
                AcDoc a = f.Document;
                f.Owe("301", "302", "a sync failed");
                AcDoc b = f.NewDrawing("b", "501");
                f.Activate(b, false);
                f.Owe("501", "502", "b sync failed");

                // A's dictionary drifted: A is now waiting for reconfirmation.
                f.Activate(a, false);
                f.WriteExternal("302", "999");
                int callsBefore = f.ServiceCalls;
                f.Retry();
                Report("N4-a-asks-reconfirm", f.NeedsReconfirmFor(a),
                    "a=" + f.NeedsReconfirmFor(a) + "|calls=" + (f.ServiceCalls - callsBefore));

                // B has no confirm phase of its own: its first click must be a plain
                // retry, which refuses because B's dictionary drifted too.
                f.Activate(b, false);
                f.WriteExternal("502", "888");
                Report("N4-b-has-no-confirm-phase", !f.NeedsReconfirmFor(b),
                    "b=" + f.NeedsReconfirmFor(b));
                callsBefore = f.ServiceCalls;
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult
                { Matched = 1, Changed = 1, Committed = true });
                f.Retry();
                Report("N4-confirm-not-inherited", f.ServiceCalls == callsBefore,
                    "expected-first-retry-calls=0|actual=" + (f.ServiceCalls - callsBefore));
                Report("N4-b-retry-now-asks-reconfirm", f.NeedsReconfirmFor(b),
                    "b=" + f.NeedsReconfirmFor(b));
                Report("N4-a-phase-intact", f.NeedsReconfirmFor(a),
                    "a=" + f.NeedsReconfirmFor(a));
            });
        }

        private static void PerDocumentRelease()
        {
            Run("N4-close-unrelated", delegate (Fixture f)
            {
                AcDoc a = f.Document;
                f.Owe("301", "302", "a sync failed");
                AcDoc b = f.NewDrawing("b", "999");
                f.CloseDrawing(b);
                Report("N4-closing-unrelated-keeps-a", f.HasPendingFor(a), "a=" + f.PendingFor(a));
            });

            Run("N4-close-reused-old-path", delegate (Fixture f)
            {
                AcDoc a = f.Document;
                string oldName = a.Name;
                f.Owe("301", "302", "a sync failed");
                // A is saved under a new path: same Document object, new Name.
                f.SaveAsSameDocument("a-renamed", "302", "999");
                f.Activate(a, false);

                // The old path is reopened as a DIFFERENT Document later.
                AcDoc reopened = new AcDoc { Name = oldName };
                f.CloseDrawing(reopened);
                Report("N4-close-reused-old-path-keeps-renamed-doc", f.HasPendingFor(a),
                    "pending-A=" + f.PendingFor(a));
            });
        }

        private static void DiscardReachableWhenJsonUnusable()
        {
            Run("N4-discard-when-json-unusable", delegate (Fixture f)
            {
                f.Owe("301", "302", "a sync failed");
                File.WriteAllText(f.DictPath, "{broken", new UTF8Encoding(false));
                f.Activate(false);
                Report("N4-broken-json-keeps-debt", f.HasPendingFor(f.Document),
                    "pending=" + f.PendingFor(f.Document));
                Report("N4-broken-json-keeps-discard-reachable", f.HasDiscardControl(),
                    "control=" + (f.HasDiscardControl() ? "visible" : "missing"));
                f.Discard();
                Report("N4-broken-json-discard-clears", f.Pending() == "none", "pending=" + f.Pending());
            });
        }

        // =====================================================================
        // harness
        // =====================================================================

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
            private AcDoc _document;

            public readonly string DrawingPath;

            public string WrittenByThisSave = "";
            public int LastChanged = -1;

            public Fixture()
            {
                _dir = Path.Combine(_root, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(_dir);
                DrawingPath = Path.Combine(_dir, "a.dwg");
                WriteDict(DictPathForName(DrawingPath), "301", "401");

                _document = new AcDoc { Name = DrawingPath };
                DictPaletteCadService.Reset();
                ConfigLoader.Current = null;

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
                Activate(_document, true);
            }

            public string Dir { get { return _dir; } }
            public AcDoc Document { get { return _document; } }
            public string DictPath { get { return DictPathFor(_document); } }
            public string Identity { get { return DictFileVisibility.ReadIdentity(DictPath); } }
            public int ServiceCalls { get { return DictPaletteCadService.Calls.Count; } }

            public static string DictPathForName(string drawingPath)
            {
                return Path.ChangeExtension(drawingPath, ".dict.json");
            }

            public string DictPathFor(AcDoc document)
            {
                return DictPathForName(document.Name);
            }

            public string LastCall()
            {
                return DictPaletteCadService.Calls.Count == 0
                    ? "" : DictPaletteCadService.Calls[DictPaletteCadService.Calls.Count - 1];
            }

            /// <summary>Creates another drawing in this fixture's directory.</summary>
            public AcDoc NewDrawing(string name, params string[] numbers)
            {
                AcDoc created = new AcDoc { Name = Path.Combine(_dir, name + ".dwg") };
                WriteDict(DictPathFor(created), numbers);
                return created;
            }

            /// <summary>Another writer replaces the dictionary on disk.</summary>
            public void WriteExternal(params string[] numbers)
            {
                WriteDict(DictPath, numbers);
                DictLoader.InvalidateCache();
            }

            /// <summary>Another writer replaces the dictionary on disk.</summary>
            public void WriteExternal(AcDoc document, params string[] numbers)
            {
                WriteDict(DictPathFor(document), numbers);
                DictLoader.InvalidateCache();
            }

            /// <summary>Same Document object, new path: exactly what Save As does.</summary>
            public void SaveAsSameDocument(string name, params string[] numbers)
            {
                _document.Name = Path.Combine(_dir, name + ".dwg");
                WriteDict(DictPathFor(_document), numbers);
                DictLoader.InvalidateCache();
            }

            /// <summary>Makes this drawing active and refreshes the palette.</summary>
            public void Activate(bool cold)
            {
                Activate(_document, cold);
            }

            public void Activate(AcDoc document, bool cold)
            {
                _document = document;
                AcDoc bound = document;
                RuntimeHost.SetActiveDocumentOverride(delegate { return bound; });
                if (cold) DictLoader.InvalidateCache();
                Poll();
            }

            private void Poll()
            {
                DictModel model = DictLoader.LoadForCurrentDrawing();
                if (model == null) Invoke("ShowNoDict");
                else Invoke("LoadDict", model, false);
            }

            // ---- the formal edit path ------------------------------------

            /// <summary>
            /// Selects the row and invokes the production EditSelectedEntry entry;
            /// <paramref name="whileOpen"/> runs inside the real modal dialog.
            /// </summary>
            public void EditSelectedEntry(Action<EditEntryDialog> whileOpen)
            {
                SelectRow("301");
                System.Windows.Forms.Timer pump = new System.Windows.Forms.Timer();
                pump.Interval = 40;
                pump.Tick += delegate
                {
                    EditEntryDialog dlg = FindDialog();
                    if (dlg == null) return;
                    pump.Stop();
                    whileOpen(dlg);
                    if (dlg.DialogResult == DialogResult.None)
                    {
                        Button cancel = (Button)typeof(EditEntryDialog)
                            .GetField("_btnCancel", Private).GetValue(dlg);
                        cancel.PerformClick();
                    }
                };
                pump.Start();
                try { Invoke("EditSelectedEntry"); }
                finally { pump.Stop(); pump.Dispose(); }
                Poll();
            }

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

            /// <summary>
            /// Establishes a real owed operation: the JSON is really written by the
            /// production edit path, and the real first sync then fails.
            /// </summary>
            public void Owe(string oldNumber, string newNumber, string detail)
            {
                DictPaletteCadService.Enqueue(new DictPaletteRenameResult { Matched = 1, Error = detail });
                Observation written = Edit(oldNumber, newNumber);
                if (!written.Written) throw new Exception("Fixture real edit failed: " + Short(written.Error));
                if (Pending() != oldNumber + ">" + newNumber)
                    throw new Exception("Fixture failed to register a real failed sync: " + Pending());
                DictPaletteCadService.Reset();
                LastChanged = -1;
            }

            public void Retry()
            {
                Button retry = (Button)typeof(DictPaletteControl)
                    .GetField("_btnRenameRetry", Private).GetValue(_control);
                if (retry == null) throw new MissingFieldException("DictPaletteControl", "_btnRenameRetry");
                retry.PerformClick();
                LastChanged = DictPaletteCadService.LastChanged;
            }

            public bool HasDiscardControl()
            {
                FieldInfo info = typeof(DictPaletteControl).GetField("_btnRenameDiscard", Private);
                Button discard = info == null ? null : info.GetValue(_control) as Button;
                return discard != null && discard.Visible;
            }

            public void Discard()
            {
                Button discard = (Button)typeof(DictPaletteControl)
                    .GetField("_btnRenameDiscard", Private).GetValue(_control) as Button;
                if (discard == null) throw new MissingFieldException("DictPaletteControl", "_btnRenameDiscard");
                discard.PerformClick();
                Poll();
            }

            public void CloseDrawing(AcDoc document)
            {
                Invoke("ReleasePendingRenameForDrawing", document.Name, document);
            }

            public bool Buttons
            {
                get
                {
                    Button retry = (Button)typeof(DictPaletteControl)
                        .GetField("_btnRenameRetry", Private).GetValue(_control);
                    Button discard = (Button)typeof(DictPaletteControl)
                        .GetField("_btnRenameDiscard", Private).GetValue(_control);
                    return retry != null && retry.Visible && discard != null && discard.Visible;
                }
            }

            /// <summary>The debt of the ACTIVE drawing, through the public read.</summary>
            public string Pending()
            {
                PendingRenameOperation pending = _control.CurrentPendingRename;
                return pending == null ? "none" : pending.OldNumber + ">" + pending.NewNumber;
            }

            /// <summary>The debt of one specific drawing, read from the registry.</summary>
            public string PendingFor(AcDoc document)
            {
                PendingRenameOperation pending = Registry().Get(document);
                return pending == null ? "none" : pending.OldNumber + ">" + pending.NewNumber;
            }

            public bool HasPendingFor(AcDoc document) { return Registry().Get(document) != null; }

            /// <summary>The identity the owed record carries (what it believes it wrote).</summary>
            public string PendingRecordIdentity()
            {
                PendingRenameOperation pending = Registry().Get(_document);
                return pending == null ? "<no-record>" : pending.DictIdentity;
            }

            /// <summary>
            /// The confirm phase of one drawing. G24 makes it per Document; when the
            /// snapshot has no such accessor the global flag is read instead, so the
            /// SAME assertion runs on both snapshots and the red one shows the leak.
            /// </summary>
            public bool NeedsReconfirmFor(AcDoc document)
            {
                object value = TryCall("PendingRenameNeedsReconfirmFor", document);
                if (value is bool) return (bool)value;
                return _control.PendingRenameNeedsReconfirm;
            }

            public bool NeedsReconfirm() { return _control.PendingRenameNeedsReconfirm; }

            public PendingRenameRegistry Registry()
            {
                return (PendingRenameRegistry)typeof(DictPaletteControl)
                    .GetField("_pendingRenames", Private).GetValue(_control);
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
                MethodInfo method = Find(name, args.Length);
                if (method == null) throw new MissingMethodException("DictPaletteControl", name);
                try { method.Invoke(_control, args); }
                catch (TargetInvocationException ex)
                {
                    throw new InvalidOperationException(name + " threw: " + ex.InnerException, ex.InnerException);
                }
            }

            /// <summary>
            /// Calls an optional accessor. A snapshot without it yields null rather
            /// than an exception; the assertion that uses it then fails on its own
            /// evidence, never silently.
            /// </summary>
            public object TryCall(string name, params object[] args)
            {
                MethodInfo method = Find(name, args.Length);
                if (method == null) return null;
                try { return method.Invoke(_control, args); }
                catch (TargetInvocationException ex)
                {
                    throw new InvalidOperationException(name + " threw: " + ex.InnerException, ex.InnerException);
                }
            }

            private MethodInfo Find(string name, int argCount)
            {
                foreach (MethodInfo candidate in typeof(DictPaletteControl).GetMethods(Private))
                {
                    if (candidate.Name == name && candidate.GetParameters().Length == argCount)
                        return candidate;
                }
                return null;
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
                    RuntimeHost.ClearActiveDocumentOverride();
                    ConfigLoader.Current = null;
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

        private delegate void Scenario(Fixture fixture);

        private static void Run(string name, Scenario scenario)
        {
            try
            {
                using (Fixture f = new Fixture()) scenario(f);
            }
            catch (Exception ex)
            {
                _skip++;
                Lines.Add("SKIP/BLOCKED|" + name + "|" + Short(ex.GetBaseException().Message));
            }
        }
    }
}
