using System.Text;
using System.Text.Json;
using PatentMarker.IO;

internal static class Program
{
    private static int _failures;
    private static string _dir;

    private static void Check(bool result, string label)
    {
        Console.WriteLine((result ? "PASS " : "FAIL ") + label);
        if (!result) _failures++;
    }

    private static void Run(string name, Action action)
    {
        try { action(); }
        catch (Exception ex) { Console.WriteLine("FAIL " + name + " exception: " + ex); _failures++; }
    }

    private static string FileWith(string name, string json)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return path;
    }

    private static void R002_Syntax()
    {
        string[] bad = {
            "", " \r\n\t", "{ invalid json }}}", "{\"entries\":[{\"number\":\"301\",\"name\":\"sample\"}",
            "{\"entries\":[]} junk", "{\"entries\" []}", "{\"entries\":[] \"warnings\":[]}",
            "{\"entries\":[1,]}", "{\"entries\":[],}", "{\"x\":\"\\q\"}", "{\"x\":\"\\u00X0\"}",
            "{\"x\":\"\\u00\"}", "{\"x\":\"unclosed}", "{\"x\":\"\u000b\"}",
            "{\"x\":01}", "{\"x\":1e}", "{\"x\":+1}", "{\"x\":-.1}",
            "{\"x\":truee}", "{\"x\":nullx}", "{ /* comment */ \"entries\":[]}", "{\"x\":NaN}"
        };
        for (int i = 0; i < bad.Length; i++)
            Check(DictLoader.Load(FileWith("bad-" + i, bad[i])) == null, "R002_Syntax[" + i + "]");
    }

    private static void R002_Root()
    {
        string[] bad = { "null", "[]", "\"hello\"", "0", "true" };
        for (int i = 0; i < bad.Length; i++)
            Check(DictLoader.Load(FileWith("root-" + i, bad[i])) == null, "R002_Root[" + i + "]");
    }

    private static void R002_Compat()
    {
        string[] good = {
            "{}", "{\"entries\":[]}", "{\"entries\":null,\"metadata\":null,\"warnings\":null}",
            "{\"unknown\":{\"x\":true,\"y\":[-1.25e+2,null,\"中文\"]},\"entries\":[{\"number\":\"301\",\"name\":\"sample\"}]}",
            "{\"unknown\":[1e400,-1E999,-0.125,0,123.75,4e-2],\"entries\":[]}",
            "{\"entries\":[],\"entries\":[{\"number\":\"302\",\"name\":\"duplicate\"}]}"
        };
        for (int i = 0; i < good.Length; i++)
        {
            DictModel model = DictLoader.Load(FileWith("good-" + i, good[i]));
            Check(model != null && model.Metadata != null && model.Entries != null && model.Warnings != null &&
                (i != 3 || model.Entries.Count == 1 && model.Entries[0].Number == "301" && model.Entries[0].Name == "sample") &&
                (i != 5 || model.Entries.Count == 1 && model.Entries[0].Number == "302"),
                "R002_Compat[" + i + "]");
        }
    }

#if LEGACY
    private static void R002_Config()
    {
        string valid = FileWith("config-valid.json", "{\"defaultDictPath\":\"example\",\"patStyle\":{\"textHeight\":2.5},\"unknown\":1e400}");
        PatConfig config = ConfigLoader.Load(valid);
        Check(config != null && config.DefaultDictPath == "example" && config.PatStyle != null && config.PatStyle.TextHeight == 2.5,
            "R002_Config valid config and unknown large number");
        string bad = FileWith("config-bad.json", "{\"defaultDictPath\":\"damaged\"");
        config = ConfigLoader.Load(bad);
        Check(config != null && config.DefaultDictPath == "", "R002_Config malformed uses defaults");
    }
