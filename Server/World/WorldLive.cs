using System.Text.Json;

// What the launcher's "World map" page reads: two small files in the server folder.
//   world-map.json   written once when the world is built: the region of every cell (the launcher draws the picture from it)
//   world-live.json  written every second while the world has players: who is where, the carriers and the counts
// "active": false in world-live.json means nobody is in the world (also written when the server starts, so an old file never
// shows players that are gone). Files are written to a temporary name and moved over the old one, so the launcher never reads half of one.
public static class WorldLive
{
    public const string MapFileName = "world-map.json";
    public const string LiveFileName = "world-live.json";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

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

    public sealed class LiveFile
    {
        public bool Active { get; set; }
        // true: the world is built and waits for its first player (Active is false).
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
    }

    public static string MapPath => Path.Combine(Directory.GetCurrentDirectory(), MapFileName);
    public static string LivePath => Path.Combine(Directory.GetCurrentDirectory(), LiveFileName);

    // The picture of the world: one character per cell, a row per b index, a column per a index (base 36 index into "legend";
    // "_" = outside the world). The launcher turns world coordinates into cells with the same formulas as Ground.cs.
    public static void WriteMap(WorldMap map, float spawnX, float spawnY, Action<string, string> log)
    {
        try
        {
            var legend = new List<object>
            {
                new { name = "water", zone = "Water", @base = "water", tier = 0, variant = 1 },
                new { name = "canyonFloor", zone = "Canyon", @base = "canyon", tier = 0, variant = 1 }
            };
            var index = new Dictionary<WorldData.Region, int>();
            foreach (WorldData.Region region in WorldData.Regions.Where(r => r.SortOrder >= 0))
            {
                index[region] = legend.Count;
                legend.Add(new { name = region.Name, zone = region.ZoneName, @base = region.Base, tier = region.Tier, variant = region.Variant });
            }

            var rows = new List<string>(map.Cells);
            for (int b = 0; b < map.Cells; b++)
            {
                var row = new char[map.Cells];
                for (int a = 0; a < map.Cells; a++)
                {
                    int code;
                    if (!map.InWorld[a, b]) code = -1;
                    else if (map.RegionAt[a, b] is WorldData.Region r && index.TryGetValue(r, out int i)) code = i;
                    else code = map.Kind[a, b] == WorldData.CanyonFloor ? 1 : 0;
                    row[a] = code < 0 ? '_' : "0123456789abcdefghijklmnopqrstuvwxyz"[code];
                }
                rows.Add(new string(row));
            }

            var file = new
            {
                seed = map.Seed,
                radius = map.Radius,
                half = map.Half,
                cells = map.Cells,
                cellSize = Ground.CellSize,
                spawn = new { x = spawnX, y = spawnY },
                legend,
                rows
            };
            Write(MapPath, JsonSerializer.Serialize(file, Options));
        }
        catch (Exception e)
        {
            log("WORLD", "could not write " + MapFileName + ": " + e.Message);
        }
    }

    public static void WriteLive(LiveFile live, Action<string, string> log)
    {
        try
        {
            live.Updated = DateTime.Now.ToString("o");
            Write(LivePath, JsonSerializer.Serialize(live, Options));
        }
        catch (Exception e)
        {
            log("WORLD", "could not write " + LiveFileName + ": " + e.Message);
        }
    }

    // The world is built and waits for its first player: the launcher shows its map and that it is ready.
    public static void WriteStandby(float radius, int seed, int sites, string name, Action<string, string> log)
    {
        WriteLive(new LiveFile { Active = false, Standby = true, Name = name, Seed = seed, Radius = radius, Sites = sites }, log);
    }

    public static void WriteInactive(Action<string, string> log)
    {
        WriteLive(new LiveFile { Active = false }, log);
    }

    private static void Write(string path, string text)
    {
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, overwrite: true);
    }
}
