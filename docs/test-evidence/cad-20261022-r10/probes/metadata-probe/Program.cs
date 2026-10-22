using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text.Json;

string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
var rows=new List<object>();
foreach(bool legacy in new[]{false,true})
{
 string dir=Path.Combine(root,legacy?"legacy":"automatic");Directory.CreateDirectory(dir);
 string p=Path.Combine(dir,"old.json"),tmp=p+".tmp",backup=p+".backup";
 File.WriteAllText(p,"OLD");File.WriteAllText(p+":sentinel","ADS");
 if(legacy){var sd=new FileInfo(p).GetAccessControl().GetSecurityDescriptorBinaryForm();if(!Native.SetFileSecurity(p,4,sd))throw new Win32Exception();}
 var original=new FileInfo(p).GetAccessControl();string before=original.GetSecurityDescriptorSddlForm(AccessControlSections.All);
 File.WriteAllText(tmp,"NEW");File.Replace(tmp,p,backup);
 string after=new FileInfo(p).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
 bool restored=Native.SetFileSecurity(p,4,original.GetSecurityDescriptorBinaryForm());int error=Marshal.GetLastWin32Error();
 string repaired=new FileInfo(p).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
 string backupAcl=new FileInfo(backup).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
 bool rollback=Native.MoveFileEx(backup,p,1|8);int rollbackError=Marshal.GetLastWin32Error();
 string rolled=new FileInfo(p).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
 rows.Add(new{legacy,before,after,changed=before!=after,restored,error,repaired,repair_exact=before==repaired,backup_exact=before==backupAcl,rollback,rollbackError,rollback_exact=rolled==before,rollback_bytes=File.ReadAllText(p),ads=File.ReadAllText(p+":sentinel")});
}
File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
static class Native
{
 [DllImport("advapi32.dll",EntryPoint="SetFileSecurityW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]public static extern bool SetFileSecurity(string path,uint info,byte[] descriptor);
 [DllImport("kernel32.dll",EntryPoint="MoveFileExW",CharSet=CharSet.Unicode,SetLastError=true)] [return:MarshalAs(UnmanagedType.Bool)]public static extern bool MoveFileEx(string from,string to,uint flags);
}
