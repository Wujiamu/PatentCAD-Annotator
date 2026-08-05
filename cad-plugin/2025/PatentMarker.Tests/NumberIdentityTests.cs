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
            Assert.True(NumberIdentity.Comparer.Equals("S1", "s1"));
        }

        [Fact]
        public void Normalize_NullBecomesEmpty()
        {
            Assert.Equal("", NumberIdentity.Normalize(null));
        }
    }
}
