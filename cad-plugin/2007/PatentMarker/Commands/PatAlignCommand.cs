using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.I18n;
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
    /// v2.3：中英双语支持。
    /// </summary>
    public class PatAlignCommand
    {
        [CommandMethod("PATALIGN", CommandFlags.UsePickSet)]
        [CommandMethod("BZA", CommandFlags.UsePickSet)]   // 拼音别名：标注-对齐
        public void Run()
        {
            PatentMarkerApp.RawLog("=== PATALIGN START ===");

            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            var options = new PromptKeywordOptions(Strings.PatAlign_ModePrompt);
            options.Keywords.Add(Strings.PatAlign_KwSelect);
            options.Keywords.Add(Strings.PatAlign_KwFrame);
            options.Keywords.Default = Strings.PatAlign_KwSelect;
            var kwResult = ed.GetKeywords(options);

            if (kwResult.Status != PromptStatus.OK) return;
            PatentMarkerApp.RawLog("Mode: " + kwResult.StringResult);

            if (kwResult.StringResult == Strings.PatAlign_KwSelect)
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
            ed.WriteMessage(Strings.PatAlign_PromptSelect);
            var selection = ed.GetSelection();
            if (selection.Status != PromptStatus.OK || selection.Value.Count == 0)
            {
                ed.WriteMessage(Strings.PatAlign_NoSelection);
                return;
            }

            var refResult = ed.GetPoint(Strings.PatAlign_PromptRefPoint);
            if (refResult.Status != PromptStatus.OK) return;

            var dirOpts = new PromptKeywordOptions(Strings.PatAlign_DirectionPrompt);
            dirOpts.Keywords.Add(Strings.PatAlign_KwHorizontal);
            dirOpts.Keywords.Add(Strings.PatAlign_KwVertical);
            var dirResult = ed.GetKeywords(dirOpts);
            if (dirResult.Status != PromptStatus.OK) return;

            var doc = IO.RuntimeHost.ActiveDocument;
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

                        if (dirResult.StringResult == Strings.PatAlign_KwHorizontal)
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
                ed.WriteMessage(string.Format(Strings.PatAlign_ResultSelect, aligned, skipped, errors));
            }
        }

        /// <summary>
        /// 框边模式：选框两角 → 选边 → 选中引线 → 对齐到框边 + margin
        /// </summary>
        private void AlignToFrame(Editor ed)
        {
            var p1Result = ed.GetPoint(Strings.PatAlign_PromptFrameCorner1);
            if (p1Result.Status != PromptStatus.OK) return;

            var p2Result = ed.GetCorner(Strings.PatAlign_PromptFrameCorner2, p1Result.Value);
            if (p2Result.Status != PromptStatus.OK) return;

            var sideOpts = new PromptKeywordOptions(Strings.PatAlign_SidePrompt);
            sideOpts.Keywords.Add(Strings.PatAlign_KwLeft);
            sideOpts.Keywords.Add(Strings.PatAlign_KwRight);
            sideOpts.Keywords.Add(Strings.PatAlign_KwTop);
            sideOpts.Keywords.Add(Strings.PatAlign_KwBottom);
            var sideResult = ed.GetKeywords(sideOpts);
            if (sideResult.Status != PromptStatus.OK) return;

            double margin = IO.PatSettingsStore.Current.MarginToFrame;

            ed.WriteMessage(Strings.PatAlign_PromptSelect);
            var selection = ed.GetSelection();
            if (selection.Status != PromptStatus.OK || selection.Value.Count == 0)
            {
                ed.WriteMessage(Strings.PatAlign_NoSelection);
                return;
            }

            var doc = IO.RuntimeHost.ActiveDocument;
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

                        if (sideResult.StringResult == Strings.PatAlign_KwLeft)
                            pos = new Point3d(minX - margin, pos.Y, pos.Z);
                        else if (sideResult.StringResult == Strings.PatAlign_KwRight)
                            pos = new Point3d(maxX + margin, pos.Y, pos.Z);
                        else if (sideResult.StringResult == Strings.PatAlign_KwTop)
                            pos = new Point3d(pos.X, maxY + margin, pos.Z);
                        else if (sideResult.StringResult == Strings.PatAlign_KwBottom)
                            pos = new Point3d(pos.X, minY - margin, pos.Z);

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
                ed.WriteMessage(string.Format(Strings.PatAlign_ResultFrame,
                    aligned, sideResult.StringResult, margin.ToString("F1"), skipped, errors));
            }
        }
    }
}
