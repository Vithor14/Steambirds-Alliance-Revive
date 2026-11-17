using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SteambirdsLauncher.Core;

// The Steam account logged in on this PC, read from Steam's own files (no Steam API):
//   HKCU\Software\Valve\Steam\ActiveProcess  "ActiveUser" = 32-bit account id of the logged-in user (0 = nobody)
//   <Steam>\config\loginusers.vdf            "PersonaName" (the name shown in Steam) of each account used on this PC
// The game logs in on the server with this account (Steam ticket): the server keeps one account per SteamID, and the
// player name the launcher sends is only the name the other players see.
public sealed class SteamAccount
{
    private const ulong SteamId64Base = 76561197960265728UL;

    public ulong SteamId64 { get; private init; }
    public string PersonaName { get; private init; } = "";
    public string AccountName { get; private init; } = "";

    // Steam is open with this account logged in (ActiveUser); false = the last account in loginusers.vdf.
    public bool LoggedIn { get; private init; }

    public string SteamIdText => SteamId64 == 0 ? "" : SteamId64.ToString(CultureInfo.InvariantCulture);

    private static readonly object Gate = new object();
    private static SteamAccount? cached;
    private static DateTime cachedAt = DateTime.MinValue;

    // The account of Steam on this PC, or null when Steam was never used here. Read again every 10 seconds.
    public static SteamAccount? Current()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - cachedAt < TimeSpan.FromSeconds(10)) return cached;
            cachedAt = DateTime.UtcNow;
            try
            {
                cached = Read();
            }
            catch
            {
                cached = null;
            }
            return cached;
        }
    }

    private static SteamAccount? Read()
    {
        uint activeUser = 0;
        try
        {
            object? value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", 0);
            if (value is int number) activeUser = unchecked((uint)number);
        }
        catch
        {
        }

        List<(ulong Id, string Persona, string Account, bool MostRecent)> users = ReadLoginUsers();
        if (activeUser != 0)
        {
            ulong id = SteamId64Base + activeUser;
            var match = users.FirstOrDefault(u => u.Id == id);
            return new SteamAccount { SteamId64 = id, PersonaName = match.Persona ?? "", AccountName = match.Account ?? "", LoggedIn = true };
        }

        // Steam closed: the account that used Steam last on this PC.
        var last = users.FirstOrDefault(u => u.MostRecent);
        if (last.Id == 0 && users.Count > 0) last = users[0];
        if (last.Id == 0) return null;
        return new SteamAccount { SteamId64 = last.Id, PersonaName = last.Persona ?? "", AccountName = last.Account ?? "", LoggedIn = false };
    }

    // loginusers.vdf: "users" { "<SteamID64>" { "AccountName" "..." "PersonaName" "..." "MostRecent" "1" ... } ... }
    private static List<(ulong Id, string Persona, string Account, bool MostRecent)> ReadLoginUsers()
    {
        var result = new List<(ulong, string, string, bool)>();
        string? steam = GameInstall.SteamFolder();
        if (steam == null) return result;
        string path = Path.Combine(steam, "config", "loginusers.vdf");
        if (!File.Exists(path)) return result;

        string text = TextFiles.ReadShared(path);
        foreach (Match block in Regex.Matches(text, "\"(7656\\d{13})\"\\s*\\{([^{}]*)\\}"))
        {
            if (!ulong.TryParse(block.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)) continue;
            string body = block.Groups[2].Value;
            string persona = Field(body, "PersonaName");
            string account = Field(body, "AccountName");
            string mostRecent = Field(body, "MostRecent");
            result.Add((id, persona, account, mostRecent == "1"));
        }
        return result;
    }

    private static string Field(string body, string key)
    {
        Match m = Regex.Match(body, "\"" + key + "\"\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.Replace("\\\"", "\"").Replace("\\\\", "\\") : "";
    }
}
