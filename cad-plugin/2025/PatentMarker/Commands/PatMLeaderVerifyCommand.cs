// ============================================================================
// 复线（mleader 分支）版本本地文件 — 仅 2025/2026 版编译。
// PATMLVERIFY：Explode 全部 PAT MLeader，将实际绘制几何与创建时记录的
// 用户点链（attach → dogleg… → text）对照，输出形态验证报告。
// 这是复线的回归测试工具（等价于探针 MLFORM 的生产版断言）。
// ============================================================================
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using AppAcad = Autodesk.AutoCAD.ApplicationServices.Application;

namespace PatentMarker.Commands
{
    public class PatMLeaderVerifyCommand
    {
        private class Exploded
        {
            public List<Point3d[]> Segments = new List<Point3d[]>();
            public List<string> OtherTypes = new List<string>();
            public int SolidCount;
        }

        [CommandMethod("PATMLVERIFY", CommandFlags.Modal)]
        public void Run()
        {
            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            // 报告目录：优先 PATML_REPORT_DIR 环境变量（测试/只读安装目录场景），
            // 默认写 DLL 同目录。
            string reportDir = Environment.GetEnvironmentVariable("PATML_REPORT_DIR");
            if (string.IsNullOrWhiteSpace(reportDir))
                reportDir = Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location) ?? ".";
            string reportPath = Path.Combine(reportDir,
                "PatentMarker-mleader-verify.txt");
            StringBuilder report = new StringBuilder();
            report.AppendLine("=====================================================");
            report.AppendLine("PATMLVERIFY — MLeader form verification");
            report.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("AutoCAD: " + AppAcad.Version);
            report.AppendLine("=====================================================");

            int total = 0, passed = 0, failed = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    MLeader ml = tr.GetObject(id, OpenMode.ForRead) as MLeader;
                    if (ml == null || !PatMLeaderCreator.IsPatMLeader(ml, tr)) continue;

