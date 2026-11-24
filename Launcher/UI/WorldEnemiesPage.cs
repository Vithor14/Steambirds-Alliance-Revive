using System.Globalization;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.UI;

// "Enemies" tab of the open world (Server > World map): how enemies spawn (world.json > enemies: how many at once, how close a player
// must be...) and which enemies live in each biome, group by group. The lists are saved in world-enemies.json; a biome that was never
// changed keeps the game's own lists (world-enemy-defaults.json, written by the server). The server reads both files again by itself.
internal sealed class WorldEnemiesPage : UserControl
{
    // One number of world.json > enemies.
    private sealed class Field
    {
        public Field(string key, string label, string tip, decimal min, decimal max, int decimals, decimal increment, decimal fallback)
        {
            Key = key;
            Label = label;
            Tip = tip;
            Min = min;
            Max = max;
            Decimals = decimals;
            Increment = increment;
            Fallback = fallback;
        }

        public string Key { get; }
        public string Label { get; }
        public string Tip { get; }
        public decimal Min { get; }
        public decimal Max { get; }
        public int Decimals { get; }
        public decimal Increment { get; }
        public decimal Fallback { get; }
        public NumberBox Box { get; set; } = null!;
        public decimal Loaded { get; set; }
    }

    private readonly LauncherContext context;
    private readonly List<Field> fields;
    private readonly ThemedCheckBox enabledBox;
    private readonly ThemedButton saveSettings;
    private readonly ThemedButton undoSettings;
    private readonly Label settingsStatus;
    private bool enabledLoaded = true;
    private bool loadingSettings;

    private readonly ThemedComboBox regionBox;
    private readonly ThemedComboBox bossBox;
    private readonly ThemedListView list;
    private readonly Label listStatus;
    private readonly Label hint;
    private readonly ThemedButton addButton;
    private readonly ThemedButton removeButton;
    private readonly ThemedButton removeGroupButton;
    private readonly ThemedButton renameGroupButton;
    private readonly ThemedButton resetButton;
    private readonly ThemedButton saveLists;
    private readonly ThemedButton undoLists;
    private List<WorldEnemyRegion> defaults = new List<WorldEnemyRegion>();
    private Dictionary<string, WorldEnemyRegion> edits = new Dictionary<string, WorldEnemyRegion>(StringComparer.OrdinalIgnoreCase);
    private bool listsDirty;
    private bool fillingRegion;
    private DateTime defaultsStamp = DateTime.MinValue;

