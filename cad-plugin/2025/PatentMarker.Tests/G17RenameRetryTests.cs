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
    /// G17 / R-004：保存并验证**具体的**待同步操作。
    ///
    /// 修复前的待重试状态只有 old/new/Document：
    ///   * 同一图纸换到另一本恰好含新号的字典仍会重试；
    ///   * 301→302 失败后继续把同一项改到 303，JSON 已经到 303，
    ///     而待重试目标仍停在 302，点击重试调用服务 0 次。
    ///
    /// 修复后待同步记录保存原 Document、完整字典路径、成功 JSON 写出的内容
    /// 身份、旧号／新号与失败信息；重试前逐项核对；后续相关改号在第二次
    /// JSON 写入之前被拦住。待处理状态按图纸隔离。
    ///
    /// 这些用例走生产类型（PendingRenameRegistry / PendingRenameGuard /
    /// RenameRetryPlanner / RenameSyncContext / EditEntryDialog 的真实守卫路径），
    /// 不通过测试辅助入口预置待测状态，也不直接调用 RenameNumber 服务。
    /// </summary>
    public class G17RenameRetryTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _dictPath;
        private readonly List<string> _createdDirs = new List<string>();
        private object _document;

        public G17RenameRetryTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pm-g17-" + Guid.NewGuid().ToString("N"));
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
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            StringBuilder json = new StringBuilder();
            json.Append("{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n");
            json.Append("    \"extracted_at\": \"2026-11-01T01:23:45\"\n  },\n  \"entries\": [\n");
            for (int i = 0; i < numbers.Length; i++)
            {
                if (i > 0) json.Append(",\n");
                json.Append("    {\n      \"number\": \"").Append(numbers[i])
                    .Append("\",\n      \"name\": \"part ").Append(numbers[i]).Append("\"\n    }");
            }
            json.Append("\n  ],\n  \"warnings\": []\n}");
            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
        }

        /// <summary>
        /// Binds the host seam to a stable document per drawing path.
        /// The instance is cached so reference equality matches the real
        /// ActiveDocument, exactly as IsCurrentContext expects.
        /// </summary>
        private void BindDocument(string drawingName)
        {
            string drawing = Path.Combine(_dir, drawingName + ".dwg");
            if (_document == null || !object.Equals(DocumentName(_document), drawing))
                _document = new Autodesk.AutoCAD.ApplicationServices.Document { Name = drawing };
            object document = _document;
            RuntimeHost.SetActiveDocumentOverride(delegate { return (Autodesk.AutoCAD.ApplicationServices.Document)document; });
        }

        private static string DocumentName(object document)
        {
            return ((Autodesk.AutoCAD.ApplicationServices.Document)document).Name;
        }

        private static string Identity(string path) { return DictFileVisibility.ReadIdentity(path); }

        /// <summary>
        /// The exact state the palette records after a failed first sync:
        /// the drawing and dictionary it confirmed, plus the identity of the
        /// JSON that was successfully written.
        /// </summary>
        private PendingRenameOperation RecordFailedSync(
            object document, string oldNumber, string newNumber, string detail)
        {
            PendingRenameOperation operation = new PendingRenameOperation();
            operation.Document = document;
            operation.DrawingPath = RenameSyncContext.DrawingPathOf(document);
            operation.DictPath = Path.GetFullPath(_dictPath);
            operation.DictIdentity = Identity(_dictPath);
            operation.OldNumber = oldNumber;
            operation.NewNumber = newNumber;
            operation.Detail = detail;
            operation.Matched = 1;
            operation.Changed = 0;
            return operation;
        }

        // ===== the record keeps the full context =====

        [Fact]
        public void G17_PendingRecord_KeepsDocumentPathIdentityAndNumbers()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            PendingRenameOperation operation = RecordFailedSync(_document, "301", "302", "refused");
            registry.Set(operation);

            PendingRenameOperation stored = registry.Get(_document);
            Assert.NotNull(stored);
            Assert.True(object.ReferenceEquals(_document, stored.Document));
            Assert.Equal(Path.GetFullPath(_dictPath), stored.DictPath);
            Assert.Equal(Identity(_dictPath), stored.DictIdentity);
            Assert.Equal("301", stored.OldNumber);
            Assert.Equal("302", stored.NewNumber);
            Assert.Equal("refused", stored.Detail);
        }

        [Fact]
        public void G17_SyncContext_CapturesDrawingAndContentIdentity()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath);
            Assert.NotNull(context);
            Assert.Equal(Path.GetFullPath(_dictPath), context.DictPath);
            Assert.Equal(Identity(_dictPath), context.DictIdentity);
            Assert.Equal(Path.Combine(_dir, "A.dwg"), context.DrawingPath);
        }

        /// <summary>
        /// The sealed line 3 defect: the SAME drawing whose dictionary was
        /// replaced by another one that also contains 302. The pending task
        /// belongs to the first content, so the retry must not run.
        /// </summary>
        [Fact]
        public void G17_Retry_DifferentDictionaryContentWithSameNewNumber_IsNotExecuted()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameOperation pending = RecordFailedSync(_document, "301", "302", "refused");

            // Another writer replaces the dictionary; it still contains 302
            // but its bytes (and therefore its content identity) differ.
            WriteDict(_dictPath, "302", "999");
            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath);
            Assert.NotEqual(pending.DictIdentity, context.DictIdentity);
            RenameRetryDecision decision = RenameRetryPlanner.Decide(
                pending, context, delegate (string n) { return Entry("302"); });

            Assert.NotEqual(RenameRetryOutcome.Ready, decision.Outcome);
            Assert.False(decision.CanRetry);
            Assert.Equal(RenameRetryOutcome.NeedsReconfirm, decision.Outcome);
            // The failure record is kept, not silently cleared.
            Assert.NotNull(decision.Pending);
            Assert.Equal("301", decision.Pending.OldNumber);
        }

        [Fact]
        public void G17_Retry_SameContext_IsReady()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameOperation pending = RecordFailedSync(_document, "301", "302", "refused");
            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath);
            RenameRetryDecision decision = RenameRetryPlanner.Decide(
                pending, context, delegate (string n) { return Entry(n); });
            Assert.True(decision.CanRetry);
            Assert.Equal(RenameRetryOutcome.Ready, decision.Outcome);
        }

        [Fact]
        public void G17_Retry_OtherDrawing_IsWrongDocument()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameOperation pending = RecordFailedSync(_document, "301", "302", "refused");
            BindDocument("B");   // the user switched drawings
            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath);
            RenameRetryDecision decision = RenameRetryPlanner.Decide(
                pending, context, delegate (string n) { return Entry(n); });
            Assert.Equal(RenameRetryOutcome.WrongDocument, decision.Outcome);
            Assert.False(decision.CanRetry);
        }

        [Fact]
        public void G17_Retry_NewNumberDeleted_IsNotReady()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameOperation pending = RecordFailedSync(_document, "301", "302", "refused");
            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath);
            // Content unchanged, but the entry is gone from the parsed model.
            RenameRetryDecision decision = RenameRetryPlanner.Decide(
                pending, context, delegate (string n) { return null; });
            Assert.Equal(RenameRetryOutcome.MissingNumber, decision.Outcome);
            Assert.False(decision.CanRetry);
            Assert.NotNull(decision.Pending);
        }

        [Fact]
        public void G17_Retry_NewNumberRedefined_KeepsFailureRecord()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameOperation pending = RecordFailedSync(_document, "301", "302", "refused");
            // Same number, different content: another writer redefined it.
            WriteDict(_dictPath, "302", "999");
            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath);
            RenameRetryDecision decision = RenameRetryPlanner.Decide(
                pending, context, delegate (string n) { return Entry(n); });
            Assert.Equal(RenameRetryOutcome.NeedsReconfirm, decision.Outcome);
            Assert.NotNull(decision.Pending);
            Assert.Equal("refused", decision.Pending.Detail);
        }

        // ===== the second JSON write is blocked =====

        /// <summary>
        /// The sealed line 4 defect: after 301→302 fails, the user edits the
        /// same entry to 303. The guard must refuse BEFORE the JSON write, so
        /// the dictionary never reaches 303 behind an owed 302.
        /// </summary>
        [Fact]
        public void G17_Guard_BlocksChainedRename_BeforeJsonWrite()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Set(RecordFailedSync(_document, "301", "302", "refused"));
            PendingRenameGuard guard = new PendingRenameGuard(registry, _document, _dictPath);

            RenameFollowUpDecision decision = guard.Evaluate("302", "303");
            Assert.False(decision.Allowed);
            Assert.True(decision.RequiresResolution);
            Assert.NotNull(decision.Pending);
            Assert.Contains("301", decision.Reason);
            Assert.Contains("302", decision.Reason);
        }

        /// <summary>
        /// Contract delta (G21 / repair-plan §12.4): a pending record blocks EVERY
        /// later number change on that drawing, not only the ones sharing a number.
        /// G17 asserted the opposite ("unrelated renames stay writable"); §12.4
        /// explicitly replaces that arrangement because a single slot cannot keep
        /// two failed operations alive — the unrelated rename's result handling
        /// cleared or overwrote the owed one.
        /// </summary>
        [Fact]
        public void G17_Guard_BlocksAllNumberChangesWhileOwed()
        {
            WriteDict(_dictPath, "302", "777");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Set(RecordFailedSync(_document, "301", "302", "refused"));
            PendingRenameGuard guard = new PendingRenameGuard(registry, _document, _dictPath);
            RenameFollowUpDecision decision = guard.Evaluate("777", "778");
            Assert.False(decision.Allowed);
            Assert.True(decision.RequiresResolution);
            Assert.NotNull(decision.Pending);
            Assert.Contains("301", decision.Reason);
            Assert.Contains("302", decision.Reason);
        }

        [Fact]
        public void G17_Guard_AllowsAfterExplicitDiscard()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Set(RecordFailedSync(_document, "301", "302", "refused"));
            PendingRenameGuard guard = new PendingRenameGuard(registry, _document, _dictPath);
            Assert.False(guard.Evaluate("302", "303").Allowed);

            registry.Clear(_document);
            Assert.True(guard.Evaluate("302", "303").Allowed);
            Assert.Null(registry.Get(_document));
        }

        [Fact]
        public void G17_Guard_AllowsWhenNoPendingOperationExists()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            PendingRenameGuard guard = new PendingRenameGuard(registry, _document, _dictPath);
            Assert.True(guard.Evaluate("302", "303").Allowed);
        }

        // ===== per-drawing isolation =====

        [Fact]
        public void G17_Registry_IsolatesTwoDrawings()
        {
            string dirA = Path.Combine(_dir, "A");
            string dirB = Path.Combine(_dir, "B");
            _createdDirs.Add(dirA);
            _createdDirs.Add(dirB);
            WriteDict(Path.Combine(dirA, "t.dict.json"), "302");
            WriteDict(Path.Combine(dirB, "t.dict.json"), "402");

            PendingRenameRegistry registry = new PendingRenameRegistry();

            object documentA = new Autodesk.AutoCAD.ApplicationServices.Document
            { Name = Path.Combine(dirA, "t.dwg") };
            object documentB = new Autodesk.AutoCAD.ApplicationServices.Document
            { Name = Path.Combine(dirB, "t.dwg") };

            PendingRenameOperation a = new PendingRenameOperation();
            a.Document = documentA;
            a.DrawingPath = RenameSyncContext.DrawingPathOf(documentA);
            a.DictPath = Path.Combine(dirA, "t.dict.json");
            a.DictIdentity = Identity(a.DictPath);
            a.OldNumber = "301";
            a.NewNumber = "302";
            registry.Set(a);

            PendingRenameOperation b = new PendingRenameOperation();
            b.Document = documentB;
            b.DrawingPath = RenameSyncContext.DrawingPathOf(documentB);
            b.DictPath = Path.Combine(dirB, "t.dict.json");
            b.DictIdentity = Identity(b.DictPath);
            b.OldNumber = "401";
            b.NewNumber = "402";
            registry.Set(b);

            // B's failure must not overwrite A's, and vice versa.
            Assert.Equal("301", registry.Get(documentA).OldNumber);
            Assert.Equal("401", registry.Get(documentB).OldNumber);
            Assert.Equal(2, registry.Count);

            // The guard is bound to one drawing, so A's task does not block B.
            PendingRenameGuard guardB = new PendingRenameGuard(registry, documentB, b.DictPath);
            Assert.False(guardB.Evaluate("402", "403").Allowed);
            registry.Clear(documentA);
            Assert.False(guardB.Evaluate("402", "403").Allowed);
            Assert.Null(registry.Get(documentA));
            Assert.Equal("401", registry.Get(documentB).OldNumber);
        }

        [Fact]
        public void G17_Registry_ReleaseForDrawingReleasesOnlyThatDrawing()
        {
            string dirA = Path.Combine(_dir, "A");
            string dirB = Path.Combine(_dir, "B");
            _createdDirs.Add(dirA);
            _createdDirs.Add(dirB);
            WriteDict(Path.Combine(dirA, "t.dict.json"), "302");
            WriteDict(Path.Combine(dirB, "t.dict.json"), "402");

            PendingRenameRegistry registry = new PendingRenameRegistry();
            object documentA = new Autodesk.AutoCAD.ApplicationServices.Document
            { Name = Path.Combine(dirA, "t.dwg") };
            object documentB = new Autodesk.AutoCAD.ApplicationServices.Document
            { Name = Path.Combine(dirB, "t.dwg") };

            PendingRenameOperation a = new PendingRenameOperation();
            a.Document = documentA;
            a.DrawingPath = RenameSyncContext.DrawingPathOf(documentA);
            a.DictPath = Path.Combine(dirA, "t.dict.json");
            a.DictIdentity = Identity(a.DictPath);
            a.OldNumber = "301";
            a.NewNumber = "302";
            registry.Set(a);

            PendingRenameOperation b = new PendingRenameOperation();
            b.Document = documentB;
            b.DrawingPath = RenameSyncContext.DrawingPathOf(documentB);
            b.DictPath = Path.Combine(dirB, "t.dict.json");
            b.DictIdentity = Identity(b.DictPath);
            b.OldNumber = "401";
            b.NewNumber = "402";
            registry.Set(b);

            // Closing drawing A releases A's record only.
            registry.ReleaseForDrawing(a.DrawingPath, documentA);
            Assert.Null(registry.Get(documentA));
            Assert.NotNull(registry.Get(documentB));
            Assert.Equal(1, registry.Count);
        }

        // ===== reconfirmation gives a new snapshot, not a permanent refusal =====

        [Fact]
        public void G17_Rebind_ConfirmsAgainstCurrentDictionaryAndBecomesReady()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameOperation pending = RecordFailedSync(_document, "301", "302", "refused");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Set(pending);

            // The dictionary changed, so the first retry needs a new confirmation.
            WriteDict(_dictPath, "302", "999");
            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath);
            RenameRetryDecision before = RenameRetryPlanner.Decide(
                registry.Get(_document), context, delegate (string n) { return Entry(n); });
            Assert.Equal(RenameRetryOutcome.NeedsReconfirm, before.Outcome);

            // The user explicitly reconfirms against the dictionary now on disk.
            Assert.True(registry.Rebind(pending, context));
            RenameRetryDecision after = RenameRetryPlanner.Decide(
                registry.Get(_document), context, delegate (string n) { return Entry(n); });
            Assert.True(after.CanRetry);
            // The original numbers and failure text are preserved.
            Assert.Equal("301", registry.Get(_document).OldNumber);
            Assert.Equal("302", registry.Get(_document).NewNumber);
            Assert.Equal("refused", registry.Get(_document).Detail);
        }

        [Fact]
        public void G17_Rebind_RejectsMissingSnapshot()
        {
            PendingRenameOperation pending = new PendingRenameOperation();
            pending.OldNumber = "301";
            pending.NewNumber = "302";
            PendingRenameRegistry registry = new PendingRenameRegistry();
            Assert.False(registry.Rebind(pending, null));
            RenameSyncContext blank = new RenameSyncContext();
            Assert.False(registry.Rebind(pending, blank));
        }

        // ===== the CAD transaction is gated on the disk identity =====

        [Fact]
        public void G17_DescribeDrift_RejectsExternalWriteBeforeTransaction()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath);
            Assert.Equal("", context.DescribeDrift());

            // Another process writes the dictionary between confirmation and
            // the CAD transaction.
            WriteDict(_dictPath, "302", "999");
            Assert.Equal(DictFileVisibility.ChangedError, context.DescribeDrift());
        }

        [Fact]
        public void G17_DescribeDrift_RejectsSwitchedDocument()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath);
            BindDocument("B");
            Assert.Equal(RenameSyncContext.DocumentChanged, context.DescribeDrift());
        }

        // ===== the real dialog refuses before writing =====
        //
        // EditEntryDialog.cs is not compiled into this test project (its csproj
        // lists the production files explicitly and is out of G17's write
        // scope), so the dialog-level path is covered by the red/green probe in
        // docs/test-evidence/repair-review-20261101/G17/probe/, which compiles
        // the REAL EditEntryDialog and asserts that a chained rename leaves the
        // dictionary bytes untouched (scenario rename_second_json_write_blocked).
        // What this file proves is the guard the dialog consults, which is the
        // production PendingRenameGuard below.

        /// <summary>
        /// The exact predicate EditEntryDialog.ApplyAndWrite calls before it
        /// touches the dictionary: a chained rename is refused, so no second
        /// JSON write can happen while a related task is owed.
        /// </summary>
        [Fact]
        public void G17_Guard_IsTheGateTheDialogConsultsBeforeWriting()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Set(RecordFailedSync(_document, "301", "302", "refused"));
            PendingRenameGuard guard = new PendingRenameGuard(registry, _document, _dictPath);

            // Same predicate, same arguments as the dialog's save path.
            RenameFollowUpDecision chained = guard.Evaluate("302", "303");
            Assert.False(chained.Allowed);
            Assert.True(chained.RequiresResolution);
            Assert.True(chained.Reason.Length > 0);

            // After the user discards, the identical edit is permitted.
            registry.Clear(_document);
            Assert.True(guard.Evaluate("302", "303").Allowed);
        }

        [Fact]
        public void G17_Guard_TreatsNumberOverlapAsRelated()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Set(RecordFailedSync(_document, "301", "302", "refused"));
            PendingRenameGuard guard = new PendingRenameGuard(registry, _document, _dictPath);

            // Rewriting the old number again, and chaining off the new number,
            // both touch the owed step and must be refused.
            Assert.False(guard.Evaluate("301", "305").Allowed);
            Assert.False(guard.Evaluate("302", "303").Allowed);
            // Contract delta (G21 / §12.4): an unrelated number pair is refused too,
            // because the single pending slot would lose the owed operation.
            Assert.False(guard.Evaluate("800", "801").Allowed);
        }

        private static DictEntry Entry(string number)
        {
            DictEntry entry = new DictEntry();
            entry.Number = number;
            entry.Name = "part " + number;
            return entry;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
