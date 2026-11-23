using SteambirdsLauncher.Core;
using SteambirdsLauncher.Core.GameData;

namespace SteambirdsLauncher.UI;

// "Settings" tab, in four sub-tabs:
//  - General: theme and launcher language.
//  - Game: game folder and game language.
//  - Server: report option and the server search port.
//  - Folders and files: every folder and file the launcher can open (game, BepInEx, server, maps, accounts, logs,
//    backups, languages, config files).
internal sealed class SettingsTab : UserControl
{
    private readonly LauncherContext context;
    private readonly ThemedTextBox folderBox;
    private readonly ThemedComboBox gameLanguage;
    private readonly HelpButton gameLanguageHelp;
    private readonly StatusPill folderPill;
    private bool loading;

    public SettingsTab(LauncherContext context, Action languageChanged)
    {
        this.context = context;
        LauncherSettings settings = context.Settings;
        Padding = new Padding(12, 6, 12, 12);

        // ------------------------------------------------------------ general: appearance and language
        var theme = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Margin = new Padding(3, 4, 3, 3) };
        theme.Items.Add(L.T("Light"));
        theme.Items.Add(L.T("Dark"));
        theme.SelectedIndex = settings.DarkTheme ? 1 : 0;
        theme.SelectedIndexChanged += (_, _) =>
        {
            settings.DarkTheme = theme.SelectedIndex == 1;
            context.SaveSettings();
            Theme.Set(settings.DarkTheme);
        };

