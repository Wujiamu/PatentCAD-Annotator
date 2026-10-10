using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace PatentOffice.Shared
{
    internal static class OfficeDiagnostics
    {
        private static readonly object Gate = new object();
        private static string _runId;
        private static string _logPath;
        private static string _logPrefix = "office-addin";

        public static string RunId
        {
            get { return _runId; }
        }

        public static string LogPath
        {
            get { return _logPath ?? ""; }
        }

        public static void Start(string hostVersion, string logPrefix)
        {
            _runId = Guid.NewGuid().ToString("N");
            _logPrefix = string.IsNullOrWhiteSpace(logPrefix) ? "office-addin" : Safe(logPrefix);
            _logPath = null;
            Write("lifecycle", "PASS", "connected;product=" + VersionText() + ";host=" +
                Safe(hostVersion) + ";bits=" + (IntPtr.Size * 8) + ";assembly=" + Safe(AssemblyPath()));
        }

        public static void Write(string stage, string result, string details)
        {
            try
            {
                EnsureLogPath();
                string line = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff") + "\t" +
                    Safe(_runId) + "\t" + Safe(stage) + "\t" + Safe(result) + "\t" + Safe(details) + "\r\n";
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
                // Diagnostics must not change host document behavior.
            }
        }

        public static string AssemblyPath()
        {
            try { return Assembly.GetExecutingAssembly().Location; }
            catch { return "unknown"; }
        }

        private static void EnsureLogPath()
        {
            if (!string.IsNullOrEmpty(_logPath)) return;
            string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string directory = Path.Combine(root, "PatentMarker", "Logs");
            Directory.CreateDirectory(directory);
            _logPath = Path.Combine(directory, _logPrefix + "-" + DateTime.Now.ToString("yyyyMMdd") + ".tsv");
        }

        private static string VersionText()
        {
            try { return Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
            catch { return "unknown"; }
        }

        private static string Safe(string value)
        {
            if (value == null) return "";
            return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
