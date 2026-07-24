using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using Exception = System.Exception;

namespace PatentMarker.Commands
{
    /// <summary>
    /// PATMARK — 引线标注命令 (M2) — AutoCAD 2007 版本。
    ///
    /// 2007 无 MLeader，使用 Leader（继承 Dimension）+ 独立 MText 组合。
    /// v2：样条曲线引线（IsSplined）+ 无限拐点（循环采集）+ 默认无箭头（面板可切换）。
    /// 交互流程：点击附着点 → 循环点击拐点（回车结束）→ 点击文字位置 → 循环。
    /// </summary>
    public class PatMarkCommand
    {
        private string _currentNumber;
        private string _currentName;

        [CommandMethod("PATMARK", CommandFlags.UsePickSet | CommandFlags.Redraw)]
        [CommandMethod("BZM", CommandFlags.UsePickSet | CommandFlags.Redraw)]   // 拼音别名：标注-标记
        public void Run()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            // 懒初始化样式（修复 D3：不在 Initialize 中创建，在首次命令时创建）
            Styles.PatStyleInitializer.EnsurePatDimStyle();

            if (_currentNumber == null)
            {
                if (Palette.PatPaletteCommand.PendingNumber != null)
                {
                    _currentNumber = Palette.PatPaletteCommand.PendingNumber;
                    _currentName = Palette.PatPaletteCommand.PendingName;
                    Palette.PatPaletteCommand.PendingNumber = null;
                    Palette.PatPaletteCommand.PendingName = null;
                }
                else
                {
                    var numResult = ed.GetString("\n输入零件编号: ");
                    if (numResult.Status != PromptStatus.OK) return;
                    _currentNumber = numResult.StringResult;

                    var nameResult = ed.GetString("\n输入零件名称（可选）: ");
                    if (nameResult.Status == PromptStatus.OK)
                        _currentName = nameResult.StringResult;
                }
            }

            if (IsNullOrWhiteSpace(_currentNumber))
            {
                ed.WriteMessage("\nPatentMarker: 未指定零件编号。\n");
                return;
            }

            while (true)
            {
                // 热切换：循环开头检查面板是否有新的待标注编号
                ApplyPendingIfNeeded(ed);

                string namePart = _currentName != null ? _currentName : "";
                string prompt = "\n点击 [" + _currentNumber + " " + namePart + "] 的标注点（Esc 取消）: ";
                var ptResult = ed.GetPoint(prompt);
                if (ptResult.Status != PromptStatus.OK) break;

                // 附着点返回后再次检查（用户可能在 GetPoint 阻塞期间双击了面板）
                ApplyPendingIfNeeded(ed);

                // v2：循环采集拐点（至少1个，回车/空格结束）
                List<Point3d> doglegPts = new List<Point3d>();
                Point3d lastBase = ptResult.Value;
                bool doglegCancelled = false;
                while (true)
                {
                    var doglegOpts = new PromptPointOptions(
                        doglegPts.Count == 0
                            ? "\n点击拐点（回车结束，至少1个）: "
                            : "\n点击下一个拐点（回车结束）: ");
                    doglegOpts.BasePoint = lastBase;
                    doglegOpts.UseBasePoint = true;
                    doglegOpts.AllowNone = true;
                    var doglegResult = ed.GetPoint(doglegOpts);
                    if (doglegResult.Status == PromptStatus.None)
                    {
                        if (doglegPts.Count == 0)
                        {
                            ed.WriteMessage("\n  至少需要1个拐点，请继续点击。\n");
                            continue;
                        }
                        break;  // 正常结束拐点采集
                    }
                    if (doglegResult.Status != PromptStatus.OK)
                    {
                        // Esc：取消本次标注，回到附着点选择
                        doglegCancelled = true;
                        break;
                    }
                    doglegPts.Add(doglegResult.Value);
                    lastBase = doglegResult.Value;
                    // 每次拐点返回后检查（用户可能在选拐点期间双击了面板）
                    ApplyPendingIfNeeded(ed);
                }
                if (doglegCancelled) continue;  // 重新选择附着点

                // 文字位置：回车直接用最后拐点（符合 2007 原始标注习惯），或点击新位置
                var textOpts = new PromptPointOptions("\n点击文字位置（回车=最后拐点）: ");
                textOpts.BasePoint = lastBase;
                textOpts.UseBasePoint = true;
                textOpts.AllowNone = true;
                var textResult = ed.GetPoint(textOpts);
                Point3d textPt;
                if (textResult.Status == PromptStatus.None)
                {
                    textPt = lastBase;  // 回车：使用最后拐点作为文字位置
                }
                else if (textResult.Status != PromptStatus.OK)
                {
                    continue;  // Esc：取消本次，重新选附着点
                }
                else
                {
                    textPt = textResult.Value;
                }

                // 文字位置返回后最后一次检查（确保创建标注用的是最新编号）
                ApplyPendingIfNeeded(ed);

                try
                {
                    CreateLeaderWithText(db, ptResult.Value, doglegPts, textPt, _currentNumber);
                    ed.WriteMessage("\n  已创建引线: " + _currentNumber + "（" + (doglegPts.Count + 1) + " 个顶点）\n");
                }
                catch (Exception ex)
                {
                    ed.WriteMessage("\nPatentMarker 错误: " + ex.GetType().Name + ": " + ex.Message + "\n");
                    PatentMarkerApp.RawLog("CreateLeaderWithText EXCEPTION: " + ex.GetType().FullName + ": " + ex.Message);
                }
            }

