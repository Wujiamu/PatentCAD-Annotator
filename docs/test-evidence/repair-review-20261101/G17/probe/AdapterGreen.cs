// G17 (R-004) GREEN adapter.
//
// Drives the REPAIRED production palette. The same real DictPaletteControl is
// exercised, so the fix is measured on the code that ships:
//
//   * RenameLeadersInDrawing(old, new, context) captures the drawing + full
//     dictionary path + the identity of the JSON that was written.
//   * RecordPendingRename stores old/new/detail + that context per drawing.
//   * RetryRenameInDrawing goes through RenameRetryPlanner.Decide: same
//     document, same dictionary path, same content identity, and the new
//     number still present. Otherwise the record is KEPT and the user is asked
//     to reconfirm or reload.
//   * EditEntryDialog.ApplyAndWrite consults PendingRenameGuard BEFORE the
//     second JSON write, so a chained 302 -> 303 is refused, not written.

using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using PatentMarker.IO;
using PatentMarker.Palette;
using PatentMarker.I18n;

namespace G17Probe
{
    internal sealed class GreenEntry : IProbeEntry
    {
        private readonly DictPaletteControl _control = new DictPaletteControl();

        private static readonly BindingFlags Private =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private FieldInfo Field(string name)
        {
            FieldInfo field = typeof(DictPaletteControl).GetField(name, Private);
            if (field == null) throw new MissingFieldException("DictPaletteControl", name);
            return field;
        }

        private void Invoke(string name, params object[] args)
        {
            MethodInfo method = FindMethod(name, args.Length);
            method.Invoke(_control, args);
        }

        private static MethodInfo FindMethod(string name, int argc)
        {
            foreach (MethodInfo candidate in typeof(DictPaletteControl).GetMethods(Private))
            {
                if (candidate.Name != name) continue;
                if (candidate.GetParameters().Length == argc) return candidate;
            }
            throw new MissingMethodException("DictPaletteControl", name);
        }

        /// <summary>The 2-second poll: load the current drawing's dictionary.</summary>
        public void Refresh()
        {
            DictModel model = DictLoader.LoadForCurrentDrawing();
            if (model == null)
            {
                Invoke("ShowNoDict");
                return;
            }
            Invoke("LoadDict", model, true);
        }

        /// <summary>
        /// The failed first sync. The palette builds the context from the model
        /// it just wrote, exactly as the edit path does.
        /// </summary>
        public ProbeRenameRun RecordFailedSync(string oldNumber, string newNumber, string detail)
        {
            string dictPath = DictLoader.ResolveDictPath();
            RenameSyncContext context = RenameSyncContext.Capture(
                RuntimeHost.ActiveDocument, dictPath, null);
            Invoke("RenameLeadersInDrawing", oldNumber, newNumber, context);
            ProbeRenameRun run = new ProbeRenameRun();
            run.OldNumber = oldNumber;
            run.NewNumber = newNumber;
            run.Status = Status();
            run.ReportedChanged = ReportedChanged(run.Status, oldNumber, newNumber);
            run.HasPending = Pending() != null;
            run.Pending = Pending();
            return run;
        }

        public ProbeRenameRun Retry()
        {
            int before = DictPaletteCadService.Calls.Count;
            ProbeRenameRun run = new ProbeRenameRun();
            ProbePending pendingBefore = Pending();
            if (pendingBefore != null)
            {
                run.OldNumber = pendingBefore.OldNumber;
                run.NewNumber = pendingBefore.NewNumber;
            }
            try { Invoke("RetryRenameInDrawing"); }
            catch (TargetInvocationException ex) { run.Status = "exception: " + ex.InnerException; }
            run.ServiceCalls = DictPaletteCadService.Calls.Count - before;
            if (run.OldNumber == null && run.ServiceCalls > 0)
            {
                // No record left to read the pair from; use the call that ran.
                string[] parts = DictPaletteCadService.Calls[DictPaletteCadService.Calls.Count - 1].Split('>');
                if (parts.Length == 2) { run.OldNumber = parts[0]; run.NewNumber = parts[1]; }
            }
            run.Status = run.Status.Length > 0 ? run.Status : Status();
            run.ReportedChanged = ReportedChanged(run.Status, run.OldNumber, run.NewNumber);
            run.HasPending = Pending() != null;
            run.Pending = Pending();
            return run;
        }

