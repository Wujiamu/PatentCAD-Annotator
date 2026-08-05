using Xunit;
using PatentMarker.IO;
using System.IO;
using System.Text;

namespace PatentMarker.Tests
{
    /// <summary>
    /// DictWriter 序列化/写回单元测试 — 验证与 VBA JsonWriter 输出格式兼容：
    /// 键顺序、2 空格缩进、\r\n 行尾、UTF-8 无 BOM、null 可选字段不输出。
    /// </summary>
    public class DictWriterTests : IDisposable
    {
        private readonly string _tempDir;

        public DictWriterTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PatentMarkerWriterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private static DictModel MakeModel(bool withModified = false)
        {
            var model = new DictModel();
            model.Metadata.SourceFile = "test.docx";
            model.Metadata.ExtractedAt = "2026-08-04T10:00:00";
            model.Metadata.Version = "3.0";
            if (withModified)
            {
                model.Metadata.ModifiedBy = "cad";
                model.Metadata.ModifiedAt = "2026-08-04T11:30:00";
            }
            model.Entries.Add(new DictEntry { Number = "1", Name = "底座", Occurrences = 3 });
            var conflictEntry = new DictEntry { Number = "2", Name = "支架", Occurrences = 1 };
            conflictEntry.Conflicts.Add(new ConflictInfo { Number = "2a", Candidates = new() { "支架", "支杆" } });
            model.Entries.Add(conflictEntry);
            model.Warnings.Add("测试警告");
            return model;
        }

        // ================================================================
        // 序列化格式
        // ================================================================

        [Fact]
        public void Serialize_KeyOrder_MetadataFirst()
        {
            string json = DictWriter.Serialize(MakeModel());
            int meta = json.IndexOf("\"metadata\"", System.StringComparison.Ordinal);
            int entries = json.IndexOf("\"entries\"", System.StringComparison.Ordinal);
            int warnings = json.IndexOf("\"warnings\"", System.StringComparison.Ordinal);
            Assert.True(meta >= 0 && entries > meta && warnings > entries,
                "键顺序应为 metadata → entries → warnings");
        }

        [Fact]
        public void Serialize_EntryKeyOrder()
        {
            string json = DictWriter.Serialize(MakeModel());
            int num = json.IndexOf("\"number\"", System.StringComparison.Ordinal);
            int name = json.IndexOf("\"name\"", System.StringComparison.Ordinal);
            int occ = json.IndexOf("\"occurrences\"", System.StringComparison.Ordinal);
            int conflicts = json.IndexOf("\"conflicts\"", System.StringComparison.Ordinal);
            Assert.True(num >= 0 && name > num && occ > name && conflicts > occ,
                "entry 键顺序应为 number → name → occurrences → conflicts");
        }

        [Fact]
        public void Serialize_NoModifiedFields_WhenNull()
        {
            string json = DictWriter.Serialize(MakeModel(withModified: false));
            Assert.DoesNotContain("modified_by", json);
            Assert.DoesNotContain("modified_at", json);
        }

        [Fact]
        public void Serialize_IncludesModifiedFields_WhenSet()
        {
            string json = DictWriter.Serialize(MakeModel(withModified: true));
            Assert.Contains("\"modified_by\": \"cad\"", json);
            Assert.Contains("2026-08-04T11:30:00", json);
        }

        [Fact]
        public void Serialize_ChineseNotEscaped_Utf8Plain()
        {
            // VBA 输出原始 UTF-8 中文，不允许 \uXXXX 转义
            string json = DictWriter.Serialize(MakeModel());
            Assert.DoesNotContain("\\u", json);
            Assert.Contains("底座", json);
        }

        [Fact]
        public void Serialize_IndentedTwoSpaces()
        {
            string json = DictWriter.Serialize(MakeModel());
            Assert.Contains("\r\n  \"metadata\"", json);
            Assert.Contains("\r\n    \"source_file\"", json);
            Assert.Contains("\r\n    {\r\n      \"number\"", json);
        }

