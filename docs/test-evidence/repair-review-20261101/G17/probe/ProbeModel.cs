// G17 (R-004) probe model.
//
// One scenario file (RenameProbe.cs) asserts the SAME business outcomes against
// two snapshots of the production palette code. Only two thin adapters differ:
//
//   AdapterRed.cs   -> drives the PRE-FIX palette entry, statement for
//                      statement: pending state is only old/new/Document, and
//                      RetryRenameInDrawing looks up the new number in whatever
//                      dictionary is current, then clears the task and retries.
//   AdapterGreen.cs -> drives the FIXED entry: a document-bound pending record
//                      (path + content identity + old/new + failure) that is
//                      checked before the second JSON write and before the CAD
//                      transaction.
//
// Nothing is re-implemented in the probe: the red build exercises the code that
// actually shipped, the green build the repaired code.

using System;

namespace G17Probe
{
    /// <summary>What one rename attempt did to the drawing and to pending state.</summary>
    internal sealed class ProbeRenameRun
    {
        /// <summary>How many times the CAD renumber service was actually invoked.</summary>
        public int ServiceCalls;
        /// <summary>Values the palette reported as committed (Changed).</summary>
        public int ReportedChanged;
        /// <summary>The status text the palette left behind.</summary>
        public string Status = "";
        /// <summary>The pending operation as it stood before this attempt.</summary>
        public string OldNumber;
        public string NewNumber;
        /// <summary>Whether a pending operation is still owed for this drawing.</summary>
        public bool HasPending;
        /// <summary>The pending operation as the palette would retry it.</summary>
        public ProbePending Pending;
    }

    /// <summary>The specific pending operation a snapshot is willing to retry.</summary>
    internal sealed class ProbePending
    {
        public string OldNumber;
        public string NewNumber;
        public string DictPath;
        public string DictIdentity;
    }

    /// <summary>A rename the user asked for through the edit dialog.</summary>
    internal sealed class ProbeEditRequest
    {
        public string DictPath;
        public string OldNumber;
        public string NewNumber;
        public string NewName = "part";
    }

    internal sealed class ProbeEditResult
    {
        /// <summary>False when the entry refused to write (blocked before JSON).</summary>
        public bool Written;
        /// <summary>The dialog's error label text when it refused.</summary>
        public string Error = "";
        /// <summary>Bytes on disk after the attempt, to prove nothing was written.</summary>
        public string DictIdentityAfter;
    }

    internal interface IProbeEntry
    {
        /// <summary>
        /// Mimics the 2-second poll: load the current drawing's dictionary
        /// through the production cache so the panel has a parsed model with a
        /// registered identity, exactly as a real edit would find it.
        /// </summary>
        void Refresh();

        /// <summary>
        /// Records the outcome of a failed first sync (301 → 302) and reports
        /// what the palette told the user and what it left owed.
        /// </summary>
        ProbeRenameRun RecordFailedSync(string oldNumber, string newNumber, string detail);

        /// <summary>The explicit retry button.</summary>
        ProbeRenameRun Retry();

        /// <summary>The explicit "discard the pending operation" action.</summary>
        void Discard();

        /// <summary>
        /// The user's later edit through the dialog. Must be refused *before*
        /// the second JSON write when a related pending operation exists.
        /// </summary>
        ProbeEditResult Edit(ProbeEditRequest request);

        /// <summary>The pending record the palette holds for the active drawing.</summary>
        ProbePending Pending();

        /// <summary>Simulates the user closing the drawing.</summary>
        void CloseDrawing(string drawingPath);

        /// <summary>Reads the dictionary bytes the way the palette verifies them.</summary>
        string Identity(string path);
    }
}
