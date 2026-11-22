using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SteambirdsLauncher.Core;

// What stores.json says (Server\Stores\StoreSettings.cs reads the same file). The Store page edits this model and writes the
// six top-level values back with JsoncFile, so the comments of the file stay as they are.

// Rule of one product (key = product id of the game's store config).
public sealed class StoreProductRule
{
    public bool Enabled { get; set; } = true;

    // Percent off (0-100). null = the global discount.
    public int? Discount { get; set; }

    // Units the whole server can sell (0 = no limit).
    public int Stock { get; set; }

    // Times each account can buy it (0 = the game's own limit).
    public int MaxPerAccount { get; set; }

    public bool IsDefault => Enabled && Discount == null && Stock == 0 && MaxPerAccount == 0;
}

// Rule of one section of a store (key = section id).
public sealed class StoreSectionRule
{
    public bool Enabled { get; set; } = true;
    public List<string> Include { get; set; } = new List<string>();
    public List<string> Exclude { get; set; } = new List<string>();

    public bool IsDefault => Enabled && Include.Count == 0 && Exclude.Count == 0;
}

// Rule of one store (key = store id).
public sealed class StoreShopRule
{
    public bool Enabled { get; set; } = true;
    public Dictionary<string, StoreSectionRule> Sections { get; set; } = new Dictionary<string, StoreSectionRule>(StringComparer.Ordinal);

    public bool IsDefault => Enabled && Sections.Values.All(s => s.IsDefault);
}

public sealed class StoreRules
{
    public const string FileName = "stores.json";

    public bool PurchasesEnabled { get; set; } = true;

    // "currency" (the account pays with its gold and iron) or "free" (everything costs 0).
    public string PriceMode { get; set; } = "currency";

    public int DiscountPercent { get; set; }
    public bool ShowPreRelease { get; set; }
    public Dictionary<string, StoreShopRule> Stores { get; } = new Dictionary<string, StoreShopRule>(StringComparer.Ordinal);
    public Dictionary<string, StoreProductRule> Products { get; } = new Dictionary<string, StoreProductRule>(StringComparer.Ordinal);

    public bool Free => string.Equals(PriceMode, "free", StringComparison.OrdinalIgnoreCase);

    public static string FilePath => LauncherPaths.ServerFile(FileName);

    // Discount percent charged for a product (same rule as the server: free = 100, else the product's own, else the global one).
    public int DiscountFor(string productId)
    {
        if (Free) return 100;
        int percent = Products.TryGetValue(productId, out StoreProductRule? rule) && rule.Discount.HasValue ? rule.Discount.Value : DiscountPercent;
        return Math.Clamp(percent, 0, 100);
    }

    public StoreProductRule ProductRule(string id)
    {
        if (!Products.TryGetValue(id, out StoreProductRule? rule))
        {
            rule = new StoreProductRule();
            Products[id] = rule;
        }
        return rule;
    }

    public StoreShopRule ShopRule(string id)
    {
        if (!Stores.TryGetValue(id, out StoreShopRule? rule))
        {
            rule = new StoreShopRule();
            Stores[id] = rule;
        }
        return rule;
    }

    public StoreSectionRule SectionRule(string storeId, string sectionId)
    {
        StoreShopRule shop = ShopRule(storeId);
        if (!shop.Sections.TryGetValue(sectionId, out StoreSectionRule? rule))
        {
            rule = new StoreSectionRule();
            shop.Sections[sectionId] = rule;
        }
        return rule;
    }

    // Drops the rules that say nothing (the file only keeps what differs from the default).
    public void Prune()
    {
        foreach (string id in Products.Where(p => p.Value.IsDefault).Select(p => p.Key).ToList()) Products.Remove(id);
        foreach (StoreShopRule shop in Stores.Values)
        {
            foreach (string id in shop.Sections.Where(s => s.Value.IsDefault).Select(s => s.Key).ToList()) shop.Sections.Remove(id);
        }
        foreach (string id in Stores.Where(s => s.Value.IsDefault).Select(s => s.Key).ToList()) Stores.Remove(id);
    }

