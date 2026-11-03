using System;
using System.Collections.Generic;
using Xunit;
using PatentMarker.IO;

namespace PatentMarker.Tests
{
    /// <summary>
    /// G02 解析器回归（R-016、R-017、R-018、R-021、R-032、R-033）。
    ///
    /// 每个方法只断言一条业务规则，期望值来自 docs/repair-backlog.md 的关闭标准与
    /// docs/repair-plan.md 第 3 节的统一行为契约，不来自当前实现的输出。
    /// 修复前的实现必须在这些断言上稳定变红（见 docs/test-evidence/repair-20261031/G02
    /// 下的 red-*.txt），因此这里不做“先跑一遍再把实际值抄成期望”的操作。
    /// </summary>
    public class G02ParserTests
    {
        private static List<MarkingHit> Parse(string text)
        {
            string pre = MarkingTextParser.Preprocess(text);
            MarkingSectionResult section = MarkingTextParser.ExtractMarkingSection(pre);
            return MarkingTextParser.ExtractAll(section.SectionText);
        }

        /// <summary>
        /// 统计通路的原始命中视图。用反射调用是为了让同一份测试源码也能编译
        /// 修复前的快照（那时该方法尚不存在）；缺失时抛出的异常就是 R-017 的
        /// 业务失败，而不是跳过。
        /// </summary>
        private static List<MarkingHit> ParseRaw(string text)
        {
            string pre = MarkingTextParser.Preprocess(text);
            MarkingSectionResult section = MarkingTextParser.ExtractMarkingSection(pre);
            System.Reflection.MethodInfo? method = typeof(MarkingTextParser).GetMethod(
                "ExtractAllRaw",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (method == null)
            {
                throw new InvalidOperationException(
                    "MarkingTextParser.ExtractAllRaw 缺失：统计没有未去重命中通路（R-017）");
            }
            object? result = method.Invoke(null, new object[] { section.SectionText });
            List<MarkingHit>? hits = result as List<MarkingHit>;
            if (hits == null)
            {
                throw new InvalidOperationException("MarkingTextParser.ExtractAllRaw 返回类型不符");
            }
            return hits;
        }

        private static string Describe(List<MarkingHit> hits)
        {
            string s = "[";
            for (int i = 0; i < hits.Count; i++)
            {
                if (i > 0) s += " ";
                s += hits[i].Number + "|" + hits[i].Name;
            }
            return s + "]";
        }

        // ============================================================
        // R-016 完整编号词法：编号在前 / 括号 / 表格都不截断
        // ============================================================

        /// <summary>R-016：编号在前的旧格式必须保留字母前缀 S1。</summary>
        [Fact]
        public void G02_R016_NumberFirstKeepsLetterPrefix()
        {
            List<MarkingHit> hits = Parse("S1底座，");
            Assert.Equal(1, hits.Count);
            Assert.Equal("S1", hits[0].Number);
            Assert.Equal("底座", hits[0].Name);
        }

        /// <summary>R-016：编号在前的旧格式必须保留连字符子编号 10-1。</summary>
        [Fact]
        public void G02_R016_NumberFirstKeepsHyphenSubNumber()
        {
            List<MarkingHit> hits = Parse("10-1支架；");
            Assert.Equal(1, hits.Count);
            Assert.Equal("10-1", hits[0].Number);
            Assert.Equal("支架", hits[0].Name);
        }

        /// <summary>R-016：括号变体（编号在前）同样保留字母前缀。</summary>
        [Fact]
        public void G02_R016_ParenthesizedNumberFirstKeepsLetterPrefix()
        {
            List<MarkingHit> hits = Parse("S1(底座)、");
            Assert.Equal(1, hits.Count);
            Assert.Equal("S1", hits[0].Number);
            Assert.Equal("底座", hits[0].Name);
        }

        /// <summary>R-016：字母+数字后缀 123A1 / 123A2 必须完整保留。</summary>
        [Fact]
        public void G02_R016_LetterDigitSuffixIsKeptWhole()
        {
            List<MarkingHit> hits = Parse("123A1第一支路，123A2第二支路；");
            Assert.Equal(2, hits.Count);
            Assert.Equal("123A1", hits[0].Number);
            Assert.Equal("第一支路", hits[0].Name);
            Assert.Equal("123A2", hits[1].Number);
            Assert.Equal("第二支路", hits[1].Name);
        }

        /// <summary>R-016：后缀里可以再带数字（123A10 是一个编号，不是 123A1 + 0）。</summary>
        [Fact]
        public void G02_R016_SuffixWithTrailingDigitsIsOneNumber()
        {
            List<MarkingHit> hits = Parse("123A10底座，");
            Assert.Equal(1, hits.Count);
            Assert.Equal("123A10", hits[0].Number);
            Assert.Equal("底座", hits[0].Name);
        }

        /// <summary>
        /// R-016：不能从更长编号的中间起配。S12345底座 必须整体识别为编号 S12345，
        /// 而不是丢掉字母前缀后只取 12345。
        /// </summary>
        [Fact]
        public void G02_R016_DoesNotCutFromMiddleOfLongerNumber()
        {
            List<MarkingHit> hits = Parse("S12345底座，");
            Assert.Equal(1, hits.Count);
            Assert.Equal("S12345", hits[0].Number);
            Assert.Equal("底座", hits[0].Name);
        }

        /// <summary>
        /// R-016：超长编号（6 位）不在支持的词法范围内，必须显式不匹配，
        /// 不能静默截成尾部数字 12345 / 23456。
        /// </summary>
        [Fact]
        public void G02_R016_OverlongNumberIsRejectedNotTruncated()
        {
            List<MarkingHit> hits = Parse("123456底座，");
            Assert.Equal(0, hits.Count);
        }

        /// <summary>R-016：四种写法混在一句里，各自都保留完整编号。</summary>
        [Fact]
        public void G02_R016_MixedFormsKeepFullNumbers()
        {
            List<MarkingHit> hits = Parse("S1底座，10-1支架；1(底座)、123A1第一支路，");
            Assert.Equal(4, hits.Count);
            Assert.Equal("S1", hits[0].Number);
            Assert.Equal("10-1", hits[1].Number);
            Assert.Equal("1", hits[2].Number);
            Assert.Equal("123A1", hits[3].Number);
        }

        /// <summary>R-016：表格流里编号单元格是 S1 时不能被截成 1。</summary>
        [Fact]
        public void G02_R016_TableStreamKeepsFullNumber()
        {
            // Word 表格流：单元格文本 + vbCr + Chr(7)，行末再加一组 vbCr + Chr(7)。
            string table = "S1\r\u0007底座\r\u0007\r\u000710\r\u0007支架\r\u0007";
            string pre = MarkingTextParser.Preprocess(table);
            List<MarkingHit> hits = MarkingTextParser.ExtractAll(pre);
            Assert.True(2 == hits.Count, "表格流命中条数: " + Describe(hits));
            Assert.Equal("S1", hits[0].Number);
            Assert.Equal("底座", hits[0].Name);
            Assert.Equal("10", hits[1].Number);
            Assert.Equal("支架", hits[1].Name);
        }

        // ============================================================
        // R-017 统计使用未去重的原始命中
        // ============================================================

        /// <summary>
        /// R-017：ExtractAll 仍按 (number,name) 去重（展示/粘贴契约不变），
        /// 但 ExtractAllRaw 必须保留每一次真实出现，供统计使用。
        /// </summary>
        [Fact]
        public void G02_R017_RawKeepsEveryOccurrenceWhileDisplayDedupes()
        {
            const string text = "1支架，1底座，1底座，";
            List<MarkingHit> display = Parse(text);
            List<MarkingHit> raw = ParseRaw(text);

            Assert.True(2 == display.Count, "去重视图条数: " + Describe(display));
            Assert.True(3 == raw.Count, "原始视图条数: " + Describe(raw));

            int baseCount = 0;
            for (int i = 0; i < raw.Count; i++)
            {
                if (raw[i].Number == "1" && raw[i].Name == "底座") baseCount++;
            }
            Assert.Equal(2, baseCount);
        }

        /// <summary>R-017：同一 (number,name) 只出现一次时，两个视图条数相同。</summary>
        [Fact]
        public void G02_R017_SingleOccurrenceGivesSameCountInBothViews()
        {
            const string text = "1支架，1底座，";
            Assert.Equal(2, Parse(text).Count);
            Assert.Equal(2, ParseRaw(text).Count);
        }

        /// <summary>
        /// R-017：去重视图保持首次出现顺序（对外契约不变），
        /// 不能因为新增原始视图而改变展示顺序。
        /// </summary>
        [Fact]
        public void G02_R017_DisplayKeepsFirstOccurrenceOrder()
        {
            const string text = "1支架，2外壳，1支架，";
            List<MarkingHit> display = Parse(text);
            Assert.Equal(2, display.Count);
            Assert.Equal("支架", display[0].Name);
            Assert.Equal("外壳", display[1].Name);
        }

        // ============================================================
        // R-018 混合格式按原文位置稳定排序
        // ============================================================

        /// <summary>
        /// R-018：新格式（名称在前）先出现时，输出顺序必须按原文位置，
        /// 不能被“旧格式优先收集”的梯队顺序打乱。
        /// </summary>
        [Fact]
        public void G02_R018_MixedFormatsFollowSourceOrder()
        {
            List<MarkingHit> hits = Parse("支架2、1底座，");
            Assert.True(2 == hits.Count, "支架2、1底座， 命中条数: " + Describe(hits));
            Assert.Equal("2", hits[0].Number);
            Assert.Equal("支架", hits[0].Name);
            Assert.Equal("1", hits[1].Number);
            Assert.Equal("底座", hits[1].Name);
        }

        /// <summary>R-018：新旧格式交替出现时，顺序仍然按原文位置。</summary>
        [Fact]
        public void G02_R018_AlternatingFormatsFollowSourceOrder()
        {
            List<MarkingHit> hits = Parse("箱体结构10、1底座，加热器(3)、2支架；");
            Assert.True(4 == hits.Count, "交替格式命中条数: " + Describe(hits));
            Assert.Equal("10", hits[0].Number);
            Assert.Equal("1", hits[1].Number);
            Assert.Equal("3", hits[2].Number);
            Assert.Equal("2", hits[3].Number);
        }

        /// <summary>
        /// R-018：括号变体在前、旧格式在后时，仍按原文位置输出，
        /// 同时保留“旧格式优先、重叠的新格式丢弃”的仲裁规则。
        /// </summary>
        [Fact]
        public void G02_R018_ParenthesizedBeforeLegacyKeepsSourceOrder()
        {
            List<MarkingHit> hits = Parse("加热器(3)、1底座，");
            Assert.True(2 == hits.Count, "加热器(3)、1底座， 命中条数: " + Describe(hits));
            Assert.Equal("3", hits[0].Number);
            Assert.Equal("加热器", hits[0].Name);
            Assert.Equal("1", hits[1].Number);
            Assert.Equal("底座", hits[1].Name);
        }

        // ============================================================
        // R-021 编号身份与显示分离
        // ============================================================

        /// <summary>
        /// R-021：编号身份按 NumberIdentity（去首尾空白 + 忽略大小写）归组，
        /// 但显示必须保留首次出现的原始写法，不做大写化、不转整数。
        /// </summary>
        [Fact]
        public void G02_R021_IdentityIgnoresCaseButDisplayKeepsOriginal()
        {
            Assert.True(NumberIdentity.AreEqual("1342A", "1342a"));
            Assert.True(NumberIdentity.AreEqual("S1", "s1"));
            Assert.True(NumberIdentity.AreEqual("131a", "131A"));
            Assert.True(NumberIdentity.AreEqual(" 10 ", "10"));

            // 不同的编号不能被归成同一个身份
            Assert.False(NumberIdentity.AreEqual("10-1", "101"));
            Assert.False(NumberIdentity.AreEqual("S1", "1"));
            Assert.False(NumberIdentity.AreEqual("123A1", "123"));
        }

        /// <summary>
        /// R-021：解析层不得改写编号原文——大小写、前导零和连字符都必须原样保留，
        /// 身份比较是消费端的事，显示用原文。
        /// </summary>
        [Fact]
        public void G02_R021_ParserPreservesOriginalNumberSpelling()
        {
            List<MarkingHit> hits = Parse("1342A过渡连接板部，1342b凹陷板部，");
            Assert.True(2 == hits.Count, "混合大小写编号命中条数: " + Describe(hits));
            Assert.Equal("1342A", hits[0].Number);
            Assert.Equal("过渡连接板部", hits[0].Name);
            Assert.Equal("1342b", hits[1].Number);
            Assert.Equal("凹陷板部", hits[1].Name);
        }

        /// <summary>
        /// R-021：编号身份比较必须与 NumberIdentity.Comparer 一致，
        /// 供字典/集合直接用作键时不出现同一编号两个条目。
        /// </summary>
        [Fact]
        public void G02_R021_NumberIdentityComparerMatchesAreEqual()
        {
            Dictionary<string, int> map = new Dictionary<string, int>(NumberIdentity.Comparer);
            map["1342A"] = 1;
            Assert.True(map.ContainsKey("1342a"));
            Assert.True(map.ContainsKey(" 1342A "));
            Assert.Equal(1, map.Count);

            map["10-1"] = 2;
            map["101"] = 3;
            Assert.Equal(3, map.Count);
        }

        // ============================================================
        // R-032 段落文末边界（\Z 被当成字面量 Z）
        // ============================================================

        /// <summary>
        /// R-032：名称里的大写 Z（2Z形支架）不是段落终止符，
        /// 它之后的条目必须保留在标记段落里。
        /// </summary>
        [Fact]
        public void G02_R032_CapitalZInsideNameDoesNotEndSection()
        {
            // 编号词法允许字母后缀，因此 2Z形支架 是 编号 2Z + 名称 形支架（与既有
            // VBA 语料 2442U形架 一致，本次不改变该拆分）。R-032 只要求：大写 Z 不能
            // 被当作文末，它之后的条目必须仍然保留。
            string text = "附图标记说明：1底座，2Z形支架，3外壳。";
            List<MarkingHit> hits = Parse(text);
            Assert.True(3 == hits.Count, "2Z形支架 之后的内容不得被截断: " + Describe(hits));
            Assert.Equal("2Z", hits[1].Number);
            Assert.Equal("形支架", hits[1].Name);
            Assert.Equal("3", hits[2].Number);
        }

        /// <summary>R-032：小写 z 同样不能被当作段落终止符。</summary>
        [Fact]
        public void G02_R032_LowerZInsideNameDoesNotEndSection()
        {
            string text = "附图标记说明：1底座，2z形支架，3外壳。";
            List<MarkingHit> hits = Parse(text);
            Assert.True(3 == hits.Count, "2z形支架 之后的内容不得被截断: " + Describe(hits));
            Assert.Equal("2z", hits[1].Number);
            Assert.Equal("形支架", hits[1].Name);
        }

        /// <summary>R-032：没有 Z 的普通标记段落仍然按句号截断，行为不变。</summary>
        [Fact]
        public void G02_R032_PlainSectionStillEndsAtFullStop()
        {
            List<MarkingHit> hits = Parse("附图标记说明：1底座，2支架。");
            Assert.Equal(2, hits.Count);
            Assert.Equal("支架", hits[1].Name);
        }

        /// <summary>
        /// R-032：空段落边界仍然生效——句号之后的下一段正文不得被并入标记段落。
        /// </summary>
        [Fact]
        public void G02_R032_BlankParagraphStillEndsSection()
        {
            string text = "附图标记说明：1底座，2支架。\r\n\r\n下一页200正文。";
            List<MarkingHit> hits = Parse(text);
            Assert.True(2 == hits.Count, "空段落之后的内容不得被并入: " + Describe(hits));
            for (int i = 0; i < hits.Count; i++)
            {
                Assert.False(hits[i].Number == "200");
            }
        }

        /// <summary>R-032：Z 与空段落同时存在时，两个边界都仍然生效。</summary>
        [Fact]
        public void G02_R032_ZAndBlankParagraphBothRespected()
        {
            string text = "附图标记说明：1底座，2Z形支架，3外壳。\r\n\r\n下一页200正文。";
            List<MarkingHit> hits = Parse(text);
            Assert.True(3 == hits.Count, "Z + 空段落命中条数: " + Describe(hits));
            for (int i = 0; i < hits.Count; i++)
            {
                Assert.False(hits[i].Number == "200");
            }
        }

        /// <summary>
        /// R-032：文末没有句号也没有空段落时，段落边界必须是真正的文本末尾
        /// （不能停在末尾换行之前，也不能把 \Z 当字面量 Z）。
        /// </summary>
        [Fact]
        public void G02_R032_SectionEndsAtTrueEndOfText()
        {
            // 标记段落必须延伸到真正的文本末尾；末尾条目没有分隔符，
            // 因此按既有分隔符规则不产生命中，这里只断言段落边界。
            string text = "附图标记说明：1底座，2支架";
            MarkingSectionResult section = MarkingTextParser.ExtractMarkingSection(text);
            Assert.Contains("2支架", section.SectionText);
            Assert.True(1 == Parse(text).Count, "无终止符段落命中条数: " + Describe(Parse(text)));
        }

        // ============================================================
        // R-033 行内空白（裸列表不得跨行拼接）
        // ============================================================

        /// <summary>
        /// R-033：普通段落后面紧跟一个孤立数字行时不得匹配。
        /// 名称与编号必须在同一行内。
        /// </summary>
        [Fact]
        public void G02_R033_BareListDoesNotCrossLineBreaks()
        {
            Assert.Equal(0, Parse("普通段落\n123\n").Count);
            Assert.Equal(0, Parse("普通段落\r\n123\r\n").Count);
            Assert.Equal(0, Parse("普通段落\r123\r").Count);
        }

        /// <summary>R-033：名称与编号在同一行时，普通空格仍然允许。</summary>
        [Fact]
        public void G02_R033_BareListAllowsSpaceWithinLine()
        {
            List<MarkingHit> hits = Parse("支架 123");
            Assert.Equal(1, hits.Count);
            Assert.Equal("123", hits[0].Number);
            Assert.Equal("支架", hits[0].Name);
        }

        /// <summary>R-033：多行裸列表（每行一条）仍然要全部识别。</summary>
        [Fact]
        public void G02_R033_BareListMatchesEachLine()
        {
            List<MarkingHit> hits = Parse("底座 1\n支架 2");
            Assert.True(2 == hits.Count, "多行裸列表命中条数: " + Describe(hits));
            Assert.Equal("1", hits[0].Number);
            Assert.Equal("2", hits[1].Number);
        }

        /// <summary>R-033：行内 Tab 与首尾空格仍然允许。</summary>
        [Fact]
        public void G02_R033_BareListAllowsTabAndSurroundingSpaces()
        {
            Assert.Equal(1, Parse("支架\t123").Count);
            List<MarkingHit> hits = Parse("  支架 123  \n");
            Assert.Equal(1, hits.Count);
            Assert.Equal("支架", hits[0].Name);
        }

        /// <summary>
        /// R-033：CRLF 与 CR 换行的多行裸列表同样按行匹配，
        /// 说明行内空白收紧没有破坏换行处理。
        /// </summary>
        [Fact]
        public void G02_R033_BareListHandlesCrlfAndCrLineBreaks()
        {
            Assert.Equal(2, Parse("底座 1\r\n支架 2").Count);
            Assert.Equal(2, Parse("底座 1\r支架 2").Count);
        }
    }
}
