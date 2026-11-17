using System.Text.Json;

namespace SteambirdsLauncher.Core;

// server\network.json (Server\Network\ServerNetworkSettings.cs): name and ports of the server of this PC.
// The launcher reads it to open the game on the right port, to check which ports are busy and to show the addresses
// other players use. The Server tab edits it (Network and multiplayer). Missing file or keys = the defaults.
public sealed class ServerNetworkFile
{
    public const string FileName = "network.json";
    public const int DefaultHttpPort = 6443;
    public const int DefaultNexusPort = 1111;
    public const int DefaultTutorialPort = 1112;
    public const int DefaultDungeonPort = 1113;
    public const int DefaultSearchPort = 6444;

    public static string FilePath => LauncherPaths.ServerFile(FileName);

    public string ServerName { get; private set; } = "Steambirds server";
    public bool AllowOtherPlayers { get; private set; } = true;
    public bool ShowInServerSearch { get; private set; } = true;
    public int MaxPlayers { get; private set; } = 16;
    public int HttpPort { get; private set; } = DefaultHttpPort;
    public int NexusPort { get; private set; } = DefaultNexusPort;
    public int TutorialPort { get; private set; } = DefaultTutorialPort;
    public int DungeonPort { get; private set; } = DefaultDungeonPort;
    public int SearchPort { get; private set; } = DefaultSearchPort;

    // Error reading the file (the defaults are used), or null.
    public string? Error { get; private set; }

    private static readonly object Gate = new object();
    private static ServerNetworkFile? cached;
    private static DateTime cachedStamp = DateTime.MinValue;

    // Current values (the file is read again when it changes).
    public static ServerNetworkFile Read()
    {
        lock (Gate)
        {
            DateTime stamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
            if (cached != null && stamp == cachedStamp) return cached;
            cached = Load();
            cachedStamp = stamp;
            return cached;
        }
    }

    public string PortsDescription =>
        L.F("HTTP {0} (TCP) · Nexus {1} · Tutorial {2} · Dungeon {3} · search {4} (UDP)", HttpPort, NexusPort, TutorialPort, DungeonPort, SearchPort);

    private static ServerNetworkFile Load()
    {
        var file = new ServerNetworkFile();
        if (!File.Exists(FilePath)) return file;
        try
        {
            using JsonDocument document = JsonDocument.Parse(TextFiles.ReadShared(FilePath), JsoncFile.ParseOptions);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return file;
            file.ServerName = ReadString(root, "serverName") ?? file.ServerName;
            file.AllowOtherPlayers = ReadBool(root, "allowOtherPlayers") ?? file.AllowOtherPlayers;
            file.ShowInServerSearch = ReadBool(root, "showInServerSearch") ?? file.ShowInServerSearch;
            file.MaxPlayers = Math.Max(1, ReadInt(root, "maxPlayers") ?? file.MaxPlayers);
            file.HttpPort = Port(ReadInt(root, "httpPort"), DefaultHttpPort);
            file.NexusPort = Port(ReadInt(root, "nexusPort"), DefaultNexusPort);
            file.TutorialPort = Port(ReadInt(root, "tutorialPort"), DefaultTutorialPort);
            file.DungeonPort = Port(ReadInt(root, "dungeonPort"), DefaultDungeonPort);
            file.SearchPort = Port(ReadInt(root, "searchPort"), DefaultSearchPort);
        }
        catch (Exception e)
        {
            file.Error = e.Message;
        }
        return file;
    }

    private static int Port(int? value, int fallback) => value is >= 1 and <= 65535 ? value.Value : fallback;

    private static JsonElement? Find(JsonElement root, string key)
    {
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, key, StringComparison.OrdinalIgnoreCase)) return property.Value;
        }
        return null;
    }

    private static string? ReadString(JsonElement root, string key) =>
        Find(root, key) is JsonElement v && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? ReadBool(JsonElement root, string key) =>
        Find(root, key) is JsonElement v && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) ? v.GetBoolean() : null;

    private static int? ReadInt(JsonElement root, string key) =>
        Find(root, key) is JsonElement v && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n) ? n : null;
}
