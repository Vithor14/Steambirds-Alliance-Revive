using System.Globalization;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Core.GameData;

namespace SteambirdsLauncher.UI;

// "Client" tab: the state of the game (and its download with Steam), the player name, the server list (this PC, the
// servers found on the local network and on VPNs like Radmin and Hamachi, and the ones typed in "Connect by IP"),
// the Play button, and a console that follows the game logs (BepInEx\LogOutput.log and the Unity Player.log).
// The game folder and the game language are chosen in the Settings tab.
internal sealed class ClientTab : UserControl
{
    private readonly LauncherContext context;
    private readonly Label folderLabel;
    private readonly Label gameStatus;
    private readonly Label languageStatus;
    private readonly Label steamStatus;
    private readonly Button openSteamButton;
    private readonly Label pluginStatus;
    private readonly Button installPluginButton;
    private readonly TextBox playerName;
    private readonly Label steamAccountLabel;
    private readonly ListView servers;
    private readonly Label searchStatus;
    private readonly Button searchButton;
    private readonly TextBox ipBox;
    private readonly Button connectButton;
    private readonly Button forgetButton;
    private readonly Label targetLabel;
    private readonly CheckBox startServerWithGame;
    private readonly Button playButton;
    private Font? boldFont;
    private DateTime nextPluginCheck = DateTime.MinValue;
    private bool launching;
    private bool connecting;
    private bool updatingList;
    private bool updatingCheckbox;

    public ClientTab(LauncherContext context)
    {
        this.context = context;
        ToolTip tip = context.ToolTip;

        // ------------------------------------------------------------ game
        folderLabel = Ui.Text("");
        gameStatus = Ui.Text("");
        languageStatus = Ui.Text("");
        steamStatus = Ui.Text("");
        openSteamButton = Ui.Button(L.T("Open Steam"), (_, _) => OpenSteam());
        pluginStatus = Ui.Text("");
        installPluginButton = Ui.Button(L.T("Install plugin"), (_, _) => InstallPlugin());
        var settingsButton = Ui.Button(L.T("Change..."), (_, _) => context.OpenSettingsTab?.Invoke());

        var downloadButton = Ui.Button(L.T("Download the game with Steam"), (_, _) => OpenDownload());
        tip.SetToolTip(downloadButton, L.F("Copies the command\n{0}\nopens the Steam console and follows the download.", LauncherInfo.DepotCommand));

        TableLayoutPanel gameTable = Ui.Table(2);
        gameTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gameTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(gameTable, Ui.Text(L.T("Game folder:")), Ui.Flow(folderLabel, settingsButton));
        Ui.AddRow(gameTable, Ui.Text(L.T("Status:")), gameStatus);
        Ui.AddRow(gameTable, Ui.Text(L.T("Game language:")), languageStatus);
        Ui.AddRow(gameTable, Ui.Text(L.T("Steam:")), Ui.Flow(steamStatus, openSteamButton));
        Ui.AddRow(gameTable, Ui.Text(L.T("Quieter plugin:")), Ui.Flow(pluginStatus, installPluginButton));
        Label pluginHelp = Ui.Wrapping(L.T("What it does: a small mod (BepInEx) that runs inside your game. It fixes client bugs (the Flock Hall building invisible in Rebel City, and the death window showing the numbers of the previous death from the second death on), hides the noise of the game logs and turns off the telemetry errors. Without it: the game still opens and you can play, but with those bugs and with a Player.log full of noise. A PC that only joins other people's servers loses nothing else. The server does not need it: its data tables ship in server\\data."), 560, gray: true);
        Ui.AddRow(gameTable, null, pluginHelp);
        tip.SetToolTip(pluginStatus, L.T("The Quieter plugin fixes bugs of the game client and hides log noise. Without it the game works, with those bugs."));
        Ui.AddRow(gameTable, Ui.Text(L.T("Install:")), downloadButton);

        // ------------------------------------------------------------ player
        playerName = new TextBox { Text = context.Settings.PlayerName, Width = 220, MaxLength = 24, Margin = new Padding(3, 4, 3, 3) };
        playerName.TextChanged += (_, _) =>
        {
            context.Settings.PlayerName = playerName.Text.Trim();
            context.SaveSettings();
        };
        steamAccountLabel = Ui.Note("");
        Label nameHelp = Ui.Wrapping(L.T("Empty = the name of your Steam account. Your account on each server is your Steam account: changing the name keeps your pilot and your planes."), 420, gray: true);
        TableLayoutPanel playerTable = Ui.Table(2);
        playerTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        playerTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(playerTable, Ui.Text(L.T("Player name:")), playerName);
        Ui.AddRow(playerTable, null, steamAccountLabel);
        Ui.AddRow(playerTable, null, nameHelp);

        // ------------------------------------------------------------ servers
        servers = Ui.Grid((L.T("Server"), 220), (L.T("Address"), 160), (L.T("Players"), 70), (L.T("Ping (ms)"), 70),
            (L.T("Network"), 110), (L.T("Status"), 330));
        servers.Dock = DockStyle.None;
        servers.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        servers.Height = 150;
        servers.SelectedIndexChanged += (_, _) => OnServerClicked();
        servers.DoubleClick += async (_, _) => await PlayAsync();

        searchButton = Ui.Button(L.T("Search again"), async (_, _) => await context.SearchServersAsync());
        var autoSearch = new CheckBox
        {
            Text = L.T("Search by itself"),
            Checked = context.Settings.AutoSearch,
            AutoSize = true,
            Margin = new Padding(8, 6, 3, 3)
        };
        autoSearch.CheckedChanged += (_, _) =>
        {
            context.Settings.AutoSearch = autoSearch.Checked;
            context.SaveSettings();
        };
        tip.SetToolTip(autoSearch, L.T("Searches the local network and the VPNs (Radmin, Hamachi...) when the launcher opens and every minute while this tab is open."));
        searchStatus = Ui.Note("");

        ipBox = new TextBox { Width = 200, PlaceholderText = L.T("e.g. 26.10.20.30 or 26.10.20.30:6443"), Margin = new Padding(3, 4, 3, 3) };
        ipBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await ConnectByIpAsync();
        };
        connectButton = Ui.Button(L.T("Connect"), async (_, _) => await ConnectByIpAsync());
        forgetButton = Ui.Button(L.T("Remove from list"), (_, _) => ForgetSelected());
        tip.SetToolTip(ipBox, L.T("IP of the server PC (local network, Radmin, Hamachi or internet) and, when it is not 6443, the login port of the server."));

