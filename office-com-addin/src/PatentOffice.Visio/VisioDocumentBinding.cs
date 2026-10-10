using System;
using System.IO;

namespace PatentOffice.Visio
{
    internal sealed class BindingResult
    {
        public string Path { get; set; }
        public string Error { get; set; }
        public bool IsBound { get { return !string.IsNullOrEmpty(Path); } }
    }

    internal static class BindingPath
    {
        public static string ToStoredPath(string documentPath, string dictionaryPath, out string kind)
        {
            string documentFull = System.IO.Path.GetFullPath(documentPath);
            string dictionaryFull = System.IO.Path.GetFullPath(dictionaryPath);
            string documentRoot = System.IO.Path.GetPathRoot(documentFull);
            string dictionaryRoot = System.IO.Path.GetPathRoot(dictionaryFull);
            if (string.Equals(documentRoot, dictionaryRoot, StringComparison.OrdinalIgnoreCase))
            {
                Uri baseUri = new Uri(EnsureTrailingSeparator(System.IO.Path.GetDirectoryName(documentFull)));
                Uri fileUri = new Uri(dictionaryFull);
                string relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString());
                kind = "relative";
                return relative.Replace('/', System.IO.Path.DirectorySeparatorChar);
            }

            kind = "absolute";
            return dictionaryFull;
        }

        public static string Resolve(string documentPath, string storedPath, string kind)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) throw new InvalidDataException("文档中保存的字典路径为空。");
            if (string.Equals(kind, "relative", StringComparison.Ordinal))
                return System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(documentPath), storedPath));
            if (string.Equals(kind, "absolute", StringComparison.Ordinal))
                return System.IO.Path.GetFullPath(storedPath);
            throw new InvalidDataException("文档中的字典路径类型无法识别。");
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Visio 文档路径无效。");
            if (path[path.Length - 1] != System.IO.Path.DirectorySeparatorChar &&
                path[path.Length - 1] != System.IO.Path.AltDirectorySeparatorChar)
                path += System.IO.Path.DirectorySeparatorChar;
            return path;
        }
    }

    internal static class VisioDocumentBinding
    {
        private const int VisSectionUser = 242;
        private const string PathRow = "PatentMarkerDictPath";
        private const string KindRow = "PatentMarkerDictKind";
        private const string VersionRow = "PatentMarkerBindingVersion";

        public static BindingResult Resolve(dynamic document)
        {
            try
            {
                string documentPath = GetDocumentPath(document);
                if (string.IsNullOrEmpty(documentPath) || !File.Exists(documentPath))
                    return new BindingResult { Error = "请先保存 Visio 文档，再绑定字典。" };

                dynamic sheet = document.DocumentSheet;
                string storedPath = ShapeSheetString.Read(sheet, PathRow);
                if (storedPath == null) return new BindingResult();
                string kind = ShapeSheetString.Read(sheet, KindRow);
                string resolved = BindingPath.Resolve(documentPath, storedPath, kind);
                if (!File.Exists(resolved))
                    return new BindingResult { Path = resolved, Error = "关联的字典文件不存在，请重新选择。" };
                return new BindingResult { Path = resolved };
            }
            catch (Exception ex)
            {
                return new BindingResult { Error = "读取 Visio 文档中的字典关联失败：" + ex.Message };
            }
        }

        public static void Save(dynamic document, string dictionaryPath)
        {
            string documentPath = GetDocumentPath(document);
            if (string.IsNullOrEmpty(documentPath) || !File.Exists(documentPath))
                throw new InvalidOperationException("请先保存 Visio 文档，再绑定字典。");
            if (!File.Exists(dictionaryPath)) throw new FileNotFoundException("字典文件不存在。", dictionaryPath);

            string kind;
            string storedPath = BindingPath.ToStoredPath(documentPath, dictionaryPath, out kind);
            int scope = Convert.ToInt32(document.Application.BeginUndoScope("PatentMarker dictionary binding"));
            bool scopeEnded = false;
            try
            {
                dynamic sheet = document.DocumentSheet;
                ShapeSheetString.Write(sheet, PathRow, storedPath, VisSectionUser);
                ShapeSheetString.Write(sheet, KindRow, kind, VisSectionUser);
                ShapeSheetString.Write(sheet, VersionRow, "1", VisSectionUser);
                document.Saved = false;
                document.Application.EndUndoScope(scope, true);
                scopeEnded = true;
            }
            catch
            {
                if (!scopeEnded)
                {
                    try { document.Application.EndUndoScope(scope, false); }
                    catch { }
                }
                throw;
            }
        }

        public static string GetDocumentPath(dynamic document)
        {
            try
            {
                string fullName = Convert.ToString(document.FullName);
                if (string.IsNullOrWhiteSpace(fullName)) return null;
                return System.IO.Path.GetFullPath(fullName);
            }
            catch { return null; }
        }
    }

    internal static class ShapeSheetString
    {
        public static void Write(dynamic shape, string rowName, string value, int section)
        {
            string cellName = "User." + rowName + ".Value";
            if (Convert.ToInt32(shape.CellExistsU(cellName, 0)) == 0)
                shape.AddNamedRow(section, rowName, 0);
            shape.CellsU(cellName).FormulaU = Encode(value);
        }

        public static string Read(dynamic shape, string rowName)
        {
            string cellName = "User." + rowName + ".Value";
            if (Convert.ToInt32(shape.CellExistsU(cellName, 0)) == 0) return null;
            return Decode(Convert.ToString(shape.CellsU(cellName).FormulaU));
        }

        public static string Encode(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        }

        public static string Decode(string formula)
        {
            if (formula == null) return null;
            string value = formula.Trim();
            if (value.StartsWith("=", StringComparison.Ordinal)) value = value.Substring(1).Trim();
            if (value.Length < 2 || value[0] != '"' || value[value.Length - 1] != '"')
                throw new InvalidDataException("ShapeSheet 字符串公式格式无法识别。");
            return value.Substring(1, value.Length - 2).Replace("\"\"", "\"");
        }
    }
}
