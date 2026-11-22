using System.Globalization;

// The game's own store configuration: the "store" file of the configBinary (SBConfig.storeConfig).
// It holds everything the CLIENT needs to draw a store: the currencies, the 294 products with their prices, the stores
// and the sections of each store. What only lived on the original server (the shelves: which products each section shows
// at a given moment, the sales and the purchase itself) is rebuilt in StoreShelves.cs and StorePurchase.cs.
//
// This file mirrors the client classes StoreConfig, StoreProduct, StorePrice, StoreSection, StoreSelection and the
// part of StoreManager.ExtendConfig that adds the automatic tags and the sort order. Names are kept close to the
// client ones so the two can be compared.

// One currency of the store window (StoreCurrency): gold, iron, newscrap and the hidden keys.
public sealed class StoreCurrencyDef
{
    public string Id = "";
    public bool Hidden;
}

// StorePrice. The final amount is (amount - amountSubs[owned]) minus the discount, same integer math as the client
// (StorePrice.GetAmountWithSubAndDiscount), so the price in the window and the price charged are always the same.
public sealed class StorePrice
{
    public string Currency = "";
    public int Amount;

    // Escalating price: the n-th unit takes amountSubs[n] off the price (hangar slots: 3750, minus 3000, 2000, 1000).
    public int[] AmountSubs = Array.Empty<int>();

    // Account item counted to know how many units the account already owns (hangar_slot, skill_loadout_slot...).
    public string SubItemTag = "";

    public int AmountWith(int ownedCount, int discount)
    {
        int sub = 0;
        if (AmountSubs.Length > 0 && ownedCount < AmountSubs.Length) sub = AmountSubs[Math.Clamp(ownedCount, 0, AmountSubs.Length - 1)];
        int rest = Amount - sub;
        int off = discount > 0 ? rest * discount / 100 : 0;
        return rest - off;
    }
}

// One way to pay for a product (StorePriceTag): id "normal" is the first one, the others come from additional_price_tags.
public sealed class StorePriceTag
{
    public string Id = "";
    public StorePrice Price = new StorePrice();
}

// StoreProduct.
public sealed class StoreProductDef
{
    public string Id = "";
    public List<string> Tags = new List<string>();
    public List<StorePriceTag> PriceTags = new List<StorePriceTag>();

    // Inventory items (ItemConfig ids) the product puts in the plane cargo or the storage.
    public List<string> Items = new List<string>();

    // Unlock items added to the pilot (emotes, plane classes).
    public List<string> Unlocks = new List<string>();

    // Counters added to the account (hangar_slot, storage_slot, inventory_slot, skill_loadout_slot and the markers).
    public List<string> AccountItems = new List<string>();

    // What the player can choose to do with the item after buying: nothing, use_instantly, use_and_inventory...
    public List<string> PostPurchaseActions = new List<string>();

    public bool Disabled;
    public bool PreRelease;
    public bool NoAutomaticTags;

    // Limit per account written in the game's config (0 = none) and what it counts.
    public int MaxPerAccount;
    public string MaxPerAccountLimitingItem = "";
    public bool MaxCountAccountItems;
    public bool MaxCountUnlocks;
    public bool MaxCountItems;

    // Position in the config file (1-based): the shelf shows products in this order.
    public int SortOrder;

    // Readable name for the chat commands and the log.
    public string Name = "";
}

// StoreSection.Type.
public enum StoreSectionType
{
    Normal = 0,
    Banner = 1,
    Special = 2
}

// StoreSelection: which products a section shows. Same rule as the client's StoreSelection.Matches.
public sealed class StoreSelection
{
    public string WithId = "";
    public List<string> WithTagsAny = new List<string>();
    public List<string> WithTagsAll = new List<string>();

    public bool Matches(string id, List<string> tags)
    {
        if (WithId.Length > 0 && id == WithId) return true;
        if (tags.Count == 0) return false;
        // The client treats "with_tags_all" as "any of these tags" too (it returns on the first match); kept as is.
        foreach (string tag in WithTagsAll) if (tags.Contains(tag)) return true;
        foreach (string tag in WithTagsAny) if (tags.Contains(tag)) return true;
        return false;
    }
}

