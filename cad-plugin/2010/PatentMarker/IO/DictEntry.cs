using System;
using System.Collections.Generic;
using System.IO;

namespace PatentMarker.IO
{
    /// <summary>
    /// dict.json 反序列化模型（由 M0 Word VBA 提取器生成）。
    /// 2007 版用 SimpleJson 手动映射，无 Newtonsoft.Json 依赖。
    /// </summary>
    public class DictModel
    {
        public DictMetadata Metadata = new DictMetadata();
        public List<DictEntry> Entries = new List<DictEntry>();
        public List<string> Warnings = new List<string>();
    }

    public class DictMetadata
    {
        public string SourceFile = "";
        public string ExtractedAt = "";
        public string Version = "";
        // v4.0：CAD 端手动修改标记（Word 导出前检测此字段决定是否备份）
        public string ModifiedBy;
        // v4.0：CAD 端手动修改时间（yyyy-MM-ddTHH:mm:ss）
        public string ModifiedAt;
    }

    public class DictEntry
    {
        public string Number = "";
        public string Name = "";
        public int Occurrences;
        public List<ConflictInfo> Conflicts = new List<ConflictInfo>();
    }

    public class ConflictInfo
    {
        public string Number = "";
        public List<string> Candidates = new List<string>();
    }

    /// <summary>
    /// 加载并解析 dict.json（带时间戳缓存，文件变化时自动重载）。
    /// v2.2：文件变化时自动保留旧版到 _previousModel，供面板对比。
    /// 查找顺序：
    ///  1. 当前 DWG 同目录同主名 + ".dict.json"
    ///  2. config.json 中的 defaultDictPath
    ///  3. null（未加载字典）
    /// </summary>
    public static class DictLoader
    {
        private static DictModel _cachedModel;
        private static string _cachedPath;
        private static DateTime _cachedTime = DateTime.MinValue;
        // v2.2：上一次的字典快照（文件变化时由当前缓存转入）。null 表示无对比基线。
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

        public static bool HasCache { get { return _cachedModel != null; } }
        public static DictModel PreviousModel { get { return _previousModel; } }

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

        /// <summary>
        /// v2.2：清除对比基线（面板用户主动关闭对比模式时调用）。
        /// </summary>
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

        private static DictModel LoadForCurrentDrawingPerPath()
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
                    _timesByPath.TryGetValue(key, out cachedTime) && wt == cachedTime)
                {
                    Activate(path);
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
                    return model;
                }

                DictModel cachedModel;
                if (_modelsByPath.TryGetValue(key, out cachedModel))
                {
                    Activate(path);
                    return cachedModel;
                }
                ClearActive();
                return null;
            }
            catch (Exception ex)
            {
                PatentMarkerApp.RawLog("DictLoader timestamp check failed: " + ex.Message);
                return Load(path);
            }
        }

        public static DictModel LoadForCurrentDrawing()
        {
            DictModel result = LoadForCurrentDrawingPerPath();
            var doc = RuntimeHost.ActiveDocument;
            string drawingPath = doc != null ? doc.Name : null;
            string dictPath = ResolveDictPath();
            if (drawingPath != null && drawingPath.Length > 0 && dictPath != null)
                _dictPathByDrawing[CacheKey(drawingPath)] = CacheKey(dictPath);
            return result;
        }

        /// <summary>图纸关闭时释放该图纸关联的字典缓存和 Diff 基线。</summary>
        public static void ReleaseForDrawing(string drawingPath)
        {
            if (drawingPath == null || drawingPath.Length == 0) return;
            string drawingKey = CacheKey(drawingPath);
            string dictKey;
            if (!_dictPathByDrawing.TryGetValue(drawingKey, out dictKey)) return;
            _dictPathByDrawing.Remove(drawingKey);

            bool stillUsed = false;
            foreach (string value in _dictPathByDrawing.Values)
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(value, dictKey))
                {
                    stillUsed = true;
                    break;
                }
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
            if (doc != null && doc.Name != null && doc.Name.Length > 0)
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
            if (config != null && config.DefaultDictPath != null && config.DefaultDictPath.Length > 0)
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
                Dictionary<string, object> root = SimpleJson.ParseObject(json);
                DictModel dict = new DictModel();

                // Metadata
                Dictionary<string, object> meta = SimpleJson.GetObj(root, "metadata");
                if (meta != null)
                {
                    dict.Metadata.SourceFile = SimpleJson.GetStr(meta, "source_file");
                    dict.Metadata.ExtractedAt = SimpleJson.GetStr(meta, "extracted_at");
                    dict.Metadata.Version = SimpleJson.GetStr(meta, "version");
                    // v4.0：CAD 修改标记
                    dict.Metadata.ModifiedBy = SimpleJson.GetStr(meta, "modified_by");
                    dict.Metadata.ModifiedAt = SimpleJson.GetStr(meta, "modified_at");
                }

                // Entries
                List<object> entries = SimpleJson.GetArr(root, "entries");
                if (entries != null)
                {
                    foreach (object item in entries)
                    {
                        Dictionary<string, object> e = item as Dictionary<string, object>;
                        if (e == null) continue;

                        DictEntry entry = new DictEntry();
                        entry.Number = SimpleJson.GetStr(e, "number");
                        entry.Name = SimpleJson.GetStr(e, "name");
                        entry.Occurrences = SimpleJson.GetInt(e, "occurrences");

                        // Conflicts
                        List<object> conflicts = SimpleJson.GetArr(e, "conflicts");
                        if (conflicts != null)
                        {
                            foreach (object c in conflicts)
                            {
                                Dictionary<string, object> co = c as Dictionary<string, object>;
                                if (co == null) continue;
                                ConflictInfo ci = new ConflictInfo();
                                ci.Number = SimpleJson.GetStr(co, "number");
                                List<object> cands = SimpleJson.GetArr(co, "candidates");
                                if (cands != null)
                                {
                                    foreach (object cand in cands)
                                    {
                                        if (cand is string)
                                            ci.Candidates.Add((string)cand);
                                    }
                                }
                                entry.Conflicts.Add(ci);
                            }
                        }

                        dict.Entries.Add(entry);
                    }
                }

                // Warnings
                List<object> warnings = SimpleJson.GetArr(root, "warnings");
                if (warnings != null)
                {
                    foreach (object w in warnings)
                    {
                        if (w is string)
                            dict.Warnings.Add((string)w);
                    }
                }

                PatentMarkerApp.RawLog("DictLoader.Load OK: " + path + " -> " + dict.Entries.Count + " entries");
                return dict;
            }
            catch (Exception ex)
            {
                PatentMarkerApp.RawLog("DictLoader.Load FAILED: " + path + " -> " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }
    }
}
