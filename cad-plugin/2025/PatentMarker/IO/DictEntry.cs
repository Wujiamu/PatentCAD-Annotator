using System.Text.Json;
using System.Text.Json.Serialization;

namespace PatentMarker.IO
{
    /// <summary>
    /// dict.json 反序列化模型（由 Word VBA 提取器生成）。
    /// 2025 版用 System.Text.Json（内置，零 NuGet 依赖）。
    /// </summary>
    public class DictModel
    {
        [JsonPropertyName("metadata")]
        public DictMetadata Metadata { get; set; } = new();

        [JsonPropertyName("entries")]
        public List<DictEntry> Entries { get; set; } = new();

        [JsonPropertyName("warnings")]
        public List<string> Warnings { get; set; } = new();
    }

    public class DictMetadata
    {
        [JsonPropertyName("source_file")]
        public string SourceFile { get; set; } = "";

        [JsonPropertyName("extracted_at")]
        public string ExtractedAt { get; set; } = "";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "";
    }

    public class DictEntry
    {
        [JsonPropertyName("number")]
        public string Number { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("occurrences")]
        public int Occurrences { get; set; }

        [JsonPropertyName("conflicts")]
        public List<ConflictInfo> Conflicts { get; set; } = new();
    }

    public class ConflictInfo
    {
        [JsonPropertyName("number")]
        public string Number { get; set; } = "";

        [JsonPropertyName("candidates")]
        public List<string> Candidates { get; set; } = new();
    }

    /// <summary>
    /// 加载并解析 dict.json（带时间戳缓存，文件变化时自动重载）。
    /// </summary>
    public static class DictLoader
    {
        private static DictModel? _cachedModel;
        private static string? _cachedPath;
        private static DateTime _cachedTime = DateTime.MinValue;
        private static DictModel? _previousModel;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        public static bool HasCache => _cachedModel is not null;
        public static DictModel? PreviousModel => _previousModel;

        public static void InvalidateCache()
        {
            _cachedModel = null;
            _cachedPath = null;
            _cachedTime = DateTime.MinValue;
            _previousModel = null;
        }

        public static void ClearPrevious()
        {
            _previousModel = null;
        }

        public static bool IsFileChanged()
        {
            string? path = ResolveDictPath();
            if (path is null) return _cachedModel is not null;
            if (!File.Exists(path)) return _cachedModel is not null;
            if (_cachedPath is null || !_cachedPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                return File.GetLastWriteTime(path) != _cachedTime;
            }
            catch
            {
                return true;
            }
        }

        public static DictModel? LoadForCurrentDrawing()
        {
            string? path = ResolveDictPath();
            if (path is null)
            {
                InvalidateCache();
                return null;
            }

            if (!File.Exists(path))
            {
                PatentMarkerApp.RawLog("Dict file not found at: " + path);
                InvalidateCache();
                return null;
            }

            try
            {
                DateTime wt = File.GetLastWriteTime(path);
                if (_cachedModel is not null && _cachedPath is not null &&
                    _cachedPath.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                    wt == _cachedTime)
                {
                    return _cachedModel;
                }

                PatentMarkerApp.RawLog("Dict file changed, reloading: " + path);
                DictModel? model = Load(path);
                if (model is not null)
                {
                    if (_cachedModel is not null)
                        _previousModel = _cachedModel;
                    _cachedModel = model;
                    _cachedPath = path;
                    _cachedTime = wt;
                    return model;
                }
                else
                {
                    if (_cachedModel is not null && _cachedPath is not null &&
                        _cachedPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                    {
                        return _cachedModel;
                    }
                    InvalidateCache();
                    return null;
                }
            }
            catch (Exception ex)
            {
                PatentMarkerApp.RawLog("DictLoader timestamp check failed: " + ex.Message);
                return Load(path);
            }
        }

        private static string? ResolveDictPath()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc is not null && !string.IsNullOrEmpty(doc.Name))
            {
                string? dwgDir = Path.GetDirectoryName(doc.Name);
                string? dwgBase = Path.GetFileNameWithoutExtension(doc.Name);
                if (dwgDir is not null && dwgBase is not null)
                {
                    string coDict = Path.Combine(dwgDir, dwgBase + ".dict.json");
                    if (File.Exists(coDict))
                        return coDict;
                }
            }

            var config = ConfigLoader.Current;
            if (config is not null && !string.IsNullOrEmpty(config.DefaultDictPath))
            {
                if (File.Exists(config.DefaultDictPath))
                    return config.DefaultDictPath;
            }

            return null;
        }

        public static DictModel? Load(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                DictModel? dict = JsonSerializer.Deserialize<DictModel>(json, JsonOpts);
                dict ??= new DictModel();

                PatentMarkerApp.RawLog($"DictLoader.Load OK: {path} -> {dict.Entries.Count} entries");
                return dict;
            }
            catch (Exception ex)
            {
                PatentMarkerApp.RawLog($"DictLoader.Load FAILED: {path} -> {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }
}
