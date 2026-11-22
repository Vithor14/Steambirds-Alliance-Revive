using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// The authoritative inventories of each account, saved in accounts\<key>\inventory.json next to its account.json:
// every plane (equipped items, alternative main gun, cargo, permanent upgrades, accumulated upgrades),
// the account storage, the shared cargo pod, currencies and ingredients.
//
// Read/Change work on the account of the current scope (Accounts.Current), like Accounts itself. The inventory
// of an account is read from disk the first time it is used, and gets then the inventories of its planes and the
// starting state of items.json (once per account).
//
// The client only changes its own copy after the server says yes (Inventory.ClientToServerTransport waits for
// the RPC answer), so these lists must always match what the client shows.
//
// Thread safety: every read and change goes through Read/Change, under one lock. The helpers below that take an
// InventorySaveData are meant to be called inside those callbacks.

// One inventory resolved by characterId: the storage or a plane (with its cargo list: own or shared).
public sealed class InvAccess
{
    public string CharacterId = "";
    public bool IsStorage;
    public StoredInventory Inv = null!;
    public List<StoredItem?> Cargo = null!;

    // InventoryOwner: Storage 0, Plane 1.
    public byte Owner => IsStorage ? (byte)0 : (byte)1;
}

public static class InventoryStore
{
    public const string FileName = "inventory.json";

    // GeneralIDType (GeneralIDType.cs): Character = 7, CharacterItem = 8.
    private const int IdTypeCharacter = 7;
    private const int IdTypeCharacterItem = 8;

    private static readonly object gate = new object();
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static Action<string, string> log = (_, _) => { };

    // inventory.json of the account of the current scope ("" for the temporary account).
    public static string FilePath => Accounts.Current.InventoryPath;

    // Called once at server start. The inventories are read per account (Current).
    public static void Start(Action<string, string> log)
    {
        lock (gate)
        {
            InventoryStore.log = log;
        }
    }

    // Account of the current scope, with its inventory loaded.
    private static Accounts.Account Current()
    {
        Accounts.Account account = Accounts.Current;
        if (account.Inventory != null) return account;

        // Planes of the account, read before taking the lock (Accounts has its own lock).
        List<Accounts.SavedPlane> planes = Accounts.PlanesOf(account);
        lock (gate)
        {
            if (account.Inventory == null) account.Inventory = LoadFor(account, planes);
        }
        return account;
    }

    private static InventorySaveData LoadFor(Accounts.Account account, List<Accounts.SavedPlane> planes)
    {
        string path = account.InventoryPath;
        InventorySaveData? read = null;
        if (path.Length > 0 && File.Exists(path))
        {
            try
            {
                read = JsonSerializer.Deserialize<InventorySaveData>(File.ReadAllText(path), Options);
            }
            catch (Exception e)
            {
                // A broken file is kept aside instead of being overwritten.
                string backup = path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                try { File.Copy(path, backup, true); } catch { }
                log("INVENTORY", "could not read " + path + " (" + e.Message + "): copy in " + backup + ", starting empty");
            }
        }

        InventorySaveData data = read ?? new InventorySaveData();
        Normalize(data);

        // Planes that left the account while the server was off; every plane of the account has an inventory
        // (planes from before the inventory get their starting items).
        var ids = new HashSet<string>(planes.Select(a => a.CharacterId), StringComparer.Ordinal);
        foreach (string gone in data.Planes.Keys.Where(k => !ids.Contains(k)).ToList()) data.Planes.Remove(gone);
        foreach (Accounts.SavedPlane plane in planes) EnsurePlane(data, plane.CharacterId, plane.Class);

        GiveStartingState(data);
        SaveLocked(account, data);

        int items = data.Storage.AllItems().Count() + data.SharedCargo.Count(i => i != null) +
                    data.Planes.Values.Sum(p => p.AllItems().Count());
        log("INVENTORY", (path.Length > 0 ? path : "(temporary account)") + ": " + data.Planes.Count + " plane inventories, storage " +
            data.StorageCharacterId + ", " + items + " item(s) in total" +
            (data.Currencies.Count > 0 ? ", currencies " + string.Join(", ", data.Currencies.Select(c => c.Key + "=" + c.Value)) : "") +
            (data.Ingredients.Count > 0 ? ", " + data.Ingredients.Count + " ingredient(s)" : ""));
        return data;
    }