        /// <summary>
        /// The committed count the palette actually reported to the user,
        /// recovered from the localized status line rather than assumed.
        /// </summary>
        private static int ReportedChanged(string status, string oldNumber, string newNumber)
        {
            if (string.IsNullOrEmpty(status) || oldNumber == null || newNumber == null) return -1;
            for (int n = 0; n <= 32; n++)
            {
                if (status == string.Format(Strings.Status_NumberSynced, n, oldNumber, newNumber))
                    return n;
            }
            return -1;
        }

        /// <summary>The repaired palette has an explicit "discard" action.</summary>
        public void Discard()
        {
            Invoke("DiscardPendingRename");
        }

        /// <summary>
        /// The user's later edit, through the real dialog and the real guard.
        /// A related pending operation must be refused BEFORE the JSON write.
        /// </summary>
        public ProbeEditResult Edit(ProbeEditRequest request)
        {
            ProbeEditResult result = new ProbeEditResult();
            DictModel model = CurrentModel();
            if (model == null)
            {
                result.Error = "panel has no model";
                result.DictIdentityAfter = Identity(request.DictPath);
                return result;
            }
            DictEntry entry = Find(model, request.OldNumber);
            object document = RuntimeHost.ActiveDocument;
            string identity = DictLoader.GetIdentity(model, request.DictPath);
            PendingRenameGuard guard = new PendingRenameGuard(
                PendingRegistry(), document, request.DictPath);
            using (EditEntryDialog dlg = new EditEntryDialog(
                model, entry, request.DictPath, document, identity, guard))
            {
                TextBox number = (TextBox)typeof(EditEntryDialog)
                    .GetField("_txtNumber", Private).GetValue(dlg);
                number.Text = request.NewNumber;
                Label error = (Label)typeof(EditEntryDialog)
                    .GetField("_lblError", Private).GetValue(dlg);
                try
                {
                    typeof(EditEntryDialog).GetMethod("BtnSave_Click", Private)
                        .Invoke(dlg, new object[] { null, EventArgs.Empty });
                }
                catch (TargetInvocationException ex)
                {
                    result.Error = "exception: " + ex.InnerException;
                }
                result.Error = result.Error.Length > 0 ? result.Error : error.Text;
                result.Written = dlg.DialogResult == DialogResult.OK;
            }
            result.DictIdentityAfter = Identity(request.DictPath);
            return result;
        }

        public ProbePending Pending()
        {
            PendingRenameOperation pending = _control.CurrentPendingRename;
            if (pending == null) return null;
            ProbePending probe = new ProbePending();
            probe.OldNumber = pending.OldNumber;
            probe.NewNumber = pending.NewNumber;
            probe.DictPath = pending.DictPath;
            probe.DictIdentity = pending.DictIdentity;
            return probe;
        }

        public void CloseDrawing(string drawingPath)
        {
            Invoke("ReleasePendingRenameForDrawing", drawingPath, RuntimeHost.ActiveDocument);
        }

        public string Identity(string path) { return DictFileVisibility.ReadIdentity(path); }

        /// <summary>The parsed model the panel is currently showing.</summary>
        private DictModel CurrentModel()
        {
            PropertyInfo property = typeof(DictPaletteControl).GetProperty("_currentDict", Private);
            if (property == null) throw new MissingMemberException("DictPaletteControl", "_currentDict");
            return (DictModel)property.GetValue(_control, null);
        }

        internal void ShowModel(string dictPath)
        {
            Invoke("LoadDict", LoadModel(dictPath), true);
        }

        private PendingRenameRegistry PendingRegistry()
        {
            return (PendingRenameRegistry)Field("_pendingRenames").GetValue(_control);
        }

        private string Status()
        {
            Label label = (Label)typeof(DictPaletteControl).GetField("_lblStatus", Private)
                .GetValue(_control);
            return label == null ? "" : label.Text;
        }

        private static DictModel LoadModel(string path)
        {
            DictModel model = DictLoader.Load(path);
            if (model == null) throw new IOException("dictionary could not be parsed: " + path);
            return model;
        }

        private static DictEntry Find(DictModel model, string number)
        {
            foreach (DictEntry entry in model.Entries)
                if (NumberIdentity.AreEqual(entry.Number, number)) return entry;
            return null;
        }
    }
}
