using PatentMarker.IO;
using PatentMarker.I18n;
using System;
using System.IO;

namespace PatentMarker.Palette
{
    /// <summary>
    /// Dictionary and conflict lifecycle used by the palette.  It keeps the
    /// cache/path facade in one place so the WinForms control does not mix
    /// global loader state with view event handling.
    /// </summary>
    public sealed class DictPaletteWorkflow
    {
        public DictModel PreviousModel
        {
            get { return DictLoader.PreviousModel; }
        }

        public bool IsFileChanged()
        {
            return DictLoader.IsFileChanged();
        }

        public DictModel LoadCurrent()
        {
            return DictLoader.LoadForCurrentDrawing();
        }

        public DictModel ReloadCurrent()
        {
            DictLoader.InvalidateCache();
            return DictLoader.LoadForCurrentDrawing();
        }

        public string ResolveDictPath()
        {
            string path = DictLoader.CurrentPath;
            // A cached path can outlive a deleted/renamed dictionary.  Fall
            // back to the loader's canonical resolver so every palette action
            // sees the same current-drawing/configured path.
            return path != null && File.Exists(path) ? path : DictLoader.ResolveDictPath();
        }

        public static bool IsCurrentContext(object document, string path)
        {
            if (document == null || !object.ReferenceEquals(document, RuntimeHost.ActiveDocument)) return false;
            string current = DictLoader.ResolveDictPath();
            if (string.IsNullOrEmpty(current) || string.IsNullOrEmpty(path)) return false;
            try { return string.Equals(Path.GetFullPath(path), Path.GetFullPath(current), StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        }

        public static string DescribeWriteError(string error)
        {
            if (error == DictFileVisibility.BusyError) return Strings.Dict_Busy;
            if (error == DictFileVisibility.ChangedError) return Strings.Dict_Changed;
            if (error == DictFileVisibility.PendingError) return Strings.Dict_Pending;
            if (error == DictFileVisibility.BaselineError) return Strings.Dict_BaselineFailed;
            return error;
        }

        public bool IsDictVisible(string path)
        {
            return DictFileVisibility.IsVisible(path);
        }

        public bool TrySetDictVisible(string path, bool visible, out string error)
        {
            return DictFileVisibility.TrySetVisible(path, visible, out error);
        }

        public bool IsPendingConflict(DictModel current, string dictPath)
        {
            return DictConflict.IsPendingConflict(current, dictPath);
        }

        /// <summary>
        /// R-034：只按磁盘状态判断是否存在待裁决备份。
        /// 当前字典是否解析成功不影响「有备份待处理」这个事实，
        /// 因此损坏的当前文件仍然可以让裁决入口保持可用。
        /// </summary>
        public bool IsPendingConflict(string dictPath)
        {
            return DictConflict.IsPendingConflict(null, dictPath);
        }

        /// <summary>
        /// R-034：抓取恢复选择上下文。当前文件损坏时仍然返回上下文，
        /// 这样「从合法备份恢复」不再依赖 <see cref="LoadCurrent"/> 是否成功。
        /// 没有待裁决备份或备份不可读时返回 null。
        /// </summary>
        public DictConflict.RecoverySelection CaptureRecoverySelection(string dictPath)
        {
            if (string.IsNullOrEmpty(dictPath)) return null;
            string backupPath = DictConflict.FindWordBackup(dictPath);
            if (backupPath == null) return null;
            DictConflict.RecoverySelection selection = DictConflict.CaptureSelection(dictPath, backupPath);
            if (selection == null) return null;
            selection.Document = RuntimeHost.ActiveDocument;
            return selection;
        }

        /// <summary>上下文检查绑定到本次恢复选择所对应的文档与路径。</summary>
        public static DictConflict.ContextCheck ContextGuard(DictConflict.RecoverySelection selection)
        {
            if (selection == null) return null;
            object document = selection.Document;
            string dictPath = selection.DictPath;
            return delegate { return IsCurrentContext(document, dictPath); };
        }

        public DictConflict.RecoveryOutcome KeepSelectedVersion(DictConflict.RecoverySelection selection)
        {
            return DictConflict.KeepSelectedVersion(selection, ContextGuard(selection));
        }

        public DictConflict.RecoveryOutcome RestoreSelectedBackup(DictConflict.RecoverySelection selection)
        {
            return DictConflict.RestoreSelectedBackup(selection, ContextGuard(selection));
        }

        /// <summary>
        /// 恢复写入后刷新缓存。NotifySelfWrite 已登记新身份，这里只在缓存仍
        /// 与磁盘一致时返回模型，避免把过期模型重新挂到面板上。
        /// </summary>
        public DictModel ReloadAfterRecovery(string dictPath, DictModel restored)
        {
            if (restored == null) return ReloadCurrent();
            if (!string.Equals(DictLoader.CurrentPath, dictPath, StringComparison.OrdinalIgnoreCase))
                DictLoader.InvalidateCache();
            DictModel current = LoadCurrent();
            return current != null ? current : restored;
        }

        public DictEntry FindEntry(DictModel dict, string number)
        {
            if (dict == null || dict.Entries == null) return null;
            foreach (DictEntry entry in dict.Entries)
            {
                if (NumberIdentity.AreEqual(entry.Number, number)) return entry;
            }
            return null;
        }
    }
}
