using Xunit;
using PatentMarker.IO;
using System.Text.Json;

namespace PatentMarker.Tests
{
    /// <summary>
    /// DictLoader.Load 核心路径单元测试 — 验证 .dict.json 文件解析。
    /// 使用临时文件进行文件 I/O 测试，测试后自动清理。
    /// </summary>
    public class DictLoaderTests : IDisposable
    {
        private readonly string _tempDir;

        public DictLoaderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PatentMarkerTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        private string WriteTempJson(string filename, string content)
        {
            string path = Path.Combine(_tempDir, filename);
            File.WriteAllText(path, content);
            return path;
        }

        // ================================================================
        // 核心路径：JSON 解析
        // ================================================================

        [Fact]
        public void Load_ValidJson_ParsesEntries()
        {
            string json = """
            {
                "metadata": {
                    "source_file": "test.docx",
                    "extracted_at": "2026-08-03T10:00:00",
                    "version": "1.0"
                },
                "entries": [
                    { "number": "1", "name": "壳体", "occurrences": 3, "conflicts": [] },
                    { "number": "2", "name": "端盖", "occurrences": 1, "conflicts": [] }
                ],
                "warnings": []
            }
            """;
            string path = WriteTempJson("test.dict.json", json);

            DictModel? result = DictLoader.Load(path);

            Assert.NotNull(result);
            Assert.Equal(2, result.Entries.Count);
            Assert.Equal("1", result.Entries[0].Number);
            Assert.Equal("壳体", result.Entries[0].Name);
            Assert.Equal(3, result.Entries[0].Occurrences);
            Assert.Equal("2", result.Entries[1].Number);
            Assert.Equal("端盖", result.Entries[1].Name);
        }

        [Fact]
        public void Load_ValidJson_ParsesMetadata()
        {
            string json = """
            {
                "metadata": {
                    "source_file": "MP26015179.docx",
                    "extracted_at": "2026-08-03T10:00:00",
                    "version": "2.0"
                },
                "entries": [],
                "warnings": []
            }
            """;
            string path = WriteTempJson("test.dict.json", json);

            DictModel? result = DictLoader.Load(path);

            Assert.NotNull(result);
            Assert.Equal("MP26015179.docx", result.Metadata.SourceFile);
            Assert.Equal("2026-08-03T10:00:00", result.Metadata.ExtractedAt);
            Assert.Equal("2.0", result.Metadata.Version);
        }

        [Fact]
        public void Load_WithConflicts_ParsesConflictInfo()
        {
            string json = """
            {
                "metadata": {},
                "entries": [
                    {
                        "number": "1",
                        "name": "壳体",
                        "occurrences": 2,
                        "conflicts": [
                            { "number": "1a", "candidates": ["壳体", "外壳"] },
                            { "number": "1b", "candidates": ["底座"] }
                        ]
                    }
                ],
                "warnings": []
            }
            """;
            string path = WriteTempJson("test.dict.json", json);

            DictModel? result = DictLoader.Load(path);

            Assert.NotNull(result);
            Assert.Single(result.Entries);
            Assert.Equal(2, result.Entries[0].Conflicts.Count);
            Assert.Equal("1a", result.Entries[0].Conflicts[0].Number);
            Assert.Equal(2, result.Entries[0].Conflicts[0].Candidates.Count);
            Assert.Contains("壳体", result.Entries[0].Conflicts[0].Candidates);
        }

        [Fact]
        public void Load_WithWarnings_ParsesWarnings()
        {
            string json = """
            {
                "metadata": {},
                "entries": [],
                "warnings": ["编号 1 重复出现", "名称为空"]
            }
            """;
            string path = WriteTempJson("test.dict.json", json);

            DictModel? result = DictLoader.Load(path);

            Assert.NotNull(result);
            Assert.Equal(2, result.Warnings.Count);
            Assert.Equal("编号 1 重复出现", result.Warnings[0]);
        }

        // ================================================================
        // 边界情况
        // ================================================================