    // ------------------------------------------------------------------ reading

    public static StoreRules Parse(string text)
    {
        var rules = new StoreRules();
        using JsonDocument document = JsonDocument.Parse(text, JsoncFile.ParseOptions);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return rules;

        foreach (JsonProperty property in root.EnumerateObject())
        {
            JsonElement value = property.Value;
            switch (property.Name.ToLowerInvariant())
            {
                case "purchasesenabled":
                    rules.PurchasesEnabled = ReadBool(value, true);
                    break;
                case "pricemode":
                    if (value.ValueKind == JsonValueKind.String) rules.PriceMode = (value.GetString() ?? "currency").Trim().ToLowerInvariant();
                    break;
                case "discountpercent":
                    rules.DiscountPercent = Math.Clamp(ReadInt(value, 0), 0, 100);
                    break;
                case "showprerelease":
                    rules.ShowPreRelease = ReadBool(value, false);
                    break;
                case "stores":
                    if (value.ValueKind == JsonValueKind.Object) ReadStores(rules, value);
                    break;
                case "products":
                    if (value.ValueKind == JsonValueKind.Object) ReadProducts(rules, value);
                    break;
            }
        }
        return rules;
    }

    private static void ReadProducts(StoreRules rules, JsonElement element)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            var rule = new StoreProductRule();
            foreach (JsonProperty field in property.Value.EnumerateObject())
            {
                switch (field.Name.ToLowerInvariant())
                {
                    case "enabled": rule.Enabled = ReadBool(field.Value, true); break;
                    case "discount": rule.Discount = field.Value.ValueKind == JsonValueKind.Null ? null : Math.Clamp(ReadInt(field.Value, 0), 0, 100); break;
                    case "stock": rule.Stock = Math.Max(0, ReadInt(field.Value, 0)); break;
                    case "maxperaccount": rule.MaxPerAccount = Math.Max(0, ReadInt(field.Value, 0)); break;
                }
            }
            rules.Products[property.Name] = rule;
        }
    }

    private static void ReadStores(StoreRules rules, JsonElement element)
    {
        foreach (JsonProperty shopProperty in element.EnumerateObject())
        {
            if (shopProperty.Value.ValueKind != JsonValueKind.Object) continue;
            var shop = new StoreShopRule();
            foreach (JsonProperty field in shopProperty.Value.EnumerateObject())
            {
                string name = field.Name.ToLowerInvariant();
                if (name == "enabled") shop.Enabled = ReadBool(field.Value, true);
                else if (name == "sections" && field.Value.ValueKind == JsonValueKind.Object) ReadSections(shop, field.Value);
            }
            rules.Stores[shopProperty.Name] = shop;
        }
    }

    private static void ReadSections(StoreShopRule shop, JsonElement element)
    {
        foreach (JsonProperty sectionProperty in element.EnumerateObject())
        {
            if (sectionProperty.Value.ValueKind != JsonValueKind.Object) continue;
            var section = new StoreSectionRule();
            foreach (JsonProperty field in sectionProperty.Value.EnumerateObject())
            {
                switch (field.Name.ToLowerInvariant())
                {
                    case "enabled": section.Enabled = ReadBool(field.Value, true); break;
                    case "include": section.Include = ReadStrings(field.Value); break;
                    case "exclude": section.Exclude = ReadStrings(field.Value); break;
                }
            }
            shop.Sections[sectionProperty.Name] = section;
        }
    }

    private static bool ReadBool(JsonElement value, bool fallback)
    {
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out bool parsed)) return parsed;
        return fallback;
    }

    private static int ReadInt(JsonElement value, int fallback)
    {
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetInt32(out int whole) ? whole : (int)Math.Round(value.GetDouble());
        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) return parsed;
        return fallback;
    }

    private static List<string> ReadStrings(JsonElement value)
    {
        var result = new List<string>();
        if (value.ValueKind != JsonValueKind.Array) return result;
        foreach (JsonElement item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) result.Add(item.GetString()!.Trim());
        }
        return result;
    }

    // ------------------------------------------------------------------ writing

    // The raw JSON text of each top-level value, to put in the file with JsoncFile.ApplyChanges.
    public Dictionary<string, string> ToRawValues(string newline)
    {
        Prune();
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["purchasesEnabled"] = JsoncFile.FormatBool(PurchasesEnabled),
            ["priceMode"] = JsoncFile.FormatString(Free ? "free" : "currency"),
            ["discountPercent"] = DiscountPercent.ToString(CultureInfo.InvariantCulture),
            ["showPreRelease"] = JsoncFile.FormatBool(ShowPreRelease),
            ["stores"] = StoresRaw(newline),
            ["products"] = ProductsRaw(newline)
        };
    }

    private string ProductsRaw(string newline)
    {
        if (Products.Count == 0) return "{}";
        var lines = new List<string>();
        foreach (KeyValuePair<string, StoreProductRule> pair in Products.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            StoreProductRule rule = pair.Value;
            var fields = new List<string>();
            if (!rule.Enabled) fields.Add("\"enabled\": false");
            if (rule.Discount.HasValue) fields.Add("\"discount\": " + rule.Discount.Value.ToString(CultureInfo.InvariantCulture));
            if (rule.Stock > 0) fields.Add("\"stock\": " + rule.Stock.ToString(CultureInfo.InvariantCulture));
            if (rule.MaxPerAccount > 0) fields.Add("\"maxPerAccount\": " + rule.MaxPerAccount.ToString(CultureInfo.InvariantCulture));
            lines.Add("    " + JsoncFile.FormatString(pair.Key) + ": { " + string.Join(", ", fields) + " }");
        }
        return "{" + newline + string.Join("," + newline, lines) + newline + "  }";
    }

    private string StoresRaw(string newline)
    {
        if (Stores.Count == 0) return "{}";
        var shops = new List<string>();
        foreach (KeyValuePair<string, StoreShopRule> pair in Stores.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            StoreShopRule shop = pair.Value;
            var members = new List<string>();
            if (!shop.Enabled) members.Add("      \"enabled\": false");
            if (shop.Sections.Count > 0)
            {
                var sections = new List<string>();
                foreach (KeyValuePair<string, StoreSectionRule> sectionPair in shop.Sections.OrderBy(s => s.Key, StringComparer.Ordinal))
                {
                    StoreSectionRule section = sectionPair.Value;
                    var fields = new List<string>();
                    if (!section.Enabled) fields.Add("\"enabled\": false");
                    if (section.Include.Count > 0) fields.Add("\"include\": [ " + string.Join(", ", section.Include.Select(JsoncFile.FormatString)) + " ]");
                    if (section.Exclude.Count > 0) fields.Add("\"exclude\": [ " + string.Join(", ", section.Exclude.Select(JsoncFile.FormatString)) + " ]");
                    sections.Add("        " + JsoncFile.FormatString(sectionPair.Key) + ": { " + string.Join(", ", fields) + " }");
                }
                members.Add("      \"sections\": {" + newline + string.Join("," + newline, sections) + newline + "      }");
            }
            shops.Add("    " + JsoncFile.FormatString(pair.Key) + ": {" + newline + string.Join("," + newline, members) + newline + "    }");
        }
        return "{" + newline + string.Join("," + newline, shops) + newline + "  }";
    }

    // Puts back the keys the file does not have (an older stores.json): appended before the closing brace.
    public static string AppendMissing(string text, IReadOnlyDictionary<string, string> values, IEnumerable<string> missing, string newline)
    {
        int end = text.LastIndexOf('}');
        if (end < 0) return text;
        string before = text.Substring(0, end).TrimEnd();
        var sb = new StringBuilder(before);
        bool needsComma = before.Length > 0 && before[^1] != '{' && before[^1] != ',';
        foreach (string key in missing)
        {
            if (!values.TryGetValue(key, out string? raw)) continue;
            if (needsComma) sb.Append(',');
            sb.Append(newline).Append("  ").Append(JsoncFile.FormatString(key)).Append(": ").Append(raw);
            needsComma = true;
        }
        sb.Append(newline).Append(text.Substring(end));
        return sb.ToString();
    }
}
