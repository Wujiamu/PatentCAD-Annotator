using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace PatentMarker.IO
{
    /// <summary>
    /// 单条识别命中。Number/Name 为提取结果，Position/Length 用于区间重叠过滤。
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
    /// 附图标记识别引擎 — Patterns.bas v3.0 的 C# 移植（v4.0）。
    /// 纯文本正则，不依赖 AutoCAD/Word 对象，可在单测中直接验证。
    /// 与 Word 端 VBA 提取共用同一套规则，保证 CAD 粘贴识别结果与 VBA 一致。
    ///
    /// 支持格式（中国专利附图标注常见写法）：
    ///   [旧格式] 编号在前 + 名称在后   1底座， 2支架； 10外壳A1，
    ///   [新格式] 名称在前 + 编号在后   箱体结构10、 第一空间S1、 隔板131a、
    ///   [括号变体] 名称(编号) / 编号(名称)   加热器(1)、 1(底座)、
    ///   [裸列表] 每行一条，名称 编号   箱体结构 10&lt;换行&gt;箱壁 1
    ///
    /// 编号形式：纯数字(10)、字母前缀(S1)、字母后缀(131a)、连字符子编号(10-1)
    /// 分隔符  ：中文逗号/顿号/分号/句号、英文逗号/分号
    ///
    /// 防误匹配策略（与 VBA 一致）：
    ///   1. 旧格式(模式1/括号B)优先收集，新格式(模式2/括号A)若与旧格式命中区间重叠则丢弃
    ///   2. 裸列表按行匹配(^...$)，与任何标点模式命中重叠的行跳过
    ///   3. 最终按 (number, name) 去重，保持首次出现顺序
    /// </summary>
    public static class MarkingTextParser
    {
        // ================================================================
        // 识别模式（与 Patterns.bas v3.0 逐条对应，v3.2 全角分号修复已包含）
        // ================================================================

        /// <summary>模式 1：编号 + 名称 + 分隔符（旧格式）1底座， 2支架； 10外壳A1，</summary>
        private static readonly Regex RePattern1 = new Regex(
            @"(\d{1,5}[A-Za-z]?)\s*([\u4e00-\u9fa5A-Za-z0-9]*[\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9]*)\s*[，；;,、。.]");

        /// <summary>模式 B：编号 + 括号名称 + 分隔符 1(底座)、 10（泵体）；</summary>
        private static readonly Regex RePatternB = new Regex(
            @"(\d{1,5}[A-Za-z]?)\s*[（(]([\u4e00-\u9fa5A-Za-z0-9]*[\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9]*)[）)]\s*[，;；,、。.]");

        /// <summary>模式 2：名称 + 编号 + 分隔符（新格式）箱体结构10、 第一空间S1、 液体3000。</summary>
        private static readonly Regex RePattern2 = new Regex(
            @"([\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9 ]*?)\s*([A-Z]?\d{1,5}(?:-[A-Z]?\d{1,5})?[A-Za-z]?)\s*[、；。，;,.]");

        /// <summary>模式 A：名称 + 括号编号 + 分隔符 加热器(1)、 泵体（2）；</summary>
        private static readonly Regex RePatternA = new Regex(
            @"([\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9 ]*?)\s*[（(]([A-Z]?\d{1,5}(?:-[A-Z]?\d{1,5})?[A-Za-z]?)[）)]\s*[、；。，;,.]");

        /// <summary>模式 3：裸列表（每行 名称 编号，行内无标点）箱体结构 10&lt;换行&gt;箱壁 1</summary>
        private static readonly Regex RePattern3 = new Regex(
            @"^\s*([\u4e00-\u9fa5A-Za-z ]+?)\s*([A-Z]?\d{1,5}(?:-[A-Z]?\d{1,5})?[A-Za-z]?)\s*$",
            RegexOptions.Multiline);

        // ================================================================
        // 段落定位（DictModel.bas ExtractMarkingSection 移植）
        // ================================================================

        /// <summary>标记头模式（按优先级），对应 VBA 的 patterns 数组。</summary>
        private static readonly string[] SectionHeaderPatterns = {
            "附图标记说明如下[：:\n\r]*",
            "附图标记说明[：:]\\s*",
            "附图标记[：:]\\s*",
            "标记说明如下[：:\n\r]*",
            "标记说明[：:]\\s*",
            "标号说明[：:]\\s*"
        };

        /// <summary>截取到下一个段落空行（两连换行）或文末。</summary>
        private static readonly Regex ReSectionEnd = new Regex(@"[\s\S]*?(\r\n\s*\r\n|\n\s*\n|\r\s*\r|\Z)");

        // ================================================================
        // 表格预处理（DictModel.bas v1.1 表格支持移植）
        // Word 表格在 Content.Text 中的文本流：单元格文本 + vbCr + Chr(7)。
        // ================================================================

        /// <summary>表格模式 A：编号单元格在前 "10" + vbCr + Chr(7) + "箱体结构" + vbCr + Chr(7) → 箱体结构10、</summary>
        private static readonly Regex ReTableA = new Regex(
            @"(\d{1,5}[A-Za-z]?)\r\x07([\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9]*)\r\x07");

        /// <summary>表格模式 B：名称单元格在前 "箱体结构" + vbCr + Chr(7) + "10" + vbCr + Chr(7) → 箱体结构10、</summary>
        private static readonly Regex ReTableB = new Regex(
            @"([\u4e00-\u9fa5][\u4e00-\u9fa5A-Za-z0-9]*?)\r\x07([A-Z]?\d{1,5}(?:-[A-Z]?\d{1,5})?[A-Za-z]?)\r\x07");

        // ================================================================
        // 公开入口
        // ================================================================

        /// <summary>
        /// 定位并截取「附图标记说明」段落。
        /// 匹配常见标记头（附图标记说明如下：/ 附图标记说明：/ 标记说明如下：/ 标号说明：）。
        /// 截取从标记头之后、到下一个段落空行或文末的内容。
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
            if (endMatch.Success)
            {
                result.SectionText = endMatch.Value;
                result.HeaderFound = true;
                return result;
            }

            result.SectionText = after;
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
            text = ReTableA.Replace(text, "$2$1、");
            text = ReTableB.Replace(text, "$1$2、");
            text = text.Replace("\x07", "、");
            return text;
        }

        /// <summary>
        /// 从文本中提取所有附图标记（等价于 Patterns.bas ExtractAll）。
        /// 调用前建议先用 ExtractMarkingSection 定位段落，否则按全文扫描（误匹配风险由调用方承担）。
        /// 若输入为 Word/网页粘贴文本（含表格流或 HTML 换行），先调用 Preprocess。
        /// </summary>
        public static List<MarkingHit> ExtractAll(string text)
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
                    // 复刻 VBA 原样：keepRanges.Add Array(ch(2), ch(3)) = [position, length]
                    // （VBA 此处未换算为 [start, end]，为保证行为一致原样保留）
                    keepRanges.Add(new int[] { ch.Position, ch.Length });
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

            // === 去重（number|name），保持首次出现顺序 ===
            return Dedupe(allHits);
        }

        // ================================================================
        // 内部逻辑（对应 VBA CollectHits / Overlaps / Dedupe）
        // ================================================================

        /// <summary>收集命中：与 VBA CollectHits 等价。nameFirst 决定捕获组顺序。</summary>
        private static void CollectHits(List<MarkingHit> hits, List<int[]> ranges,
                                        string text, Regex re, bool nameFirst)
        {
            foreach (Match m in re.Matches(text))
            {
                if (!m.Success) continue;
                MarkingHit h = new MarkingHit();
                if (nameFirst)
                {
                    h.Name = m.Groups[1].Value;
                    h.Number = m.Groups[2].Value;
                }
                else
                {
                    h.Number = m.Groups[1].Value;
                    h.Name = m.Groups[2].Value;
                }
                h.Position = m.Index;
                h.Length = m.Length;
                hits.Add(h);
                ranges.Add(new int[] { m.Index, m.Index + m.Length - 1 });
            }
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
