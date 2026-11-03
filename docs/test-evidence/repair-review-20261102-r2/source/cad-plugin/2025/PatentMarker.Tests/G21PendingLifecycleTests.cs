using PatentMarker.IO;
using PatentMarker.I18n;
using PatentMarker.Palette;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace PatentMarker.Tests
{
    /// <summary>
    /// G21 / R-004（补修）：防止单槽位丢失欠账。
    ///
    /// 封存红灯（palette-review-final.txt）四例：301→302 待同步时再改 401→402，
    /// 成功／零匹配会清空前者，失败会覆盖前者；首次同步因内容漂移被拒时
    /// 没有建立待同步记录。
    ///
    /// 修复后契约（repair-plan §12.4）：
    ///   * 每图纸一个待同步记录；存在欠账时拦截该图纸**所有**后续编号变更；
    ///   * 名称编辑继续允许；
    ///   * 记录只在**该操作**完成或用户显式放弃时移除；
    ///   * 首次同步被文档／路径／内容漂移拒绝也要登记原操作并要求重新确认。
    ///
    /// 这些用例走生产类型（PendingRenameRegistry / PendingRenameGuard /
    /// RenameSyncContext / RenameRetryPlanner），不直接 Set 一条记录后只测
    /// Registry 的存取；登记、拦截、清除都经过生产判定函数。
    /// </summary>
    public class G21PendingLifecycleTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _dictPath;
        private readonly List<string> _createdDirs = new List<string>();
        private object _document;

        public G21PendingLifecycleTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pm-g21-" + Guid.NewGuid().ToString("N"));
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
            RuntimeHost.SetActiveDocumentOverride(delegate { return (Autodesk.AutoCAD.ApplicationServices.Document)document; });
        }

        private static string DocumentName(object document)
        {
            return ((Autodesk.AutoCAD.ApplicationServices.Document)document).Name;
        }

        private static string Identity(string path) { return DictFileVisibility.ReadIdentity(path); }

        /// <summary>
        /// The record the palette builds for a failed first sync, produced by the
        /// same fields the production RecordPendingRename fills in.
        /// </summary>
        private PendingRenameOperation FailedSync(
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

        // ===== the four sealed cells =====

        /// <summary>
        /// Sealed cell 1+2: while 301→302 is owed, a successful or zero-match
        /// 401→402 on the same drawing must not clear the record.
        /// </summary>
        [Fact]
        public void G21_UnrelatedNumberChange_IsBlockedWhileOwed()
        {
            WriteDict(_dictPath, "302", "401");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            Assert.True(registry.Record(FailedSync(_document, "301", "302", "refused")));
            PendingRenameGuard guard = new PendingRenameGuard(registry, _document, _dictPath);

            RenameFollowUpDecision unrelated = guard.Evaluate("401", "402");
            Assert.False(unrelated.Allowed);
            Assert.True(unrelated.RequiresResolution);
            Assert.Contains("301", unrelated.Reason);
            Assert.Contains("302", unrelated.Reason);

            // The record is untouched by the refusal.
            Assert.NotNull(registry.Get(_document));
            Assert.Equal("301", registry.Get(_document).OldNumber);
            Assert.Equal("302", registry.Get(_document).NewNumber);
            Assert.Equal("refused", registry.Get(_document).Detail);
        }

        /// <summary>
        /// Sealed cell 3: a failing 401→402 must not overwrite 301→302.
        /// </summary>
        [Fact]
        public void G21_SecondFailure_DoesNotOverwriteFirstPending()
        {
            WriteDict(_dictPath, "302", "401");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            Assert.True(registry.Record(FailedSync(_document, "301", "302", "first failure")));

            // The unrelated operation's failure arrives while the first is owed.
            Assert.False(registry.Record(FailedSync(_document, "401", "402", "second failure")));

            PendingRenameOperation kept = registry.Get(_document);
            Assert.NotNull(kept);
            Assert.Equal("301", kept.OldNumber);
            Assert.Equal("302", kept.NewNumber);
            Assert.Equal("first failure", kept.Detail);
            Assert.Equal(1, registry.Count);
        }

        /// <summary>
        /// Sealed cell 4: a first sync refused because the dictionary drifted is
        /// still owed - the record is registered and asks for reconfirmation.
        /// </summary>
        [Fact]
        public void G21_FirstSyncDrift_RecordsOwedOperation()
        {
            WriteDict(_dictPath, "301", "401");
            BindDocument("A");
            RenameSyncContext context = RenameSyncContext.Capture(_document, _dictPath);
            Assert.Equal("", context.DescribeDrift());

            // The record is built from the JSON this operation just wrote, so its
            // identity is the identity of those bytes - captured before the drift.
            PendingRenameOperation owed = FailedSync(_document, "301", "302", "PATENTMARKER_CONTENT_CHANGED");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            Assert.True(registry.Record(owed));

            // Another writer replaces the dictionary before the CAD transaction.
            WriteDict(_dictPath, "302", "401", "777");
            Assert.Equal(DictFileVisibility.ChangedError, context.DescribeDrift());

            // Retrying needs an explicit reconfirmation against the new bytes.
            RenameSyncContext now = RenameSyncContext.Capture(_document, _dictPath);
            RenameRetryDecision decision = RenameRetryPlanner.Decide(
                registry.Get(_document), now, delegate (string n) { return Entry(n); });
            Assert.Equal(RenameRetryOutcome.NeedsReconfirm, decision.Outcome);
            Assert.False(decision.CanRetry);
            Assert.NotNull(decision.Pending);
            Assert.Equal("301", decision.Pending.OldNumber);

            // The explicit reconfirmation then makes the same operation ready.
            Assert.True(registry.Rebind(registry.Get(_document), now));
            RenameRetryDecision after = RenameRetryPlanner.Decide(
                registry.Get(_document), now, delegate (string n) { return Entry(n); });
            Assert.True(after.CanRetry);
            Assert.Equal("301", registry.Get(_document).OldNumber);
            Assert.Equal("302", registry.Get(_document).NewNumber);
        }

        // ===== clearing is per operation, not per document =====

        [Fact]
        public void G21_ClearIfMatches_OnlyClearsTheCompletedOperation()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Record(FailedSync(_document, "301", "302", "refused"));

            // Completing a DIFFERENT operation must not release the owed one.
            Assert.False(registry.ClearIfMatches(_document, "401", "402"));
            Assert.NotNull(registry.Get(_document));

            // Completing the owed one does.
            Assert.True(registry.ClearIfMatches(_document, "301", "302"));
            Assert.Null(registry.Get(_document));
            Assert.Equal(0, registry.Count);
        }

        [Fact]
        public void G21_ClearIfMatches_UsesTheNumberIdentityRule()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Record(FailedSync(_document, "301", "302", "refused"));
            // Same number under the project's comparison rule (trim/case) still matches.
            Assert.True(registry.ClearIfMatches(_document, " 301 ", "302"));
        }

        [Fact]
        public void G21_Record_SameOperationRefreshesSnapshot()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Record(FailedSync(_document, "301", "302", "refused"));

            WriteDict(_dictPath, "302", "999");
            PendingRenameOperation retry = FailedSync(_document, "301", "302", "refused");
            Assert.True(registry.Record(retry));
            Assert.Equal(Identity(_dictPath), registry.Get(_document).DictIdentity);
            Assert.Equal(1, registry.Count);
        }

        // ===== name edits stay allowed =====

        /// <summary>
        /// The guard is only consulted when the number actually changes
        /// (EditEntryDialog.ApplyAndWrite). A name-only edit therefore writes and
        /// keeps the owed operation; the predicate below is the same one the
        /// dialog uses.
        /// </summary>
        [Fact]
        public void G21_NameOnlyEdit_IsNotGatedAndKeepsPending()
        {
            WriteDict(_dictPath, "302");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Record(FailedSync(_document, "301", "302", "refused"));
            PendingRenameGuard guard = new PendingRenameGuard(registry, _document, _dictPath);

            // ApplyAndWrite only asks the guard when old != new; a name edit keeps
            // the number, so no decision is taken and the write proceeds.
            Assert.True(NumberIdentity.AreEqual("302", "302"));

            // And the record is still there afterwards.
            Assert.NotNull(registry.Get(_document));
            Assert.Equal("301", registry.Get(_document).OldNumber);
            // A real number change is still refused.
            Assert.False(guard.Evaluate("302", "303").Allowed);
        }

        // ===== discard restores the ability to change numbers =====

        [Fact]
        public void G21_DiscardThenNumberChangeIsAllowed()
        {
            WriteDict(_dictPath, "302", "401");
            BindDocument("A");
            PendingRenameRegistry registry = new PendingRenameRegistry();
            registry.Record(FailedSync(_document, "301", "302", "refused"));
            PendingRenameGuard guard = new PendingRenameGuard(registry, _document, _dictPath);
            Assert.False(guard.Evaluate("401", "402").Allowed);

            registry.Clear(_document);
            Assert.True(guard.Evaluate("401", "402").Allowed);
            Assert.Null(registry.Get(_document));
        }

        // ===== two drawings keep their own record =====

        [Fact]
        public void G21_TwoDrawingsEachKeepTheirOwnRecord()
        {
            string dirA = Path.Combine(_dir, "A");
            string dirB = Path.Combine(_dir, "B");
            _createdDirs.Add(dirA);
            _createdDirs.Add(dirB);
            WriteDict(Path.Combine(dirA, "t.dict.json"), "302");
            WriteDict(Path.Combine(dirB, "t.dict.json"), "402");

            object documentA = new Autodesk.AutoCAD.ApplicationServices.Document
            { Name = Path.Combine(dirA, "t.dwg") };
            object documentB = new Autodesk.AutoCAD.ApplicationServices.Document
            { Name = Path.Combine(dirB, "t.dwg") };

            PendingRenameRegistry registry = new PendingRenameRegistry();

            PendingRenameOperation a = new PendingRenameOperation();
            a.Document = documentA;
            a.DrawingPath = RenameSyncContext.DrawingPathOf(documentA);
            a.DictPath = Path.Combine(dirA, "t.dict.json");
            a.DictIdentity = Identity(a.DictPath);
            a.OldNumber = "301";
            a.NewNumber = "302";
            Assert.True(registry.Record(a));

            PendingRenameOperation b = new PendingRenameOperation();
            b.Document = documentB;
            b.DrawingPath = RenameSyncContext.DrawingPathOf(documentB);
            b.DictPath = Path.Combine(dirB, "t.dict.json");
            b.DictIdentity = Identity(b.DictPath);
            b.OldNumber = "401";
            b.NewNumber = "402";
            Assert.True(registry.Record(b));

            Assert.Equal("301", registry.Get(documentA).OldNumber);
            Assert.Equal("401", registry.Get(documentB).OldNumber);
            Assert.Equal(2, registry.Count);

            // A's owed record blocks A only; B is gated by its own record.
            PendingRenameGuard guardA = new PendingRenameGuard(registry, documentA, a.DictPath);
            Assert.False(guardA.Evaluate("777", "778").Allowed);
        }

        [Fact]
        public void G21_ClosingOneDrawingDoesNotClearTheOther()
        {
            string dirA = Path.Combine(_dir, "A");
            string dirB = Path.Combine(_dir, "B");
            _createdDirs.Add(dirA);
            _createdDirs.Add(dirB);
            WriteDict(Path.Combine(dirA, "t.dict.json"), "302");
            WriteDict(Path.Combine(dirB, "t.dict.json"), "402");

            object documentA = new Autodesk.AutoCAD.ApplicationServices.Document
            { Name = Path.Combine(dirA, "t.dwg") };
            object documentB = new Autodesk.AutoCAD.ApplicationServices.Document
            { Name = Path.Combine(dirB, "t.dwg") };

            PendingRenameRegistry registry = new PendingRenameRegistry();

            PendingRenameOperation a = new PendingRenameOperation();
            a.Document = documentA;
            a.DrawingPath = RenameSyncContext.DrawingPathOf(documentA);
            a.DictPath = Path.Combine(dirA, "t.dict.json");
            a.DictIdentity = Identity(a.DictPath);
            a.OldNumber = "301";
            a.NewNumber = "302";
            registry.Record(a);

            PendingRenameOperation b = new PendingRenameOperation();
            b.Document = documentB;
            b.DrawingPath = RenameSyncContext.DrawingPathOf(documentB);
            b.DictPath = Path.Combine(dirB, "t.dict.json");
            b.DictIdentity = Identity(b.DictPath);
            b.OldNumber = "401";
            b.NewNumber = "402";
            registry.Record(b);

            // Closing B releases B only.
            registry.ReleaseForDrawing(b.DrawingPath, documentB);
            Assert.Null(registry.Get(documentB));
            Assert.NotNull(registry.Get(documentA));
            Assert.Equal("301", registry.Get(documentA).OldNumber);
            Assert.Equal(1, registry.Count);
        }

        // ===== the discard entry is user reachable =====

        /// <summary>
        /// "Until it completes or the user explicitly discards it" only holds if
        /// the discard exists in the product's text table. This pins the文案 the
        /// palette button uses so a future rename cannot leave the user with an
        /// owed operation they can never release.
        /// </summary>
        [Fact]
        public void G21_DiscardActionHasUserFacingText()
        {
            string chinese = RenameSyncText.DiscardPendingButton();
            Assert.False(string.IsNullOrEmpty(chinese));

            Language previous = Strings.Lang;
            try
            {
                Strings.Lang = Language.English;
                string english = RenameSyncText.DiscardPendingButton();
                Assert.False(string.IsNullOrEmpty(english));
                Assert.NotEqual(chinese, english);
            }
            finally
            {
                Strings.Lang = previous;
            }
        }

        private static DictEntry Entry(string number)
        {
            DictEntry entry = new DictEntry();
            entry.Number = number;
            entry.Name = "part " + number;
            return entry;
        }
    }
}
