using System.Text.RegularExpressions;

// Support for the handling of server messages.
//
// The known messages (the 232 of MessageCatalog) are handled in Game.
// Only what is left is here:
//  - messages with an ID outside the catalog appear with their raw bytes, the first time of each ID
//    and then every 100 occurrences;
//  - exceptions of handlers are logged and the UDP thread keeps running;
//  - tickets and passwords are redacted before the login body is written to the log.
public static class MessageLog
{
    private static readonly Dictionary<ushort, int> counts = new();
    private static readonly object gate = new();

    private static readonly Regex secretField = new(
        "\"(steam_ticket|netease_ticket|password|session_guid)\"\\s*:\\s*\"[^\"]*\"",
        RegexOptions.Compiled);

    // Logs a message whose ID is not in the client's catalog. Never throws.
    public static void RecordUnhandled(ushort id, byte[] body, int bytesTotal,
        string role, string sender, Action<string> log)
    {
        try
        {
            int total;
            lock (gate)
            {
                counts.TryGetValue(id, out total);
                total++;
                counts[id] = total;
            }

            if (total == 1 || total % 100 == 0)
            {
                var sample = Convert.ToHexString(body[..Math.Min(64, body.Length)]);
                log($"{role} msg {id} ({bytesTotal}B) x{total} from {sender}: ID outside the catalog | {sample}");
            }
        }
        catch (Exception e)
        {
            log($"failed to log msg {id}: {e.Message}");
        }
    }

    // Wraps the handling of a message: if anything throws, the error goes to the log and the UDP thread goes on.
    public static void RunSafe(string context, Action action, Action<string> log)
    {
        try
        {
            action();
        }
        catch (Exception e)
        {
            var stack = (e.StackTrace ?? "").Split('\n').Take(3).Select(l => "    " + l.Trim());
            log($"EXCEPTION in {context}: {e.GetType().Name}: {e.Message}\n{string.Join("\n", stack)}");
        }
    }

    // Replaces the value of tickets and passwords with a marker before the body is written to the log.
    public static string Redact(string body) =>
        secretField.Replace(body, m => $"\"{m.Groups[1].Value}\":\"<redacted>\"");
}
