using System.Text.Json;

// Stock of the products that the host limited in stores.json ("stock": N units for the whole server).
// What was already sold is kept in data\store_stock.json, so a restart does not refill the shelves. When the host changes
// the "stock" of a product, the sold units are still counted: the remaining amount is the new stock minus what was sold.
// To refill everything use /store restock in the chat, or delete the file.
public static class StoreStock
{
    private static readonly object gate = new object();
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true };
    private static Dictionary<string, int>? sold;
    private static Action<string, string> log = (_, _) => { };

    private static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), "data", "store_stock.json");

    public static void Start(Action<string, string> log)
    {
        StoreStock.log = log;
    }

    // Units of the product left to sell, or null when the host did not limit it.
    public static int? Remaining(string productId)
    {
        int stock = StoreSettingsFile.Current.ProductRules(productId)?.Stock ?? 0;
        if (stock <= 0) return null;
        lock (gate)
        {
            Load();
            return Math.Max(0, stock - (sold!.TryGetValue(productId, out int n) ? n : 0));
        }
    }

    // One more unit sold (only counts when the product has a stock).
    public static void Sold(string productId)
    {
        int stock = StoreSettingsFile.Current.ProductRules(productId)?.Stock ?? 0;
        if (stock <= 0) return;
        lock (gate)
        {
            Load();
            sold![productId] = (sold.TryGetValue(productId, out int n) ? n : 0) + 1;
            Save();
        }
    }

    // Units sold of every product that has a count (for the /store commands).
    public static Dictionary<string, int> SoldUnits()
    {
        lock (gate)
        {
            Load();
            return new Dictionary<string, int>(sold!, StringComparer.Ordinal);
        }
    }

    // Forgets what was sold (refills every limited product).
    public static void Reset()
    {
        lock (gate)
        {
            sold = new Dictionary<string, int>(StringComparer.Ordinal);
            Save();
        }
    }

    private static void Load()
    {
        if (sold != null) return;
        sold = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(FilePath))
            {
                Dictionary<string, int>? read = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath));
                if (read != null) sold = new Dictionary<string, int>(read, StringComparer.Ordinal);
            }
        }
        catch (Exception e)
        {
            log("STORE", "could not read " + FilePath + " (" + e.Message + "): starting with nothing sold");
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(sold, Options));
        }
        catch (Exception e)
        {
            log("STORE", "could not write " + FilePath + ": " + e.Message);
        }
    }
}
