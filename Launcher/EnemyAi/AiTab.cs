using System.Text.Json.Nodes;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// The "Enemy AI" tab: the list of AI files (left), the editor of the selected one (middle) and the simulator (right).
// Every AI and every behavior is its own .json file in the server's ai folder, so they can be copied, shared and loaded
// as needed; the server reads them again by itself when they change.
internal sealed class AiTab : Panel
{
    private readonly LauncherContext context;
    private readonly AiStore store;
    private readonly TreeView tree;
    private readonly TreeNode brainsNode;
    private readonly TreeNode behaviorsNode;
    private readonly AiBrainEditor brainEditor;
    private readonly AiBehaviorEditor behaviorEditor;
    private readonly Label placeholder;
    private readonly TextBox problems;
    private readonly AiSimView simView;
    private readonly Label idHint;
    private readonly SplitContainer outer;
    private readonly SplitContainer inner;
    private readonly Dictionary<AiDoc, TreeNode> nodes = new Dictionary<AiDoc, TreeNode>();
    private readonly ToolStripMenuItem duplicateItem;
    private readonly ToolStripMenuItem renameItem;
    private readonly ToolStripMenuItem moveItem;
    private readonly ToolStripMenuItem deleteItem;
    private readonly ToolStripMenuItem saveItem;
    private readonly ToolStripMenuItem saveAllItem;
    private readonly ToolStripMenuItem saveAsItem;
    private readonly ToolStripMenuItem revertItem;
    private readonly ToolStripMenuItem exportItem;
    private readonly MenuStrip menu;
    private readonly Button unitButton;
    private readonly ComboBox aiBox;
    private readonly Button loadOtherButton;
    private List<AiUnitGroup> unitGroups = new List<AiUnitGroup>();
    private bool fillingAiBox;
    private readonly CheckBox armedOnly;
    private readonly Label banner;
    private readonly Button backButton;
    private readonly Button loadButton;
    private readonly Dictionary<string, AiUnitChoice> byLabel = new Dictionary<string, AiUnitChoice>(StringComparer.OrdinalIgnoreCase);
    private static string lastChoiceLabel = "";

    // The unit in the simulator, the file selected in the list (only for the file buttons), the file loaded on the unit
    // just to be edited (null = the unit's own AI), and the file the editors show: loaded ?? the unit's own AI.
    private AiUnitChoice? choice;
    // The unit type the map editor opened here last (kept in the list of units while the tab lives).
    private AiUnitChoice? mapChoice;
    private AiDoc? picked;
    private AiDoc? loaded;
    private AiDoc? current;
    private bool placed;
    private bool refreshing;
    private bool building;
    // A generic AI was edited: the edits went to this new AI (shown in the banner until it is saved); the switch is queued.
    private AiDoc? divertedTo;
    private string divertedFrom = "";
    private bool diverting;

