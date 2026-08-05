using Xunit;
using PatentMarker.IO;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit.Sdk;

namespace PatentMarker.Tests
{
    /// <summary>
    /// 真实语料对比：8 份说明书 txt 跑 C# 引擎 vs 当前实际 VBA 权威预期。
    /// 预期文件「批量测试/vba-expected-v4-output.txt」由真实 VBA 模块生成。
    /// 它从 5 套部署包
    /// 共享的 Patterns.bas / DictModel.bas（GBK）提取函数体、转换为独立 VBScript 后，
    /// 用 cscript 运行生成（同一份语料、同一份引擎代码，避免人工转写偏差）。
    /// </summary>
    public class CorpusComparisonTests
    {
        private static string FindBatchDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "批量测试");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("未找到「批量测试」目录");
        }

        private class ExpectedCorpus
        {
            public string FileName = "";
            public List<(string Number, string Name)> Hits = new();
        }

        private static List<ExpectedCorpus> ParseExpected(string path)
        {
            var result = new List<ExpectedCorpus>();
            ExpectedCorpus? cur = null;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw.TrimEnd('\r');
                if (line.StartsWith("[") && line.Contains(']'))
                {
                    var idx = line.IndexOf(']');
                    cur = new ExpectedCorpus { FileName = line.Substring(idx + 1).Trim() };
                    result.Add(cur);
                }
                else if (cur != null && line.StartsWith("    ") && line.Contains('='))
                {
                    var eq = line.IndexOf('=');
                    cur.Hits.Add((line.Substring(0, eq).Trim(), line.Substring(eq + 1)));
                }
            }
            return result;
        }

        [Fact]
        public void Corpus_CSharpMatchesVbaExpected()
        {
            string batchDir;
            try
            {
                batchDir = FindBatchDir();
            }
            catch (DirectoryNotFoundException)
            {
                throw SkipException.ForSkip("未提供本机 Word/VBA 批量语料；运行受版本控制的解析契约测试即可完成干净环境验证。");
            }
            var expectedPath = Path.Combine(batchDir, "vba-expected-v4-output.txt");
            Assert.True(File.Exists(expectedPath), "预期文件缺失: " + expectedPath);

            var expected = ParseExpected(expectedPath);
            Assert.True(expected.Count >= 8, $"预期语料数={expected.Count}，应至少 8 份");

            var failures = new List<string>();
            foreach (var exp in expected)
            {
                var filePath = Path.Combine(batchDir, exp.FileName);
                if (!File.Exists(filePath))
                {
                    failures.Add($"[{exp.FileName}] 语料文件不存在");
                    continue;
                }

                var text = File.ReadAllText(filePath, Encoding.UTF8);
                var pre = MarkingTextParser.Preprocess(text);
                var section = MarkingTextParser.ExtractMarkingSection(pre);
                var hits = MarkingTextParser.ExtractAll(section.SectionText)
                    .Select(h => (h.Number, h.Name)).ToList();

                if (hits.Count != exp.Hits.Count)
                {
                    failures.Add($"[{exp.FileName}] 命中数量不匹配: C#={hits.Count} VBA={exp.Hits.Count}");
                    int n = Math.Min(Math.Max(hits.Count, exp.Hits.Count), 10);
                    for (int i = 0; i < n; i++)
                    {
                        var c = i < hits.Count ? hits[i].ToString() : "(无)";
                        var v = i < exp.Hits.Count ? exp.Hits[i].ToString() : "(无)";
                        failures.Add($"   [{i}] C#={c} VBA={v}");
                    }
                    continue;
                }

                var firstDiff = -1;
                for (int i = 0; i < hits.Count; i++)
                {
                    if (hits[i].Number != exp.Hits[i].Number || hits[i].Name != exp.Hits[i].Name)
                    {
                        firstDiff = i;
                        break;
                    }
                }
                if (firstDiff >= 0)
                {
                    failures.Add($"[{exp.FileName}] 首个差异 @[{firstDiff}]: " +
                        $"C#=({hits[firstDiff].Number},{hits[firstDiff].Name}) " +
                        $"VBA=({exp.Hits[firstDiff].Number},{exp.Hits[firstDiff].Name})");
                }
            }

            Assert.True(failures.Count == 0,
                "C# 与 VBA 预期存在差异（" + failures.Count + " 项）:\n" + string.Join("\n", failures.Take(60)));
        }
    }
}
