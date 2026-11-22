// The shelves of a store: which products each section shows to the player.
//
// The original server filled the shelves with StoreAlgorithms.UpdateStores (the same file is in the client): every section
// picks the products its product_selection matches, up to max_product_count, sorted by sort_order. The sales (fillers)
// were never in any build's config, so that shelf (general_sales) starts empty here and is up to the host.
// Differences from the original, on purpose: the pick is not random (the first products by sort_order, so the window
// does not change at every reopen) and there are no rotating specials or timers (filler_end_timestamp is always 0).

// One product on a shelf (StoreShelveItem).
public sealed class ShelfItem
{
    public StoreProductDef Product = null!;

    // Percent off shown and charged (stores.json: priceMode, discountPercent and the product's own "discount").
    public int Discount;

    // Server stock left (HasLimitedAmount = the host limited it).
    public bool HasLimitedAmount;
    public int Amount;
}

// One section of a store with its products (StoreShelve).
public sealed class Shelf
{
    public string StoreId = "";
    public string SectionId = "";
    public List<ShelfItem> Items = new List<ShelfItem>();
}

public static class StoreShelves
{
    // The shelves the player sees in a store. Sections without products are left out.
    // Call inside the account scope: the limits depend on what the account already bought.
    public static List<Shelf> Build(string storeId)
    {
        var shelves = new List<Shelf>();
        StoreDef? store = StoreCatalog.Store(storeId);
        if (store == null || !StoreCatalog.Loaded) return shelves;

        StoreSettings settings = StoreSettingsFile.Current;
        StoreShopRules? shop = settings.ShopRules(storeId);
        if (shop != null && !shop.Enabled) return shelves;

        foreach (StoreSectionDef section in store.Sections)
        {
            StoreSectionRules? rules = null;
            shop?.Sections.TryGetValue(section.Id, out rules);
            if (rules != null && !rules.Enabled) continue;

            var chosen = new List<StoreProductDef>();
            foreach (StoreProductDef product in StoreCatalog.Products)
            {
                if (!StoreRules.Sellable(product, settings)) continue;
                bool match = section.ProductSelection != null && section.ProductSelection.Matches(product.Id, product.Tags);
                if (rules != null && ListMatches(rules.Include, product)) match = true;
                if (rules != null && ListMatches(rules.Exclude, product)) match = false;
                if (!match) continue;
                if (StoreRules.ShelfBlocker(product, settings) != StoreRefusal.None) continue;
                chosen.Add(product);
            }
            if (chosen.Count == 0) continue;

            var shelf = new Shelf { StoreId = store.Id, SectionId = section.Id };
            foreach (StoreProductDef product in chosen.OrderBy(p => p.SortOrder).Take(section.Limit))
            {
                int? remaining = StoreStock.Remaining(product.Id);
                shelf.Items.Add(new ShelfItem
                {
                    Product = product,
                    Discount = settings.DiscountFor(product.Id),
                    HasLimitedAmount = remaining.HasValue,
                    Amount = remaining ?? 0
                });
            }
            shelves.Add(shelf);
        }
        return shelves;
    }

    // The product on the shelf of a section, or null when it is not there for this player right now.
    public static ShelfItem? Find(string storeId, string sectionId, string productId)
    {
        foreach (Shelf shelf in Build(storeId))
        {
            if (shelf.SectionId != sectionId) continue;
            return shelf.Items.FirstOrDefault(i => i.Product.Id == productId);
        }
        return null;
    }

    // An entry of "include" / "exclude": a product id or "tag:<tag>".
    private static bool ListMatches(List<string> entries, StoreProductDef product)
    {
        foreach (string entry in entries)
        {
            if (entry.StartsWith("tag:", StringComparison.OrdinalIgnoreCase))
            {
                if (product.Tags.Contains(entry.Substring(4).Trim())) return true;
            }
            else if (entry.Trim() == product.Id)
            {
                return true;
            }
        }
        return false;
    }
}
