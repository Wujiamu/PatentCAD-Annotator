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

        /// <summary>
        /// R-004（G24）：当前图纸的规范字典绑定——与 <see cref="IsCurrentContext"/>
        /// 完全同一套解析约定（活动文档 + <c>DictLoader.ResolveDictPath()</c> 的
        /// 当前解析结果），但明确返回那个路径，供调用方绑定「这份字节属于哪本字典」。
        /// 解析不到时返回 null。不使用面板缓存路径：缓存里的路径可能已经不再属于
        /// 当前图纸（另存为、默认路径切换、目录里残留旧文件）。
        /// </summary>
        public static string ResolveCanonicalPath(object document)
        {
            if (document == null || !object.ReferenceEquals(document, RuntimeHost.ActiveDocument)) return null;
            string current = DictLoader.ResolveDictPath();
            if (string.IsNullOrEmpty(current)) return null;
            try { return Path.GetFullPath(current); }
            catch { return current; }
        }

        /// <summary>
        /// R-004（G24）：从**同一份实际字节**读出当前字典并严格解析。
        ///
        /// 一次 <c>ReadAllBytes</c> 同时产生身份和模型，两者必然对应同一份内容；
        /// 不经过 <see cref="DictLoader"/> 的按路径缓存，因此热刷新后的旧模型、
        /// 损坏文件上残留的旧模型都不会被借来判定「还有没有新号」。
        /// </summary>
        public RenameSnapshotRead ReadSnapshot(string dictPath)
        {
            RenameSnapshotRead read = new RenameSnapshotRead();
            read.DictPath = string.IsNullOrEmpty(dictPath) ? "" : dictPath;
            if (string.IsNullOrEmpty(dictPath))
            {
                read.Error = "no dictionary path";
                return read;
            }
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(dictPath);
            }
            catch (Exception ex)
            {
                read.Error = ex.GetType().Name + ": " + ex.Message;
                return read;
            }
            read.Read = true;
            read.DictIdentity = DictFileVisibility.Identity(bytes);
            // Parse the bytes we just hashed. Loading by path re-reads the file,
            // so a rewrite between hash and parse yields one file's identity
            // with another file's model.
            DictModel model = DictLoader.LoadBytes(dictPath, bytes);
            if (model == null)
            {
                read.Error = "PATENTMARKER_DICT_UNPARSABLE";
                return read;
            }
            read.Parsed = true;
            read.Model = model;
            return read;
        }

        /// <summary>
        /// R-004（G24）：读当前图纸规范绑定的字典，并核对这份字节确实属于它。
        /// 规范路径解析不到、读取失败或解析失败都返回未就绪的读取结果，
        /// 由调用方保留任务、不调用 CAD。
        /// </summary>
        public RenameSnapshotRead ReadCanonicalSnapshot(object document)
        {
            string canonical = ResolveCanonicalPath(document);
            if (canonical == null)
            {
                RenameSnapshotRead missing = new RenameSnapshotRead();
                missing.Error = "PATENTMARKER_NO_CANONICAL_DICT";
                return missing;
            }
            return ReadSnapshot(canonical);
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

        /// <summary>
        /// R-034（G20）：裁决的两个动作。可用性按动作分别判断，
        /// 不能用一个动作的前置条件否决另一个动作，更不能否决整个对话框。
        /// </summary>
        public const string KeepWordAction = "keep-word";
        public const string RestoreCadAction = "restore-cad";

        /// <summary>
        /// R-034（G20）：某个裁决动作在当前选择下是否可用。
        /// 「采用 Word 版」由当前文件能否严格解析约束（把坏文件认定为权威内容没有意义）；
        /// 「恢复 CAD 版」由所选备份能否严格解析约束（损坏备份不得覆盖好当前）。
        /// 两者互不影响，也不决定对话框能否打开。
        /// </summary>
        public static bool IsActionAvailable(DictConflict.RecoverySelection selection, string action)
        {
            if (selection == null) return false;
            if (action == KeepWordAction) return selection.CurrentParsed;
            if (action == RestoreCadAction) return selection.BackupParsed;
            return false;
        }

        /// <summary>
        /// R-034（G20）：说明哪些动作不可用、为什么。两个动作都可用时返回空串。
        /// 原因文本复用产品里已有的两个解析失败常量（与「裁决失败: …」同一措辞），
        /// 本轮不改 Strings.cs，因此不自造新的本地化文案。
        /// </summary>
        public static string DescribeUnavailableActions(DictConflict.RecoverySelection selection)
        {
            if (selection == null) return "";
            bool keep = selection.CurrentParsed;
            bool restore = selection.BackupParsed;
            if (keep && restore) return "";
            string text = "";
            if (!keep) text = Strings.Conflict_BtnKeepWord + ": " + DictConflict.CurrentUnparsableError;
            if (!restore)
            {
                if (text.Length > 0) text = text + "\n";
                text = text + Strings.Conflict_BtnRestoreCad + ": " + DictConflict.BackupUnparsableError;
            }
            return text;
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

        /// <summary>
        /// R-034（G25）：一次裁决会话的累积事实。
        ///
        /// 单次 <see cref="DictConflict.RecoveryOutcome"/> 会被下一次点击**覆盖**：
        /// 第一次已经把备份内容写进当前字典（Wrote=true），用户再点一次通常只会得到
        /// 「内容已变化」的拒绝（Wrote=false），先前真实发生的写入就从单次结果里消失。
        /// 因此「这次裁决到底有没有写过盘」不能由最后一次返回值回答，只能由这里
        /// 只追加、不覆盖地累积。它只活在对话框实例里，不写盘、不跨会话。
        /// </summary>
        public sealed class ArbitrationSessionFact
        {
            /// <summary>用户在本对话框里点击裁决动作的次数（「稍后」不计）。</summary>
            public int Attempts;
            /// <summary>至少有一次尝试真的把备份内容写到了当前字典。</summary>
            public bool AnyWrote;
            /// <summary>至少有一次尝试完整成功（写入 + 基线 + 备份清理）。</summary>
            public bool AnySucceeded;
            /// <summary>至少有一次尝试删掉了备份文件。</summary>
            public bool AnyBackupRemoved;
            /// <summary>第一次「写了盘却没完成」的尝试的错误；之后不再改写。</summary>
            public string WriteError = "";
            /// <summary>最后一次尝试的错误（可能只是一次什么都没做的拒绝）。</summary>
            public string LastError = "";
            /// <summary>最后一次尝试是否成功——只用于说明，不作「是否写过盘」的依据。</summary>
            public bool LastSucceeded;

            /// <summary>记录一次动作结果。只追加，后一次失败不会抹掉前一次的真实结果。</summary>
            public void Record(DictConflict.RecoveryOutcome outcome)
            {
                Attempts++;
                if (outcome == null) return;
                LastError = outcome.Error == null ? "" : outcome.Error;
                LastSucceeded = outcome.Succeeded;
                if (outcome.BackupRemoved) AnyBackupRemoved = true;
                if (outcome.Succeeded) AnySucceeded = true;
                if (!outcome.Wrote) return;
                AnyWrote = true;
                if (!outcome.Succeeded && WriteError.Length == 0) WriteError = LastError;
            }

            /// <summary>对话框销毁后仍有效的独立副本。</summary>
            public ArbitrationSessionFact Copy()
            {
                ArbitrationSessionFact copy = new ArbitrationSessionFact();
                copy.Attempts = Attempts;
                copy.AnyWrote = AnyWrote;
                copy.AnySucceeded = AnySucceeded;
                copy.AnyBackupRemoved = AnyBackupRemoved;
                copy.WriteError = WriteError;
                copy.LastError = LastError;
                copy.LastSucceeded = LastSucceeded;
                return copy;
            }

            /// <summary>
            /// 会话级失败说明。任一次尝试真的写过盘时，必须说明「字典已写入」，
            /// 不能因为最后一次尝试没写就报成什么都没发生。基线失败复用产品既有
            /// 文案；备份清理失败沿用恢复核心给出的「已恢复、备份保留」原话
            /// （不改 <see cref="DictConflict"/> 的措辞）。
            /// </summary>
            public string DescribeFailure()
            {
                string error = WriteError.Length > 0 ? WriteError : LastError;
                if (!AnyWrote) return DictPaletteWorkflow.DescribeWriteError(error);
                if (error == DictFileVisibility.BaselineError)
                    return DictPaletteWorkflow.DescribeWriteError(DictFileVisibility.BaselineError);
                if (error.Length > 0) return error;
                return DictPaletteWorkflow.DescribeWriteError(DictFileVisibility.BaselineError);
            }
        }
    }
}
