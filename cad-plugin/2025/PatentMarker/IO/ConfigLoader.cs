using System.Text.Json;
using System.Text.Json.Serialization;
using PatentMarker.I18n;

namespace PatentMarker.IO
{
    /// <summary>
    /// 配置模型（从 config.json 读取）。2025 版用 System.Text.Json（内置，零依赖）。
    /// </summary>
    public class PatConfig
    {
        [JsonPropertyName("defaultDictPath")]
        public string DefaultDictPath { get; set; } = "";

        [JsonPropertyName("patStyle")]
        public PatStyleConfig PatStyle { get; set; } = new();

        [JsonPropertyName("align")]
        public AlignConfig Align { get; set; } = new();

        [JsonPropertyName("language")]
        public string LanguageStr { get; set; } = "chinese";

        [JsonIgnore]
        public Language Language =>
            LanguageStr?.Equals("english", StringComparison.OrdinalIgnoreCase) == true
                ? Language.English
                : Language.Chinese;
    }

    public class PatStyleConfig
    {
        [JsonPropertyName("textHeight")]
        public double TextHeight { get; set; } = 3.5;
    }

    public class AlignConfig
    {
        [JsonPropertyName("marginToFrame")]
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
        public static PatConfig? Current;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static PatConfig Load(string? path)
        {
            string? filePath = path;

            if (filePath is null)
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                if (doc is not null && !string.IsNullOrEmpty(doc.Name))
                {
                    string? dwgDir = Path.GetDirectoryName(doc.Name);
                    if (dwgDir is not null)
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

                if (filePath is null)
                {
                    string? dllDir = null;
                    try
                    {
                        dllDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    }
                    catch { }
                    if (dllDir is not null)
                    {
                        string dllCfg = Path.Combine(dllDir, "config.json");
                        if (File.Exists(dllCfg))
                            filePath = dllCfg;
                    }
                }
            }

            if (filePath is not null && File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    PatConfig? config = JsonSerializer.Deserialize<PatConfig>(json, JsonOpts);
                    config ??= new PatConfig();

                    Strings.Lang = config.Language;
                    return config;
                }
                catch (Exception ex)
                {
                    var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                    doc?.Editor.WriteMessage($"\nPatentMarker: config load error: {ex.Message}\n");
                }
            }

            return new PatConfig();
        }
    }
}
