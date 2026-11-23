using System.Text;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Core.GameData;

namespace SteambirdsLauncher.UI;

// State of the launcher that outlives the screens: settings, the server of this PC, the game, the console lines,
// the game logs being followed and the data read from the game. The tabs are only views of it, so they can be built
// again (language change) without stopping anything.
internal sealed class LauncherContext : IDisposable
{
    private readonly LogTailer bepinexTail;
    private readonly LogTailer unityTail;
    private readonly NoiseFilter bepinexNoise = new NoiseFilter();
    private readonly NoiseFilter unityNoise = new NoiseFilter();
    private readonly object clientLogGate = new object();
    private StreamWriter? clientLog;
    private bool clientLogClosed;
    private bool restartPending;
    private bool startWithoutConfigBinaryAccepted;
    private int gameDataVersion;

    public LauncherContext(Form owner, LauncherSettings settings)
    {
        Owner = owner;
        Settings = settings;
        Theme.StyleToolTip(ToolTip);

        Server.LineReceived += (line, fromStdErr) =>
        {
            LineKind kind = fromStdErr ? LineKind.Error : ClassifyServerLine(line);
            ServerConsole.Append(line, kind, ServerLogLevel.IsInfo(line, kind));
        };
        Server.StateChanged += () =>
        {
            Presence?.Refresh();
            RunOnUi(() => ServerStateChanged?.Invoke());
        };
        Server.Exited += (requested, code) => RunOnUi(() => OnServerExited(requested, code));
        Game.Exited += code =>
        {
            PlayingTarget = null;
            Presence?.Refresh();
            ClientNotice(code.HasValue ? L.F("the game closed (code {0})", code.Value) : L.T("the game closed"));
        };
        Presence = new DiscordPresence(() => Settings.DiscordPresence, DiscordApplicationId, GatherPresenceAsync);
        Presence.Start();

        bepinexTail = new LogTailer("BepInEx", BepInExLogPath(), fromEnd: true);
        unityTail = new LogTailer("Unity", GameInstall.PlayerLog, fromEnd: true);
        bepinexTail.Line += (_, line) => OnGameLogLine(bepinexNoise, "[BepInEx] ", line, fromUnity: false);
        unityTail.Line += (_, line) => OnGameLogLine(unityNoise, "[Unity] ", line, fromUnity: true);
        bepinexTail.Restarted += _ =>
        {
            bepinexNoise.Reset();
            ClientNotice(L.T("BepInEx\\LogOutput.log started over (the game opened)"));
        };
        unityTail.Restarted += _ =>
        {
            unityNoise.Reset();
            ClientNotice(L.T("Player.log started over (the game opened)"));
        };

        // Servers typed before ("Connect by IP") stay in the list until they are removed.
        foreach (string address in settings.RecentServers)
        {
            if (!ServerBrowser.TryParseAddress(address, out string host, out int port)) continue;
            var server = new FoundServer { Name = host, Host = host, HttpPort = port, Manual = true, Network = "IP" };
            servers[server.Key] = server;
        }
        if (settings.ServerAddress.Length > 0 && !servers.ContainsKey(settings.ServerAddress.ToLowerInvariant()) &&
            ServerBrowser.TryParseAddress(settings.ServerAddress, out string chosenHost, out int chosenPort))
        {
            var chosen = new FoundServer
            {
                Name = settings.ServerAddressName.Length > 0 ? settings.ServerAddressName : chosenHost,
                Host = chosenHost,
                HttpPort = chosenPort
            };
            servers[chosen.Key] = chosen;
        }
    }

    public Form Owner { get; }
    public LauncherSettings Settings { get; }
    public ServerProcess Server { get; } = new ServerProcess();
    public GameProcess Game { get; } = new GameProcess();

    // Discord Rich Presence (Core\DiscordPresence.cs).
    public DiscordPresence Presence { get; }  // null only while the constructor runs

    // Where the game opened by the launcher joined (null = no game of the launcher open).
    public GameTarget? PlayingTarget { get; set; }

    private string DiscordApplicationId() =>
        Settings.DiscordApplicationId.Trim().Length > 0 ? Settings.DiscordApplicationId.Trim() : LauncherInfo.DiscordApplicationId;

