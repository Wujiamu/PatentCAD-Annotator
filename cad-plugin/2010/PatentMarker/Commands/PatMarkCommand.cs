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
    /// <summary>
    /// PATMARK — 引线标注命令 (M2) — AutoCAD 2007 版本。
    ///
    /// 2007 无 MLeader，使用 Leader（继承 Dimension）+ 独立 MText 组合。
    /// v2：样条曲线引线（IsSplined）+ 无限拐点（循环采集）+ 默认无箭头（面板可切换）。
    /// v2.3：中英双语支持。
    /// v3.1：三点模式（面板开关）。开启后固定 3 点：附着点 → 1 个拐点 → 文字位置，
    /// 第 3 点点击后自动创建，Esc/回车取消本次；关闭时保持无限拐点循环采集。
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
            var doc = IO.RuntimeHost.ActiveDocument;
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
                    var numResult = ed.GetString(Strings.PatMark_EnterNumber);
                    if (numResult.Status != PromptStatus.OK) return;
                    _currentNumber = numResult.StringResult;

                    var nameResult = ed.GetString(Strings.PatMark_EnterName);
                    if (nameResult.Status == PromptStatus.OK)
                        _currentName = nameResult.StringResult;
                }
            }

            if (IsNullOrWhiteSpace(_currentNumber))
            {
                ed.WriteMessage(Strings.PatMark_NoNumber);
                return;
            }

            while (true)
            {
                // 热切换：循环开头检查面板是否有新的待标注编号
                ApplyPendingIfNeeded(ed);

                if (!IO.PatSettingsStore.Current.HasLeader)
                {
                    var textOnlyResult = ed.GetPoint(Strings.PatMark_PromptTextOnly);
                    if (textOnlyResult.Status != PromptStatus.OK) break;
                    ApplyPendingIfNeeded(ed);
                    try
                    {
                        CreateTextOnly(db, textOnlyResult.Value, _currentNumber);
                        ed.WriteMessage(string.Format(Strings.PatMark_Created, _currentNumber, 0));
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage(Strings.ErrorPrefix + ex.GetType().Name + ": " + ex.Message + "\n");
                        PatentMarkerApp.RawLog("CreateTextOnly EXCEPTION: " + ex.GetType().FullName + ": " + ex.Message);
                    }
                    continue;
                }

                string namePart = _currentName != null ? _currentName : "";
                string prompt = string.Format(Strings.PatMark_PromptAttachPoint, _currentNumber, namePart);
                var ptResult = ed.GetPoint(prompt);
                if (ptResult.Status != PromptStatus.OK) break;

                // 附着点返回后再次检查（用户可能在 GetPoint 阻塞期间双击了面板）
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
                        CreateLeaderWithText(db, ptResult.Value, doglegPts3, textResult3.Value, _currentNumber);
                        ed.WriteMessage(string.Format(Strings.PatMark_Created, _currentNumber, doglegPts3.Count + 1));
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage(Strings.ErrorPrefix + ex.GetType().Name + ": " + ex.Message + "\n");
                        PatentMarkerApp.RawLog("CreateLeaderWithText EXCEPTION: " + ex.GetType().FullName + ": " + ex.Message);
                    }
                    continue;  // 进入下一个附着点选择
                }

                // v2：循环采集拐点（至少1个，回车/空格结束）
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
                var textOpts = new PromptPointOptions(Strings.PatMark_PromptTextPos);
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
                    ed.WriteMessage(string.Format(Strings.PatMark_Created, _currentNumber, doglegPts.Count + 1));
                }
                catch (Exception ex)
                {
                    ed.WriteMessage(Strings.ErrorPrefix + ex.GetType().Name + ": " + ex.Message + "\n");
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
        ///  3. 将文字附着点作为 Leader 最后一个顶点，不设置 leader.Annotation
        ///  4. 设置 leader.DimensionStyle = PAT_DIM
        ///  5. 设置 IsSplined = true、HasArrowHead = PatPaletteCommand.HasArrowHead
        ///  6. Commit
        /// </summary>
        private void CreateTextOnly(Database db, Point3d textPt, string number)
        {
            PatentMarkerApp.RawLog("=== CreateTextOnly START (number=" + number + ") ===");
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                ObjectId textStyleId = GetOrCreateTextStyleId(db, tr);
                ObjectId originalTextStyle = db.Textstyle;
                db.Textstyle = textStyleId;

                MText mt = new MText();
                mt.SetDatabaseDefaults(db);
                mt.Contents = IO.PatEntityHelper.FormatText(number,
                    IO.PatSettingsStore.Current.UnderlineText);
                mt.TextHeight = IO.PatSettingsStore.Current.TextHeight;
                mt.Attachment = AttachmentPoint.MiddleCenter;
                mt.Location = textPt;
                mt.Rotation = 0.0;
                btr.AppendEntity(mt);
                tr.AddNewlyCreatedDBObject(mt, true);
                IO.PatEntityHelper.MarkStandaloneText(mt, tr);
                db.Textstyle = originalTextStyle;
                tr.Commit();
            }
            PatentMarkerApp.RawLog("=== CreateTextOnly END (success) ===");
        }

        private void CreateLeaderWithText(Database db, Point3d attachPt, List<Point3d> doglegPts, Point3d textPt, string number)
        {
            PatentMarkerApp.RawLog("=== CreateLeaderWithText START (number=" + number + ", doglegPoints=" + doglegPts.Count + ", arrow=" + IO.PatSettingsStore.Current.HasArrowHead + ") ===");

            ObjectId annotationId = ObjectId.Null;
            ObjectId leaderId = ObjectId.Null;
            AttachmentPoint textAttachment = AttachmentPoint.MiddleLeft;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // 1. 创建 MText（先入库获得 ObjectId，修复 B3）
                // v2.4：默认使用 Times New Roman 字体
                ObjectId tnrStyleId = GetOrCreateTextStyleId(db, tr);
                ObjectId origTextStyle = db.Textstyle;
                db.Textstyle = tnrStyleId;

                MText mt = new MText();
                mt.SetDatabaseDefaults(db);
                mt.Contents = IO.PatEntityHelper.FormatText(number,
                    IO.PatSettingsStore.Current.UnderlineText);
                mt.TextHeight = IO.PatSettingsStore.Current.TextHeight;
                Point3d lastLeaderPoint = doglegPts.Count > 0
                    ? doglegPts[doglegPts.Count - 1]
                    : attachPt;
                textAttachment = PatLeaderTextAttachment.Get(
                    lastLeaderPoint, textPt);
                mt.Attachment = textAttachment;
                mt.Location = textPt;
                mt.Rotation = 0.0;
                PatentMarkerApp.RawLog("Text attachment=" + textAttachment
                    + ", lastLeaderPoint=" + lastLeaderPoint
                    + ", textPoint=" + textPt);

                btr.AppendEntity(mt);
                tr.AddNewlyCreatedDBObject(mt, true);

                // 恢复原始文字样式
                db.Textstyle = origTextStyle;

                // 2. 创建 Leader（v2：样条曲线 + 无限拐点 + 默认无箭头）
                Leader leader = new Leader();
                leader.SetDatabaseDefaults(db);
                leader.AppendVertex(attachPt);              // 起点（箭头端）
                foreach (Point3d p in doglegPts)            // v2：循环追加所有拐点
                    leader.AppendVertex(p);
                PatLeaderTextAttachment.AppendTextEndpoint(leader, textPt);
                leader.IsSplined = IO.PatSettingsStore.Current.IsSplined;   // v2.1：样条/直线，取自面板开关
                leader.HasArrowHead = IO.PatSettingsStore.Current.HasArrowHead;

                // v2.1：实例级同步箭头大小，确保修改后新建的引线立即生效
                //（只改 DimStyle 不够，Leader 创建时已继承旧值，需强制覆盖实例属性）
                leader.Dimasz = IO.PatSettingsStore.Current.ArrowSize;

                btr.AppendEntity(leader);
                tr.AddNewlyCreatedDBObject(leader, true);
                leaderId = leader.ObjectId;

                // 3. 保存 MText 关系但不设置 AutoCAD 原生 Annotation hook
                annotationId = mt.ObjectId;
                PatLeaderTextAttachment.LinkText(leader, mt, tr);

                // Re-apply both values after storing the detached link so the
                // requested quadrant is retained after host regeneration.
                mt.Attachment = textAttachment;
                mt.Location = textPt;
                PatentMarkerApp.RawLog("Text attachment after link=" + mt.Attachment
                    + ", location=" + mt.Location);

                // 4. 设置标注样式（同步箭头大小到 PAT_DIM）
                ObjectId dimId = Styles.PatStyleInitializer.GetPatDimStyleId(db, tr);
                if (!dimId.IsNull)
                {
                    leader.DimensionStyle = dimId;
                    // v2.1：同步箭头大小到 DimStyle（影响所有 PAT 引线，专利标注统一规格）
                    DimStyleTableRecord dsr = (DimStyleTableRecord)tr.GetObject(dimId, OpenMode.ForWrite);
                    dsr.Dimasz = IO.PatSettingsStore.Current.ArrowSize;
                }

                tr.Commit();
            }

            AttachmentPoint committedAttachment = PatLeaderTextAttachment.ReapplyAfterCommit(
                db, annotationId, textAttachment, textPt);
            PatentMarkerApp.RawLog("Text attachment after commit=" + committedAttachment
                + ", location=" + textPt);
            PatentMarkerApp.RawLog(PatLeaderTextAttachment.DescribeLeader(db, leaderId));
            PatentMarkerApp.RawLog("=== CreateLeaderWithText END (success) ===");
        }

        /// <summary>
        /// 获取或创建 Times New Roman 文字样式。
        /// v2.4：专利标注默认使用 Times New Roman 字体。
        /// </summary>
        private static ObjectId GetOrCreateTextStyleId(Database db, Transaction tr)
        {
            TextStyleTable tt = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (tt.Has("PatentTimesNewRoman"))
                return tt["PatentTimesNewRoman"];

            tt.UpgradeOpen();
            TextStyleTableRecord ttr = new TextStyleTableRecord();
            ttr.Name = "PatentTimesNewRoman";
            ttr.FileName = "times.ttf";
            ObjectId styleId = tt.Add(ttr);
            tr.AddNewlyCreatedDBObject(ttr, true);
            return styleId;
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
            ed.WriteMessage(string.Format(Strings.PatMark_Switched, _currentNumber));
        }
    }
}
