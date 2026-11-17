using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// "New map" dialog: a copy of a dungeon of the game (the list of dungeons.json) or an empty dungeon of any terrain.
internal sealed class NewMapDialog : Form
{
    private readonly RadioButton fromGame;
    private readonly RadioButton blank;
    private readonly ComboBox dungeons;
    private readonly ComboBox terrains;
    private readonly TextBox nameBox;
    private readonly TextBox idBox;
    private readonly Label hint;
    private readonly List<DungeonCatalogFile.Dungeon> catalog = new List<DungeonCatalogFile.Dungeon>();
    private bool idEdited;
    private bool updating;

    public NewMapDialog(bool startBlank)
    {
        Text = L.T("New map");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Padding = new Padding(10);

        try
        {
            if (DungeonCatalogFile.Exists) catalog.AddRange(DungeonCatalogFile.Load().Dungeons);
        }
        catch
        {
            // Without dungeons.json only the empty dungeon is offered.
        }

        fromGame = new RadioButton { Text = L.T("A dungeon of the game (copy of its map)"), AutoSize = true, Checked = !startBlank && catalog.Count > 0 };
        blank = new RadioButton { Text = L.T("An empty dungeon"), AutoSize = true, Checked = startBlank || catalog.Count == 0 };
        fromGame.Enabled = catalog.Count > 0;

        dungeons = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        foreach (DungeonCatalogFile.Dungeon dungeon in catalog) dungeons.Items.Add(DungeonLabel(dungeon));
        if (dungeons.Items.Count > 0) dungeons.SelectedIndex = 0;

        terrains = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        foreach (TerritoryInfo info in Territories.Dungeons) terrains.Items.Add(info.Label + "  (" + info.Terrain + ")");
        terrains.SelectedIndex = 0;

        nameBox = new TextBox { Width = 330 };
        idBox = new TextBox { Width = 330 };
        hint = Ui.Wrapping("", 420, true);

        var table = Ui.Table(2);
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.Dock = DockStyle.Top;
        Ui.AddRow(table, fromGame, null);
        Ui.AddRow(table, Ui.Text(L.T("Dungeon")), dungeons);
        Ui.AddRow(table, blank, null);
        Ui.AddRow(table, Ui.Text(L.T("Terrain")), terrains);
        Ui.AddRow(table, Ui.Text(L.T("Name")), nameBox);
        Ui.AddRow(table, Ui.Text(L.T("Id (file name)")), idBox);
        table.SetColumnSpan(fromGame, 2);
        table.SetColumnSpan(blank, 2);

        var ok = Ui.Button(L.T("Create"), (_, _) => Accept());
        var cancel = Ui.Button(L.T("Cancel"), (_, _) => DialogResult = DialogResult.Cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        FlowLayoutPanel buttons = Ui.Flow(ok, cancel);
        buttons.Dock = DockStyle.Bottom;
        buttons.FlowDirection = FlowDirection.RightToLeft;

        hint.Dock = DockStyle.Bottom;
        Controls.Add(hint);
        Controls.Add(buttons);
        Controls.Add(table);

        fromGame.CheckedChanged += (_, _) => UpdateSuggestion();
        blank.CheckedChanged += (_, _) => UpdateSuggestion();
        dungeons.SelectedIndexChanged += (_, _) => UpdateSuggestion();
        terrains.SelectedIndexChanged += (_, _) => UpdateSuggestion();
        nameBox.TextChanged += (_, _) =>
        {
            if (!idEdited && !updating) SuggestId();
        };
        idBox.TextChanged += (_, _) =>
        {
            if (!updating) idEdited = true;
        };

        ClientSize = new Size(520, 270);
        UpdateSuggestion();
        Theme.AttachWindow(this);
    }

    // The map the user asked for (null until Create is pressed).
    public MapDocument? Result { get; private set; }

    // True when the map is a copy of a map file of the game (false: it starts empty).
    public bool Copied { get; private set; }

    private static string DungeonLabel(DungeonCatalogFile.Dungeon dungeon)
    {
        string name = dungeon.Name.Length > 0 ? dungeon.Name : (Territories.ForTerrain(dungeon.Type)?.Label ?? dungeon.Type);
        return name + "  (" + dungeon.Type + ")";
    }

    private DungeonCatalogFile.Dungeon? SelectedDungeon =>
        dungeons.SelectedIndex >= 0 && dungeons.SelectedIndex < catalog.Count ? catalog[dungeons.SelectedIndex] : null;

    private void UpdateSuggestion()
    {
        dungeons.Enabled = fromGame.Checked;
        terrains.Enabled = blank.Checked;

        string name;
        if (fromGame.Checked && SelectedDungeon != null)
        {
            DungeonCatalogFile.Dungeon dungeon = SelectedDungeon;
            name = dungeon.Name.Length > 0 ? dungeon.Name : (Territories.ForTerrain(dungeon.Type)?.Label ?? dungeon.Type);
            bool has = dungeon.Map.Length > 0 && File.Exists(Path.Combine(LauncherPaths.MapsFolder, dungeon.Map + ".json"));
            hint.Text = has
                ? L.F("The new map starts as a copy of {0}.json. The original file is not changed.", dungeon.Map)
                : L.T("This copy of the game has no map file for this dungeon yet: the map starts empty, with the terrain of the dungeon.");
            name += " " + L.T("(copy)");
        }
        else
        {
            TerritoryInfo info = Territories.Dungeons[Math.Max(0, terrains.SelectedIndex)];
            name = L.F("New {0}", info.Label);
            hint.Text = L.T("The map starts empty: paint the floor, then add the entrance, the exit and the rooms.");
        }

        updating = true;
        nameBox.Text = name;
        updating = false;
        idEdited = false;
        SuggestId();
    }

    private void SuggestId()
    {
        updating = true;
        idBox.Text = MapFactory.FreeId(MapFactory.SuggestId(nameBox.Text));
        updating = false;
    }

    private void Accept()
    {
        string name = nameBox.Text.Trim();
        string id = idBox.Text.Trim();
        if (name.Length == 0)
        {
            Ui.ShowError(this, L.T("Type a name for the map."));
            return;
        }
        if (!MapFactory.IsValidId(id))
        {
            Ui.ShowError(this, L.T("The id is the file name: use only lowercase letters, digits and _ (up to 60 characters)."));
            return;
        }

        if (fromGame.Checked && SelectedDungeon != null)
        {
            Result = MapFactory.FromGameDungeon(SelectedDungeon, id, name, out bool copied);
            Copied = copied;
        }
        else
        {
            TerritoryInfo info = Territories.Dungeons[Math.Max(0, terrains.SelectedIndex)];
            Result = MapFactory.Blank(id, name, info.Terrain, info.Territory);
            Copied = false;
        }
        DialogResult = DialogResult.OK;
    }
}

// How the check dialog is used.
internal enum CheckMode
{
    // Tools > Check map: only a list.
    Report,

