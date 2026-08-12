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
    /// transactions, and PAT Leader/MText entity traversal.
    /// </summary>
    public sealed class DictPaletteDeleteResult
    {
        public int Deleted;
        public int Skipped;
    }

    public static class DictPaletteCadService
    {
        /// <summary>
        /// Renames matching PAT annotation text in model space and commits
        /// the transaction.  The caller decides how to display the result.
        /// </summary>
        public static int RenameNumber(Document doc, string oldNumber, string newNumber)
        {
            if (doc == null) return 0;

            AcDb.Database db = doc.Database;
            int changed;
            using (DocumentLock docLock = doc.LockDocument())
            using (AcDb.Transaction tr = db.TransactionManager.StartTransaction())
            {
                AcDb.BlockTable bt = (AcDb.BlockTable)tr.GetObject(
                    db.BlockTableId, AcDb.OpenMode.ForRead);
                AcDb.BlockTableRecord btr = (AcDb.BlockTableRecord)tr.GetObject(
                    bt[AcDb.BlockTableRecord.ModelSpace], AcDb.OpenMode.ForRead);
                changed = PatEntityHelper.RenameNumberInModelSpace(
                    tr, btr, oldNumber, newNumber);
                tr.Commit();
            }

            return changed;
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
                List<AcDb.ObjectId> annotationsToDelete = new List<AcDb.ObjectId>();
                List<AcDb.ObjectId> standaloneTextsToDelete = new List<AcDb.ObjectId>();

                foreach (AcDb.ObjectId entId in btr)
                {
                    AcDb.Entity ent = (AcDb.Entity)tr.GetObject(
                        entId, AcDb.OpenMode.ForRead);
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