                    total++;
                    bool ok = VerifyOne(tr, ml, report);
                    if (ok) passed++; else failed++;
                }
                tr.Commit();
            }

            report.AppendLine();
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "SUMMARY: total={0} passed={1} failed={2}", total, passed, failed));
            File.WriteAllText(reportPath, report.ToString(),
                new UTF8Encoding(false));

            ed.WriteMessage(string.Format(
                "PATMLVERIFY: {0} MLeader(s), {1} passed, {2} failed. Report: {3}\n",
                total, passed, failed, reportPath));
            PatentMarkerApp.RawLog("PATMLVERIFY: total=" + total
                + " passed=" + passed + " failed=" + failed
                + " report=" + reportPath);
        }

        private bool VerifyOne(Transaction tr, MLeader ml, StringBuilder report)
        {
            report.AppendLine();
            report.AppendLine("-----------------------------------------------------");
            bool hasArrow, isSplined;
            List<Point3d> chain = PatMLeaderCreator.ReadChain(ml, tr, out hasArrow, out isSplined);
            report.AppendLine("MLeader " + ml.ObjectId);

            if (chain.Count < 3)
            {
                report.AppendLine("  [FAIL] marker chain unreadable or incomplete ("
                    + chain.Count + " points)");
                return false;
            }
            Point3d attach = chain[0];
            Point3d textPt = chain[chain.Count - 1];
            List<Point3d> doglegs = chain.GetRange(1, chain.Count - 2);

            double textHeight = ml.MText != null ? ml.MText.TextHeight : ml.TextHeight;
            Point3d textLoc = ml.TextLocation;
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  recorded: attach={0} doglegs={1} text={2} arrow={3} splined={4}",
                P(attach), doglegs.Count, P(textPt), hasArrow, isSplined));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  entity: textLocation={0} height={1} leaderType={2} arrowSymbol={3} dogleg={4} landing={5}",
                P(textLoc), textHeight.ToString("F2", CultureInfo.InvariantCulture),
                ml.LeaderLineType, ml.ArrowSymbolId, ml.EnableDogleg, ml.EnableLanding));

            Exploded ex = ExplodeOne(ml);
            if (ex == null)
            {
                report.AppendLine("  [FAIL] Explode failed");
                return false;
            }
            report.AppendLine("  drawn segments=" + ex.Segments.Count
                + " otherTypes=[" + string.Join(",", ex.OtherTypes.ToArray()) + "]"
                + " solids=" + ex.SolidCount);

            bool ok = true;

            // C1 绘制路径起点 = 附着点
            double bestAttach = MinDistanceToEndpoint(ex, attach);
            Report(report, ref ok, bestAttach < 0.5,
                "C1 attach-on-path-start", bestAttach.ToString("F3"));

            // C2 每个用户拐点都在绘制路径上（直线模式精确校验；样条模式信息性）
            if (!isSplined)
            {
                for (int i = 0; i < doglegs.Count; i++)
                {
                    double d = MinDistanceToPath(ex, doglegs[i]);
                    Report(report, ref ok, d < 0.5,
                        "C2 dogleg" + i + "-on-path", d.ToString("F3"));
                }
            }
            else
            {
                report.AppendLine("  [INFO] C2 skipped in spline mode (curve containment needs sampling)");
            }

            // C3 文字位置 = 记录的文字点
            double dt = textLoc.DistanceTo(textPt);
            Report(report, ref ok, dt < 0.01, "C3 text-location", dt.ToString("F3"));

            // C4 多附着点检测：距文字 1×字高内线段端点数 ≤ 1
            int near = 0;
            foreach (Point3d[] seg in ex.Segments)
            {
                if (seg[0].DistanceTo(textLoc) < textHeight) near++;
                if (seg[1].DistanceTo(textLoc) < textHeight) near++;
            }
            Report(report, ref ok, near <= 1, "C4 single-attachment", near.ToString());

            // C5 箭头一致性：记录有箭头 → Explode 含 Solid；无箭头 → 无 Solid
            bool arrowConsistent = hasArrow ? ex.SolidCount > 0 : ex.SolidCount == 0;
            Report(report, ref ok, arrowConsistent, "C5 arrow-consistency",
                "recorded=" + hasArrow + " solids=" + ex.SolidCount);

            // C6 直线模式 → 折线段均为 Line
            if (!isSplined)
            {
                bool allLines = ex.OtherTypes.Count == 0;
                Report(report, ref ok, allLines, "C6 straight-all-lines",
                    ex.OtherTypes.Count.ToString());
            }

            report.AppendLine(ok ? "  => PASS" : "  => FAIL");
            return ok;
        }

        private static void Report(StringBuilder report, ref bool ok,
            bool pass, string check, string detail)
        {
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  [{0}] {1}: {2}", pass ? "PASS" : "FAIL", check, detail));
            if (!pass) ok = false;
        }

        private static Exploded ExplodeOne(MLeader ml)
        {
            try
            {
                DBObjectCollection objs = new DBObjectCollection();
                ml.Explode(objs);
                Exploded ex = new Exploded();
                foreach (DBObject o in objs)
                {
                    Line ln = o as Line;
                    if (ln != null)
                    {
                        ex.Segments.Add(new Point3d[] { ln.StartPoint, ln.EndPoint });
                        continue;
                    }
                    Polyline pl = o as Polyline;
                    if (pl != null)
                    {
                        for (int i = 0; i + 1 < pl.NumberOfVertices; i++)
                            ex.Segments.Add(new Point3d[]
                            {
                                new Point3d(pl.GetPoint2dAt(i).X, pl.GetPoint2dAt(i).Y, 0),
                                new Point3d(pl.GetPoint2dAt(i + 1).X, pl.GetPoint2dAt(i + 1).Y, 0)
                            });
                        continue;
                    }
                    Solid solid = o as Solid;
                    if (solid != null) { ex.SolidCount++; continue; }
                    MText mt = o as MText;
                    if (mt != null) continue; // 文字本体，不参与线段统计
                    ex.OtherTypes.Add(o.GetType().Name);
                }
                foreach (DBObject o in objs) o.Dispose();
                return ex;
            }
            catch { return null; }
        }

        private static double MinDistanceToEndpoint(Exploded ex, Point3d pt)
        {
            double best = double.MaxValue;
            foreach (Point3d[] seg in ex.Segments)
            {
                best = Math.Min(best, seg[0].DistanceTo(pt));
                best = Math.Min(best, seg[1].DistanceTo(pt));
            }
            return best;
        }

        private static double MinDistanceToPath(Exploded ex, Point3d pt)
        {
            double best = double.MaxValue;
            foreach (Point3d[] seg in ex.Segments)
                best = Math.Min(best, DistancePointSegment(pt, seg[0], seg[1]));
            return best;
        }

        private static double DistancePointSegment(Point3d pt, Point3d a, Point3d b)
        {
            Vector3d ab = b - a;
            double len2 = ab.DotProduct(ab);
            if (len2 < 1e-12) return pt.DistanceTo(a);
            double t = ((pt - a).DotProduct(ab)) / len2;
            t = Math.Max(0.0, Math.Min(1.0, t));
            Point3d proj = a + ab * t;
            return pt.DistanceTo(proj);
        }

        private static string P(Point3d p)
        {
            return "(" + p.X.ToString("F2", CultureInfo.InvariantCulture)
                + "," + p.Y.ToString("F2", CultureInfo.InvariantCulture) + ")";
        }
    }
}
