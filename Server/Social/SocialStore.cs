using System.Text.Json;

// What an account keeps about other players: its friends, the players it blocked or muted, the default flock and when it joined
// flocks (for the "joined recently" rule). One file per account: accounts\<key>\social.json, next to account.json.
//
// Players are saved by accountId (base64url, the same text as GeneralID.ToBase64): names change, ids do not.
public sealed class SocialSave
{
    public List<string> Friends { get; set; } = new List<string>();
    public List<string> Blocked { get; set; } = new List<string>();
    public List<string> Muted { get; set; } = new List<string>();

    // The flock that goes in the player's name plate, in /f and in the roster (accountId text of the flock; "" = none).
    public string DefaultFlock { get; set; } = "";

    // Unix seconds of the last times this account joined a flock (the newest last).
    public List<long> FlockJoins { get; set; } = new List<long>();
}

public static class SocialStore
{
    public const string FileName = "social.json";

    private static readonly object gate = new object();
    private static readonly Dictionary<string, SocialSave> cache = new Dictionary<string, SocialSave>(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };
    private static Action<string, string> log = (_, _) => { };

    public static void Start(Action<string, string> logger)
    {
        lock (gate) log = logger;
    }

    // Reads the account's social data inside the lock. The callback must not keep the object.
    public static T Read<T>(Accounts.Account account, Func<SocialSave, T> reading)
    {
        lock (gate) return reading(Load(account));
    }

    // Changes the account's social data inside the lock and saves it.
    public static T Change<T>(Accounts.Account account, Func<SocialSave, T> change)
    {
        lock (gate)
        {
            SocialSave save = Load(account);
            T result = change(save);
            Save(account, save);
            return result;
        }
    }

    // Copy of a list, so the caller can go through it without the lock.
    public static List<string> FriendsOf(Accounts.Account account) => Read(account, s => new List<string>(s.Friends));

    public static string DefaultFlockOf(Accounts.Account account) => Read(account, s => s.DefaultFlock);

    private static SocialSave Load(Accounts.Account account)
    {
        string key = account.Key;
        if (cache.TryGetValue(key, out SocialSave? known)) return known;

        SocialSave save = new SocialSave();
        string? folder = account.Folder;
        if (folder != null)
        {
            string path = Path.Combine(folder, FileName);
            if (File.Exists(path))
            {
                try
                {
                    save = JsonSerializer.Deserialize<SocialSave>(File.ReadAllText(path), Options) ?? new SocialSave();
                }
                catch (Exception e)
                {
                    string copy = path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    try { File.Copy(path, copy, true); } catch { }
                    log("SOCIAL", "could not read " + path + " (" + e.Message + "): copy in " + copy + ", starting empty");
                    save = new SocialSave();
                }
            }
        }
        save.Friends ??= new List<string>();
        save.Blocked ??= new List<string>();
        save.Muted ??= new List<string>();
        save.DefaultFlock ??= "";
        save.FlockJoins ??= new List<long>();
        cache[key] = save;
        return save;
    }

    private static void Save(Accounts.Account account, SocialSave save)
    {
        string? folder = account.Folder;
        if (folder == null) return;   // temporary account: nothing is saved
        try
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, FileName);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(save, Options));
            File.Move(temp, path, true);
        }
        catch (Exception e)
        {
            log("SOCIAL", "could not write the social data of " + account.Name + ": " + e.Message);
        }
    }
}
