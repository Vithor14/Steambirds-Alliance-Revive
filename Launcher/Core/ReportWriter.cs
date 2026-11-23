using System.Text;

namespace SteambirdsLauncher.Core;

// logs\server-report.txt: the report of a test session, in one txt:
// server output, BepInEx\LogOutput.log, Unity Player.log, the accounts (account.json) and the .txt files of BepInEx.
// In the game logs the HarmonyX IL and the missing script warnings are summarized, and each log gets a list
// of its most frequent errors.
public static class ReportWriter
{
    public static string Write(string? serverLogPath, string? gameFolder)
    {
        LauncherPaths.EnsureLogsFolder();
        string reportPath = LauncherPaths.ReportFile;
        using var writer = new StreamWriter(reportPath, false, new UTF8Encoding(false));

        writer.WriteLine("=== STEAMBIRDS REVIVE REPORT ===");
        writer.WriteLine("Generated at: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
        writer.WriteLine("Windows: " + Environment.OSVersion.VersionString);
        writer.WriteLine("Launcher: " + LauncherInfo.Version + " (" + LauncherPaths.BaseFolder + ")");
        writer.WriteLine("Server: " + DescribeFile(LauncherPaths.ServerExe));
        writer.WriteLine("Plugin bundled with the launcher: " + DescribeFile(LauncherPaths.BundledPlugin) + PluginVersionText(LauncherPaths.BundledPlugin));
        if (GameInstall.HasGame(gameFolder))
        {
            string build = GameInstall.ReadBuildId(gameFolder);
            writer.WriteLine("Game: " + gameFolder + " (build " + (build.Length > 0 ? build : "unknown") + ")");
            string installedPlugin = GameInstall.InstalledPluginPath(gameFolder!);
            writer.WriteLine("Plugin in the game: " + DescribeFile(installedPlugin) + PluginVersionText(installedPlugin));
        }
        else
        {
            writer.WriteLine("Game: not found at '" + gameFolder + "'");
        }

        writer.WriteLine();
        writer.WriteLine("=== SERVER OUTPUT ===");
        if (string.IsNullOrEmpty(serverLogPath) || !File.Exists(serverLogPath))
        {
            writer.WriteLine("(the server did not run through the launcher in this session)");
        }
        else
        {
            writer.WriteLine("(" + serverLogPath + ")");
            foreach (string line in SafeReadLines(serverLogPath)) writer.WriteLine(line);
        }

        writer.WriteLine();
        writer.WriteLine("=== GAME LOGS ===");
        if (GameInstall.HasGame(gameFolder))
        {
            Append(writer, "BepInEx\\LogOutput.log (last 4000 lines)", GameInstall.BepInExLog(gameFolder!), 4000, true);
        }
        Append(writer, "Unity Player.log (last 4000 lines)", GameInstall.PlayerLog, 4000, true);

        // Accounts of the server (planes and finished dungeons): show whether the tutorial was marked as done.
        // One per player in server\accounts\<key>\account.json.
        foreach (AccountStore.Summary account in AccountStore.List().Take(20))
            Append(writer, "server\\accounts\\" + account.Key + "\\account.json (" + account.Name + ")", Path.Combine(account.Folder, AccountStore.AccountFileName), 0, false);

        if (GameInstall.HasGame(gameFolder))
        {
            string bepinex = GameInstall.BepInExFolder(gameFolder!);
            string[] txts = Directory.Exists(bepinex)
                ? Directory.GetFiles(bepinex, "*.txt", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray()
                : Array.Empty<string>();
            writer.WriteLine();
            writer.WriteLine(".txt files found in the BepInEx folder: " + txts.Length);
            foreach (string txt in txts)
            {
                Append(writer, "BepInEx\\" + Path.GetFileName(txt), txt, 0, false);
            }
        }

        return reportPath;
    }

    private static void Append(StreamWriter writer, string title, string path, int lastLines, bool clean)
    {
        writer.WriteLine();
        writer.WriteLine(new string('=', 70));
        writer.WriteLine("FILE: " + title);

        if (!File.Exists(path))
        {
            writer.WriteLine("(not found: " + path + ")");
            return;
        }

        var info = new FileInfo(path);
        writer.WriteLine("PATH: " + path);
        writer.WriteLine("MODIFIED: " + info.LastWriteTime.ToString("dd/MM/yyyy HH:mm:ss"));
        writer.WriteLine(new string('=', 70));

        if (info.Length == 0)
        {
            writer.WriteLine("(empty file: nothing was written to it in this run)");
            return;
        }

        List<string> lines = SafeReadLines(path);
        if (lines.Count == 0)
        {
            writer.WriteLine("(could not read the file; it may be open in another program)");
            return;
        }

        if (clean)
        {
            var filter = new NoiseFilter();
            lines = lines.Where(filter.Keep).ToList();
            if (filter.IlLinesHidden > 0)
                writer.WriteLine("(" + filter.IlLinesHidden + " lines of HarmonyX IL omitted; plugin 1.13.0 and newer turns this dump off at the source)");
            if (filter.MissingScriptWarnings > 0)
                writer.WriteLine("(" + filter.MissingScriptWarnings + " warnings 'The referenced script on this Behaviour ... is missing!' omitted: they come from the game assets, not from the server)");

            writer.WriteLine("--- most frequent errors in this log ---");
            List<KeyValuePair<string, int>> errors = LogSummary.TopErrors(lines, 12);
            if (errors.Count == 0) writer.WriteLine("(no error or exception line)");
            foreach (KeyValuePair<string, int> error in errors)
                writer.WriteLine(error.Value.ToString().PadLeft(5) + "x  " + error.Key);
            writer.WriteLine("--- content ---");
        }

        if (lastLines > 0 && lines.Count > lastLines)
        {
            writer.WriteLine("(showing the last " + lastLines + " of " + lines.Count + " lines)");
            lines = lines.GetRange(lines.Count - lastLines, lastLines);
        }

        foreach (string line in lines) writer.WriteLine(line);
    }

    private static List<string> SafeReadLines(string path)
    {
        try
        {
            return TextFiles.ReadLinesShared(path);
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string DescribeFile(string path)
    {
        if (!File.Exists(path)) return "not found (" + path + ")";
        var info = new FileInfo(path);
        return path + " (" + info.LastWriteTime.ToString("dd/MM/yyyy HH:mm") + ", " + info.Length + " bytes)";
    }

    private static string PluginVersionText(string path)
    {
        if (!File.Exists(path)) return "";
        string? version = PluginInfo.ReadVersion(path);
        return string.IsNullOrEmpty(version) ? "" : " version " + version;
    }
}
