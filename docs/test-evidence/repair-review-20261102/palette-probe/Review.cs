using System.Reflection;
using System.Text;
using G17Probe;
using PatentMarker.IO;
using PatentMarker.Palette;
using Doc = Autodesk.AutoCAD.ApplicationServices.Document;

internal static class Review
{
    static int failed, total;
    static string dir, path;
    static Doc document;
    static void Check(string id, bool ok, string detail)
    {
        total++; if (!ok) failed++;
        Console.WriteLine((ok ? "PASS" : "FAIL") + "|" + id + "|" + detail);
    }
    static void Write(params string[] numbers)
    {
        File.WriteAllText(path, "{\"metadata\":{},\"entries\":[" + string.Join(",", numbers.Select(n =>
            "{\"number\":\"" + n + "\",\"name\":\"part\"}")) + "],\"warnings\":[]}", new UTF8Encoding(false));
        DictLoader.InvalidateCache();
    }
    static GreenEntry Setup()
    {
        dir = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        path = Path.Combine(dir, "t.dict.json");
        document = new Doc { Name = Path.Combine(dir, "t.dwg") };
        RuntimeHost.SetActiveDocumentOverride(delegate { return document; });
        ConfigLoader.Current = null;
        DictPaletteCadService.Reset();
        Write("302", "401");
        var entry = new GreenEntry(); entry.Refresh();
        DictPaletteCadService.Enqueue(new DictPaletteRenameResult { Matched = 1, Error = "injected CAD failure" });
        entry.RecordFailedSync("301", "302", "");
        return entry;
    }
    static string Pair(GreenEntry entry)
    {
        var p = entry.Pending(); return p == null ? "none" : p.OldNumber + ">" + p.NewNumber;
    }
    [STAThread]
    static int Main()
    {
        foreach (string mode in new[] { "success", "no-match", "failure" })
        {
            var e = Setup();
            var edit = e.Edit(new ProbeEditRequest { DictPath = path, OldNumber = "401", NewNumber = "402" });
            Check("unrelated-" + mode + "-json-written", edit.Written, "written=" + edit.Written);
            e.Refresh();
            var result = mode == "success" ? new DictPaletteRenameResult { Matched = 1, Changed = 1, Committed = true }
                : mode == "no-match" ? new DictPaletteRenameResult { Matched = 0, Committed = true }
                : new DictPaletteRenameResult { Matched = 1, Error = "second injected failure" };
            DictPaletteCadService.Enqueue(result);
            e.RecordFailedSync("401", "402", "");
            Check("unrelated-" + mode + "-preserves-first-pending", Pair(e) == "301>302", "expected=301>302|actual=" + Pair(e));
        }
        {
            var e = Setup();
            var edit = e.Edit(new ProbeEditRequest { DictPath = path, OldNumber = "302", NewNumber = "303" });
            Check("related-followup-blocked", !edit.Written && Pair(e) == "301>302", "written=" + edit.Written + "|pending=" + Pair(e));
            DictPaletteCadService.Enqueue(new DictPaletteRenameResult { Matched = 1, Changed = 1, Committed = true });
            e.Retry();
            Check("same-operation-success-clears", e.Pending() == null, "pending=" + Pair(e));
        }
        {
            var e = Setup(); e.Discard();
            var context = RenameSyncContext.Capture(document, path, DictFileVisibility.ReadIdentity(path));
            Write("302", "401", "777");
            var control = typeof(GreenEntry).GetField("_control", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(e);
            var method = typeof(DictPaletteControl).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single(m => m.Name == "RenameLeadersInDrawing" && m.GetParameters().Length == 3);
            int before = DictPaletteCadService.Calls.Count;
            method.Invoke(control, new object[] { "301", "302", context });
            Check("first-sync-drift-no-cad-call", DictPaletteCadService.Calls.Count == before, "calls=" + (DictPaletteCadService.Calls.Count - before));
            Check("first-sync-drift-retains-owed-operation", Pair(e) == "301>302", "expected=301>302|actual=" + Pair(e));
        }
        {
            var e = Setup(); e.Discard();
            string backup = path + ".word-20261102-010101.bak";
            File.WriteAllText(backup, "{broken backup", new UTF8Encoding(false));
            var selection = DictConflict.CaptureSelection(path, backup);
            Check("valid-current-invalid-backup-fixture", selection.CurrentParsed && !selection.BackupParsed,
                "current=" + selection.CurrentParsed + "|backup=" + selection.BackupParsed);
            var control = typeof(GreenEntry).GetField("_control", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(e);
            typeof(DictPaletteControl).GetMethod("BtnArbitrate_Click", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(control, new object[] { null, EventArgs.Empty });
            string status = ((System.Windows.Forms.Label)typeof(DictPaletteControl)
                .GetField("_lblStatus", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(control)).Text;
            string rejection = string.Format(PatentMarker.I18n.Strings.Conflict_Failed,
                DictPaletteWorkflow.DescribeWriteError(DictConflict.BackupUnparsableError));
            Check("valid-current-keep-action-not-rejected-by-bad-backup", status != rejection,
                "status=" + status + "|backup-still-present=" + File.Exists(backup));
            var outcome = DictConflict.KeepSelectedVersion(selection, delegate { return true; });
            Check("valid-current-keep-component-works", outcome.Succeeded && outcome.BackupRemoved,
                "success=" + outcome.Succeeded + "|error=" + outcome.Error);
        }
        foreach (string state in new[] { "valid-valid", "bad-valid", "bad-bad" })
        {
            var e = Setup(); e.Discard();
            string backup = path + ".word-20261102-010101.bak";
            File.WriteAllText(backup, state == "bad-bad" ? "{bad backup" : File.ReadAllText(path), new UTF8Encoding(false));
            if (state.StartsWith("bad")) File.WriteAllText(path, "{bad current", new UTF8Encoding(false));
            var selection = DictConflict.CaptureSelection(path, backup);
            string before = DictFileVisibility.ReadIdentity(path);
            if (state == "valid-valid")
            {
                var keep = DictConflict.KeepSelectedVersion(selection, delegate { return true; });
                Check("valid-valid-keep", keep.Succeeded && !File.Exists(backup) && before == DictFileVisibility.ReadIdentity(path), "success=" + keep.Succeeded);
            }
            else if (state == "bad-valid")
            {
                using (var dialog = new ArbitrateDialog(selection))
                {
                    var button = (System.Windows.Forms.Button)typeof(ArbitrateDialog)
                        .GetField("_btnKeepWord", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(dialog);
                    Check("bad-current-valid-backup-keep-disabled", !button.Enabled && !selection.CurrentParsed && selection.BackupParsed, "enabled=" + button.Enabled);
                }
                var restored = DictConflict.RestoreSelectedBackup(selection, delegate { return true; });
                Check("bad-current-valid-backup-restore", restored.Succeeded && restored.Wrote && restored.Model != null,
                    "success=" + restored.Succeeded + "|error=" + restored.Error);
            }
            else
            {
                var keep = DictConflict.KeepSelectedVersion(selection, delegate { return true; });
                var restored = DictConflict.RestoreSelectedBackup(selection, delegate { return true; });
                Check("both-bad-preserve-files", !keep.Succeeded && !restored.Succeeded && File.Exists(backup) && before == DictFileVisibility.ReadIdentity(path),
                    "keep=" + keep.Succeeded + "|restore=" + restored.Succeeded);
            }
        }
        Console.WriteLine("SUMMARY|total=" + total + "|failed=" + failed);
        return failed == 0 ? 0 : 1;
    }
}
