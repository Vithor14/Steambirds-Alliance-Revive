using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// "Server" tab: hosts the server of this PC (what "Run server.ps1" did) for this PC and for players of other PCs,
// and edits its config files, its network settings (network.json) and the accounts of the players.
// Features the server does not have yet (world map, map preview...) are shown grayed out until they exist.
// Folders and files are opened from the Settings tab.
internal sealed class ServerTab : UserControl
{
    private readonly LauncherContext context;
    private readonly Button startButton;
    private readonly Button stopButton;
    private readonly Button restartButton;
    private readonly Label stateDot;
    private readonly Label stateText;
    private readonly Label portsLabel;
    private readonly System.Windows.Forms.Timer clock;

    public ServerTab(LauncherContext context)
    {
        this.context = context;
        ToolTip tip = context.ToolTip;

        // ------------------------------------------------------------ header
        startButton = Ui.Button(L.T("Start server"), (_, _) => context.StartServer());
        stopButton = Ui.Button(L.T("Stop"), (_, _) => context.StopServer());
        restartButton = Ui.Button(L.T("Restart"), (_, _) => context.RestartServer());
        var reportButton = Ui.Button(L.T("Write report now"), (_, _) => context.WriteReport());
        tip.SetToolTip(reportButton, L.T("Writes logs\\server-report.txt (server output, game logs, account.json and the .txt files of BepInEx), like Run server.ps1 did on Ctrl+C."));
        stateDot = new Label { Text = "●", AutoSize = true, Font = new Font(Control.DefaultFont.FontFamily, 13f), Margin = new Padding(12, 1, 0, 0) };
        stateText = Ui.Bold("");
        stateText.Margin = new Padding(0, 7, 3, 3);

        var header = Ui.WrapFlow(startButton, stopButton, restartButton, reportButton, stateDot, stateText);
        portsLabel = Ui.Note("");

        var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Padding = new Padding(0, 0, 0, 4) };
        Ui.AddRow(top, header);
        Ui.AddRow(top, portsLabel);

        // ------------------------------------------------------------ console page
        var console = new ConsoleView(context.ServerConsole) { Dock = DockStyle.Fill };
        var commandBox = new TextBox { Width = 360, PlaceholderText = "/command", Margin = new Padding(3, 4, 3, 3) };
        var sendButton = Ui.Button(L.T("Send"));
        Ui.Disable(commandBox, sendButton);
        var commandRow = Ui.Flow(Ui.Text(L.T("Command:")), commandBox, sendButton, Ui.ComingSoon(tip,
            L.T("The server does not read commands from its console yet.\nThe admin commands (/xp, /dungeon, /items, /loot...) are typed in the game chat.")));
        commandRow.Dock = DockStyle.Bottom;
        var consolePage = new TabPage(L.T("Console")) { Padding = new Padding(4) };
        consolePage.Controls.Add(console);
        consolePage.Controls.Add(commandRow);

        // ------------------------------------------------------------ settings pages
        var progressionPage = Page(L.T("Progression"), new SettingsPage(tip,
            L.T("Pilot, planes and skills (progression.json). Can be changed with the server running: it reads the file again by itself."),
            new SettingsSection(L.T("Progression"), "progression.json", SettingsSection.AllExcept("startingPilotLevel", "startingUnlocks"))));

        var itemsSettings = new SettingsPage(tip,
            L.T("Loot, cargo, storage, reforge and what is lost when a plane goes down (items.json). The loot tables are in server\\loot_tables. Can be changed with the server running."),
            new SettingsSection(L.T("Items and loot"), "items.json",
                SettingsSection.AllExcept("startingCurrencies", "startingIngredients", "startingStorageItems", "dungeonDifficulty")));
        itemsSettings.AddToolbarButton(L.T("Edit loot tables"), (_, _) => Ui.OpenFolder(FindForm(), LauncherPaths.ServerFile("loot_tables")));
        var itemsPage = Page(L.T("Items and loot"), itemsSettings);

        var storesPage = Page(L.T("Store"), new StorePage(context));

        var difficultyPage = Page(L.T("Enemies and difficulty"), new SettingsPage(tip,
            L.T("Enemy health and loot difficulty. The enemies of each dungeon also have their own health multiplier, in the Maps and dungeons tab. The enemy types and encounters of the tutorial and the Nexus are in enemies.json (Settings tab, Files)."),
            new SettingsSection(L.T("Enemies"), "enemies.json", SettingsSection.Only("globalHealthMultiplier", "chatWarnings")),
            new SettingsSection(L.T("Dungeon loot"), "items.json", SettingsSection.Only("dungeonDifficulty"))));

        var accountsPage = Page(L.T("Accounts"), new AccountsPage(context));
        var mapsPage = Page(L.T("Maps and dungeons"), new MapsPage(tip));

        var networkPage = Page(L.T("Network and multiplayer"), new NetworkPage(context));
        var worldPage = new TabPage(L.T("World map"));
        ThemedTabControl.MarkNotImplemented(worldPage,
            L.T("Coming soon: world map with the position of each player. The open world (islands) does not exist in the server yet."));

        var pages = new ThemedTabControl { Dock = DockStyle.Fill };
        pages.TabPages.AddRange(new[] { consolePage, progressionPage, itemsPage, storesPage, difficultyPage, accountsPage, mapsPage, networkPage, worldPage });

        Controls.Add(pages);
        Controls.Add(top);

        context.ServerStateChanged += UpdateState;
        clock = new System.Windows.Forms.Timer { Interval = 1000 };
        clock.Tick += (_, _) => UpdateState();
        clock.Start();
        UpdateState();
    }

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(6) };
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

        Ui.SetStatus(stateText, text, role);
        ServerNetworkFile network = ServerNetworkFile.Read();
        string ports = L.F("{0}: {1}", network.ServerName, network.PortsDescription) + "  ·  " +
                       (network.AllowOtherPlayers ? L.T("accepts players of other PCs") : L.T("only this PC"));
        if (portsLabel.Text != ports) portsLabel.Text = ports;
        if (stateDot.ForeColor != Theme.ColorOf(role)) Ui.SetRole(stateDot, role);
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
