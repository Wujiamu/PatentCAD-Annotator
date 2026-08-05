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

        // v4.0：CAD 端手动修改标记（Word 导出前检测此字段决定是否备份）
        [JsonPropertyName("modified_by")]
        public string? ModifiedBy { get; set; }

        // v4.0：CAD 端手动修改时间（yyyy-MM-ddTHH:mm:ss）
        [JsonPropertyName("modified_at")]
        public string? ModifiedAt { get; set; }
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
        private static readonly Dictionary<string, DictModel> _modelsByPath = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> _timesByPath = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DictModel> _previousByPath = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _dictPathByDrawing = new(StringComparer.OrdinalIgnoreCase);
        private static string? _activeKey;

        private static string CacheKey(string path)
        {
            try { return Path.GetFullPath(path); }
            catch { return path; }
        }

        private static void ClearActive()
        {
            _cachedModel = null;
            _cachedPath = null;
            _cachedTime = DateTime.MinValue;
            _previousModel = null;
            _activeKey = null;
        }

        private static void Activate(string path)
        {
            string key = CacheKey(path);
            _activeKey = key;
            _cachedPath = path;
            if (_modelsByPath.TryGetValue(key, out DictModel? model))
            {
                _cachedModel = model;
                _cachedTime = _timesByPath[key];
                _previousByPath.TryGetValue(key, out _previousModel);
            }
            else
            {
                _cachedModel = null;
                _cachedTime = DateTime.MinValue;
                _previousModel = null;
            }
        }

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
            if (_activeKey is not null)
            {
                _modelsByPath.Remove(_activeKey);
                _timesByPath.Remove(_activeKey);
                _previousByPath.Remove(_activeKey);
            }
            ClearActive();
        }

        public static void ClearPrevious()
        {
            if (_activeKey is not null)
                _previousByPath.Remove(_activeKey);
            _previousModel = null;
        }

        /// <summary>
        /// v4.0：当前已缓存字典的文件路径（无缓存时为 null）。
        /// 供写回 / 备份检测使用。
        /// </summary>
        public static string? CurrentPath => _cachedPath;

        /// <summary>
        /// v4.0：CAD 端写回 dict.json 后调用，同步缓存状态。
        /// 避免 2s 轮询把自身写入当作外部变更触发假 Diff 高亮；
        /// 同时清除对比基线（用户自己的修改不应被标成新增/变更）。
        /// </summary>
        public static void NotifySelfWrite(DictModel model, string path)
        {
            string key = CacheKey(path);
            DateTime writeTime;
            try { writeTime = File.GetLastWriteTime(path); }
            catch { writeTime = DateTime.Now; }
            _modelsByPath[key] = model;
            _timesByPath[key] = writeTime;
            _previousByPath.Remove(key);
            Activate(path);
        }

        public static bool IsFileChanged()
        {
            string? path = ResolveDictPath();
            if (path is null) return _cachedModel is not null;
            if (!File.Exists(path)) return _cachedModel is not null;
            string key = CacheKey(path);
            if (!_timesByPath.TryGetValue(key, out DateTime cachedTime))
                return true;
            try
            {
                return File.GetLastWriteTime(path) != cachedTime;
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
                ClearActive();
                return null;
            }

            if (!File.Exists(path))
            {
                PatentMarkerApp.RawLog("Dict file not found at: " + path);
                ClearActive();
                return null;
            }

            try
            {
                string key = CacheKey(path);
                DateTime wt = File.GetLastWriteTime(path);
                if (_modelsByPath.TryGetValue(key, out DictModel? cached) &&
                    _timesByPath.TryGetValue(key, out DateTime cachedTime) &&
                    wt == cachedTime)
                {
                    Activate(path);
                    RegisterDrawingPath(path);
                    return cached;
                }

                PatentMarkerApp.RawLog("Dict file changed, reloading: " + path);
                DictModel? model = Load(path);
                if (model is not null)
                {
                    if (_modelsByPath.TryGetValue(key, out DictModel? oldModel))
                        _previousByPath[key] = oldModel;
                    else
                        _previousByPath.Remove(key);
                    _modelsByPath[key] = model;
                    _timesByPath[key] = wt;
                    Activate(path);
                    RegisterDrawingPath(path);
                    return model;
                }
                else
                {
                    if (_modelsByPath.TryGetValue(key, out DictModel? cachedModel))
                    {
                        Activate(path);
                        RegisterDrawingPath(path);
                        return cachedModel;
                    }
                    ClearActive();
                    return null;
                }
            }
            catch (Exception ex)
            {
                PatentMarkerApp.RawLog("DictLoader timestamp check failed: " + ex.Message);
                DictModel? fallback = Load(path);
                RegisterDrawingPath(path);
                return fallback;
            }
        }

        private static void RegisterDrawingPath(string dictPath)
        {
            var doc = RuntimeHost.ActiveDocument;
            if (doc is not null && !string.IsNullOrEmpty(doc.Name) && dictPath is not null)
                _dictPathByDrawing[CacheKey(doc.Name)] = CacheKey(dictPath);
        }

        /// <summary>图纸关闭时释放该图纸关联的字典缓存和 Diff 基线。</summary>
        public static void ReleaseForDrawing(string drawingPath)
        {
            if (string.IsNullOrEmpty(drawingPath)) return;
            string drawingKey = CacheKey(drawingPath);
            if (!_dictPathByDrawing.TryGetValue(drawingKey, out string? dictKey)) return;
            _dictPathByDrawing.Remove(drawingKey);
            bool stillUsed = _dictPathByDrawing.Values.Any(v => StringComparer.OrdinalIgnoreCase.Equals(v, dictKey));
            if (!stillUsed)
            {
                _modelsByPath.Remove(dictKey);
                _timesByPath.Remove(dictKey);
                _previousByPath.Remove(dictKey);
            }
            if (_activeKey == dictKey) ClearActive();
        }

        /// <summary>
        /// v4.0：解析当前应使用的 dict.json 路径（原私有方法公开，供写回/备份使用）。
        /// </summary>
        public static string? ResolveDictPath()
        {
            var doc = RuntimeHost.ActiveDocument;
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

                // JSON 允许显式 null；将可选/损坏的集合归一化，避免面板和写回路径在后续操作中空引用。
                dict.Metadata ??= new DictMetadata();
                dict.Entries ??= new List<DictEntry>();
                dict.Warnings ??= new List<string>();
                for (int i = dict.Entries.Count - 1; i >= 0; i--)
                {
                    DictEntry? entry = dict.Entries[i];
                    if (entry == null)
                    {
                        dict.Entries.RemoveAt(i);
                        continue;
                    }

                    entry.Number ??= "";
                    entry.Name ??= "";
                    entry.Conflicts ??= new List<ConflictInfo>();
                    for (int j = entry.Conflicts.Count - 1; j >= 0; j--)
                    {
                        ConflictInfo? conflict = entry.Conflicts[j];
                        if (conflict == null)
                        {
                            entry.Conflicts.RemoveAt(j);
                            continue;
                        }
                        conflict.Number ??= "";
                        conflict.Candidates ??= new List<string>();
                        for (int k = conflict.Candidates.Count - 1; k >= 0; k--)
                        {
                            if (conflict.Candidates[k] == null)
                                conflict.Candidates.RemoveAt(k);
                        }
                    }
                }
                for (int i = dict.Warnings.Count - 1; i >= 0; i--)
                {
                    if (dict.Warnings[i] == null)
                        dict.Warnings.RemoveAt(i);
                }

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
