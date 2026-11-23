using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;
using SteambirdsLauncher.EnemyAi;

namespace SteambirdsLauncher.UI;

// Main window: a header with the main tabs (Client, Server, Map editor, Enemy AI, Settings) and the state of the server
// and of the game.
// Changing the launcher language builds the tabs again; the server, the game and the consoles keep running
// (they live in LauncherContext).
internal sealed class MainForm : Form
{
    private readonly LauncherContext context;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private readonly string? settingsWarning;
    private readonly string? languageWarning;
    private MainNav? nav;
    private int settingsIndex;
    private int aiIndex;
    private ClientTab? clientTab;

    // The map editor keeps its map (and the undo history) when the launcher builds the tabs again (new language).
    private MapEditorTab? editorTab;
    private MapSession? editorSession;

    // The Enemy AI tab keeps its files (and what is not saved yet) in the same way.
    private readonly AiStore aiStore = new AiStore(LauncherPaths.AiFolder);
    private AiTab? aiTab;
    private bool aiStoreLoaded;

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
        MinimumSize = new Size(1000, 660);
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
        context.OpenSettingsTab = () => nav?.Select(settingsIndex);

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
        MainNav? old = nav;
        SuspendLayout();
        try
        {
            var newNav = new MainNav { Dock = DockStyle.Fill, Version = LauncherInfo.Version };

            // Play and host first, then the two tools for making content, then the settings at the right end.
            clientTab = new ClientTab(context) { Dock = DockStyle.Fill };
            newNav.AddPage(L.T("Client"), clientTab, icon: Icons.Play);
            newNav.AddPage(L.T("Server"), new ServerTab(context) { Dock = DockStyle.Fill }, icon: Icons.Server);

            // The AI tab is built before the map editor: the editor asks it for the list of AIs.
            Control ai = BuildAiTab();
            Control editor = BuildEditor();
            newNav.AddPage(L.T("Map editor"), editor, MainNav.Side.Left, dividerBefore: true, icon: Icons.MapEditor);
            newNav.AddPage(L.T("Enemy AI"), ai, icon: Icons.EnemyAi);
            aiIndex = 3;

            newNav.AddPage(L.T("Settings"), new SettingsTab(context, OnLanguageChanged) { Dock = DockStyle.Fill }, MainNav.Side.Right, icon: Icons.Settings);
            settingsIndex = 4;
            newNav.Select(selected >= 0 && selected < 5 ? selected : 0);
            newNav.SetStatus(0, "", "", TextRole.Muted);

            Controls.Add(newNav);
            newNav.BringToFront();
            nav = newNav;
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
        RefreshStatus();
    }

    // The Enemy AI tab. Like the map editor, a failure here only shows in its own tab.
    private Control BuildAiTab()
    {
        try
        {
            if (!aiStoreLoaded)
            {
                aiStore.Reload();
                aiStoreLoaded = true;
            }
            aiTab = new AiTab(context, aiStore) { Dock = DockStyle.Fill };
            EnemyAiEditors.Current = new AiEditorBridge(aiStore, OpenAi);
            context.OpenAiTab = OpenAi;
            return aiTab;
        }
        catch (Exception error)
        {
            aiTab = null;
            EnemyAiEditors.Current = null;
            CrashLog.Report(error, showDialog: false);
            return new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(16),
                Tag = TextRole.Bad,
                Text = L.F("The Enemy AI tab could not open:\n\n{0}\n\nThe details were saved in {1}.", error.Message, LauncherPaths.ErrorLogFile)
            };
        }
    }

    // Selects the Enemy AI tab (on one AI when its id is given).
    private void OpenAi(string? id)
    {
        if (nav == null || nav.PageCount < 5) return;
        nav.Select(aiIndex);
        if (!string.IsNullOrWhiteSpace(id)) aiTab?.OpenBrain(id);
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
            BuildTabs(settingsIndex);
            context.ClientNotice(L.F("launcher language: {0}", L.Code));
        }));
    }

    private void RefreshStatus()
    {
        clientTab?.RefreshStatus();
        if (nav == null) return;

        // The two pills of the header: the full text, and a short one for a narrow window.
        (string text, TextRole role) = context.Server.State switch
        {
            ServerState.Running => (L.F("Server: running ({0} connection(s))", context.Server.ConnectionCount), TextRole.Good),
            ServerState.Starting => (L.T("Server: starting"), TextRole.Warn),
            ServerState.Stopping => (L.T("Server: stopping"), TextRole.Warn),
            _ => NetworkCheck.AnyServerListening()
                ? (L.T("Server: running outside the launcher"), TextRole.Warn)
                : (L.T("Server: stopped"), TextRole.Muted)
        };
        nav.SetStatus(0, text, L.T("Server"), role);

        bool gameOpen = context.Game.IsRunning(context.GameFolder);
        nav.SetStatus(1, gameOpen ? L.T("Game: open") : L.T("Game: closed"), L.T("Game"), gameOpen ? TextRole.Good : TextRole.Muted);
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

        // AI files that were not saved.
        if (aiTab != null && !aiTab.ConfirmDiscard())
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
        s.SelectedTab = nav?.SelectedPage ?? 0;
        context.SaveSettings();
        refreshTimer.Stop();
        L.SaveMissing();
        context.Dispose();
    }
}