// StoreSection.
public sealed class StoreSectionDef
{
    public string Id = "";
    public bool Hidden;
    public StoreSectionType Type;
    public StoreSelection? ProductSelection;
    public bool HasFillerSelection;
    public int MaxProductCount;
    public int MinPilotLevel = -1;
    public string ShowWithinOtherSection = "";

    // StoreSection.MaxProductCount: 0 means no limit (999).
    public int Limit => MaxProductCount > 0 ? MaxProductCount : 999;
}

// StoreStore: store_general, store_hangar, store_storage and store_skill_loadouts.
public sealed class StoreDef
{
    public string Id = "";
    public List<StoreSectionDef> Sections = new List<StoreSectionDef>();
}

public static class StoreCatalog
{
    private static readonly Dictionary<string, StoreProductDef> products = new Dictionary<string, StoreProductDef>(StringComparer.Ordinal);
    private static readonly List<StoreProductDef> productList = new List<StoreProductDef>();
    private static readonly Dictionary<string, StoreDef> stores = new Dictionary<string, StoreDef>(StringComparer.Ordinal);
    private static readonly Dictionary<string, StoreCurrencyDef> currencies = new Dictionary<string, StoreCurrencyDef>(StringComparer.Ordinal);

    public static bool Loaded { get; private set; }
    public static IReadOnlyList<StoreProductDef> Products => productList;
    public static IReadOnlyCollection<StoreDef> Stores => stores.Values;
    public static IReadOnlyCollection<StoreCurrencyDef> Currencies => currencies.Values;

    public static StoreProductDef? Product(string? id) => id != null && products.TryGetValue(id, out StoreProductDef? p) ? p : null;
    public static StoreDef? Store(string? id) => id != null && stores.TryGetValue(id, out StoreDef? s) ? s : null;

    public static StoreSectionDef? Section(string? storeId, string? sectionId)
    {
        return Store(storeId)?.Sections.FirstOrDefault(s => s.Id == sectionId);
    }

    public static bool IsCurrency(string? id) => id != null && currencies.ContainsKey(id);

    // Reads the "store" file of the configBinary (ItemCatalog.Load must have run: the automatic tags look at the items).
    public static bool Load(Action<string, string> log)
    {
        products.Clear();
        productList.Clear();
        stores.Clear();
        currencies.Clear();
        Loaded = false;

        ConfigNode root = ConfigBinary.Get("store");
        if (root.IsMissing)
        {
            log("STORE", "the configBinary has no 'store' config (not found or not read): the stores stay empty");
            return false;
        }

        foreach (ConfigNode c in root["currencies"].Items)
        {
            string id = c["id"].AsString("");
            if (id.Length == 0) continue;
            currencies[id] = new StoreCurrencyDef { Id = id, Hidden = c["hidden"].AsBool(false) };
        }

        int order = 0;
        foreach (ConfigNode p in root["products"].Items)
        {
            string id = p["id"].AsString("");
            if (id.Length == 0 || products.ContainsKey(id)) continue;
            order++;
            var product = new StoreProductDef
            {
                Id = id,
                Tags = p["tags"].AsStringList(),
                Items = p["items"].AsStringList(),
                Unlocks = p["unlocks"].AsStringList(),
                AccountItems = p["account_items"].AsStringList(),
                PostPurchaseActions = p["postPurchaseActions"].AsStringList(),
                Disabled = p["disabled"].AsBool(false),
                PreRelease = p["preRelease"].AsBool(false),
                NoAutomaticTags = p["no_automatic_tags"].AsBool(false),
                MaxPerAccount = p["max_per_account"].AsInt(0),
                MaxPerAccountLimitingItem = p["max_per_account_limiting_account_item"].AsString(""),
                MaxCountAccountItems = p["max_per_account_count_account_items"].AsBool(false),
                MaxCountUnlocks = p["max_per_account_count_unlocks"].AsBool(false),
                MaxCountItems = p["max_per_account_count_items"].AsBool(false),
                SortOrder = p["sort_order"].AsInt(0)
            };
            if (product.SortOrder == 0) product.SortOrder = order;

            // StoreProduct.EnumPriceTags: "normal_price" first (id "normal"), then additional_price_tags.
            if (p.Has("normal_price")) product.PriceTags.Add(new StorePriceTag { Id = "normal", Price = ReadPrice(p["normal_price"]) });
            foreach (ConfigNode tag in p["additional_price_tags"].Items)
                product.PriceTags.Add(new StorePriceTag { Id = tag["id"].AsString(""), Price = ReadPrice(tag["price"]) });

            AddAutomaticTags(product);
            product.Name = ProductName(product);
            products[id] = product;
            productList.Add(product);
        }

        foreach (ConfigNode s in root["stores"].Items)
        {
            string id = s["id"].AsString("");
            if (id.Length == 0) continue;
            var store = new StoreDef { Id = id };
            foreach (ConfigNode sec in s["sections"].Items)
            {
                string sectionId = sec["id"].AsString("");
                if (sectionId.Length == 0) continue;
                var section = new StoreSectionDef
                {
                    Id = sectionId,
                    Hidden = sec["is_hidden"].AsBool(false),
                    MaxProductCount = sec["max_product_count"].AsInt(0),
                    MinPilotLevel = sec["min_pilot_level"].AsInt(-1),
                    ShowWithinOtherSection = sec["show_within_other_section_in_ui"].AsString(""),
                    HasFillerSelection = sec.Has("filler_selection"),
                    Type = ParseType(sec["type"].AsString(""))
                };
                if (sec.Has("product_selection"))
                {
                    ConfigNode sel = sec["product_selection"];
                    section.ProductSelection = new StoreSelection
                    {
                        WithId = sel["with_id"].AsString(""),
                        WithTagsAny = sel["with_tags_any"].AsStringList(),
                        WithTagsAll = sel["with_tags_all"].AsStringList()
                    };
                }
                store.Sections.Add(section);
            }
            stores[id] = store;
        }

        Loaded = products.Count > 0 && stores.Count > 0;
        log("STORE", "store config of the game: " + products.Count + " product(s), " + stores.Count + " store(s) (" +
            string.Join(", ", stores.Values.Select(s => s.Id + " " + s.Sections.Count + " section(s)")) + "), currencies " +
            string.Join(", ", currencies.Values.Select(c => c.Id + (c.Hidden ? " (hidden)" : ""))));
        return Loaded;
    }

