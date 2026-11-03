using System;using System.IO;using System.Collections.Generic;using System.Text;using System.Text.RegularExpressions;using PatentMarker.IO;using PatentMarker.I18n;
namespace PatentMarker {public static class PatentMarkerApp{public static void RawLog(string s){}}}
namespace PatentMarker.I18n {public static class Strings{public const string Status_DictLoaded="loaded",Status_Ready="ready",Conflict_StatusDetected="pending";}}
namespace PatentMarker.IO {
 public class HostDoc{public string Name;}
 public static class RuntimeHost{public static HostDoc ActiveDocument;}
 public class PatConfig{public string DefaultDictPath;}
 public static class ConfigLoader{public static PatConfig Current;}
 public static class DictFileVisibility{public const string MissingIdentity="<missing>",ChangedError="PATENTMARKER_CONTENT_CHANGED";public static string Identity(byte[] bytes)
        {
            using (System.Security.Cryptography.SHA256 hash = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }
public static string ReadIdentity(string path)
        {
            try { return Identity(File.ReadAllBytes(path)); }
            catch (FileNotFoundException) { return MissingIdentity; }
            catch (DirectoryNotFoundException) { return MissingIdentity; }
        }
public static string DecodeText(byte[] bytes)
        {
            int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return new System.Text.UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        } }
 public static class DictConflict{public const string BackupInfix=".word-";public delegate bool ContextCheck();public static string FindWordBackup(string dictPath)
        {
            if (string.IsNullOrEmpty(dictPath)) return null;
            string dir = Path.GetDirectoryName(dictPath);
            if (dir == null || !Directory.Exists(dir)) return null;
            string prefix = Path.GetFileName(dictPath) + BackupInfix;

