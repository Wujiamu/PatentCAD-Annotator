// G16 (R-034) probe: shared model + entry interface.
//
// One scenario file (RecoveryProbe.cs) asserts the SAME business outcomes in
// both builds. Only two thin adapters differ:
//
//   AdapterRed.cs   -> drives the PRE-FIX production entry, statement for
//                      statement (cached model identity + ResolveRestoreCad).
//   AdapterGreen.cs -> drives the FIXED entry (recovery selection captured from
//                      disk bytes + RestoreSelectedBackup).
//
// Nothing is re-implemented in the probe: the red build exercises the code that
// actually shipped, the green build exercises the repaired code.

using System;
using System.Windows.Forms;
using PatentMarker.IO;

namespace G16Probe
{
    /// <summary>What the recovery entry could establish about the selection.</summary>
    internal sealed class ProbeSelection
    {
        public bool Available;
        public string DictPath;
        public string BackupPath;
        /// <summary>Identity the entry would hand to the arbitration action.</summary>
        public string CurrentIdentity;
        public string BackupIdentity;
        public bool CurrentParsed;
        public bool BackupParsed;
        /// <summary>Snapshot-specific selection payload.</summary>
        public object Payload;
    }

    /// <summary>Result of one arbitration attempt.</summary>
    internal sealed class ProbeOutcome
    {
        public bool Succeeded;
        public bool Wrote;
        public bool BackupRemoved;
        public string Error = "";
        public DictModel Model;
    }

    internal interface IProbeEntry
    {
        /// <summary>
        /// Whether this snapshot can capture a recovery selection from the bytes
        /// on disk at all. The pre-fix entry cannot: it derives the identity from
        /// the cached model only, which is the R-034 defect itself.
        /// </summary>
        bool HasByteSnapshot { get; }
        /// <summary>What the palette establishes when the user clicks "裁决".</summary>
        ProbeSelection Capture(string dictPath);
        /// <summary>Whether the palette offers arbitration at all (button enabled).</summary>
        bool Pending(string dictPath);
        ProbeOutcome Restore(ProbeSelection selection);
        ProbeOutcome Keep(ProbeSelection selection);
        /// <summary>Builds the dialog the palette would show; the probe never shows it.</summary>
        Form CreateDialog(ProbeSelection selection);
        void Release();
    }
}
