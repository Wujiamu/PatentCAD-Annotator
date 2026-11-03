// G17 (R-004) RED adapter.
//
// Drives the PRE-FIX production palette, statement for statement. The sealed
// probe (docs/test-evidence/repair-review-20261101/rename-probe.cs) re-typed
// these methods into its own class; this adapter instead runs the code that
// actually shipped, taken from red-source/DictPaletteControl.cs:
//
//   private string _pendingRenameOld;      // no dict path, no content identity
//   private string _pendingRenameNew;
//   private object _pendingRenameDocument;
//
//   RetryRenameInDrawing():
//       if (_pendingRenameDocument != ActiveDocument) -> "changed", return
//       entry = _workflow.FindEntry(_currentDict, _pendingRenameNew)
//       if (entry == null) -> "changed", return
//       ClearRenameRetry(); RenameLeadersInDrawing(old, new)
//
// Because the retry only asks "does the CURRENT model contain the new number",
// a different dictionary that happens to contain 302 is accepted, and after a
// 302 -> 303 edit the task is cleared but the drawing is never synced. Those
// are exactly the two sealed FAIL lines.
//
// Reflection is used only to reach the private members of the real control;
// nothing is re-implemented here.

using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using PatentMarker.IO;
using PatentMarker.Palette;
using PatentMarker.I18n;
using AcDoc = Autodesk.AutoCAD.ApplicationServices.Document;

namespace G17Probe
{
    internal sealed class RedEntry : IProbeEntry
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

        /// <summary>Pre-fix: only old/new/document are kept.</summary>
        public ProbeRenameRun RecordFailedSync(string oldNumber, string newNumber, string detail)
        {
            Invoke("RenameLeadersInDrawing", oldNumber, newNumber);
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

        public void Discard()
        {
            // Pre-fix has no discard path at all; the only way out is a retry
            // that happens to match, or losing the panel.
        }

        /// <summary>
        /// The user's later edit. Pre-fix EditEntryDialog has no guard hook, so
        /// a related renumber writes the dictionary regardless of the pending
        /// task. The model and identity come from the panel, exactly as
        /// EditSelectedEntry uses _currentDict and DictLoader.GetIdentity.
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
            using (EditEntryDialog dlg = new EditEntryDialog(
                model, entry, request.DictPath, document, identity))
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
            string oldNumber = (string)Field("_pendingRenameOld").GetValue(_control);
            string newNumber = (string)Field("_pendingRenameNew").GetValue(_control);
            if (oldNumber == null || newNumber == null) return null;
            ProbePending pending = new ProbePending();
            pending.OldNumber = oldNumber;
            pending.NewNumber = newNumber;
            // Pre-fix stores neither the dictionary path nor the content
            // identity, which is the root cause of R-004.
            pending.DictPath = "";
            pending.DictIdentity = "";
            return pending;
        }

        public void CloseDrawing(string drawingPath)
        {
            // Pre-fix has no per-drawing release: closing A leaves A's task in
            // the single set of fields, ready to be retried against B.
        }

        public string Identity(string path) { return DictFileVisibility.ReadIdentity(path); }

        /// <summary>The parsed model the panel is currently showing.</summary>
        private DictModel CurrentModel()
        {
            PropertyInfo property = typeof(DictPaletteControl).GetProperty("_currentDict", Private);
            if (property == null) throw new MissingMemberException("DictPaletteControl", "_currentDict");
            return (DictModel)property.GetValue(_control, null);
        }

        /// <summary>Shows the panel the model of the given dictionary, as the poll would.</summary>
        internal void ShowModel(string dictPath)
        {
            Invoke("LoadDict", LoadModel(dictPath), true);
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
