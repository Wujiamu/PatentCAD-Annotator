using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.I18n;
using System;
using System.Collections.Generic;

namespace PatentMarker.Commands
{
    /// <summary>
    /// PATCHECK v2（简化版，2007 Leader+MText 扫描）：只报告
    /// "字典有 · 图纸未标注"清单。旧版三类检测中，"图纸有·字典无"在
    /// 纯插件流程下不可能出现（编号只能来自字典面板），"同号重复"是
    /// 合法用法（同一部件多处标同号），均已删除。结果写入 PatCheckResult
    /// 供面板高亮未标注条目（与 MLeader 版行为一致）。
    /// </summary>
    public class PatCheckCommand
    {
        [CommandMethod("PATCHECK", CommandFlags.Modal)]
        [CommandMethod("BZC", CommandFlags.Modal)]
        public void Run()
        {
            PatentMarkerApp.RawLog("=== PATCHECK START (v2 unmarked-only) ===");
            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            IO.DictModel dict = IO.DictLoader.LoadForCurrentDrawing();
            if (dict == null)
            {
                PatCheckResult.Clear(doc);
                ed.WriteMessage(Strings.PatCheck_NoDict);
                return;
            }

            var marked = new Dictionary<string, bool>(IO.NumberIdentity.Comparer);
            int patCount = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(
                    db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId entId in btr)
                {
                    Entity ent = (Entity)tr.GetObject(entId, OpenMode.ForRead);
                    Leader leader = ent as Leader;
                    if (leader == null)
                    {
                        MText standaloneText = ent as MText;
                        if (standaloneText == null ||
                            !IO.PatEntityHelper.IsStandaloneText(standaloneText, tr))
                            continue;
                        patCount++;
                        AddMarked(marked, IO.PatEntityHelper.GetTextNumber(standaloneText));
                        continue;
                    }
                    if (!IO.PatEntityHelper.IsPatEntity(leader, tr)) continue;
                    patCount++;
                    AddMarked(marked, IO.PatEntityHelper.GetLeaderNumber(leader, tr));
                }
                tr.Commit();
            }

            var unmarked = new List<string>();
            var unmarkedNames = new List<string>();
            foreach (IO.DictEntry entry in dict.Entries)
            {
                string normalized = IO.NumberIdentity.Normalize(entry.Number);
                if (!marked.ContainsKey(normalized))
                {
                    unmarked.Add(normalized);
                    unmarkedNames.Add(entry.Name);
                }
            }

            PatCheckResult.SetUnmarked(doc, unmarked);

            ed.WriteMessage(Strings.PatCheck_ReportTitle);
            ed.WriteMessage(string.Format(Strings.PatCheck_Summary,
                dict.Entries.Count, patCount));

            if (unmarked.Count == 0)
            {
                ed.WriteMessage(Strings.PatCheck_AllMarked);
                PatentMarkerApp.RawLog("PATCHECK: all entries marked");
            }
            else
            {
                ed.WriteMessage(string.Format(
                    Strings.PatCheck_SectionUnmarked, unmarked.Count));
                for (int i = 0; i < unmarked.Count; i++)
                    ed.WriteMessage("  #" + unmarked[i] + " " + unmarkedNames[i] + "\n");
                ed.WriteMessage(Strings.PatCheck_PaletteHint);
                PatentMarkerApp.RawLog("PATCHECK: " + unmarked.Count + " unmarked");
            }
            ed.WriteMessage("==========================================\n");
            PatentMarkerApp.RawLog("=== PATCHECK END ===");
        }

        private static void AddMarked(Dictionary<string, bool> marked, string number)
        {
            if (number == null) return;
            number = number.Trim();
            if (number.Length == 0) return;
            marked[IO.NumberIdentity.Normalize(number)] = true;
        }
    }
}
