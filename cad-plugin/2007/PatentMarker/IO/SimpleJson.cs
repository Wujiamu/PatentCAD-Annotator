using System;
using System.Collections.Generic;
using System.Text;

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
            if (json == null || json.Length == 0) return null;
            int pos = 0;
            SkipWhitespace(json, ref pos);
            return ParseValue(json, ref pos);
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            object result = Parse(json);
            if (result is Dictionary<string, object>)
                return (Dictionary<string, object>)result;
            return new Dictionary<string, object>();
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

        private static object ParseValue(string json, ref int pos)
        {
            SkipWhitespace(json, ref pos);
            if (pos >= json.Length) return null;
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
            pos++; // skip {
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == '}')
            {
                pos++;
                return obj;
            }
            while (pos < json.Length)
            {
                SkipWhitespace(json, ref pos);
                string key = ParseString(json, ref pos);
                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ':') pos++;
                object value = ParseValue(json, ref pos);
                obj[key] = value;
                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ',') { pos++; continue; }
                if (pos < json.Length && json[pos] == '}') { pos++; break; }
                break;
            }
            return obj;
        }

        private static List<object> ParseArray(string json, ref int pos)
        {
            List<object> list = new List<object>();
            pos++; // skip [
            SkipWhitespace(json, ref pos);
            if (pos < json.Length && json[pos] == ']')
            {
                pos++;
                return list;
            }
            while (pos < json.Length)
            {
                object value = ParseValue(json, ref pos);
                list.Add(value);
                SkipWhitespace(json, ref pos);
                if (pos < json.Length && json[pos] == ',') { pos++; continue; }
                if (pos < json.Length && json[pos] == ']') { pos++; break; }
                break;
            }
            return list;
        }

        private static string ParseString(string json, ref int pos)
        {
            if (pos >= json.Length || json[pos] != '"') return "";
            pos++; // skip opening quote
            StringBuilder sb = new StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == '"') { pos++; break; }
                if (c == '\\' && pos + 1 < json.Length)
                {
                    pos++;
                    char esc = json[pos];
                    switch (esc)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (pos + 4 < json.Length)
                            {
                                string hex = json.Substring(pos + 1, 4);
                                try
                                {
                                    int code = Convert.ToInt32(hex, 16);
                                    sb.Append((char)code);
                                }
                                catch { }
                                pos += 4;
                            }
                            break;
                        default: sb.Append(esc); break;
                    }
                    pos++;
                }
                else
                {
                    sb.Append(c);
                    pos++;
                }
            }
            return sb.ToString();
        }

        private static object ParseNumber(string json, ref int pos)
        {
            int start = pos;
            while (pos < json.Length)
            {
                char c = json[pos];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                    pos++;
                else
                    break;
            }
            if (start == pos) return 0.0;
            string numStr = json.Substring(start, pos - start);
            double d;
            if (double.TryParse(numStr, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out d))
                return d;
            return 0.0;
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
            return false;
        }

        private static object ParseNull(string json, ref int pos)
        {
            if (pos + 4 <= json.Length && json.Substring(pos, 4) == "null")
            {
                pos += 4;
                return null;
            }
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
