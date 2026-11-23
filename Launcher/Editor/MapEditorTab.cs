using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// The "Map editor" tab of the launcher: menu, tool palette, the map canvas and the property pages.
// Maps are the .json files of the server (server\maps). The editor can start from a dungeon of the game or from an
// empty dungeon, open and save map files, and checks the map when it is saved.
internal sealed class MapEditorTab : UserControl
{
    private readonly LauncherContext context;
    private readonly MapSession session;
    private readonly MapCanvas canvas;
    private readonly EditorContext editor;

    private readonly MenuStrip menu;
    private readonly ToolStripMenuItem undoItem;
    private readonly ToolStripMenuItem redoItem;
    private readonly ToolStripMenuItem openServerMenu;
    private readonly ToolStripStatusLabel statusLabel;
    private readonly ToolStripStatusLabel fileLabel;
    private readonly ToolStripStatusLabel issuesLabel;

    private readonly Dictionary<EditorTool, ToolButton> toolButtons = new Dictionary<EditorTool, ToolButton>();
    private readonly ComboBox unitTypeBox;
    private readonly ComboBox roomBox;
    private readonly ComboBox gateTypeBox;
    private readonly TextBox groupBox;
    private readonly ComboBox snapBox;
    private readonly Dictionary<string, Control[]> optionControls = new Dictionary<string, Control[]>();
    private Label? optionsHeading;
    private readonly Label tabsHint;

    private readonly ThemedTabControl pages;
    private readonly List<EditorPage> pageList = new List<EditorPage>();
    private readonly List<TabPage> allTabs = new List<TabPage>();
    private readonly HashSet<EditorPage> stalePages = new HashSet<EditorPage>();
    private readonly HashSet<string> backedUp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly SplitContainer split;
    private bool splitPlaced;
    private bool fitted;
    private bool settingChoices;

    public MapEditorTab(LauncherContext context, MapSession? existing = null)
    {
        this.context = context;
        Dock = DockStyle.Fill;

        EditorAssets.Load(context.GameFolder);
        session = existing ?? new MapSession(MapFactory.Blank("new_dungeon", L.T("New dungeon"), "DungeonBomb", 41));
        canvas = new MapCanvas(session) { Dock = DockStyle.Fill };
        editor = new EditorContext(session, canvas, context.ToolTip);

        // ---- pages (right side)
        var mapPage = new MapSettingsPage(editor);
        var roomsPage = new RoomsPage(editor);
        var typesPage = new UnitTypesPage(editor);
        var gatesPage = new GatesPage(editor);
        var pointsPage = new PointsPage(editor);
        pageList.AddRange(new EditorPage[] { pointsPage, gatesPage, roomsPage, typesPage, mapPage });

        pages = new ThemedTabControl { Dock = DockStyle.Fill, Padding = new Point(8, 4) };
        AddPage(L.T("Map"), mapPage);
        AddPage(L.T("Rooms"), roomsPage);
        AddPage(L.T("Units"), typesPage);
        AddPage(L.T("Gates"), gatesPage);
        AddPage(L.T("Points"), pointsPage);
        pages.SelectedIndexChanged += (_, _) => ReloadStalePage();

        // ---- left palette
        FlowLayoutPanel palette = BuildPalette(out unitTypeBox, out roomBox, out gateTypeBox, out groupBox, out snapBox);

        // ---- menu and status
        menu = new MenuStrip { Renderer = new EditorRenderer(), Dock = DockStyle.Top };
        undoItem = Item(L.T("Undo"), () => session.Undo(), "Ctrl+Z");
        redoItem = Item(L.T("Redo"), () => session.Redo(), "Ctrl+Y");
        openServerMenu = new ToolStripMenuItem(L.T("Open a map of the server"));
        BuildMenu();

        var status = new StatusStrip();
        statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        issuesLabel = new ToolStripStatusLabel { Tag = TextRole.Muted };
        fileLabel = new ToolStripStatusLabel();
        status.Items.AddRange(new ToolStripItem[] { statusLabel, issuesLabel, new ToolStripSeparator(), fileLabel });

        // ---- layout: [palette | canvas] | pages
        // The minimum sizes are set in PlaceSplitter, once the editor has its real width: a new SplitContainer is
        // 150 px wide, and Panel2MinSize = 360 there throws InvalidOperationException (the launcher closed at start).
        split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 5
        };
        split.Panel1.Controls.Add(canvas);
        palette.Dock = DockStyle.Left;
        split.Panel1.Controls.Add(palette);
        tabsHint = Ui.Wrapping(L.T("Showing only the tabs of the selected object. Click an empty spot of the map to see all tabs."), 340, gray: true);
        tabsHint.Dock = DockStyle.Top;
        tabsHint.Visible = false;
        split.Panel2.Controls.Add(pages);
        split.Panel2.Controls.Add(tabsHint);
        split.Panel2.Padding = new Padding(2);

