using System;
using System.Reflection;
using Xunit;
using Xunit.Sdk;
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, Inherited=true)] public class RequireSetupAttribute : BeforeAfterTestAttribute { public override void Before(MethodInfo methodUnderTest) { throw new InvalidOperationException("required setup failed"); } }
public sealed class ChildSetupAttribute : RequireSetupAttribute { } public class FixtureTests { [Fact] [ChildSetup] public void Valid() { Assert.True(true); } }
