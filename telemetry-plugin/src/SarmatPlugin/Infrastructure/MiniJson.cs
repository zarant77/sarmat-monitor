using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SarmatPlugin.Infrastructure
{
    // Small dependency-free JSON parser/serializer for router and OBS protocol envelopes.
    internal static class MiniJson
    {
        public static object Parse(string json) => new Parser(json).ParseValue();
        public static string Serialize(object value)
        {
            var b = new StringBuilder();
            Write(value, b);
            return b.ToString();
        }
        public static IDictionary<string, object> Object(object value) => value as IDictionary<string, object>;
        public static string String(IDictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : null;
        public static int Int(IDictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) && v != null ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0;
        public static bool Bool(IDictionary<string, object> o, string key) =>
            o != null && o.TryGetValue(key, out var v) && v != null && Convert.ToBoolean(v, CultureInfo.InvariantCulture);

        private static void Write(object value, StringBuilder b)
        {
            if (value == null) { b.Append("null"); return; }
            if (value is string s) { b.Append('"').Append(Escape(s)).Append('"'); return; }
            if (value is bool flag) { b.Append(flag ? "true" : "false"); return; }
            if (value is IDictionary dictionary)
            {
                b.Append('{'); var first = true;
                foreach (DictionaryEntry item in dictionary)
                {
                    if (!first) b.Append(','); first = false;
                    Write(Convert.ToString(item.Key), b); b.Append(':'); Write(item.Value, b);
                }
                b.Append('}'); return;
            }
            if (value is IEnumerable enumerable)
            {
                b.Append('['); var first = true;
                foreach (var item in enumerable) { if (!first) b.Append(','); first = false; Write(item, b); }
                b.Append(']'); return;
            }
            b.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        }
        private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"")
            .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

        private sealed class Parser
        {
            private readonly string text; private int pos;
            public Parser(string text) { this.text = text ?? ""; }
            public object ParseValue()
            {
                Space(); if (pos >= text.Length) throw new FormatException("Unexpected end of JSON");
                var c = text[pos];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (Match("true")) return true;
                if (Match("false")) return false;
                if (Match("null")) return null;
                return ParseNumber();
            }
            private IDictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); pos++; Space();
                if (Take('}')) return result;
                while (true)
                {
                    Space(); var key = ParseString(); Space(); Expect(':'); result[key] = ParseValue(); Space();
                    if (Take('}')) return result; Expect(',');
                }
            }
            private IList<object> ParseArray()
            {
                var result = new List<object>(); pos++; Space(); if (Take(']')) return result;
                while (true) { result.Add(ParseValue()); Space(); if (Take(']')) return result; Expect(','); }
            }
            private string ParseString()
            {
                Expect('"'); var b = new StringBuilder();
                while (pos < text.Length)
                {
                    var c = text[pos++]; if (c == '"') return b.ToString();
                    if (c != '\\') { b.Append(c); continue; }
                    c = text[pos++];
                    if (c == 'u') { b.Append((char)Convert.ToInt32(text.Substring(pos, 4), 16)); pos += 4; }
                    else b.Append(c == 'n' ? '\n' : c == 'r' ? '\r' : c == 't' ? '\t' : c);
                }
                throw new FormatException("Unterminated JSON string");
            }
            private object ParseNumber()
            {
                var start = pos;
                while (pos < text.Length && "-+0123456789.eE".IndexOf(text[pos]) >= 0) pos++;
                var token = text.Substring(start, pos - start);
                if (token.IndexOfAny(new[] {'.','e','E'}) >= 0) return double.Parse(token, CultureInfo.InvariantCulture);
                return long.Parse(token, CultureInfo.InvariantCulture);
            }
            private bool Match(string value)
            {
                Space(); if (pos + value.Length > text.Length || text.Substring(pos, value.Length) != value) return false;
                pos += value.Length; return true;
            }
            private bool Take(char c) { Space(); if (pos < text.Length && text[pos] == c) { pos++; return true; } return false; }
            private void Expect(char c) { if (!Take(c)) throw new FormatException("Expected " + c); }
            private void Space() { while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++; }
        }
    }
}
