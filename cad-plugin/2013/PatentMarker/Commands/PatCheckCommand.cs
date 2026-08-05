using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.I18n;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Exception = System.Exception;

namespace PatentMarker.Commands
{
    /// <summary>
    /// PATCHECK — 双向一致性校验 — AutoCAD 2013/2014 版本。
    ///
    /// 扫描所有 PAT_STYLE MLeader 实体，与 dict.json 进行比对。
    /// </summary>
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

            var dict = IO.DictLoader.LoadForCurrentDrawing();
            if (dict == null)
            {
                ed.WriteMessage(Strings.PatCheck_NoDict);
                return;
            }
            PatentMarkerApp.RawLog("Dict loaded: " + dict.Entries.Count + " entries");

            // 收集图纸中的 PAT 编号
            var drawingNumbers = new Dictionary<string, List<Point3d>>(IO.NumberIdentity.Comparer);
            int totalMLeaders = 0;
            int patCount = 0;
            int textErrors = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId entId in btr)
                {
                    Entity ent = (Entity)tr.GetObject(entId, OpenMode.ForRead);
                    MLeader mleader = ent as MLeader;
                    if (mleader == null) continue;
                    totalMLeaders++;

                    if (!IO.PatEntityHelper.IsPatEntity(mleader, tr)) continue;
                    patCount++;

                    string number = null;
                    try
                    {
                        number = IO.PatEntityHelper.GetMLeaderNumber(mleader);
                    }
                    catch (Exception ex)
                    {
                        textErrors++;
                        PatentMarkerApp.RawLog("  Text access error: " + ex.Message);
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(number)) continue;
                    number = IO.NumberIdentity.Normalize(number);

                    Point3d pos = IO.PatEntityHelper.GetMLeaderTextPos(mleader);

                    if (!drawingNumbers.ContainsKey(number))
                        drawingNumbers[number] = new List<Point3d>();
                    drawingNumbers[number].Add(pos);
                }
                tr.Commit();
            }

            PatentMarkerApp.RawLog("Scan: total=" + totalMLeaders + ", pat=" + patCount +
                ", numbers=" + drawingNumbers.Count + ", errors=" + textErrors);

            // 构建字典编号集合
            var dictNumbers = new HashSet<string>(
                dict.Entries.Select(e => IO.NumberIdentity.Normalize(e.Number)),
                IO.NumberIdentity.Comparer);

            // 检查 1：图纸有但字典没有
            var drawingOnly = drawingNumbers.Keys.Where(n => !dictNumbers.Contains(n)).OrderBy(n => n).ToList();

            // 检查 2：字典有但图纸没有
            var dictOnly = dictNumbers.Where(n => !drawingNumbers.ContainsKey(n)).OrderBy(n => n).ToList();

            // 检查 3：图纸中重复
            var duplicates = drawingNumbers.Where(kv => kv.Value.Count > 1)
                .OrderBy(kv => kv.Key).ToList();

            // 输出结果
            ed.WriteMessage(Strings.PatCheck_ReportTitle);
            ed.WriteMessage(string.Format(Strings.PatCheck_Summary, dict.Entries.Count, drawingNumbers.Count));
            ed.WriteMessage(string.Format(Strings.PatCheck_ScanStats, totalMLeaders, patCount, textErrors));

            if (drawingOnly.Count > 0)
            {
                ed.WriteMessage(string.Format(Strings.PatCheck_SectionDrawingOnly, drawingOnly.Count));
                foreach (string num in drawingOnly)
                {
                    Point3d pos = drawingNumbers[num][0];
                    ed.WriteMessage("  #" + num + " at (" + pos.X.ToString("F2") + ", " + pos.Y.ToString("F2") + ")\n");
                }
            }

            if (dictOnly.Count > 0)
            {
                ed.WriteMessage(string.Format(Strings.PatCheck_SectionDictOnly, dictOnly.Count));
                foreach (string num in dictOnly)
                {
                    string name = dict.Entries
                        .FirstOrDefault(e => IO.NumberIdentity.AreEqual(e.Number, num))?.Name ?? "?";
                    ed.WriteMessage("  #" + num + " (" + name + ")\n");
                }
            }

            if (duplicates.Count > 0)
            {
                ed.WriteMessage(string.Format(Strings.PatCheck_SectionDuplicates, duplicates.Count));
                foreach (var kv in duplicates)
                {
                    ed.WriteMessage(string.Format(Strings.PatCheck_DuplicateDetail, kv.Key, kv.Value.Count));
                    for (int i = 0; i < kv.Value.Count; i++)
                    {
                        Point3d p = kv.Value[i];
                        ed.WriteMessage("    " + (i + 1) + ". (" + p.X.ToString("F2") + ", " + p.Y.ToString("F2") + ")\n");
                    }
                }
            }

            if (drawingOnly.Count == 0 && dictOnly.Count == 0 && duplicates.Count == 0)
            {
                ed.WriteMessage(Strings.PatCheck_AllMatch);
                PatentMarkerApp.RawLog("PATCHECK: ALL CLEAR");
            }
            else
            {
                int totalIssues = drawingOnly.Count + dictOnly.Count + duplicates.Count;
                ed.WriteMessage(string.Format(Strings.PatCheck_TotalIssues, totalIssues));
                PatentMarkerApp.RawLog("PATCHECK: " + totalIssues + " issues");
            }

            ed.WriteMessage("==========================================\n");

            SaveReport(doc, dict, drawingNumbers, drawingOnly, dictOnly, duplicates, ed);
            PatentMarkerApp.RawLog("=== PATCHECK END ===");
        }

        private void SaveReport(
            Document doc, IO.DictModel dict,
            Dictionary<string, List<Point3d>> drawingNumbers,
            List<string> drawingOnly, List<string> dictOnly,
            List<KeyValuePair<string, List<Point3d>>> duplicates,
            Editor ed)
        {
            try
            {
                var saveOpts = new PromptKeywordOptions(Strings.PatCheck_SavePrompt);
                saveOpts.Keywords.Add(Strings.PatCheck_KwYes);
                saveOpts.Keywords.Add(Strings.PatCheck_KwNo);
                saveOpts.Keywords.Default = Strings.PatCheck_KwNo;
                var saveResult = ed.GetKeywords(saveOpts);
                if (saveResult.Status != PromptStatus.OK || saveResult.StringResult != Strings.PatCheck_KwYes)
                    return;

                string dwgDir = Path.GetDirectoryName(doc.Name) ?? "";
                string dwgBase = Path.GetFileNameWithoutExtension(doc.Name);
                string reportPath = Path.Combine(dwgDir, dwgBase + ".check.txt");

                using (StreamWriter sw = new StreamWriter(reportPath, false, System.Text.Encoding.UTF8))
                {
                    sw.WriteLine("PATCHECK Report - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    sw.WriteLine("Dict: " + dict.Entries.Count + " entries | Drawing: " + drawingNumbers.Count + " MLeaders (PAT_STYLE)");
                    sw.WriteLine(new string('=', 60));

                    if (drawingOnly.Count > 0)
                    {
                        sw.WriteLine("\n--- Drawing has, dict missing (" + drawingOnly.Count + ") ---");
                        foreach (string num in drawingOnly)
                        {
                            Point3d pos = drawingNumbers[num][0];
                            sw.WriteLine("  #" + num + " at (" + pos.X.ToString("F2") + ", " + pos.Y.ToString("F2") + ")");
                        }
                    }
                    if (dictOnly.Count > 0)
                    {
                        sw.WriteLine("\n--- Dict has, drawing missing (" + dictOnly.Count + ") ---");
                        foreach (string num in dictOnly)
                        {
                            string name = dict.Entries
                                .FirstOrDefault(e => IO.NumberIdentity.AreEqual(e.Number, num))?.Name ?? "?";
                            sw.WriteLine("  #" + num + " (" + name + ")");
                        }
                    }
                    if (duplicates.Count > 0)
                    {
                        sw.WriteLine("\n--- Duplicates in drawing (" + duplicates.Count + ") ---");
                        foreach (var kv in duplicates)
                        {
                            sw.WriteLine("  #" + kv.Key + " appears " + kv.Value.Count + " times:");
                            for (int i = 0; i < kv.Value.Count; i++)
                            {
                                Point3d p = kv.Value[i];
                                sw.WriteLine("    " + (i + 1) + ". (" + p.X.ToString("F2") + ", " + p.Y.ToString("F2") + ")");
                            }
                        }
                    }
                    if (drawingOnly.Count == 0 && dictOnly.Count == 0 && duplicates.Count == 0)
                    {
                        sw.WriteLine("\n*** ALL CLEAR - Drawing and dict are consistent. ***");
                    }
                }

                ed.WriteMessage(string.Format(Strings.PatCheck_ReportSaved, reportPath));
                PatentMarkerApp.RawLog("Report saved: " + reportPath);
            }
            catch (Exception ex)
            {
                PatentMarkerApp.RawLog("Report save error: " + ex.Message);
            }
        }
    }
}
