using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Recount.Core
{
    /// <summary>
    /// The little JSON the records file needs: objects, arrays, strings, numbers, booleans and
    /// null. Written by hand so the mod depends on nothing beyond the game's own assemblies.
    /// </summary>
    public static class Json
    {
        // ---- writing -------------------------------------------------------------------

        public static string Write(object value)
        {
            var sb = new StringBuilder(1024);
            Write(sb, value, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object v, int depth)
        {
            if (v == null) { sb.Append("null"); return; }

            var s = v as string;
            if (s != null) { WriteString(sb, s); return; }

            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is int) { sb.Append(((int)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is long) { sb.Append(((long)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is float) { sb.Append(((float)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (v is double) { sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture)); return; }

            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Newline(sb, depth + 1);
                    WriteString(sb, kv.Key);
                    sb.Append(": ");
                    Write(sb, kv.Value, depth + 1);
                }
                if (!first) Newline(sb, depth);
                sb.Append('}');
                return;
            }

            var list = v as IList<object>;
            if (list != null)
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    Newline(sb, depth + 1);
                    Write(sb, list[i], depth + 1);
                }
                if (list.Count > 0) Newline(sb, depth);
                sb.Append(']');
                return;
            }

            WriteString(sb, v.ToString());
        }

        private static void Newline(StringBuilder sb, int depth)
        {
            sb.Append('\n');
            for (int i = 0; i < depth; i++) sb.Append("  ");
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
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

        // ---- reading -------------------------------------------------------------------

        public static object Parse(string text)
        {
            int pos = 0;
            object v = ParseValue(text, ref pos);
            SkipWs(text, ref pos);
            if (pos != text.Length)
                throw new FormatException("trailing characters at " + pos);
            return v;
        }

        private static object ParseValue(string t, ref int p)
        {
            SkipWs(t, ref p);
            if (p >= t.Length)
                throw new FormatException("unexpected end");
            char c = t[p];
            if (c == '{') return ParseObject(t, ref p);
            if (c == '[') return ParseArray(t, ref p);
            if (c == '"') return ParseString(t, ref p);
            if (c == 't') { Expect(t, ref p, "true"); return true; }
            if (c == 'f') { Expect(t, ref p, "false"); return false; }
            if (c == 'n') { Expect(t, ref p, "null"); return null; }
            return ParseNumber(t, ref p);
        }

        private static Dictionary<string, object> ParseObject(string t, ref int p)
        {
            var d = new Dictionary<string, object>();
            p++; // {
            SkipWs(t, ref p);
            if (p < t.Length && t[p] == '}') { p++; return d; }
            while (true)
            {
                SkipWs(t, ref p);
                string key = ParseString(t, ref p);
                SkipWs(t, ref p);
                if (p >= t.Length || t[p] != ':') throw new FormatException("expected ':' at " + p);
                p++;
                d[key] = ParseValue(t, ref p);
                SkipWs(t, ref p);
                if (p >= t.Length) throw new FormatException("unterminated object");
                if (t[p] == ',') { p++; continue; }
                if (t[p] == '}') { p++; return d; }
                throw new FormatException("expected ',' or '}' at " + p);
            }
        }

        private static List<object> ParseArray(string t, ref int p)
        {
            var l = new List<object>();
            p++; // [
            SkipWs(t, ref p);
            if (p < t.Length && t[p] == ']') { p++; return l; }
            while (true)
            {
                l.Add(ParseValue(t, ref p));
                SkipWs(t, ref p);
                if (p >= t.Length) throw new FormatException("unterminated array");
                if (t[p] == ',') { p++; continue; }
                if (t[p] == ']') { p++; return l; }
                throw new FormatException("expected ',' or ']' at " + p);
            }
        }

        private static string ParseString(string t, ref int p)
        {
            if (p >= t.Length || t[p] != '"') throw new FormatException("expected string at " + p);
            p++;
            var sb = new StringBuilder();
            while (p < t.Length)
            {
                char c = t[p++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (p >= t.Length) break;
                char e = t[p++];
                switch (e)
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
                        if (p + 4 > t.Length) throw new FormatException("bad escape");
                        sb.Append((char)int.Parse(t.Substring(p, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        p += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("unterminated string");
        }

        private static object ParseNumber(string t, ref int p)
        {
            int start = p;
            while (p < t.Length && "+-0123456789.eE".IndexOf(t[p]) >= 0) p++;
            if (start == p) throw new FormatException("unexpected character at " + p);
            return double.Parse(t.Substring(start, p - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        private static void Expect(string t, ref int p, string word)
        {
            if (string.CompareOrdinal(t, p, word, 0, word.Length) != 0)
                throw new FormatException("expected " + word + " at " + p);
            p += word.Length;
        }

        private static void SkipWs(string t, ref int p)
        {
            while (p < t.Length && char.IsWhiteSpace(t[p])) p++;
        }

        // ---- typed accessors -----------------------------------------------------------

        public static string Str(Dictionary<string, object> d, string key, string def = "")
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is string ? (string)v : def;
        }

        public static double Num(Dictionary<string, object> d, string key, double def = 0)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is double ? (double)v : def;
        }

        public static bool Bool(Dictionary<string, object> d, string key, bool def = false)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v is bool ? (bool)v : def;
        }

        public static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> Arr(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as List<object> : null;
        }
    }
}