    private DateTime playingSince = DateTime.UtcNow;
    private DateTime hostingSince = DateTime.UtcNow;
    private bool wasPlaying;
    private bool wasHosting;
    private string cachedPlayerName = "";
    private DateTime cachedPlayerNameAt = DateTime.MinValue;

    // Background thread (DiscordPresence): reads what the game and the server of this PC are doing.
    private async Task<PresenceInput?> GatherPresenceAsync(CancellationToken cancel)
    {
        bool hosting = Server.State == ServerState.Running;
        bool playing = Game.IsRunning(Settings.GameFolder);
        if (!hosting && !playing)
        {
            wasHosting = wasPlaying = false;
            return null;
        }
        if (hosting && !wasHosting) hostingSince = DateTime.UtcNow;
        if (playing && !wasPlaying) playingSince = DateTime.UtcNow;
        wasHosting = hosting;
        wasPlaying = playing;

        ServerNetworkFile local = ServerNetworkFile.Read();
        FoundServer? hosted = null;
        if (hosting || (playing && (PlayingTarget?.IsThisPc ?? Settings.ServerAddress.Length == 0)))
        {
            ServerBrowser.ProbeResult probe = await ServerBrowser.ProbeAsync("localhost", local.HttpPort, 2000, cancel);
            hosted = probe.Server;
        }

        FoundServer? played = null;
        if (playing)
        {
            GameTarget? target = PlayingTarget;
            if (target?.IsThisPc ?? Settings.ServerAddress.Length == 0)
            {
                played = hosted;
            }
            else
            {
                string host = target?.Host ?? "";
                int port = target?.HttpPort ?? ServerNetworkFile.DefaultHttpPort;
                if (target == null) ServerBrowser.TryParseAddress(Settings.ServerAddress, out host, out port);
                if (host.Length > 0) played = (await ServerBrowser.ProbeAsync(host, port, 3000, cancel)).Server;
            }
        }

        if (DateTime.UtcNow - cachedPlayerNameAt > TimeSpan.FromMinutes(1))
        {
            cachedPlayerName = EffectivePlayerName();
            cachedPlayerNameAt = DateTime.UtcNow;
        }

        return new PresenceInput
        {
            Playing = playing,
            PlayedServer = played,
            PlayerName = PlayingTarget?.PlayerName ?? cachedPlayerName,
            Hosting = hosting,
            HostedServer = hosted,
            HostedName = local.ServerName,
            Since = playing ? playingSince : hostingSince
        };
    }
    public ToolTip ToolTip { get; } = new ToolTip { AutoPopDelay = 20000, InitialDelay = 350, ReshowDelay = 200, ShowAlways = true };
    public ConsoleBuffer ServerConsole { get; } = new ConsoleBuffer(6000);
    public ConsoleBuffer ClientConsole { get; } = new ConsoleBuffer(6000);

    // Lists and tables read from the game's configBinary (fallback lists when the game is not found).
    public GameConfig GameData { get; private set; } = GameConfig.Empty();

    // Set by the main window: selects the Settings tab (the Client tab links to it).
    public Action? OpenSettingsTab { get; set; }

    // Set by the main window: selects the Enemy AI tab, on the AI with this id when it exists (the map editor uses it).
    public Action<string?>? OpenAiTab { get; set; }

    // True while the launcher is closing (the report is then written by MainForm itself).
    public bool ShuttingDown { get; set; }

    public string GameFolder => Settings.GameFolder;

    // Raised on the UI thread.
    public event Action? GameFolderChanged;
    public event Action? GameDataChanged;
    public event Action? ServerStateChanged;
    public event Action? ServersChanged;

    // ------------------------------------------------------------------ settings and game data

    public void SaveSettings()
    {
        if (!Settings.Save(out string? error) && error != null) ServerNotice(error);
    }

    public void SetGameFolder(string folder)
    {
        string trimmed = folder.Trim().TrimEnd('\\', '/');
        if (string.Equals(trimmed, Settings.GameFolder, StringComparison.OrdinalIgnoreCase)) return;
        Settings.GameFolder = trimmed;
        SaveSettings();
        bepinexTail.ChangePath(BepInExLogPath(), fromEnd: true);
        GameFolderChanged?.Invoke();
        ReloadGameData();
    }

    // First load, before the screens are built (about half a second with the game installed).
    public void LoadGameDataNow()
    {
        GameData = GameConfig.Load(Settings.GameFolder);
    }

