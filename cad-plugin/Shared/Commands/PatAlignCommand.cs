// ============================================================================
// PATALIGN v2（Shared 版，2007 Leader+MText 引擎）：
// 选择集先行 → 线/框两种基准模式 → 溢出时默认延伸。
// 与 MLeader 组版本（2010/2013/2015/2025 本地 Commands）逻辑一致，仅
// 标注实体类型不同：本版只处理旧 Leader 标注与独立文字（无 MLeader API）。
//
// v2 与 v1 的差异：
//   1. 选集先行（一 DWG 多附图时各附图可分别对齐，互不影响）。
//   2. 线模式：文字投影到 P1→P2 基准线；框模式：文字推到框边外侧 margin。
//   3. 溢出规则（空间不足时的默认延伸）：
//      - 线模式：沿 P1→P2 方向紧凑排列，排到线端后继续沿线延伸；
//      - 框模式：第一列沿框边排满后，剩余文字放第二列（沿远离框方向
//        再退一列宽 + 列间距），不按各边延伸散开（避免交叉重叠）。
//      排列顺序 = 投影顺序，绝不按编号大小/层级重排。
//   4. Fall back：文字占位测量失败时退化为纯投影（v1 行为）并提示。
// ============================================================================
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.I18n;
using System;
using System.Collections.Generic;
using Exception = System.Exception;

namespace PatentMarker.Commands
{
    public class PatAlignCommand
    {
        private const double GapPerTextHeight = 1.5;   // 溢出重排：文字最小间距 = 1.5 × 字高
        private const double ColGapPerTextHeight = 2.0; // 框模式第二列的列间距 = 2 × 字高

        [CommandMethod("PATALIGN", CommandFlags.UsePickSet)]
        [CommandMethod("BZA", CommandFlags.UsePickSet)]
        public void Run()
        {
            PatentMarkerApp.RawLog("=== PATALIGN START (v2) ===");
            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            // ① 选择集先行：pickfirst 预选集优先（UsePickSet），否则提示选择
            SelectionSet ss;
            var implied = ed.SelectImplied();
            if (implied.Status == PromptStatus.OK && implied.Value.Count > 0)
            {
                ss = implied.Value;
            }
            else
            {
                ed.WriteMessage(Strings.PatAlign_PromptSelect);
                var prompted = ed.GetSelection();
                if (prompted.Status != PromptStatus.OK || prompted.Value.Count == 0)
                {
                    ed.WriteMessage(Strings.PatAlign_NoSelection);
                    return;
                }
                ss = prompted.Value;
            }

            // ② 基准模式：线 / 框
            var mode = new PromptKeywordOptions(Strings.PatAlign_ModePrompt2);
            mode.Keywords.Add(Strings.PatAlign_KwLine);
            mode.Keywords.Add(Strings.PatAlign_KwFrame);
            mode.Keywords.Default = Strings.PatAlign_KwLine;
            var modeResult = ed.GetKeywords(mode);
            if (modeResult.Status != PromptStatus.OK) return;

            if (modeResult.StringResult == Strings.PatAlign_KwLine)
                AlignToLine(ed, ss);
            else
                AlignToFrame(ed, ss);

            PatentMarkerApp.RawLog("=== PATALIGN END ===");
        }

