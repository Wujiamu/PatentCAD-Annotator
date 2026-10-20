using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.Diagnostics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

using Exception = System.Exception;

namespace PatentMarker.Commands
{
    /// <summary>
    /// PATGEOMVERIFY: command-level geometry regression verification for all
    /// five editions. It reads entities only; it never creates or edits DWG
    /// objects. One JSON-like text report is emitted per run and a failing
    /// required assertion produces a FAIL report. The external verifier owns
    /// the nonzero gate exit; managed errors must not escape the CAD command.
    /// </summary>
    public sealed class PatGeomVerifyCommand
    {
        [CommandMethod("PATGEOMVERIFY", CommandFlags.Modal)]
        public void Run()
        {
            try
            {
                RunVerification();
            }
            catch (Exception ex)
            {
                // Core Console's native exception filter can itself crash or
                // open an error dialog when a managed command exception escapes.
                // Preserve a fail-closed diagnostic instead of invoking it.
                string message = "PATGEOMVERIFY: FAIL; verification aborted: "
                    + ex.GetType().FullName + ": " + ex.Message;
                try { PatentMarkerApp.RawLog(message + "\n" + ex.StackTrace); }
                catch { }
                try
                {
                    Document doc = IO.RuntimeHost.ActiveDocument;
                    if (doc != null) doc.Editor.WriteMessage(message + "\n");
                }
                catch { }
                try
                {
                    string directory = ResolveReportDirectory();
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, "PatentMarker-geom-verify.txt"),
                        "PATGEOMVERIFY - verification aborted\nRUN_ID: "
                        + Environment.GetEnvironmentVariable("PATGEOM_RUN_ID")
                        + "\nERROR: " + message + "\nRESULT: FAIL\n", new UTF8Encoding(false));
                }
                catch { }
            }
        }

        private static string ResolveReportDirectory()
        {
            string directory = Environment.GetEnvironmentVariable("PATGEOM_REPORT_DIR");
            if (string.IsNullOrEmpty(directory))
                directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return string.IsNullOrEmpty(directory) ? "." : directory;
        }

        private static void RunVerification()
        {
            Document doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            Autodesk.AutoCAD.EditorInput.Editor ed = doc.Editor;
            Database db = doc.Database;

            string runId = Environment.GetEnvironmentVariable("PATGEOM_RUN_ID");
            if (string.IsNullOrEmpty(runId))
                runId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                    + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

            string reportDir = ResolveReportDirectory();

            int requiredBraces = ReadRequiredCount("PATGEOM_REQUIRE_BRACES", 1);
            int requiredTexts = ReadRequiredCount("PATGEOM_REQUIRE_TEXTS", 1);
            bool strict = ReadBooleanEnv("PATGEOM_STRICT");

            StringBuilder report = new StringBuilder();
            report.AppendLine("=====================================================");
            report.AppendLine("PATGEOMVERIFY - geometry regression verification");
            report.AppendLine("RUN_ID: " + runId);
            report.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            report.AppendLine("AutoCAD: (not queried in this build)");
            PatReleaseManifest.AssemblyIdentity identity =
                PatReleaseManifest.InspectCurrentAssembly();
            report.AppendLine("ASSEMBLY_PATH: " + identity.Path);
            report.AppendLine("ASSEMBLY_SHA256: " + identity.Sha256);
            report.AppendLine("ASSEMBLY_MVID: " + identity.Mvid);
            report.AppendLine("ASSEMBLY_VERSION: " + identity.InformationalVersion);
            report.AppendLine("REQUIRED: braces>=" + requiredBraces
                + " texts>=" + requiredTexts + " strict=" + (strict ? "true" : "false"));
            report.AppendLine("=====================================================");

            int braces = 0, bracesPassed = 0, bracesFailed = 0, bracesUnreadable = 0;
            int texts = 0, textsPassed = 0, textsFailed = 0, textsUnreadable = 0;
            int currentRequiredBraces = requiredBraces;
            int currentRequiredTexts = requiredTexts;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    Entity entity;
                    try { entity = tr.GetObject(id, OpenMode.ForRead) as Entity; }
                    catch (Exception ex)
                    {
                        report.AppendLine();
                        report.AppendLine("-----------------------------------------------------");
                        report.AppendLine("UNREADABLE OBJECT " + id);
                        report.AppendLine("  [FAIL] B0b object-readable: " + ex.Message);
                        bracesUnreadable++;
                        continue;
                    }
                    if (entity == null) continue;

                    Polyline polyline = entity as Polyline;
                    if (polyline != null && PatBraceEntity.IsBrace(polyline, tr))
                    {
                        braces++;
                        report.AppendLine();
                        report.AppendLine("-----------------------------------------------------");
                        report.AppendLine("LWPOLYLINE " + polyline.ObjectId);
                        PatBraceDefinition definition;
                        bool readable = false;
                        string definitionError = "no valid PATENTMARKER_BRACE Xrecord";
                        try
                        {
                            readable = PatBraceEntity.TryReadDefinition(polyline, tr, out definition);
                        }
                        catch (Exception ex)
                        {
                            definition = null;
                            definitionError = ex.Message;
                        }
                        if (!readable || definition == null)
                        {
                            report.AppendLine("  [FAIL] B0a definition-readable: " + definitionError);
                            report.AppendLine("  => FAIL");
                            bracesFailed++;
                            continue;
                        }
                        bool ok = PatBraceAssertions.Verify(polyline, definition, report);
                        report.AppendLine(ok ? "  => PASS" : "  => FAIL");
                        if (ok) bracesPassed++; else bracesFailed++;
                        continue;
                    }

                    // Product Leader + detached MText (primarily 2007, and any
                    // historical 2010+ fallback drawing).
                    Leader leader = entity as Leader;
                    if (leader != null && IO.PatEntityHelper.IsPatEntity(leader, tr))
                    {
                        texts++;
                        report.AppendLine();
                        report.AppendLine("-----------------------------------------------------");
                        report.AppendLine("LEADER " + leader.ObjectId);
                        MText leaderText = null;
                        try
                        {
                            ObjectId annotationId = PatLeaderTextAttachment.GetAnnotationId(leader, tr);
                            if (!annotationId.IsNull)
                                leaderText = tr.GetObject(annotationId, OpenMode.ForRead) as MText;
                        }
                        catch (Exception ex)
                        {
                            report.AppendLine("  [FAIL] T0 leader-text-readable: " + ex.Message);
                            textsUnreadable++;
                            continue;
                        }
                        if (leaderText == null)
                        {
                            report.AppendLine("  [FAIL] T0 leader-text-readable: linked MText missing");
                            textsUnreadable++;
                            continue;
                        }
                        if (VerifyText(leaderText, leader, tr, true, report))
                            textsPassed++;
                        else
                            textsFailed++;
                        continue;
                    }

                    // Standalone product MText (leader switch off).
                    MText standalone = entity as MText;
                    if (standalone != null && IO.PatEntityHelper.IsStandaloneText(standalone, tr))
                    {
                        texts++;
                        report.AppendLine();
                        report.AppendLine("-----------------------------------------------------");
                        report.AppendLine("MTEXT " + standalone.ObjectId + " (standalone)");
                        if (VerifyText(standalone, null, tr, false, report))
                            textsPassed++;
                        else
                            textsFailed++;
                        continue;
                    }

                    // Product MLeader, read without a static MLeader reference
                    // so the shared source remains loadable by AutoCAD 2007.
                    if (IO.PatEntityHelper.IsPatMLeader(entity, tr))
                    {
                        texts++;
                        report.AppendLine();
                        report.AppendLine("-----------------------------------------------------");
                        report.AppendLine("MLEADER " + entity.ObjectId);
                        MText mleaderText = TryReadMLeaderText(entity);
                        if (mleaderText == null)
                        {
                            report.AppendLine("  [FAIL] T0 mleader-text-readable: MText getter returned null");
                            textsUnreadable++;
                            continue;
                        }
                        if (VerifyText(mleaderText, entity, tr, true, report))
                            textsPassed++;
                        else
                            textsFailed++;
                    }
                }
                tr.Commit();
            }

            int assertionPass = CountOccurrences(report.ToString(), "[PASS]");
            int assertionFail = CountOccurrences(report.ToString(), "[FAIL]");
            int assertionNa = CountOccurrences(report.ToString(), "[N/A]");
            PatGeomVerifySummary.Result summary = PatGeomVerifySummary.Evaluate(
                braces, bracesPassed, bracesFailed, bracesUnreadable,
                texts, textsPassed, textsFailed, textsUnreadable,
                currentRequiredBraces, currentRequiredTexts, strict);

            report.AppendLine();
            report.AppendLine("-----------------------------------------------------");
            report.AppendLine("SUMMARY: " + summary.Detail);
            report.AppendLine("ASSERTIONS: pass=" + assertionPass + " fail=" + assertionFail
                + " n/a=" + assertionNa);
            report.AppendLine("COVERAGE: brace(B0-B9); text(T1-T2); leader-text(T3 when applicable)");
            report.AppendLine("NOTE: display, grips and other GUI behaviour are NOT covered.");
            report.AppendLine("RESULT: " + summary.Status);

            try
            {
                Directory.CreateDirectory(reportDir);
                string reportPath = Path.Combine(reportDir, "PatentMarker-geom-verify.txt");
                File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "PATGEOMVERIFY: {0}. Report: {1}\n", summary.Status, reportPath));
                PatentMarkerApp.RawLog("PATGEOMVERIFY run=" + runId
                    + " status=" + summary.Status + " report=" + reportPath);
            }
            catch (Exception ex)
            {
                ed.WriteMessage("PATGEOMVERIFY report write failed: " + ex.Message + "\n");
                PatentMarkerApp.RawLog("PATGEOMVERIFY run=" + runId
                    + " status=FAIL report-write-error=" + ex.Message);
            }
            // FAIL/SKIP stay in the report. verify-geometry.ps1 rejects them
            // (and missing/unwritable reports) even when CAD exits normally.
        }

        private static bool VerifyText(
            MText text, Entity leaderOrMLeader, Transaction tr,
            bool hasLeader, StringBuilder report)
        {
            bool ok = true;
            bool rotationReadable = false;
            double rotation = 0.0;
            try { rotation = text.Rotation; rotationReadable = true; } catch { }
            ok &= PatGeomTextAssertions.VerifyRotation(rotationReadable, rotation, report);

            bool directionReadable = false;
            Vector3d direction = new Vector3d(0, 0, 0);
            try { direction = text.Direction; directionReadable = true; } catch { }
            ok &= PatGeomTextAssertions.VerifyDirection(directionReadable, direction, report);

            if (!hasLeader)
            {
                report.AppendLine("  [N/A] T3 leader-text-retract: standalone text has no leader");
                return ok;
            }

            bool retractReadable = false;
            bool actualTextReadable = false;
            Point3d previous = new Point3d(0, 0, 0);
            Point3d textPoint = new Point3d(0, 0, 0);
            Point3d actualTextPoint = new Point3d(0, 0, 0);
            Point3d actualEndpoint = new Point3d(0, 0, 0);
            double textHeight = double.NaN;
            try { textHeight = text.TextHeight; } catch { }
            try { actualTextPoint = text.Location; actualTextReadable = true; } catch { }

            Leader leader = leaderOrMLeader as Leader;
            if (leader != null)
            {
                try
                {
                    if (actualTextReadable && leader.NumVertices >= 2)
                    {
                        previous = leader.VertexAt(leader.NumVertices - 2);
                        actualEndpoint = leader.VertexAt(leader.NumVertices - 1);
                        textPoint = actualTextPoint;
                        retractReadable = true;
                    }
                }
                catch { }
            }
            else if (leaderOrMLeader != null)
            {
                List<Point3d> chain;
                bool hasArrow, isSplined;
                if (IO.PatEntityHelper.TryReadMLeaderChain(
                    leaderOrMLeader, tr, out chain, out hasArrow, out isSplined)
                    && chain.Count >= 2)
                {
                    previous = chain[chain.Count - 2];
                    Point3d recordedTextPoint = chain[chain.Count - 1];
                    ok &= PatGeomTextAssertions.VerifyPointMatch(
                        actualTextReadable, "T3a recorded-text-anchor",
                        recordedTextPoint, actualTextPoint, textHeight, report);

                    Point3d mleaderTextLocation;
                    bool mleaderTextReadable = IO.PatEntityHelper.TryGetMLeaderTextLocation(
                        leaderOrMLeader, out mleaderTextLocation);
                    ok &= PatGeomTextAssertions.VerifyPointMatch(
                        mleaderTextReadable, "T3b mleader-text-location",
                        mleaderTextLocation, actualTextPoint, textHeight, report);

                    Point3d actualPrevious;
                    Point3d actualLastVertex;
                    bool actualPreviousReadable = IO.PatEntityHelper.TryReadMLeaderActualPreviousVertex(
                        leaderOrMLeader, out actualPrevious, out actualLastVertex);
                    ok &= PatGeomTextAssertions.VerifyPointMatch(
                        actualPreviousReadable, "T3c actual-previous-anchor",
                        previous, actualPrevious, textHeight, report);

                    if (actualTextReadable && actualPreviousReadable)
                    {
                        previous = actualPrevious;
                        textPoint = actualTextPoint;
                        retractReadable = IO.PatEntityHelper.TryGetMLeaderLastVertex(
                            leaderOrMLeader, out actualEndpoint);
                        ok &= PatGeomTextAssertions.VerifyPointMatch(
                            retractReadable, "T3d actual-endpoint-anchor",
                            actualLastVertex, actualEndpoint, textHeight, report);
                    }
                }
            }
            ok &= PatGeomTextAssertions.VerifyRetract(
                retractReadable, previous, textPoint, actualEndpoint, textHeight, report);
            return ok;

        }
        private static MText TryReadMLeaderText(Entity mleader)
        {
            try
            {
                PropertyInfo property = mleader.GetType().GetProperty("MText");
                if (property == null || !property.CanRead) return null;
                return property.GetValue(mleader, null) as MText;
            }
            catch { return null; }
        }


        private static int ReadRequiredCount(string name, int fallback)
        {
            string raw = Environment.GetEnvironmentVariable(name);
            int value;
            if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out value) && value >= 0)
                return value;
            return fallback;
        }

        private static bool ReadBooleanEnv(string name)
        {
            string raw = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrEmpty(raw)) return false;
            return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static int CountOccurrences(string text, string token)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token)) return 0;
            int count = 0, index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }
            return count;
        }
    }
}
