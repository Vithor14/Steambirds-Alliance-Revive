using System.Text.Json;
using System.Text.Json.Serialization;

// Host settings of the stores: stores.json in the server folder (Server).
// Created with the default values the first time and read again when the file changes (like items.json).
// The launcher builds its "Stores" page from this file: every top-level key is a field and the comment above it is
// the explanation, so keep one comment line block above each key.

// What the host wants for one product (key = product id of the game's store config).
public sealed class StoreProductRules
{
    // false: the product is not sold anywhere.
    public bool Enabled { get; set; } = true;

    // Percent taken off the price (0-100). Missing = the global "discountPercent". The game shows it as "x% off".
    public int? Discount { get; set; }

    // Units the whole server can still sell (0 = no limit). The remaining amount is kept in data\store_stock.json.
    public int Stock { get; set; }

    // Times one account can buy this product (0 = the game's own limit, if it has one).
    public int MaxPerAccount { get; set; }

    // Item definitions the product gives instead of the game's own (null = the game's). The window still shows the name, icon and
    // price of the original product: this is how a product the game never had (a flock founder token) is sold.
    public List<string>? Items { get; set; }
}

// What the host wants for one section of a store (key = section id, for example "general_account").
public sealed class StoreSectionRules
{
    // false: the section shows nothing.
    public bool Enabled { get; set; } = true;

    // Products added to what the game's rule already puts in the section: a product id or "tag:<tag>".
    public List<string> Include { get; set; } = new List<string>();

    // Products taken out of the section: a product id or "tag:<tag>".
    public List<string> Exclude { get; set; } = new List<string>();
}

// What the host wants for one store (key = store id: store_general, store_hangar, store_storage, store_skill_loadouts).
public sealed class StoreShopRules
{
    // false: the store opens empty and nothing can be bought in it.
    public bool Enabled { get; set; } = true;

    public Dictionary<string, StoreSectionRules> Sections { get; set; } = new Dictionary<string, StoreSectionRules>(StringComparer.Ordinal);
}

public sealed class StoreSettings
{
    // Master switch: false = the stores open but every purchase is refused.
    public bool PurchasesEnabled { get; set; } = true;

    // "currency" = the account pays with its own gold / iron (original rule, without the Steam packages).
    // "free" = every price is 0 (the game shows 100% off).
    public string PriceMode { get; set; } = "currency";

    // Percent off every price (0-100), on top of nothing else: the same number the game shows as "x% off".
    public int DiscountPercent { get; set; }

    // Products the game marks as pre-release (some trails and emotes) are shown when this is on.
    public bool ShowPreRelease { get; set; }

    public Dictionary<string, StoreShopRules> Stores { get; set; } = new Dictionary<string, StoreShopRules>(StringComparer.Ordinal);
    public Dictionary<string, StoreProductRules> Products { get; set; } = new Dictionary<string, StoreProductRules>(StringComparer.Ordinal);

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownKeys { get; set; }

    public bool Free => string.Equals(PriceMode, "free", StringComparison.OrdinalIgnoreCase);

    public StoreShopRules? ShopRules(string storeId) => Stores.TryGetValue(storeId, out StoreShopRules? s) ? s : null;
    public StoreProductRules? ProductRules(string productId) => Products.TryGetValue(productId, out StoreProductRules? p) ? p : null;

    // Discount percent charged and shown for a product: free mode = 100, else the product's own, else the global one.
    public int DiscountFor(string productId)
    {
        if (Free) return 100;
        int percent = ProductRules(productId)?.Discount ?? DiscountPercent;
        return Math.Clamp(percent, 0, 100);
    }
}

public static class StoreSettingsFile
{
    public const string FileName = "stores.json";

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static StoreSettings current = new StoreSettings();
    private static DateTime readAt = DateTime.MinValue;
    private static DateTime nextCheck = DateTime.MinValue;
    private static Action<string, string> log = (_, _) => { };
    private static readonly object gate = new object();

