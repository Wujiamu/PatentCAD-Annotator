using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.ComponentModel;
using PatentMarker.IO;
using Xunit;

namespace PatentMarker.Tests;

public sealed class DictWriterBoundaryTests : IDisposable
{
    [DllImport("advapi32.dll", EntryPoint = "SetFileSecurityW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileSecurity(string path, uint information, byte[] descriptor);

    private void UseLegacyInheritance()
    {
        byte[] descriptor = new DirectoryInfo(directory).GetAccessControl().GetSecurityDescriptorBinaryForm();
        Assert.True(SetFileSecurity(directory, 4, descriptor), new Win32Exception(Marshal.GetLastWin32Error()).Message);
    }

    [Fact]
    public void LegacyInheritedAcl_WithStreamsAndCreationTime_IsPreservedExactly()
    {
        UseLegacyInheritance();
        string path = Seed();
        File.WriteAllText(path + ":sentinel", "STREAM");
        File.SetCreationTimeUtc(path, new DateTime(2020,1,2,3,4,5,DateTimeKind.Utc));
        var before = new FileInfo(path).GetAccessControl();
        var raw = new RawSecurityDescriptor(before.GetSecurityDescriptorBinaryForm(), 0);
        Assert.Equal(0, (int)(raw.ControlFlags & ControlFlags.DiscretionaryAclAutoInherited));
        string sddl = before.GetSecurityDescriptorSddlForm(AccessControlSections.All);
        var created = File.GetCreationTimeUtc(path);
        Assert.True(DictWriter.Write(path, Model(), out string error), error);
        string after = new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
        Assert.True(sddl == after, "ACL before=" + sddl + " after=" + after);
        Assert.Equal("STREAM", File.ReadAllText(path + ":sentinel"));
        Assert.Equal(created, File.GetCreationTimeUtc(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void LegacyAclRepairDenied_RollsBackOriginalBytesSecurityAndStreams()
    {
        UseLegacyInheritance();
        string path = Seed();
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.System | FileAttributes.Archive | FileAttributes.NotContentIndexed);
        var attributes = File.GetAttributes(path);
        File.WriteAllText(path + ":sentinel", "STREAM");
        var original = new FileInfo(path).GetAccessControl();
        var raw = new RawSecurityDescriptor(original.GetSecurityDescriptorBinaryForm(), 0);
        var ownerRights = new SecurityIdentifier("S-1-3-4");
        raw.DiscretionaryAcl!.InsertAce(0, new CommonAce(AceFlags.None, AceQualifier.AccessDenied,
            (int)FileSystemRights.ChangePermissions, ownerRights, false, null));
        byte[] descriptor = new byte[raw.BinaryLength]; raw.GetBinaryForm(descriptor, 0);
        Assert.True(SetFileSecurity(path, 4, descriptor), new Win32Exception(Marshal.GetLastWin32Error()).Message);
        string before = new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
        Assert.False(DictWriter.Write(path, Model(), out string error));
        Assert.NotEmpty(error);
        Assert.Contains("原文件已恢复", error);
        Assert.Equal("ORIGINAL", File.ReadAllText(path));
        Assert.Equal("STREAM", File.ReadAllText(path + ":sentinel"));
        Assert.Equal(before, new FileInfo(path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All));
        Assert.Equal(attributes, File.GetAttributes(path));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Fact]
    public void LegacyInheritedPermission_RemainsInheritedAfterParentRevocation()
    {
        var sentinel = new SecurityIdentifier("S-1-5-21-1-2-3-424242");
        var parent = new DirectoryInfo(directory).GetAccessControl();
        var grant = new FileSystemAccessRule(sentinel, FileSystemRights.ReadData,
            InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
            PropagationFlags.None, AccessControlType.Allow);
        parent.AddAccessRule(grant); new DirectoryInfo(directory).SetAccessControl(parent);
        UseLegacyInheritance();
        string path = Seed();
        var before = new FileInfo(path).GetAccessControl();
        Assert.Contains(before.GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
            rule => rule.IdentityReference.Equals(sentinel) && rule.IsInherited);
        Assert.True(DictWriter.Write(path, Model(), out string error), error);
        parent = new DirectoryInfo(directory).GetAccessControl();parent.RemoveAccessRuleSpecific(grant);
        new DirectoryInfo(directory).SetAccessControl(parent);
        Assert.DoesNotContain(new FileInfo(path).GetAccessControl().GetAccessRules(true,true,typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>(),
            rule => rule.IdentityReference.Equals(sentinel));
    }

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
