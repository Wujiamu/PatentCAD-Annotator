using System;
using System.IO;
using PatentMarker.IO;
namespace PatentMarker { internal static class PatentMarkerApp { internal static void RawLog(string message) { } } }
namespace PatentMarker.IO {
    public sealed class PatConfig { public string DefaultDictPath; }
    public static class ConfigLoader { public static PatConfig Current; }
    public sealed class TestDocument { public string Name; }
    public static class RuntimeHost { public static TestDocument ActiveDocument; }
}
internal class Program {
    static int failures;
    static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failures++; }
    static int Main() {
        string p = Path.Combine(Path.GetTempPath(), "g04-fw-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            string[] invalid = { "", "null", "[]", "{\"entries\":[],}", "{\"entries\":[]} trailing", "{\"x\":\"\\q\"}", "{\"x\":01}", "{\"x\":\"\u000b\"}", "{ /* c */ \"entries\":[]}" };
            for (int i = 0; i < invalid.Length; i++) { File.WriteAllText(p, invalid[i]); Check(DictLoader.Load(p) == null, "R002_Framework_SyntaxRoot[" + i + "]"); }
            string[] valid = { "{}", "{\"entries\":[]}", "{\"entries\":null,\"metadata\":null}",
                "{\"unknown\":[-3.25,0,1e2,-1.25e-2],\"entries\":[]}",
                "{\"entries\":[],\"entries\":[{\"number\":\"302\",\"name\":\"duplicate\"}]}" };
            for (int i = 0; i < valid.Length; i++) { File.WriteAllText(p, valid[i]); DictModel m = DictLoader.Load(p); Check(m != null && (i != 4 || m.Entries.Count == 1 && m.Entries[0].Number == "302"), "R002_Framework_Compat[" + i + "]"); }
            string huge = "{\"unknown\":[1e400,-1e999],\"entries\":[]}";
            Check(SimpleJson.ParseObject(huge) != null, "R002_Framework_ValidatorAcceptsHugeNumber");
            File.WriteAllText(p, huge);
            Check(DictLoader.Load(p) == null, "R002_Framework_ExistingNewtonsoftHugeLimit");
            Console.WriteLine("SUMMARY failures=" + failures);
            return failures == 0 ? 0 : 1;
        } finally { File.Delete(p); }
    }
}
