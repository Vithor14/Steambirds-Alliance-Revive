using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lidgren.Network;

// Accounts of the players, one folder per player in accounts\<key>\ (key = steam_<SteamID64>, or pc_/ip_ without Steam):
//   account.json       accountId, name, planes, pilot and finished dungeons
//   inventory.json     items, currencies and ingredients (Items/InventoryStore.cs)
//   acceptances.json   EULA and other legal notices accepted on this server
//
// The rest of the server calls the static methods below without saying whose account it is (they were written when
// there was a single account). The account is the one of the "current scope": Program.cs opens a scope for every
// message with the connection that sent it (UseFor), and the hooks called outside a message (enemy killed by the
// update loop, leaving a dungeon...) open their own. Without a scope, the account of the only player online is
// used; with nobody or with several players online, a temporary account that saves nothing (and a warning in the log
// says which method had no scope).
//
// What each account keeps (as account.json did before):
//  - the accountId (16 bytes), so the planes stay in the same account after a restart;
//  - the planes (characterId, class, variant, XP, level): they go in the PlayerData of the ClientWelcomeMessage and in
//    ClientSwitchToServerWithPlane, so the same plane reaches the Nexus after the tutorial;
//  - the finished dungeons (finishedDungeonTypes). With "DungeonTutorialWalker" in it, the client stops sending the
//    player to the tutorial at the Nexus (GameClient.IsInitialTutorialPossible);
//  - the pilot (Progression/PilotSave.cs): rank, XP, skills, loadouts, portrait, buffs, unlocks.
//
// To see the tutorial again: /reset_tutorial in the chat, or /tutorial at the Nexus.
public static class Accounts
{
    public const string AccountsFolderName = "accounts";
    public const string AccountFileName = "account.json";
    public const string InventoryFileName = "inventory.json";
    public const string AcceptancesFileName = "acceptances.json";

    public sealed class SavedPlane
    {
        public string CharacterId { get; set; } = "";
        public string Class { get; set; } = "walker";
        public string Variant { get; set; } = "default";

        // Total XP of the plane and the level it has (PlaneRecord.XP / level).
        public int Xp { get; set; }
        public int Level { get; set; } = PlayerData.StartingPlaneLevel;

        // Pilot value of this plane already paid as a fall bonus (deathPilotXpBonus). It only changes with "permadeath": false,
        // when the plane comes back from the fall and can fall again.
        public int BankedPilotXp { get; set; }

        public SavedPlane Copy()
        {
            return new SavedPlane
            {
                CharacterId = CharacterId,
                Class = Class,
                Variant = Variant,
                Xp = Xp,
                Level = Level,
                BankedPilotXp = BankedPilotXp
            };
        }
    }

    // Format of account.json.
    public sealed class AccountData
    {
        public string AccountId { get; set; } = "";

        // Owner of the account (written at every login): key, last name used and SteamID64 (empty without Steam).
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string SteamId { get; set; } = "";
        public DateTime? CreatedAt { get; set; }
        public DateTime? LastAccess { get; set; }

        public List<SavedPlane> Planes { get; set; } = new List<SavedPlane>();
        public List<string> FinishedDungeons { get; set; } = new List<string>();
        public PilotSave Pilot { get; set; } = new PilotSave();
    }

    // One account loaded in memory.
    public sealed class Account
    {
        internal Account(string key, string? folder, AccountData data, Dictionary<string, LegalAcceptance> acceptances, byte[] id, bool newPilot)
        {
            Key = key;
            Folder = folder;
            Data = data;
            Acceptances = acceptances;
            Id = id;
            NewPilot = newPilot;
        }

        public string Key { get; }

        // accounts\<key> (null = temporary account: nothing is saved).
        public string? Folder { get; }

        internal AccountData Data { get; }
        internal Dictionary<string, LegalAcceptance> Acceptances { get; }

        // 16 bytes of the accountId (GeneralID) and the same in base64url.
        public byte[] Id { get; }
        public string IdText => PlayerData.ToBase64Url(Id);

        // true when account.json had no pilot (new account or from before the progression): PilotProgression applies the
        // starting rank and unlocks of progression.json.
        public bool NewPilot { get; }

        // PilotProgression.PrepareAccount already ran for this account (once per load).
        public bool Prepared { get; set; }

        // Inventory of the account, loaded by InventoryStore the first time it is used.
        public InventorySaveData? Inventory { get; set; }

        public string AccountPath => Folder == null ? "" : Path.Combine(Folder, AccountFileName);
        public string InventoryPath => Folder == null ? "" : Path.Combine(Folder, InventoryFileName);
        public string AcceptancesPath => Folder == null ? "" : Path.Combine(Folder, AcceptancesFileName);