    // Reads the game data again in the background (after the game folder changed).
    public void ReloadGameData()
    {
        int version = Interlocked.Increment(ref gameDataVersion);
        string folder = Settings.GameFolder;
        Task.Run(() =>
        {
            GameConfig loaded = GameConfig.Load(folder);
            RunOnUi(() =>
            {
                if (version != gameDataVersion) return;
                GameData = loaded;
                GameDataChanged?.Invoke();
            });
        });
    }

    // ------------------------------------------------------------------ servers of other PCs (Client tab)
    // The list lives here so it survives the tabs being built again (language change) and a search that is running.

    private readonly Dictionary<string, FoundServer> servers = new Dictionary<string, FoundServer>(StringComparer.OrdinalIgnoreCase);

    // Found by the search or typed in "Connect by IP", by name.
    public List<FoundServer> Servers => servers.Values.OrderByDescending(s => s.Answering).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();

    // Answer of a server on this PC in the last search (name, players), or null.
    public FoundServer? ThisPcAnswer { get; private set; }

    // Networks of this PC in the last search.
    public List<LocalNetwork> SearchedNetworks { get; private set; } = new List<LocalNetwork>();

    public bool Searching { get; private set; }
    public DateTime LastSearch { get; private set; } = DateTime.MinValue;

    // The server Play joins: null = the server of this PC.
    public FoundServer? SelectedServer =>
        Settings.ServerAddress.Length == 0 ? null : servers.TryGetValue(Settings.ServerAddress.ToLowerInvariant(), out FoundServer? s) ? s : null;

    public bool PlaysOnThisPc => SelectedServer == null;

    // Name the other players see: the one typed in the Client tab, or the name of the Steam account.
    public string EffectivePlayerName()
    {
        string typed = Settings.PlayerName.Trim();
        if (typed.Length > 0) return typed;
        return SteamAccount.Current()?.PersonaName.Trim() ?? "";
    }

    // Searches the local network and the VPNs (about 2 seconds). UI thread.
    public async Task SearchServersAsync()
    {
        if (Searching) return;
        Searching = true;
        ServersChanged?.Invoke();
        try
        {
            int port = Settings.SearchPort;
            List<string> extra = Settings.RecentServers.ToList();
            ServerBrowser.SearchResult result = await Task.Run(() => ServerBrowser.SearchAsync(port, extra, 1800, CancellationToken.None));

            foreach (FoundServer found in result.Servers)
            {
                if (servers.TryGetValue(found.Key, out FoundServer? known)) found.Manual = known.Manual;
                servers[found.Key] = found;
            }
            // Servers found before that stopped answering leave after a few minutes (typed ones and the chosen one stay).
            foreach (FoundServer old in servers.Values.ToList())
            {
                bool chosen = string.Equals(old.Key, Settings.ServerAddress, StringComparison.OrdinalIgnoreCase);
                if (!old.Manual && !chosen && old.SeenAt != DateTime.MinValue && DateTime.UtcNow - old.SeenAt > TimeSpan.FromMinutes(5))
                    servers.Remove(old.Key);
            }
            ThisPcAnswer = result.ThisPc;
            SearchedNetworks = result.Networks;
        }
        catch (Exception e)
        {
            ClientNotice(L.F("server search failed: {0}", e.Message));
        }
        finally
        {
            LastSearch = DateTime.UtcNow;
            Searching = false;
            ServersChanged?.Invoke();
        }
    }

    // A server checked by "Connect by IP": goes to the list and to the recent servers of launcher.json.
    public void AddManualServer(FoundServer server)
    {
        server.Manual = true;
        servers[server.Key] = server;
        Settings.RecentServers.RemoveAll(a => string.Equals(a, server.Address, StringComparison.OrdinalIgnoreCase));
        Settings.RecentServers.Insert(0, server.Address);
        while (Settings.RecentServers.Count > 12) Settings.RecentServers.RemoveAt(Settings.RecentServers.Count - 1);
        SaveSettings();
        ServersChanged?.Invoke();
    }

    public void RemoveServer(FoundServer server)
    {
        servers.Remove(server.Key);
        Settings.RecentServers.RemoveAll(a => string.Equals(a, server.Address, StringComparison.OrdinalIgnoreCase));
        if (string.Equals(Settings.ServerAddress, server.Key, StringComparison.OrdinalIgnoreCase))
        {
            Settings.ServerAddress = "";
            Settings.ServerAddressName = "";
        }
        SaveSettings();
        ServersChanged?.Invoke();
    }

