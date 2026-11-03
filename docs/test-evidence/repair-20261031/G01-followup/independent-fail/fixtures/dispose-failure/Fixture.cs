using System;
using Xunit;
public class FixtureTests : IDisposable { [Fact] public void Valid() { Assert.True(true); } public void Dispose() { throw new InvalidOperationException("cleanup failed"); } }
