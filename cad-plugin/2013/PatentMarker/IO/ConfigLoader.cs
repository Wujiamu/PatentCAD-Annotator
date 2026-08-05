using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using PatentMarker.I18n;

namespace PatentMarker.IO
{
    /// <summary>
    /// 配置模型（从 config.json 读取）。2013 版用 Newtonsoft.Json。
    /// </summary>
    public class PatConfig
    {
        [JsonProperty("defaultDictPath")]
        public string DefaultDictPath { get; set; } = "";

        [JsonProperty("patStyle")]
        public PatStyleConfig PatStyle { get; set; } = new PatStyleConfig();

        [JsonProperty("align")]
        public AlignConfig Align { get; set; } = new AlignConfig();

        [JsonProperty("language")]
        public string LanguageStr { get; set; } = "chinese";

        [JsonIgnore]
        public Language Language
        {
            get { return LanguageStr?.ToLower() == "english" ? Language.English : Language.Chinese; }
        }
    }

    public class PatStyleConfig
    {
        [JsonProperty("textHeight")]
        public double TextHeight { get; set; } = 3.5;
    }

    public class AlignConfig
    {
        [JsonProperty("marginToFrame")]
        public double MarginToFrame { get; set; } = 5.0;
    }

    /// <summary>
    /// 加载 config.json。查找顺序：
    ///  1. DWG 目录下的 config.local.json
    ///  2. DWG 目录下的 config.json
    ///  3. DLL 目录下的 config.json
    ///  4. 内置默认值
    /// </summary>
    public static class ConfigLoader
    {
        public static PatConfig Current;
        private static readonly Dictionary<string, PatConfig> ConfigsByDrawing = new Dictionary<string, PatConfig>(StringComparer.OrdinalIgnoreCase);
        private static string ActiveDrawingKey;

        private static string DrawingKey(string drawingPath)
        {
            if (String.IsNullOrEmpty(drawingPath)) return "<default>";
            try { return Path.GetFullPath(drawingPath); }
            catch { return drawingPath; }
        }

        private static string ResolveConfigPathForDrawing(string drawingPath)
        {
            string filePath = null;
            if (!String.IsNullOrEmpty(drawingPath))
            {
                string dwgDir = Path.GetDirectoryName(drawingPath);
                if (dwgDir != null)
                {
                    string localCfg = Path.Combine(dwgDir, "config.local.json");
                    if (File.Exists(localCfg)) filePath = localCfg;
                    else
                    {
                        string dwgCfg = Path.Combine(dwgDir, "config.json");
                        if (File.Exists(dwgCfg)) filePath = dwgCfg;
                    }
                }
            }
            if (filePath == null)
            {
                string dllDir = null;
                try { dllDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location); }
                catch { }
                if (dllDir != null)
                {
                    string dllCfg = Path.Combine(dllDir, "config.json");
                    if (File.Exists(dllCfg)) filePath = dllCfg;
                }
            }
            return filePath;
        }

        /// <summary>按图纸显式激活配置，避免依赖上一次 MDI 活动文档。</summary>
        public static PatConfig ActivateForDrawing(string drawingPath)
        {
            string key = DrawingKey(drawingPath);
            string configPath = ResolveConfigPathForDrawing(drawingPath);
            PatConfig config = Load(configPath == null ? "" : configPath);
            if (config == null) config = new PatConfig();
            ConfigsByDrawing[key] = config;
            ActiveDrawingKey = key;
            Current = config;
            Strings.Lang = config.Language;
            return config;
        }

        /// <summary>图纸关闭时释放该图纸的配置快照。</summary>
        public static void ReleaseDrawing(string drawingPath)
        {
            string key = DrawingKey(drawingPath);
            ConfigsByDrawing.Remove(key);
            if (ActiveDrawingKey == key)
            {
                ActiveDrawingKey = null;
                Current = null;
                Strings.Lang = Language.Chinese;
            }
        }

        public static PatConfig Load(string path)
        {
            string filePath = path;

            if (filePath == null)
            {
                var doc = RuntimeHost.ActiveDocument;
                if (doc != null && !string.IsNullOrEmpty(doc.Name))
                {
                    string dwgDir = Path.GetDirectoryName(doc.Name);
                    if (dwgDir != null)
                    {
                        string localCfg = Path.Combine(dwgDir, "config.local.json");
                        if (File.Exists(localCfg))
                            filePath = localCfg;
                        else
                        {
                            string dwgCfg = Path.Combine(dwgDir, "config.json");
                            if (File.Exists(dwgCfg))
                                filePath = dwgCfg;
                        }
                    }
                }

                if (filePath == null)
                {
                    string dllDir = null;
                    try
                    {
                        dllDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    }
                    catch { }
                    if (dllDir != null)
                    {
                        string dllCfg = Path.Combine(dllDir, "config.json");
                        if (File.Exists(dllCfg))
                            filePath = dllCfg;
                    }
                }
            }

            if (filePath != null && File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    PatConfig config = JsonConvert.DeserializeObject<PatConfig>(json);
                    if (config == null) config = new PatConfig();

                    // 同步语言到全局 Strings
                    Strings.Lang = config.Language;

                    return config;
                }
                catch (Exception ex)
                {
                    var doc = RuntimeHost.ActiveDocument;
                    if (doc != null)
                        doc.Editor.WriteMessage("\nPatentMarker: config load error: " + ex.Message + "\n");
                }
            }

            return new PatConfig();
        }
    }
}