        public string Name
        {
            get
            {
                lock (gate) return Data.Name.Length > 0 ? Data.Name : Key;
            }
        }

        // Name saved in account.json ("" when the player never had one).
        public string SavedName
        {
            get
            {
                lock (gate) return Data.Name;
            }
        }

        public override string ToString() => Name + " [" + Key + "]";
    }

    // Disposable returned by Use/UseFor: puts the previous account back.
    public readonly struct Scope : IDisposable
    {
        private readonly Account? previous;
        private readonly bool changed;

        internal Scope(Account? previous, bool changed)
        {
            this.previous = previous;
            this.changed = changed;
        }

        public void Dispose()
        {
            if (changed) ambient.Value = previous;
        }
    }

    private static readonly object gate = new object();
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    // Account of the current scope. AsyncLocal (and not ThreadStatic) so it also follows the HTTP handlers.
    private static readonly AsyncLocal<Account?> ambient = new AsyncLocal<Account?>();
    private static readonly Dictionary<string, Account> loaded = new Dictionary<string, Account>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> warnedWithoutScope = new HashSet<string>(StringComparer.Ordinal);
    private static Account? temporary;
    private static Action<string, string> log = (_, _) => { };

    public static string BaseFolder => Directory.GetCurrentDirectory();
    public static string AccountsFolder => Path.Combine(BaseFolder, AccountsFolderName);

    // ================================================================== startup and accounts

    // Called once when the server starts. The accounts are loaded at the login of each player.
    public static void Load(Action<string, string> log)
    {
        lock (gate)
        {
            Accounts.log = log;
            int onDisk = 0;
            try
            {
                if (Directory.Exists(AccountsFolder))
                    onDisk = Directory.GetDirectories(AccountsFolder).Count(d => File.Exists(Path.Combine(d, AccountFileName)));
            }
            catch
            {
            }
            log("ACCOUNT", AccountsFolder + ": " + onDisk + " account(s) saved, one per player (loaded at each one's login)");
        }
    }

    // Account of a player (loaded from accounts\<key> or created).
    public static Account GetOrCreate(string key, string name, string? steamId, out string note)
    {
        note = "";
        key = SteamTicket.Clean(key);
        lock (gate)
        {
            if (!loaded.TryGetValue(key, out Account? account))
            {
                string folder = Path.Combine(AccountsFolder, key);
                bool exists = File.Exists(Path.Combine(folder, AccountFileName));
                account = LoadFrom(key, folder);
                loaded[key] = account;
                if (!exists) note = "new account";
            }

            AccountData d = account.Data;
            if (name.Length > 0) d.Name = name;
            if (!string.IsNullOrEmpty(steamId)) d.SteamId = steamId;
            d.Key = key;
            d.CreatedAt ??= DateTime.Now;
            d.LastAccess = DateTime.Now;
            SaveUnlocked(account);
            return account;
        }
    }

    // Accounts loaded in this run of the server.
    public static List<Account> LoadedAccounts()
    {
        lock (gate) return loaded.Values.ToList();
    }

    // ================================================================== scope

    // The static methods below use this account until the scope is disposed. null keeps the current one.
    public static Scope Use(Account? account)
    {
        if (account == null) return new Scope(null, false);
        Account? previous = ambient.Value;
        ambient.Value = account;
        return new Scope(previous, true);
    }

    // Scope with the account of the player of this connection (PlayerSessions).
    public static Scope UseFor(NetConnection? connection) => Use(PlayerSessions.AccountOf(connection));

    // Account of the current scope (see the comment at the top for the fallback).
    public static Account Current
    {
        get
        {
            Account? account = ambient.Value;
            if (account != null) return account;
            Account? only = PlayerSessions.OnlyOnlineAccount();
            if (only != null) return only;
            WarnWithoutScope();
            return Temporary();
        }
    }

    private static Account Temporary()
    {
        lock (gate)
        {
            if (temporary == null)
            {
                var data = new AccountData { Key = "_temporary", Name = "(no account)" };
                data.Pilot.Normalize();
                temporary = new Account("_temporary", null, data, new Dictionary<string, LegalAcceptance>(), RandomNumberGenerator.GetBytes(16), false);
            }
            return temporary;
        }
    }

