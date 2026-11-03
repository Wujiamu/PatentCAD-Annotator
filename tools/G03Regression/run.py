import os, pathlib, subprocess, sys
root = pathlib.Path(os.environ.get('G03_SOURCE_ROOT', pathlib.Path(__file__).resolve().parents[2]))
out = pathlib.Path(__file__).resolve().parent / 'obj' / 'Production.cs'

def extract(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 0
    for i in range(opening, len(source)):
        if source[i] == '{': depth += 1
        if source[i] == '}':
            depth -= 1
            if depth == 0: return source[start:i+1]
    raise RuntimeError('unclosed ' + signature)

header = """using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using PatentMarker.I18n;
namespace PatentMarker.Commands {
"""
routing = (root/'cad-plugin/Shared/Commands/PatTextPathRouting.cs').read_text(encoding='utf-8-sig')
header += extract(routing,'public static class PatTextPathRouting')+'\n'
att = (root/'cad-plugin/Shared/Commands/PatLeaderTextAttachment.cs').read_text(encoding='utf-8-sig')
header += 'public static class PatLeaderTextAttachment { private const string AnnotationLinkKey="PATENTMARKER_MTEXT"; private const int HardPointerIdCode=340; public const double TextGapPerHeight=0.4;\n'
for name in ('public static AttachmentPoint Get(', 'public static Point3d Retract(', 'public static void SetTextEndpoint(', 'public static ObjectId GetAnnotationId('):
    header += extract(att,name)+'\n'
for extra in ('public static void ReadTextMetrics(', 'public static bool MoveText(', 'private static bool SamePoint('):
    if extra in att: header += extract(att,extra)+'\n'
header += '}\n'
for cls, filename in [('Align07','cad-plugin/Shared/Commands/PatAlignCommand.cs'),('Align10','cad-plugin/2010/PatentMarker/Commands/PatAlignCommand.cs')]:
    source=(root/filename).read_text(encoding='utf-8-sig')
    header += 'public class '+cls+' {\n'
    for constant in ('GapPerTextHeight', 'ColGapPerTextHeight'):
        header += next(line for line in source.splitlines() if 'private const double '+constant in line)+'\n'
    header += extract(source,'private class Target')+'\n'
    for name in ('private static List<Target> CollectTargets(', 'private static bool MeasureWidth(', 'private static bool MoveTarget(', 'private void AlignToLine(', 'private void AlignToFrame(', 'private static Point3d FramePos(', 'private static int CompareByProjection(', 'private static void Report('):
        header += extract(source,name)+'\n'
    header += """public static object Collect(Transaction tr, SelectionSet ss) { return CollectTargets(tr,ss,new Editor())[0]; }
    public static double Height(object t) { return ((Target)t).TextHeight; }
    public static double Width(Transaction tr, object t) { if(!MeasureWidth(tr,(Target)t)) throw new Exception("measurement failed"); return ((Target)t).Width; }
    public static bool Move(Transaction tr, object t, Point3d p) { return MoveTarget(tr,(Target)t,p); }
    public void DoLine(Editor ed, SelectionSet ss) { AlignToLine(ed,ss); }
    public void DoFrame(Editor ed, SelectionSet ss) { AlignToFrame(ed,ss); }
    }\n"""
header += '}\n'
out.parent.mkdir(exist_ok=True)
out.write_text(header,encoding='utf-8')
raise SystemExit(subprocess.call(['dotnet','run','--project',str(out.parent.parent/'G03Regression.csproj'),'-c','Release','--',*(sys.argv[1:] or ['all'])],cwd=pathlib.Path(__file__).resolve().parents[2]))
