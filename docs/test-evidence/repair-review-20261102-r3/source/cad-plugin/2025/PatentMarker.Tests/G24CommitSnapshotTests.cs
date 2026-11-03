using PatentMarker.IO;
using PatentMarker.Palette;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace PatentMarker.Tests
{
    /// <summary>
    /// G24 / R-004（补修）：把封存红灯（`palette-matrix-final.txt` 的 N2/N3/N4，
    /// 20 条 FAIL）拆成可断言的生产类型行为。
    ///
    /// 三部分契约（repair-plan §13.2）：
    ///  1. **提交快照由写入方保存**：成功 Write 之后由对话框留住当时 Document／
    ///     字典路径／真实序列化身份；空身份不得退化为回读当前文件，MISSING 不算
    ///     成功提交。
    ///  2. **首次同步与重试都核对规范路径和真实内容**：判定输入是从规范路径一次
    ///     读出的字节严格解析结果，不是面板缓存里的 `_currentDict`。
    ///  3. **入口、确认阶段、释放按 Document 归属**：A 的「等待重新确认」不是
    ///     B 的确认；有 Document 时释放只删该对象。
    ///
    /// 这些用例走生产类型（RenameCommitFact / RenameSnapshotRead /
    /// RenameRetryPlanner.DecideOnBytes / PendingRenameRegistry / RenameSyncContext /
    /// DictPaletteWorkflow.ReadSnapshot），不通过测试辅助入口预置待测状态。
    /// 真实按钮与真实模态对话框的调用链证据在
    /// `docs/test-evidence/repair-review-20261102-r2/G24/` 的红绿探针里。
    /// </summary>
    public class G24CommitSnapshotTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _dictPath;
        private readonly List<string> _createdDirs = new List<string>();
        private object? _document;

        public G24CommitSnapshotTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pm-g24-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _createdDirs.Add(_dir);
            _dictPath = Path.Combine(_dir, "t.dict.json");
        }

        public void Dispose()
        {
            foreach (string dir in _createdDirs)
            {
                try
                {
                    foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(dir, true);
                }
                catch { }
            }
            DictLoader.InvalidateCache();
            ConfigLoader.Current = null;
            RuntimeHost.ClearActiveDocumentOverride();
        }

        // ===== helpers =====

        private static void WriteDict(string path, params string[] numbers)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            StringBuilder json = new StringBuilder();
            json.Append("{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n");
            json.Append("    \"extracted_at\": \"2026-11-02T01:23:45\"\n  },\n  \"entries\": [\n");
            for (int i = 0; i < numbers.Length; i++)
            {
                if (i > 0) json.Append(",\n");
                json.Append("    {\n      \"number\": \"").Append(numbers[i])
                    .Append("\",\n      \"name\": \"part ").Append(numbers[i]).Append("\"\n    }");
            }
            json.Append("\n  ],\n  \"warnings\": []\n}");
            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
        }

        private void BindDocument(string drawingName)
        {
            string drawing = Path.Combine(_dir, drawingName + ".dwg");
            if (_document == null || !object.Equals(DocumentName(_document), drawing))
                _document = new Autodesk.AutoCAD.ApplicationServices.Document { Name = drawing };
            object document = _document;
            RuntimeHost.SetActiveDocumentOverride(
                delegate { return (Autodesk.AutoCAD.ApplicationServices.Document)document; });
        }

        private static string DocumentName(object document)
        {
            return ((Autodesk.AutoCAD.ApplicationServices.Document)document).Name;
        }

        private static DictEntry Entry(string number)
        {
            DictEntry entry = new DictEntry();
            entry.Number = number;
            entry.Name = "part " + number;
            return entry;
        }

        private static string Identity(string path) { return DictFileVisibility.ReadIdentity(path); }

        /// <summary>
        /// A commit fact as the writer produces it: the identity is the one
        /// DictWriter registered for the model it just serialized.
        /// </summary>
        private RenameCommitFact Commit(object document, string dictPath,
            string identity, string oldNumber, string newNumber)
        {
            RenameCommitFact fact = new RenameCommitFact();
            fact.Written = true;
            fact.Document = document;
            fact.DrawingPath = RenameSyncContext.DrawingPathOf(document);
            fact.DictPath = RenameSyncContext.FullPath(dictPath);
            fact.DictIdentity = identity;
            fact.OldNumber = oldNumber;
            fact.NewNumber = newNumber;
            return fact;
        }

        private PendingRenameOperation Owed(object document, string dictPath,
            string identity, string oldNumber, string newNumber)
        {
            PendingRenameOperation operation = new PendingRenameOperation();
            operation.Document = document;
            operation.DrawingPath = RenameSyncContext.DrawingPathOf(document);
            operation.DictPath = RenameSyncContext.FullPath(dictPath);
            operation.DictIdentity = identity;
            operation.OldNumber = oldNumber;
            operation.NewNumber = newNumber;
            operation.Detail = "first sync refused";
            operation.Matched = 1;
            operation.Changed = 0;
            return operation;
        }

        // ===== 1. the writer saves the commit fact =====

        /// <summary>
        /// Sealed `N2-real-edit-replacement-sync-calls|calls=1` and
        /// `N2-real-edit-replacement-keeps-debt|`: an external legal JSON inside the
        /// window between save and first sync must never become "this commit".
        /// </summary>
        [Fact]
        public void G24_CommitFact_KeepsTheIdentityOfItsOwnWrite()
        {
            WriteDict(_dictPath, "301", "401");
            BindDocument("A");
            string ownWrite = Identity(_dictPath);

            RenameCommitFact fact = Commit(_document, _dictPath, ownWrite, "301", "302");
            Assert.True(fact.HasTrustedIdentity);
            Assert.True(fact.NumberChanged);

            // Another writer replaces the file before the first sync runs.
            WriteDict(_dictPath, "999");
            string external = Identity(_dictPath);
            Assert.NotEqual(ownWrite, external);

            // The commit fact still describes THIS write, not the new bytes.
            Assert.Equal(ownWrite, fact.DictIdentity);
            Assert.Equal(ownWrite, fact.ToContext().DictIdentity);
        }

        /// <summary>
        /// Sealed `N2-real-edit-deleted-*`: a deleted dictionary is not a commit.
        /// </summary>
        [Fact]
        public void G24_CommitFact_MissingIdentityIsNotACommit()
        {
            WriteDict(_dictPath, "301", "401");
            BindDocument("A");
            File.Delete(_dictPath);

            RenameCommitFact fact = Commit(_document, _dictPath,
                DictFileVisibility.MissingIdentity, "301", "302");
            Assert.False(fact.HasTrustedIdentity);
            Assert.Null(fact.ToContext());
            Assert.Equal(RenameSyncText.CommitNoIdentity(), fact.DescribeUntrusted());
        }

        /// <summary>
        /// The hard rule from §13.2: an empty identity must not degrade into
        /// re-reading the current file.
        /// </summary>
        [Fact]
        public void G24_CommitFact_EmptyIdentityNeverFallsBackToTheFile()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            RenameCommitFact fact = Commit(_document, _dictPath, "", "301", "302");
            Assert.False(fact.HasTrustedIdentity);
            Assert.Null(fact.ToContext());
            Assert.Equal(RenameSyncText.CommitNoIdentity(), fact.DescribeUntrusted());
        }

        /// <summary>
        /// Sealed `N2-real-edit-bad-*` control: the same real commit fact survives
        /// the dialog object, and the corrupt file that followed is a drift.
        /// </summary>
        [Fact]
        public void G24_CommitFact_SurvivesTheDialogAndDetectsDrift()
        {
            WriteDict(_dictPath, "301", "401");
            BindDocument("A");
            RenameCommitFact fact = Commit(_document, _dictPath, Identity(_dictPath), "301", "302");
            RenameCommitFact copy = fact.Copy();
            Assert.True(copy.HasTrustedIdentity);
            Assert.Equal(fact.DictIdentity, copy.DictIdentity);
            Assert.Equal("", copy.ToContext().DescribeDrift());

            File.WriteAllText(_dictPath, "{broken after save", new UTF8Encoding(false));
            Assert.Equal(DictFileVisibility.ChangedError, copy.ToContext().DescribeDrift());
        }

        /// <summary>
        /// A name-only edit or an added entry is not a number change: the drawing
        /// must not be touched (sealed `N1-name-allowed` still holds).
        /// </summary>
        [Fact]
        public void G24_CommitFact_NameEditAndNewEntryAreNotNumberChanges()
        {
            WriteDict(_dictPath, "301", "401");
            BindDocument("A");
            RenameCommitFact nameOnly = Commit(_document, _dictPath, Identity(_dictPath), "301", "301");
            Assert.False(nameOnly.NumberChanged);
            Assert.True(nameOnly.HasTrustedIdentity);

            RenameCommitFact added = Commit(_document, _dictPath, Identity(_dictPath), "", "501");
            Assert.False(added.NumberChanged);
        }

        /// <summary>
        /// A failed write leaves no commit fact at all (the rollback semantics of
        /// G17/G21 are unchanged).
        /// </summary>
        [Fact]
        public void G24_CommitFact_NoFactWhenNothingWasWritten()
        {
            RenameCommitFact fact = new RenameCommitFact();
            Assert.False(fact.Written);
            Assert.False(fact.HasTrustedIdentity);
            Assert.False(fact.NumberChanged);
            Assert.Null(fact.ToContext());
            Assert.Equal(RenameSyncText.CommitNotWritten(), fact.DescribeUntrusted());
        }
    }

    /// <summary>
    /// G24：首次同步与重试都以「从规范路径读出的同一份真实字节」为判定输入。
    /// 覆盖封存 N3 的 8 条 FAIL（缺新号／损坏 × 旧模型／热刷新／冷缓存）与
    /// N2 的另存为换路径。
    /// </summary>
    public class G24CanonicalSnapshotTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _dictPath;
        private readonly List<string> _createdDirs = new List<string>();
        private object? _document;

        public G24CanonicalSnapshotTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pm-g24b-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _createdDirs.Add(_dir);
            // The dictionary must be the one the canonical resolver finds for the
            // bound drawing (same directory, same base name), otherwise the loader
            // has nothing to return and the test would not exercise the real path.
            _dictPath = Path.Combine(_dir, "a.dict.json");
        }

        public void Dispose()
        {
            foreach (string dir in _createdDirs)
            {
                try
                {
                    foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(dir, true);
                }
                catch { }
            }
            DictLoader.InvalidateCache();
            ConfigLoader.Current = null;
            RuntimeHost.ClearActiveDocumentOverride();
        }

        private static void WriteDict(string path, params string[] numbers)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            StringBuilder json = new StringBuilder();
            json.Append("{\n  \"metadata\": {},\n  \"entries\": [\n");
            for (int i = 0; i < numbers.Length; i++)
            {
                if (i > 0) json.Append(",\n");
                json.Append("    {\n      \"number\": \"").Append(numbers[i])
                    .Append("\",\n      \"name\": \"part ").Append(numbers[i]).Append("\"\n    }");
            }
            json.Append("\n  ],\n  \"warnings\": []\n}");
            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
        }

        private void BindDocument(string drawingName)
        {
            string drawing = Path.Combine(_dir, drawingName + ".dwg");
            if (_document == null || !object.Equals(DocumentName(_document), drawing))
                _document = new Autodesk.AutoCAD.ApplicationServices.Document { Name = drawing };
            object document = _document;
            RuntimeHost.SetActiveDocumentOverride(
                delegate { return (Autodesk.AutoCAD.ApplicationServices.Document)document; });
        }

        private static string DocumentName(object document)
        {
            return ((Autodesk.AutoCAD.ApplicationServices.Document)document).Name;
        }

        private static string Identity(string path) { return DictFileVisibility.ReadIdentity(path); }

        private PendingRenameOperation Owed(string identity, string oldNumber, string newNumber)
        {
            PendingRenameOperation operation = new PendingRenameOperation();
            operation.Document = _document;
            operation.DrawingPath = RenameSyncContext.DrawingPathOf(_document);
            operation.DictPath = Path.GetFullPath(_dictPath);
            operation.DictIdentity = identity;
            operation.OldNumber = oldNumber;
            operation.NewNumber = newNumber;
            operation.Detail = "first sync refused";
            return operation;
        }

        /// <summary>
        /// Sealed `N3-missing-new-stale|expected-calls=0|actual-calls=1`: the warm
        /// cache still holds the old model, but the real bytes no longer contain the
        /// new number, so the retry must end as a real result without CAD.
        /// </summary>
        [Fact]
        public void G24_RetryOnBytes_MissingNewNumberIsNotMaskedByTheCachedModel()
        {
            WriteDict(_dictPath, "301", "401");
            BindDocument("A");
            DictLoader.InvalidateCache();
            DictModel? loaded = DictLoader.LoadForCurrentDrawing();
            Assert.NotNull(loaded);                       // warm cache
            PendingRenameOperation owed = Owed(Identity(_dictPath), "301", "302");

            // Another writer drops 302 from the dictionary.
            WriteDict(_dictPath, "999");

            RenameSnapshotRead snapshot = new DictPaletteWorkflow().ReadSnapshot(_dictPath);
            Assert.True(snapshot.Read);
            Assert.True(snapshot.Parsed);
            Assert.NotEqual(owed.DictIdentity, snapshot.DictIdentity);

            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath, snapshot.DictIdentity);
            RenameRetryDecision decision = RenameRetryPlanner.DecideOnBytes(owed, context, snapshot);
            Assert.Equal(RenameRetryOutcome.NeedsReconfirm, decision.Outcome);
            Assert.False(decision.CanRetry);

            // After an explicit reconfirmation the real bytes decide: no 302 there.
            RenameSyncContext confirmed = RenameSyncContext.Capture(_document, _dictPath, snapshot.DictIdentity);
            owed.DictPath = confirmed.DictPath;
            owed.DictIdentity = confirmed.DictIdentity;
            RenameRetryDecision after = RenameRetryPlanner.DecideOnBytes(owed, confirmed, snapshot);
            Assert.Equal(RenameRetryOutcome.MissingNumber, after.Outcome);
            Assert.False(after.CanRetry);
        }

        /// <summary>
        /// Sealed `N3-bad-warm|expected-calls=0|actual-calls=1`: a broken file must
        /// not be retried against the model the cache kept from before.
        /// </summary>
        [Fact]
        public void G24_RetryOnBytes_BrokenFileKeepsTheTask()
        {
            WriteDict(_dictPath, "301", "401");
            BindDocument("A");
            DictLoader.InvalidateCache();
            Assert.NotNull(DictLoader.LoadForCurrentDrawing());       // warm cache holds a valid model
            PendingRenameOperation owed = Owed(Identity(_dictPath), "301", "302");

            File.WriteAllText(_dictPath, "{broken", new UTF8Encoding(false));

            RenameSnapshotRead snapshot = new DictPaletteWorkflow().ReadSnapshot(_dictPath);
            Assert.True(snapshot.Read);
            Assert.False(snapshot.Parsed);
            Assert.Null(snapshot.Model);
            Assert.NotEqual("", snapshot.DescribeFailure());

            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath, snapshot.DictIdentity);
            RenameRetryDecision decision = RenameRetryPlanner.DecideOnBytes(owed, context, snapshot);
            Assert.Equal(RenameRetryOutcome.NeedsReconfirm, decision.Outcome);
            Assert.False(decision.CanRetry);
            Assert.NotEqual("", decision.Reason);

            // The same task, after the user confirmed these (already broken) bytes:
            // the parse failure is reported instead of borrowing the cached model.
            owed.DictPath = context.DictPath;
            owed.DictIdentity = context.DictIdentity;
            RenameRetryDecision confirmed = RenameRetryPlanner.DecideOnBytes(owed, context, snapshot);
            Assert.Equal(RenameRetryOutcome.NeedsReconfirm, confirmed.Outcome);
            Assert.False(confirmed.CanRetry);
            Assert.Equal(snapshot.DescribeFailure(), confirmed.Reason);
        }

        /// <summary>
        /// A dictionary that cannot be read at all (a directory replaced the file)
        /// is not "the dictionary no longer has the number": the task stays.
        /// </summary>
        [Fact]
        public void G24_RetryOnBytes_UnreadableFileKeepsTheTask()
        {
            WriteDict(_dictPath, "301", "401");
            BindDocument("A");
            PendingRenameOperation owed = Owed(Identity(_dictPath), "301", "302");

            File.Delete(_dictPath);
            Directory.CreateDirectory(_dictPath);

            RenameSnapshotRead snapshot = new DictPaletteWorkflow().ReadSnapshot(_dictPath);
            Assert.False(snapshot.Read);
            Assert.Equal("", snapshot.DictIdentity);

            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath, null);
            Assert.Equal(DictFileVisibility.MissingIdentity, context.DictIdentity);
            RenameRetryDecision decision = RenameRetryPlanner.DecideOnBytes(owed, context, snapshot);
            Assert.Equal(RenameRetryOutcome.NeedsReconfirm, decision.Outcome);
            Assert.False(decision.CanRetry);
            Assert.Contains(RenameSyncText.SnapshotUnreadable(""), decision.Reason);
        }

        /// <summary>
        /// The retry is only ready when the real bytes still contain the new number.
        /// </summary>
        [Fact]
        public void G24_RetryOnBytes_ReadyOnlyWithTheSameBytesAndTheNewNumber()
        {
            WriteDict(_dictPath, "301", "401");
            BindDocument("A");
            PendingRenameOperation owed = Owed(Identity(_dictPath), "301", "302");

            // The dictionary this operation wrote (301 replaced by 302) is still
            // byte-identical to what the record carries.
            string same = Identity(_dictPath);
            owed.DictIdentity = same;
            RenameSnapshotRead unchanged = new DictPaletteWorkflow().ReadSnapshot(_dictPath);
            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath, same);
            RenameRetryDecision decision = RenameRetryPlanner.DecideOnBytes(owed, context, unchanged);
            Assert.Equal(RenameRetryOutcome.MissingNumber, decision.Outcome);

            // A legal dictionary that does contain 302 and matches the record.
            WriteDict(_dictPath, "302", "401");
            string withNew = Identity(_dictPath);
            owed.DictIdentity = withNew;
            RenameSnapshotRead snapshot = new DictPaletteWorkflow().ReadSnapshot(_dictPath);
            RenameSyncContext ready = RenameSyncContext.Capture(_document, _dictPath, withNew);
            RenameRetryDecision ok = RenameRetryPlanner.DecideOnBytes(owed, ready, snapshot);
            Assert.Equal(RenameRetryOutcome.Ready, ok.Outcome);
            Assert.True(ok.CanRetry);
            Assert.NotNull(RenameRetryPlanner.FindEntry(snapshot.Model, "302"));
            Assert.Equal("302", RenameRetryPlanner.FindEntry(snapshot.Model, "302").Number);
        }

        /// <summary>
        /// Sealed `N3-save-as-first-refuses-FalseFalse|calls=1`: the same Document
        /// saved under a new path is a different canonical binding, so the first
        /// click must refuse.
        /// </summary>
        [Fact]
        public void G24_CanonicalPath_SaveAsMovesTheBinding()
        {
            string first = Path.Combine(_dir, "a.dwg");
            WriteDict(Path.ChangeExtension(first, ".dict.json"), "301", "401");
            _document = new Autodesk.AutoCAD.ApplicationServices.Document { Name = first };
            object document = _document;
            RuntimeHost.SetActiveDocumentOverride(
                delegate { return (Autodesk.AutoCAD.ApplicationServices.Document)document; });
            DictLoader.InvalidateCache();
            DictModel? loaded = DictLoader.LoadForCurrentDrawing();
            Assert.NotNull(loaded);

            string original = DictPaletteWorkflow.ResolveCanonicalPath(_document);
            Assert.Equal(Path.GetFullPath(_dictPath), original);

            // Save As: same Document object, new path, new dictionary bytes.
            string second = Path.Combine(_dir, "b.dwg");
            string secondDict = Path.ChangeExtension(second, ".dict.json");
            WriteDict(secondDict, "302", "999");
            ((Autodesk.AutoCAD.ApplicationServices.Document)_document).Name = second;
            DictLoader.InvalidateCache();

            string moved = DictPaletteWorkflow.ResolveCanonicalPath(_document);
            Assert.Equal(Path.GetFullPath(secondDict), moved);

            // The record still points at the old path, so the snapshot differs even
            // though the old file is untouched on disk.
            PendingRenameOperation owed = new PendingRenameOperation();
            owed.Document = _document;
            owed.DrawingPath = RenameSyncContext.DrawingPathOf(_document);
            owed.DictPath = Path.GetFullPath(_dictPath);
            owed.DictIdentity = Identity(_dictPath);
            owed.OldNumber = "301";
            owed.NewNumber = "302";

            RenameSnapshotRead snapshot = new DictPaletteWorkflow().ReadSnapshot(moved);
            RenameSyncContext context = RenameSyncContext.Capture(_document, moved, snapshot.DictIdentity);
            RenameRetryDecision decision = RenameRetryPlanner.DecideOnBytes(owed, context, snapshot);
            Assert.Equal(RenameRetryOutcome.NeedsReconfirm, decision.Outcome);
            Assert.False(decision.CanRetry);

            // The first sync would also be refused: the canonical path moved.
            RenameSyncContext stale = RenameSyncContext.Capture(
                _document, Path.GetFullPath(_dictPath), Identity(_dictPath));
            Assert.NotEqual("", stale.DescribeContextDrift());
            Assert.Equal("", context.DescribeContextDrift());
        }

        /// <summary>
        /// A canonical binding that cannot be resolved (no dictionary for this
        /// drawing) is not silently "the cached path".
        /// </summary>
        [Fact]
        public void G24_CanonicalPath_UnresolvableIsNotTheCachedPath()
        {
            string drawing = Path.Combine(_dir, "none.dwg");
            _document = new Autodesk.AutoCAD.ApplicationServices.Document { Name = drawing };
            object document = _document;
            RuntimeHost.SetActiveDocumentOverride(
                delegate { return (Autodesk.AutoCAD.ApplicationServices.Document)document; });
            DictLoader.InvalidateCache();

            Assert.Null(DictPaletteWorkflow.ResolveCanonicalPath(_document));
            RenameSnapshotRead snapshot = new DictPaletteWorkflow().ReadCanonicalSnapshot(_document!);
            Assert.False(snapshot.Read);
            Assert.Equal("", snapshot.DictIdentity);

            RenameSyncContext? context = RenameSyncContext.Capture(_document, null, null);
            Assert.Null(context);
            PendingRenameOperation owed = new PendingRenameOperation();
            owed.Document = _document;
            owed.OldNumber = "301";
            owed.NewNumber = "302";
            RenameRetryDecision decision = RenameRetryPlanner.DecideOnBytes(owed, context, snapshot);
            Assert.Equal(RenameRetryOutcome.WrongDocument, decision.Outcome);
            Assert.False(decision.CanRetry);
        }
    }

    /// <summary>
    /// G24：入口、确认阶段与释放按 Document 归属。
    /// 覆盖封存 N4 的 6 条 FAIL。
    /// </summary>
    public class G24PerDocumentLifecycleTests : IDisposable
    {
        private readonly string _dir;
        private readonly List<string> _createdDirs = new List<string>();

        public G24PerDocumentLifecycleTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pm-g24c-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _createdDirs.Add(_dir);
        }

        public void Dispose()
        {
            foreach (string dir in _createdDirs)
            {
                try
                {
                    foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                        File.SetAttributes(file, FileAttributes.Normal);
                    Directory.Delete(dir, true);
                }
                catch { }
            }
            RuntimeHost.ClearActiveDocumentOverride();
        }

        private object Drawing(string name)
        {
            return new Autodesk.AutoCAD.ApplicationServices.Document
            { Name = Path.Combine(_dir, name + ".dwg") };
        }

        private PendingRenameOperation Owed(object document, string oldNumber, string newNumber)
        {
            PendingRenameOperation operation = new PendingRenameOperation();
            operation.Document = document;
            operation.DrawingPath = RenameSyncContext.DrawingPathOf(document);
            operation.DictPath = Path.GetFullPath(
                Path.ChangeExtension(RenameSyncContext.DrawingPathOf(document), ".dict.json"));
            operation.DictIdentity = "identity-" + oldNumber;
            operation.OldNumber = oldNumber;
            operation.NewNumber = newNumber;
            operation.Detail = "sync failed";
            return operation;
        }

        /// <summary>
        /// Sealed `N4-confirm-not-inherited|expected-first-retry-calls=0|actual=1`:
        /// A's "waiting for reconfirmation" must never be B's confirmation.
        /// </summary>
        [Fact]
        public void G24_ConfirmPhase_IsPerDocument()
        {
            object a = Drawing("a");
            object b = Drawing("b");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            Assert.True(registry.Record(Owed(a, "301", "302")));
            Assert.True(registry.Record(Owed(b, "501", "502")));

            Assert.True(registry.MarkNeedsReconfirm(registry.Get(a)));
            Assert.True(registry.NeedsReconfirm(a));
            Assert.False(registry.NeedsReconfirm(b));

            // A's explicit reconfirmation consumes only A's phase. The new binding
            // must be a real canonical path + a non-empty byte identity, otherwise
            // Rebind refuses (and then the phase must survive).
            string canonical = Path.GetFullPath(
                Path.ChangeExtension(RenameSyncContext.DrawingPathOf(a), ".dict.json"));
            RenameSyncContext rebind = RenameSyncContext.Capture(a, canonical, "identity-302");
            Assert.True(registry.Rebind(registry.Get(a), rebind));
            Assert.False(registry.NeedsReconfirm(a));
            Assert.False(registry.NeedsReconfirm(b));
        }

        /// <summary>
        /// A's phase is still there after B is looked at: the flag is not global.
        /// </summary>
        [Fact]
        public void G24_ConfirmPhase_OtherDrawingLeavesItIntact()
        {
            object a = Drawing("a");
            object b = Drawing("b");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Record(Owed(a, "301", "302"));
            registry.Record(Owed(b, "501", "502"));
            registry.MarkNeedsReconfirm(registry.Get(a));

            // B has no phase; A's survives any read of B.
            Assert.False(registry.NeedsReconfirm(b));
            Assert.True(registry.NeedsReconfirm(a));
            Assert.True(registry.Get(a).NeedsReconfirm);
            Assert.False(registry.Get(b).NeedsReconfirm);

            // A copy carries the phase, so a retry that works on a copy does not
            // lose it either.
            Assert.True(registry.Get(a).Copy().NeedsReconfirm);
        }

        /// <summary>
        /// Sealed `N4-close-reused-old-path-keeps-renamed-doc|pending-A=False`:
        /// releasing with a Document must release only that object, even when the
        /// path is the one A used before Save As.
        /// </summary>
        [Fact]
        public void G24_Release_WithDocumentReleasesOnlyThatObject()
        {
            object a = Drawing("a");
            object reopened = Drawing("a");       // a DIFFERENT Document at A's old path
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Record(Owed(a, "301", "302"));

            // A was saved as a.dwg-renamed; the record still carries A's object.
            registry.ReleaseForDrawing(RenameSyncContext.DrawingPathOf(reopened), reopened);

            Assert.NotNull(registry.Get(a));
            Assert.Equal("301", registry.Get(a).OldNumber);
            Assert.Equal(1, registry.Count);
        }

        /// <summary>
        /// The historical-path fallback stays, but only for callers that pass no
        /// Document (compatibility), and it still never touches another object.
        /// </summary>
        [Fact]
        public void G24_Release_WithoutDocumentFallsBackToTheHistoricalPath()
        {
            object a = Drawing("a");
            object b = Drawing("b");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Record(Owed(a, "301", "302"));
            registry.Record(Owed(b, "501", "502"));

            registry.ReleaseForDrawing(RenameSyncContext.DrawingPathOf(a), null);

            Assert.Null(registry.Get(a));
            Assert.NotNull(registry.Get(b));
            Assert.Equal(1, registry.Count);
        }

        /// <summary>
        /// Closing an unrelated drawing keeps the owed record (sealed
        /// `N4-closing-unrelated-keeps-A` stays green).
        /// </summary>
        [Fact]
        public void G24_Release_ClosingAnotherDrawingKeepsTheDebt()
        {
            object a = Drawing("a");
            object b = Drawing("b");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Record(Owed(a, "301", "302"));

            registry.ReleaseForDrawing(RenameSyncContext.DrawingPathOf(b), b);

            Assert.NotNull(registry.Get(a));
            Assert.Equal("301>302", registry.Get(a).OldNumber + ">" + registry.Get(a).NewNumber);
        }

        /// <summary>
        /// Sealed `N4-b-buttons-after-A-resolved|pending=True|buttons=False`: the
        /// entry is decided by the ACTIVE drawing's debt, not by a global flag.
        /// </summary>
        [Fact]
        public void G24_Entry_VisibilityFollowsTheActiveDrawing()
        {
            object a = Drawing("a");
            object b = Drawing("b");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Record(Owed(a, "301", "302"));
            registry.Record(Owed(b, "501", "502"));

            // A is resolved; B still owes.
            Assert.True(registry.ClearIfMatches(a, "301", "302"));
            Assert.Null(registry.Get(a));
            Assert.NotNull(registry.Get(b));
            Assert.True(registry.Count > 0);

            // Sealed `N4-no-debt-hides-buttons`: a drawing without a debt shows
            // nothing even though another drawing still owes.
            object c = Drawing("c");
            Assert.Null(registry.Get(c));
            Assert.NotNull(registry.Get(b));
        }
    }
}
