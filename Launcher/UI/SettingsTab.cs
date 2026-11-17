using SteambirdsLauncher.Core;
using SteambirdsLauncher.Core.GameData;

namespace SteambirdsLauncher.UI;

// "Settings" tab: theme, launcher language, game folder, game language, server search port, report option, and every
// folder and file the launcher can open (game, BepInEx, server, maps, accounts, logs, backups, languages, config files).
internal sealed class SettingsTab : UserControl
{
    private readonly LauncherContext context;
    private readonly TextBox folderBox;
    private readonly ComboBox gameLanguage;
    private readonly Label gameLanguageNote;
    private bool loading;

    public SettingsTab(LauncherContext context, Action languageChanged)
    {
        this.context = context;
        LauncherSettings settings = context.Settings;

        // ------------------------------------------------------------ appearance
        var theme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(3, 4, 3, 3) };
        theme.Items.Add(L.T("Light"));
        theme.Items.Add(L.T("Dark"));
        theme.SelectedIndex = settings.DarkTheme ? 1 : 0;
        theme.SelectedIndexChanged += (_, _) =>
        {
            settings.DarkTheme = theme.SelectedIndex == 1;
            context.SaveSettings();
            Theme.Set(settings.DarkTheme);
        };

        var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(3, 4, 3, 3) };
        List<L.LanguageInfo> languages = L.Available();
        foreach (L.LanguageInfo info in languages) language.Items.Add(info);
        language.SelectedIndex = Math.Max(0, languages.FindIndex(l => string.Equals(l.Code, L.Code, StringComparison.OrdinalIgnoreCase)));
        language.SelectedIndexChanged += (_, _) =>
        {
            if (language.SelectedItem is not L.LanguageInfo chosen || chosen.Code == settings.Language) return;
            settings.Language = chosen.Code;
            context.SaveSettings();
            languageChanged();
        };

        TableLayoutPanel appearance = Ui.Table(3);
        Ui.AddRow(appearance, Ui.Text(L.T("Theme:")), theme);
        Ui.AddRow(appearance, Ui.Text(L.T("Launcher language:")), language,
            Ui.Button(L.T("Languages folder"), (_, _) => OpenFolderCreating(LauncherPaths.LangFolder)));
        Label languageHelp = Ui.Wrapping(L.T("English is built in. To add a language, copy lang\\_template.json to lang\\<code>.json (for example es-ES.json), fill in \"_language\" and translate the texts (lang\\README.md explains it). Texts without translation show in English and are listed in lang\\<code>.missing.json when the launcher closes. The language list is read when the launcher opens."), 820, gray: true);
        Ui.AddRow(appearance, languageHelp);
        appearance.SetColumnSpan(languageHelp, 3);

        // ------------------------------------------------------------ game
        folderBox = new TextBox { Text = settings.GameFolder, Width = 520, Margin = new Padding(3, 4, 3, 3) };
        folderBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            ApplyTypedFolder();
        };
        folderBox.Leave += (_, _) => ApplyTypedFolder();

        gameLanguage = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Margin = new Padding(3, 4, 3, 3) };
        gameLanguage.SelectedIndexChanged += (_, _) => OnGameLanguageChanged();
        gameLanguageNote = Ui.Wrapping("", 820, gray: true);

        TableLayoutPanel game = Ui.Table(2);
        Ui.AddRow(game, Ui.Text(L.T("Game folder:")), Ui.Flow(folderBox,
            Ui.Button(L.T("Browse..."), (_, _) => BrowseFolder()),
            Ui.Button(L.T("Detect"), (_, _) => DetectFolder())));
        Ui.AddRow(game, Ui.Text(L.T("Game language:")), gameLanguage);
        Ui.AddRow(game, null, gameLanguageNote);
        FillGameLanguages();

        // ------------------------------------------------------------ server and logs
        var reportOnStop = new CheckBox
        {
            Text = L.T("Write the report when the server stops"),
            Checked = settings.ReportOnServerStop,
            AutoSize = true,
            Margin = new Padding(3, 6, 3, 3)
        };
        reportOnStop.CheckedChanged += (_, _) =>
        {
            settings.ReportOnServerStop = reportOnStop.Checked;
            context.SaveSettings();
        };
        context.ToolTip.SetToolTip(reportOnStop, L.T("Writes logs\\server-report.txt (server output, game logs, account.json and the .txt files of BepInEx), like Run server.ps1 did on Ctrl+C."));
        TableLayoutPanel logs = Ui.Table(1);
        Ui.AddRow(logs, reportOnStop);

        // ------------------------------------------------------------ server search
        var searchPort = new NumericUpDown
        {
            Minimum = 1,
            Maximum = 65535,
            Value = Math.Clamp(settings.SearchPort, 1, 65535),
            Width = 90,
            Margin = new Padding(3, 4, 3, 3)
        };
        searchPort.ValueChanged += (_, _) =>
        {
            settings.SearchPort = (int)searchPort.Value;
            context.SaveSettings();
        };
        TableLayoutPanel search = Ui.Table(2);
        Ui.AddRow(search, Ui.Text(L.T("Server search port (UDP):")), searchPort);
        Label searchHelp = Ui.Wrapping(L.F("The Client tab looks for servers on this port ({0} by default), on the local network and on the VPNs. It must be the \"searchPort\" of the servers (network.json). Servers on another port are joined with Connect by IP.", ServerNetworkFile.DefaultSearchPort), 820, gray: true);
        Ui.AddRow(search, searchHelp);
        search.SetColumnSpan(searchHelp, 2);

        // ------------------------------------------------------------ folders and files
        FlowLayoutPanel folders = Ui.WrapFlow(
            Ui.Button(L.T("Game"), (_, _) => Ui.OpenFolder(FindForm(), context.GameFolder)),
            Ui.Button("BepInEx", (_, _) => Ui.OpenFolder(FindForm(), GameInstall.BepInExFolder(context.GameFolder))),
            Ui.Button(L.T("Launcher"), (_, _) => Ui.OpenFolder(FindForm(), LauncherPaths.BaseFolder)),
            Ui.Button(L.T("Server"), (_, _) => Ui.OpenFolder(FindForm(), LauncherPaths.ServerFolder)),
            Ui.Button(L.T("Maps"), (_, _) => Ui.OpenFolder(FindForm(), LauncherPaths.MapsFolder)),
            Ui.Button(L.T("Accounts"), (_, _) => OpenFolderCreating(AccountStore.AccountsFolder)),
            Ui.Button(L.T("Loot tables"), (_, _) => Ui.OpenFolder(FindForm(), LauncherPaths.ServerFile("loot_tables"))),
            Ui.Button(L.T("Logs"), (_, _) => OpenFolderCreating(LauncherPaths.LogsFolder)),
            Ui.Button(L.T("Backups"), (_, _) => OpenFolderCreating(LauncherPaths.BackupFolder)),
            Ui.Button(L.T("Languages"), (_, _) => OpenFolderCreating(LauncherPaths.LangFolder)));
        folders.Dock = DockStyle.Fill;

        FlowLayoutPanel logFiles = Ui.WrapFlow(
            Ui.Button(L.T("Report (server-report.txt)"), (_, _) => Ui.OpenFile(FindForm(), LauncherPaths.ReportFile)),
            Ui.Button(L.T("Last server log"), (_, _) => OpenLastServerLog()),
            Ui.Button("LogOutput.log (BepInEx)", (_, _) => Ui.OpenFile(FindForm(), GameInstall.BepInExLog(context.GameFolder))),
            Ui.Button("Player.log (Unity)", (_, _) => Ui.OpenFile(FindForm(), GameInstall.PlayerLog)));
        logFiles.Dock = DockStyle.Fill;

        FlowLayoutPanel configFiles = Ui.WrapFlow(
            FileButton("progression.json"),
            FileButton("items.json"),
            FileButton("enemies.json"),
            FileButton("dungeons.json"),
            FileButton(ServerNetworkFile.FileName),
            Ui.Button("launcher.json", (_, _) => Ui.OpenFile(FindForm(), LauncherPaths.SettingsFile)));
        configFiles.Dock = DockStyle.Fill;

        TableLayoutPanel open = Ui.Table(2);
        Ui.AddRow(open, Ui.Text(L.T("Folders:")), folders);
        Ui.AddRow(open, Ui.Text(L.T("Logs:")), logFiles);
        Ui.AddRow(open, Ui.Text(L.T("Server files:")), configFiles);
        Label filesHelp = Ui.Wrapping(L.T("The accounts are in server\\accounts (one folder per player, with account.json and inventory.json). Editing them by hand only works with the server stopped: the server keeps the accounts of the players who joined in memory and writes over the files."), 820, gray: true);
        Ui.AddRow(open, filesHelp);
        open.SetColumnSpan(filesHelp, 2);

        // ------------------------------------------------------------ layout
        var column = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(column, Ui.Group(L.T("Appearance"), appearance));
        Ui.AddRow(column, Ui.Group(L.T("Game"), game));
        Ui.AddRow(column, Ui.Group(L.T("Server"), logs));
        Ui.AddRow(column, Ui.Group(L.T("Multiplayer"), search));
        Ui.AddRow(column, Ui.Group(L.T("Open folders and files"), open));
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroller.Controls.Add(column);
        Controls.Add(scroller);

        context.GameFolderChanged += OnGameFolderChanged;
        context.GameDataChanged += FillGameLanguages;
    }

    private Button FileButton(string name) => Ui.Button(name, (_, _) => Ui.OpenFile(FindForm(), LauncherPaths.ServerFile(name)));

    private void OpenFolderCreating(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch
        {
            // OpenFolder shows the error.
        }
        Ui.OpenFolder(FindForm(), folder);
    }

    private void OpenLastServerLog()
    {
        string? path = context.Server.SessionLogPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            try
            {
                path = Directory.Exists(LauncherPaths.LogsFolder)
                    ? new DirectoryInfo(LauncherPaths.LogsFolder).GetFiles("server-*.log").OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault()?.FullName
                    : null;
            }
            catch
            {
                path = null;
            }
        }
        if (path == null)
        {
            Ui.ShowInfo(FindForm(), L.T("The server has not run through the launcher yet."));
            return;
        }
        Ui.OpenFile(FindForm(), path);
    }

    // ------------------------------------------------------------------ game folder

    private void OnGameFolderChanged()
    {
        if (folderBox.Text != context.GameFolder) folderBox.Text = context.GameFolder;
    }

    private void ApplyTypedFolder()
    {
        string typed = folderBox.Text.Trim();
        if (typed.Length == 0 || string.Equals(typed.TrimEnd('\\', '/'), context.GameFolder, StringComparison.OrdinalIgnoreCase)) return;
        context.SetGameFolder(typed);
        context.ClientNotice(L.F("game folder: {0}", context.GameFolder) + (GameInstall.HasGame(context.GameFolder) ? "" : " " + L.T("(Steambirds.exe not found)")));
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = L.T("Game folder (where Steambirds.exe is)"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(context.GameFolder)) dialog.SelectedPath = context.GameFolder;
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;

        string chosen = dialog.SelectedPath;
        if (!GameInstall.HasGame(chosen) &&
            !Ui.Confirm(FindForm(), L.F("Steambirds.exe was not found in:\n{0}\n\nUse this folder anyway?", chosen)))
            return;
        context.SetGameFolder(chosen);
        folderBox.Text = context.GameFolder;
        context.ClientNotice(L.F("game folder: {0}", context.GameFolder));
    }

    private void DetectFolder()
    {
        string detected = GameInstall.DetectFolder();
        context.SetGameFolder(detected);
        folderBox.Text = context.GameFolder;
        context.ClientNotice(GameInstall.HasGame(detected) ? L.F("game found in {0}", detected) : L.F("the game was not found in the Steam folder ({0})", detected));
    }

    // ------------------------------------------------------------------ game language

    private sealed class LocaleItem
    {
        public LocaleItem(string id, string display)
        {
            Id = id;
            Display = display;
        }

        public string Id { get; }
        public string Display { get; }
        public override string ToString() => Display;
    }

    private void FillGameLanguages()
    {
        loading = true;
        try
        {
            gameLanguage.Items.Clear();
            gameLanguage.Items.Add(new LocaleItem("", L.T("Chosen in the game (default)")));
            GameConfig data = context.GameData;
            foreach (GameConfig.Locale locale in data.Locales.OrderBy(l => l.Hidden))
                gameLanguage.Items.Add(new LocaleItem(locale.Id, locale + (locale.Hidden ? " " + L.T("(hidden in the game)") : "")));

            string current = context.Settings.GameLocale;
            int index = 0;
            for (int i = 0; i < gameLanguage.Items.Count; i++)
            {
                if (gameLanguage.Items[i] is LocaleItem item && item.Id == current) index = i;
            }
            if (current.Length > 0 && index == 0)
            {
                gameLanguage.Items.Add(new LocaleItem(current, current));
                index = gameLanguage.Items.Count - 1;
            }
            gameLanguage.SelectedIndex = index;

            gameLanguageNote.Text = (data.Loaded
                ? L.T("Languages of the game (Texts/locales of the configBinary).")
                : L.T("The game data could not be read, so only English is listed.")) + " " +
                L.T("A chosen language is passed to the game with -locale when it opens; \"Chosen in the game\" uses the language of the game's own options menu.");
        }
        finally
        {
            loading = false;
        }
    }

    private void OnGameLanguageChanged()
    {
        if (loading || gameLanguage.SelectedItem is not LocaleItem item) return;
        if (item.Id == context.Settings.GameLocale) return;
        context.Settings.GameLocale = item.Id;
        context.SaveSettings();
        context.ClientNotice(item.Id.Length == 0 ? L.T("game language: chosen in the game") : L.F("game language: {0} (-locale {1})", item.Display, item.Id));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            context.GameFolderChanged -= OnGameFolderChanged;
            context.GameDataChanged -= FillGameLanguages;
        }
        base.Dispose(disposing);
    }
}