    // The map has errors: the save is blocked unless the user chooses to save a draft.
    BlockedSave
}

// List of the problems found in the map (errors and warnings).
internal sealed class ValidationDialog : Form
{
    public ValidationDialog(List<MapIssue> issues, CheckMode mode)
    {
        Text = L.T("Check map");
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Padding = new Padding(8);
        ClientSize = new Size(760, 440);
        MinimumSize = new Size(560, 300);

        int errors = issues.Count(i => i.Level == IssueLevel.Error);
        int warnings = issues.Count - errors;

        string summary;
        if (issues.Count == 0) summary = L.T("No problems found.");
        else if (mode == CheckMode.BlockedSave)
            summary = L.F("The map has {0} error(s) and {1} warning(s). A dungeon with errors does not work on the server. Fix the errors, or save it as a draft to keep working on it later.", errors, warnings);
        else summary = L.F("{0} error(s), {1} warning(s).", errors, warnings);

        Label top = Ui.Wrapping(summary, 720, false);
        top.Dock = DockStyle.Top;

        ListView list = Ui.Grid((L.T("Level"), 80), (L.T("Where"), 200), (L.T("Problem"), 440));
        foreach (MapIssue issue in issues)
        {
            var item = new ListViewItem(issue.Level == IssueLevel.Error ? L.T("Error") : L.T("Warning"));
            item.SubItems.Add(issue.Where);
            item.SubItems.Add(issue.Message);
            item.ForeColor = issue.Level == IssueLevel.Error ? Theme.P.Bad : Theme.P.Warn;
            list.Items.Add(item);
        }
        list.Resize += (_, _) =>
        {
            if (list.Columns.Count == 3) list.Columns[2].Width = Math.Max(200, list.ClientSize.Width - list.Columns[0].Width - list.Columns[1].Width - 4);
        };

        FlowLayoutPanel buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        if (mode == CheckMode.BlockedSave)
        {
            Button draft = Ui.Button(L.T("Save as draft anyway"), (_, _) => DialogResult = DialogResult.Yes);
            Button cancel = Ui.Button(L.T("Back to the editor"), (_, _) => DialogResult = DialogResult.Cancel);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(draft);
            CancelButton = cancel;
        }
        else
        {
            Button close = Ui.Button(L.T("Close"), (_, _) => DialogResult = DialogResult.Cancel);
            buttons.Controls.Add(close);
            CancelButton = close;
            AcceptButton = close;
        }

        Controls.Add(list);
        Controls.Add(buttons);
        Controls.Add(top);
        Theme.AttachWindow(this);
    }
}

// The text of the map file, to read or edit by hand.
internal sealed class RawJsonDialog : Form
{
    private readonly TextBox box;

    public RawJsonDialog(string json)
    {
        Text = L.T("Map file text");
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Padding = new Padding(8);
        ClientSize = new Size(820, 560);
        MinimumSize = new Size(480, 320);

        Label note = Ui.Wrapping(L.T("This is the file as it will be saved. You can change it here: the text is checked when you press Apply, and nothing changes if it is not a valid map."), 780, true);
        note.Dock = DockStyle.Top;

        box = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            AcceptsReturn = true,
            AcceptsTab = true,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9f),
            Text = json.Replace("\r\n", "\n").Replace("\n", "\r\n")
        };
        box.Select(0, 0);

        Button apply = Ui.Button(L.T("Apply"), (_, _) => DialogResult = DialogResult.OK);
        Button cancel = Ui.Button(L.T("Cancel"), (_, _) => DialogResult = DialogResult.Cancel);
        FlowLayoutPanel buttons = Ui.Flow(apply, cancel);
        buttons.Dock = DockStyle.Bottom;
        buttons.FlowDirection = FlowDirection.RightToLeft;
        CancelButton = cancel;

        Controls.Add(box);
        Controls.Add(buttons);
        Controls.Add(note);
        Theme.AttachWindow(this);
    }

    public string EditedText => box.Text;
}
