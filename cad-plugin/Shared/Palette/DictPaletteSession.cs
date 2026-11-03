using System;
using System.Collections.Generic;
using System.IO;
using PatentMarker.IO;
using PatentMarker.I18n;

namespace PatentMarker.Palette
{
    /// <summary>
    /// UI-independent dictionary palette session state.
    /// It owns the current dictionary, the diff baseline and filtered rows;
    /// WinForms and AutoCAD objects stay outside this class.
    /// </summary>
    public class DictPaletteSession
    {
        private readonly List<PaletteEntry> _allEntries = new List<PaletteEntry>();
        private DictModel _currentDict;
        private List<DictDiffEntry> _currentDiff;

        public DictModel CurrentDict { get { return _currentDict; } }
        public List<PaletteEntry> AllEntries { get { return _allEntries; } }
        public List<DictDiffEntry> CurrentDiff { get { return _currentDiff; } }

        public void Load(DictModel dict, DictModel previous)
        {
            if (dict == null)
            {
                Clear();
                return;
            }

            _currentDict = dict;
            _currentDiff = previous != null ? DictDiff.Compute(previous, dict) : null;
            _allEntries.Clear();
            foreach (DictEntry entry in dict.Entries)
            {
                PaletteEntry item = new PaletteEntry();
                item.Number = entry.Number;
                item.Name = entry.Name;
                item.Occurrences = entry.Occurrences;
                item.Source = entry;
                _allEntries.Add(item);
            }

            // Keep removed entries in the session as read-only rows so the
            // compare view does not silently hide one side of the diff.
            if (_currentDiff != null)
            {
                foreach (DictDiffEntry diff in _currentDiff)
                {
                    if (diff.Status != DiffStatus.Removed || diff.OldEntry == null)
                        continue;
                    PaletteEntry removed = new PaletteEntry();
                    removed.Number = diff.OldEntry.Number;
                    removed.Name = diff.OldEntry.Name;
                    removed.Occurrences = diff.OldEntry.Occurrences;
                    removed.Source = diff.OldEntry;
                    removed.IsRemoved = true;
                    _allEntries.Add(removed);
                }
            }
        }

        public void Clear()
        {
            _allEntries.Clear();
            _currentDict = null;
            _currentDiff = null;
        }

