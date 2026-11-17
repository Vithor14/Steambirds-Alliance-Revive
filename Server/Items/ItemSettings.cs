using System.Text.Json;
using System.Text.Json.Serialization;

// Host settings for items, inventory and loot: items.json in the server folder (Server).
// Created with the default values the first time and read again when the file changes (like progression.json).
// The loot tables themselves live in the loot_tables folder (LootTables.cs), one file per table.
public sealed class ItemSettings
{
    // Enemies drop loot bags.
    public bool LootEnabled { get; set; } = true;

    // How many times the loot of a kill is rolled (1 = original).
    public int LootRolls { get; set; } = 1;

    // Tables used when a unit has no loot of its own (or only lootTableIds no file in loot_tables knows).
    public string NormalLootTable { get; set; } = "normal";
    public string BossLootTable { get; set; } = "boss";

    // Also roll the loot written in the game's unit configs (inline tables, like the dungeon key some
    // enemies drop). The named tables they point to (lootTableId) only existed on the original server:
    // a file in loot_tables with that id (or a "replaces" pattern) takes their place.
    public bool UseGameUnitLoot { get; set; } = true;

    // Dungeon difficulty for loot entries named "..._normal", "..._difficult" and "..._lethal".
    public string DungeonDifficulty { get; set; } = "normal";

    // Seconds a loot bag stays in the world. 0 = the game's lootbagLifetime (30 s).
    public float LootBagLifetime { get; set; }

    // Original rule (isUsingSharedCargoForAll): one cargo pod for all the planes of the account.
    public bool SharedCargoForAllPlanes { get; set; } = true;

    // Extra cargo and storage slots for every account (numAdditionalInventorySlots / numAdditionalStorageSlots).
    public int ExtraCargoSlots { get; set; }
    public int ExtraStorageSlots { get; set; }

    // What a destroyed plane keeps. Original: the cargo and the equipped items are lost with the plane.
    public bool KeepCargoOnDeath { get; set; }
    public bool KeepEquippedOnDeath { get; set; }

    // DeathLootConfig.salvageRandomEquipment: equipped items sent to the storage when the plane is destroyed.
    // The death config comes empty in build 201219 (death = {}), so the original value there is 0.
    public int DeathSalvageRandomEquipment { get; set; }

    // Salvaged items lose condition (InventoryItem.DegradeCondition) and supercharged ones lose the extra stats.
    public bool DegradeSalvagedItems { get; set; } = true;

    // "Trash" in the inventory recycles the item into the mulch currency (MulchConfig).
    public bool MulchOnTrash { get; set; } = true;

    // Reforging and supercharging at the Workshop (ReforgeConfig).
    public bool ReforgeEnabled { get; set; } = true;

    // Currencies, ingredients and storage items of a new account (the launcher "accounts" tab will edit these).
    public Dictionary<string, int> StartingCurrencies { get; set; } = new Dictionary<string, int>();
    public Dictionary<string, int> StartingIngredients { get; set; } = new Dictionary<string, int>();
    public List<string> StartingStorageItems { get; set; } = new List<string>();

    // Writes item_catalog.md and .json (folder "catalog") when the server starts and the game build changed.
    public bool ExportItemCatalog { get; set; } = true;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownKeys { get; set; }
}

public static class ItemSettingsFile
{
    public const string FileName = "items.json";

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static ItemSettings current = new ItemSettings();
    private static DateTime readAt = DateTime.MinValue;
    private static DateTime nextCheck = DateTime.MinValue;
    private static Action<string, string> log = (_, _) => { };
    private static readonly object gate = new object();

