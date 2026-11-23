using System.Text.RegularExpressions;

namespace SteambirdsLauncher.Core;

// Noise of the game logs, the rules used for the report:
//  - the IL that HarmonyX dumps for every patch ("Generated patch ..." until the next blank line or log header);
//  - "The referenced script on this Behaviour ... is missing!" warnings, which come from the game assets.
// One filter per log: it keeps state between lines.
public sealed class NoiseFilter
{
    private static readonly Regex LogHeader = new Regex(@"^\[(Info|Debug|Warning|Error|Message|Fatal)\s*:",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex HarmonyPatch = new Regex(@"HarmonyX\] Generated patch", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MissingScript = new Regex(@"^The referenced script on this Behaviour .* is missing!",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex MissingScriptFile = new Regex(@"^\(Filename: .*ManagedMonoBehaviourRef",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // A BepInEx line ("[Info   :   BepInEx] ..."). BepInEx also copies them into the Unity Player.log, so the client
    // console hides them there when it already follows BepInEx\LogOutput.log.
    public static bool IsBepInExLine(string line) => LogHeader.IsMatch(line);

    private bool insideIl;
    private bool afterMissingScript;

    public int IlLinesHidden { get; private set; }
    public int MissingScriptWarnings { get; private set; }

    public void Reset()
    {
        insideIl = false;
        afterMissingScript = false;
    }

    // true = show the line.
    public bool Keep(string line)
    {
        if (insideIl)
        {
            if (LogHeader.IsMatch(line))
            {
                insideIl = false;
            }
            else if (line.Trim().Length == 0)
            {
                insideIl = false;
                return false;
            }
            else
            {
                IlLinesHidden++;
                return false;
            }
        }

        if (HarmonyPatch.IsMatch(line))
        {
            insideIl = true;
            IlLinesHidden++;
            return false;
        }

        if (MissingScript.IsMatch(line))
        {
            MissingScriptWarnings++;
            afterMissingScript = true;
            return false;
        }

        if (afterMissingScript)
        {
            if (line.Trim().Length == 0) return false;
            if (MissingScriptFile.IsMatch(line)) return false;
            afterMissingScript = false;
        }

        return true;
    }
}

public static class LogSummary
{
    private static readonly Regex ErrorLine = new Regex(@"Exception|\[Error|\[Fatal|Caught exception|Curl error",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static bool LooksLikeError(string line) => ErrorLine.IsMatch(line);

    // Error lines that repeat the most (cut at 180 characters), most frequent first.
    public static List<KeyValuePair<string, int>> TopErrors(IEnumerable<string> lines, int max)
    {
        return lines
            .Where(l => ErrorLine.IsMatch(l))
            .Select(l =>
            {
                string t = l.Trim();
                return t.Length > 180 ? t.Substring(0, 180) : t;
            })
            .GroupBy(t => t)
            .OrderByDescending(g => g.Count())
            .Take(max)
            .Select(g => new KeyValuePair<string, int>(g.Key, g.Count()))
            .ToList();
    }
}
