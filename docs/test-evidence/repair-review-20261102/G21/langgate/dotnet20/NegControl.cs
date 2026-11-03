// Negative control for the G21 C# 2.0 gate: an auto-property (C# 3+) must be
// rejected by the same compiler, proving the gate really runs at C# 2.
namespace G21NegControl { public class Bad { public int Value { get; set; } } }
