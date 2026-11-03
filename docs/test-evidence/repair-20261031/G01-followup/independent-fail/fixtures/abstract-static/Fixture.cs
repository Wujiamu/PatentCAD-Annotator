using System;
using Xunit;
public class FixtureTests { [Fact] public void Valid() { Assert.True(true); } }
public abstract class HiddenTests { [Fact] public static void Unsupported() { throw new InvalidOperationException("must not run"); } }
