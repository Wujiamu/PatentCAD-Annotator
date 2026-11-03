namespace PatentMarker
{
    internal static class PatentMarkerApp
    {
        internal static void RawLog(string message) { }
    }
}
namespace PatentMarker.IO
{
#if !LEGACY
    public sealed class PatConfig
    {
        public string DefaultDictPath;
        public PatStyleConfig PatStyle;
        public AlignConfig Align;
    }
    public sealed class PatStyleConfig { public double TextHeight; }
    public sealed class AlignConfig { public double MarginToFrame; }
    public static class ConfigLoader { public static PatConfig Current; }
#endif
    public sealed class TestEditor { public void WriteMessage(string message) { } }
    public sealed class TestDocument { public string Name; public TestEditor Editor = new TestEditor(); }
    public static class RuntimeHost { public static TestDocument ActiveDocument; }
}