        TableLayoutPanel serverTable = Ui.Table(1);
        serverTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(serverTable, Ui.WrapFlow(searchButton, autoSearch, searchStatus));
        Ui.AddRow(serverTable, servers);
        Ui.AddRow(serverTable, Ui.Flow(Ui.Text(L.T("Connect by IP:")), ipBox, connectButton, forgetButton));

        // ------------------------------------------------------------ play
        targetLabel = Ui.Bold("");
        targetLabel.Margin = new Padding(3, 14, 12, 3);
        startServerWithGame = new CheckBox
        {
            Text = L.T("Start the server of this PC with the game"),
            Checked = context.Settings.StartServerWithGame,
            AutoSize = true,
            Margin = new Padding(3, 12, 12, 3)
        };
        startServerWithGame.CheckedChanged += (_, _) =>
        {
            if (updatingCheckbox) return;
            context.Settings.StartServerWithGame = startServerWithGame.Checked;
            context.SaveSettings();
        };
        tip.SetToolTip(startServerWithGame, L.T("Only for the server of this PC: it turns off by itself while another PC is chosen in the list."));
        playButton = new Button
        {
            Text = L.T("PLAY"),
            Font = new Font(Control.DefaultFont.FontFamily, 13f, FontStyle.Bold),
            Size = new Size(190, 46),
            Margin = new Padding(3, 3, 3, 3),
            UseVisualStyleBackColor = true
        };
        playButton.Click += async (_, _) => await PlayAsync();
        var playPanel = Ui.Flow(targetLabel, startServerWithGame, playButton);
        playPanel.Anchor = AnchorStyles.Right | AnchorStyles.Top;

        // ------------------------------------------------------------ console
        var console = new ConsoleView(context.ClientConsole) { Dock = DockStyle.Fill };
        var hideNoise = new CheckBox
        {
            Text = L.T("Hide noise (HarmonyX IL, missing scripts)"),
            Checked = context.Settings.HideClientLogNoise,
            AutoSize = true,
            Margin = new Padding(12, 6, 3, 3)
        };
        hideNoise.CheckedChanged += (_, _) =>
        {
            context.Settings.HideClientLogNoise = hideNoise.Checked;
            context.SaveSettings();
        };
        console.Toolbar.Controls.Add(hideNoise);

