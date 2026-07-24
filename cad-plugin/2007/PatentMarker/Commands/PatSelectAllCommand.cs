using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System;
using Exception = System.Exception;

namespace PatentMarker.Commands
{
    /// <summary>
    /// PATSELECTALL / BZS — 选中所有 PAT 引线及其关联文字。
    /// v2.2：用户可用 Ctrl+1 统一修改字高、样式等属性。
    /// </summary>
    public class PatSelectAllCommand
    {
        [CommandMethod("PATSELECTALL", CommandFlags.Modal)]
        [CommandMethod("BZS", CommandFlags.Modal)]   // 拼音别名：标注-选中
        public void Run()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
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
                        Leader leader = ent as Leader;
                        if (leader == null) continue;

                        if (!IO.PatEntityHelper.IsPatEntity(leader, tr)) continue;

                        // 选中 Leader 本身
                        ids.Add(id);

                        // 同时选中关联的 MText（用户改字高/样式时一并应用）
                        if (!leader.Annotation.IsNull)
                            ids.Add(leader.Annotation);
                    }
                    tr.Commit();
                }

                if (ids.Count == 0)
                {
                    ed.WriteMessage("\nPatentMarker: 未找到 PAT 引线。\n");
                    return;
                }

                // 设置当前选择集（SetImpliedSelection 接收 ObjectId[]）
                ObjectId[] idArray = new ObjectId[ids.Count];
                ids.CopyTo(idArray, 0);
                ed.SetImpliedSelection(idArray);
                ed.WriteMessage("\nPatentMarker: 已选中 " + ids.Count + " 个 PAT 实体（Leader + MText）。按 Ctrl+1 修改属性。\n");
            }
            catch (Exception ex)
            {
                ed.WriteMessage("\nPatentMarker 错误: " + ex.GetType().Name + ": " + ex.Message + "\n");
                PatentMarkerApp.RawLog("PatSelectAll EXCEPTION: " + ex.GetType().FullName + ": " + ex.Message);
            }
        }
    }
}
