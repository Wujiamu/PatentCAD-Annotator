using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
public sealed class DescendingOrderer : ITestCaseOrderer { public IEnumerable<T> OrderTestCases<T>(IEnumerable<T> tests) where T : ITestCase { return tests.OrderByDescending(t => t.TestMethod.Method.Name, StringComparer.Ordinal); } }
[TestCaseOrderer("DescendingOrderer", "Fixture")] public class FixtureTests { static bool ready; [Fact] public void A_Setup() { ready=true; } [Fact] public void Z_RequiresSetup() { Assert.True(ready, "setup did not run"); } }
