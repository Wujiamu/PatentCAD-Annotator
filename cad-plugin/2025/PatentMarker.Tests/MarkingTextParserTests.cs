using Xunit;
using PatentMarker.IO;
using System.Collections.Generic;

namespace PatentMarker.Tests
{
    /// <summary>
    /// MarkingTextParser 识别引擎单元测试 — 与 Patterns.bas v3.0 逐模式对应。
    /// 权威基准：5 套部署包 vba/Patterns.bas（GBK 解码核对过完全一致）。
    /// </summary>
    public class MarkingTextParserTests
    {
        // ================================================================
        // 模式 1：编号 + 名称 + 分隔符（旧格式）
        // ================================================================

        [Fact]
        public void Pattern1_NumberName_Delimiter()
        {
            var hits = MarkingTextParser.ExtractAll("1底座， 2支架； 10外壳A1，");
            Assert.Equal(3, hits.Count);
            Assert.Equal("1", hits[0].Number);
            Assert.Equal("底座", hits[0].Name);
            Assert.Equal("2", hits[1].Number);
            Assert.Equal("支架", hits[1].Name);
            Assert.Equal("10", hits[2].Number);
            Assert.Equal("外壳A1", hits[2].Name);
        }

        [Fact]
        public void Pattern1_EnglishSemicolonDelimiter()
        {
            // v3.2 修复：全角分号也作为分隔符
            var hits = MarkingTextParser.ExtractAll("1底座；2支架；");
            Assert.Equal(2, hits.Count);
            Assert.Equal("1", hits[0].Number);
            Assert.Equal("底座", hits[0].Name);
        }

        // ================================================================
        // 模式 B：编号 + 括号名称 + 分隔符
        // ================================================================

        [Fact]
        public void PatternB_NumberParenName()
        {
            var hits = MarkingTextParser.ExtractAll("1(底座)、 10（泵体）；");
            Assert.Equal(2, hits.Count);
            Assert.Equal("1", hits[0].Number);
            Assert.Equal("底座", hits[0].Name);
            Assert.Equal("10", hits[1].Number);
            Assert.Equal("泵体", hits[1].Name);
        }

        // ================================================================
        // 模式 2：名称 + 编号 + 分隔符（新格式）
        // ================================================================

        [Fact]
        public void Pattern2_NameNumber()
        {
            var hits = MarkingTextParser.ExtractAll("箱体结构10、 第一空间S1、 隔板131a、");
            Assert.Equal(3, hits.Count);
            Assert.Equal("箱体结构", hits[0].Name);
            Assert.Equal("10", hits[0].Number);
            Assert.Equal("第一空间", hits[1].Name);
            Assert.Equal("S1", hits[1].Number);
            Assert.Equal("隔板", hits[2].Name);
            Assert.Equal("131a", hits[2].Number);
        }

        [Fact]
        public void Pattern2_SubNumberWithHyphen()
        {
            var hits = MarkingTextParser.ExtractAll("安装座10-1、 支板10-2、");
            Assert.Equal(2, hits.Count);
            Assert.Equal("安装座", hits[0].Name);
            Assert.Equal("10-1", hits[0].Number);
            Assert.Equal("支板", hits[1].Name);
            Assert.Equal("10-2", hits[1].Number);
        }

        [Fact]
        public void Pattern2_UppercaseSuffix()
        {
            var hits = MarkingTextParser.ExtractAll("过渡连接板部1342A、 凹陷板部1342B、");
            Assert.Equal(2, hits.Count);
            Assert.Equal("1342A", hits[0].Number);
            Assert.Equal("过渡连接板部", hits[0].Name);
            Assert.Equal("1342B", hits[1].Number);
            Assert.Equal("凹陷板部", hits[1].Name);
        }

        // ================================================================
        // 模式 A：名称 + 括号编号 + 分隔符
        // ================================================================

        [Fact]
        public void PatternA_NameParenNumber()
        {
            var hits = MarkingTextParser.ExtractAll("加热器(1)、 泵体（2）；");
            Assert.Equal(2, hits.Count);
            Assert.Equal("加热器", hits[0].Name);
            Assert.Equal("1", hits[0].Number);
            Assert.Equal("泵体", hits[1].Name);
            Assert.Equal("2", hits[1].Number);
        }

        // ================================================================
        // 模式 3：裸列表（每行 名称 编号）
        // ================================================================