        public List<PaletteEntry> Filter(string keyword)
        {
            List<PaletteEntry> result = new List<PaletteEntry>();
            string term = keyword == null ? "" : keyword.Trim();
            foreach (PaletteEntry entry in _allEntries)
            {
                if (term.Length == 0 ||
                    (entry.Number ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (entry.Name ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(entry);
                }
            }
            return result;
        }

        public int WarningCount
        {
            get { return _currentDict == null || _currentDict.Warnings == null ? 0 : _currentDict.Warnings.Count; }
        }

        public int ConflictCount
        {
            get
            {
                if (_currentDict == null || _currentDict.Entries == null) return 0;
                int count = 0;
                foreach (DictEntry entry in _currentDict.Entries)
                    count += entry.Conflicts == null ? 0 : entry.Conflicts.Count;
                return count;
            }
        }
    }

    /// <summary>Data row used by the palette list.</summary>
    public class PaletteEntry
    {
        public string Number = "";
        public string Name = "";
        public int Occurrences;
        // Source keeps the diff identity when rows are filtered.  Removed
        // rows are displayed for comparison but are not valid edit targets.
        public DictEntry Source;
        public bool IsRemoved;
    }

    /// <summary>
    /// R-004：一次图纸同步失败后仍欠着的**具体**操作。
    ///
    /// 修复前只保存 old/new/Document：换到另一本恰好含新号的字典仍会重试，
    /// 连续改号后旧任务永远停在已不存在的号上。这里把“操作是在哪张图纸、
    /// 哪一本字典、哪一份字节内容上被确认的”一起保存下来，重试前逐项核对。
    ///
    /// 这不是持久任务队列：记录只活在本面板会话内，按图纸（文档引用）隔离，
    /// 关闭图纸即释放。
    /// </summary>
    public sealed class PendingRenameOperation
    {
        /// <summary>原图纸文档引用（引用相等比较，与 IsCurrentContext 同一约定）。</summary>
        public object Document;
        /// <summary>原图纸完整路径（未保存图纸为空串，仅用于按图纸释放与诊断）。</summary>
        public string DrawingPath = "";
        /// <summary>完整字典路径：本次 JSON 写入的那一本。</summary>
        public string DictPath = "";
        /// <summary>成功 JSON 写出的内容身份（SHA-256）。</summary>
        public string DictIdentity = "";
        public string OldNumber = "";
        public string NewNumber = "";
        /// <summary>失败信息（服务错误或“N of M”说明）。</summary>
        public string Detail = "";
        public int Matched;
        public int Changed;
        /// <summary>
        /// R-004（G24）：这次欠账是否在等待用户对**当前字典**重新确认。
        /// 确认阶段属于这条记录（也就是属于这张图纸），不再是面板上的全局布尔：
        /// A 的「等待重新确认」不会成为 B 的确认。
        /// </summary>
        public bool NeedsReconfirm;

        public PendingRenameOperation Copy()
        {
            PendingRenameOperation copy = new PendingRenameOperation();
            copy.Document = Document;
            copy.DrawingPath = DrawingPath;
            copy.DictPath = DictPath;
            copy.DictIdentity = DictIdentity;
            copy.OldNumber = OldNumber;
            copy.NewNumber = NewNumber;
            copy.Detail = Detail;
            copy.Matched = Matched;
            copy.Changed = Changed;
            copy.NeedsReconfirm = NeedsReconfirm;
            return copy;
        }
    }

    /// <summary>
    /// R-004：一次改号同步所基于的图纸／字典快照。
    /// 首次同步与重试都必须先有它，之后不再盲用另一个 ActiveDocument。
    /// </summary>
    public sealed class RenameSyncContext
    {
        /// <summary>目标文档已不是活动文档（关闭、切换或另开）。</summary>
        public const string DocumentChanged = "PATENTMARKER_DOCUMENT_CHANGED";

        public object Document;
        public string DrawingPath = "";
        public string DictPath = "";
        public string DictIdentity = "";

        /// <summary>
        /// 抓取当前图纸与字典的快照。<paramref name="identity"/> 为刚写出的
        /// 内容身份（由写回方提供）；为空时才回读磁盘。
        /// </summary>
        public static RenameSyncContext Capture(object document, string dictPath, string identity)
        {
            if (document == null || string.IsNullOrEmpty(dictPath)) return null;
            RenameSyncContext context = new RenameSyncContext();
            context.Document = document;
            context.DrawingPath = DrawingPathOf(document);
            context.DictPath = FullPath(dictPath);
            context.DictIdentity = string.IsNullOrEmpty(identity)
                ? ReadIdentity(dictPath) : identity;
            return context;
        }

        /// <summary>抓取当前图纸与磁盘字典的快照（重试入口使用）。</summary>
        public static RenameSyncContext Capture(object document, string dictPath)
        {
            return Capture(document, dictPath, null);
        }

        public bool SameDocumentAs(object other)
        {
            return Document != null && other != null &&
                object.ReferenceEquals(Document, other);
        }

        /// <summary>快照是否仍与待同步记录指向同一本字典、同一份内容。</summary>
        public bool SameSnapshotAs(PendingRenameOperation pending)
        {
            if (pending == null) return false;
            return string.Equals(DictPath, pending.DictPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(DictIdentity, pending.DictIdentity, StringComparison.Ordinal);
        }

        /// <summary>
        /// 实际 CAD 事务前核对：文档仍是同一张、磁盘字节仍是确认过的那一份。
        /// 返回空串表示可以继续；否则返回具体漂移原因，调用方不得写图。
        /// </summary>
        public string DescribeDrift()
        {
            if (!SameDocumentAs(RuntimeHost.ActiveDocument)) return DocumentChanged;
            if (string.IsNullOrEmpty(DictPath) || string.IsNullOrEmpty(DictIdentity))
                return DictFileVisibility.ChangedError;
            string now = ReadIdentity(DictPath);
            if (!string.Equals(now, DictIdentity, StringComparison.Ordinal))
                return DictFileVisibility.ChangedError;
            return "";
        }

        /// <summary>
        /// R-004（G24）：核对「这次同步还能不能算作对当前图纸的那本字典」。
        ///
        /// 与 <see cref="DescribeDrift"/> 的区别是：这里不接受任何缓存路径，而是
        /// 用 <see cref="DictPaletteWorkflow.IsCurrentContext"/> 的规范解析约定
        /// （活动文档引用相等 + <c>Path.GetFullPath</c> 比较）确认这本字典仍然是
        /// 当前图纸的字典，再逐字节核对内容。目录里仍残留的旧路径不能充当当前
        /// 绑定，另存为换路径、默认路径切换都因此被识别。
        /// 返回空串表示可以继续。
        /// </summary>
        public string DescribeContextDrift()
        {
            if (!SameDocumentAs(RuntimeHost.ActiveDocument)) return DocumentChanged;
            if (string.IsNullOrEmpty(DictPath) || string.IsNullOrEmpty(DictIdentity))
                return DictFileVisibility.ChangedError;
            if (!DictPaletteWorkflow.IsCurrentContext(Document, DictPath))
                return DictFileVisibility.ChangedError;
            return DescribeDrift();
        }

        public static string DrawingPathOf(object document)
        {
            string name = DocumentName(document);
            if (string.IsNullOrEmpty(name)) return "";
            return FullPath(name);
        }

        public static string DocumentName(object document)
        {
            if (document == null) return "";
            try
            {
                // 宿主 Document 的 Name 是图纸完整路径；用反射避免把
                // 宿主类型写进共享源码（五个版本的 Document 类型不同）。
                object value = document.GetType().InvokeMember(
                    "Name", System.Reflection.BindingFlags.GetProperty, null,
                    document, null);
                return value as string;
            }
            catch { return ""; }
        }

        internal static string ReadIdentity(string path)
        {
            try { return DictFileVisibility.ReadIdentity(path); }
            catch { return DictFileVisibility.MissingIdentity; }
        }

        internal static string FullPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }
    }

    /// <summary>
    /// R-004（G24）：写入方保存的「这次提交到底写了什么」。
    ///
    /// 修复前调用方在对话框关闭后先 Reload、再从**新加载的模型**取
    /// <c>writtenIdentity</code>：窗口期内的外部合法 JSON、被删除的文件甚至
    /// MISSING 都会被当成「本次提交版本」，于是首次同步照常调用 CAD 且不留欠账。
    ///
    /// 成功 <c>DictWriter.Write</c> 已经通过 <c>NotifySelfWrite</c> 登记了真实序列化
    /// 身份，所以对话框在**写成功的那一刻**把它连同 Document、DWG 路径、字典路径
    /// 一起保存下来，调用方刷新列表后仍读这份提交事实。
    ///
    /// 空身份**不会**退化为回读当前文件；<c>MISSING</c> 不算成功提交。
    /// 这不是持久化服务，也不是任务队列：它只活在这一个对话框实例里。
    /// </summary>
    public sealed class RenameCommitFact
    {
        /// <summary>提交所在的文档引用（引用相等，与 IsCurrentContext 同一约定）。</summary>
        public object Document;
        /// <summary>提交时的图纸完整路径（未保存图纸为空串）。</summary>
        public string DrawingPath = "";
        /// <summary>提交时写入的字典完整路径。</summary>
        public string DictPath = "";
        /// <summary>提交时真正序列化出的字节身份（SHA-256）。空串表示没有可信身份。</summary>
        public string DictIdentity = "";
        public string OldNumber = "";
        public string NewNumber = "";
        public string OldName = "";
        public string NewName = "";
        /// <summary>是否确实完成了一次成功的 JSON 写入。</summary>
        public bool Written;

        /// <summary>
        /// 编号是否真的变了（名称编辑、新增条目都不算改号）。
        /// 新增条目没有旧号，图纸里没有可改的标注，不能当作改号提交。
        /// </summary>
        public bool NumberChanged
        {
            get
            {
                return Written && !string.IsNullOrEmpty(OldNumber)
                    && !NumberIdentity.AreEqual(OldNumber, NewNumber);
            }
        }

        /// <summary>
        /// 这份提交事实能否直接用于同步：必须有文档、字典路径和**非空**身份。
        /// 空身份一律视为不可用，调用方不得回读当前文件来补齐。
        /// </summary>
        public bool HasTrustedIdentity
        {
            get
            {
                return Written && Document != null
                    && !string.IsNullOrEmpty(DictPath)
                    && !string.IsNullOrEmpty(DictIdentity)
                    && !string.Equals(DictIdentity, DictFileVisibility.MissingIdentity,
                        StringComparison.Ordinal);
            }
        }

        public RenameSyncContext ToContext()
        {
            if (!HasTrustedIdentity) return null;
            RenameSyncContext context = new RenameSyncContext();
            context.Document = Document;
            context.DrawingPath = DrawingPath;
            context.DictPath = DictPath;
            context.DictIdentity = DictIdentity;
            return context;
        }

        /// <summary>
        /// 调用方读到的独立副本：对话框销毁后这份提交事实仍然有效，
        /// 但也不会因为对话框被再次使用而被改写。
        /// </summary>
        public RenameCommitFact Copy()
        {
            RenameCommitFact copy = new RenameCommitFact();
            copy.Document = Document;
            copy.DrawingPath = DrawingPath;
            copy.DictPath = DictPath;
            copy.DictIdentity = DictIdentity;
            copy.OldNumber = OldNumber;
            copy.NewNumber = NewNumber;
            copy.OldName = OldName;
            copy.NewName = NewName;
            copy.Written = Written;
            return copy;
        }

        /// <summary>不可信时的原因，用于状态行与日志（不为空）。</summary>
        public string DescribeUntrusted()
        {
            if (!Written) return RenameSyncText.CommitNotWritten();
            if (Document == null) return RenameSyncText.CommitNoDocument();
            if (string.IsNullOrEmpty(DictPath)) return RenameSyncText.CommitNoPath();
            if (string.IsNullOrEmpty(DictIdentity)
                || string.Equals(DictIdentity, DictFileVisibility.MissingIdentity,
                    StringComparison.Ordinal))
                return RenameSyncText.CommitNoIdentity();
            return "";
        }
    }

    /// <summary>
    /// R-004（G24）：从**同一份实际字节**严格解析出的当前字典视图。
    ///
    /// 首次同步与重试都必须拿它来判定，而不是面板缓存里的模型：
    /// 缓存可能落后于磁盘（热刷新），也可能因为文件损坏而保留旧模型，
    /// 于是「当前字典已经没有新号」被误判成「还有」，CAD 被无谓调用。
    /// </summary>
    public sealed class RenameSnapshotRead
    {
        public bool Read;
        public bool Parsed;
        public string DictPath = "";
        public string DictIdentity = "";
        /// <summary>解析成功时的模型；失败或读取失败时为 null。</summary>
        public DictModel Model;
        public string Error = "";

        /// <summary>失败原因（读取失败 / 解析失败），成功时为空串。</summary>
        public string DescribeFailure()
        {
            if (!Read) return RenameSyncText.SnapshotUnreadable(Error);
            if (!Parsed) return RenameSyncText.SnapshotUnparsable(Error);
            return "";
        }
    }

    /// <summary>R-004：后续改号在第二次 JSON 写入前得到的判定。</summary>
    public sealed class RenameFollowUpDecision
    {
        public bool Allowed;
        /// <summary>有待同步操作且本次改号与之相关：必须先完成或显式放弃。</summary>
        public bool RequiresResolution;
        public string Reason = "";
        public PendingRenameOperation Pending;
    }

    /// <summary>
    /// R-004：改号写回前的守卫。EditEntryDialog 在写 JSON 之前调用它，
    /// 因此“拦住”发生在第二次写入之前，而不是写完之后才发现冲突。
    /// </summary>
    public interface IRenameFollowUpGuard
    {
        RenameFollowUpDecision Evaluate(string oldNumber, string newNumber);
    }

    /// <summary>
    /// R-004：按图纸隔离的待同步操作登记。只保留本面板会话内的操作记录，
    /// 不是持久任务队列；A、B 两张图纸各有失败任务时互不覆盖。
    /// </summary>
    public sealed class PendingRenameRegistry
    {
        private readonly Dictionary<object, PendingRenameOperation> _byDocument =
            new Dictionary<object, PendingRenameOperation>();

        public PendingRenameOperation Get(object document)
        {
            if (document == null) return null;
            PendingRenameOperation pending;
            return _byDocument.TryGetValue(document, out pending) ? pending : null;
        }

        /// <summary>记录（或替换）该图纸的待同步操作。文档为空时丢弃。</summary>
        public void Set(PendingRenameOperation operation)
        {
            if (operation == null || operation.Document == null) return;
            _byDocument[operation.Document] = operation;
        }

        public void Clear(object document)
        {
            if (document == null) return;
            _byDocument.Remove(document);
        }

        /// <summary>
        /// R-004（G21）：只有当完成／放弃的**正是**登记的那一次操作时才移除记录。
        /// 按 Document 无条件清空会让另一次操作的完成顺带抹掉别人的欠账，
        /// 所以清除必须核对旧号与新号。
        /// </summary>
        public bool ClearIfMatches(object document, string oldNumber, string newNumber)
        {
            PendingRenameOperation pending = Get(document);
            if (pending == null) return false;
            if (!MatchesOperation(pending, oldNumber, newNumber)) return false;
            _byDocument.Remove(document);
            return true;
        }

        /// <summary>
        /// R-004（G21）：登记（或刷新）该图纸的待同步操作。
        ///
        /// 单槽位意味着一个图纸只能欠一件事。若该图纸已经欠着**另一次**操作，
        /// 新记录不会覆盖它——返回 false 让调用方保留已有欠账与原因。
        /// 同一操作的重试／重新确认（旧号新号一致）可以刷新快照。
        /// </summary>
        public bool Record(PendingRenameOperation operation)
        {
            if (operation == null || operation.Document == null) return false;
            PendingRenameOperation existing = Get(operation.Document);
            if (existing != null && !MatchesOperation(existing, operation.OldNumber, operation.NewNumber))
                return false;
            _byDocument[operation.Document] = operation;
            return true;
        }

        /// <summary>
        /// 图纸关闭时释放该图纸的记录。
        ///
        /// R-004（G24）：**收到 Document 时只释放这个对象**。修复前即使调用方
        /// 已经给了 Document，这里仍会按历史路径遍历删除；于是 A 另存为换了
        /// 路径之后，再关闭一张在 A 旧路径上重开的 B，会把 A 的欠账一起删掉。
        /// 历史路径清理只保留作**没有 Document** 时的兼容回退（旧调用方只传路径）。
        /// </summary>
        public void ReleaseForDrawing(string drawingPath, object document)
        {
            if (document != null)
            {
                _byDocument.Remove(document);
                return;
            }
            if (string.IsNullOrEmpty(drawingPath)) return;
            List<object> stale = new List<object>();
            foreach (KeyValuePair<object, PendingRenameOperation> pair in _byDocument)
            {
                if (string.Equals(pair.Value.DrawingPath, drawingPath,
                    StringComparison.OrdinalIgnoreCase))
                    stale.Add(pair.Key);
            }
            foreach (object key in stale) _byDocument.Remove(key);
        }

        public void ClearAll()
        {
            _byDocument.Clear();
        }

        public int Count { get { return _byDocument.Count; } }

        /// <summary>
        /// R-004（G21）：一次新的改号是否与待同步操作相关。
        ///
        /// G17 曾按号段判断，允许「无关改号」照常写入。计划 §12.4 已明确用
        /// 「存在欠账时拦截该图纸所有后续编号变更」替代那个安排：单槽位无法
        /// 保证两个失败任务同时保留，无关改号的**结果处理**会按 Document
        /// 无条件清空或覆盖前一条记录，导致欠账被静默遗失。
        ///
        /// 因此这里恒为 true——凡是编号变更都视为相关。名称修改不经过本方法，
        /// 所以仍然允许（见 <see cref="Evaluate"/> 的说明）。
        /// </summary>
        public static bool IsRelated(PendingRenameOperation pending,
            string dictPath, string oldNumber, string newNumber)
        {
            if (pending == null) return false;
            return true;
        }

        /// <summary>两条记录是否描述同一次操作（按编号规则比较旧号与新号）。</summary>
        public static bool MatchesOperation(PendingRenameOperation pending,
            string oldNumber, string newNumber)
        {
            if (pending == null) return false;
            return NumberIdentity.AreEqual(pending.OldNumber, oldNumber) &&
                NumberIdentity.AreEqual(pending.NewNumber, newNumber);
        }

        /// <summary>
        /// 后续改号在写 JSON 之前的判定。
        ///
        /// R-004（G21）：该图纸只要还有未完成的待同步操作，**任何**编号变更都
        /// 不允许写入，并给出必须先完成或显式放弃的具体提示。名称修改不调用
        /// 本方法（<c>EditEntryDialog.ApplyAndWrite</c> 只在旧号与新号不同时
        /// 询问守卫），所以名称编辑继续可用。
        /// </summary>
        public static RenameFollowUpDecision Evaluate(object document,
            string dictPath, string oldNumber, string newNumber,
            PendingRenameOperation pending)
        {
            RenameFollowUpDecision decision = new RenameFollowUpDecision();
            decision.Pending = pending;
            if (pending == null)
            {
                decision.Allowed = true;
                return decision;
            }
            if (!IsRelated(pending, dictPath, oldNumber, newNumber))
            {
                decision.Allowed = true;
                return decision;
            }
            decision.Allowed = false;
            decision.RequiresResolution = true;
            decision.Reason = RenameSyncText.PendingBlocksFollowUp(
                pending.OldNumber, pending.NewNumber);
            return decision;
        }

        /// <summary>
        /// 用户显式对“当前字典”重新确认同一个待同步操作：换绑到新快照，
        /// 保留旧号／新号与失败信息。返回 false 表示没有可重新确认的记录。
        /// 这一步只在用户动作下发生，不会因“新字典恰好含同号”自动执行。
        ///
        /// R-004（G24）：确认阶段随记录一起换绑——只有这条记录自己的确认被消费，
        /// 另一张图纸的确认阶段不受影响。
        /// </summary>
        public bool Rebind(PendingRenameOperation pending, RenameSyncContext context)
        {
            if (pending == null || context == null) return false;
            if (string.IsNullOrEmpty(context.DictPath) || string.IsNullOrEmpty(context.DictIdentity))
                return false;
            pending.DictPath = context.DictPath;
            pending.DictIdentity = context.DictIdentity;
            pending.Document = context.Document;
            pending.DrawingPath = context.DrawingPath;
            pending.NeedsReconfirm = false;
            _byDocument[pending.Document] = pending;
            return true;
        }

        /// <summary>
        /// R-004（G24）：把这条记录标记为「等待用户对当前字典重新确认」。
        /// 状态放在记录上而不是面板全局布尔上，因此按图纸隔离。
        /// </summary>
        public bool MarkNeedsReconfirm(PendingRenameOperation pending)
        {
            if (pending == null || pending.Document == null) return false;
            pending.NeedsReconfirm = true;
            return true;
        }

        /// <summary>该图纸是否正在等待重新确认（没有记录时为 false）。</summary>
        public bool NeedsReconfirm(object document)
        {
            PendingRenameOperation pending = Get(document);
            return pending != null && pending.NeedsReconfirm;
        }

        private static string FullPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }
    }