        // =====================================================================
        // 线模式：文字投影到 P1→P2 基准线；线长不足时沿 P1→P2 延伸紧凑排列
        // =====================================================================
        private void AlignToLine(Editor ed, SelectionSet ss)
        {
            var p1Result = ed.GetPoint(Strings.PatAlign_PromptLineP1);
            if (p1Result.Status != PromptStatus.OK) return;
            var p2Options = new PromptPointOptions(Strings.PatAlign_PromptLineP2);
            p2Options.BasePoint = p1Result.Value;
            p2Options.UseBasePoint = true;
            var p2Result = ed.GetPoint(p2Options);
            if (p2Result.Status != PromptStatus.OK) return;

            Point3d p1 = p1Result.Value;
            Point3d p2 = p2Result.Value;
            Vector3d dir = p2 - p1;
            if (dir.Length < 1e-9)
            {
                ed.WriteMessage(Strings.PatAlign_Degenerate);
                return;
            }
            double lineLen = dir.Length;   // 走单位向量前先留线长，溢出判定依赖
            dir = dir.GetNormal();

            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var items = CollectTargets(tr, ss, ed);
                if (items.Count == 0) return;

                // 投影参数 t（沿 P1→P2 的有向距离）与占位宽度
                bool measureOk = true;
                double gap = 0.0;
                foreach (Target t in items)
                {
                    t.Projection = (t.TextLocation - p1).DotProduct(dir);
                    if (!MeasureWidth(tr, t)) measureOk = false;
                    if (t.TextHeight > gap) gap = t.TextHeight;
                }
                gap *= GapPerTextHeight;
                items.Sort(CompareByProjection);

                // 需要的总长 vs 线长
                double need = 0.0;
                if (measureOk)
                {
                    foreach (Target t in items) need += t.Width;
                    need += gap * (items.Count - 1);
                }
                bool compact = measureOk && need > lineLen + 1e-9;

                int aligned = 0;
                double cursor = 0.0;
                foreach (Target t in items)
                {
                    Point3d newPos;
                    if (compact)
                    {
                        // 紧凑排列：从 P1 沿方向依次排，超出 P2 继续延伸
                        double center = cursor + t.Width / 2.0;
                        newPos = p1 + dir * center;
                        cursor += t.Width + gap;
                    }
                    else
                    {
                        // 空间足够：各落垂足（保持原间距）
                        newPos = p1 + dir * t.Projection;
                    }
                    if (MoveTarget(tr, t, newPos)) aligned++;
                }

                tr.Commit();
                Report(ed, aligned, items.Count - aligned, compact, 0);
            }
        }