#endif

    private static void R002_Restore()
    {
        string[] broken = { "{\"entries\":[{\"number\":\"301\",\"name\":\"partial\"}", "", "null", "[]",
            "{\"entries\":[]} trailing", "{\"x\":\"\\q\"}" };
        for (int i = 0; i < broken.Length; i++)
        {
            string current = FileWith("current-" + i + ".dict.json", "{\"entries\":[{\"number\":\"101\",\"name\":\"original\"}]}");
            string backup = FileWith("current-" + i + ".dict.json.word-20261031-010101.bak", broken[i]);
            File.SetAttributes(current, FileAttributes.Archive);
            File.SetAttributes(backup, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
            byte[] currentBytes = File.ReadAllBytes(current), backupBytes = File.ReadAllBytes(backup);
            FileAttributes currentAttributes = File.GetAttributes(current), backupAttributes = File.GetAttributes(backup);
            string error;
            DictModel result = DictConflict.ResolveRestoreCad(current, DictFileVisibility.ReadIdentity(current), backup,
                DictFileVisibility.ReadIdentity(backup), () => true, out error);
            string label = "R002_Restore[" + i + "] ";
            Check(result == null, label + "returns failure");
            Check(error == "Backup dictionary cannot be parsed.", label + "error visible");
            Check(File.Exists(current) && currentBytes.SequenceEqual(File.ReadAllBytes(current)), label + "current bytes");
            Check(File.Exists(backup) && backupBytes.SequenceEqual(File.ReadAllBytes(backup)), label + "backup bytes");
            Check(File.Exists(current) && currentAttributes == File.GetAttributes(current), label + "current attributes");
            Check(File.Exists(backup) && backupAttributes == File.GetAttributes(backup), label + "backup attributes");
        }
    }

    private static void R002_Utf8()
    {
        string goodJson = "{\"entries\":[{\"number\":\"301\",\"name\":\"中文\"}]}";
        byte[] good = Encoding.UTF8.GetBytes(goodJson);
        byte[] bom = { 0xef, 0xbb, 0xbf };
        foreach (bool hasBom in new[] { false, true })
        {
            byte[] bytes = hasBom ? bom.Concat(good).ToArray() : good;
            string path = Path.Combine(_dir, "utf8-good-" + hasBom);
            File.WriteAllBytes(path, bytes);
            DictModel loaded = DictLoader.Load(path);
            Check(loaded != null && loaded.Entries.Count == 1 && loaded.Entries[0].Name == "中文",
                "R002_Utf8 valid Chinese BOM=" + hasBom);
        }
        byte[] prefix = Encoding.UTF8.GetBytes("{\"entries\":[{\"number\":\"301\",\"name\":\"broken");
        byte[] suffix = Encoding.UTF8.GetBytes("\"}]}");
        byte[][] invalid = { new byte[] { 0xff }, new byte[] { 0xc3 }, new byte[] { 0xc0, 0x80 } };
        for (int i = 0; i < invalid.Length; i++)
        foreach (bool hasBom in new[] { false, true })
        {
            byte[] broken = (hasBom ? bom : Array.Empty<byte>()).Concat(prefix).Concat(invalid[i]).Concat(suffix).ToArray();
            string id = i + "-" + hasBom;
            string bad = Path.Combine(_dir, "utf8-bad-" + id);
            File.WriteAllBytes(bad, broken);
            Check(DictLoader.Load(bad) == null, "R002_Utf8 malformed rejected " + id);
            string current = FileWith("utf8-current-" + id + ".dict.json", goodJson);
            string backup = Path.Combine(_dir, "utf8-current-" + id + ".dict.json.word-20261101-010101.bak");
            File.WriteAllBytes(backup, broken);
            File.SetAttributes(current, FileAttributes.Archive);
            File.SetAttributes(backup, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
            byte[] original = File.ReadAllBytes(current);
            FileAttributes currentAttributes = File.GetAttributes(current), backupAttributes = File.GetAttributes(backup);
            string error;
            DictModel result = DictConflict.ResolveRestoreCad(current, DictFileVisibility.ReadIdentity(current), backup,
                DictFileVisibility.ReadIdentity(backup), () => true, out error);
            Check(result == null && error == "Backup dictionary cannot be parsed.", "R002_Utf8 restore rejected " + id);
            Check(File.Exists(current) && original.SequenceEqual(File.ReadAllBytes(current)) &&
                File.GetAttributes(current) == currentAttributes, "R002_Utf8 current preserved " + id);
            Check(File.Exists(backup) && broken.SequenceEqual(File.ReadAllBytes(backup)) &&
                File.GetAttributes(backup) == backupAttributes, "R002_Utf8 backup preserved " + id);
        }
    }

    private static void R002_Cache()
    {
        string path = FileWith("cache.dict.json", "{\"entries\":[{\"number\":\"101\",\"name\":\"original\"}]}");
        ConfigLoader.Current = new PatConfig { DefaultDictPath = path };
        DictLoader.InvalidateCache();
        DictModel original = DictLoader.LoadForCurrentDrawing();
        Check(original != null && original.Entries.Count == 1 && original.Entries[0].Number == "101", "R002_Cache original");
        File.WriteAllText(path, "{\"entries\":[{\"number\":\"102\",\"name\":\"updated\"}]}", new UTF8Encoding(false));
        File.SetLastWriteTime(path, DateTime.Now.AddSeconds(10));
        DictModel before = DictLoader.LoadForCurrentDrawing();
        Check(before != null && before.Entries[0].Number == "102" && object.ReferenceEquals(original, DictLoader.PreviousModel), "R002_Cache valid diff baseline");
        File.WriteAllText(path, "{\"entries\":[{\"number\":\"301\"}", new UTF8Encoding(false));
        File.SetLastWriteTime(path, DateTime.Now.AddSeconds(20));
        DictModel after = DictLoader.LoadForCurrentDrawing();
        Check(object.ReferenceEquals(before, after) && after.Entries[0].Number == "102", "R002_Cache retains valid model");
        Check(object.ReferenceEquals(original, DictLoader.PreviousModel), "R002_Cache retains diff baseline");
        ConfigLoader.Current = null;
        DictLoader.InvalidateCache();
    }

    private static void R019_Parse()
    {
        string path = FileWith("escape.dict.json", "{\"entries\":[{\"number\":\"1\",\"name\":\"A\\\"B\\\\C\\/D\\bE\\fF\\nG\\rH\\tI\\u0000J\\u4e2dK\\ud83d\\ude00\"}]}");
        DictModel model = DictLoader.Load(path);
        string expected = "A\"B\\C/D\bE\fF\nG\rH\tI\0J中K😀";
        Check(model != null && model.Entries.Count == 1 && model.Entries[0].Name == expected, "R019_Parse all escapes");
        for (int i = 0; i < 32; i++)
        {
            string escaped = "{\"entries\":[{\"number\":\"1\",\"name\":\"X\\u" + i.ToString("X4") + "Y\"}]}";
            DictModel parsed = DictLoader.Load(FileWith("escape-" + i, escaped));
            Check(parsed != null && parsed.Entries.Count == 1 && parsed.Entries[0].Name == "X" + (char)i + "Y", "R019_Parse escaped U+" + i.ToString("X4"));
        }
    }

    private static void R019_Write()
    {
        for (int i = 0; i < 32; i++)
        {
            string input = "A" + (char)i + "B";
#if LEGACY
            string expected = i == 13 ? "A\nB" : input;
#else
            string expected = input;
#endif
            DictModel model = new DictModel();
            model.Metadata.ModifiedBy = "cad";
            model.Entries.Add(new DictEntry { Number = "1", Name = input });
            string json = DictWriter.Serialize(model);
            bool ok;
            try { using JsonDocument parsed = JsonDocument.Parse(json); ok = parsed.RootElement.GetProperty("entries")[0].GetProperty("name").GetString() == expected; }
            catch (JsonException) { ok = false; }
            Check(ok, "R019_Write control U+" + i.ToString("X4"));
            Check(json.Contains("\"modified_by\": \"cad\""), "R019_Write CAD marker U+" + i.ToString("X4"));
        }
        DictModel sample = new DictModel();
        sample.Entries.Add(new DictEntry { Number = "1", Name = "A\"\\/中文" });
        string text = DictWriter.Serialize(sample);
        using JsonDocument doc = JsonDocument.Parse(text);
        Check(doc.RootElement.GetProperty("entries")[0].GetProperty("name").GetString() == "A\"\\/中文", "R019_Write escaped ordinary text");
        Check(!text.Replace("\r\n", "").Contains('\n') && !text.Replace("\r\n", "").Contains('\r'), "R019_Write JSON line endings CRLF");
        string path = Path.Combine(_dir, "written.dict.json");
        string error;
        bool written = DictWriter.Write(path, sample, DictFileVisibility.ReadIdentity(path), out error);
        byte[] bytes = written ? File.ReadAllBytes(path) : new byte[0];
        Check(written && bytes.Length > 3 && !(bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf), "R019_Write UTF-8 without BOM");
        Check(written && Encoding.UTF8.GetString(bytes) == text, "R019_Write file matches serialization");
    }

    private static void R019_CRLF()
    {
        DictModel model = new DictModel();
        model.Entries.Add(new DictEntry { Number = "1", Name = "A\r\nB" });
        string json = DictWriter.Serialize(model);
        using JsonDocument parsed = JsonDocument.Parse(json);
#if LEGACY
        string expected = "A\nB";
#else
        string expected = "A\r\nB";
#endif
        Check(parsed.RootElement.GetProperty("entries")[0].GetProperty("name").GetString() == expected, "R019_CRLF string value");
        Check(!json.Replace("\r\n", "").Contains('\n') && !json.Replace("\r\n", "").Contains('\r'), "R019_CRLF file structure");
    }

    public static int Main(string[] args)
    {
        _dir = Path.Combine(Path.GetTempPath(), "PatentMarker-G04-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        var tests = new (string Name, Action Run)[] {
            ("R002_Syntax", R002_Syntax), ("R002_Root", R002_Root), ("R002_Compat", R002_Compat),
            ("R002_Restore", R002_Restore), ("R002_Utf8", R002_Utf8), ("R002_Cache", R002_Cache),
            ("R019_Parse", R019_Parse), ("R019_Write", R019_Write), ("R019_CRLF", R019_CRLF)
#if LEGACY
            , ("R002_Config", R002_Config)
#endif
        };
        try
        {
            var selected = args.Length == 0 ? tests : tests.Where(t => args.Contains(t.Name)).ToArray();
            if (selected.Length == 0) { Console.WriteLine("FAIL no matching selector"); return 2; }
            foreach (var test in selected) Run(test.Name, test.Run);
            Console.WriteLine("SUMMARY selectors=" + selected.Length + " failures=" + _failures);
            return _failures == 0 ? 0 : 1;
        }
        finally
        {
            foreach (string path in Directory.GetFiles(_dir)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
    }
}
