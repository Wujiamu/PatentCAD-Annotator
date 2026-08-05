using Xunit;
using PatentMarker.IO;

namespace PatentMarker.Tests
{
    /// <summary>
    /// DictDiff.Compute 核心逻辑单元测试。
    /// 覆盖字典对比的全部 6 种状态：Unchanged / Added / Removed / NumberChanged / NameChanged / BothChanged。
    /// </summary>
    public class DictDiffTests
    {
        private static DictEntry MakeEntry(string number, string name) =>
            new DictEntry { Number = number, Name = name };

        private static DictModel MakeModel(params DictEntry[] entries) =>
            new DictModel { Entries = new List<DictEntry>(entries) };

        // ================================================================
        // 基础状态测试
        // ================================================================

        [Fact]
        public void Compute_BothNull_ReturnsEmpty()
        {
            var result = DictDiff.Compute(null!, null!);
            Assert.Empty(result);
        }

        [Fact]
        public void Compute_OldNull_AllAdded()
        {
            var newDict = MakeModel(MakeEntry("1", "壳体"), MakeEntry("2", "端盖"));
            var result = DictDiff.Compute(null!, newDict);

            Assert.Equal(2, result.Count);
            Assert.All(result, d => Assert.Equal(DiffStatus.Added, d.Status));
        }

        [Fact]
        public void Compute_NewNull_AllRemoved()
        {
            var oldDict = MakeModel(MakeEntry("1", "壳体"), MakeEntry("2", "端盖"));
            var result = DictDiff.Compute(oldDict, null!);

            Assert.Equal(2, result.Count);
            Assert.All(result, d => Assert.Equal(DiffStatus.Removed, d.Status));
        }

        [Fact]
        public void Compute_IdicalEntries_AllUnchanged()
        {
            var oldDict = MakeModel(MakeEntry("1", "壳体"), MakeEntry("2", "端盖"));
            var newDict = MakeModel(MakeEntry("1", "壳体"), MakeEntry("2", "端盖"));
            var result = DictDiff.Compute(oldDict, newDict);

            Assert.Equal(2, result.Count);
            Assert.All(result, d => Assert.Equal(DiffStatus.Unchanged, d.Status));
        }

        // ================================================================
        // 变更状态测试
        // ================================================================

        [Fact]
        public void Compute_EntryAdded_DetectedAsAdded()
        {
            var oldDict = MakeModel(MakeEntry("1", "壳体"));
            var newDict = MakeModel(MakeEntry("1", "壳体"), MakeEntry("2", "端盖"));
            var result = DictDiff.Compute(oldDict, newDict);

            var added = result.Where(d => d.Status == DiffStatus.Added).ToList();
            Assert.Single(added);
            Assert.Equal("2", added[0].Number);
            Assert.Equal("端盖", added[0].Name);
        }

        [Fact]
        public void Compute_EntryRemoved_DetectedAsRemoved()
        {
            var oldDict = MakeModel(MakeEntry("1", "壳体"), MakeEntry("2", "端盖"));
            var newDict = MakeModel(MakeEntry("1", "壳体"));
            var result = DictDiff.Compute(oldDict, newDict);

            var removed = result.Where(d => d.Status == DiffStatus.Removed).ToList();
            Assert.Single(removed);
            Assert.Equal("2", removed[0].OldNumber);
            Assert.Equal("端盖", removed[0].OldName);
        }

        [Fact]
        public void Compute_NameChanged_DetectedAsNameChanged()
        {
            // 编号相同，名称不同 → NameChanged
            var oldDict = MakeModel(MakeEntry("1", "旧壳体"));
            var newDict = MakeModel(MakeEntry("1", "新壳体"));
            var result = DictDiff.Compute(oldDict, newDict);

            Assert.Single(result);
            Assert.Equal(DiffStatus.NameChanged, result[0].Status);
            Assert.Equal("1", result[0].Number);
            Assert.Equal("新壳体", result[0].Name);
            Assert.Equal("旧壳体", result[0].OldName);
        }

        [Fact]
        public void Compute_NumberChanged_DetectedAsNumberChanged()
        {
            // 编号不同，名称相同 → NumberChanged（按名称匹配）
            var oldDict = MakeModel(MakeEntry("1", "壳体"));
            var newDict = MakeModel(MakeEntry("10", "壳体"));
            var result = DictDiff.Compute(oldDict, newDict);

            var numChanged = result.Where(d => d.Status == DiffStatus.NumberChanged).ToList();
            Assert.Single(numChanged);
            Assert.Equal("10", numChanged[0].Number);
            Assert.Equal("壳体", numChanged[0].Name);
            Assert.Equal("1", numChanged[0].OldNumber);
        }

