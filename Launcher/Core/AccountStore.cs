using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using SteambirdsLauncher.Editor;
namespace SteambirdsLauncher.Core;

// The accounts of the server: one folder per player in server\accounts\<key>\ (key = steam_<SteamID64>), with
// account.json (pilot, planes, finished dungeons), inventory.json (items, currencies, ingredients) and acceptances.json.
// server\account.json (+ inventory.json, acceptances.json) is the old single account, until the server gives it to the
// first player who logs in from the host's PC.
//
// Editing and removing: the server keeps the accounts of the players who logged in in memory and writes over the
// files, so they are only changed while the launcher's server is stopped. Every field the launcher does not edit is
// kept as it is, and the files are copied to server\_backup before any change.
public sealed class AccountStore
{
    public const string AccountFileName = "account.json";
    public const string InventoryFileName = "inventory.json";
    public const string AcceptancesFileName = "acceptances.json";
    public const string AccountsFolderName = "accounts";

    public static string AccountsFolder => LauncherPaths.ServerFile(AccountsFolderName);

    public sealed class Plane
    {
        public string CharacterId { get; set; } = "";
        public string PlaneClass { get; set; } = "";
        public string Variant { get; set; } = "";
        public int Xp { get; set; }
        public int Level { get; set; } = 1;
        public int BankedPilotXp { get; set; }
    }

    // One account in the list of the Accounts page.
    public sealed class Summary
    {
        public string Key { get; init; } = "";
        public string Folder { get; init; } = "";
        public string Name { get; init; } = "";
        public string SteamId { get; init; } = "";
        public DateTime? LastSeen { get; init; }

        // server\account.json: the old single account, not assigned to a player yet.
        public bool IsLegacy { get; init; }

        public AccountFile.Account? Data { get; init; }
        public string Error { get; init; } = "";
    }

    private static readonly JsonNodeOptions NodeOptions = new JsonNodeOptions { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly JsonObject account;
    private readonly JsonObject? inventory;
    private readonly bool accountBom;
    private readonly bool inventoryBom;

    private AccountStore(string folder, JsonObject account, bool accountBom, JsonObject? inventory, bool inventoryBom)
    {
        Folder = folder;
        this.account = account;
        this.accountBom = accountBom;
        this.inventory = inventory;
        this.inventoryBom = inventoryBom;
    }

    // Folder of the account (server\accounts\<key>, or server\ for the old single account).
    public string Folder { get; }
    public string AccountPath => Path.Combine(Folder, AccountFileName);
    public string InventoryPath => Path.Combine(Folder, InventoryFileName);

    // Every account of the server: the old single account first (when it is still there), then the players by the
    // last time they played.
    public static List<Summary> List()
    {
        var list = new List<Summary>();
        string legacy = LauncherPaths.ServerFile(AccountFileName);
        if (File.Exists(legacy)) list.Add(Read(LauncherPaths.ServerFolder, "", isLegacy: true));
        try
        {
            if (Directory.Exists(AccountsFolder))
            {
                var players = new List<Summary>();
                foreach (string folder in Directory.GetDirectories(AccountsFolder))
                {
                    if (!File.Exists(Path.Combine(folder, AccountFileName))) continue;
                    players.Add(Read(folder, Path.GetFileName(folder), isLegacy: false));
                }
                list.AddRange(players.OrderByDescending(p => p.LastSeen ?? DateTime.MinValue).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase));
            }
        }
        catch
        {
            // A folder that cannot be read leaves the list shorter.
        }
        return list;
    }

    // Newest change of any account file (the Accounts page reads the list again when it changes).
    public static DateTime LatestChange()
    {
        DateTime latest = DateTime.MinValue;
        try
        {
            string legacy = LauncherPaths.ServerFile(AccountFileName);
            if (File.Exists(legacy)) latest = File.GetLastWriteTimeUtc(legacy);
            if (Directory.Exists(AccountsFolder))
            {
                latest = Max(latest, Directory.GetLastWriteTimeUtc(AccountsFolder));
                foreach (string folder in Directory.GetDirectories(AccountsFolder))
                {
                    string file = Path.Combine(folder, AccountFileName);
                    if (File.Exists(file)) latest = Max(latest, File.GetLastWriteTimeUtc(file));
                }
            }
        }
        catch
        {
        }
        return latest;
    }

    private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;

    private static Summary Read(string folder, string key, bool isLegacy)
    {
        try
        {
            AccountFile.Account? data = AccountFile.Load(Path.Combine(folder, AccountFileName));
            string name = data != null && data.Name.Length > 0 ? data.Name : (isLegacy ? L.T("(old account of the server)") : key);
            return new Summary
            {
                Key = key.Length > 0 ? key : data?.Key ?? "",
                Folder = folder,
                Name = name,
                SteamId = data?.SteamId ?? "",
                LastSeen = data?.LastAccess,
                IsLegacy = isLegacy,
                Data = data
            };
        }
        catch (Exception e)
        {
            return new Summary { Key = key, Folder = folder, Name = key.Length > 0 ? key : AccountFileName, IsLegacy = isLegacy, Error = e.Message };
        }
    }

