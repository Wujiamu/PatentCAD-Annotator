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

        public static bool HasCache => _cachedModel != null;
        public static DictModel PreviousModel => _previousModel;

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
            string path = ResolveDictPath();
            if (path == null) return _cachedModel != null;
            if (!File.Exists(path)) return _cachedModel != null;
            if (_cachedPath == null || !_cachedPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                return true;
            try
            {
                DateTime wt = File.GetLastWriteTime(path);
                return wt != _cachedTime;
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
                if (_cachedModel != null && _cachedPath != null &&
                    _cachedPath.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                    wt == _cachedTime)
                {
                    return _cachedModel;
                }

                PatentMarkerApp.RawLog("Dict file changed, reloading: " + path);
                DictModel model = Load(path);
                if (model != null)
                {
                    if (_cachedModel != null)
                        _previousModel = _cachedModel;
                    _cachedModel = model;
                    _cachedPath = path;
                    _cachedTime = wt;
                    return model;
                }
                else
                {
                    if (_cachedModel != null && _cachedPath != null &&
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

        private static string ResolveDictPath()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
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
