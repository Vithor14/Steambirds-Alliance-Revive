using System.Globalization;

// Item definitions read from the game's configBinary: the same files the client loads in SBConfig
// (Items/**, ingredients, playerclasses, mulching, reforging, global, clouds, gunlist and the Units/** loot).
//
// The server needs them to:
//  - roll loot (items bucketed by slot and tier, like SBConfig.GetBucketedItemConfigs);
//  - check equip slots and which classes can use an item (PlayerClassConfig.allowedItemTags);
//  - price recycling (MulchConfig: InventoryItem.GetItemTrashReward) and reforging (ReforgeConfig);
//  - write the item catalog (ItemCatalogExport).
//
// Nothing here is hardcoded: a newer or modded configBinary changes the items without touching the server.
public sealed class ItemStatMod
{
    public string Name = "";
    public float Amount;
    public int Math = StatModMathAdd;          // StatModMath: Multiply 0, Add 1, PostAdd 2, Scale 3
    public bool Leveling;
    public int LevelingSource;                 // StatMod.LevelingSource: Context 0, Plane 1, Pilot 2, NearbyPlayers 3

    public const int StatModMathMultiply = 0;
    public const int StatModMathAdd = 1;
    public const int StatModMathPostAdd = 2;
    public const int StatModMathScale = 3;

    public static ItemStatMod FromNode(ConfigNode node)
    {
        return new ItemStatMod
        {
            Name = node["name"].AsString(""),
            Amount = node["amount"].AsFloat(0f),
            Math = ParseMath(node["math"].AsString("add")),
            Leveling = node["leveling"].AsBool(false),
            LevelingSource = ParseLevelingSource(node["levelingSource"].AsString("Context"))
        };
    }

    public static int ParseMath(string text)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "multiply": return StatModMathMultiply;
            case "postadd": return StatModMathPostAdd;
            case "scale": return StatModMathScale;
            default: return StatModMathAdd;
        }
    }

    public static int ParseLevelingSource(string text)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "plane": return 1;
            case "pilot": return 2;
            case "nearbyplayers": return 3;
            default: return 0;
        }
    }
}

public sealed class ItemDef
{
    public string Id = "";
    public string Slot = "";             // EquipmentSlot name: MainGun, Armor, Engine, SecondaryAttack, Health...
    public int SlotValue;                // EquipmentSlot value (CopperReader.NomesDosSlots index)
    public int Tier;
    public string Tag = "";
    public List<string> ExtraTags = new List<string>();
    public string Icon = "";
    public bool Soulbound;
    public bool Tradable = true;
    public bool Obsolete;
    public string ObsoleteMigrateTo = "";
    public bool PreRelease;
    public bool DevourOnPickup;
    public int Amount;
    public int Uses;
    public int DecaysInSeconds;
    public string Plane = "";
    public string ConditionModsPrefix = "";
    public string DropTrashMode = "";    // ItemDropTrashMode: Auto, Never, Always
    public List<ItemStatMod> StatMods = new List<ItemStatMod>();
    public string SourceFile = "";

    // Raw config node: gunMounts and actions (the catalog and the consume code read them).
    public ConfigNode Node = ConfigNode.Missing;

    // Generated ingredient pickups ("<ingredient>_ingpickup"): the ingredient they give.
    public string IngredientId = "";

    // MainGun, Armor, Engine, SecondaryAttack (InventoryDisplay.IsValidEquipmentSlot).
    public bool IsEquippable => ItemCatalog.IsEquipSlot(SlotValue);

    // Upgrade and UserUpgrade: picked up into the cargo, then "accumulated" into the plane (permanent stats).
    public bool IsUpgrade => SlotValue == ItemCatalog.SlotUpgrade || SlotValue == ItemCatalog.SlotUserUpgrade;

