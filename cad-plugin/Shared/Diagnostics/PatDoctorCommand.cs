using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using PatentMarker.I18n;
using PatentMarker.Styles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Exception = System.Exception;

namespace PatentMarker.Diagnostics
{
    /// <summary>
    /// PATDOCTOR（别名 BZD）：一键自检并生成诊断报告。
    /// 检查运行环境、日志目录、标注样式、设置、字典加载和图纸实体扫描，
    /// 汇总最近错误，报告写入 DLL 旁的 PatentMarker-doctor-report.txt。
    /// | One-command self check: environment, log dir, styles, settings,
    ///   dictionary load and a model-space scan, plus recent errors,
    ///   written to PatentMarker-doctor-report.txt next to the DLL.
    ///
    /// 该文件链接进全部 5 个版本工程，只使用各版本共有的 API 表面，
    /// 语法保持 .NET 2.0 / C# 3.0 兼容。
    /// </summary>
    public sealed class PatDoctorCommand
    {
        [CommandMethod("PATDOCTOR", CommandFlags.Modal)]
        [CommandMethod("BZD", CommandFlags.Modal)]
        public void Run()
        {
            Document doc = IO.RuntimeHost.ActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            try
            {
                ed.WriteMessage(Strings_Doctor_Running);
                List<PatDoctorReport.Check> checks = new List<PatDoctorReport.Check>();

                string assemblyPath = Assembly.GetExecutingAssembly().Location ?? "";
                string reportDir = GetReportDirectory(assemblyPath);
                string drawingName = doc.Name ?? "";

                string envBlock = BuildEnvironmentBlock(assemblyPath);

                checks.Add(CheckReportDirWritable(reportDir));
                RunStyleChecks(doc.Database, checks);
                checks.Add(CheckSettings());
                RunDictChecks(checks);
                checks.Add(ScanModelSpace(doc.Database));

                List<PatDiagnostics.Entry> recentErrors = PatDiagnostics.Snapshot();
                string summary = PatDoctorReport.BuildSummary(checks, recentErrors);

                string reportPath = Path.Combine(reportDir, "PatentMarker-doctor-report.txt");
                PatDoctorReport.Write(reportPath, drawingName, checks, recentErrors, envBlock);

                ed.WriteMessage(string.Format(Strings_Doctor_Summary, summary));
                ed.WriteMessage(string.Format(Strings_Doctor_ReportPath, reportPath));
                PatentMarkerApp.RawLog("PATDOCTOR run: checks=" + checks.Count
                    + ", buffered=" + recentErrors.Count + ", report: " + reportPath);
            }
            catch (Exception ex)
            {
                PatDiagnostics.RecordException("PATDOCTOR", ex);
                ed.WriteMessage(Strings.ErrorPrefix + ex.Message + "\n");
                PatentMarkerApp.RawLog("PATDOCTOR error: " + ex);
            }
        }

        // ── 自检项 | Checks ──────────────────────────────────────────

        private static PatDoctorReport.Check CheckReportDirWritable(string dir)
        {
            string probe = Path.Combine(dir, "PatentMarker-doctor-probe.tmp");
            try
            {
                File.WriteAllText(probe, "probe", System.Text.Encoding.UTF8);
                File.Delete(probe);
                return new PatDoctorReport.Check(
                    Strings_Doctor_CheckReportDir, PatDoctorReport.Check.Pass, dir);
            }
            catch (Exception ex)
            {
                return new PatDoctorReport.Check(
                    Strings_Doctor_CheckReportDir, PatDoctorReport.Check.Fail,
                    dir + " - " + ex.Message);
            }
        }

        private static void RunStyleChecks(Database db, List<PatDoctorReport.Check> checks)
        {
            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    checks.Add(CheckDimStyle(tr, db));
                    checks.Add(CheckTextStyle(tr, db));
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                PatDiagnostics.RecordException("PATDOCTOR.styles", ex);
                checks.Add(new PatDoctorReport.Check(
                    Strings_Doctor_CheckStyles, PatDoctorReport.Check.Fail, ex.Message));
            }
        }

