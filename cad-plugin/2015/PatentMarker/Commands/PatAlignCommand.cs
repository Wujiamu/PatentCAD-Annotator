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
    public class PatAlignCommand
    {
        [CommandMethod("PATALIGN", CommandFlags.UsePickSet)]
        [CommandMethod("BZA", CommandFlags.UsePickSet)]
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
            var keyword = ed.GetKeywords(options);
            if (keyword.Status != PromptStatus.OK) return;

            if (keyword.StringResult == Strings.PatAlign_KwSelect)
                AlignSelected(ed);
            else
                AlignToFrame(ed);

            PatentMarkerApp.RawLog("=== PATALIGN END ===");
}
        private void AlignSelected(Editor ed)
        {
            ed.WriteMessage(Strings.PatAlign_PromptSelect);
            var selection = ed.GetSelection();
            if (selection.Status != PromptStatus.OK ||
                selection.Value.Count == 0)
            {
                ed.WriteMessage(Strings.PatAlign_NoSelection);
                return;
            }

            var reference = ed.GetPoint(Strings.PatAlign_PromptRefPoint);
            if (reference.Status != PromptStatus.OK) return;

            var direction = new PromptKeywordOptions(
                Strings.PatAlign_DirectionPrompt);
            direction.Keywords.Add(Strings.PatAlign_KwHorizontal);
            direction.Keywords.Add(Strings.PatAlign_KwVertical);
            var directionResult = ed.GetKeywords(direction);
            if (directionResult.Status != PromptStatus.OK) return;

            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int aligned = 0;
                int skipped = 0;
                int errors = 0;

                foreach (SelectedObject selected in selection.Value)
                {
                    try
                    {
                        Entity entity = (Entity)tr.GetObject(
                            selected.ObjectId, OpenMode.ForRead);
                        if (!IO.PatEntityHelper.IsPatEntity(entity, tr))
                        {
                            skipped++;
                            continue;
                        }

                        Leader leader = (Leader)entity;
                        ObjectId annotationId = PatLeaderTextAttachment.GetAnnotationId(leader, tr);
                        if (annotationId.IsNull)
                        {
                            skipped++;
                            continue;
                        }

                        MText text = (MText)tr.GetObject(
                            annotationId, OpenMode.ForWrite);
                        Point3d position = text.Location;
                        if (directionResult.StringResult ==
                            Strings.PatAlign_KwHorizontal)
                            position = new Point3d(
                                position.X, reference.Value.Y, position.Z);
                        else
                            position = new Point3d(
                                reference.Value.X, position.Y, position.Z);
                        text.Location = position;
                        if (leader.Annotation.IsNull)
                            PatLeaderTextAttachment.SetTextEndpoint(leader, position);
                        aligned++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        PatentMarkerApp.RawLog("PATALIGN error: " + ex.Message);
                    }
                }

                tr.Commit();
                ed.WriteMessage(string.Format(
                    Strings.PatAlign_ResultSelect, aligned, skipped, errors));
            }
        }

        private void AlignToFrame(Editor ed)
        {
            var first = ed.GetPoint(Strings.PatAlign_PromptFrameCorner1);
            if (first.Status != PromptStatus.OK) return;
            var second = ed.GetCorner(
                Strings.PatAlign_PromptFrameCorner2, first.Value);
            if (second.Status != PromptStatus.OK) return;

            var side = new PromptKeywordOptions(Strings.PatAlign_SidePrompt);
            side.Keywords.Add(Strings.PatAlign_KwLeft);
            side.Keywords.Add(Strings.PatAlign_KwRight);
            side.Keywords.Add(Strings.PatAlign_KwTop);
            side.Keywords.Add(Strings.PatAlign_KwBottom);
            var sideResult = ed.GetKeywords(side);
            if (sideResult.Status != PromptStatus.OK) return;

            double margin = IO.PatSettingsStore.Current.MarginToFrame;
            ed.WriteMessage(Strings.PatAlign_PromptSelect);
            var selection = ed.GetSelection();
            if (selection.Status != PromptStatus.OK ||
                selection.Value.Count == 0)
            {
                ed.WriteMessage(Strings.PatAlign_NoSelection);
                return;
            }

            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            double minX = Math.Min(first.Value.X, second.Value.X);
            double maxX = Math.Max(first.Value.X, second.Value.X);
            double minY = Math.Min(first.Value.Y, second.Value.Y);
            double maxY = Math.Max(first.Value.Y, second.Value.Y);

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                int aligned = 0;
                int skipped = 0;
                int errors = 0;

                foreach (SelectedObject selected in selection.Value)
                {
                    try
                    {
                        Entity entity = (Entity)tr.GetObject(
                            selected.ObjectId, OpenMode.ForRead);
                        if (!IO.PatEntityHelper.IsPatEntity(entity, tr))
                        {
                            skipped++;
                            continue;
                        }

                        Leader leader = (Leader)entity;
                        ObjectId annotationId = PatLeaderTextAttachment.GetAnnotationId(leader, tr);
                        if (annotationId.IsNull)
                        {
                            skipped++;
                            continue;
                        }

                        MText text = (MText)tr.GetObject(
                            annotationId, OpenMode.ForWrite);
                        Point3d position = text.Location;
                        if (sideResult.StringResult == Strings.PatAlign_KwLeft)
                            position = new Point3d(
                                minX - margin, position.Y, position.Z);
                        else if (sideResult.StringResult ==
                            Strings.PatAlign_KwRight)
                            position = new Point3d(
                                maxX + margin, position.Y, position.Z);
                        else if (sideResult.StringResult ==
                            Strings.PatAlign_KwTop)
                            position = new Point3d(
                                position.X, maxY + margin, position.Z);
                        else
                            position = new Point3d(
                                position.X, minY - margin, position.Z);
                        text.Location = position;
                        if (leader.Annotation.IsNull)
                            PatLeaderTextAttachment.SetTextEndpoint(leader, position);
                        aligned++;
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        PatentMarkerApp.RawLog("PATALIGN error: " + ex.Message);
                    }
                }

                tr.Commit();
                ed.WriteMessage(string.Format(
                    Strings.PatAlign_ResultFrame, aligned,
                    sideResult.StringResult, margin.ToString("F1"),
                    skipped, errors));
            }
        }
    }
}
