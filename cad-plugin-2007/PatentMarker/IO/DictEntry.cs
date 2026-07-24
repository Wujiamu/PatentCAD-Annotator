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

        public static bool HasCache { get { return _cachedModel != null; } }
        public static DictModel PreviousModel { get { return _previousModel; } }

        public static void InvalidateCache()
        {
            _cachedModel = null;
            _cachedPath = null;
            _cachedTime = DateTime.MinValue;
            _previousModel = null;
        }

        /// <summary>
        /// v2.2：清除对比基线（面板用户主动关闭对比模式时调用）。
        /// </summary>
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
                    // v2.2：保留旧版作为对比基线（仅当当前缓存非空时）
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
                        PatentMarkerApp.RawLog("Reload failed (file locked?), keeping previous cache");
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
