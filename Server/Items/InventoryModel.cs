using System.Globalization;

// Server-side copy of the client's inventory types (InventoryItem, StandardInventory, InventoryPosition).
// These are the objects saved in inventory.json; InventoryWire turns them into the bytes the client reads.

// One item instance (InventoryItem + InventoryItemSerializer fields).
public sealed class StoredItem
{
    public string DefId { get; set; } = "";
    public string Id { get; set; } = "";          // InventoryId: GeneralID (type CharacterItem) in base64url
    public int Uses { get; set; }
    public int DecayTimestamp { get; set; }       // unix seconds; 0 = never decays
    public bool Soulbound { get; set; }           // soulboundInstance
    public bool Used { get; set; }                // usedInstance
    public bool Salvaged { get; set; }            // salvagedInstance
    public int Condition { get; set; }            // 0 pristine, < 0 degraded, > 0 supercharged
    public bool Favorite { get; set; }
    public List<StoredStatMod> Supercharged { get; set; } = new List<StoredStatMod>();

    public StoredItem Clone()
    {
        return new StoredItem
        {
            DefId = DefId,
            Id = Id,
            Uses = Uses,
            DecayTimestamp = DecayTimestamp,
            Soulbound = Soulbound,
            Used = Used,
            Salvaged = Salvaged,
            Condition = Condition,
            Favorite = Favorite,
            Supercharged = Supercharged.Select(m => m.Clone()).ToList()
        };
    }

    // InventoryItem.HasDecayed.
    public bool HasDecayed(long nowUnix) => DecayTimestamp > 0 && DecayTimestamp < nowUnix;

    public override string ToString()
    {
        string text = DefId + "#" + (Id.Length > 6 ? Id.Substring(0, 6) : Id);
        if (Condition != 0) text += " cond " + Condition.ToString(CultureInfo.InvariantCulture);
        return text;
    }
}

// StatMod of a supercharged item (InventoryItemSerializer writes name, amount, math, leveling, levelingSource).
public sealed class StoredStatMod
{
    public string Name { get; set; } = "";
    public float Amount { get; set; }
    public int Math { get; set; }
    public bool Leveling { get; set; }
    public int LevelingSource { get; set; }

    public StoredStatMod Clone() => new StoredStatMod
    {
        Name = Name,
        Amount = Amount,
        Math = Math,
        Leveling = Leveling,
        LevelingSource = LevelingSource
    };

    public static StoredStatMod From(ItemStatMod mod) => new StoredStatMod
    {
        Name = mod.Name,
        Amount = mod.Amount,
        Math = mod.Math,
        Leveling = mod.Leveling,
        LevelingSource = mod.LevelingSource
    };
}

// One StandardInventory: a plane (Owner = Plane) or the account storage (Owner = Storage).
// Equipped and Alternative are keyed by slot name (MainGun, SecondaryAttack, Engine, Armor).
public sealed class StoredInventory
{
    public string CharacterId { get; set; } = "";
    public string PlaneClass { get; set; } = "";
    public Dictionary<string, StoredItem> Equipped { get; set; } = new Dictionary<string, StoredItem>(StringComparer.Ordinal);
    public Dictionary<string, StoredItem> Alternative { get; set; } = new Dictionary<string, StoredItem>(StringComparer.Ordinal);
    public List<StoredItem?> Cargo { get; set; } = new List<StoredItem?>();
    public List<StoredItem> Permanents { get; set; } = new List<StoredItem>();
    public Dictionary<string, int> Accumulated { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

    public void Normalize()
    {
        Equipped ??= new Dictionary<string, StoredItem>(StringComparer.Ordinal);
        Alternative ??= new Dictionary<string, StoredItem>(StringComparer.Ordinal);
        Cargo ??= new List<StoredItem?>();
        Permanents ??= new List<StoredItem>();
        Accumulated ??= new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string key in Equipped.Where(p => p.Value == null || string.IsNullOrEmpty(p.Value.DefId)).Select(p => p.Key).ToList())
            Equipped.Remove(key);
        foreach (string key in Alternative.Where(p => p.Value == null || string.IsNullOrEmpty(p.Value.DefId)).Select(p => p.Key).ToList())
            Alternative.Remove(key);
        for (int i = 0; i < Cargo.Count; i++)
        {
            if (Cargo[i] != null && string.IsNullOrEmpty(Cargo[i]!.DefId)) Cargo[i] = null;
        }
        Permanents.RemoveAll(p => p == null || string.IsNullOrEmpty(p.DefId));
        foreach (StoredItem item in AllItems()) item.Supercharged ??= new List<StoredStatMod>();
    }