    /// <summary>绑定到当前图纸与当前字典的改号守卫。</summary>
    public sealed class PendingRenameGuard : IRenameFollowUpGuard
    {
        private readonly PendingRenameRegistry _registry;
        private readonly object _document;
        private readonly string _dictPath;

        public PendingRenameGuard(PendingRenameRegistry registry, object document, string dictPath)
        {
            _registry = registry;
            _document = document;
            _dictPath = dictPath;
        }

        public RenameFollowUpDecision Evaluate(string oldNumber, string newNumber)
        {
            PendingRenameOperation pending = _registry == null ? null : _registry.Get(_document);
            return PendingRenameRegistry.Evaluate(
                _document, _dictPath, oldNumber, newNumber, pending);
        }
    }

    /// <summary>R-004：显式重试的判定结果。</summary>
    public enum RenameRetryOutcome
    {
        /// <summary>该图纸没有待同步操作。</summary>
        NoPending = 0,
        /// <summary>待同步操作属于另一张图纸。</summary>
        WrongDocument = 1,
        /// <summary>字典路径或内容已变：保留失败记录并要求重新确认／重载。</summary>
        NeedsReconfirm = 2,
        /// <summary>字典已不含新号（被重新定义或删除），不能按原确认完成。</summary>
        MissingNumber = 3,
        /// <summary>同一图纸、同一字典、同一内容：可以继续重试。</summary>
        Ready = 4,
    }

