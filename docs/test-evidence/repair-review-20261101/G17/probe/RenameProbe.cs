// G17 (R-004) rename probe.
//
// The SAME scenario file is compiled against two snapshots of the production
// palette. Only the adapter differs, so the assertions are identical in both
// runs and the only variable is the fix.
//
// Evidence format (same as the sealed probe):
//   PASS|name|detail
//   FAIL|name|detail
//   SKIP/BLOCKED|name|reason
//
// The first four scenarios reproduce, verbatim, the four lines of the sealed
// docs/test-evidence/repair-review-20261101/rename-probe.txt, including its two
// FAIL lines. The rest cover the matrix in docs/repair-plan.md §11 G17.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PatentMarker.IO;
using PatentMarker.Palette;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace G17Probe
{
    internal static class RenameProbe
    {
        private static string _dir;
        private static int _pass;
        private static int _fail;
        private static int _skip;
        private static readonly List<string> _lines = new List<string>();
        private static readonly Dictionary<string, AcDoc> _documents = new Dictionary<string, AcDoc>();
        private static IProbeEntry _entry;
        private static string _activeDrawing;

        private const string BackupInfix = ".word-";
        private const string BackupTs = "20261101-010101";

        private static string DictPath { get { return Path.Combine(_dir, "t.dict.json"); } }

        public static int Main()
        {
            _dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(
                typeof(RenameProbe).Assembly.Location)), "g17-work");
            if (Directory.Exists(_dir)) RemoveDir(_dir);
            Directory.CreateDirectory(_dir);

            _entry = CreateEntry();

            // === the four sealed-probe lines, same business assertions ===
            ScenarioSealedSameContext();
            ScenarioSealedDifferentDocument();
            ScenarioSealedDifferentDictionarySameNewNumber();
            ScenarioSealedChain301To303();

            // === the rest of the required matrix ===
            ScenarioRetrySucceedsSameContext();
            ScenarioNewNumberRedefined();
            ScenarioNewNumberDeleted();
            ScenarioPendingBlocksSecondJsonWrite();
            ScenarioUnrelatedRenameStillAllowed();
            ScenarioDiscardThenRenameAllowed();
            ScenarioTwoDrawingsIndependent();
            ScenarioCloseDrawingReleasesOnlyThatDrawing();
            ScenarioExternalWriteDuringFirstSync();
            ScenarioLockedTargetAndCommitFailure();
            ScenarioPlainTextSameNumberUntouched();

            _entry.Discard();
            foreach (string line in _lines) Console.WriteLine(line);
            Console.WriteLine("SUMMARY: passed=" + _pass + " failed=" + _fail + " skipped=" + _skip);
            return _fail == 0 ? 0 : 1;
        }

        private static IProbeEntry CreateEntry()
        {
#if G17_RED
            return new RedEntry();
#else
            return new GreenEntry();
#endif
        }

        // ===== the four sealed scenarios =====

        /// <summary>Sealed line 1: same drawing, same dictionary → one service call.</summary>
        private static void ScenarioSealedSameContext()
        {
            const string name = "rename_retry_same_context";
            SetupRename("301", "302", "original part", 301, 302);
            EnqueueSuccess();
            ProbeRenameRun run = _entry.Retry();
            Report(name, run.ServiceCalls == 1 && run.ReportedChanged == 1,
                "expected_calls=1|actual_calls=" + run.ServiceCalls);
        }

        /// <summary>Sealed line 2: another drawing is active → no service call.</summary>
        private static void ScenarioSealedDifferentDocument()
        {
            const string name = "rename_retry_different_document";
            SetupRename("301", "302", "original part", 301, 302);
            UseDrawing(Path.Combine(_dir, "other"));
            ProbeRenameRun run = _entry.Retry();
            Report(name, run.ServiceCalls == 0,
                "expected_calls=0|actual_calls=" + run.ServiceCalls);
        }

        /// <summary>
        /// Sealed line 3: the same drawing's dictionary is replaced by ANOTHER
        /// dictionary that happens to contain 302. The pending task belongs to
        /// the first dictionary, so the retry must not run against the second.
        /// </summary>
        private static void ScenarioSealedDifferentDictionarySameNewNumber()
        {
            const string name = "rename_retry_different_dictionary_same_new_number";
            SetupRename("301", "302", "original part", 301, 302);

            // Same drawing, but the dictionary at that path is now a different
            // one that also carries 302 under an unrelated name.
            WriteDict(DictPath, "302", "unrelated entry from another dictionary");
            UseDrawing(_dir);
            _entry.Refresh();
            EnqueueSuccess();

            ProbeRenameRun run = _entry.Retry();
            Report(name, run.ServiceCalls == 0,
                "expected_calls=0|actual_calls=" + run.ServiceCalls +
                "|pending_kept=" + run.HasPending);
        }

        /// <summary>
        /// Sealed line 4: 301→302 fails, then the same entry is edited 302→303.
        /// The dictionary must NOT reach 303 behind a still-owed 302 task, and
        /// the retry must be able to do something useful afterwards.
        /// </summary>
        private static void ScenarioSealedChain301To303()
        {
            const string name = "rename_chain_301_302_303";
            SetupRename("301", "302", "part", 301, 302);   // first sync fails

            ProbeEditRequest edit = new ProbeEditRequest();
            edit.DictPath = DictPath;
            edit.OldNumber = "302";
            edit.NewNumber = "303";
            ProbeEditResult result = _entry.Edit(edit);

            string dictionary = DictionaryNumber();
            ProbePending pending = _entry.Pending();
            int before = DictPaletteCadService.Calls.Count;
            ProbeRenameRun retry = _entry.Retry();

            bool ok = dictionary == "302" && pending != null && pending.NewNumber == "302";
            Report(name, ok, "dictionary=" + dictionary +
                "|pending_new=" + (pending == null ? "<none>" : pending.NewNumber) +
                "|retry_service_calls=" + retry.ServiceCalls +
                "|edit_written=" + result.Written);
        }

        // ===== the rest of the matrix =====

        /// <summary>A successful retry on the same context clears the record.</summary>
        private static void ScenarioRetrySucceedsSameContext()
        {
            const string name = "rename_retry_succeeds_and_clears";
            SetupRename("301", "302", "part", 301, 302);
            EnqueueSuccess();
            ProbeRenameRun run = _entry.Retry();
            Report(name, run.ServiceCalls == 1 && run.ReportedChanged == 1 && !run.HasPending,
                "calls=" + run.ServiceCalls + "|changed=" + run.ReportedChanged +
                "|pending=" + run.HasPending);
        }

        /// <summary>302 is redefined (its identity changed): keep the record, demand reconfirm.</summary>
        private static void ScenarioNewNumberRedefined()
        {
            const string name = "rename_new_number_redefined_keeps_record";
            SetupRename("301", "302", "part", 301, 302);
            WriteDict(DictPath, "302", "redefined by another writer");
            UseDrawing(_dir);
            ProbeRenameRun run = _entry.Retry();
            Report(name, run.ServiceCalls == 0 && run.HasPending,
                "calls=" + run.ServiceCalls + "|pending=" + run.HasPending);
        }

        /// <summary>302 deleted from the dictionary: the task cannot be completed as confirmed.</summary>
        private static void ScenarioNewNumberDeleted()
        {
            const string name = "rename_new_number_deleted_keeps_record";
            SetupRename("301", "302", "part", 301, 302);
            WriteDict(DictPath, "999", "part");   // 302 is gone
            UseDrawing(_dir);
            ProbeRenameRun run = _entry.Retry();
            Report(name, run.ServiceCalls == 0 && run.HasPending,
                "calls=" + run.ServiceCalls + "|pending=" + run.HasPending);
        }

        /// <summary>
        /// The core interaction rule: a related rename must be refused BEFORE
        /// the second JSON write, so the file keeps its old bytes.
        /// </summary>
        private static void ScenarioPendingBlocksSecondJsonWrite()
        {
            const string name = "rename_second_json_write_blocked";
            SetupRename("301", "302", "part", 301, 302);
            string before = _entry.Identity(DictPath);

            ProbeEditRequest edit = new ProbeEditRequest();
            edit.DictPath = DictPath;
            edit.OldNumber = "302";
            edit.NewNumber = "303";
            ProbeEditResult result = _entry.Edit(edit);

            bool unchanged = string.Equals(before, result.DictIdentityAfter, StringComparison.Ordinal);
            Report(name, !result.Written && unchanged,
                "written=" + result.Written + "|bytes_unchanged=" + unchanged +
                "|error=" + (result.Error.Length > 0 ? result.Error : "<none>"));
        }

        /// <summary>An unrelated entry on the same drawing is still editable.</summary>
        private static void ScenarioUnrelatedRenameStillAllowed()
        {
            const string name = "rename_unrelated_entry_allowed";
            SetupRename("301", "302", "part", 301, 302);
            ProbeEditRequest edit = new ProbeEditRequest();
            edit.DictPath = DictPath;
            edit.OldNumber = "777";
            edit.NewNumber = "778";
            ProbeEditResult result = _entry.Edit(edit);
            Report(name, result.Written,
                "written=" + result.Written + "|error=" + (result.Error.Length > 0 ? result.Error : "<none>"));
        }

        /// <summary>Explicit discard unblocks a related rename.</summary>
        private static void ScenarioDiscardThenRenameAllowed()
        {
            const string name = "rename_discard_then_related_allowed";
            SetupRename("301", "302", "part", 301, 302);
            _entry.Discard();
            ProbeEditRequest edit = new ProbeEditRequest();
            edit.DictPath = DictPath;
            edit.OldNumber = "302";
            edit.NewNumber = "303";
            ProbeEditResult result = _entry.Edit(edit);
            Report(name, result.Written && _entry.Pending() == null,
                "written=" + result.Written + "|pending=" + (_entry.Pending() != null));
        }

        /// <summary>A and B each failing must not overwrite each other.</summary>
        private static void ScenarioTwoDrawingsIndependent()
        {
            const string name = "rename_two_drawings_do_not_overwrite";
            string dirA = Path.Combine(_dir, "A");
            string dirB = Path.Combine(_dir, "B");
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);
            WriteDict(Path.Combine(dirA, "t.dict.json"), "302", "A part");
            WriteDict(Path.Combine(dirB, "t.dict.json"), "402", "B part");

            UseDrawing(dirA);
            _entry.Refresh();
            DictPaletteCadService.Enqueue(new PatentMarker.Palette.DictPaletteRenameResult
            { Matched = 1, Changed = 0, Committed = false, Error = "refused in A" });
            _entry.RecordFailedSync("301", "302", "refused in A");
            ProbePending recordedA = _entry.Pending();

            UseDrawing(dirB);
            _entry.Refresh();
            DictPaletteCadService.Enqueue(new PatentMarker.Palette.DictPaletteRenameResult
            { Matched = 1, Changed = 0, Committed = false, Error = "refused in B" });
            _entry.RecordFailedSync("401", "402", "refused in B");
            ProbePending recordedB = _entry.Pending();

            // Back on A: A's own task must still be there, untouched by B's failure.
            UseDrawing(dirA);
            _entry.Refresh();
            ProbePending onA = _entry.Pending();
            UseDrawing(dirB);
            _entry.Refresh();
            ProbePending onB = _entry.Pending();

            bool okA = recordedA != null && recordedA.OldNumber == "301" &&
                onA != null && onA.OldNumber == "301";
            bool okB = recordedB != null && recordedB.OldNumber == "401" &&
                onB != null && onB.OldNumber == "401";
            Report(name, okA && okB,
                "A_recorded=" + Describe(recordedA) + "|A_after_B=" + Describe(onA) +
                "|B_recorded=" + Describe(recordedB) + "|B_after_A=" + Describe(onB));
        }

        /// <summary>Closing drawing A releases A's task only.</summary>
        private static void ScenarioCloseDrawingReleasesOnlyThatDrawing()
        {
            const string name = "rename_close_drawing_releases_only_that_drawing";
            string dirA = Path.Combine(_dir, "A");
            string dirB = Path.Combine(_dir, "B");
            WriteDict(Path.Combine(dirA, "t.dict.json"), "302", "A part");
            WriteDict(Path.Combine(dirB, "t.dict.json"), "402", "B part");

            UseDrawing(dirA);
            _entry.Refresh();
            DictPaletteCadService.Enqueue(new PatentMarker.Palette.DictPaletteRenameResult
            { Matched = 1, Changed = 0, Committed = false, Error = "refused in A" });
            _entry.RecordFailedSync("301", "302", "refused in A");
            _entry.CloseDrawing(Path.Combine(dirA, "t.dwg"));

            UseDrawing(dirB);
            _entry.Refresh();
            DictPaletteCadService.Enqueue(new PatentMarker.Palette.DictPaletteRenameResult
            { Matched = 1, Changed = 0, Committed = false, Error = "refused in B" });
            _entry.RecordFailedSync("401", "402", "refused in B");

            UseDrawing(dirA);
            _entry.Refresh();
            ProbeRenameRun reopened = _entry.Retry();
            Report(name, reopened.Pending == null && reopened.ServiceCalls == 0,
                "A_after_close=" + Describe(reopened.Pending) +
                "|A_service_calls=" + reopened.ServiceCalls);
        }

        /// <summary>An external write between confirmation and the CAD transaction blocks the write.</summary>
        private static void ScenarioExternalWriteDuringFirstSync()
        {
            const string name = "rename_external_write_before_transaction";
            SetupRename("301", "302", "part", 301, 302);
            WriteDict(DictPath, "302", "written by another process");
            UseDrawing(_dir);
            ProbeRenameRun run = _entry.Retry();
            Report(name, run.ServiceCalls == 0 && run.HasPending,
                "calls=" + run.ServiceCalls + "|pending=" + run.HasPending);
        }

        /// <summary>Locked target / failed commit is reported, never as "synced".</summary>
        private static void ScenarioLockedTargetAndCommitFailure()
        {
            const string name = "rename_lock_or_commit_failure_is_not_synced";
            SetupRename("301", "302", "part", 301, 302);
            DictPaletteCadService.Enqueue(new PatentMarker.Palette.DictPaletteRenameResult
            {
                Matched = 1,
                Changed = 0,
                Committed = false,
                Error = "commit failure"
            });
            ProbeRenameRun run = _entry.Retry();
            bool reported = run.ReportedChanged <= 0 && run.HasPending;
            Report(name, reported,
                "changed=" + run.ReportedChanged + "|pending=" + run.HasPending);
        }

        /// <summary>Ordinary text with the same number is still not renamed.</summary>
        private static void ScenarioPlainTextSameNumberUntouched()
        {
            const string name = "rename_plain_text_same_number_untouched";
            DictPaletteCadService.Reset();
            DictLoader.InvalidateCache();
            ConfigLoader.Current = null;
            WriteDict(DictPath, "302", "part");
            UseDrawing(_dir);
            _entry.Refresh();
            // The service finds no PAT annotation carrying the old number and
            // reports no error: plain text is simply not a rename target.
            DictPaletteCadService.Enqueue(new PatentMarker.Palette.DictPaletteRenameResult
            { Matched = 0, Changed = 0, Committed = true, Error = "" });
            ProbeRenameRun run = _entry.RecordFailedSync("301", "302", "");
            // "0 synced" is the honest report, and nothing is left owed.
            Report(name, run.ReportedChanged == 0 && !run.HasPending,
                "changed=" + run.ReportedChanged + "|pending=" + run.HasPending +
                "|service_calls=" + DictPaletteCadService.Calls.Count +
                "|status=" + run.Status);
        }

        // ===== helpers =====

        /// <summary>
        /// The shared starting state: one dictionary holding the new number,
        /// one drawing refreshed through the production poll, and a failed
        /// first sync recorded on it.
        /// </summary>
        private static void SetupRename(string oldNumber, string newNumber, string newName,
            int matched, int changed)
        {
            SetupRename(oldNumber, newNumber, newName, matched, changed, "refused");
        }

        private static void SetupRename(string oldNumber, string newNumber, string newName,
            int matched, int changed, string error)
        {
            DictPaletteCadService.Reset();
            DictLoader.InvalidateCache();
            ConfigLoader.Current = null;
            WriteDict(DictPath, newNumber, newName);
            UseDrawing(_dir);
            _entry.Refresh();
            DictPaletteCadService.Enqueue(new PatentMarker.Palette.DictPaletteRenameResult
            {
                Matched = matched,
                Changed = changed,
                Committed = false,
                Error = error
            });
            _entry.RecordFailedSync(oldNumber, newNumber, error);
        }

        /// <summary>Queues a fully successful renumber (all matched entities changed).</summary>
        private static void EnqueueSuccess()
        {
            DictPaletteCadService.Enqueue(new PatentMarker.Palette.DictPaletteRenameResult
            { Matched = 1, Changed = 1, Committed = true });
        }

        private static string DictionaryNumber()
        {
            DictModel model = DictLoader.Load(DictPath);
            if (model == null || model.Entries.Count == 0) return "<none>";
            return model.Entries[0].Number;
        }

        private static void WriteDict(string path, string number, string name)
        {
            string dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string json = "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n" +
                "    \"extracted_at\": \"2026-11-01T01:23:45\"\n  },\n  \"entries\": [\n" +
                "    {\n      \"number\": \"" + number + "\",\n      \"name\": \"" + name + "\"\n    }" +
                ",\n    {\n      \"number\": \"777\",\n      \"name\": \"untouched\"\n    }\n  ],\n" +
                "  \"warnings\": []\n}";
            File.WriteAllText(path, json, new UTF8Encoding(false));
        }

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
            _activeDrawing = drawing;
            PatentMarker.IO.RuntimeHost.SetActiveDocumentOverride(delegate { return stable; });
        }

        private static void RemoveDir(string dir)
        {
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(dir, true);
        }

        private static string Describe(ProbePending pending)
        {
            if (pending == null) return "<none>";
            return pending.OldNumber + ">" + pending.NewNumber + "@" +
                (pending.DictPath.Length == 0 ? "<no-path>" : Path.GetFileName(pending.DictPath));
        }

        private static void Report(string name, bool ok, string detail)
        {
            if (ok) { _pass++; _lines.Add("PASS|" + name + "|" + detail); }
            else { _fail++; _lines.Add("FAIL|" + name + "|" + detail); }
        }
    }
}