    public IEnumerable<StoredItem> AllItems()
    {
        foreach (StoredItem item in Equipped.Values) yield return item;
        foreach (StoredItem item in Alternative.Values) yield return item;
        foreach (StoredItem? item in Cargo) if (item != null) yield return item;
        foreach (StoredItem item in Permanents) yield return item;
    }
}

// inventory.json.
public sealed class InventorySaveData
{
    // characterId of the account storage (PlayerData.storageCharacterId comes from storageInv.CharacterId).
    public string StorageCharacterId { get; set; } = "";
    public StoredInventory Storage { get; set; } = new StoredInventory();

    // Cargo shared by every plane (sharedCargoForAllPlanes).
    public List<StoredItem?> SharedCargo { get; set; } = new List<StoredItem?>();

    // Plane inventories by characterId.
    public Dictionary<string, StoredInventory> Planes { get; set; } = new Dictionary<string, StoredInventory>(StringComparer.Ordinal);

    // PlayerData.currencies (mulch "newscrap", store coins...) and ingredientInventory.
    public Dictionary<string, int> Currencies { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);
    public Dictionary<string, int> Ingredients { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

    // Counter of the ids that go in the RPC answers (AuditLog ids; -1 = refused).
    public int NextAuditId { get; set; } = 1;

    // true once the starting currencies, ingredients and storage items of items.json were given.
    public bool StartingStateGiven { get; set; }
}

// InventoryPosition: location (Cargo 0, Equipped 1, Permanent 2, Equipped_Alternative 3), slot and index.
public readonly struct InvPos : IEquatable<InvPos>
{
    public const int LocCargo = 0;
    public const int LocEquipped = 1;
    public const int LocPermanent = 2;
    public const int LocAlternative = 3;

    public readonly bool Valid;
    public readonly int Location;
    public readonly int Slot;
    public readonly int Index;

    public InvPos(bool valid, int location, int slot, int index)
    {
        Valid = valid;
        Location = location;
        Slot = slot;
        Index = index;
    }

    public static readonly InvPos Invalid = new InvPos(false, 0, 0, 0);

    // new InventoryPosition(InventoryLocation.Cargo, index): slot Plane (0).
    public static InvPos CargoAt(int index) => new InvPos(true, LocCargo, 0, index);

    public static InvPos EquippedAt(int slot, bool alternative = false) =>
        new InvPos(true, alternative ? LocAlternative : LocEquipped, slot, -1);

    public bool HasIndex => Location == LocCargo || Location == LocPermanent;
    public bool IsEquip => Location == LocEquipped || Location == LocAlternative;

    public bool Equals(InvPos other) => Valid == other.Valid && Location == other.Location && Slot == other.Slot && Index == other.Index;
    public override bool Equals(object? obj) => obj is InvPos other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Valid, Location, Slot, Index);

    public override string ToString()
    {
        if (!Valid) return "[invalid]";
        switch (Location)
        {
            case LocEquipped: return "[equipped " + ItemCatalog.SlotName(Slot) + "]";
            case LocAlternative: return "[alternate " + ItemCatalog.SlotName(Slot) + "]";
            case LocPermanent: return "[permanent " + Index + "]";
            default: return "[cargo " + Index + "]";
        }
    }
}