    public static AccountStore Load(string folder)
    {
        JsonObject account = ReadObject(Path.Combine(folder, AccountFileName), out bool accountBom);
        JsonObject? inventory = null;
        bool inventoryBom = false;
        string inventoryPath = Path.Combine(folder, InventoryFileName);
        if (File.Exists(inventoryPath)) inventory = ReadObject(inventoryPath, out inventoryBom);
        return new AccountStore(folder, account, accountBom, inventory, inventoryBom);
    }

    public bool HasInventory => inventory != null;

    // ------------------------------------------------------------------ account (account.json)

    public string AccountId => GetString(account, "AccountId");
    public string PlayerName => GetString(account, "Nome");

    // "Name (key)" for titles; the account id when there is no name.
    public string DisplayName
    {
        get
        {
            string name = PlayerName;
            string key = Path.GetFileName(Folder.TrimEnd('\\', '/'));
            if (Folder.TrimEnd('\\', '/').Equals(LauncherPaths.ServerFolder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) key = AccountFileName;
            return name.Length > 0 ? name + " (" + key + ")" : key + " " + AccountId;
        }
    }

    private JsonObject Pilot
    {
        get
        {
            if (account["Pilot"] is JsonObject pilot) return pilot;
            var created = new JsonObject();
            account["Pilot"] = created;
            return created;
        }
    }

    public int PilotLevel { get => GetInt(Pilot, "Level", 1); set => Pilot["Level"] = value; }
    public int PilotBaselineXp { get => GetInt(Pilot, "BaselineXp", 0); set => Pilot["BaselineXp"] = value; }
    public int LastLevelupShown { get => GetInt(Pilot, "LastLevelupShown", 1); set => Pilot["LastLevelupShown"] = value; }
    public int LastXpLevelupShown { get => GetInt(Pilot, "LastXpLevelupShown", 0); set => Pilot["LastXpLevelupShown"] = value; }
    public int Kills { get => GetInt(Pilot, "Kills", 0); set => Pilot["Kills"] = value; }
    public int BossKills { get => GetInt(Pilot, "BossKills", 0); set => Pilot["BossKills"] = value; }
    public int PlanesLost { get => GetInt(Pilot, "PlanesLost", 0); set => Pilot["PlanesLost"] = value; }
    public int HighestPlaneLevel { get => GetInt(Pilot, "HighestPlaneLevel", 0); set => Pilot["HighestPlaneLevel"] = value; }

    public List<string> Unlocks
    {
        get => GetStringList(Pilot, "Unlocks");
        set => Pilot["Unlocks"] = ToArray(value);
    }

    public List<string> FinishedDungeons
    {
        get => GetStringList(account, "FinishedDungeons");
        set => account["FinishedDungeons"] = ToArray(value);
    }

    public List<Plane> Planes
    {
        get
        {
            var planes = new List<Plane>();
            if (account["Planes"] is not JsonArray list) return planes;
            foreach (JsonObject plane in list.OfType<JsonObject>())
            {
                planes.Add(new Plane
                {
                    CharacterId = GetString(plane, "CharacterId"),
                    PlaneClass = GetString(plane, "Class"),
                    Variant = GetString(plane, "Variant"),
                    Xp = GetInt(plane, "Xp", 0),
                    Level = GetInt(plane, "Level", 1),
                    BankedPilotXp = GetInt(plane, "BankedPilotXp", 0)
                });
            }
            return planes;
        }
    }

    // Writes the edited planes back: planes missing from the list are removed (also their items in inventory.json).
    public void SetPlanes(List<Plane> planes)
    {
        if (account["Planes"] is not JsonArray list) return;
        var keep = new HashSet<string>(planes.Select(p => p.CharacterId), StringComparer.Ordinal);
        foreach (JsonObject plane in list.OfType<JsonObject>().ToList())
        {
            string id = GetString(plane, "CharacterId");
            if (!keep.Contains(id))
            {
                list.Remove(plane);
                if (inventory?["Planes"] is JsonObject inventoryPlanes) inventoryPlanes.Remove(FindKey(inventoryPlanes, id) ?? id);
                continue;
            }
            Plane edited = planes.First(p => p.CharacterId == id);
            plane["Variant"] = edited.Variant;
            plane["Xp"] = edited.Xp;
            plane["Level"] = edited.Level;
            plane["BankedPilotXp"] = edited.BankedPilotXp;
        }
    }

    // ------------------------------------------------------------------ inventory (inventory.json)

    public Dictionary<string, int> Currencies
    {
        get => GetIntMap(inventory, "Currencies");
        set => SetIntMap(inventory, "Currencies", value);
    }

    public Dictionary<string, int> Ingredients
    {
        get => GetIntMap(inventory, "Ingredients");
        set => SetIntMap(inventory, "Ingredients", value);
    }

    // ------------------------------------------------------------------ files

    // Copies the files of the account to server\_backup\account-<date> and writes the changes.
    public string Save()
    {
        string backup = Backup(Folder, "account");
        TextFiles.WriteAtomic(AccountPath, account.ToJsonString(WriteOptions), accountBom);
        if (inventory != null) TextFiles.WriteAtomic(InventoryPath, inventory.ToJsonString(WriteOptions), inventoryBom);
        return backup;
    }

    // Removes an account: its files go to server\_backup\account-removed-<date>. A player's folder in server\accounts is
    // removed whole (the player gets a new account at the next login). For the old single account (server\account.json)
    // only account.json and inventory.json go; acceptances.json stays.
    public static string Delete(string folder)
    {
        string backup = Backup(folder, "account-removed");
        bool isLegacy = Path.GetFullPath(folder).TrimEnd('\\').Equals(Path.GetFullPath(LauncherPaths.ServerFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        if (isLegacy)
        {
            DeleteIfThere(Path.Combine(folder, AccountFileName));
            DeleteIfThere(Path.Combine(folder, InventoryFileName));
        }
        else if (Directory.Exists(folder))
        {
            // Only a folder of server\accounts is removed whole.
            string parent = Path.GetDirectoryName(Path.GetFullPath(folder).TrimEnd('\\')) ?? "";
            if (!parent.Equals(Path.GetFullPath(AccountsFolder).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(folder + " is not an account folder of server\\accounts");
            Directory.Delete(folder, recursive: true);
        }
        return backup;
    }

    private static void DeleteIfThere(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static string Backup(string folder, string kind)
    {
        string target = LauncherPaths.NewBackupFolder(kind);
        foreach (string name in new[] { AccountFileName, InventoryFileName, AcceptancesFileName })
        {
            string source = Path.Combine(folder, name);
            if (File.Exists(source)) File.Copy(source, Path.Combine(target, name), overwrite: true);
        }
        return target;
    }

    // ------------------------------------------------------------------ JSON helpers

    private static JsonObject ReadObject(string path, out bool bom)
    {
        string text = TextFiles.ReadKeepingBom(path, out bom);
        JsonNode? node = JsonNode.Parse(text, NodeOptions, JsoncFile.ParseOptions);
        if (node != null) LegacyNames.Translate(node);   // files saved with the old Portuguese names
        return node as JsonObject ?? throw new FormatException(Path.GetFileName(path) + " is not a JSON object");
    }

    private static string? FindKey(JsonObject obj, string key)
    {
        foreach (KeyValuePair<string, JsonNode?> pair in obj)
        {
            if (string.Equals(pair.Key, key, StringComparison.Ordinal)) return pair.Key;
        }
        return null;
    }

    private static string GetString(JsonObject obj, string key)
    {
        return obj[key] is JsonValue value && value.TryGetValue(out string? text) && text != null ? text : "";
    }

    private static int GetInt(JsonObject obj, string key, int fallback)
    {
        if (obj[key] is not JsonValue value) return fallback;
        if (value.TryGetValue(out int whole)) return whole;
        if (value.TryGetValue(out double real)) return (int)Math.Round(real);
        return fallback;
    }

    private static List<string> GetStringList(JsonObject obj, string key)
    {
        var result = new List<string>();
        if (obj[key] is not JsonArray list) return result;
        foreach (JsonNode? item in list)
        {
            if (item is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrEmpty(text)) result.Add(text);
        }
        return result;
    }

    private static JsonArray ToArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (string value in values) array.Add(JsonValue.Create(value));
        return array;
    }

    private static Dictionary<string, int> GetIntMap(JsonObject? obj, string key)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        if (obj?[key] is not JsonObject map) return result;
        foreach (KeyValuePair<string, JsonNode?> pair in map)
        {
            if (pair.Value is JsonValue value && value.TryGetValue(out int amount)) result[pair.Key] = amount;
            else if (pair.Value is JsonValue real && real.TryGetValue(out double amountReal)) result[pair.Key] = (int)Math.Round(amountReal);
        }
        return result;
    }

    // Amounts of zero leave the map (the server removes them too).
    private static void SetIntMap(JsonObject? obj, string key, Dictionary<string, int> values)
    {
        if (obj == null) return;
        var map = new JsonObject();
        foreach (KeyValuePair<string, int> pair in values.Where(p => p.Value != 0)) map[pair.Key] = pair.Value;
        obj[key] = map;
    }
}
