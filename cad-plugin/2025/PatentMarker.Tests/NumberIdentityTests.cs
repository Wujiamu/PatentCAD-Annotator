using PatentMarker.IO;
using Xunit;

namespace PatentMarker.Tests
{
    public class NumberIdentityTests
    {
        [Fact]
        public void AreEqual_TrimsAndIgnoresCase()
        {
            Assert.True(NumberIdentity.AreEqual(" 1342A ", "1342a"));
        }

        [Fact]
        public void Comparer_UsesTheSameRuleAsAreEqual()
        {
            Assert.True(NumberIdentity.Comparer.Equals(" 1342A ", "1342a"));
            Assert.True(NumberIdentity.Comparer.Equals("S1", "s1"));
            Assert.False(NumberIdentity.Comparer.Equals("12", "123"));
        }

        [Fact]
        public void Comparer_ProducesTheSameHashCodeForEqualValues()
        {
            Assert.Equal(
                NumberIdentity.Comparer.GetHashCode(" 1342A "),
                NumberIdentity.Comparer.GetHashCode("1342a"));
        }

        [Fact]
        public void Comparer_MatchesNormalizedDictionaryKeys()
        {
            var numbers = new System.Collections.Generic.HashSet<string>(NumberIdentity.Comparer);

            Assert.True(numbers.Add("  S1  "));
            Assert.False(numbers.Add("s1"));
        }

        [Fact]
        public void Normalize_NullBecomesEmpty()
        {
            Assert.Equal("", NumberIdentity.Normalize(null!));
        }
    }
}
