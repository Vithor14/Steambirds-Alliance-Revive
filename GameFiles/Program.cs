using System.Diagnostics;
using System.Text;

namespace SteambirdsGameFiles;

// Steambirds Game Files: takes from the player's own copy of Steambirds Alliance the files that cannot be published
// on GitHub, and puts them where the server and the map editor read them. Steps:
//   1. finds the game (Steam depot folder) and copies its configBinary to the server's data folder;
//   2. installs BepInEx and the Steambirds Quieter plugin in the game folder (BepInEx is downloaded from its GitHub);
//   3. starts the server of the package and opens the game once in export mode (STEAMBIRDS_EXPORT): the plugin writes
//      the data files (units, actions, map info, portraits, floor assets), draws the map editor pictures and closes
//      the game;
//   4. copies the data files to the server and the pictures to editor-assets.
// See README.md. Everything shown is also written to logs\game-files.log.
internal static class Program
{
    private static int Main(string[] args)
    {
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }

        Options options = Options.Parse(args);
        if (options.Help || options.Error != null)
        {
            if (options.Error != null) ConsoleUi.Error(options.Error);
            Console.WriteLine(Options.Usage);
            return options.Error != null ? 2 : 0;
        }

        ConsoleUi.Interactive = !options.NoPause;
        ToolLayout layout = ToolLayout.Resolve(options);
        ConsoleUi.OpenLog(Path.Combine(layout.LogsFolder, "game-files.log"));

        int code;
        try
        {
            code = Run(options, layout);
        }
        catch (Exception e)
        {
            ConsoleUi.Error("unexpected error: " + e);
            code = 1;
        }

