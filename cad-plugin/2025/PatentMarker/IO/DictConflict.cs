using System;
using System.IO;
using System.Text.RegularExpressions;

namespace PatentMarker.IO
{
    /// <summary>
    /// v4.0 功能 3：双端冲突检测与裁决。
    /// Word 端（AutoExport.bas）在覆盖 CAD 修改前把旧文件备份为
    /// <主名>.dict.json.word-<yyyymmdd-hhnnss>.bak（只保留最新一个）。
    /// CAD 端据此检测「Word 已覆盖 CAD 修改」并触发人工裁决：
    ///   采用 Word 版（删备份）/ 恢复 CAD 版（备份覆盖回 + 删备份 + 清 CAD 标记）/ 稍后再说。
    /// 裁决动作均在此类实现（文件操作），对话框只做 UI。
    /// </summary>
    public static class DictConflict
    {
        /// <summary>备份文件名中缀（VBA 与 CAD 共用约定）</summary>
        public const string BackupInfix = ".word-";

        /// <summary>
        /// 查找 dictPath 对应目录下最新的 Word 备份文件（按文件名时间戳排序，
        /// 格式固定为 yyyymmdd-hhnnss，字符串比较即时间比较）。无则返回 null。
        /// </summary>
        public static string? FindWordBackup(string dictPath)
        {
            if (string.IsNullOrEmpty(dictPath)) return null;
            string? dir = Path.GetDirectoryName(dictPath);
            if (dir == null || !Directory.Exists(dir)) return null;
            string prefix = Path.GetFileName(dictPath) + BackupInfix;

            string? latest = null;
            foreach (string f in Directory.GetFiles(dir, "*.bak"))
            {
                string name = Path.GetFileName(f);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                // 校验时间戳段基本形态（避免误匹配其他 .bak）
                string ts = name.Substring(prefix.Length);
                if (!Regex.IsMatch(ts, @"^\d{8}-\d{6}\.bak$", RegexOptions.IgnoreCase)) continue;
                if (latest == null ||
                    string.Compare(name, Path.GetFileName(latest), StringComparison.Ordinal) > 0)
                    latest = f;
            }
            return latest;
        }

        /// <summary>
        /// 冲突待裁决状态：目录中存在 Word 备份 且 当前 dict.json 无 CAD 标记。
        /// （Word 覆盖 CAD 修改后导出的文件不带 modified_by；若当前 JSON 仍有 CAD 标记，
        ///   说明 CAD 版本仍是最新，无需裁决。）
        /// </summary>
        public static bool IsPendingConflict(DictModel? current, string? dictPath)
        {
            if (string.IsNullOrEmpty(dictPath)) return false;
            if (FindWordBackup(dictPath) == null) return false;
            if (current != null && current.Metadata != null && current.Metadata.ModifiedBy == "cad")
                return false;
            return true;
        }

        /// <summary>
        /// 裁决「采用 Word 版」：删除备份（当前 dict.json 即 Word 最新导出，保持不变）。
        /// </summary>
        public static bool ResolveKeepWord(string backupPath, out string error)
        {
            error = "";
            try
            {
                if (File.Exists(backupPath)) File.Delete(backupPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 裁决「恢复 CAD 版」：备份内容覆盖回 dict.json → 清除 CAD 修改标记（防循环提示）
        /// → 删除备份。返回恢复后的模型（供缓存同步），失败返回 null。
        /// </summary>
        public static DictModel? ResolveRestoreCad(string dictPath, string backupPath, out string error)
        {
            error = "";
            try
            {
                if (!File.Exists(backupPath))
                {
                    error = "备份文件不存在: " + backupPath;
                    return null;
                }
                // 先解析验证备份，再写回当前字典，避免损坏备份先覆盖掉可用字典。

                // 清除 CAD 标记（Q8：裁决后清理标记防循环）
                DictModel? restored = DictLoader.Load(backupPath);
                if (restored == null)
                {
                    error = "备份文件无法解析: " + backupPath;
                    return null;
                }
                if (restored != null)
                {
                    restored.Metadata ??= new DictMetadata();
                    restored.Metadata.ModifiedBy = null;
                    restored.Metadata.ModifiedAt = null;
                    if (!DictWriter.Write(dictPath, restored, out error))
                        return null;
                }

                if (File.Exists(backupPath)) File.Delete(backupPath);
                return restored;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }
    }
}