    public sealed class RenameRetryDecision
    {
        public RenameRetryOutcome Outcome;
        public string Reason = "";
        public PendingRenameOperation Pending;

        public bool CanRetry { get { return Outcome == RenameRetryOutcome.Ready; } }
    }

    /// <summary>
    /// 按编号查找字典条目。用具名委托而不是 Func&lt;,&gt;：
    /// 2007 组目标是 .NET 2.0，没有 System.Func。
    /// </summary>
    public delegate DictEntry NumberLookup(string number);

    /// <summary>
    /// R-004：重试判定与首次同步共用的纯逻辑。面板与单元测试走同一个函数，
    /// 因此“面板调度”行为可以在不弹对话框的情况下被断言。
    /// </summary>
    public static class RenameRetryPlanner
    {
        /// <param name="pending">该图纸登记的待同步操作（可为 null）。</param>
        /// <param name="context">当前图纸／字典快照（可为 null）。</param>
        /// <param name="findEntry">
        /// 在当前字典模型里按号查找条目的委托；返回 null 表示字典已不含该号。
        /// </param>
        public static RenameRetryDecision Decide(PendingRenameOperation pending,
            RenameSyncContext context, NumberLookup findEntry)
        {
            RenameRetryDecision decision = new RenameRetryDecision();
            decision.Pending = pending;
            if (pending == null)
            {
                decision.Outcome = RenameRetryOutcome.NoPending;
                return decision;
            }
            if (context == null || !context.SameDocumentAs(pending.Document))
            {
                decision.Outcome = RenameRetryOutcome.WrongDocument;
                decision.Reason = RenameSyncText.RetryWrongDocument();
                return decision;
            }
            if (!context.SameSnapshotAs(pending))
            {
                // 字典变化：保留失败记录，不静默清除，也不猜测串接映射。
                decision.Outcome = RenameRetryOutcome.NeedsReconfirm;
                decision.Reason = RenameSyncText.RetryNeedsReconfirm(pending.Detail);
                return decision;
            }
            DictEntry entry = findEntry == null ? null : findEntry(pending.NewNumber);
            if (entry == null || !NumberIdentity.AreEqual(entry.Number, pending.NewNumber))
            {
                decision.Outcome = RenameRetryOutcome.MissingNumber;
                decision.Reason = RenameSyncText.RetryMissingNumber(pending.NewNumber);
                return decision;
            }
            decision.Outcome = RenameRetryOutcome.Ready;
            return decision;
        }