    public static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), FileName);

    public static ItemSettings Current
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

    public static void Start(Action<string, string> log)
    {
        lock (gate)
        {
            ItemSettingsFile.log = log;
            if (!File.Exists(FilePath))
            {
                try
                {
                    File.WriteAllText(FilePath, DefaultFileText);
                    log("ITEMS", FilePath + " created with the default values (edit and save: the server reloads it by itself)");
                }
                catch (Exception e)
                {
                    log("ITEMS", "could not create " + FilePath + ": " + e.Message);
                }
            }
            ReloadIfChanged();
        }
    }

    private static void ReloadIfChanged()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            DateTime modified = File.GetLastWriteTimeUtc(FilePath);
            if (modified == readAt) return;
            readAt = modified;

            ItemSettings? read = JsonSerializer.Deserialize<ItemSettings>(File.ReadAllText(FilePath), ReadOptions);
            if (read == null) return;
            read.StartingCurrencies ??= new Dictionary<string, int>();
            read.StartingIngredients ??= new Dictionary<string, int>();
            read.StartingStorageItems ??= new List<string>();
            read.LootRolls = Math.Clamp(read.LootRolls, 0, 20);
            read.ExtraCargoSlots = Math.Clamp(read.ExtraCargoSlots, 0, 200);
            read.ExtraStorageSlots = Math.Clamp(read.ExtraStorageSlots, 0, 500);
            read.DeathSalvageRandomEquipment = Math.Clamp(read.DeathSalvageRandomEquipment, 0, 4);
            read.NormalLootTable ??= "normal";
            read.BossLootTable ??= "boss";
            read.DungeonDifficulty = (read.DungeonDifficulty ?? "normal").Trim().ToLowerInvariant();
            current = read;
            log("ITEMS", FilePath + " read: loot " + (read.LootEnabled ? "on x" + read.LootRolls : "off") +
                ", tables '" + read.NormalLootTable + "'/'" + read.BossLootTable + "', cargo " + (read.SharedCargoForAllPlanes ? "shared" : "per plane") +
                " (+" + read.ExtraCargoSlots + " slots), storage +" + read.ExtraStorageSlots + " slots, recycling " + (read.MulchOnTrash ? "sim" : "no") +
                ", reforge " + (read.ReforgeEnabled ? "sim" : "no") + ", save on fall " + read.DeathSalvageRandomEquipment + " item(ns)");
            if (read.UnknownKeys != null && read.UnknownKeys.Count > 0)
                log("ITEMS", "key(s) this version does not use in " + FileName + ": " + string.Join(", ", read.UnknownKeys.Keys));
        }
        catch (Exception e)
        {
            log("ITEMS", "error in " + FilePath + " (" + e.Message + "): keeping the previous configuration");
        }
    }

    private const string DefaultFileText =
@"// Items, inventory and loot of the server. Save the file and the server reloads it by itself.
// The loot tables are in the loot_tables folder (one file per table; see loot_tables\README.md).
{
  // Enemies drop loot bags.
  ""lootEnabled"": true,

  // How many times the loot of each kill is rolled (1 = original).
  ""lootRolls"": 1,

  // Tables used when the unit has no loot of its own (files in loot_tables, without the .json).
  ""normalLootTable"": ""normal"",
  ""bossLootTable"": ""boss"",

  // Also rolls the loot written in the game's units (built-in tables, like the dungeon key that some enemies
  // drop). The tables by name (lootTableId) only existed on the original server: a file in loot_tables with that
  // id (or a pattern in ""replaces"") takes their place.
  ""useGameUnitLoot"": true,

  // Dungeon difficulty for loot entries ""..._normal"", ""..._difficult"" and ""..._lethal"".
  ""dungeonDifficulty"": ""normal"",

  // Seconds a loot bag stays in the world (0 = the game's value, 30).
  ""lootBagLifetime"": 0,

  // Original rule: a single cargo shared by all the planes of the account.
  ""sharedCargoForAllPlanes"": true,

  // Extra plane cargo and storage slots for all accounts.
  ""extraCargoSlots"": 0,
  ""extraStorageSlots"": 0,

  // What a destroyed plane keeps. Original: it loses the cargo and the equipped items.
  ""keepCargoOnDeath"": false,
  ""keepEquippedOnDeath"": false,

  // Equipped items that go to storage when the plane falls (DeathLootConfig.salvageRandomEquipment).
  // The ""death"" config comes empty in this game version, so the original here is 0.
  ""deathSalvageRandomEquipment"": 0,

  // Salvaged items lose condition (wear) and supercharged ones lose the bonus.
  ""degradeSalvagedItems"": true,

  // ""Trash"" in the inventory recycles the item into the recycling currency (the game's ""mulching"" config).
  ""mulchOnTrash"": true,

  // Reforging and supercharging at the Workshop (the game's ""reforging"" config).
  ""reforgeEnabled"": true,

  // State of a new account: currencies, ingredients and items in storage. Example:
  // ""startingCurrencies"": { ""newscrap"": 100 }, ""startingStorageItems"": [ ""armor_2"", ""engine_speed_2"" ]
  ""startingCurrencies"": {},
  ""startingIngredients"": {},
  ""startingStorageItems"": [],

  // Writes catalog\item_catalog.md and .json when the server starts and the game version changed.
  ""exportItemCatalog"": true
}
";
}
