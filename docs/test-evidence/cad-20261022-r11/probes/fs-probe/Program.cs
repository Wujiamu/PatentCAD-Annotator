using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

string dll=Path.GetFullPath(args[0]), output=Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var assembly=Assembly.LoadFrom(dll);
var modelType=assembly.GetType("PatentMarker.IO.DictModel",true)!;
var writer=assembly.GetType("PatentMarker.IO.DictWriter",true)!.GetMethod("Write")!;
var model=JsonSerializer.Deserialize("{\"metadata\":{\"version\":\"r9\"},\"entries\":[{\"number\":\"901\",\"name\":\"new\"}],\"warnings\":[]}",modelType)!;
var sid=WindowsIdentity.GetCurrent().User!;
var rows=new List<object>();
void SetSddl(string path,string sddl){var acl=new FileSecurity();acl.SetSecurityDescriptorSddlForm(sddl,AccessControlSections.Access|AccessControlSections.Owner|AccessControlSections.Group);new FileInfo(path).SetAccessControl(acl);}
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
bool Write(string p,out string error){object?[] a={p,model,""};try{var ok=(bool)writer.Invoke(null,a)!;error=(string)a[2]!;return ok;}catch(Exception e){error=e.ToString();return false;}}
void Case(string id,Action<string,List<string>,Dictionary<string,object?>> action){
 string dir=Path.Combine(output,id);Directory.CreateDirectory(dir);var failures=new List<string>();var details=new Dictionary<string,object?>();
 try{action(dir,failures,details);}catch(Exception e){failures.Add(e.ToString());}
 rows.Add(new{id,result=failures.Count==0?"PASS":"FAIL",failures,details});
 Console.WriteLine(id+" "+(failures.Count==0?"PASS":"FAIL"));
}
void Check(bool ok,string message,List<string> failures){if(!ok)failures.Add(message);}
string Seed(string dir){string p=Path.Combine(dir,"sample.dict.json");File.WriteAllText(p,"{\"entries\":[{\"number\":\"old\"}]}",new UTF8Encoding(false));return p;}
foreach(var attrs in new[]{FileAttributes.Archive,FileAttributes.Hidden|FileAttributes.System|FileAttributes.Archive,FileAttributes.Hidden|FileAttributes.System|FileAttributes.Archive|FileAttributes.NotContentIndexed}){
 Case("replace-attributes-"+(int)attrs,(dir,f,d)=>{string p=Seed(dir);File.SetAttributes(p,attrs);var before=File.GetAttributes(p);bool ok=Write(p,out string error);var after=File.GetAttributes(p);d["write_result"]=ok;d["error"]=error;d["attributes_before"]=(int)before;d["attributes_after"]=(int)after;Check(ok,"write should succeed",f);Check(after==before,"full attributes changed",f);Check(!File.Exists(p+".tmp"),"temp leak",f);});
}
Case("readonly-protection",(dir,f,d)=>{string p=Seed(dir);var beforeHash=Hash(p);File.SetAttributes(p,FileAttributes.ReadOnly|FileAttributes.Hidden|FileAttributes.System|FileAttributes.Archive);var before=File.GetAttributes(p);bool ok=Write(p,out string error);d["write_result"]=ok;d["error"]=error;d["attributes_after"]=(int)File.GetAttributes(p);d["sha_before"]=beforeHash;d["sha_after"]=Hash(p);Check(!ok,"readonly file should reject write",f);Check(Hash(p)==beforeHash,"readonly original bytes changed",f);Check(File.GetAttributes(p)==before,"readonly attributes changed",f);});
Case("locked-preservation-and-recovery",(dir,f,d)=>{string p=Seed(dir);File.SetAttributes(p,FileAttributes.Hidden|FileAttributes.System|FileAttributes.Archive|FileAttributes.NotContentIndexed);var before=File.GetAttributes(p);var bytes=File.ReadAllBytes(p);bool ok;string error;using(var held=new FileStream(p,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){ok=Write(p,out error);}d["locked_write"]=ok;d["error"]=error;Check(!ok,"locked target accepted",f);Check(File.ReadAllBytes(p).SequenceEqual(bytes),"locked original bytes changed",f);Check(File.GetAttributes(p)==before,"locked original attributes changed",f);Check(!File.Exists(p+".tmp"),"failed write temp leak",f);Check(Write(p,out error),"recovery write failed: "+error,f);Check(File.GetAttributes(p)==before,"recovery attributes changed",f);});
Case("foreign-temp-sentinel",(dir,f,d)=>{string p=Seed(dir),temp=p+".tmp";File.WriteAllText(temp,"FOREIGN-SENTINEL");string sentinel=Hash(temp);bool ok=Write(p,out string error);d["write_result"]=ok;d["error"]=error;Check(File.Exists(temp)&&Hash(temp)==sentinel,"preexisting foreign temp file overwritten/deleted",f);});
Case("replace-acl-ads-creation",(dir,f,d)=>{string p=Seed(dir);File.WriteAllText(p+":r9-sentinel","ADS-SENTINEL");var acl=new FileInfo(p).GetAccessControl();acl.SetAccessRuleProtection(true,true);new FileInfo(p).SetAccessControl(acl);File.SetCreationTimeUtc(p,new DateTime(2020,1,2,3,4,5,DateTimeKind.Utc));string sddl=new FileInfo(p).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);var created=File.GetCreationTimeUtc(p);bool ok=Write(p,out string error);d["write_result"]=ok;d["error"]=error;Check(ok,"normal replace failed",f);Check(new FileInfo(p).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All)==sddl,"ACL changed",f);Check(File.Exists(p+":r9-sentinel")&&File.ReadAllText(p+":r9-sentinel")=="ADS-SENTINEL","ADS lost",f);Check(File.GetCreationTimeUtc(p)==created,"creation time changed",f);});
foreach(var right in new[]{FileSystemRights.ReadData,FileSystemRights.ChangePermissions,FileSystemRights.WriteData}){
 Case("replace-denied-control-"+right,(dir,f,d)=>{
  string p=Seed(dir),control=Path.Combine(dir,"control.dict.json"),temp=control+".tmp";File.WriteAllText(p+":r9-sentinel","ADS-SENTINEL");File.WriteAllText(control,"CONTROL-ORIGINAL");File.WriteAllText(control+":r9-sentinel","ADS-SENTINEL");
  var original=new FileInfo(p).GetAccessControl();string originalSddl=original.GetSecurityDescriptorSddlForm(AccessControlSections.All);var denied=new FileInfo(p).GetAccessControl();denied.AddAccessRule(new FileSystemAccessRule(sid,right,AccessControlType.Deny));new FileInfo(p).SetAccessControl(denied);
  string protectedSddl=new FileInfo(p).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);string replaceError="";bool replaceDenied=false;
  SetSddl(control,protectedSddl);
  File.WriteAllText(temp,"CONTROL");try{File.Replace(temp,control,null);}catch(UnauthorizedAccessException e){replaceDenied=true;replaceError=e.Message;}catch(Exception e){replaceError=e.GetType().Name+":"+e.Message;}
  if(right==FileSystemRights.ReadData||right==FileSystemRights.WriteData)Check(replaceDenied,"ACL control did not actually deny Replace",f);
  d["control_replace_denied"]=replaceDenied;d["control_replace_error"]=replaceError;
  if(File.Exists(temp))File.Delete(temp);
  SetSddl(control,originalSddl);
  bool ok=Write(p,out string error);d["write_result"]=ok;d["error"]=error;
  string afterSddl=new FileInfo(p).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);d["acl_preserved"]=afterSddl==protectedSddl;
  Check(afterSddl==protectedSddl,"ACL changed under denied Replace control",f);
  SetSddl(p,originalSddl);
  bool ads=File.Exists(p+":r9-sentinel")&&File.ReadAllText(p+":r9-sentinel")=="ADS-SENTINEL";d["ads_preserved"]=ads;Check(ads,"ADS lost under denied Replace control",f);
  if(!ok)Check(File.ReadAllText(p)=="{\"entries\":[{\"number\":\"old\"}]}","failed write changed original",f);
 });
}
Case("new-unicode-space-path",(dir,f,d)=>{string p=Path.Combine(dir,"中文 空格.dict.json");Check(Write(p,out string error),"create failed: "+error,f);var bytes=File.ReadAllBytes(p);Check(!(bytes.Length>=3&&bytes[0]==239&&bytes[1]==187&&bytes[2]==191),"UTF8 BOM emitted",f);Check((File.GetAttributes(p)&(FileAttributes.Hidden|FileAttributes.System))==(FileAttributes.Hidden|FileAttributes.System),"new default attributes missing",f);});
Case("missing-parent-fails-cleanly",(dir,f,d)=>{string p=Path.Combine(dir,"missing","test.dict.json");Check(!Write(p,out string error),"missing parent accepted",f);Check(!File.Exists(p),"unexpected target created",f);d["error"]=error;});
Case("ntfs-compression-preserved",(dir,f,d)=>{string p=Seed(dir);var info=new System.Diagnostics.ProcessStartInfo("compact.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(string arg in new[]{"/C","/I","/Q",p})info.ArgumentList.Add(arg);using(var process=System.Diagnostics.Process.Start(info)!){string text=process.StandardOutput.ReadToEnd();string stderr=process.StandardError.ReadToEnd();process.WaitForExit();d["compact_exit"]=process.ExitCode;d["compact_stdout"]=text;d["compact_stderr"]=stderr;}Check((File.GetAttributes(p)&FileAttributes.Compressed)!=0,"fixture was not actually compressed",f);bool ok=Write(p,out string error);d["error"]=error;Check(ok,"compressed replacement rejected",f);Check((File.GetAttributes(p)&FileAttributes.Compressed)!=0,"compression lost",f);});
Case("legacy-inheritance-exact",(dir,f,d)=>{
 var parent=new DirectoryInfo(dir).GetAccessControl().GetSecurityDescriptorBinaryForm();Check(Native.SetFileSecurity(dir,4,parent),"legacy parent fixture failed",f);
 string p=Seed(dir);File.WriteAllText(p+":r9-sentinel","ADS-SENTINEL");File.SetCreationTimeUtc(p,new DateTime(2020,1,2,3,4,5,DateTimeKind.Utc));var created=File.GetCreationTimeUtc(p);
 var acl=new FileInfo(p).GetAccessControl();var raw=new RawSecurityDescriptor(acl.GetSecurityDescriptorBinaryForm(),0);Check((raw.ControlFlags&ControlFlags.DiscretionaryAclAutoInherited)==0,"fixture not legacy",f);
 string before=acl.GetSecurityDescriptorSddlForm(AccessControlSections.All);bool ok=Write(p,out string error);string after=new FileInfo(p).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
 d["before_acl"]=before;d["after_acl"]=after;d["write_result"]=ok;d["error"]=error;Check(ok,"legacy replacement failed",f);Check(before==after,"legacy ACL changed",f);Check(File.ReadAllText(p+":r9-sentinel")=="ADS-SENTINEL","legacy ADS changed",f);Check(created==File.GetCreationTimeUtc(p),"legacy creation changed",f);
});
Case("legacy-repair-denied-rollback",(dir,f,d)=>{
 Check(Native.SetFileSecurity(dir,4,new DirectoryInfo(dir).GetAccessControl().GetSecurityDescriptorBinaryForm()),"legacy parent fixture failed",f);
 string p=Seed(dir);File.WriteAllText(p+":r9-sentinel","ADS-SENTINEL");File.SetAttributes(p,FileAttributes.Hidden|FileAttributes.System|FileAttributes.Archive|FileAttributes.NotContentIndexed);var attributes=File.GetAttributes(p);var hash=Hash(p);
 var raw=new RawSecurityDescriptor(new FileInfo(p).GetAccessControl().GetSecurityDescriptorBinaryForm(),0);raw.DiscretionaryAcl!.InsertAce(0,new CommonAce(AceFlags.None,AceQualifier.AccessDenied,(int)FileSystemRights.ChangePermissions,new SecurityIdentifier("S-1-3-4"),false,null));byte[] buffer=new byte[raw.BinaryLength];raw.GetBinaryForm(buffer,0);Check(Native.SetFileSecurity(p,4,buffer),"deny fixture failed",f);
 string before=new FileInfo(p).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);bool ok=Write(p,out string error);string after=new FileInfo(p).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
 d["before_acl"]=before;d["after_acl"]=after;d["write_result"]=ok;d["error"]=error;Check(!ok,"denied repair reported success",f);Check(error.Contains("原文件已恢复"),"rollback branch not observed",f);Check(hash==Hash(p),"rollback bytes changed",f);Check(before==after,"rollback ACL changed",f);Check(attributes==File.GetAttributes(p),"rollback attributes changed",f);Check(File.ReadAllText(p+":r9-sentinel")=="ADS-SENTINEL","rollback ADS changed",f);
});
Case("unique-owned-temp-cleanup",(dir,f,d)=>{var leaks=Directory.GetFiles(output,"*.pat-*",SearchOption.AllDirectories);d["leaks"]=leaks;Check(leaks.Length==0,"owned temporary or rollback files leaked",f);});
File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{assembly_path=dll,assembly_sha256=Hash(dll),assembly_mvid=assembly.ManifestModule.ModuleVersionId,filesystem=new DriveInfo(Path.GetPathRoot(output)!).DriveFormat,layer="Actual candidate DLL IO entry on real isolated NTFS; no CAD/UI claim",rows},new JsonSerializerOptions{WriteIndented=true}));
Environment.ExitCode=rows.Any(x=>(string)x.GetType().GetProperty("result")!.GetValue(x)! == "FAIL")?1:0;
static class Native {
 [System.Runtime.InteropServices.DllImport("advapi32.dll",EntryPoint="SetFileSecurityW",CharSet=System.Runtime.InteropServices.CharSet.Unicode,SetLastError=true)]
 [return:System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]public static extern bool SetFileSecurity(string path,uint information,byte[] descriptor);
}