        /// <summary>
        /// R-004（G24）：在**同一份实际字节**上判定重试。
        ///
        /// 与 <see cref="Decide"/> 的差别不是判定规则，而是判定输入：
        /// <paramref name="snapshot"/> 必须是从磁盘一次读出的字节严格解析出的结果，
        /// 不能是面板缓存里的旧模型。这样：
        ///   * 文件读不出来（损坏、被删、被目录顶替）→ 保留任务，绝不借用旧模型；
        ///   * 字节与记录不一致 → 要求重新确认；
        ///   * 字节一致但没有新号 → 按真实解析结果结束，不猜；
        ///   * 只有「同一张图纸 + 规范路径仍是这本字典 + 同一份字节 + 含新号」
        ///     才 Ready。
        ///
        /// 判定顺序与 <see cref="Decide"/> 一致，因此原有 19 条 G17 断言的含义不变。
        /// </summary>
        public static RenameRetryDecision DecideOnBytes(PendingRenameOperation pending,
            RenameSyncContext context, RenameSnapshotRead snapshot)
        {
            RenameRetryDecision decision = new RenameRetryDecision();
            decision.Pending = pending;
            if (pending == null)
            {
                decision.Outcome = RenameRetryOutcome.NoPending;
                return decision;
            }
            if (context == null || !context.SameDocumentAs(pending.Document))
            {
                decision.Outcome = RenameRetryOutcome.WrongDocument;
                decision.Reason = RenameSyncText.RetryWrongDocument();
                return decision;
            }
            if (snapshot == null || !snapshot.Read)
            {
                // 读不出真实字节：既不能确认，也不能当作「字典已不含新号」。
                decision.Outcome = RenameRetryOutcome.NeedsReconfirm;
                decision.Reason = snapshot == null
                    ? RenameSyncText.SnapshotUnreadable("")
                    : snapshot.DescribeFailure();
                return decision;
            }
            if (!context.SameSnapshotAs(pending))
            {
                decision.Outcome = RenameRetryOutcome.NeedsReconfirm;
                decision.Reason = RenameSyncText.RetryNeedsReconfirm(pending.Detail);
                return decision;
            }
            if (!snapshot.Parsed || snapshot.Model == null)
            {
                // 字节就是确认过的那一份，但已经解析不出来：保留任务并要求重载。
                decision.Outcome = RenameRetryOutcome.NeedsReconfirm;
                decision.Reason = snapshot.DescribeFailure();
                return decision;
            }
            DictEntry entry = FindEntry(snapshot.Model, pending.NewNumber);
            if (entry == null)
            {
                decision.Outcome = RenameRetryOutcome.MissingNumber;
                decision.Reason = RenameSyncText.RetryMissingNumber(pending.NewNumber);
                return decision;
            }
            decision.Outcome = RenameRetryOutcome.Ready;
            return decision;
        }

