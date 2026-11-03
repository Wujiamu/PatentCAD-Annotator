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
                ScanShapes(slide.Shapes, numbers);
            }
            return numbers;
        }

        /// <summary>
        /// Walks the top level and every nested group. A user may group an
        /// existing product annotation with other shapes; the annotation stays
        /// valid and visible, so skipping nested groups would report a marked
        /// number as missing. Only shapes carrying the product identity and the
        /// line+label structure are claimed; ordinary groups and text are left
        /// alone.
        /// </summary>
        private static void ScanShapes(dynamic shapes, HashSet<string> numbers)
        {
            int count = Convert.ToInt32(shapes.Count);
            for (int index = 1; index <= count; index++)
            {
                dynamic shape = shapes.Item(index);
                int type = Convert.ToInt32(shape.Type);
                if (type == MsoGroupShape)
                {
                    if (IsPatentAnnotationGroup(shape) && TryAddMarkedNumber(shape, numbers))
                        continue;
                    try { ScanShapes(shape.GroupItems, numbers); }
                    catch (Exception ex)
                    {
                        OfficeDiagnostics.Write("annotation.scan", "SKIP",
                            "nested group not readable: " + ex.Message);
                    }
                    continue;
                }
                if (IsPatentAnnotationGroup(shape)) TryAddMarkedNumber(shape, numbers);
            }
        }

        /// <summary>
        /// Claims the group's number only when the visible label text still
        /// matches the identity tag. A user who retypes or clears the label
        /// must not be reported as "301 already marked" for a tag that says 301.
        /// </summary>
        private static bool TryAddMarkedNumber(dynamic group, HashSet<string> numbers)
        {
            string marker = null;
            string number = null;
            if (!TryGetTag(group, "PATMARKER", out marker) || marker != "PATENTMARKER") return false;
            if (!TryGetTag(group, "PATNUMBER", out number) || string.IsNullOrWhiteSpace(number)) return false;
            string visible;
            if (!TryGetVisibleLabel(group, out visible)) return false;
            if (!NumberIdentity.AreEqual(visible, number))
            {
                OfficeDiagnostics.Write("annotation.scan", "SKIP",
                    "label text does not match identity tag; tag=" + number);
                return false;
            }
            numbers.Add(NumberIdentity.Normalize(number));
            return true;
        }

        private static bool TryGetVisibleLabel(dynamic group, out string text)
        {
            text = null;
            try
            {
                dynamic items = group.GroupItems;
                int count = Convert.ToInt32(items.Count);
                for (int index = 1; index <= count; index++)
                {
                    dynamic item = items.Item(index);
                    if (Convert.ToInt32(item.Type) != MsoTextBoxShape) continue;
                    text = Convert.ToString(item.TextFrame.TextRange.Text);
                    return true;
                }
            }
            catch { }
            return false;
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

        /// <summary>
        /// Logical start/end of a native line.
        ///
        /// Measured on PowerPoint 16.0 (tools/probe-r031-rotation-sign.ps1):
        /// Rotation rotates the shape clockwise about its bounding-box centre,
        /// and the arrowhead end moves with it. Drawing a line backwards
        /// stores HorizontalFlip=-1, so a flip already swaps the ends. Rotation
        /// was ignored before, which put the label on the wrong end for any
        /// rotated line.
        /// </summary>
        internal static void GetLineEndpoints(dynamic line, out double startX, out double startY, out double endX, out double endY)
        {
            double left = Convert.ToDouble(line.Left);
            double top = Convert.ToDouble(line.Top);
            double width = Convert.ToDouble(line.Width);
            double height = Convert.ToDouble(line.Height);
            bool flipH = Convert.ToBoolean(line.HorizontalFlip);
            bool flipV = Convert.ToBoolean(line.VerticalFlip);
            double rotation = Convert.ToDouble(line.Rotation);

            // Unrotated endpoints in bounding-box coordinates.
            startX = flipH ? left + width : left;
            startY = flipV ? top + height : top;
            endX = flipH ? left : left + width;
            endY = flipV ? top : top + height;

            if (rotation == 0.0) return;

            double centreX = left + width / 2.0;
            double centreY = top + height / 2.0;
            RotatePoint(ref startX, ref startY, centreX, centreY, rotation);
            RotatePoint(ref endX, ref endY, centreX, centreY, rotation);
        }

        /// <summary>
        /// PowerPoint rotates clockwise on screen, whose y axis points down, so
        /// the standard rotation matrix applies directly with the angle in
        /// degrees. Measured, not assumed: see the probe named above.
        /// </summary>
        private static void RotatePoint(ref double x, ref double y,
            double centreX, double centreY, double degrees)
        {
            double radians = degrees * Math.PI / 180.0;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            double dx = x - centreX;
            double dy = y - centreY;
            x = centreX + dx * cos - dy * sin;
            y = centreY + dx * sin + dy * cos;
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
