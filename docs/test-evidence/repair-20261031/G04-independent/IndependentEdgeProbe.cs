using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PatentMarker.IO;

internal static class Program
{
    private static int failures;
    private static string directory;

    private static void Check(bool ok, string name)
    {
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
        if (!ok) failures++;
    }

    private static string SaveText(string name, string content)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static string Digest(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void AdditionalGrammar()
    {
        string[] invalid = {
            "{\"entries\":[],\"x\":-01}",
            "{\"entries\":[],\"x\":1e+}",
            "{\"entries\":[],\"x\":1.}",
            "{\"entries\":[],\"x\":.1}",
            "{\"entries\":[],\"x\": [0,,1]}",
            "{\"entries\":[],\"x\":false false}",
            "{\"entries\":[],\"x\":\"\\u00 1\"}",
            "{\"entries\":[],\"x\":\"\\u+001\"}",
            "{\"entries\":[],\"x\":\"hello\u0000world\"}",
            "{\"entries\":[],\"x\":\u000b 1}",
            "{\"entries\":[]}\ufeff",
            "{\"entries\":[]}\r\n{}"
        };
        for (int i = 0; i < invalid.Length; i++)
            Check(DictLoader.Load(SaveText("grammar-" + i + ".json", invalid[i])) == null,
                "grammar rejection[" + i + "]");

        string good = "{\t\"unknown\": {\"nested\": [true, false, null, -0.5, 1E+2, \"中文\"]},\r\n\"entries\":[]}";
        DictModel model = DictLoader.Load(SaveText("valid.json", good));
        Check(model != null && model.Entries != null && model.Entries.Count == 0, "legal nested unknown field");
    }

    private static void InvalidUtf8AndBackupRecovery()
    {
        byte[] prefix = Encoding.UTF8.GetBytes("{\"entries\":[{\"number\":\"301\",\"name\":\"broken");
        byte[] suffix = Encoding.UTF8.GetBytes("\"}]}");
        byte[] broken = prefix.Concat(new byte[] { 0xff }).Concat(suffix).ToArray();
        string invalidPath = Path.Combine(directory, "invalid-utf8.dict.json");
        File.WriteAllBytes(invalidPath, broken);
        DictModel parsed = DictLoader.Load(invalidPath);
        Console.WriteLine("invalid-utf8 parse=" + (parsed == null ? "null" : "accepted") +
            " replacement-character=" + (parsed != null && parsed.Entries.Count != 0 &&
                parsed.Entries[0].Name.IndexOf('\ufffd') >= 0));
        Check(parsed == null, "invalid UTF-8 dictionary rejected");

        string current = SaveText("current.dict.json", "{\"entries\":[{\"number\":\"101\",\"name\":\"original\"}]}");
        string backup = Path.Combine(directory, "current.dict.json.word-20261101-010101.bak");
        File.WriteAllBytes(backup, broken);
        File.SetAttributes(current, FileAttributes.Archive);
        File.SetAttributes(backup, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
        byte[] beforeCurrent = File.ReadAllBytes(current);
        byte[] beforeBackup = File.ReadAllBytes(backup);
        FileAttributes currentAttributes = File.GetAttributes(current);
        FileAttributes backupAttributes = File.GetAttributes(backup);
        File.WriteAllBytes(Path.Combine(directory, "before-current.bin"), beforeCurrent);
        File.WriteAllBytes(Path.Combine(directory, "before-backup.bin"), beforeBackup);

        string error;
        DictModel restored = DictConflict.ResolveRestoreCad(current, backup, out error);
        bool currentExists = File.Exists(current), backupExists = File.Exists(backup);
        bool sameCurrent = currentExists && beforeCurrent.SequenceEqual(File.ReadAllBytes(current));
        bool sameBackup = backupExists && beforeBackup.SequenceEqual(File.ReadAllBytes(backup));
        Console.WriteLine("restore result=" + (restored == null ? "null" : "accepted") +
            " error=" + (string.IsNullOrEmpty(error) ? "empty" : "nonempty") +
            " current-preserved=" + sameCurrent + " backup-exists=" + backupExists);
        Console.WriteLine("before current sha256=" + Digest(beforeCurrent) + " backup sha256=" + Digest(beforeBackup));
        if (currentExists) Console.WriteLine("after current sha256=" + Digest(File.ReadAllBytes(current)));
        if (backupExists) Console.WriteLine("after backup sha256=" + Digest(File.ReadAllBytes(backup)));
        Check(restored == null && !string.IsNullOrEmpty(error), "invalid UTF-8 backup rejected with error");
        Check(sameCurrent && File.GetAttributes(current) == currentAttributes, "current bytes and attributes preserved");
        Check(sameBackup && File.GetAttributes(backup) == backupAttributes, "backup bytes and attributes preserved");
    }

    public static int Main(string[] args)
    {
        if (args.Length != 1 || Directory.Exists(args[0])) return 2;
        directory = args[0];
        Directory.CreateDirectory(directory);
        AdditionalGrammar();
        InvalidUtf8AndBackupRecovery();
        Console.WriteLine("SUMMARY failures=" + failures);
        return failures == 0 ? 0 : 1;
    }
}
