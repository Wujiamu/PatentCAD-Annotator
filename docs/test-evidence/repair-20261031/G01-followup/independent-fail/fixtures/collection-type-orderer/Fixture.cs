using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
[assembly: CollectionBehavior(DisableTestParallelization=true)]
[assembly: TestCollectionOrderer("DescendingCollectionOrderer", "Fixture")]
public sealed class DescendingCollectionOrderer : ITestCollectionOrderer { public IEnumerable<ITestCollection> OrderTestCollections(IEnumerable<ITestCollection> tests) { return tests.OrderByDescending(t => t.DisplayName, StringComparer.Ordinal); } }
public static class SharedState { public static bool Ready; }
[Collection("A")] public class ATests { [Fact] public void Setup() { SharedState.Ready=true; } }
[Collection("Z")] public class ZTests { [Fact] public void RequiresSetup() { Assert.True(SharedState.Ready, "setup did not run"); } }
