// G24 language gate NEGATIVE CONTROL: must be rejected by the same csc.exe.
// Auto-property is C# 3+; .NET 2.0 group would fail with CS8023/CS0501.
namespace G24Neg
{
    public class Neg
    {
        public int Value { get; set; }
    }
}
