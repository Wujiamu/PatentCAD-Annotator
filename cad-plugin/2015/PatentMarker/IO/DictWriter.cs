using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace PatentMarker.IO
{
    /// <summary>粘贴识别/编辑对话框提交的一行条目（UI → 写回模型的中间类型）</summary>
    public class DictWriteRow
    {
        public string Number = "";
        public string Name = "";
    }

    /// <summary>
    /// dict.json 序列化与写回（v4.0 新增，CAD 端手动编辑的落盘通道，2013/2015 用 Newtonsoft.Json）。
    /// 输出格式与 Word 端 VBA JsonWriter 保持兼容：
    ///   2 空格缩进、\r\n 行尾、UTF-8 无 BOM、键顺序 metadata→entries→warnings，
    ///   entry 内 number→name→occurrences→conflicts，conflicts 内 number→candidates。
    /// 可选字段（modified_by / modified_at）为 null 时不输出，兼容旧版字典文件。
    /// </summary>
    public static class DictWriter
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            // VBA 输出原始 UTF-8 中文，Newtonsoft 默认不转义非 ASCII
            NullValueHandling = NullValueHandling.Ignore
        };

        /// <summary>
        /// 序列化 DictModel → VBA 兼容 JSON 字符串。
        /// 行尾统一为 \r\n（与 VBA 字符串拼接输出一致，避免运行时差异产生 \r\r\n）。
        /// </summary>
        public static string Serialize(DictModel model)
        {
            if (model == null)
                throw new ArgumentNullException("model");

            string json = JsonConvert.SerializeObject(model, JsonSettings);
            return Regex.Replace(json, "\r\n|\r|\n", "\r\n");
        }

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
                    newModel.Entries.Add(new DictEntry
                    {
                        Number = NumberIdentity.Normalize(r.Number),
                        Name = r.Name
                    });
                }
                return newModel;
            }

            // 合并：按 number 匹配（忽略大小写，与 BZC 编号比较逻辑一致）
            // 顺序 = 原 JSON 顺序（更新过 name 的条目原地保留）+ 新编号追加尾部
            Dictionary<string, DictEntry> byNumber =
                new Dictionary<string, DictEntry>(NumberIdentity.Comparer);
            List<DictEntry> ordered = new List<DictEntry>();
            if (current.Entries != null)
            {
                foreach (DictEntry e in current.Entries)
                {
                    if (e == null) continue;
                    DictEntry copy = CloneEntry(e);
                    copy.Number = NumberIdentity.Normalize(copy.Number);
                    if (byNumber.ContainsKey(copy.Number)) continue;
                    byNumber[copy.Number] = copy;
                    ordered.Add(copy);
                }
            }

            foreach (DictWriteRow r in rows)
            {
                string number = NumberIdentity.Normalize(r.Number);
                DictEntry existing;
                if (byNumber.TryGetValue(number, out existing))
                {
                    if (!string.Equals(existing.Name, r.Name, StringComparison.Ordinal))
                        existing.Name = r.Name;
                }
                else
                {
                    DictEntry ne = new DictEntry { Number = number, Name = r.Name };
                    byNumber[number] = ne;
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
                if (!ReferenceEquals(e, entry) &&
                    NumberIdentity.AreEqual(e.Number, num))
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
