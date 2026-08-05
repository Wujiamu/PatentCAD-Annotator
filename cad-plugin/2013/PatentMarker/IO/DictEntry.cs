using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace PatentMarker.IO
{
    /// <summary>
    /// dict.json 反序列化模型（由 Word VBA 提取器生成）。
    /// 2013 版用 Newtonsoft.Json。
    /// </summary>
    public class DictModel
    {
        [JsonProperty("metadata")]
        public DictMetadata Metadata { get; set; } = new DictMetadata();

        [JsonProperty("entries")]
        public List<DictEntry> Entries { get; set; } = new List<DictEntry>();

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; } = new List<string>();
    }

    public class DictMetadata
    {
        [JsonProperty("source_file")]
        public string SourceFile { get; set; } = "";

        [JsonProperty("extracted_at")]
        public string ExtractedAt { get; set; } = "";

        [JsonProperty("version")]
        public string Version { get; set; } = "";

        // v4.0：CAD 端手动修改标记（Word 导出前检测此字段决定是否备份）
        [JsonProperty("modified_by")]
        public string ModifiedBy { get; set; }

        // v4.0：CAD 端手动修改时间（yyyy-MM-ddTHH:mm:ss）
        [JsonProperty("modified_at")]
        public string ModifiedAt { get; set; }
    }

    public class DictEntry
    {
        [JsonProperty("number")]
        public string Number { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("occurrences")]
        public int Occurrences { get; set; }

        [JsonProperty("conflicts")]
        public List<ConflictInfo> Conflicts { get; set; } = new List<ConflictInfo>();
    }

    public class ConflictInfo
    {
        [JsonProperty("number")]
        public string Number { get; set; } = "";

        [JsonProperty("candidates")]
        public List<string> Candidates { get; set; } = new List<string>();
    }

    /// <summary>
    /// 加载并解析 dict.json（带时间戳缓存，文件变化时自动重载）。
    /// v2.2：文件变化时自动保留旧版到 _previousModel，供面板对比。
    /// </summary>
    public static class DictLoader
    {
        private static DictModel _cachedModel;
        private static string _cachedPath;
        private static DateTime _cachedTime = DateTime.MinValue;
        private static DictModel _previousModel;
        private static readonly Dictionary<string, DictModel> _modelsByPath = new Dictionary<string, DictModel>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DateTime> _timesByPath = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, DictModel> _previousByPath = new Dictionary<string, DictModel>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> _dictPathByDrawing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _activeKey;

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
            DictModel model;
            if (_modelsByPath.TryGetValue(key, out model))
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

        public static bool HasCache => _cachedModel != null;
        public static DictModel PreviousModel => _previousModel;

        public static void InvalidateCache()
        {
            if (_activeKey != null)
            {
                _modelsByPath.Remove(_activeKey);
                _timesByPath.Remove(_activeKey);
                _previousByPath.Remove(_activeKey);
            }
            ClearActive();
        }

        public static void ClearPrevious()
        {
            if (_activeKey != null)
                _previousByPath.Remove(_activeKey);
            _previousModel = null;
        }

        /// <summary>
        /// v4.0：当前已缓存字典的文件路径（无缓存时为 null）。
        /// 供写回 / 备份检测使用。
        /// </summary>
        public static string CurrentPath
        {
            get { return _cachedPath; }
        }

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
            string path = ResolveDictPath();
            if (path == null) return _cachedModel != null;
            if (!File.Exists(path)) return _cachedModel != null;
            string key = CacheKey(path);
            DateTime cachedTime;
            if (!_timesByPath.TryGetValue(key, out cachedTime))
                return true;
            try
            {
                DateTime wt = File.GetLastWriteTime(path);
                return wt != cachedTime;
            }
            catch
            {
                return true;
            }
        }

        public static DictModel LoadForCurrentDrawing()
        {
            string path = ResolveDictPath();
            if (path == null)
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
                DictModel cached;
                DateTime cachedTime;
                if (_modelsByPath.TryGetValue(key, out cached) &&
                    _timesByPath.TryGetValue(key, out cachedTime) &&
                    wt == cachedTime)
                {
                    Activate(path);
                    RegisterDrawingPath(path);
                    return cached;
                }

                PatentMarkerApp.RawLog("Dict file changed, reloading: " + path);
                DictModel model = Load(path);
                if (model != null)
                {
                    DictModel oldModel;
                    if (_modelsByPath.TryGetValue(key, out oldModel))
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
                    DictModel cachedModel;
                    if (_modelsByPath.TryGetValue(key, out cachedModel))
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
                DictModel fallback = Load(path);
                RegisterDrawingPath(path);
                return fallback;
            }
        }

        private static void RegisterDrawingPath(string dictPath)
        {
            var doc = RuntimeHost.ActiveDocument;
            if (doc != null && !String.IsNullOrEmpty(doc.Name) && dictPath != null)
                _dictPathByDrawing[CacheKey(doc.Name)] = CacheKey(dictPath);
        }

        /// <summary>图纸关闭时释放该图纸关联的字典缓存和 Diff 基线。</summary>
        public static void ReleaseForDrawing(string drawingPath)
        {
            if (String.IsNullOrEmpty(drawingPath)) return;
            string drawingKey = CacheKey(drawingPath);
            string dictKey;
            if (!_dictPathByDrawing.TryGetValue(drawingKey, out dictKey)) return;
            _dictPathByDrawing.Remove(drawingKey);
            bool stillUsed = false;
            foreach (string value in _dictPathByDrawing.Values)
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(value, dictKey)) { stillUsed = true; break; }
            }
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
        public static string ResolveDictPath()
        {
            var doc = RuntimeHost.ActiveDocument;
            if (doc != null && !string.IsNullOrEmpty(doc.Name))
            {
                string dwgDir = Path.GetDirectoryName(doc.Name);
                string dwgBase = Path.GetFileNameWithoutExtension(doc.Name);
                if (dwgDir != null && dwgBase != null)
                {
                    string coDict = Path.Combine(dwgDir, dwgBase + ".dict.json");
                    if (File.Exists(coDict))
                        return coDict;
                }
            }

            var config = ConfigLoader.Current;
            if (config != null && !string.IsNullOrEmpty(config.DefaultDictPath))
            {
                if (File.Exists(config.DefaultDictPath))
                    return config.DefaultDictPath;
            }

            return null;
        }

        public static DictModel Load(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                DictModel dict = JsonConvert.DeserializeObject<DictModel>(json);
                if (dict == null) dict = new DictModel();
                if (dict.Entries == null) dict.Entries = new List<DictEntry>();
                if (dict.Warnings == null) dict.Warnings = new List<string>();
                if (dict.Metadata == null) dict.Metadata = new DictMetadata();
                NormalizeNestedValues(dict);

                PatentMarkerApp.RawLog("DictLoader.Load OK: " + path + " -> " + dict.Entries.Count + " entries");
                return dict;
            }
            catch (Exception ex)
            {
                PatentMarkerApp.RawLog("DictLoader.Load FAILED: " + path + " -> " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static void NormalizeNestedValues(DictModel dict)
        {
            for (int i = dict.Entries.Count - 1; i >= 0; i--)
            {
                DictEntry entry = dict.Entries[i];
                if (entry == null)
                {
                    dict.Entries.RemoveAt(i);
                    continue;
                }
                if (entry.Number == null) entry.Number = "";
                if (entry.Name == null) entry.Name = "";
                if (entry.Conflicts == null) entry.Conflicts = new List<ConflictInfo>();
                for (int j = entry.Conflicts.Count - 1; j >= 0; j--)
                {
                    ConflictInfo conflict = entry.Conflicts[j];
                    if (conflict == null)
                    {
                        entry.Conflicts.RemoveAt(j);
                        continue;
                    }
                    if (conflict.Number == null) conflict.Number = "";
                    if (conflict.Candidates == null) conflict.Candidates = new List<string>();
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
        }
    }
}