    private static void WarnWithoutScope()
    {
        string where = "?";
        try
        {
            StackFrame[] frames = new StackTrace(2, false).GetFrames();
            foreach (StackFrame frame in frames)
            {
                Type? type = frame.GetMethod()?.DeclaringType;
                string typeName = type?.FullName ?? "";
                if (typeName.StartsWith("InventoryStore", StringComparison.Ordinal)) continue;
                where = typeName + "." + frame.GetMethod()?.Name;
                break;
            }
        }
        catch
        {
        }
        lock (gate)
        {
            if (!warnedWithoutScope.Add(where)) return;
        }
        log("ACCOUNT", "WARNING: " + where + " touched an account without knowing which player's (nobody or several players online): " +
            "using a temporary account that is not saved");
    }

    // ================================================================== reading and changing (current account)

    // 16 bytes of the accountId of the current account.
    public static byte[] AccountId => Current.Id;
    public static string AccountIdText => Current.IdText;
    public static string Name => Current.Name;
    public static bool NewPilot => Current.NewPilot;

    // Read under the lock: the caller copies what it needs inside the function (no references kept).
    public static T Read<T>(Func<AccountData, T> reading)
    {
        Account account = Current;
        lock (gate)
        {
            return reading(account.Data);
        }
    }

    // Change under the lock; account.json is written at the end.
    public static T Change<T>(Func<AccountData, T> change)
    {
        Account account = Current;
        lock (gate)
        {
            T result = change(account.Data);
            SaveUnlocked(account);
            return result;
        }
    }

    // Copies: the caller can go through them without holding the lock.
    public static List<SavedPlane> Planes() => PlanesOf(Current);

    public static List<SavedPlane> PlanesOf(Account account)
    {
        lock (gate)
        {
            return account.Data.Planes.Select(a => a.Copy()).ToList();
        }
    }

    public static List<string> FinishedDungeons()
    {
        Account account = Current;
        lock (gate)
        {
            return new List<string>(account.Data.FinishedDungeons);
        }
    }

    // Copy of the pilot.
    public static PilotSave Pilot()
    {
        Account account = Current;
        lock (gate)
        {
            return account.Data.Pilot.Clone();
        }
    }

    public static SavedPlane? GetPlane(string? characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return null;
        Account account = Current;
        lock (gate)
        {
            SavedPlane? a = account.Data.Planes.FirstOrDefault(x => x.CharacterId == characterId);
            return a?.Copy();
        }
    }

    // New plane (ServerRequestCreateCharacter). With a full hangar, the oldest one leaves.
    public static void AddPlane(string characterId, string planeClass, string variant)
    {
        Account account = Current;
        lock (gate)
        {
            AccountData data = account.Data;
            data.Planes.RemoveAll(a => a.CharacterId == characterId);
            int slots = Math.Max(1, PlayerData.HangarSlots);
            while (data.Planes.Count >= slots)
            {
                SavedPlane left = data.Planes[0];
                data.Planes.RemoveAt(0);
                log("ACCOUNT", account.Name + ": hangar full (" + slots + " slots): the oldest plane " +
                    left.Class + " level " + left.Level + " " + left.CharacterId + " left the account");
            }
            data.Planes.Add(new SavedPlane { CharacterId = characterId, Class = planeClass, Variant = variant });
            SaveUnlocked(account);
        }
    }

    // Destroyed plane: in the game death is permanent (the client takes the plane out of the PlayerData).
    public static bool RemovePlane(string? characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return false;
        Account account = Current;
        lock (gate)
        {
            int removed = account.Data.Planes.RemoveAll(a => a.CharacterId == characterId);
            if (removed > 0) SaveUnlocked(account);
            return removed > 0;
        }
    }

    public static bool FinishedDungeon(string name)
    {
        Account account = Current;
        lock (gate)
        {
            return account.Data.FinishedDungeons.Contains(name);
        }
    }

    // SendMarkDungeonFinished. True when the dungeon entered the list now.
    public static bool MarkDungeonFinished(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        Account account = Current;
        lock (gate)
        {
            if (account.Data.FinishedDungeons.Contains(name)) return false;
            account.Data.FinishedDungeons.Add(name);
            SaveUnlocked(account);
            return true;
        }
    }

    // SendResetDungeonFinished (/reset_tutorial). Returns how many left.
    public static int ClearFinishedDungeons()
    {
        Account account = Current;
        lock (gate)
        {
            int total = account.Data.FinishedDungeons.Count;
            account.Data.FinishedDungeons.Clear();
            SaveUnlocked(account);
            return total;
        }
    }

    // Legal notices (EULA) accepted by the current account: a copy.
    public static Dictionary<string, LegalAcceptance> Acceptances()
    {
        Account account = Current;
        lock (gate)
        {
            return new Dictionary<string, LegalAcceptance>(account.Acceptances);
        }
    }

