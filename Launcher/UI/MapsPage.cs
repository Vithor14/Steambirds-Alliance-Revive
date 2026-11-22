using System.Globalization;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// "Maps and dungeons" page of the Server tab.
//  - Map of each dungeon, when it opens, whether it is on and how much health its enemies have (dungeons.json).
//  - Nexus and tutorial maps: fixed in the server for now (maps\nexus.json and maps\tutorial_walker.json).
//  - Map preview: not implemented yet.
internal sealed class MapsPage : UserControl
{
    private readonly DataGridView grid;
    private readonly DataGridViewComboBoxColumn mapColumn;
    private readonly NumericUpDown maxPlayers;
    private readonly NumericUpDown autoExit;
    private readonly CheckBox portalsOn;
    private readonly ComboBox entryMode;
    private readonly CheckBox lockBossRooms;
    private readonly NumericUpDown fireRateScale;
    private readonly NumericUpDown maxBullets;
    private readonly Button saveButton;
    private readonly Button undoButton;
    private readonly Label statusLabel;
    private readonly System.Windows.Forms.Timer watchTimer;
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

    public MapsPage(ToolTip tip)
    {
        // ------------------------------------------------------------ fixed maps (not selectable yet)
        var nexusMap = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        nexusMap.Items.Add("nexus");
        nexusMap.SelectedIndex = 0;
        var tutorialMap = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        tutorialMap.Items.Add("tutorial_walker");
        tutorialMap.SelectedIndex = 0;
        Ui.Disable(nexusMap, tutorialMap);

        TableLayoutPanel fixedTable = Ui.Table(3);
        Ui.AddRow(fixedTable, Ui.Text(L.T("Nexus (Rebel City):")), nexusMap, Ui.ComingSoon(tip,
            L.T("The server always uses maps\\nexus.json. Choosing another map file for the Nexus is not implemented yet.")));
        Ui.AddRow(fixedTable, Ui.Text(L.T("Tutorial (Training Grounds):")), tutorialMap, Ui.ComingSoon(tip,
            L.T("The server always uses maps\\tutorial_walker.json. Choosing another map file for the tutorial is not implemented yet.")));

        // ------------------------------------------------------------ dungeons
        saveButton = Ui.Button(L.T("Save"), (_, _) => Save());
        undoButton = Ui.Button(L.T("Undo changes"), (_, _) => Reload());
        saveButton.Enabled = false;
        undoButton.Enabled = false;
        statusLabel = Ui.Note("");
        var previewButton = Ui.Button(L.T("View the selected map"));
        Ui.Disable(previewButton);

        var toolbar = Ui.WrapFlow(saveButton, undoButton, previewButton,
            Ui.ComingSoon(tip, L.T("Viewing each map here on the server screen is not implemented yet.\nOpen a map in the Map editor tab to see it.")),
            statusLabel);
        toolbar.Dock = DockStyle.Top;

        maxPlayers = new NumericUpDown { Minimum = 1, Maximum = 100, Width = 70, Margin = new Padding(3, 4, 12, 3) };
        autoExit = new NumericUpDown { Minimum = 0, Maximum = 3600, Width = 80, Margin = new Padding(3, 4, 3, 3) };
        portalsOn = new CheckBox { Text = L.T("Dungeon carriers in the Nexus"), AutoSize = true, Margin = new Padding(12, 6, 3, 3) };
        maxPlayers.ValueChanged += (_, _) => MarkDirty();
        autoExit.ValueChanged += (_, _) => MarkDirty();
        portalsOn.CheckedChanged += (_, _) => MarkDirty();
        var generalRow = Ui.WrapFlow(
            Ui.Text(L.T("Max players per dungeon:")), maxPlayers,
            Ui.Text(L.T("Automatic exit after the boss (seconds):")), autoExit,
            portalsOn);
        generalRow.Dock = DockStyle.Top;

        // How players enter dungeons, boss room locks and how hard the enemies shoot (dungeons.json).
        entryMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330, Margin = new Padding(3, 4, 12, 3) };
        foreach ((string value, string label) in EntryModes) entryMode.Items.Add(label);
        entryMode.SelectedIndex = 0;
        lockBossRooms = new CheckBox { Text = L.T("Lock the doors of a boss room behind the player"), AutoSize = true, Margin = new Padding(3, 6, 12, 3) };
        fireRateScale = new NumericUpDown { Minimum = 0.1m, Maximum = 5m, DecimalPlaces = 2, Increment = 0.1m, Value = 1m, Width = 70, Margin = new Padding(3, 4, 12, 3) };
        maxBullets = new NumericUpDown { Minimum = 0, Maximum = 200, Width = 70, Value = 10, Margin = new Padding(3, 4, 3, 3) };
        entryMode.SelectedIndexChanged += (_, _) => MarkDirty();
        lockBossRooms.CheckedChanged += (_, _) => MarkDirty();
        fireRateScale.ValueChanged += (_, _) => MarkDirty();
        maxBullets.ValueChanged += (_, _) => MarkDirty();
        tip.SetToolTip(entryMode, L.T("both: the carriers outside the city and the Mission Transport (the mega carrier at the end of the main ramp, which lists the dungeons as missions). portals: only the carriers. missionTransport: only the mission list."));
        tip.SetToolTip(maxBullets, L.T("The most bullets per second one dungeon enemy may put out (its guns slow down together until it fits). 0 = no limit. Lower = easier to dodge."));
        tip.SetToolTip(fireRateScale, L.T("Multiplies the fire rate of every dungeon enemy (0.5 = half as fast)."));
        var entryRow = Ui.WrapFlow(
            Ui.Text(L.T("How players enter dungeons:")), entryMode,
            lockBossRooms);
        entryRow.Dock = DockStyle.Top;
        var fireRow = Ui.WrapFlow(
            Ui.Text(L.T("Enemy fire rate ×:")), fireRateScale,
            Ui.Text(L.T("Max bullets per second per enemy (0 = no limit):")), maxBullets);
        fireRow.Dock = DockStyle.Top;

