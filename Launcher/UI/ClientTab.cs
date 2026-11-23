using System.Globalization;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Core.GameData;

namespace SteambirdsLauncher.UI;

// "Client" tab. Three sub-tabs and a bar that stays in view:
//  - Servers: the server list (this PC, the servers found on the local network and on VPNs like Radmin and Hamachi,
//    and the ones typed in "Connect by IP").
//  - Game: the game install (and its download with Steam, only when it is missing), the Quieter plugin and the
//    player name.
//  - Console: the game logs (BepInEx\LogOutput.log and the Unity Player.log).
//  - Play bar (always visible): where Play joins, the "start the server with the game" option, and the Play button.
// The game folder and the game language are chosen in the Settings tab.
internal sealed class ClientTab : UserControl
{
    private readonly LauncherContext context;
    private readonly EllipsisLabel folderLabel;
    private readonly StatusPill gamePill;
    private readonly Label gameNote;
    private readonly Label languageLabel;
    private readonly StatusPill steamPill;
    private readonly ThemedButton openSteamButton;
    private readonly StatusPill pluginPill;
    private readonly Label pluginNote;
    private readonly ThemedButton installPluginButton;
    private readonly ThemedButton downloadButton;
    private readonly Control downloadRow;
    private readonly ThemedTextBox playerName;
    private readonly Label steamAccountLabel;
    private readonly ThemedListView servers;
    private readonly Label searchStatus;
    private readonly ThemedButton searchButton;
    private readonly ThemedTextBox ipBox;
    private readonly ThemedButton connectButton;
    private readonly ThemedButton forgetButton;
    private readonly Label targetLabel;
    private readonly Label hintLabel;
    private readonly ThemedCheckBox startServerWithGame;
    private readonly ThemedButton playButton;
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
        Padding = new Padding(12, 6, 12, 12);

        // ------------------------------------------------------------ game page
        folderLabel = new EllipsisLabel { Dock = DockStyle.Fill };
        Ui.SetRole(folderLabel, TextRole.Muted);
        gamePill = new StatusPill();
        gameNote = Ui.Note("");
        gameNote.Margin = new Padding(6, 7, 3, 3);
        languageLabel = Ui.Text("");
        steamPill = new StatusPill();
        openSteamButton = Ui.Button(L.T("Open Steam"), (_, _) => OpenSteam());
        ThemedButton changeFolder = Ui.Button(L.T("Change..."), (_, _) => context.OpenSettingsTab?.Invoke());
        ThemedButton changeLanguage = Ui.Button(L.T("Change..."), (_, _) => context.OpenSettingsTab?.Invoke());

        downloadButton = Ui.Primary(L.T("Download the game with Steam"), (_, _) => OpenDownload());
        tip.SetToolTip(downloadButton, L.F("Copies the command\n{0}\nopens the Steam console and follows the download.", LauncherInfo.DepotCommand));
        downloadRow = Ui.Flow(downloadButton);
        downloadRow.Margin = new Padding(0, 10, 0, 0);

        TableLayoutPanel gameTable = Ui.Table(3);
        gameTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gameTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        gameTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Ui.AddRow(gameTable, Ui.Note(L.T("Game folder:")), folderLabel, changeFolder);
        Control statusFlow = Ui.Flow(gamePill, gameNote);
        Ui.AddRow(gameTable, Ui.Note(L.T("Status:")), statusFlow);
        gameTable.SetColumnSpan(statusFlow, 2);
        Control steamFlow = Ui.Flow(steamPill, openSteamButton);
        Ui.AddRow(gameTable, Ui.Note(L.T("Steam:")), steamFlow);
        gameTable.SetColumnSpan(steamFlow, 2);
        Ui.AddRow(gameTable, Ui.Note(L.T("Game language:")), languageLabel, changeLanguage);
        Ui.AddRow(gameTable, null, downloadRow);
        gameTable.SetColumnSpan(downloadRow, 2);
        Card gameCard = Ui.Group(L.T("Game"), gameTable);

        // The Quieter plugin: what it is goes behind the "?".
        pluginPill = new StatusPill();
        pluginNote = Ui.Note("");
        installPluginButton = Ui.Button(L.T("Install plugin"), (_, _) => InstallPlugin());
        HelpButton pluginHelp = Ui.Help(L.T("What it does: a small mod (BepInEx) that runs inside your game. It fixes client bugs (the Flock Hall building invisible in Rebel City, and the death window showing the numbers of the previous death from the second death on), hides the noise of the game logs and turns off the telemetry errors. Without it: the game still opens and you can play, but with those bugs and with a Player.log full of noise. A PC that only joins other people's servers loses nothing else. The server does not need it: its data tables ship in server\\data."),
            L.T("Quieter plugin"));
        pluginHelp.Margin = new Padding(8, 5, 3, 3);
        TableLayoutPanel pluginTable = Ui.Table(1);
        pluginTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(pluginTable, Ui.WrapFlow(pluginPill, installPluginButton, pluginHelp));
        Ui.AddRow(pluginTable, pluginNote);
        pluginNote.Margin = new Padding(3, 0, 3, 3);
        Card pluginCard = Ui.Group(L.T("Quieter plugin"), pluginTable);