            string latest = null;
            foreach (string f in Directory.GetFiles(dir, "*.bak"))
            {
                string name = Path.GetFileName(f);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                // 校验时间戳段基本形态（避免误匹配其他 .bak）
                string ts = name.Substring(prefix.Length);
                if (!Regex.IsMatch(ts, @"^\d{8}-\d{6}\.bak$", RegexOptions.IgnoreCase)) continue;
                if (latest == null ||
                    string.Compare(name, Path.GetFileName(latest), StringComparison.Ordinal) > 0)
                    latest = f;
            }
            return latest;
        }
public static bool IsPendingConflict(DictModel current, string dictPath)
        {
            return !string.IsNullOrEmpty(dictPath) && FindWordBackup(dictPath) != null;
        }
private static bool IsSelectedBackup(string dictPath, string backupPath)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(dictPath));
            string backupDirectory = Path.GetDirectoryName(Path.GetFullPath(backupPath));
            if (!string.Equals(directory, backupDirectory, StringComparison.OrdinalIgnoreCase)) return false;
            string name = Path.GetFileName(backupPath);
            string prefix = Path.GetFileName(dictPath) + BackupInfix;
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(name.Substring(prefix.Length), @"^\d{8}-\d{6}\.bak$", RegexOptions.IgnoreCase);
        }
private static bool ReadSelected(string dictPath, string identity, string backupPath, string backupIdentity,
            ContextCheck isCurrent, out byte[] current, out byte[] backup, out string error)
        {
            current = null; backup = null; error = "";
            if (string.IsNullOrEmpty(dictPath) || string.IsNullOrEmpty(backupPath) ||
                string.IsNullOrEmpty(identity) || string.IsNullOrEmpty(backupIdentity) ||
                isCurrent == null || !isCurrent() || !IsSelectedBackup(dictPath, backupPath))
            {
                error = DictFileVisibility.ChangedError;
                return false;
            }
            try
            {
                current = File.ReadAllBytes(dictPath);
                backup = File.ReadAllBytes(backupPath);
            }
            catch (FileNotFoundException) { error = DictFileVisibility.ChangedError; return false; }
            catch (DirectoryNotFoundException) { error = DictFileVisibility.ChangedError; return false; }
            if (!string.Equals(identity, DictFileVisibility.Identity(current), StringComparison.Ordinal) ||
                !string.Equals(backupIdentity, DictFileVisibility.Identity(backup), StringComparison.Ordinal))
            {
                error = DictFileVisibility.ChangedError;
                return false;
            }
            return true;
        }  public static bool CheckSelected(string path,string id,string backup,string backupId,out string error){byte[] a,b;return ReadSelected(path,id,backup,backupId,delegate{return true;},out a,out b,out error);}
 }
}
namespace PatentMarker.Palette {
 public class LabelStub{public string Text="ready";}
 public class ButtonStub{public bool Enabled;}
 public class WorkflowStub{public string ResolveDictPath(){return DictLoader.ResolveDictPath();}public bool IsPendingConflict(DictModel m,string p){return DictConflict.IsPendingConflict(m,p);}}
 public class RecoveryProbe{
  private DictModel _currentDict;private ButtonStub _btnArbitrate=new ButtonStub();private LabelStub _lblStatus=new LabelStub();private WorkflowStub _workflow=new WorkflowStub();private void UpdateConflictState()
        {
            if (_btnArbitrate == null) return;
            string dictPath = _workflow.ResolveDictPath();
            bool pending = _currentDict != null && dictPath != null &&
                _workflow.IsPendingConflict(_currentDict, dictPath);
            _btnArbitrate.Enabled = pending;
            if (pending && _lblStatus != null &&
                (_lblStatus.Text == Strings.Status_DictLoaded ||
                 _lblStatus.Text == Strings.Status_Ready))
                _lblStatus.Text = Strings.Conflict_StatusDetected;
        }  public static string[] Run(string folder){var lines=new List<string>();string path=Path.Combine(folder,"sample.dict.json");string backup=path+".word-20261101-222222.bak";
   string a="{\"entries\":[{\"number\":\"301\",\"name\":\"old\"}]}";
   File.WriteAllText(path,a,new UTF8Encoding(false));File.WriteAllText(backup,a,new UTF8Encoding(false));ConfigLoader.Current=new PatConfig{DefaultDictPath=path};DictLoader.InvalidateCache();
   var original=DictLoader.LoadForCurrentDrawing();string cachedId=DictLoader.GetIdentity(original,path),backupId=DictFileVisibility.ReadIdentity(backup),error;
   bool initial=DictConflict.CheckSelected(path,cachedId,backup,backupId,out error);lines.Add((initial?"PASS":"FAIL")+"|recovery_valid_control|selected_valid="+initial);
   File.WriteAllText(path,"{ broken-json",new UTF8Encoding(false));var retained=DictLoader.LoadForCurrentDrawing();
   string paletteId=DictLoader.GetIdentity(retained,path),diskId=DictFileVisibility.ReadIdentity(path);
   bool oldPick=DictConflict.CheckSelected(path,paletteId,backup,backupId,out error);
   lines.Add((oldPick?"PASS":"FAIL")+"|recovery_after_parse_error|valid_backup="+(DictLoader.Load(backup)!=null)+"|retained_old_model="+Object.ReferenceEquals(original,retained)+"|palette_snapshot_matches_disk="+(paletteId==diskId)+"|recovery_allowed="+oldPick+"|error="+error);
   bool rawPick=DictConflict.CheckSelected(path,diskId,backup,backupId,out error);lines.Add((rawPick?"PASS":"FAIL")+"|recovery_raw_current_snapshot_control|selected_valid="+rawPick);
   DictLoader.InvalidateCache();var cold=DictLoader.LoadForCurrentDrawing();var p=new RecoveryProbe();p._currentDict=cold;p.UpdateConflictState();
   bool enabled=p._btnArbitrate.Enabled;lines.Add((enabled?"PASS":"FAIL")+"|recovery_after_explicit_reload|current_model_null="+(cold==null)+"|valid_backup="+(DictLoader.Load(backup)!=null)+"|button_enabled="+enabled);
   ConfigLoader.Current=null;DictLoader.InvalidateCache();return lines.ToArray();
  }
 }
}
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
        private static readonly Dictionary<string, string> _identitiesByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
                _identitiesByPath.Remove(_activeKey);
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
        public static string GetIdentity(DictModel model, string path)
        {
            if (model == null || string.IsNullOrEmpty(path)) return null;
            string key = CacheKey(path);
            DictModel cached;
            string identity;
            if (!_modelsByPath.TryGetValue(key, out cached) || !object.ReferenceEquals(cached, model) ||
                !_identitiesByPath.TryGetValue(key, out identity)) return null;
            return identity;
        }

        public static void NotifySelfWrite(DictModel model, string path, string identity)
        {
            string key = CacheKey(path);
            DateTime writeTime;
            try { writeTime = File.GetLastWriteTime(path); }
            catch { writeTime = DateTime.Now; }
            _modelsByPath[key] = model;
            _timesByPath[key] = writeTime;
            _identitiesByPath[key] = identity;
            _previousByPath.Remove(key);
            Activate(path);
        }

        public static bool IsFileChanged()
        {
            string path = ResolveDictPath();
            if (path == null || !File.Exists(path)) return _cachedModel != null;
            string identity;
            if (!_identitiesByPath.TryGetValue(CacheKey(path), out identity)) return true;
            try { return !string.Equals(identity, DictFileVisibility.ReadIdentity(path), StringComparison.Ordinal); }
            catch { return true; }
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
                byte[] bytes = File.ReadAllBytes(path);
                string identity = DictFileVisibility.Identity(bytes);
                DateTime wt = File.GetLastWriteTime(path);
                DictModel cached;
                string cachedIdentity;
                if (_modelsByPath.TryGetValue(key, out cached) &&
                    _identitiesByPath.TryGetValue(key, out cachedIdentity) &&
                    string.Equals(identity, cachedIdentity, StringComparison.Ordinal))
                {
                    Activate(path);
                    return cached;
                }

                PatentMarkerApp.RawLog("Dict file changed, reloading: " + path);
                DictModel model = LoadBytes(path, bytes);
                if (model != null)
                {
                    DictModel oldModel;
                    if (_modelsByPath.TryGetValue(key, out oldModel))
                        _previousByPath[key] = oldModel;
                    else
                        _previousByPath.Remove(key);
                    _modelsByPath[key] = model;
                    _timesByPath[key] = wt;
                    _identitiesByPath[key] = identity;
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
                PatentMarkerApp.RawLog("DictLoader content check failed: " + ex.Message);
                DictModel cachedModel;
                if (_modelsByPath.TryGetValue(CacheKey(path), out cachedModel))
                {
                    Activate(path);
                    return cachedModel;
                }
                ClearActive();
                return null;
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
                _identitiesByPath.Remove(dictKey);
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
            return LoadBytes(path, null);
        }

        internal static DictModel LoadBytes(string path, byte[] bytes)
        {
            try
            {
                string json = DictFileVisibility.DecodeText(bytes ?? File.ReadAllBytes(path));
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


namespace PatentMarker.IO
{
    /// <summary>
    /// 极简 JSON 解析器 — 仅支持 dict.json / config.json 所需的子集。
    /// 返回 Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null
    /// 替代 Newtonsoft.Json，实现零外部依赖部署。
    /// </summary>
    internal static class SimpleJson
    {
        // ===== 公开入口 =====

        public static object Parse(string json)
        {
            if (json == null) throw new FormatException("JSON input is null.");
            int pos = 0;
            object value = ParseValue(json, ref pos);
            SkipWhitespace(json, ref pos);
            if (pos != json.Length) throw InvalidJson(pos);
            return value;
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            Dictionary<string, object> result = Parse(json) as Dictionary<string, object>;
            if (result == null) throw new FormatException("JSON root must be an object.");
            return result;
        }

        // ===== 辅助取值器 =====

        public static string GetStr(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return "";
            if (obj.ContainsKey(key) && obj[key] is string)
                return (string)obj[key];
            return "";
        }

        public static int GetInt(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return 0;
            if (obj.ContainsKey(key) && obj[key] is double)
                return (int)(double)obj[key];
            return 0;
        }

        public static double GetDouble(Dictionary<string, object> obj, string key, double def)
        {
            if (obj == null) return def;
            if (obj.ContainsKey(key) && obj[key] is double)
                return (double)obj[key];
            return def;
        }

        public static Dictionary<string, object> GetObj(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return null;
            if (obj.ContainsKey(key) && obj[key] is Dictionary<string, object>)
                return (Dictionary<string, object>)obj[key];
            return null;
        }

        public static List<object> GetArr(Dictionary<string, object> obj, string key)
        {
            if (obj == null) return null;
            if (obj.ContainsKey(key) && obj[key] is List<object>)
                return (List<object>)obj[key];
            return null;
        }

        // ===== 内部解析逻辑 =====

        private static FormatException InvalidJson(int pos)
        {
            return new FormatException("Invalid JSON at position " + pos + ".");
        }

        private static void Expect(string json, ref int pos, char expected)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length || json[pos] != expected) throw InvalidJson(pos);
            pos++;
        }

        private static object ParseValue(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length) throw InvalidJson(pos);
            char c = json[pos];
            if (c == '{') return ParseObjectValue(json, ref pos);
            if (c == '[') return ParseArray(json, ref pos);
            if (c == '"') return ParseString(json, ref pos);
            if (c == 't' || c == 'f') return ParseBool(json, ref pos);
            if (c == 'n') return ParseNull(json, ref pos);
            return ParseNumber(json, ref pos);
        }

        private static Dictionary<string, object> ParseObjectValue(string json, ref int pos)
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            pos++;
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == '}') { pos++; return obj; }
            while (true)
            {
                SkipWhitespace(json, ref pos);
                string key = ParseString(json, ref pos);
                Expect(json, ref pos, ':');
                obj[key] = ParseValue(json, ref pos);
                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == '}') { pos++; return obj; }
                Expect(json, ref pos, ',');
            }
        }

        private static List<object> ParseArray(string json, ref int pos)
        {
            List<object> list = new List<object>();
            pos++;
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ']') { pos++; return list; }
            while (true)
            {
                list.Add(ParseValue(json, ref pos));
                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ']') { pos++; return list; }
                Expect(json, ref pos, ',');
            }
        }

        private static string ParseString(string json, ref int pos)
        {
            if (pos >= json.Length || json[pos] != '"') throw InvalidJson(pos);
            pos++;
            StringBuilder sb = new StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos++];
                if (c == '"') return sb.ToString();
                if (c < 0x20) throw InvalidJson(pos - 1);
                if (c != '\\') { sb.Append(c); continue; }
                if (pos >= json.Length) throw InvalidJson(pos);
                char esc = json[pos++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        int code;
                        if (pos + 4 > json.Length || !int.TryParse(json.Substring(pos, 4),
                            System.Globalization.NumberStyles.AllowHexSpecifier,
                            System.Globalization.CultureInfo.InvariantCulture, out code))
                            throw InvalidJson(pos);
                        sb.Append((char)code);
                        pos += 4;
                        break;
                    default: throw InvalidJson(pos - 1);
                }
            }
            throw InvalidJson(pos);
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        private static object ParseNumber(string json, ref int pos)
        {
            int start = pos;
            if (pos < json.Length && json[pos] == '-') pos++;
            if (pos >= json.Length) throw InvalidJson(pos);
            if (json[pos] == '0') pos++;
            else
            {
                if (json[pos] < '1' || json[pos] > '9') throw InvalidJson(pos);
                while (pos < json.Length && IsDigit(json[pos])) pos++;
            }
            if (pos < json.Length && json[pos] == '.')
            {
                pos++;
                if (pos >= json.Length || !IsDigit(json[pos])) throw InvalidJson(pos);
                while (pos < json.Length && IsDigit(json[pos])) pos++;
            }
            if (pos < json.Length && (json[pos] == 'e' || json[pos] == 'E'))
            {
                pos++;
                if (pos < json.Length && (json[pos] == '+' || json[pos] == '-')) pos++;
                if (pos >= json.Length || !IsDigit(json[pos])) throw InvalidJson(pos);
                while (pos < json.Length && IsDigit(json[pos])) pos++;
            }
            double value;
            if (!double.TryParse(json.Substring(start, pos - start), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) || double.IsInfinity(value))
                return 0.0;
            return value;
        }

        private static bool ParseBool(string json, ref int pos)
        {
            if (pos + 4 <= json.Length && json.Substring(pos, 4) == "true")
            {
                pos += 4;
                return true;
            }
            if (pos + 5 <= json.Length && json.Substring(pos, 5) == "false")
            {
                pos += 5;
                return false;
            }
            throw InvalidJson(pos);
        }

        private static object ParseNull(string json, ref int pos)
        {
            if (pos + 4 > json.Length || json.Substring(pos, 4) != "null") throw InvalidJson(pos);
            pos += 4;
            return null;
        }

        private static void SkipWhitespace(string json, ref int pos)
        {
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                    pos++;
                else
                    break;
            }
        }
    }
}
