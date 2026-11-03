using System;
using System.Reflection;
using Xunit;
using Xunit.Sdk;
[assembly: RequireSetup]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, Inherited=true)] public class RequireSetupAttribute : BeforeAfterTestAttribute { public override void Before(MethodInfo methodUnderTest) { throw new InvalidOperationException("required setup failed"); } }
public class FixtureTests { [Fact] public void Valid() { Assert.True(true); } }
