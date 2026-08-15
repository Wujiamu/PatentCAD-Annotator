// ============================================================================
// 复线（mleader 分支）版本本地替换文件 — 仅 2025/2026 版编译。
// 来源：cad-plugin/Shared/Commands/PatMarkCommand.cs（交互流保持同源）。
// 差异：CreateLeaderWithText 改为 PatMLeaderCreator.Create（F 方案三点链），
//       文字由 MLeader 自持，不再有独立 MText / LinkText / ReapplyAfterCommit。
// 同步警示：主线 Shared 版本更新交互流时需手动对齐本文件。
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
    /// <summary>Creates patent callouts as single MLeader entities (Plan F).</summary>
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
                ApplyPendingIfNeeded(ed);
                if (!IO.PatSettingsStore.Current.HasLeader)
                {
                    var textOnlyOptions = new PromptPointOptions(Strings.PatMark_PromptTextOnly);
                    textOnlyOptions.AllowNone = true;
                    var textOnlyResult = ed.GetPoint(textOnlyOptions);
                    if (textOnlyResult.Status != PromptStatus.OK) break;
                    ApplyPendingIfNeeded(ed);
                    try
                    {
                        CreateTextOnly(db, textOnlyResult.Value, _currentNumber);
                        ed.WriteMessage(string.Format(Strings.PatMark_Created,
                            _currentNumber, 0));
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage(Strings.ErrorPrefix + ex.GetType().Name + ": " + ex.Message + "\n");
                        PatentMarkerApp.RawLog("CreateTextOnly EXCEPTION: " + ex.GetType().FullName + ": " + ex.Message);
                    }
                    continue;
                }
                string namePart = _currentName != null ? _currentName : "";
                var attachOptions = new PromptPointOptions(string.Format(
                    Strings.PatMark_PromptAttachPoint, _currentNumber, namePart));
                attachOptions.AllowNone = true;
                var ptResult = ed.GetPoint(attachOptions);
                if (ptResult.Status != PromptStatus.OK) break;
                ApplyPendingIfNeeded(ed);

                if (IO.PatSettingsStore.Current.ThreePointMode)
                {
                    var doglegOpts = new PromptPointOptions(Strings.PatMark_PromptDogleg3);
                    doglegOpts.BasePoint = ptResult.Value;
                    doglegOpts.UseBasePoint = true;
                    doglegOpts.AllowNone = true;
                    var doglegResult = ed.GetPoint(doglegOpts);
                    if (doglegResult.Status != PromptStatus.OK) break;
                    ApplyPendingIfNeeded(ed);

                    var textOpts = new PromptPointOptions(Strings.PatMark_PromptTextPos3);
                    textOpts.BasePoint = doglegResult.Value;
                    textOpts.UseBasePoint = true;
                    textOpts.AllowNone = true;
                    var textResult = ed.GetPoint(textOpts);
                    if (textResult.Status != PromptStatus.OK) break;
                    ApplyPendingIfNeeded(ed);

                    try
                    {
                        var doglegPts = new List<Point3d>();
                        doglegPts.Add(doglegResult.Value);
                        CreateMLeaderWithText(db, ptResult.Value, doglegPts,
                            textResult.Value, _currentNumber);
                        ed.WriteMessage(string.Format(Strings.PatMark_Created,
                            _currentNumber, doglegPts.Count + 1));
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage(Strings.ErrorPrefix + ex.GetType().Name + ": " + ex.Message + "\n");
                        PatentMarkerApp.RawLog("CreateMLeaderWithText EXCEPTION: " + ex.GetType().FullName + ": " + ex.Message);
                    }
                    continue;
                }

                var doglegs = new List<Point3d>();
                Point3d lastBase = ptResult.Value;
                bool cancelled = false;
                while (true)
                {
                    var doglegOpts = new PromptPointOptions(doglegs.Count == 0
                        ? Strings.PatMark_PromptFirstDogleg
                        : Strings.PatMark_PromptNextDogleg);
                    doglegOpts.BasePoint = lastBase;
                    doglegOpts.UseBasePoint = true;
                    doglegOpts.AllowNone = true;
                    var doglegResult = ed.GetPoint(doglegOpts);
                    if (doglegResult.Status == PromptStatus.None)
                    {
                        if (doglegs.Count == 0)
                        {
                            ed.WriteMessage(Strings.PatMark_NeedOneDogleg);
                            cancelled = true;
                            break;
                        }
                        break;
                    }
                    if (doglegResult.Status != PromptStatus.OK)
                    {
                        cancelled = true;
                        break;
                    }
                    doglegs.Add(doglegResult.Value);
                    lastBase = doglegResult.Value;
                    ApplyPendingIfNeeded(ed);
                }
                if (cancelled) break;

                var textPrompt = new PromptPointOptions(Strings.PatMark_PromptTextPos);
                textPrompt.BasePoint = lastBase;
                textPrompt.UseBasePoint = true;
                textPrompt.AllowNone = true;
                var textResultFree = ed.GetPoint(textPrompt);
                Point3d textPt;
                if (textResultFree.Status == PromptStatus.None)
                {
                    textPt = lastBase;
                }
                else if (textResultFree.Status != PromptStatus.OK)
                    break;
                else
                    textPt = textResultFree.Value;

                ApplyPendingIfNeeded(ed);
                try
                {
                    CreateMLeaderWithText(db, ptResult.Value, doglegs, textPt,
                        _currentNumber);
                    ed.WriteMessage(string.Format(Strings.PatMark_Created,
                        _currentNumber, doglegs.Count + 1));
                }
                catch (Exception ex)
                {
                    ed.WriteMessage(Strings.ErrorPrefix + ex.GetType().Name + ": " + ex.Message + "\n");
                    PatentMarkerApp.RawLog("CreateMLeaderWithText EXCEPTION: " + ex.GetType().FullName + ": " + ex.Message);
                }
            }

            _currentNumber = null;
            _currentName = null;
        }

        private void CreateTextOnly(Database db, Point3d textPt, string number)
        {
            PatentMarkerApp.RawLog("=== CreateTextOnly START (number=" + number + ") ===");
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                ObjectId textStyleId = PatMLeaderCreator.GetOrCreateTextStyleId(db, tr);
                ObjectId originalTextStyle = db.Textstyle;
                db.Textstyle = textStyleId;

                MText mt = new MText();
                mt.SetDatabaseDefaults(db);
                mt.Contents = IO.PatEntityHelper.FormatText(
                    number, IO.PatSettingsStore.Current.UnderlineText);
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

        /// <summary>Plan-F creation: one MLeader carrying attach → doglegs → text.</summary>
        private void CreateMLeaderWithText(Database db, Point3d attachPt,
            List<Point3d> doglegPts, Point3d textPt, string number)
        {
            PatentMarkerApp.RawLog("=== CreateMLeaderWithText START (number=" + number
                + ", doglegPoints=" + doglegPts.Count
                + ", arrow=" + IO.PatSettingsStore.Current.HasArrowHead
                + ", splined=" + IO.PatSettingsStore.Current.IsSplined + ") ===");

            ObjectId mleaderId;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                mleaderId = PatMLeaderCreator.Create(
                    db, tr, btr, attachPt, doglegPts, textPt, number);
                tr.Commit();
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                MLeader ml = (MLeader)tr.GetObject(mleaderId, OpenMode.ForRead);
                PatentMarkerApp.RawLog("MLeader state: style=" + ml.MLeaderStyle
                    + ", lines=" + ml.LeaderLineCount
                    + ", contentType=" + ml.ContentType
                    + ", textLocation=" + ml.TextLocation
                    + ", enableDogleg=" + ml.EnableDogleg
                    + ", enableLanding=" + ml.EnableLanding
                    + ", extendLeaderToText=" + ml.ExtendLeaderToText
                    + ", leaderLineType=" + ml.LeaderLineType
                    + ", arrowSymbolId=" + ml.ArrowSymbolId);
            }
            PatentMarkerApp.RawLog("=== CreateMLeaderWithText END (success) ===");
        }

        private static bool IsNullOrWhiteSpace(string value)
        {
            return value == null || value.Trim().Length == 0;
        }

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