        private static PatDoctorReport.Check CheckDimStyle(Transaction tr, Database db)
        {
            DimStyleTable table = (DimStyleTable)tr.GetObject(
                db.DimStyleTableId, OpenMode.ForRead);
            if (!table.Has(PatStyleInitializer.DimStyleName))
            {
                return new PatDoctorReport.Check(
                    Strings_Doctor_CheckDimStyle, PatDoctorReport.Check.Skip,
                    string.Format(Strings_Doctor_StyleMissing,
                        PatStyleInitializer.DimStyleName));
            }

            DimStyleTableRecord record = (DimStyleTableRecord)tr.GetObject(
                table[PatStyleInitializer.DimStyleName], OpenMode.ForRead);
            return new PatDoctorReport.Check(
                Strings_Doctor_CheckDimStyle, PatDoctorReport.Check.Pass,
                string.Format(Strings_Doctor_StyleValues,
                    record.Dimasz.ToString("0.###"), record.Dimtxt.ToString("0.###")));
        }

        private static PatDoctorReport.Check CheckTextStyle(Transaction tr, Database db)
        {
            TextStyleTable table = (TextStyleTable)tr.GetObject(
                db.TextStyleTableId, OpenMode.ForRead);
            bool present = table.Has(PatStyleInitializer.TextStyleName);
            return new PatDoctorReport.Check(
                Strings_Doctor_CheckTextStyle,
                present ? PatDoctorReport.Check.Pass : PatDoctorReport.Check.Skip,
                present
                    ? PatStyleInitializer.TextStyleName
                    : string.Format(Strings_Doctor_StyleMissing,
                        PatStyleInitializer.TextStyleName));
        }

        private static PatDoctorReport.Check CheckSettings()
        {
            try
            {
                IO.PatRuntimeSettings settings = IO.PatSettingsStore.Current;
                return new PatDoctorReport.Check(
                    Strings_Doctor_CheckSettings, PatDoctorReport.Check.Pass,
                    string.Format(Strings_Doctor_SettingsValues,
                        settings.ArrowSize.ToString("0.###"),
                        settings.TextHeight.ToString("0.###")));
            }
            catch (Exception ex)
            {
                PatDiagnostics.RecordException("PATDOCTOR.settings", ex);
                return new PatDoctorReport.Check(
                    Strings_Doctor_CheckSettings, PatDoctorReport.Check.Fail, ex.Message);
            }
        }

        private static void RunDictChecks(List<PatDoctorReport.Check> checks)
        {
            string dictPath = "";
            try
            {
                Palette.DictPaletteWorkflow workflow = new Palette.DictPaletteWorkflow();
                dictPath = workflow.ResolveDictPath() ?? "";
                if (dictPath.Length == 0)
                {
                    checks.Add(new PatDoctorReport.Check(
                        Strings_Doctor_CheckDict, PatDoctorReport.Check.Fail,
                        Strings_Doctor_DictNoPath));
                    return;
                }

                IO.DictModel model = workflow.LoadCurrent();
                int count = model != null && model.Entries != null ? model.Entries.Count : 0;
                string fileState = File.Exists(dictPath)
                    ? Strings_Doctor_DictFilePresent
                    : Strings_Doctor_DictFileMissing;
                checks.Add(new PatDoctorReport.Check(
                    Strings_Doctor_CheckDict,
                    count > 0 ? PatDoctorReport.Check.Pass : PatDoctorReport.Check.Skip,
                    string.Format(Strings_Doctor_DictValues, dictPath, count, fileState)));
            }
            catch (Exception ex)
            {
                PatDiagnostics.RecordException("PATDOCTOR.dict", ex);
                checks.Add(new PatDoctorReport.Check(
                    Strings_Doctor_CheckDict, PatDoctorReport.Check.Fail,
                    dictPath + " - " + ex.Message));
            }
        }

