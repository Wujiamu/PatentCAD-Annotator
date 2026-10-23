using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.Commands;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using App = Autodesk.AutoCAD.ApplicationServices.Core.Application;
public class R9ScaleProbe
{
    [CommandMethod("R9BOUNDARIES")]
    public void Run()
    {
        string root = Environment.GetEnvironmentVariable("R9_SCALE_ROOT")!;
        Directory.CreateDirectory(root);
        string[] names = { "PATGEOM_REPORT_DIR", "PATGEOM_RUN_ID", "PATGEOM_REQUIRE_BRACES", "PATGEOM_REQUIRE_TEXTS", "PATGEOM_STRICT" };
        var original = names.ToDictionary(x => x, Environment.GetEnvironmentVariable);
        var rows = new List<object>();
        var db = App.DocumentManager.MdiActiveDocument.Database;
        var owned = new List<ObjectId>();
        try
        {
            for (int repeat = 1; repeat <= 3; repeat++)
            foreach (var spec in new[] { ("vertices-100",24,1,"valid"), ("vertices-1000",249,1,"valid"), ("vertices-5000",1249,1,"valid"), ("entities-100",8,100,"valid"), ("entities-1000",8,1000,"valid"), ("entities-5000",8,5000,"valid"), ("closed",8,1,"closed"), ("bulged",8,1,"bulged") })
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var id in owned) ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase();
                    owned.Clear();
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                    for (int i = 0; i < spec.Item3; i++)
                    {
                        var definition = PatBraceGeometry.Create(new Point3d(i*100,0,0),new Point3d(i*100,100,0),40,1);
                        var pl = new Polyline(); pl.SetDatabaseDefaults(db);
                        var points = PatBraceGeometry.BuildPoints(definition,spec.Item2);
                        // Exact even vertex counts: one coincident straight-stem
                        // junction vertex; geometry and symmetry are unchanged.
                        if (spec.Item1.StartsWith("vertices-")) points.Insert(spec.Item2+1,points[spec.Item2]);
                        foreach (var p in points) pl.AddVertexAt(pl.NumberOfVertices,new Point2d(p.X,p.Y),0,0,0);
                        if (spec.Item4=="closed") pl.Closed=true;
                        if (spec.Item4=="bulged") pl.SetBulgeAt(3,.25);
                        owned.Add(ms.AppendEntity(pl)); tr.AddNewlyCreatedDBObject(pl,true);
                        PatBraceEntity.WriteDefinition(pl,definition,tr);
                    }
                    tr.Commit();
                }
                string dir = Path.Combine(root,$"{repeat}-{spec.Item1}"); Directory.CreateDirectory(dir);
                Environment.SetEnvironmentVariable("PATGEOM_REPORT_DIR",dir);
                Environment.SetEnvironmentVariable("PATGEOM_RUN_ID",$"r9-{repeat}-{spec.Item1}");
                Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_BRACES",spec.Item3.ToString());
                Environment.SetEnvironmentVariable("PATGEOM_REQUIRE_TEXTS","0");
                Environment.SetEnvironmentVariable("PATGEOM_STRICT","1");
                var timer = Stopwatch.StartNew(); new PatGeomVerifyCommand().Run(); timer.Stop();
                string path = Path.Combine(dir,"PatentMarker-geom-verify.txt"), text=File.ReadAllText(path);
                bool accepted = text.Contains("RESULT: PASS"), expected = spec.Item4=="valid";
                var process = Process.GetCurrentProcess(); process.Refresh();
                rows.Add(new { repeat, name=spec.Item1, entities=spec.Item3, vertices_per_entity=4*spec.Item2+3+(spec.Item1.StartsWith("vertices-")?1:0), elapsed_ms=timer.Elapsed.TotalMilliseconds, peak_working_set_bytes=process.PeakWorkingSet64, report_bytes=new FileInfo(path).Length, accepted, expected_accepted=expected, result=accepted==expected?"PASS":"FAIL", report=path, report_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant() });
                File.WriteAllText(Path.Combine(root,"results.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
                if (accepted!=expected) break;
            }
        }
        finally
        {
            foreach (string name in names) Environment.SetEnvironmentVariable(name, original[name]);
            File.WriteAllText(Path.Combine(root,"isolation.json"),JsonSerializer.Serialize(new {environment_restored=names.All(x=>Environment.GetEnvironmentVariable(x)==original[x]), scope="L2 native fixture construction + production PATGEOMVERIFY; no GUI/installation proof", reference_points_per_corner=257, max_curve_samples=4096, maximum_generated_vertices=16387, entity_upper_limit="No product hard cap; measured through 5000 only", sampling_note="even vertex-count cases include one coincident straight-stem junction"},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
