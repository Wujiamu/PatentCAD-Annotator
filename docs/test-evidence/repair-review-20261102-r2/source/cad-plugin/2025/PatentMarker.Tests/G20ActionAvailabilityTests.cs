using PatentMarker.IO;
using PatentMarker.Palette;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace PatentMarker.Tests
{
    /// <summary>
    /// G20 / R-034（补修）：裁决动作的可用性按**所选动作**判断。
    ///
    /// 修复前 <c>DictPaletteControl.BtnArbitrate_Click</c> 把 <c>BackupParsed</c>
    /// 当成整个对话框的入口条件：当前字典合法、最新备份是损坏文本时，事件直接
    /// 返回「裁决失败: Backup dictionary cannot be parsed.」，用户连「采用 Word 版」
    /// 都选不了。而同一快照下 <c>DictConflict.KeepSelectedVersion</c> 本来就能成功。
    ///
    /// 修复后：<c>CurrentParsed</c> 只约束「采用 Word 版」，<c>BackupParsed</c> 只约束
    /// 「恢复 CAD 版」；两者均坏时给出明确原因并保留「稍后」。损坏备份一律不被自动
    /// 删除，也不覆盖合法当前文件。
    ///
    /// 这些用例固定的是动作可用性契约（<see cref="DictPaletteWorkflow.IsActionAvailable"/>）
    /// 与底层动作本身；「面板入口是否真的打开对话框、按钮是否可点」由同一轮
    /// L1 探针（docs/test-evidence/repair-review-20261102/G20）在生产 Control 与真实
    /// 模态对话框上验证，不在本文件冒充。
    ///
    /// 四格的好／坏取**实际严格解析结果**（<c>CaptureRecoverySelection</c> 读磁盘字节
    /// 后调用生产解析器），不由测试布尔值预置；若语料没有解析成预期状态，断言会
    /// 在 fixture 行先失败，而不是悄悄测了另一格。
    /// </summary>
    public class G20ActionAvailabilityTests : IDisposable
    {
        private readonly string _dir;
        private Autodesk.AutoCAD.ApplicationServices.Document _document;

        private string DictPath { get { return Path.Combine(_dir, "t.dict.json"); } }
        private string BackupPath { get { return DictPath + DictConflict.BackupInfix + "20261102-010101.bak"; } }

        private const string WordVersion =
            "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"extracted_at\": \"2026-11-02T01:23:45\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"301\",\n      \"name\": \"Word 版壳体\"\n    }\n  ],\n  \"warnings\": []\n}";

        private const string CadVersion =
            "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"modified_by\": \"cad\",\n    \"modified_at\": \"2026-11-02T02:00:00\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"301\",\n      \"name\": \"CAD 版壳体\"\n    },\n    {\n      \"number\": \"602\",\n      \"name\": \"CAD 版端盖\"\n    }\n  ],\n  \"warnings\": []\n}";

        private const string BadCurrent = "{ this current file is not json";
        private const string BadBackup = "{ this backup file is not json";

        public G20ActionAvailabilityTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pm-g20-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try
            {
                foreach (string path in Directory.GetFiles(_dir)) File.SetAttributes(path, FileAttributes.Normal);
                Directory.Delete(_dir, true);
            }
            catch { }
            DictLoader.InvalidateCache();
            ConfigLoader.Current = null;
            RuntimeHost.ClearActiveDocumentOverride();
        }

        // ===== helpers =====

        private void WriteText(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private void Reset(string current, string backup)
        {
            DictLoader.InvalidateCache();
            ConfigLoader.Current = null;
            foreach (string path in Directory.GetFiles(_dir)) File.SetAttributes(path, FileAttributes.Normal);
            foreach (string path in Directory.GetFiles(_dir)) File.Delete(path);
            if (current != null) WriteText(DictPath, current);
            if (backup != null) WriteText(BackupPath, backup);
        }

        private void BindDocument()
        {
            if (_document == null)
                _document = new Autodesk.AutoCAD.ApplicationServices.Document
                { Name = Path.Combine(_dir, "t.dwg") };
            Autodesk.AutoCAD.ApplicationServices.Document document = _document;
            RuntimeHost.SetActiveDocumentOverride(delegate { return document; });
        }

        /// <summary>
        /// Captures the selection through the production entry, so CurrentParsed
        /// and BackupParsed are the real strict parse results for these bytes.
        /// </summary>
        private DictConflict.RecoverySelection Capture()
        {
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);
            Assert.NotNull(selection);
            return selection;
        }

        private static byte[] Bytes(string path) { return File.ReadAllBytes(path); }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // ===== the four cells: availability is per action =====

        [Fact]
        public void G20_GoodCurrentGoodBackup_BothActionsAvailable()
        {
            Reset(WordVersion, CadVersion);
            DictConflict.RecoverySelection selection = Capture();

            Assert.True(selection.CurrentParsed);
            Assert.True(selection.BackupParsed);
            Assert.True(DictPaletteWorkflow.IsActionAvailable(selection, DictPaletteWorkflow.KeepWordAction));
            Assert.True(DictPaletteWorkflow.IsActionAvailable(selection, DictPaletteWorkflow.RestoreCadAction));
            Assert.Equal("", DictPaletteWorkflow.DescribeUnavailableActions(selection));
        }

        [Fact]
        public void G20_BadCurrentGoodBackup_OnlyRestoreAvailable()
        {
            Reset(BadCurrent, CadVersion);
            DictConflict.RecoverySelection selection = Capture();

            Assert.False(selection.CurrentParsed);
            Assert.True(selection.BackupParsed);
            Assert.False(DictPaletteWorkflow.IsActionAvailable(selection, DictPaletteWorkflow.KeepWordAction));
            Assert.True(DictPaletteWorkflow.IsActionAvailable(selection, DictPaletteWorkflow.RestoreCadAction));

            // 原因只说清「采用」为什么不可用，不能连「恢复」一起否定。
            string reason = DictPaletteWorkflow.DescribeUnavailableActions(selection);
            Assert.Contains(DictConflict.CurrentUnparsableError, reason);
            Assert.DoesNotContain(DictConflict.BackupUnparsableError, reason);
        }

        /// <summary>本轮缺陷所在格：当前合法、备份损坏。</summary>
        [Fact]
        public void G20_GoodCurrentBadBackup_KeepAvailableRestoreDisabled()
        {
            Reset(WordVersion, BadBackup);
            DictConflict.RecoverySelection selection = Capture();

            Assert.True(selection.CurrentParsed);
            Assert.False(selection.BackupParsed);
            Assert.True(DictPaletteWorkflow.IsActionAvailable(selection, DictPaletteWorkflow.KeepWordAction));
            Assert.False(DictPaletteWorkflow.IsActionAvailable(selection, DictPaletteWorkflow.RestoreCadAction));

            string reason = DictPaletteWorkflow.DescribeUnavailableActions(selection);
            Assert.Contains(DictConflict.BackupUnparsableError, reason);
            Assert.DoesNotContain(DictConflict.CurrentUnparsableError, reason);
        }

        [Fact]
        public void G20_BothBad_NoActionAvailableWithBothReasons()
        {
            Reset(BadCurrent, BadBackup);
            DictConflict.RecoverySelection selection = Capture();

            Assert.False(selection.CurrentParsed);
            Assert.False(selection.BackupParsed);
            Assert.False(DictPaletteWorkflow.IsActionAvailable(selection, DictPaletteWorkflow.KeepWordAction));
            Assert.False(DictPaletteWorkflow.IsActionAvailable(selection, DictPaletteWorkflow.RestoreCadAction));

            string reason = DictPaletteWorkflow.DescribeUnavailableActions(selection);
            Assert.Contains(DictConflict.CurrentUnparsableError, reason);
            Assert.Contains(DictConflict.BackupUnparsableError, reason);
        }

        [Fact]
        public void G20_UnknownActionAndNullSelection_AreUnavailable()
        {
            Reset(WordVersion, CadVersion);
            DictConflict.RecoverySelection selection = Capture();

            Assert.False(DictPaletteWorkflow.IsActionAvailable(null, DictPaletteWorkflow.KeepWordAction));
            Assert.False(DictPaletteWorkflow.IsActionAvailable(null, DictPaletteWorkflow.RestoreCadAction));
            Assert.False(DictPaletteWorkflow.IsActionAvailable(selection, "merge"));
            Assert.False(DictPaletteWorkflow.IsActionAvailable(selection, null));
            Assert.Equal("", DictPaletteWorkflow.DescribeUnavailableActions(null));
        }

        // ===== the defect scenario, on the production action entry =====

        /// <summary>
        /// 当前合法 + 备份损坏：底层「采用」本来就能成功（封存记录
        /// <c>valid-current-keep-component-works</c>），所以入口不应以备份为由拒绝。
        /// 这里断言动作层：采用成功、当前字节不变、损坏备份被清理，且不产生
        /// 任何「无法解析」的失败。
        /// </summary>
        [Fact]
        public void G20_KeepSelectedVersion_SucceedsWithCorruptBackup()
        {
            Reset(WordVersion, BadBackup);
            byte[] currentBefore = Bytes(DictPath);
            DictConflict.RecoverySelection selection = Capture();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();

            DictConflict.RecoveryOutcome outcome = workflow.KeepSelectedVersion(selection);

            Assert.True(outcome.Succeeded, outcome.Error);
            Assert.Equal("", outcome.Error);
            Assert.True(outcome.BackupRemoved);
            Assert.True(SameBytes(currentBefore, Bytes(DictPath)));
            Assert.False(File.Exists(BackupPath));
            Assert.DoesNotContain(DictConflict.BackupUnparsableError, outcome.Error);
        }

        /// <summary>
        /// 同一格里「恢复」仍必须拒绝：损坏备份不得覆盖合法当前文件，
        /// 当前字节与属性、备份字节与属性都不变。
        /// </summary>
        [Fact]
        public void G20_RestoreSelectedBackup_StillRefusesCorruptBackup()
        {
            Reset(WordVersion, BadBackup);
            byte[] currentBefore = Bytes(DictPath);
            byte[] backupBefore = Bytes(BackupPath);
            FileAttributes currentAttributes = File.GetAttributes(DictPath);
            FileAttributes backupAttributes = File.GetAttributes(BackupPath);
            DictConflict.RecoverySelection selection = Capture();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();

            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);

            Assert.False(outcome.Succeeded);
            Assert.False(outcome.Wrote);
            Assert.Equal(DictConflict.BackupUnparsableError, outcome.Error);
            Assert.True(SameBytes(currentBefore, Bytes(DictPath)));
            Assert.True(SameBytes(backupBefore, Bytes(BackupPath)));
            Assert.Equal(currentAttributes, File.GetAttributes(DictPath));
            Assert.Equal(backupAttributes, File.GetAttributes(BackupPath));
        }

        /// <summary>
        /// 「稍后」等价物：不调用任何动作时，两端文件（含损坏备份）保持原样。
        /// 采用当前版本必须由用户显式选择，入口不得自动删坏备份。
        /// </summary>
        [Fact]
        public void G20_NoActionLeavesBothFilesUntouched()
        {
            Reset(WordVersion, BadBackup);
            byte[] currentBefore = Bytes(DictPath);
            byte[] backupBefore = Bytes(BackupPath);
            DictConflict.RecoverySelection selection = Capture();

            // 只是取得上下文与可用性，不做任何写入。
            Assert.True(DictPaletteWorkflow.IsActionAvailable(selection, DictPaletteWorkflow.KeepWordAction));
            Assert.False(DictPaletteWorkflow.IsActionAvailable(selection, DictPaletteWorkflow.RestoreCadAction));

            Assert.True(SameBytes(currentBefore, Bytes(DictPath)));
            Assert.True(SameBytes(backupBefore, Bytes(BackupPath)));
            Assert.False(File.Exists(DictPath + ".word-baseline"));
        }

        // ===== the current / backup changing while the dialog is open =====

        [Fact]
        public void G20_CurrentChangedAfterCapture_KeepRefusedAndFilesKept()
        {
            Reset(WordVersion, CadVersion);
            DictConflict.RecoverySelection selection = Capture();
            WriteText(DictPath, WordVersion.Replace("Word 版壳体", "Word 版第二次导出"));
            byte[] newer = Bytes(DictPath);
            byte[] backup = Bytes(BackupPath);
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();

            DictConflict.RecoveryOutcome outcome = workflow.KeepSelectedVersion(selection);

            Assert.False(outcome.Succeeded);
            Assert.False(outcome.Wrote);
            Assert.Equal(DictFileVisibility.ChangedError, outcome.Error);
            Assert.True(SameBytes(newer, Bytes(DictPath)));
            Assert.True(SameBytes(backup, Bytes(BackupPath)));
        }

        [Fact]
        public void G20_BackupChangedAfterCapture_RestoreRefusedAndFilesKept()
        {
            Reset(WordVersion, CadVersion);
            DictConflict.RecoverySelection selection = Capture();
            WriteText(BackupPath, CadVersion.Replace("CAD 版壳体", "CAD 版被外部改写"));
            byte[] current = Bytes(DictPath);
            byte[] changed = Bytes(BackupPath);
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();

            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);

            Assert.False(outcome.Succeeded);
            Assert.False(outcome.Wrote);
            Assert.Equal(DictFileVisibility.ChangedError, outcome.Error);
            Assert.True(SameBytes(current, Bytes(DictPath)));
            Assert.True(SameBytes(changed, Bytes(BackupPath)));
        }

        /// <summary>
        /// 打开后备份被改写成「更坏」的内容：可用性来自打开时的快照，
        /// 但动作入口会按当前字节复核，因此不会拿一个已经变了的文件去覆盖。
        /// </summary>
        [Fact]
        public void G20_BackupBecomesCorruptAfterCapture_RestoreRefusedAndFilesKept()
        {
            Reset(WordVersion, CadVersion);
            DictConflict.RecoverySelection selection = Capture();
            WriteText(BackupPath, BadBackup);
            byte[] current = Bytes(DictPath);
            byte[] changed = Bytes(BackupPath);
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();

            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);

            Assert.False(outcome.Succeeded);
            Assert.False(outcome.Wrote);
            Assert.Equal(DictFileVisibility.ChangedError, outcome.Error);
            Assert.True(SameBytes(current, Bytes(DictPath)));
            Assert.True(SameBytes(changed, Bytes(BackupPath)));
        }
    }
}