    // null = the server of this PC.
    public void SelectServer(FoundServer? server)
    {
        string key = server?.Key ?? "";
        if (string.Equals(Settings.ServerAddress, key, StringComparison.OrdinalIgnoreCase)) return;
        if (server != null) servers[server.Key] = server;
        Settings.ServerAddress = key;
        Settings.ServerAddressName = server?.Name ?? "";
        SaveSettings();
        ServersChanged?.Invoke();
    }

    // Updates a server of the list after "Play" checked it again.
    public void UpdateServer(FoundServer server)
    {
        if (servers.TryGetValue(server.Key, out FoundServer? known)) server.Manual = known.Manual;
        servers[server.Key] = server;
        if (string.Equals(Settings.ServerAddress, server.Key, StringComparison.OrdinalIgnoreCase) && Settings.ServerAddressName != server.Name)
        {
            Settings.ServerAddressName = server.Name;
            SaveSettings();
        }
        ServersChanged?.Invoke();
    }

    // ------------------------------------------------------------------ consoles

    public void ServerNotice(string text) => ServerConsole.Append(">> " + text, LineKind.Launcher);

    public void ClientNotice(string text)
    {
        ClientConsole.Append(">> " + text, LineKind.Launcher);
        WriteClientLog(">> " + text);
    }

    private static LineKind ClassifyServerLine(string line)
    {
        if (line.StartsWith(">>", StringComparison.Ordinal)) return LineKind.Launcher;
        if (line.Contains("[ERROR]", StringComparison.Ordinal) || line.Contains(" ERROR ", StringComparison.Ordinal) ||
            LogSummary.LooksLikeError(line))
            return LineKind.Error;
        if (line.Contains("[WELCOME]", StringComparison.Ordinal) || line.Contains("listening on", StringComparison.Ordinal))
            return LineKind.Good;
        if (line.Contains("[UDP]", StringComparison.Ordinal) || line.Contains("[SYNC]", StringComparison.Ordinal) ||
            line.Contains("[HTTP]", StringComparison.Ordinal))
            return LineKind.Muted;
        if (line.StartsWith("warn:", StringComparison.OrdinalIgnoreCase) || line.Contains("aviso", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("WARNING:", StringComparison.Ordinal))
            return LineKind.Warning;
        return LineKind.Normal;
    }

    private static LineKind ClassifyClientLine(string line)
    {
        if (LogSummary.LooksLikeError(line)) return LineKind.Error;
        if (line.Contains("[Warning", StringComparison.OrdinalIgnoreCase)) return LineKind.Warning;
        if (line.Contains("[Debug", StringComparison.OrdinalIgnoreCase)) return LineKind.Muted;
        return LineKind.Normal;
    }

    private string BepInExLogPath() => GameInstall.HasGame(Settings.GameFolder) ? GameInstall.BepInExLog(Settings.GameFolder) : "";

    private void OnGameLogLine(NoiseFilter filter, string prefix, string line, bool fromUnity)
    {
        // BepInEx copies its lines into Player.log too: they are shown once, from LogOutput.log.
        if (fromUnity && NoiseFilter.IsBepInExLine(line) && GameInstall.HasBepInEx(Settings.GameFolder)) return;
        bool keep = filter.Keep(line);
        if (!keep && Settings.HideClientLogNoise) return;
        string text = prefix + line;
        ClientConsole.Append(text, ClassifyClientLine(line));
        WriteClientLog(text);
    }

    // logs\client-<date>.log: what the client console showed in this launcher session.
    private void WriteClientLog(string text)
    {
        lock (clientLogGate)
        {
            if (clientLogClosed) return;
            try
            {
                if (clientLog == null)
                {
                    LauncherPaths.EnsureLogsFolder();
                    string path = Path.Combine(LauncherPaths.LogsFolder, "client-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
                    clientLog = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
                        new UTF8Encoding(false)) { AutoFlush = true };
                    TrimOldClientLogs(path);
                }
                clientLog.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + text);
            }
            catch
            {
                // The console keeps working without the file.
            }
        }
    }

