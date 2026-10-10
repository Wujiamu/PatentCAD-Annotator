using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace PatentOffice.Shared
{
    internal sealed class DictionarySnapshot
    {
        public string Path { get; set; }
        public string Sha256 { get; set; }
        public PatentDictionary Dictionary { get; set; }
    }

    internal static class DictionaryReader
    {
        public static string ComputeHash(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(bytes));
        }

        public static DictionarySnapshot Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string hash;
            using (SHA256 sha = SHA256.Create())
                hash = ToHex(sha.ComputeHash(bytes));

            string json;
            using (MemoryStream memory = new MemoryStream(bytes, false))
            using (StreamReader reader = new StreamReader(memory, new UTF8Encoding(false, true), true))
                json = reader.ReadToEnd();

            PatentDictionary dictionary = JsonConvert.DeserializeObject<PatentDictionary>(json);
            if (dictionary == null) throw new InvalidDataException("JSON 没有字典对象。");
            if (dictionary.Entries == null) dictionary.Entries = new System.Collections.Generic.List<PatentEntry>();
            for (int i = 0; i < dictionary.Entries.Count; i++)
            {
                PatentEntry entry = dictionary.Entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.Number))
                    throw new InvalidDataException("字典条目缺少有效编号（行 " + (i + 1) + "）。");
                if (entry.Name == null) entry.Name = "";
            }

            return new DictionarySnapshot { Path = System.IO.Path.GetFullPath(path), Sha256 = hash, Dictionary = dictionary };
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }
    }
}
