using System.Text;
using System.Text.Json;

namespace SteambirdsLauncher.EnemyAi;

// Reads and writes one entry of the "types" object in enemies.json without touching the comments in the rest of the file.
internal static class EnemyTypeFile
{
    public sealed class Row
    {
        public string Id = "";
        public string Name = "";
        public string Template = "auto:weak";
        public string Ai = "";
        public string DeathUnit = "";
        public bool Boss;
    }

    public static string Path => System.IO.Path.Combine(Core.LauncherPaths.ServerFolder, "enemies.json");

    public static List<Row> Read()
    {
        var list = new List<Row>();
        if (!File.Exists(Path)) return list;
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(Path), new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        if (!doc.RootElement.TryGetProperty("types", out JsonElement types) || types.ValueKind != JsonValueKind.Object) return list;
        foreach (JsonProperty prop in types.EnumerateObject())
        {
            JsonElement value = prop.Value;
            list.Add(new Row
            {
                Id = prop.Name,
                Name = Text(value, "name"),
                Template = Text(value, "template").Length == 0 ? "auto:weak" : Text(value, "template"),
                Ai = Text(value, "ai"),
                DeathUnit = Text(value, "deathUnit"),
                Boss = value.TryGetProperty("boss", out JsonElement boss) && boss.ValueKind == JsonValueKind.True
            });
        }
        list.Sort((a, b) => string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    public static void Save(Row row)
    {
        string text = File.ReadAllText(Path);
        if (!TryObject(text, "types", out int start, out int end))
            throw new FormatException("enemies.json has no types object.");
        string body = text.Substring(start, end - start);
        string raw = Raw(row);
        if (TryProperty(body, row.Id, out _, out int valueStart, out int valueEnd))
            body = body.Substring(0, valueStart) + raw + body.Substring(valueEnd);
        else
        {
            int close = body.LastIndexOf('}');
            string head = body.Substring(0, close).TrimEnd();
            if (head.Length > 0 && head[head.Length - 1] != '{' && head[head.Length - 1] != ',') head += ",";
            body = head + "\r\n    \"" + Escape(row.Id) + "\": " + raw + "\r\n  }";
        }
        File.WriteAllText(Path, text.Substring(0, start) + body + text.Substring(end));
    }

    public static void Delete(string id)
    {
        string text = File.ReadAllText(Path);
        if (!TryObject(text, "types", out int start, out int end)) return;
        string body = text.Substring(start, end - start);
        if (!TryProperty(body, id, out int keyStart, out _, out int valueEnd)) return;
        int cut = keyStart;
        while (cut > 0 && (body[cut - 1] == ' ' || body[cut - 1] == '\t')) cut--;
        if (cut > 0 && body[cut - 1] == '\n') cut--;
        if (cut > 0 && body[cut - 1] == '\r') cut--;
        int after = valueEnd;
        while (after < body.Length && (body[after] == ' ' || body[after] == '\t')) after++;
        if (after < body.Length && body[after] == ',') after++;
        body = body.Substring(0, cut) + body.Substring(after);
        File.WriteAllText(Path, text.Substring(0, start) + body + text.Substring(end));
    }

    private static string Raw(Row row)
    {
        var json = new StringBuilder();
        json.Append("{ \"name\": \"").Append(Escape(row.Name.Length == 0 ? row.Id : row.Name)).Append("\"");
        json.Append(", \"template\": \"").Append(Escape(row.Template.Length == 0 ? "auto:weak" : row.Template)).Append("\"");
        if (row.Ai.Length > 0) json.Append(", \"ai\": \"").Append(Escape(row.Ai)).Append("\"");
        if (row.Boss) json.Append(", \"boss\": true");
        if (row.DeathUnit.Length > 0) json.Append(", \"deathUnit\": \"").Append(Escape(row.DeathUnit)).Append("\"");
        json.Append(" }");
        return json.ToString();
    }

    private static string Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");

    // The object value of a top-level key. start/end cover the braces.
    private static bool TryObject(string text, string key, out int start, out int end)
    {
        start = end = 0;
        int i = 0;
        int depth = 0;
        while (i < text.Length)
        {
            i = SkipTrivia(text, i);
            if (i >= text.Length) return false;
            char c = text[i];
            if (c == '{') { depth++; i++; continue; }
            if (c == '}') { depth--; i++; continue; }
            if (c == '[') { i = SkipValue(text, i); continue; }
            if (c != '"') { i++; continue; }
            int keyAt = i;
            string name = ReadString(text, ref i);
            int after = SkipTrivia(text, i);
            if (depth == 1 && name == key && after < text.Length && text[after] == ':')
            {
                after = SkipTrivia(text, after + 1);
                if (after >= text.Length || text[after] != '{') return false;
                start = after;
                end = SkipValue(text, after);
                return true;
            }
            if (after < text.Length && text[after] == ':') i = SkipValue(text, SkipTrivia(text, after + 1));
            else i = keyAt + 1;
        }
        return false;
    }

    // A property of an object. keyStart is the opening quote of the name. valueStart/valueEnd cover the value.
    private static bool TryProperty(string objectText, string key, out int keyStart, out int valueStart, out int valueEnd)
    {
        keyStart = valueStart = valueEnd = 0;
        int i = 0;
        int depth = 0;
        while (i < objectText.Length)
        {
            i = SkipTrivia(objectText, i);
            if (i >= objectText.Length) return false;
            char c = objectText[i];
            if (c == '{') { depth++; i++; continue; }
            if (c == '}') { depth--; i++; continue; }
            if (c == '[') { i = SkipValue(objectText, i); continue; }
            if (c != '"') { i++; continue; }
            int at = i;
            string name = ReadString(objectText, ref i);
            int after = SkipTrivia(objectText, i);
            if (depth == 1 && name.Equals(key, StringComparison.Ordinal) && after < objectText.Length && objectText[after] == ':')
            {
                valueStart = SkipTrivia(objectText, after + 1);
                valueEnd = SkipValue(objectText, valueStart);
                keyStart = at;
                return true;
            }
            if (after < objectText.Length && objectText[after] == ':') i = SkipValue(objectText, SkipTrivia(objectText, after + 1));
            else i = at + 1;
        }
        return false;
    }

    private static int SkipTrivia(string text, int i)
    {
        while (i < text.Length)
        {
            char c = text[i];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                while (i < text.Length && text[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < text.Length && !(text[i] == '*' && text[i + 1] == '/')) i++;
                i = Math.Min(text.Length, i + 2);
                continue;
            }
            break;
        }
        return i;
    }

    private static string ReadString(string text, ref int i)
    {
        var value = new StringBuilder();
        i++;
        while (i < text.Length)
        {
            char c = text[i++];
            if (c == '\\' && i < text.Length) { value.Append(text[i++]); continue; }
            if (c == '"') break;
            value.Append(c);
        }
        return value.ToString();
    }

    private static int SkipValue(string text, int i)
    {
        if (i >= text.Length) return i;
        char c = text[i];
        if (c == '"') { ReadString(text, ref i); return i; }
        if (c == '{' || c == '[')
        {
            char close = c == '{' ? '}' : ']';
            int depth = 0;
            while (i < text.Length)
            {
                char d = text[i];
                if (d == '"') { ReadString(text, ref i); continue; }
                i++;
                if (d == c) depth++;
                else if (d == close && --depth == 0) return i;
            }
            return i;
        }
        while (i < text.Length && text[i] != ',' && text[i] != '}' && text[i] != ']' && text[i] != '\n') i++;
        return i;
    }
}
