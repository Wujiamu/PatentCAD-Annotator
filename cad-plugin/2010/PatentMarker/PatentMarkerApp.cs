using Autodesk.AutoCAD.Runtime;
using System;
using System.IO;
using System.Reflection;
using Exception = System.Exception;

namespace PatentMarker
{
    /// <summary>
    /// 插件入口 — AutoCAD 2010/2011/2012 (.NET 3.5) 版本。
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
                // 检查 AutoCAD DLL 加载状态（手动循环，无 LINQ）
                string[] names = new string[] { "acdbmgd", "acmgd" };
                foreach (string name in names)
                {
                    Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                    Assembly found = null;
                    foreach (Assembly a in asms)
                    {
                        if (a.GetName().Name != null &&
                            a.GetName().Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            found = a;
                            break;
                        }
                    }
                    if (found != null)
                        RawLog("  Assembly '" + name + "': LOADED (" + found.GetName().Version.ToString() + ")");
                    else
                        RawLog("  Assembly '" + name + "': NOT LOADED");
                }

                // 修复 D3：Initialize 中不再创建样式（NETLOAD 时可能无活动文档）
                // 样式在首次命令执行时懒创建

                // 加载配置
                try
                {
                    RawLog("Loading config...");
                    Autodesk.AutoCAD.ApplicationServices.Document activeDocument =
                        IO.RuntimeHost.ActiveDocument;
                    IO.PatSettingsStore.Activate(activeDocument != null ? activeDocument.Name : "");
                    IO.PatSettingsStore.ResetConfigDefaults();
                    var config = IO.ConfigLoader.ActivateForDrawing(activeDocument != null ? activeDocument.Name : "");
                    if (config != null)
                    {
                        IO.ConfigLoader.Current = config;
                        IO.PatSettingsStore.Apply(config);
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
        /// 健壮日志 — 写入 DLL 旁的 PatentMarker.log（或 temp 目录回退）。
        /// </summary>
        internal static void RawLog(string msg)
        {
            try
            {
                string logDir;
                try
                {
                    logDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (logDir == null || logDir.Length == 0)
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
