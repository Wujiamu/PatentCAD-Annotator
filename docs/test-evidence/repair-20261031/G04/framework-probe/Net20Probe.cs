using System;
using System.Collections.Generic;
using PatentMarker.IO;
internal class Program {
    static int Main() {
        Dictionary<string, object> obj = SimpleJson.ParseObject("{\"unknown\":1e400,\"value\":-12.5e+2}");
        if ((double)obj["value"] != -1250) return 1;
        try { SimpleJson.ParseObject("{\"a\":1,}"); return 2; }
        catch (FormatException) { Console.WriteLine("PASS net20 syntax and compatibility"); return 0; }
    }
}