        [Fact]
        public void Pattern3_BareListPerLine()
        {
            var hits = MarkingTextParser.ExtractAll("箱体结构 10\n箱壁 1\n底架 S1\n");
            Assert.Equal(3, hits.Count);
            Assert.Equal("箱体结构", hits[0].Name);
            Assert.Equal("10", hits[0].Number);
            Assert.Equal("箱壁", hits[1].Name);
            Assert.Equal("1", hits[1].Number);
            Assert.Equal("底架", hits[2].Name);
            Assert.Equal("S1", hits[2].Number);
        }

        // ================================================================
        // 区间重叠过滤：新格式与旧格式命中重叠时丢弃新格式
        // ================================================================

        [Fact]
        public void OverlapFilter_NewFormatDroppedWhenOverlappingOld()
        {
            // "10外壳A1，" 模式1命中 [0,6)；模式2在尾部命中 [1,6)，重叠 → 丢弃
            var hits = MarkingTextParser.ExtractAll("10外壳A1，");
            Assert.Single(hits);
            Assert.Equal("10", hits[0].Number);
            Assert.Equal("外壳A1", hits[0].Name);
        }

        [Fact]
        public void OverlapFilter_MixedFormats_NoOverlapKept()
        {
            // "箱体结构10、" 只有模式2命中；"底座1，" 模式1与模式2同时命中 → 保留模式1
            var hits = MarkingTextParser.ExtractAll("箱体结构10、 底座1，");
            Assert.Equal(2, hits.Count);
            Assert.Equal("10", hits[0].Number);
            Assert.Equal("箱体结构", hits[0].Name);
            Assert.Equal("1", hits[1].Number);
            Assert.Equal("底座", hits[1].Name);
        }

        [Fact]
        public void OverlapFilter_BareLineSkippedWhenPunctuated()
        {
            // 行 "箱体结构 10，" 带标点：模式3 不匹配（行尾有逗号），模式2 命中
            var hits = MarkingTextParser.ExtractAll("箱体结构 10，");
            Assert.Single(hits);
            Assert.Equal("箱体结构", hits[0].Name);
            Assert.Equal("10", hits[0].Number);
        }

        // ================================================================
        // 去重：按 (number|name) 保持首次出现顺序
        // ================================================================

        [Fact]
        public void Dedupe_SameNumberName_KeepsFirst()
        {
            var hits = MarkingTextParser.ExtractAll("1底座， 1底座、 2支架，");
            Assert.Equal(2, hits.Count);
            Assert.Equal("1", hits[0].Number);
            Assert.Equal("2", hits[1].Number);
        }

        // ================================================================
        // 段落定位 ExtractMarkingSection
        // ================================================================

        [Fact]
        public void ExtractSection_WithHeader_ReturnsSectionText()
        {
            string text = "本发明涉及一种设备。\n附图标记说明如下：\n1底座， 2支架；\n\n以上仅为本发明实施例。";
            var result = MarkingTextParser.ExtractMarkingSection(text);
            Assert.True(result.HeaderFound);
            Assert.Contains("1底座", result.SectionText);
            Assert.Contains("2支架", result.SectionText);
            Assert.DoesNotContain("以上仅为本发明实施例", result.SectionText);
        }

        [Fact]
        public void ExtractSection_NoHeader_FallsBackToFullText()
        {
            string text = "1底座， 2支架；";
            var result = MarkingTextParser.ExtractMarkingSection(text);
            Assert.False(result.HeaderFound);
            Assert.Equal(text, result.SectionText);
        }

        [Fact]
        public void ExtractSection_HeaderVariants()
        {
            foreach (string header in new[] { "附图标记说明：", "标记说明如下：", "标号说明：", "附图标记说明如下：" })
            {
                var result = MarkingTextParser.ExtractMarkingSection(header + "\n1底座，");
                Assert.True(result.HeaderFound, "header: " + header);
                Assert.Contains("1底座", result.SectionText);
            }
        }

        // ================================================================
        // 综合：段落定位 + 提取
        // ================================================================

        [Fact]
        public void EndToEnd_SectionExtractThenParse()
        {
            string text = "说明书正文……\n附图标记说明如下：\n1底座， 2支架； 3(泵体)、\n加热器(1)、\n箱体结构10、 隔板131a、\n\n正文结束。";
            var section = MarkingTextParser.ExtractMarkingSection(text);
            var hits = MarkingTextParser.ExtractAll(section.SectionText);
            // 梯队顺序（与 VBA 一致）：模式1/模式B 先收集，模式2 先于模式A
            Assert.Equal(6, hits.Count);
            Assert.Equal("1", hits[0].Number);
            Assert.Equal("2", hits[1].Number);
            Assert.Equal("3", hits[2].Number);
            Assert.Equal("10", hits[3].Number);
            Assert.Equal("箱体结构", hits[3].Name);
            Assert.Equal("131a", hits[4].Number);
            Assert.Equal("隔板", hits[4].Name);
            Assert.Equal("1", hits[5].Number);
            Assert.Equal("加热器", hits[5].Name);
        }

