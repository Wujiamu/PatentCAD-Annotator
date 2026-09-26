using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace PatentOffice.PowerPoint
{
    internal static class OfficeDiagnostics
    {
        private static readonly object Gate = new object();
        private static string _runId;
        private static string _logPath;

        public static string RunId
        {
            get { return _runId; }
        }

        public static void Start(string hostVersion)
        {
            _runId = Guid.NewGuid().ToString("N");
            try
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string directory = Path.Combine(root, "PatentMarker", "Logs");
                Directory.CreateDirectory(directory);
                _logPath = Path.Combine(directory, "office-ppt-" + DateTime.Now.ToString("yyyyMMdd") + ".tsv");
            }
            catch { _logPath = null; }
            Write("lifecycle", "connected", "run=" + _runId + ";product=" + VersionText() + ";host=" + Safe(hostVersion) + ";bits=" + (IntPtr.Size * 8));
        }

        public static void Write(string stage, string result, string details)
        {
            try
            {
                if (string.IsNullOrEmpty(_logPath))
                {
                    string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string directory = Path.Combine(root, "PatentMarker", "Logs");
                    Directory.CreateDirectory(directory);
                    _logPath = Path.Combine(directory, "office-ppt-" + DateTime.Now.ToString("yyyyMMdd") + ".tsv");
                }

                string line = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff") + "\t" +
                    Safe(_runId) + "\t" + Safe(stage) + "\t" + Safe(result) + "\t" + Safe(details) + "\r\n";
                lock (Gate)
                {
                    if (File.Exists(_logPath) && new FileInfo(_logPath).Length > 2 * 1024 * 1024)
                    {
                        string archived = _logPath + "." + DateTime.Now.ToString("HHmmss") + ".bak";
                        File.Move(_logPath, archived);
                    }
                    File.AppendAllText(_logPath, line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Diagnostics must never change add-in behavior.
            }
        }

        public static string LogPath
        {
            get { return _logPath ?? ""; }
        }

        public static string AssemblyPath()
        {
            try { return Assembly.GetExecutingAssembly().Location; }
            catch { return "unknown"; }
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
