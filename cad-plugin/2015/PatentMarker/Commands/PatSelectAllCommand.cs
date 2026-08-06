using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.I18n;
using System;
using Exception = System.Exception;

namespace PatentMarker.Commands
{
    public class PatSelectAllCommand
    {
        [CommandMethod("PATSELECTALL", CommandFlags.Modal)]
        [CommandMethod("BZS", CommandFlags.Modal)]
        public void Run()
        {
            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                ObjectIdCollection ids = new ObjectIdCollection();
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(
                        db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in btr)
                    {
                        Entity entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        Leader leader = entity as Leader;
                        if (leader == null ||
                            !IO.PatEntityHelper.IsPatEntity(leader, tr))
                            continue;

                        ids.Add(id);
                        if (!leader.Annotation.IsNull)
                            ids.Add(leader.Annotation);
                    }
                    tr.Commit();
}
                if (ids.Count == 0)
                {
                    ed.WriteMessage(Strings.PatSelectAll_None);
                    return;
                }

                ObjectId[] selected = new ObjectId[ids.Count];
                ids.CopyTo(selected, 0);
                ed.SetImpliedSelection(selected);
                ed.WriteMessage(string.Format(
                    Strings.PatSelectAll_Result, ids.Count));
            }
            catch (Exception ex)
            {
                ed.WriteMessage(Strings.ErrorPrefix + ex.GetType().Name +
                    ": " + ex.Message + "\n");
                PatentMarkerApp.RawLog(
                    "PatSelectAll EXCEPTION: " + ex.GetType().FullName +
                    ": " + ex.Message);
            }
        }
    }
}