        // The player.
        playerName = new ThemedTextBox { Text = context.Settings.PlayerName, Width = 240, MaxLength = 24, Margin = new Padding(3, 4, 3, 3) };
        playerName.TextChanged += (_, _) =>
        {
            context.Settings.PlayerName = playerName.Text.Trim();
            context.SaveSettings();
        };
        steamAccountLabel = Ui.Note("");
        steamAccountLabel.Margin = new Padding(3, 0, 3, 3);
        HelpButton nameHelp = Ui.Help(L.T("Empty = the name of your Steam account. Your account on each server is your Steam account: changing the name keeps your pilot and your planes."),
            L.T("Player name"));
        TableLayoutPanel playerTable = Ui.Table(1);
        playerTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(playerTable, Ui.Flow(Ui.Note(L.T("Player name:")), playerName, nameHelp));
        Ui.AddRow(playerTable, steamAccountLabel);
        Card playerCard = Ui.Group(L.T("Player"), playerTable);

        // Game on the left; plugin and player stacked on the right.
        var rightColumn = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true, Margin = Padding.Empty };
        rightColumn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(rightColumn, pluginCard);
        Ui.AddRow(rightColumn, playerCard);
        var gameLayout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1 };
        gameLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        gameLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        gameCard.Margin = new Padding(0, 0, 12, 0);
        gameLayout.Controls.Add(gameCard, 0, 0);
        gameLayout.Controls.Add(rightColumn, 1, 0);
        var gamePage = new TabPage(L.T("Game")) { Padding = new Padding(2, 14, 2, 4), AutoScroll = true };
        gamePage.Controls.Add(gameLayout);

        // ------------------------------------------------------------ servers page
        servers = Ui.Grid((L.T("Server"), 220), (L.T("Address"), 160), (L.T("Players"), 70), (L.T("Ping (ms)"), 70),
            (L.T("Network"), 110), (L.T("Status"), 330));
        servers.SelectedIndexChanged += (_, _) => OnServerClicked();
        servers.DoubleClick += async (_, _) => await PlayAsync();

        searchButton = Ui.Button(L.T("Search again"), async (_, _) => await context.SearchServersAsync());
        var autoSearch = new ThemedCheckBox
        {
            Text = L.T("Search by itself"),
            Checked = context.Settings.AutoSearch,
            Margin = new Padding(10, 6, 3, 3)
        };
        autoSearch.CheckedChanged += (_, _) =>
        {
            context.Settings.AutoSearch = autoSearch.Checked;
            context.SaveSettings();
        };
        tip.SetToolTip(autoSearch, L.T("Searches the local network and the VPNs (Radmin, Hamachi...) when the launcher opens and every minute while this tab is open."));
        searchStatus = Ui.Note("");
        searchStatus.Margin = new Padding(6, 6, 3, 3);

        ipBox = new ThemedTextBox { Width = 230, PlaceholderText = L.T("e.g. 26.10.20.30 or 26.10.20.30:6443"), Margin = new Padding(3, 4, 3, 3) };
        ipBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await ConnectByIpAsync();
        };
        connectButton = Ui.Button(L.T("Connect"), async (_, _) => await ConnectByIpAsync());
        forgetButton = Ui.Danger(L.T("Remove from list"), (_, _) => ForgetSelected());
        tip.SetToolTip(ipBox, L.T("IP of the server PC (local network, Radmin, Hamachi or internet) and, when it is not 6443, the login port of the server."));
        HelpButton serversHelp = Ui.Help(L.T("This PC, the servers of the local network and of VPNs like Radmin and Hamachi show up here by themselves. A server that is not found is joined with Connect by IP: type the IP of the server PC and, when it is not 6443, the login port after a colon."),
            L.T("Servers"));

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 0, 0, 8) };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        FlowLayoutPanel left = Ui.WrapFlow(searchButton, autoSearch);
        left.Anchor = AnchorStyles.Left;
        FlowLayoutPanel right = Ui.Flow(Ui.Note(L.T("Connect by IP:")), ipBox, connectButton, serversHelp);
        right.Anchor = AnchorStyles.Right;
        toolbar.Controls.Add(left, 0, 0);
        toolbar.Controls.Add(right, 1, 0);

        var footer = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 8, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchStatus.Anchor = AnchorStyles.Left;
        footer.Controls.Add(searchStatus, 0, 0);
        footer.Controls.Add(forgetButton, 1, 0);

        var serversPage = new TabPage(L.T("Servers")) { Padding = new Padding(2, 14, 2, 4) };
        serversPage.Controls.Add(Ui.Framed(servers));
        serversPage.Controls.Add(footer);
        serversPage.Controls.Add(toolbar);

        // ------------------------------------------------------------ console page
        var console = new ConsoleView(context.ClientConsole) { Dock = DockStyle.Fill };
        var hideNoise = new ThemedCheckBox
        {
            Text = L.T("Hide noise (HarmonyX IL, missing scripts)"),
            Checked = context.Settings.HideClientLogNoise,
            Margin = new Padding(12, 6, 3, 3)
        };
        hideNoise.CheckedChanged += (_, _) =>
        {
            context.Settings.HideClientLogNoise = hideNoise.Checked;
            context.SaveSettings();
        };
        console.Toolbar.Controls.Add(hideNoise);
        var consolePage = new TabPage(L.T("Console")) { Padding = new Padding(2, 14, 2, 4) };
        consolePage.Controls.Add(Ui.Framed(console));

        var subTabs = new ThemedTabControl { Dock = DockStyle.Fill };
        subTabs.TabPages.AddRange(new[] { serversPage, gamePage, consolePage });
        subTabs.SetIcon(serversPage, Icons.Servers);
        subTabs.SetIcon(gamePage, Icons.Game);
        subTabs.SetIcon(consolePage, Icons.Console);

        // ------------------------------------------------------------ play bar
        targetLabel = new Label { AutoSize = true, Font = Theme.Heading(12.5f), Margin = new Padding(3, 0, 3, 0) };
        hintLabel = Ui.Note("");
        hintLabel.Margin = new Padding(3, 2, 3, 0);
        startServerWithGame = new ThemedCheckBox
        {
            Text = L.T("Start the server of this PC with the game"),
            Checked = context.Settings.StartServerWithGame,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 3, 22, 3)
        };
        startServerWithGame.CheckedChanged += (_, _) =>
        {
            if (updatingCheckbox) return;
            context.Settings.StartServerWithGame = startServerWithGame.Checked;
            context.SaveSettings();
        };
        tip.SetToolTip(startServerWithGame, L.T("Only for the server of this PC: it turns off by itself while another PC is chosen in the list."));
        playButton = Ui.Primary(L.T("PLAY"));
        playButton.AutoSize = false;
        playButton.TextPoints = 14f;
        playButton.Size = new Size(210, 52);
        playButton.Margin = new Padding(3);
        playButton.Anchor = AnchorStyles.Right;
        playButton.Click += async (_, _) => await PlayAsync();

        var textBlock = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        textBlock.Controls.Add(targetLabel);
        textBlock.Controls.Add(hintLabel);
        ThemedButton discordButton = Ui.Quiet(L.T("Discord"), (_, _) => Ui.OpenUrl(FindForm(), LauncherInfo.DiscordUrl));
        discordButton.Anchor = AnchorStyles.Right;
        tip.SetToolTip(discordButton, L.T("Opens the invite to the Steambirds Alliance Revive community on Discord."));
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        bar.Controls.Add(textBlock, 0, 0);
        bar.Controls.Add(startServerWithGame, 1, 0);
        bar.Controls.Add(discordButton, 2, 0);
        bar.Controls.Add(playButton, 3, 0);
        var playCard = new Card { Dock = DockStyle.Fill, Margin = Padding.Empty, ContentPadding = new Padding(16, 10, 12, 10) };
        playCard.Controls.Add(bar);
        var playHost = new Panel { Dock = DockStyle.Bottom, Height = 94, Padding = new Padding(0, 12, 0, 0) };
        playHost.Controls.Add(playCard);

        Controls.Add(subTabs);
        Controls.Add(playHost);

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

        string folderText = folder.Length > 0 ? folder : L.T("(not chosen)");
        if (folderLabel.Text != folderText)
        {
            folderLabel.Text = folderText;
            context.ToolTip.SetToolTip(folderLabel, folderText);
        }

        bool wrongBuild = false;
        if (!hasGame)
        {
            gamePill.Set(L.T("Not found"), TextRole.Bad);
            Ui.SetStatus(gameNote, L.T("Steambirds.exe was not found in this folder. Use \"Download the game with Steam\" below."), TextRole.Muted);
        }
        else
        {
            string build = GameInstall.ReadBuildId(folder);
            wrongBuild = build.Length > 0 && build != LauncherInfo.ExpectedGameBuild;
            gamePill.Set(L.F("Steambirds.exe found, build {0}", build.Length > 0 ? build : L.T("unknown")), wrongBuild ? TextRole.Warn : TextRole.Good);
            string note = wrongBuild ? L.F("(the server was made for {0})", LauncherInfo.ExpectedGameBuild) : "";
            if (gameId.HasValue) note += (note.Length > 0 ? " " : "") + L.F("game open (process {0})", gameId.Value);
            Ui.SetStatus(gameNote, note, TextRole.Muted);
        }
        // The download is only offered while the game is missing or is not the build the server was made for.
        bool offerDownload = !hasGame || wrongBuild;
        if (downloadRow.Visible != offerDownload) downloadRow.Visible = offerDownload;

        string locale = context.Settings.GameLocale;
        GameConfig.Locale? chosen = context.GameData.Locales.FirstOrDefault(l => l.Id == locale);
        string languageText = locale.Length == 0 ? L.T("chosen in the game") : (chosen?.ToString() ?? locale);
        if (languageLabel.Text != languageText) languageLabel.Text = languageText;

        bool steam = GameProcess.IsSteamRunning();
        steamPill.Set(steam ? L.T("open") : L.T("closed: open Steam and log in before playing"), steam ? TextRole.Good : TextRole.Bad);
        if (openSteamButton.Visible == steam) openSteamButton.Visible = !steam;

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

        // What stops Play, in one line next to it.
        string hint = "";
        TextRole hintRole = TextRole.Muted;
        if (!hasGame)
        {
            hint = L.T("Game not found: see the Game tab.");
            hintRole = TextRole.Bad;
        }
        else if (!steam && !gameId.HasValue)
        {
            hint = L.T("Steam is closed: open Steam and log in before playing.");
            hintRole = TextRole.Warn;
        }
        Ui.SetStatus(hintLabel, hint, hintRole);

        // Search by itself every minute while this tab is on screen.
        if (context.Settings.AutoSearch && Visible && !context.Searching && DateTime.UtcNow - context.LastSearch > TimeSpan.FromSeconds(60))
            _ = context.SearchServersAsync();

        playButton.Enabled = !launching && hasGame && !gameId.HasValue;
        string playText = gameId.HasValue ? L.T("GAME OPEN") : launching ? L.T("OPENING...") : L.T("PLAY");
        if (playButton.Text != playText) playButton.Text = playText;
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
                pluginPill.Set("—", TextRole.Muted);
                Ui.SetStatus(pluginNote, "", TextRole.Muted);
                installPluginButton.Visible = false;
                break;
            case PluginInfo.Status.NoBepInEx:
                pluginPill.Set(L.T("BepInEx missing"), TextRole.Warn);
                Ui.SetStatus(pluginNote, L.T("BepInEx is not installed in the game: it runs without the plugin, but without its fixes."), TextRole.Muted);
                installPluginButton.Visible = false;
                break;
            case PluginInfo.Status.NotInstalled:
                pluginPill.Set(L.T("Not installed"), TextRole.Warn);
                Ui.SetStatus(pluginNote, report.HasBundled ? L.F("The launcher has version {0}.", bundled) : L.T("The launcher's plugin folder is empty."), TextRole.Muted);
                installPluginButton.Text = L.T("Install plugin");
                installPluginButton.Visible = report.HasBundled;
                break;
            case PluginInfo.Status.SameAsBundled:
                pluginPill.Set(L.T("Installed"), TextRole.Good);
                Ui.SetStatus(pluginNote, L.F("Version {0}, the same as the launcher's.", VersionOrUnknown(report.InstalledVersion)), TextRole.Muted);
                installPluginButton.Visible = false;
                break;
            case PluginInfo.Status.DifferentFromBundled:
                pluginPill.Set(L.T("Update available"), TextRole.Warn);
                Ui.SetStatus(pluginNote, L.F("Version {0} installed; the launcher has {1}.", VersionOrUnknown(report.InstalledVersion), bundled), TextRole.Muted);
                installPluginButton.Text = L.T("Update plugin");
                installPluginButton.Visible = true;
                break;
            case PluginInfo.Status.InstalledNoBundle:
                pluginPill.Set(L.T("Installed"), TextRole.Good);
                Ui.SetStatus(pluginNote, L.F("Version {0}.", VersionOrUnknown(report.InstalledVersion)), TextRole.Muted);
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

    // First row: the server of this PC (the launcher's, or one started outside it).
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
        else if (NetworkCheck.AnyServerListening()) status = L.T("running outside the launcher");
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
