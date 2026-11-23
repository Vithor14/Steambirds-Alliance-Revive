using System.Text.Json;

namespace SteambirdsLauncher.Core;

// The two files the server writes for the "World map" page (Server\World\WorldLive.cs):
//   world-map.json   the region of every cell of the open world, written when the world is built
//   world-live.json  the players, carriers and counts, written every second while somebody is in the world
public sealed class WorldMapData
{
    public sealed class LegendEntry
    {
        public string Name { get; set; } = "";
        public string Base { get; set; } = "";
        public string Zone { get; set; } = "";
        public int Tier { get; set; }
        public int Variant { get; set; } = 1;
    }

    public sealed class Point2
    {
        public float X { get; set; }
        public float Y { get; set; }
    }

    public int Seed { get; set; }
    public float Radius { get; set; } = 650f;
    public int Half { get; set; }
    public int Cells { get; set; }
    public float CellSize { get; set; } = 12f;
    public Point2 Spawn { get; set; } = new Point2();
    public List<LegendEntry> Legend { get; set; } = new List<LegendEntry>();
    public string[] Rows { get; set; } = Array.Empty<string>();

    public static string FilePath => LauncherPaths.ServerFile("world-map.json");

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    private static readonly float Scale = 12f / MathF.Sqrt(2f);

    public static DateTime Stamp() => File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;

    // Null (and the reason) when the file is missing or unreadable.
    public static WorldMapData? Load(out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(FilePath))
            {
                error = L.T("The server has not built the open world yet.");
                return null;
            }
            WorldMapData? data = JsonSerializer.Deserialize<WorldMapData>(TextFiles.ReadShared(FilePath), Options);
            if (data == null || data.Rows.Length == 0 || data.Cells <= 0)
            {
                error = L.T("world-map.json is empty.");
                return null;
            }
            return data;
        }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }
    }

    // Index into Legend of the cell under a world position; -1 outside the world.
    public int CodeAt(float x, float y)
    {
        int a = (int)MathF.Floor((x + y) / (2f * Scale)) + Half;
        int b = (int)MathF.Floor((y - x) / (2f * Scale)) + Half;
        if (a < 0 || b < 0 || a >= Cells || b >= Cells || b >= Rows.Length || a >= Rows[b].Length) return -1;
        char c = Rows[b][a];
        if (c == '_') return -1;
        int value = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'z' ? c - 'a' + 10 : -1;
        return value;
    }
}

public sealed class WorldLiveData
{
    public sealed class PlayerInfo
    {
        public string Name { get; set; } = "";
        public float? X { get; set; }
        public float? Y { get; set; }
        public float? Rotation { get; set; }
        public bool Alive { get; set; }
        public float? Health { get; set; }
        public string Region { get; set; } = "";
        public int? Tier { get; set; }
        public float? Elevation { get; set; }
        public float? Distance { get; set; }
        public string PlaneClass { get; set; } = "";
        public string PlaneVariant { get; set; } = "";
        public int? PlaneLevel { get; set; }
        public int? PilotRank { get; set; }
        public int Shots { get; set; }
        public int Hits { get; set; }
        public int SecondsInWorld { get; set; }
    }

    public sealed class CarrierInfo
    {
        public string Label { get; set; } = "";
        public string Type { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public int SecondsLeft { get; set; }
    }

    public bool Active { get; set; }
    // The world is built and waits for its first player.
    public bool Standby { get; set; }
    public string Updated { get; set; } = "";
    public string Name { get; set; } = "";
    public int Seed { get; set; }
    public float Radius { get; set; }
    public int UptimeSeconds { get; set; }
    public int Sites { get; set; }
    public int ActiveSites { get; set; }
    public int EnemiesAlive { get; set; }
    public int SitesCleared { get; set; }
    public List<PlayerInfo> Players { get; set; } = new List<PlayerInfo>();
    public List<CarrierInfo> Carriers { get; set; } = new List<CarrierInfo>();

    public static string FilePath => LauncherPaths.ServerFile("world-live.json");

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    // How old the data is (the server writes every second). Null when the file is missing.
    public TimeSpan? Age => DateTime.TryParse(Updated, out DateTime when) ? DateTime.Now - when : null;

    // Null when the file is missing or half written (the next read gets it).
    public static WorldLiveData? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            return JsonSerializer.Deserialize<WorldLiveData>(TextFiles.ReadShared(FilePath), Options);
        }
        catch
        {
            return null;
        }
    }
}