        private static PatDoctorReport.Check ScanModelSpace(Database db)
        {
            try
            {
                int total = 0, leaders = 0, mtexts = 0;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in btr)
                    {
                        total++;
                        Entity entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (entity == null) continue;
                        if (entity is Leader) leaders++;
                        else if (entity is MText) mtexts++;
                    }
                    tr.Commit();
                }
                return new PatDoctorReport.Check(
                    Strings_Doctor_CheckModel, PatDoctorReport.Check.Pass,
                    string.Format(Strings_Doctor_ModelValues, total, leaders, mtexts));
            }
            catch (Exception ex)
            {
                PatDiagnostics.RecordException("PATDOCTOR.model", ex);
                return new PatDoctorReport.Check(
                    Strings_Doctor_CheckModel, PatDoctorReport.Check.Fail, ex.Message);
            }
        }

        // ── 辅助 | Helpers ───────────────────────────────────────────

        private static string GetReportDirectory(string assemblyPath)
        {
            string dir = "";
            try
            {
                if (assemblyPath.Length > 0)
                    dir = Path.GetDirectoryName(assemblyPath) ?? "";
            }
            catch
            {
                dir = "";
            }
            if (dir.Length == 0)
                dir = Path.GetTempPath();
            return dir;
        }

        private static string BuildEnvironmentBlock(string assemblyPath)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("## Environment\n\n");
            sb.Append("- Assembly: ").Append(assemblyPath.Length > 0 ? assemblyPath : "(unknown)");
            sb.Append("\n- .NET Runtime: ").Append(Environment.Version.ToString());
            sb.Append("\n- BaseDirectory: ").Append(AppDomain.CurrentDomain.BaseDirectory);
            return sb.ToString();
        }

        // ── 文案 | Strings（随主语言切换；Phase 2 收敛后并入 Strings.cs） ──

        private static bool En
        {
            get { return Strings.Lang == Language.English; }
        }

        private static string Strings_Doctor_Running
        {
            get { return En ? "\nPatentMarker doctor: running checks...\n"
                            : "\nPatentMarker 体检：正在自检...\n"; }
        }

        private static string Strings_Doctor_Summary
        {
            get { return En ? "Doctor result: {0}\n" : "体检结果: {0}\n"; }
        }

        private static string Strings_Doctor_ReportPath
        {
            get { return En ? "Report written to: {0}\n" : "报告已写入: {0}\n"; }
        }

        private static string Strings_Doctor_CheckReportDir
        {
            get { return En ? "Report directory writable" : "报告目录可写"; }
        }

        private static string Strings_Doctor_CheckDimStyle
        {
            get { return En ? "PAT_DIM dimension style" : "PAT_DIM 标注样式"; }
        }

        private static string Strings_Doctor_CheckTextStyle
        {
            get { return En ? "TIMES_ROMAN text style" : "TIMES_ROMAN 文字样式"; }
        }

        private static string Strings_Doctor_CheckStyles
        {
            get { return En ? "Style checks" : "样式检查"; }
        }

        private static string Strings_Doctor_StyleMissing
        {
            get { return En ? "not created yet (created on first mark): {0}"
                            : "尚未创建（首次标注时自动创建）: {0}"; }
        }

        private static string Strings_Doctor_StyleValues
        {
            get { return En ? "arrow={0}, text height={1}" : "箭头={0}, 文字高度={1}"; }
        }

        private static string Strings_Doctor_CheckSettings
        {
            get { return En ? "Runtime settings" : "运行设置"; }
        }

        private static string Strings_Doctor_SettingsValues
        {
            get { return En ? "arrow size={0}, text height={1}" : "箭头大小={0}, 文字高度={1}"; }
        }

        private static string Strings_Doctor_CheckDict
        {
            get { return En ? "Dictionary (.dict.json)" : "字典 (.dict.json)"; }
        }

        private static string Strings_Doctor_DictNoPath
        {
            get { return En ? "no dictionary path resolved - save the Word doc or set the path in the palette"
                            : "未解析到字典路径 - 请保存 Word 文档或在面板中设置路径"; }
        }

        private static string Strings_Doctor_DictFilePresent
        {
            get { return En ? "file exists" : "文件存在"; }
        }

        private static string Strings_Doctor_DictFileMissing
        {
            get { return En ? "file missing" : "文件不存在"; }
        }

        private static string Strings_Doctor_DictValues
        {
            get { return En ? "{0} | {1} entries | {2}" : "{0} | {1} 条 | {2}"; }
        }

        private static string Strings_Doctor_CheckModel
        {
            get { return En ? "Model space scan" : "模型空间扫描"; }
        }

        private static string Strings_Doctor_ModelValues
        {
            get { return En ? "{0} entities, {1} Leaders, {2} MTexts"
                            : "{0} 个实体，{1} 条引线，{2} 个多行文字"; }
        }
    }
}
