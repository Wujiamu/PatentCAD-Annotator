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

        private static string ReadIdentity(string path)
        {
            try { return DictFileVisibility.ReadIdentity(path); }
            catch { return DictFileVisibility.MissingIdentity; }
        }

        private static string FullPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            try { return Path.GetFullPath(path); }
            catch { return path; }
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

        /// <summary>图纸关闭时释放该图纸的记录（也覆盖未保存图纸的空路径）。</summary>
        public void ReleaseForDrawing(string drawingPath, object document)
        {
            if (document != null) _byDocument.Remove(document);
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
        /// R-004：一次新的改号是否与待同步操作相关：号段有重叠才会串接、覆盖或反向
        /// 改写待完成的那一步（例如 301→302 未完成时再改 302→303 或 301→305）。
        ///
        /// 只按号码判断，不按字典路径：同一本字典里另一条无关条目的改号
        /// 不会影响这张图纸欠着的那一步，不该被一并拦住。字典路径／内容是否
        /// 仍然一致属于**重试**门禁，见 <see cref="RenameRetryPlanner"/>。
        /// </summary>
        public static bool IsRelated(PendingRenameOperation pending,
            string dictPath, string oldNumber, string newNumber)
        {
            if (pending == null) return false;
            return SharesNumber(pending, oldNumber, newNumber);
        }

        private static bool SharesNumber(PendingRenameOperation pending,
            string oldNumber, string newNumber)
        {
            string[] mine = new string[] { oldNumber, newNumber };
            string[] theirs = new string[] { pending.OldNumber, pending.NewNumber };
            for (int i = 0; i < mine.Length; i++)
            {
                if (string.IsNullOrEmpty(mine[i])) continue;
                for (int j = 0; j < theirs.Length; j++)
                {
                    if (string.IsNullOrEmpty(theirs[j])) continue;
                    if (NumberIdentity.AreEqual(mine[i], theirs[j])) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 后续改号在写 JSON 之前的判定。有待同步操作且本次相关时不允许写入，
        /// 并给出必须先完成或显式放弃的具体提示。
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
            _byDocument[pending.Document] = pending;
            return true;
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
    }
}
