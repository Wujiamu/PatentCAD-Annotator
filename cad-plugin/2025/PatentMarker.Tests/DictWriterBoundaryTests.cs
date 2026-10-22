using System.Security.AccessControl;
using System.Security.Principal;
using PatentMarker.IO;
using Xunit;

namespace PatentMarker.Tests;

public sealed class DictWriterBoundaryTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "PatentWriterBoundary_" + Guid.NewGuid().ToString("N"));
    public DictWriterBoundaryTests() => Directory.CreateDirectory(directory);
    private string Seed()
    {
        string path = Path.Combine(directory, "test.dict.json");
        File.WriteAllText(path, "ORIGINAL");
        return path;
    }
    private static DictModel Model()
    {
        var model = new DictModel();
        model.Entries.Add(new DictEntry { Number = "901", Name = "new" });
        return model;
    }
    public void Dispose()
    {
        foreach (string path in Directory.GetFiles(directory)) File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(directory, true);
    }

    [Fact]
    public void ReadOnlyTarget_IsRejectedWithoutChangingBytesOrAttributes()
    {
        string path = Seed();
        var attributes = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive;
        File.SetAttributes(path, attributes);
        Assert.False(DictWriter.Write(path, Model(), out string error));
        Assert.NotEmpty(error);
        Assert.Equal("ORIGINAL", File.ReadAllText(path));
        Assert.Equal(attributes, File.GetAttributes(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void AtomicRewrite_PreservesAllSettableAttributes()
    {
        string path = Seed();
        var attributes = FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.NotContentIndexed;
        File.SetAttributes(path, attributes);
        Assert.True(DictWriter.Write(path, Model(), out string error), error);
        Assert.Equal(attributes, File.GetAttributes(path));
        Assert.Contains("901", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void LockedTarget_RetainsBytesAndAttributes_ThenRecovers()
    {
        string path = Seed();
        var attributes = FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.NotContentIndexed;
        File.SetAttributes(path, attributes);
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.False(DictWriter.Write(path, Model(), out _));
        Assert.Equal("ORIGINAL", File.ReadAllText(path));
        Assert.Equal(attributes, File.GetAttributes(path));
        Assert.Single(Directory.GetFiles(directory));
        Assert.True(DictWriter.Write(path, Model(), out string error), error);
        Assert.Equal(attributes, File.GetAttributes(path));
    }

    [Fact]
    public void ExistingForeignTemp_IsNeverOverwrittenOrDeleted()
    {
        string path = Seed();
        File.WriteAllText(path + ".tmp", "FOREIGN");
        Assert.True(DictWriter.Write(path, Model(), out string error), error);
        Assert.Equal("FOREIGN", File.ReadAllText(path + ".tmp"));
        Assert.Equal(2, Directory.GetFiles(directory).Length);
    }

    [Fact]
    public void Replace_PreservesNamedStreamAclAndCreationTime()
    {
        string path = Seed();
        File.WriteAllText(path + ":sentinel", "STREAM");
        File.SetCreationTimeUtc(path, new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var creation = File.GetCreationTimeUtc(path);
        string acl = new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
        Assert.True(DictWriter.Write(path, Model(), out string error), error);
        Assert.Equal("STREAM", File.ReadAllText(path + ":sentinel"));
        Assert.Equal(acl, new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All));
        Assert.Equal(creation, File.GetCreationTimeUtc(path));
    }

    [Fact]
    public void DeniedReplace_DoesNotUseMetadataLosingOverwriteMove()
    {
        string path = Seed();
        File.WriteAllText(path + ":sentinel", "STREAM");
        var original = new FileInfo(path).GetAccessControl();
        string sddl = original.GetSecurityDescriptorSddlForm(AccessControlSections.All);
        var denied = new FileInfo(path).GetAccessControl();
        denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny));
        new FileInfo(path).SetAccessControl(denied);
        try
        {
            Assert.False(DictWriter.Write(path, Model(), out _));
        }
        finally
        {
            var restore = new FileSecurity();
            restore.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group);
            new FileInfo(path).SetAccessControl(restore);
        }
        Assert.Equal("ORIGINAL", File.ReadAllText(path));
        Assert.Equal("STREAM", File.ReadAllText(path + ":sentinel"));
        Assert.Single(Directory.GetFiles(directory));
    }
}
