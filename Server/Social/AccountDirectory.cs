using System.Text.Json;

// Who exists on this server: accountId, name and last visit of every account saved in accounts\<key>\account.json, without
// loading the accounts (their planes and inventories). The friend list and the flocks keep only account ids; this is how an id
// becomes a name when the player is offline, and how a name typed in /friend or /w becomes an id.
//
// Read from disk the first time it is asked, and again (at most every 10 s) when an id or name is not known, so a new account
// shows up soon. Accounts loaded in this run keep the directory fresh through Seen.
public static class AccountDirectory
{
    public sealed class Entry
    {
        public string Key = "";
        public string IdText = "";
        public string Name = "";
        public DateTime LastAccess = DateTime.MinValue;
    }

    private static readonly object gate = new object();
    private static readonly Dictionary<string, Entry> byId = new Dictionary<string, Entry>(StringComparer.Ordinal);
    private static DateTime scannedAt = DateTime.MinValue;

    public static Entry? Find(string? idText)
    {
        if (string.IsNullOrEmpty(idText)) return null;
        lock (gate)
        {
            if (byId.TryGetValue(idText, out Entry? entry)) return entry;
            if (!ScanIfOld()) return null;
            return byId.TryGetValue(idText, out entry) ? entry : null;
        }
    }

    public static Entry? Find(byte[]? id)
    {
        return id == null || id.Length < 16 ? null : Find(PlayerData.ToBase64Url(id));
    }

    // Exact name, ignoring case.
    public static Entry? FindByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        lock (gate)
        {
            Entry? found = byId.Values.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
            if (!ScanIfOld()) return null;
            return byId.Values.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }

    // The name of an account (its key when it never had one); the id itself when nothing is known.
    public static string NameOf(string? idText)
    {
        Entry? entry = Find(idText);
        if (entry == null) return idText ?? "";
        return entry.Name.Length > 0 ? entry.Name : entry.Key;
    }

    // An account that logged in or was loaded: keeps its name and last visit up to date.
    public static void Seen(Accounts.Account account)
    {
        lock (gate)
        {
            string id = account.IdText;
            if (!byId.TryGetValue(id, out Entry? entry))
            {
                entry = new Entry { IdText = id };
                byId[id] = entry;
            }
            entry.Key = account.Key;
            entry.Name = account.Name;
            entry.LastAccess = DateTime.Now;
        }
    }

    // Every known account (a copy).
    public static List<Entry> All()
    {
        lock (gate)
        {
            ScanIfOld();
            return byId.Values.ToList();
        }
    }

    // Reads accounts\ again, at most every 10 s. False when it did not (too soon).
    private static bool ScanIfOld()
    {
        if (DateTime.UtcNow - scannedAt < TimeSpan.FromSeconds(10)) return false;
        scannedAt = DateTime.UtcNow;
        try
        {
            string folder = Accounts.AccountsFolder;
            if (Directory.Exists(folder))
            {
                foreach (string directory in Directory.GetDirectories(folder))
                {
                    string path = Path.Combine(directory, Accounts.AccountFileName);
                    if (!File.Exists(path)) continue;
                    try
                    {
                        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { AllowTrailingCommas = true });
                        JsonElement root = document.RootElement;
                        string id = Text(root, "AccountId");
                        if (id.Length == 0) continue;
                        if (!byId.TryGetValue(id, out Entry? entry))
                        {
                            entry = new Entry { IdText = id };
                            byId[id] = entry;
                        }
                        entry.Key = Path.GetFileName(directory);
                        string name = Text(root, "Name");
                        // An account loaded in this run knows its name better than the file read a moment ago.
                        if (name.Length > 0 && (entry.Name.Length == 0 || entry.LastAccess == DateTime.MinValue)) entry.Name = name;
                        if (entry.LastAccess == DateTime.MinValue && DateTime.TryParse(Text(root, "LastAccess"), out DateTime last)) entry.LastAccess = last;
                    }
                    catch
                    {
                        // A broken file: Accounts keeps it aside when the player logs in.
                    }
                }
            }
        }
        catch
        {
        }
        return true;
    }

    private static string Text(JsonElement root, string property)
    {
        if (root.ValueKind != JsonValueKind.Object) return "";
        foreach (JsonProperty p in root.EnumerateObject())
        {
            if (string.Equals(p.Name, property, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                return p.Value.GetString() ?? "";
        }
        return "";
    }
}