    public WorldEnemiesPage(LauncherContext context)
    {
        this.context = context;
        ToolTip tip = context.ToolTip;

        // ---------------------------------------------------------------- numbers
        fields = new List<Field>
        {
            new Field("spawnRadius", L.T("A spawn site wakes up when a player is closer than"),
                L.T("Distance (units) from a player at which a spawn site creates its enemies. Farther away the land is empty."), 40, 400, 0, 10, 150),
            new Field("despawnRadius", L.T("Enemies are taken away when every player is farther than"),
                L.T("Distance (units) from every player beyond which an enemy group is removed. It stays at least 30 units above the spawn distance."), 70, 800, 0, 10, 260),
            new Field("wakeRadius", L.T("A sleeping enemy attacks when a player is closer than"),
                L.T("Enemies are created asleep. They wake up when a player comes this close (units) or when they are hit."), 10, 300, 0, 5, 75),
            new Field("viewRadius", L.T("A player sees enemies within (0 = all)"),
                L.T("A client is only told about the enemies within this distance of its plane, so the radar does not fill with arrows. 0 tells every player about every enemy. Keep it above the wake distance."), 0, 800, 0, 10, 90),
            new Field("maxAlive", L.T("Most enemies alive at once"),
                L.T("Limit of enemies from spawn sites in the whole world (the server's own limit is 140 per instance)."), 1, 130, 0, 5, 100),
            new Field("siteSpacingTiles", L.T("Distance between spawn sites (tiles)"),
                L.T("A tile is 12 units. Smaller = more spawn sites, so more enemies. Applies the next time the world is built."), 1.5m, 30, 1, 0.5m, 4),
            new Field("groupSizeMax", L.T("Most enemies in a group"),
                L.T("A group is a leader and its followers; a spawn site creates between 2 and this many."), 1, 8, 0, 1, 4),
            new Field("respawnScale", L.T("Respawn time multiplier"),
                L.T("A spawn site rests, after its enemies die, the biome's time (from the game) times the enemies it had times this. 0.5 = twice as fast, 2 = twice as slow."), 0.05m, 100, 2, 0.25m, 1),
            new Field("healthMultiplier", L.T("Health multiplier of the enemies"),
                L.T("Multiplies the health of every enemy and boss the open world creates."), 0.05m, 100, 2, 0.25m, 1)
        };

        enabledBox = new ThemedCheckBox { Text = L.T("Enemies in the open world") };
        enabledBox.CheckedChanged += (_, _) => UpdateSettingsButtons();
        tip.SetToolTip(enabledBox, L.T("Off: the open world is just the map, with no enemies."));

        var numbers = Ui.Table(2);
        numbers.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        numbers.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        numbers.Padding = new Padding(0, 0, 8, 0);
        Ui.AddRow(numbers, enabledBox, null);
        foreach (Field field in fields)
        {
            Field captured = field;
            Label label = Ui.Text(field.Label);
            label.Dock = DockStyle.Fill;
            label.AutoSize = false;
            label.Height = 30;
            label.TextAlign = ContentAlignment.MiddleLeft;
            captured.Box = new NumberBox
            {
                Minimum = field.Min,
                Maximum = field.Max,
                DecimalPlaces = field.Decimals,
                Increment = field.Increment,
                Value = field.Fallback,
                Width = 90,
                TextAlign = HorizontalAlignment.Right,
                Margin = new Padding(3, 3, 3, 3)
            };
            captured.Box.ValueChanged += (_, _) =>
            {
                if (!loadingSettings) UpdateSettingsButtons();
            };
            tip.SetToolTip(label, field.Tip);
            tip.SetToolTip(captured.Box, field.Tip);
            Ui.AddRow(numbers, label, captured.Box);
        }

        saveSettings = Ui.Primary(L.T("Save"), (_, _) => SaveSettings());
        undoSettings = Ui.Button(L.T("Undo changes"), (_, _) => LoadSettings());
        settingsStatus = Ui.Wrapping("", 420, gray: true);
        FlowLayoutPanel settingsButtons = Ui.WrapFlow(saveSettings, undoSettings);
        settingsButtons.Dock = DockStyle.Top;
        settingsStatus.Dock = DockStyle.Top;

        var settingsBody = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        numbers.Dock = DockStyle.Top;
        // Docked controls: the last one added is laid out first.
        settingsBody.Controls.Add(settingsStatus);
        settingsBody.Controls.Add(settingsButtons);
        settingsBody.Controls.Add(numbers);
        Card settingsCard = Ui.Group(L.T("How enemies spawn"), settingsBody);
        settingsCard.AutoSize = false;
        settingsCard.Dock = DockStyle.Left;
        settingsCard.Width = 470;
        settingsBody.Dock = DockStyle.Fill;

        // ---------------------------------------------------------------- lists
        regionBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 270 };
        regionBox.SelectedIndexChanged += (_, _) =>
        {
            if (!fillingRegion) ShowRegion();
        };

        bossBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 180 };
        bossBox.SelectedIndexChanged += (_, _) => BossEdited();
        bossBox.Leave += (_, _) => BossEdited();
        bossBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            BossEdited();
        };
        tip.SetToolTip(bossBox, L.T("The boss that \"Spawn boss here\" (right click on the map) creates in this biome. The list has the boss units of the game for the biome; you can also type the name of any unit."));

        FlowLayoutPanel top = Ui.WrapFlow(Ui.Text(L.T("Biome:")), regionBox, Ui.Text(L.T("Boss of the biome:")), bossBox);
        top.Dock = DockStyle.Top;

        listStatus = Ui.Wrapping("", 640, gray: true);
        listStatus.Dock = DockStyle.Top;

        list = Ui.Grid((L.T("Group"), 210), (L.T("Role"), 90), (L.T("Enemy"), 170), (L.T("Tier"), 45), (L.T("Health"), 70));
        list.MultiSelect = true;
        list.SelectedIndexChanged += (_, _) => UpdateListButtons();
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Delete) return;
            e.Handled = true;
            RemoveSelected();
        };

        addButton = Ui.Button(L.T("Add enemy..."), (_, _) => AddEnemies());
        removeButton = Ui.Button(L.T("Remove"), (_, _) => RemoveSelected());
        removeGroupButton = Ui.Button(L.T("Remove group"), (_, _) => RemoveGroup());
        renameGroupButton = Ui.Button(L.T("Rename group..."), (_, _) => RenameGroup());
        resetButton = Ui.Button(L.T("Back to the game's list"), (_, _) => ResetRegion());
        tip.SetToolTip(addButton, L.T("Adds units of the game to a group of this biome, or to a new group."));
        tip.SetToolTip(removeButton, L.T("Takes the selected enemies out of the list of this biome (Delete)."));
        tip.SetToolTip(resetButton, L.T("Drops what you changed in this biome (the groups and the boss): it goes back to the game's own list."));
        FlowLayoutPanel editButtons = Ui.WrapFlow(addButton, removeButton, removeGroupButton, renameGroupButton, resetButton);
        editButtons.Dock = DockStyle.Bottom;

        saveLists = Ui.Primary(L.T("Save lists"), (_, _) => SaveLists());
        undoLists = Ui.Button(L.T("Undo changes"), (_, _) => LoadLists(true));
        FlowLayoutPanel saveButtons = Ui.WrapFlow(saveLists, undoLists);
        saveButtons.Dock = DockStyle.Bottom;

        hint = Ui.Wrapping(L.T("Each spawn site of a biome picks one of these groups at random: a leader with followers, or one enemy of a solo group. " +
                               "The enemies are the game's units with the default AI; to change how one fights use the Enemy AI tab."), 640, gray: true);
        hint.Dock = DockStyle.Bottom;

        var listsBody = new Panel { Dock = DockStyle.Fill };
        // Fill first, then the others (WinForms docks the last added control first).
        listsBody.Controls.Add(Ui.Framed(list));
        listsBody.Controls.Add(hint);
        listsBody.Controls.Add(saveButtons);
        listsBody.Controls.Add(editButtons);
        listsBody.Controls.Add(listStatus);
        listsBody.Controls.Add(top);
        Card listsCard = Ui.Group(L.T("Enemies of each biome"), listsBody);
        listsCard.AutoSize = false;
        listsCard.Dock = DockStyle.Fill;
        listsBody.Dock = DockStyle.Fill;

        Controls.Add(listsCard);
        Controls.Add(new Panel { Dock = DockStyle.Left, Width = 8 });
        Controls.Add(settingsCard);

        LoadSettings();
        LoadLists(false);

        // Back on this tab after the server wrote the defaults (it does so when it builds the world): pick them up.
        VisibleChanged += (_, _) =>
        {
            if (!Visible) return;
            if (!listsDirty && DefaultsStampNow() != defaultsStamp) LoadLists(false);
        };
    }

    private static DateTime DefaultsStampNow()
    {
        try
        {
            return File.Exists(WorldEnemyFiles.DefaultsPath) ? File.GetLastWriteTimeUtc(WorldEnemyFiles.DefaultsPath) : DateTime.MinValue;
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }

    // ================================================================== numbers (world.json > enemies)

    private static string WorldFile => LauncherPaths.ServerFile("world.json");

    private void LoadSettings()
    {
        loadingSettings = true;
        try
        {
            Dictionary<string, JsoncProperty> values = ReadEnemiesObject(out string? problem);
            foreach (Field field in fields)
            {
                decimal value = field.Fallback;
                if (values.TryGetValue(field.Key, out JsoncProperty? property) &&
                    decimal.TryParse(property.RawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal parsed))
                    value = parsed;
                value = Math.Min(field.Max, Math.Max(field.Min, value));
                field.Box.Value = value;
                field.Loaded = value;
            }
            enabledLoaded = !values.TryGetValue("enabled", out JsoncProperty? enabled) || enabled.RawValue != "false";
            enabledBox.Checked = enabledLoaded;
            if (problem != null) Ui.SetStatus(settingsStatus, problem, TextRole.Warn);
            else Ui.SetStatus(settingsStatus, L.T("The server reads world.json by itself: changes apply at once, except the distance between spawn sites (next world)."), TextRole.Muted);
        }
        finally
        {
            loadingSettings = false;
        }
        UpdateSettingsButtons();
    }

    private Dictionary<string, JsoncProperty> ReadEnemiesObject(out string? problem)
    {
        problem = null;
        var result = new Dictionary<string, JsoncProperty>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(WorldFile))
            {
                problem = L.T("world.json does not exist yet. Start the server once: it creates the file with these values.");
                return result;
            }
            string text = TextFiles.ReadKeepingBom(WorldFile, out _);
            JsoncProperty? parent = JsoncFile.ReadTopLevel(text)
                .FirstOrDefault(p => p.Kind == JsoncKind.Object && p.Key.Equals("enemies", StringComparison.OrdinalIgnoreCase));
            if (parent == null)
            {
                problem = L.T("world.json has no \"enemies\" section: these are the defaults. Saving cannot change the file.");
                return result;
            }
            foreach (JsoncProperty property in JsoncFile.ReadTopLevel(parent.RawValue)) result[property.Key] = property;
        }
        catch (Exception e)
        {
            problem = L.F("Could not read world.json: {0}", e.Message);
        }
        return result;
    }

    private bool SettingsChanged => enabledBox.Checked != enabledLoaded || fields.Any(f => f.Box.Value != f.Loaded);

    private void UpdateSettingsButtons()
    {
        if (loadingSettings) return;
        saveSettings.Enabled = SettingsChanged;
        undoSettings.Enabled = SettingsChanged;
    }

    private void SaveSettings()
    {
        try
        {
            if (!File.Exists(WorldFile))
            {
                Ui.ShowInfo(FindForm(), L.T("world.json does not exist yet. Start the server once: it creates the file."));
                return;
            }
            var changes = new Dictionary<string, string>();
            foreach (Field field in fields)
            {
                if (field.Box.Value == field.Loaded) continue;
                changes[field.Key] = JsoncFile.FormatNumber(field.Box.Value, field.Decimals > 0);
            }
            if (enabledBox.Checked != enabledLoaded) changes["enabled"] = JsoncFile.FormatBool(enabledBox.Checked);

            string text = TextFiles.ReadKeepingBom(WorldFile, out bool hasBom);
            string result = JsoncFile.ApplyChangesIn(text, "enemies", changes, out List<string> missing);
            if (missing.Count > 0)
            {
                Ui.ShowError(FindForm(), L.T("world.json has no \"enemies\" section."));
                return;
            }
            TextFiles.WriteAtomic(WorldFile, result, hasBom);
            LoadSettings();
            Ui.SetStatus(settingsStatus, L.F("Saved at {0}. The server reads it again by itself.", DateTime.Now.ToString("HH:mm:ss")), TextRole.Good);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not change {0}:\n{1}", WorldFile, e.Message));
        }
    }

    // ================================================================== lists (world-enemies.json)

    private WorldEnemyRegion? Current => regionBox.SelectedIndex >= 0 && regionBox.SelectedIndex < defaults.Count ? defaults[regionBox.SelectedIndex] : null;

    private void LoadLists(bool keepRegion)
    {
        string? keep = keepRegion ? Current?.Key : null;
        defaultsStamp = DefaultsStampNow();
        string? error = null;
        List<WorldEnemyRegion>? loaded = WorldEnemyFiles.LoadDefaults(out error);
        defaults = loaded ?? new List<WorldEnemyRegion>();
        try
        {
            edits = WorldEnemyFiles.LoadEdits();
        }
        catch (Exception e)
        {
            edits = new Dictionary<string, WorldEnemyRegion>(StringComparer.OrdinalIgnoreCase);
            error = L.F("Could not read {0}: {1}", WorldEnemyFiles.EditsFileName, e.Message);
        }
        listsDirty = false;

        fillingRegion = true;
        regionBox.Items.Clear();
        foreach (WorldEnemyRegion region in defaults)
            regionBox.Items.Add((region.Zone.Length > 0 ? region.Zone : region.Key) + "  (" + region.Key + ", " + L.F("tier {0}", region.Tier) + ")");
        fillingRegion = false;
        if (defaults.Count > 0)
        {
            int index = keep == null ? 0 : Math.Max(0, defaults.FindIndex(r => r.Key == keep));
            regionBox.SelectedIndex = index;
        }
        bool any = defaults.Count > 0;
        regionBox.Enabled = bossBox.Enabled = any;
        if (!any)
        {
            list.Items.Clear();
            bossBox.Items.Clear();
            bossBox.Text = "";
            Ui.SetStatus(listStatus, error ?? "", TextRole.Warn);
        }
        UpdateListButtons();
    }

    private WorldEnemyRegion? EditOf(WorldEnemyRegion region) => edits.TryGetValue(region.Key, out WorldEnemyRegion? edit) ? edit : null;

    // The groups the biome has now: the host's list, or the game's.
    private List<WorldEnemyGroup> EffectiveGroups(WorldEnemyRegion region) => EditOf(region)?.Groups ?? region.Groups ?? new List<WorldEnemyGroup>();

    // The biome's list is going to change: from here on it is the host's (a copy of the game's list the first time).
    private List<WorldEnemyGroup> Customize(WorldEnemyRegion region)
    {
        WorldEnemyRegion? edit = EditOf(region);
        if (edit == null)
        {
            edit = new WorldEnemyRegion { Key = region.Key };
            edits[region.Key] = edit;
        }
        edit.Groups ??= (region.Groups ?? new List<WorldEnemyGroup>()).Select(g => g.Copy()).ToList();
        return edit.Groups;
    }

    private void ShowRegion()
    {
        WorldEnemyRegion? region = Current;
        if (region == null) return;

        bossBox.Items.Clear();
        foreach (string choice in region.BossChoices) bossBox.Items.Add(choice);
        string boss = EditOf(region)?.Boss ?? "";
        bossBox.Text = boss.Length > 0 ? boss : region.Boss;

        Fill();
    }

    private static string GroupName(WorldEnemyGroup group, int index) => group.Name.Length > 0 ? group.Name : L.F("group {0}", index + 1);

    private void Fill()
    {
        WorldEnemyRegion? region = Current;
        if (region == null) return;
        List<(int Group, bool Leader, string Name)> selected = SelectedRows().ToList();
        List<WorldEnemyGroup> groups = EffectiveGroups(region);

        list.BeginUpdate();
        list.Items.Clear();
        for (int g = 0; g < groups.Count; g++)
        {
            WorldEnemyGroup group = groups[g];
            if (group.Leaders.Count == 0 && group.Members.Count == 0)
            {
                AddRow(g, GroupName(group, g), "", true, "", "");
                continue;
            }
            foreach (string leader in group.Leaders) AddRow(g, GroupName(group, g), L.T("Leader"), true, leader, leader);
            foreach (string member in group.Members)
                AddRow(g, GroupName(group, g), group.Solo ? L.T("Alone") : L.T("Follower"), false, member, member);
        }
        foreach (ListViewItem item in list.Items)
        {
            if (item.Tag is ValueTuple<int, bool, string> tag && selected.Contains(tag)) item.Selected = true;
        }
        list.EndUpdate();

        WorldEnemyRegion? edit = EditOf(region);
        string state = edit?.Groups != null
            ? L.T("The list of this biome is yours (changed from the game's).")
            : L.T("This biome uses the game's own list.");
        if (listsDirty) state += " " + L.T("Not saved yet.");
        int total = groups.Sum(g => g.Leaders.Count + g.Members.Count);
        Ui.SetStatus(listStatus, state + "  " + L.F("{0} group(s), {1} enemy entries.", groups.Count, total), listsDirty ? TextRole.Warn : TextRole.Muted);
        UpdateListButtons();
    }

    private void AddRow(int group, string groupName, string role, bool leader, string enemy, string name)
    {
        GameUnit? unit = enemy.Length > 0 && EditorAssets.Current.Units.TryGetValue(enemy, out GameUnit? found) ? found : null;
        var item = new ListViewItem(groupName) { Tag = (group, leader, name) };
        item.SubItems.Add(role);
        item.SubItems.Add(enemy.Length > 0 ? enemy : L.T("(empty group)"));
        item.SubItems.Add(unit != null ? unit.Tier.ToString() : "");
        item.SubItems.Add(unit != null ? unit.MaxHealth.ToString("0") : "");
        list.Items.Add(item);
    }

    private IEnumerable<(int Group, bool Leader, string Name)> SelectedRows()
    {
        foreach (ListViewItem item in list.SelectedItems)
        {
            if (item.Tag is ValueTuple<int, bool, string> tag) yield return tag;
        }
    }

    private void UpdateListButtons()
    {
        bool has = Current != null;
        addButton.Enabled = has;
        bool anySelected = list.SelectedItems.Count > 0;
        removeButton.Enabled = has && SelectedRows().Any(r => r.Name.Length > 0);
        removeGroupButton.Enabled = has && anySelected;
        renameGroupButton.Enabled = has && list.SelectedItems.Count > 0 && SelectedRows().Select(r => r.Group).Distinct().Count() == 1;
        resetButton.Enabled = has && EditOf(Current!) != null;
        saveLists.Enabled = listsDirty;
        undoLists.Enabled = listsDirty;
    }

    private void MarkDirty()
    {
        listsDirty = true;
        Fill();
    }

    private void AddEnemies()
    {
        WorldEnemyRegion? region = Current;
        if (region == null) return;
        List<WorldEnemyGroup> groups = EffectiveGroups(region);
        int selectedGroup = SelectedRows().Select(r => r.Group).DefaultIfEmpty(-1).First();

        using var picker = new WorldEnemyPickerForm(region.Zone.Length > 0 ? region.Zone : region.Key, groups, selectedGroup);
        if (picker.ShowDialog(FindForm()) != DialogResult.OK || picker.ChosenUnits.Count == 0) return;

        List<WorldEnemyGroup> target = Customize(region);
        WorldEnemyGroup group;
        if (picker.ChosenGroup >= 0 && picker.ChosenGroup < target.Count)
        {
            group = target[picker.ChosenGroup];
        }
        else
        {
            bool solo = picker.ChosenGroup == WorldEnemyPickerForm.NewSoloGroup;
            group = new WorldEnemyGroup { Solo = solo, Name = picker.ChosenUnits[0] + (solo ? "" : " group") };
            target.Add(group);
        }
        foreach (string unit in picker.ChosenUnits)
        {
            List<string> side = picker.AsLeader && !group.Solo ? group.Leaders : group.Members;
            if (!side.Contains(unit, StringComparer.OrdinalIgnoreCase)) side.Add(unit);
        }
        MarkDirty();
    }

    private void RemoveSelected()
    {
        WorldEnemyRegion? region = Current;
        if (region == null) return;
        List<(int Group, bool Leader, string Name)> rows = SelectedRows().Where(r => r.Name.Length > 0).ToList();
        if (rows.Count == 0) return;

        List<WorldEnemyGroup> groups = Customize(region);
        foreach ((int g, bool leader, string name) in rows)
        {
            if (g < 0 || g >= groups.Count) continue;
            (leader ? groups[g].Leaders : groups[g].Members).RemoveAll(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
        // A group with nobody left is gone.
        groups.RemoveAll(g => g.Leaders.Count == 0 && g.Members.Count == 0);
        MarkDirty();
    }

    private void RemoveGroup()
    {
        WorldEnemyRegion? region = Current;
        if (region == null) return;
        List<int> indexes = SelectedRows().Select(r => r.Group).Distinct().OrderByDescending(i => i).ToList();
        if (indexes.Count == 0) return;

        List<WorldEnemyGroup> groups = Customize(region);
        foreach (int index in indexes)
        {
            if (index >= 0 && index < groups.Count) groups.RemoveAt(index);
        }
        MarkDirty();
    }

    private void RenameGroup()
    {
        WorldEnemyRegion? region = Current;
        if (region == null) return;
        int index = SelectedRows().Select(r => r.Group).DefaultIfEmpty(-1).First();
        List<WorldEnemyGroup> groups = EffectiveGroups(region);
        if (index < 0 || index >= groups.Count) return;

        string? name = SteambirdsLauncher.EnemyAi.AiPrompt.Ask(FindForm()!, L.T("Name of the group"), L.T("Group name:"), GroupName(groups[index], index));
        if (name == null) return;
        Customize(region)[index].Name = name.Trim();
        MarkDirty();
    }

    private void ResetRegion()
    {
        WorldEnemyRegion? region = Current;
        if (region == null || EditOf(region) == null) return;
        if (!Ui.Confirm(FindForm(), L.F("Drop your changes to {0} and go back to the game's list?", region.Zone.Length > 0 ? region.Zone : region.Key))) return;
        edits.Remove(region.Key);
        listsDirty = true;
        ShowRegion();
    }

    // The boss box: the name picked or typed. The game's own boss is not saved (the biome keeps following the game).
    private void BossEdited()
    {
        WorldEnemyRegion? region = Current;
        if (region == null || fillingRegion) return;
        string typed = bossBox.Text.Trim();
        string now = EditOf(region)?.Boss ?? "";
        string effective = now.Length > 0 ? now : region.Boss;
        if (typed.Equals(effective, StringComparison.OrdinalIgnoreCase)) return;
        if (typed.Length == 0)
        {
            bossBox.Text = effective;
            return;
        }

        if (typed.Equals(region.Boss, StringComparison.OrdinalIgnoreCase))
        {
            if (EditOf(region) != null) EditOf(region)!.Boss = "";
        }
        else
        {
            WorldEnemyRegion? edit = EditOf(region);
            if (edit == null)
            {
                edit = new WorldEnemyRegion { Key = region.Key };
                edits[region.Key] = edit;
            }
            edit.Boss = typed;
        }
        listsDirty = true;
        Fill();
    }

    private void SaveLists()
    {
        try
        {
            BossEdited();
            // A region with nothing changed (no groups of its own, the game's boss) is not written.
            foreach (string key in edits.Where(p => p.Value.Groups == null && string.IsNullOrWhiteSpace(p.Value.Boss)).Select(p => p.Key).ToList())
                edits.Remove(key);
            WorldEnemyFiles.SaveEdits(edits);
            listsDirty = false;
            Fill();
            Ui.SetStatus(listStatus, L.F("Saved at {0}. The server reads it by itself; groups already on the map stay until they die.", DateTime.Now.ToString("HH:mm:ss")),
                TextRole.Good);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not change {0}:\n{1}", WorldEnemyFiles.EditsPath, e.Message));
        }
    }

    public bool HasUnsavedChanges => listsDirty || SettingsChanged;
}