    private static void TrimOldClientLogs(string current)
    {
        try
        {
            FileInfo[] files = new DirectoryInfo(LauncherPaths.LogsFolder).GetFiles("client-*.log")
                .Where(f => !string.Equals(f.FullName, current, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(f => f.LastWriteTimeUtc).ToArray();
            for (int i = 29; i < files.Length; i++) files[i].Delete();
        }
        catch
        {
        }
    }

    // ------------------------------------------------------------------ server

    // Is a server answering on this PC (the launcher's or one started outside it)?
    public bool IsAnyServerUp() => Server.IsActive || NetworkCheck.AnyServerListening();

    // Starts the server of this PC, explaining in a dialog what is in the way when it cannot start. UI thread only.
    public bool StartServer()
    {
        if (Server.IsActive) return true;

        string? busy = NetworkCheck.FirstBusyServerPort();
        if (busy != null)
        {
            List<NetworkCheck.ServerProcessInfo> others = NetworkCheck.FindServerProcesses(Server.ProcessId);
            NetworkCheck.ServerProcessInfo? orphan = others.FirstOrDefault(o => o.IsLauncherServer);
            if (orphan != null)
            {
                if (!Ui.Confirm(Owner, L.F("A server opened earlier by the launcher is still running (process {0}) and is using {1}.\n\nEnd that server and start a new one?", orphan.Id, busy)))
                    return false;
                if (!NetworkCheck.KillProcess(orphan.Id, out string killError))
                {
                    Ui.ShowError(Owner, L.F("Could not end process {0}: {1}", orphan.Id, killError));
                    return false;
                }
                ServerNotice(L.F("old launcher server (process {0}) ended", orphan.Id));
                Thread.Sleep(500);
                busy = NetworkCheck.FirstBusyServerPort();
            }

            if (busy != null)
            {
                NetworkCheck.ServerProcessInfo? other = others.FirstOrDefault(o => !o.IsLauncherServer);
                string who = other != null
                    ? L.F("Another server of the project is already open ({0}), started outside the launcher.", other.Path.Length > 0 ? other.Path : L.F("process {0}", other.Id))
                    : L.F("Another program is using {0}.", busy);
                Ui.ShowError(Owner, who + "\n\n" + L.T("Close that server to start the launcher's one. While it is open, the Play button of the Client tab uses it."));
                return false;
            }
        }

        if (!EnsureServerGameData()) return false;

        restartPending = false;
        if (!Server.Start(Settings.GameFolder, out string error))
        {
            Ui.ShowError(Owner, error);
            return false;
        }
        ServerNotice(L.F("server started: {0}", LauncherPaths.ServerExe));
        return true;
    }

    // Before the server starts: without the game's configBinary (host PC without the game and a launcher package
    // without server\data\configBinary) the server has no item catalog, so no loot at all, plus built-in XP tables
    // and only the walker. Asks what to do; false cancels the start. "Start without it" is asked once per session.
    private bool EnsureServerGameData()
    {
        if (ServerGameData.Find(Settings.GameFolder) != null) return true;

        string notice = L.T("the server has no configBinary (game not found and no server\\data\\configBinary): no item catalog, so no loot, reforge or recycling; built-in XP tables; only the walker unlocked");
        if (startWithoutConfigBinaryAccepted)
        {
            ServerNotice(notice);
            return true;
        }

        while (true)
        {
            var chooseFile = new TaskDialogButton(L.T("Choose the configBinary file..."));
            var chooseFolder = new TaskDialogButton(L.T("Choose the game folder"));
            var startAnyway = new TaskDialogButton(L.T("Start without it"));
            var page = new TaskDialogPage
            {
                Caption = LauncherInfo.Title,
                Heading = L.T("The server is missing the game file configBinary"),
                Text = L.F("The game was not found on this PC ({0}) and the launcher has no copy in server\\data.\n\n" +
                           "Without configBinary the server has no item catalog: enemies and chests drop no loot, and reforge and recycling are off. " +
                           "It also uses the built-in XP tables, does not check skills and only unlocks the walker.\n\n" +
                           "Fix: copy the file from a PC with the game (Steambirds_Data\\StreamingAssets\\configBinary) and choose it below, " +
                           "or choose the game folder in the Settings tab. Running \"Build launcher.ps1\" on a PC with the game also puts it in the package.",
                           Settings.GameFolder.Length > 0 ? Settings.GameFolder : L.T("no folder set")),
                Icon = TaskDialogIcon.Warning,
                AllowCancel = true
            };
            page.Buttons.Add(chooseFile);
            page.Buttons.Add(chooseFolder);
            page.Buttons.Add(startAnyway);
            page.Buttons.Add(TaskDialogButton.Cancel);
            page.DefaultButton = chooseFile;

            TaskDialogButton answer = TaskDialog.ShowDialog(Owner, page, TaskDialogStartupLocation.CenterOwner);
            if (answer == chooseFile)
            {
                if (ChooseConfigBinary()) return true;
                continue;
            }
            if (answer == chooseFolder)
            {
                OpenSettingsTab?.Invoke();
                return false;
            }
            if (answer == startAnyway)
            {
                startWithoutConfigBinaryAccepted = true;
                ServerNotice(notice);
                return true;
            }
            return false;
        }
    }

    // Lets the user pick a configBinary and copies it to server\data. True when the server now has one.
    private bool ChooseConfigBinary()
    {
        using var dialog = new OpenFileDialog
        {
            Title = L.T("Choose the configBinary of the game (Steambirds_Data\\StreamingAssets)"),
            FileName = ServerGameData.FileName,
            Filter = L.T("configBinary of the game") + "|" + ServerGameData.FileName + "|" + L.T("All files") + "|*.*",
            CheckFileExists = true,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(Owner) != DialogResult.OK) return false;

        Cursor? previous = Cursor.Current;
        Cursor.Current = Cursors.WaitCursor;
        bool ok;
        string error;
        try
        {
            ok = ServerGameData.TryImport(dialog.FileName, out error);
        }
        finally
        {
            Cursor.Current = previous ?? Cursors.Default;
        }
        if (!ok)
        {
            Ui.ShowError(Owner, error);
            return false;
        }

        ServerNotice(L.F("configBinary copied to {0}", ServerGameData.ServerCopyPath));
        ReloadGameData();
        return true;
    }

    public void StopServer()
    {
        restartPending = false;
        Server.Stop();
    }

    public void RestartServer()
    {
        if (!Server.IsActive)
        {
            StartServer();
            return;
        }
        restartPending = true;
        ServerNotice(L.T("restarting the server..."));
        Server.Stop();
    }

    // Waits (without freezing the window) until the server is ready, stopped or the time is over.
    public async Task<bool> WaitForServerReady(int timeoutMilliseconds)
    {
        DateTime limit = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (DateTime.UtcNow < limit)
        {
            if (Server.State == ServerState.Running) return true;
            if (!Server.IsActive) return false;
            await Task.Delay(250);
        }
        return Server.State == ServerState.Running;
    }

    private void OnServerExited(bool requested, int? code)
    {
        if (requested) ServerNotice(L.T("server stopped"));
        else ServerConsole.Append(">> " + (code.HasValue ? L.F("the server closed by itself (code {0}). The last lines above show why.", code.Value)
                                                          : L.T("the server closed by itself. The last lines above show why.")), LineKind.Error);

        if (!ShuttingDown && Settings.ReportOnServerStop) WriteReport();

        if (restartPending && !ShuttingDown)
        {
            restartPending = false;
            StartServer();
        }
    }

    // Writes logs\server-report.txt in the background (the logs can be a few MB).
    public void WriteReport()
    {
        string? logPath = Server.SessionLogPath;
        string gameFolder = Settings.GameFolder;
        ServerNotice(L.T("writing the report..."));
        Task.Run(() =>
        {
            try
            {
                string path = ReportWriter.Write(logPath, gameFolder);
                ServerNotice(L.F("report saved in {0}", path));
            }
            catch (Exception e)
            {
                ServerConsole.Append(">> " + L.F("could not write the report: {0}", e.Message), LineKind.Error);
            }
        });
    }

    // ------------------------------------------------------------------ helpers

    public void RunOnUi(Action action)
    {
        if (Owner.IsDisposed || !Owner.IsHandleCreated) return;
        try
        {
            Owner.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // The window is closing.
        }
    }

    public void Dispose()
    {
        Presence.Dispose();
        bepinexTail.Dispose();
        unityTail.Dispose();
        lock (clientLogGate)
        {
            clientLogClosed = true;
            clientLog?.Dispose();
            clientLog = null;
        }
    }
}
