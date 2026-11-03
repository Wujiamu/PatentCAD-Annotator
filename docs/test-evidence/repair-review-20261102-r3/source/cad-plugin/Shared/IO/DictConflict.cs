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

        /// <summary>
        /// R-034：恢复选择上下文。
        /// 用户在裁决里比较的是「磁盘上的当前文件」与「所选备份」，
        /// 与面板是否成功解析出当前字典模型无关。身份取自文件真实字节，
        /// 不能取自可能已过期的缓存模型。
        /// </summary>
        public sealed class RecoverySelection
        {
            public string DictPath = "";
            public string BackupPath = "";
            public object Document;
            /// <summary>当前文件的真实字节身份（SHA-256）。</summary>
            public string CurrentIdentity = "";
            /// <summary>所选备份的真实字节身份（SHA-256）。</summary>
            public string BackupIdentity = "";
            /// <summary>当前文件可解析为字典；false 时仍可恢复备份，但不能「采用当前版本」。</summary>
            public bool CurrentParsed;
            /// <summary>所选备份可解析为字典；false 时不得覆盖当前文件。</summary>
            public bool BackupParsed;
        }

        /// <summary>
        /// 一次裁决尝试的结果。区分「什么都没做」与「字典字节已写入但后续步骤失败」，
        /// 让调用者如实报告已发生的写入，而不是把用户留在一个永远过期的快照上重试。
        /// </summary>
        public sealed class RecoveryOutcome
        {
            public bool Succeeded;
            /// <summary>当前字典字节已被备份内容替换。</summary>
            public bool Wrote;
            /// <summary>备份文件已删除。</summary>
            public bool BackupRemoved;
            /// <summary>恢复出的模型；未写入或无法解析时为 null。</summary>
            public DictModel Model;
            public string Error = "";
        }

        /// <summary>当前文件不能解析为字典，因此不能「采用当前版本」。</summary>
        public const string CurrentUnparsableError = "Current dictionary cannot be parsed.";
        /// <summary>所选备份不能解析为字典，因此不能覆盖当前文件。</summary>
        public const string BackupUnparsableError = "Backup dictionary cannot be parsed.";

        /// <summary>
        /// 按磁盘真实字节抓取恢复选择上下文。当前文件损坏时仍然返回上下文
        /// （<see cref="RecoverySelection.CurrentParsed"/> 为 false），
        /// 这样「从合法备份恢复」不再依赖当前模型是否解析成功。
        /// 备份缺失或读取失败时返回 null。
        /// </summary>
        public static RecoverySelection CaptureSelection(string dictPath, string backupPath)
        {
            if (string.IsNullOrEmpty(dictPath) || string.IsNullOrEmpty(backupPath)) return null;
            RecoverySelection selection = new RecoverySelection();
            selection.DictPath = dictPath;
            selection.BackupPath = backupPath;
            byte[] current, backup;
            try
            {
                current = File.ReadAllBytes(dictPath);
                backup = File.ReadAllBytes(backupPath);
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            selection.CurrentIdentity = DictFileVisibility.Identity(current);
            selection.BackupIdentity = DictFileVisibility.Identity(backup);
            selection.CurrentParsed = DictLoader.LoadBytes(dictPath, current) != null;
            selection.BackupParsed = DictLoader.LoadBytes(backupPath, backup) != null;
            return selection;
        }

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
            RecoveryOutcome outcome = KeepSelectedVersion(SelectionOf(dictPath, identity, backupPath, backupIdentity), isCurrent);
            error = outcome.Error;
            return outcome.Succeeded;
        }

        public static DictModel ResolveRestoreCad(string dictPath, string identity, string backupPath, string backupIdentity,
            ContextCheck isCurrent, out string error)
        {
            RecoveryOutcome outcome = RestoreSelectedBackup(SelectionOf(dictPath, identity, backupPath, backupIdentity), isCurrent);
            error = outcome.Error;
            return outcome.Succeeded ? outcome.Model : null;
        }

        private static RecoverySelection SelectionOf(string dictPath, string identity, string backupPath, string backupIdentity)
        {
            RecoverySelection selection = new RecoverySelection();
            selection.DictPath = dictPath;
            selection.BackupPath = backupPath;
            selection.CurrentIdentity = identity;
            selection.BackupIdentity = backupIdentity;
            return selection;
        }

        /// <summary>
        /// R-034：采用当前（Word）版本。与恢复相反，这一步把当前文件认定为
        /// 权威内容，所以当前文件必须仍是可解析字典；当前文件损坏时只能走恢复。
        /// </summary>
        public static RecoveryOutcome KeepSelectedVersion(RecoverySelection selection, ContextCheck isCurrent)
        {
            RecoveryOutcome outcome = new RecoveryOutcome();
            outcome.Error = "";
            if (selection == null || string.IsNullOrEmpty(selection.DictPath))
            {
                outcome.Error = DictFileVisibility.ChangedError;
                return outcome;
            }
            string dictPath = selection.DictPath;
            string backupPath = selection.BackupPath;
            try
            {
                using (FileStream held = DictFileVisibility.AcquireLock(dictPath, out outcome.Error))
                {
                    if (held == null) return outcome;
                    byte[] current, backup;
                    if (!ReadSelected(dictPath, selection.CurrentIdentity, backupPath, selection.BackupIdentity,
                        isCurrent, out current, out backup, out outcome.Error)) return outcome;
                    if (DictLoader.LoadBytes(dictPath, current) == null)
                    {
                        outcome.Error = CurrentUnparsableError;
                        return outcome;
                    }
                    if (!isCurrent()) { outcome.Error = DictFileVisibility.ChangedError; return outcome; }
                    string detail;
                    if (!DictFileVisibility.TryWriteBaseline(dictPath, DictFileVisibility.DecodeText(current), out detail))
                    {
                        outcome.Error = "Baseline update failed; backup retained: " + detail;
                        return outcome;
                    }
                    if (!ReadSelected(dictPath, selection.CurrentIdentity, backupPath, selection.BackupIdentity,
                        isCurrent, out current, out backup, out outcome.Error)) return outcome;
                    DeleteBackup(backupPath);
                    outcome.BackupRemoved = true;
                    outcome.Succeeded = true;
                    return outcome;
                }
            }
            catch (Exception ex) { outcome.Error = ex.GetType().Name + ": " + ex.Message; return outcome; }
        }

        /// <summary>
        /// R-034：从所选备份恢复。当前文件是否可解析不参与判断——损坏的当前
        /// 文件正是本入口要救回的情形；备份自身不可解析仍然拒绝覆盖。
        /// </summary>
        public static RecoveryOutcome RestoreSelectedBackup(RecoverySelection selection, ContextCheck isCurrent)
        {
            RecoveryOutcome outcome = new RecoveryOutcome();
            outcome.Error = "";
            if (selection == null || string.IsNullOrEmpty(selection.DictPath))
            {
                outcome.Error = DictFileVisibility.ChangedError;
                return outcome;
            }
            string dictPath = selection.DictPath;
            string backupPath = selection.BackupPath;
            try
            {
                using (FileStream held = DictFileVisibility.AcquireLock(dictPath, out outcome.Error))
                {
                    if (held == null) return outcome;
                    byte[] current, backup;
                    if (!ReadSelected(dictPath, selection.CurrentIdentity, backupPath, selection.BackupIdentity,
                        isCurrent, out current, out backup, out outcome.Error)) return outcome;
                    DictModel restored = DictLoader.LoadBytes(backupPath, backup);
                    if (restored == null) { outcome.Error = BackupUnparsableError; return outcome; }
                    if (restored.Metadata == null) restored.Metadata = new DictMetadata();
                    restored.Metadata.ModifiedBy = null;
                    restored.Metadata.ModifiedAt = null;
                    string json = DictWriter.Serialize(restored);
                    if (!ReadSelected(dictPath, selection.CurrentIdentity, backupPath, selection.BackupIdentity,
                        isCurrent, out current, out backup, out outcome.Error)) return outcome;
                    if (!DictFileVisibility.TryWriteAtomic(dictPath, json, out outcome.Error)) return outcome;
                    outcome.Wrote = true;
                    outcome.Model = restored;
                    string newIdentity = DictFileVisibility.Identity(new System.Text.UTF8Encoding(false).GetBytes(json));
                    DictLoader.NotifySelfWrite(restored, dictPath, newIdentity);
                    string detail;
                    if (!DictFileVisibility.TryWriteBaseline(dictPath, json, out detail))
                    {
                        outcome.Error = DictFileVisibility.BaselineError;
                        return outcome;
                    }
                    if (!isCurrent() || !string.Equals(selection.BackupIdentity, DictFileVisibility.ReadIdentity(backupPath), StringComparison.Ordinal))
                    {
                        outcome.Error = "Dictionary was restored; changed backup was retained.";
                        return outcome;
                    }
                    DeleteBackup(backupPath);
                    outcome.BackupRemoved = true;
                    outcome.Succeeded = true;
                    return outcome;
                }
            }
            catch (Exception ex)
            {
                outcome.Error = (outcome.Wrote ? "Dictionary was restored; backup retained: " : "") + ex.GetType().Name + ": " + ex.Message;
                return outcome;
            }
        }
    }
}
