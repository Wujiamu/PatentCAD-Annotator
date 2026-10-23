using PatentMarker.IO;
using Xunit;

namespace PatentMarker.Tests;

public sealed class DictConflictBoundaryTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "pm-backup-boundary-" + Guid.NewGuid().ToString("N"));
    public DictConflictBoundaryTests() { Directory.CreateDirectory(dir); }
    private string Current => Path.Combine(dir, "test.dict.json");
    private string Backup => Current + ".word-20261022-100000.bak";
    private const string WordJson = "{\"entries\":[{\"number\":\"901\",\"name\":\"word\"}],\"warnings\":[]}";
    private const string CadJson = "{\"metadata\":{\"modified_by\":\"cad\",\"modified_at\":\"old\"},\"entries\":[{\"number\":\"901\",\"name\":\"cad\"}],\"warnings\":[]}";
    private void Seed() { File.WriteAllText(Current, WordJson); File.WriteAllText(Backup, CadJson); }

    [Fact]
    public void RestoreMissingOrCorruptBackup_PreservesCurrent()
    {
        File.WriteAllText(Current, WordJson);
        Assert.Null(DictConflict.ResolveRestoreCad(Current, Backup, out _));
        File.WriteAllText(Backup, "INVALID JSON");
        Assert.Null(DictConflict.ResolveRestoreCad(Current, Backup, out _));
        Assert.Equal(WordJson, File.ReadAllText(Current));
        Assert.Equal("INVALID JSON", File.ReadAllText(Backup));
        Assert.True(DictConflict.ResolveKeepWord(Backup + ".missing", out _));
    }

    [Fact]
    public void RestoreReadOnlyAndLockedCurrent_PreservesBackupThenRecovers()
    {
        Seed();
        File.SetAttributes(Current, FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive);
        var attributes = File.GetAttributes(Current);
        Assert.Null(DictConflict.ResolveRestoreCad(Current, Backup, out _));
        Assert.Equal(attributes, File.GetAttributes(Current));
        Assert.Equal(WordJson, File.ReadAllText(Current));
        Assert.Equal(CadJson, File.ReadAllText(Backup));
        File.SetAttributes(Current, attributes & ~FileAttributes.ReadOnly);
        using (var held = new FileStream(Current, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Null(DictConflict.ResolveRestoreCad(Current, Backup, out _));
        Assert.Equal(CadJson, File.ReadAllText(Backup));
        Assert.NotNull(DictConflict.ResolveRestoreCad(Current, Backup, out _));
        Assert.False(File.Exists(Backup));
        string restored = File.ReadAllText(Current);
        Assert.Contains("cad", restored);
        Assert.DoesNotContain("modified_by", restored);
        Assert.DoesNotContain("modified_at", restored);
    }

    [Fact]
    public void LockedBackupCleanup_PreservesFullAttributesAndAllowsRetry()
    {
        foreach (bool restore in new[] { false, true })
        {
            Seed();
            File.SetAttributes(Backup, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.NotContentIndexed);
            var attributes = File.GetAttributes(Backup);
            using (var held = new FileStream(Backup, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (restore) Assert.Null(DictConflict.ResolveRestoreCad(Current, Backup, out _));
                else Assert.False(DictConflict.ResolveKeepWord(Backup, out _));
                Assert.Equal(attributes, File.GetAttributes(Backup));
            }
            Assert.Equal(CadJson, File.ReadAllText(Backup));
            // Restore may already have committed the target before cleanup
            // failed. The retained backup is the recovery source, not rollback.
            if (restore) Assert.NotNull(DictConflict.ResolveRestoreCad(Current, Backup, out _));
            else Assert.True(DictConflict.ResolveKeepWord(Backup, out _));
            Assert.False(File.Exists(Backup));
        }
    }

    public void Dispose()
    {
        foreach (string path in Directory.GetFiles(dir)) File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(dir, true);
    }
}