        var language = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Margin = new Padding(3, 4, 3, 3) };
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
        HelpButton languageHelp = Ui.Help(L.T("English is built in. To add a language, copy lang\\_template.json to lang\\<code>.json (for example es-ES.json), fill in \"_language\" and translate the texts (lang\\README.md explains it). Texts without translation show in English and are listed in lang\\<code>.missing.json when the launcher closes. The language list is read when the launcher opens."),
            L.T("Launcher language"));

        TableLayoutPanel appearance = Ui.Table(2);
        appearance.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        appearance.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(appearance, Ui.Note(L.T("Theme:")), theme);
        TableLayoutPanel languageTable = Ui.Table(2);
        languageTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        languageTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(languageTable, Ui.Note(L.T("Launcher language:")), Ui.Flow(language, languageHelp));
        Ui.AddRow(languageTable, null, Ui.Flow(Ui.Button(L.T("Languages folder"), (_, _) => OpenFolderCreating(LauncherPaths.LangFolder))));

        TableLayoutPanel communityTable = Ui.Table(1);
        communityTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(communityTable, Ui.Wrapping(L.T("Questions, bugs, maps and servers to join: the Steambirds Alliance Revive community is on Discord."), 420, true));
        Ui.AddRow(communityTable, Ui.Flow(Ui.DiscordButton(L.T("Join the Discord"), (_, _) => Ui.OpenUrl(FindForm(), LauncherInfo.DiscordUrl))));

        // Discord Rich Presence: plane, level and place while playing; server and players online while hosting.
        var presenceSwitch = new ThemedCheckBox
        {
            Text = L.T("Show what I am doing on my Discord profile"),
            Checked = settings.DiscordPresence,
            Switch = true
        };
        Label presenceStatus = Ui.Wrapping(context.Presence.Status, 420, true);
        presenceSwitch.CheckedChanged += (_, _) =>
        {
            settings.DiscordPresence = presenceSwitch.Checked;
            context.SaveSettings();
            context.Presence.Refresh();
        };
        Action updatePresenceStatus = () => context.RunOnUi(() =>
        {
            if (!presenceStatus.IsDisposed) presenceStatus.Text = context.Presence.Status;
        });
        context.Presence.StatusChanged += updatePresenceStatus;
        Disposed += (_, _) => context.Presence.StatusChanged -= updatePresenceStatus;
        TableLayoutPanel presenceTable = Ui.Table(1);
        presenceTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(presenceTable, Ui.Wrapping(L.T("Playing: your plane, its level, your pilot rank and where you are. Hosting: the name of your server and how many pilots are online. Nothing else: never your IP address, your account or your name."), 420, true));
        Ui.AddRow(presenceTable, Ui.Flow(presenceSwitch));
        Ui.AddRow(presenceTable, presenceStatus);

        var generalPage = Page(L.T("General"),
            Stack(Columns(Ui.Group(L.T("Appearance"), appearance), Ui.Group(L.T("Language"), languageTable)),
                  Ui.Group(L.T("Community"), communityTable),
                  Ui.Group(L.T("Discord Rich Presence"), presenceTable)));

        // ------------------------------------------------------------ game: folder and language
        folderBox = new ThemedTextBox { Text = settings.GameFolder, Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 3) };
        folderBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            ApplyTypedFolder();
        };
        folderBox.Leave += (_, _) => ApplyTypedFolder();
        folderPill = new StatusPill { Margin = new Padding(3, 8, 3, 3) };

        gameLanguage = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Margin = new Padding(3, 4, 3, 3) };
        gameLanguage.SelectedIndexChanged += (_, _) => OnGameLanguageChanged();
        gameLanguageHelp = Ui.Help("", L.T("Game language"));

        TableLayoutPanel folderTable = Ui.Table(4);
        folderTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        folderTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Ui.AddRow(folderTable, Ui.Note(L.T("Game folder:")), folderBox,
            Ui.Button(L.T("Browse..."), (_, _) => BrowseFolder()),
            Ui.Button(L.T("Detect"), (_, _) => DetectFolder()));
        Ui.AddRow(folderTable, null, folderPill);
        folderTable.SetColumnSpan(folderPill, 3);

        TableLayoutPanel gameLanguageTable = Ui.Table(2);
        gameLanguageTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        gameLanguageTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(gameLanguageTable, Ui.Note(L.T("Game language:")), Ui.Flow(gameLanguage, gameLanguageHelp));
        FillGameLanguages();

        var gamePage = Page(L.T("Game"), Stack(Ui.Group(L.T("Game folder"), folderTable), Ui.Group(L.T("Game language"), gameLanguageTable)));

        // ------------------------------------------------------------ server: report and multiplayer
        var reportOnStop = new ThemedCheckBox
        {
            Text = L.T("Write the report when the server stops"),
            Checked = settings.ReportOnServerStop,
            Switch = true
        };
        reportOnStop.CheckedChanged += (_, _) =>
        {
            settings.ReportOnServerStop = reportOnStop.Checked;
            context.SaveSettings();
        };
        HelpButton reportHelp = Ui.Help(L.T("Writes logs\\server-report.txt (server output, game logs, account.json and the .txt files of BepInEx)."),
            L.T("Report"));

        var searchPort = new NumberBox
        {
            Minimum = 1,
            Maximum = 65535,
            Value = Math.Clamp(settings.SearchPort, 1, 65535),
            Width = 110,
            Margin = new Padding(3, 4, 3, 3)
        };
        searchPort.ValueChanged += (_, _) =>
        {
            settings.SearchPort = (int)searchPort.Value;
            context.SaveSettings();
        };
        HelpButton searchHelp = Ui.Help(L.F("The Client tab looks for servers on this port ({0} by default), on the local network and on the VPNs. It must be the \"searchPort\" of the servers (network.json). Servers on another port are joined with Connect by IP.", ServerNetworkFile.DefaultSearchPort),
            L.T("Server search port"));
        TableLayoutPanel searchTable = Ui.Table(2);
        searchTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        searchTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Ui.AddRow(searchTable, Ui.Note(L.T("Server search port (UDP):")), Ui.Flow(searchPort, searchHelp));

        var serverPage = Page(L.T("Server"),
            Columns(Ui.Group(L.T("Server"), Ui.Flow(reportOnStop, reportHelp)), Ui.Group(L.T("Multiplayer"), searchTable)));

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
        HelpButton foldersHelp = Ui.Help(L.T("The accounts are in server\\accounts (one folder per player, with account.json and inventory.json). Editing them by hand only works with the server stopped: the server keeps the accounts of the players who joined in memory and writes over the files."),
            L.T("Accounts"));
        folders.Controls.Add(foldersHelp);

        FlowLayoutPanel logFiles = Ui.WrapFlow(
            Ui.Button(L.T("Report (server-report.txt)"), (_, _) => Ui.OpenFile(FindForm(), LauncherPaths.ReportFile)),
            Ui.Button(L.T("Last server log"), (_, _) => OpenLastServerLog()),
            Ui.Button("LogOutput.log (BepInEx)", (_, _) => Ui.OpenFile(FindForm(), GameInstall.BepInExLog(context.GameFolder))),
            Ui.Button("Player.log (Unity)", (_, _) => Ui.OpenFile(FindForm(), GameInstall.PlayerLog)));

        FlowLayoutPanel configFiles = Ui.WrapFlow(
            FileButton("progression.json"),
            FileButton("items.json"),
            FileButton("enemies.json"),
            FileButton("dungeons.json"),
            FileButton(ServerNetworkFile.FileName),
            Ui.Button("launcher.json", (_, _) => Ui.OpenFile(FindForm(), LauncherPaths.SettingsFile)));

        var launcherFolder = Ui.Note(L.F("Launcher folder: {0}", LauncherPaths.BaseFolder));
        launcherFolder.Margin = new Padding(3, 4, 3, 3);

        var filesPage = Page(L.T("Folders and files"), Stack(
            Ui.Group(L.T("Folders"), folders),
            Ui.Group(L.T("Logs"), logFiles),
            Ui.Group(L.T("Server files"), configFiles),
            launcherFolder));

        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.AddRange(new[] { generalPage, gamePage, serverPage, filesPage });
        tabs.SetIcon(generalPage, Icons.General);
        tabs.SetIcon(gamePage, Icons.Game);
        tabs.SetIcon(serverPage, Icons.Server);
        tabs.SetIcon(filesPage, Icons.Files);
        Controls.Add(tabs);

        context.GameFolderChanged += OnGameFolderChanged;
        context.GameDataChanged += FillGameLanguages;
        UpdateFolderPill();
    }

    // A sub-tab whose content scrolls when the window is small.
    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(2, 14, 2, 4), AutoScroll = true };
        content.Dock = DockStyle.Top;
        page.Controls.Add(content);
        return page;
    }

    // Cards one under the other, as wide as the page.
    private static Control Stack(params Control[] items)
    {
        TableLayoutPanel table = Ui.Table(1);
        table.Dock = DockStyle.Top;
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        foreach (Control item in items)
        {
            item.Dock = DockStyle.Fill;
            Ui.AddRow(table, item);
        }
        return table;
    }

    // Cards side by side, as wide as half of the page each.
    private static Control Columns(params Control[] items)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = items.Length, RowCount = 1 };
        for (int i = 0; i < items.Length; i++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / items.Length));
            items[i].Dock = DockStyle.Fill;
            items[i].Margin = new Padding(0, 0, i == items.Length - 1 ? 0 : 12, 12);
            table.Controls.Add(items[i], i, 0);
        }
        return table;
    }

    private ThemedButton FileButton(string name) => Ui.Button(name, (_, _) => Ui.OpenFile(FindForm(), LauncherPaths.ServerFile(name)));

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
        UpdateFolderPill();
    }

    // Does the chosen folder have the game?
    private void UpdateFolderPill()
    {
        bool has = GameInstall.HasGame(context.GameFolder);
        folderPill.Set(has ? L.T("Steambirds.exe found") : L.T("Steambirds.exe not found"), has ? TextRole.Good : TextRole.Warn);
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

            gameLanguageHelp.HelpText = (data.Loaded
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
