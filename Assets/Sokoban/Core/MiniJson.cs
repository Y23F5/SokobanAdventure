/**
 * @file MiniJson.cs
 * @brief 极小的 JSON 读写器，只覆盖存档用到的结构，不依赖任何外部库，使 Core 在 Unity 与 dotnet 两侧都能编译。
 * MiniJson.Parse(text)：解析为 null、bool、string、long、List<object>、Dictionary<string, object>；语法错误或多余内容抛 FormatException。
 * MiniJson.Write(value)：把上述类型写成紧凑 JSON 文本，字符串按需转义，不支持的类型抛 ArgumentException。
 * Parse(text, allowFractionalNumbers)：素材包导入可显式启用有限小数和科学记数；存档仍只允许整数。
 * Reader：递归下降解析器，逐字符前进。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Sokoban.Core
{
    public static class MiniJson
    {
        public static object Parse(string text) => Parse(text, false);

        internal static object Parse(string text, bool allowFractionalNumbers)
        {
            var reader = new Reader(text, allowFractionalNumbers);
            reader.SkipSpace();
            var value = reader.ReadValue();
            reader.SkipSpace();
            if (!reader.End) throw new FormatException("JSON 末尾有多余内容");
            return value;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        static void WriteValue(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string s: WriteString(sb, s); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> d:
                    sb.Append('{');
                    bool firstKey = true;
                    foreach (var pair in d)
                    {
                        if (!firstKey) sb.Append(',');
                        firstKey = false;
                        WriteString(sb, pair.Key);
                        sb.Append(':');
                        WriteValue(sb, pair.Value);
                    }
                    sb.Append('}');
                    break;
                case IEnumerable<object> list:
                    sb.Append('[');
                    bool firstItem = true;
                    foreach (var item in list)
                    {
                        if (!firstItem) sb.Append(',');
                        firstItem = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                default: throw new ArgumentException("不支持的 JSON 类型：" + value.GetType().Name);
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        sealed class Reader
        {
            readonly string text;
            readonly bool allowFractionalNumbers;
            int pos;

            public Reader(string text, bool allowFractionalNumbers)
            {
                this.text = text;
                this.allowFractionalNumbers = allowFractionalNumbers;
            }

            public bool End => pos >= text.Length;

            public void SkipSpace()
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
            }

            public object ReadValue()
            {
                if (End) throw new FormatException("JSON 意外结束");
                char c = text[pos];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                if (Match("null")) return null;
                if (Match("true")) return true;
                if (Match("false")) return false;
                throw new FormatException($"JSON 位置 {pos} 出现意外字符 '{c}'");
            }

            bool Match(string word)
            {
                if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0) return false;
                pos += word.Length;
                return true;
            }

            Dictionary<string, object> ReadObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                pos++;
                SkipSpace();
                if (!End && text[pos] == '}') { pos++; return result; }
                while (true)
                {
                    SkipSpace();
                    if (End || text[pos] != '"') throw new FormatException("JSON 对象键必须是字符串");
                    string key = ReadString();
                    SkipSpace();
                    if (End || text[pos] != ':') throw new FormatException("JSON 对象缺少冒号");
                    pos++;
                    SkipSpace();
                    if (result.ContainsKey(key)) throw new FormatException("JSON 对象键重复：" + key);
                    result[key] = ReadValue();
                    SkipSpace();
                    if (End) throw new FormatException("JSON 对象未闭合");
                    if (text[pos] == ',') { pos++; continue; }
                    if (text[pos] == '}') { pos++; return result; }
                    throw new FormatException("JSON 对象分隔符无效");
                }
            }

            List<object> ReadArray()
            {
                var result = new List<object>();
                pos++;
                SkipSpace();
                if (!End && text[pos] == ']') { pos++; return result; }
                while (true)
                {
                    SkipSpace();
                    result.Add(ReadValue());
                    SkipSpace();
                    if (End) throw new FormatException("JSON 数组未闭合");
                    if (text[pos] == ',') { pos++; continue; }
                    if (text[pos] == ']') { pos++; return result; }
                    throw new FormatException("JSON 数组分隔符无效");
                }
            }

            string ReadString()
            {
                var sb = new StringBuilder();
                pos++;
                while (true)
                {
                    if (End) throw new FormatException("JSON 字符串未闭合");
                    char c = text[pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw new FormatException("JSON 转义不完整");
                    char e = text[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (pos + 4 > text.Length || !int.TryParse(text.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                throw new FormatException("JSON \\u 转义无效");
                            sb.Append((char)code);
                            pos += 4;
                            break;
                        default: throw new FormatException("JSON 转义字符无效");
                    }
                }
            }

            object ReadNumber()
            {
                int start = pos;
                if (text[pos] == '-') pos++;
                ReadDigits();
                bool fractional = !End && (text[pos] == '.' || text[pos] == 'e' || text[pos] == 'E');
                if (!fractional)
                {
                    if (!long.TryParse(text.Substring(start, pos - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer))
                        throw new FormatException("JSON 数字无效");
                    return integer;
                }
                if (!allowFractionalNumbers) throw new FormatException("JSON 只支持整数");
                if (text[pos] == '.') { pos++; ReadDigits(); }
                if (!End && (text[pos] == 'e' || text[pos] == 'E'))
                {
                    pos++;
                    if (!End && (text[pos] == '+' || text[pos] == '-')) pos++;
                    ReadDigits();
                }
                if (!double.TryParse(text.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    || double.IsInfinity(value) || double.IsNaN(value)) throw new FormatException("JSON 数字无效");
                return value;
            }

            void ReadDigits()
            {
                int start = pos;
                while (!End && text[pos] >= '0' && text[pos] <= '9') pos++;
                if (pos == start) throw new FormatException("JSON 缺少数字");
            }
        }
    }
}