        /// <summary>在给定模型里按号查找条目（编号按 NumberIdentity 规则比较）。</summary>
        public static DictEntry FindEntry(DictModel model, string number)
        {
            if (model == null || model.Entries == null) return null;
            foreach (DictEntry entry in model.Entries)
            {
                if (entry != null && NumberIdentity.AreEqual(entry.Number, number)) return entry;
            }
            return null;
        }
    }

    /// <summary>R-004：面向用户的双语提示（不改动 Strings 资源文件）。</summary>
    public static class RenameSyncText
    {
        private static bool En { get { return Strings.Lang == Language.English; } }

        /// <summary>有待同步操作时拦截后续相关改号。</summary>
        public static string PendingBlocksFollowUp(string pendingOld, string pendingNew)
        {
            return En
                ? "Finish or discard the pending drawing sync (" + pendingOld + " → " + pendingNew
                  + ") before changing this number. The dictionary was not written."
                : "请先完成或显式放弃待同步操作（" + pendingOld + " → " + pendingNew
                  + "），本次改号未写入字典。";
        }

        /// <summary>字典内容／路径已变，重试需要重新确认。</summary>
        public static string RetryNeedsReconfirm(string detail)
        {
            return En
                ? "The dictionary changed since the failed sync: " + detail
                  + " Confirm the retry against the current dictionary, or reload it."
                : "字典在失败后已变化：" + detail
                  + " 请对当前字典重新确认该重试，或重载字典。";
        }

