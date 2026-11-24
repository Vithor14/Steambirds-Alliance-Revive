using System.Diagnostics;

namespace SteambirdsLauncher.Core;

// Where the game logs in and plays.
//  - This PC: OZY_URL=http://localhost:<httpPort>/ozy and "-connecttolocal" (the game joins 127.0.0.1:<nexusPort>
//    right after the login; "-connecttolocalport" when the Nexus port is not 1111).
//  - Another PC: OZY_URL=http://<ip>:<httpPort>/ozy and no connect argument: the login answers with the Nexus of that
//    server (its address and port) and the game joins it (GameClient.LoginAndThenJoinNexus).
// The player name goes in the path of OZY_URL when the server accepts it (FoundServer.OzyUrl).
public sealed class GameTarget
{
    public bool IsThisPc { get; init; }
    public string Host { get; init; } = "localhost";
    public int HttpPort { get; init; } = ServerNetworkFile.DefaultHttpPort;
    public int NexusPort { get; init; } = ServerNetworkFile.DefaultNexusPort;
    public string? PlayerName { get; init; }
    public string ServerName { get; init; } = "";
    // The map test server of the map editor (MapTestPorts), not the server of the Server tab.
    public bool IsMapTest { get; init; }

    public string OzyUrl => FoundServer.OzyUrl(Host, HttpPort, PlayerName);
}

// The game client. Besides the target above: "-locale <id> -skipInitMenu" when a game language is chosen in the
// launcher (CommandLine.Locale in the client takes it before the language saved in the game; -skipInitMenu skips the
// first-run language picker). Steam must be open: the game logs in with a Steam ticket.
public sealed class GameProcess
{
    private readonly object gate = new object();
    private Process? launched;

    // Raised on a background thread when the game opened by the launcher closes.
    public event Action<int?>? Exited;

    public static bool IsSteamRunning()
    {
        Process[] found = Process.GetProcessesByName("steam");
        foreach (Process p in found) p.Dispose();
        return found.Length > 0;
    }

    public static void OpenSteam()
    {
        Process.Start(new ProcessStartInfo("steam://open/main") { UseShellExecute = true });
    }

    // Is the game running (opened by the launcher or not)? Only processes from the configured folder count.
    public bool IsRunning(string? gameFolder) => FindRunningId(gameFolder) != null;

    public int? FindRunningId(string? gameFolder)
    {
        lock (gate)
        {
            try
            {
                if (launched != null && !launched.HasExited) return launched.Id;
            }
            catch
            {
            }
        }

        string expected = string.IsNullOrWhiteSpace(gameFolder) ? "" : Path.GetFullPath(GameInstall.ExePath(gameFolder));
        int? result = null;
        foreach (Process p in Process.GetProcessesByName(GameInstall.ProcessName))
        {
            try
            {
                if (result != null) continue;
                string path = "";
                try
                {
                    path = p.MainModule?.FileName ?? "";
                }
                catch
                {
                }
                // Without access to the path (another user, protected process) it is counted as the game.
                if (path.Length == 0 || expected.Length == 0 || string.Equals(Path.GetFullPath(path), expected, StringComparison.OrdinalIgnoreCase))
                    result = p.Id;
            }
            finally
            {
                p.Dispose();
            }
        }
        return result;
    }

    public static string Arguments(string? locale, GameTarget target)
    {
        var arguments = new List<string>();
        if (target.IsThisPc)
        {
            arguments.Add("-connecttolocal");
            if (target.NexusPort != ServerNetworkFile.DefaultNexusPort) arguments.Add("-connecttolocalport " + target.NexusPort);
        }
        if (!string.IsNullOrWhiteSpace(locale)) arguments.Add("-locale " + locale.Trim() + " -skipInitMenu");
        return string.Join(" ", arguments);
    }

    public int Launch(string gameFolder, string? locale, GameTarget target)
    {
        // steam_appid.txt lets the game start the Steam API outside the Steam library (depot downloaded by hand).
        GameInstall.EnsureSteamAppId(gameFolder);
        var startInfo = new ProcessStartInfo(GameInstall.ExePath(gameFolder))
        {
            WorkingDirectory = gameFolder,
            Arguments = Arguments(locale, target),
            UseShellExecute = false
        };
        startInfo.Environment["OZY_URL"] = target.OzyUrl;

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Exited += (_, _) =>
        {
            int? code = null;
            try
            {
                code = process.ExitCode;
            }
            catch
            {
            }
            lock (gate)
            {
                if (ReferenceEquals(launched, process)) launched = null;
            }
            process.Dispose();
            Exited?.Invoke(code);
        };

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException(L.T("Windows did not start the game."));
        }
        lock (gate) launched = process;
        return process.Id;
    }
}