    public static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), FileName);

    public static StoreSettings Current
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
            StoreSettingsFile.log = log;
            if (!File.Exists(FilePath))
            {
                try
                {
                    File.WriteAllText(FilePath, DefaultFileText);
                    log("STORE", FilePath + " created with the default values (edit and save: the server reloads it by itself)");
                }
                catch (Exception e)
                {
                    log("STORE", "could not create " + FilePath + ": " + e.Message);
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

            StoreSettings? read = JsonSerializer.Deserialize<StoreSettings>(File.ReadAllText(FilePath), ReadOptions);
            if (read == null) return;
            read.PriceMode = (read.PriceMode ?? "currency").Trim().ToLowerInvariant();
            read.DiscountPercent = Math.Clamp(read.DiscountPercent, 0, 100);
            read.Stores ??= new Dictionary<string, StoreShopRules>(StringComparer.Ordinal);
            read.Products ??= new Dictionary<string, StoreProductRules>(StringComparer.Ordinal);
            foreach (StoreShopRules shop in read.Stores.Values)
            {
                shop.Sections ??= new Dictionary<string, StoreSectionRules>(StringComparer.Ordinal);
                foreach (StoreSectionRules section in shop.Sections.Values)
                {
                    section.Include ??= new List<string>();
                    section.Exclude ??= new List<string>();
                }
            }
            foreach (StoreProductRules product in read.Products.Values)
            {
                product.Stock = Math.Max(0, product.Stock);
                product.MaxPerAccount = Math.Max(0, product.MaxPerAccount);
                if (product.Discount.HasValue) product.Discount = Math.Clamp(product.Discount.Value, 0, 100);
            }
            current = read;
            log("STORE", FilePath + " read: purchases " + (read.PurchasesEnabled ? "on" : "OFF") + ", prices " +
                (read.Free ? "free" : "in gold/iron" + (read.DiscountPercent > 0 ? " (-" + read.DiscountPercent + "%)" : "")) +
                ", " + read.Stores.Count + " store rule(s), " + read.Products.Count + " product rule(s)");
            if (read.UnknownKeys != null && read.UnknownKeys.Count > 0)
                log("STORE", "key(s) this version does not use in " + FileName + ": " + string.Join(", ", read.UnknownKeys.Keys));
            foreach (string id in read.Products.Keys)
            {
                if (StoreCatalog.Loaded && StoreCatalog.Product(id) == null)
                    log("STORE", FileName + ": product '" + id + "' does not exist in the game's store config (ignored)");
            }
            foreach (string id in read.Stores.Keys)
            {
                if (StoreCatalog.Loaded && StoreCatalog.Store(id) == null)
                    log("STORE", FileName + ": store '" + id + "' does not exist in the game's store config (ignored)");
            }
        }
        catch (Exception e)
        {
            log("STORE", "error in " + FilePath + " (" + e.Message + "): keeping the previous configuration");
        }
    }

    private const string DefaultFileText =
@"// Stores of the server (the General Store, the Hangar, the Storage and the Pilot School loadouts). Save the file and the
// server reloads it by itself. What each store sells comes from the game's own store config; the keys below only
// change it. Gold used to be bought with real money on Steam: those packages do not exist here. Give gold with
// /currency gold 5000 in the chat, ""startingCurrencies"" in items.json or the account editor of the launcher.
{
  // false = the stores open, but every purchase is refused.
  ""purchasesEnabled"": true,

  // How the price is charged. ""currency"" = the account pays with its own gold and iron (like the original, without the
  // Steam packages). ""free"" = every product costs 0 and the game shows ""100% off"".
  ""priceMode"": ""currency"",

  // Percent taken off every price, from 0 to 100 (the game shows it as ""x% off""). A product can have its own value in
  // ""products"" below.
  ""discountPercent"": 0,

  // The game marks a few trails and emotes as pre-release. true = they are sold too.
  ""showPreRelease"": false,

  // Rules per store. Keys: store_general, store_hangar, store_storage, store_skill_loadouts. Each store has ""enabled""
  // and ""sections"" (the section id, for example general_account, general_keys, general_sales) with ""enabled"",
  // ""include"" and ""exclude"": lists of product ids or ""tag:<tag>"". ""include"" adds products to what the game's rule
  // already puts in the section. general_sales is the sale shelf: it is empty until you add products to its ""include""
  // (combine with a ""discount"" in ""products"").
  // Example: { ""store_general"": { ""sections"": { ""general_keys"": { ""enabled"": false },
  //   ""general_sales"": { ""include"": [ ""store_key_eye"" ] } } } }
  ""stores"": {},

  // Rules per product (id as in the game's store config, for example hangar_slot, store_skill_loadout_slot,
  // store_key_eye). ""enabled"": false hides it everywhere. ""discount"": percent off (0-100). ""stock"": units the whole
  // server can sell (0 = no limit). ""maxPerAccount"": times each account can buy it (0 = the game's own limit).
  // Example: { ""hangar_slot"": { ""discount"": 50, ""maxPerAccount"": 5 }, ""store_bundle_starter"": { ""stock"": 20 } }
  ""products"": {}
}
";
}
