using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SteambirdsLauncher.Editor;

// Small helpers to read and write the map file as a JsonObject tree.
//
// The editor keeps the whole map file as a JSON tree (not as fixed C# classes), so every field it does not know about
// (notes, fields of newer server versions, the future "ai" of the enemy types) is kept when the file is saved again.
internal static class Json
{
    // ------------------------------------------------------------------ reading

    public static string AsString(JsonNode? node, string fallback = "")
    {
        if (node is JsonValue value && value.TryGetValue(out string? text) && text != null) return text;
        return fallback;
    }

    public static double AsNumber(JsonNode? node, double fallback = 0)
    {
        if (node is not JsonValue v) return fallback;
        // A value made by the editor keeps its C# type (int, double...); a value read from the file is a JsonElement.
        if (v.TryGetValue(out double d)) return d;
        if (v.TryGetValue(out int i)) return i;
        if (v.TryGetValue(out long l)) return l;
        if (v.TryGetValue(out float f)) return f;
        if (v.TryGetValue(out decimal m)) return (double)m;
        if (v.TryGetValue(out short s)) return s;
        if (v.TryGetValue(out byte b)) return b;
        if (v.TryGetValue(out string? text) && text != null &&
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)) return parsed;
        return fallback;
    }

    public static bool AsBool(JsonNode? node, bool fallback = false)
    {
        if (node is JsonValue v && v.TryGetValue(out bool flag)) return flag;
        return fallback;
    }

    public static bool IsNumber(JsonNode? node)
    {
        if (node is not JsonValue v) return false;
        return v.TryGetValue(out double _) || v.TryGetValue(out int _) || v.TryGetValue(out long _) ||
               v.TryGetValue(out float _) || v.TryGetValue(out decimal _) || v.TryGetValue(out short _) || v.TryGetValue(out byte _);
    }

    public static string Str(JsonObject? obj, string key, string fallback = "") => AsString(obj?[key], fallback);
    public static double Num(JsonObject? obj, string key, double fallback = 0) => AsNumber(obj?[key], fallback);
    public static int Int(JsonObject? obj, string key, int fallback = 0) => (int)Math.Round(AsNumber(obj?[key], fallback));
    public static bool Bool(JsonObject? obj, string key, bool fallback = false) => AsBool(obj?[key], fallback);
    public static bool Has(JsonObject? obj, string key) => obj != null && obj[key] != null;

    public static JsonObject? ObjOrNull(JsonObject? parent, string key) => parent?[key] as JsonObject;
    public static JsonArray? ArrOrNull(JsonObject? parent, string key) => parent?[key] as JsonArray;

    // Objects of an array (anything that is not an object is skipped).
    public static IEnumerable<JsonObject> Objects(JsonArray? array)
    {
        if (array == null) yield break;
        foreach (JsonNode? node in array)
        {
            if (node is JsonObject obj) yield return obj;
        }
    }

    // Properties of an object whose value is an object.
    public static IEnumerable<KeyValuePair<string, JsonObject>> ObjectProperties(JsonObject? parent)
    {
        if (parent == null) yield break;
        foreach (KeyValuePair<string, JsonNode?> pair in parent)
        {
            if (pair.Value is JsonObject obj) yield return new KeyValuePair<string, JsonObject>(pair.Key, obj);
        }
    }

    public static List<string> Strings(JsonNode? node)
    {
        var list = new List<string>();
        if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                string s = AsString(item);
                if (s.Length > 0) list.Add(s);
            }
        }
        return list;
    }

    public static List<int> Ints(JsonNode? node)
    {
        var list = new List<int>();
        if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                if (IsNumber(item)) list.Add((int)Math.Round(AsNumber(item)));
            }
        }
        return list;
    }

    // ------------------------------------------------------------------ writing

    // The child object of "parent" (created when it does not exist yet or is not an object).
    public static JsonObject Obj(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject existing) return existing;
        var created = new JsonObject();
        parent[key] = created;
        return created;
    }

    public static JsonArray Arr(JsonObject parent, string key)
    {
        if (parent[key] is JsonArray existing) return existing;
        var created = new JsonArray();
        parent[key] = created;
        return created;
    }

    public static void SetStr(JsonObject obj, string key, string value) => obj[key] = value;

    // Coordinates are kept with two decimals (0.01 u), like the generated maps.
    public static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    public static void SetNum(JsonObject obj, string key, double value) => obj[key] = Round(value);

    public static void SetInt(JsonObject obj, string key, int value) => obj[key] = value;

    public static void SetBool(JsonObject obj, string key, bool value) => obj[key] = value;

    public static JsonArray IntArray(IEnumerable<int> values)
    {
        var array = new JsonArray();
        foreach (int value in values) array.Add(JsonValue.Create(value));
        return array;
    }

    public static JsonArray StringArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (string value in values) array.Add(JsonValue.Create(value));
        return array;
    }

    // Removes the property when the text is empty (keeps the file short), otherwise sets it.
    public static void SetOrRemove(JsonObject obj, string key, string value)
    {
        if (string.IsNullOrEmpty(value)) obj.Remove(key);
        else obj[key] = value;
    }

    // "u", "v" and "rot" of a point.
    public static void SetPoint(JsonObject obj, double u, double v, double? rot = null)
    {
        obj["u"] = Round(u);
        obj["v"] = Round(v);
        if (rot.HasValue) obj["rot"] = Round(rot.Value);
    }

    public static JsonObject NewPoint(double u, double v, double rot)
    {
        var obj = new JsonObject();
        SetPoint(obj, u, v, rot);
        return obj;
    }

    // Name that does not exist yet in the dictionary ("name", "name 2", "name 3"...).
    public static string UniqueName(IEnumerable<string> existing, string wanted)
    {
        var used = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(wanted)) return wanted;
        for (int n = 2; ; n++)
        {
            string candidate = wanted + " " + n.ToString(CultureInfo.InvariantCulture);
            if (!used.Contains(candidate)) return candidate;
        }
    }

    // Renames a property keeping its place in the object.
    public static void RenameProperty(JsonObject obj, string oldName, string newName)
    {
        if (oldName == newName || !obj.ContainsKey(oldName)) return;
        var pairs = new List<KeyValuePair<string, JsonNode?>>();
        foreach (KeyValuePair<string, JsonNode?> pair in obj) pairs.Add(pair);
        foreach (KeyValuePair<string, JsonNode?> pair in pairs) obj.Remove(pair.Key);
        foreach (KeyValuePair<string, JsonNode?> pair in pairs)
        {
            obj[pair.Key == oldName ? newName : pair.Key] = pair.Value;
        }
    }
}

