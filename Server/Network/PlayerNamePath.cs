using System.Text;

// Player name chosen in the launcher. The game logs in with username = null (only the Steam ticket), so the launcher
// puts the name in the address of the login server:
//   OZY_URL = http://<ip>:<port>/n/<name in UTF-8, as hex>/ozy
// The game only adds "/user/login", "/alive"... after OZY_URL (Api.Route), so every request arrives as
// /n/<hex>/ozy/... and Split gives back /ozy/... and the name. Without the prefix the name comes from the account.
public static class PlayerNamePath
{
    public const int MaxLength = 24;

    // "/n/4A6F616F/ozy/user/login" -> "/ozy/user/login", name "Joao". Paths without the prefix come back as they are.
    public static string Split(string path, out string? name)
    {
        name = null;
        if (!path.StartsWith("/n/", StringComparison.Ordinal)) return path;
        int end = path.IndexOf('/', 3);
        string hex = end < 0 ? path.Substring(3) : path.Substring(3, end - 3);
        string rest = end < 0 ? "/" : path.Substring(end);
        try
        {
            if (hex.Length > 0 && hex.Length % 2 == 0 && hex.Length <= 400)
                name = Clean(Encoding.UTF8.GetString(Convert.FromHexString(hex)));
        }
        catch (FormatException)
        {
            name = null;
        }
        if (name != null && name.Length == 0) name = null;
        return rest;
    }

    // No control characters, no line breaks, spaces trimmed, at most MaxLength characters.
    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var builder = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (char.IsControl(c)) continue;
            builder.Append(c);
        }
        string clean = builder.ToString().Trim();
        while (clean.Contains("  ", StringComparison.Ordinal)) clean = clean.Replace("  ", " ", StringComparison.Ordinal);
        if (clean.Length > MaxLength) clean = clean.Substring(0, MaxLength).TrimEnd();
        return clean;
    }
}