    private static StoreSectionType ParseType(string text)
    {
        if (text.Equals("BANNER", StringComparison.OrdinalIgnoreCase)) return StoreSectionType.Banner;
        if (text.Equals("SPECIAL", StringComparison.OrdinalIgnoreCase)) return StoreSectionType.Special;
        return StoreSectionType.Normal;
    }

    private static StorePrice ReadPrice(ConfigNode node)
    {
        var price = new StorePrice
        {
            Currency = node["currency"].AsString(""),
            Amount = node["amount"].AsInt(0),
            SubItemTag = node["amount_sub_item_tag"].AsString("")
        };
        var subs = new List<int>();
        foreach (ConfigNode sub in node["amount_subs"].Items) subs.Add(sub.AsInt(0));
        price.AmountSubs = subs.ToArray();
        return price;
    }

    // StoreManager.ExtendProductWithDefaults: tags added from the first item of the product.
    private static void AddAutomaticTags(StoreProductDef product)
    {
        if (product.NoAutomaticTags || product.Items.Count == 0 || !ItemCatalog.Loaded) return;
        string first = product.Items[0];
        ItemDef? def = ItemCatalog.Get(first);
        if (def == null) return;

        void Ensure(string tag)
        {
            if (!product.Tags.Contains(tag)) product.Tags.Add(tag);
        }
        Ensure("item");
        if (first.StartsWith("key_", StringComparison.Ordinal)) Ensure("item_key");
        if (first.StartsWith("dye_", StringComparison.Ordinal)) Ensure("item_dye");
        if (first.StartsWith("consumable_", StringComparison.Ordinal)) Ensure("item_consumable");
        if (first.StartsWith("unlock_", StringComparison.Ordinal)) Ensure("item_unlock");
        Ensure("item_tier_" + def.Tier.ToString(CultureInfo.InvariantCulture));
        Ensure("item_slot_" + def.Slot);
    }

    // English name: the store texts of the game (product_name_<id>), else the name of its first item, else the id.
    private static string ProductName(StoreProductDef product)
    {
        string name = ConfigBinary.Get("Texts/en-US/store")["product_name_" + product.Id].AsString("");
        if (name.Length == 0 && product.Items.Count > 0 && ItemCatalog.Loaded) name = ItemCatalog.Name(product.Items[0]);
        return string.IsNullOrWhiteSpace(name) ? product.Id : name;
    }
}
