using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.UI;

// Main window: Client, Server, Settings and Map editor tabs and a status bar.
// Changing the launcher language builds the tabs again; the server, the game and the consoles keep running
// (they live in LauncherContext).
internal sealed class MainForm : Form
{
    private readonly LauncherContext context;
    private readonly ToolStripStatusLabel serverStatus;
    private readonly ToolStripStatusLabel gameStatus;
    private readonly ToolStripStatusLabel folderStatus;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly string? settingsWarning;
    private readonly string? languageWarning;
    private ThemedTabControl? tabs;
    private ClientTab? clientTab;

    // The map editor keeps its map (and the undo history) when the launcher builds the tabs again (new language).
    private MapEditorTab? editorTab;
    private MapSession? editorSession;

    public MainForm()
    {
        LauncherSettings settings = LauncherSettings.Load(out settingsWarning);
        if (string.IsNullOrWhiteSpace(settings.GameFolder)) settings.GameFolder = GameInstall.DetectFolder();
        L.Load(settings.Language, out languageWarning);
        Theme.Set(settings.DarkTheme);

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = LauncherInfo.Title + "  " + LauncherInfo.Version;
        MinimumSize = new Size(960, 640);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? Application.ExecutablePath);
        }
        catch
        {
            // Default icon.
        }

        context = new LauncherContext(this, settings);

        // No game in the game folder: ask how to get it before the main window opens.
        if (!GameInstall.HasGame(settings.GameFolder))
        {
            using var setup = new GameSetupForm(context);
            setup.ShowDialog();
        }
        context.LoadGameDataNow();
        context.OpenSettingsTab = () =>
        {
            if (tabs != null && tabs.TabCount > 2) tabs.SelectedIndex = 2;
        };

        serverStatus = new ToolStripStatusLabel();
        gameStatus = new ToolStripStatusLabel();
        folderStatus = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight, Tag = TextRole.Muted };
        var statusStrip = new StatusStrip();
        statusStrip.Items.AddRange(new ToolStripItem[] { serverStatus, new ToolStripSeparator(), gameStatus, folderStatus });
        Controls.Add(statusStrip);

        BuildTabs(settings.SelectedTab);
        ResumeLayout(false);
        PerformLayout();

        RestoreWindow();
        Theme.AttachWindow(this);

        refreshTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        refreshTimer.Tick += (_, _) => RefreshStatus();
        refreshTimer.Start();
        RefreshStatus();

        context.ClientNotice(L.F("launcher {0} in {1}", LauncherInfo.Version, LauncherPaths.BaseFolder));
        context.ClientNotice(L.T("this console shows the new lines of the BepInEx LogOutput.log and of the Unity Player.log while the game runs"));
        context.ServerNotice(L.F("server: {0}", LauncherPaths.ServerExe) + (File.Exists(LauncherPaths.ServerExe) ? "" : " " + L.T("(NOT FOUND: run Build launcher.ps1)")));
        context.ServerNotice(L.F("server data (maps, settings, accounts): {0}", LauncherPaths.ServerFolder));
        if (!context.GameData.Loaded) context.ClientNotice(L.F("game data not read ({0}): the lists of classes, currencies and game languages use the built-in values", context.GameData.Error));

        Shown += (_, _) =>
        {
            if (settingsWarning != null) Ui.ShowError(this, settingsWarning);
            if (languageWarning != null) Ui.ShowError(this, languageWarning);
            if (!File.Exists(LauncherPaths.ServerExe))
                Ui.ShowError(this, L.F("The server folder was not found next to the launcher:\n{0}\n\nRun \"Build launcher.ps1\" in the project folder to build the full launcher.", LauncherPaths.ServerExe));
        };
        FormClosing += HandleClosing;
    }

    // Builds (or builds again) the tabs in the current language.
    private void BuildTabs(int selected)
    {
        ThemedTabControl? old = tabs;
        SuspendLayout();
        try
        {
            var clientPage = new TabPage(L.T("Client")) { Padding = new Padding(4) };
            clientTab = new ClientTab(context) { Dock = DockStyle.Fill };
            clientPage.Controls.Add(clientTab);

            var serverPage = new TabPage(L.T("Server")) { Padding = new Padding(4) };
            serverPage.Controls.Add(new ServerTab(context) { Dock = DockStyle.Fill });

            var settingsPage = new TabPage(L.T("Settings")) { Padding = new Padding(4) };
            settingsPage.Controls.Add(new SettingsTab(context, OnLanguageChanged) { Dock = DockStyle.Fill });

            var editorPage = new TabPage(L.T("Map editor")) { Padding = new Padding(2) };
            editorPage.Controls.Add(BuildEditor());

            var newTabs = new ThemedTabControl { Dock = DockStyle.Fill, Padding = new Point(18, 5) };
            newTabs.TabPages.AddRange(new[] { clientPage, serverPage, settingsPage, editorPage });
            if (selected >= 0 && selected < 4) newTabs.SelectedIndex = selected;

            Controls.Add(newTabs);
            newTabs.BringToFront();
            tabs = newTabs;
            if (old != null)
            {
                Controls.Remove(old);
                old.Dispose();
            }
        }
        finally
        {
            ResumeLayout(true);
        }
        Text = LauncherInfo.Title + "  " + LauncherInfo.Version;
        folderStatus.Text = L.F("Launcher folder: {0}", LauncherPaths.BaseFolder);
        serverStatus.Text = "";
        gameStatus.Text = "";
    }

    // The map editor is the newest part of the launcher. If it fails to open, the error goes to
    // logs\launcher-errors.txt and is shown inside its tab, but the Client, Server and Settings tabs still open.
    private Control BuildEditor()
    {
        try
        {
            editorTab = new MapEditorTab(context, editorSession) { Dock = DockStyle.Fill };
            editorSession = editorTab.Session;
            return editorTab;
        }
        catch (Exception error)
        {
            editorTab = null;
            CrashLog.Report(error, showDialog: false);
            return new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                Tag = TextRole.Bad,
                Text = L.F("The map editor could not open:\n\n{0}\n\nThe details were saved in {1}.", error.Message, LauncherPaths.ErrorLogFile)
            };
        }
    }

    // The language changes after the Settings tab handled its event: the tabs are built again right after.
    private void OnLanguageChanged()
    {
        BeginInvoke(new Action(() =>
        {
            L.SaveMissing();
            L.Load(context.Settings.Language, out string? error);
            if (error != null) Ui.ShowError(this, error);
            BuildTabs(2);
            context.ClientNotice(L.F("launcher language: {0}", L.Code));
        }));
    }

    private void RefreshStatus()
    {
        clientTab?.RefreshStatus();

        string server = context.Server.State switch
        {
            ServerState.Running => L.F("Server: running ({0} connection(s))", context.Server.ConnectionCount),
            ServerState.Starting => L.T("Server: starting"),
            ServerState.Stopping => L.T("Server: stopping"),
            _ => NetworkCheck.AnyServerListening() ? L.T("Server: running outside the launcher") : L.T("Server: stopped")
        };
        if (serverStatus.Text != server) serverStatus.Text = server;

        string game = context.Game.IsRunning(context.GameFolder) ? L.T("Game: open") : L.T("Game: closed");
        if (gameStatus.Text != game) gameStatus.Text = game;
    }

    private void RestoreWindow()
    {
        LauncherSettings s = context.Settings;
        var bounds = new Rectangle(s.WindowX, s.WindowY, Math.Max(MinimumSize.Width, s.WindowWidth), Math.Max(MinimumSize.Height, s.WindowHeight));
        bool visible = s.WindowX >= 0 && s.WindowY >= 0 && Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds));
        if (visible)
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
        }
        else
        {
            // First run (or the saved place is on a screen that is gone): centered, no bigger than the screen.
            Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, bounds.Width, bounds.Height);
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Math.Min(bounds.Width, area.Width), Math.Min(bounds.Height, area.Height));
        }
        if (s.WindowMaximized) WindowState = FormWindowState.Maximized;
    }

    private void HandleClosing(object? sender, FormClosingEventArgs e)
    {
        // Maps of the editor that were not saved.
        if (editorTab != null && !editorTab.ConfirmDiscard())
        {
            e.Cancel = true;
            return;
        }

        if (context.Server.IsActive)
        {
            bool gameOpen = context.Game.IsRunning(context.GameFolder);
            string text = L.T("The server of this PC is running.") +
                          (gameOpen ? " " + L.T("The game is open and will lose the connection.") : "") +
                          "\n\n" + L.T("Stop the server and close the launcher?");
            if (!Ui.Confirm(this, text))
            {
                e.Cancel = true;
                return;
            }

            context.ShuttingDown = true;
            string? logPath = context.Server.SessionLogPath;
            Cursor = Cursors.WaitCursor;
            context.Server.StopAndWait(5000);
            if (context.Settings.ReportOnServerStop)
            {
                try
                {
                    ReportWriter.Write(logPath, context.GameFolder);
                }
                catch (Exception error)
                {
                    CrashLog.Report(error, showDialog: false);
                }
            }
        }
        context.ShuttingDown = true;

        LauncherSettings s = context.Settings;
        s.WindowMaximized = WindowState == FormWindowState.Maximized;
        Rectangle normal = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        s.WindowX = normal.X;
        s.WindowY = normal.Y;
        s.WindowWidth = normal.Width;
        s.WindowHeight = normal.Height;
        s.SelectedTab = tabs?.SelectedIndex ?? 0;
        context.SaveSettings();
        refreshTimer.Stop();
        L.SaveMissing();
        context.Dispose();
    }
}