    public AiTab(LauncherContext context, AiStore store)
    {
        this.context = context;
        this.store = store;
        Dock = DockStyle.Fill;

        if (EditorAssets.Current.Folder.Length == 0) EditorAssets.Load(context.GameFolder);

        // ---- menu (the same kind of menu bar as the map editor's)
        saveItem = Item(L.T("Save"), SaveCurrent, "Ctrl+S");
        saveAllItem = Item(L.T("Save all"), SaveAll, "Ctrl+Shift+S");
        saveAsItem = Item(L.T("Save as..."), SaveAs);
        saveAsItem.ToolTipText = L.T("Saves what is open under a new name. A generic AI is never overwritten: it is saved as a new AI made for the unit.");
        revertItem = Item(L.T("Revert"), Revert);
        duplicateItem = Item(L.T("Duplicate"), DuplicateCurrent);
        renameItem = Item(L.T("Rename..."), RenameCurrent);
        moveItem = Item(L.T("Move to folder..."), MoveCurrent);
        deleteItem = Item(L.T("Delete"), DeleteCurrent);
        exportItem = Item(L.T("Export..."), ExportCurrent);
        exportItem.ToolTipText = L.T("Writes one file with this AI and the behaviors it uses, to send to someone else (Import... reads it).");
        menu = BuildMenu();

        // ---- the unit: what the simulator runs and what the editor is allowed to edit
        unitButton = Ui.Button("", (_, _) => OpenUnitPicker());
        unitButton.Width = 400;
        unitButton.TextAlign = ContentAlignment.MiddleLeft;
        aiBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        aiBox.SelectedIndexChanged += (_, _) => AiBoxChosen();
        loadOtherButton = Ui.Button(L.T("Load another AI..."), (_, _) => ShowLoadOtherMenu());
        armedOnly = new ThemedCheckBox { Text = L.T("Armed units only"), AutoSize = true, Checked = true };
        armedOnly.CheckedChanged += (_, _) => FillUnits(true);
        backButton = Ui.Button(L.T("Back to the unit's AI"), (_, _) => BackToOwn());
        banner = Ui.Wrapping("", 800, false);
        context.ToolTip.SetToolTip(unitButton, L.T("The unit the simulator flies. Click to pick one: the list is sorted by where the unit lives (a dungeon, a biome of the open world, generic, or no origin) and each entry runs the AI that place gives it. Units written in a different color have no AI."));
        context.ToolTip.SetToolTip(aiBox, L.T("The AIs that fit this unit: the ones made for it first, then the generic ones. Picking one loads it on the unit to edit and test; \"Back to the unit's AI\" returns to the AI the unit really runs."));
        context.ToolTip.SetToolTip(loadOtherButton, L.T("Loads an AI that is not made for this unit, or a file from anywhere on this computer, on the unit."));
        context.ToolTip.SetToolTip(backButton, L.T("Stops testing the loaded file on this unit and goes back to the AI the unit really uses."));
        var unitBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
        unitBar.Controls.AddRange(new Control[] { Ui.Flow(Ui.Bold(L.T("Unit")), unitButton), Ui.Flow(Ui.Bold(L.T("AI")), aiBox), loadOtherButton, armedOnly, backButton, banner });
        unitBar.SetFlowBreak(backButton, true);

        // ---- left: the files
        tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true };
        brainsNode = new TreeNode(L.T("AIs"));
        behaviorsNode = new TreeNode(L.T("Behaviors"));
        tree.Nodes.Add(brainsNode);
        tree.Nodes.Add(behaviorsNode);
        tree.AfterSelect += (_, e) =>
        {
            if (refreshing) return;
            picked = e.Node?.Tag as AiDoc;
            UpdateHint();
            UpdateButtons();
        };
        // Right click on a file: the same actions as the Edit menu, where the file is.
        tree.NodeMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.Node?.Tag is not AiDoc) return;
            tree.SelectedNode = e.Node;
            ShowFileMenu(e.Location);
        };
        // Right click on a file: duplicate or delete it (the same actions as the Edit menu, on the file under the mouse).
        var treeMenu = new ContextMenuStrip { Renderer = new EditorRenderer() };
        var menuDuplicate = new ToolStripMenuItem(L.T("Duplicate"));
        menuDuplicate.Click += (_, _) => DuplicateCurrent();
        var menuDelete = new ToolStripMenuItem(L.T("Delete"));
        menuDelete.Click += (_, _) => DeleteCurrent();
        treeMenu.Items.Add(menuDuplicate);
        treeMenu.Items.Add(menuDelete);
        tree.NodeMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.Node?.Tag is not AiDoc doc) return;
            tree.SelectedNode = e.Node;
            picked = doc;
            UpdateHint();
            UpdateButtons();
            menuDuplicate.Enabled = doc.LoadError == null;
            treeMenu.Show(tree, e.Location);
        };
        tree.NodeMouseDoubleClick += (_, e) =>
        {
            if (e.Node?.Tag is not AiDoc doc) return;
            picked = doc;
            LoadPicked();
        };
        idHint = Ui.Wrapping("", 210, true);
        idHint.Dock = DockStyle.Bottom;
        Button copyId = Ui.Button(L.T("Copy id"), (_, _) =>
        {
            if (picked != null) Clipboard.SetText(picked.Id);
        });
        loadButton = Ui.Primary(L.T("Load on the unit"), (_, _) => LoadPicked());
        loadButton.Dock = DockStyle.Bottom;
        context.ToolTip.SetToolTip(loadButton, L.T("Puts the selected AI or behavior on the unit above, to edit it and try it there. The unit's own AI comes back with \"Back to the unit's AI\"."));
        copyId.Dock = DockStyle.Bottom;
        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(tree);
        left.Controls.Add(loadButton);
        left.Controls.Add(copyId);
        left.Controls.Add(idHint);

        // ---- middle: the editors
        brainEditor = new AiBrainEditor(store) { Visible = false };
        behaviorEditor = new AiBehaviorEditor(store) { Visible = false };
        placeholder = Ui.Wrapping(
            L.T("Pick an AI or a behavior on the left, or make a new one.\n\nAn AI is a set of states (for example: wait, chase, retreat). Each state acts like a behavior and has transitions that say when to change to another state. A behavior is one way of acting: how to move and shoot.\n\nThe simulator on the right shows what you build, with the art and the guns of any game unit."),
            400, true);
        placeholder.Padding = new Padding(10);
        brainEditor.Edited += OnEdited;
        behaviorEditor.Edited += OnEdited;

        problems = new ThemedTextBox
        {
            Dock = DockStyle.Bottom, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Height = 70, Font = new Font(FontFamily.GenericMonospace, 8.5f)
        };
        var middle = new Panel { Dock = DockStyle.Fill };
        middle.Controls.Add(brainEditor);
        middle.Controls.Add(behaviorEditor);
        middle.Controls.Add(placeholder);
        middle.Controls.Add(problems);

        // ---- right: the simulator
        simView = new AiSimView(context);
        simView.RunningStateChanged += id => brainEditor.SetRunningState(id);
        simView.SummonResolver = ResolveSummon;

        inner = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 5 };
        inner.Panel1.Controls.Add(middle);
        inner.Panel2.Controls.Add(simView);
        outer = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 5 };
        outer.Panel1.Controls.Add(left);
        outer.Panel2.Controls.Add(inner);

        Controls.Add(outer);
        Controls.Add(unitBar);
        Controls.Add(menu);

        store.Changed += RefreshTree;
        context.GameFolderChanged += OnGameFolderChanged;
        SizeChanged += (_, _) =>
        {
            banner.MaximumSize = new Size(Math.Max(300, Width - 40), 0);
            PlaceSplitters();
        };
        RefreshTree();
        FillUnits(false);
    }

    // ------------------------------------------------------------------ menu

    private static ToolStripMenuItem Item(string text, Action action, string? shortcut = null)
    {
        var item = new ToolStripMenuItem(text);
        if (shortcut != null) item.ShortcutKeyDisplayString = shortcut;
        item.Click += (_, _) => action();
        return item;
    }

    private MenuStrip BuildMenu()
    {
        var bar = new MenuStrip { Renderer = new EditorRenderer(), Dock = DockStyle.Top };

        var file = new ToolStripMenuItem(L.T("File"));
        file.DropDownItems.Add(Item(L.T("New AI"), () => NewDoc(AiKind.Brain), "Ctrl+N"));
        file.DropDownItems.Add(Item(L.T("New behavior"), () => NewDoc(AiKind.Behavior)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(saveItem);
        file.DropDownItems.Add(saveAsItem);
        file.DropDownItems.Add(saveAllItem);
        file.DropDownItems.Add(revertItem);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item(L.T("Import..."), ImportFile));
        file.DropDownItems.Add(exportItem);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item(L.T("Open folder"), OpenFolder));
        file.DropDownItems.Add(Item(L.T("Reload"), ReloadFromDisk));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item(L.T("Enemy types..."), OpenEnemyTypes));

        var edit = new ToolStripMenuItem(L.T("Edit"));
        edit.DropDownItems.Add(duplicateItem);
        edit.DropDownItems.Add(renameItem);
        edit.DropDownItems.Add(moveItem);
        edit.DropDownItems.Add(deleteItem);

        var help = new ToolStripMenuItem(L.T("Help"));
        help.DropDownItems.Add(Item(L.T("Help topics..."), () => ShowHelp(0), "F1"));
        help.DropDownItems.Add(new ToolStripSeparator());
        IReadOnlyList<HelpTopic> topics = AiHelp.Topics;
        for (int i = 0; i < topics.Count; i++)
        {
            int index = i;
            help.DropDownItems.Add(Item(L.T(topics[i].Title), () => ShowHelp(index)));
        }

        bar.Items.AddRange(new ToolStripItem[] { file, edit, help });
        return bar;
    }

    private void ShowHelp(int topic)
    {
        HelpWindow.ShowFor(FindForm() ?? (IWin32Window)this, "ai", L.T("Help: Enemy AI editor"), AiHelp.Topics, topic);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S:
                if (saveItem.Enabled) SaveCurrent();
                return true;
            case Keys.Control | Keys.Shift | Keys.S:
                if (saveAllItem.Enabled) SaveAll();
                return true;
            case Keys.Control | Keys.N:
                NewDoc(AiKind.Brain);
                return true;
            case Keys.F1:
                ShowHelp(0);
                return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    // The map editor's "Load unit in the AI editor": the unit type goes into the simulator with its game unit, its speed, fire
    // rate and aim values, and its AI opens in the editor. Without an AI the unit is ready for one to be loaded on it.
    public void OpenType(string typeName, JsonObject type)
    {
        mapChoice = AiUnits.FromType(typeName, type, L.T("map type"));
        FillUnits(false, mapChoice.Label);
        AiDoc? own = OwnDoc();
        if (own != null) SelectNode(own);
    }

    // The unit types page and the map checks ask the tab to show an AI: it goes on the unit that uses it in
    // enemies.json (the first one), or is loaded on the current unit when no type uses it.
    public bool OpenBrain(string? id)
    {
        AiDoc? doc = string.IsNullOrWhiteSpace(id) ? null : store.Find(AiKind.Brain, id.Trim());
        if (doc == null) return false;
        AiUnitChoice? owner = byLabel.Values.FirstOrDefault(c => string.Equals(c.OwnAi, doc.Id, StringComparison.OrdinalIgnoreCase));
        if (owner != null && !ReferenceEquals(owner, choice) && owner.Label != choice?.Label) ApplyChoice(owner, false);
        SelectNode(doc);
        picked = doc;
        LoadPicked();
        return true;
    }

    // True when nothing is lost: nothing is unsaved, or the user chose to save / discard.
    public bool ConfirmDiscard()
    {
        if (!store.AnyDirty) return true;
        DialogResult answer = MessageBox.Show(this,
            L.T("Some AI files were changed and not saved.\n\nSave them before closing?"), LauncherInfo.Title,
            MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes) return TrySaveAll();
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            store.Changed -= RefreshTree;
            context.GameFolderChanged -= OnGameFolderChanged;
        }
        base.Dispose(disposing);
    }

    private void OnGameFolderChanged()
    {
        EditorAssets.Load(context.GameFolder);
        FillUnits(true);
    }

    // Widths are set once the tab has its real size (a SplitContainer rejects values while it is still 150 px wide).
    private void PlaceSplitters()
    {
        if (placed || Width < 700) return;
        try
        {
            outer.SplitterDistance = Math.Clamp(Width * 17 / 100, 170, 260);
            inner.SplitterDistance = Math.Max(300, inner.Width * 48 / 100);
            placed = true;
        }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
        {
            // Tried too early: it is tried again on the next resize.
        }
    }

    // ------------------------------------------------------------------ tree

    private void RefreshTree()
    {
        refreshing = true;
        try
        {
            AiDoc? keep = picked;
            nodes.Clear();
            brainsNode.Nodes.Clear();
            behaviorsNode.Nodes.Clear();
            var folders = new Dictionary<string, TreeNode>(StringComparer.OrdinalIgnoreCase);
            // The folders every server has show even when empty, so it is clear where an AI can live.
            foreach (string standard in new[] { AiCatalog.GenericFolder, AiCatalog.WorldFolder, AiCatalog.DungeonsFolder })
                FolderNode(folders, standard);
            foreach (AiDoc doc in store.Brains) AddNode(FolderNode(folders, doc.Origin), doc);
            foreach (AiDoc doc in store.Behaviors) AddNode(behaviorsNode, doc);
            brainsNode.ExpandAll();
            behaviorsNode.Expand();
            if (keep != null && nodes.TryGetValue(keep, out TreeNode? node)) tree.SelectedNode = node;
        }
        finally
        {
            refreshing = false;
        }
        UpdateButtons();
    }

    // The tree node of a folder inside brains\ ("dungeons/arena" is "arena" inside "dungeons"); "" is the AIs node itself.
    private TreeNode FolderNode(Dictionary<string, TreeNode> folders, string origin)
    {
        if (origin.Length == 0) return brainsNode;
        if (folders.TryGetValue(origin, out TreeNode? known)) return known;
        int cut = origin.LastIndexOf('/');
        TreeNode parent = cut < 0 ? brainsNode : FolderNode(folders, origin.Substring(0, cut));
        var node = new TreeNode(origin.Substring(cut + 1)) { ForeColor = Theme.ColorOf(TextRole.Muted) };
        node.ToolTipText = L.T("Folder inside server\ai\brains");
        parent.Nodes.Add(node);
        folders[origin] = node;
        return node;
    }

    private void AddNode(TreeNode parent, AiDoc doc)
    {
        var node = new TreeNode(doc.Title) { Tag = doc };
        if (doc.LoadError != null) node.ForeColor = Theme.ColorOf(TextRole.Bad);
        node.ToolTipText = doc.LoadError ?? doc.DisplayName;
        parent.Nodes.Add(node);
        nodes[doc] = node;
    }

    private void UpdateNode(AiDoc doc)
    {
        if (nodes.TryGetValue(doc, out TreeNode? node)) node.Text = doc.Title;
    }

    private void SelectNode(AiDoc doc)
    {
        picked = doc;
        if (nodes.TryGetValue(doc, out TreeNode? node)) tree.SelectedNode = node;
        UpdateHint();
        UpdateButtons();
    }

    // ------------------------------------------------------------------ the unit

    // Fills the list of units (the enemy types of enemies.json first, then the game units) and picks one.
    // keepState: the same unit stays (and what is loaded on it) instead of starting over.
    private void FillUnits(bool keepState, string? force = null)
    {
        string wanted = force ?? choice?.Label ?? lastChoiceLabel;
        unitGroups = AiUnitOrigins.Build(LauncherPaths.ServerFolder, armedOnly.Checked, mapChoice);
        List<AiUnitChoice> list = unitGroups.SelectMany(g => g.AllUnits()).ToList();
        building = true;
        try
        {
            byLabel.Clear();
            foreach (AiUnitChoice c in list) byLabel.TryAdd(c.Label, c);
        }
        finally
        {
            building = false;
        }
        AiUnitChoice? pick = wanted.Length > 0 && byLabel.TryGetValue(wanted, out AiUnitChoice? same) ? same : DefaultChoice(list);
        ApplyChoice(pick, keepState && pick != null && pick.Label == choice?.Label);
    }

    // The first enemy type that has an AI file; without one, a mobile armed unit of middle strength.
    private AiUnitChoice? DefaultChoice(List<AiUnitChoice> list)
    {
        AiUnitChoice? withAi = list.FirstOrDefault(c => c.IsType && c.OwnAi.Length > 0 && store.Find(AiKind.Brain, c.OwnAi) != null);
        if (withAi != null) return withAi;
        List<GameUnit> candidates = EditorAssets.Current.Units.Values
            .Where(u => u.Armed && !u.Wall && !u.Boss && u.VelMax >= 3 && u.VelMax <= 12 && u.Art.Length > 0 && !u.Art.StartsWith("!") &&
                        !u.Name.StartsWith("tut", StringComparison.OrdinalIgnoreCase) && !u.Name.StartsWith("ally", StringComparison.OrdinalIgnoreCase))
            .OrderBy(u => u.MaxHealth).ThenBy(u => u.Name, StringComparer.Ordinal).ToList();
        GameUnit? middle = candidates.Count == 0 ? null : candidates[candidates.Count / 3];
        return list.FirstOrDefault(c => !c.IsType && middle != null && c.UnitName == middle.Name) ?? list.FirstOrDefault(c => !c.IsType);
    }

    // True when the unit's own AI file exists (a unit written in another color in the picker has none).
    private bool HasAiFile(AiUnitChoice unit) => unit.OwnAi.Length > 0 && store.Find(AiKind.Brain, unit.OwnAi) != null;

    private void OpenUnitPicker()
    {
        if (building) return;
        using var picker = new AiUnitPicker(unitGroups, HasAiFile, choice?.Label ?? "")
        {
            Location = unitButton.PointToScreen(new Point(0, unitButton.Height))
        };
        if (picker.ShowDialog(this) == DialogResult.OK && picker.Chosen != null) ApplyChoice(picker.Chosen, false);
    }

    // Puts a unit in the simulator. What was loaded on the previous unit goes away (an unsaved file stays unsaved in the list).
    private void ApplyChoice(AiUnitChoice? unit, bool keepLoaded)
    {
        choice = unit;
        if (!keepLoaded) loaded = null;
        building = true;
        try
        {
            unitButton.Text = unit == null ? "" : unit.Label;
        }
        finally
        {
            building = false;
        }
        if (unit != null) lastChoiceLabel = unit.Label;
        simView.SetUnit(unit, unit?.LeaderChoice == null ? null : AiSimSetups.Build(unit.LeaderChoice, BrainOf));
        AiUnits.CurrentGuns = simView.UnitGuns.ToList();
        Rebind();
    }

    // The compiled AI with this id (null when there is no such file or it does not compile): for the units the simulator adds
    // next to the edited one (its leader, what it summons).
    private AiBrain? BrainOf(string id)
    {
        AiDoc? doc = store.Find(AiKind.Brain, id);
        return doc == null || doc.LoadError != null ? null : store.Compile(doc, new List<AiProblem>());
    }

    // A unit the AI summons, by the name it gives: a type of the same group as the edited unit (same dungeon or biome) first, then any type,
    // then a game unit of that name. Null when there is none.
    private AiSimSetup? ResolveSummon(string name)
    {
        AiUnitChoice? found = null;
        if (choice != null)
            found = unitGroups.SelectMany(LeafGroups).FirstOrDefault(g => g.Units.Contains(choice))?.Units
                .FirstOrDefault(u => u.TypeId.Equals(name, StringComparison.OrdinalIgnoreCase));
        found ??= unitGroups.SelectMany(g => g.AllUnits()).FirstOrDefault(u => u.TypeId.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (found == null && EditorAssets.Current.Units.ContainsKey(name)) found = new AiUnitChoice { Label = name, UnitName = name };
        return found == null ? null : AiSimSetups.Build(found, BrainOf);
    }

    private static IEnumerable<AiUnitGroup> LeafGroups(AiUnitGroup group)
    {
        yield return group;
        foreach (AiUnitGroup child in group.Children.SelectMany(LeafGroups)) yield return child;
    }

    // The AI file the enemy type of the unit really uses (null for a plain unit or a missing file).
    private AiDoc? OwnDoc()
    {
        return choice == null || choice.OwnAi.Length == 0 ? null : store.Find(AiKind.Brain, choice.OwnAi);
    }

    private void LoadPicked()
    {
        if (picked == null) return;
        if (picked.LoadError != null)
        {
            Ui.ShowError(this, L.F("This file could not be read, so it cannot be edited here:\n\n{0}\n\nFix it with a text editor (the file is in the ai folder) and press Reload.", picked.LoadError));
            return;
        }
        loaded = ReferenceEquals(picked, OwnDoc()) ? null : picked;
        Rebind();
    }

    private void BackToOwn()
    {
        if (loaded == null) return;
        if (loaded.Dirty)
        {
            DialogResult answer = MessageBox.Show(this,
                L.F("\"{0}\" has changes that are not saved.\n\nYes: save it and go back.\nNo: go back without saving (it stays marked as unsaved in the list).", loaded.Id),
                LauncherInfo.Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            if (answer == DialogResult.Yes)
            {
                try
                {
                    store.Save(loaded);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
                {
                    Ui.ShowError(this, L.F("Could not save the file: {0}", e.Message));
                    return;
                }
            }
        }
        loaded = null;
        Rebind();
    }

    // ------------------------------------------------------------------ what the editor shows

    // The editors, the banner and the simulator follow the file that runs on the unit: the loaded one, else its own AI.
    private void Rebind(bool restart = true)
    {
        AiDoc? own = OwnDoc();
        current = loaded ?? own;
        bool editable = current != null && current.LoadError == null;
        brainEditor.Visible = editable && current!.Kind == AiKind.Brain;
        behaviorEditor.Visible = editable && current!.Kind == AiKind.Behavior;
        placeholder.Visible = !editable;
        if (current != null && current.LoadError != null)
            placeholder.Text = L.F("This file could not be read, so it cannot be edited here:\n\n{0}\n\nFix it with a text editor (the file is in the ai folder) and press Reload.", current.LoadError);
        else if (!editable)
            placeholder.Text = L.T("Nothing is loaded on this unit, so there is nothing to edit.\n\nThe editor only shows the AI of the unit in the simulator. To edit another AI or a behavior, pick it in the list on the left and press \"Load on the unit\" (or double-click it): it runs on this unit, you edit and save it there, and \"Back to the unit's AI\" returns the unit to its own AI.\n\nAn AI is a set of states (for example: wait, chase, retreat). Each state acts like a behavior and has transitions that say when to change to another state. A behavior is one way of acting: how to move and shoot.");

        if (editable)
        {
            if (current!.Kind == AiKind.Brain) brainEditor.Bind(current);
            else behaviorEditor.Bind(current);
        }
        UpdateBanner(own);
        UpdateHint();
        UpdateButtons();
        UpdateAiBox();
        Simulate(restart);
    }

    // ------------------------------------------------------------------ the AI of the unit

    private sealed class AiPick
    {
        public AiPick(string id, string text)
        {
            Id = id;
            Text = text;
        }

        public string Id { get; }
        public string Text { get; }
        public override string ToString() => Text;
    }

    // The AIs that fit the unit in the simulator: the ones made for it, then the generic ones. The AI that runs now is selected.
    private void UpdateAiBox()
    {
        fillingAiBox = true;
        try
        {
            aiBox.Items.Clear();
            loadOtherButton.Enabled = choice != null;
            aiBox.Enabled = choice != null;
            if (choice == null) return;
            string own = OwnDoc()?.Id ?? "";
            string now = current != null && current.Kind == AiKind.Brain ? current.Id : "";
            var names = new List<string>();
            if (choice.UnitName.Length > 0) names.Add(choice.UnitName);
            List<(string Id, bool Specific)> fit = store.ApplicableBrains(names);
            if (now.Length > 0 && !fit.Any(f => f.Id.Equals(now, StringComparison.OrdinalIgnoreCase)))
                aiBox.Items.Add(new AiPick(now, L.F("{0}  (not made for this unit)", now)));
            foreach ((string id, bool specific) in fit)
            {
                string text = id;
                if (id.Equals(own, StringComparison.OrdinalIgnoreCase)) text = L.F("{0}  (the unit's AI)", id);
                else if (specific) text = L.F("{0}  (made for this unit)", id);
                aiBox.Items.Add(new AiPick(id, text));
            }
            int index = now.Length == 0 ? -1 : aiBox.Items.Cast<AiPick>().ToList().FindIndex(p => p.Id.Equals(now, StringComparison.OrdinalIgnoreCase));
            aiBox.SelectedIndex = index;
        }
        finally
        {
            fillingAiBox = false;
        }
    }

    private void AiBoxChosen()
    {
        if (fillingAiBox || aiBox.SelectedItem is not AiPick pick) return;
        AiDoc? doc = store.Find(AiKind.Brain, pick.Id);
        if (doc == null || doc.LoadError != null) return;
        SelectNode(doc);
        LoadPicked();
    }

    // The menu of a file of the list (right click): load it on the unit, duplicate it, delete it.
    private void ShowFileMenu(Point at)
    {
        if (picked == null) return;
        var menu = new ContextMenuStrip { Renderer = new EditorRenderer() };
        bool ok = picked.LoadError == null;
        ToolStripMenuItem load = Item(L.T("Load on the unit"), LoadPicked);
        load.Enabled = ok && !ReferenceEquals(picked, current);
        ToolStripMenuItem duplicate = Item(L.T("Duplicate"), DuplicateCurrent);
        duplicate.Enabled = ok;
        menu.Items.Add(load);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(duplicate);
        menu.Items.Add(Item(L.T("Delete"), DeleteCurrent));
        menu.Show(tree, at);
    }

    // "Load another AI...": any AI of the folders (also the ones made for other units), or a file from anywhere on the computer.
    private void ShowLoadOtherMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new EditorRenderer() };
        foreach (IGrouping<string, AiDoc> folder in store.Brains.Where(d => d.LoadError == null)
                     .GroupBy(d => d.Origin.Length == 0 ? L.T("(no folder)") : d.Origin).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            var sub = new ToolStripMenuItem(folder.Key);
            foreach (AiDoc doc in folder.OrderBy(d => d.Id, StringComparer.OrdinalIgnoreCase))
            {
                AiDoc captured = doc;
                var item = new ToolStripMenuItem(doc.Id);
                item.Click += (_, _) =>
                {
                    SelectNode(captured);
                    LoadPicked();
                };
                sub.DropDownItems.Add(item);
            }
            menu.Items.Add(sub);
        }
        menu.Items.Add(new ToolStripSeparator());
        var file = new ToolStripMenuItem(L.T("A file on this computer..."));
        file.Click += (_, _) => LoadAiFromFile();
        menu.Items.Add(file);
        menu.Show(loadOtherButton, new Point(0, loadOtherButton.Height));
    }

    private void LoadAiFromFile()
    {
        using var dialog = new OpenFileDialog { Title = L.T("Load an AI file"), Filter = L.T("AI files") + " (*.json)|*.json|" + L.T("All files") + "|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            AiPackage.Contents contents = AiStore.ReadShared(dialog.FileName);
            if (contents.Brains.Count == 0)
            {
                Ui.ShowError(this, L.T("That file has no AI in it (only behaviors, or something else)."));
                return;
            }
            // The file goes into the list as new, unsaved files (nothing is written to the ai folder until you save them).
            List<AiDoc> added = store.Import(contents, false, (FolderForAi(), UnitForAi()));
            AiDoc? brain = added.FirstOrDefault(d => d.Kind == AiKind.Brain);
            if (brain == null) return;
            SelectNode(brain);
            LoadPicked();
        }
        catch (Exception e) when (e is System.Text.Json.JsonException || e is IOException || e is UnauthorizedAccessException)
        {
            Ui.ShowError(this, L.F("Could not read the file: {0}", e.Message));
        }
    }

    private void UpdateBanner(AiDoc? own)
    {
        string unit = choice == null ? "" : choice.IsType ? choice.TypeName + " (" + choice.TypeId + ")" : choice.Label;
        string text;
        TextRole role = TextRole.Normal;
        if (choice == null)
        {
            text = "";
        }
        else if (loaded != null && ReferenceEquals(loaded, divertedTo) && loaded.IsNew)
        {
            text = L.F("\"{0}\" is a generic AI and is never overwritten, so your changes went to a new AI, \"{1}\", made for {2}. Save it (Ctrl+S) to keep it; the generic AI is as it was.", divertedFrom, loaded.Id, unit);
            role = TextRole.Warn;
        }
        else if (loaded != null)
        {
            string ownText = own != null ? L.F("The unit's own AI is \"{0}\".", own.Id) : L.T("This unit has no AI file of its own.");
            text = L.F("Testing {0} \"{1}\" on {2}. {3} What you edit here is this file, not the unit's AI.", loaded.Kind == AiKind.Brain ? L.T("the AI") : L.T("the behavior"), loaded.Id, unit, ownText);
            role = TextRole.Warn;
        }
        else if (own != null)
        {
            text = L.F("{0} runs the AI \"{1}\" (the \"ai\" of the type in enemies.json). What you edit here is this AI.", unit, own.Id);
            role = TextRole.Good;
        }
        else if (choice.IsType && choice.OwnAi.Length > 0)
        {
            text = L.F("{0} uses the AI \"{1}\", but there is no such file in the ai folder (the server falls back to its normal behavior).", unit, choice.OwnAi);
            role = TextRole.Bad;
        }
        else if (choice.IsType && choice.BuiltInBehavior.Length > 0)
        {
            text = L.F("{0} uses the built-in behavior \"{1}\", which is not an AI file, so there is nothing to edit. Load an AI from the list to see it act on this unit.", unit, choice.BuiltInBehavior);
        }
        else if (choice.IsType)
        {
            text = L.F("{0} has no AI file. Load an AI from the list to see it act on this unit.", unit);
        }
        else
        {
            text = L.F("{0} is a plain game unit: it has no AI of its own. Load an AI or a behavior from the list to edit it and try it on this unit.", unit);
        }
        banner.Text = text;
        Ui.SetRole(banner, role);

        string caption = current == null ? "" : current.Kind == AiKind.Behavior ? L.T("behavior") + " " + current.Id : current.Id;
        simView.SetCaptions(caption, loaded != null);
    }

    private void UpdateHint()
    {
        idHint.Text = picked == null
            ? ""
            : picked.Kind == AiKind.Brain
                ? L.F("To use it: write \"ai\": \"{0}\" in a unit type (map editor, Units, Enemy AI) or in enemies.json.", picked.Id)
                : L.F("A behavior is used by the states of an AI: pick \"{0}\" in the \"Behavior (file)\" field of a state.", picked.Id);
    }

    private void UpdateButtons()
    {
        bool has = picked != null;
        bool pickedOk = has && picked!.LoadError == null;
        bool editing = current != null && current.LoadError == null;
        bool locked = has && store.IsProtected(picked!);
        duplicateItem.Enabled = pickedOk;
        renameItem.Enabled = has && !locked;
        moveItem.Enabled = has && picked!.Kind == AiKind.Brain && !locked;
        deleteItem.Enabled = has && !locked;
        saveAsItem.Enabled = editing;
        exportItem.Enabled = pickedOk;
        loadButton.Enabled = pickedOk && !ReferenceEquals(picked, current);
        backButton.Visible = loaded != null;
        saveItem.Enabled = editing && (current!.Dirty || current.IsNew);
        revertItem.Enabled = editing && current!.Dirty && !current.IsNew;
        saveAllItem.Enabled = store.AnyDirty;
    }

    private void OnEdited()
    {
        if (current == null) return;
        if (store.IsProtected(current))
        {
            // A generic AI is never changed: the edit is moved to a new AI made for the unit once the field that raised it is done.
            if (diverting) return;
            diverting = true;
            if (IsHandleCreated) BeginInvoke(new Action(DivertToCopy));
            else DivertToCopy();
            return;
        }
        current.Dirty = true;
        UpdateNode(current);
        UpdateButtons();
        Simulate(false);
    }

    // The unit the new AIs are made for (the game unit of the type, else its name) and the folder they go to.
    private string UnitForAi() => choice == null ? "" : choice.UnitName.Length > 0 ? choice.UnitName : choice.IsType ? choice.TypeName : "";

    private string FolderForAi() => choice != null && choice.AiFolder.Length > 0 ? choice.AiFolder : AiCatalog.WorldFolder;

    // The open file is generic (or a behavior one uses) and was changed in memory: the changes become "new <unit> AI" (unsaved, in the
    // folder of the unit, offered to the unit) and the generic file is read from the disk again. The simulation goes on.
    private void DivertToCopy()
    {
        diverting = false;
        AiDoc? source = current;
        if (source == null || !store.IsProtected(source)) return;
        try
        {
            AiDoc copy = store.DivertEdits(source, UnitForAi(), FolderForAi());
            divertedFrom = source.Id;
            divertedTo = copy;
            loaded = copy;
            UpdateNode(source);
            SelectNode(copy);
            Rebind(false);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is System.Text.Json.JsonException)
        {
            Ui.ShowError(this, L.F("Could not keep the generic AI as it was: {0}", e.Message));
        }
    }

    // Compiles what is open and shows the problems. Another AI (restart = true) starts the simulator again; an edit of the
    // same one is handed to the simulation that is running, which goes on from where it is: only the Restart button
    // starts it again. An edit that does not compile leaves the last good version running.
    private void Simulate(bool restart)
    {
        var found = new List<AiProblem>();
        AiBrain? brain = current == null ? null : store.Compile(current, found);
        ShowProblems(found);
        string name = current == null ? "" : current.DisplayName;
        if (restart || current == null) simView.SetBrain(brain, name);
        else if (brain != null) simView.ReplaceBrain(brain, name);
    }

    private void ShowProblems(List<AiProblem> found)
    {
        if (current == null)
        {
            problems.Text = "";
            return;
        }
        bool errors = found.Any(p => p.Error);
        problems.Text = found.Count == 0
            ? L.T("No problems found.")
            : string.Join("\r\n", found.Select(p => (p.Error ? L.T("ERROR") : L.T("warning")) + ": " + p.Where + " - " + p.Text));
        Ui.SetRole(problems, found.Count == 0 ? TextRole.Good : errors ? TextRole.Bad : TextRole.Warn);
    }

    // ------------------------------------------------------------------ actions

    private void NewDoc(AiKind kind)
    {
        string? name = AiPrompt.Ask(this, kind == AiKind.Brain ? L.T("New AI") : L.T("New behavior"),
            L.T("Name (the file name is made from it):"), kind == AiKind.Brain ? L.T("My AI") : L.T("My behavior"));
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            JsonObject root = kind == AiKind.Brain ? AiFields.NewBrain(name) : AiFields.NewBehavior(name);
            string unit = UnitForAi();
            // A new AI is made for the unit in the simulator and goes to its folder (a generic AI is protected, so none is made by accident).
            if (kind == AiKind.Brain && unit.Length > 0) root["units"] = new JsonArray(unit);
            AiDoc doc = store.Add(kind, name, root, true, kind == AiKind.Brain ? FolderForAi() : null);
            SelectNode(doc);
            LoadPicked();
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Ui.ShowError(this, L.F("Could not save the file: {0}", e.Message));
        }
    }

    private void DuplicateCurrent()
    {
        if (picked == null) return;
        AiDoc copy = store.Duplicate(picked, UnitForAi(), FolderForAi());
        SelectNode(copy);
        LoadPicked();
    }

    // The enemy types of enemies.json that run an AI file (those are told to change if the file goes away or changes id).
    private IEnumerable<string> TypesUsing(AiDoc doc)
    {
        if (doc.Kind != AiKind.Brain) return Enumerable.Empty<string>();
        return byLabel.Values.Where(c => c.IsType && c.Label.StartsWith(L.T("type") + ":", StringComparison.Ordinal) &&
                                         string.Equals(c.OwnAi, doc.Id, StringComparison.OrdinalIgnoreCase)).Select(c => c.TypeId).ToList();
    }

    private void RenameCurrent()
    {
        if (picked == null) return;
        if (Protected(picked)) return;
        string? id = AiPrompt.Ask(this, L.T("Rename"), L.T("New id (lowercase letters, digits and _):"), picked.Id);
        if (string.IsNullOrWhiteSpace(id)) return;
        List<string> types = TypesUsing(picked).ToList();
        if (types.Count > 0 && !Ui.Confirm(this, L.F("The enemy types {0} (enemies.json) use \"{1}\". They keep looking for the old id until you change their \"ai\" there.\n\nRename anyway?", string.Join(", ", types), picked.Id)))
            return;
        try
        {
            store.Rename(picked, id);
            SelectNode(picked);
            Rebind();
        }
        catch (Exception e) when (e is InvalidOperationException || e is IOException)
        {
            Ui.ShowError(this, e.Message);
        }
    }

    private void MoveCurrent()
    {
        if (picked == null || picked.Kind != AiKind.Brain) return;
        if (Protected(picked)) return;
        string? typed = AiPrompt.Ask(this, L.T("Move to folder"),
            L.T("Folder: generic, world or dungeons/<name> (the tutorial is a dungeon):"), picked.Origin.Length > 0 ? picked.Origin : AiCatalog.GenericFolder);
        if (string.IsNullOrWhiteSpace(typed)) return;
        string? origin = AiStore.NormalizeOrigin(typed);
        if (origin == null)
        {
            Ui.ShowError(this, L.T("Use generic, world, or dungeons/<name> (for example dungeons/arena)."));
            return;
        }
        try
        {
            store.Move(picked, origin);
            SelectNode(picked);
        }
        catch (Exception e) when (e is IOException || e is InvalidOperationException)
        {
            Ui.ShowError(this, e.Message);
        }
    }

    // A generic AI (or a behavior one uses) is never renamed, moved or deleted from here: says so and returns true.
    private bool Protected(AiDoc doc)
    {
        if (!store.IsProtected(doc)) return false;
        Ui.ShowInfo(this, L.T(AiStore.ProtectedText));
        return true;
    }

    private void DeleteCurrent()
    {
        if (picked == null) return;
        if (Protected(picked)) return;
        string extra = picked.Kind == AiKind.Behavior && store.UsedBy(picked).Any()
            ? "\n\n" + L.F("These AIs use it and will have a state with a missing behavior: {0}", string.Join(", ", store.UsedBy(picked)))
            : "";
        List<string> types = TypesUsing(picked).ToList();
        if (types.Count > 0) extra += "\n\n" + L.F("The enemy types {0} (enemies.json) use this AI and will fall back to their normal behavior.", string.Join(", ", types));
        if (!Ui.Confirm(this, L.F("Delete \"{0}\"?\n\nThe file goes to server\\_backup, it is not lost for good.", picked.Id) + extra)) return;
        try
        {
            AiDoc doc = picked;
            if (ReferenceEquals(loaded, doc)) loaded = null;
            picked = null;
            store.Delete(doc);
            Rebind();
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Ui.ShowError(this, L.F("Could not delete the file: {0}", e.Message));
        }
    }

    private void SaveCurrent()
    {
        if (current == null) return;
        try
        {
            store.Save(current);
            UpdateNode(current);
            UpdateBanner(OwnDoc());
            UpdateButtons();
            Simulate(false);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
        {
            Ui.ShowError(this, L.F("Could not save the file: {0}", e.Message));
        }
    }

    // "Save as...": the open file under a new name, saved now in the folder of the unit (a generic AI becomes an AI made for the unit).
    private void SaveAs()
    {
        if (current == null || current.LoadError != null) return;
        string unit = UnitForAi();
        bool generic = current.Kind == AiKind.Brain && AiLibrary.IsGenericOrigin(current.Origin);
        string suggestion = generic || store.IsProtected(current) ? AiStore.SpecificName(current.Kind, unit) : current.DisplayName + " " + L.T("(copy)");
        string? name = AiPrompt.Ask(this, L.T("Save as"), L.T("Name (the file name is made from it):"), suggestion);
        if (string.IsNullOrWhiteSpace(name)) return;
        try
        {
            AiDoc made = store.SaveAs(current, name, unit, FolderForAi());
            loaded = ReferenceEquals(made, OwnDoc()) ? null : made;
            divertedTo = null;
            SelectNode(made);
            Rebind(false);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is System.Text.Json.JsonException)
        {
            Ui.ShowError(this, L.F("Could not save the file: {0}", e.Message));
        }
    }

    private void SaveAll()
    {
        TrySaveAll();
        UpdateButtons();
    }

    private bool TrySaveAll()
    {
        try
        {
            store.SaveAll();
            foreach (AiDoc doc in store.Docs) UpdateNode(doc);
            return true;
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Ui.ShowError(this, L.F("Could not save the file: {0}", e.Message));
            return false;
        }
    }

    // Throws away the changes of the open file and reads it from the disk again.
    private void Revert()
    {
        if (current == null || current.IsNew) return;
        if (!Ui.Confirm(this, L.F("Throw away the changes of \"{0}\" and read it from the disk again?", current.Id))) return;
        current.Dirty = false;
        ReloadStore();
    }

    private void ReloadFromDisk()
    {
        ReloadStore();
        FillUnits(true);
        if (store.AnyDirty) Ui.ShowInfo(this, L.T("Files with unsaved changes were kept as they are in the launcher."));
    }

    // Reads the ai folder again; the file loaded on the unit and the selected one are found again in the new documents.
    private void ReloadStore()
    {
        (AiKind Kind, string Id)? loadedKey = loaded == null ? null : (loaded.Kind, loaded.Id);
        (AiKind Kind, string Id)? pickedKey = picked == null ? null : (picked.Kind, picked.Id);
        store.Reload();
        loaded = loadedKey == null ? null : store.Find(loadedKey.Value.Kind, loadedKey.Value.Id);
        picked = pickedKey == null ? null : store.Find(pickedKey.Value.Kind, pickedKey.Value.Id);
        if (picked != null) SelectNode(picked);
        Rebind();
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(store.BrainsFolder);
            Directory.CreateDirectory(store.BehaviorsFolder);
        }
        catch (IOException)
        {
            // Opening the folder reports it.
        }
        Ui.OpenFolder(this, store.Folder);
    }

    private void OpenEnemyTypes()
    {
        using var dialog = new EnemyTypesForm(store.Brains.Select(b => b.Id));
        dialog.ShowDialog(FindForm());
    }

    private void ExportCurrent()
    {
        if (picked == null) return;
        using var dialog = new SaveFileDialog
        {
            Title = L.T("Export"),
            Filter = L.T("AI files") + " (*.json)|*.json",
            FileName = picked.Id + ".ai.json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            // A generic AI is exported as an AI made for the unit in the simulator, never as the generic one.
            string exported = store.Export(picked, dialog.FileName, UnitForAi(), FolderForAi());
            int behaviors = picked.Kind == AiKind.Brain ? AiPackage.UsedBehaviors(picked.Root).Count(id => store.Find(AiKind.Behavior, id) != null) : 0;
            Ui.ShowInfo(this, L.F("Saved {0}\n\nIt holds \"{1}\"{2}.", dialog.FileName, exported,
                behaviors > 0 ? " " + L.F("and {0} behavior(s) it uses", behaviors) : ""));
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            Ui.ShowError(this, L.F("Could not save the file: {0}", e.Message));
        }
    }

    private void ImportFile()
    {
        using var dialog = new OpenFileDialog { Title = L.T("Import"), Filter = L.T("AI files") + " (*.json)|*.json|" + L.T("All files") + "|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            AiPackage.Contents contents = AiStore.ReadShared(dialog.FileName);
            string what = string.Join(", ", contents.Brains.Select(b => b.Id).Concat(contents.Behaviors.Select(b => b.Id)));
            if (!Ui.Confirm(this, L.F("Add {0} AI(s) and {1} behavior(s) to your files?\n\n{2}\n\nNothing of yours is overwritten: a file with the same id gets a new one.",
                    contents.Brains.Count, contents.Behaviors.Count, what))) return;
            List<AiDoc> added = store.Import(contents, true, (FolderForAi(), UnitForAi()));
            AiDoc? first = added.FirstOrDefault(d => d.Kind == AiKind.Brain) ?? added.FirstOrDefault();
            if (first != null)
            {
                SelectNode(first);
                LoadPicked();
            }
        }
        catch (Exception e) when (e is System.Text.Json.JsonException || e is IOException || e is UnauthorizedAccessException)
        {
            Ui.ShowError(this, L.F("Could not read the file: {0}", e.Message));
        }
    }
}

// What the map editor uses to reach the Enemy AI tab: the "ai" field of a unit type names an AI file.
internal sealed class AiEditorBridge : IEnemyAiEditor
{
    private readonly AiStore store;
    private readonly Action<string, JsonObject> open;

    public AiEditorBridge(AiStore store, Action<string, JsonObject> open)
    {
        this.store = store;
        this.open = open;
    }

    public string DisplayName => L.T("Enemy AI");

    public IEnumerable<string> BrainIds() => store.Brains.Select(b => b.Id).ToList();

    public IEnumerable<string> ApplicableBrainIds(IEnumerable<string> unitNames) => store.ApplicableBrainIds(unitNames);

    public void Edit(IWin32Window owner, string typeName, JsonObject unitType, Action beforeChange, Action afterChange)
    {
        open(typeName, unitType);
    }

    public IEnumerable<string> Validate(string typeName, JsonObject unitType)
    {
        string id = Json.Str(unitType, EnemyAiEditors.AiField).Trim();
        if (id.Length > 0 && store.Find(AiKind.Brain, id) == null)
            yield return L.F("The unit type \"{0}\" uses the AI \"{1}\", which is not in the ai folder (the server will use its normal behavior).", typeName, id);
    }
}
