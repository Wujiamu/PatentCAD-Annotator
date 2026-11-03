extern alias core;
using System.Text;
using PatentMarker.IO;
using PatentMarker.Palette;
using RealLoader = core::PatentMarker.IO.DictLoader;

// Only the second file-read boundary is controlled. The production workflow is
// compiled unchanged; the loader still parses through the production loader.
namespace PatentMarker.IO
{
    // The tested ReadSnapshot path does not use the host. Other methods in the
    // unchanged workflow need this name to compile across the assembly boundary.
    public static class RuntimeHost { public static object ActiveDocument; }
    public static class DictLoader
    {
        public static Action BeforeParse, AfterParse;
        public static DictModel PreviousModel => RealLoader.PreviousModel;
        public static string CurrentPath => RealLoader.CurrentPath;
        public static void InvalidateCache() => RealLoader.InvalidateCache();
        public static bool IsFileChanged() => RealLoader.IsFileChanged();
        public static DictModel LoadForCurrentDrawing() => RealLoader.LoadForCurrentDrawing();
        public static string ResolveDictPath() => RealLoader.ResolveDictPath();
        public static DictModel Load(string path)
        { BeforeParse?.Invoke(); var result=RealLoader.Load(path); AfterParse?.Invoke(); return result; }
        public static DictModel LoadBytes(string path,byte[] bytes)
        {
            BeforeParse?.Invoke();
            var method=typeof(RealLoader).GetMethod("LoadBytes",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);
            if(method==null)throw new MissingMethodException("Production LoadBytes boundary unavailable");
            var result=(DictModel)method.Invoke(null,new object[]{path,bytes});
            AfterParse?.Invoke();return result;
        }
    }
}
internal static class SnapshotProbe
{
    static int passed,failed;
    static void Check(string id,bool ok,string detail)
    { if(ok)passed++;else failed++;Console.WriteLine((ok?"PASS":"FAIL")+"|"+id+"|"+detail); }
    static int Main()
    {
        var dir=Path.Combine(AppContext.BaseDirectory,"fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        string a="{\"metadata\":{},\"entries\":[{\"number\":\"999\",\"name\":\"version A\"}],\"warnings\":[]}";
        string b="{\"metadata\":{},\"entries\":[{\"number\":\"302\",\"name\":\"version B\"}],\"warnings\":[]}";
        byte[] plain=new UTF8Encoding(false).GetBytes(a);
        var payloads=new Dictionary<string,(byte[] Bytes,string Numbers,bool Parsed)>{
            ["valid"]=(plain,"999",true),
            ["empty"]=(new UTF8Encoding(false).GetBytes("{\"metadata\":{},\"entries\":[],\"warnings\":[]}"),"",true),
            ["bad-json"]=(new UTF8Encoding(false).GetBytes("{broken"),"null",false),
            ["bad-utf8"]=(new byte[]{0xff,0xc3,0x28},"null",false),
            ["bom"]=(new byte[]{0xef,0xbb,0xbf}.Concat(plain).ToArray(),"999",true)
        };
        foreach(var payload in payloads)foreach(string mode in new[]{"stable","A-to-B","A-to-bad","A-to-B-to-A"})
        {
            string id=payload.Key+"/"+mode;
            byte[] original=payload.Value.Bytes;string identity=DictFileVisibility.Identity(original);
            string path=Path.Combine(dir,payload.Key+"-"+mode+".dict.json");File.WriteAllBytes(path,original);
            DictLoader.BeforeParse=mode=="stable"?null:()=>File.WriteAllText(path,mode=="A-to-bad"?"{broken":b,new UTF8Encoding(false));
            DictLoader.AfterParse=mode=="A-to-B-to-A"?()=>File.WriteAllBytes(path,original):null;
            var read=new DictPaletteWorkflow().ReadSnapshot(path);
            string number=read.Model==null?"null":string.Join(",",read.Model.Entries.Select(e=>e.Number));
            Check(id+"-read",read.Read,"read="+read.Read);
            Check(id+"-parsed",read.Parsed==payload.Value.Parsed,"expected="+payload.Value.Parsed+"|actual="+read.Parsed);
            Check(id+"-identity-A",read.DictIdentity==identity,"identity="+read.DictIdentity);
            Check(id+"-model-A",number==payload.Value.Numbers,"expected="+payload.Value.Numbers+"|actual="+number+"|disk-identity="+DictFileVisibility.ReadIdentity(path));
            if(mode=="A-to-B-to-A")
            {
                // Same cache-registration and writer APIs used after arbitration.
                // A phantom 302 must not become an editable entry over file A.
                DictEntry phantom=read.Model==null?null:read.Model.Entries.FirstOrDefault(e=>e.Number=="302");
                bool wrote=false;string detail="no phantom entry";
                if(phantom!=null)
                {
                    RealLoader.NotifySelfWrite(read.Model,path,read.DictIdentity);
                    string conflict,error;
                    if(core::PatentMarker.IO.DictWriter.TryApplyEdit(read.Model,phantom,"303","phantom edit",out conflict))
                    {wrote=core::PatentMarker.IO.DictWriter.Write(path,read.Model,read.DictIdentity,out error);detail=error;}
                }
                bool keptA=File.ReadAllBytes(path).SequenceEqual(original);
                Check(id+"-no-phantom-overwrite",!wrote&&keptA,"writer-succeeded="+wrote+"|original-bytes-preserved="+keptA+"|detail="+detail);
            }
        }
        Console.WriteLine("SUMMARY|passed="+passed+"|failed="+failed);return failed==0?0:1;
    }
}
