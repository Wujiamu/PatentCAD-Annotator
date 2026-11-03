using System;
using System.Reflection;
using Xunit;
using Xunit.Sdk;
public class FixtureTests : IDisposable { [Fact] public void Fails() { Assert.True(false, "assertion failed"); } public void Dispose() { throw new InvalidOperationException("cleanup failed"); } }