    public static T Read<T>(Func<InventorySaveData, T> reader)
    {
        Accounts.Account account = Current();
        lock (gate)
        {
            return reader(account.Inventory!);
        }
    }

    public static T Change<T>(Func<InventorySaveData, T> change)
    {
        Accounts.Account account = Current();
        lock (gate)
        {
            InventorySaveData data = account.Inventory!;
            T result = change(data);
            SaveLocked(account, data);
            return result;
        }
    }

    public static string StorageCharacterId
    {
        get
        {
            Accounts.Account account = Current();
            lock (gate) return account.Inventory!.StorageCharacterId;
        }
    }

    private static void Normalize(InventorySaveData d)
    {
        d.Storage ??= new StoredInventory();
        d.SharedCargo ??= new List<StoredItem?>();
        d.Planes ??= new Dictionary<string, StoredInventory>(StringComparer.Ordinal);
        d.Currencies ??= new Dictionary<string, int>(StringComparer.Ordinal);
        d.Ingredients ??= new Dictionary<string, int>(StringComparer.Ordinal);
        if (d.NextAuditId < 1) d.NextAuditId = 1;
        if (PlayerData.FromBase64Url(d.StorageCharacterId) == null)
            d.StorageCharacterId = PlayerData.ToBase64Url(PlayerData.NewId(IdTypeCharacter));
        d.Storage.CharacterId = d.StorageCharacterId;
        d.Storage.Normalize();
        // Storage has no equipped items.
        d.Storage.Equipped.Clear();
        d.Storage.Alternative.Clear();
        for (int i = 0; i < d.SharedCargo.Count; i++)
        {
            if (d.SharedCargo[i] != null && string.IsNullOrEmpty(d.SharedCargo[i]!.DefId)) d.SharedCargo[i] = null;
        }
        foreach (KeyValuePair<string, StoredInventory> pair in d.Planes)
        {
            pair.Value.CharacterId = pair.Key;
            pair.Value.Normalize();
        }
    }

    // Starting currencies, ingredients and storage items of items.json (once per account).
    private static void GiveStartingState(InventorySaveData d)
    {
        if (d.StartingStateGiven) return;
        d.StartingStateGiven = true;
        ItemSettings settings = ItemSettingsFile.Current;
        foreach (KeyValuePair<string, int> c in settings.StartingCurrencies)
            if (c.Value != 0) d.Currencies[c.Key] = (d.Currencies.TryGetValue(c.Key, out int v) ? v : 0) + c.Value;
        foreach (KeyValuePair<string, int> c in settings.StartingIngredients)
            if (c.Value != 0) d.Ingredients[c.Key] = (d.Ingredients.TryGetValue(c.Key, out int v) ? v : 0) + c.Value;
        InvAccess storage = Resolve(d, d.StorageCharacterId)!;
        foreach (string defId in settings.StartingStorageItems)
        {
            if (ItemCatalog.Loaded && ItemCatalog.Get(defId) == null)
            {
                log("INVENTORY", "startingStorageItems: item '" + defId + "' does not exist in the game, ignored");
                continue;
            }
            InvPos free = FirstFreeCargo(storage);
            if (!free.Valid) break;
            storage.Cargo[free.Index] = NewItem(defId);
        }
    }

