using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using AcDb = Autodesk.AutoCAD.DatabaseServices;
using PatentMarker.Commands;
using PatentMarker.IO;

namespace PatentMarker.Palette
{
    /// <summary>
    /// CAD-side operations used by the palette.  The control owns user
    /// interaction and status text; this service owns document locks,
    /// transactions, and PAT annotation entity traversal.
    /// </summary>
    public sealed class DictPaletteDeleteResult
    {
        public int Deleted;
        public int Skipped;
    }

    /// <summary>
    /// Result of a renumber sync.  When <see cref="Committed"/> is false the
    /// CAD transaction was rolled back, so the drawing still shows the old
    /// number everywhere; the dictionary may already carry the new one.
    /// </summary>
    public sealed class DictPaletteRenameResult
    {
        public int Matched;
        public int Changed;
        public bool Committed;
        public readonly List<string> Failures = new List<string>();
        public string Error = "";

        public int Failed { get { return Matched - Changed; } }
        public bool Complete { get { return Committed && Matched > 0 && Failed == 0; } }
    }

    public static class DictPaletteCadService
    {
        /// <summary>
        /// Renames matching PAT annotation text in model space.  Any single
        /// write failure rolls the whole CAD transaction back, so the caller
        /// reports "0 of N updated" instead of a partial count.  JSON and DWG
        /// are not one transaction: the caller still has to say that the
        /// dictionary changed and the drawing did not.
        /// </summary>
        public static DictPaletteRenameResult RenameNumber(
            Document doc, string oldNumber, string newNumber)
        {
            DictPaletteRenameResult result = new DictPaletteRenameResult();
            if (doc == null) { result.Error = "No active document."; return result; }

            AcDb.Database db = doc.Database;
            try
            {
                using (DocumentLock docLock = doc.LockDocument())
                using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
                {
                    AcDb.BlockTable bt = (AcDb.BlockTable)tr.GetObject(
                        db.BlockTableId, AcDb.OpenMode.ForRead);
                    AcDb.BlockTableRecord btr = (AcDb.BlockTableRecord)tr.GetObject(
                        bt[AcDb.BlockTableRecord.ModelSpace], AcDb.OpenMode.ForRead);
                    RenameResult pass = PatEntityHelper.RenameNumberInModelSpace(
                        tr, btr, oldNumber, newNumber);
                    result.Matched = pass.Matched;
                    result.Failures.AddRange(pass.Failures);
                    if (pass.Matched > 0 && pass.Changed != pass.Matched)
                    {
                        result.Error = pass.Changed + " of " + pass.Matched
                            + " PAT annotation(s) could not be updated.";
                        tr.Abort();
                        return result;
                    }
                    tr.Commit();
                    result.Changed = pass.Changed;
                    result.Committed = true;
                    return result;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().Name + ": " + ex.Message;
                result.Changed = 0;
                result.Committed = false;
                return result;
            }
        }

        /// <summary>
        /// Deletes all PAT Leader entities, their associated MText objects,
        /// and standalone PAT MText objects. Non-PAT leaders are retained and
        /// counted as skipped.
        /// </summary>
        public static DictPaletteDeleteResult DeleteAll(Document doc)
        {
            DictPaletteDeleteResult result = new DictPaletteDeleteResult();
            if (doc == null) return result;

            AcDb.Database db = doc.Database;
            using (DocumentLock docLock = doc.LockDocument())
            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.BlockTable bt = (AcDb.BlockTable)tr.GetObject(
                    db.BlockTableId, AcDb.OpenMode.ForRead);
                AcDb.BlockTableRecord btr = (AcDb.BlockTableRecord)tr.GetObject(
                    bt[AcDb.BlockTableRecord.ModelSpace], AcDb.OpenMode.ForWrite);

                List<AcDb.ObjectId> leadersToDelete = new List<AcDb.ObjectId>();
                List<AcDb.ObjectId> mleadersToDelete = new List<AcDb.ObjectId>();
                List<AcDb.ObjectId> annotationsToDelete = new List<AcDb.ObjectId>();
                List<AcDb.ObjectId> standaloneTextsToDelete = new List<AcDb.ObjectId>();

                foreach (AcDb.ObjectId entId in btr)
                {
                    AcDb.Entity ent = (AcDb.Entity)tr.GetObject(
                        entId, AcDb.OpenMode.ForRead);
                    if (PatEntityHelper.IsPatMLeader(ent, tr))
                    {
                        mleadersToDelete.Add(entId);
                        continue;
                    }

                    AcDb.Leader leader = ent as AcDb.Leader;
                    if (leader == null)
                    {
                        AcDb.MText standaloneText = ent as AcDb.MText;
                        if (standaloneText != null &&
                            PatEntityHelper.IsStandaloneText(standaloneText, tr))
                            standaloneTextsToDelete.Add(entId);
                        continue;
                    }

                    if (!PatEntityHelper.IsPatEntity(leader, tr))
                    {
                        result.Skipped++;
                        continue;
                    }

                    leadersToDelete.Add(entId);
                    AcDb.ObjectId annotationId = PatLeaderTextAttachment.GetAnnotationId(leader, tr);
                    if (!annotationId.IsNull)
                        annotationsToDelete.Add(annotationId);
                }

                foreach (AcDb.ObjectId id in leadersToDelete)
                {
                    try
                    {
                        AcDb.Leader leader = (AcDb.Leader)tr.GetObject(
                            id, AcDb.OpenMode.ForWrite);
                        leader.Erase(true);
                        result.Deleted++;
                    }
                    catch (Exception ex)
                    {
                        PatentMarkerApp.RawLog(
                            "DictPaletteCadService leader delete error: " + ex.Message);
                    }
                }

                foreach (AcDb.ObjectId id in mleadersToDelete)
                {
                    try
                    {
                        AcDb.Entity mleader = (AcDb.Entity)tr.GetObject(
                            id, AcDb.OpenMode.ForWrite);
                        mleader.Erase(true);
                        result.Deleted++;
                    }
                    catch (Exception ex)
                    {
                        PatentMarkerApp.RawLog(
                            "DictPaletteCadService mleader delete error: " + ex.Message);
                    }
                }

                foreach (AcDb.ObjectId id in annotationsToDelete)
                {
                    try
                    {
                        AcDb.MText mtext = (AcDb.MText)tr.GetObject(
                            id, AcDb.OpenMode.ForWrite);
                        mtext.Erase(true);
                    }
                    catch (Exception ex)
                    {
                        PatentMarkerApp.RawLog(
                            "DictPaletteCadService mtext delete error: " + ex.Message);
                    }
                }

                foreach (AcDb.ObjectId id in standaloneTextsToDelete)
                {
                    try
                    {
                        AcDb.MText mtext = (AcDb.MText)tr.GetObject(
                            id, AcDb.OpenMode.ForWrite);
                        mtext.Erase(true);
                        result.Deleted++;
                    }
                    catch (Exception ex)
                    {
                        PatentMarkerApp.RawLog(
                            "DictPaletteCadService standalone mtext delete error: " + ex.Message);
                    }
                }

                tr.Commit();
            }

            return result;
        }
    }
}