    // The client adds the tag to extraTags when it registers the item (SBConfig.RegisterItem).
    public bool HasTag(string tag)
    {
        if (string.IsNullOrEmpty(tag)) return false;
        if (string.Equals(Tag, tag, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (string t in ExtraTags)
        {
            if (string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    // Item actions (the "actions" list; items without it use gunMounts as actions).
    public IEnumerable<ConfigNode> Actions => Node["actions"].Items;

    public override string ToString() => Id + " (" + Slot + " T" + Tier + ")";
}

public sealed class PlaneClassInfo
{
    public string Id = "";
    public string Name = "";
    public bool HideIfLocked;
    public bool PreRelease;
    public string UnlockItemId = "";
    public HashSet<string> AllowedItemTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> StartingItems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // slot -> item
    public Dictionary<string, string> VariantUnlocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // variant -> unlock item

    // PlayerClassConfig.DoesAllowItemTag: an item without tag fits every class.
    public bool AllowsTag(string tag) => string.IsNullOrEmpty(tag) || AllowedItemTags.Contains(tag);
}

public sealed class IngredientInfo
{
    public string Id = "";
    public string Icon = "";
    public int StackCap = -1;
    public int PickupTier = -1;
}

// ReforgeCost.ItemsReforgeCost and CurrencyReforgeCost.
public sealed class ReforgeItemCost
{
    public int Count;
    public string Id = "";
    public List<string> Tags = new List<string>();
    public int? Tier;
    public int? MinTier;

    public bool Matches(ItemDef def)
    {
        if (Id.Length > 0 && !string.Equals(def.Id, Id, StringComparison.OrdinalIgnoreCase)) return false;
        if (Tier.HasValue && def.Tier != Tier.Value) return false;
        if (MinTier.HasValue && def.Tier < MinTier.Value) return false;
        foreach (string tag in Tags)
        {
            if (!def.HasTag(tag)) return false;
        }
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string> { Count.ToString(CultureInfo.InvariantCulture) + "x" };
        if (Id.Length > 0) parts.Add(Id);
        if (Tier.HasValue) parts.Add("T" + Tier.Value);
        if (MinTier.HasValue) parts.Add(">=T" + MinTier.Value);
        if (Tags.Count > 0) parts.Add("[" + string.Join(" ", Tags) + "]");
        return string.Join(" ", parts);
    }
}

public sealed class ReforgeCostInfo
{
    public List<ReforgeItemCost> Items = new List<ReforgeItemCost>();
    public Dictionary<string, int> Currencies = new Dictionary<string, int>(StringComparer.Ordinal);
}

public sealed class SuperchargeChance
{
    public string ItemId = "";
    public float Probability;
    public List<ItemStatMod> StatMods = new List<ItemStatMod>();
}

// Loot entries a unit has in the game data: "loot" (per player) and "lootGlobal". Each entry is a LootTableDef:
// a lootTableId (a named table the original server had) and/or an inline lootTable.
public sealed class UnitLootEntry
{
    public string Key = "";
    public string LootTableId = "";
    public ConfigNode Node = ConfigNode.Missing;
    public bool Global;
}

public sealed class UnitLootInfo
{
    public string TemplateName = "";
    public int Tier;
    public List<UnitLootEntry> Entries = new List<UnitLootEntry>();
}

public static class ItemCatalog
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // EquipmentSlot values used here.
    public const int SlotPlane = 0;
    public const int SlotArmor = 1;
    public const int SlotEngine = 2;
    public const int SlotMainGun = 4;
    public const int SlotSecondaryAttack = 5;
    public const int SlotHealth = 6;
    public const int SlotEnergy = 7;
    public const int SlotConsumable = 8;
    public const int SlotUpgrade = 10;
    public const int SlotUserUpgrade = 11;
    public const int SlotConsumableAction = 12;
    public const int SlotQuest = 13;

    // Slots a random item ({ "tier": N } without slot) can come from (SteambirdsLootTable.slotsForUnspecifiedItems).
    public static readonly int[] RandomItemSlots = { SlotArmor, SlotEngine, SlotMainGun, SlotSecondaryAttack };

    // StandardInventory.UsedEquipmentSlots, in the order of StandardInventorySerializer.
    public static readonly int[] EquipSlotsInWireOrder = { SlotMainGun, SlotSecondaryAttack, SlotEngine, SlotArmor };

    public static bool IsEquipSlot(int slot) => slot == SlotArmor || slot == SlotEngine || slot == SlotMainGun || slot == SlotSecondaryAttack;

    public static string SlotName(int slot) => CopperReader.Name(CopperReader.SlotNames, slot);

    // EquipmentSlot from its name (case does not matter: playerclasses writes "armor"). -1 when unknown.
    public static int ParseSlot(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return -1;
        for (int i = 0; i < CopperReader.SlotNames.Length; i++)
        {
            if (string.Equals(CopperReader.SlotNames[i], name.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static Dictionary<string, ItemDef> items = new Dictionary<string, ItemDef>(StringComparer.Ordinal);
    private static Dictionary<int, Dictionary<int, List<ItemDef>>> buckets = new Dictionary<int, Dictionary<int, List<ItemDef>>>();
    private static Dictionary<string, PlaneClassInfo> classes = new Dictionary<string, PlaneClassInfo>(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, IngredientInfo> ingredients = new Dictionary<string, IngredientInfo>(StringComparer.Ordinal);
    private static Dictionary<string, ReforgeCostInfo> reforgeCosts = new Dictionary<string, ReforgeCostInfo>(StringComparer.Ordinal);
    private static Dictionary<string, List<SuperchargeChance>> supercharge = new Dictionary<string, List<SuperchargeChance>>(StringComparer.Ordinal);
    private static Dictionary<string, UnitLootInfo> unitLoot = new Dictionary<string, UnitLootInfo>(StringComparer.OrdinalIgnoreCase);

    public static bool Loaded { get; private set; }
    public static int Count => items.Count;
    public static IEnumerable<ItemDef> All => items.Values;
    public static IReadOnlyDictionary<string, PlaneClassInfo> Classes => classes;
    public static IReadOnlyDictionary<string, IngredientInfo> Ingredients => ingredients;

    // global config values the inventory needs (with this build's values as fallback).
    public static int BaseCargoSlots { get; private set; } = 12;          // baseNumInventorySlots
    public static int BaseStorageSlots { get; private set; } = 16;        // baseNumStorageSlots
    public static float LootBagLifetime { get; private set; } = 30f;      // lootbagLifetime
    public static int IngredientDefaultCap { get; private set; } = 999;   // ingredientDefaultCap
    public static string IngredientPickupSuffix { get; private set; } = "_ingpickup";
    public static float DefaultConditionDegradation { get; private set; } = 1f;
    public static List<KeyValuePair<float, float>> ConditionDegradationTable { get; private set; } = new List<KeyValuePair<float, float>>();

    // MulchConfig.
    public static string MulchCurrency { get; private set; } = "newscrap";
    private static int mulchDefault;
    private static Dictionary<int, int> mulchTierValues = new Dictionary<int, int>();
    private static List<(List<string> Tags, int Additional, float Multiply)> mulchItemValues = new List<(List<string>, int, float)>();
    public static bool HasMulchConfig { get; private set; }
    public static bool HasReforgeConfig => reforgeCosts.Count > 0;

    public static ItemDef? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return items.TryGetValue(id, out ItemDef? def) ? def : null;
    }

    public static PlaneClassInfo? GetClass(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return classes.TryGetValue(id, out PlaneClassInfo? info) ? info : null;
    }

    public static UnitLootInfo? GetUnitLoot(string? templateName)
    {
        if (string.IsNullOrEmpty(templateName)) return null;
        return unitLoot.TryGetValue(templateName, out UnitLootInfo? info) ? info : null;
    }

    // Items of a slot and tier (SBConfig.GetBucketedItemConfigs). Obsolete and pre-release items stay out.
    public static IReadOnlyList<ItemDef> Bucket(int slot, int tier)
    {
        if (buckets.TryGetValue(slot, out Dictionary<int, List<ItemDef>>? byTier) && byTier.TryGetValue(tier, out List<ItemDef>? list))
            return list;
        return Array.Empty<ItemDef>();
    }

    // Classes that can equip the item (InventoryItem.GetUsableClasses). Empty tag = every class.
    public static List<PlaneClassInfo> UsableClasses(ItemDef def, bool includeHidden = false)
    {
        return classes.Values
            .Where(c => (includeHidden || (!c.HideIfLocked && !c.PreRelease)) && c.AllowsTag(def.Tag))
            .ToList();
    }

    // ------------------------------------------------------------------ loading

    public static bool Load(Action<string, string> log)
    {
        if (!ConfigBinary.Loaded)
        {
            log("ITEMS", "configBinary not loaded: no item catalog. Loot, reforging and recycling are off " +
                "and the planes use the built-in starting items.");
            return false;
        }

        try
        {
            LoadGlobal();
            LoadClasses();
            LoadIngredients();
            LoadItems();
            LoadMulch();
            LoadReforge();
            LoadUnitLoot();
            Loaded = true;

            int lootUnits = unitLoot.Count;
            log("ITEMS", items.Count + " items from the configBinary (" + string.Join(", ",
                    items.Values.GroupBy(i => i.Slot).OrderByDescending(g => g.Count()).Select(g => g.Key + " " + g.Count())) + "), " +
                classes.Count + " classes, " + ingredients.Count + " ingredient(s), " + lootUnits + " unit(s) with loot in the game, " +
                "recycling " + (HasMulchConfig ? "(mulching, currency '" + MulchCurrency + "')" : "no 'mulching' config (value 0)") + ", reforge " +
                (HasReforgeConfig ? reforgeCosts.Count + " cost(s)" : "no 'reforging' config"));
            return true;
        }
        catch (Exception e)
        {
            log("ITEMS", "error reading the items of the configBinary: " + e.GetType().Name + " " + e.Message);
            return false;
        }
    }

    private static void LoadGlobal()
    {
        ConfigNode g = ConfigBinary.Get("global");
        BaseCargoSlots = Math.Max(1, g["baseNumInventorySlots"].AsInt(12));
        BaseStorageSlots = Math.Max(1, g["baseNumStorageSlots"].AsInt(16));
        LootBagLifetime = Math.Max(5f, g["lootbagLifetime"].AsFloat(30f));
        IngredientDefaultCap = Math.Max(1, g["ingredientDefaultCap"].AsInt(999));
        IngredientPickupSuffix = g["ingredientPickupSuffix"].AsString("_ingpickup");
        DefaultConditionDegradation = g["defaultItemConditionDegradation"].AsFloat(1f);

        var table = new List<KeyValuePair<float, float>>();
        foreach (KeyValuePair<string, ConfigNode> pair in g["itemConditionDegradationTable"].Pairs)
        {
            if (float.TryParse(pair.Key, NumberStyles.Float, Inv, out float key))
                table.Add(new KeyValuePair<float, float>(key, pair.Value.AsFloat(DefaultConditionDegradation)));
        }
        ConditionDegradationTable = table;
    }

    private static void LoadClasses()
    {
        var result = new Dictionary<string, PlaneClassInfo>(StringComparer.OrdinalIgnoreCase);
        ConfigNode names = ConfigBinary.Get("Texts/en-US/planeclasses");
        foreach (KeyValuePair<string, ConfigNode> pair in ConfigBinary.Get("playerclasses").Pairs)
        {
            ConfigNode c = pair.Value;
            var info = new PlaneClassInfo
            {
                Id = pair.Key,
                Name = names[pair.Key]["name"].AsString(c["name"].AsString(pair.Key)),
                HideIfLocked = c["hideIfLocked"].AsBool(false),
                PreRelease = c["preRelease"].AsBool(false),
                UnlockItemId = c["unlockItemId"].AsString("")
            };
            foreach (string tag in c["allowedItemTags"].AsStringList()) info.AllowedItemTags.Add(tag);
            foreach (ConfigNode start in c["startingItems"].Items)
            {
                int slot = ParseSlot(start["slot"].AsString(""));
                string item = start["item"].AsString("");
                if (slot >= 0 && item.Length > 0) info.StartingItems[SlotName(slot)] = item;
            }
            foreach (ConfigNode variant in c["variants"].Items)
            {
                string vid = variant["id"].AsString("");
                if (vid.Length > 0) info.VariantUnlocks[vid] = variant["unlockItemId"].AsString("");
            }
            result[pair.Key] = info;
        }
        classes = result;
    }

    private static void LoadIngredients()
    {
        var result = new Dictionary<string, IngredientInfo>(StringComparer.Ordinal);
        foreach (ConfigNode n in ConfigBinary.Get("ingredients").Items)
        {
            string id = n["id"].AsString("");
            if (id.Length == 0) continue;
            int cap = n["stackCap"].AsInt(-1);
            result[id] = new IngredientInfo
            {
                Id = id,
                Icon = n["icon"].AsString("UnknownArt"),
                StackCap = cap <= 0 ? IngredientDefaultCap : cap,
                PickupTier = n["pickupTier"].AsInt(-1)
            };
        }
        ingredients = result;
    }

    private static void LoadItems()
    {
        var result = new Dictionary<string, ItemDef>(StringComparer.Ordinal);
        ConfigNode? ingredientTemplate = null;

        foreach (KeyValuePair<string, ConfigNode> file in ConfigBinary.WithPrefix("Items/").OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (ConfigNode n in file.Value.Items)
            {
                string id = n["id"].AsString("");
                if (id.Length == 0) continue;
                if (id == "ingredient_template_pickup")
                {
                    ingredientTemplate = n;
                    continue;
                }
                ItemDef def = FromNode(n, file.Key);
                result[id] = def;
            }
        }

        // Ingredient pickups (SBConfig.GenerateIngredientPickup): one item per ingredient, from the template.
        foreach (IngredientInfo ing in ingredients.Values)
        {
            ItemDef def = ingredientTemplate != null ? FromNode(ingredientTemplate, "ingredients") : new ItemDef
            {
                Slot = SlotName(SlotConsumableAction),
                SlotValue = SlotConsumableAction,
                DevourOnPickup = true
            };
            def.Id = ing.Id + IngredientPickupSuffix;
            def.Icon = ing.Icon;
            def.Tier = ing.PickupTier;
            def.IngredientId = ing.Id;
            def.DevourOnPickup = true;
            def.SourceFile = "ingredients";
            result[def.Id] = def;
        }

        items = result;

        var newBuckets = new Dictionary<int, Dictionary<int, List<ItemDef>>>();
        foreach (ItemDef def in result.Values)
        {
            if (def.PreRelease || def.Obsolete) continue;
            AddToBucket(newBuckets, def.SlotValue, def.Tier, def);
            // Health and Energy also count as Consumable (SBConfig.OnLoadItemConfig).
            if (def.SlotValue == SlotHealth || def.SlotValue == SlotEnergy) AddToBucket(newBuckets, SlotConsumable, def.Tier, def);
        }
        buckets = newBuckets;
    }

    private static void AddToBucket(Dictionary<int, Dictionary<int, List<ItemDef>>> target, int slot, int tier, ItemDef def)
    {
        if (!target.TryGetValue(slot, out Dictionary<int, List<ItemDef>>? byTier))
        {
            byTier = new Dictionary<int, List<ItemDef>>();
            target[slot] = byTier;
        }
        if (!byTier.TryGetValue(tier, out List<ItemDef>? list))
        {
            list = new List<ItemDef>();
            byTier[tier] = list;
        }
        list.Add(def);
    }

    private static ItemDef FromNode(ConfigNode n, string file)
    {
        int slot = ParseSlot(n["slot"].AsString(""));
        var def = new ItemDef
        {
            Id = n["id"].AsString(""),
            Slot = slot >= 0 ? SlotName(slot) : n["slot"].AsString("?"),
            SlotValue = slot,
            Tier = n["tier"].AsInt(0),
            Tag = n["tag"].AsString(""),
            Icon = n["icon"].AsString(""),
            Soulbound = n["soulbound"].AsBool(false),
            Tradable = n["tradable"].AsBool(slot != SlotQuest),
            Obsolete = n["obsolete"].AsBool(false),
            ObsoleteMigrateTo = n["obsoleteMigrateTo"].AsString(""),
            PreRelease = n["preRelease"].AsBool(false),
            DevourOnPickup = n["devourOnPickup"].AsBool(false),
            Amount = n["amount"].AsInt(0),
            Uses = n["uses"].AsInt(0),
            DecaysInSeconds = n["decaysInSeconds"].AsInt(0),
            Plane = n["plane"].AsString(""),
            ConditionModsPrefix = n["conditionModsPrefix"].AsString(""),
            DropTrashMode = n["dropTrashMode"].AsString(""),
            Node = n,
            SourceFile = file
        };
        def.ExtraTags = SplitTags(n["extraTags"]);
        foreach (ConfigNode mod in n["statMods"].Items) def.StatMods.Add(ItemStatMod.FromNode(mod));
        return def;
    }

    // Tag lists come as a list or as one text ("elemental fire"); both are accepted.
    private static List<string> SplitTags(ConfigNode node)
    {
        return node.AsStringList()
            .SelectMany(t => t.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void LoadMulch()
    {
        ConfigNode m = ConfigBinary.Get("mulching");
        HasMulchConfig = !m.IsMissing && m.Count > 0;
        MulchCurrency = m["mulchCurrency"].AsString("newscrap");
        mulchDefault = m["defaultValue"].AsInt(0);
        var tiers = new Dictionary<int, int>();
        foreach (KeyValuePair<string, ConfigNode> pair in m["tierMulchValues"].Pairs)
        {
            if (int.TryParse(pair.Key, NumberStyles.Integer, Inv, out int tier)) tiers[tier] = pair.Value.AsInt(0);
        }
        mulchTierValues = tiers;
        var values = new List<(List<string>, int, float)>();
        foreach (ConfigNode v in m["itemMulchValues"].Items)
            values.Add((SplitTags(v["extraTags"]), v["additionalValue"].AsInt(0), v["multiplyAmount"].AsFloat(1f)));
        mulchItemValues = values;
    }

    // InventoryItem.GetItemTrashReward: value by tier, plus the extra of every itemMulchValues whose tags the item has.
    public static int MulchValue(ItemDef? def)
    {
        if (def == null) return 0;
        // The client starts from defaultValue and then always takes the tier value (0 when the tier is missing).
        int value = mulchTierValues.TryGetValue(def.Tier, out int byTier) ? byTier : (HasMulchConfig ? 0 : mulchDefault);
        float multiply = 1f;
        foreach ((List<string> tags, int additional, float mult) in mulchItemValues)
        {
            if (tags.Count == 0 || def.ExtraTags.Count == 0) continue;
            if (tags.All(t => def.ExtraTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
            {
                value += additional;
                multiply += mult - 1f;
            }
        }
        return (int)Math.Ceiling(value * multiply);
    }

    private static void LoadReforge()
    {
        ConfigNode r = ConfigBinary.Get("reforging");
        var costs = new Dictionary<string, ReforgeCostInfo>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ConfigNode> pair in r["Costs"].Pairs)
        {
            var cost = new ReforgeCostInfo();
            foreach (ConfigNode it in pair.Value["Items"].Items)
            {
                var c = new ReforgeItemCost
                {
                    Count = it["count"].AsInt(1),
                    Id = it["id"].AsString(""),
                    Tier = it["tier"].AsNullableInt(),
                    MinTier = it["minTier"].AsNullableInt()
                };
                c.Tags = SplitTags(it["tags"]).Select(t => t.ToLowerInvariant()).ToList();
                cost.Items.Add(c);
            }
            foreach (ConfigNode cur in pair.Value["Currencies"].Items)
            {
                string id = cur["id"].AsString("");
                if (id.Length > 0) cost.Currencies[id] = cur["count"].AsInt(0);
            }
            costs[pair.Key.ToLowerInvariant()] = cost;
        }
        reforgeCosts = costs;

        var chances = new Dictionary<string, List<SuperchargeChance>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ConfigNode> pair in r["SuperchargeNewStatProbabilities"].Pairs)
        {
            var list = new List<SuperchargeChance>();
            foreach (ConfigNode p in pair.Value.Items)
            {
                var chance = new SuperchargeChance { ItemId = p["itemId"].AsString(""), Probability = p["probability"].AsFloat(0f) };
                foreach (ConfigNode mod in p["statmods"].Items) chance.StatMods.Add(ItemStatMod.FromNode(mod));
                list.Add(chance);
            }
            chances[pair.Key.ToLowerInvariant()] = list;
        }
        supercharge = chances;
    }

    // InventoryItem.GenerateItemConditionLookupKeys: from the most specific key to "generic <suffix>".
    public static List<string> ConditionLookupKeys(ItemDef def, string suffix)
    {
        var keys = new List<string>();
        if (def.ConditionModsPrefix.Length > 0) keys.Add(def.ConditionModsPrefix + " " + suffix);
        string rarity = "common";
        if (def.ExtraTags.Contains("legendary", StringComparer.OrdinalIgnoreCase)) rarity = "legendary";
        if (def.ExtraTags.Contains("epic", StringComparer.OrdinalIgnoreCase)) rarity = "epic";
        if (def.ExtraTags.Contains("rare", StringComparer.OrdinalIgnoreCase)) rarity = "rare";
        // The client's TagSet also holds the item's tag (SBConfig.RegisterItem).
        var tags = new List<string>(def.ExtraTags);
        if (def.Tag.Length > 0 && !tags.Contains(def.Tag, StringComparer.OrdinalIgnoreCase)) tags.Add(def.Tag);

        foreach (string tag in tags) keys.Add("T" + def.Tier + " " + tag + " " + rarity + " " + suffix);
        keys.Add("T" + def.Tier + " " + rarity + " " + suffix);
        foreach (string tag in tags) keys.Add("T" + def.Tier + " " + tag + " " + suffix);
        keys.Add("T" + def.Tier + " " + suffix);
        foreach (string tag in tags) keys.Add(tag + " " + rarity + " " + suffix);
        foreach (string tag in tags) keys.Add(tag + " " + suffix);
        keys.Add("Generic " + suffix);
        return keys.Select(k => k.Trim().ToLowerInvariant()).ToList();
    }

    // InventoryItem.GetReforgeCost: degraded items look up their condition ("-2"), the others "Supercharge".
    public static ReforgeCostInfo? ReforgeCost(ItemDef def, int condition)
    {
        string suffix = condition < 0 ? condition.ToString(Inv) : "Supercharge";
        foreach (string key in ConditionLookupKeys(def, suffix))
        {
            if (reforgeCosts.TryGetValue(key, out ReforgeCostInfo? cost)) return cost;
        }
        return null;
    }

    public static List<SuperchargeChance> SuperchargeChances(ItemDef def)
    {
        foreach (string key in ConditionLookupKeys(def, ""))
        {
            if (supercharge.TryGetValue(key, out List<SuperchargeChance>? list)) return list;
        }
        return new List<SuperchargeChance>();
    }

    // InventoryItem.canBeSupercharged: tier 9+ armor, engine, main gun or secondary.
    public static bool CanBeSupercharged(ItemDef def) => def.Tier >= 9 && def.IsEquippable;

    // InventoryItem.canBeDegraded: armor, engine, main gun and secondary.
    public static bool CanBeDegraded(ItemDef def) => def.IsEquippable;

    // InventoryItem.DegradeCondition: the condition goes down by the global table (the first key the condition is
    // above) or by defaultItemConditionDegradation. A supercharged item (condition 1) loses its extra stats and
    // becomes pristine (0) with the default step.
    public static int DegradedCondition(int condition)
    {
        float step = DefaultConditionDegradation;
        foreach (KeyValuePair<float, float> pair in ConditionDegradationTable)
        {
            if (condition > pair.Key)
            {
                step = pair.Value;
                break;
            }
        }
        return (int)Math.Floor(condition - step);
    }

    private static void LoadUnitLoot()
    {
        var result = new Dictionary<string, UnitLootInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, ConfigNode> file in ConfigBinary.WithPrefix("Units/"))
        {
            ConfigNode unit = file.Value;
            string name = unit["templateName"].AsString("");
            if (name.Length == 0) continue;
            var info = new UnitLootInfo { TemplateName = name, Tier = unit["tier"].AsInt(0) };
            AddLootEntries(info, unit["loot"], false);
            AddLootEntries(info, unit["lootGlobal"], true);
            if (info.Entries.Count > 0) result[name] = info;
        }
        unitLoot = result;
    }

    private static void AddLootEntries(UnitLootInfo info, ConfigNode loot, bool global)
    {
        foreach (KeyValuePair<string, ConfigNode> pair in loot.Pairs)
        {
            info.Entries.Add(new UnitLootEntry
            {
                Key = pair.Key,
                LootTableId = pair.Value["lootTableId"].AsString(""),
                Node = pair.Value,
                Global = global
            });
        }
    }

    // ------------------------------------------------------------------ texts

    // Item name and description (the lore text) in a locale; falls back to en-US and then to the id.
    public static string Name(string id, string locale = "en-US")
    {
        string text = ItemText(id, "name", locale);
        if (text.Length == 0) text = ItemText(id, "nadescriptionme", locale); // typo in the game's texts
        return text.Length > 0 ? text : id;
    }

    public static string Description(string id, string locale = "en-US") => ItemText(id, "description", locale);

    private static string ItemText(string id, string field, string locale)
    {
        string text = ConfigBinary.Get("Texts/" + locale + "/item")[id][field].AsString("");
        if (text.Length == 0 && locale != "en-US") text = ConfigBinary.Get("Texts/en-US/item")[id][field].AsString("");
        return text;
    }

    public static string StatName(string stat)
    {
        string text = ConfigBinary.Get("Texts/en-US/stats")[stat].AsString("");
        return text.Length > 0 ? text : stat;
    }
}
