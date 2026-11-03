using System;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
[assembly: TestFramework("MarkingFramework", "Fixture")]
public sealed class MarkingFramework : XunitTestFramework { public MarkingFramework(IMessageSink diagnostics) : base(diagnostics) { Environment.SetEnvironmentVariable("PAT_G01_CUSTOM_FRAMEWORK", "active"); } }
public class FixtureTests { [Fact] public void Valid() { Assert.Null(Environment.GetEnvironmentVariable("PAT_G01_CUSTOM_FRAMEWORK")); } }