        // =====================================================================
        // 框模式：文字推到框边外侧 margin；边长不足时排第二列（远离框方向）
        // =====================================================================
        private void AlignToFrame(Editor ed, SelectionSet ss)
        {
            var c1 = ed.GetPoint(Strings.PatAlign_PromptFrameCorner1);
            if (c1.Status != PromptStatus.OK) return;
            var c2 = ed.GetCorner(Strings.PatAlign_PromptFrameCorner2, c1.Value);
            if (c2.Status != PromptStatus.OK) return;

            var side = new PromptKeywordOptions(Strings.PatAlign_SidePrompt);
            side.Keywords.Add(Strings.PatAlign_KwLeft);
            side.Keywords.Add(Strings.PatAlign_KwRight);
            side.Keywords.Add(Strings.PatAlign_KwTop);
            side.Keywords.Add(Strings.PatAlign_KwBottom);
            side.Keywords.Default = Strings.PatAlign_KwLeft;
            var sideResult = ed.GetKeywords(side);
            if (sideResult.Status != PromptStatus.OK) return;
            string sideName = sideResult.StringResult;

            double margin = IO.PatSettingsStore.Current.MarginToFrame;

            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            double minX = Math.Min(c1.Value.X, c2.Value.X);
            double maxX = Math.Max(c1.Value.X, c2.Value.X);
            double minY = Math.Min(c1.Value.Y, c2.Value.Y);
            double maxY = Math.Max(c1.Value.Y, c2.Value.Y);
            bool verticalSide = sideName == Strings.PatAlign_KwLeft ||
                sideName == Strings.PatAlign_KwRight;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var items = CollectTargets(tr, ss, ed);
                if (items.Count == 0) return;

                bool measureOk = true;
                double textHeight = 0.0;
                foreach (Target t in items)
                {
                    // 投影参数：左右边按 Y（越大越靠前，自上而下），上下边按 X
                    t.Projection = verticalSide ? t.TextLocation.Y : t.TextLocation.X;
                    if (!MeasureWidth(tr, t)) measureOk = false;
                    if (t.TextHeight > textHeight) textHeight = t.TextHeight;
                }
                double pitch = textHeight * GapPerTextHeight;   // 行距/列距
                double colGap = textHeight * ColGapPerTextHeight;
                items.Sort(CompareByProjection);
                // 左右边：Y 大的先排（自上而下）；上下边：X 小的先排（自左向右）
                if (verticalSide) items.Reverse();

                double span = verticalSide ? (maxY - minY) : (maxX - minX);
                bool compact = measureOk && items.Count * pitch > span + 1e-9;

                int aligned = 0;
                int overflowCols = 0;
                if (!compact)
                {
                    // 空间足够：投影（保留原位置），仅移动垂直于边的轴
                    foreach (Target t in items)
                        if (MoveTarget(tr, t, FramePos(t, sideName,
                            minX, maxX, minY, maxY, margin, verticalSide)))
                            aligned++;
                }
                else
                {
                    // 溢出：第一列沿边排满 → 第二列远离框 → ……
                    double cursorBase = verticalSide ? maxY : minX; // 起点：上端 / 左端
                    int index = 0;
                    int perColumn = Math.Max(1, (int)Math.Floor(span / pitch));
                    int column = 0;
                    while (index < items.Count)
                    {
                        // 该列文字的最大占位宽（决定下一列的退距）
                        double maxW = 0.0;
                        int count = Math.Min(perColumn, items.Count - index);
                        for (int i = index; i < index + count; i++)
                            if (items[i].Width > maxW) maxW = items[i].Width;

                        for (int i = 0; i < count; i++)
                        {
                            Target t = items[index + i];
                            double along = cursorBase +
                                (verticalSide ? -(i + 0.5) * pitch : (i + 0.5) * pitch);
                            Point3d pos;
                            if (verticalSide)
                            {
                                double x = sideName == Strings.PatAlign_KwLeft
                                    ? minX - margin - column * (maxW + colGap)
                                    : maxX + margin + column * (maxW + colGap);
                                pos = new Point3d(x, along, 0);
                            }
                            else
                            {
                                double y = sideName == Strings.PatAlign_KwTop
                                    ? maxY + margin + column * (pitch + colGap)
                                    : minY - margin - column * (pitch + colGap);
                                pos = new Point3d(along, y, 0);
                            }
                            if (MoveTarget(tr, t, pos)) aligned++;
                        }
                        index += count;
                        column++;
                        if (column > overflowCols) overflowCols = column;
                        if (column > 50) break; // 安全阀：异常数据防死循环
                    }
                }

                tr.Commit();
                Report(ed, aligned, items.Count - aligned,
                    compact, Math.Max(0, overflowCols - 1));
            }
        }

        private static Point3d FramePos(Target t, string sideName,
            double minX, double maxX, double minY, double maxY,
            double margin, bool verticalSide)
        {
            Point3d p = t.TextLocation;
            if (sideName == Strings.PatAlign_KwLeft)
                return new Point3d(minX - margin, p.Y, p.Z);
            if (sideName == Strings.PatAlign_KwRight)
                return new Point3d(maxX + margin, p.Y, p.Z);
            if (sideName == Strings.PatAlign_KwTop)
                return new Point3d(p.X, maxY + margin, p.Z);
            return new Point3d(p.X, minY - margin, p.Z);
        }

        // =====================================================================
        // 目标收集与移动（2007：独立文字 + 旧 Leader 标注）
        // =====================================================================

        private class Target
        {
            public ObjectId EntityId;
            public Point3d TextLocation;
            public double TextHeight = 3.5;
            public double Width;        // 文字占位宽（沿排列方向）
            public double Projection;   // 沿基准方向的投影参数
            public bool IsStandalone;   // 纯文字
        }

        /// <summary>过滤选集：只保留 PAT 标注（独立文字 / 旧 Leader）。</summary>
        private static List<Target> CollectTargets(Transaction tr, SelectionSet ss,
            Editor ed)
        {
            var items = new List<Target>();
            int skipped = 0;
            foreach (SelectedObject so in ss)
            {
                try
                {
                    Entity ent = (Entity)tr.GetObject(so.ObjectId, OpenMode.ForRead);
                    Target t = null;

                    MText standalone = ent as MText;
                    if (standalone != null &&
                        IO.PatEntityHelper.IsStandaloneText(standalone, tr))
                    {
                        t = new Target();
                        t.EntityId = standalone.ObjectId;
                        t.TextLocation = standalone.Location;
                        t.TextHeight = standalone.TextHeight;
                        t.IsStandalone = true;
                    }

                    if (t == null)
                    {
                        Leader leader = ent as Leader;
                        if (leader != null && IO.PatEntityHelper.IsPatEntity(leader, tr))
                        {
                            t = new Target();
                            t.EntityId = leader.ObjectId;
                            t.TextLocation = IO.PatEntityHelper.GetLeaderTextPos(leader, tr);
                            t.TextHeight = 3.5;
                        }
                    }

                    if (t != null) items.Add(t); else skipped++;
                }
                catch (Exception ex)
                {
                    skipped++;
                    PatentMarkerApp.RawLog("PATALIGN collect error: " + ex.Message);
                }
            }
            if (skipped > 0)
                ed.WriteMessage(string.Format(Strings.PatAlign_Skipped, skipped));
            return items;
        }

        /// <summary>文字占位测量：优先 ActualWidth，失败按字符数估算。</summary>
        private static bool MeasureWidth(Transaction tr, Target t)
        {
            try
            {
                if (t.IsStandalone)
                {
                    MText mt = (MText)tr.GetObject(t.EntityId, OpenMode.ForRead);
                    if (mt.ActualWidth > 0)
                    {
                        t.Width = mt.ActualWidth;
                        return true;
                    }
                    t.Width = (mt.Contents ?? "").Length * 0.7 * t.TextHeight;
                    return true;
                }
                // 旧 Leader：无内嵌 MText 宽度可读，按当前编号文本估宽
                t.Width = t.TextHeight * 2.0;
                return true;
            }
            catch
            {
                t.Width = t.TextHeight * 2.0;
                return false;
            }
        }

        private static int CompareByProjection(Target a, Target b)
        {
            return a.Projection.CompareTo(b.Projection);
        }

        /// <summary>移动单个标注的文字到新位置（Leader 路径沿用 v1：
        /// 移动关联 MText + 重写文字端点）。</summary>
        private static bool MoveTarget(Transaction tr, Target t, Point3d newPos)
        {
            try
            {
                if (t.IsStandalone)
                {
                    MText mt = (MText)tr.GetObject(t.EntityId, OpenMode.ForWrite);
                    mt.Location = newPos;
                    return true;
                }
                Leader leader = (Leader)tr.GetObject(t.EntityId, OpenMode.ForWrite);
                ObjectId annId = PatLeaderTextAttachment.GetAnnotationId(leader, tr);
                if (annId.IsNull) return false;
                MText text = (MText)tr.GetObject(annId, OpenMode.ForWrite);
                text.Location = newPos;
                if (leader.Annotation.IsNull)
                    PatLeaderTextAttachment.SetTextEndpoint(leader, newPos, text.TextHeight);
                return true;
            }
            catch (Exception ex)
            {
                PatentMarkerApp.RawLog("PATALIGN move error: " + ex.Message);
                return false;
            }
        }

        private static void Report(Editor ed, int aligned, int failed,
            bool overflowed, int extraColumns)
        {
            if (overflowed)
                ed.WriteMessage(string.Format(
                    Strings.PatAlign_ResultOverflow, aligned, failed, extraColumns));
            else
                ed.WriteMessage(string.Format(
                    Strings.PatAlign_Result, aligned, failed));
        }
    }
}
