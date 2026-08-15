// ============================================================================
// 复线（mleader 分支）版本本地文件 — 仅 MLeader 复线版本编译
//（2010/2013/2015/2025；2007 无 MLeader API，用 Shared 版本）。
// PATCHECK v2（简化版）：只报告"字典有 · 图纸未标注"清单（漏标检测）。
// 旧版的三类检测中，"图纸有·字典无"在纯插件流程下不可能出现（编号只能
// 来自字典面板），"同号重复"是合法用法（同一部件多处标同号），均已删除。
// 结果同时写入 PatCheckResult 供面板高亮未标注条目。
// ============================================================================
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.I18n;
using System;
using System.Collections.Generic;

namespace PatentMarker.Commands
{
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
                ed.WriteMessage(Strings.PatCheck_NoDict);
                return;
            }

            // 图纸上已标注的编号集合（归一化）
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

                    // 1) 新 MLeader 标注（F 方案）
                    MLeader mleader = ent as MLeader;
                    if (mleader != null)
                    {
                        if (!PatMLeaderCreator.IsPatMLeader(mleader, tr)) continue;
                        patCount++;
                        string number = IO.PatEntityHelper.GetTextNumber(mleader.MText);
                        AddMarked(marked, number);
                        continue;
                    }

                    // 2) 纯文字模式（无引线独立 MText）
                    MText standalone = ent as MText;
                    if (standalone != null)
                    {
                        if (!IO.PatEntityHelper.IsStandaloneText(standalone, tr)) continue;
                        patCount++;
                        AddMarked(marked, IO.PatEntityHelper.GetTextNumber(standalone));
                        continue;
                    }

                    // 3) 旧图纸 Leader 标注（兼容历史 DWG）
                    Leader leader = ent as Leader;
                    if (leader != null && IO.PatEntityHelper.IsPatEntity(leader, tr))
                    {
                        patCount++;
                        AddMarked(marked, IO.PatEntityHelper.GetLeaderNumber(leader, tr));
                    }
                }
                tr.Commit();
            }

            // 漏标清单：保持字典顺序
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

            PatCheckResult.SetUnmarked(unmarked);

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
