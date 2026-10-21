using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using PatentMarker.Commands;

// Supplemental L1 adversarial inputs. No native AutoCAD or GUI claim.
string output = Path.GetFullPath(args[0]);
Directory.CreateDirectory(output);
var rows = new List<object>();
int positives = 0, rejected = 0, failures = 0;
foreach (int side in new[] { -1, 1 })
foreach (int shape in new[] { 0, 1, 2 })
foreach (int samples in new[] { 2, 4, 16, 32 }) {
    var def = shape switch {
        0 => PatBraceGeometry.Create(new Point3d(0, 0, 0), new Point3d(0, 100, 0), 50, side),
        1 => PatBraceGeometry.Create(new Point3d(0, 0, 0), new Point3d(0, -20, 0), 100, side),
        _ => PatBraceGeometry.Create(new Point3d(10, 20, 7), new Point3d(80, 90, 7), 25, side)
    };
    var original = PatBraceGeometry.BuildPoints(def, samples);
    bool Check(List<Point3d> points, out string report) {
        var pl = new Polyline { Elevation = def.Top.Z };
        foreach (var p in points) pl.AddVertexAt(pl.NumberOfVertices, new Point2d(p.X, p.Y), 0, 0, 0);
        var text = new StringBuilder();
        bool accepted = PatBraceAssertions.Verify(pl, def, text);
        report = text.ToString();
        return accepted;
    }
    bool positive = Check(original, out string baselineReport);
    positives++;
    if (!positive) { failures++; Console.WriteLine(baselineReport); }
    rows.Add(new { kind = "positive", shape, side, samples, accepted = positive, result = positive ? "PASS" : "FAIL" });
    var ranges = new[] { (0, samples), (samples + 1, 2 * samples + 1), (2 * samples + 1, 3 * samples + 1), (3 * samples + 2, 4 * samples + 2) };
    double dx = def.Bottom.X - def.Top.X, dy = def.Bottom.Y - def.Top.Y;
    double height = Math.Sqrt(dx * dx + dy * dy), ax = dx / height, ay = dy / height;
    (double A, double O) Local(Point3d p) => ((p.X - def.Top.X) * ax + (p.Y - def.Top.Y) * ay, -(p.X - def.Top.X) * ay + (p.Y - def.Top.Y) * ax);
    for (int curve = 0; curve < ranges.Length; curve++) {
        var range = ranges[curve];
        var start = Local(original[range.Item1]); var end = Local(original[range.Item2]);
        foreach (int position in new[] { 1, samples / 2, samples - 1 }.Distinct())
        foreach (int corner in new[] { 0, 1 }) {
            double a = corner == 0 ? start.A : end.A, o = corner == 0 ? end.O : start.O;
            var changed = new List<Point3d>(original);
            changed[range.Item1 + position] = new Point3d(def.Top.X + ax * a - ay * o, def.Top.Y + ay * a + ax * o, def.Top.Z);
            bool accepted = Check(changed, out string report);
            bool b9Rejected = report.Contains("[FAIL] B9 corner-curves:", StringComparison.Ordinal);
            bool pass = !accepted && b9Rejected;
            if (pass) rejected++; else { failures++; Console.WriteLine($"Unexpected shape={shape} side={side} samples={samples} curve={curve} position={position} corner={corner}\n{report}"); }
            rows.Add(new { kind = "rectangle_corner_vertex", shape, side, samples, curve, position, corner, accepted, b9Rejected, result = pass ? "PASS" : "FAIL" });
        }
    }
}
var result = new { layer = "L1_SIMULATED_GEOMETRY", statement = "24 normal outlines and sharp rectangle-corner vertices inserted at four curve locations, early/middle/late sample indices, both corner choices, both sides, three aspect/orientation/Z variants. Does not inspect continuous curvature or prove all sharp polygons rejected.", positives, rejected, failures, rows, result = failures == 0 ? "PASS" : "FAIL" };
File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
Console.WriteLine($"CORNER_MATRIX positives={positives} negatives_rejected={rejected} failures={failures}");
return failures == 0 ? 0 : 1;
