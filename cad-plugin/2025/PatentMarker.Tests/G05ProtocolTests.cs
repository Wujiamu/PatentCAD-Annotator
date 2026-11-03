using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using PatentMarker.IO;
using Xunit;

namespace PatentMarker.Tests
{
    public sealed class G05ProtocolTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "PatentMarker-G05-" + Guid.NewGuid().ToString("N"));
        public G05ProtocolTests() => Directory.CreateDirectory(_dir);
        private string PathFor(string name) => Path.Combine(_dir, name + ".dict.json");
        private static void WriteText(string path, string content) => File.WriteAllText(path, content, new UTF8Encoding(false));
        private static string Identity(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        private static bool WriteExpected(string path, DictModel model, string expected, out string error)
        {
            MethodInfo? guarded = typeof(DictWriter).GetMethod("Write", new[] { typeof(string), typeof(DictModel), typeof(string), typeof(string).MakeByRefType() });
            if (guarded != null)
            {
                object?[] args = { path, model, expected, null };
                bool success = (bool)guarded.Invoke(null, args)!;
                error = (string?)args[3] ?? "";
                return success;
            }
            MethodInfo legacy = typeof(DictWriter).GetMethod("Write", new[] { typeof(string), typeof(DictModel), typeof(string).MakeByRefType() })!;
            object?[] oldArgs = { path, model, null };
            bool oldSuccess = (bool)legacy.Invoke(null, oldArgs)!;
            error = (string?)oldArgs[2] ?? "";
            return oldSuccess;
        }
        private static bool AlwaysCurrent() => true;
        private static MethodInfo? GuardedConflict(string name)
        {
            foreach (MethodInfo method in typeof(DictConflict).GetMethods())
                if (method.Name == name && method.GetParameters().Length == 6) return method;
            return null;
        }
        private static object?[] ConflictArgs(MethodInfo method, string path, string identity, string backup, string backupIdentity)
        {
            Delegate guard = Delegate.CreateDelegate(method.GetParameters()[4].ParameterType,
                typeof(G05ProtocolTests).GetMethod(nameof(AlwaysCurrent), BindingFlags.Static | BindingFlags.NonPublic)!);
            return new object?[] { path, identity, backup, backupIdentity, guard, null };
        }
        private static bool KeepExpected(string path, string identity, string backup, string backupIdentity)
        {
            MethodInfo? method = GuardedConflict("ResolveKeepWord");
            if (method != null) return (bool)method.Invoke(null, ConflictArgs(method, path, identity, backup, backupIdentity))!;
            return DictConflict.ResolveKeepWord(backup, out _);
        }
        private static DictModel? RestoreExpected(string path, string identity, string backup, string backupIdentity)
        {
            MethodInfo? method = GuardedConflict("ResolveRestoreCad");
            if (method != null) return (DictModel?)method.Invoke(null, ConflictArgs(method, path, identity, backup, backupIdentity));
            return DictConflict.ResolveRestoreCad(path, backup, out _);
        }
        private const string A = "{\"entries\":[{\"number\":\"301\",\"name\":\"old\"}]}";
        private const string B = "{\"entries\":[{\"number\":\"602\",\"name\":\"new\"}]}";

        [Fact]
        public void R009_ExternalWriteAfterReadMustNotBeOverwritten()
        {
            string path = PathFor("stale"); WriteText(path, A);
            DictModel model = Assert.IsType<DictModel>(DictLoader.Load(path));
            string identity = Identity(path);
            Assert.True(DictWriter.TryApplyEdit(model, model.Entries[0], "301", "user edit", out _));
            WriteText(path, B); byte[] expected = File.ReadAllBytes(path);
            Assert.False(WriteExpected(path, model, identity, out string error));
            Assert.False(string.IsNullOrEmpty(error));
            Assert.Equal(expected, File.ReadAllBytes(path));
        }

        [Fact]
        public void R009_HeldProductLockPreventsCadWrite()
        {
            string path = PathFor("busy"); WriteText(path, A);
            DictModel model = Assert.IsType<DictModel>(DictLoader.Load(path));
            string identity = Identity(path); byte[] before = File.ReadAllBytes(path);
            Assert.True(DictWriter.TryApplyEdit(model, model.Entries[0], "301", "new name", out _));
            using (var held = new FileStream(path + ".patlock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.False(WriteExpected(path, model, identity, out string error));
                Assert.False(string.IsNullOrEmpty(error));
                Assert.Equal(before, File.ReadAllBytes(path));
            }
        }

        [Fact]
        public void R010_SameLengthAndTimestampStillDetectsChangedBytes()
        {
            string path = PathFor("same-time"); WriteText(path, A);
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            ConfigLoader.Current = new PatConfig { DefaultDictPath = path };
            DictLoader.InvalidateCache();
            try
            {
                DictModel first = Assert.IsType<DictModel>(DictLoader.LoadForCurrentDrawing());
                Assert.Equal("301", first.Entries[0].Number);
                Assert.Equal(new UTF8Encoding(false).GetByteCount(A), new UTF8Encoding(false).GetByteCount(B));
                WriteText(path, B); File.SetLastWriteTimeUtc(path, stamp);
                Assert.True(DictLoader.IsFileChanged());
                DictModel second = Assert.IsType<DictModel>(DictLoader.LoadForCurrentDrawing());
                Assert.Equal("602", second.Entries[0].Number);
                Assert.Same(first, DictLoader.PreviousModel);
            }
            finally { DictLoader.InvalidateCache(); ConfigLoader.Current = null; }
        }

        [Fact]
        public void R010_InvalidUtf8RetainsCachedModelAndBlocksStaleWrite()
        {
            string path = PathFor("bad-utf8"); WriteText(path, A);
            ConfigLoader.Current = new PatConfig { DefaultDictPath = path };
            DictLoader.InvalidateCache();
            try
            {
                DictModel first = Assert.IsType<DictModel>(DictLoader.LoadForCurrentDrawing());
                string identity = Identity(path);
                byte[] before = Encoding.UTF8.GetBytes("{\"entries\":[{\"number\":\"301\",\"name\":\"");
                byte[] after = Encoding.UTF8.GetBytes("\"}]}");
                using (var stream = File.Create(path))
                {
                    stream.Write(before); stream.WriteByte(0xC3); stream.WriteByte(0x28); stream.Write(after);
                }
                byte[] corrupt = File.ReadAllBytes(path);
                Assert.Null(DictLoader.Load(path));
                Assert.Same(first, DictLoader.LoadForCurrentDrawing());
                Assert.False(WriteExpected(path, first, identity, out _));
                Assert.Equal(corrupt, File.ReadAllBytes(path));
            }
            finally { DictLoader.InvalidateCache(); ConfigLoader.Current = null; }
        }

        [Fact]
        public void BaselineDigest_MatchesWordNormalizationAndUtf8()
        {
            string[] input = { "", "abc", "\u4e2d\u6587\U0001f600", "  {\"extracted_at\": \"2026-11-01T01:23:45\",\r\n\"x\":1}\r ",
                "\tA\rB\nC\r\nD  ", " {\"extracted_at\": \"\", \"v\":1} " };
            string[] expected = { "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                "e973a1c1b41c5c9f4fbac31fcc311536dfffd003bfb914580455170a599953fa", "249ebcad47d760b52ce4776dc3bb173f239215bca242f243148ff3f6255f183a",
                "433c31786f7176afd5fd19eb3e5cb2536657627b0999b2b52abe93a17399ff00", "a9a00cd774aa6ae76aa74f607dd0657a01ff6002bc92349170bfa92ca3366aa5" };
            for (int i = 0; i < input.Length; ++i) Assert.Equal(expected[i], DictFileVisibility.BaselineDigest(input[i]));
        }

        [Fact]
        public void R013_PendingBackupIsNotClearedByCadMarker()
        {
            string path = PathFor("pending"); WriteText(path, A);
            string backup = path + ".word-20261101-010101.bak"; WriteText(backup, A);
            DictModel model = Assert.IsType<DictModel>(DictLoader.Load(path));
            model.Metadata.ModifiedBy = "cad";
            Assert.True(DictConflict.IsPendingConflict(model, path));
        }

        [Fact]
        public void R013_PendingBackupPreventsDictionaryWrite()
        {
            string path = PathFor("blocked"); WriteText(path, A);
            string backup = path + ".word-20261101-010101.bak"; WriteText(backup, B);
            DictModel model = Assert.IsType<DictModel>(DictLoader.Load(path));
            string identity = Identity(path); byte[] original = File.ReadAllBytes(path), oldBackup = File.ReadAllBytes(backup);
            Assert.True(DictWriter.TryApplyEdit(model, model.Entries[0], "301", "new name", out _));
            Assert.False(WriteExpected(path, model, identity, out string error));
            Assert.False(string.IsNullOrEmpty(error));
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(oldBackup, File.ReadAllBytes(backup));
        }

        [Fact]
        public void R013_ChangedSelectedBackupCannotBeDeleted()
        {
            string path = PathFor("keep-changed"); WriteText(path, A);
            string backup = path + ".word-20261101-010101.bak"; WriteText(backup, A);
            string currentIdentity = Identity(path), backupIdentity = Identity(backup);
            WriteText(backup, B); byte[] changed = File.ReadAllBytes(backup);
            Assert.False(KeepExpected(path, currentIdentity, backup, backupIdentity));
            Assert.Equal(changed, File.ReadAllBytes(backup));
        }

        [Fact]
        public void R013_RestoreSelectedBackupWritesDigestAndKeepsOtherBackup()
        {
            string path = PathFor("restore"); WriteText(path, A);
            string backup = path + ".word-20261101-010102.bak"; WriteText(backup, B);
            string other = path + ".word-20261101-010101.bak"; WriteText(other, A);
            byte[] otherBytes = File.ReadAllBytes(other);
            DictModel restored = Assert.IsType<DictModel>(RestoreExpected(path, Identity(path), backup, Identity(backup)));
            Assert.Equal("602", restored.Entries[0].Number);
            Assert.Equal("602", Assert.IsType<DictModel>(DictLoader.Load(path)).Entries[0].Number);
            Assert.False(File.Exists(backup)); Assert.Equal(otherBytes, File.ReadAllBytes(other));
            string current = File.ReadAllText(path, Encoding.UTF8);
            Assert.Equal(DictFileVisibility.BaselineHeader + DictFileVisibility.BaselineDigest(current), File.ReadAllText(path + ".word-baseline", Encoding.UTF8));
        }

        [Fact]
        public void R009_FirstCreateWithObservedAbsenceDefaultsToHidden()
        {
            string path = PathFor("first-create");
            Assert.False(File.Exists(path));
            string expected = DictFileVisibility.ReadIdentity(path);
            Assert.Equal(DictFileVisibility.MissingIdentity, expected);
            var model = new DictModel { Entries = new System.Collections.Generic.List<DictEntry> { new DictEntry { Number = "99", Name = "created" } } };
            Assert.True(DictWriter.Write(path, model, expected, out string error), error);
            Assert.Equal("99", Assert.IsType<DictModel>(DictLoader.Load(path)).Entries[0].Number);
            Assert.Equal(FileAttributes.Hidden | FileAttributes.System, File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System));
        }

        [Fact]
        public void R009_FirstCreateRejectsAnotherWritersNewFile()
        {
            string path = PathFor("first-create-race");
            Assert.False(File.Exists(path));
            string expected = DictFileVisibility.ReadIdentity(path);
            WriteText(path, B); byte[] other = File.ReadAllBytes(path);
            var model = new DictModel { Entries = new System.Collections.Generic.List<DictEntry> { new DictEntry { Number = "99", Name = "created" } } };
            Assert.False(DictWriter.Write(path, model, expected, out string error));
            Assert.Equal(DictFileVisibility.ChangedError, error);
            Assert.Equal(other, File.ReadAllBytes(path));
        }

        [Fact]
        public void R013_ActualGuardedRestoreRejectsCorruptBackup()
        {
            string path = PathFor("bad-backup"); WriteText(path, A);
            string backup = path + ".word-20261101-010102.bak"; WriteText(backup, "{broken");
            byte[] currentBytes = File.ReadAllBytes(path), backupBytes = File.ReadAllBytes(backup);
            DictModel? restored = DictConflict.ResolveRestoreCad(path, Identity(path), backup, Identity(backup), AlwaysCurrent, out string error);
            Assert.Null(restored); Assert.Equal("Backup dictionary cannot be parsed.", error);
            Assert.Equal(currentBytes, File.ReadAllBytes(path)); Assert.Equal(backupBytes, File.ReadAllBytes(backup));
            Assert.False(File.Exists(path + ".word-baseline"));
        }

        [Fact]
        public void R013_UnknownBaselineIsNotOverwrittenAndBackupSurvives()
        {
            string path = PathFor("unknown-baseline"); WriteText(path, A);
            string backup = path + ".word-20261101-010102.bak"; WriteText(backup, B);
            string baseline = path + ".word-baseline"; WriteText(baseline, "unrelated sentinel");
            byte[] sentinel = File.ReadAllBytes(baseline), backupBytes = File.ReadAllBytes(backup);
            DictModel? restored = DictConflict.ResolveRestoreCad(path, Identity(path), backup, Identity(backup), AlwaysCurrent, out string error);
            Assert.Null(restored); Assert.Equal(DictFileVisibility.BaselineError, error);
            Assert.Equal("602", Assert.IsType<DictModel>(DictLoader.Load(path)).Entries[0].Number);
            Assert.Equal(sentinel, File.ReadAllBytes(baseline)); Assert.Equal(backupBytes, File.ReadAllBytes(backup));
        }

        [Fact]
        public void R013_ChangedDocumentContextPreventsKeep()
        {
            string path = PathFor("context"); WriteText(path, A);
            string backup = path + ".word-20261101-010102.bak"; WriteText(backup, B);
            byte[] currentBytes = File.ReadAllBytes(path), backupBytes = File.ReadAllBytes(backup);
            Assert.False(DictConflict.ResolveKeepWord(path, Identity(path), backup, Identity(backup), delegate { return false; }, out string error));
            Assert.Equal(DictFileVisibility.ChangedError, error);
            Assert.Equal(currentBytes, File.ReadAllBytes(path)); Assert.Equal(backupBytes, File.ReadAllBytes(backup));
            Assert.False(File.Exists(path + ".word-baseline"));
        }

        [Fact]
        public void R013_ActualGuardedRestoreRejectsInvalidUtf8Backup()
        {
            string path = PathFor("bad-utf8-backup"); WriteText(path, A);
            string backup = path + ".word-20261101-010102.bak";
            byte[] prefix = Encoding.UTF8.GetBytes("{\\\"entries\\\":[{\\\"number\\\":\\\"9\\\",\\\"name\\\":\\\"");
            byte[] suffix = Encoding.UTF8.GetBytes("\\\"}]}");
            using (var file = File.Create(backup))
            {
                file.Write(prefix); file.WriteByte(0xC3); file.WriteByte(0x28); file.Write(suffix);
            }
            byte[] before = File.ReadAllBytes(path), damaged = File.ReadAllBytes(backup);
            DictModel? restored = DictConflict.ResolveRestoreCad(path, Identity(path), backup, Identity(backup), AlwaysCurrent, out string error);
            Assert.Null(restored); Assert.Equal("Backup dictionary cannot be parsed.", error);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.Equal(damaged, File.ReadAllBytes(backup));
        }

        [Fact]
        public void R013_KeepWordWritesDigestAndKeepsOtherBackup()
        {
            string path = PathFor("keep-valid"); WriteText(path, A);
            string backup = path + ".word-20261101-010102.bak"; WriteText(backup, B);
            string other = path + ".word-20261101-010101.bak"; WriteText(other, A);
            byte[] before = File.ReadAllBytes(path), otherBytes = File.ReadAllBytes(other);
            Assert.True(DictConflict.ResolveKeepWord(path, Identity(path), backup, Identity(backup), AlwaysCurrent, out string error), error);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.False(File.Exists(backup)); Assert.Equal(otherBytes, File.ReadAllBytes(other));
            Assert.Equal(DictFileVisibility.BaselineHeader + DictFileVisibility.BaselineDigest(A), File.ReadAllText(path + ".word-baseline", Encoding.UTF8));
        }

        [Fact]
        public void R009_HeldProductLockPreventsCadVisibilityChange()
        {
            string path = PathFor("busy-visibility"); WriteText(path, A);
            File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System);
            FileAttributes before = File.GetAttributes(path);
            using (var held = new FileStream(path + ".patlock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.False(DictFileVisibility.TrySetVisible(path, true, out string error));
                Assert.Equal(DictFileVisibility.BusyError, error);
                Assert.Equal(before, File.GetAttributes(path));
            }
            Assert.True(DictFileVisibility.TrySetVisible(path, true, out _));
        }

        [Fact]
        public void R013_DeletedSelectedBackupReportsChanged()
        {
            string path = PathFor("deleted-backup"); WriteText(path, A);
            string backup = path + ".word-20261101-010102.bak"; WriteText(backup, B);
            string currentIdentity = Identity(path), backupIdentity = Identity(backup);
            byte[] before = File.ReadAllBytes(path); File.Delete(backup);
            Assert.False(DictConflict.ResolveKeepWord(path, currentIdentity, backup, backupIdentity, AlwaysCurrent, out string error));
            Assert.Equal(DictFileVisibility.ChangedError, error);
            Assert.Equal(before, File.ReadAllBytes(path)); Assert.False(File.Exists(path + ".word-baseline"));
        }

        public void Dispose()
        {
            foreach (string path in Directory.GetFiles(_dir)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
    }
}
