using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace PatentOffice.Visio
{
    internal static class VisioDiagnostics
    {
        private static readonly object Gate = new object();
        private static string _runId;
        private static string _logPath;

        public static void Start(string hostVersion)
        {
            _runId = Guid.NewGuid().ToString("N");
            _logPath = null;
            Write("lifecycle", "PASS", "connected;run=" + _runId + ";product=" + ProductVersion() +
                ";host=" + Safe(hostVersion) + ";bits=" + (IntPtr.Size * 8) +
                ";assembly=" + Safe(AssemblyPath()));
        }

        public static void Write(string stage, string result, string details)
        {
            try
            {
                if (string.IsNullOrEmpty(_logPath))
                {
                    string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "PatentMarker", "Logs");
                    Directory.CreateDirectory(directory);
                    _logPath = Path.Combine(directory, "office-visio-" + DateTime.Now.ToString("yyyyMMdd") + ".tsv");
                }

                string line = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff") + "\t" + Safe(_runId) + "\t" +
                    Safe(stage) + "\t" + Safe(result) + "\t" + Safe(details) + "\r\n";
                lock (Gate)
                {
                    if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 2 * 1024 * 1024)
                    {
                        string archive = _logPath + "." + DateTime.Now.ToString("HHmmssfff") + ".bak";
                        File.Move(_logPath, archive);
                    }
                    File.AppendAllText(_logPath, line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Diagnostics must not change Visio document behavior.
            }
        }

        public static string AssemblyPath()
        {
            try { return Assembly.GetExecutingAssembly().Location; }
            catch { return "unknown"; }
        }

        private static string ProductVersion()
        {
            try { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
            catch { return "unknown"; }
        }

        private static string Safe(string value)
        {
            return (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
