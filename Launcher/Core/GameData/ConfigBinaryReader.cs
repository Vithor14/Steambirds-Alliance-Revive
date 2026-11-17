using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace SteambirdsLauncher.Core.GameData;

// Reader for the game's configBinary (Steambirds_Data\StreamingAssets\configBinary): every game config in one file,
// loaded by the client with DarkConfig. Copy of the parsing part of Server\GameData\ConfigBinary.cs
// (format described there). The launcher uses it for lists the screens show: game languages, plane classes and
// unlocks, currencies, ingredients and the XP tables.
public static class ConfigBinaryReader
{
    private const int Magic = 860704227;

    public static Dictionary<string, ConfigNode> Parse(byte[] raw)
    {
        if (raw.Length < 7) throw new InvalidDataException("file too short");
        int magic = BitConverter.ToInt32(raw, 0);
        if (magic != Magic) throw new InvalidDataException("unknown magic number " + magic);
        byte version = raw[4];
        if (version != 1) throw new InvalidDataException("unknown version " + version);
        byte compression = raw[5];
        byte encryption = raw[6];
        if (encryption != 0) throw new NotSupportedException("encrypted configBinary (type " + encryption + ")");

        byte[] body;
        switch (compression)
        {
            case 0:
                body = new byte[raw.Length - 7];
                Array.Copy(raw, 7, body, 0, body.Length);
                break;
            case 1:
                body = Inflate(new DeflateStream(new MemoryStream(raw, 7, raw.Length - 7), CompressionMode.Decompress));
                break;
            case 2:
                body = Inflate(new GZipStream(new MemoryStream(raw, 7, raw.Length - 7), CompressionMode.Decompress));
                break;
            case 3:
                if (raw.Length < 20) throw new InvalidDataException("incomplete LZMA header");
                var props = new byte[5];
                Array.Copy(raw, 7, props, 0, 5);
                long size = BitConverter.ToInt64(raw, 12);
                body = LzmaDecoder.Decode(props, raw, 20, size);
                break;
            default:
                throw new NotSupportedException("unknown compression " + compression);
        }

        var result = new Dictionary<string, ConfigNode>(StringComparer.Ordinal);
        using var reader = new BinaryReader(new MemoryStream(body), Encoding.UTF8);

        int stringCount = reader.ReadInt32();
        var strings = new Dictionary<int, string>(Math.Max(0, stringCount));
        for (int i = 0; i < stringCount; i++)
        {
            int id = ReadCompressedInt(reader);
            strings[id] = reader.ReadString();
        }

        int fileCount = reader.ReadUInt16();
        for (int i = 0; i < fileCount; i++)
        {
            string name = reader.ReadString();
            reader.ReadInt32();   // checksum
            reader.ReadInt32();   // size
            reader.ReadInt64();   // modified
            result[name] = ReadNode(reader, strings);
        }
        return result;
    }

    private static byte[] Inflate(Stream stream)
    {
        using (stream)
        using (var output = new MemoryStream())
        {
            stream.CopyTo(output);
            return output.ToArray();
        }
    }

    private static int ReadCompressedInt(BinaryReader reader)
    {
        int value = 0;
        int shift = 0;
        byte b;
        do
        {
            b = reader.ReadByte();
            value |= (b & 0x7F) << shift;
            shift += 7;
        }
        while ((b & 0x80) != 0);
        return value;
    }

    private static string? ReadText(BinaryReader reader, Dictionary<int, string> strings, byte kind)
    {
        switch (kind)
        {
            case 0:
                return null;
            case 240:
                return reader.ReadString();
            case 255:
                int id = ReadCompressedInt(reader);
                return strings.TryGetValue(id, out string? text) ? text : null;
            default:
                throw new InvalidDataException("unknown text marker " + kind);
        }
    }

    private static ConfigNode ReadNode(BinaryReader reader, Dictionary<int, string> strings)
    {
        byte type = reader.ReadByte();
        switch (type)
        {
            case 1:
            {
                int count = ReadCompressedInt(reader);
                var node = new ConfigNode(ConfigNodeType.Dictionary);
                for (int i = 0; i < count; i++)
                {
                    string key = ReadText(reader, strings, reader.ReadByte()) ?? "";
                    node.Set(key, ReadNode(reader, strings));
                }
                return node;
            }
            case 2:
            {
                int count = ReadCompressedInt(reader);
                var node = new ConfigNode(ConfigNodeType.List);
                for (int i = 0; i < count; i++) node.Add(ReadNode(reader, strings));
                return node;
            }
            case 3:
            {
                var node = new ConfigNode(ConfigNodeType.Scalar);
                node.Value = ReadText(reader, strings, reader.ReadByte());
                return node;
            }
            default:
                return new ConfigNode(ConfigNodeType.Invalid);
        }
    }
}

public enum ConfigNodeType
{
    Invalid = 0,
    Dictionary = 1,
    List = 2,
    Scalar = 3
}

