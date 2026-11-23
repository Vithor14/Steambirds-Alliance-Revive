using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// "Download the game with Steam" (Client tab): copies the download_depot command, opens the Steam console and follows
// the download (DepotDownloadWatcher). When Steam is done, the depot folder becomes the game folder of the launcher
// and gets steam_appid.txt (the game logs in with Steam when opened by the launcher).
internal sealed class DepotDownloadForm : Form
{
    private readonly LauncherContext context;
    private readonly DepotDownloadWatcher watcher = new DepotDownloadWatcher();
    private readonly Label steamStatus;
    private readonly Button openSteamButton;
    private readonly Label progress;
    private readonly Label consoleLine;
    private readonly Button useFolderButton;
    private readonly System.Windows.Forms.Timer timer;
    private bool finished;

    public DepotDownloadForm(LauncherContext context)
    {
        this.context = context;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = L.T("Download the game with Steam");
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(760, 520);
        MinimumSize = new Size(640, 460);
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;

        Label intro = Ui.Wrapping(L.T("The server was made for the build 201219.1413 of the game, which Steam still downloads from its console. Your Steam account needs Steambirds Alliance in its library (the game was free to play)."), 700, gray: false);

        // ------------------------------------------------------------ step 1
        var command = new ThemedTextBox { Text = LauncherInfo.DepotCommand, ReadOnly = true, Width = 420, Margin = new Padding(3, 4, 3, 3) };
        var copyButton = Ui.Button(L.T("Copy command"), (_, _) => CopyCommand());

        // ------------------------------------------------------------ step 2
        steamStatus = Ui.Text("");
        openSteamButton = Ui.Button(L.T("Open Steam"), (_, _) => OpenSteam());
        var consoleButton = Ui.Primary(L.T("Copy and open the Steam console"), (_, _) =>
        {
            CopyCommand();
            OpenConsole();
        });

        // ------------------------------------------------------------ step 3
        progress = Ui.Bold("");
        consoleLine = Ui.Wrapping("", 700, gray: true);
        useFolderButton = Ui.Primary(L.T("Use this folder"), (_, _) => UseFolder());
        useFolderButton.Enabled = false;

        TableLayoutPanel steps = Ui.Table(2);
        steps.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        steps.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(steps, Ui.Bold("1."), Ui.Flow(command, copyButton));
        Ui.AddRow(steps, Ui.Bold("2."), Ui.Flow(consoleButton, steamStatus, openSteamButton));
        Ui.AddRow(steps, null, Ui.Wrapping(L.T("In the console, paste the command (Ctrl+V) and press Enter. Steam shows \"Downloading depot 386011...\" and later \"Depot download complete\". The new Steam may ask to confirm opening the console."), 640, gray: true));
        Ui.AddRow(steps, Ui.Bold("3."), progress);
        Ui.AddRow(steps, null, consoleLine);
        Ui.AddRow(steps, null, Ui.Wrapping(L.F("Folder: {0}", watcher.TargetFolder ?? L.T("(Steam was not found on this PC)")), 640, gray: true));

        var closeButton = Ui.Button(L.T("Close"), (_, _) => Close());
        var buttons = Ui.Flow(useFolderButton, closeButton);
        buttons.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(root, intro);
        Ui.AddRow(root, Ui.Group(L.T("Download"), steps));
        root.RowCount++;
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        Ui.AddRow(root, buttons);
        Controls.Add(root);
        ResumeLayout(true);
        Theme.AttachWindow(this);

        timer = new System.Windows.Forms.Timer { Interval = 1000 };
        timer.Tick += (_, _) => Tick();
        timer.Start();
        Tick();
        FormClosed += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
        };
    }

    private void CopyCommand()
    {
        try
        {
            Clipboard.SetText(LauncherInfo.DepotCommand);
            context.ClientNotice(L.F("copied to the clipboard: {0}", LauncherInfo.DepotCommand));
        }
        catch (Exception e)
        {
            Ui.ShowError(this, L.F("Could not copy: {0}", e.Message));
        }
    }

    private void OpenConsole()
    {
        if (!GameProcess.IsSteamRunning())
        {
            if (!Ui.Confirm(this, L.T("Steam is not open. Open Steam now? Log in and then click the console button again."))) return;
            OpenSteam();
            return;
        }
        try
        {
            DepotDownloadWatcher.OpenSteamConsole();
            context.ClientNotice(L.T("Steam console opened: paste the command (Ctrl+V) and press Enter"));
        }
        catch (Exception e)
        {
            Ui.ShowError(this, L.F("Could not open the Steam console: {0}\n\nOpen it by typing steam://open/console in the address bar of a browser or of Windows Run (Win+R).", e.Message));
        }
    }

    private void OpenSteam()
    {
        try
        {
            GameProcess.OpenSteam();
        }
        catch (Exception e)
        {
            Ui.ShowError(this, L.F("Could not open Steam: {0}", e.Message));
        }
    }

    private void Tick()
    {
        bool steam = GameProcess.IsSteamRunning();
        Ui.SetStatus(steamStatus, steam ? L.T("Steam is open") : L.T("Steam is closed"), steam ? TextRole.Good : TextRole.Bad);
        openSteamButton.Visible = !steam;

        watcher.Poll();
        string size = DepotDownloadWatcher.FormatSize(watcher.DownloadedBytes);
        switch (watcher.State)
        {
            case DepotDownloadWatcher.Phase.Waiting:
                Ui.SetStatus(progress, watcher.HadGameBefore
                    ? L.F("Waiting for Steam. The folder already has the game ({0}): downloading again checks and updates the files.", size)
                    : L.T("Waiting for Steam to start the download..."), TextRole.Muted);
                break;
            case DepotDownloadWatcher.Phase.Downloading:
                Ui.SetStatus(progress, L.F("Downloading... {0} so far", size), TextRole.Warn);
                break;
            case DepotDownloadWatcher.Phase.Done:
                Ui.SetStatus(progress, L.F("Download complete ({0}).", size), TextRole.Good);
                break;
            case DepotDownloadWatcher.Phase.Failed:
                Ui.SetStatus(progress, L.T("Steam says the download failed (see the line below). Check that the game is in the library of the account logged in on Steam."), TextRole.Bad);
                break;
        }
        consoleLine.Text = watcher.LastConsoleLine.Length > 0 ? L.F("Steam console: {0}", watcher.LastConsoleLine) : "";

        bool hasGame = GameInstall.HasGame(watcher.TargetFolder);
        useFolderButton.Enabled = hasGame;
        if (watcher.State == DepotDownloadWatcher.Phase.Done && !finished && hasGame)
        {
            finished = true;
            UseFolder();
        }
    }

    // The depot folder becomes the game folder of the launcher, with steam_appid.txt.
    private void UseFolder()
    {
        string? folder = watcher.TargetFolder;
        if (!GameInstall.HasGame(folder))
        {
            Ui.ShowError(this, L.F("Steambirds.exe is not in {0} yet.", folder ?? "?"));
            return;
        }

        context.SetGameFolder(folder!);
        string? appIdError = GameInstall.EnsureSteamAppId(folder);
        context.ClientNotice(L.F("game folder: {0} (build {1})", folder!, GameInstall.ReadBuildId(folder)));
        if (appIdError != null) context.ClientNotice(L.F("could not create steam_appid.txt: {0}", appIdError));

        string text = L.F("The game is in:\n{0}\n\nThe launcher uses this folder now. Click Play in the Client tab.", folder!);
        if (!GameInstall.HasBepInEx(folder))
            text += "\n\n" + L.T("This folder has no BepInEx: the game runs, but without the Quieter plugin (its fixes and the tables it writes for the server). To use it, copy the BepInEx folder of a game that has it into this folder.");
        if (appIdError != null)
            text += "\n\n" + L.F("steam_appid.txt could not be created ({0}). Open the launcher as administrator once, or create the file with the text 386010 in the game folder.", appIdError);
        Ui.ShowInfo(this, text);
    }
}
