namespace SteambirdsLauncher.Core.GameData;

// The stores of the game, from the "store" file of the configBinary (same data the server reads in Server\Stores\StoreCatalog.cs):
// the products with their prices and the sections of each store. The Store page of the launcher lists them to set prices,
// stock and limits in stores.json.
public sealed partial class GameConfig
{
    // One way to pay for a product. Escalating prices (hangar slots...) take Subs[owned] off the amount for each unit owned.
    public sealed class StorePrice
    {
        public string Id { get; init; } = "";
        public string Currency { get; init; } = "";
        public int Amount { get; init; }
        public int[] Subs { get; init; } = Array.Empty<int>();

        public bool Escalating => Subs.Length > 0;

        // Price of the first unit after a discount percent (same integer math as the game and the server).
        public int FirstUnit(int discount)
        {
            int rest = Amount - (Subs.Length > 0 ? Subs[0] : 0);
            int off = discount > 0 ? rest * discount / 100 : 0;
            return rest - off;
        }
    }

    public sealed class StoreProduct
    {
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public List<string> Tags { get; } = new List<string>();
        public List<StorePrice> Prices { get; } = new List<StorePrice>();
        public bool PreRelease { get; init; }
        public bool Disabled { get; init; }
        public int MaxPerAccount { get; init; }

        // "store/section" ids that show the product by the game's own rules, and the section the list groups it under.
        public List<string> Shelves { get; } = new List<string>();
        public string Category { get; set; } = "";

        public StorePrice? Price(string currency) => Prices.FirstOrDefault(p => p.Currency == currency);
        public override string ToString() => Name.Length > 0 && Name != Id ? Name + " (" + Id + ")" : Id;
    }

    public sealed class StoreSection
    {
        public string StoreId { get; init; } = "";
        public string Id { get; init; } = "";
        public string Name { get; init; } = "";
        public string Type { get; init; } = "";
        public bool Hidden { get; init; }
        public int Products { get; set; }
    }

    public List<StoreProduct> StoreProducts { get; } = new List<StoreProduct>();
    public List<StoreSection> StoreSections { get; } = new List<StoreSection>();
    public List<string> StoreIds { get; } = new List<string>();

    public bool HasStore => StoreProducts.Count > 0 && StoreSections.Count > 0;

    public StoreProduct? FindStoreProduct(string id) => StoreProducts.FirstOrDefault(p => p.Id == id);

    private void ReadStore(Dictionary<string, ConfigNode> files)
    {
        ConfigNode Get(string name) => files.TryGetValue(name, out ConfigNode? node) ? node : ConfigNode.Missing;
        ConfigNode store = Get("store");
        if (store.IsMissing) return;
        ConfigNode storeTexts = Get("Texts/en-US/store");
        ConfigNode itemTexts = Get("Texts/en-US/item");

        foreach (ConfigNode p in store["products"].Items)
        {
            string id = p["id"].AsString("");
            if (id.Length == 0) continue;
            List<string> items = p["items"].AsStringList();
            string name = storeTexts["product_name_" + id].AsString("");
            if (name.Length == 0 && items.Count > 0) name = itemTexts[items[0]]["name"].AsString("");
            var product = new StoreProduct
            {
                Id = id,
                Name = name.Length > 0 ? name : id,
                PreRelease = p["preRelease"].AsBool(false),
                Disabled = p["disabled"].AsBool(false),
                MaxPerAccount = p["max_per_account"].AsInt(0)
            };
            product.Tags.AddRange(p["tags"].AsStringList());
            // StoreManager.ExtendProductWithDefaults: tags from the first item (the ones the sections look at).
            if (items.Count > 0 && !p["no_automatic_tags"].AsBool(false))
            {
                string first = items[0];
                void Ensure(string tag)
                {
                    if (!product.Tags.Contains(tag)) product.Tags.Add(tag);
                }
                Ensure("item");
                if (first.StartsWith("key_", StringComparison.Ordinal)) Ensure("item_key");
                if (first.StartsWith("dye_", StringComparison.Ordinal)) Ensure("item_dye");
                if (first.StartsWith("consumable_", StringComparison.Ordinal)) Ensure("item_consumable");
                if (first.StartsWith("unlock_", StringComparison.Ordinal)) Ensure("item_unlock");
            }

            if (p.Has("normal_price")) product.Prices.Add(ReadPrice("normal", p["normal_price"]));
            foreach (ConfigNode tag in p["additional_price_tags"].Items)
                product.Prices.Add(ReadPrice(tag["id"].AsString("normal"), tag["price"]));
            StoreProducts.Add(product);
        }

        foreach (ConfigNode s in store["stores"].Items)
        {
            string storeId = s["id"].AsString("");
            if (storeId.Length == 0) continue;
            StoreIds.Add(storeId);
            foreach (ConfigNode sec in s["sections"].Items)
            {
                string sectionId = sec["id"].AsString("");
                if (sectionId.Length == 0) continue;
                var section = new StoreSection
                {
                    StoreId = storeId,
                    Id = sectionId,
                    Name = storeTexts["section_name_" + sectionId].AsString(Pretty(sectionId)),
                    Type = sec["type"].AsString("NORMAL"),
                    Hidden = sec["is_hidden"].AsBool(false)
                };
                StoreSections.Add(section);

                ConfigNode selection = sec["product_selection"];
                if (selection.IsMissing) continue;
                string withId = selection["with_id"].AsString("");
                List<string> tags = selection["with_tags_any"].AsStringList();
                tags.AddRange(selection["with_tags_all"].AsStringList());
                foreach (StoreProduct product in StoreProducts)
                {
                    bool match = (withId.Length > 0 && product.Id == withId) || product.Tags.Any(tags.Contains);
                    if (!match) continue;
                    product.Shelves.Add(storeId + "/" + sectionId);
                    section.Products++;
                    // The list groups a product under its first normal section (banners and the sale shelf come second).
                    if (product.Category.Length == 0 && section.Type == "NORMAL") product.Category = section.Name;
                }
            }
        }
        foreach (StoreProduct product in StoreProducts)
        {
            if (product.Category.Length == 0 && product.Shelves.Count > 0)
                product.Category = StoreSections.First(sec => product.Shelves[0] == sec.StoreId + "/" + sec.Id).Name;
        }
    }

    // "general_dye_rare" -> "Dye rare" (the game has no text for some sections).
    private static string Pretty(string id)
    {
        string text = (id.StartsWith("general_", StringComparison.Ordinal) ? id.Substring("general_".Length) : id).Replace('_', ' ');
        return text.Length == 0 ? id : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    private static StorePrice ReadPrice(string id, ConfigNode node)
    {
        return new StorePrice
        {
            Id = id,
            Currency = node["currency"].AsString(""),
            Amount = node["amount"].AsInt(0),
            Subs = node["amount_subs"].Items.Select(n => n.AsInt(0)).ToArray()
        };
    }
}
