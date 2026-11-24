using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteambirdsLauncher.Core;

// The enemy lists of the open world, as the server reads them (Server\World\WorldEnemyLists.cs):
//   world-enemy-defaults.json  written by the server: for each region of the map its zone, the game's own groups and the boss choices
//   world-enemies.json         written here: what the host changed. A region that is not in it keeps the game's lists.
// And the requests of the world map (Server\World\WorldServer.Requests.cs): one small file per request in world-requests\, answered in
// world-request-result.json.
public sealed class WorldEnemyGroup
{
    public string Name { get; set; } = "";
    // Solo: one enemy of the list alone (the "members"). Otherwise a leader and its followers.
    public bool Solo { get; set; }
    public List<string> Leaders { get; set; } = new List<string>();
    public List<string> Members { get; set; } = new List<string>();

    public WorldEnemyGroup Copy() => new WorldEnemyGroup
    {
        Name = Name,
        Solo = Solo,
        Leaders = Leaders.ToList(),
        Members = Members.ToList()
    };
}

// A boss of a biome: the unit that is the boss and the parts and minions that come with it.
public sealed class WorldBossGroup
{
    public string Name { get; set; } = "";
    public List<string> Parts { get; set; } = new List<string>();
}

public sealed class WorldEnemyRegion
{
    // The tile name of the region (beach, lowlands, lowlands2...). Not in the file: it is the key.
    [JsonIgnore] public string Key { get; set; } = "";
    public string Zone { get; set; } = "";
    public int Tier { get; set; }
    public string Boss { get; set; } = "";
    public List<string> BossChoices { get; set; } = new List<string>();
    // The bosses of the biome with their parts (the server writes it in world-enemy-defaults.json).
    public List<WorldBossGroup> Bosses { get; set; } = new List<WorldBossGroup>();
    public List<WorldEnemyGroup>? Groups { get; set; }
    // The AI file each unit of the biome runs (unit name -> AI id). A unit that is not in it runs the default AI of the open world.
    public Dictionary<string, string>? Ai { get; set; }
}

public sealed class WorldEnemyFiles
{
    public const string EditsFileName = "world-enemies.json";
    public const string DefaultsFileName = "world-enemy-defaults.json";

    private sealed class RegionsFile
    {
        public string Note { get; set; } = "";
        public Dictionary<string, WorldEnemyRegion> Regions { get; set; } = new Dictionary<string, WorldEnemyRegion>(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string EditsPath => LauncherPaths.ServerFile(EditsFileName);
    public static string DefaultsPath => LauncherPaths.ServerFile(DefaultsFileName);

    // The game's own lists, in the order of the map (null and the reason when the server has not written them yet).
    public static List<WorldEnemyRegion>? LoadDefaults(out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(DefaultsPath))
            {
                error = L.T("The server has not written the game's enemy lists yet. Start the server once (it builds the open world and writes them).");
                return null;
            }
            RegionsFile? file = JsonSerializer.Deserialize<RegionsFile>(TextFiles.ReadShared(DefaultsPath), Options);
            if (file == null || file.Regions.Count == 0)
            {
                error = L.T("world-enemy-defaults.json is empty.");
                return null;
            }
            var list = new List<WorldEnemyRegion>();
            foreach (KeyValuePair<string, WorldEnemyRegion> pair in file.Regions)
            {
                pair.Value.Key = pair.Key;
                pair.Value.Groups ??= new List<WorldEnemyGroup>();
                list.Add(pair.Value);
            }
            return list;
        }
        catch (Exception e)
        {
            error = e.Message;
            return null;
        }
    }

    // What the file keeps of a region the host changed.
    private sealed class EditRegion
    {
        public string Boss { get; set; } = "";
        public List<WorldEnemyGroup>? Groups { get; set; }
        public Dictionary<string, string>? Ai { get; set; }
    }

    // The AI every open-world unit runs unless one is picked for it (Server\World\WorldEnemyLists.DefaultAi).
    public const string DefaultAi = "generic_chase";

    private sealed class EditsFile
    {
        public string Note { get; set; } = "";
        public Dictionary<string, EditRegion> Regions { get; set; } = new Dictionary<string, EditRegion>(StringComparer.OrdinalIgnoreCase);
    }

    // What the host changed (empty when the file does not exist). Throws when the file cannot be read.
    public static Dictionary<string, WorldEnemyRegion> LoadEdits()
    {
        var result = new Dictionary<string, WorldEnemyRegion>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(EditsPath)) return result;
        EditsFile? file = JsonSerializer.Deserialize<EditsFile>(TextFiles.ReadShared(EditsPath), Options);
        if (file == null) return result;
        foreach (KeyValuePair<string, EditRegion> pair in file.Regions)
        {
            if (pair.Value == null) continue;
            result[pair.Key] = new WorldEnemyRegion { Key = pair.Key, Boss = pair.Value.Boss ?? "", Groups = pair.Value.Groups, Ai = pair.Value.Ai };
        }
        return result;
    }

    // Writes the file (a region that is back to the game's lists is simply not in "edits").
    public static void SaveEdits(Dictionary<string, WorldEnemyRegion> edits)
    {
        var file = new EditsFile
        {
            Note = "Written by the launcher (Server > World map > Enemies). A region listed here replaces the game's own enemy lists " +
                   "of that region; a region that is not listed keeps them. See docs/open-world.md."
        };
        foreach (KeyValuePair<string, WorldEnemyRegion> pair in edits)
            file.Regions[pair.Key] = new EditRegion
            {
                Boss = pair.Value.Boss,
                Groups = pair.Value.Groups,
                Ai = pair.Value.Ai != null && pair.Value.Ai.Count > 0 ? pair.Value.Ai : null
            };
        TextFiles.WriteAtomic(EditsPath, JsonSerializer.Serialize(file, Options), false);
    }
}

// A request to the world server (right click on the map, "Spawn dungeon"), and its answer.
public static class WorldRequests
{
    public const string FolderName = "world-requests";
    public const string ResultFileName = "world-request-result.json";

    public sealed class Result
    {
        public string Id { get; set; } = "";
        public bool Ok { get; set; }
        public string Message { get; set; } = "";
    }

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Folder => LauncherPaths.ServerFile(FolderName);

    // "boss" at a point ("boss": that boss of the biome; null: the biome's boss), or "carrier" of a dungeon type (at a point, or at a
    // random place when x and y are null). Returns the id.
    public static string Send(string action, float? x, float? y, string? dungeon, string? boss = null)
    {
        string id = DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-" + Random.Shared.Next(1000, 9999);
        Directory.CreateDirectory(Folder);
        string text = JsonSerializer.Serialize(new { id, action, x, y, dungeon, boss }, Options);
        TextFiles.WriteAtomic(Path.Combine(Folder, id + ".json"), text, false);
        return id;
    }

    // The answer to a request, or null while the server has not given it.
    public static Result? ReadResult(string id)
    {
        try
        {
            string path = LauncherPaths.ServerFile(ResultFileName);
            if (!File.Exists(path)) return null;
            Result? result = JsonSerializer.Deserialize<Result>(TextFiles.ReadShared(path), Options);
            return result != null && result.Id == id ? result : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Takes back a request the server never picked up (it is not running): so it does not fire when the server starts later.
    public static void Withdraw(string id)
    {
        try
        {
            string path = Path.Combine(Folder, id + ".json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
            // The server took it at the same moment: fine.
        }
    }
}