    private static void SaveLocked(Accounts.Account account, InventorySaveData data)
    {
        string path = account.InventoryPath;
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
            File.Move(temp, path, true);
        }
        catch (Exception e)
        {
            log("INVENTORY", "could not write " + path + ": " + e.Message);
        }
    }

    // ------------------------------------------------------------------ helpers (call inside Read/Change)

    // The base value of the game, the host's extra slots (items.json) and the slots the account bought (Stores\StoreAccounts).
    public static int CargoCapacity =>
        ItemCatalog.BaseCargoSlots + ItemSettingsFile.Current.ExtraCargoSlots + StoreAccounts.Count(StoreAccounts.InventorySlot);
    public static int StorageCapacity =>
        ItemCatalog.BaseStorageSlots + ItemSettingsFile.Current.ExtraStorageSlots + StoreAccounts.Count(StoreAccounts.StorageSlot);

    // The storage or a plane of the account. Null when the characterId is unknown.
    public static InvAccess? Resolve(InventorySaveData d, string? characterId)
    {
        if (characterId == null) return null;
        MigrateCargoMode(d);
        if (characterId == d.StorageCharacterId)
        {
            FitCapacity(d.Storage.Cargo, StorageCapacity);
            return new InvAccess { CharacterId = characterId, IsStorage = true, Inv = d.Storage, Cargo = d.Storage.Cargo };
        }
        if (!d.Planes.TryGetValue(characterId, out StoredInventory? inv)) return null;
        List<StoredItem?> cargo = ItemSettingsFile.Current.SharedCargoForAllPlanes ? d.SharedCargo : inv.Cargo;
        FitCapacity(cargo, CargoCapacity);
        return new InvAccess { CharacterId = characterId, IsStorage = false, Inv = inv, Cargo = cargo };
    }

    // Plane inventory, created with the class's starting items when it does not exist yet. The starting items keep
    // the ids the server always used for them (same plane and slot = same id), so planes from before keep matching.
    public static InvAccess EnsurePlane(InventorySaveData d, string characterId, string? planeClass)
    {
        if (!d.Planes.TryGetValue(characterId, out StoredInventory? inv))
        {
            inv = new StoredInventory { CharacterId = characterId, PlaneClass = planeClass ?? "" };
            foreach (KeyValuePair<string, string> start in StartingItems(planeClass))
            {
                StoredItem item = NewItem(start.Value, StartingItemId(characterId, start.Key));
                item.Used = true;
                inv.Equipped[start.Key] = item;
            }
            d.Planes[characterId] = inv;
            log("INVENTORY", "new inventory of the plane " + (planeClass ?? "?") + " " + characterId + ": " +
                string.Join(", ", inv.Equipped.Select(p => p.Key + "=" + p.Value.DefId)));
        }
        else if (string.IsNullOrEmpty(inv.PlaneClass) && !string.IsNullOrEmpty(planeClass))
        {
            inv.PlaneClass = planeClass;
        }
        return Resolve(d, characterId)!;
    }

    // Starting items of a class: playerclasses.startingItems of the configBinary, or the table in PlayerData.
    public static Dictionary<string, string> StartingItems(string? planeClass)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        PlaneClassInfo? info = ItemCatalog.GetClass(planeClass);
        if (info != null && info.StartingItems.Count > 0)
        {
            foreach (KeyValuePair<string, string> pair in info.StartingItems)
            {
                int slot = ItemCatalog.ParseSlot(pair.Key);
                if (ItemCatalog.IsEquipSlot(slot)) result[ItemCatalog.SlotName(slot)] = pair.Value;
            }
            if (result.Count > 0) return result;
        }
        if (planeClass != null && PlayerData.StartingItemsByClass.TryGetValue(planeClass, out Dictionary<string, string>? table))
        {
            foreach (KeyValuePair<string, string> pair in table) result[pair.Key] = pair.Value;
        }
        return result;
    }

    // Same id PlayerData gave the starting items before the inventory existed: SHA-256 of
    // "item|characterId|slot", as a GeneralID of type CharacterItem.
    private static string StartingItemId(string characterId, string slot)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("item|" + characterId + "|" + slot));
        var id = new byte[16];
        Array.Copy(hash, id, 16);
        id[0] = (byte)((IdTypeCharacterItem << 2) | (id[0] & 0x03));
        bool lowZero = true;
        for (int i = 8; i < 16; i++)
        {
            if (id[i] != 0) { lowZero = false; break; }
        }
        if (lowZero) id[15] = 1;
        return PlayerData.ToBase64Url(id);
    }

    public static string NewItemId() => PlayerData.ToBase64Url(PlayerData.NewId(IdTypeCharacterItem));

    // New item instance (InventoryItem.Init): uses and decay from the config.
    public static StoredItem NewItem(string defId, string? id = null)
    {
        ItemDef? def = ItemCatalog.Get(defId);
        var item = new StoredItem { DefId = defId, Id = id ?? NewItemId() };
        if (def != null)
        {
            item.Uses = def.Uses;
            item.Soulbound = def.Soulbound;
            if (def.DecaysInSeconds > 0) item.DecayTimestamp = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + def.DecaysInSeconds);
        }
        return item;
    }

    // Grows the list to the capacity (empty slots); trailing empty slots beyond it are cut. Items are never dropped.
    public static void FitCapacity(List<StoredItem?> list, int capacity)
    {
        while (list.Count < capacity) list.Add(null);
        while (list.Count > capacity && list[list.Count - 1] == null) list.RemoveAt(list.Count - 1);
    }

    // Switching sharedCargoForAllPlanes moves the items: on, every plane's own cargo goes to the shared pod;
    // off, the shared pod goes to the storage (and to the first plane if the storage is full).
    private static void MigrateCargoMode(InventorySaveData d)
    {
        bool shared = ItemSettingsFile.Current.SharedCargoForAllPlanes;
        if (shared)
        {
            foreach (StoredInventory plane in d.Planes.Values)
            {
                for (int i = 0; i < plane.Cargo.Count; i++)
                {
                    StoredItem? item = plane.Cargo[i];
                    if (item == null) continue;
                    plane.Cargo[i] = null;
                    PutInList(d.SharedCargo, item);
                }
                plane.Cargo.Clear();
            }
        }
        else if (d.SharedCargo.Any(i => i != null))
        {
            FitCapacity(d.Storage.Cargo, StorageCapacity);
            StoredInventory? firstPlane = d.Planes.Values.FirstOrDefault();
            for (int i = 0; i < d.SharedCargo.Count; i++)
            {
                StoredItem? item = d.SharedCargo[i];
                if (item == null) continue;
                d.SharedCargo[i] = null;
                int free = d.Storage.Cargo.IndexOf(null);
                if (free >= 0) d.Storage.Cargo[free] = item;
                else if (firstPlane != null) PutInList(firstPlane.Cargo, item);
                else PutInList(d.Storage.Cargo, item);
            }
            d.SharedCargo.Clear();
        }
    }

    private static void PutInList(List<StoredItem?> list, StoredItem item)
    {
        int free = list.IndexOf(null);
        if (free >= 0) list[free] = item;
        else list.Add(item);
    }

    public static bool PositionExists(InvAccess a, InvPos p)
    {
        if (!p.Valid) return false;
        switch (p.Location)
        {
            case InvPos.LocCargo:
                return p.Index >= 0 && p.Index < a.Cargo.Count;
            case InvPos.LocEquipped:
                return !a.IsStorage && ItemCatalog.IsEquipSlot(p.Slot);
            case InvPos.LocAlternative:
                // EquipmentSlotHelpers.HasAlternativeSlot: only the main gun.
                return !a.IsStorage && p.Slot == ItemCatalog.SlotMainGun;
            case InvPos.LocPermanent:
                return p.Index >= 0 && p.Index < a.Inv.Permanents.Count;
        }
        return false;
    }

    public static StoredItem? Get(InvAccess a, InvPos p)
    {
        if (!PositionExists(a, p)) return null;
        switch (p.Location)
        {
            case InvPos.LocCargo:
                return a.Cargo[p.Index];
            case InvPos.LocEquipped:
                return a.Inv.Equipped.TryGetValue(ItemCatalog.SlotName(p.Slot), out StoredItem? e) ? e : null;
            case InvPos.LocAlternative:
                return a.Inv.Alternative.TryGetValue(ItemCatalog.SlotName(p.Slot), out StoredItem? alt) ? alt : null;
            case InvPos.LocPermanent:
                return a.Inv.Permanents[p.Index];
        }
        return null;
    }

    // Puts (or clears, with null) the item at the position. Permanent positions only accept removal here.
    public static void Set(InvAccess a, InvPos p, StoredItem? item)
    {
        switch (p.Location)
        {
            case InvPos.LocCargo:
                a.Cargo[p.Index] = item;
                break;
            case InvPos.LocEquipped:
                if (item == null) a.Inv.Equipped.Remove(ItemCatalog.SlotName(p.Slot));
                else a.Inv.Equipped[ItemCatalog.SlotName(p.Slot)] = item;
                break;
            case InvPos.LocAlternative:
                if (item == null) a.Inv.Alternative.Remove(ItemCatalog.SlotName(p.Slot));
                else a.Inv.Alternative[ItemCatalog.SlotName(p.Slot)] = item;
                break;
            case InvPos.LocPermanent:
                if (item == null && p.Index >= 0 && p.Index < a.Inv.Permanents.Count) a.Inv.Permanents.RemoveAt(p.Index);
                break;
        }
    }

    // StandardInventory.GetFirstFreeSpaceCargoSpace.
    public static InvPos FirstFreeCargo(InvAccess a)
    {
        for (int i = 0; i < a.Cargo.Count; i++)
        {
            if (a.Cargo[i] == null) return InvPos.CargoAt(i);
        }
        return InvPos.Invalid;
    }

    // Where an item is (by instance id), in this inventory.
    public static InvPos Find(InvAccess a, string itemId)
    {
        foreach (KeyValuePair<string, StoredItem> pair in a.Inv.Equipped)
            if (pair.Value.Id == itemId) return InvPos.EquippedAt(ItemCatalog.ParseSlot(pair.Key));
        foreach (KeyValuePair<string, StoredItem> pair in a.Inv.Alternative)
            if (pair.Value.Id == itemId) return InvPos.EquippedAt(ItemCatalog.ParseSlot(pair.Key), true);
        for (int i = 0; i < a.Cargo.Count; i++)
            if (a.Cargo[i]?.Id == itemId) return InvPos.CargoAt(i);
        for (int i = 0; i < a.Inv.Permanents.Count; i++)
            if (a.Inv.Permanents[i].Id == itemId) return new InvPos(true, InvPos.LocPermanent, 0, i);
        return InvPos.Invalid;
    }

    public static int NextAudit(InventorySaveData d)
    {
        int id = d.NextAuditId;
        d.NextAuditId = id >= int.MaxValue - 1 ? 1 : id + 1;
        return id;
    }

    public static int Currency(InventorySaveData d, string id) => d.Currencies.TryGetValue(id, out int v) ? v : 0;

    public static void AddCurrency(InventorySaveData d, string id, int amount)
    {
        int value = Math.Max(0, Currency(d, id) + amount);
        if (value == 0) d.Currencies.Remove(id);
        else d.Currencies[id] = value;
    }

    // PlayerData.SetIngredient: capped by the ingredient's stackCap (or ingredientDefaultCap).
    public static int AddIngredient(InventorySaveData d, string id, int amount)
    {
        int before = d.Ingredients.TryGetValue(id, out int v) ? v : 0;
        int cap = ItemCatalog.Ingredients.TryGetValue(id, out IngredientInfo? info) ? info.StackCap : ItemCatalog.IngredientDefaultCap;
        int after = Math.Max(0, before + amount);
        if (amount > 0 && after > cap) after = Math.Max(before, cap);
        if (after == 0) d.Ingredients.Remove(id);
        else d.Ingredients[id] = after;
        return after - before;
    }

    // Takes out the items whose decay time passed (InventoryItem.HasDecayed). Returns how many.
    public static int PurgeDecayed(InventorySaveData d)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int removed = 0;
        void PurgeList(List<StoredItem?> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i]!.HasDecayed(now)) { list[i] = null; removed++; }
            }
        }
        void PurgeInv(StoredInventory inv)
        {
            foreach (string key in inv.Equipped.Where(p => p.Value.HasDecayed(now)).Select(p => p.Key).ToList()) { inv.Equipped.Remove(key); removed++; }
            foreach (string key in inv.Alternative.Where(p => p.Value.HasDecayed(now)).Select(p => p.Key).ToList()) { inv.Alternative.Remove(key); removed++; }
            PurgeList(inv.Cargo);
            removed += inv.Permanents.RemoveAll(p => p.HasDecayed(now));
        }
        PurgeInv(d.Storage);
        PurgeList(d.SharedCargo);
        foreach (StoredInventory plane in d.Planes.Values) PurgeInv(plane);
        return removed;
    }
}
