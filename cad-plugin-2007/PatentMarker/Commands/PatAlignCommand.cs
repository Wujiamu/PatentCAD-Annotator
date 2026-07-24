using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using Exception = System.Exception;

namespace PatentMarker.Commands
{
    /// <summary>
    /// PATALIGN — PAT_DIM 引线对齐 — AutoCAD 2007 版本。
    ///
    /// 两种模式：
    ///   选择模式：选中引线 → 选参考点 → 水平/垂直对齐文字
    ///   框边模式：选框边 → 选中引线 → 对齐到框边 + margin
    ///
    /// 2007 无 MLEADERALIGN 命令，全部手动实现对齐。
    /// </summary>
    public class PatAlignCommand
    {
        [CommandMethod("PATALIGN", CommandFlags.UsePickSet)]
        [CommandMethod("BZA", CommandFlags.UsePickSet)]   // 拼音别名：标注-对齐
        public void Run()
        {
            PatentMarkerApp.RawLog("=== PATALIGN START ===");

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            var options = new PromptKeywordOptions("\n对齐模式:");
            options.Keywords.Add("选择");
            options.Keywords.Add("框边");
            options.Keywords.Default = "选择";
            var kwResult = ed.GetKeywords(options);

            if (kwResult.Status != PromptStatus.OK) return;
            PatentMarkerApp.RawLog("Mode: " + kwResult.StringResult);

            if (kwResult.StringResult == "选择")
                AlignSelected(ed);
            else
                AlignToFrame(ed);

            PatentMarkerApp.RawLog("=== PATALIGN END ===");
        }

        /// <summary>
        /// 选择模式：选中引线 → 选参考点 → 水平/垂直对齐文字
        /// </summary>
        private void AlignSelected(Editor ed)
        {
            ed.WriteMessage("\n选择要对齐的 PAT_DIM 引线: ");
            var selection = ed.GetSelection();
            if (selection.Status != PromptStatus.OK || selection.Value.Count == 0)
            {
                ed.WriteMessage("\n未选择对象。\n");
                return;
            }

            var refResult = ed.GetPoint("\n选择对齐参考点: ");
            if (refResult.Status != PromptStatus.OK) return;

            var dirOpts = new PromptKeywordOptions("\n对齐方向?");
            dirOpts.Keywords.Add("水平");
            dirOpts.Keywords.Add("垂直");
            var dirResult = ed.GetKeywords(dirOpts);
            if (dirResult.Status != PromptStatus.OK) return;

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            double refX = refResult.Value.X;
            double refY = refResult.Value.Y;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int aligned = 0, skipped = 0, errors = 0;

                foreach (SelectedObject selObj in selection.Value)
                {
                    try
                    {
                        var ent = (Entity)tr.GetObject(selObj.ObjectId, OpenMode.ForRead);

                        // 统一使用 PatEntityHelper 识别（修复 S3）
                        if (!IO.PatEntityHelper.IsPatEntity(ent, tr)) { skipped++; continue; }

                        var leader = (Leader)ent;
                        if (leader.Annotation.IsNull) { skipped++; continue; }

                        var mt = (MText)tr.GetObject(leader.Annotation, OpenMode.ForWrite);
                        Point3d pos = mt.Location;

                        if (dirResult.StringResult == "水平")
                            pos = new Point3d(pos.X, refY, pos.Z);
                        else
                            pos = new Point3d(refX, pos.Y, pos.Z);

                        mt.Location = pos;
                        aligned++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        PatentMarkerApp.RawLog("PATALIGN error: " + ex.Message);
                    }
                }

                tr.Commit();
                ed.WriteMessage("\n对齐 " + aligned + " 条引线（跳过 " + skipped + "，错误 " + errors + "）。\n");
            }
        }

        /// <summary>
        /// 框边模式：选框两角 → 选边 → 选中引线 → 对齐到框边 + margin
        /// </summary>
        private void AlignToFrame(Editor ed)
        {
            var p1Result = ed.GetPoint("\n参考框第一角: ");
            if (p1Result.Status != PromptStatus.OK) return;

            var p2Result = ed.GetCorner("\n对角: ", p1Result.Value);
            if (p2Result.Status != PromptStatus.OK) return;

            var sideOpts = new PromptKeywordOptions("\n对齐到哪边?");
            sideOpts.Keywords.Add("左");
            sideOpts.Keywords.Add("右");
            sideOpts.Keywords.Add("上");
            sideOpts.Keywords.Add("下");
            var sideResult = ed.GetKeywords(sideOpts);
            if (sideResult.Status != PromptStatus.OK) return;

            double margin = 5.0;
            if (IO.ConfigLoader.Current != null && IO.ConfigLoader.Current.Align != null)
                margin = IO.ConfigLoader.Current.Align.MarginToFrame;

            ed.WriteMessage("\n选择要对齐的 PAT_DIM 引线: ");
            var selection = ed.GetSelection();
            if (selection.Status != PromptStatus.OK || selection.Value.Count == 0)
            {
                ed.WriteMessage("\n未选择对象。\n");
                return;
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            double minX = Math.Min(p1Result.Value.X, p2Result.Value.X);
            double maxX = Math.Max(p1Result.Value.X, p2Result.Value.X);
            double minY = Math.Min(p1Result.Value.Y, p2Result.Value.Y);
            double maxY = Math.Max(p1Result.Value.Y, p2Result.Value.Y);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int aligned = 0, skipped = 0, errors = 0;

                foreach (SelectedObject selObj in selection.Value)
                {
                    try
                    {
                        var ent = (Entity)tr.GetObject(selObj.ObjectId, OpenMode.ForRead);

                        if (!IO.PatEntityHelper.IsPatEntity(ent, tr)) { skipped++; continue; }

                        var leader = (Leader)ent;
                        if (leader.Annotation.IsNull) { skipped++; continue; }

                        var mt = (MText)tr.GetObject(leader.Annotation, OpenMode.ForWrite);
                        Point3d pos = mt.Location;

                        switch (sideResult.StringResult)
                        {
                            case "左":
                                pos = new Point3d(minX - margin, pos.Y, pos.Z);
                                break;
                            case "右":
                                pos = new Point3d(maxX + margin, pos.Y, pos.Z);
                                break;
                            case "上":
                                pos = new Point3d(pos.X, maxY + margin, pos.Z);
                                break;
                            case "下":
                                pos = new Point3d(pos.X, minY - margin, pos.Z);
                                break;
                        }

                        mt.Location = pos;
                        aligned++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        PatentMarkerApp.RawLog("PATALIGN error: " + ex.Message);
                    }
                }

                tr.Commit();
                ed.WriteMessage("\n对齐 " + aligned + " 条引线到" + sideResult.StringResult +
                    "边（margin=" + margin.ToString("F1") + "，跳过 " + skipped + "，错误 " + errors + "）。\n");
            }
        }
    }
}
