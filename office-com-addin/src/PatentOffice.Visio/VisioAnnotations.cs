using System;
using System.Collections.Generic;
using System.IO;
using PatentMarker.IO;

namespace PatentOffice.Visio
{
    internal sealed class AnnotationScanResult
    {
        public HashSet<string> MarkedNumbers { get; private set; }
        public int MalformedCount { get; set; }

        public AnnotationScanResult()
        {
            MarkedNumbers = new HashSet<string>(NumberIdentity.Comparer);
        }
    }

    internal static class VisioAnnotations
    {
        private const int VisSectionUser = 242;
        private const int VisTypePage = 1;
        private const int FilledArrow = 13;
        private const string ProductValue = "PATMARKER_VISIO";
        private const string ProductRow = "PatentMarkerProduct";
        private const string RoleRow = "PatentMarkerRole";
        private const string IdRow = "PatentMarkerAnnotationId";
        private const string NumberRow = "PatentMarkerNumber";
        private const string VersionRow = "PatentMarkerSchema";

        private sealed class MarkPart
        {
            public string Number;
            public bool IsLine;
            public bool IsLabel;
            public bool IsValid;
        }

        public static void MarkSelectedLine(dynamic application, PatentEntry entry)
        {
            if (entry == null) throw new ArgumentNullException("entry");
            if (string.IsNullOrWhiteSpace(entry.Number)) throw new InvalidOperationException("所选字典条目没有编号。");

            dynamic document = GetActiveDocument(application);
            if (document == null) throw new InvalidOperationException("请先打开 Visio 文档。");
            string documentPath = VisioDocumentBinding.GetDocumentPath(document);
            if (string.IsNullOrEmpty(documentPath) || !File.Exists(documentPath))
                throw new InvalidOperationException("请先保存 Visio 文档，再添加标注。");

            dynamic window = application.ActiveWindow;
            dynamic selection = window.Selection;
            if (Convert.ToInt32(selection.Count) != 1)
                throw new InvalidOperationException("请在当前页面只选中一条 Visio 一维直线或连接线。");

            dynamic line = selection.Item(1);
            if (Convert.ToInt32(line.OneD) == 0)
                throw new InvalidOperationException("所选对象不是 Visio 一维线条。请先画好引线，再选中它。");
            if (HasProductIdentity(line))
                throw new InvalidOperationException("这条线已是产品标注的一部分；如需重新编号，请先编辑现有标注。");

            object containingShape = null;
            try { containingShape = (object)line.ContainingShape; }
            catch { }
            if (containingShape != null)
            {
                int containerType;
                try { containerType = Convert.ToInt32(((dynamic)containingShape).Type); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("无法确认所选线条是否位于组合形状内。", ex);
                }
                if (containerType != VisTypePage)
                    throw new InvalidOperationException("暂不支持组内的线条。请先取消组合，或在页面上绘制独立线条。");
            }

            dynamic page = line.ContainingPage;
            dynamic activePage = application.ActivePage;
            if (Convert.ToInt32(page.ID) != Convert.ToInt32(activePage.ID))
                throw new InvalidOperationException("所选线条不属于当前页面。");
            if (Convert.ToInt32(page.Background) != 0)
                throw new InvalidOperationException("请在前景页面添加标注；背景页面不参与漏标统计。");

            dynamic ownerDocument = page.Document;
            string ownerPath = VisioDocumentBinding.GetDocumentPath(ownerDocument);
            if (!string.Equals(documentPath, ownerPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("所选线条不属于当前 Visio 文档。");

            double beginX = ReadCoordinate(line, "BeginX");
            double beginY = ReadCoordinate(line, "BeginY");
            double endX = ReadCoordinate(line, "EndX");
            double endY = ReadCoordinate(line, "EndY");
            if (Math.Abs(beginX - endX) < 0.000001 && Math.Abs(beginY - endY) < 0.000001)
                throw new InvalidOperationException("所选线条长度为零，不能创建标注。");

            string annotationId = Guid.NewGuid().ToString("N");
            int scope = Convert.ToInt32(application.BeginUndoScope("PatentMarker Visio annotation"));
            bool scopeEnded = false;
            try
            {
                line.CellsU("EndArrow").FormulaU = FilledArrow.ToString();

                double halfWidth = 0.30;
                double halfHeight = 0.16;
                dynamic label = page.DrawRectangle(beginX - halfWidth, beginY - halfHeight,
                    beginX + halfWidth, beginY + halfHeight);
                label.Text = entry.Number;
                label.CellsU("FillPattern").FormulaU = "1";
                label.CellsU("FillForegnd").FormulaU = "RGB(255,255,255)";
                label.CellsU("LinePattern").FormulaU = "0";

                WriteIdentity(line, "leader", annotationId, entry.Number);
                WriteIdentity(label, "label", annotationId, entry.Number);
                document.Saved = false;
                application.EndUndoScope(scope, true);
                scopeEnded = true;
                VisioDiagnostics.Write("annotation.create", "PASS", "doc=" + documentPath +
                    ";page=" + Convert.ToString(page.NameU) + ";shape_id=" + Convert.ToString(line.ID) +
                    ";number=" + entry.Number + ";annotation=" + annotationId);
            }
            catch
            {
                if (!scopeEnded)
                {
                    try { application.EndUndoScope(scope, false); }
                    catch { }
                }
                throw;
            }
        }

        public static AnnotationScanResult ScanDocument(dynamic document)
        {
            AnnotationScanResult result = new AnnotationScanResult();
            dynamic pages = document.Pages;
            int pageCount = Convert.ToInt32(pages.Count);
            for (int pageIndex = 1; pageIndex <= pageCount; pageIndex++)
            {
                dynamic page = pages.Item(pageIndex);
                if (Convert.ToInt32(page.Background) != 0) continue;
                Dictionary<string, List<MarkPart>> partsById =
                    new Dictionary<string, List<MarkPart>>(StringComparer.OrdinalIgnoreCase);
                WalkShapes(page.Shapes, partsById, result, 0);
                AddPageResults(partsById, result);
            }
            return result;
        }

        private static void AddPageResults(Dictionary<string, List<MarkPart>> partsById,
            AnnotationScanResult result)
        {
            foreach (KeyValuePair<string, List<MarkPart>> pair in partsById)
            {
                List<MarkPart> parts = pair.Value;
                bool foundValidPair = false;
                bool containsMalformedPart = false;
                for (int i = 0; i < parts.Count; i++)
                {
                    MarkPart line = parts[i];
                    if (!line.IsValid) containsMalformedPart = true;
                    if (!line.IsLine || !line.IsValid) continue;
                    for (int j = 0; j < parts.Count; j++)
                    {
                        MarkPart label = parts[j];
                        if (!label.IsValid) containsMalformedPart = true;
                        if (label.IsLabel && label.IsValid && NumberIdentity.AreEqual(line.Number, label.Number))
                        {
                            result.MarkedNumbers.Add(NumberIdentity.Normalize(line.Number));
                            foundValidPair = true;
                            break;
                        }
                    }
                }
                if (containsMalformedPart || !foundValidPair) result.MalformedCount++;
            }
        }

        private static void WalkShapes(dynamic shapes, Dictionary<string, List<MarkPart>> partsById,
            AnnotationScanResult result, int depth)
        {
            if (depth > 64) return;
            int count = Convert.ToInt32(shapes.Count);
            for (int index = 1; index <= count; index++)
            {
                dynamic shape = shapes.Item(index);
                string product;
                try { product = ShapeSheetString.Read(shape, ProductRow); }
                catch
                {
                    result.MalformedCount++;
                    product = null;
                }
                if (string.Equals(product, ProductValue, StringComparison.Ordinal))
                {
                    bool isLine = false;
                    bool isLabel = false;
                    string id = null;
                    string number = null;
                    bool valid = false;
                    try
                    {
                        string role = ShapeSheetString.Read(shape, RoleRow);
                        id = ShapeSheetString.Read(shape, IdRow);
                        number = ShapeSheetString.Read(shape, NumberRow);
                        string schema = ShapeSheetString.Read(shape, VersionRow);
                        isLine = string.Equals(role, "leader", StringComparison.Ordinal) && Convert.ToInt32(shape.OneD) != 0;
                        isLabel = string.Equals(role, "label", StringComparison.Ordinal) && Convert.ToInt32(shape.OneD) == 0;
                        valid = (isLine || isLabel) && !string.IsNullOrWhiteSpace(id) &&
                            !string.IsNullOrWhiteSpace(number) && schema == "1";
                        if (isLabel && valid)
                        {
                            string shapeText = Convert.ToString(shape.Text);
                            valid = NumberIdentity.AreEqual(shapeText, number);
                        }
                    }
                    catch { valid = false; }
                    if (string.IsNullOrWhiteSpace(id)) id = "invalid-" + Guid.NewGuid().ToString("N");
                    List<MarkPart> parts;
                    if (!partsById.TryGetValue(id, out parts))
                    {
                        parts = new List<MarkPart>();
                        partsById.Add(id, parts);
                    }
                    parts.Add(new MarkPart { Number = number, IsLine = isLine, IsLabel = isLabel, IsValid = valid });
                }

                try
                {
                    dynamic children = shape.Shapes;
                    if (Convert.ToInt32(children.Count) > 0)
                        WalkShapes(children, partsById, result, depth + 1);
                }
                catch { }
            }
        }

        private static void WriteIdentity(dynamic shape, string role, string id, string number)
        {
            ShapeSheetString.Write(shape, ProductRow, ProductValue, VisSectionUser);
            ShapeSheetString.Write(shape, RoleRow, role, VisSectionUser);
            ShapeSheetString.Write(shape, IdRow, id, VisSectionUser);
            ShapeSheetString.Write(shape, NumberRow, number, VisSectionUser);
            ShapeSheetString.Write(shape, VersionRow, "1", VisSectionUser);
        }

        private static bool HasProductIdentity(dynamic shape)
        {
            try { return string.Equals(ShapeSheetString.Read(shape, ProductRow), ProductValue, StringComparison.Ordinal); }
            catch { return false; }
        }

        private static double ReadCoordinate(dynamic shape, string cellName)
        {
            double value = Convert.ToDouble(shape.CellsU(cellName).ResultIU);
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new InvalidOperationException("所选线条的坐标无法读取。");
            return value;
        }

        public static dynamic GetActiveDocument(dynamic application)
        {
            try
            {
                if (Convert.ToInt32(application.Documents.Count) == 0) return null;
                return application.ActiveDocument;
            }
            catch { return null; }
        }
    }
}
