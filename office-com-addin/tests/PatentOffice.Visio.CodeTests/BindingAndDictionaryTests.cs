using System;
using System.IO;
using PatentMarker.IO;
using PatentOffice.Visio;
using PatentOffice.Shared;
using Xunit;

namespace PatentOffice.Visio.CodeTests
{
    public sealed class BindingAndDictionaryTests
    {
        [Theory]
        [InlineData("")]
        [InlineData("编号 1")]
        [InlineData("A\"B")]
        [InlineData("  中文\\路径\\字典.dict.json  ")]
        public void ShapeSheetString_RoundTripsLiteralValues(string value)
        {
            Assert.Equal(value, ShapeSheetString.Decode(ShapeSheetString.Encode(value)));
        }

        [Fact]
        public void BindingPath_UsesRelativePathOnSameVolume_AndResolvesIt()
        {
            string root = Path.GetTempPath();
            string document = Path.Combine(root, "source", "diagram file.vsdx");
            string dictionary = Path.Combine(root, "source", "nested", "parts.dict.json");
            string kind;

            string stored = BindingPath.ToStoredPath(document, dictionary, out kind);
            string resolved = BindingPath.Resolve(document, stored, kind);

            Assert.Equal("relative", kind);
            Assert.Equal(Path.GetFullPath(dictionary), resolved);
        }

        [Fact]
        public void BindingPath_UsesAbsolutePathAcrossVolumes()
        {
            string documentRoot = Path.GetPathRoot(Path.GetTempPath());
            char alternativeDrive = char.ToUpperInvariant(documentRoot[0]) == 'C' ? 'D' : 'C';
            string dictionary = alternativeDrive + @":\Word Output\parts.dict.json";
            string kind;

            string stored = BindingPath.ToStoredPath(Path.Combine(Path.GetTempPath(), "diagram.vsdx"),
                dictionary, out kind);

            Assert.Equal("absolute", kind);
            Assert.Equal(Path.GetFullPath(dictionary), stored);
            Assert.Equal(Path.GetFullPath(dictionary), BindingPath.Resolve("C:\\diagram.vsdx", stored, kind));
        }

        [Theory]
        [InlineData(" 1342A ", "1342a", true)]
        [InlineData("S1", "s1", true)]
        [InlineData("12", "123", false)]
        public void NumberIdentity_MatchesTheCadComparisonRule(string left, string right, bool expected)
        {
            Assert.Equal(expected, NumberIdentity.AreEqual(left, right));
        }

        [Fact]
        public void NumberIdentityComparer_TrimsKeysForSetAndDictionaryUse()
        {
            var numbers = new System.Collections.Generic.HashSet<string>(NumberIdentity.Comparer);

            Assert.True(numbers.Add(" 1342A "));
            Assert.False(numbers.Add("1342a"));
        }
    }
}
