// ============================================================================
// 复线（mleader 分支）版本本地替换文件 — 仅 2025/2026 版编译。
// 来源：cad-plugin/Shared/Commands/PatSelectAllCommand.cs。
// 差异：新增 PAT MLeader 识别（PatMLeaderCreator 标记），保留旧图纸
//       Leader 标注与独立文字的识别能力。
// ============================================================================
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

                        MLeader mleader = entity as MLeader;
                        if (mleader != null)
                        {
                            if (PatMLeaderCreator.IsPatMLeader(mleader, tr))
                                ids.Add(id);
                            continue;
                        }

                        Leader leader = entity as Leader;
                        if (leader == null)
                        {
                            MText standaloneText = entity as MText;
                            if (standaloneText != null && IO.PatEntityHelper.IsStandaloneText(standaloneText, tr))
                                ids.Add(id);
                            continue;
                        }
                        if (!IO.PatEntityHelper.IsPatEntity(leader, tr)) continue;

                        ids.Add(id);
                        ObjectId annotationId = PatLeaderTextAttachment.GetAnnotationId(leader, tr);
                        if (!annotationId.IsNull)
                            ids.Add(annotationId);
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
