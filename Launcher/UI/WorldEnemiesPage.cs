using System.Globalization;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.UI;

// Which half of the open world's enemy settings a WorldEnemiesPage shows (each one is its own tab of Server > World map).
internal enum WorldEnemiesPart
{
    // "Enemies": how enemies spawn (world.json > enemies: how many at once, how close a player must be...).
    Spawning,
    // "Enemies of each biome": which enemies live in each biome, group by group.
    Biomes
}

// Enemy tabs of the open world (Server > World map): how enemies spawn (world.json > enemies) and which enemies live in each biome. The
// lists are saved in world-enemies.json; a biome that was never changed keeps the game's own lists (world-enemy-defaults.json, written
// by the server). The server reads both files again by itself. One class builds both tabs; WorldEnemiesPart says which one is shown.
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
    private readonly ThemedComboBox aiBox;
    private readonly ThemedButton aiEditButton;
    private readonly ThemedComboBox formationBox;
    private readonly ThemedButton formationEditButton;
    private bool fillingFormation;
    private FormationLibrary formations = new FormationLibrary();
    private DateTime formationsStamp = DateTime.MinValue;
    private readonly Label aiNote;
    private readonly NumberBox countBox;
    private readonly ThemedButton randomGroupButton;
    private bool fillingCount;
    private AiLibrary aiLibrary = new AiLibrary();
    private string aiSignature = "";
    private bool fillingAi;
    private readonly ThemedButton saveLists;
    private readonly ThemedButton undoLists;
    private List<WorldEnemyRegion> defaults = new List<WorldEnemyRegion>();
    private Dictionary<string, WorldEnemyRegion> edits = new Dictionary<string, WorldEnemyRegion>(StringComparer.OrdinalIgnoreCase);
    private bool listsDirty;
    private bool fillingRegion;
    private DateTime defaultsStamp = DateTime.MinValue;

    private readonly WorldEnemiesPart part;

    public WorldEnemiesPage(LauncherContext context, WorldEnemiesPart part)
    {
        this.context = context;
        this.part = part;
        ToolTip tip = context.ToolTip;
        // The unit catalog and the pictures come from the editor's assets (normally loaded by the map editor tab).
        if (EditorAssets.Current.Folder.Length == 0) EditorAssets.Load(context.GameFolder);

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
        // A fixed label column: in a wide window the boxes stay next to their labels instead of at the far right edge.
        numbers.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 460f));
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
        // The "Enemies" tab fills the whole window.
        settingsCard.Dock = DockStyle.Fill;
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

        list = Ui.Grid((L.T("Group"), 160), (L.T("Role"), 72), (L.T("Enemy"), 140), (L.T("AI"), 150), (L.T("Formation"), 130), (L.T("Amount"), 70), (L.T("Tier"), 45), (L.T("Health"), 64));
        list.MultiSelect = true;
        // The picture of each enemy, before its name.
        list.PictureColumn = 2;
        list.PictureOf = UnitThumb;
        // Tall rows (twice the usual), so the picture of each enemy is big enough to recognize.
        list.RowHeight = 52;
        list.PictureSize = 48;
        list.SelectedIndexChanged += (_, _) => UpdateListButtons();
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Delete) return;
            e.Handled = true;
            RemoveSelected();
        };

        aiBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, Enabled = false };
        aiBox.SelectedIndexChanged += (_, _) => AiPicked();
        tip.SetToolTip(aiBox, L.T("The AI the selected enemies run in this biome. The list shows the AIs made for the enemy first, then the generic ones (the AI tab edits them)."));
        aiEditButton = Ui.Button(L.T("Load unit in the AI editor"), (_, _) => LoadInAiEditor());
        tip.SetToolTip(aiEditButton, L.T("Goes to the Enemy AI tab with this enemy and its AI loaded, ready to edit and test."));
        aiNote = Ui.Wrapping("", 640, gray: true);
        FlowLayoutPanel aiRow = Ui.WrapFlow(Ui.Text(L.T("AI of the selected enemy:")), aiBox, aiEditButton);
        aiRow.Dock = DockStyle.Bottom;

        formationBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, Enabled = false };
        formationBox.SelectedIndexChanged += (_, _) => FormationPicked();
        tip.SetToolTip(formationBox, L.T("The formation born with this enemy when it leads a group or spawns alone. The whole formation is created, not only this unit. \"(from the AI or the unit)\" uses the formation the AI names, or the one bound to the unit when the AI is generic."));
        formationEditButton = Ui.Button(L.T("Open formation"), (_, _) => OpenFormation());
        tip.SetToolTip(formationEditButton, L.T("Opens this formation in the Formation tab."));
        FlowLayoutPanel formationRow = Ui.WrapFlow(Ui.Text(L.T("Formation of the selected enemy:")), formationBox, formationEditButton);
        formationRow.Dock = DockStyle.Bottom;

        countBox = new NumberBox { Minimum = 0, Maximum = WorldEnemyGroup.MaxCount, DecimalPlaces = 0, Value = 1, Width = 70, TextAlign = HorizontalAlignment.Right, Enabled = false };
        countBox.ValueChanged += (_, _) => CountPicked();
        tip.SetToolTip(countBox, L.T("How many of the selected enemy the group creates each time (0 = none). Once a group has amounts it is exact: one leader and every member, as many as said. The original game's Five Star group, for example, is 1 leader and 5 escorts."));
        randomGroupButton = Ui.Button(L.T("Random group"), (_, _) => RandomGroups());
        tip.SetToolTip(randomGroupButton, L.T("Takes the amounts off the selected groups: they go back to a leader and a random number of members (the group size of world.json)."));
        FlowLayoutPanel countRow = Ui.WrapFlow(Ui.Text(L.T("Amount of the selected enemy per spawn:")), countBox, randomGroupButton);
        countRow.Dock = DockStyle.Bottom;
        aiNote.Dock = DockStyle.Bottom;

        addButton = Ui.Button(L.T("Add enemy..."), (_, _) => AddEnemies());
        removeButton = Ui.Button(L.T("Remove"), (_, _) => RemoveSelected());
        removeGroupButton = Ui.Button(L.T("Remove group"), (_, _) => RemoveGroup());
        renameGroupButton = Ui.Button(L.T("Rename group..."), (_, _) => RenameGroup());
        resetButton = Ui.Button(L.T("Back to the game's list"), (_, _) => ResetRegion());
        tip.SetToolTip(addButton, L.T("Adds units of the game to a group of this biome, or to a new group."));
        tip.SetToolTip(removeButton, L.T("Takes the selected enemies out of the list of this biome (Delete)."));
        tip.SetToolTip(resetButton, L.T("Drops what you changed in this biome (the groups, the boss and the AIs): it goes back to the game's own list."));
        FlowLayoutPanel editButtons = Ui.WrapFlow(addButton, removeButton, removeGroupButton, renameGroupButton, resetButton);
        editButtons.Dock = DockStyle.Bottom;

        saveLists = Ui.Primary(L.T("Save lists"), (_, _) => SaveLists());
        undoLists = Ui.Button(L.T("Undo changes"), (_, _) => LoadLists(true));
        FlowLayoutPanel saveButtons = Ui.WrapFlow(saveLists, undoLists);
        saveButtons.Dock = DockStyle.Bottom;

        hint = Ui.Wrapping(L.T("Each spawn site of a biome picks one of these groups at random: a leader with followers, or one enemy of a solo group. " +
                               "The enemies are the game's units with the generic chase AI; pick another AI for an enemy below (one made for it, or a generic one) and edit AIs in the Enemy AI tab. " +
                               "The bosses of the biome are listed after the groups, each with its parts and minions; they take an AI the same way.") +
                               " " + L.T("A formation picked below is born whole with that enemy when it leads the group or spawns alone."), 640, gray: true);
        hint.Dock = DockStyle.Bottom;

        var listsBody = new Panel { Dock = DockStyle.Fill };
        // Fill first, then the others (WinForms docks the last added control first).
        listsBody.Controls.Add(Ui.Framed(list));
        listsBody.Controls.Add(hint);
        listsBody.Controls.Add(formationRow);
        listsBody.Controls.Add(aiNote);
        listsBody.Controls.Add(aiRow);
        listsBody.Controls.Add(countRow);
        listsBody.Controls.Add(saveButtons);
        listsBody.Controls.Add(editButtons);
        listsBody.Controls.Add(listStatus);
        listsBody.Controls.Add(top);
        Card listsCard = Ui.Group(L.T("Enemies of each biome"), listsBody);
        listsCard.AutoSize = false;
        listsCard.Dock = DockStyle.Fill;
        listsBody.Dock = DockStyle.Fill;

        // Each tab shows one card; the other half is built but never shown nor loaded.
        if (part == WorldEnemiesPart.Biomes)
        {
            Controls.Add(listsCard);
            LoadLists(false);
        }
        else
        {
            Controls.Add(settingsCard);
            LoadSettings();
        }

        // Back on this tab after the server wrote the defaults (it does so when it builds the world): pick them up.
        VisibleChanged += (_, _) =>
        {
            if (part != WorldEnemiesPart.Biomes || !Visible) return;
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

    // The small picture of the game unit of a row (a dotted square when the unit has none, so the names stay in line).
    private static Image? UnitThumb(ListViewItem item, int size)
    {
        if (!EditorAssets.Current.HasPictures) return null;
        string unit = item.Tag is ValueTuple<int, bool, string> tag ? tag.Item3 : "";
        if (unit.Length == 0) return null;
        return EditorAssets.Current.PictureOfUnit(unit)?.Thumb(size) ?? AssetPicture.Placeholder(size);
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
                AddRow(g, GroupName(group, g), "", true, "", "", "");
                continue;
            }
            foreach (string leader in group.Leaders) AddRow(g, GroupName(group, g), L.T("Leader"), true, leader, leader, CountText(group, leader));
            foreach (string member in group.Members)
                AddRow(g, GroupName(group, g), group.Solo ? L.T("Alone") : L.T("Follower"), false, member, member, CountText(group, member));
        }
        AddBossRows(region);
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

    // The rows of the bosses are not part of the host's groups: they have this group number, so they can be given an AI but not removed.
    private const int BossRowGroup = -1;

    // The bosses of the biome (the ones "Spawn boss here" can create) and the parts and minions that come with each, after the groups.
    // Their AI is picked like the AI of any other enemy of the biome (the server uses it for the boss and for its parts).
    private void AddBossRows(WorldEnemyRegion region)
    {
        List<WorldBossGroup> bosses = region.Bosses.Count > 0
            ? region.Bosses.ToList()
            : region.BossChoices.Select(name => new WorldBossGroup { Name = name }).ToList();
        string edited = EditOf(region)?.Boss ?? "";
        string chosen = edited.Length > 0 ? edited : region.Boss;
        // A boss the host typed that is not one of the game's choices.
        if (chosen.Length > 0 && !bosses.Any(b => b.Name.Equals(chosen, StringComparison.OrdinalIgnoreCase)))
            bosses.Add(new WorldBossGroup { Name = chosen });

        foreach (WorldBossGroup boss in bosses)
        {
            if (boss.Name.Length == 0) continue;
            string label = L.F("Boss: {0}", boss.Name);
            AddRow(BossRowGroup, label, L.T("Boss"), true, boss.Name, boss.Name, "");
            foreach (string part in boss.Parts.Distinct(StringComparer.OrdinalIgnoreCase))
                AddRow(BossRowGroup, label, L.T("Part"), false, part, part, "");
        }
    }

    // "random" while the group has no amounts, else how many of the unit it creates each time.
    private static string CountText(WorldEnemyGroup group, string unit) => group.Counts == null ? L.T("random") : group.CountOf(unit).ToString();

    private void AddRow(int group, string groupName, string role, bool leader, string enemy, string name, string count)
    {
        GameUnit? unit = enemy.Length > 0 && EditorAssets.Current.Units.TryGetValue(enemy, out GameUnit? found) ? found : null;
        var item = new ListViewItem(groupName) { Tag = (group, leader, name) };
        item.SubItems.Add(role);
        item.SubItems.Add(enemy.Length > 0 ? enemy : L.T("(empty group)"));
        item.SubItems.Add(enemy.Length > 0 ? AiLabel(enemy) : "");
        item.SubItems.Add(enemy.Length > 0 ? FormationLabel(enemy) : "");
        item.SubItems.Add(count);
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
        UpdateAiBox();
        UpdateFormationBox();
        UpdateCountControls();
        bool has = Current != null;
        addButton.Enabled = has;
        bool anyGroupRow = SelectedRows().Any(r => r.Group >= 0);
        removeButton.Enabled = has && SelectedRows().Any(r => r.Group >= 0 && r.Name.Length > 0);
        removeGroupButton.Enabled = has && anyGroupRow;
        renameGroupButton.Enabled = has && anyGroupRow && SelectedRows().Select(r => r.Group).Distinct().Count() == 1;
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
        List<(int Group, bool Leader, string Name)> rows = SelectedRows().Where(r => r.Group >= 0 && r.Name.Length > 0).ToList();
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
        List<int> indexes = SelectedRows().Select(r => r.Group).Where(i => i >= 0).Distinct().OrderByDescending(i => i).ToList();
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

    // ------------------------------------------------------------------ how many of each enemy a group creates

    // The rows of the host's groups that are selected (the bosses have no amount).
    private List<(int Group, bool Leader, string Name)> SelectedGroupRows() => SelectedRows().Where(r => r.Group >= 0 && r.Name.Length > 0).ToList();

    private void UpdateCountControls()
    {
        WorldEnemyRegion? region = Current;
        List<(int Group, bool Leader, string Name)> rows = region == null ? new List<(int Group, bool Leader, string Name)>() : SelectedGroupRows();
        fillingCount = true;
        try
        {
            countBox.Enabled = rows.Count > 0;
            randomGroupButton.Enabled = false;
            if (region == null || rows.Count == 0) return;
            List<WorldEnemyGroup> groups = EffectiveGroups(region);
            var amounts = new List<int>();
            foreach ((int g, _, string name) in rows)
            {
                if (g >= groups.Count) continue;
                amounts.Add(groups[g].CountOf(name));
                if (groups[g].Counts != null) randomGroupButton.Enabled = true;
            }
            if (amounts.Count > 0) countBox.Value = Math.Clamp(amounts[0], 0, WorldEnemyGroup.MaxCount);
        }
        finally
        {
            fillingCount = false;
        }
    }

    // A new amount for the selected enemies. The group becomes exact: every entry that had no amount counts as 1.
    private void CountPicked()
    {
        if (fillingCount) return;
        WorldEnemyRegion? region = Current;
        if (region == null) return;
        List<(int Group, bool Leader, string Name)> rows = SelectedGroupRows();
        if (rows.Count == 0) return;
        int amount = (int)countBox.Value;

        List<WorldEnemyGroup> groups = EffectiveGroups(region);
        bool changed = rows.Any(r => r.Group < groups.Count && (groups[r.Group].Counts == null || groups[r.Group].CountOf(r.Name) != amount));
        if (!changed) return;

        groups = Customize(region);
        foreach ((int g, _, string name) in rows)
        {
            if (g >= groups.Count) continue;
            WorldEnemyGroup group = groups[g];
            if (group.Counts == null)
            {
                group.Counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (string unit in group.Leaders.Concat(group.Members)) group.Counts[unit] = 1;
            }
            group.Counts[name] = amount;
        }
        MarkDirty();
    }

    private void RandomGroups()
    {
        WorldEnemyRegion? region = Current;
        if (region == null) return;
        List<int> indexes = SelectedGroupRows().Select(r => r.Group).Distinct().ToList();
        List<WorldEnemyGroup> groups = Customize(region);
        bool changed = false;
        foreach (int index in indexes)
        {
            if (index < 0 || index >= groups.Count || groups[index].Counts == null) continue;
            groups[index].Counts = null;
            changed = true;
        }
        if (changed) MarkDirty();
    }

    // ------------------------------------------------------------------ the AI of each enemy

    // The AI an enemy runs in the biome now: the host's choice, or the default of the open world.
    private string AiOf(WorldEnemyRegion region, string unit)
    {
        Dictionary<string, string>? chosen = EditOf(region)?.Ai;
        if (chosen != null)
        {
            foreach (KeyValuePair<string, string> pair in chosen)
            {
                if (pair.Key.Equals(unit, StringComparison.OrdinalIgnoreCase) && pair.Value.Length > 0) return pair.Value;
            }
        }
        return WorldEnemyFiles.DefaultAiOf(unit);
    }

    private string AiLabel(string unit)
    {
        WorldEnemyRegion? region = Current;
        if (region == null) return "";
        string id = AiOf(region, unit);
        return id.Equals(WorldEnemyFiles.DefaultAiOf(unit), StringComparison.OrdinalIgnoreCase) ? id + " (" + L.T("default") + ")" : id;
    }

    private sealed class AiChoice
    {
        public AiChoice(string id, string text)
        {
            Id = id;
            Text = text;
        }

        public string Id { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    // The AI files of the server, read again when one changes.
    private AiLibrary Library()
    {
        string folder = LauncherPaths.AiFolder;
        string signature = AiLibrary.Signature(folder);
        if (signature != aiSignature)
        {
            aiLibrary = AiLibrary.Load(folder);
            aiSignature = signature;
        }
        return aiLibrary;
    }

    private List<string> SelectedUnits()
    {
        return SelectedRows().Select(r => r.Name).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // The box lists the AIs that fit every selected enemy: the ones made for them first, then the generic ones.
    private void UpdateAiBox()
    {
        WorldEnemyRegion? region = Current;
        List<string> units = region == null ? new List<string>() : SelectedUnits();
        fillingAi = true;
        try
        {
            aiBox.Items.Clear();
            aiBox.Enabled = false;
            aiEditButton.Enabled = false;
            aiNote.Text = "";
            if (region == null || units.Count == 0) return;

            AiLibrary library = Library();
            List<List<(string Id, bool Specific)>> perUnit = units.Select(u => library.ApplicableTo(new[] { u })).ToList();
            List<(string Id, bool Specific)> fit = perUnit[0]
                .Where(a => perUnit.All(l => l.Any(b => b.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase))))
                .Select(a => (a.Id, perUnit.All(l => l.Any(b => b.Id.Equals(a.Id, StringComparison.OrdinalIgnoreCase) && b.Specific))))
                .OrderByDescending(a => a.Item2).ToList();

            List<string> currents = units.Select(u => AiOf(region, u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string? current = currents.Count == 1 ? currents[0] : null;
            if (current != null && !fit.Any(a => a.Id.Equals(current, StringComparison.OrdinalIgnoreCase)))
                aiBox.Items.Add(new AiChoice(current, L.F("{0}  (not made for this unit)", current)));
            foreach ((string id, bool specific) in fit)
                aiBox.Items.Add(new AiChoice(id, specific ? L.F("{0}  (made for this unit)", id) : id));

            int index = current == null ? -1 : aiBox.Items.Cast<AiChoice>().ToList().FindIndex(c => c.Id.Equals(current, StringComparison.OrdinalIgnoreCase));
            aiBox.SelectedIndex = index;
            aiBox.Enabled = true;
            aiEditButton.Enabled = EnemyAiEditors.Available && units.Count == 1;
            if (units.Count > 1)
                aiNote.Text = L.T("Several enemies are selected: only the AIs that fit all of them are listed.");
        }
        finally
        {
            fillingAi = false;
        }
    }

    private void AiPicked()
    {
        if (fillingAi || aiBox.SelectedItem is not AiChoice choice) return;
        WorldEnemyRegion? region = Current;
        if (region == null) return;
        List<string> units = SelectedUnits();
        if (units.Count == 0) return;

        WorldEnemyRegion? edit = EditOf(region);
        if (edit == null)
        {
            edit = new WorldEnemyRegion { Key = region.Key };
            edits[region.Key] = edit;
        }
        edit.Ai ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (string unit in units)
        {
            string before = AiOf(region, unit);
            if (before.Equals(choice.Id, StringComparison.OrdinalIgnoreCase)) continue;
            // The default is not written: the biome keeps following the default AI of the open world.
            if (choice.Id.Equals(WorldEnemyFiles.DefaultAiOf(unit), StringComparison.OrdinalIgnoreCase)) edit.Ai.Remove(unit);
            else edit.Ai[unit] = choice.Id;
            changed = true;
        }
        if (changed) MarkDirty();
    }

    // ------------------------------------------------------------------ the formation born with an enemy

    private FormationLibrary Formations()
    {
        DateTime stamp = DateTime.MinValue;
        try
        {
            string dir = Path.Combine(LauncherPaths.AiFolder, "formations");
            if (Directory.Exists(dir))
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
                {
                    DateTime write = File.GetLastWriteTimeUtc(file);
                    if (write > stamp) stamp = write;
                }
            }
        }
        catch (IOException)
        {
            stamp = DateTime.MinValue;
        }
        if (stamp != formationsStamp)
        {
            try { formations = FormationLibrary.Load(LauncherPaths.AiFolder); }
            catch (IOException) { formations = new FormationLibrary(); }
            formationsStamp = stamp;
        }
        return formations;
    }

    // The formation the host wrote for this unit. Empty = the AI or the unit binding decides.
    private string FormationOf(WorldEnemyRegion region, string unit)
    {
        Dictionary<string, string>? chosen = EditOf(region)?.Formations;
        if (chosen == null) return "";
        foreach (KeyValuePair<string, string> pair in chosen)
        {
            if (pair.Key.Equals(unit, StringComparison.OrdinalIgnoreCase) && pair.Value.Length > 0) return pair.Value;
        }
        return "";
    }

    private string FormationLabel(string unit)
    {
        WorldEnemyRegion? region = Current;
        if (region == null) return "";
        string id = FormationOf(region, unit);
        if (id.Length > 0) return id;
        FormationFile? bound = Formations().ForUnit(unit);
        return bound != null && bound.BindToUnit ? bound.Id + " (" + L.T("bound") + ")" : "";
    }

    private sealed class FormationChoice
    {
        public FormationChoice(string id, string text)
        {
            Id = id;
            Text = text;
        }

        public string Id { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    private void UpdateFormationBox()
    {
        WorldEnemyRegion? region = Current;
        List<string> units = region == null ? new List<string>() : SelectedUnits();
        fillingFormation = true;
        try
        {
            formationBox.Items.Clear();
            formationBox.Enabled = false;
            formationEditButton.Enabled = false;
            if (region == null || units.Count == 0) return;

            formationBox.Items.Add(new FormationChoice("", L.T("(from the AI or the unit)")));
            foreach (string id in Formations().ById.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
                formationBox.Items.Add(new FormationChoice(id, id));

            List<string> currents = units.Select(u => FormationOf(region, u)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (currents.Count != 1) formationBox.SelectedIndex = -1;
            else
            {
                int index = formationBox.Items.Cast<FormationChoice>().ToList().FindIndex(c => c.Id.Equals(currents[0], StringComparison.OrdinalIgnoreCase));
                formationBox.SelectedIndex = index >= 0 ? index : 0;
            }
            formationBox.Enabled = true;
            string openId = units.Count == 1 ? EffectiveFormationId(region, units[0]) : "";
            formationEditButton.Enabled = openId.Length > 0 && context.OpenFormationTab != null;
        }
        finally
        {
            fillingFormation = false;
        }
    }

    private string EffectiveFormationId(WorldEnemyRegion region, string unit)
    {
        string id = FormationOf(region, unit);
        if (id.Length > 0) return id;
        FormationFile? bound = Formations().ForUnit(unit);
        return bound != null && bound.BindToUnit ? bound.Id : "";
    }

    private void FormationPicked()
    {
        if (fillingFormation || formationBox.SelectedItem is not FormationChoice choice) return;
        WorldEnemyRegion? region = Current;
        if (region == null) return;
        List<string> units = SelectedUnits();
        if (units.Count == 0) return;

        WorldEnemyRegion? edit = EditOf(region);
        if (edit == null)
        {
            edit = new WorldEnemyRegion { Key = region.Key };
            edits[region.Key] = edit;
        }
        edit.Formations ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        edit.Ai ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        FormationFile? file = choice.Id.Length == 0 ? null : Formations().Get(choice.Id);
        foreach (string unit in units)
        {
            string before = FormationOf(region, unit);
            if (!before.Equals(choice.Id, StringComparison.OrdinalIgnoreCase))
            {
                if (choice.Id.Length == 0) edit.Formations.Remove(unit);
                else edit.Formations[unit] = choice.Id;
                changed = true;
            }
            // A formation names the AI its leader should run. Apply it when this enemy still has the default AI.
            if (units.Count == 1 && file != null && file.LeaderAi.Length > 0 &&
                AiOf(region, unit).Equals(WorldEnemyFiles.DefaultAiOf(unit), StringComparison.OrdinalIgnoreCase) &&
                !file.LeaderAi.Equals(AiOf(region, unit), StringComparison.OrdinalIgnoreCase))
            {
                edit.Ai[unit] = file.LeaderAi;
                changed = true;
            }
        }
        if (changed) MarkDirty();
    }

    private void OpenFormation()
    {
        WorldEnemyRegion? region = Current;
        List<string> units = SelectedUnits();
        if (region == null || units.Count != 1 || context.OpenFormationTab == null) return;
        string id = EffectiveFormationId(region, units[0]);
        if (id.Length == 0) return;
        context.OpenFormationTab(id);
    }

    private void LoadInAiEditor()
    {
        WorldEnemyRegion? region = Current;
        IEnemyAiEditor? editor = EnemyAiEditors.Current;
        List<string> units = SelectedUnits();
        if (region == null || editor == null || units.Count != 1) return;
        var type = new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = units[0],
            ["template"] = units[0],
            [EnemyAiEditors.AiField] = AiOf(region, units[0])
        };
        editor.Edit(FindForm()!, "world/" + units[0], type, () => { }, () => { });
    }

    private void SaveLists()
    {
        try
        {
            BossEdited();
            // A region with nothing changed (no groups of its own, the game's boss) is not written.
            foreach (string key in edits.Where(p => p.Value.Groups == null && string.IsNullOrWhiteSpace(p.Value.Boss) &&
                         (p.Value.Ai == null || p.Value.Ai.Count == 0) &&
                         (p.Value.Formations == null || p.Value.Formations.Count == 0)).Select(p => p.Key).ToList())
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
