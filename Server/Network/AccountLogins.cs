using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Accounts with username and password (network.json "passwordLogin"): the credentials of each account, in
// accounts\<key>\login.json (never in account.json, so the Accounts tab and the exports do not see the hash).
//
// How the game gets here (Ozy API, Api.cs of the client):
//  - Steam login (steam_ticket) with no account for that Steam ID -> error "user_missing" -> the game opens its register screen
//    (username, e-mail, password) -> /user/create with the same ticket -> the account of the Steam ID gets a username and password.
//  - /user/login with username + password (no Steam, or -loginas / -password) -> the account of that username.
//  - /user/change changes the password, /user/reset_request only logs the request (there is no e-mail server: the host
//    resets the password with the chat command /passwd NAME NEW_PASSWORD).
// An account that was made before the option existed has no login.json: it keeps logging in through Steam, and /passwd can give it one.
public static class AccountLogins
{
    public const string FileName = "login.json";
    private const int Iterations = 100_000;
    public const int MinPassword = 4;
    public const int MaxPassword = 64;
    public const int MinName = 3;

    public sealed class LoginData
    {
        public string Username { get; set; } = "";
        public string Salt { get; set; } = "";
        public string Hash { get; set; } = "";
        public string Email { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    private static readonly object gate = new object();
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
    // username (lower case) -> account key; filled by scanning accounts\*\login.json once and kept up to date by Save.
    private static Dictionary<string, string>? index;

    public static bool Enabled => ServerNetworkSettingsFile.Current.PasswordLogin;

    // The name as it is compared: trimmed, lower case.
    public static string Normalize(string? username) => PlayerNamePath.Clean(username).ToLowerInvariant();

    // null = fine; otherwise the Ozy error the game shows ("username_invalid", "password_invalid").
    public static string? CheckName(string? username)
    {
        string name = PlayerNamePath.Clean(username);
        return name.Length < MinName ? "username_invalid" : null;
    }

    public static string? CheckPassword(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinPassword || password.Length > MaxPassword ? "password_invalid" : null;

    private static string FolderOf(string key) => Path.Combine(Accounts.AccountsFolder, SteamTicket.Clean(key));
    private static string PathOf(string key) => Path.Combine(FolderOf(key), FileName);

    private static Dictionary<string, string> Index()
    {
        lock (gate)
        {
            if (index != null) return index;
            index = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                if (Directory.Exists(Accounts.AccountsFolder))
                {
                    foreach (string folder in Directory.GetDirectories(Accounts.AccountsFolder))
                    {
                        LoginData? data = Read(Path.Combine(folder, FileName));
                        if (data != null && data.Username.Length > 0) index[Normalize(data.Username)] = Path.GetFileName(folder);
                    }
                }
            }
            catch
            {
                // An unreadable folder only means that account cannot log in by name.
            }
            return index;
        }
    }

    private static LoginData? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<LoginData>(File.ReadAllText(path), Options) : null;
        }
        catch
        {
            return null;
        }
    }

    // The account key of a username, or null.
    public static string? KeyOf(string? username)
    {
        string name = Normalize(username);
        if (name.Length == 0) return null;
        lock (gate) return Index().TryGetValue(name, out string? key) ? key : null;
    }

    // true when the account already has a username and password.
    public static bool Has(string key)
    {
        lock (gate) return Read(PathOf(key)) != null;
    }

    public static string? UsernameOf(string key)
    {
        lock (gate) return Read(PathOf(key))?.Username;
    }

    private static string HashOf(string password, byte[] salt) =>
        Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32));

    public static bool Verify(string key, string? password)
    {
        if (password == null) return false;
        LoginData? data;
        lock (gate) data = Read(PathOf(key));
        if (data == null || data.Salt.Length == 0) return false;
        string hash = HashOf(password, Convert.FromHexString(data.Salt));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(data.Hash));
    }

    // Saves (or replaces) the credentials of the account. The caller has already checked the name and the password.
    public static void Save(string key, string username, string password, string? email)
    {
        key = SteamTicket.Clean(key);
        lock (gate)
        {
            LoginData? old = Read(PathOf(key));
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            var data = new LoginData
            {
                Username = PlayerNamePath.Clean(username),
                Salt = Convert.ToHexString(salt),
                Hash = HashOf(password, salt),
                Email = email ?? old?.Email ?? "",
                CreatedAt = old?.CreatedAt ?? DateTime.Now
            };
            Directory.CreateDirectory(FolderOf(key));
            File.WriteAllText(PathOf(key), JsonSerializer.Serialize(data, Options));
            Dictionary<string, string> names = Index();
            if (old != null) names.Remove(Normalize(old.Username));
            names[Normalize(data.Username)] = key;
        }
    }

    public static void SetPassword(string key, string password)
    {
        string? name = UsernameOf(key);
        if (name != null) Save(key, name, password, null);
    }

    // POST /ozy/user/create: the account for a new username. With a Steam ticket the account is the one of that Steam ID
    // (the player keeps logging in through Steam), otherwise "user_<name>". Returns the account, or the error for the game.
    public static Accounts.Account? Create(string? username, string? password, string? email, string? steamTicket, string? sessionGuid,
        string? remoteAddress, out string? error, out string how)
    {
        how = "";
        error = CheckName(username) ?? CheckPassword(password);
        if (error != null) return null;
        string name = PlayerNamePath.Clean(username);
        if (KeyOf(name) != null)
        {
            error = "username_duplicate";
            return null;
        }
        string key;
        string? steamId = null;
        if (SteamTicket.TryGetSteamId(steamTicket, out ulong id))
        {
            steamId = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            key = "steam_" + steamId;
            how = "Steam " + steamId;
            if (Has(key))
            {
                error = "username_duplicate";
                return null;
            }
        }
        else
        {
            key = "user_" + SteamTicket.Clean(Normalize(name));
            how = "username";
            if (Accounts.FindExisting(key) != null && Has(key))
            {
                error = "username_duplicate";
                return null;
            }
        }
        Accounts.Account account = Accounts.GetOrCreate(key, name, steamId, out _);
        Save(key, name, password!, email);
        return account;
    }

    // POST /ozy/user/login with username and password: the account, or the error for the game.
    public static Accounts.Account? Login(string? username, string? password, out string? error)
    {
        error = null;
        if (CheckName(username) != null)
        {
            error = "username_invalid";
            return null;
        }
        string? key = KeyOf(username);
        if (key == null)
        {
            error = "user_missing";
            return null;
        }
        if (!Verify(key, password))
        {
            error = "password_wrong";
            return null;
        }
        return Accounts.GetOrCreate(key, UsernameOf(key) ?? "", null, out _);
    }

    // true when this Steam ID already has an account folder (a Steam player who is not new keeps logging in without a password).
    public static bool SteamAccountExists(string? steamTicket)
    {
        if (!SteamTicket.TryGetSteamId(steamTicket, out ulong id)) return true;
        string key = "steam_" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Accounts.FindExisting(key) != null;
    }
}
