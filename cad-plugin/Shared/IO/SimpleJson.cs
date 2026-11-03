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
