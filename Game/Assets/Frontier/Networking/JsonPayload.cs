using System;
using System.Globalization;

namespace Frontier.Networking
{
    /// <summary>Small dependency-free boundary validator, not a game-state serializer.</summary>
    public static class JsonPayload
    {
        public static bool IsValidObject(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            var reader = new Reader(value);
            return reader.ParseObject(0) && reader.AtEnd;
        }

        sealed class Reader
        {
            readonly string source;
            int at;
            int nodes;
            public Reader(string source) { this.source = source; }
            public bool AtEnd { get { Space(); return at == source.Length; } }
            void Space() { while (at < source.Length && (source[at] == ' ' || source[at] == '\r' || source[at] == '\n' || source[at] == '\t')) at++; }
            bool Take(char c) { Space(); if (at >= source.Length || source[at] != c) return false; at++; return true; }

            public bool ParseObject(int depth)
            {
                if (depth > 24 || !Take('{')) return false;
                if (Take('}')) return true;
                do { if (!String() || !Take(':') || !Value(depth + 1)) return false; } while (Take(','));
                return Take('}');
            }

            bool Value(int depth)
            {
                if (++nodes > 16000 || depth > 24) return false;
                Space(); if (at >= source.Length) return false;
                char c = source[at];
                if (c == '{') return ParseObject(depth);
                if (c == '[')
                {
                    at++; if (Take(']')) return true;
                    do { if (!Value(depth + 1)) return false; } while (Take(','));
                    return Take(']');
                }
                if (c == '"') return String();
                if (c == 't') return Literal("true");
                if (c == 'f') return Literal("false");
                if (c == 'n') return Literal("null");
                return Number();
            }

            bool Literal(string text)
            {
                if (at + text.Length > source.Length || string.CompareOrdinal(source, at, text, 0, text.Length) != 0) return false;
                at += text.Length; return true;
            }

            bool String()
            {
                if (!Take('"')) return false;
                while (at < source.Length)
                {
                    char c = source[at++];
                    if (c == '"') return true;
                    if (c < 32) return false;
                    if (c == '\\')
                    {
                        if (at >= source.Length) return false;
                        c = source[at++];
                        if (c == 'u')
                        {
                            int code;
                            if (!Hex4(out code)) return false;
                            if (code >= 0xD800 && code <= 0xDBFF)
                            {
                                if (at + 2 > source.Length || source[at++] != '\\' || source[at++] != 'u') return false;
                                int low; if (!Hex4(out low) || low < 0xDC00 || low > 0xDFFF) return false;
                            }
                            else if (code >= 0xDC00 && code <= 0xDFFF) return false;
                        }
                        else if (c != '"' && c != '\\' && c != '/' && c != 'b' && c != 'f' && c != 'n' && c != 'r' && c != 't') return false;
                    }
                    else if (char.IsHighSurrogate(c))
                    {
                        if (at >= source.Length || !char.IsLowSurrogate(source[at++])) return false;
                    }
                    else if (char.IsLowSurrogate(c)) return false;
                }
                return false;
            }

            bool Hex4(out int code)
            {
                code = 0;
                if (at + 4 > source.Length) return false;
                for (int i = 0; i < 4; i++)
                {
                    char c = source[at++];
                    int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
                    if (digit < 0) return false;
                    code = code * 16 + digit;
                }
                return true;
            }

            bool Number()
            {
                int start = at;
                if (at < source.Length && source[at] == '-') at++;
                if (at >= source.Length) return false;
                if (source[at] == '0') at++;
                else
                {
                    if (source[at] < '1' || source[at] > '9') return false;
                    while (at < source.Length && source[at] >= '0' && source[at] <= '9') at++;
                }
                if (at < source.Length && source[at] == '.')
                {
                    at++; int digits = at;
                    while (at < source.Length && source[at] >= '0' && source[at] <= '9') at++;
                    if (at == digits) return false;
                }
                if (at < source.Length && (source[at] == 'e' || source[at] == 'E'))
                {
                    at++; if (at < source.Length && (source[at] == '+' || source[at] == '-')) at++;
                    int digits = at;
                    while (at < source.Length && source[at] >= '0' && source[at] <= '9') at++;
                    if (at == digits) return false;
                }
                double number;
                return double.TryParse(source.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !double.IsNaN(number) && !double.IsInfinity(number);
            }
        }
    }
}