        /// <summary>图纸已不是原来那张。</summary>
        public static string RetryWrongDocument()
        {
            return En
                ? "The failed sync belongs to another drawing. Reopen that drawing and retry there."
                : "该待同步操作属于另一张图纸，请回到原图纸重试。";
        }

        /// <summary>字典已不再包含新号（被重新定义或删除）。</summary>
        public static string RetryMissingNumber(string newNumber)
        {
            return En
                ? "The dictionary no longer contains " + newNumber
                  + "; the pending sync cannot be completed as confirmed."
                : "字典已不再包含 " + newNumber + "，待同步操作无法按原确认完成。";
        }

        /// <summary>待同步操作的已提交数（报告与实际一致）。</summary>
        public static string PendingCommitted(int changed, int matched, string oldNumber, string newNumber)
        {
            return En
                ? changed + " of " + matched + " annotation(s) committed before the failure: "
                  + oldNumber + " → " + newNumber
                : "失败前已提交 " + changed + "/" + matched + " 条标注：" + oldNumber + " → " + newNumber;
        }

        /// <summary>
        /// R-004（G21）：放弃待同步操作的按钮文案。与 <see cref="PendingBlocksFollowUp"/>
        /// 一起构成「完成或显式放弃」的完整出路；沿用 RenameSyncText 而不改
        /// Strings.cs（G17 已定的做法）。
        /// </summary>
        public static string DiscardPendingButton()
        {
            return En ? "Discard pending sync" : "放弃待同步";
        }

