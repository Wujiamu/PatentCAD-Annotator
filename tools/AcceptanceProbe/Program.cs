using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Commands;

// Independent acceptance harness. Identical input and oracle for both snapshots.
// This is a simulated host; it never proves AutoCAD installation or GUI behavior.
string mode=args[0], output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
string[] envNames={"PATGEOM_REPORT_DIR","PATGEOM_RUN_ID","PATGEOM_REQUIRE_BRACES","PATGEOM_REQUIRE_TEXTS","PATGEOM_STRICT"};
var original=envNames.ToDictionary(n=>n,n=>Environment.GetEnvironmentVariable(n));
var result=new Dictionary<string,object>{{"case",mode},{"layer","L1_SIMULATED_COMMAND"}};
bool pass=false;
try {
    var definition=PatBraceGeometry.Create(new Point3d(0,0,0),new Point3d(0,100,0),50,1);
    if(mode=="sharp") {
        double c=50*0.08333;
        var coords=new (double a,double o)[]{(0,0),(0.001,24.99),(c,25),(50-c,25),(49.999,25.01),(50,50),(50.001,25.01),(50+c,25),(100-c,25),(99.999,24.99),(100,0)};
        var pl=new Polyline();foreach(var p in coords)pl.AddVertexAt(pl.NumberOfVertices,new Point2d(-p.o,p.a),0,0,0);
        var sb=new StringBuilder();bool accepted=PatBraceAssertions.Verify(pl,definition,sb);
        Console.WriteLine("ORIGINAL_A01 accepted="+accepted);Console.Write(sb);
        result["accepted"]=accepted;pass=!accepted;
    } else if(mode=="geometry-matrix" || mode=="scale") {
        var method=typeof(PatBraceGeometry).GetMethod("BuildPoints",new[]{typeof(PatBraceDefinition),typeof(int)});
        if(method==null){result["result"]="SKIP/BLOCKED";result["reason"]="sampling overload absent in this snapshot";}
        else {
            var rows=new List<object>();pass=true;
            var definitions= mode=="scale"?new[]{definition}:new[]{definition,
                PatBraceGeometry.Create(new Point3d(5,10,7.5),new Point3d(105,10,7.5),30,-1),
                PatBraceGeometry.Create(new Point3d(0,0,0),new Point3d(0,-100,0),50,-1),
                PatBraceGeometry.Create(new Point3d(10,20,2),new Point3d(80,90,2),25,1),
                PatBraceGeometry.Create(new Point3d(0,0,0),new Point3d(0,20,0),100,-1)};
            foreach(var def in definitions)foreach(int samples in mode=="scale"?new[]{24,249,1249}:new[]{2,4,16,32}) {
                var points=(IEnumerable<Point3d>)method.Invoke(null,new object[]{def,samples});
                var pl=new Polyline();foreach(var p in points)pl.AddVertexAt(pl.NumberOfVertices,new Point2d(p.X,p.Y),0,0,0);pl.Elevation=def.Top.Z;
                var sb=new StringBuilder();var timer=Stopwatch.StartNew();bool accepted=PatBraceAssertions.Verify(pl,def,sb);timer.Stop();
                rows.Add(new{samples,vertices=pl.NumberOfVertices,height=def.Height,width=def.Width,side=def.Side,elapsed_ms=timer.Elapsed.TotalMilliseconds,accepted});
                Console.WriteLine(JsonSerializer.Serialize(rows.Last()));if(!accepted)Console.Write(sb);pass&=accepted;
            }
            result["measurements"]=rows;result["performance_scope"]="L1 brace assertions only; not PATGEOMVERIFY host throughput";
        }
    } else {
        var db=new Database();var doc=new Autodesk.AutoCAD.ApplicationServices.Document{Database=db,Editor=new Autodesk.AutoCAD.EditorInput.Editor(),Name="C:\\sim\\acceptance.dwg"};
        Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument=doc;
        PatentMarker.IO.PatSettingsStore.Activate(doc.Name);PatentMarker.IO.PatSettingsStore.ResetConfigDefaults();
        double originX=mode=="translated-large"?10000000.0:0.0;
        using(var tr=db.TransactionManager.StartTransaction()) {
            var brace=PatBraceEntity.CreatePolyline(db,definition);db.ModelSpace.AppendEntity(brace);tr.AddNewlyCreatedDBObject(brace,true);PatBraceEntity.WriteDefinition(brace,definition,tr);
            if(mode=="legacy-leader") {
                var text=new MText{Location=new Point3d(20,10,0),TextHeight=3.5,Rotation=0};db.ModelSpace.AppendEntity(text);tr.AddNewlyCreatedDBObject(text,true);
                var leader=new Leader();leader.AppendVertex(new Point3d(0,0,0));leader.AppendVertex(new Point3d(10,10,0));leader.AppendVertex(new Point3d(18.6,10,0));db.ModelSpace.AppendEntity(leader);tr.AddNewlyCreatedDBObject(leader,true);PatLeaderTextAttachment.LinkText(leader,text,tr);
            } else PatMLeaderCreator.Create(db,tr,db.ModelSpace,new Point3d(originX,0,0),new List<Point3d>{new Point3d(originX+10,10,0)},new Point3d(originX+20,10,0),"901");tr.Commit();
        }
        var ml=db.CommittedEntities.OfType<MLeader>().SingleOrDefault();var vertices=ml==null?new List<Point3d>(): (IList<Point3d>)ml.Vertices;
        bool moveMtext=mode is "moved-text" or "mtext-only" or "all-synchronized";
        bool moveTextLocation=mode is "moved-text" or "textlocation-only" or "all-synchronized";
        if(moveMtext){var mt=ml.MText;mt.Location=new Point3d(2000,1000,0);ml.MText=mt;}
        if(moveTextLocation)ml.TextLocation=new Point3d(2000,1000,0);
        if(mode=="moved-previous")vertices[vertices.Count-2]=new Point3d(10,100,0);
        if(mode is "record-only" or "all-synchronized")using(var tr=db.TransactionManager.StartTransaction()) {
            var dictionary=(DBDictionary)tr.GetObject(ml.ExtensionDictionary,OpenMode.ForWrite);
            var record=(Xrecord)tr.GetObject(dictionary.GetAt("PATENTMARKER_MLEADER"),OpenMode.ForWrite);
            var values=new List<TypedValue>();foreach(TypedValue v in record.Data)values.Add(v);
            int lastPoint=values.FindLastIndex(v=>v.TypeCode==10);values[lastPoint]=new TypedValue(10,new Point3d(2000,1000,0));
            record.Data=new ResultBuffer(values.ToArray());tr.Commit();
        }
        if(mode=="all-synchronized") {
            // Analytical expected endpoint from (10,10) to (2000,1000), gap=1.4.
            double length=Math.Sqrt(1990.0*1990+990.0*990);vertices[vertices.Count-1]=new Point3d(2000-1.4*1990/length,1000-1.4*990/length,0);
        }
        foreach(var pair in new Dictionary<string,string>{{"PATGEOM_REPORT_DIR",output},{"PATGEOM_RUN_ID","acceptance-"+mode},{"PATGEOM_REQUIRE_BRACES","1"},{"PATGEOM_REQUIRE_TEXTS","1"},{"PATGEOM_STRICT","1"}})Environment.SetEnvironmentVariable(pair.Key,pair.Value);
        Exception error=null;var timer=Stopwatch.StartNew();try{new PatGeomVerifyCommand().Run();}catch(Exception ex){error=ex;}timer.Stop();
        string report=File.ReadAllText(Path.Combine(output,"PatentMarker-geom-verify.txt"));bool accepted=report.Contains("RESULT: PASS");
        Console.WriteLine("CASE="+mode+(ml==null?" product Leader + MText":" actual_previous="+vertices[vertices.Count-2]+" actual_endpoint="+vertices.Last()+" actual_text="+ml.MText.Location));
        Console.WriteLine("command_exception="+(error?.GetType().Name??"none"));Console.Write(report);
        result["accepted"]=accepted;result["command_exception"]=error?.GetType().Name;result["command_elapsed_ms"]=timer.Elapsed.TotalMilliseconds;
        result["report_bytes"]=new FileInfo(Path.Combine(output,"PatentMarker-geom-verify.txt")).Length;
        bool expectedAcceptance=mode is "baseline" or "all-synchronized" or "translated-large" or "legacy-leader";
        // Rejection belongs to the FAIL report and external gate, never an
        // exception crossing AutoCAD's native command boundary.
        pass=(expectedAcceptance?accepted:!accepted)&&error==null;
    }
} catch(Exception ex){Console.Error.WriteLine(ex);result["error"]=ex.ToString();pass=false;}
finally {
    foreach(var pair in original)Environment.SetEnvironmentVariable(pair.Key,pair.Value);
    result["environment_restored"]=original.All(p=>Environment.GetEnvironmentVariable(p.Key)==p.Value);
    result["result"]=result.ContainsKey("result")?result["result"]:(pass?"PASS":"FAIL");
    File.WriteAllText(Path.Combine(output,"case-result.json"),JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
}
Environment.ExitCode=(string)result["result"]=="SKIP/BLOCKED"?3:(pass?0:1);
