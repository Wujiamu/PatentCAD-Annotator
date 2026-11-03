using System;
using System.Reflection;
using Xunit;
using Xunit.Sdk;
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly, Inherited=true)] public class RequireSetupAttribute : BeforeAfterTestAttribute { public override void Before(MethodInfo methodUnderTest) { throw new InvalidOperationException("required setup failed"); } }
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)] public sealed class LabelAttribute : Attribute { } [Label] public class FixtureTests { [Fact] [Trait("kind", "unit")] [Label] public void Valid() { Assert.True(true); } }
