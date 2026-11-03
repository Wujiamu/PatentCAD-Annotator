// Negative control for the G20 language gate.
//
// This file must NOT compile at LangVersion=2 (auto-properties are C# 3+). It
// proves the gate above really runs at C# 2 and is not silently accepting a
// modern language level. It is never part of the gate's own build.
namespace NegCtl { public class C { public int P { get; set; } } }