            _currentNumber = null;
            _currentName = null;
        }

        /// <summary>
        /// 创建 Leader + MText 组合 — AutoCAD 2007 API。
        ///
        /// v2：样条曲线引线（IsSplined）+ 无限拐点 + 默认无箭头（面板可切换）。
        ///
        /// 顺序（修复 B3）：
        ///  1. 创建 MText → AppendEntity → 获得有效 ObjectId
        ///  2. 创建 Leader → AppendVertex（起点 + 所有拐点）→ AppendEntity → AddNewlyCreatedDBObject
        ///  3. 设置 leader.Annotation = mt.ObjectId（Leader 已在数据库中）
        ///  4. 设置 leader.DimensionStyle = PAT_DIM
        ///  5. 设置 IsSplined = true、HasArrowHead = PatPaletteCommand.HasArrowHead
        ///  6. Commit
        /// </summary>
        private void CreateLeaderWithText(Database db, Point3d attachPt, List<Point3d> doglegPts, Point3d textPt, string number)
        {
            PatentMarkerApp.RawLog("=== CreateLeaderWithText START (number=" + number + ", vertices=" + (doglegPts.Count + 1) + ", arrow=" + Palette.PatPaletteCommand.HasArrowHead + ") ===");

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 1. 创建 MText（先入库获得 ObjectId，修复 B3）
                MText mt = new MText();
                mt.SetDatabaseDefaults(db);
                mt.Contents = number;
                mt.TextHeight = Palette.PatPaletteCommand.TextHeight;
                mt.Location = textPt;

                // 2007 的 MText 没有 TextStyleId 属性，使用默认文字样式

                btr.AppendEntity(mt);
                tr.AddNewlyCreatedDBObject(mt, true);

                // 2. 创建 Leader（v2：样条曲线 + 无限拐点 + 默认无箭头）
                Leader leader = new Leader();
                leader.SetDatabaseDefaults(db);
                leader.AppendVertex(attachPt);              // 起点（箭头端）
                foreach (Point3d p in doglegPts)            // v2：循环追加所有拐点
                    leader.AppendVertex(p);
                leader.IsSplined = Palette.PatPaletteCommand.IsSplined;   // v2.1：样条/直线，取自面板开关
                leader.HasArrowHead = Palette.PatPaletteCommand.HasArrowHead;  // v2：默认 false，面板可切换

                btr.AppendEntity(leader);
                tr.AddNewlyCreatedDBObject(leader, true);

                // 3. 关联 MText（Leader 已在数据库中）
                leader.Annotation = mt.ObjectId;

                // 4. 设置标注样式（同步箭头大小到 PAT_DIM）
                ObjectId dimId = Styles.PatStyleInitializer.GetPatDimStyleId(db, tr);
                if (!dimId.IsNull)
                {
                    leader.DimensionStyle = dimId;
                    // v2.1：同步箭头大小到 DimStyle（影响所有 PAT 引线，专利标注统一规格）
                    DimStyleTableRecord dsr = (DimStyleTableRecord)tr.GetObject(dimId, OpenMode.ForWrite);
                    dsr.Dimasz = Palette.PatPaletteCommand.ArrowSize;
                }

                tr.Commit();
                PatentMarkerApp.RawLog("=== CreateLeaderWithText END (success) ===");
            }
        }

        // .NET 2.0 没有 string.IsNullOrWhiteSpace（修复 B4）
        private static bool IsNullOrWhiteSpace(string s)
        {
            return s == null || s.Trim().Length == 0;
        }

        /// <summary>
        /// 检查面板是否有新的待标注编号，有则立即覆盖当前编号。
        /// 在每个 GetPoint 返回后调用，确保用户随时双击面板都能立即切换。
        /// </summary>
        private void ApplyPendingIfNeeded(Editor ed)
        {
            if (Palette.PatPaletteCommand.PendingNumber == null) return;
            _currentNumber = Palette.PatPaletteCommand.PendingNumber;
            _currentName = Palette.PatPaletteCommand.PendingName;
            Palette.PatPaletteCommand.PendingNumber = null;
            Palette.PatPaletteCommand.PendingName = null;
            ed.WriteMessage("\n  >> 已切换为: " + _currentNumber + "\n");
        }
    }
}
