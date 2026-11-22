// Rules that decide whether a product can be bought and how much it costs. Shared by the shelves (what is shown) and by the
// purchase (what is accepted), so the window and the server never disagree.
//
// The game's own limits mirror StoreManager.IsProductBuyable of the client. The host's limits come from stores.json.

// Why a purchase is refused. Each reason maps to a text key of the game's store texts (see FaultKey).
public enum StoreRefusal
{
    None = 0,
    Unknown,            // not on the shelf, no such product, purchases off, store off...
    LimitExceeded,      // max_per_account of the game or of the host, or the top of the hangar / loadouts
    NoInventorySpace,   // not enough room in the cargo and the storage for the items of the product
    NoCurrency,         // the account cannot pay
    SoldOut             // the server stock of the product ran out
}

public static class StoreRules
{
    // Most planes and skill loadouts an account can have, base plus bought (the clamps the server always had).
    public const int MaxHangarSlots = 20;
    public const int MaxSkillLoadouts = 10;

    // Text key the client shows in the chat for a refused purchase (Texts/<locale>/store: purchase_error_*).
    // "purchase_error_no_currency" also makes the client open its "Buy currency" page, so it is only used when the account
    // really lacks the currency.
    public static string FaultKey(StoreRefusal refusal)
    {
        switch (refusal)
        {
            case StoreRefusal.LimitExceeded: return "purchase_error_limit_exceeded";
            case StoreRefusal.NoInventorySpace: return "purchase_error_no_inventory_space";
            case StoreRefusal.NoCurrency: return "purchase_error_no_currency";
            default: return "purchase_error_unknown";
        }
    }

    // Product whose items go to the inventory: the item definitions must exist in this game version.
    public static bool ItemsExist(StoreProductDef product)
    {
        if (!ItemCatalog.Loaded) return true;
        return product.Items.All(id => ItemCatalog.Get(id) != null);
    }

    // On sale at all: not disabled by the game or the host, not pre-release (unless the host wants it), has a price.
    public static bool Sellable(StoreProductDef product, StoreSettings settings)
    {
        if (product.Disabled) return false;
        if (product.PreRelease && !settings.ShowPreRelease) return false;
        if (product.PriceTags.Count == 0) return false;
        StoreProductRules? rules = settings.ProductRules(product.Id);
        if (rules != null && !rules.Enabled) return false;
        return ItemsExist(product);
    }

    // Reasons that take the product off the shelf for this account: server stock, the host's limit per account and the top
    // of the hangar and of the skill loadouts. (The game's own max_per_account only greys the product out in the window.)
    public static StoreRefusal ShelfBlocker(StoreProductDef product, StoreSettings settings)
    {
        int? remaining = StoreStock.Remaining(product.Id);
        if (remaining.HasValue && remaining.Value <= 0) return StoreRefusal.SoldOut;

        int hostMax = settings.ProductRules(product.Id)?.MaxPerAccount ?? 0;
        if (hostMax > 0 && StoreAccounts.Bought(product.Id) >= hostMax) return StoreRefusal.LimitExceeded;

        if (product.AccountItems.Contains(StoreAccounts.HangarSlot) && PlayerData.HangarSlots >= MaxHangarSlots) return StoreRefusal.LimitExceeded;
        if (product.AccountItems.Contains(StoreAccounts.SkillLoadoutSlot) && PilotProgression.SkillLoadoutSlots() >= MaxSkillLoadouts) return StoreRefusal.LimitExceeded;
        return StoreRefusal.None;
    }

    // StoreManager.IsProductBuyable: the limits of the game's config (max_per_account and what it counts).
    public static StoreRefusal GameLimit(StoreProductDef product)
    {
        if (product.MaxPerAccount <= 0) return StoreRefusal.None;
        int max = product.MaxPerAccount;
        Dictionary<string, int> owned = StoreAccounts.AccountItems();

        if (product.MaxCountAccountItems)
        {
            foreach (string item in product.AccountItems)
                if ((owned.TryGetValue(item, out int n) ? n : 0) >= max) return StoreRefusal.LimitExceeded;
        }
        if (product.MaxPerAccountLimitingItem.Length > 0 &&
            (owned.TryGetValue(product.MaxPerAccountLimitingItem, out int limiting) ? limiting : 0) >= max)
            return StoreRefusal.LimitExceeded;
        if (product.MaxCountUnlocks)
        {
            List<string> unlocks = PilotProgression.CurrentUnlocks();
            foreach (string unlock in product.Unlocks)
                if (unlocks.Count(u => u == unlock) >= max) return StoreRefusal.LimitExceeded;
        }
        if (product.MaxCountItems)
        {
            foreach (string itemId in product.Items)
                if (CountItems(itemId) >= max) return StoreRefusal.LimitExceeded;
        }
        return StoreRefusal.None;
    }

    // Persist.CountItemDefId: copies of the item in the planes and in the storage.
    public static int CountItems(string defId)
    {
        return InventoryStore.Read(d =>
        {
            int count = d.Storage.AllItems().Count(i => i.DefId == defId);
            foreach (StoredInventory plane in d.Planes.Values) count += plane.AllItems().Count(i => i.DefId == defId);
            count += d.SharedCargo.Count(i => i != null && i.DefId == defId);
            return count;
        });
    }

    // Amount of the currency charged for one price of the product, as the client computes it: the escalating part counts
    // the account items named by amount_sub_item_tag.
    public static int PriceAmount(StorePriceTag tag, int discount)
    {
        int owned = tag.Price.SubItemTag.Length > 0 ? StoreAccounts.Count(tag.Price.SubItemTag) : 0;
        return tag.Price.AmountWith(owned, discount);
    }
}
