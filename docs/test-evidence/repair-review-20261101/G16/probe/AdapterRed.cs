// G16 (R-034) RED adapter.
//
// Reproduces, statement for statement, what the pre-fix palette did when the
// user clicked "裁决" (DictPaletteControl.BtnArbitrate_Click) and confirmed
// "restore the CAD version":
//
//     identity = DictLoader.GetIdentity(_currentDict, dictPath)   // cached model
//     backup   = DictConflict.FindWordBackup(dictPath)
//     pending  = _currentDict != null && IsPendingConflict(_currentDict, dictPath)
//     outcome  = DictConflict.ResolveRestoreCad(dictPath, identity, backup,
//                    backupIdentity, IsCurrentContext, out error)
//
// The identity therefore comes from the CACHED MODEL, not from the bytes on
// disk. Once the current JSON is corrupt the cached identity no longer matches
// the file, ReadSelected rejects the pair, and the user is told
// PATENTMARKER_CONTENT_CHANGED - with a perfectly valid backup beside the file.
// That is the sealed red light.

using System;
using System.IO;
using System.Windows.Forms;
using PatentMarker.IO;
using PatentMarker.Palette;

namespace G16Probe
{
    internal sealed class RedEntry : IProbeEntry
    {
        private readonly DictPaletteWorkflow _workflow = new DictPaletteWorkflow();
        private string _contextPath;

        /// <summary>The pre-fix entry has no byte snapshot; identity comes from the cache.</summary>
        public bool HasByteSnapshot { get { return false; } }

        /// <summary>Mirrors the pre-fix panel state: the model the panel last parsed.</summary>
        private DictModel CachedModel() { return _workflow.LoadCurrent(); }

        private sealed class RedPayload
        {
            public string Identity;
            public string BackupPath;
            public string BackupIdentity;
        }

        public ProbeSelection Capture(string dictPath)
        {
            _contextPath = dictPath;
            ProbeSelection probe = new ProbeSelection();
            probe.DictPath = dictPath;
            probe.BackupPath = DictConflict.FindWordBackup(dictPath);

            DictModel model = CachedModel();
            string identity = DictLoader.GetIdentity(model, dictPath);
            string backupIdentity = probe.BackupPath == null
                ? DictFileVisibility.MissingIdentity
                : DictFileVisibility.ReadIdentity(probe.BackupPath);

            RedPayload payload = new RedPayload();
            payload.Identity = identity;
            payload.BackupPath = probe.BackupPath;
            payload.BackupIdentity = backupIdentity;
            probe.Payload = payload;
            probe.CurrentIdentity = identity;
            probe.BackupIdentity = backupIdentity;
            // The pre-fix entry never parses either file to describe the selection,
            // so it cannot say which choice is available. The probe still learns
            // that from the bytes on disk; see RecoveryProbe.BackupParses().
            probe.CurrentParsed = model != null;
            probe.BackupParsed = probe.BackupPath != null &&
                DictLoader.Load(probe.BackupPath) != null;
            probe.Available = identity != null && probe.BackupPath != null &&
                backupIdentity != DictFileVisibility.MissingIdentity;
            return probe;
        }

        public bool Pending(string dictPath)
        {
            // Pre-fix: an absent model disables the button outright.
            DictModel model = CachedModel();
            return model != null && _workflow.IsPendingConflict(model, dictPath);
        }

        public ProbeOutcome Restore(ProbeSelection selection)
        {
            ProbeOutcome outcome = new ProbeOutcome();
            if (selection == null || selection.Payload == null) { outcome.Error = DictFileVisibility.ChangedError; return outcome; }
            RedPayload payload = (RedPayload)selection.Payload;
            DictModel restored = DictConflict.ResolveRestoreCad(
                selection.DictPath, payload.Identity, payload.BackupPath,
                payload.BackupIdentity, Current, out outcome.Error);
            outcome.Succeeded = restored != null;
            outcome.Model = restored;
            outcome.Wrote = restored != null;
            outcome.BackupRemoved = payload.BackupPath != null && !File.Exists(payload.BackupPath);
            return outcome;
        }

        public ProbeOutcome Keep(ProbeSelection selection)
        {
            ProbeOutcome outcome = new ProbeOutcome();
            if (selection == null || selection.Payload == null) { outcome.Error = DictFileVisibility.ChangedError; return outcome; }
            RedPayload payload = (RedPayload)selection.Payload;
            bool ok = DictConflict.ResolveKeepWord(
                selection.DictPath, payload.Identity, payload.BackupPath,
                payload.BackupIdentity, Current, out outcome.Error);
            outcome.Succeeded = ok;
            outcome.Wrote = ok;
            outcome.BackupRemoved = payload.BackupPath != null && !File.Exists(payload.BackupPath);
            return outcome;
        }

        public Form CreateDialog(ProbeSelection selection)
        {
            if (selection == null || selection.Payload == null) return null;
            RedPayload payload = (RedPayload)selection.Payload;
            object document = RuntimeHost.ActiveDocument;
            return new ArbitrateDialog(selection.DictPath, document, payload.Identity,
                payload.BackupPath, payload.BackupIdentity);
        }

        private bool Current()
        {
            return DictPaletteWorkflow.IsCurrentContext(RuntimeHost.ActiveDocument, _contextPath);
        }

        public void Release() { }
    }
}
