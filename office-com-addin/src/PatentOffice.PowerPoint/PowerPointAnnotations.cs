using System;
using System.Collections.Generic;
using PatentMarker.IO;
using PatentOffice.Shared;

namespace PatentOffice.PowerPoint
{
    internal static class PowerPointAnnotations
    {
        private const int PpSelectionShapes = 2;
        private const int MsoLineShape = 9;
        private const int MsoGroupShape = 6;
        private const int MsoTextBoxShape = 17;
        private const int MsoHorizontal = 1;
        private const int MsoArrowheadNone = 1;
        private const int MsoArrowheadTriangle = 2;
        private const int MsoAnchorMiddle = 3;
        private const int PpAlignCenter = 2;

        public static void MarkSelectedLine(dynamic application, PatentEntry entry)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            dynamic presentation = GetActivePresentation(application);
            if (presentation == null) throw new InvalidOperationException("请先打开演示文稿。");
            if (!IsSavedPresentation(presentation)) throw new InvalidOperationException("请先保存演示文稿，再添加标注。");

            dynamic window = application.ActiveWindow;
            dynamic selection = window.Selection;
            if (Convert.ToInt32(selection.Type) != PpSelectionShapes)
                throw new InvalidOperationException("请先在当前幻灯片中选中一条 PowerPoint 直线。");

            dynamic range = selection.ShapeRange;
            if (Convert.ToInt32(range.Count) != 1)
                throw new InvalidOperationException("一次只能标注一条选中的直线。");

            dynamic line = range.Item(1);
            if (Convert.ToInt32(line.Type) != MsoLineShape)
                throw new InvalidOperationException("选中的对象不是 PowerPoint 原生直线。请先绘制直线，再选中它。");

            dynamic slide = window.View.Slide;
            dynamic ownerSlide = line.Parent;
            if (Convert.ToInt32(slide.SlideID) != Convert.ToInt32(ownerSlide.SlideID))
                throw new InvalidOperationException("所选直线不属于当前幻灯片。");
            dynamic ownerPresentation = ownerSlide.Parent;
            if (!string.Equals(Convert.ToString(presentation.FullName), Convert.ToString(ownerPresentation.FullName), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("所选直线不属于当前演示文稿。");

            double startX;
            double startY;
            double endX;
            double endY;
            GetLineEndpoints(line, out startX, out startY, out endX, out endY);

            int oldBeginArrow = Convert.ToInt32(line.Line.BeginArrowheadStyle);
            int oldEndArrow = Convert.ToInt32(line.Line.EndArrowheadStyle);
            dynamic label = null;
            dynamic group = null;
            string labelName = null;

            try
            {
                line.Line.BeginArrowheadStyle = MsoArrowheadNone;
                line.Line.EndArrowheadStyle = MsoArrowheadTriangle;

                label = slide.Shapes.AddTextbox(
                    MsoHorizontal,
                    (float)(startX - 32.0),
                    (float)(startY - 11.0),
                    64.0f,
                    22.0f);
                labelName = Convert.ToString(label.Name);
                label.TextFrame.TextRange.Text = entry.Number;
                label.TextFrame.TextRange.Font.Size = 12.0f;
                label.TextFrame.TextRange.ParagraphFormat.Alignment = PpAlignCenter;
                label.TextFrame.VerticalAnchor = MsoAnchorMiddle;
                label.TextFrame.MarginLeft = 0.0f;
                label.TextFrame.MarginRight = 0.0f;
                label.TextFrame.MarginTop = 0.0f;
                label.TextFrame.MarginBottom = 0.0f;
                label.Fill.Visible = 0;
                label.Line.Visible = 0;

                object[] names = new object[] { line.Name, labelName };
                dynamic shapeRange = slide.Shapes.Range(names);
                group = shapeRange.Group();
                group.Tags.Add("PATMARKER", "PATENTMARKER");
                group.Tags.Add("PATNUMBER", entry.Number);
            }
            catch
            {
                RollBack(slide, line, label, labelName, group, oldBeginArrow, oldEndArrow);
                throw;
            }

            OfficeDiagnostics.Write("annotation.create", "PASS",
                "number=" + entry.Number + ";slide_id=" + Convert.ToString(slide.SlideID));
        }

        public static HashSet<string> GetMarkedNumbers(dynamic presentation)
        {
            HashSet<string> numbers = new HashSet<string>(NumberIdentity.Comparer);
            dynamic slides = presentation.Slides;
            int slideCount = Convert.ToInt32(slides.Count);
            for (int slideIndex = 1; slideIndex <= slideCount; slideIndex++)
            {
                dynamic slide = slides.Item(slideIndex);
                dynamic shapes = slide.Shapes;
                int shapeCount = Convert.ToInt32(shapes.Count);
                for (int shapeIndex = 1; shapeIndex <= shapeCount; shapeIndex++)
                {
                    dynamic shape = shapes.Item(shapeIndex);
                    if (Convert.ToInt32(shape.Type) != MsoGroupShape || !IsPatentAnnotationGroup(shape))
                        continue;
                    string marker = null;
                    string number = null;
                    if (TryGetTag(shape, "PATMARKER", out marker) && marker == "PATENTMARKER" &&
                        TryGetTag(shape, "PATNUMBER", out number) && !string.IsNullOrWhiteSpace(number))
                    {
                        numbers.Add(NumberIdentity.Normalize(number));
                    }
                }
            }
            return numbers;
        }

        private static bool IsPatentAnnotationGroup(dynamic group)
        {
            try
            {
                dynamic items = group.GroupItems;
                if (Convert.ToInt32(items.Count) != 2) return false;
                int lines = 0;
                int textBoxes = 0;
                for (int index = 1; index <= 2; index++)
                {
                    int type = Convert.ToInt32(items.Item(index).Type);
                    if (type == MsoLineShape) lines++;
                    else if (type == MsoTextBoxShape) textBoxes++;
                }
                return lines == 1 && textBoxes == 1;
            }
            catch { return false; }
        }

        public static dynamic GetActivePresentation(dynamic application)
        {
            try
            {
                if (Convert.ToInt32(application.Presentations.Count) == 0) return null;
                return application.ActivePresentation;
            }
            catch { return null; }
        }

        public static bool IsSavedPresentation(dynamic presentation)
        {
            try
            {
                string path = Convert.ToString(presentation.FullName);
                return !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path);
            }
            catch { return false; }
        }

