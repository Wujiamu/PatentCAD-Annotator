// G16 (R-034) GREEN adapter.
//
// Drives the repaired entry: the palette captures a recovery selection from the
// bytes on disk (DictPaletteWorkflow.CaptureRecoverySelection), keeps the
// arbitration button available whenever a backup is pending regardless of
// whether the current file parses, and runs the action through
// DictConflict.KeepSelectedVersion / RestoreSelectedBackup.

using System;
using System.IO;
using System.Windows.Forms;
using PatentMarker.IO;
using PatentMarker.Palette;

namespace G16Probe
{
    internal sealed class GreenEntry : IProbeEntry
    {
        private readonly DictPaletteWorkflow _workflow = new DictPaletteWorkflow();

        /// <summary>The fixed entry captures identities from the bytes on disk.</summary>
        public bool HasByteSnapshot { get { return true; } }

        public ProbeSelection Capture(string dictPath)
        {
            DictConflict.RecoverySelection selection = _workflow.CaptureRecoverySelection(dictPath);
            if (selection == null)
            {
                ProbeSelection absent = new ProbeSelection();
                absent.DictPath = dictPath;
                absent.BackupPath = DictConflict.FindWordBackup(dictPath);
                return absent;
            }
            ProbeSelection probe = new ProbeSelection();
            probe.DictPath = selection.DictPath;
            probe.BackupPath = selection.BackupPath;
            probe.CurrentIdentity = selection.CurrentIdentity;
            probe.BackupIdentity = selection.BackupIdentity;
            probe.CurrentParsed = selection.CurrentParsed;
            probe.BackupParsed = selection.BackupParsed;
            probe.Payload = selection;
            probe.Available = true;
            return probe;
        }

        public bool Pending(string dictPath)
        {
            return _workflow.IsPendingConflict(dictPath);
        }

        public ProbeOutcome Restore(ProbeSelection selection)
        {
            ProbeOutcome outcome = new ProbeOutcome();
            if (selection == null || selection.Payload == null) { outcome.Error = DictFileVisibility.ChangedError; return outcome; }
            DictConflict.RecoveryOutcome actual = _workflow.RestoreSelectedBackup((DictConflict.RecoverySelection)selection.Payload);
            outcome.Succeeded = actual.Succeeded;
            outcome.Wrote = actual.Wrote;
            outcome.BackupRemoved = actual.BackupRemoved;
            outcome.Model = actual.Model;
            outcome.Error = actual.Error ?? "";
            return outcome;
        }

        public ProbeOutcome Keep(ProbeSelection selection)
        {
            ProbeOutcome outcome = new ProbeOutcome();
            if (selection == null || selection.Payload == null) { outcome.Error = DictFileVisibility.ChangedError; return outcome; }
            DictConflict.RecoveryOutcome actual = _workflow.KeepSelectedVersion((DictConflict.RecoverySelection)selection.Payload);
            outcome.Succeeded = actual.Succeeded;
            outcome.Wrote = actual.Wrote;
            outcome.BackupRemoved = actual.BackupRemoved;
            outcome.Model = actual.Model;
            outcome.Error = actual.Error ?? "";
            return outcome;
        }

        public Form CreateDialog(ProbeSelection selection)
        {
            if (selection == null || selection.Payload == null) return null;
            return new ArbitrateDialog((DictConflict.RecoverySelection)selection.Payload);
        }

        public void Release() { }
    }
}
