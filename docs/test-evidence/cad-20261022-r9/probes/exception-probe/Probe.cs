using System.Reflection;
using System.Text.Json;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using PatentMarker.Commands;
public class ExceptionProbe
{
    public static Document InjectedFailure() { throw new InvalidOperationException("r9-native-injected-active-document-failure"); }
    [CommandMethod("R9TOPFAULTS")]
    public void Run()
    {
        string root=Environment.GetEnvironmentVariable("R9_EXCEPTION_ROOT")!;Directory.CreateDirectory(root);
        var host=typeof(PatGeomVerifyCommand).Assembly.GetType("PatentMarker.IO.RuntimeHost",true)!;
        var field=host.GetField("_override",BindingFlags.NonPublic|BindingFlags.Static)!;
        object? previous=field.GetValue(null);string? oldDir=Environment.GetEnvironmentVariable("PATGEOM_REPORT_DIR"),oldRun=Environment.GetEnvironmentVariable("PATGEOM_RUN_ID");
        var rows=new List<object>();
        try
        {
            field.SetValue(null,Delegate.CreateDelegate(field.FieldType,typeof(ExceptionProbe).GetMethod(nameof(InjectedFailure))!));
            for(int i=1;i<=3;i++) foreach(bool denied in new[]{false,true})
            {
                string dir=Path.Combine(root,$"{i}-"+(denied?"denied":"fallback"));
                if(denied)File.WriteAllText(dir,"FAULT-SENTINEL");else Directory.CreateDirectory(dir);
                Environment.SetEnvironmentVariable("PATGEOM_REPORT_DIR",dir);Environment.SetEnvironmentVariable("PATGEOM_RUN_ID",$"r9-top-{i}-{denied}");
                string? escaped=null;try{new PatGeomVerifyCommand().Run();}catch(System.Exception e){escaped=e.ToString();}
                string report=Path.Combine(dir,"PatentMarker-geom-verify.txt");bool exists=File.Exists(report);
                bool expected=denied?!exists:exists&&File.ReadAllText(report).Contains("RESULT: FAIL")&&File.ReadAllText(report).Contains("r9-native-injected-active-document-failure");
                rows.Add(new{iteration=i,report_write_denied=denied,escaped_exception=escaped,report_exists=exists,report,result=escaped==null&&expected?"PASS":"FAIL",scope="L2 actual production DLL + native host; internal resolver seam injected only to force top-level failure; no user-path claim"});
            }
        }
        finally
        {
            field.SetValue(null,previous);Environment.SetEnvironmentVariable("PATGEOM_REPORT_DIR",oldDir);Environment.SetEnvironmentVariable("PATGEOM_RUN_ID",oldRun);
            File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(new{rows,host_override_restored=ReferenceEquals(field.GetValue(null),previous),environment_restored=Environment.GetEnvironmentVariable("PATGEOM_REPORT_DIR")==oldDir&&Environment.GetEnvironmentVariable("PATGEOM_RUN_ID")==oldRun},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
