using System.Text.RegularExpressions;

namespace SteambirdsLauncher.Core;

// Levels of the lines of the server console. The server writes "HH:mm:ss.fff [TAG] text".
//  - default level: warnings, errors and the events of the server itself (a player connects or leaves, a dungeon opens,
//    a portal is used, a plane is destroyed, settings are read...);
//  - info level: what the players do while they play (skills, shots, game messages, XP, loot, inventory, spawns...).
//    The console hides the info lines until "Show player actions (info)" is on; the log file keeps every line.
public static class ServerLogLevel
{
    private static readonly Regex TagOfLine = new Regex(@"^\d{2}:\d{2}:\d{2}(?:\.\d+)?\s+\[([A-Z_]+)\]", RegexOptions.Compiled);

    private static readonly HashSet<string> InfoTags = new HashSet<string>(StringComparer.Ordinal)
    {
        "SKILL", "SHOT", "MSG", "UNIT", "EFFECT", "RELAY", "SYNC", "SUMMARY", "RPC", "QUEST", "STORE", "WORKSHOP",
        "XP", "LOOT", "ITEMS", "INVENTORY", "BUFF", "CAREER", "PROGRESSION", "ACTIONS", "SPAWN", "PLANE", "FLOOR", "MAPINFO", "ENEMY"
    };

    // true = an info line (hidden in the default view). Errors and warnings are never info.
    public static bool IsInfo(string line, LineKind kind)
    {
        if (kind is LineKind.Error or LineKind.Warning or LineKind.Launcher) return false;
        Match match = TagOfLine.Match(line);
        return match.Success && InfoTags.Contains(match.Groups[1].Value);
    }
}
