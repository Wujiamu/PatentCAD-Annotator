using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PatentMarker.IO
{
    /// <summary>
    /// 单条识别命中。Number/Name 为提取结果，Position/Length 用于区间重叠过滤与原文排序。
    /// </summary>
    public class MarkingHit
    {
        public string Number = "";
        public string Name = "";
        public int Position;
        public int Length;
    }

    /// <summary>段落定位结果。HeaderFound=false 表示未找到标记头（回退全文）。</summary>
    public class MarkingSectionResult
    {
        public string SectionText = "";
        public bool HeaderFound;
    }

    /// <summary>
    /// 附图标记识别引擎 — Patterns.bas v4.0 的 C# 移植（v4.0）。
    /// 纯文本正则，不依赖 AutoCAD/Word 对象，可在单测中直接验证。
    /// 与 Word 端 VBA 提取共用同一套规则，保证 CAD 粘贴识别结果与 VBA 一致。
    ///
    /// 支持格式（中国专利附图标注常见写法）：
    ///   [旧格式] 编号在前 + 名称在后   1底座， 2支架； 10外壳A1，
    ///   [新格式] 名称在前 + 编号在后   箱体结构10、 第一空间S1、 隔板131a、
    ///   [括号变体] 名称(编号) / 编号(名称)   加热器(1)、 1(底座)、
    ///   [裸列表] 每行一条，名称 编号   箱体结构 10&lt;换行&gt;箱壁 1
    ///
    /// 编号形式：纯数字(10)、字母前缀(S1)、字母后缀(131a/123A1)、连字符子编号(10-1)
    /// 字母后可继续数字，但必须在既有分隔符处结束（例如 123A1、123A2）
    /// 分隔符  ：中文逗号/顿号/分号/句号、英文逗号/分号
    ///
    /// 防误匹配策略（与 VBA 一致）：
    ///   1. 旧格式(模式1/括号B)优先收集，新格式(模式2/括号A)若与旧格式命中区间重叠则丢弃
    ///   2. 裸列表按行匹配(^...$)，行首行尾与名称/编号之间的空白只允许空格/Tab；
    ///      与任何标点模式命中重叠的行跳过
    ///   3. 获胜命中按原文位置稳定排序（同位置保持规则批次顺序），
    ///      ExtractAll 再按 (number, name) 去重，统计需要的未去重命中用 ExtractAllRaw
    ///
    /// 跨引擎约束（与 vba/Patterns.bas 的 G02_* 常量同步）：
    ///   * 编号词法、名称字符类、分隔符类、行内空白类必须与 VBA 文本一致；
    ///   * .NET 的 \d 会匹配全角/阿拉伯数字，VBScript.RegExp 只匹配 0-9，故统一写 [0-9]；
    ///   * 空白统一写显式字符类，避免 \s 在两个引擎中的成员差异；
    ///   * 编号左边界防止从更长编号中间截取：.NET 侧用零宽断言，VBScript 侧在代码中判定。
    /// </summary>
    public static class MarkingTextParser
    {
        // ================================================================
        // 共享模式常量
        //
        // 每一段正则文本都以单个 verbatim 字符串字面量写在 const 中，且与
        // vba/Patterns.bas 中同名 G02_* 常量逐字相同（tools/test-g02-parser.vbs
        // 会直接从这两个文件读取文本并逐字比较，再用真实 VBScript.RegExp 与
        // 真实 C# 生产代码跑同一份语料）。改动任何一段都必须两侧同步，否则
        // 对照探针失败。
        //
        // 跨引擎约束：
        //   * .NET 的 \d 会匹配全角/阿拉伯数字，VBScript 只匹配 0-9，故统一写 [0-9]；
        //   * 空白统一写显式字符类（VBScript 的 \s 只有 6 个成员，.NET 有 25 个）；
        //   * 不用 \Z（.NET 停在末尾换行之前，VBScript 视其为字面量 Z），文末统一 (?![\s\S])；
        //   * VBScript 不支持后顾断言，编号左边界由 NumberStartOk 在两侧用相同算法判定。
        // ================================================================

        /// <summary>G02_NUM：完整编号词法（名称在前/表格B/裸列表）。字母前缀 + 1~5 位数字 + 可选连字符子编号 + 可选字母后缀。</summary>
        internal const string G02_NUM = @"[A-Z]?[0-9]{1,5}(?:-[A-Z]?[0-9]{1,5})?(?:[A-Za-z][0-9]*)?";


        /// <summary>G02_WS：与 .NET \s 完全等价的显式空白类（覆盖 .NET \s 全部 25 个码位）。</summary>
        internal const string G02_WS = @"[ \t\r\n\f\v\u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000]";

        /// <summary>G02_INLINE：行内空白类，只允许空格与 Tab，不含任何换行。</summary>
        internal const string G02_INLINE = @"[ \t]";

        /// <summary>G02_NAME：名称体（编号在前），允许中英数字但必须含至少一个汉字。</summary>
        internal const string G02_NAME = @"[\u4e00-\u9fa5A-Za-z0-9]*[\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9]*";

        /// <summary>G02_NAMEFIRST：名称体（名称在前），以汉字开头，可含汉字/字母/数字/空格。</summary>
        internal const string G02_NAMEFIRST = @"[\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9 ]*?";

        /// <summary>G02_NAMEBARE：名称体（裸列表行），以汉字或字母开头且不含数字。</summary>
        internal const string G02_NAMEBARE = @"[\u4e00-\u9fa5A-Za-z ]+?";

        /// <summary>G02_SEP：分隔符类（编号在前）。</summary>
        internal const string G02_SEP = @"[，；;,、。.]";

        /// <summary>G02_SEPN：分隔符类（名称在前），不含英文句点。</summary>
        internal const string G02_SEPN = @"[、；。，;,.]";

        /// <summary>G02_CELLSTART：表格单元格左边界（文首、段落标记、换行或上一单元格结束符 Chr(7)）。</summary>
        internal const string G02_CELLSTART = @"(^|[\r\n\u0007])";

        /// <summary>G02_P1：模式 1 编号 + 名称 + 分隔符（旧格式）。</summary>
        internal const string G02_P1 =
            "(" + G02_NUM + ")(?![0-9])" + G02_WS + "*(" + G02_NAME + ")" + G02_WS + "*" + G02_SEP;

        /// <summary>G02_PB：模式 B 编号 + 括号名称 + 分隔符。</summary>
        internal const string G02_PB =
            "(" + G02_NUM + ")(?![0-9])" + G02_WS + "*[（(](" + G02_NAME + ")[）)]" + G02_WS + "*" + G02_SEP;

        /// <summary>G02_P2：模式 2 名称 + 编号 + 分隔符（新格式）。</summary>
        internal const string G02_P2 =
            "(" + G02_NAMEFIRST + ")" + G02_WS + "*(" + G02_NUM + ")(?![0-9])" + G02_WS + "*" + G02_SEPN;

        /// <summary>G02_PA：模式 A 名称 + 括号编号 + 分隔符。</summary>
        internal const string G02_PA =
            "(" + G02_NAMEFIRST + ")" + G02_WS + "*[（(](" + G02_NUM + ")(?![0-9])[）)]" + G02_WS + "*" + G02_SEPN;

        /// <summary>G02_P3：模式 3 裸列表，每行 名称 编号，空白只在本行内。</summary>
        internal const string G02_P3 =
            "^" + G02_INLINE + "*(" + G02_NAMEBARE + ")" + G02_INLINE + "*(" + G02_NUM + ")(?![0-9])" + G02_INLINE + "*$";

        /// <summary>G02_HDR0..5：标记头模式（按优先级），对应 DictModel.bas 的 patterns 数组。</summary>
        internal const string G02_HDR0 = @"附图标记说明如下[：:\n\r]*";
        internal const string G02_HDR1 = "附图标记说明[：:]" + G02_WS + "*";
        internal const string G02_HDR2 = "附图标记[：:]" + G02_WS + "*";
        internal const string G02_HDR3 = @"标记说明如下[：:\n\r]*";
        internal const string G02_HDR4 = "标记说明[：:]" + G02_WS + "*";
        internal const string G02_HDR5 = "标号说明[：:]" + G02_WS + "*";

        /// <summary>
        /// G02_SECTIONEND：段落结束边界，优先空段落（两连换行），否则文末。
        /// 文末用 (?![\s\S])（等价于 .NET 的 \z），不要用 \Z（.NET 会停在末尾换行之前，
        /// VBScript 会把它当字面量 Z，正是 R-032 的成因）。
        /// </summary>
        internal const string G02_SECTIONEND =
            @"[\s\S]*?(\r\n" + G02_WS + @"*\r\n|\n" + G02_WS + @"*\n|\r" + G02_WS + @"*\r|(?![\s\S]))";

        /// <summary>G02_TABLEA：表格流 编号单元格在前。分组 1=单元格左边界，2=编号，3=名称。</summary>
        internal const string G02_TABLEA =
            G02_CELLSTART + "(" + G02_NUM + @")\r\u0007(" + G02_NAME + @")\r\u0007";

        /// <summary>G02_TABLEB：表格流 名称单元格在前。分组 1=名称，2=编号。</summary>
        internal const string G02_TABLEB =
            "(" + G02_NAME + @")\r\u0007(" + G02_NUM + @")\r\u0007";

        // ================================================================
        // 识别模式（直接由上面的共享常量构造，与 Patterns.bas 的 Pattern1/PatternB/... 一一对应）
        // ================================================================

        private static readonly Regex RePattern1 = new Regex(G02_P1);
        private static readonly Regex RePatternB = new Regex(G02_PB);
        private static readonly Regex RePattern2 = new Regex(G02_P2);
        private static readonly Regex RePatternA = new Regex(G02_PA);
        private static readonly Regex RePattern3 = new Regex(G02_P3, RegexOptions.Multiline);

        // ================================================================
        // 段落定位（DictModel.bas ExtractMarkingSection 移植）
        // ================================================================

        /// <summary>标记头模式（按优先级），对应 VBA 的 patterns 数组。</summary>
        private static readonly string[] SectionHeaderPatterns = {
            G02_HDR0, G02_HDR1, G02_HDR2, G02_HDR3, G02_HDR4, G02_HDR5
        };

        /// <summary>段落结束边界（空段落或文末），见 G02_SECTIONEND 的说明。</summary>
        private static readonly Regex ReSectionEnd = new Regex(G02_SECTIONEND);

        /// <summary>专利文档中的附图标记说明以中文句号结束。</summary>
        private static readonly Regex ReSectionSentenceEnd = new Regex(@"。", RegexOptions.Compiled);

        // ================================================================
        // 表格预处理（DictModel.bas v1.1 表格支持移植）
        // Word 表格在 Content.Text 中的文本流：单元格文本 + vbCr + Chr(7)。
        // ================================================================

        /// <summary>表格模式 A：编号单元格在前 → 名称+编号+顿号；分组 1=左边界 2=编号 3=名称。</summary>
        private static readonly Regex ReTableA = new Regex(G02_TABLEA);

        /// <summary>表格模式 B：名称单元格在前 → 名称+编号+顿号；分组 1=名称 2=编号。</summary>
        private static readonly Regex ReTableB = new Regex(G02_TABLEB);

        // ================================================================
        // 公开入口
        // ================================================================

        /// <summary>
        /// 定位并截取「附图标记说明」段落。
        /// 匹配常见标记头（附图标记说明如下：/ 附图标记说明：/ 标记说明如下：/ 标号说明：）。
        /// 截取从标记头之后、到句号或空段落中更早的边界；没有边界时取到文末。
        /// 未找到任何标记头时返回全文（HeaderFound=false，调用方应提示回退全文扫描）。
        /// </summary>
        public static MarkingSectionResult ExtractMarkingSection(string text)
        {
            MarkingSectionResult result = new MarkingSectionResult();
            if (text == null)
            {
                result.SectionText = "";
                return result;
            }

            Match headerMatch = null;
            foreach (string p in SectionHeaderPatterns)
            {
                try
                {
                    headerMatch = Regex.Match(text, p);
                }
                catch (ArgumentException)
                {
                    headerMatch = null;
                }
                if (headerMatch != null && headerMatch.Success)
                    break;
            }

            if (headerMatch == null || !headerMatch.Success)
            {
                // 未找到标记头，回退到全文扫描
                result.SectionText = text;
                result.HeaderFound = false;
                return result;
            }

            int startPos = headerMatch.Index + headerMatch.Length;
            string after;
            if (startPos >= text.Length)
                after = "";
            else
                after = text.Substring(startPos);

            Match endMatch = ReSectionEnd.Match(after);
            int boundaryLength = after.Length;
            if (endMatch.Success && endMatch.Index + endMatch.Length < boundaryLength)
            {
                boundaryLength = endMatch.Index + endMatch.Length;
            }
            Match sentenceEndMatch = ReSectionSentenceEnd.Match(after);
            if (sentenceEndMatch.Success && sentenceEndMatch.Index + sentenceEndMatch.Length < boundaryLength)
            {
                boundaryLength = sentenceEndMatch.Index + sentenceEndMatch.Length;
            }

            result.SectionText = after.Substring(0, boundaryLength);
            result.HeaderFound = true;
            return result;
        }

        /// <summary>
        /// 文档级预处理（对应 DictModel.BuildModel 预处理步骤，供粘贴识别使用）：
        ///   1. HTML 换行标签 → vbCr（\r），保证段落定位正确
        ///   2. 表格流预处理：编号/名称 单元格对 → 名称+编号+顿号；残余 Chr(7) → 顿号
        /// 调用顺序：Preprocess → ExtractMarkingSection → ExtractAll
        /// </summary>
        public static string Preprocess(string text)
        {
            if (text == null) return "";
            text = text.Replace("<br/>", "\r");
            text = text.Replace("<br />", "\r");
            text = text.Replace("<br>", "\r");
            // ReTableA 分组：1=单元格左边界，2=编号，3=名称 → 保留左边界，输出 名称+编号+顿号
            text = ReTableA.Replace(text, "$1$3$2、");
            text = ReTableB.Replace(text, "$1$2、");
            text = text.Replace("\x07", "、");
            return text;
        }

        /// <summary>
        /// 从文本中提取所有附图标记（去重视图，等价于 Patterns.bas ExtractAll）。
        /// 保持既有对外契约：按 (number|name) 去重，按原文位置首次出现排序。
        /// 调用前建议先用 ExtractMarkingSection 定位段落，否则按全文扫描（误匹配风险由调用方承担）。
        /// 若输入为 Word/网页粘贴文本（含表格流或 HTML 换行），先调用 Preprocess。
        /// </summary>
        public static List<MarkingHit> ExtractAll(string text)
        {
            return Dedupe(ExtractAllRaw(text));
        }

        /// <summary>
        /// 从文本中提取所有附图标记，保留重复命中（等价于 Patterns.bas ExtractAllRaw）。
        /// 顺序为原文位置稳定排序，供频率统计等需要真实出现次数的调用方使用；
        /// 展示/粘贴仍用 ExtractAll，避免重复条目流入字典。
        /// </summary>
        public static List<MarkingHit> ExtractAllRaw(string text)
        {
            List<MarkingHit> allHits = new List<MarkingHit>();
            if (text == null || text.Length == 0) return allHits;

            // === 预处理（与 VBA 一致）===
            // HTML 换行标签 → 空格
            text = text.Replace("<br/>", " ");
            text = text.Replace("<br />", " ");
            text = text.Replace("<br>", " ");
            text = text.Replace("<BR/>", " ");
            // 统一换行符（裸列表按行匹配依赖 \n）
            text = text.Replace("\r", "\n");

            // === 区间收集器：第一梯队（旧格式）命中区间，用于过滤 ===
            List<int[]> keepRanges = new List<int[]>();

            // === 第一梯队：旧格式（编号在前）===
            // 模式 1：编号 + 名称 + 分隔符
            CollectHits(allHits, keepRanges, text, RePattern1, false);
            // 模式 B：编号 + 括号名称 + 分隔符
            CollectHits(allHits, keepRanges, text, RePatternB, false);

            // === 第二梯队：新格式（名称在前），与第一梯队重叠则丢弃 ===
            List<MarkingHit> candHits = new List<MarkingHit>();
            List<int[]> candRanges = new List<int[]>();
            // 模式 2：名称 + 编号 + 分隔符
            CollectHits(candHits, candRanges, text, RePattern2, true);
            // 模式 A：名称 + 括号编号 + 分隔符
            CollectHits(candHits, candRanges, text, RePatternA, true);

            for (int i = 0; i < candHits.Count; i++)
            {
                MarkingHit ch = candHits[i];
                if (!Overlaps(ch, keepRanges))
                {
                    allHits.Add(ch);
                    // 与第一梯队一致，范围保存为 [start, end]，允许分隔符紧邻下一条命中。
                    keepRanges.Add(new int[] { ch.Position, ch.Position + ch.Length - 1 });
                }
            }

            // === 第三梯队：裸列表（每行 名称 编号，行内无标点）===
            foreach (Match m3 in RePattern3.Matches(text))
            {
                MarkingHit h3 = new MarkingHit();
                h3.Name = m3.Groups[1].Value;
                h3.Number = m3.Groups[2].Value;
                h3.Position = m3.Index;
                h3.Length = m3.Length;
                if (!Overlaps(h3, keepRanges))
                {
                    allHits.Add(h3);
                }
            }

            // === 按原文位置稳定排序（同位置保持收集顺序：旧格式优先）===
            SortByPosition(allHits);
            return allHits;
        }

        // ================================================================
        // 内部逻辑（对应 VBA CollectHits / Overlaps / Dedupe / SortByPosition）
        // ================================================================

        /// <summary>收集命中：与 VBA CollectHits 等价。nameFirst 决定捕获组顺序。</summary>
        private static void CollectHits(List<MarkingHit> hits, List<int[]> ranges,
                                        string text, Regex re, bool nameFirst)
        {
            foreach (Match m in re.Matches(text))
            {
                if (!m.Success) continue;
                MarkingHit h = new MarkingHit();
                bool accept = true;
                if (nameFirst)
                {
                    h.Name = m.Groups[1].Value;
                    h.Number = m.Groups[2].Value;
                    // 名称体允许数字，正则会把超长编号的首位让给名称
                    // （底座123456 → 名称"底座1"、编号"23456"）。编号起点
                    // 同样要过左边界检查，与编号在前的分支一致。
                    accept = NumberStartOk(text, m.Groups[2].Index);
                }
                else
                {
                    h.Number = m.Groups[1].Value;
                    h.Name = m.Groups[2].Value;
                    // 编号在前的模式从编号开始匹配，须拒绝从更长编号中间起配。
                    accept = NumberStartOk(text, m.Index);
                }
                if (!accept) continue;
                h.Position = m.Index;
                h.Length = m.Length;
                hits.Add(h);
                ranges.Add(new int[] { m.Index, m.Index + m.Length - 1 });
            }
        }

        /// <summary>
        /// 判断编号起点是否合法：前一字符不能是数字、ASCII 字母或连字符，
        /// 否则说明这是从更长编号中间截取的片段（例如 123456底座 里的 23456）。
        /// 与 VBA NumberStartOk 完全一致。
        /// </summary>
        private static bool NumberStartOk(string text, int start)
        {
            if (start <= 0) return true;
            char c = text[start - 1];
            if ((c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') ||
                (c >= 'a' && c <= 'z') || c == '-') return false;
            return true;
        }

        /// <summary>判断命中区间是否与已有区间重叠（与 VBA Overlaps 等价）。</summary>
        private static bool Overlaps(MarkingHit hit, List<int[]> ranges)
        {
            int startPos = hit.Position;
            int endPos = hit.Position + hit.Length - 1;
            for (int i = 0; i < ranges.Count; i++)
            {
                int[] r = ranges[i];
                if (startPos <= r[1] && endPos >= r[0])
                    return true;
            }
            return false;
        }

        /// <summary>按原文位置稳定排序（插入排序，保持同位置原顺序；与 VBA SortByPosition 等价）。</summary>
        private static void SortByPosition(List<MarkingHit> hits)
        {
            for (int i = 1; i < hits.Count; i++)
            {
                MarkingHit current = hits[i];
                int j = i - 1;
                while (j >= 0 && hits[j].Position > current.Position)
                {
                    hits[j + 1] = hits[j];
                    j--;
                }
                hits[j + 1] = current;
            }
        }

        /// <summary>按 (number|name) 去重，保持首次出现顺序（与 VBA Dedupe 等价）。</summary>
        private static List<MarkingHit> Dedupe(List<MarkingHit> hits)
        {
            List<MarkingHit> outList = new List<MarkingHit>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            for (int i = 0; i < hits.Count; i++)
            {
                MarkingHit h = hits[i];
                string key = h.Number + "|" + h.Name;
                if (!seen.ContainsKey(key))
                {
                    seen[key] = true;
                    outList.Add(h);
                }
            }
            return outList;
        }
    }
}
