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
    /// PATMARK — 引线标注命令 — AutoCAD 2013/2014 版本。
    ///
    /// 2013 引入 MLeader（一体式：引线 + 文字 + 样式），无需 Leader + MText 拼合。
    /// v2：样条曲线引线 + 无限拐点 + 默认无箭头（面板可切换）。
    /// 交互流程：点击附着点 → 循环点击拐点（回车结束）→ 点击文字位置 → 循环。
    /// </summary>
    public class PatMarkCommand
    {
        private string _currentNumber;
        private string _currentName;

        [CommandMethod("PATMARK", CommandFlags.UsePickSet | CommandFlags.Redraw)]
        [CommandMethod("BZM", CommandFlags.UsePickSet | CommandFlags.Redraw)]
        public void Run()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            // 懒初始化样式
            Styles.PatStyleInitializer.EnsurePatStyle();

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
                    var numResult = ed.GetString(Strings.PatMark_EnterNumber);
                    if (numResult.Status != PromptStatus.OK) return;
                    _currentNumber = numResult.StringResult;

                    var nameResult = ed.GetString(Strings.PatMark_EnterName);
                    if (nameResult.Status == PromptStatus.OK)
                        _currentName = nameResult.StringResult;
                }
            }

            if (string.IsNullOrWhiteSpace(_currentNumber))
            {
                ed.WriteMessage(Strings.PatMark_NoNumber);
                return;
            }

            while (true)
            {
                ApplyPendingIfNeeded(ed);

                string namePart = _currentName ?? "";
                string prompt = string.Format(Strings.PatMark_PromptAttachPoint, _currentNumber, namePart);
                var ptResult = ed.GetPoint(prompt);
                if (ptResult.Status != PromptStatus.OK) break;

                ApplyPendingIfNeeded(ed);

                // 循环采集拐点（至少1个，回车/空格结束）
                List<Point3d> doglegPts = new List<Point3d>();
                Point3d lastBase = ptResult.Value;
                bool doglegCancelled = false;
                while (true)
                {
                    var doglegOpts = new PromptPointOptions(
                        doglegPts.Count == 0
                            ? Strings.PatMark_PromptFirstDogleg
                            : Strings.PatMark_PromptNextDogleg);
                    doglegOpts.BasePoint = lastBase;
                    doglegOpts.UseBasePoint = true;
                    doglegOpts.AllowNone = true;
                    var doglegResult = ed.GetPoint(doglegOpts);
                    if (doglegResult.Status == PromptStatus.None)
                    {
                        if (doglegPts.Count == 0)
                        {
                            ed.WriteMessage(Strings.PatMark_NeedOneDogleg);
                            continue;
                        }
                        break;
                    }
                    if (doglegResult.Status != PromptStatus.OK)
                    {
                        doglegCancelled = true;
                        break;
                    }
                    doglegPts.Add(doglegResult.Value);
                    lastBase = doglegResult.Value;
                    ApplyPendingIfNeeded(ed);
                }
                if (doglegCancelled) continue;

                // 文字位置：回车直接用最后拐点
                var textOpts = new PromptPointOptions(Strings.PatMark_PromptTextPos);
                textOpts.BasePoint = lastBase;
                textOpts.UseBasePoint = true;
                textOpts.AllowNone = true;
                var textResult = ed.GetPoint(textOpts);
                Point3d textPt;
                if (textResult.Status == PromptStatus.None)
                {
                    textPt = lastBase;
                }
                else if (textResult.Status != PromptStatus.OK)
                {
                    continue;
                }
                else
                {
                    textPt = textResult.Value;
                }

                ApplyPendingIfNeeded(ed);

                try
                {
                    CreateMLeader(db, ptResult.Value, doglegPts, textPt, _currentNumber);
                    ed.WriteMessage(string.Format(Strings.PatMark_Created, _currentNumber, doglegPts.Count + 1));
                }
                catch (Exception ex)
                {
                    ed.WriteMessage(Strings.ErrorPrefix + ex.GetType().Name + ": " + ex.Message + "\n");
                    PatentMarkerApp.RawLog("CreateMLeader EXCEPTION: " + ex.GetType().FullName + ": " + ex.Message);
                }
            }

            _currentNumber = null;
            _currentName = null;
        }

        /// <summary>
        /// 创建 MLeader — AutoCAD 2013+ API。
        ///
        /// MLeader 是一体式对象：引线 + 文字 + 样式统一管理。
        /// 顺序：
        ///  1. 创建 MLeader，设置 ContentType = MTextContent
        ///  2. 添加引线和顶点
        ///  3. 设置 MText 内容和高度
        ///  4. 设置 TextPosition
        ///  5. 设置 MLeaderStyle = PAT_STYLE
        ///  6. 设置引线类型（样条/直线）和箭头
        ///  7. AppendEntity + Commit
        /// </summary>
        private void CreateMLeader(Database db, Point3d attachPt, List<Point3d> doglegPts, Point3d textPt, string number)
        {
            PatentMarkerApp.RawLog("=== CreateMLeader START (number=" + number + ", vertices=" + (doglegPts.Count + 1) + ") ===");

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 1. 创建 MLeader
                MLeader mleader = new MLeader();
                mleader.SetDatabaseDefaults(db);
                mleader.ContentType = ContentType.MTextContent;

                // 2. 添加引线：从附着点开始，经过所有拐点
                // AddLeaderLine 返回引线索引，后续用 AddVertex(index, point) 追加顶点
                int lineIndex = mleader.AddLeaderLine(attachPt);
                foreach (Point3d p in doglegPts)
                {
                    mleader.AddVertex(lineIndex, p);
                }

                // 3. 设置文字
                MText mt = mleader.MText;
                if (mt == null)
                {
                    mt = new MText();
                    mleader.MText = mt;
                }
                mt.Contents = number;
                mt.TextHeight = Palette.PatPaletteCommand.TextHeight;

                // 设置文字样式
                ObjectId tnrId = Styles.PatStyleInitializer.GetOrCreateTimesRoman(db, tr);
                if (!tnrId.IsNull)
                    mt.TextStyleId = tnrId;

                // 4. 设置文字位置
                mleader.TextPosition = textPt;

                // 5. 设置样式
                ObjectId styleId = Styles.PatStyleInitializer.GetPatStyleId(db, tr);
                if (!styleId.IsNull)
                    mleader.MLeaderStyle = styleId;

                // 6. 覆盖引线类型和箭头（面板控制）
                mleader.LeaderLineType = Palette.PatPaletteCommand.IsSplined
                    ? LeaderLineType.Splines
                    : LeaderLineType.Straight;

                // 箭头控制：通过样式或直接属性
                if (!Palette.PatPaletteCommand.HasArrowHead)
                {
                    // 无箭头：设置箭头符号为空
                    mleader.ArrowSymbolId = ObjectId.Null;
                }

                // 7. 入库
                btr.AppendEntity(mleader);
                tr.AddNewlyCreatedDBObject(mleader, true);

                tr.Commit();
                PatentMarkerApp.RawLog("=== CreateMLeader END (success) ===");
            }
        }

        /// <summary>
        /// 检查面板是否有新的待标注编号，有则立即覆盖当前编号。
        /// </summary>
        private void ApplyPendingIfNeeded(Editor ed)
        {
            if (Palette.PatPaletteCommand.PendingNumber == null) return;
            _currentNumber = Palette.PatPaletteCommand.PendingNumber;
            _currentName = Palette.PatPaletteCommand.PendingName;
            Palette.PatPaletteCommand.PendingNumber = null;
            Palette.PatPaletteCommand.PendingName = null;
            ed.WriteMessage(string.Format(Strings.PatMark_Switched, _currentNumber));
        }
    }
}
