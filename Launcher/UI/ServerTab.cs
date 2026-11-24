using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// "Server" tab: hosts the server of this PC for this PC and for players of other PCs,
// and edits its config files, its network settings (network.json) and the accounts of the players.
// The pages are grouped in a side navigation: what the server prints (Monitor), how the game is tuned (Game rules),
// who plays (Players), what is played on (World) and how others join (Connection).
// Features the server does not have yet (map preview...) are shown grayed out until they exist.
// Folders and files are opened from the Settings tab.
internal sealed class ServerTab : UserControl
{
    private readonly LauncherContext context;
    private readonly ThemedButton startButton;
    private readonly ThemedButton stopButton;
    private readonly ThemedButton restartButton;
    private readonly StatusPill statePill;
    private readonly Label portsLabel;
    private readonly System.Windows.Forms.Timer clock;

    public ServerTab(LauncherContext context)
    {
        this.context = context;
        ToolTip tip = context.ToolTip;
        Padding = new Padding(12, 10, 12, 0);

        // ------------------------------------------------------------ header
        startButton = Ui.Primary(L.T("Start server"), (_, _) => context.StartServer());
        stopButton = Ui.Button(L.T("Stop"), (_, _) => context.StopServer());
        restartButton = Ui.Button(L.T("Restart"), (_, _) => context.RestartServer());
        ThemedButton reportButton = Ui.Button(L.T("Write report now"), (_, _) => context.WriteReport());
        tip.SetToolTip(reportButton, L.T("Writes logs\\server-report.txt (server output, game logs, account.json and the .txt files of BepInEx)."));
        statePill = new StatusPill { Anchor = AnchorStyles.Right };
        portsLabel = Ui.Note("");
        portsLabel.Margin = new Padding(3, 2, 3, 0);

        FlowLayoutPanel buttons = Ui.Flow(startButton, stopButton, restartButton, reportButton);
        var top = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2 };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        top.Controls.Add(buttons, 0, 0);
        top.Controls.Add(statePill, 1, 0);
        top.Controls.Add(portsLabel, 0, 1);
        top.SetColumnSpan(portsLabel, 2);
        var header = new Card
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ContentPadding = new Padding(12, 8, 14, 8),
            Margin = Padding.Empty
        };
        header.Controls.Add(top);

        // ------------------------------------------------------------ console page
        var console = new ConsoleView(context.ServerConsole) { Dock = DockStyle.Fill };
        console.AddInfoToggle(context.Settings.ShowServerInfoLines, tip, on =>
        {
            context.Settings.ShowServerInfoLines = on;
            context.SaveSettings();
        });
        var commandBox = new ThemedTextBox { Width = 360, PlaceholderText = "/command", Margin = new Padding(3, 4, 3, 3) };
        ThemedButton sendButton = Ui.Button(L.T("Send"));
        Ui.Disable(commandBox, sendButton);
        FlowLayoutPanel commandRow = Ui.Flow(Ui.Text(L.T("Command:")), commandBox, sendButton, Ui.ComingSoon(tip,
            L.T("The server does not read commands from its console yet.\nThe admin commands (/xp, /dungeon, /items, /loot...) are typed in the game chat.")));
        commandRow.Dock = DockStyle.Bottom;
        var consolePage = new Panel { Dock = DockStyle.Fill };
        consolePage.Controls.Add(Ui.Framed(console));
        consolePage.Controls.Add(commandRow);

        // ------------------------------------------------------------ game rules
        var progressionPage = new SettingsPage(tip,
            L.T("Pilot, planes and skills (progression.json). Can be changed with the server running: it reads the file again by itself."),
            new SettingsSection(L.T("Progression"), "progression.json", SettingsSection.AllExcept("startingPilotLevel", "startingUnlocks")));

        var itemsPage = new SettingsPage(tip,
            L.T("Loot, cargo, storage, reforge and what is lost when a plane goes down (items.json). The loot tables are in server\\loot_tables. Can be changed with the server running."),
            new SettingsSection(L.T("Items and loot"), "items.json",
                SettingsSection.AllExcept("startingCurrencies", "startingIngredients", "startingStorageItems", "dungeonDifficulty")));
        itemsPage.AddToolbarButton(L.T("Edit loot tables"), (_, _) => Ui.OpenFolder(FindForm(), LauncherPaths.ServerFile("loot_tables")));

        var difficultyPage = new SettingsPage(tip,
            L.T("Enemy health and loot difficulty. The enemies of each dungeon also have their own health multiplier, in the Maps and dungeons page. The enemy types and encounters of the tutorial and the Nexus are in enemies.json (Settings tab, Files)."),
            new SettingsSection(L.T("Enemies"), "enemies.json", SettingsSection.Only("globalHealthMultiplier", "chatWarnings")),
            new SettingsSection(L.T("Dungeon loot"), "items.json", SettingsSection.Only("dungeonDifficulty")));

        // ------------------------------------------------------------ pages in the side navigation
        var nav = new NavPages { Dock = DockStyle.Fill };
        nav.AddGroup(L.T("Monitor"));
        nav.AddPage(L.T("Console"), Pad(consolePage), Icons.Console);
        nav.AddPage(L.T("Chat"), Pad(new ChatPage(context)), Icons.Chat);
        nav.AddGroup(L.T("Game rules"));
        nav.AddPage(L.T("Progression"), Pad(progressionPage), Icons.Progression);
        nav.AddPage(L.T("Items and loot"), Pad(itemsPage), Icons.Items);
        nav.AddPage(L.T("Enemies and difficulty"), Pad(difficultyPage), Icons.Enemies);
        nav.AddPage(L.T("Store"), Pad(new StorePage(context)), Icons.Store);
        nav.AddGroup(L.T("Players"));
        nav.AddPage(L.T("Accounts"), Pad(new AccountsPage(context)), Icons.Account);
        nav.AddPage(L.T("New accounts"), Pad(new NewAccountsPage(context)), Icons.NewAccount);
        nav.AddPage(L.T("Friends and flocks"), Pad(new SocialPage(context)), Icons.Friends);
        nav.AddGroup(L.T("World"));
        nav.AddPage(L.T("Maps and dungeons"), Pad(new MapsPage(context)), Icons.Maps);
        nav.AddPage(L.T("World map"), Pad(new WorldPage(context)), Icons.World);
        nav.AddGroup(L.T("Connection"));
        nav.AddPage(L.T("Network and multiplayer"), Pad(new NetworkPage(context)), Icons.Network);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 10, 0, 0) };
        body.Controls.Add(nav);

        Controls.Add(body);
        Controls.Add(header);

        context.ServerStateChanged += UpdateState;
        clock = new System.Windows.Forms.Timer { Interval = 1000 };
        clock.Tick += (_, _) => UpdateState();
        clock.Start();
        UpdateState();
    }

    // Space around a page, inside the side navigation.
    private static Control Pad(Control content)
    {
        var page = new Panel { Padding = new Padding(18, 4, 4, 12) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private void UpdateState()
    {
        ServerProcess server = context.Server;
        string text;
        TextRole role;
        switch (server.State)
        {
            case ServerState.Starting:
                text = L.T("Starting...");
                role = TextRole.Warn;
                break;
            case ServerState.Running:
                TimeSpan uptime = server.StartedAt.HasValue ? DateTime.Now - server.StartedAt.Value : TimeSpan.Zero;
                text = L.F("Running for {0}  ·  open connections: {1}", uptime.ToString(@"hh\:mm\:ss"), server.ConnectionCount);
                role = TextRole.Good;
                break;
            case ServerState.Stopping:
                text = L.T("Stopping...");
                role = TextRole.Warn;
                break;
            default:
                if (NetworkCheck.AnyServerListening())
                {
                    text = L.T("Stopped (another server is running on this PC, outside the launcher)");
                    role = TextRole.Warn;
                }
                else
                {
                    text = server.LastExitCode.HasValue && server.LastExitCode.Value != 0
                        ? L.F("Stopped (the server closed with code {0})", server.LastExitCode.Value)
                        : L.T("Stopped");
                    role = TextRole.Muted;
                }
                break;
        }

        statePill.Set(text, role);
        ServerNetworkFile network = ServerNetworkFile.Read();
        string ports = L.F("{0}: {1}", network.ServerName, network.PortsDescription) + "  ·  " +
                       (network.AllowOtherPlayers ? L.T("accepts players of other PCs") : L.T("only this PC"));
        if (portsLabel.Text != ports) portsLabel.Text = ports;
        startButton.Enabled = server.State == ServerState.Stopped;
        stopButton.Enabled = server.State == ServerState.Starting || server.State == ServerState.Running;
        restartButton.Enabled = server.State == ServerState.Running;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            context.ServerStateChanged -= UpdateState;
            clock.Stop();
            clock.Dispose();
        }
        base.Dispose(disposing);
    }
}