        var help = Ui.Wrapping(L.T("Active: the dungeon shows in the Nexus and accepts players. Map file: any .json of the server\\maps folder (the community can make other versions of each dungeon). Health ×: multiplies the health of the dungeon's enemies. Opens at: date and time of this PC as yyyy-mm-dd hh:mm (empty = already open). The server reads dungeons.json again by itself; changes apply to the next entries."), 1000, gray: true);
        help.Dock = DockStyle.Top;

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
        grid.CellValueChanged += (_, _) => MarkDirty();
        grid.DataError += (_, e) => e.ThrowException = false;

        var dungeonsPanel = new Panel { Dock = DockStyle.Fill };
        dungeonsPanel.Resize += (_, _) => help.MaximumSize = new Size(Math.Max(200, dungeonsPanel.ClientSize.Width - 10), 0);
        dungeonsPanel.Controls.Add(grid);
        dungeonsPanel.Controls.Add(help);
        dungeonsPanel.Controls.Add(fireRow);
        dungeonsPanel.Controls.Add(entryRow);
        dungeonsPanel.Controls.Add(generalRow);
        dungeonsPanel.Controls.Add(toolbar);

        var dungeonsGroup = new GroupBox { Text = L.T("Dungeons (dungeons.json)"), Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6) };
        dungeonsGroup.Controls.Add(dungeonsPanel);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.Controls.Add(Ui.Group(L.T("Nexus and tutorial maps"), fixedTable), 0, 0);
        root.Controls.Add(dungeonsGroup, 0, 1);
        Controls.Add(root);

        watchTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        watchTimer.Tick += (_, _) => CheckExternalChanges();
        watchTimer.Start();

        Reload();
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
            MaxBulletsPerSecond = (double)maxBullets.Value
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
        }
        base.Dispose(disposing);
    }
}
