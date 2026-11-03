using System;
using System.IO;
using System.Text.RegularExpressions;

namespace PatentMarker.IO
{
    /// <summary>
    /// v4.0 功能 3：双端冲突检测与裁决。
    /// Word 端（AutoExport.bas）在覆盖 CAD 修改前把旧文件备份为
    /// <主名>.dict.json.word-<yyyymmdd-hhnnss>.bak；未裁决备份保留。
    /// CAD 端据此检测「Word 已覆盖 CAD 修改」并触发人工裁决：
    ///   采用 Word 版（删备份）/ 恢复 CAD 版（备份覆盖回 + 删备份 + 清 CAD 标记）/ 稍后再说。
    /// 裁决动作均在此类实现（文件操作），对话框只做 UI。
    /// </summary>
    public static class DictConflict
    {
        private static void DeleteBackup(string path)
        {
            if (!File.Exists(path)) return;
            FileAttributes original = File.GetAttributes(path);
            try
            {
                // Delete accepts hidden/system files. Only ReadOnly needs
                // temporary removal, and failed cleanup must restore it.
                if ((original & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, original & ~FileAttributes.ReadOnly);
                File.Delete(path);
            }
            catch (Exception deletion)
            {
                try { if (File.Exists(path)) File.SetAttributes(path, original); }
                catch (Exception restoration)
                {
                    throw new IOException(deletion.Message + "; backup attributes could not be restored: " + restoration.Message, deletion);
                }
                throw;
            }
        }
        /// <summary>备份文件名中缀（VBA 与 CAD 共用约定）</summary>
        public const string BackupInfix = ".word-";

        /// <summary>
        /// 查找 dictPath 对应目录下最新的 Word 备份文件（按文件名时间戳排序，
        /// 格式固定为 yyyymmdd-hhnnss，字符串比较即时间比较）。无则返回 null。
        /// </summary>
        public static string FindWordBackup(string dictPath)
        {
            if (string.IsNullOrEmpty(dictPath)) return null;
            string dir = Path.GetDirectoryName(dictPath);
            if (dir == null || !Directory.Exists(dir)) return null;
            string prefix = Path.GetFileName(dictPath) + BackupInfix;

            string latest = null;
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

        public static bool IsPendingConflict(DictModel current, string dictPath)
        {
            return !string.IsNullOrEmpty(dictPath) && FindWordBackup(dictPath) != null;
        }

        public delegate bool ContextCheck();

        private static bool IsSelectedBackup(string dictPath, string backupPath)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(dictPath));
            string backupDirectory = Path.GetDirectoryName(Path.GetFullPath(backupPath));
            if (!string.Equals(directory, backupDirectory, StringComparison.OrdinalIgnoreCase)) return false;
            string name = Path.GetFileName(backupPath);
            string prefix = Path.GetFileName(dictPath) + BackupInfix;
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(name.Substring(prefix.Length), @"^\d{8}-\d{6}\.bak$", RegexOptions.IgnoreCase);
        }

        private static bool ReadSelected(string dictPath, string identity, string backupPath, string backupIdentity,
            ContextCheck isCurrent, out byte[] current, out byte[] backup, out string error)
        {
            current = null; backup = null; error = "";
            if (string.IsNullOrEmpty(dictPath) || string.IsNullOrEmpty(backupPath) ||
                string.IsNullOrEmpty(identity) || string.IsNullOrEmpty(backupIdentity) ||
                isCurrent == null || !isCurrent() || !IsSelectedBackup(dictPath, backupPath))
            {
                error = DictFileVisibility.ChangedError;
                return false;
            }
            try
            {
                current = File.ReadAllBytes(dictPath);
                backup = File.ReadAllBytes(backupPath);
            }
            catch (FileNotFoundException) { error = DictFileVisibility.ChangedError; return false; }
            catch (DirectoryNotFoundException) { error = DictFileVisibility.ChangedError; return false; }
            if (!string.Equals(identity, DictFileVisibility.Identity(current), StringComparison.Ordinal) ||
                !string.Equals(backupIdentity, DictFileVisibility.Identity(backup), StringComparison.Ordinal))
            {
                error = DictFileVisibility.ChangedError;
                return false;
            }
            return true;
        }

