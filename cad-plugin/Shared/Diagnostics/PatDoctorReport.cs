using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PatentMarker.Diagnostics
{
    /// <summary>
    /// PATDOCTOR 自检结果模型与报告输出。
    /// 纯文本报告，无 JSON 依赖，.NET 2.0 即可运行。
    /// | Check model and plain-text report writer for PATDOCTOR.
    /// </summary>
    public static class PatDoctorReport
    {
        /// <summary>单项检查状态 | Status of a single check.</summary>
        public sealed class Check
        {
            public const string Pass = "PASS";
            public const string Fail = "FAIL";
            public const string Skip = "SKIP";

            private readonly string _name;
            private readonly string _status;
            private readonly string _detail;

            public Check(string name, string status, string detail)
            {
                _name = name ?? "";
                _status = status ?? Skip;
                _detail = detail ?? "";
            }

            public string Name { get { return _name; } }
            public string Status { get { return _status; } }
            public string Detail { get { return _detail; } }
        }

        /// <summary>
        /// 计算自检摘要行 | Build the one-line summary.
        /// </summary>
        public static string BuildSummary(List<Check> checks, List<PatDiagnostics.Entry> recentErrors)
        {
            int pass = 0, fail = 0, skip = 0;
            foreach (Check c in checks)
            {
                if (c.Status == Check.Pass) pass++;
                else if (c.Status == Check.Fail) fail++;
                else skip++;
            }
            return string.Format(
                "PASS {0} / FAIL {1} / SKIP {2} | recent errors: {3}",
                pass, fail, skip, recentErrors == null ? 0 : recentErrors.Count);
        }

        /// <summary>
        /// 生成完整报告并写盘 | Render the full report to disk. Returns the path written.
        /// </summary>
        public static string Write(string path, string drawingName,
            List<Check> checks, List<PatDiagnostics.Entry> recentErrors,
            string environmentBlock)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("# PatentMarker Doctor Report\n");
            sb.Append("\n- Generated: ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.Append("\n- Drawing: ").Append(drawingName ?? "");
            sb.Append("\n\n").Append(environmentBlock ?? "").Append("\n");

            sb.Append("\n## Checks\n\n");
            sb.Append("| # | Check | Result | Detail |\n");
            sb.Append("|---|-------|--------|--------|\n");
            for (int i = 0; i < checks.Count; i++)
            {
                sb.Append("| ").Append(i + 1)
                  .Append(" | ").Append(EscapeCell(checks[i].Name))
                  .Append(" | ").Append(checks[i].Status)
                  .Append(" | ").Append(EscapeCell(checks[i].Detail))
                  .Append(" |\n");
            }
            sb.Append("\nSummary: ").Append(BuildSummary(checks, recentErrors)).Append("\n");

            sb.Append("\n## Recent errors\n\n");
            if (recentErrors == null || recentErrors.Count == 0)
            {
                sb.Append("(none)\n");
            }
            else
            {
                foreach (PatDiagnostics.Entry e in recentErrors)
                {
                    sb.Append("- [").Append(e.Time.ToString("HH:mm:ss.fff"))
                      .Append("] [").Append(e.Source).Append("] ")
                      .Append(EscapeCell(e.Message)).Append("\n");
                }
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        private static string EscapeCell(string text)
        {
            if (text == null) return "";
            return text.Replace("|", "\\|").Replace("\r", " ").Replace("\n", "; ");
        }
    }
}
