using PatentMarker.IO;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace PatentMarker.Tests
{
    /// <summary>
    /// DictConflict（v4.0 功能 3）单测：备份查找 / 待裁决判定 / 两种裁决动作。
    /// 全部基于临时目录文件操作，与 AutoCAD 无关。
    /// </summary>
    public class DictConflictTests : IDisposable
    {
        private readonly string _dir;
        private string DictPath => Path.Combine(_dir, "test.dict.json");

        public DictConflictTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "pm-conflict-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, true); } catch { }
        }

        private void WriteFile(string path, string content)
        {
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private void MakeBackup(string ts, string content)
        {
            WriteFile(DictPath + DictConflict.BackupInfix + ts + ".bak", content);
        }

        private static string MakeJson(string? modifiedBy = null)
        {
            string mark = modifiedBy != null
                ? ",\n    \"modified_by\": \"" + modifiedBy + "\""
                : "";
            return "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\"" + mark +
                   "\n  },\n  \"entries\": [],\n  \"warnings\": []\n}";
        }

        // ===== FindWordBackup =====

        [Fact]
        public void FindWordBackup_NoBackup_ReturnsNull()
        {
            WriteFile(DictPath, MakeJson());
            Assert.Null(DictConflict.FindWordBackup(DictPath));
        }

        [Fact]
        public void FindWordBackup_PicksLatestByName()
        {
            MakeBackup("20260703-101500", MakeJson("cad"));
            MakeBackup("20260704-090000", MakeJson("cad"));
            string? found = DictConflict.FindWordBackup(DictPath);
            Assert.NotNull(found);
            Assert.EndsWith("20260704-090000.bak", Path.GetFileName(found));
        }

        [Fact]
        public void FindWordBackup_IgnoresMalformedTimestamp()
        {
            // 前缀匹配但时间戳段不足 15 字符（yyyymmdd-hhnnss）→ 跳过
            WriteFile(DictPath + DictConflict.BackupInfix + "bad-name.bak", MakeJson("cad"));
            Assert.Null(DictConflict.FindWordBackup(DictPath));
        }

        [Fact]
        public void FindWordBackup_IgnoresLongMalformedTimestamp()
        {
            WriteFile(DictPath + DictConflict.BackupInfix + "bad-name-that-is-long.bak", MakeJson("cad"));
            Assert.Null(DictConflict.FindWordBackup(DictPath));
        }

        [Fact]
        public void FindWordBackup_IgnoresOtherBackups()
        {
            // 不同主名的 .bak（other.dict.json.word-*）不匹配 test.dict.json 前缀
            WriteFile(Path.Combine(_dir, "other.dict.json.word-20260704-090000.bak"), MakeJson("cad"));
            WriteFile(DictPath, MakeJson());
            Assert.Null(DictConflict.FindWordBackup(DictPath));
        }

        [Fact]
        public void FindWordBackup_NullOrEmptyPath_ReturnsNull()
        {
            Assert.Null(DictConflict.FindWordBackup(null));
            Assert.Null(DictConflict.FindWordBackup(""));
            Assert.Null(DictConflict.FindWordBackup(Path.Combine(_dir, "no-such-dir", "x.json")));
        }

        // ===== IsPendingConflict =====

        [Fact]
        public void IsPendingConflict_NoBackup_False()
        {
            WriteFile(DictPath, MakeJson());
            Assert.False(DictConflict.IsPendingConflict(new DictModel(), DictPath));
        }

        [Fact]
        public void IsPendingConflict_BackupAndNoCadMark_True()
        {
            WriteFile(DictPath, MakeJson(null));   // Word 最新导出，无 CAD 标记
            MakeBackup("20260704-090000", MakeJson("cad"));
            Assert.True(DictConflict.IsPendingConflict(new DictModel(), DictPath));
        }

        [Fact]
        public void IsPendingConflict_BackupAndCadMark_False()
        {
            // 当前 JSON 仍有 CAD 标记 → CAD 版本仍是最新，无需裁决
            WriteFile(DictPath, MakeJson("cad"));
            MakeBackup("20260704-090000", MakeJson("cad"));
            DictModel? current = DictLoader.Load(DictPath);   // 从磁盘读回带标记的模型
            Assert.False(DictConflict.IsPendingConflict(current, DictPath));
        }

        [Fact]
        public void IsPendingConflict_NullPath_False()
        {
            Assert.False(DictConflict.IsPendingConflict(new DictModel(), null));
            Assert.False(DictConflict.IsPendingConflict(new DictModel(), ""));
        }

        // ===== ResolveKeepWord =====

        [Fact]
        public void ResolveKeepWord_DeletesBackup()
        {
            string backup = DictPath + DictConflict.BackupInfix + "20260704-090000.bak";
            MakeBackup("20260704-090000", MakeJson("cad"));
            string error;
            Assert.True(DictConflict.ResolveKeepWord(backup, out error));
            Assert.Equal("", error);
            Assert.False(File.Exists(backup));
        }

        [Fact]
        public void ResolveKeepWord_MissingBackup_StillTrue()
        {
            string backup = DictPath + DictConflict.BackupInfix + "20260704-090000.bak";
            string error;
            Assert.True(DictConflict.ResolveKeepWord(backup, out error));
            Assert.Equal("", error);
        }

        // ===== ResolveRestoreCad =====

        [Fact]
        public void ResolveRestoreCad_RestoresBackupAndClearsMark()
        {
            // Word 版覆盖了 CAD 版，备份中是带 CAD 标记的旧版（含 2 条目）
            string backupContent = "{\n  \"metadata\": {\n    \"source_file\": \"t.docx\",\n    \"modified_by\": \"cad\",\n    \"modified_at\": \"2026-07-04T10:00:00\"\n  },\n  \"entries\": [\n    {\n      \"number\": \"1\",\n      \"name\": \"壳体\"\n    },\n    {\n      \"number\": \"2\",\n      \"name\": \"端盖\"\n    }\n  ],\n  \"warnings\": []\n}";
            WriteFile(DictPath, MakeJson(null));   // 当前 = Word 最新导出
            string backup = DictPath + DictConflict.BackupInfix + "20260704-090000.bak";
            WriteFile(backup, backupContent);

            string error;
            DictModel? restored = DictConflict.ResolveRestoreCad(DictPath, backup, out error);

            Assert.NotNull(restored);
            Assert.Equal("", error);
            Assert.Equal(2, restored!.Entries.Count);
            Assert.Equal("壳体", restored.Entries[0].Name);

            // 标记已清除（防循环提示）
            Assert.Null(restored.Metadata.ModifiedBy);
            Assert.Null(restored.Metadata.ModifiedAt);

            // 备份已删除
            Assert.False(File.Exists(backup));

            // dict.json 内容已恢复为 CAD 版
            string onDisk = File.ReadAllText(DictPath);
            Assert.Contains("壳体", onDisk);
            Assert.DoesNotContain("modified_by", onDisk);
        }

        [Fact]
        public void ResolveRestoreCad_MissingBackup_ReturnsNullWithError()
        {
            WriteFile(DictPath, MakeJson());
            string backup = DictPath + DictConflict.BackupInfix + "20260704-090000.bak";
            string error;
            DictModel? restored = DictConflict.ResolveRestoreCad(DictPath, backup, out error);
            Assert.Null(restored);
            Assert.NotEqual("", error);
        }

        [Fact]
        public void ResolveRestoreCad_InvalidBackup_DoesNotOverwriteCurrent()
        {
            string current = "{\n  \"metadata\": {},\n  \"entries\": [{\"number\":\"keep\",\"name\":\"current\"}],\n  \"warnings\": []\n}";
            WriteFile(DictPath, current);
            string backup = DictPath + DictConflict.BackupInfix + "20260704-090000.bak";
            WriteFile(backup, "{ invalid json }");

            string error;
            DictModel? restored = DictConflict.ResolveRestoreCad(DictPath, backup, out error);

            Assert.Null(restored);
            Assert.NotEqual("", error);
            Assert.Contains("\"keep\"", File.ReadAllText(DictPath));
            Assert.True(File.Exists(backup));
        }
    }
}