        Controls.Add(split);
        Controls.Add(status);
        Controls.Add(menu);

        // ---- events
        canvas.StatusChanged += text => statusLabel.Text = text;
        canvas.ToolChanged += UpdateToolButtons;
        canvas.SelectionChanged += OnCanvasSelection;
        canvas.StructureChanged += OnStructureChanged;
        canvas.CommandRequested += OnCommand;
        session.Changed += OnSessionChanged;
        session.Replaced += OnSessionReplaced;
        context.GameFolderChanged += OnGameFolderChanged;
        split.SizeChanged += (_, _) => PlaceSplitter();
        canvas.SizeChanged += (_, _) =>
        {
            // The first time the canvas has a real size, the whole map is shown.
            if (fitted || canvas.Width < 200 || canvas.Height < 200) return;
            fitted = true;
            canvas.FitToMap();
        };

        RefreshChoices();
        UpdateToolButtons();
        UpdateFileLabel();
        canvas.FitToMap();
        statusLabel.Text = L.T("Pick a tool on the left. Middle button or Space+drag moves the view; the wheel zooms.");
    }

    // The session survives a rebuild of the launcher tabs (language change).
    public MapSession Session => session;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            session.Changed -= OnSessionChanged;
            session.Replaced -= OnSessionReplaced;
            context.GameFolderChanged -= OnGameFolderChanged;
        }
        base.Dispose(disposing);
    }

    // Minimum widths of the two sides: palette + canvas on the left, pages on the right.
    private const int LeftMinWidth = 300;
    private const int RightMinWidth = 360;

    private void PlaceSplitter()
    {
        if (splitPlaced || split.Width < 700) return;
        splitPlaced = true;
        try
        {
            // Splitter first, then the minimum sizes: each setter checks the others against the current width.
            split.SplitterDistance = Math.Max(LeftMinWidth, split.Width - 500);
            split.Panel1MinSize = LeftMinWidth;
            split.Panel2MinSize = RightMinWidth;
        }
        catch (InvalidOperationException)
        {
            // Too small to place now: the defaults are used.
        }
    }

    private void AddPage(string title, EditorPage page)
    {
        var tab = new TabPage(title) { Padding = new Padding(4) };
        tab.Controls.Add(page);
        pages.TabPages.Add(tab);
        allTabs.Add(tab);
    }

    // ------------------------------------------------------------------ palette

    private FlowLayoutPanel BuildPalette(out ComboBox types, out ComboBox rooms, out ComboBox gateTypes, out TextBox group, out ComboBox snap)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Width = 142,
            Padding = new Padding(2)
        };

        void Heading(string text)
        {
            Label label = Ui.Bold(L.T(text));
            label.Margin = new Padding(3, 8, 3, 1);
            panel.Controls.Add(label);
        }

        void Tool(EditorTool tool, string text, string help)
        {
            var button = new ToolButton(L.T(text));
            context.ToolTip.SetToolTip(button, L.T(help));
            button.Click += (_, _) =>
            {
                canvas.Tool = tool;
                canvas.Focus();
            };
            toolButtons[tool] = button;
            panel.Controls.Add(button);
        }

        Heading("Terrain");
        Tool(EditorTool.Select, "Select / move", "Click selects, drag moves. Move the view with the middle button, the right button or Space+drag.");
        Tool(EditorTool.Floor, "Floor", "Left button paints floor tiles, right button erases. With \"Rectangle\" on, drag a rectangle.");
        Tool(EditorTool.Walls, "Walls", "Left button closes a wall cell, right button opens it. Walls only exist on tiles with floor.");

        Heading("Dungeon");
        Tool(EditorTool.Entrance, "Entrance", "Click to set where the planes appear (nascimento).");
        Tool(EditorTool.Exit, "Exit", "Click to place the point where the exit carrier appears.");
        Tool(EditorTool.Checkpoint, "Checkpoint", "Click to place a respawn point.");
        Tool(EditorTool.Gate, "Gate", "Drag along a row (or a column) of cells to place a gate. The type is chosen below.");
        Tool(EditorTool.Unit, "Enemy", "Click to place an enemy of the chosen type in the chosen room. Right button removes one.");

        Heading("Markers");
        Tool(EditorTool.Point, "Point", "A named place the script or the server looks up.");
        Tool(EditorTool.GroupPoint, "Group point", "A place that belongs to a group of points (the group name is chosen below).");
        Tool(EditorTool.Area, "Area", "Drag a rectangle: the zone where the enemy AI of a room stays.");
        Tool(EditorTool.Line, "Line", "Click to place a line at that v (wakes up the room when the player crosses it).");
        Tool(EditorTool.Route, "Route", "Click to add points to a route; Esc ends it.");
        Tool(EditorTool.Sign, "Sign", "A text on the floor, shown when a plane comes near.");

        Heading("Options");
        optionsHeading = (Label)panel.Controls[panel.Controls.Count - 1];

        types = OptionBox(panel, "Enemy type", "Unit type the Enemy tool places (the types of this map, Units tab).", out Label typeLabel);
        rooms = OptionBox(panel, "Room", "Room that receives the enemies placed with the Enemy tool (Rooms tab).", out Label roomLabel);
        gateTypes = OptionBox(panel, "Gate type", "Unit type the gate is made of: the blocks that close the way until the gate opens.", out Label gateLabel);
        optionControls["type"] = new Control[] { typeLabel, types };
        optionControls["room"] = new Control[] { roomLabel, rooms };
        optionControls["gate"] = new Control[] { gateLabel, gateTypes };

        Label groupLabel = Ui.Text(L.T("Group name"));
        panel.Controls.Add(groupLabel);
        group = new ThemedTextBox { Width = 126, Margin = new Padding(3, 0, 3, 3) };
        panel.Controls.Add(group);
        string groupHelp = L.T("Group of points the Group point tool adds to. A new name creates a new group.");
        context.ToolTip.SetToolTip(groupLabel, groupHelp);
        context.ToolTip.SetToolTip(group, groupHelp);
        optionControls["group"] = new Control[] { groupLabel, group };

        Label snapLabel = Ui.Text(L.T("Snap (u)"));
        panel.Controls.Add(snapLabel);
        snap = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 126, Margin = new Padding(3, 0, 3, 3) };
        snap.Items.AddRange(new object[] { L.T("Off"), "0.25", "0.5", "1", "3" });
        snap.SelectedIndex = 2;
        string snapHelp = L.T("Placing and moving objects rounds their position to this step, in map units (u). Off = free position. A tile is 12 u.");
        context.ToolTip.SetToolTip(snapLabel, snapHelp);
        context.ToolTip.SetToolTip(snap, snapHelp);
        optionControls["snap"] = new Control[] { snapLabel, snap };

        var rectangle = new ThemedCheckBox { Text = L.T("Rectangle"), AutoSize = true, Margin = new Padding(3, 6, 3, 0) };
        var wallTile = new ThemedCheckBox { Text = L.T("Walls by tile"), AutoSize = true, Margin = new Padding(3, 0, 3, 0) };
        context.ToolTip.SetToolTip(rectangle, L.T("Floor and Walls tools: drag a rectangle instead of painting cell by cell."));
        context.ToolTip.SetToolTip(wallTile, L.T("Walls tool: one click closes the whole tile edge (4 cells)."));
        rectangle.CheckedChanged += (_, _) => canvas.RectangleMode = rectangle.Checked;
        wallTile.CheckedChanged += (_, _) => canvas.WallTileSized = wallTile.Checked;
        panel.Controls.Add(rectangle);
        panel.Controls.Add(wallTile);
        optionControls["rectangle"] = new Control[] { rectangle };
        optionControls["wallTile"] = new Control[] { wallTile };

        // The boxes write into the canvas options.
        ComboBox typeBox = types;
        ComboBox roomBoxLocal = rooms;
        ComboBox gateBoxLocal = gateTypes;
        TextBox groupLocal = group;
        ComboBox snapLocal = snap;
        typeBox.SelectedIndexChanged += (_, _) => { if (!settingChoices) canvas.PlaceType = typeBox.Text; };
        roomBoxLocal.SelectedIndexChanged += (_, _) => { if (!settingChoices) canvas.PlaceRoom = roomBoxLocal.Text; };
        gateBoxLocal.SelectedIndexChanged += (_, _) => { if (!settingChoices) canvas.GateType = gateBoxLocal.Text; };
        groupLocal.TextChanged += (_, _) => canvas.PlaceGroup = groupLocal.Text;
        snapLocal.SelectedIndexChanged += (_, _) =>
        {
            canvas.SnapStep = snapLocal.SelectedIndex <= 0
                ? 0
                : double.Parse(snapLocal.Text, System.Globalization.CultureInfo.InvariantCulture);
        };
        canvas.SnapStep = 0.5;
        return panel;
    }

    private ComboBox OptionBox(FlowLayoutPanel panel, string caption, string help, out Label label)
    {
        label = Ui.Text(L.T(caption));
        panel.Controls.Add(label);
        var box = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 126, Margin = new Padding(3, 0, 3, 3), DropDownWidth = 260 };
        panel.Controls.Add(box);
        context.ToolTip.SetToolTip(label, L.T(help));
        context.ToolTip.SetToolTip(box, L.T(help));
        return box;
    }

    private void UpdateToolButtons()
    {
        foreach (KeyValuePair<EditorTool, ToolButton> pair in toolButtons) pair.Value.Checked = pair.Key == canvas.Tool;
        UpdateOptions();
    }

    // The left side shows only the options the chosen tool uses.
    private void UpdateOptions()
    {
        EditorTool tool = canvas.Tool;
        bool places = tool is EditorTool.Select or EditorTool.Entrance or EditorTool.Exit or EditorTool.Checkpoint or EditorTool.Point or
            EditorTool.GroupPoint or EditorTool.Sign or EditorTool.Line or EditorTool.Unit or EditorTool.Route;
        var visible = new Dictionary<string, bool>
        {
            ["type"] = tool == EditorTool.Unit,
            ["room"] = tool == EditorTool.Unit,
            ["gate"] = tool == EditorTool.Gate,
            ["group"] = tool == EditorTool.GroupPoint,
            ["snap"] = places,
            ["rectangle"] = tool is EditorTool.Floor or EditorTool.Walls,
            ["wallTile"] = tool == EditorTool.Walls
        };
        foreach (KeyValuePair<string, Control[]> pair in optionControls)
        {
            foreach (Control control in pair.Value) control.Visible = visible[pair.Key];
        }
        if (optionsHeading != null) optionsHeading.Visible = visible.Values.Any(v => v);
    }

    // The lists of the palette follow the map: unit types, rooms and gate types.
    private void RefreshChoices()
    {
        settingChoices = true;
        try
        {
            FillBox(unitTypeBox, editor.TypeNames().ToList(), canvas.PlaceType);
            FillBox(roomBox, editor.RoomNames().ToList(), canvas.PlaceRoom);
            FillBox(gateTypeBox, editor.TypeNames().ToList(), canvas.GateType);
        }
        finally
        {
            settingChoices = false;
        }
        canvas.PlaceType = unitTypeBox.Text;
        canvas.PlaceRoom = roomBox.Text;
        canvas.GateType = gateTypeBox.Text;
    }

    private static void FillBox(ComboBox box, List<string> values, string keep)
    {
        box.BeginUpdate();
        box.Items.Clear();
        foreach (string value in values) box.Items.Add(value);
        box.EndUpdate();
        int index = values.IndexOf(keep);
        if (index < 0 && values.Count > 0) index = 0;
        box.SelectedIndex = index;
    }

    // ------------------------------------------------------------------ menu

    private ToolStripMenuItem Item(string text, Action action, string? shortcut = null)
    {
        var item = new ToolStripMenuItem(text);
        if (shortcut != null) item.ShortcutKeyDisplayString = shortcut;
        item.Click += (_, _) => action();
        return item;
    }

    private ToolStripMenuItem Toggle(string text, bool initial, Action<bool> apply)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = initial };
        item.CheckedChanged += (_, _) =>
        {
            apply(item.Checked);
            canvas.Invalidate();
        };
        return item;
    }

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem(L.T("File"));
        file.DropDownItems.Add(Item(L.T("New map..."), () => NewMap(false), "Ctrl+N"));
        file.DropDownItems.Add(Item(L.T("New empty dungeon..."), () => NewMap(true)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item(L.T("Open..."), OpenMap, "Ctrl+O"));
        openServerMenu.DropDownOpening += (_, _) => FillServerMaps();
        openServerMenu.DropDownItems.Add(new ToolStripMenuItem("...") { Enabled = false });
        file.DropDownItems.Add(openServerMenu);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item(L.T("Save"), () => Save(false), "Ctrl+S"));
        file.DropDownItems.Add(Item(L.T("Save as..."), () => Save(true)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item(L.T("Show the maps folder"), () => Ui.OpenFolder(this, LauncherPaths.MapsFolder)));

        var edit = new ToolStripMenuItem(L.T("Edit"));
        edit.DropDownOpening += (_, _) =>
        {
            undoItem.Enabled = session.CanUndo;
            redoItem.Enabled = session.CanRedo;
        };
        edit.DropDownItems.Add(undoItem);
        edit.DropDownItems.Add(redoItem);
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add(Item(L.T("Duplicate"), () => canvas.DuplicateSelected(), "Ctrl+D"));
        edit.DropDownItems.Add(Item(L.T("Delete"), () => canvas.DeleteSelected(), "Del"));

        var view = new ToolStripMenuItem(L.T("View"));
        view.DropDownItems.Add(Toggle(L.T("Floor"), canvas.ShowFloor, on => canvas.ShowFloor = on));
        view.DropDownItems.Add(Toggle(L.T("Walls"), canvas.ShowWalls, on => canvas.ShowWalls = on));
        view.DropDownItems.Add(Toggle(L.T("Grid"), canvas.ShowGrid, on => canvas.ShowGrid = on));
        view.DropDownItems.Add(Toggle(L.T("Areas and lines"), canvas.ShowAreas, on => canvas.ShowAreas = on));
        view.DropDownItems.Add(Toggle(L.T("Gates"), canvas.ShowGates, on => canvas.ShowGates = on));
        view.DropDownItems.Add(Toggle(L.T("Points"), canvas.ShowPoints, on => canvas.ShowPoints = on));
        view.DropDownItems.Add(Toggle(L.T("Enemies"), canvas.ShowUnits, on => canvas.ShowUnits = on));
        view.DropDownItems.Add(Toggle(L.T("Decoration"), canvas.ShowDecor, on => canvas.ShowDecor = on));
        view.DropDownItems.Add(Toggle(L.T("Names"), canvas.ShowLabels, on => canvas.ShowLabels = on));
        ToolStripMenuItem pictures = Toggle(L.T("Game pictures"), canvas.UsePictures && EditorAssets.Current.HasPictures, on => canvas.UsePictures = on);
        pictures.Enabled = EditorAssets.Current.HasPictures;
        if (!pictures.Enabled) pictures.ToolTipText = L.T("The editor-assets folder was not found next to the launcher: shapes and colors are drawn instead of pictures of the game.");
        view.DropDownItems.Add(pictures);
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Item(L.T("Fit the map in the window"), () => canvas.FitToMap(), "F"));
        view.DropDownItems.Add(Item(L.T("Zoom in"), () => canvas.ZoomBy(1.25)));
        view.DropDownItems.Add(Item(L.T("Zoom out"), () => canvas.ZoomBy(0.8)));

        var tools = new ToolStripMenuItem(L.T("Tools"));
        tools.DropDownItems.Add(Item(L.T("Check map..."), CheckMap));
        tools.DropDownItems.Add(Item(L.T("Center the map on the grid"), CenterMap));
        tools.DropDownItems.Add(Item(L.T("Edit the file text..."), EditText));
        tools.DropDownItems.Add(new ToolStripSeparator());
        var ai = new ToolStripMenuItem(L.T("Enemy AI editor...")) { Enabled = EnemyAiEditors.Available };
        if (!ai.Enabled) ai.ToolTipText = L.T("The Enemy AI tab of the launcher is not available.");
        ai.Click += (_, _) => EnemyAiEditors.Current?.Edit(this, "", new System.Text.Json.Nodes.JsonObject(), () => { }, () => { });
        tools.DropDownItems.Add(ai);

        menu.Items.AddRange(new ToolStripItem[] { file, edit, view, tools });
    }

    private void FillServerMaps()
    {
        openServerMenu.DropDownItems.Clear();
        List<string> maps = DungeonCatalogFile.AvailableMaps();
        if (maps.Count == 0)
        {
            openServerMenu.DropDownItems.Add(new ToolStripMenuItem(L.T("(no maps found)")) { Enabled = false });
            return;
        }
        foreach (string map in maps)
        {
            string path = Path.Combine(LauncherPaths.MapsFolder, map + ".json");
            openServerMenu.DropDownItems.Add(Item(map, () => OpenPath(path)));
        }
    }

    // ------------------------------------------------------------------ files

    private string DisplayName =>
        session.Document.FilePath != null ? Path.GetFileName(session.Document.FilePath) : session.Document.Id + ".json";

    private void UpdateFileLabel()
    {
        fileLabel.Text = DisplayName + (session.Dirty ? " *" : "") + (session.Document.FilePath == null ? "  (" + L.T("not saved yet") + ")" : "");
    }

    // Asks what to do with the changes that were not saved. False = the user cancelled.
    public bool ConfirmDiscard()
    {
        if (!session.Dirty) return true;
        DialogResult answer = MessageBox.Show(this, L.F("Save the changes to \"{0}\"?", DisplayName), LauncherInfo.Title,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false;
        return answer != DialogResult.Yes || Save(false);
    }

    private void NewMap(bool blank)
    {
        if (!ConfirmDiscard()) return;
        using var dialog = new NewMapDialog(blank);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null) return;
        session.SetDocument(dialog.Result, dirty: true);
        canvas.FitToMap();
        issuesLabel.Text = "";
        statusLabel.Text = dialog.Copied
            ? L.T("New map made from a copy of the dungeon. Save it to keep it.")
            : L.T("New empty map. Paint the floor with the Floor tool.");
    }

    private void OpenMap()
    {
        if (!ConfirmDiscard()) return;
        using var dialog = new OpenFileDialog
        {
            Title = L.T("Open map"),
            Filter = L.T("Map files (*.json)") + "|*.json",
            InitialDirectory = Directory.Exists(LauncherPaths.MapsFolder) ? LauncherPaths.MapsFolder : LauncherPaths.BaseFolder
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadFile(dialog.FileName);
    }

    private void OpenPath(string path)
    {
        if (!ConfirmDiscard()) return;
        LoadFile(path);
    }

    private void LoadFile(string path)
    {
        try
        {
            MapDocument document = MapDocument.Load(path);
            session.SetDocument(document, dirty: false);
            canvas.FitToMap();
            issuesLabel.Text = "";
            statusLabel.Text = L.F("Opened {0}.", Path.GetFileName(path));
        }
        catch (Exception e)
        {
            Ui.ShowError(this, L.F("Could not open the map:\n{0}", e.Message));
        }
    }

    private string? AskSavePath(MapDocument document)
    {
        string folder = document.FilePath != null ? Path.GetDirectoryName(document.FilePath) ?? LauncherPaths.MapsFolder : LauncherPaths.MapsFolder;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch
        {
            folder = LauncherPaths.BaseFolder;
        }
        using var dialog = new SaveFileDialog
        {
            Title = L.T("Save map"),
            Filter = L.T("Map files (*.json)") + "|*.json",
            DefaultExt = "json",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = folder,
            FileName = (document.Id.Length > 0 ? document.Id : "new_map") + ".json"
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    // Saves the map. A map with errors is only saved as a draft ("draft": true), when the user agrees.
    public bool Save(bool saveAs)
    {
        MapDocument document = session.Document;
        string? path = document.FilePath;
        if (saveAs || path == null)
        {
            path = AskSavePath(document);
            if (path == null) return false;
        }

        string id = Path.GetFileNameWithoutExtension(path);
        if (!MapFactory.IsValidId(id))
        {
            Ui.ShowError(this, L.T("The file name is the id of the map: use only lowercase letters, digits and _ (up to 60 characters)."));
            return false;
        }
        if (document.Id != id) document.Id = id;

        List<MapIssue> issues = MapValidator.Validate(document);
        int errors = issues.Count(i => i.Level == IssueLevel.Error);
        int warnings = issues.Count - errors;
        bool draft = false;
        if (errors > 0)
        {
            using var dialog = new ValidationDialog(issues, CheckMode.BlockedSave);
            if (dialog.ShowDialog(this) != DialogResult.Yes) return false;
            draft = true;
        }

        if (draft) document.Root["draft"] = true;
        else document.Root.Remove("draft");

        try
        {
            BackupOnce(path);
            document.Save(path);
        }
        catch (Exception e)
        {
            Ui.ShowError(this, L.F("Could not save the map:\n{0}", e.Message));
            return false;
        }

        session.MarkSaved();
        UpdateFileLabel();
        if (draft) issuesLabel.Text = L.F("Draft: {0} error(s)", errors);
        else issuesLabel.Text = warnings > 0 ? L.F("{0} warning(s): Tools > Check map", warnings) : "";
        statusLabel.Text = L.F("Saved {0}.", path) + (draft ? " " + L.T("It is a draft: it may not work on the server until the errors are fixed.") : "");
        return true;
    }

    // The first time a file of the maps folder is overwritten in this session, the old one is copied to server\_backup.
    private void BackupOnce(string path)
    {
        if (!File.Exists(path) || !backedUp.Add(path)) return;
        try
        {
            string folder = LauncherPaths.NewBackupFolder("maps");
            File.Copy(path, Path.Combine(folder, Path.GetFileName(path)), true);
        }
        catch
        {
            // The backup is a courtesy: saving goes on.
        }
    }

    // ------------------------------------------------------------------ tools

    private void CheckMap()
    {
        List<MapIssue> issues = MapValidator.Validate(session.Document);
        int errors = issues.Count(i => i.Level == IssueLevel.Error);
        issuesLabel.Text = issues.Count == 0 ? "" : L.F("{0} error(s), {1} warning(s)", errors, issues.Count - errors);
        using var dialog = new ValidationDialog(issues, CheckMode.Report);
        dialog.ShowDialog(this);
    }

    private void CenterMap()
    {
        session.Checkpoint();
        if (!MapTransforms.CenterOnGrid(session.Document))
        {
            Ui.ShowInfo(this, L.T("The map has no floor yet, or it is already centered."));
            return;
        }
        session.Touch();
        ReloadVisiblePage();
        canvas.FitToMap();
    }

    private void EditText()
    {
        string before = session.Document.Serialize();
        using var dialog = new RawJsonDialog(before);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        string text = dialog.EditedText.Replace("\r\n", "\n");
        if (text == before.Replace("\r\n", "\n")) return;
        try
        {
            session.ReplaceText(text);
        }
        catch (Exception e)
        {
            Ui.ShowError(this, L.F("The text is not a valid map:\n{0}", e.Message));
        }
    }

    // ------------------------------------------------------------------ canvas and session events

    private void OnCommand(string command)
    {
        switch (command)
        {
            case "undo":
                session.Undo();
                break;
            case "redo":
                session.Redo();
                break;
            case "save":
                Save(false);
                break;
        }
    }

    // The object picked on the canvas is shown in the tabs that have its fields, and only those tabs stay visible.
    // With nothing selected every tab is back.
    private void OnCanvasSelection()
    {
        MapObject? item = canvas.Selected;
        if (item == null)
        {
            ShowTabs(allTabs, null);
            return;
        }

        TabPage? first = null;
        var related = new HashSet<TabPage>();
        foreach (EditorPage page in pageList)
        {
            // A page that was not on screen yet has no lists: load it before asking it to show the object.
            if (stalePages.Remove(page)) page.Reload();
            if (!page.ShowObject(item)) continue;
            TabPage? tab = TabOf(page);
            if (tab == null) continue;
            related.Add(tab);
            first ??= tab;
        }
        if (related.Count == 0)
        {
            ShowTabs(allTabs, null);
            return;
        }
        ShowTabs(allTabs.Where(related.Contains).ToList(), first);
    }

    private TabPage? TabOf(EditorPage? page)
    {
        if (page == null) return null;
        return allTabs.FirstOrDefault(tab => tab.Controls.Contains(page));
    }

    // Puts exactly these tabs in the tab control (in their usual order) and selects one.
    private void ShowTabs(IReadOnlyList<TabPage> wanted, TabPage? select)
    {
        bool same = pages.TabPages.Count == wanted.Count && wanted.Select((tab, i) => ReferenceEquals(pages.TabPages[i], tab)).All(x => x);
        if (!same)
        {
            TabPage? keep = pages.SelectedTab;
            pages.SuspendLayout();
            try
            {
                pages.TabPages.Clear();
                pages.TabPages.AddRange(wanted.ToArray());
                if (select == null && keep != null && wanted.Contains(keep)) select = keep;
            }
            finally
            {
                pages.ResumeLayout(true);
            }
        }
        tabsHint.Visible = wanted.Count < allTabs.Count;
        if (select != null && !ReferenceEquals(pages.SelectedTab, select)) pages.SelectedTab = select;
        ReloadVisiblePage();
    }

    // The canvas added, removed or moved objects: the lists of the visible page are read again.
    private void OnStructureChanged()
    {
        foreach (EditorPage page in pageList) stalePages.Add(page);
        ReloadVisiblePage();
        RefreshChoices();
    }

    private void OnSessionChanged()
    {
        UpdateFileLabel();
        // The fields of the pages show the numbers the canvas just changed.
        if (canvas.Focused || canvas.Capture) RefreshForms(pages);
    }

    private void OnSessionReplaced()
    {
        foreach (EditorPage page in pageList) stalePages.Add(page);
        ReloadVisiblePage();
        RefreshChoices();
        UpdateFileLabel();
    }

    private void OnGameFolderChanged()
    {
        EditorAssets.Load(context.GameFolder);
        canvas.Invalidate();
    }

    private void ReloadVisiblePage()
    {
        EditorPage? page = PageOf(pages.SelectedTab);
        if (page == null) return;
        if (stalePages.Remove(page)) page.Reload();
    }

    private void ReloadStalePage() => ReloadVisiblePage();

    private EditorPage? PageOf(TabPage? tab)
    {
        if (tab == null) return null;
        foreach (Control control in tab.Controls)
        {
            if (control is EditorPage page) return page;
        }
        return null;
    }

    private static void RefreshForms(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is JsonForm form) form.RefreshValues();
            else RefreshForms(child);
        }
    }

    // ------------------------------------------------------------------ keys

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S:
                Save(false);
                return true;
            case Keys.Control | Keys.O:
                OpenMap();
                return true;
            case Keys.Control | Keys.N:
                NewMap(false);
                return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }
}
