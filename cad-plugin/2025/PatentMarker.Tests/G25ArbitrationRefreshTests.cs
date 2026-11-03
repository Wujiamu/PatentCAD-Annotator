using PatentMarker.IO;
using PatentMarker.Palette;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace PatentMarker.Tests
{
    /// <summary>
    /// G25 / R-034（补修）：裁决结束后的视图必须与**真实当前文件**一致。
    ///
    /// 失败契约（封存于 `palette-matrix-final.txt` 的 A3 四格）：当前 301、合法备份 201；
    /// 恢复已经把备份内容写到当前字典（`DictLoader.NotifySelfWrite` 使缓存身份等于
    /// 新文件），随后基线路径被目录阻挡或备份不能删除。用户选「稍后」，或再点一次
    /// 失败再「稍后」，面板仍显示 301，而磁盘与加载器都已是 201。
    ///
    /// 根因两层：
    ///  1. `DictPaletteControl.BtnArbitrate_Click` 只在 `DialogResult.OK/Yes` 时刷新；
    ///     且刷新走 `IsFileChanged()`／`ReloadAfterRecovery`，而 `NotifySelfWrite` 已让
    ///     缓存身份等于新文件，轮询判「没变化」，陈旧视图可以长期不动。
    ///  2. `ArbitrateDialog` 只有单个 `Outcome`，第二次点击的「内容已变化」会覆盖第一次
    ///     真实的 `Wrote`，于是「是否写过盘」无法从最后一次结果读到。
    ///
    /// 本文件固定会话事实（`DictPaletteWorkflow.ArbitrationSessionFact`）与「按真实字节
    /// 刷新」（`DictPaletteWorkflow.ReadCanonicalSnapshot`）这两个契约；真实按钮与真实
    /// 模态对话框的调用链证据在 `docs/test-evidence/repair-review-20261102-r2/G25/` 的
    /// 红绿探针里（同一份业务断言，`-p:G25Snapshot=red|green` 只换生产源码快照）。
    /// </summary>
    public class G25ArbitrationRefreshTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _dictPath;
        private Autodesk.AutoCAD.ApplicationServices.Document? _document;

        private const string CurrentJson =
            "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"extracted_at\": \"2026-11-02T01:23:45\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"301\",\n      \"name\": \"当前版壳体\"\n    }\n  ],\n  \"warnings\": []\n}";

        private const string BackupJson =
            "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"modified_by\": \"cad\",\n    \"modified_at\": \"2026-11-02T02:00:00\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"201\",\n      \"name\": \"CAD 版壳体\"\n    }\n  ],\n  \"warnings\": []\n}";

        private string BackupPath { get { return _dictPath + DictConflict.BackupInfix + "20261102-010101.bak"; } }
        private string BaselinePath { get { return _dictPath + ".word-baseline"; } }

        public G25ArbitrationRefreshTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pm-g25-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _dictPath = Path.Combine(_dir, "t.dict.json");
        }

        public void Dispose()
        {
            try
            {
                foreach (string file in Directory.GetFiles(_dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(_dir, true);
            }
            catch { }
            DictLoader.InvalidateCache();
            ConfigLoader.Current = null;
            RuntimeHost.ClearActiveDocumentOverride();
        }

        // ===== helpers =====

        private void Reset(string current, string backup)
        {
            DictLoader.InvalidateCache();
            ConfigLoader.Current = null;
            foreach (string file in Directory.GetFiles(_dir)) File.SetAttributes(file, FileAttributes.Normal);
            foreach (string file in Directory.GetFiles(_dir)) File.Delete(file);
            foreach (string dir in Directory.GetDirectories(_dir)) Directory.Delete(dir, true);
            WriteText(_dictPath, current);
            WriteText(BackupPath, backup);
        }

        private static void WriteText(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private void BindDocument()
        {
            if (_document == null)
                _document = new Autodesk.AutoCAD.ApplicationServices.Document
                { Name = Path.Combine(_dir, "t.dwg") };
            Autodesk.AutoCAD.ApplicationServices.Document document = _document!;
            RuntimeHost.SetActiveDocumentOverride(delegate { return document; });
        }

        private DictConflict.RecoverySelection Capture()
        {
            BindDocument();
            DictConflict.RecoverySelection selection =
                new DictPaletteWorkflow().CaptureRecoverySelection(_dictPath);
            Assert.NotNull(selection);
            return selection;
        }

        private static string Number(DictModel? model)
        {
            Assert.NotNull(model);
            Assert.NotEmpty(model!.Entries);
            return model.Entries[0].Number;
        }

        /// <summary>The number the loader reports for the real file.</summary>
        private string LoadedNumber()
        {
            return Number(DictLoader.Load(_dictPath));
        }

        // ===== 1. the session fact must survive a later failed attempt =====

        /// <summary>
        /// 一次写盘 + 基线失败，然后再点一次只得到「内容已变化」的拒绝：
        /// 会话事实必须仍报告「写过盘」，而最后一次 Outcome 已经是 Wrote=false。
        /// 这正是「不能只看最后一次 Outcome」的契约。
        /// </summary>
        [Fact]
        public void G25_SessionFact_KeepsWriteWhenLaterAttemptReportsNoWrite()
        {
            Reset(CurrentJson, BackupJson);
            Directory.CreateDirectory(BaselinePath);
            DictConflict.RecoverySelection selection = Capture();
            DictPaletteWorkflow.ArbitrationSessionFact fact = new DictPaletteWorkflow.ArbitrationSessionFact();

            DictConflict.RecoveryOutcome first = DictConflict.RestoreSelectedBackup(
                selection, DictPaletteWorkflow.ContextGuard(selection));
            fact.Record(first);
            Assert.True(first.Wrote, first.Error);
            Assert.False(first.Succeeded);
            Assert.Equal(DictFileVisibility.BaselineError, first.Error);

            // The dictionary bytes are already the restored version.
            Assert.Equal("201", LoadedNumber());

            // A second click cannot write again: the selection snapshot is stale.
            DictConflict.RecoveryOutcome second = DictConflict.RestoreSelectedBackup(
                selection, DictPaletteWorkflow.ContextGuard(selection));
            fact.Record(second);
            Assert.False(second.Wrote);
            Assert.False(second.Succeeded);

            Assert.Equal(2, fact.Attempts);
            Assert.True(fact.AnyWrote);
            Assert.False(fact.AnySucceeded);
            Assert.False(fact.LastSucceeded);
            Assert.Equal(DictFileVisibility.BaselineError, fact.WriteError);
            Assert.Equal(DictFileVisibility.ChangedError, fact.LastError);
        }

        /// <summary>
        /// 「稍后」不是一次尝试：会话事实保持空，面板不得据此编造成功或失败。
        /// </summary>
        [Fact]
        public void G25_SessionFact_LaterDoesNotRecordAnAttempt()
        {
            Reset(CurrentJson, BackupJson);
            Capture();
            DictPaletteWorkflow.ArbitrationSessionFact fact = new DictPaletteWorkflow.ArbitrationSessionFact();

            Assert.Equal(0, fact.Attempts);
            Assert.False(fact.AnyWrote);
            Assert.False(fact.AnySucceeded);
            Assert.Equal("", fact.WriteError);

            // Both files untouched.
            Assert.True(File.Exists(BackupPath));
            Assert.Equal("301", LoadedNumber());
        }

        /// <summary>
        /// 备份清理失败也保留「已写盘」事实，且不谎报整体成功。
        /// </summary>
        [Fact]
        public void G25_SessionFact_BackupDeletionFailureKeepsWriteAndNoSuccess()
        {
            Reset(CurrentJson, BackupJson);
            DictConflict.RecoverySelection selection = Capture();
            DictPaletteWorkflow.ArbitrationSessionFact fact = new DictPaletteWorkflow.ArbitrationSessionFact();

            DictConflict.RecoveryOutcome outcome;
            using (FileStream held = new FileStream(BackupPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                outcome = DictConflict.RestoreSelectedBackup(
                    selection, DictPaletteWorkflow.ContextGuard(selection));
            }
            fact.Record(outcome);

            Assert.True(outcome.Wrote, outcome.Error);
            Assert.False(outcome.Succeeded);
            Assert.False(outcome.BackupRemoved);
            Assert.True(fact.AnyWrote);
            Assert.False(fact.AnySucceeded);
            Assert.True(fact.WriteError.Length > 0);
            Assert.True(File.Exists(BackupPath));
            Assert.Equal("201", LoadedNumber());
        }

        /// <summary>完整成功：会话事实报告成功，且备份已清理。</summary>
        [Fact]
        public void G25_SessionFact_CompleteSuccessIsRecorded()
        {
            Reset(CurrentJson, BackupJson);
            DictConflict.RecoverySelection selection = Capture();
            DictPaletteWorkflow.ArbitrationSessionFact fact = new DictPaletteWorkflow.ArbitrationSessionFact();

            DictConflict.RecoveryOutcome outcome = DictConflict.RestoreSelectedBackup(
                selection, DictPaletteWorkflow.ContextGuard(selection));
            fact.Record(outcome);

            Assert.True(outcome.Succeeded, outcome.Error);
            Assert.True(fact.AnySucceeded);
            Assert.True(fact.AnyWrote);
            Assert.True(fact.AnyBackupRemoved);
            Assert.Equal("", fact.WriteError);
            Assert.False(File.Exists(BackupPath));
            Assert.Equal("201", LoadedNumber());
        }

        // ===== 2. the refresh reads the real bytes, never the cached identity =====

        /// <summary>
        /// 写盘后基线失败：缓存身份已被 `NotifySelfWrite` 登记成新文件，因此
        /// `IsFileChanged()` 为 false（轮询永远不会纠正旧视图）。按真实字节读取
        /// 必须仍拿到恢复后的模型。
        /// </summary>
        [Fact]
        public void G25_ReadCanonicalSnapshot_SeesRestoredBytesDespiteSelfWriteBaseline()
        {
            Reset(CurrentJson, BackupJson);
            Directory.CreateDirectory(BaselinePath);
            DictConflict.RecoverySelection selection = Capture();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();

            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);
            Assert.True(outcome.Wrote, outcome.Error);
            Assert.False(outcome.Succeeded);

            // The defect's precondition: the poll can no longer see a change.
            Assert.False(workflow.IsFileChanged());

            // The real bytes still describe the restored dictionary.
            RenameSnapshotRead read = workflow.ReadCanonicalSnapshot(_document);
            Assert.True(read.Read, read.Error);
            Assert.True(read.Parsed, read.Error);
            Assert.Equal("201", Number(read.Model));
            Assert.Equal(DictFileVisibility.ReadIdentity(_dictPath), read.DictIdentity);
        }

        /// <summary>
        /// 正常成功路径同样必须从真实字节读到 201（对照格）。
        /// </summary>
        [Fact]
        public void G25_ReadCanonicalSnapshot_NormalSuccessSeesRestoredBytes()
        {
            Reset(CurrentJson, BackupJson);
            DictConflict.RecoverySelection selection = Capture();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();

            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);
            Assert.True(outcome.Succeeded, outcome.Error);

            RenameSnapshotRead read = workflow.ReadCanonicalSnapshot(_document);
            Assert.True(read.Parsed, read.Error);
            Assert.Equal("201", Number(read.Model));
        }

        /// <summary>
        /// 写前就失败（目标只读）：磁盘仍是 301，真实字节读取也必须仍是 301，
        /// 备份与基线都不变——刷新不得把「没写」显示成「写了」。
        /// </summary>
        [Fact]
        public void G25_ReadCanonicalSnapshot_PreWriteFailureStillShowsOldBytes()
        {
            Reset(CurrentJson, BackupJson);
            byte[] before = File.ReadAllBytes(_dictPath);
            DictConflict.RecoverySelection selection = Capture();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();

            File.SetAttributes(_dictPath, FileAttributes.ReadOnly);
            try
            {
                DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);
                Assert.False(outcome.Wrote);
                Assert.False(outcome.Succeeded);
            }
            finally
            {
                File.SetAttributes(_dictPath, FileAttributes.Normal);
            }

            RenameSnapshotRead read = workflow.ReadCanonicalSnapshot(_document);
            Assert.True(read.Parsed, read.Error);
            Assert.Equal("301", Number(read.Model));
            Assert.Equal(DictFileVisibility.Identity(before), read.DictIdentity);
            Assert.True(File.Exists(BackupPath));
            Assert.False(Directory.Exists(BaselinePath));
        }

        /// <summary>
        /// 当前文件被目录顶替（无法读字节）：读取结果必须明确「读不出」，
        /// 不能当成空字典，也不能把旧模型当权威。
        /// </summary>
        [Fact]
        public void G25_ReadCanonicalSnapshot_RefusesDirectoryInsteadOfFile()
        {
            Reset(CurrentJson, BackupJson);
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            BindDocument();

            File.Delete(_dictPath);
            Directory.CreateDirectory(_dictPath);
            try
            {
                RenameSnapshotRead read = workflow.ReadCanonicalSnapshot(_document);
                Assert.False(read.Read);
                Assert.False(read.Parsed);
                Assert.Null(read.Model);
                Assert.True(read.Error.Length > 0);
            }
            finally
            {
                Directory.Delete(_dictPath, true);
            }
        }

        // ===== 3. the refresh must not cross drawings =====

        /// <summary>
        /// 活动图纸不是被裁决的那张时，规范路径解析到的是当前图纸自己的字典：
        /// 绝不把原图纸模型显示到另一活动图纸上。
        /// </summary>
        [Fact]
        public void G25_Refresh_DoesNotShowArbitratedDrawingOnAnotherActiveDrawing()
        {
            Reset(CurrentJson, BackupJson);
            Directory.CreateDirectory(BaselinePath);
            DictConflict.RecoverySelection selection = Capture();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();

            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);
            Assert.True(outcome.Wrote, outcome.Error);
            Assert.Equal("201", LoadedNumber());

            // Another drawing becomes active: its dictionary holds 501.
            string otherDict = Path.Combine(_dir, "b.dict.json");
            WriteText(otherDict, "{\"metadata\":{},\"entries\":[{\"number\":\"501\",\"name\":\"b\"}],\"warnings\":[]}");
            Autodesk.AutoCAD.ApplicationServices.Document other = new Autodesk.AutoCAD.ApplicationServices.Document
            { Name = Path.Combine(_dir, "b.dwg") };
            Autodesk.AutoCAD.ApplicationServices.Document document = other;
            RuntimeHost.SetActiveDocumentOverride(delegate { return document; });

            Assert.False(DictPaletteWorkflow.IsCurrentContext(selection.Document, selection.DictPath));
            Assert.Equal(otherDict, DictPaletteWorkflow.ResolveCanonicalPath(other));

            RenameSnapshotRead read = workflow.ReadCanonicalSnapshot(other);
            Assert.True(read.Parsed, read.Error);
            Assert.Equal("501", Number(read.Model));
            // The arbitrated drawing still holds the restored content.
            Assert.Equal("201", LoadedNumber());
        }

        /// <summary>
        /// 图纸没有字典时规范路径解析不到：刷新应当显示「未加载」，而不是继续挂着
        /// 上一份内容或把别的路径当成本图纸的字典。
        /// </summary>
        [Fact]
        public void G25_Refresh_NoCanonicalPathReportsMissingDictionary()
        {
            Reset(CurrentJson, BackupJson);
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();

            File.Delete(_dictPath);
            Assert.Null(DictPaletteWorkflow.ResolveCanonicalPath(_document));

            RenameSnapshotRead read = workflow.ReadCanonicalSnapshot(_document);
            Assert.False(read.Read);
            Assert.False(read.Parsed);
            Assert.Null(read.Model);
        }
    }
}
