using System;
using System.Reflection;
using Xunit;
using Xunit.Sdk;
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, Inherited=true)] public class RequireCleanupAttribute : BeforeAfterTestAttribute { public override void After(MethodInfo methodUnderTest) { throw new InvalidOperationException("required cleanup failed"); } }
[RequireCleanup] public class FixtureTests { [Fact] public void Valid() { Assert.True(true); } }
