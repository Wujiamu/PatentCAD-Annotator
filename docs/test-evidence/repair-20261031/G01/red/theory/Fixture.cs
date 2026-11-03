using System; using System.Threading.Tasks; using Xunit;
public class FixtureTests { [Fact] public void Valid() { Assert.True(true); } [Theory] [InlineData(1)] public void Unsupported(int n) { } }