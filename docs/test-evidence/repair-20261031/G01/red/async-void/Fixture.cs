using System; using System.Threading.Tasks; using Xunit;
public class FixtureTests { [Fact] public void Valid() { Assert.True(true); } [Fact] public async void Unsupported() { await Task.Yield(); } }