// One DarkConfig DocNode. Indexers never return null: a missing key or index gives ConfigNode.Missing,
// so lookups can be chained: ConfigBinary.Get("global")["xpTargets"][0]["xp"].AsInt(0).
public sealed class ConfigNode
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static readonly ConfigNode Missing = new ConfigNode(ConfigNodeType.Invalid);

    private Dictionary<string, ConfigNode>? map;
    private List<ConfigNode>? list;

    public ConfigNode(ConfigNodeType type)
    {
        Type = type;
    }

    public ConfigNodeType Type { get; }
    public string? Value { get; set; }

    public bool IsMissing => Type == ConfigNodeType.Invalid;
    public bool IsDictionary => Type == ConfigNodeType.Dictionary;
    public bool IsList => Type == ConfigNodeType.List;
    public bool IsScalar => Type == ConfigNodeType.Scalar;

    public int Count => map != null ? map.Count : (list != null ? list.Count : 0);

    public IEnumerable<KeyValuePair<string, ConfigNode>> Pairs =>
        map != null ? (IEnumerable<KeyValuePair<string, ConfigNode>>)map : Enumerable.Empty<KeyValuePair<string, ConfigNode>>();

    // List items. For a dictionary: its values (same as DocNode.Values in DarkConfig).
    public IEnumerable<ConfigNode> Items
    {
        get
        {
            if (list != null) return list;
            if (map != null) return map.Values;
            return Enumerable.Empty<ConfigNode>();
        }
    }

    public ConfigNode this[string key]
    {
        get
        {
            if (map == null) return Missing;
            if (map.TryGetValue(key, out ConfigNode? node)) return node;
            // DarkConfig matches field names without caring about case.
            foreach (KeyValuePair<string, ConfigNode> pair in map)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) return pair.Value;
            }
            return Missing;
        }
    }

    public ConfigNode this[int index]
    {
        get
        {
            if (list == null || index < 0 || index >= list.Count) return Missing;
            return list[index];
        }
    }

    public bool Has(string key) => !this[key].IsMissing;

    internal void Set(string key, ConfigNode value)
    {
        map ??= new Dictionary<string, ConfigNode>(StringComparer.Ordinal);
        map[key] = value;
    }

    internal void Add(ConfigNode value)
    {
        list ??= new List<ConfigNode>();
        list.Add(value);
    }

    public string? AsString() => IsScalar ? Value : null;

    public string AsString(string fallback) => IsScalar && Value != null ? Value : fallback;

    public int AsInt(int fallback)
    {
        if (!IsScalar || string.IsNullOrWhiteSpace(Value)) return fallback;
        if (int.TryParse(Value, NumberStyles.Integer, Inv, out int whole)) return whole;
        if (double.TryParse(Value, NumberStyles.Float, Inv, out double real)) return (int)real;
        return fallback;
    }

    public int? AsNullableInt()
    {
        if (!IsScalar || string.IsNullOrWhiteSpace(Value)) return null;
        if (int.TryParse(Value, NumberStyles.Integer, Inv, out int whole)) return whole;
        if (double.TryParse(Value, NumberStyles.Float, Inv, out double real)) return (int)real;
        return null;
    }

    public float AsFloat(float fallback)
    {
        if (!IsScalar || string.IsNullOrWhiteSpace(Value)) return fallback;
        return double.TryParse(Value, NumberStyles.Float, Inv, out double real) ? (float)real : fallback;
    }

    public bool AsBool(bool fallback)
    {
        if (!IsScalar || string.IsNullOrWhiteSpace(Value)) return fallback;
        string text = Value.Trim();
        if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) || text == "1") return true;
        if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) || text == "0") return false;
        return fallback;
    }

    // DarkConfig TimeSpans are written like "0:02" (2 minutes) or "0:00:10" (10 seconds).
    public TimeSpan AsTimeSpan(TimeSpan fallback)
    {
        if (!IsScalar || string.IsNullOrWhiteSpace(Value)) return fallback;
        return TimeSpan.TryParse(Value.Trim(), Inv, out TimeSpan span) ? span : fallback;
    }

    public List<string> AsStringList()
    {
        var result = new List<string>();
        if (IsScalar && Value != null)
        {
            result.Add(Value);
            return result;
        }
        foreach (ConfigNode item in Items)
        {
            if (item.IsScalar && item.Value != null) result.Add(item.Value);
        }
        return result;
    }

    // Compact JSON-like text, for the diagnostic dump in the server log. Stops after maxChars characters.
    public string ToJson(int maxChars = 4000)
    {
        var sb = new StringBuilder();
        WriteJson(sb, maxChars);
        if (sb.Length > maxChars)
        {
            sb.Length = maxChars;
            sb.Append("...");
        }
        return sb.ToString();
    }

    private void WriteJson(StringBuilder sb, int maxChars)
    {
        if (sb.Length > maxChars) return;
        switch (Type)
        {
            case ConfigNodeType.Dictionary:
            {
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, ConfigNode> pair in Pairs)
                {
                    if (sb.Length > maxChars) return;
                    if (!first) sb.Append(", ");
                    first = false;
                    sb.Append(pair.Key).Append(": ");
                    pair.Value.WriteJson(sb, maxChars);
                }
                sb.Append('}');
                break;
            }
            case ConfigNodeType.List:
            {
                sb.Append('[');
                bool first = true;
                foreach (ConfigNode item in Items)
                {
                    if (sb.Length > maxChars) return;
                    if (!first) sb.Append(", ");
                    first = false;
                    item.WriteJson(sb, maxChars);
                }
                sb.Append(']');
                break;
            }
            case ConfigNodeType.Scalar:
                sb.Append(Value ?? "null");
                break;
            default:
                sb.Append("?");
                break;
        }
    }
}
