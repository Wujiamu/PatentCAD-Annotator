using System;
using System.Reflection;
using Xunit;
using Xunit.Sdk;
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, Inherited=true)] public class RequireSetupAttribute : BeforeAfterTestAttribute { public override void Before(MethodInfo methodUnderTest) { throw new InvalidOperationException("required setup failed"); } }
public abstract class BaseTests { [Fact] [RequireSetup] public void Valid() { Assert.True(true); } } public class FixtureTests : BaseTests { }
