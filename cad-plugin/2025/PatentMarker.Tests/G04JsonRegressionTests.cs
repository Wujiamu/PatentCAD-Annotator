using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using PatentMarker.IO;
using Xunit;

namespace PatentMarker.Tests
{
    public sealed class G04JsonRegressionTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "PatentMarker-G04-" + Guid.NewGuid().ToString("N"));

        public G04JsonRegressionTests() => Directory.CreateDirectory(_directory);

        private string Write(string name, string content)
        {
            string path = Path.Combine(_directory, name);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        [Fact]
        public void R002_InvalidInputAndRootFailWhileOldFieldsRemainOptional()
        {
            string[] invalid = { "", "null", "[]", "\"wrong root\"", "{\"entries\":[{\"number\":\"301\"}",
                "{\"entries\":[]} trailing", "{\"entries\":[],}", "{ /* comment */ \"entries\":[]}", "{\"x\":\"\\q\"}" };
            for (int i = 0; i < invalid.Length; i++)
                Assert.Null(DictLoader.Load(Write("bad-" + i, invalid[i])));
            DictModel? empty = DictLoader.Load(Write("old", "{\"unknown\":1,\"entries\":[]}"));
            Assert.NotNull(empty);
            Assert.Empty(empty.Entries);
            Assert.NotNull(empty.Metadata);
            Assert.NotNull(empty.Warnings);
        }

        [Fact]
        public void R002_CorruptBackupDoesNotChangeCurrentOrBackupBytesAndAttributes()
        {
            string current = Write("sample.dict.json", "{\"entries\":[{\"number\":\"101\",\"name\":\"original\"}]}");
            string backup = Write("sample.dict.json.word-20261031-010101.bak", "{\"entries\":[{\"number\":\"301\"}");
            File.SetAttributes(backup, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
            byte[] beforeCurrent = File.ReadAllBytes(current), beforeBackup = File.ReadAllBytes(backup);
            FileAttributes currentAttributes = File.GetAttributes(current), backupAttributes = File.GetAttributes(backup);
            Assert.Null(DictConflict.ResolveRestoreCad(current, DictFileVisibility.ReadIdentity(current), backup,
                DictFileVisibility.ReadIdentity(backup), () => true, out string error));
            Assert.Equal("Backup dictionary cannot be parsed.", error);
            Assert.Equal(beforeCurrent, File.ReadAllBytes(current));
            Assert.Equal(beforeBackup, File.ReadAllBytes(backup));
            Assert.Equal(currentAttributes, File.GetAttributes(current));
            Assert.Equal(backupAttributes, File.GetAttributes(backup));
        }

        [Fact]
        public void R019_ControlCharactersAndCrLfRemainValidAndPreserved()
        {
            string value = string.Concat(Enumerable.Range(0, 32).Select(i => ((char)i).ToString())) + "\r\n\\\"中文";
            var model = new DictModel();
            model.Metadata.ModifiedBy = "cad";
            model.Entries.Add(new DictEntry { Number = "1", Name = value });
            string json = DictWriter.Serialize(model);
            using JsonDocument doc = JsonDocument.Parse(json);
            Assert.Equal(value, doc.RootElement.GetProperty("entries")[0].GetProperty("name").GetString());
            Assert.Contains("\"modified_by\": \"cad\"", json);
            Assert.DoesNotContain("\n", json.Replace("\r\n", ""));
        }

        public void Dispose()
        {
            foreach (string path in Directory.GetFiles(_directory)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(_directory, true);
        }
    }
}
