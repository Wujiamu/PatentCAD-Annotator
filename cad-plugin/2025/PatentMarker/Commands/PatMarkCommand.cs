using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.I18n;
using System;
using System.Collections.Generic;
using AppAcad = Autodesk.AutoCAD.ApplicationServices.Application;
using Exception = System.Exception;

namespace PatentMarker.Commands
{
    /// <summary>
    /// PATMARK — 引线标注命令 — AutoCAD 2013/2014 版本。
    ///
    /// 2013 引入 MLeader（一体式：引线 + 文字 + 样式），无需 Leader + MText 拼合。
    /// v2：样条曲线引线 + 无限拐点 + 默认无箭头（面板可切换）。
    /// v3.1：三点模式（面板开关）。开启后固定 3 点：附着点 → 1 个拐点 → 文字位置，
    /// 第 3 点点击后自动创建，Esc/回车取消本次；关闭时保持无限拐点循环采集。
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
            var doc = IO.RuntimeHost.ActiveDocument;
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

                // v3.1：三点模式 — 附着点(已点) → 1 个拐点 → 文字位置，第 3 点点击后自动创建
                if (IO.PatSettingsStore.Current.ThreePointMode)
                {
                    var doglegOpts3 = new PromptPointOptions(Strings.PatMark_PromptDogleg3);
                    doglegOpts3.BasePoint = ptResult.Value;
                    doglegOpts3.UseBasePoint = true;
                    var doglegResult3 = ed.GetPoint(doglegOpts3);
                    if (doglegResult3.Status != PromptStatus.OK) continue;  // Esc/回车：硬性三点，取消本次

                    ApplyPendingIfNeeded(ed);

                    var textOpts3 = new PromptPointOptions(Strings.PatMark_PromptTextPos3);
                    textOpts3.BasePoint = doglegResult3.Value;
                    textOpts3.UseBasePoint = true;
                    var textResult3 = ed.GetPoint(textOpts3);
                    if (textResult3.Status != PromptStatus.OK) continue;  // Esc/回车：硬性三点，取消本次

                    ApplyPendingIfNeeded(ed);

                    try
                    {
                        List<Point3d> doglegPts3 = new List<Point3d>();
                        doglegPts3.Add(doglegResult3.Value);
                        CreateMLeader(db, ptResult.Value, doglegPts3, textResult3.Value, _currentNumber);
                        ed.WriteMessage(string.Format(Strings.PatMark_Created, _currentNumber, doglegPts3.Count + 1));
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage(Strings.ErrorPrefix + ex.GetType().Name + ": " + ex.Message + "\n");
                        PatentMarkerApp.RawLog("CreateMLeader EXCEPTION: " + ex.GetType().FullName + ": " + ex.Message);
                    }
                    continue;  // 进入下一个附着点选择
                }

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
        ///  2. 设置 MLeaderStyle、MText 内容和文字位置
        ///  3. 设置引线类型（样条/直线）和箭头
        ///  4. 添加引线和顶点
        ///  5. AppendEntity + Commit
        /// </summary>
        private void CreateMLeader(Database db, Point3d attachPt, List<Point3d> doglegPts, Point3d textPt, string number)
        {
            PatentMarkerApp.RawLog("=== CreateMLeader START (number=" + number + ", vertices=" + (doglegPts.Count + 1) + ") ===");

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 1. 创建 MLeader 并先准备内容。
                // AutoCAD 2013 对空内容的 MLeader 调用 AddLeaderLine 较严格；
                // 必须先挂载有效的 MText 和 MLeaderStyle，再添加引线顶点。
                MLeader mleader = new MLeader();
                mleader.SetDatabaseDefaults(db);
                mleader.ContentType = ContentType.MTextContent;

                // 2. 设置样式和文字
                ObjectId tnrId = Styles.PatStyleInitializer.GetOrCreateTimesRoman(db, tr);
                ObjectId styleId = Styles.PatStyleInitializer.GetPatStyleId(db, tr);
                if (!styleId.IsNull)
                    mleader.MLeaderStyle = styleId;

                // 禁用 MLeader 自动 dogleg/landing，几何只保留用户点击的顶点。
                // 同时强制文字水平，避免移动文字后沿最后一段引线倾斜。
                mleader.EnableDogleg = false;
                mleader.EnableLanding = false;
                mleader.ExtendLeaderToText = false;
                mleader.DoglegLength = 0.0;
                mleader.LandingGap = 0.0;
                mleader.TextAttachmentDirection = TextAttachmentDirection.AttachmentHorizontal;
                mleader.TextAttachmentType = TextAttachmentType.AttachmentMiddle;
                mleader.TextAngleType = TextAngleType.HorizontalAngle;

                // 6. 覆盖引线类型和箭头（面板控制）
                mleader.LeaderLineType = IO.PatSettingsStore.Current.IsSplined
                    ? LeaderType.SplineLeader
                    : LeaderType.StraightLeader;

                // 箭头控制
                if (!IO.PatSettingsStore.Current.HasArrowHead)
                {
                    mleader.ArrowSymbolId = ObjectId.Null;
                }
                mleader.ArrowSize = IO.PatSettingsStore.Current.ArrowSize;

                MText mt = new MText();
                mt.SetDatabaseDefaults(db);
                mt.Contents = number;
                mt.TextHeight = IO.PatSettingsStore.Current.TextHeight;
                if (!tnrId.IsNull)
                    mt.TextStyleId = tnrId;
                mt.Rotation = 0.0;
                mt.Location = textPt;
                mleader.MText = mt;
                mleader.TextLocation = textPt;

                // 文字高度同步到 MLeader 实例（覆盖样式默认值）
                mleader.TextHeight = IO.PatSettingsStore.Current.TextHeight;

                // 3. 最后添加引线：从附着点开始，经过所有拐点
                // AddLeaderLine(Point3d) 返回引线索引，后续用 AddLastVertex 追加顶点。
                int lineIndex = mleader.AddLeaderLine(attachPt);
                foreach (Point3d p in doglegPts)
                {
                    mleader.AddLastVertex(lineIndex, p);
                }

                // 4. 入库
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
