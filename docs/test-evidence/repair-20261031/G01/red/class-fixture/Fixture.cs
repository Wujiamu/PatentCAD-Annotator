using System; using System.Threading.Tasks; using Xunit;
public class Resource { } public class FixtureTests : IClassFixture<Resource> { [Fact] public void Unsupported() { } }