        if (ConsoleUi.LogPath != null) ConsoleUi.Say("Log: " + ConsoleUi.LogPath);
        ConsoleUi.Close();
        ConsoleUi.WaitForEnter("Press Enter to close");
        return code;
    }

    private static int Run(Options options, ToolLayout layout)
    {
        string version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "?";
        ConsoleUi.Say("=== STEAMBIRDS GAME FILES " + version + " ===", ConsoleColor.Cyan);
        ConsoleUi.Say("Takes from YOUR copy of Steambirds Alliance the files that cannot be shared on GitHub:");
        ConsoleUi.Say("  - configBinary and the data the server needs (units, skills, map info, portraits);");
        ConsoleUi.Say("  - the pictures of the floor pieces and units for the map editor.");
        ConsoleUi.Say("Package: " + layout.Kind + " (" + layout.BaseFolder + ")");
        ConsoleUi.Say("  server data     -> " + layout.ServerDataFolder);
        ConsoleUi.Say("  editor pictures -> " + layout.EditorAssetsFolder);

        // ------------------------------------------------------------ 1. game
        ConsoleUi.Title("Game");
        string? game = FindGame(options);
        if (game == null) return 1;
        ConsoleUi.Info("folder: " + game);
        string build = GameInstallation.ReadBuildId(game);
        if (build.Length > 0) ConsoleUi.Info("build: " + build + (build == "201219.1413" ? "" : " (the project targets 201219.1413)"));

        ConsoleUi.Title("Game configs (configBinary)");
        GameDataCopy.CopyConfigBinary(game, layout.ServerDataFolder);

        if (options.CopyOnly)
        {
            ConsoleUi.Title("Data files already in the game's BepInEx folder");
            GameDataCopy.CopyPluginFiles(game, layout.ServerDataFolder, layout.EditorAssetsFolder, null);
            ConsoleUi.Info("(--copy-only: the game was not opened, so no pictures were drawn)");
            return 0;
        }

        // ------------------------------------------------------------ 2. plan
        bool hasBepInEx = BepInExSetup.IsInstalled(game);
        ConsoleUi.Title("Export from the game");
        ConsoleUi.Say("Next steps:");
        ConsoleUi.Say(hasBepInEx
            ? "  - update the Steambirds Quieter plugin in the game folder (BepInEx is already there);"
            : "  - install BepInEx " + BepInExSetup.Version + " (downloaded from its GitHub) and the Steambirds Quieter plugin in the game folder;");
        ConsoleUi.Say(options.NoServer
            ? "  - open the game once, without a server (Steam must be open);"
            : "  - start the server of this package and open the game once on this PC (Steam must be open);");
        ConsoleUi.Say("  - wait while the plugin writes the data and draws the pictures; the game closes by itself (a few minutes).");
        ConsoleUi.Say("If the game asks something (language, terms), answer it: the export goes on by itself.");
        if (!ConsoleUi.Ask("Continue?", true)) return 0;

        // ------------------------------------------------------------ 3. Steam and the game
        if (!WaitForSteam()) return 1;
        if (!CloseRunningGame()) return 1;

        // ------------------------------------------------------------ 4. BepInEx and the plugin
        ConsoleUi.Title("BepInEx and the plugin");
        if (!hasBepInEx)
        {
            bool? x64 = GameInstallation.Is64Bit(game);
            if (x64 == null) ConsoleUi.Warn("could not read the architecture of " + GameInstallation.ExeName + "; using the 64-bit BepInEx.");
            string? error = BepInExSetup.Install(game, layout.BaseFolder, x64 ?? true);
            if (error != null)
            {
                ConsoleUi.Error("BepInEx was not installed: " + error);
                ConsoleUi.Say("Install it by hand: extract " + BepInExSetup.DownloadUrl(x64 ?? true));
                ConsoleUi.Say("into " + game + " (or put that zip next to this tool) and run the tool again.");
                return 1;
            }
            ConsoleUi.Done("BepInEx " + BepInExSetup.Version + " installed in " + GameInstallation.BepInExFolder(game));
        }
        else
        {
            ConsoleUi.Info("BepInEx is already installed.");
        }

        if (!File.Exists(layout.PluginFile))
        {
            ConsoleUi.Error("the plugin is missing from the package: " + layout.PluginFile);
            return 1;
        }
        string? pluginError = BepInExSetup.InstallPlugin(game, layout.PluginFile, out bool pluginChanged);
        if (pluginError != null)
        {
            ConsoleUi.Error("could not install the plugin in " + BepInExSetup.PluginPath(game) + ": " + pluginError);
            return 1;
        }
        if (pluginChanged) ConsoleUi.Done("plugin installed: " + BepInExSetup.PluginPath(game));
        else ConsoleUi.Info("the plugin is up to date.");

        string? appIdError = GameInstallation.EnsureSteamAppId(game);
        if (appIdError != null) ConsoleUi.Warn("could not create steam_appid.txt: " + appIdError);

        // ------------------------------------------------------------ 5. server
        using var server = new LocalServer();
        server.ReadPorts(layout.NetworkFile);
        bool useServer = !options.NoServer;
        if (useServer)
        {
            ConsoleUi.Title("Server");
            if (server.IsAnswering())
            {
                ConsoleUi.Info("a server is already running on port " + server.HttpPort + ": using it.");
            }
            else if (layout.HasServer)
            {
                ConsoleUi.Info("starting " + layout.ServerExe);
                string? serverError = server.Start(layout.ServerExe, layout.ServerFolder, game, Path.Combine(layout.LogsFolder, "game-files-server.log"));
                if (serverError != null)
                {
                    ConsoleUi.Warn(serverError);
                    ConsoleUi.Warn("the game opens without a server; the export may still work.");
                    server.Stop();
                    useServer = false;
                }
                else
                {
                    ConsoleUi.Done("server running on port " + server.HttpPort + ".");
                }
            }
            else
            {
                ConsoleUi.Warn(ToolLayout.ServerExeName + " is not in " + layout.ServerFolder + "; the game opens without a server.");
                useServer = false;
            }
        }

        // ------------------------------------------------------------ 6. export
        ConsoleUi.Title("Export");
        string exportFolder = Path.Combine(Path.GetTempPath(), "SteambirdsGameFiles", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        DateTime exportStartUtc = DateTime.UtcNow.AddSeconds(-5);
        ExportRun.Result result;
        try
        {
            result = ExportRun.Run(game, exportFolder, server.HttpPort, server.NexusPort, useServer);
        }
        finally
        {
            if (server.StartedHere)
            {
                // A few seconds for the server to save the account of the game that just left.
                Thread.Sleep(3000);
                server.Stop();
                ConsoleUi.Info("server stopped.");
            }
        }

        // ------------------------------------------------------------ 7. copy
        ConsoleUi.Title("Copying to the server and the map editor");
        GameDataCopy.CopyPluginFiles(game, layout.ServerDataFolder, layout.EditorAssetsFolder, exportStartUtc);
        int pictures = 0;
        if (result.Finished)
        {
            pictures = GameDataCopy.CopyPictures(exportFolder, layout.EditorAssetsFolder);
            ConsoleUi.Done(pictures + " picture(s) copied to " + layout.EditorAssetsFolder);
            try { Directory.Delete(exportFolder, recursive: true); } catch { }
        }

        // ------------------------------------------------------------ 8. result
        ConsoleUi.Title("Result");
        if (!result.Finished)
        {
            ConsoleUi.Error("the export did not finish: " + result.Outcome + ".");
            ConsoleUi.Say("Look at " + Path.Combine(GameInstallation.BepInExFolder(game), "LogOutput.log") + " (lines with \"Export mode\").");
            ConsoleUi.Say("The pictures drawn so far stay in " + exportFolder);
            return 1;
        }

        ConsoleUi.Info("export: " + result.Outcome + " - " + result.Value("detail"));
        ConsoleUi.Info("floor pictures: " + result.Value("ground") + ", unit pictures: " + result.Value("units"));
        List<string> problems = result.Values("problem").ToList();
        if (problems.Count > 0)
        {
            ConsoleUi.Info(problems.Count + " object(s) without a picture (details in the log).");
            foreach (string problem in problems) ConsoleUi.Write("  problem: " + problem);
        }
        if (!layout.IsLauncherPackage)
            ConsoleUi.Say("Map editor: copy the folder " + layout.EditorAssetsFolder + " next to SteambirdsLauncher.exe (or run this tool from the launcher folder).");
        ConsoleUi.Say("Start (or restart) the server so it reads the new files.");
        return result.Outcome == "ok" ? 0 : 1;
    }

    private static string? FindGame(Options options)
    {
        if (!string.IsNullOrWhiteSpace(options.GameFolder))
        {
            string full = Path.GetFullPath(options.GameFolder);
            if (GameInstallation.HasGame(full)) return full;
            ConsoleUi.Error(GameInstallation.ExeName + " is not in " + full);
            return null;
        }

        string? found = GameInstallation.Find();
        if (found != null) return found;

        ConsoleUi.Error("the game was not found.");
        ConsoleUi.Say("Download it with Steam (Steambirds Alliance must be in your library): open the Steam console,");
        ConsoleUi.Say("paste the command below and press Enter:");
        ConsoleUi.Say("  " + GameInstallation.DepotCommand, ConsoleColor.White);
        ConsoleUi.Say("When Steam says \"Depot download complete\", run this tool again (or use --game <folder>).");
        if (ConsoleUi.Interactive && ConsoleUi.Ask("Open the Steam console now?", true))
            GameInstallation.OpenSteam("steam://open/console");
        return null;
    }

    // The game logs in with the Steam account: Steam must be open.
    private static bool WaitForSteam()
    {
        if (GameInstallation.IsSteamRunning()) return true;

        ConsoleUi.Warn("Steam is not open. Opening it...");
        GameInstallation.OpenSteam("steam://open/main");
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            Thread.Sleep(1000);
            if (GameInstallation.IsSteamRunning()) break;
        }

        while (!GameInstallation.IsSteamRunning())
        {
            if (!ConsoleUi.Interactive)
            {
                ConsoleUi.Error("Steam is not open.");
                return false;
            }
            if (!ConsoleUi.Ask("Steam is still closed. Open Steam, sign in and answer Y to try again (N stops)", true))
                return false;
        }
        ConsoleUi.Info("Steam is open. If it is still signing in, wait for it before the game opens.");
        if (ConsoleUi.Interactive) ConsoleUi.WaitForEnter("Press Enter when Steam shows your library... ");
        return true;
    }

    // The game must start again with the export variable.
    private static bool CloseRunningGame()
    {
        Process[] running = GameInstallation.RunningGames();
        try
        {
            if (running.Length == 0) return true;
            ConsoleUi.Warn("the game is open; it has to start again for the export.");
            if (!ConsoleUi.Ask("Close it now?", true)) return false;
            foreach (Process process in running)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(10000);
                }
                catch
                {
                }
            }
            return true;
        }
        finally
        {
            foreach (Process process in running) process.Dispose();
        }
    }
}
