using System.Runtime.CompilerServices;
using System.Text.Json;

// What each account bought in the stores, saved in accounts\<key>\store.json next to account.json and inventory.json.
//
// The game keeps this as "account items": counters named hangar_slot, storage_slot, inventory_slot, skill_loadout_slot
// and the markers store_storage_slot, inventory_slot_buy and store_bundle_starter. The client sends them back to the
// server in PlayerData.accumulatedItems and counts them to price the next slot (hangar slot: 750, 1750, 2750, 3750).
// The slots of the account are the host's base value (progression.json, items.json) plus these counters.
//
// Thread safety: every access goes through this class under one lock. The lock is a leaf: nothing is called while
// holding it, except the file write.
public sealed class StoreAccountData
{
    // Account item name -> how many the account has (as sent in PlayerData.accumulatedItems).
    public Dictionary<string, int> AccountItems { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

    // Product id -> times bought (the host's "maxPerAccount" counts these).
    public Dictionary<string, int> Purchases { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

    // The last purchases, newest last (for the host to see what happened).
    public List<string> History { get; set; } = new List<string>();
}

public static class StoreAccounts
{
    public const string FileName = "store.json";
    private const int HistoryLimit = 40;

    private static readonly object gate = new object();
    private static readonly ConditionalWeakTable<Accounts.Account, StoreAccountData> cache = new ConditionalWeakTable<Accounts.Account, StoreAccountData>();
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static Action<string, string> log = (_, _) => { };

    public static void Start(Action<string, string> log)
    {
        StoreAccounts.log = log;
    }

    // Slots the game counts as "bought": hangar_slot, storage_slot, inventory_slot, skill_loadout_slot.
    public const string HangarSlot = "hangar_slot";
    public const string StorageSlot = "storage_slot";
    public const string InventorySlot = "inventory_slot";
    public const string SkillLoadoutSlot = "skill_loadout_slot";

    // How many of an account item the account of the current scope has.
    public static int Count(string accountItem)
    {
        Accounts.Account account = Accounts.Current;
        lock (gate)
        {
            return Load(account).AccountItems.TryGetValue(accountItem, out int n) ? n : 0;
        }
    }

    // Copy of all the counters, for PlayerData.accumulatedItems.
    public static Dictionary<string, int> AccountItems()
    {
        Accounts.Account account = Accounts.Current;
        lock (gate)
        {
            return new Dictionary<string, int>(Load(account).AccountItems, StringComparer.Ordinal);
        }
    }

    // Times the account bought a product.
    public static int Bought(string productId)
    {
        Accounts.Account account = Accounts.Current;
        lock (gate)
        {
            return Load(account).Purchases.TryGetValue(productId, out int n) ? n : 0;
        }
    }

    // Records a purchase: the account items it gives and one more unit of the product.
    public static void Record(string productId, IEnumerable<string> accountItems, string note)
    {
        Accounts.Account account = Accounts.Current;
        lock (gate)
        {
            StoreAccountData data = Load(account);
            foreach (string item in accountItems)
                data.AccountItems[item] = (data.AccountItems.TryGetValue(item, out int n) ? n : 0) + 1;
            data.Purchases[productId] = (data.Purchases.TryGetValue(productId, out int times) ? times : 0) + 1;
            data.History.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + note);
            while (data.History.Count > HistoryLimit) data.History.RemoveAt(0);
            Save(account, data);
        }
    }

    // Forgets everything the account bought (the /store reset test command).
    public static void Reset()
    {
        Accounts.Account account = Accounts.Current;
        lock (gate)
        {
            StoreAccountData data = Load(account);
            data.AccountItems.Clear();
            data.Purchases.Clear();
            data.History.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " reset");
            while (data.History.Count > HistoryLimit) data.History.RemoveAt(0);
            Save(account, data);
        }
    }

    private static string? PathOf(Accounts.Account account) => account.Folder == null ? null : Path.Combine(account.Folder, FileName);

    // Reads store.json the first time the account is used. Call under the lock.
    private static StoreAccountData Load(Accounts.Account account)
    {
        if (cache.TryGetValue(account, out StoreAccountData? known)) return known;

        StoreAccountData data = new StoreAccountData();
        string? path = PathOf(account);
        if (path != null && File.Exists(path))
        {
            try
            {
                StoreAccountData? read = JsonSerializer.Deserialize<StoreAccountData>(File.ReadAllText(path), Options);
                if (read != null) data = read;
            }
            catch (Exception e)
            {
                // A broken file is kept aside instead of being overwritten.
                string backup = path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                try { File.Copy(path, backup, true); } catch { }
                log("STORE", "could not read " + path + " (" + e.Message + "): copy in " + backup + ", starting empty");
            }
        }
        data.AccountItems ??= new Dictionary<string, int>(StringComparer.Ordinal);
        data.Purchases ??= new Dictionary<string, int>(StringComparer.Ordinal);
        data.History ??= new List<string>();
        cache.Add(account, data);
        return data;
    }

    private static void Save(Accounts.Account account, StoreAccountData data)
    {
        string? path = PathOf(account);
        if (path == null) return;   // temporary account: nothing is saved
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
            File.Move(temp, path, true);
        }
        catch (Exception e)
        {
            log("STORE", "could not write " + path + ": " + e.Message);
        }
    }
}
