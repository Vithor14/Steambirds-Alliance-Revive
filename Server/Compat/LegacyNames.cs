using System.Text.Json;
using System.Text.Json.Nodes;

// Older versions of this project wrote maps, enemies.json, dungeons.json and accounts with Portuguese keys and values
// ("chao", "portoes", "comportamento": "perseguir"...). LegacyNames turns those files into the current English
// format when they are read, so maps made by the community and accounts saved before the rename keep working.
// Files are not rewritten here: the map editor saves them in the new format the next time they are saved, and
// tools\maps\migrate_legacy_map.py converts a whole folder.
//
// The tables live in legacy-names.json (server folder): embedded in the server and in the launcher, read by the Python tools.
public static class LegacyNames
{
    public const string FileName = "legacy-names.json";

    private sealed class Tables
    {
        public Dictionary<string, string> Keys = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> BarkKeys = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Prefixes = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Formats = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> NameDictionaries = new(StringComparer.OrdinalIgnoreCase);
    }

    private static Tables? tables;
    private static readonly object gate = new object();

    private static readonly JsonDocumentOptions DocOptions = new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static Tables Load()
    {
        lock (gate)
        {
            if (tables != null) return tables;
            var t = new Tables();
            try
            {
                using Stream? stream = typeof(LegacyNames).Assembly.GetManifestResourceStream(FileName);
                if (stream != null && JsonNode.Parse(stream, documentOptions: DocOptions) is JsonObject root)
                {
                    Fill(t.Keys, root["keys"]);
                    Fill(t.Names, root["names"]);
                    Fill(t.Values, root["values"]);
                    Fill(t.BarkKeys, root["barkKeys"]);
                    Fill(t.Prefixes, root["prefixes"]);
                    Fill(t.Formats, root["formats"]);
                    if (root["nameDictionaries"] is JsonArray list)
                        foreach (JsonNode? n in list) if (n != null) t.NameDictionaries.Add(n.GetValue<string>());
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[LEGACY] could not read {FileName}: {e.Message}");
            }
            tables = t;
            return t;
        }
    }
    private static void Fill(Dictionary<string, string> target, JsonNode? node)
    {
        if (node is not JsonObject obj) return;
        foreach (KeyValuePair<string, JsonNode?> pair in obj)
            if (pair.Value != null) target[pair.Key] = pair.Value.GetValue<string>();
    }

    // Reads JSON (comments and trailing commas allowed), translates legacy names and deserializes it.
    public static T? Deserialize<T>(string json, JsonSerializerOptions options)
    {
        JsonNode? node = JsonNode.Parse(json, documentOptions: DocOptions);
        if (node == null) return default;
        return Translate(node).Deserialize<T>(options);
    }

    // Translates a parsed document in place and returns it.
    public static JsonNode Translate(JsonNode node)
    {
        Walk(Load(), node, null);
        return node;
    }

    // The current name of a map format id ("steambirds-esboco-mapa" -> "steambirds-map").
    public static string Format(string format) =>
        Load().Formats.TryGetValue(format, out string? f) ? f : format;

    private static void Walk(Tables t, JsonNode? node, string? parentKey)
    {
        if (node is JsonObject obj)
        {
            bool names = parentKey != null && t.NameDictionaries.Contains(parentKey);
            bool barks = string.Equals(parentKey, "barks", StringComparison.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, JsonNode?> pair in obj.ToList())
            {
                string key = pair.Key;
                Dictionary<string, string> map = names ? t.Names : barks ? t.BarkKeys : t.Keys;
                string newKey = map.TryGetValue(key, out string? k) ? k : key;
                JsonNode? value = pair.Value;
                if (newKey != key)
                {
                    obj.Remove(key);
                    if (obj.ContainsKey(newKey)) continue;   // the file already has the new key: it wins
                    obj[newKey] = value;
                }
                if (value is JsonValue v && v.TryGetValue(out string? s))
                {
                    string translated = TranslateValue(t, s);
                    if (translated != s) obj[newKey] = translated;
                }
                else
                {
                    Walk(t, value, names || barks ? null : newKey);
                }
            }
        }
        else if (node is JsonArray array)
        {
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is JsonValue v && v.TryGetValue(out string? s))
                {
                    string translated = TranslateValue(t, s);
                    if (translated != s) array[i] = translated;
                }
                else
                {
                    // Elements of a list are never "dictionaries of author-chosen names" (a wave's "groups" is a list).
                    Walk(t, array[i], null);
                }
            }
        }
    }

    // One string value: fixed values, content names, "+"-joined conditions ("morte:x+limpa:y") and "a|b" templates.
    private static string TranslateValue(Tables t, string s)
    {
        if (s.Length == 0) return s;
        if (t.Values.TryGetValue(s, out string? v)) return v;
        if (t.Names.TryGetValue(s, out string? n)) return n;
        if (t.Formats.TryGetValue(s, out string? f)) return f;
        if (s.Contains('|'))
            return string.Join("|", s.Split('|').Select(p => TranslateValue(t, p)));
        if (s.Contains(':'))
        {
            string[] parts = s.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                foreach (KeyValuePair<string, string> prefix in t.Prefixes)
                {
                    if (!parts[i].StartsWith(prefix.Key, StringComparison.OrdinalIgnoreCase)) continue;
                    string rest = parts[i].Substring(prefix.Key.Length);
                    parts[i] = prefix.Value + (t.Names.TryGetValue(rest, out string? r) ? r : rest);
                }
            }
            return string.Join("+", parts);
        }
        return s;
    }
}
