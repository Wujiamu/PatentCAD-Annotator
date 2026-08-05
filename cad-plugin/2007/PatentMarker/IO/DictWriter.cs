using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PatentMarker.IO
{
    /// <summary>粘贴识别/编辑对话框提交的一行条目（UI → 写回模型的中间类型）</summary>
    public class DictWriteRow
    {
        public string Number = "";
        public string Name = "";
    }

    /// <summary>
    /// dict.json 序列化与写回（v4.0 新增，CAD 端手动编辑的落盘通道，2007/2010 用 SimpleJson 风格手写序列化）。
    /// 输出格式与 Word 端 VBA JsonWriter 保持兼容：
    ///   2 空格缩进、\r\n 行尾、UTF-8 无 BOM、键顺序 metadata→entries→warnings，
    ///   entry 内 number→name→occurrences→conflicts，conflicts 内 number→candidates。
    /// 可选字段（modified_by / modified_at）为 null 时不输出，兼容旧版字典文件。
    /// </summary>
    public static class DictWriter
    {
        /// <summary>
        /// 序列化 DictModel → VBA 兼容 JSON 字符串。
        /// 手写生成（SimpleJson 只解析不序列化）：键顺序固定、2 空格缩进、\r\n 行尾，
        /// 与 VBA JsonWriter.SerializeDict 输出逐字符一致。
        /// </summary>
        public static string Serialize(DictModel model)
        {
            if (model == null)
                throw new ArgumentNullException("model");

            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\r\n  \"metadata\": ");
            AppendMetadata(sb, model.Metadata);
            sb.Append(",\r\n  \"entries\": ");
            AppendEntries(sb, model.Entries);
            sb.Append(",\r\n  \"warnings\": ");
            AppendStringArray(sb, model.Warnings, 1);
            sb.Append("\r\n}");
            return sb.ToString();
        }

        // ===== 手写序列化（VBA JsonWriter 逐字符等价）=====

        /// <summary>metadata 对象（indent=1 → 内容缩进 4 空格；modified_* 为 null 时省略）</summary>
        private static void AppendMetadata(StringBuilder sb, DictMetadata m)
        {
            if (m == null)
            {
                sb.Append("{}");
                return;
            }

            List<string> parts = new List<string>();
            parts.Add(EscapeString(m.SourceFile != null ? m.SourceFile : ""));
            parts.Add(EscapeString(m.ExtractedAt != null ? m.ExtractedAt : ""));
            parts.Add(EscapeString(m.Version != null ? m.Version : ""));

            sb.Append("{\r\n");
            sb.Append("    \"source_file\": ").Append(parts[0]);
            sb.Append(",\r\n    \"extracted_at\": ").Append(parts[1]);
            sb.Append(",\r\n    \"version\": ").Append(parts[2]);
            if (m.ModifiedBy != null)
            {
                sb.Append(",\r\n    \"modified_by\": ").Append(EscapeString(m.ModifiedBy));
                if (m.ModifiedAt != null)
                    sb.Append(",\r\n    \"modified_at\": ").Append(EscapeString(m.ModifiedAt));
            }
            else if (m.ModifiedAt != null)
            {
                sb.Append(",\r\n    \"modified_at\": ").Append(EscapeString(m.ModifiedAt));
            }
            sb.Append("\r\n  }");
        }

        /// <summary>entries 数组（indent=1）</summary>
        private static void AppendEntries(StringBuilder sb, List<DictEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                sb.Append("[]");
                return;
            }

            sb.Append("[");
            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append("\r\n    {");
                DictEntry e = entries[i];
                sb.Append("\r\n      \"number\": ").Append(EscapeString(e.Number != null ? e.Number : ""));
                sb.Append(",\r\n      \"name\": ").Append(EscapeString(e.Name != null ? e.Name : ""));
                sb.Append(",\r\n      \"occurrences\": ").Append(e.Occurrences.ToString());
                sb.Append(",\r\n      \"conflicts\": ");
                AppendConflicts(sb, e.Conflicts);
                sb.Append("\r\n    }");
                if (i < entries.Count - 1) sb.Append(",");
            }
            sb.Append("\r\n  ]");
        }

        /// <summary>conflicts 数组（indent=3）</summary>
        private static void AppendConflicts(StringBuilder sb, List<ConflictInfo> conflicts)
        {
            if (conflicts == null || conflicts.Count == 0)
            {
                sb.Append("[]");
                return;
            }

            sb.Append("[");
            for (int i = 0; i < conflicts.Count; i++)
            {
                ConflictInfo c = conflicts[i];
                sb.Append("\r\n        {");
                sb.Append("\r\n          \"number\": ").Append(EscapeString(c.Number != null ? c.Number : ""));
                sb.Append(",\r\n          \"candidates\": ");
                AppendStringArray(sb, c.Candidates, 4);
                sb.Append("\r\n        }");
                if (i < conflicts.Count - 1) sb.Append(",");
            }
            sb.Append("\r\n      ]");
        }

        /// <summary>字符串数组（indent 为数组所在层级，元素缩进 (indent+1)*2）</summary>
        private static void AppendStringArray(StringBuilder sb, List<string> items, int indent)
        {
            if (items == null || items.Count == 0)
            {
                sb.Append("[]");
                return;
            }

            string innerPad = new string(' ', (indent + 1) * 2);
            string closePad = new string(' ', indent * 2);
            sb.Append("[");
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append("\r\n").Append(innerPad).Append(EscapeString(items[i] != null ? items[i] : ""));
                if (i < items.Count - 1) sb.Append(",");
            }
            sb.Append("\r\n").Append(closePad).Append("]");
        }

        /// <summary>字符串转义（与 VBA EscapeString 一致：\、"、换行→\n、\t）</summary>
        private static string EscapeString(string s)
        {
            if (s == null) s = "";
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\n"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(ch); break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        // ===== 写回模型构造 =====

        /// <summary>
        /// v4.0：构造写回模型。
        /// 覆盖模式：全量替换 entries（预览顺序）；
        /// 合并模式：按 number（忽略大小写）更新已有条目 name、新编号追加尾部，
        /// 保持 JSON 原始顺序；occurrences/conflicts 不动。
        /// metadata 记录 CAD 修改标记（modified_by=CAD、modified_at=当前时间）。
        /// 返回 null 表示 rows 为空。
        /// </summary>
        public static DictModel BuildWriteModel(DictModel current, List<DictWriteRow> rows, bool overwrite)
        {
            if (current == null || rows == null || rows.Count == 0) return null;

            DictModel newModel = new DictModel();
            newModel.Warnings = current.Warnings != null
                ? new List<string>(current.Warnings)
                : new List<string>();
            newModel.Metadata = CloneMetadata(current.Metadata);
            MarkCadModified(newModel);

            if (overwrite)
            {
                foreach (DictWriteRow r in rows)
                {
                    newModel.Entries.Add(new DictEntry { Number = r.Number, Name = r.Name });
                }
                return newModel;
            }

            // 合并：按 number 匹配（忽略大小写，与 BZC 编号比较逻辑一致）
            // 顺序 = 原 JSON 顺序（更新过 name 的条目原地保留）+ 新编号追加尾部
            Dictionary<string, DictEntry> byNumber =
                new Dictionary<string, DictEntry>(StringComparer.OrdinalIgnoreCase);
            List<DictEntry> ordered = new List<DictEntry>();
            if (current.Entries != null)
            {
                foreach (DictEntry e in current.Entries)
                {
                    if (e == null) continue;
                    if (byNumber.ContainsKey(e.Number)) continue;
                    DictEntry copy = CloneEntry(e);
                    byNumber[copy.Number] = copy;
                    ordered.Add(copy);
                }
            }

            foreach (DictWriteRow r in rows)
            {
                DictEntry existing;
                if (byNumber.TryGetValue(r.Number, out existing))
                {
                    if (!string.Equals(existing.Name, r.Name, StringComparison.Ordinal))
                        existing.Name = r.Name;
                }
                else
                {
                    DictEntry ne = new DictEntry { Number = r.Number, Name = r.Name };
                    byNumber[r.Number] = ne;
                    ordered.Add(ne);
                }
            }

            newModel.Entries = ordered;
            return newModel;
        }

        /// <summary>
        /// v4.0：单条目修改/新增（编辑对话框用）。
        /// entry==null 表示新增；否则原地修改 number/name。
        /// 编号冲突（忽略大小写，排除自身）时返回 false 并给出冲突编号。
        /// 成功时自动记录 CAD 修改标记。
        /// </summary>
        public static bool TryApplyEdit(DictModel model, DictEntry entry, string newNumber, string newName,
            out string conflictNumber)
        {
            conflictNumber = null;
            if (model == null) return false;
            string num = newNumber != null ? newNumber.Trim() : "";
            string name = newName != null ? newName.Trim() : "";
            if (num.Length == 0 || name.Length == 0) return false;

            if (model.Entries == null) model.Entries = new List<DictEntry>();
            if (model.Metadata == null) model.Metadata = new DictMetadata();

            foreach (DictEntry e in model.Entries)
            {
                if (e == null) continue;
                if (!object.ReferenceEquals(e, entry) &&
                    string.Equals(e.Number, num, StringComparison.OrdinalIgnoreCase))
                {
                    conflictNumber = e.Number;
                    return false;
                }
            }

            if (entry == null)
            {
                model.Entries.Add(new DictEntry { Number = num, Name = name });
            }
            else
            {
                entry.Number = num;
                entry.Name = name;
            }
            MarkCadModified(model);
            return true;
        }

        /// <summary>
        /// v4.0：删除单条目（编辑对话框用）。成功时自动记录 CAD 修改标记。
        /// </summary>
        public static bool TryRemoveEntry(DictModel model, DictEntry entry)
        {
            if (model == null || entry == null || model.Entries == null) return false;
            if (!model.Entries.Remove(entry)) return false;
            if (model.Metadata == null) model.Metadata = new DictMetadata();
            MarkCadModified(model);
            return true;
        }

        private static void MarkCadModified(DictModel model)
        {
            if (model.Metadata == null) model.Metadata = new DictMetadata();
            model.Metadata.ModifiedBy = "cad";
            model.Metadata.ModifiedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        private static DictMetadata CloneMetadata(DictMetadata source)
        {
            if (source == null) return new DictMetadata();
            return new DictMetadata
            {
                SourceFile = source.SourceFile ?? "",
                ExtractedAt = source.ExtractedAt ?? "",
                Version = source.Version ?? "",
                ModifiedBy = source.ModifiedBy,
                ModifiedAt = source.ModifiedAt
            };
        }

        private static DictEntry CloneEntry(DictEntry source)
        {
            DictEntry copy = new DictEntry
            {
                Number = source.Number ?? "",
                Name = source.Name ?? "",
                Occurrences = source.Occurrences,
                Conflicts = new List<ConflictInfo>()
            };
            if (source.Conflicts != null)
            {
                foreach (ConflictInfo conflict in source.Conflicts)
                {
                    if (conflict == null) continue;
                    copy.Conflicts.Add(new ConflictInfo
                    {
                        Number = conflict.Number ?? "",
                        Candidates = conflict.Candidates != null
                            ? new List<string>(conflict.Candidates)
                            : new List<string>()
                    });
                }
            }
            return copy;
        }

        /// <summary>
        /// 写回 dict.json（UTF-8 无 BOM）。写临时文件后原子替换，
        /// 避免写入中断产生半截 JSON；随后由调用方（DictLoader.NotifySelfWrite）同步缓存。
        /// </summary>
        public static bool Write(string path, DictModel model, out string error)
        {
            error = "";
            try
            {
                string json = Serialize(model);
                string dir = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(dir))
                {
                    error = "路径无目录部分: " + path;
                    return false;
                }
                if (!Directory.Exists(dir))
                {
                    error = "目录不存在: " + dir;
                    return false;
                }

                string tmp = path + ".tmp";
                // 不输出 BOM（与 VBA ADODB.Stream 输出一致）
                File.WriteAllText(tmp, json, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                try { if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp"); } catch { }
                return false;
            }
        }
    }
}