        /// <summary>R-004（G24）：这次提交没有真正写出 JSON，不能拿它当同步依据。</summary>
        public static string CommitNotWritten()
        {
            return En
                ? "The edit was not written to the dictionary; the drawing was not changed."
                : "本次编辑没有写入字典，图纸未改动。";
        }

        /// <summary>R-004（G24）：提交时没有目标图纸。</summary>
        public static string CommitNoDocument()
        {
            return En
                ? "The edit has no target drawing; the drawing sync was not started."
                : "本次编辑没有目标图纸，未开始同步图纸。";
        }

        /// <summary>R-004（G24）：提交时没有字典路径。</summary>
        public static string CommitNoPath()
        {
            return En
                ? "The edit has no dictionary path; the drawing sync was not started."
                : "本次编辑没有字典路径，未开始同步图纸。";
        }

        /// <summary>R-004（G24）：提交没有可信的写出身份（文件已被替换或删除）。</summary>
        public static string CommitNoIdentity()
        {
            return En
                ? "The dictionary this edit wrote is no longer identifiable (replaced or deleted). "
                  + "Reopen the dictionary and confirm the change again."
                : "本次编辑写出的字典已无法识别（被替换或删除），请重载字典后重新确认。";
        }

        /// <summary>R-004（G24）：读不出当前字典的真实字节。</summary>
        public static string SnapshotUnreadable(string detail)
        {
            string suffix = string.IsNullOrEmpty(detail) ? "" : " " + detail;
            return En
                ? "The current dictionary could not be read; the pending sync was kept." + suffix
                : "当前字典无法读取，待同步操作已保留。" + suffix;
        }

        /// <summary>R-004（G24）：读到的真实字节解析失败。</summary>
        public static string SnapshotUnparsable(string detail)
        {
            string suffix = string.IsNullOrEmpty(detail) ? "" : " " + detail;
            return En
                ? "The current dictionary cannot be parsed; the pending sync was kept." + suffix
                : "当前字典无法解析，待同步操作已保留。" + suffix;
        }
    }
}