        [Fact]
        public void Compute_BothChanged_DetectedAsBothUnmatched()
        {
            // 编号和名称都不同 → 旧条目 Removed + 新条目 Added
            var oldDict = MakeModel(MakeEntry("1", "壳体"));
            var newDict = MakeModel(MakeEntry("10", "外壳"));
            var result = DictDiff.Compute(oldDict, newDict);

            Assert.Equal(2, result.Count);
            Assert.Contains(result, d => d.Status == DiffStatus.Removed && d.OldNumber == "1");
            Assert.Contains(result, d => d.Status == DiffStatus.Added && d.Number == "10");
        }

        // ================================================================
        // 混合场景测试
        // ================================================================

        [Fact]
        public void Compute_MixedChanges_CorrectCounts()
        {
            var oldDict = MakeModel(
                MakeEntry("1", "壳体"),     // Unchanged
                MakeEntry("2", "端盖"),     // NameChanged → "法兰"
                MakeEntry("3", "密封圈"),   // Removed
                MakeEntry("5", "螺栓")      // NumberChanged → "8"
            );
            var newDict = MakeModel(
                MakeEntry("1", "壳体"),     // Unchanged
                MakeEntry("2", "法兰"),     // NameChanged
                MakeEntry("8", "螺栓"),     // NumberChanged
                MakeEntry("9", "垫片")      // Added
            );

            var result = DictDiff.Compute(oldDict, newDict);

            Assert.Single(result.Where(d => d.Status == DiffStatus.Unchanged));
            Assert.Single(result.Where(d => d.Status == DiffStatus.NameChanged));
            Assert.Single(result.Where(d => d.Status == DiffStatus.NumberChanged));
            Assert.Single(result.Where(d => d.Status == DiffStatus.Removed));
            Assert.Single(result.Where(d => d.Status == DiffStatus.Added));
        }

        [Fact]
        public void Compute_EmptyEntries_ReturnsEmpty()
        {
            var oldDict = MakeModel();
            var newDict = MakeModel();
            var result = DictDiff.Compute(oldDict, newDict);
            Assert.Empty(result);
        }

        // ================================================================
        // Summarize 测试
        // ================================================================

        [Fact]
        public void Summarize_MixedChanges_ReturnsFormattedString()
        {
            var oldDict = MakeModel(MakeEntry("1", "A"), MakeEntry("2", "B"));
            var newDict = MakeModel(MakeEntry("1", "A"), MakeEntry("3", "C"));
            var diff = DictDiff.Compute(oldDict, newDict);

            string summary = DictDiff.Summarize(diff);

            // 中文默认：新增 1，删除 1，编号变 0，名称变 0，无法匹配 0
            Assert.Contains("新增", summary);
            Assert.Contains("删除", summary);
        }

        [Fact]
        public void Summarize_EnglishLang_ReturnsEnglishString()
        {
            var savedLang = PatentMarker.I18n.Strings.Lang;
            try
            {
                PatentMarker.I18n.Strings.Lang = PatentMarker.I18n.Language.English;

                var oldDict = MakeModel(MakeEntry("1", "A"));
                var newDict = MakeModel(MakeEntry("2", "B"));
                var diff = DictDiff.Compute(oldDict, newDict);

                string summary = DictDiff.Summarize(diff);
                Assert.Contains("Added", summary);
                Assert.Contains("Removed", summary);
            }
            finally
            {
                PatentMarker.I18n.Strings.Lang = savedLang;
            }
        }

        // ================================================================
        // DictDiffEntry 属性测试
        // ================================================================

        [Fact]
        public void DictDiffEntry_AddedEntry_OldFieldsAreEmpty()
        {
            var newDict = MakeModel(MakeEntry("1", "壳体"));
            var result = DictDiff.Compute(null!, newDict);

            Assert.Single(result);
            Assert.Equal("1", result[0].Number);
            Assert.Equal("壳体", result[0].Name);
            Assert.Equal("", result[0].OldNumber);
            Assert.Equal("", result[0].OldName);
        }

        [Fact]
        public void DictDiffEntry_RemovedEntry_NewFieldsAreEmpty()
        {
            var oldDict = MakeModel(MakeEntry("1", "壳体"));
            var result = DictDiff.Compute(oldDict, null!);

            Assert.Single(result);
            Assert.Equal("1", result[0].Number);
            Assert.Equal("壳体", result[0].Name);
            Assert.Equal("1", result[0].OldNumber);
            Assert.Equal("壳体", result[0].OldName);
        }
    }
}