        [Fact]
        public void Serialize_CrlfLineEndings()
        {
            string json = DictWriter.Serialize(MakeModel());
            Assert.DoesNotContain("\n\"", json); // 不允许单独 \n 行首
            Assert.Contains("\r\n", json);
        }

        [Fact]
        public void Serialize_EmptyCollections()
        {
            var model = new DictModel();
            string json = DictWriter.Serialize(model);
            Assert.Contains("\"entries\": []", json);
            Assert.Contains("\"warnings\": []", json);
        }

        // ================================================================
        // 写回文件
        // ================================================================

        [Fact]
        public void Write_NoBom_Utf8()
        {
            string path = Path.Combine(_tempDir, "test.dict.json");
            bool ok = DictWriter.Write(path, MakeModel(), out string error);
            Assert.True(ok, error);
            byte[] bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF,
                "不允许输出 BOM");
        }

        [Fact]
        public void Write_ThenLoad_RoundTrip()
        {
            string path = Path.Combine(_tempDir, "rt.dict.json");
            Assert.True(DictWriter.Write(path, MakeModel(withModified: true), out string error), error);

            DictModel? loaded = DictLoader.Load(path);
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded.Entries.Count);
            Assert.Equal("底座", loaded.Entries[0].Name);
            Assert.Equal(3, loaded.Entries[0].Occurrences);
            Assert.Single(loaded.Entries[1].Conflicts);
            Assert.Equal("cad", loaded.Metadata.ModifiedBy);
            Assert.Equal("2026-08-04T11:30:00", loaded.Metadata.ModifiedAt);
            Assert.Single(loaded.Warnings);
        }

        [Fact]
        public void Write_OverwritesExistingFile()
        {
            string path = Path.Combine(_tempDir, "overwrite.dict.json");
            File.WriteAllText(path, "旧内容", new UTF8Encoding(false));
            Assert.True(DictWriter.Write(path, MakeModel(), out string error), error);
            Assert.Contains("底座", File.ReadAllText(path, new UTF8Encoding(false)));
            Assert.False(File.Exists(path + ".tmp"), "临时文件应已清理");
        }

        [Fact]
        public void Write_MissingDirectory_ReturnsFalse()
        {
            string path = Path.Combine(_tempDir, "no", "such", "dir.dict.json");
            bool ok = DictWriter.Write(path, MakeModel(), out string error);
            Assert.False(ok);
            Assert.False(string.IsNullOrEmpty(error));
        }

        // ================================================================
        // v4.0 BuildWriteModel：覆盖 / 合并 / CAD 标记
        // ================================================================

        private static DictModel MakeCurrent()
        {
            var model = new DictModel();
            model.Metadata.SourceFile = "a.docx";
            model.Metadata.Version = "3.0";
            model.Entries.Add(new DictEntry { Number = "1", Name = "底座", Occurrences = 3 });
            model.Entries.Add(new DictEntry { Number = "2", Name = "支架", Occurrences = 1 });
            model.Entries.Add(new DictEntry { Number = "10", Name = "泵体", Occurrences = 2 });
            model.Warnings.Add("旧警告");
            return model;
        }

        private static List<DictWriteRow> MakeRows()
        {
            return new List<DictWriteRow>
            {
                new DictWriteRow { Number = "2", Name = "支架（改）" },
                new DictWriteRow { Number = "11", Name = "阀盖" },
                new DictWriteRow { Number = "10", Name = "泵体" }
            };
        }

        [Fact]
        public void BuildWriteModel_Overwrite_ReplacesAllWithRows()
        {
            var m = DictWriter.BuildWriteModel(MakeCurrent(), MakeRows(), true)!;
            Assert.NotNull(m);
            Assert.Equal(3, m.Entries.Count);
            // 覆盖：按预览行顺序
            Assert.Equal("2", m.Entries[0].Number);
            Assert.Equal("支架（改）", m.Entries[0].Name);
            Assert.Equal("11", m.Entries[1].Number);
            // 覆盖：无原条目 occurrences 可保留，归零
            Assert.Equal(0, m.Entries[0].Occurrences);
            // CAD 标记
            Assert.Equal("cad", m.Metadata.ModifiedBy);
            Assert.False(string.IsNullOrEmpty(m.Metadata.ModifiedAt));
        }

        [Fact]
        public void BuildWriteModel_Merge_UpdatesExistingAppendsNew_KeepsOrder()
        {
            var m = DictWriter.BuildWriteModel(MakeCurrent(), MakeRows(), false)!;
            Assert.NotNull(m);
            // 原顺序保持：1, 2, 10，新增 11 追加尾部
            Assert.Equal(4, m.Entries.Count);
            Assert.Equal("1", m.Entries[0].Number);
            Assert.Equal("底座", m.Entries[0].Name);
            Assert.Equal("2", m.Entries[1].Number);
            Assert.Equal("支架（改）", m.Entries[1].Name);   // 已有条目 name 更新
            Assert.Equal("10", m.Entries[2].Number);
            Assert.Equal("泵体", m.Entries[2].Name);
            Assert.Equal("11", m.Entries[3].Number);        // 新编号追加尾部
            Assert.Equal("阀盖", m.Entries[3].Name);
            // occurrences 保留
            Assert.Equal(3, m.Entries[0].Occurrences);
            Assert.Equal(1, m.Entries[1].Occurrences);
            // warnings 保留 + CAD 标记
            Assert.Single(m.Warnings);
            Assert.Equal("cad", m.Metadata.ModifiedBy);
            Assert.Equal("a.docx", m.Metadata.SourceFile);
        }

        [Fact]
        public void BuildWriteModel_Merge_CaseInsensitiveNumberMatch()
        {
            var rows = new List<DictWriteRow> { new DictWriteRow { Number = "S1", Name = "第一空间" } };
            var cur = new DictModel();
            cur.Entries.Add(new DictEntry { Number = "s1", Name = "旧名", Occurrences = 5 });
            var m = DictWriter.BuildWriteModel(cur, rows, false)!;
            Assert.Single(m.Entries);
            Assert.Equal("第一空间", m.Entries[0].Name);
            Assert.Equal(5, m.Entries[0].Occurrences);  // 保留原值
        }

        [Fact]
        public void BuildWriteModel_EmptyRows_ReturnsNull()
        {
            Assert.Null(DictWriter.BuildWriteModel(MakeCurrent(), new List<DictWriteRow>(), false));
            Assert.Null(DictWriter.BuildWriteModel(MakeCurrent(), null!, false));
        }

        [Fact]
        public void BuildWriteModel_Merge_DoesNotMutateCurrentModel()
        {
            var current = MakeCurrent();
            string originalName = current.Entries[1].Name;
            string originalModifiedBy = current.Metadata.ModifiedBy;
            var result = DictWriter.BuildWriteModel(current, MakeRows(), false)!;

            Assert.Equal(originalName, current.Entries[1].Name);
            Assert.Equal(originalModifiedBy, current.Metadata.ModifiedBy);
            Assert.NotSame(current.Metadata, result.Metadata);
            Assert.NotSame(current.Warnings, result.Warnings);
            Assert.NotSame(current.Entries[0], result.Entries[0]);
            Assert.NotEqual(originalName, result.Entries[1].Name);
        }

        [Fact]
        public void BuildWriteModel_Overwrite_DropsOldEntries()
        {
            var rows = new List<DictWriteRow> { new DictWriteRow { Number = "99", Name = "全新" } };
            var m = DictWriter.BuildWriteModel(MakeCurrent(), rows, true)!;
            Assert.Single(m.Entries);
            Assert.Equal("99", m.Entries[0].Number);
        }

        // ================================================================
        // v4.0 TryApplyEdit / TryRemoveEntry：单条目编辑
        // ================================================================

        [Fact]
        public void TryApplyEdit_ModifyNumberAndName()
        {
            var model = MakeCurrent();
            var target = model.Entries[0];
            Assert.True(DictWriter.TryApplyEdit(model, target, "100", "底座（改）", out string? conflict));
            Assert.Null(conflict);
            Assert.Equal("100", model.Entries[0].Number);
            Assert.Equal("底座（改）", model.Entries[0].Name);
            Assert.Equal(3, model.Entries.Count);
            Assert.Equal("cad", model.Metadata.ModifiedBy);
            Assert.False(string.IsNullOrEmpty(model.Metadata.ModifiedAt));
        }

        [Fact]
        public void TryApplyEdit_Conflict_Rejects()
        {
            var model = MakeCurrent();
            var target = model.Entries[0];  // number=1
            Assert.False(DictWriter.TryApplyEdit(model, target, "2", "支架", out string? conflict));
            Assert.Equal("2", conflict);
            Assert.Equal("1", target.Number);  // 未改动
            Assert.Null(model.Metadata.ModifiedBy);
        }

        [Fact]
        public void TryApplyEdit_Conflict_IgnoreSelf()
        {
            var model = MakeCurrent();
            var target = model.Entries[0];
            // 自身编号不改动时不算冲突
            Assert.True(DictWriter.TryApplyEdit(model, target, "1", "底座新名", out string? conflict));
            Assert.Null(conflict);
            Assert.Equal("底座新名", model.Entries[0].Name);
        }

        [Fact]
        public void TryApplyEdit_Conflict_CaseInsensitive()
        {
            var model = new DictModel();
            model.Entries.Add(new DictEntry { Number = "s1", Name = "旧" });
            Assert.False(DictWriter.TryApplyEdit(model, null, "S1", "新", out string? conflict));
            Assert.Equal("s1", conflict);
        }

        [Fact]
        public void TryApplyEdit_AddNewEntry()
        {
            var model = MakeCurrent();
            Assert.True(DictWriter.TryApplyEdit(model, null, "99", "阀盖", out string? conflict));
            Assert.Null(conflict);
            Assert.Equal(4, model.Entries.Count);
            Assert.Equal("99", model.Entries[3].Number);
            Assert.Equal("cad", model.Metadata.ModifiedBy);
        }

        [Fact]
        public void TryApplyEdit_EmptyFields_Rejected()
        {
            var model = MakeCurrent();
            Assert.False(DictWriter.TryApplyEdit(model, null, "", "名", out _));
            Assert.False(DictWriter.TryApplyEdit(model, null, "1", "", out _));
            Assert.False(DictWriter.TryApplyEdit(model, null, "  ", "名", out _));
        }

        [Fact]
        public void TryApplyEdit_NullMetadata_IsRecovered()
        {
            var model = MakeCurrent();
            model.Metadata = null!;

            Assert.True(DictWriter.TryApplyEdit(model, null, "99", "new-name", out _));
            Assert.NotNull(model.Metadata);
            Assert.Equal("cad", model.Metadata.ModifiedBy);
        }

        [Fact]
        public void TryRemoveEntry_RemovesAndMarks()
        {
            var model = MakeCurrent();
            var target = model.Entries[1];  // number=2
            Assert.True(DictWriter.TryRemoveEntry(model, target));
            Assert.Equal(2, model.Entries.Count);
            Assert.DoesNotContain(model.Entries, e => e.Number == "2");
            Assert.Equal("cad", model.Metadata.ModifiedBy);
        }

        [Fact]
        public void TryRemoveEntry_NotInModel_ReturnsFalse()
        {
            var model = MakeCurrent();
            var alien = new DictEntry { Number = "999", Name = "外部" };
            Assert.False(DictWriter.TryRemoveEntry(model, alien));
            Assert.Equal(3, model.Entries.Count);
            Assert.Null(model.Metadata.ModifiedBy);
        }
    }
}
