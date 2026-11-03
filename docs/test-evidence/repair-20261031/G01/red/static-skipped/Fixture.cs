using System; using System.Threading.Tasks; using Xunit;
public class FixtureTests { [Fact] public void Valid() { Assert.True(true); } [Fact(Skip="later")] public static void Unsupported() { } }