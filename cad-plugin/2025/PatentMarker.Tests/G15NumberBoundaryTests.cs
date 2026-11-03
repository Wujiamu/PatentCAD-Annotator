using System.Collections.Generic;
using Xunit;
using PatentMarker.IO;

namespace PatentMarker.Tests
{
    /// <summary>
    /// G15 回归（R-016 补修）：名称在前的超长编号不得被静默截尾。
    /// 期望来自 docs/repair-backlog.md 的 R-016 关闭标准与
    /// docs/repair-plan.md 第 11 节 G15 的矩阵，不来自当前实现输出。
    /// 修复前：`底座123456、` 被识别为 23456，首位 1 被吞入名称。
    /// </summary>
    public sealed class G15NumberBoundaryTests
    {
        private static List<MarkingHit> Parse(string text)
        {
            string pre = MarkingTextParser.Preprocess(text);
            MarkingSectionResult section = MarkingTextParser.ExtractMarkingSection(pre);
            return MarkingTextParser.ExtractAll(section.SectionText);
        }

        private static string Numbers(List<MarkingHit> hits)
        {
            var parts = new List<string>();
            foreach (MarkingHit hit in hits) parts.Add(hit.Number + "|" + hit.Name);
            return string.Join(" ", parts.ToArray());
        }

        [Fact]
        public void G15_NameFirst_SixDigits_IsRejectedNotTruncated()
        {
            Assert.Equal("", Numbers(Parse("底座123456、")));
        }

        [Fact]
        public void G15_NameFirst_SixDigitsWithSpace_IsRejectedNotTruncated()
        {
            Assert.Equal("", Numbers(Parse("底座 123456、")));
        }

        [Fact]
        public void G15_NameFirst_SixDigitsWithTab_IsRejectedNotTruncated()
        {
            Assert.Equal("", Numbers(Parse("底座\t123456、")));
        }

        [Fact]
        public void G15_NameFirst_FiveDigits_StillMatches()
        {
            Assert.Equal("12345|底座", Numbers(Parse("底座12345、")));
        }

        [Fact]
        public void G15_NameFirst_FiveDigitsWithSpace_StillMatches()
        {
            Assert.Equal("12345|底座", Numbers(Parse("底座 12345、")));
        }

        [Fact]
        public void G15_NameFirst_LetterPrefixSixDigits_IsRejected()
        {
            // S123456 的完整编号超过 5 位，不能退化成 12345 或 23456。
            Assert.Equal("", Numbers(Parse("底座S123456、")));
        }

        [Fact]
        public void G15_NameFirst_LetterPrefixFiveDigits_StillMatches()
        {
            Assert.Equal("S12345|底座", Numbers(Parse("底座S12345、")));
        }

        [Fact]
        public void G15_NameFirst_HyphenSubNumber_StillMatches()
        {
            Assert.Equal("10-1|支架", Numbers(Parse("支架10-1、")));
        }

        [Fact]
        public void G15_NameFirst_HyphenSubNumberWithLongTail_IsRejected()
        {
            // 子号部分同样受 5 位上限约束。
            Assert.Equal("", Numbers(Parse("支架10-123456、")));
        }

        [Fact]
        public void G15_Parenthesized_SixDigits_IsRejected()
        {
            Assert.Equal("", Numbers(Parse("底座(123456)、")));
        }

        [Fact]
        public void G15_Parenthesized_FiveDigits_StillMatches()
        {
            Assert.Equal("12345|底座", Numbers(Parse("底座(12345)、")));
        }

        [Fact]
        public void G15_NameContainingDigits_IsNotRejected()
        {
            // 名称自带数字是合法的；不能被“编号左边界”规则连带拒掉。
            Assert.Equal("123|第2定位件", Numbers(Parse("第2定位件 123、")));
        }

        [Fact]
        public void G15_InvalidFollowedByValid_KeepsOnlyTheValidOne()
        {
            Assert.Equal("301|端盖", Numbers(Parse("底座123456、端盖301、")));
        }

        [Fact]
        public void G15_BareList_SixDigits_IsRejected()
        {
            Assert.Equal("", Numbers(Parse("底座 123456\n")));
        }

        [Fact]
        public void G15_BareList_FiveDigits_StillMatches()
        {
            Assert.Equal("12345|底座", Numbers(Parse("底座 12345\n")));
        }

        [Fact]
        public void G15_NumberFirst_SixDigits_StillRejected()
        {
            // 原 R-016 修复的场景不得回退。
            Assert.Equal("", Numbers(Parse("123456底座，")));
        }

        [Fact]
        public void G15_NumberFirst_FiveDigits_StillMatches()
        {
            Assert.Equal("12345|底座", Numbers(Parse("12345底座，")));
        }

        [Fact]
        public void G15_MixedValidForms_KeepOrderAndFullNumbers()
        {
            Assert.Equal("2|支架 1|底座", Numbers(Parse("支架2、1底座，")));
        }
    }
}