        // Compatibility entry points cannot establish the original identities.
        public static bool ResolveKeepWord(string backupPath, out string error)
        {
            error = DictFileVisibility.ChangedError;
            return false;
        }

        public static DictModel ResolveRestoreCad(string dictPath, string backupPath, out string error)
        {
            error = DictFileVisibility.ChangedError;
            return null;
        }

        public static bool ResolveKeepWord(string dictPath, string identity, string backupPath, string backupIdentity,
            ContextCheck isCurrent, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(dictPath)) { error = DictFileVisibility.ChangedError; return false; }
            try
            {
                using (FileStream held = DictFileVisibility.AcquireLock(dictPath, out error))
                {
                    if (held == null) return false;
                    byte[] current, backup;
                    if (!ReadSelected(dictPath, identity, backupPath, backupIdentity, isCurrent, out current, out backup, out error)) return false;
                    if (DictLoader.LoadBytes(dictPath, current) == null)
                    {
                        error = "Current dictionary cannot be parsed.";
                        return false;
                    }
                    if (!isCurrent()) { error = DictFileVisibility.ChangedError; return false; }
                    string detail;
                    if (!DictFileVisibility.TryWriteBaseline(dictPath, DictFileVisibility.DecodeText(current), out detail))
                    {
                        error = "Baseline update failed; backup retained: " + detail;
                        return false;
                    }
                    if (!ReadSelected(dictPath, identity, backupPath, backupIdentity, isCurrent, out current, out backup, out error)) return false;
                    DeleteBackup(backupPath);
                    return true;
                }
            }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return false; }
        }

        public static DictModel ResolveRestoreCad(string dictPath, string identity, string backupPath, string backupIdentity,
            ContextCheck isCurrent, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(dictPath)) { error = DictFileVisibility.ChangedError; return null; }
            bool wrote = false;
            try
            {
                using (FileStream held = DictFileVisibility.AcquireLock(dictPath, out error))
                {
                    if (held == null) return null;
                    byte[] current, backup;
                    if (!ReadSelected(dictPath, identity, backupPath, backupIdentity, isCurrent, out current, out backup, out error)) return null;
                    DictModel restored = DictLoader.LoadBytes(backupPath, backup);
                    if (restored == null) { error = "Backup dictionary cannot be parsed."; return null; }
                    if (restored.Metadata == null) restored.Metadata = new DictMetadata();
                    restored.Metadata.ModifiedBy = null;
                    restored.Metadata.ModifiedAt = null;
                    string json = DictWriter.Serialize(restored);
                    if (!ReadSelected(dictPath, identity, backupPath, backupIdentity, isCurrent, out current, out backup, out error)) return null;
                    if (!DictFileVisibility.TryWriteAtomic(dictPath, json, out error)) return null;
                    wrote = true;
                    string newIdentity = DictFileVisibility.Identity(new System.Text.UTF8Encoding(false).GetBytes(json));
                    DictLoader.NotifySelfWrite(restored, dictPath, newIdentity);
                    string detail;
                    if (!DictFileVisibility.TryWriteBaseline(dictPath, json, out detail))
                    {
                        error = DictFileVisibility.BaselineError;
                        return null;
                    }
                    if (!isCurrent() || !string.Equals(backupIdentity, DictFileVisibility.ReadIdentity(backupPath), StringComparison.Ordinal))
                    {
                        error = "Dictionary was restored; changed backup was retained.";
                        return null;
                    }
                    DeleteBackup(backupPath);
                    return restored;
                }
            }
            catch (Exception ex)
            {
                error = (wrote ? "Dictionary was restored; backup retained: " : "") + ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }
    }
}