    // ServerAcceptLegalNotice: saves acceptances.json of the account and returns the updated copy.
    public static Dictionary<string, LegalAcceptance> RecordAcceptance(string legalId, LegalAcceptance acceptance)
    {
        Account account = Current;
        lock (gate)
        {
            account.Acceptances[legalId] = acceptance;
            if (account.Folder != null)
            {
                try
                {
                    Directory.CreateDirectory(account.Folder);
                    WriteAtomic(account.AcceptancesPath, JsonSerializer.Serialize(account.Acceptances));
                }
                catch (Exception e)
                {
                    log("ACCOUNT", "could not write " + account.AcceptancesPath + ": " + e.Message);
                }
            }
            return new Dictionary<string, LegalAcceptance>(account.Acceptances);
        }
    }

    // ================================================================== files

    private static Account LoadFrom(string key, string folder)
    {
        string path = Path.Combine(folder, AccountFileName);
        AccountData? read = null;
        bool hadPilot = false;
        if (File.Exists(path))
        {
            try
            {
                string text = File.ReadAllText(path);
                read = JsonSerializer.Deserialize<AccountData>(text, JsonOptions);
                using JsonDocument document = JsonDocument.Parse(text);
                foreach (JsonProperty property in document.RootElement.EnumerateObject())
                {
                    if (string.Equals(property.Name, "Pilot", StringComparison.OrdinalIgnoreCase) &&
                        property.Value.ValueKind == JsonValueKind.Object)
                        hadPilot = true;
                }
            }
            catch (Exception e)
            {
                // A broken file is kept aside instead of being overwritten.
                string copy = path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                try { File.Copy(path, copy, true); } catch { }
                log("ACCOUNT", "could not read " + path + " (" + e.Message + "): copy in " + copy + ", starting a new account");
            }
        }

        AccountData data = read ?? new AccountData();
        data.Planes ??= new List<SavedPlane>();
        data.FinishedDungeons ??= new List<string>();
        data.Name ??= "";
        data.SteamId ??= "";
        data.Key ??= "";
        data.Planes.RemoveAll(a => a == null || PlayerData.FromBase64Url(a.CharacterId) == null);
        foreach (SavedPlane plane in data.Planes)
        {
            if (plane.Level < 1) plane.Level = 1;
            if (plane.Xp < 0) plane.Xp = 0;
        }
        data.Pilot ??= new PilotSave();
        data.Pilot.Normalize();

        byte[]? id = PlayerData.FromBase64Url(data.AccountId);
        if (id == null)
        {
            id = RandomNumberGenerator.GetBytes(16);
            data.AccountId = PlayerData.ToBase64Url(id);
        }

        var acceptances = new Dictionary<string, LegalAcceptance>();
        string acceptancesPath = Path.Combine(folder, AcceptancesFileName);
        if (File.Exists(acceptancesPath))
        {
            try
            {
                acceptances = JsonSerializer.Deserialize<Dictionary<string, LegalAcceptance>>(File.ReadAllText(acceptancesPath)) ?? acceptances;
            }
            catch (Exception e)
            {
                log("ACCOUNT", "could not read " + acceptancesPath + " (" + e.Message + "): the player will accept the terms again");
            }
        }

        var account = new Account(key, folder, data, acceptances, id, !hadPilot);
        log("ACCOUNT", path + ": account " + data.AccountId + (data.Name.Length > 0 ? " of " + data.Name : "") + ", " + data.Planes.Count + " plane(s)" +
            (data.Planes.Count > 0 ? " (" + string.Join(", ", data.Planes.Select(a => a.Class + "/" + a.Variant + " level " + a.Level)) + ")" : "") +
            ", pilot rank " + data.Pilot.Level + " (" + data.Pilot.BaselineXp + " XP)" + (account.NewPilot ? " [new pilot]" : "") +
            ", finished dungeons: " + (data.FinishedDungeons.Count == 0 ? "none (the client asks for the tutorial)" : string.Join(", ", data.FinishedDungeons)));
        return account;
    }

    private static void SaveUnlocked(Account account)
    {
        if (account.Folder == null) return;
        try
        {
            Directory.CreateDirectory(account.Folder);
            WriteAtomic(account.AccountPath, JsonSerializer.Serialize(account.Data, JsonOptions));
        }
        catch (Exception e)
        {
            log("ACCOUNT", "could not write " + account.AccountPath + ": " + e.Message);
        }
    }

    private static void WriteAtomic(string path, string text)
    {
        string temp = path + ".tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, true);
    }
}
