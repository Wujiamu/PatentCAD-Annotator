using System; using System.Threading.Tasks; using Xunit;
public class FixtureTests { [Fact] public void Valid() { Assert.Equal(2, 1 + 1); } }