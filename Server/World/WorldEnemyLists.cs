using System.Text.Json;
using System.Text.Json.Serialization;

// The lists of enemies of each region of the open world that the host edits (the launcher: Server > World map > Enemies).
//
// The game's own lists (WorldEnemyPools.Build, from the units filed by region in the configBinary) are the default. world-enemies.json
// holds what the host changed; a region that is not in the file keeps the game's lists. For a region in the file:
//   "groups": the groups of enemies a spawn site of the region picks from. Each group has "leaders" and "members" (a group of one
//             leader and followers) or "solo": true (one enemy of "members" alone). When "groups" is missing the game's groups stay.
//   "boss":   the unit that "spawn boss here" (the launcher's map menu) creates in the region. Empty = the default boss of the region.
// The server also writes world-enemy-defaults.json (the game's lists, the boss choices and the zone names) so the launcher can show
// what is default, add to it and go back to it. The key of a region is the tile name of the map: beach, lowlands, lowlands2...
//
// The file is read again when it changes (checked about once per second), like world.json.
public static class WorldEnemyLists
{
    public const string FileName = "world-enemies.json";
    public const string DefaultsFileName = "world-enemy-defaults.json";

    public sealed class GroupEntry
    {
        public string Name { get; set; } = "";
        public bool Solo { get; set; }
        public List<string> Leaders { get; set; } = new List<string>();
        public List<string> Members { get; set; } = new List<string>();
    }

    public sealed class RegionEntry
    {
        public string Boss { get; set; } = "";
        public List<GroupEntry>? Groups { get; set; }
    }

    public sealed class FileContent
    {
        public string Note { get; set; } = "";
        public Dictionary<string, RegionEntry> Regions { get; set; } = new Dictionary<string, RegionEntry>(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly object gate = new object();
    private static FileContent current = new FileContent();
    private static DateTime readAt = DateTime.MinValue;
    private static DateTime nextCheck = DateTime.MinValue;
    private static bool existed;

    public static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), FileName);
    public static string DefaultsPath => Path.Combine(Directory.GetCurrentDirectory(), DefaultsFileName);

    // What the host changed, read again when the file changes.
    public static FileContent Current
    {
        get
        {
            lock (gate)
            {
                if (DateTime.UtcNow >= nextCheck)
                {
                    nextCheck = DateTime.UtcNow.AddSeconds(1);
                    ReloadIfChanged();
                }
                return current;
            }
        }
    }

    // The entry of a region (by its tile name), or null when the host did not change it.
    public static RegionEntry? Of(string regionName)
    {
        return Current.Regions.TryGetValue(regionName, out RegionEntry? entry) ? entry : null;
    }

    private static void ReloadIfChanged()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                if (existed) current = new FileContent();
                existed = false;
                readAt = DateTime.MinValue;
                return;
            }
            DateTime modified = File.GetLastWriteTimeUtc(FilePath);
            if (modified == readAt) return;
            readAt = modified;
            existed = true;
            FileContent? read = JsonSerializer.Deserialize<FileContent>(File.ReadAllText(FilePath), ReadOptions);
            if (read == null) return;
            read.Regions = new Dictionary<string, RegionEntry>(read.Regions ?? new Dictionary<string, RegionEntry>(), StringComparer.OrdinalIgnoreCase);
            foreach (RegionEntry entry in read.Regions.Values.Where(e => e != null))
            {
                entry.Boss = (entry.Boss ?? "").Trim();
                if (entry.Groups == null) continue;
                entry.Groups.RemoveAll(g => g == null);
                foreach (GroupEntry group in entry.Groups)
                {
                    group.Name ??= "";
                    group.Leaders = (group.Leaders ?? new List<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
                    group.Members = (group.Members ?? new List<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
                }
            }
            read.Regions = read.Regions.Where(p => p.Value != null).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
            current = read;
            WorldEnemyPools.Log("[world] " + FileName + " read: " + read.Regions.Count + " region(s) changed by the host" +
                (read.Regions.Count == 0 ? "" : " (" + string.Join(", ", read.Regions.Keys) + ")"));
        }
        catch (Exception e)
        {
            WorldEnemyPools.Log("[world] error in " + FileName + " (" + e.Message + "): keeping the previous lists");
        }
    }

    // Writes world-enemy-defaults.json: for each region of the map its zone, tier, the game's groups and the boss choices.
    public static void WriteDefaults(IEnumerable<WorldData.Region> regions, Action<string, string> log)
    {
        try
        {
            var file = new Dictionary<string, object>();
            foreach (WorldData.Region region in regions.OrderBy(r => r.SortOrder))
            {
                List<WorldEnemyPools.Family> groups = WorldEnemyPools.DefaultsFor(region);
                file[region.Name] = new
                {
                    zone = region.ZoneName,
                    tier = region.Tier,
                    boss = WorldEnemyPools.DefaultBossFor(region) ?? "",
                    bossChoices = WorldEnemyPools.BossChoicesFor(region).Select(b => b.Name).ToList(),
                    bosses = WorldEnemyPools.BossChoicesFor(region).Select(b => new { name = b.Name, parts = b.Parts }).ToList(),
                    groups = groups.Select(g => new GroupEntry
                    {
                        Name = g.Name,
                        Solo = g.Solo,
                        Leaders = g.Leaders.ToList(),
                        Members = g.Members.ToList()
                    }).ToList()
                };
            }
            string text = JsonSerializer.Serialize(new { note = "Written by the server: the game's own enemy lists of each region. Edit world-enemies.json, not this file.", regions = file }, WriteOptions);
            string temporary = DefaultsPath + ".tmp";
            File.WriteAllText(temporary, text);
            File.Move(temporary, DefaultsPath, overwrite: true);
        }
        catch (Exception e)
        {
            log("WORLD", "could not write " + DefaultsFileName + ": " + e.Message);
        }
    }
}
