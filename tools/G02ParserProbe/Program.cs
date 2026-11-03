using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using PatentMarker.IO;

namespace PatentMarker.G02Probe
{
    /// <summary>
    /// G02 探针：编译真实生产 MarkingTextParser.cs，用独立业务期望核对
    ///   R-016 完整编号词法（编号在前／括号／表格不截断）
    ///   R-017 统计用未去重原始命中
    ///   R-018 混合格式按原文位置稳定排序
    ///   R-032 段落文末边界（大写 Z 不被当作文末）
    ///   R-033 行内空白（不跨行拼接普通段落与孤立数字）
    ///
    /// 用法：G02ParserProbe &lt;cases-file&gt; &lt;expected-file&gt;
    /// 每行输出 PASS|/FAIL| 加选择器；末尾 SUMMARY；有 FAIL 时退出码 1，未执行断言时退出码 2。
    /// 修复前后使用同一份语料、同一份期望和同一判定函数。
    /// </summary>
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            if (args.Length == 1 && args[0] == "--dump-patterns")
            {
                return DumpPatterns();
            }
            if (args.Length == 2 && args[0] == "--dump-cases")
            {
                return DumpCases(args[1]);
            }
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: G02ParserProbe <cases-file> <expected-file>");
                Console.Error.WriteLine("       G02ParserProbe --dump-patterns");
                Console.Error.WriteLine("       G02ParserProbe --dump-cases <cases-file>");
                return 2;
            }

            List<Case> cases = LoadCases(args[0]);
            List<Expectation> expectations = LoadExpectations(args[1]);
            if (cases.Count == 0 || expectations.Count == 0)
            {
                Console.Error.WriteLine("SKIP/BLOCKED: no cases or no expectations loaded");
                return 2;
            }

            Dictionary<string, Case> byId = new Dictionary<string, Case>(StringComparer.Ordinal);
            foreach (Case c in cases) byId[c.Id] = c;

            HashSet<string> asserted = new HashSet<string>(StringComparer.Ordinal);
            foreach (Expectation e in expectations)
            {
                Case? c;
                if (!byId.TryGetValue(e.Id, out c))
                {
                    Fail(e.Id, "case id missing from corpus");
                    continue;
                }
                if (!asserted.Add(e.Id))
                {
                    Fail(e.Id, "duplicate expectation for the same case");
                    continue;
                }
                RunCase(c, e);
            }

            // 语料里存在但期望文件没有覆盖的用例必须显式失败，避免矩阵静默缩小。
            foreach (Case c in cases)
            {
                if (!asserted.Contains(c.Id)) Fail(c.Id, "case has no independent expectation");
            }

            Console.WriteLine("SUMMARY: passed=" + _passed + " failed=" + _failed);
            if (_failed > 0) return 1;
            if (_passed == 0)
            {
                Console.WriteLine("SKIP/BLOCKED: no business assertion executed");
                return 2;
            }
            return 0;
        }

        /// <summary>
        /// 把语料转成 VBScript 可直接使用的 UTF-16LE 数据文件，避免在 VBScript 源码里
        /// 手抄任何中文或控制字符。每行 `&lt;id&gt;\u0001&lt;text&gt;`，换行统一为 CRLF。
        /// </summary>
        private static int DumpCases(string casesFile)
        {
            List<Case> cases = LoadCases(casesFile);
            if (cases.Count == 0)
            {
                Console.Error.WriteLine("SKIP/BLOCKED: no cases loaded");
                return 2;
            }
            StringBuilder sb = new StringBuilder();
            foreach (Case c in cases)
            {
                // The text is escaped exactly like the source matrix so the receiving
                // VBScript side can split on CRLF and unescape afterwards.
                sb.Append(c.Id).Append('\u0001').Append(Escape(c.Text)).Append("\r\n");
            }
            Console.Out.Write(sb.ToString());
            Console.Out.Flush();
            return 0;
        }

        /// <summary>
        /// 输出生产解析器实际使用的每条正则文本，供 VBScript 侧直接加载
        /// （不手抄模式字符串，避免两侧文本漂移）。字段之间用 \u0001 分隔。
        /// </summary>
        private static int DumpPatterns()
        {
            string[] names = new string[]
            {
                "G02_NUM", "G02_WS", "G02_INLINE",
                "G02_NAME", "G02_NAMEFIRST", "G02_NAMEBARE",
                "G02_SEP", "G02_SEPN", "G02_CELLSTART",
                "G02_P1", "G02_PB", "G02_P2", "G02_PA", "G02_P3",
                "G02_HDR0", "G02_HDR1", "G02_HDR2", "G02_HDR3", "G02_HDR4", "G02_HDR5",
                "G02_SECTIONEND", "G02_TABLEA", "G02_TABLEB"
            };
            Type t = typeof(MarkingTextParser);
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                string text = "";
                FieldInfo? f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                if (f != null && f.FieldType == typeof(string))
                {
                    text = (string?)f.GetValue(null) ?? "";
                }
                else if (f != null && typeof(System.Text.RegularExpressions.Regex).IsAssignableFrom(f.FieldType))
                {
                    System.Text.RegularExpressions.Regex re =
                        (System.Text.RegularExpressions.Regex)f.GetValue(null)!;
                    text = re.ToString();
                }
                else
                {
                    PropertyInfo? p = t.GetProperty(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                    if (p != null) text = (string?)p.GetValue(null, null) ?? "";
                }
                Console.WriteLine(name + "\u0001" + text);
            }
            return 0;
        }

        private static void RunCase(Case c, Expectation exp)
        {
            // 生产调用链：Preprocess → ExtractMarkingSection → ExtractAll / ExtractAllRaw
            string pre = MarkingTextParser.Preprocess(c.Text);
            MarkingSectionResult section = MarkingTextParser.ExtractMarkingSection(pre);
            List<MarkingHit> hits = MarkingTextParser.ExtractAll(section.SectionText);

            // --- 展示视图：条数 + number/name + 顺序 ---
            if (exp.Hits >= 0 && hits.Count != exp.Hits)
            {
                Fail(c.Id, "display count expected=" + exp.Hits + " actual=" + hits.Count +
                    " actualHits=" + Format(hits));
                return;
            }
            if (exp.Hits > 0 && exp.Hits == hits.Count && exp.Items.Count == hits.Count)
            {
                for (int i = 0; i < hits.Count; i++)
                {
                    if (hits[i].Number != exp.Items[i].Key || hits[i].Name != exp.Items[i].Value)
                    {
                        Fail(c.Id, "display hit[" + i + "] expected=(" + exp.Items[i].Key + "," +
                            exp.Items[i].Value + ") actual=(" + hits[i].Number + "," +
                            hits[i].Name + ") all=" + Format(hits));
                        return;
                    }
                }
            }

            // --- 原始视图：真实出现次数的多重集（R-017）---
            if (exp.RawCounts.Count > 0)
            {
                List<MarkingHit>? raw = TryExtractAllRaw(section.SectionText, out string? rawError);
                if (raw == null)
                {
                    Fail(c.Id, "raw view unavailable: " + rawError);
                    return;
                }
                foreach (KeyValuePair<string, int> wanted in exp.RawCounts)
                {
                    int seen = 0;
                    for (int i = 0; i < raw.Count; i++)
                    {
                        if (raw[i].Number + "|" + raw[i].Name == wanted.Key) seen++;
                    }
                    if (seen != wanted.Value)
                    {
                        Fail(c.Id, "raw count (" + wanted.Key + ") expected=" + wanted.Value +
                            " actual=" + seen + " rawHits=" + Format(raw));
                        return;
                    }
                }
                if (raw.Count < hits.Count)
                {
                    Fail(c.Id, "raw count " + raw.Count + " < display count " + hits.Count);
                    return;
                }
                Pass(c.Id, "display=" + hits.Count + " raw=" + raw.Count + " " + Format(hits));
                return;
            }

            Pass(c.Id, "display=" + hits.Count + " " + Format(hits));
        }

        /// <summary>
        /// 反射调用 ExtractAllRaw：同一份探针源码需要能编译修复前的快照
        /// （那时该方法不存在），缺失时按业务失败处理，不能静默跳过。
        /// </summary>
        private static List<MarkingHit>? TryExtractAllRaw(string text, out string? error)
        {
            error = null;
            MethodInfo? method = typeof(MarkingTextParser).GetMethod(
                "ExtractAllRaw", BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                error = "MarkingTextParser.ExtractAllRaw is missing";
                return null;
            }
            try
            {
                object? result = method.Invoke(null, new object[] { text });
                List<MarkingHit>? list = result as List<MarkingHit>;
                if (list == null) error = "unexpected return type";
                return list;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                error = inner.GetType().Name + ": " + inner.Message;
                return null;
            }
        }

        private static string Format(List<MarkingHit> hits)
        {
            StringBuilder sb = new StringBuilder("[");
            for (int i = 0; i < hits.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(hits[i].Number).Append('|').Append(hits[i].Name);
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static List<Case> LoadCases(string path)
        {
            List<Case> list = new List<Case>();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                int tab = line.IndexOf('\t');
                if (tab < 0) continue;
                list.Add(new Case { Id = line.Substring(0, tab), Text = Unescape(line.Substring(tab + 1)) });
            }
            return list;
        }

        private static List<Expectation> LoadExpectations(string path)
        {
            Dictionary<string, Expectation> map = new Dictionary<string, Expectation>(StringComparer.Ordinal);
            List<Expectation> ordered = new List<Expectation>();
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                string[] parts = line.Split('\t');
                if (parts.Length < 2) continue;
                string kind = parts[0].Trim();
                string id;
                Expectation e;
                if (kind.StartsWith("S ", StringComparison.Ordinal))
                {
                    id = kind.Substring(2).Trim();
                    if (!map.TryGetValue(id, out e))
                    {
                        e = new Expectation { Id = id };
                        map[id] = e;
                        ordered.Add(e);
                    }
                    e.Hits = int.Parse(parts[1].Trim());
                    for (int i = 2; i < parts.Length; i++)
                    {
                        int bar = parts[i].IndexOf('|');
                        if (bar < 0) continue;
                        e.Items.Add(new KeyValuePair<string, string>(
                            parts[i].Substring(0, bar), parts[i].Substring(bar + 1)));
                    }
                }
                else if (kind.StartsWith("R ", StringComparison.Ordinal))
                {
                    id = kind.Substring(2).Trim();
                    if (!map.TryGetValue(id, out e))
                    {
                        e = new Expectation { Id = id };
                        map[id] = e;
                        ordered.Add(e);
                    }
                    for (int i = 1; i < parts.Length; i++)
                    {
                        string item = parts[i];
                        int lastBar = item.LastIndexOf('|');
                        if (lastBar < 0) continue;
                        // <number>|<name>|<count> — the count is after the LAST bar, the key is
                        // everything before it (numbers never contain '|', names never do either).
                        string key = item.Substring(0, lastBar);
                        int count = int.Parse(item.Substring(lastBar + 1));
                        e.RawCounts[key] = count;
                    }
                }
                else
                {
                    throw new InvalidDataException("Unrecognised expectation line: " + line);
                }
            }
            return ordered;
        }

        private static string Escape(string value)
        {
            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch == '\\') sb.Append("\\\\");
                else if (ch == '\n') sb.Append("\\n");
                else if (ch == '\r') sb.Append("\\r");
                else if (ch == '\t') sb.Append("\\t");
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        private static string Unescape(string value)
        {
            StringBuilder sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                if (ch == '\\' && i + 1 < value.Length)
                {
                    char next = value[i + 1];
                    if (next == 'n') { sb.Append('\n'); i++; continue; }
                    if (next == 'r') { sb.Append('\r'); i++; continue; }
                    if (next == 't') { sb.Append('\t'); i++; continue; }
                    if (next == '\\') { sb.Append('\\'); i++; continue; }
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        private static void Pass(string id, string detail)
        {
            _passed++;
            Console.WriteLine("PASS|" + id + "|" + detail);
        }

        private static void Fail(string id, string detail)
        {
            _failed++;
            Console.WriteLine("FAIL|" + id + "|" + detail);
        }

        private sealed class Case
        {
            public string Id = "";
            public string Text = "";
        }

        private sealed class Expectation
        {
            public string Id = "";
            public int Hits = -1;
            public List<KeyValuePair<string, string>> Items = new List<KeyValuePair<string, string>>();
            public Dictionary<string, int> RawCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        }
    }
}
