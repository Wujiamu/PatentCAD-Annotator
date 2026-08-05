using System.Text.Json;
using PatentMarker.IO;
using Xunit;

namespace PatentMarker.Tests
{
    /// <summary>
    /// 受版本控制的跨语言解析契约。样本是脱敏的最小输入，
    /// 以 C# 结果作为可重复基准，并与本机可选的 Word/VBA 批量语料测试分开。
    /// </summary>
    public class ParserContractTests
    {
        private sealed class ParserCase
        {
            public string Id { get; set; } = "";
            public string Text { get; set; } = "";
            public List<ParserHit> Hits { get; set; } = new();
        }

        private sealed class ParserHit
        {
            public string Number { get; set; } = "";
            public string Name { get; set; } = "";
        }

        [Fact]
        public void TrackedContractSamplesMatchExpectedHits()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "parser-contract.json");
            Assert.True(File.Exists(path), "解析契约文件缺失: " + path);

            var cases = JsonSerializer.Deserialize<List<ParserCase>>(File.ReadAllText(path));
            Assert.NotNull(cases);
            Assert.True(cases!.Count >= 8, "解析契约样本数量不足");

            foreach (var testCase in cases)
            {
                string preprocessed = MarkingTextParser.Preprocess(testCase.Text);
                var section = MarkingTextParser.ExtractMarkingSection(preprocessed);
                var actual = MarkingTextParser.ExtractAll(section.SectionText);

                Assert.Equal(testCase.Hits.Count, actual.Count);
                for (int i = 0; i < actual.Count; i++)
                {
                    Assert.Equal(testCase.Hits[i].Number, actual[i].Number);
                    Assert.Equal(testCase.Hits[i].Name, actual[i].Name);
                }
            }
        }
    }
}
