using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Claude.UnityMCP.Communication
{
    /// <summary>
    /// Minimal JSON serializer/deserializer with no external dependencies.
    /// Handles Dictionary, List, primitives, and nested structures.
    /// </summary>
    public static class MiniJson
    {
        public static string Serialize(object obj)
        {
            var sb = new StringBuilder(256);
            SerializeValue(obj, sb);
            return sb.ToString();
        }

        public static object Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int index = 0;
            return ParseValue(json, ref index);
        }

        public static Dictionary<string, object> DeserializeObject(string json)
        {
            return Deserialize(json) as Dictionary<string, object>;
        }

        public static List<object> DeserializeArray(string json)
        {
            return Deserialize(json) as List<object>;
        }

        // ── Serialization ────────────────────────────────────────────

        private static void SerializeValue(object value, StringBuilder sb)
        {
            if (value == null)
            {
                sb.Append("null");
            }
            else if (value is string s)
            {
                SerializeString(s, sb);
            }
            else if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
            }
            else if (value is int i)
            {
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
            }
            else if (value is long l)
            {
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
            }
            else if (value is float f)
            {
                if (float.IsNaN(f) || float.IsInfinity(f))
                    sb.Append("null");
                else
                    sb.Append(f.ToString("G9", CultureInfo.InvariantCulture));
            }
            else if (value is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d))
                    sb.Append("null");
                else
                    sb.Append(d.ToString("G17", CultureInfo.InvariantCulture));
            }
            else if (value is IDictionary dict)
            {
                SerializeDictionary(dict, sb);
            }
            else if (value is IList list)
            {
                SerializeArray(list, sb);
            }
            else if (value is Enum e)
            {
                sb.Append(Convert.ToInt32(e).ToString());
            }
            else
            {
                // Fallback: treat as string
                SerializeString(value.ToString(), sb);
            }
        }

        private static void SerializeString(string s, StringBuilder sb)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:x4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static void SerializeDictionary(IDictionary dict, StringBuilder sb)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                SerializeString(entry.Key.ToString(), sb);
                sb.Append(':');
                SerializeValue(entry.Value, sb);
            }
            sb.Append('}');
        }

        private static void SerializeArray(IList list, StringBuilder sb)
        {
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0) sb.Append(',');
                SerializeValue(list[i], sb);
            }
            sb.Append(']');
        }

        // ── Deserialization ──────────────────────────────────────────

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;

            char c = json[index];
            switch (c)
            {
                case '{': return ParseObject(json, ref index);
                case '[': return ParseArray(json, ref index);
                case '"': return ParseString(json, ref index);
                case 't':
                case 'f': return ParseBool(json, ref index);
                case 'n': index += 4; return null;
                default: return ParseNumber(json, ref index);
            }
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var dict = new Dictionary<string, object>();
            index++; // skip {
            SkipWhitespace(json, ref index);

            while (index < json.Length && json[index] != '}')
            {
                // Parse key
                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                index++; // skip :
                SkipWhitespace(json, ref index);

                // Parse value
                dict[key] = ParseValue(json, ref index);
                SkipWhitespace(json, ref index);

                if (index < json.Length && json[index] == ',')
                    index++;
                SkipWhitespace(json, ref index);
            }

            if (index < json.Length) index++; // skip }
            return dict;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var list = new List<object>();
            index++; // skip [
            SkipWhitespace(json, ref index);

            while (index < json.Length && json[index] != ']')
            {
                list.Add(ParseValue(json, ref index));
                SkipWhitespace(json, ref index);

                if (index < json.Length && json[index] == ',')
                    index++;
                SkipWhitespace(json, ref index);
            }

            if (index < json.Length) index++; // skip ]
            return list;
        }

        private static string ParseString(string json, ref int index)
        {
            index++; // skip opening "
            var sb = new StringBuilder();

            while (index < json.Length)
            {
                char c = json[index];
                if (c == '\\')
                {
                    index++;
                    if (index >= json.Length) break;
                    c = json[index];
                    switch (c)
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
                            if (index + 4 < json.Length)
                            {
                                string hex = json.Substring(index + 1, 4);
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                index += 4;
                            }
                            break;
                    }
                }
                else if (c == '"')
                {
                    index++; // skip closing "
                    return sb.ToString();
                }
                else
                {
                    sb.Append(c);
                }
                index++;
            }
            return sb.ToString();
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json[index] == 't') { index += 4; return true; }
            index += 5; return false;
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            bool isFloat = false;

            if (json[index] == '-') index++;
            while (index < json.Length && (char.IsDigit(json[index]) || json[index] == '.' || json[index] == 'e' || json[index] == 'E' || json[index] == '+' || json[index] == '-'))
            {
                if (json[index] == '.' || json[index] == 'e' || json[index] == 'E')
                    isFloat = true;
                index++;
            }

            string numStr = json.Substring(start, index - start);
            if (isFloat)
            {
                if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                    return d;
            }
            else
            {
                if (long.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out long l))
                    return l < int.MinValue || l > int.MaxValue ? (object)l : (int)l;
            }
            return 0;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;
        }
    }
}
