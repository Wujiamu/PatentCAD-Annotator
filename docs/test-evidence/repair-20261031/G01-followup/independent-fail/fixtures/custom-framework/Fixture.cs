using System;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
[assembly: TestFramework("BrokenFramework", "Fixture")]
public sealed class BrokenFramework : XunitTestFramework { public BrokenFramework(IMessageSink diagnostics) : base(diagnostics) { throw new InvalidOperationException("custom executor failed"); } }
public class FixtureTests { [Fact] public void Valid() { Assert.True(true); } }
