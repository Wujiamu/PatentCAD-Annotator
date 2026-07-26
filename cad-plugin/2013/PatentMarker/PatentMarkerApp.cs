using Autodesk.AutoCAD.Runtime;
using System;
using System.IO;
using System.Reflection;

namespace PatentMarker
{
    /// <summary>
    /// 插件入口 — AutoCAD 2013/2014 (.NET 4.0) 版本。
    /// AutoCAD 在 NETLOAD 时调用 Initialize()。
    /// </summary>
    public class PatentMarkerApp : IExtensionApplication
    {
        public void Initialize()
        {
            RawLog("=== PatentMarker Initialize START ===");
            RawLog("NET Runtime: " + Environment.Version.ToString());
            RawLog("BaseDirectory: " + AppDomain.CurrentDomain.BaseDirectory);

            try
            {
                // 检查 AutoCAD DLL 加载状态
                string[] names = { "acdbmgd", "acmgd", "accoremgd" };
                foreach (string name in names)
                {
                    var found = Array.Find(
                        AppDomain.CurrentDomain.GetAssemblies(),
                        a => a.GetName().Name != null &&
                             a.GetName().Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (found != null)
                        RawLog("  Assembly '" + name + "': LOADED (" + found.GetName().Version + ")");
                    else
                        RawLog("  Assembly '" + name + "': NOT LOADED");
                }

                // 加载配置
                try
                {
                    RawLog("Loading config...");
                    var config = IO.ConfigLoader.Load(null);
                    if (config != null)
                    {
                        IO.ConfigLoader.Current = config;
                        RawLog("Config loaded: defaultDictPath='" + config.DefaultDictPath + "'");
                    }
                }
                catch (Exception ex)
                {
                    RawLog("Config load FAILED: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            catch (Exception ex)
            {
                RawLog("FATAL: " + ex.GetType().Name + ": " + ex.Message);
                RawLog("Stack: " + ex.StackTrace);
            }

            RawLog("=== PatentMarker Initialize END ===");
        }

        public void Terminate()
        {
            RawLog("PatentMarker Terminate()");
            try
            {
                Palette.PatPaletteCommand.DisposePalette();
            }
            catch (Exception ex)
            {
                RawLog("Terminate cleanup error: " + ex.Message);
            }
        }

        /// <summary>
        /// 健壮日志 — 写入 DLL 旁的 PatentMarker.log。
        /// </summary>
        internal static void RawLog(string msg)
        {
            try
            {
                string logDir;
                try
                {
                    logDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (string.IsNullOrEmpty(logDir))
                        logDir = Path.GetTempPath();
                }
                catch
                {
                    logDir = Path.GetTempPath();
                }

                string logPath = Path.Combine(logDir, "PatentMarker.log");
                string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + msg + "\r\n";
                File.AppendAllText(logPath, line, System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