// Writer of the map file. Same look as the generated maps: short objects and lists stay on one line
// ({"u": 6, "v": 3, "rot": 45}), the rest is indented by two spaces.
internal static class CompactJson
{
    private const int InlineLimit = 150;

    private static readonly JsonSerializerOptions Escape = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Write(JsonNode? node)
    {
        var text = new StringBuilder();
        WriteNode(text, node, 0, forceMultiline: true);
        text.Append('\n');
        return text.ToString();
    }

    private static void WriteNode(StringBuilder text, JsonNode? node, int indent, bool forceMultiline)
    {
        if (!forceMultiline)
        {
            string? inline = TryInline(node, 2);
            if (inline != null && inline.Length <= InlineLimit)
            {
                text.Append(inline);
                return;
            }
        }

        string pad = new string(' ', indent + 2);
        string closePad = new string(' ', indent);
        switch (node)
        {
            case JsonObject obj:
                if (obj.Count == 0)
                {
                    text.Append("{}");
                    return;
                }
                text.Append("{\n");
                int index = 0;
                foreach (KeyValuePair<string, JsonNode?> pair in obj)
                {
                    text.Append(pad).Append(Leaf(JsonValue.Create(pair.Key))).Append(": ");
                    WriteNode(text, pair.Value, indent + 2, false);
                    index++;
                    text.Append(index < obj.Count ? ",\n" : "\n");
                }
                text.Append(closePad).Append('}');
                return;
            case JsonArray array:
                if (array.Count == 0)
                {
                    text.Append("[]");
                    return;
                }
                text.Append("[\n");
                for (int i = 0; i < array.Count; i++)
                {
                    text.Append(pad);
                    WriteNode(text, array[i], indent + 2, false);
                    text.Append(i < array.Count - 1 ? ",\n" : "\n");
                }
                text.Append(closePad).Append(']');
                return;
            default:
                text.Append(Leaf(node));
                return;
        }
    }

    // One-line text of a node, or null when it nests deeper than "depth" levels.
    private static string? TryInline(JsonNode? node, int depth)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                if (depth < 0) return null;
                if (obj.Count == 0) return "{}";
                var parts = new List<string>();
                foreach (KeyValuePair<string, JsonNode?> pair in obj)
                {
                    string? value = TryInline(pair.Value, depth - 1);
                    if (value == null) return null;
                    parts.Add(Leaf(JsonValue.Create(pair.Key)) + ": " + value);
                }
                return "{" + string.Join(", ", parts) + "}";
            }
            case JsonArray array:
            {
                if (depth < 0) return null;
                if (array.Count == 0) return "[]";
                var parts = new List<string>();
                foreach (JsonNode? item in array)
                {
                    string? value = TryInline(item, depth - 1);
                    if (value == null) return null;
                    parts.Add(value);
                }
                return "[" + string.Join(", ", parts) + "]";
            }
            default:
                return Leaf(node);
        }
    }

    private static string Leaf(JsonNode? node)
    {
        return node == null ? "null" : node.ToJsonString(Escape);
    }
}
