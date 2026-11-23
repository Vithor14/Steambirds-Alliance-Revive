using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// Shown before the main window when the launcher does not find the game (Steambirds.exe) in the game folder.
// Two ways to get it: download it with Steam (DepotDownloadForm) or point to a folder that already has it.
// Closing the window or choosing "Continue without the game" opens the launcher anyway (the game can be set later in
// the Client and Settings tabs).
internal sealed class GameSetupForm : Form
{
    private readonly LauncherContext context;

    public GameSetupForm(LauncherContext context)
    {
        this.context = context;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = LauncherInfo.Title;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 440);
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;

        Label title = Ui.Bold(L.T("The game was not found"));
        Label intro = Ui.Wrapping(L.F("The launcher did not find Steambirds.exe in:\n{0}\n\nChoose how to get the game. It opens the launcher right after.", context.GameFolder.Length > 0 ? context.GameFolder : "?"), 590, gray: false);

        // The warning about the version: this is NOT the latest build of the game.
        Label versionTitle = Ui.Bold(L.T("Not the latest version of the game"));
        Label version = Ui.Wrapping(L.F("This revival needs the build {0}, downloaded from the Steam console with the command below. Do NOT use the latest version of the game that Steam installs from the library: the latest version has all of the game's content removed, and the server and the map editor cannot work with it.", LauncherInfo.ExpectedGameBuild), 590, gray: false);
        Ui.SetRole(version, TextRole.Warn);
        var command = new ThemedTextBox { Text = LauncherInfo.DepotCommand, ReadOnly = true, Width = 420, Margin = new Padding(3, 4, 3, 3) };

        Button download = Ui.Primary(L.T("Download the game with Steam"), (_, _) => Download());
        download.AutoSize = true;
        Label downloadNote = Ui.Wrapping(L.T("Opens the Steam console with the command ready to paste and follows the download. Your Steam account needs Steambirds Alliance in its library (the game was free to play)."), 590, gray: true);

        Button choose = Ui.Button(L.T("Choose the folder where the game is"), (_, _) => ChooseFolder());
        choose.AutoSize = true;
        Label chooseNote = Ui.Wrapping(L.F("For a game that is already on this PC: pick the folder that has Steambirds.exe (build {0}).", LauncherInfo.ExpectedGameBuild), 590, gray: true);

        Button skip = Ui.Button(L.T("Continue without the game"), (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        });
        skip.AutoSize = true;
        Label skipNote = Ui.Wrapping(L.T("The server data, the Server tab and the launcher settings work, but the Client tab cannot open the game and the map editor has no pictures until the game is set."), 590, gray: true);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(14) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(root, title);
        Ui.AddRow(root, intro);
        Ui.AddRow(root, versionTitle);
        Ui.AddRow(root, version);
        Ui.AddRow(root, Ui.Flow(command, Ui.Button(L.T("Copy command"), (_, _) => CopyCommand())));
        Ui.AddRow(root, new Label { Height = 8 });
        Ui.AddRow(root, download);
        Ui.AddRow(root, downloadNote);
        Ui.AddRow(root, choose);
        Ui.AddRow(root, chooseNote);
        Ui.AddRow(root, new Label { Height = 8 });
        Ui.AddRow(root, skip);
        Ui.AddRow(root, skipNote);
        Controls.Add(root);
        ResumeLayout(true);
        Theme.AttachWindow(this);
    }

    private void CopyCommand()
    {
        try
        {
            Clipboard.SetText(LauncherInfo.DepotCommand);
        }
        catch (Exception e)
        {
            Ui.ShowError(this, L.F("Could not copy: {0}", e.Message));
        }
    }

    private void Download()
    {
        using var form = new DepotDownloadForm(context);
        form.ShowDialog(this);
        if (GameInstall.HasGame(context.GameFolder))
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private void ChooseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L.T("Game folder (where Steambirds.exe is)"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(context.GameFolder)) dialog.SelectedPath = context.GameFolder;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        string chosen = dialog.SelectedPath;
        if (!GameInstall.HasGame(chosen))
        {
            Ui.ShowError(this, L.F("Steambirds.exe was not found in:\n{0}", chosen));
            return;
        }

        string build = GameInstall.ReadBuildId(chosen);
        if (build.Length > 0 && build != LauncherInfo.ExpectedGameBuild &&
            !Ui.Confirm(this, L.F("This game is the build {0}, and the launcher and the server were made for {1}. Use it anyway?", build, LauncherInfo.ExpectedGameBuild)))
            return;

        context.SetGameFolder(chosen);
        string? appIdError = GameInstall.EnsureSteamAppId(chosen);
        context.ClientNotice(L.F("game folder: {0} (build {1})", chosen, build.Length > 0 ? build : "?"));
        if (appIdError != null) context.ClientNotice(L.F("could not create steam_appid.txt: {0}", appIdError));
        DialogResult = DialogResult.OK;
        Close();
    }
}
