using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.I18n;
using System;
using AppAcad = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace PatentMarker.Commands
{
    /// <summary>
    /// PATSELECTALL / BZS — 选中所有 PAT 多重引线。
    /// 用户可用 Ctrl+1 统一修改属性。
    /// </summary>
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
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in btr)
                    {
                        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        MLeader mleader = ent as MLeader;
                        if (mleader == null) continue;

                        if (!IO.PatEntityHelper.IsPatEntity(mleader, tr)) continue;

                        ids.Add(id);
                    }
                    tr.Commit();
                }

                if (ids.Count == 0)
                {
                    ed.WriteMessage(Strings.PatSelectAll_None);
                    return;
                }

                ObjectId[] idArray = new ObjectId[ids.Count];
                ids.CopyTo(idArray, 0);
                ed.SetImpliedSelection(idArray);
                ed.WriteMessage(string.Format(Strings.PatSelectAll_Result, ids.Count));
            }
            catch (Exception ex)
            {
                ed.WriteMessage(Strings.ErrorPrefix + ex.GetType().Name + ": " + ex.Message + "\n");
                PatentMarkerApp.RawLog("PatSelectAll EXCEPTION: " + ex.GetType().FullName + ": " + ex.Message);
            }
        }
    }
}