        [Fact]
        public void Load_NullCollectionsAndNestedValues_AreNormalized()
        {
            /* string json = """
            {
                "metadata": null,
                "entries": [
                    null,
                    {
                        "number": null,
                        "name": null,
                        "conflicts": [null, { "number": null, "candidates": [null, "候选"] }]
                    }
                ],
                "warnings": [null, "保留的警告"]
            }
            */
            string json = "{\"metadata\":null,\"entries\":[null,{\"number\":null,\"name\":null,\"conflicts\":[null,{\"number\":null,\"candidates\":[null,\"candidate\"]}]}],\"warnings\":[null,\"warning\"]}";
            string path = WriteTempJson("null-values.dict.json", json);

            DictModel? result = DictLoader.Load(path);

            Assert.NotNull(result);
            Assert.NotNull(result.Metadata);
            Assert.Single(result.Entries);
            Assert.Equal("", result.Entries[0].Number);
            Assert.Equal("", result.Entries[0].Name);
            Assert.Single(result.Entries[0].Conflicts);
            Assert.Equal("", result.Entries[0].Conflicts[0].Number);
            Assert.Single(result.Entries[0].Conflicts[0].Candidates);
            Assert.Single(result.Warnings);
            Assert.NotNull(result.Warnings[0]);
        }

        [Fact]
        public void Load_FileNotFound_ReturnsNull()
        {
            string path = Path.Combine(_tempDir, "nonexistent.dict.json");
            DictModel? result = DictLoader.Load(path);
            Assert.Null(result);
        }

        [Fact]
        public void Load_InvalidJson_ReturnsNull()
        {
            string path = WriteTempJson("bad.dict.json", "{ invalid json }}}");
            DictModel? result = DictLoader.Load(path);
            Assert.Null(result);
        }

        [Fact]
        public void Load_EmptyObject_ReturnsEmptyModel()
        {
            string path = WriteTempJson("empty.dict.json", "{}");
            DictModel? result = DictLoader.Load(path);

            Assert.NotNull(result);
            Assert.Empty(result.Entries);
            Assert.Empty(result.Warnings);
        }

        [Fact]
        public void Load_EmptyEntries_ReturnsEmptyList()
        {
            string json = """
            {
                "metadata": { "source_file": "", "extracted_at": "", "version": "" },
                "entries": [],
                "warnings": []
            }
            """;
            string path = WriteTempJson("test.dict.json", json);

            DictModel? result = DictLoader.Load(path);

            Assert.NotNull(result);
            Assert.Empty(result.Entries);
        }

        // ================================================================
        // 缓存行为测试
        // ================================================================

        [Fact]
        public void InvalidateCache_ClearsAllState()
        {
            DictLoader.InvalidateCache();
            Assert.False(DictLoader.HasCache);
            Assert.Null(DictLoader.PreviousModel);
        }

        [Fact]
        public void ClearPrevious_ClearsOnlyPreviousModel()
        {
            DictLoader.InvalidateCache();
            DictLoader.ClearPrevious();
            Assert.False(DictLoader.HasCache);
            Assert.Null(DictLoader.PreviousModel);
        }

        // ================================================================
        // 真实格式兼容性测试（模拟 VBA 导出格式）
        // ================================================================

        [Fact]
        public void Load_RealWorldFormat_ParsesCorrectly()
        {
            // 模拟 VBA PatentExtractor 实际导出的 .dict.json 格式
            string json = """
            {
                "metadata": {
                    "source_file": "MU26005942.2稿(1).docx",
                    "extracted_at": "2026-07-20T14:30:00",
                    "version": "1.0"
                },
                "entries": [
                    { "number": "1", "name": "水壶", "occurrences": 12, "conflicts": [] },
                    { "number": "2", "name": "壶盖", "occurrences": 5, "conflicts": [] },
                    { "number": "3", "name": "壶嘴", "occurrences": 3, "conflicts": [] },
                    { "number": "4", "name": "把手", "occurrences": 2, "conflicts": [] },
                    { "number": "5", "name": "底座", "occurrences": 4, "conflicts": [] }
                ],
                "warnings": []
            }
            """;
            string path = WriteTempJson("MU26005942.dict.json", json);

            DictModel? result = DictLoader.Load(path);

            Assert.NotNull(result);
            Assert.Equal(5, result.Entries.Count);
            Assert.Equal("水壶", result.Entries[0].Name);
            Assert.Equal(12, result.Entries[0].Occurrences);
            Assert.Equal("底座", result.Entries[4].Name);
        }

        [Fact]
        public void Load_TrailingCommas_ParsesSuccessfully()
        {
            // System.Text.Json 配置了 AllowTrailingCommas = true
            string json = """
            {
                "metadata": {
                    "source_file": "test.docx",
                    "extracted_at": "2026-08-03",
                    "version": "1.0",
                },
                "entries": [
                    { "number": "1", "name": "壳体", "occurrences": 1, "conflicts": [], },
                ],
                "warnings": [],
            }
            """;
            string path = WriteTempJson("test.dict.json", json);

            DictModel? result = DictLoader.Load(path);

            Assert.NotNull(result);
            Assert.Single(result.Entries);
        }
    }
}