        // ------------------------------------------------------------ layout
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(Ui.Group(L.T("Game"), gameTable), 0, 0);
        root.Controls.Add(Ui.Group(L.T("Player"), playerTable), 1, 0);
        GroupBox serversGroup = Ui.Group(L.T("Servers (this PC, local network and VPNs like Radmin and Hamachi)"), serverTable);
        root.Controls.Add(serversGroup, 0, 1);
        root.SetColumnSpan(serversGroup, 2);
        root.Controls.Add(playPanel, 0, 2);
        root.SetColumnSpan(playPanel, 2);
        GroupBox consoleGroup = Ui.Group(L.T("Client console (game logs)"), console);
        consoleGroup.AutoSize = false;
        root.Controls.Add(consoleGroup, 0, 3);
        root.SetColumnSpan(consoleGroup, 2);
        Controls.Add(root);

        context.GameFolderChanged += OnGameFolderChanged;
        context.GameDataChanged += RefreshStatus;
        context.ServersChanged += FillServers;
        FillServers();
        RefreshStatus();
    }

    private Font BoldFont => boldFont ??= new Font(servers.Font, FontStyle.Bold);

    private void OnGameFolderChanged()
    {
        nextPluginCheck = DateTime.MinValue;
        RefreshStatus();
    }

    // Called once per second by the main window.
    public void RefreshStatus()
    {
        string folder = context.GameFolder;
        bool hasGame = GameInstall.HasGame(folder);
        int? gameId = hasGame ? context.Game.FindRunningId(folder) : null;

        Ui.SetStatus(folderLabel, folder.Length > 0 ? folder : L.T("(not chosen)"), TextRole.Muted);
        if (!hasGame)
        {
            Ui.SetStatus(gameStatus, L.T("Steambirds.exe was not found in this folder. Use \"Download the game with Steam\" below."), TextRole.Bad);
        }
        else
        {
            string build = GameInstall.ReadBuildId(folder);
            string text = L.F("Steambirds.exe found, build {0}", build.Length > 0 ? build : L.T("unknown"));
            TextRole role = TextRole.Good;
            if (build.Length > 0 && build != LauncherInfo.ExpectedGameBuild)
            {
                text += " " + L.F("(the server was made for {0})", LauncherInfo.ExpectedGameBuild);
                role = TextRole.Warn;
            }
            if (gameId.HasValue) text += " — " + L.F("game open (process {0})", gameId.Value);
            Ui.SetStatus(gameStatus, text, role);
        }

        string locale = context.Settings.GameLocale;
        GameConfig.Locale? chosen = context.GameData.Locales.FirstOrDefault(l => l.Id == locale);
        Ui.SetStatus(languageStatus, locale.Length == 0 ? L.T("chosen in the game") : (chosen?.ToString() ?? locale), TextRole.Normal);

        bool steam = GameProcess.IsSteamRunning();
        Ui.SetStatus(steamStatus, steam ? L.T("open") : L.T("closed: open Steam and log in before playing"), steam ? TextRole.Good : TextRole.Bad);
        openSteamButton.Visible = !steam;

        SteamAccount? account = SteamAccount.Current();
        string steamText = account == null
            ? L.T("Steam account: not found on this PC")
            : L.F("Steam account: {0} (ID {1}){2}", account.PersonaName.Length > 0 ? account.PersonaName : account.AccountName, account.SteamIdText,
                account.LoggedIn ? "" : " — " + L.T("last used, Steam is closed"));
        Ui.SetStatus(steamAccountLabel, steamText, TextRole.Muted);
        string placeholder = account != null && account.PersonaName.Length > 0 ? account.PersonaName : L.T("Steam name");
        if (playerName.PlaceholderText != placeholder) playerName.PlaceholderText = placeholder;

        if (DateTime.UtcNow >= nextPluginCheck)
        {
            nextPluginCheck = DateTime.UtcNow.AddSeconds(5);
            RefreshPlugin(folder, gameId.HasValue);
        }

        UpdateThisPcRow();
        UpdateTarget();

        // Search by itself every minute while this tab is on screen.
        if (context.Settings.AutoSearch && Visible && !context.Searching && DateTime.UtcNow - context.LastSearch > TimeSpan.FromSeconds(60))
            _ = context.SearchServersAsync();

        playButton.Enabled = !launching && hasGame && !gameId.HasValue;
        playButton.Text = gameId.HasValue ? L.T("GAME OPEN") : launching ? L.T("OPENING...") : L.T("PLAY");
        connectButton.Enabled = !connecting;
        searchButton.Enabled = !context.Searching;
    }

    private void RefreshPlugin(string folder, bool gameRunning)
    {
        PluginInfo.Report report = PluginInfo.Check(folder);
        string bundled = report.BundledVersion.Length > 0 ? report.BundledVersion : "?";
        switch (report.State)
        {
            case PluginInfo.Status.NoGame:
                Ui.SetStatus(pluginStatus, "—", TextRole.Muted);
                installPluginButton.Visible = false;
                break;
            case PluginInfo.Status.NoBepInEx:
                Ui.SetStatus(pluginStatus, L.T("BepInEx is not installed in the game (the game runs without the plugin, but without its fixes)"), TextRole.Warn);
                installPluginButton.Visible = false;
                break;
            case PluginInfo.Status.NotInstalled:
                Ui.SetStatus(pluginStatus, report.HasBundled ? L.F("not installed (the launcher has version {0})", bundled)
                                                             : L.T("not installed (the launcher's plugin folder is empty)"), TextRole.Warn);
                installPluginButton.Text = L.T("Install plugin");
                installPluginButton.Visible = report.HasBundled;
                break;
            case PluginInfo.Status.SameAsBundled:
                Ui.SetStatus(pluginStatus, L.F("installed, version {0} (the same as the launcher's)", VersionOrUnknown(report.InstalledVersion)), TextRole.Good);
                installPluginButton.Visible = false;
                break;
            case PluginInfo.Status.DifferentFromBundled:
                Ui.SetStatus(pluginStatus, L.F("installed, version {0}; the launcher has {1}", VersionOrUnknown(report.InstalledVersion), bundled), TextRole.Warn);
                installPluginButton.Text = L.T("Update plugin");
                installPluginButton.Visible = true;
                break;
            case PluginInfo.Status.InstalledNoBundle:
                Ui.SetStatus(pluginStatus, L.F("installed, version {0}", VersionOrUnknown(report.InstalledVersion)), TextRole.Good);
                installPluginButton.Visible = false;
                break;
        }
        installPluginButton.Enabled = !gameRunning;
    }

    private static string VersionOrUnknown(string version) => version.Length > 0 ? version : L.T("unknown");

    // ------------------------------------------------------------------ server list

    // Builds the list again (after a search, a new server, a change of the chosen one).
    private void FillServers()
    {
        updatingList = true;
        servers.BeginUpdate();
        try
        {
            servers.Items.Clear();
            ServerNetworkFile local = ServerNetworkFile.Read();
            servers.Items.Add(new ListViewItem(new[] { "", "localhost:" + local.HttpPort, "", "", L.T("this PC"), "" }));

            foreach (FoundServer server in context.Servers)
            {
                var item = new ListViewItem(new[]
                {
                    server.Name,
                    server.Address,
                    server.MaxPlayers > 0 ? server.Players + "/" + server.MaxPlayers : "",
                    server.PingMs >= 0 && server.Answering ? server.PingMs.ToString(CultureInfo.InvariantCulture) : "",
                    server.Network,
                    ServerStatusText(server)
                })
                {
                    Tag = server
                };
                servers.Items.Add(item);
            }
            UpdateThisPcRow();
            MarkTarget();
        }
        finally
        {
            servers.EndUpdate();
            updatingList = false;
        }

        if (context.Searching)
        {
            Ui.SetStatus(searchStatus, L.T("searching the local network and the VPNs..."), TextRole.Warn);
        }
        else if (context.LastSearch != DateTime.MinValue)
        {
            string networks = context.SearchedNetworks.Count == 0
                ? L.T("no network found")
                : string.Join(", ", context.SearchedNetworks.Select(n => n.ToString()));
            Ui.SetStatus(searchStatus, L.F("{0} server(s) found at {1} · searched on: {2}", context.Servers.Count(s => s.Answering),
                context.LastSearch.ToLocalTime().ToString("HH:mm:ss"), networks), TextRole.Muted);
        }
        else
        {
            Ui.SetStatus(searchStatus, L.T("not searched yet"), TextRole.Muted);
        }
        UpdateTarget();
    }

    private static string ServerStatusText(FoundServer server)
    {
        if (server.SeenAt == DateTime.MinValue) return L.T("not checked yet (Search again or Connect)");
        if (!server.Answering) return L.F("not answering (last answer at {0})", server.SeenAt.ToLocalTime().ToString("HH:mm"));
        var parts = new List<string> { L.T("online") };
        if (server.MaxPlayers > 0 && server.Players >= server.MaxPlayers) parts.Add(L.T("full"));
        if (server.PlayerNames.Count > 0) parts.Add(string.Join(", ", server.PlayerNames.Take(6)) + (server.PlayerNames.Count > 6 ? "..." : ""));
        if (!server.AcceptsPlayerName) parts.Add(L.T("older server: the name comes from the account"));
        if (server.Build.Length > 0 && server.Build != LauncherInfo.ExpectedGameBuild) parts.Add(L.F("game build {0}", server.Build));
        return string.Join(" · ", parts);
    }

    // First row: the server of this PC (the launcher's, or one started by "Run server.ps1").
    private void UpdateThisPcRow()
    {
        if (servers.Items.Count == 0) return;
        ListViewItem row = servers.Items[0];
        ServerNetworkFile local = ServerNetworkFile.Read();
        FoundServer? answer = context.ThisPcAnswer;
        bool up = context.Server.State == ServerState.Running || (!context.Server.IsActive && NetworkCheck.AnyServerListening());
        string players = up && answer != null && answer.MaxPlayers > 0 ? answer.Players + "/" + answer.MaxPlayers : "";

        string status;
        if (context.Server.State == ServerState.Running) status = L.F("running ({0} connection(s))", context.Server.ConnectionCount);
        else if (context.Server.State == ServerState.Starting) status = L.T("starting...");
        else if (context.Server.State == ServerState.Stopping) status = L.T("stopping...");
        else if (NetworkCheck.AnyServerListening()) status = L.T("running outside the launcher (Run server.ps1?)");
        else status = context.Settings.StartServerWithGame ? L.T("stopped (starts when you click Play)") : L.T("stopped");

        SetCell(row, 0, L.F("This PC: {0}", local.ServerName));
        SetCell(row, 1, "localhost:" + local.HttpPort);
        SetCell(row, 2, players);
        SetCell(row, 5, status);
    }

    private static void SetCell(ListViewItem row, int index, string text)
    {
        if (row.SubItems.Count > index && row.SubItems[index].Text != text) row.SubItems[index].Text = text;
    }

    // Bold row = the server Play joins (it is also the selected row).
    private void MarkTarget()
    {
        FoundServer? target = context.SelectedServer;
        foreach (ListViewItem item in servers.Items)
        {
            bool isTarget = item.Tag is FoundServer s ? target != null && s.Key == target.Key : target == null;
            item.Font = isTarget ? BoldFont : servers.Font;
            if (isTarget && !item.Selected) item.Selected = true;
        }
    }

    private void OnServerClicked()
    {
        if (updatingList || servers.SelectedItems.Count == 0) return;
        context.SelectServer(servers.SelectedItems[0].Tag as FoundServer);
    }

    // Label next to Play and the "start the server of this PC" option, which is off (and grayed) for another PC.
    private void UpdateTarget()
    {
        FoundServer? target = context.SelectedServer;
        string text = target == null ? L.T("Joins: this PC") : L.F("Joins: {0} ({1})", target.Name, target.Address);
        if (targetLabel.Text != text) targetLabel.Text = text;

        bool thisPc = target == null;
        updatingCheckbox = true;
        try
        {
            if (startServerWithGame.Enabled != thisPc) startServerWithGame.Enabled = thisPc;
            bool shown = thisPc && context.Settings.StartServerWithGame;
            if (startServerWithGame.Checked != shown) startServerWithGame.Checked = shown;
        }
        finally
        {
            updatingCheckbox = false;
        }
        forgetButton.Enabled = target != null && target.Manual;
    }

    private void ForgetSelected()
    {
        FoundServer? target = context.SelectedServer;
        if (target == null || !target.Manual) return;
        context.RemoveServer(target);
    }

    private async Task ConnectByIpAsync()
    {
        if (connecting) return;
        if (!ServerBrowser.TryParseAddress(ipBox.Text, out string host, out int port))
        {
            Ui.ShowError(FindForm(), L.T("Type the IP of the server PC, for example 26.10.20.30 (Radmin), 25.1.2.3 (Hamachi) or 192.168.0.15 (local network). Add :port when the server does not use 6443."));
            return;
        }

        connecting = true;
        RefreshStatus();
        context.ClientNotice(L.F("checking {0}:{1}...", host, port));
        try
        {
            ServerBrowser.ProbeResult result = await ServerBrowser.ProbeAsync(host, port, 5000, CancellationToken.None);
            if (result.Server == null)
            {
                context.ClientNotice(result.Error);
                Ui.ShowError(FindForm(), result.Error + "\n\n" + ConnectionHints());
                return;
            }
            FoundServer server = result.Server;
            context.AddManualServer(server);
            context.SelectServer(server);
            context.ClientNotice(L.F("server found: {0} ({1}), {2} player(s), {3} ms. Click Play to join.", server.Name, server.Address, server.Players, server.PingMs));
            ipBox.Text = "";
        }
        finally
        {
            connecting = false;
            RefreshStatus();
        }
    }

    private static string ConnectionHints() =>
        L.T("Check that:\n• the server is running on the other PC and accepts players of other PCs (Server tab > Network and multiplayer);\n• both PCs are on the same network or VPN (Radmin, Hamachi) and the VPN is connected;\n• the Windows Firewall of the server PC allows the server (Server tab > Network and multiplayer > Allow in the Windows Firewall);\n• on the internet, the router of the server forwards the ports (TCP 6443 and UDP 1111 to 1114 by default).");

    // ------------------------------------------------------------------ actions

    private void OpenDownload()
    {
        using var form = new DepotDownloadForm(context);
        form.ShowDialog(FindForm());
        nextPluginCheck = DateTime.MinValue;
        RefreshStatus();
    }

    private async Task PlayAsync()
    {
        if (launching) return;
        string folder = context.GameFolder;
        if (!GameInstall.HasGame(folder))
        {
            if (Ui.Confirm(FindForm(), L.F("Steambirds.exe was not found in:\n{0}\n\nDownload the game with Steam now?", folder))) OpenDownload();
            return;
        }
        if (context.Game.IsRunning(folder))
        {
            Ui.ShowInfo(FindForm(), L.T("The game is already open."));
            return;
        }
        if (!GameProcess.IsSteamRunning())
        {
            if (Ui.Confirm(FindForm(), L.T("Steam is not open, and the game logs in with the Steam account.\n\nOpen Steam now? After logging in, click Play again.")))
                OpenSteam();
            return;
        }

        launching = true;
        RefreshStatus();
        try
        {
            FoundServer? chosen = context.SelectedServer;
            GameTarget? target = chosen == null ? await PrepareThisPcAsync() : await PrepareOtherPcAsync(chosen);
            if (target == null) return;

            string locale = context.Settings.GameLocale;
            int processId = context.Game.Launch(folder, locale, target);
            context.ClientNotice(L.F("game open (process {0}): OZY_URL={1} Steambirds.exe {2}", processId, target.OzyUrl, GameProcess.Arguments(locale, target)));
            string who = target.PlayerName ?? L.T("(name of the account)");
            context.ClientNotice(target.IsThisPc
                ? L.F("joining the server of this PC as {0}", who)
                : L.F("joining {0} ({1}:{2}) as {3}", target.ServerName, target.Host, target.HttpPort, who));
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not open the game: {0}", e.Message));
        }
        finally
        {
            launching = false;
            RefreshStatus();
        }
    }

    // This PC: the server must be up before the game opens (the launcher's, or one started outside it).
    private async Task<GameTarget?> PrepareThisPcAsync()
    {
        if (!await EnsureServerAsync()) return null;

        ServerNetworkFile local = ServerNetworkFile.Read();
        ServerBrowser.ProbeResult probe = await ServerBrowser.ProbeAsync("localhost", local.HttpPort, 2500, CancellationToken.None);
        string name = context.EffectivePlayerName();
        bool sendsName = probe.Server != null && probe.Server.AcceptsPlayerName && name.Length > 0;
        if (probe.Server != null && !probe.Server.AcceptsPlayerName)
            context.ClientNotice(L.T("the server of this PC is an older version: it does not take the player name (the name comes from the account)"));
        return new GameTarget
        {
            IsThisPc = true,
            Host = "localhost",
            HttpPort = local.HttpPort,
            NexusPort = probe.Server != null && probe.Server.NexusPort > 0 ? probe.Server.NexusPort : local.NexusPort,
            PlayerName = sendsName ? name : null,
            ServerName = local.ServerName
        };
    }

    // Another PC: checks that it answers (and if it takes the player name) right before opening the game.
    private async Task<GameTarget?> PrepareOtherPcAsync(FoundServer chosen)
    {
        context.ClientNotice(L.F("checking {0} ({1})...", chosen.Name, chosen.Address));
        ServerBrowser.ProbeResult probe = await ServerBrowser.ProbeAsync(chosen.Host, chosen.HttpPort, 5000, CancellationToken.None);
        FoundServer server = chosen;
        if (probe.Server == null)
        {
            context.ClientNotice(probe.Error);
            if (!Ui.Confirm(FindForm(), probe.Error + "\n\n" + L.T("Open the game anyway?") + "\n\n" + ConnectionHints())) return null;
        }
        else
        {
            server = probe.Server;
            if (chosen.Network.Length > 0) server.Network = chosen.Network;
            context.UpdateServer(server);
            if (server.MaxPlayers > 0 && server.Players >= server.MaxPlayers &&
                !Ui.Confirm(FindForm(), L.F("{0} is full ({1}/{2} players). Open the game anyway?", server.Name, server.Players, server.MaxPlayers)))
                return null;
        }

        string name = context.EffectivePlayerName();
        return new GameTarget
        {
            IsThisPc = false,
            Host = server.Host,
            HttpPort = server.HttpPort,
            PlayerName = server.AcceptsPlayerName && name.Length > 0 ? name : null,
            ServerName = server.Name
        };
    }

    // The game logs in on the server of this PC: it must be up before the game opens.
    private async Task<bool> EnsureServerAsync()
    {
        if (context.Server.State == ServerState.Running) return true;
        if (!context.Server.IsActive && NetworkCheck.AnyServerListening())
        {
            context.ClientNotice(L.T("using the server already running on this PC (outside the launcher)"));
            return true;
        }

        if (!context.Server.IsActive)
        {
            if (!context.Settings.StartServerWithGame)
                return Ui.Confirm(FindForm(), L.T("The server of this PC is not running, and the game will not be able to log in.\n\nOpen the game anyway?"));
            context.ClientNotice(L.T("starting the server of this PC before the game..."));
            if (!context.StartServer()) return false;
        }

        context.ClientNotice(L.T("waiting for the server to be ready..."));
        if (await context.WaitForServerReady(30000)) return true;
        if (!context.Server.IsActive)
        {
            Ui.ShowError(FindForm(), L.T("The server closed while starting. See the console of the Server tab."));
            return false;
        }
        return Ui.Confirm(FindForm(), L.T("The server has not said it is ready yet. Open the game anyway?"));
    }

    private void OpenSteam()
    {
        try
        {
            GameProcess.OpenSteam();
            context.ClientNotice(L.T("opening Steam..."));
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not open Steam: {0}", e.Message));
        }
    }

    private void InstallPlugin()
    {
        string folder = context.GameFolder;
        if (context.Game.IsRunning(folder))
        {
            Ui.ShowError(FindForm(), L.T("Close the game before installing the plugin (the file is in use while the game runs)."));
            return;
        }
        try
        {
            PluginInfo.Install(folder);
            context.ClientNotice(L.F("plugin copied to {0}", GameInstall.InstalledPluginPath(folder)));
        }
        catch (UnauthorizedAccessException)
        {
            Ui.ShowError(FindForm(), L.F("No permission to write to:\n{0}\n\nOpen the launcher as administrator and try again.", GameInstall.InstalledPluginPath(folder)));
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not install the plugin: {0}", e.Message));
        }
        nextPluginCheck = DateTime.MinValue;
        RefreshStatus();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            context.GameFolderChanged -= OnGameFolderChanged;
            context.GameDataChanged -= RefreshStatus;
            context.ServersChanged -= FillServers;
            boldFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}
