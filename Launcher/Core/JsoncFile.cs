using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SteambirdsLauncher.Core;

public enum JsoncKind { String, Number, Bool, Null, Array, Object }

// One top-level "key": value of a config file.
public sealed class JsoncProperty
{
    public string Key { get; init; } = "";
    public JsoncKind Kind { get; init; }
    public string RawValue { get; init; } = "";
    public int ValueStart { get; init; }
    public int ValueEnd { get; init; }

    // The // comment lines right above the key (no blank line in between): the description of the setting.
    public string Comment { get; init; } = "";
}

// The server config files (progression.json, items.json, enemies.json) are JSON with comments, and the comments are
// the documentation for the host. The launcher must not lose them: it only replaces the text of the values it changed
// and keeps everything else in the file as it was.
public static class JsoncFile
{
    public static readonly JsonDocumentOptions ParseOptions = new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static List<JsoncProperty> ReadTopLevel(string text)
    {
        var scanner = new Scanner(text);
        scanner.SkipTrivia(false);
        if (!scanner.TryConsume('{')) throw new FormatException(L.T("the file does not start with {"));

        var properties = new List<JsoncProperty>();
        while (true)
        {
            string comment = scanner.SkipTrivia(true);
            if (scanner.AtEnd) throw new FormatException(L.T("the file ends before the final }"));
            char next = scanner.Peek;
            if (next == '}') break;
            if (next == ',')
            {
                scanner.Advance();
                continue;
            }
            if (next != '"') throw new FormatException(L.F("expected the name of a key (position {0})", scanner.Position));

            string key = scanner.ReadString();
            scanner.SkipTrivia(false);
            if (!scanner.TryConsume(':')) throw new FormatException(L.F("expected ':' after \"{0}\"", key));
            scanner.SkipTrivia(false);

            int start = scanner.Position;
            JsoncKind kind = scanner.SkipValue();
            int end = scanner.Position;
            properties.Add(new JsoncProperty
            {
                Key = key,
                Kind = kind,
                RawValue = text.Substring(start, end - start),
                ValueStart = start,
                ValueEnd = end,
                Comment = comment
            });

            // A comma and a // comment on the same line still belong to this key.
            scanner.SkipInlineSpaces();
            scanner.TryConsume(',');
            scanner.SkipInlineSpaces();
            scanner.SkipSameLineComment();
        }
        return properties;
    }

    // Replaces the values of the given keys (raw JSON text) and returns the new file text.
    // Keys that are not in the file are returned in "missing" and left out.
    public static string ApplyChanges(string text, IReadOnlyDictionary<string, string> rawValues, out List<string> missing)
    {
        List<JsoncProperty> properties = ReadTopLevel(text);
        var byKey = new Dictionary<string, JsoncProperty>(StringComparer.OrdinalIgnoreCase);
        foreach (JsoncProperty property in properties) byKey[property.Key] = property;

        missing = new List<string>();
        var edits = new List<(int Start, int End, string Value)>();
        foreach (KeyValuePair<string, string> pair in rawValues)
        {
            if (byKey.TryGetValue(pair.Key, out JsoncProperty? property)) edits.Add((property.ValueStart, property.ValueEnd, pair.Value));
            else missing.Add(pair.Key);
        }

        var builder = new StringBuilder(text);
        foreach ((int start, int end, string value) in edits.OrderByDescending(e => e.Start))
        {
            builder.Remove(start, end - start);
            builder.Insert(start, value);
        }

        string result = builder.ToString();
        using (JsonDocument.Parse(result, ParseOptions))
        {
            // Throws if the result is not valid JSON: the file on disk is only replaced with a valid one.
        }
        return result;
    }

    // ------------------------------------------------------------------ value formatting

    public static string FormatBool(bool value) => value ? "true" : "false";

    // Keeps a decimal point when the original value had one ("1.0" stays "1.0"), so the file keeps its style.
    public static string FormatNumber(decimal value, bool asDecimal)
    {
        return asDecimal
            ? value.ToString("0.0###########", CultureInfo.InvariantCulture)
            : decimal.Round(value, 0).ToString("0", CultureInfo.InvariantCulture);
    }

