using System;
using System.IO;
using System.Xml;

namespace PatentOffice.PowerPoint
{
    internal sealed class BindingResult
    {
        public string Path { get; set; }
        public string Error { get; set; }
        public bool IsBound { get { return !string.IsNullOrEmpty(Path); } }
    }

    internal static class PresentationBinding
    {
        public const string NamespaceUri = "urn:patentmarker:powerpoint:binding:v1";
        private const string RootName = "PatentMarkerBinding";

        public static BindingResult Resolve(dynamic presentation)
        {
            try
            {
                string presentationPath = GetPresentationPath(presentation);
                if (string.IsNullOrEmpty(presentationPath) || !File.Exists(presentationPath))
                    return new BindingResult { Error = "请先保存为 .pptx，再绑定字典。" };

                dynamic parts = presentation.CustomXMLParts;
                dynamic selected = parts.SelectByNamespace(NamespaceUri);
                int count = Convert.ToInt32(selected.Count);
                if (count == 0) return new BindingResult();
                if (count != 1)
                    return new BindingResult { Error = "演示文稿内存在多个字典关联，请重新绑定以修复。" };

                dynamic part = selected.Item(1);
                string xml = Convert.ToString(part.XML);
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                document.LoadXml(xml);
                XmlElement root = document.DocumentElement;
                if (root == null || root.LocalName != RootName || root.NamespaceURI != NamespaceUri)
                    return new BindingResult { Error = "演示文稿中的字典关联格式无法识别，请重新绑定。" };

                string kind = root.GetAttribute("kind");
                if (root.GetAttribute("version") != "1")
                    return new BindingResult { Error = "演示文稿中的字典关联版本无法识别，请重新绑定。" };
                if (!string.Equals(kind, "relative", StringComparison.Ordinal) &&
                    !string.Equals(kind, "absolute", StringComparison.Ordinal))
                    return new BindingResult { Error = "演示文稿中的字典路径类型无法识别，请重新绑定。" };
                string storedPath = root.InnerText;
                if (string.IsNullOrWhiteSpace(storedPath))
                    return new BindingResult { Error = "演示文稿中的字典路径为空，请重新绑定。" };
                if (string.Equals(kind, "relative", StringComparison.Ordinal) == Path.IsPathRooted(storedPath))
                    return new BindingResult { Error = "演示文稿中的字典路径与类型不一致，请重新绑定。" };

                string resolved = string.Equals(kind, "relative", StringComparison.Ordinal)
                    ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(presentationPath), storedPath))
                    : Path.GetFullPath(storedPath);
                if (!File.Exists(resolved))
                    return new BindingResult { Error = "已绑定的字典文件不存在，请重新选择字典。", Path = resolved };
                return new BindingResult { Path = resolved };
            }
            catch (Exception ex)
            {
                return new BindingResult { Error = "读取演示文稿字典关联失败: " + ex.Message };
            }
        }

        public static void Save(dynamic presentation, string dictPath)
        {
            string presentationPath = GetPresentationPath(presentation);
            if (string.IsNullOrEmpty(presentationPath) || !File.Exists(presentationPath))
                throw new InvalidOperationException("请先保存为 .pptx，再绑定字典。");
            if (!File.Exists(dictPath)) throw new FileNotFoundException("字典文件不存在。", dictPath);

            string kind;
            string value = GetStoredPath(presentationPath, Path.GetFullPath(dictPath), out kind);
            XmlDocument document = new XmlDocument();
            document.AppendChild(document.CreateXmlDeclaration("1.0", "utf-8", null));
            XmlElement root = document.CreateElement(RootName, NamespaceUri);
            root.SetAttribute("version", "1");
            root.SetAttribute("kind", kind);
            root.InnerText = value;
            document.AppendChild(root);

            dynamic parts = presentation.CustomXMLParts;
            dynamic existing = parts.SelectByNamespace(NamespaceUri);
            int existingCount = Convert.ToInt32(existing.Count);
            object[] oldParts = new object[existingCount];
            for (int i = 1; i <= existingCount; i++)
                oldParts[i - 1] = existing.Item(i);

            dynamic added = parts.Add(document.OuterXml, Type.Missing);
            try
            {
                for (int i = oldParts.Length - 1; i >= 0; i--)
                    ((dynamic)oldParts[i]).Delete();
            }
            catch
            {
                try { added.Delete(); }
                catch { }
                throw;
            }
        }

        private static string GetPresentationPath(dynamic presentation)
        {
            try
            {
                string fullName = Convert.ToString(presentation.FullName);
                if (string.IsNullOrWhiteSpace(fullName)) return null;
                return Path.GetFullPath(fullName);
            }
            catch { return null; }
        }

        private static string GetStoredPath(string presentationPath, string dictPath, out string kind)
        {
            string presentationRoot = Path.GetPathRoot(presentationPath);
            string dictRoot = Path.GetPathRoot(dictPath);
            if (string.Equals(presentationRoot, dictRoot, StringComparison.OrdinalIgnoreCase))
            {
                Uri baseUri = new Uri(EnsureTrailingSeparator(Path.GetDirectoryName(presentationPath)));
                Uri fileUri = new Uri(dictPath);
                string relative = Uri.UnescapeDataString(baseUri.MakeRelativeUri(fileUri).ToString());
                kind = "relative";
                return relative.Replace('/', Path.DirectorySeparatorChar);
            }
            kind = "absolute";
            return dictPath;
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("演示文稿路径无效。");
            if (path[path.Length - 1] != Path.DirectorySeparatorChar && path[path.Length - 1] != Path.AltDirectorySeparatorChar)
                path += Path.DirectorySeparatorChar;
            return path;
        }
    }
}
