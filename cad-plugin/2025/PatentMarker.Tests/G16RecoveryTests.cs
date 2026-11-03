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
    /// G16 / R-034：当前 JSON 损坏时，合法的 Word 备份必须仍可恢复。
    ///
    /// 修复前恢复入口把「磁盘上的当前文件」与「面板缓存的模型身份」混为一谈：
    /// 缓存模型解析失败后身份为 null，或与坏文件的新字节不匹配，
    /// ReadSelected 直接返回 PATENTMARKER_CONTENT_CHANGED，用户再也无法从备份恢复。
    ///
    /// 这些用例走修复后的生产入口（RecoverySelection 抓取磁盘真实字节 →
    /// KeepSelectedVersion / RestoreSelectedBackup），不通过测试辅助入口
    /// 预置待裁决状态，也不放宽严格解析。
    /// </summary>
    public class G16RecoveryTests : IDisposable
    {
        private readonly string _dir;
        private string DictPath { get { return Path.Combine(_dir, "t.dict.json"); } }
        private string BackupPath { get { return DictPath + DictConflict.BackupInfix + "20261101-010101.bak"; } }

        private const string WordVersion =
            "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"extracted_at\": \"2026-11-01T01:23:45\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"301\",\n      \"name\": \"Word 版壳体\"\n    }\n  ],\n  \"warnings\": []\n}";

        private const string CadVersion =
            "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"modified_by\": \"cad\",\n    \"modified_at\": \"2026-11-01T02:00:00\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"301\",\n      \"name\": \"CAD 版壳体\"\n    },\n    {\n      \"number\": \"602\",\n      \"name\": \"CAD 版端盖\"\n    }\n  ],\n  \"warnings\": []\n}";

        public G16RecoveryTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pm-g16-" + Guid.NewGuid().ToString("N"));
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

        /// <summary>
        /// Binds the host seam to a stable document whose Name makes
        /// DictLoader.ResolveDictPath() pick this test's dictionary.
        /// The instance is cached: DictPaletteWorkflow.IsCurrentContext compares
        /// documents by reference, exactly as the real ActiveDocument does.
        /// </summary>
        private void BindDocument()
        {
            if (_document == null)
                _document = new Autodesk.AutoCAD.ApplicationServices.Document { Name = Path.Combine(_dir, "t.dwg") };
            Autodesk.AutoCAD.ApplicationServices.Document document = _document;
            RuntimeHost.SetActiveDocumentOverride(delegate { return document; });
        }

        private Autodesk.AutoCAD.ApplicationServices.Document _document;

        private static byte[] Bytes(string path) { return File.ReadAllBytes(path); }

        private static string Identity(string path) { return DictFileVisibility.ReadIdentity(path); }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (a == null || b == null) return a == b;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Corrupts the current file with bytes that are not valid UTF-8.</summary>
        private void WriteBadUtf8(string path)
        {
            byte[] head = Encoding.UTF8.GetBytes("{\"entries\":[{\"number\":\"301\",\"name\":\"");
            byte[] tail = Encoding.UTF8.GetBytes("\"}]}");
            using (FileStream stream = File.Create(path))
            {
                stream.Write(head);
                stream.WriteByte(0xC3);
                stream.WriteByte(0x28);
                stream.Write(tail);
            }
        }

        // ===== capture: selection independent of the parsed model =====

        [Fact]
        public void G16_CaptureSelection_UsesDiskBytesNotCachedModel()
        {
            Reset(WordVersion, CadVersion);
            BindDocument();
            DictConflict.RecoverySelection selection = DictConflict.CaptureSelection(DictPath, BackupPath);
            Assert.NotNull(selection);
            Assert.Equal(Identity(DictPath), selection.CurrentIdentity);
            Assert.Equal(Identity(BackupPath), selection.BackupIdentity);
            Assert.True(selection.CurrentParsed);
            Assert.True(selection.BackupParsed);
        }

        [Fact]
        public void G16_CaptureSelection_StillSelectableWhenCurrentIsCorrupt()
        {
            Reset(null, CadVersion);
            WriteText(DictPath, "{ this is not json");
            BindDocument();
            DictConflict.RecoverySelection selection = DictConflict.CaptureSelection(DictPath, BackupPath);
            Assert.NotNull(selection);
            Assert.False(selection.CurrentParsed);
            Assert.True(selection.BackupParsed);
            // 身份必须来自坏文件本身，不是某个旧模型。
            Assert.Equal(Identity(DictPath), selection.CurrentIdentity);
        }

        [Fact]
        public void G16_CaptureSelection_MissingBackupReturnsNull()
        {
            Reset(WordVersion, null);
            BindDocument();
            Assert.Null(DictConflict.CaptureSelection(DictPath, BackupPath));
        }

        // ===== restore: the actual R-034 scenario =====

        [Fact]
        public void G16_RestoreAfterParseError_SucceedsWithValidBackup()
        {
            Reset(WordVersion, CadVersion);
            BindDocument();

            // 热缓存：先加载一个健康模型，再让当前文件损坏。
            ConfigLoader.Current = new PatConfig { DefaultDictPath = DictPath };
            DictLoader.InvalidateCache();
            DictModel stale = DictLoader.LoadForCurrentDrawing();
            Assert.NotNull(stale);

            WriteText(DictPath, "{ corrupt after the cache was populated");
            Assert.Same(stale, DictLoader.LoadForCurrentDrawing());

            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);
            Assert.NotNull(selection);
            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);

            Assert.True(outcome.Succeeded, outcome.Error);
            Assert.Equal("", outcome.Error);
            Assert.NotNull(outcome.Model);
            Assert.Equal(2, outcome.Model.Entries.Count);
            Assert.Equal("CAD 版端盖", outcome.Model.Entries[1].Name);
            Assert.True(outcome.BackupRemoved);
            Assert.False(File.Exists(BackupPath));

            // 磁盘内容已恢复为 CAD 版，且 CAD 标记被清除。
            string onDisk = File.ReadAllText(DictPath, Encoding.UTF8);
            Assert.Contains("CAD 版壳体", onDisk);
            Assert.DoesNotContain("modified_by", onDisk);
        }

        [Fact]
        public void G16_RestoreAfterExplicitReload_SucceedsWithNullModel()
        {
            Reset(null, CadVersion);
            WriteText(DictPath, "{ corrupt before an explicit reload");
            ConfigLoader.Current = new PatConfig { DefaultDictPath = DictPath };
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            DictModel model = workflow.ReloadCurrent();
            Assert.Null(model);

            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);
            Assert.NotNull(selection);
            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);
            Assert.True(outcome.Succeeded, outcome.Error);
            Assert.Contains("CAD 版端盖", File.ReadAllText(DictPath, Encoding.UTF8));
        }

        [Fact]
        public void G16_RestoreFromColdStart_Succeeds()
        {
            Reset(null, CadVersion);
            WriteText(DictPath, "{ cold start corruption");
            DictLoader.InvalidateCache();
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            Assert.Null(workflow.LoadCurrent());

            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);
            Assert.NotNull(selection);
            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);
            Assert.True(outcome.Succeeded, outcome.Error);
        }

        [Fact]
        public void G16_RestoreFromInvalidUtf8Current_Succeeds()
        {
            Reset(null, CadVersion);
            WriteBadUtf8(DictPath);
            Assert.Null(DictLoader.Load(DictPath));
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);
            Assert.NotNull(selection);
            Assert.False(selection.CurrentParsed);
            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);
            Assert.True(outcome.Succeeded, outcome.Error);
            Assert.Equal(2, outcome.Model.Entries.Count);
        }

        // ===== pending conflict must not depend on a parsed model =====

        [Fact]
        public void G16_IsPendingConflictByPath_IgnoresUnparsableCurrent()
        {
            Reset(null, CadVersion);
            WriteText(DictPath, "{ corrupt");
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            Assert.True(workflow.IsPendingConflict(DictPath));
            // 按模型的重载同样不看模型内容（形参是历史遗留），坏文件下仍为真。
            Assert.True(workflow.IsPendingConflict((DictModel)null, DictPath));
        }

        [Fact]
        public void G16_IsPendingConflictByPath_FalseWithoutBackup()
        {
            Reset(WordVersion, null);
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            Assert.False(workflow.IsPendingConflict(DictPath));
            Assert.False(workflow.IsPendingConflict((string)null));
        }

        // ===== keep-current still requires parsable current content =====

        [Fact]
        public void G16_KeepCurrent_RejectsUnparsableCurrentAndKeepsBackup()
        {
            Reset(null, CadVersion);
            WriteText(DictPath, "{ corrupt, keep must refuse");
            byte[] corrupt = Bytes(DictPath);
            byte[] backup = Bytes(BackupPath);
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);
            Assert.NotNull(selection);
            DictConflict.RecoveryOutcome outcome = workflow.KeepSelectedVersion(selection);

            Assert.False(outcome.Succeeded);
            Assert.False(outcome.BackupRemoved);
            Assert.Equal(DictConflict.CurrentUnparsableError, outcome.Error);
            Assert.True(SameBytes(corrupt, Bytes(DictPath)));
            Assert.True(SameBytes(backup, Bytes(BackupPath)));
            Assert.False(File.Exists(DictPath + ".word-baseline"));
        }

        [Fact]
        public void G16_KeepCurrent_SucceedsOnParsableCurrent()
        {
            Reset(WordVersion, CadVersion);
            byte[] before = Bytes(DictPath);
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);
            DictConflict.RecoveryOutcome outcome = workflow.KeepSelectedVersion(selection);

            Assert.True(outcome.Succeeded, outcome.Error);
            Assert.True(outcome.BackupRemoved);
            Assert.True(SameBytes(before, Bytes(DictPath)));
            Assert.False(File.Exists(BackupPath));
            Assert.Equal(DictFileVisibility.BaselineHeader + DictFileVisibility.BaselineDigest(WordVersion),
                File.ReadAllText(DictPath + ".word-baseline", Encoding.UTF8));
        }

        // ===== stale selections must be refused, data preserved =====

        [Fact]
        public void G16_CurrentChangedAfterCapture_IsRefused()
        {
            Reset(WordVersion, CadVersion);
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);

            WriteText(DictPath, WordVersion.Replace("Word 版壳体", "Word 版第二次导出"));
            byte[] newer = Bytes(DictPath);
            byte[] backup = Bytes(BackupPath);
            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);

            Assert.False(outcome.Succeeded);
            Assert.False(outcome.Wrote);
            Assert.Equal(DictFileVisibility.ChangedError, outcome.Error);
            Assert.True(SameBytes(newer, Bytes(DictPath)));
            Assert.True(SameBytes(backup, Bytes(BackupPath)));
        }

        [Fact]
        public void G16_BackupChangedAfterCapture_IsRefused()
        {
            Reset(WordVersion, CadVersion);
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);

            WriteText(BackupPath, CadVersion.Replace("CAD 版壳体", "CAD 版被外部改写"));
            byte[] changed = Bytes(BackupPath);
            byte[] current = Bytes(DictPath);
            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);

            Assert.False(outcome.Succeeded);
            Assert.False(outcome.Wrote);
            Assert.Equal(DictFileVisibility.ChangedError, outcome.Error);
            Assert.True(SameBytes(current, Bytes(DictPath)));
            Assert.True(SameBytes(changed, Bytes(BackupPath)));
        }

        [Fact]
        public void G16_DocumentSwitchedAfterCapture_IsRefused()
        {
            Reset(WordVersion, CadVersion);
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);

            string otherDir = Path.Combine(_dir, "other");
            Directory.CreateDirectory(otherDir);
            string otherDrawing = Path.Combine(otherDir, "t.dwg");
            RuntimeHost.SetActiveDocumentOverride(delegate
            {
                return new Autodesk.AutoCAD.ApplicationServices.Document { Name = otherDrawing };
            });

            byte[] current = Bytes(DictPath);
            byte[] backup = Bytes(BackupPath);
            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);

            Assert.False(outcome.Succeeded);
            Assert.False(outcome.Wrote);
            Assert.Equal(DictFileVisibility.ChangedError, outcome.Error);
            Assert.True(SameBytes(current, Bytes(DictPath)));
            Assert.True(SameBytes(backup, Bytes(BackupPath)));
        }

        // ===== corrupt backup: refuse, change nothing =====

        [Fact]
        public void G16_CorruptBackup_IsRefusedAndCurrentUntouched()
        {
            Reset(WordVersion, "{ \"entries\": [ this backup is broken");
            byte[] current = Bytes(DictPath);
            byte[] backup = Bytes(BackupPath);
            FileAttributes attributes = File.GetAttributes(BackupPath);
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);
            Assert.NotNull(selection);
            Assert.False(selection.BackupParsed);

            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);
            Assert.False(outcome.Succeeded);
            Assert.False(outcome.Wrote);
            Assert.Equal(DictConflict.BackupUnparsableError, outcome.Error);
            Assert.True(SameBytes(current, Bytes(DictPath)));
            Assert.True(SameBytes(backup, Bytes(BackupPath)));
            Assert.Equal(attributes, File.GetAttributes(BackupPath));
            Assert.False(File.Exists(DictPath + ".word-baseline"));
        }

        // ===== lock / read-only / baseline failures =====

        [Fact]
        public void G16_HeldProductLock_ReportsBusyAndWritesNothing()
        {
            Reset(null, CadVersion);
            WriteText(DictPath, "{ corrupt under a held lock");
            byte[] current = Bytes(DictPath);
            byte[] backup = Bytes(BackupPath);
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);

            DictConflict.RecoveryOutcome outcome;
            using (FileStream held = new FileStream(DictPath + ".patlock",
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                outcome = workflow.RestoreSelectedBackup(selection);
            }

            Assert.False(outcome.Succeeded);
            Assert.False(outcome.Wrote);
            Assert.Equal(DictFileVisibility.BusyError, outcome.Error);
            Assert.True(SameBytes(current, Bytes(DictPath)));
            Assert.True(SameBytes(backup, Bytes(BackupPath)));
        }

        [Fact]
        public void G16_ReadOnlyCurrent_IsNotOverwritten()
        {
            Reset(WordVersion, CadVersion);
            File.SetAttributes(DictPath, FileAttributes.ReadOnly);
            byte[] before = Bytes(DictPath);
            FileAttributes attributes = File.GetAttributes(DictPath);
            try
            {
                BindDocument();
                DictPaletteWorkflow workflow = new DictPaletteWorkflow();
                DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);
                DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);

                Assert.False(outcome.Succeeded);
                Assert.False(outcome.Wrote);
                Assert.True(SameBytes(before, Bytes(DictPath)));
                Assert.Equal(attributes, File.GetAttributes(DictPath));
                Assert.True(File.Exists(BackupPath));
            }
            finally
            {
                File.SetAttributes(DictPath, FileAttributes.Normal);
            }
        }

        [Fact]
        public void G16_BaselineFailure_ReportsWriteAndKeepsRestorePoint()
        {
            Reset(null, CadVersion);
            WriteText(DictPath, "{ corrupt, baseline will fail");
            WriteText(DictPath + ".word-baseline", "unrelated sentinel");
            byte[] sentinel = Bytes(DictPath + ".word-baseline");
            byte[] backup = Bytes(BackupPath);
            try
            {
                BindDocument();
                DictPaletteWorkflow workflow = new DictPaletteWorkflow();
                DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);
                DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);

                // 写入已发生，必须如实说明，而不是假装什么都没做。
                Assert.False(outcome.Succeeded);
                Assert.True(outcome.Wrote);
                Assert.NotNull(outcome.Model);
                Assert.Equal(DictFileVisibility.BaselineError, outcome.Error);
                Assert.Contains("CAD 版端盖", File.ReadAllText(DictPath, Encoding.UTF8));
                Assert.True(SameBytes(sentinel, Bytes(DictPath + ".word-baseline")));
                Assert.True(SameBytes(backup, Bytes(BackupPath)));
            }
            finally
            {
                if (File.Exists(DictPath + ".word-baseline"))
                    File.SetAttributes(DictPath + ".word-baseline", FileAttributes.Normal);
            }
        }

        // ===== post-recovery refresh must not resurrect a stale model =====

        [Fact]
        public void G16_ReloadAfterRecovery_ReturnsModelMatchingDisk()
        {
            Reset(null, CadVersion);
            WriteText(DictPath, "{ corrupt before recovery");
            BindDocument();
            DictPaletteWorkflow workflow = new DictPaletteWorkflow();
            DictConflict.RecoverySelection selection = workflow.CaptureRecoverySelection(DictPath);
            DictConflict.RecoveryOutcome outcome = workflow.RestoreSelectedBackup(selection);
            Assert.True(outcome.Succeeded, outcome.Error);

            DictModel model = workflow.ReloadAfterRecovery(DictPath, outcome.Model);
            Assert.NotNull(model);
            Assert.Equal(2, model.Entries.Count);
            Assert.Equal("602", model.Entries[1].Number);
            // 缓存身份已随自写更新，因此不会再被判为外部变更。
            Assert.False(workflow.IsFileChanged());
        }
    }
}
