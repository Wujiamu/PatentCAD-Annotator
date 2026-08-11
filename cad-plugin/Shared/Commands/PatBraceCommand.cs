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
    /// Creates and edits the first-version parametric vector brace.
    /// PATBRACE: top point -> bottom point -> width side.
    /// PATBRACEEDIT: select a brace, then edit control points or dimensions.
    /// </summary>
    public sealed class PatBraceCommand
    {
        [CommandMethod("PATBRACE", CommandFlags.Modal)]
        [CommandMethod("DAGUOHAO", CommandFlags.Modal)]
        public void Create()
        {
            Document doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            PromptPointResult topResult = ed.GetPoint(Strings.PatBrace_PromptTop);
            if (topResult.Status != PromptStatus.OK) return;

            PromptPointOptions bottomOptions = new PromptPointOptions(
                Strings.PatBrace_PromptBottom);
            bottomOptions.BasePoint = topResult.Value;
            bottomOptions.UseBasePoint = true;
            PromptPointResult bottomResult = ed.GetPoint(bottomOptions);
            if (bottomResult.Status != PromptStatus.OK) return;

            PromptPointOptions widthOptions = new PromptPointOptions(
                Strings.PatBrace_PromptWidth);
            widthOptions.BasePoint = bottomResult.Value;
            widthOptions.UseBasePoint = true;
            PromptPointResult widthResult = ed.GetPoint(widthOptions);
            if (widthResult.Status != PromptStatus.OK) return;

            try
            {
                PatBraceDefinition definition = PatBraceGeometry.FromPoints(
                    topResult.Value, bottomResult.Value, widthResult.Value);
                ObjectId braceId = AppendBrace(doc.Database, definition);
                ed.WriteMessage(string.Format(Strings.PatBrace_Created,
                    definition.Height, definition.Width));
                PatentMarkerApp.RawLog("PATBRACE created: " + braceId
                    + ", height=" + definition.Height
                    + ", width=" + definition.Width
                    + ", side=" + definition.Side);
            }
            catch (Exception ex)
            {
                ed.WriteMessage(Strings.ErrorPrefix + ex.Message + "\n");
                PatentMarkerApp.RawLog("PATBRACE create error: " + ex);
            }
        }

        [CommandMethod("PATBRACEEDIT", CommandFlags.Modal)]
        public void Edit()
        {
            Document doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            ObjectId braceId = SelectBrace(ed, doc.Database);
            if (braceId.IsNull) return;

            PatBraceDefinition current;
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                Polyline polyline = (Polyline)tr.GetObject(braceId, OpenMode.ForRead);
                if (!PatBraceEntity.TryReadDefinition(polyline, tr, out current))
                {
                    ed.WriteMessage(Strings.PatBrace_NotFound);
                    return;
                }
            }

            PromptKeywordOptions modeOptions = new PromptKeywordOptions(
                Strings.PatBrace_EditModePrompt);
            modeOptions.Keywords.Add(Strings.PatBrace_EditPoints);
            modeOptions.Keywords.Add(Strings.PatBrace_EditSize);
            modeOptions.Keywords.Default = Strings.PatBrace_EditPoints;
            PromptResult modeResult = ed.GetKeywords(modeOptions);
            if (modeResult.Status != PromptStatus.OK) return;

            try
            {
                PatBraceDefinition updated = modeResult.StringResult == Strings.PatBrace_EditSize
                    ? PromptSize(ed, current)
                    : PromptPoints(ed, current);
                ReplaceBrace(doc.Database, braceId, updated);
                ed.WriteMessage(string.Format(Strings.PatBrace_Updated,
                    updated.Height, updated.Width));
                PatentMarkerApp.RawLog("PATBRACEEDIT updated: " + braceId
                    + ", height=" + updated.Height
                    + ", width=" + updated.Width
                    + ", side=" + updated.Side);
            }
            catch (Exception ex)
            {
                ed.WriteMessage(Strings.ErrorPrefix + ex.Message + "\n");
                PatentMarkerApp.RawLog("PATBRACEEDIT error: " + ex);
            }
        }

        private static ObjectId SelectBrace(Editor ed, Database db)
        {
            ed.WriteMessage(Strings.PatBrace_PromptSelect);
            PromptSelectionResult selection = ed.GetSelection();
            if (selection.Status != PromptStatus.OK || selection.Value.Count == 0)
            {
                ed.WriteMessage(Strings.PatBrace_NotSelected);
                return ObjectId.Null;
            }

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selected in selection.Value)
                {
                    Polyline polyline = tr.GetObject(selected.ObjectId,
                        OpenMode.ForRead) as Polyline;
                    if (polyline != null && PatBraceEntity.IsBrace(polyline, tr))
                        return selected.ObjectId;
                }
            }

            ed.WriteMessage(Strings.PatBrace_NotFound);
            return ObjectId.Null;
        }

        private static PatBraceDefinition PromptPoints(
            Editor ed, PatBraceDefinition current)
        {
            PromptPointOptions topOptions = new PromptPointOptions(
                Strings.PatBrace_EditTop);
            topOptions.AllowNone = true;
            topOptions.BasePoint = current.Top;
            topOptions.UseBasePoint = true;
            PromptPointResult top = ed.GetPoint(topOptions);
            if (top.Status == PromptStatus.Cancel) throw new OperationCanceledException();
            Point3d topPoint = top.Status == PromptStatus.OK ? top.Value : current.Top;

            PromptPointOptions bottomOptions = new PromptPointOptions(
                Strings.PatBrace_EditBottom);
            bottomOptions.AllowNone = true;
            bottomOptions.BasePoint = current.Bottom;
            bottomOptions.UseBasePoint = true;
            PromptPointResult bottom = ed.GetPoint(bottomOptions);
            if (bottom.Status == PromptStatus.Cancel) throw new OperationCanceledException();
            Point3d bottomPoint = bottom.Status == PromptStatus.OK ? bottom.Value : current.Bottom;

            Point3d currentWidthPoint = PatBraceGeometry.GetWidthPoint(
                PatBraceGeometry.WithEndpoints(current, topPoint, bottomPoint));
            PromptPointOptions widthOptions = new PromptPointOptions(
                Strings.PatBrace_EditWidth);
            widthOptions.AllowNone = true;
            widthOptions.BasePoint = currentWidthPoint;
            widthOptions.UseBasePoint = true;
            PromptPointResult width = ed.GetPoint(widthOptions);
            if (width.Status == PromptStatus.Cancel) throw new OperationCanceledException();
            if (width.Status == PromptStatus.OK)
                return PatBraceGeometry.FromPoints(topPoint, bottomPoint, width.Value);
            return PatBraceGeometry.WithEndpoints(current, topPoint, bottomPoint);
        }

        private static PatBraceDefinition PromptSize(
            Editor ed, PatBraceDefinition current)
        {
            PromptDoubleOptions heightOptions = new PromptDoubleOptions(
                string.Format(Strings.PatBrace_EditHeight, current.Height));
            heightOptions.AllowNone = true;
            heightOptions.DefaultValue = current.Height;
            PromptDoubleResult height = ed.GetDouble(heightOptions);
            if (height.Status != PromptStatus.OK && height.Status != PromptStatus.None)
                throw new OperationCanceledException();

            PromptDoubleOptions widthOptions = new PromptDoubleOptions(
                string.Format(Strings.PatBrace_EditWidthValue, current.Width));
            widthOptions.AllowNone = true;
            widthOptions.DefaultValue = current.Width;
            PromptDoubleResult width = ed.GetDouble(widthOptions);
            if (width.Status != PromptStatus.OK && width.Status != PromptStatus.None)
                throw new OperationCanceledException();

            double newHeight = height.Status == PromptStatus.None
                ? current.Height : height.Value;
            double newWidth = width.Status == PromptStatus.None
                ? current.Width : width.Value;
            return PatBraceGeometry.WithSize(current, newHeight, newWidth);
        }

        private static ObjectId AppendBrace(
            Database db, PatBraceDefinition definition)
        {
            ObjectId braceId;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(
                    db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                Polyline polyline = PatBraceEntity.CreatePolyline(db, definition);
                btr.AppendEntity(polyline);
                tr.AddNewlyCreatedDBObject(polyline, true);
                PatBraceEntity.WriteDefinition(polyline, definition, tr);
                braceId = polyline.ObjectId;
                tr.Commit();
            }
            return braceId;
        }

        private static void ReplaceBrace(
            Database db, ObjectId braceId, PatBraceDefinition definition)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline polyline = (Polyline)tr.GetObject(
                    braceId, OpenMode.ForWrite);
                PatBraceEntity.ReplaceGeometry(polyline, definition, tr);
                tr.Commit();
            }
        }
    }
}