        private static bool TryGetTag(dynamic shape, string requestedName, out string value)
        {
            value = null;
            try
            {
                dynamic tags = shape.Tags;
                int count = Convert.ToInt32(tags.Count);
                for (int index = 1; index <= count; index++)
                {
                    string name = Convert.ToString(tags.Name(index));
                    if (string.Equals(name, requestedName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = Convert.ToString(tags.Value(index));
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void GetLineEndpoints(dynamic line, out double startX, out double startY, out double endX, out double endY)
        {
            double left = Convert.ToDouble(line.Left);
            double top = Convert.ToDouble(line.Top);
            double width = Convert.ToDouble(line.Width);
            double height = Convert.ToDouble(line.Height);
            bool flipH = Convert.ToBoolean(line.HorizontalFlip);
            bool flipV = Convert.ToBoolean(line.VerticalFlip);

            startX = flipH ? left + width : left;
            startY = flipV ? top + height : top;
            endX = flipH ? left : left + width;
            endY = flipV ? top : top + height;
        }

        private static void RollBack(dynamic slide, dynamic line, dynamic label, string labelName,
            dynamic group, int oldBeginArrow, int oldEndArrow)
        {
            try
            {
                if (group != null)
                {
                    group.Ungroup();
                    if (!string.IsNullOrEmpty(labelName))
                    {
                        try { slide.Shapes.Item(labelName).Delete(); }
                        catch { }
                    }
                }
                else if (label != null)
                {
                    try { label.Delete(); }
                    catch { }
                }
            }
            catch { }

            try
            {
                line.Line.BeginArrowheadStyle = oldBeginArrow;
                line.Line.EndArrowheadStyle = oldEndArrow;
            }
            catch { }
        }
    }
}
