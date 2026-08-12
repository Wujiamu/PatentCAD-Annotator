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
    /// <summary>Creates patent callouts with the legacy Leader + MText pair.</summary>
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
                    var textOnlyResult = ed.GetPoint(Strings.PatMark_PromptTextOnly);
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
                var ptResult = ed.GetPoint(string.Format(
                    Strings.PatMark_PromptAttachPoint, _currentNumber, namePart));
                if (ptResult.Status != PromptStatus.OK) break;
                ApplyPendingIfNeeded(ed);

                if (IO.PatSettingsStore.Current.ThreePointMode)
                {
                    var doglegOpts = new PromptPointOptions(Strings.PatMark_PromptDogleg3);
                    doglegOpts.BasePoint = ptResult.Value;
                    doglegOpts.UseBasePoint = true;
                    var doglegResult = ed.GetPoint(doglegOpts);
                    if (doglegResult.Status != PromptStatus.OK) continue;
                    ApplyPendingIfNeeded(ed);

                    var textOpts = new PromptPointOptions(Strings.PatMark_PromptTextPos3);
                    textOpts.BasePoint = doglegResult.Value;
                    textOpts.UseBasePoint = true;
                    var textResult = ed.GetPoint(textOpts);
                    if (textResult.Status != PromptStatus.OK) continue;
                    ApplyPendingIfNeeded(ed);

                    try
                    {
                        var doglegPts = new List<Point3d>();
                        doglegPts.Add(doglegResult.Value);
                        CreateLeaderWithText(db, ptResult.Value, doglegPts,
                            textResult.Value, _currentNumber, doglegResult.Value);
                        ed.WriteMessage(string.Format(Strings.PatMark_Created,
                            _currentNumber, doglegPts.Count + 1));
                    }
                    catch (Exception ex)
                    {
                        ed.WriteMessage(Strings.ErrorPrefix + ex.GetType().Name + ": " + ex.Message + "\n");
                        PatentMarkerApp.RawLog("CreateLeaderWithText EXCEPTION: " + ex.GetType().FullName + ": " + ex.Message);
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
                            continue;
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
                if (cancelled) continue;

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
                    continue;
                else
                    textPt = textResultFree.Value;

                Point3d attachmentReference = textResultFree.Status == PromptStatus.None
                    ? (doglegs.Count > 1 ? doglegs[doglegs.Count - 2] : ptResult.Value)
                    : doglegs[doglegs.Count - 1];

                ApplyPendingIfNeeded(ed);
                try
                {
                    CreateLeaderWithText(db, ptResult.Value, doglegs, textPt,
                        _currentNumber, attachmentReference);
                    ed.WriteMessage(string.Format(Strings.PatMark_Created,
                        _currentNumber, doglegs.Count + 1));
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

        private void CreateLeaderWithText(Database db, Point3d attachPt,
            List<Point3d> doglegPts, Point3d textPt, string number,
            Point3d attachmentReference)
        {
            PatentMarkerApp.RawLog("=== CreateLeaderWithText START (number=" + number
                + ", doglegPoints=" + doglegPts.Count + ", arrow="
                + IO.PatSettingsStore.Current.HasArrowHead + ") ===");

            ObjectId annotationId = ObjectId.Null;
            ObjectId leaderId = ObjectId.Null;
            AttachmentPoint textAttachment = AttachmentPoint.MiddleLeft;
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
                mt.Contents = IO.PatEntityHelper.FormatText(
                    number, IO.PatSettingsStore.Current.UnderlineText);
                mt.TextHeight = IO.PatSettingsStore.Current.TextHeight;
                Point3d lastLeaderPoint = attachmentReference;
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
                db.Textstyle = originalTextStyle;

                Leader leader = new Leader();
                leader.SetDatabaseDefaults(db);
                leader.AppendVertex(attachPt);
                foreach (Point3d point in doglegPts)
                    leader.AppendVertex(point);
                PatLeaderTextAttachment.AppendTextEndpoint(leader, textPt);
                leader.IsSplined = IO.PatSettingsStore.Current.IsSplined;
                leader.HasArrowHead = IO.PatSettingsStore.Current.HasArrowHead;
                leader.Dimasz = IO.PatSettingsStore.Current.ArrowSize;
                btr.AppendEntity(leader);
                tr.AddNewlyCreatedDBObject(leader, true);
                leaderId = leader.ObjectId;

                annotationId = mt.ObjectId;
                PatLeaderTextAttachment.LinkText(leader, mt, tr);
                mt.Attachment = textAttachment;
                mt.Location = textPt;
                PatentMarkerApp.RawLog("Text attachment after link=" + mt.Attachment
                    + ", location=" + mt.Location);
                ObjectId dimId = Styles.PatStyleInitializer.GetPatDimStyleId(db, tr);
                if (!dimId.IsNull)
                {
                    leader.DimensionStyle = dimId;
                    DimStyleTableRecord style = (DimStyleTableRecord)tr.GetObject(
                        dimId, OpenMode.ForWrite);
                    style.Dimasz = IO.PatSettingsStore.Current.ArrowSize;
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

        private static ObjectId GetOrCreateTextStyleId(Database db, Transaction tr)
        {
            TextStyleTable table = (TextStyleTable)tr.GetObject(
                db.TextStyleTableId, OpenMode.ForRead);
            if (table.Has("PatentTimesNewRoman"))
                return table["PatentTimesNewRoman"];

            table.UpgradeOpen();
            TextStyleTableRecord record = new TextStyleTableRecord();
            record.Name = "PatentTimesNewRoman";
            record.FileName = "times.ttf";
            ObjectId styleId = table.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
            return styleId;
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