    public static string FormatString(string value)
    {
        return JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    public static string? ReadStringValue(string raw)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(raw, ParseOptions);
            return document.RootElement.ValueKind == JsonValueKind.String ? document.RootElement.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ scanner

    private sealed class Scanner
    {
        private readonly string text;
        private int index;

        public Scanner(string text)
        {
            this.text = text;
        }

        public int Position => index;
        public bool AtEnd => index >= text.Length;
        public char Peek => index < text.Length ? text[index] : '\0';

        public void Advance() => index++;

        public bool TryConsume(char c)
        {
            if (Peek != c) return false;
            index++;
            return true;
        }

        // Skips whitespace and comments. With collect = true, returns the comment lines right above the next token
        // (a blank line drops the comments collected before it).
        public string SkipTrivia(bool collect)
        {
            var comments = new List<string>();
            int newlinesInRun = 0;
            while (!AtEnd)
            {
                char c = text[index];
                if (c == '\n')
                {
                    newlinesInRun++;
                    if (newlinesInRun >= 2) comments.Clear();
                    index++;
                }
                else if (char.IsWhiteSpace(c))
                {
                    index++;
                }
                else if (c == '/' && index + 1 < text.Length && text[index + 1] == '/')
                {
                    int lineEnd = text.IndexOf('\n', index);
                    if (lineEnd < 0) lineEnd = text.Length;
                    if (collect) comments.Add(text.Substring(index + 2, lineEnd - index - 2).Trim());
                    index = lineEnd;
                    newlinesInRun = 0;
                }
                else if (c == '/' && index + 1 < text.Length && text[index + 1] == '*')
                {
                    int close = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    int stop = close < 0 ? text.Length : close + 2;
                    if (collect)
                    {
                        string inner = text.Substring(index + 2, (close < 0 ? text.Length : close) - index - 2);
                        foreach (string part in inner.Split('\n'))
                        {
                            string trimmed = part.Trim().TrimStart('*').Trim();
                            if (trimmed.Length > 0) comments.Add(trimmed);
                        }
                    }
                    index = stop;
                    newlinesInRun = 0;
                }
                else
                {
                    break;
                }
            }
            return string.Join("\n", comments);
        }

        public void SkipInlineSpaces()
        {
            while (!AtEnd && (text[index] == ' ' || text[index] == '\t' || text[index] == '\r')) index++;
        }

        public void SkipSameLineComment()
        {
            if (index + 1 < text.Length && text[index] == '/' && text[index + 1] == '/')
            {
                int lineEnd = text.IndexOf('\n', index);
                index = lineEnd < 0 ? text.Length : lineEnd;
            }
        }

        public string ReadString()
        {
            if (!TryConsume('"')) throw new FormatException(L.F("expected \" (position {0})", index));
            var builder = new StringBuilder();
            while (!AtEnd)
            {
                char c = text[index++];
                if (c == '"') return builder.ToString();
                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }
                if (AtEnd) break;
                char escaped = text[index++];
                switch (escaped)
                {
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'u':
                        if (index + 4 <= text.Length &&
                            int.TryParse(text.AsSpan(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                        {
                            builder.Append((char)code);
                            index += 4;
                        }
                        break;
                    default: builder.Append(escaped); break;
                }
            }
            throw new FormatException(L.T("text without a closing \""));
        }

        public JsoncKind SkipValue()
        {
            char c = Peek;
            if (c == '"')
            {
                ReadString();
                return JsoncKind.String;
            }
            if (c == '{' || c == '[')
            {
                SkipComposite();
                return c == '{' ? JsoncKind.Object : JsoncKind.Array;
            }

            int start = index;
            while (!AtEnd)
            {
                char d = text[index];
                if (d == ',' || d == '}' || d == ']' || char.IsWhiteSpace(d) || d == '/') break;
                index++;
            }
            if (index == start) throw new FormatException(L.F("empty value (position {0})", index));
            string literal = text.Substring(start, index - start);
            if (literal == "true" || literal == "false") return JsoncKind.Bool;
            if (literal == "null") return JsoncKind.Null;
            return JsoncKind.Number;
        }

        private void SkipComposite()
        {
            int depth = 0;
            while (!AtEnd)
            {
                char c = text[index];
                if (c == '"')
                {
                    ReadString();
                    continue;
                }
                if (c == '/' && index + 1 < text.Length && (text[index + 1] == '/' || text[index + 1] == '*'))
                {
                    SkipTrivia(false);
                    continue;
                }
                index++;
                if (c == '{' || c == '[')
                {
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0) return;
                }
            }
            throw new FormatException(L.T("{ or [ without a closing bracket"));
        }
    }
}
