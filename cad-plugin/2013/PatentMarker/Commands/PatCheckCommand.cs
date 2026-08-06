using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.I18n;
using System;
using System.Collections.Generic;
using System.IO;
using Exception = System.Exception;

namespace PatentMarker.Commands
{
    public class PatCheckCommand
    {
        [CommandMethod("PATCHECK", CommandFlags.Modal)]
        [CommandMethod("BZC", CommandFlags.Modal)]
        public void Run()
        {
            PatentMarkerApp.RawLog("=== PATCHECK START ===");
            var doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            IO.DictModel dict = IO.DictLoader.LoadForCurrentDrawing();
            if (dict == null)
            {
                ed.WriteMessage(Strings.PatCheck_NoDict);
                return;
}
            var drawingNumbers = new Dictionary<string, List<Point3d>>(
                IO.NumberIdentity.Comparer);
            int totalLeaders = 0;
            int patCount = 0;
            int textErrors = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(
                    db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId entId in btr)
                {
                    Entity ent = (Entity)tr.GetObject(entId, OpenMode.ForRead);
                    Leader leader = ent as Leader;
                    if (leader == null) continue;
                    totalLeaders++;
                    if (!IO.PatEntityHelper.IsPatEntity(leader, tr)) continue;
                    patCount++;

                    string number;
                    try
                    {
                        number = IO.PatEntityHelper.GetLeaderNumber(leader, tr);
                    }
                    catch (Exception ex)
                    {
                        textErrors++;
                        PatentMarkerApp.RawLog("Text access error: " + ex.Message);
                        continue;
                    }

                    if (number == null || number.Trim().Length == 0) continue;
                    number = IO.NumberIdentity.Normalize(number);
                    Point3d position = IO.PatEntityHelper.GetLeaderTextPos(leader, tr);
                    if (!drawingNumbers.ContainsKey(number))
                        drawingNumbers[number] = new List<Point3d>();
                    drawingNumbers[number].Add(position);
                }
                tr.Commit();
            }

            var dictNumbers = new Dictionary<string, bool>(
                IO.NumberIdentity.Comparer);
            foreach (IO.DictEntry entry in dict.Entries)
                dictNumbers[IO.NumberIdentity.Normalize(entry.Number)] = true;

            var drawingOnly = new List<string>();
            foreach (string number in drawingNumbers.Keys)
                if (!dictNumbers.ContainsKey(number)) drawingOnly.Add(number);
            drawingOnly.Sort();

            var dictOnly = new List<string>();
            foreach (string number in dictNumbers.Keys)
                if (!drawingNumbers.ContainsKey(number)) dictOnly.Add(number);
            dictOnly.Sort();

            var duplicates = new List<KeyValuePair<string, List<Point3d>>>();
            foreach (KeyValuePair<string, List<Point3d>> item in drawingNumbers)
                if (item.Value.Count > 1) duplicates.Add(item);
            duplicates.Sort(CompareByNumber);

            ed.WriteMessage(Strings.PatCheck_ReportTitle);
            ed.WriteMessage(string.Format(Strings.PatCheck_Summary,
                dict.Entries.Count, drawingNumbers.Count));
            ed.WriteMessage(string.Format(Strings.PatCheck_ScanStats,
                totalLeaders, patCount, textErrors));

            if (drawingOnly.Count > 0)
            {
                ed.WriteMessage(string.Format(
                    Strings.PatCheck_SectionDrawingOnly, drawingOnly.Count));
                foreach (string number in drawingOnly)
                {
                    Point3d position = drawingNumbers[number][0];
                    ed.WriteMessage("  #" + number + " at (" +
                        position.X.ToString("F2") + ", " +
                        position.Y.ToString("F2") + ")\n");
                }
            }

            if (dictOnly.Count > 0)
            {
                ed.WriteMessage(string.Format(
                    Strings.PatCheck_SectionDictOnly, dictOnly.Count));
                foreach (string number in dictOnly)
                    ed.WriteMessage("  #" + number + " (" +
                        FindEntryName(dict, number) + ")\n");
            }

            if (duplicates.Count > 0)
            {
                ed.WriteMessage(string.Format(
                    Strings.PatCheck_SectionDuplicates, duplicates.Count));
                foreach (KeyValuePair<string, List<Point3d>> item in duplicates)
                {
                    ed.WriteMessage(string.Format(
                        Strings.PatCheck_DuplicateDetail,
                        item.Key, item.Value.Count));
                    for (int i = 0; i < item.Value.Count; i++)
                    {
                        Point3d position = item.Value[i];
                        ed.WriteMessage("    " + (i + 1) + ". (" +
                            position.X.ToString("F2") + ", " +
                            position.Y.ToString("F2") + ")\n");
                    }
                }
            }

