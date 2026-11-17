using System.Globalization;
using System.Security.Cryptography;
using System.Text;

// Who is logging in. The game sends "steam_ticket" = <session ticket in hex>_<appid> (ExternalAuthServiceSteam)
// and "session_guid" = SystemInfo.deviceUniqueIdentifier (Api.LoginExternalAuth).
//
// A Steam session ticket (SteamUser.GetAuthSessionTicket) starts with the GC token block:
//   uint32 length (20), uint64 GC token, uint64 SteamID, uint32 time
// so the SteamID64 of the player is at bytes 12..19 (little endian). The ticket is not checked with Steam (that needs
// a Steam Web API key): a community server trusts it, like it trusts the rest of what the client says.
//
// Without a ticket the account belongs to the PC (session_guid); without both, to the IP.
public static class SteamTicket
{
    // SteamID64 of an individual account in the public universe: universe 1, type 1 (0x011 in the top 12 bits).
    private const ulong IndividualPublicMask = 0xFFF0000000000000UL;
    private const ulong IndividualPublicValue = 0x0110000000000000UL;

    public static bool TryGetSteamId(string? steamTicket, out ulong steamId)
    {
        steamId = 0;
        if (string.IsNullOrWhiteSpace(steamTicket)) return false;
        string hex = steamTicket.Trim();
        int underscore = hex.IndexOf('_');
        if (underscore >= 0) hex = hex.Substring(0, underscore);
        if (hex.Length < 40 || hex.Length % 2 != 0) return false;

        byte[] bytes;
        try
        {
            bytes = Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return false;
        }
        if (bytes.Length < 20) return false;

        uint gcBlock = BitConverter.ToUInt32(bytes, 0);
        ulong id = BitConverter.ToUInt64(bytes, 12);
        if (gcBlock != 20 || (id & IndividualPublicMask) != IndividualPublicValue || (id & 0xFFFFFFFFUL) == 0) return false;
        steamId = id;
        return true;
    }

    // Account key (folder name in accounts\): "steam_<SteamID64>", "pc_<hash of the device id>" or "ip_<address>".
    // The second value says how the player was identified (for the log).
    public static (string Key, string? SteamId, string How) AccountKey(string? steamTicket, string? sessionGuid, string? remoteAddress)
    {
        if (TryGetSteamId(steamTicket, out ulong steamId))
        {
            string text = steamId.ToString(CultureInfo.InvariantCulture);
            return ("steam_" + text, text, "Steam " + text);
        }
        if (!string.IsNullOrWhiteSpace(sessionGuid))
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionGuid.Trim()));
            return ("pc_" + Convert.ToHexString(hash, 0, 8).ToLowerInvariant(), null, "PC (no Steam ticket)");
        }
        string address = string.IsNullOrWhiteSpace(remoteAddress) ? "unknown" : remoteAddress;
        return ("ip_" + Clean(address), null, "IP " + address + " (no ticket and no session_guid)");
    }

    // Only letters, digits, "_" and "-" (the key is a folder name).
    public static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-') builder.Append(c);
            else if (c == '.' || c == ':') builder.Append('-');
        }
        return builder.Length == 0 ? "unknown" : builder.ToString();
    }
}
