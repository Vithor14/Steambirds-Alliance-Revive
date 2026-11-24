using System.Globalization;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.UI;

// "Maps and dungeons" page of the Server tab.
//  - Map of each dungeon, when it opens, whether it is on and how much health its enemies have (dungeons.json).
//  - Nexus and tutorial maps: map file of each one (dungeons.json "nexusMap" / "tutorialMap"; the originals are
//    maps\nexus.json and maps\tutorial_walker.json).
//  - "Open in the editor": loads the selected map (Nexus, tutorial or the dungeon of the selected row) in the Map editor tab.
//  - Map preview: the card at the right of the table draws the map of the selected row (or of the Nexus / tutorial choice
//    last touched) with the editor's own canvas (Editor\MapThumbnail.cs).
internal sealed class MapsPage : UserControl
{
    private readonly LauncherContext context;
    private readonly ThemedComboBox nexusMap;
    private readonly ThemedComboBox tutorialMap;
    private readonly DataGridView grid;
    private readonly DataGridViewComboBoxColumn mapColumn;
    private readonly NumberBox maxPlayers;
    private readonly NumberBox autoExit;
    private readonly CheckBox portalsOn;
    private readonly ComboBox entryMode;
    private readonly CheckBox lockBossRooms;
    private readonly NumberBox fireRateScale;
    private readonly NumberBox maxBullets;
    private readonly Button saveButton;
    private readonly Button undoButton;
    private readonly Label statusLabel;
    private readonly System.Windows.Forms.Timer watchTimer;
    private readonly PictureBox previewPicture;
    private readonly Label previewCaption;
    private readonly System.Windows.Forms.Timer previewTimer;
    // Pictures already drawn, by file: (time of the file, picture, description). Redrawn when the file changes.
    private readonly Dictionary<string, (DateTime Stamp, Bitmap Picture, string Description)> previewCache =
        new Dictionary<string, (DateTime, Bitmap, string)>(StringComparer.OrdinalIgnoreCase);
    private string previewMap = "";
    private string previewShown = "";
    private DateTime loadedStamp = DateTime.MinValue;
    private bool loading;
    private bool dirty;

    // dungeons.json "entryMode" and the text shown for each value.
    private static readonly (string Value, string Label)[] EntryModes =
    {
        ("both", L.T("Both: carriers outside the city and the Mission Transport")),
        ("portals", L.T("Only the carriers outside the city")),
        ("missionTransport", L.T("Only the Mission Transport (mega carrier mission list)"))
    };