            if (drawingOnly.Count == 0 && dictOnly.Count == 0 &&
                duplicates.Count == 0)
            {
                ed.WriteMessage(Strings.PatCheck_AllMatch);
                PatentMarkerApp.RawLog("PATCHECK: ALL CLEAR");
            }
            else
            {
                int totalIssues = drawingOnly.Count + dictOnly.Count +
                    duplicates.Count;
                ed.WriteMessage(string.Format(
                    Strings.PatCheck_TotalIssues, totalIssues));
                PatentMarkerApp.RawLog("PATCHECK: " + totalIssues + " issues");
            }

            ed.WriteMessage("==========================================\n");
            SaveReport(doc, dict, drawingNumbers, drawingOnly, dictOnly,
                duplicates, ed);
            PatentMarkerApp.RawLog("=== PATCHECK END ===");
        }

        private static string FindEntryName(IO.DictModel dict, string number)
        {
            foreach (IO.DictEntry entry in dict.Entries)
                if (IO.NumberIdentity.AreEqual(entry.Number, number))
                    return entry.Name;
            return "?";
        }

        private static int CompareByNumber(
            KeyValuePair<string, List<Point3d>> left,
            KeyValuePair<string, List<Point3d>> right)
        {
            return left.Key.CompareTo(right.Key);
        }

        private void SaveReport(
            Document doc,
            IO.DictModel dict,
            Dictionary<string, List<Point3d>> drawingNumbers,
            List<string> drawingOnly,
            List<string> dictOnly,
            List<KeyValuePair<string, List<Point3d>>> duplicates,
            Editor ed)
        {
            try
            {
                var options = new PromptKeywordOptions(Strings.PatCheck_SavePrompt);
                options.Keywords.Add(Strings.PatCheck_KwYes);
                options.Keywords.Add(Strings.PatCheck_KwNo);
                options.Keywords.Default = Strings.PatCheck_KwNo;
                var result = ed.GetKeywords(options);
                if (result.Status != PromptStatus.OK ||
                    result.StringResult != Strings.PatCheck_KwYes)
                    return;

                string directory = Path.GetDirectoryName(doc.Name) ?? "";
                string baseName = Path.GetFileNameWithoutExtension(doc.Name);
                string reportPath = Path.Combine(directory, baseName + ".check.txt");

                using (StreamWriter writer = new StreamWriter(
                    reportPath, false, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine("PATCHECK Report - " +
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    writer.WriteLine("Dict: " + dict.Entries.Count +
                        " entries | Drawing: " + drawingNumbers.Count +
                        " Leaders (PAT_DIM)");
                    writer.WriteLine(new string('=', 60));

                    if (drawingOnly.Count > 0)
                    {
                        writer.WriteLine("\n--- Drawing has, dict missing (" +
                            drawingOnly.Count + ") ---");
                        foreach (string number in drawingOnly)
                        {
                            Point3d position = drawingNumbers[number][0];
                            writer.WriteLine("  #" + number + " at (" +
                                position.X.ToString("F2") + ", " +
                                position.Y.ToString("F2") + ")");
                        }
                    }

                    if (dictOnly.Count > 0)
                    {
                        writer.WriteLine("\n--- Dict has, drawing missing (" +
                            dictOnly.Count + ") ---");
                        foreach (string number in dictOnly)
                            writer.WriteLine("  #" + number + " (" +
                                FindEntryName(dict, number) + ")");
                    }

                    if (duplicates.Count > 0)
                    {
                        writer.WriteLine("\n--- Duplicates in drawing (" +
                            duplicates.Count + ") ---");
                        foreach (KeyValuePair<string, List<Point3d>> item in duplicates)
                        {
                            writer.WriteLine("  #" + item.Key + " appears " +
                                item.Value.Count + " times:");
                            for (int i = 0; i < item.Value.Count; i++)
                            {
                                Point3d position = item.Value[i];
                                writer.WriteLine("    " + (i + 1) + ". (" +
                                    position.X.ToString("F2") + ", " +
                                    position.Y.ToString("F2") + ")");
                            }
                        }
                    }

                    if (drawingOnly.Count == 0 && dictOnly.Count == 0 &&
                        duplicates.Count == 0)
                        writer.WriteLine(
                            "\n*** ALL CLEAR - Drawing and dict are consistent. ***");
                }

                ed.WriteMessage(string.Format(
                    Strings.PatCheck_ReportSaved, reportPath));
                PatentMarkerApp.RawLog("Report saved: " + reportPath);
            }
            catch (Exception ex)
            {
                PatentMarkerApp.RawLog("Report save error: " + ex.Message);
            }
        }
    }
}