        // ================================================================
        // 边界
        // ================================================================

        [Fact]
        public void EmptyText_ReturnsEmpty()
        {
            Assert.Empty(MarkingTextParser.ExtractAll(""));
            Assert.Empty(MarkingTextParser.ExtractAll(null!));
        }

        [Fact]
        public void HtmlLineBreaks_DocumentPath_NormalizedToLineBreak()
        {
            // 文档路径：Preprocess 把 <br> 转 vbCr，ExtractAll 内 vbCr→vbLf，裸列表按行匹配
            string text = "<br/>箱体结构 10<br>箱壁 1<br/>";
            var section = MarkingTextParser.ExtractMarkingSection(MarkingTextParser.Preprocess(text));
            var hits = MarkingTextParser.ExtractAll(section.SectionText);
            Assert.Equal(2, hits.Count);
            Assert.Equal("箱体结构", hits[0].Name);
            Assert.Equal("10", hits[0].Number);
            Assert.Equal("箱壁", hits[1].Name);
            Assert.Equal("1", hits[1].Number);
        }

        [Fact]
        public void HtmlLineBreaks_DirectExtract_TreatedAsSpace()
        {
            // 直通 ExtractAll（不经 Preprocess）：<br> → 空格（与 VBA Patterns.ExtractAll 一致）
            var hits = MarkingTextParser.ExtractAll("<br/>箱体结构 10<br>箱壁 1<br/>");
            Assert.Empty(hits);
        }

        // ================================================================
        // 表格预处理（DictModel.bas v1.1 移植）
        // 注意：\x07 后紧跟十六进制字符会被贪婪解析（如 \x0710 → U+0710），
        // 故 BEL 统一用 \u0007（固定 4 位，不贪婪）。
        // ================================================================

        [Fact]
        public void TablePreprocess_NumberCellFirst()
        {
            // Word 表格流："10" + vbCr + Chr(7) + "箱体结构" + vbCr + Chr(7) + vbCr + Chr(7)
            string pre = MarkingTextParser.Preprocess("10\r\u0007箱体结构\r\u0007\r\u0007");
            Assert.Equal("箱体结构10、\r、", pre);
        }

        [Fact]
        public void TablePreprocess_NameCellFirst()
        {
            string pre = MarkingTextParser.Preprocess("箱体结构\r\u000710\r\u0007\r\u0007");
            Assert.Equal("箱体结构10、\r、", pre);
        }

        [Fact]
        public void TablePreprocess_MixedRows_ThenExtract()
        {
            string text = "10\r\u0007箱体结构\r\u0007\r\u0007箱壁\r\u00071\r\u0007\r\u0007";
            string pre = MarkingTextParser.Preprocess(text);
            Assert.Equal("箱体结构10、\r、箱壁1、\r、", pre);

            var section = MarkingTextParser.ExtractMarkingSection(pre);
            var hits = MarkingTextParser.ExtractAll(section.SectionText);
            Assert.Equal(2, hits.Count);
            Assert.Equal("箱体结构", hits[0].Name);
            Assert.Equal("10", hits[0].Number);
            Assert.Equal("箱壁", hits[1].Name);
            Assert.Equal("1", hits[1].Number);
        }

        [Fact]
        public void Crlf_NormalizedForBareList()
        {
            var hits = MarkingTextParser.ExtractAll("箱体结构 10\r\n箱壁 1\r\n");
            Assert.Equal(2, hits.Count);
        }

        [Fact]
        public void ExtractSection_WordCrOnly_StopsAtBlankParagraph()
        {
            string text = "正文\r附图标记说明如下：\r100板式换热器；\r110板片；\r\r具体实施方式\r图1所示板式换热器100。";
            var section = MarkingTextParser.ExtractMarkingSection(text);

            Assert.True(section.HeaderFound);
            Assert.Equal("100板式换热器；\r110板片；\r\r", section.SectionText);
            Assert.Equal(2, MarkingTextParser.ExtractAll(section.SectionText).Count);
        }
    }
}