    public MapsPage(LauncherContext context)
    {
        this.context = context;
        ToolTip tip = context.ToolTip;

        // ------------------------------------------------------------ Nexus and tutorial maps
        nexusMap = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        tutorialMap = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        nexusMap.SelectedIndexChanged += (_, _) =>
        {
            MarkDirty();
            RequestPreview(SelectedText(nexusMap));
        };
        tutorialMap.SelectedIndexChanged += (_, _) =>
        {
            MarkDirty();
            RequestPreview(SelectedText(tutorialMap));
        };
        tip.SetToolTip(nexusMap, L.T("Map file of the Nexus (Rebel City). The original is nexus. To make another version, open nexus in the Map editor, change it and use Save as."));
        tip.SetToolTip(tutorialMap, L.T("Map file of the tutorial (Training Grounds). The original is tutorial_walker. To make another version, open it in the Map editor, change it and use Save as."));

        nexusMap.Margin = new Padding(3, 4, 3, 3);
        tutorialMap.Margin = new Padding(3, 4, 3, 3);
        ThemedButton nexusEdit = Ui.Button(L.T("Open in the editor"), (_, _) => OpenInEditor(SelectedText(nexusMap)));
        ThemedButton tutorialEdit = Ui.Button(L.T("Open in the editor"), (_, _) => OpenInEditor(SelectedText(tutorialMap)));
        FlowLayoutPanel fixedTable = Ui.WrapFlow(
            Ui.Note(L.T("Nexus (Rebel City):")), nexusMap, nexusEdit,
            Ui.Note(L.T("Tutorial (Training Grounds):")), tutorialMap, tutorialEdit);

        // ------------------------------------------------------------ dungeons
        saveButton = Ui.Primary(L.T("Save"), (_, _) => Save());
        undoButton = Ui.Button(L.T("Undo changes"), (_, _) => Reload());
        saveButton.Enabled = false;
        undoButton.Enabled = false;
        statusLabel = Ui.Note("");
        ThemedButton openButton = Ui.Button(L.T("Open the selected dungeon in the editor"), (_, _) => OpenSelectedDungeon());

        // What the table columns mean goes behind a "?" in the toolbar.
        HelpButton tableHelp = Ui.Help(L.T("Active: the dungeon shows in the Nexus and accepts players. Map file: any .json of the server\\maps folder (the community can make other versions of each dungeon). Health ×: multiplies the health of the dungeon's enemies. Opens at: date and time of this PC as yyyy-mm-dd hh:mm (empty = already open). The server reads dungeons.json again by itself; changes apply to the next entries."),
            L.T("Dungeons (dungeons.json)"));
        var toolbar = Ui.WrapFlow(saveButton, undoButton, openButton, statusLabel, tableHelp);
        toolbar.Dock = DockStyle.Top;
        toolbar.Padding = new Padding(0, 0, 0, 8);

        maxPlayers = new NumberBox { Minimum = 1, Maximum = 100, Width = 90, TextAlign = HorizontalAlignment.Right };
        autoExit = new NumberBox { Minimum = 0, Maximum = 3600, Width = 90, TextAlign = HorizontalAlignment.Right };
        portalsOn = new ThemedCheckBox { Text = L.T("Dungeon carriers in the Nexus"), OffText = L.T("Dungeon carriers in the Nexus"), Switch = true };
        maxPlayers.ValueChanged += (_, _) => MarkDirty();
        autoExit.ValueChanged += (_, _) => MarkDirty();
        portalsOn.CheckedChanged += (_, _) => MarkDirty();

        // How players enter dungeons, boss room locks and how hard the enemies shoot (dungeons.json).
        entryMode = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        foreach ((string value, string label) in EntryModes) entryMode.Items.Add(label);
        entryMode.SelectedIndex = 0;
        lockBossRooms = new ThemedCheckBox { Text = L.T("Lock the doors of a boss room behind the player"), OffText = L.T("Lock the doors of a boss room behind the player"), Switch = true };
        fireRateScale = new NumberBox { Minimum = 0.1m, Maximum = 5m, DecimalPlaces = 2, Increment = 0.1m, Value = 1m, Width = 90, TextAlign = HorizontalAlignment.Right };
        maxBullets = new NumberBox { Minimum = 0, Maximum = 200, Width = 90, Value = 10, TextAlign = HorizontalAlignment.Right };
        entryMode.SelectedIndexChanged += (_, _) => MarkDirty();
        lockBossRooms.CheckedChanged += (_, _) => MarkDirty();
        fireRateScale.ValueChanged += (_, _) => MarkDirty();
        maxBullets.ValueChanged += (_, _) => MarkDirty();
        tip.SetToolTip(entryMode, L.T("both: the carriers outside the city and the Mission Transport (the mega carrier at the end of the main ramp, which lists the dungeons as missions). portals: only the carriers. missionTransport: only the mission list."));
        tip.SetToolTip(maxBullets, L.T("The most bullets per second one dungeon enemy may put out (its guns slow down together until it fits). 0 = no limit. Lower = easier to dodge."));
        tip.SetToolTip(fireRateScale, L.T("Multiplies the fire rate of every dungeon enemy (0.5 = half as fast)."));

        // Every setting is a small "name over field" block; the blocks wrap onto as many rows as the width needs.
        FlowLayoutPanel settingsFlow = Ui.WrapFlow(
            Field(L.T("Max players per dungeon:"), maxPlayers),
            Field(L.T("Automatic exit after the boss (seconds):"), autoExit),
            Field(L.T("Enemy fire rate ×:"), fireRateScale),
            Field(L.T("Max bullets per second per enemy (0 = no limit):"), maxBullets),
            Field(L.T("How players enter dungeons:"), entryMode),
            Field("", portalsOn),
            Field("", lockBossRooms));
        Card nexusCard = Ui.Group(L.T("Nexus and tutorial maps"), fixedTable);
        var settingsCard = new Card { Title = L.T("Dungeon settings"), Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        settingsFlow.Dock = DockStyle.Top;
        settingsCard.Controls.Add(settingsFlow);
        FitWidth(settingsCard, settingsFlow);
        FitWidth(nexusCard, fixedTable);

        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            EditMode = DataGridViewEditMode.EditOnEnter
        };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "active", HeaderText = L.T("Active"), Width = 55 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = L.T("Name"), Width = 230 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "type", HeaderText = L.T("Type (game terrain)"), Width = 150, ReadOnly = true });
        mapColumn = new DataGridViewComboBoxColumn
        {
            Name = "map",
            HeaderText = L.T("Map file"),
            Width = 170,
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        };
        grid.Columns.Add(mapColumn);
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "nexus", HeaderText = L.T("In the Nexus"), Width = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "health", HeaderText = L.T("Health ×"), Width = 65 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "opens", HeaderText = L.T("Opens at"), Width = 130 });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "note",
            HeaderText = L.T("Note"),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 120
        });

        // Checkboxes and combo boxes count as changed right away, not only when the cell loses the focus.
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell or DataGridViewComboBoxCell)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (_, e) =>
        {
            MarkDirty();
            if (!loading && e.RowIndex >= 0 && e.RowIndex == grid.CurrentCell?.RowIndex) RequestPreview(MapOfRow(e.RowIndex));
        };
        grid.CurrentCellChanged += (_, _) =>
        {
            if (!loading && grid.CurrentCell != null) RequestPreview(MapOfRow(grid.CurrentCell.RowIndex));
        };
        grid.DataError += (_, e) => e.ThrowException = false;

        // The table fills what is left; the cards and the toolbar sit above it.
        // The preview sits at the right of the table (added before the cards so it only takes the height of the table).
        previewPicture = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        previewCaption = Ui.Wrapping("", 330, gray: true);
        previewCaption.Dock = DockStyle.Bottom;
        var previewCard = new Card { Title = L.T("Map preview"), Dock = DockStyle.Right, Width = 380, Margin = Padding.Empty };
        previewCard.Controls.Add(previewPicture);
        previewCard.Controls.Add(previewCaption);
        previewTimer = new System.Windows.Forms.Timer { Interval = 150 };
        previewTimer.Tick += (_, _) =>
        {
            previewTimer.Stop();
            ShowPreview();
        };

        var dungeonsPanel = new Panel { Dock = DockStyle.Fill };
        dungeonsPanel.Controls.Add(Ui.Framed(grid));
        dungeonsPanel.Controls.Add(previewCard);
        dungeonsPanel.Controls.Add(settingsCard);
        dungeonsPanel.Controls.Add(toolbar);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(nexusCard, 0, 0);
        root.Controls.Add(dungeonsPanel, 0, 1);

        // In a small window the page scrolls instead of squeezing the table to a few rows.
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, AutoScrollMinSize = new Size(0, 620) };
        scroller.Controls.Add(root);
        Controls.Add(scroller);

        watchTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        watchTimer.Tick += (_, _) => CheckExternalChanges();
        watchTimer.Start();

        Reload();
        RequestPreview(SelectedText(nexusMap));
    }

    // A wrapping flow inside a card needs to know how wide it may be, or it does not grow taller when it wraps.
    private static void FitWidth(Card card, FlowLayoutPanel flow)
    {
        card.SizeChanged += (_, _) =>
            flow.MaximumSize = new Size(Math.Max(120, card.ClientSize.Width - card.Padding.Horizontal), 0);
    }

    // A name over its field.
    private static Control Field(string name, Control field)
    {
        var block = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 22, 8) };
        if (name.Length > 0)
        {
            Label label = Ui.Note(name);
            label.Margin = new Padding(3, 0, 3, 2);
            block.Controls.Add(label);
        }
        else
        {
            // A switch has its name in itself; keep it level with the fields next to it.
            field.Margin = new Padding(3, 22, 3, 3);
        }
        block.Controls.Add(field);
        return block;
    }

    public void Reload()
    {
        loading = true;
        try
        {
            grid.Rows.Clear();
            if (!DungeonCatalogFile.Exists)
            {
                loadedStamp = DateTime.MinValue;
                Ui.SetStatus(statusLabel, L.F("dungeons.json not found in {0} (without it the server only uses Iron Lair and Bomb Factory).", DungeonCatalogFile.FilePath), TextRole.Warn);
                return;
            }

            loadedStamp = File.GetLastWriteTimeUtc(DungeonCatalogFile.FilePath);
            DungeonCatalogFile.Catalog catalog = DungeonCatalogFile.Load();

            List<string> maps = DungeonCatalogFile.AvailableMaps();
            foreach (string used in catalog.Dungeons.Select(d => d.Map).Where(m => m.Length > 0))
            {
                if (!maps.Contains(used, StringComparer.OrdinalIgnoreCase)) maps.Add(used);
            }
            mapColumn.Items.Clear();
            foreach (string map in maps) mapColumn.Items.Add(map);
            FillMapChoice(nexusMap, maps, catalog.NexusMap, DungeonCatalogFile.DefaultNexusMap);
            FillMapChoice(tutorialMap, maps, catalog.TutorialMap, DungeonCatalogFile.DefaultTutorialMap);

            maxPlayers.Value = Math.Clamp(catalog.MaxPlayers, (int)maxPlayers.Minimum, (int)maxPlayers.Maximum);
            autoExit.Value = (decimal)Math.Clamp(catalog.AutoExitSeconds, (double)autoExit.Minimum, (double)autoExit.Maximum);
            portalsOn.Checked = catalog.NexusPortalsOn;
            int modeIndex = Array.FindIndex(EntryModes, m => string.Equals(m.Value, catalog.EntryMode, StringComparison.OrdinalIgnoreCase));
            entryMode.SelectedIndex = modeIndex >= 0 ? modeIndex : 0;
            lockBossRooms.Checked = catalog.LockBossRooms;
            fireRateScale.Value = (decimal)Math.Clamp(catalog.FireRateScale, (double)fireRateScale.Minimum, (double)fireRateScale.Maximum);
            maxBullets.Value = (decimal)Math.Clamp(catalog.MaxBulletsPerSecond, (double)maxBullets.Minimum, (double)maxBullets.Maximum);

            foreach (DungeonCatalogFile.Dungeon dungeon in catalog.Dungeons)
            {
                string map = maps.FirstOrDefault(m => string.Equals(m, dungeon.Map, StringComparison.OrdinalIgnoreCase)) ?? dungeon.Map;
                int index = grid.Rows.Add(
                    dungeon.Active,
                    dungeon.Name,
                    dungeon.Type,
                    map,
                    dungeon.InNexus,
                    dungeon.HealthMultiplier.ToString("0.###", CultureInfo.InvariantCulture),
                    dungeon.OpensAt,
                    dungeon.Note);
                grid.Rows[index].Tag = dungeon.Type;
                if (map.Length > 0 && !DungeonMapExists(map))
                    grid.Rows[index].Cells["map"].Style.ForeColor = Theme.ColorOf(TextRole.Bad);
            }

            dirty = false;
            Ui.SetStatus(statusLabel, L.F("{0} of {1} dungeon(s) active.", catalog.Dungeons.Count(d => d.Active), catalog.Dungeons.Count), TextRole.Muted);
        }
        catch (Exception e)
        {
            Ui.SetStatus(statusLabel, L.F("Could not read dungeons.json: {0}", e.Message), TextRole.Bad);
        }
        finally
        {
            loading = false;
            UpdateButtons();
        }
    }

    // Fills the choice of the Nexus / tutorial map with the map files and selects the one in use. A name without a
    // file stays in the list (the server uses the original map for it).
    private static void FillMapChoice(ThemedComboBox box, List<string> maps, string chosen, string original)
    {
        box.Items.Clear();
        // The original first, then the community versions (the dungeon and catalog maps are not offered).
        box.Items.Add(original);
        foreach (string map in maps.Where(m => !m.StartsWith("dungeon", StringComparison.OrdinalIgnoreCase) &&
                                               !m.StartsWith("map_maker", StringComparison.OrdinalIgnoreCase) &&
                                               !string.Equals(m, original, StringComparison.OrdinalIgnoreCase)))
            box.Items.Add(map);
        int index = box.Items.Cast<string>().ToList().FindIndex(m => string.Equals(m, chosen, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            box.Items.Add(chosen);
            index = box.Items.Count - 1;
        }
        box.SelectedIndex = index;
    }

    private string MapOfRow(int row) =>
        row >= 0 && row < grid.Rows.Count ? Convert.ToString(grid.Rows[row].Cells["map"].Value) ?? "" : "";

    // Asks for the picture of a map file; it is drawn a moment later (moving through the rows does not draw each one).
    private void RequestPreview(string map)
    {
        if (map.Length == 0) return;
        previewMap = map;
        previewTimer.Stop();
        previewTimer.Start();
    }

    private void ShowPreview()
    {
        string map = previewMap;
        if (map.Length == 0) return;
        string path = Path.Combine(LauncherPaths.MapsFolder, map + ".json");
        if (!File.Exists(path))
        {
            previewShown = "";
            previewPicture.Image = null;
            Ui.SetStatus(previewCaption, L.F("{0}: the map file was not found in the maps folder.", map), TextRole.Bad);
            return;
        }

        DateTime stamp = File.GetLastWriteTimeUtc(path);
        try
        {
            if (!previewCache.TryGetValue(path, out var cached) || cached.Stamp != stamp)
            {
                MapThumbnail.Result drawn = MapThumbnail.Render(path, 640, 480);
                if (previewCache.TryGetValue(path, out var old))
                {
                    if (ReferenceEquals(previewPicture.Image, old.Picture)) previewPicture.Image = null;
                    old.Picture.Dispose();
                }
                cached = (stamp, drawn.Picture, drawn.Description);
                previewCache[path] = cached;
            }
            previewPicture.Image = cached.Picture;
            previewShown = map;
            Ui.SetStatus(previewCaption, map + ": " + cached.Description, TextRole.Muted);
        }
        catch (Exception e)
        {
            previewPicture.Image = null;
            Ui.SetStatus(previewCaption, L.F("{0}: the map could not be drawn ({1}).", map, e.Message), TextRole.Bad);
        }
    }

    private static string SelectedText(ComboBox box) => box.SelectedItem as string ?? "";

    private void OpenSelectedDungeon()
    {
        grid.EndEdit();
        DataGridViewRow? row = grid.CurrentRow;
        if (row == null)
        {
            Ui.ShowError(FindForm(), L.T("Select a dungeon in the table first."));
            return;
        }
        OpenInEditor(Convert.ToString(row.Cells["map"].Value) ?? "");
    }

    // Loads a map file of the server in the Map editor tab.
    private void OpenInEditor(string map)
    {
        if (map.Length == 0)
        {
            Ui.ShowError(FindForm(), L.T("There is no map file to open."));
            return;
        }
        if (context.OpenMapInEditor == null)
        {
            Ui.ShowError(FindForm(), L.T("The Map editor tab is not available."));
            return;
        }
        context.OpenMapInEditor(map);
    }

    private static bool DungeonMapExists(string map) => File.Exists(Path.Combine(LauncherPaths.MapsFolder, map + ".json"));

    private void MarkDirty()
    {
        if (loading) return;
        dirty = true;
        UpdateButtons();
        Ui.SetStatus(statusLabel, L.T("Unsaved changes."), TextRole.Warn);
    }

    private void UpdateButtons()
    {
        saveButton.Enabled = dirty;
        undoButton.Enabled = dirty;
    }

    private void Save()
    {
        grid.EndEdit();
        ValidateChildren();

        var catalog = new DungeonCatalogFile.Catalog
        {
            MaxPlayers = (int)maxPlayers.Value,
            AutoExitSeconds = (double)autoExit.Value,
            NexusPortalsOn = portalsOn.Checked,
            EntryMode = EntryModes[Math.Max(0, entryMode.SelectedIndex)].Value,
            LockBossRooms = lockBossRooms.Checked,
            FireRateScale = (double)fireRateScale.Value,
            MaxBulletsPerSecond = (double)maxBullets.Value,
            NexusMap = SelectedText(nexusMap).Length > 0 ? SelectedText(nexusMap) : DungeonCatalogFile.DefaultNexusMap,
            TutorialMap = SelectedText(tutorialMap).Length > 0 ? SelectedText(tutorialMap) : DungeonCatalogFile.DefaultTutorialMap
        };
        var errors = new List<string>();
        foreach (DataGridViewRow row in grid.Rows)
        {
            string type = row.Tag as string ?? Convert.ToString(row.Cells["type"].Value) ?? "";
            string name = Convert.ToString(row.Cells["name"].Value) ?? "";
            string label = name.Length > 0 ? name : type;
            string healthText = (Convert.ToString(row.Cells["health"].Value) ?? "").Trim().Replace(',', '.');
            string opens = (Convert.ToString(row.Cells["opens"].Value) ?? "").Trim();

            if (!double.TryParse(healthText, NumberStyles.Float, CultureInfo.InvariantCulture, out double health) || health <= 0)
            {
                errors.Add(L.F("{0}: \"Health ×\" must be a number greater than zero (for example 1 or 1.5).", label));
                continue;
            }
            if (!DungeonCatalogFile.IsValidOpensAt(opens))
            {
                errors.Add(L.F("{0}: \"Opens at\" must be yyyy-mm-dd hh:mm (for example 2026-11-20 18:00) or empty.", label));
                continue;
            }

            catalog.Dungeons.Add(new DungeonCatalogFile.Dungeon
            {
                Type = type,
                Name = name,
                Map = Convert.ToString(row.Cells["map"].Value) ?? "",
                Active = row.Cells["active"].Value is bool active && active,
                InNexus = row.Cells["nexus"].Value is bool inNexus && inNexus,
                HealthMultiplier = health,
                OpensAt = opens,
                Note = Convert.ToString(row.Cells["note"].Value) ?? ""
            });
        }
        if (errors.Count > 0)
        {
            Ui.ShowError(FindForm(), L.T("Fix these before saving:") + "\n\n" + string.Join("\n", errors));
            return;
        }

        try
        {
            DungeonCatalogFile.Save(catalog);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not save dungeons.json:\n{0}", e.Message));
            return;
        }
        Reload();
        Ui.SetStatus(statusLabel, L.F("Saved at {0}. The server uses the new list for the next entries.", DateTime.Now.ToString("HH:mm:ss")), TextRole.Good);
    }

    private void CheckExternalChanges()
    {
        DateTime stamp = DungeonCatalogFile.Exists ? File.GetLastWriteTimeUtc(DungeonCatalogFile.FilePath) : DateTime.MinValue;
        if (stamp == loadedStamp) return;
        if (!dirty)
        {
            Reload();
            return;
        }
        Ui.SetStatus(statusLabel, L.T("dungeons.json changed outside the launcher. Saving writes the values of this screen over the fields edited here."), TextRole.Warn);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            watchTimer.Stop();
            watchTimer.Dispose();
            previewTimer.Stop();
            previewTimer.Dispose();
            previewPicture.Image = null;
            foreach (var cached in previewCache.Values) cached.Picture.Dispose();
            previewCache.Clear();
        }
        base.Dispose(disposing);
    }
}
