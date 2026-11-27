using System.Text.Json.Nodes;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// Formation editor: who is born with a leader, where they sit, and the links between them. Each piece still has its own AI.
// The file list, the tabs and the simulator share one window. Members, links, points and summons are edited in the tabs.
internal sealed class FormationTab : UserControl
{
    private readonly LauncherContext context;
    private readonly AiStore aiStore;
    private readonly FormationStore store;
    private readonly ListBox list = new ListBox { IntegralHeight = false, HorizontalScrollbar = true, Dock = DockStyle.Fill };
    private readonly ThemedTextBox nameBox = new ThemedTextBox();
    private readonly ThemedTextBox descriptionBox = new ThemedTextBox();
    private readonly UnitTreeBox leaderUnit = new UnitTreeBox(UnitPickKind.GameUnit);
    private readonly ThemedComboBox leaderAi;
    private readonly NumberBox spacing = FormationFields.Number(1.7m, 0.4m, 20m, 0.1m);
    private readonly ThemedCheckBox bindToUnit = new ThemedCheckBox { AutoSize = true };
    private readonly ListBox members = new ListBox { IntegralHeight = false, Height = 120, Dock = DockStyle.Top };
    private readonly ListBox links = new ListBox { IntegralHeight = false, Height = 120, Dock = DockStyle.Top };
    private readonly ListBox shields = new ListBox { IntegralHeight = false, Height = 120, Dock = DockStyle.Top };
    private readonly ThemedComboBox phases = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
    private readonly ListBox summons = new ListBox { IntegralHeight = false, Height = 100, Dock = DockStyle.Top };
    private readonly ListBox pointList = new ListBox { IntegralHeight = false, Height = 120, Dock = DockStyle.Top };
    private readonly ThemedCheckBox editPoints = new ThemedCheckBox { AutoSize = true };
    private readonly NumberBox polygonCount = FormationFields.Number(5, 3, 16, 1);
    private readonly NumberBox polygonRadius = FormationFields.Number(6, 1, 40, 0.5m);
    private readonly Label status = new Label { AutoSize = false, Height = 32, Dock = DockStyle.Fill, Tag = TextRole.Muted };
    private readonly MemberEditor memberEditor;
    private readonly LinkEditor linkEditor;
    private readonly LinkEditor shieldEditor;
    private readonly PointEditor pointEditor;
    private readonly SummonEditor summonEditor;
    private readonly FormationCanvas canvas;
    private readonly SplitContainer fileSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6 };
    private readonly SplitContainer editorSplit = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 6 };
    private readonly HashSet<string> dirtyIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private Button pauseButton = null!;
    private ToolStripMenuItem saveItem = null!;
    private ToolStripMenuItem duplicateItem = null!;
    private ToolStripMenuItem renameItem = null!;
    private ToolStripMenuItem deleteItem = null!;
    private FormationFile? current;
    private FormationLink? shownLink;
    private FormationLink? shownShield;
    private bool filling;
    private bool splittersPlaced;

    public FormationTab(LauncherContext context, AiStore aiStore)
    {
        this.context = context;
        this.aiStore = aiStore;
        store = new FormationStore(LauncherPaths.AiFolder);
        if (EditorAssets.Current.Folder.Length == 0) EditorAssets.Load(context.GameFolder);
        canvas = new FormationCanvas { Dock = DockStyle.Fill };
        leaderAi = FormationFields.TypedChoice(AiNames());
        memberEditor = new MemberEditor(AiNames(), context.ToolTip);
        linkEditor = new LinkEditor(context.ToolTip);
        shieldEditor = new LinkEditor(context.ToolTip);
        pointEditor = new PointEditor(context.ToolTip);
        summonEditor = new SummonEditor(AiNames());
        Dock = DockStyle.Fill;
        Build();
        SizeChanged += (_, _) => PlaceSplitters();
        LoadFiles(null);
    }

    // The world list and the map editor open a formation here.
    public void ShowId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        int index = store.Files.FindIndex(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (index < 0 && dirtyIds.Count == 0)
        {
            store.Reload();
            RefreshList(id);
            return;
        }
        if (index >= 0) list.SelectedIndex = index;
    }

    private void Build()
    {
        descriptionBox.Multiline = true;
        descriptionBox.Height = 52;
        descriptionBox.MinimumSize = new Size(160, 52);
        descriptionBox.ScrollBars = ScrollBars.Vertical;
        bindToUnit.Text = L.T("Use this formation for the leader unit when its AI is generic");

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8, 4, 4, 8) };
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        left.Controls.Add(list, 0, 0);
        left.Controls.Add(status, 0, 1);
        list.SelectedIndexChanged += (_, _) => SelectListed();
        list.MouseUp += OnListMouseUp;
        context.ToolTip.SetToolTip(list, L.T("Right-click for New, Duplicate, Rename, Delete, Save and Reload."));

        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildFormationPage());
        tabs.TabPages.Add(BuildMembersPage());
        tabs.TabPages.Add(BuildLinksPage());
        tabs.TabPages.Add(BuildShieldPage());
        tabs.TabPages.Add(BuildPointsPage());
        tabs.TabPages.Add(BuildSummonsPage());

        editorSplit.Panel1.Controls.Add(tabs);
        editorSplit.Panel2.Controls.Add(BuildPreview());
        fileSplit.Panel1.Controls.Add(left);
        fileSplit.Panel2.Controls.Add(editorSplit);
        Controls.Add(fileSplit);
        Controls.Add(BuildMenu());

        nameBox.TextChanged += (_, _) =>
        {
            if (filling || current == null) return;
            current.Name = nameBox.Text;
            RefreshCurrentLabel();
            Touch();
        };
        descriptionBox.TextChanged += (_, _) => { if (!filling && current != null) { current.Description = descriptionBox.Text; Touch(); } };
        leaderUnit.ValueChanged += () =>
        {
            if (filling || current == null) return;
            current.Leader ??= new FormationWho();
            current.Leader.Unit = leaderUnit.Value;
            Touch();
            RefreshCanvas();
        };
        leaderAi.TextChanged += (_, _) =>
        {
            if (filling || current == null) return;
            current.Leader ??= new FormationWho();
            current.Leader.Ai = leaderAi.Text.Trim();
            Touch();
            FillPhases();
        };
        spacing.ValueChanged += (_, _) => { if (!filling && current != null) { current.Spacing = (float)spacing.Value; Touch(); RefreshCanvas(); } };
        bindToUnit.CheckedChanged += (_, _) => { if (!filling && current != null) { current.BindToUnit = bindToUnit.Checked; Touch(); } };

        memberEditor.Changed += () =>
        {
            if (current == null || members.SelectedIndex < 0 || members.SelectedIndex >= current.Members.Count) return;
            OnPieceEdited(members, members.SelectedIndex, MemberText(current.Members[members.SelectedIndex]));
        };
        linkEditor.Changed += () => RefreshLinkLists(LinkOf(links), LinkOf(shields));
        shieldEditor.Changed += () => RefreshLinkLists(LinkOf(links), LinkOf(shields));
        pointEditor.Changed += () =>
        {
            if (current == null || pointList.SelectedIndex < 0 || pointList.SelectedIndex >= current.Points.Count) return;
            OnPieceEdited(pointList, pointList.SelectedIndex, PointText(current.Points[pointList.SelectedIndex]));
        };
        summonEditor.Changed += OnSummonEdited;
        canvas.Changed += OnCanvasChanged;

        members.SelectedIndexChanged += (_, _) => { if (!filling) ShowMember(); };
        links.SelectedIndexChanged += (_, _) => { if (!filling) ShowLinkEditor(links, linkEditor, true); };
        shields.SelectedIndexChanged += (_, _) => { if (!filling) ShowLinkEditor(shields, shieldEditor, false); };
        pointList.SelectedIndexChanged += (_, _) => { if (!filling) ShowPoint(); };
        summons.SelectedIndexChanged += (_, _) => { if (!filling) ShowSummon(); };
        members.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { e.Handled = true; RemoveMember(); } };
        links.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { e.Handled = true; RemoveLink(links); } };
        shields.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { e.Handled = true; RemoveLink(shields); } };
        pointList.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { e.Handled = true; RemovePoint(); } };
        summons.KeyDown += (_, e) => { if (e.KeyCode == Keys.Delete) { e.Handled = true; RemoveSummon(); } };
    }

    private TabPage BuildFormationPage()
    {
        var page = new TabPage(L.T("Formation")) { Padding = new Padding(8, 4, 8, 8), AutoScroll = true };
        TableLayoutPanel grid = FormationFields.Grid();
        FormationFields.Add(grid, L.T("Name"), nameBox);
        FormationFields.Add(grid, L.T("Description"), descriptionBox);
        FormationFields.Add(grid, L.T("Spacing"), spacing, false);
        FormationFields.Add(grid, L.T("Leader unit"), leaderUnit);
        FormationFields.Add(grid, L.T("Leader AI"), leaderAi);
        var open = Ui.Button(L.T("Open in Enemy AI"), (_, _) => OpenLeader());
        context.ToolTip.SetToolTip(open, L.T("The AI file the leader runs. Open in Enemy AI jumps to that file. Phase summons are stored on this AI, not in the formation file."));
        context.ToolTip.SetToolTip(bindToUnit, L.T("The game unit of the leader, for example trainA01, podleader or fivestarleader. With \"Use this formation for the leader unit when its AI is generic\" on, a generic AI (or no AI) on that unit still brings this formation. Turn it off when the formation must wait until an AI names it."));
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true };
        buttons.Controls.Add(open);
        page.Controls.Add(buttons);
        page.Controls.Add(bindToUnit);
        bindToUnit.Dock = DockStyle.Top;
        bindToUnit.MaximumSize = new Size(520, 0);
        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildMembersPage()
    {
        var page = new TabPage(L.T("Members")) { Padding = new Padding(6) };
        FlowLayoutPanel buttons = Buttons();
        buttons.Controls.Add(Ui.Button(L.T("Add"), (_, _) => AddMember()));
        buttons.Controls.Add(Ui.Danger(L.T("Delete"), (_, _) => RemoveMember()));
        buttons.Controls.Add(Ui.Button(L.T("Create AI"), (_, _) => CreateMemberAi()));
        page.Controls.Add(memberEditor);
        page.Controls.Add(buttons);
        page.Controls.Add(members);
        return page;
    }

    private TabPage BuildLinksPage()
    {
        var page = new TabPage(L.T("Links")) { Padding = new Padding(6) };
        FlowLayoutPanel buttons = Buttons();
        buttons.Controls.Add(Ui.Button(L.T("Add"), (_, _) => AddLink(shield: false)));
        buttons.Controls.Add(Ui.Danger(L.T("Delete"), (_, _) => RemoveLink(links)));
        page.Controls.Add(linkEditor);
        page.Controls.Add(buttons);
        page.Controls.Add(links);
        return page;
    }

    private TabPage BuildShieldPage()
    {
        var page = new TabPage(L.T("Shield")) { Padding = new Padding(6) };
        FlowLayoutPanel buttons = Buttons();
        buttons.Controls.Add(Ui.Button(L.T("Add"), (_, _) => AddLink(shield: true)));
        buttons.Controls.Add(Ui.Danger(L.T("Delete"), (_, _) => RemoveLink(shields)));
        page.Controls.Add(shieldEditor);
        page.Controls.Add(buttons);
        page.Controls.Add(shields);
        return page;
    }

    private TabPage BuildPointsPage()
    {
        var page = new TabPage(L.T("Points")) { Padding = new Padding(6) };
        FlowLayoutPanel buttons = Buttons();
        buttons.Controls.Add(Ui.Button(L.T("Add"), (_, _) => AddPoint()));
        buttons.Controls.Add(Ui.Danger(L.T("Delete"), (_, _) => RemovePoint()));
        buttons.Controls.Add(Ui.Text(L.T("Count")));
        buttons.Controls.Add(polygonCount);
        buttons.Controls.Add(Ui.Text(L.T("Radius")));
        buttons.Controls.Add(polygonRadius);
        buttons.Controls.Add(Ui.Button(L.T("Make polygon"), (_, _) => MakePolygon()));
        context.ToolTip.SetToolTip(polygonCount, L.T("How many corners the polygon has. This replaces the points."));
        page.Controls.Add(pointEditor);
        page.Controls.Add(buttons);
        page.Controls.Add(pointList);
        return page;
    }

    private TabPage BuildSummonsPage()
    {
        var page = new TabPage(L.T("Phase summons")) { Padding = new Padding(6) };
        var phaseRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
        phaseRow.Controls.Add(Ui.Text(L.T("State of the leader AI")));
        phaseRow.Controls.Add(phases);
        phases.SelectedIndexChanged += (_, _) => { if (!filling) FillSummons(); };
        FlowLayoutPanel buttons = Buttons();
        buttons.Controls.Add(Ui.Button(L.T("Add"), (_, _) => AddSummon()));
        buttons.Controls.Add(Ui.Danger(L.T("Delete"), (_, _) => RemoveSummon()));
        Button save = Ui.Primary(L.T("Save summons to the AI"), (_, _) => SaveSummons());
        context.ToolTip.SetToolTip(save, L.T("Writes the phase summons into the leader's AI file. The formation's own Save does not."));
        buttons.Controls.Add(save);
        page.Controls.Add(summonEditor);
        page.Controls.Add(buttons);
        page.Controls.Add(summons);
        page.Controls.Add(phaseRow);
        return page;
    }

    private Control BuildPreview()
    {
        var preview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(4, 4, 8, 8) };
        preview.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        pauseButton = Ui.Button(L.T("Pause"), (_, _) => TogglePause());
        var speedBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64 };
        speedBox.Items.AddRange(new object[] { "0.25x", "0.5x", "1x", "2x", "4x" });
        speedBox.SelectedIndex = 2;
        speedBox.SelectedIndexChanged += (_, _) =>
        {
            canvas.SpeedScale = speedBox.SelectedIndex switch { 0 => 0.25f, 1 => 0.5f, 3 => 2f, 4 => 4f, _ => 1f };
        };
        var motion = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        motion.Items.AddRange(new object[] { L.T("Flies in a circle"), L.T("Stands still"), L.T("Goes where I click") });
        motion.SelectedIndex = 0;
        motion.SelectedIndexChanged += (_, _) => canvas.Motion = motion.SelectedIndex;
        var damage = FormationFields.Number(25, 1, 100, 5);
        damage.ValueChanged += (_, _) => canvas.ClickDamage = (float)damage.Value / 100f;
        var drain = FormationFields.Number(0, 0, 100, 1);
        drain.ValueChanged += (_, _) => canvas.DrainPerSecond = (float)drain.Value / 100f;
        editPoints.Text = L.T("Edit points on the canvas");
        editPoints.CheckedChanged += (_, _) => canvas.EditPoints = editPoints.Checked;
        var respawn = new ThemedCheckBox { Text = L.T("Comes back after dying"), AutoSize = true, Checked = true };
        respawn.CheckedChanged += (_, _) => canvas.AutoRespawn = respawn.Checked;

        var tip = context.ToolTip;
        tip.SetToolTip(motion, L.T("How the leader moves in the test. Circle is the default. Stands still parks it. Goes where I click flies it to the point you click, unless Edit points is on."));
        tip.SetToolTip(damage, L.T("How much health a right click takes from the piece under the cursor (percent)."));
        tip.SetToolTip(drain, L.T("The pieces lose this percent of their health every second, so you can try shields and kill-all links."));
        tip.SetToolTip(respawn, L.T("A dead piece comes back after a moment, so the same link can fire again."));
        tip.SetToolTip(editPoints, L.T("Click the grid to add a point. Drag a point to move it. Right click a point to remove it."));

        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        bar.Controls.AddRange(new Control[]
        {
            pauseButton, Ui.Button(L.T("Reset"), (_, _) => canvas.ResetView()),
            Pair(L.T("Speed"), speedBox), Pair(L.T("Leader motion"), motion),
            Pair(L.T("Right click takes %"), damage), Pair(L.T("Health lost %/s"), drain),
            Ui.Button(L.T("Heal"), (_, _) => canvas.Heal()), respawn, editPoints
        });
        preview.Controls.Add(bar, 0, 0);
        preview.Controls.Add(canvas, 0, 1);
        return preview;
    }

    private static FlowLayoutPanel Pair(string label, Control box) => Ui.Flow(Ui.Text(label), box);

    private static FlowLayoutPanel Buttons() => new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top };

    // The list stays narrow and the simulator stays a side column, so the tabs keep the width.
    private void PlaceSplitters()
    {
        if (splittersPlaced || Width < 860 || fileSplit.Width < 700) return;
        try
        {
            fileSplit.Panel1MinSize = 180;
            fileSplit.Panel2MinSize = 520;
            fileSplit.SplitterDistance = Math.Clamp(220, 180, fileSplit.Width - 520);
            editorSplit.Panel1MinSize = 340;
            editorSplit.Panel2MinSize = 260;
            int sim = 340;
            int distance = editorSplit.Width - sim;
            editorSplit.SplitterDistance = Math.Clamp(distance, 340, Math.Max(340, editorSplit.Width - 260));
            splittersPlaced = true;
        }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
        {
            // Tried too early: the next resize tries again.
        }
    }

    private void TogglePause()
    {
        canvas.Playing = !canvas.Playing;
        pauseButton.Text = canvas.Playing ? L.T("Pause") : L.T("Play");
    }

    private MenuStrip BuildMenu()
    {
        var bar = new MenuStrip { Renderer = new EditorRenderer(), Dock = DockStyle.Top };
        saveItem = Item(L.T("Save"), Save, "Ctrl+S");
        duplicateItem = Item(L.T("Duplicate"), Duplicate);
        renameItem = Item(L.T("Rename"), Rename);
        deleteItem = Item(L.T("Delete"), Delete);

        var file = new ToolStripMenuItem(L.T("File"));
        file.DropDownItems.Add(Item(L.T("New"), NewFile, "Ctrl+N"));
        file.DropDownItems.Add(saveItem);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item(L.T("Reload"), ReloadFromDisk));

        var edit = new ToolStripMenuItem(L.T("Edit"));
        edit.DropDownItems.Add(duplicateItem);
        edit.DropDownItems.Add(renameItem);
        edit.DropDownItems.Add(deleteItem);

        var help = new ToolStripMenuItem(L.T("Help"));
        help.DropDownItems.Add(Item(L.T("Help topics..."), () => ShowHelp(0), "F1"));
        help.DropDownItems.Add(new ToolStripSeparator());
        IReadOnlyList<HelpTopic> topics = FormationHelp.Topics;
        for (int i = 0; i < topics.Count; i++)
        {
            int index = i;
            help.DropDownItems.Add(Item(L.T(topics[i].Title), () => ShowHelp(index)));
        }
        bar.Items.AddRange(new ToolStripItem[] { file, edit, help });
        return bar;
    }

    private void OnListMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        int index = list.IndexFromPoint(e.Location);
        if (index >= 0) list.SelectedIndex = index;
        var menu = new ContextMenuStrip { Renderer = new EditorRenderer() };
        menu.Items.Add(MenuItem(L.T("New"), NewFile));
        menu.Items.Add(MenuItem(L.T("Duplicate"), Duplicate, current != null));
        menu.Items.Add(MenuItem(L.T("Rename"), Rename, current != null));
        menu.Items.Add(MenuItem(L.T("Delete"), Delete, current != null));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(MenuItem(L.T("Save"), Save, current != null));
        menu.Items.Add(MenuItem(L.T("Reload"), ReloadFromDisk));
        menu.Show(list, e.Location);
    }

    private static ToolStripMenuItem MenuItem(string text, Action action, bool enabled = true)
    {
        var item = new ToolStripMenuItem(text) { Enabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private static ToolStripMenuItem Item(string text, Action action, string? shortcut = null)
    {
        var item = new ToolStripMenuItem(text);
        if (shortcut != null) item.ShortcutKeyDisplayString = shortcut;
        item.Click += (_, _) => action();
        return item;
    }

    private void ShowHelp(int topic)
    {
        HelpWindow.ShowFor(FindForm() ?? (IWin32Window)this, "formation", L.T("Help: Formation editor"), FormationHelp.Topics, topic);
    }

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.S:
                Save();
                return true;
            case Keys.Control | Keys.N:
                NewFile();
                return true;
            case Keys.F1:
                ShowHelp(0);
                return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    private void LoadFiles(string? selectId)
    {
        store.Reload();
        dirtyIds.Clear();
        RefreshList(selectId);
    }

    private void RefreshList(string? selectId)
    {
        filling = true;
        list.Items.Clear();
        foreach (FormationFile file in store.Files) list.Items.Add(LabelOf(file));
        int index = selectId == null ? -1 : store.Files.FindIndex(f => f.Id.Equals(selectId, StringComparison.OrdinalIgnoreCase));
        filling = false;
        if (list.Items.Count == 0) Show(null);
        else list.SelectedIndex = index >= 0 ? index : 0;
        if (store.Problems.Count > 0) status.Text = store.Problems[0];
        else if (dirtyIds.Count == 0) status.Text = L.F("{0} formation(s).", store.Files.Count);
        UpdateFileMenu();
    }

    private string LabelOf(FormationFile file)
    {
        string star = dirtyIds.Contains(file.Id) ? "* " : "";
        string name = file.Name.Length > 0 && !file.Name.Equals(file.Id, StringComparison.OrdinalIgnoreCase) ? "  " + file.Name : "";
        return star + file.Id + name;
    }

    private void RefreshCurrentLabel()
    {
        if (current == null || list.SelectedIndex < 0 || list.SelectedIndex >= store.Files.Count) return;
        ReplaceItem(list, list.SelectedIndex, LabelOf(current));
    }

    private void SelectListed()
    {
        if (filling) return;
        if (list.SelectedIndex < 0 || list.SelectedIndex >= store.Files.Count) { Show(null); return; }
        Show(store.Files[list.SelectedIndex]);
    }

    private void Show(FormationFile? file)
    {
        current = file;
        filling = true;
        bool on = file != null;
        nameBox.Enabled = descriptionBox.Enabled = leaderUnit.Enabled = leaderAi.Enabled = spacing.Enabled = bindToUnit.Enabled = on;
        Refill(leaderAi, AiNames());
        memberEditor.UseLists(AiNames());
        summonEditor.UseLists(AiNames(), file);
        nameBox.Text = file?.Name ?? "";
        descriptionBox.Text = file?.Description ?? "";
        if (file != null) file.Leader ??= new FormationWho();
        leaderUnit.Value = file?.Leader?.Unit ?? "";
        leaderAi.Text = file?.LeaderAi ?? "";
        FormationFields.SetNumber(spacing, file?.Spacing ?? 1.7f);
        bindToUnit.Checked = file?.BindToUnit ?? true;
        members.Items.Clear();
        if (file != null)
            foreach (FormationMember member in file.Members) members.Items.Add(MemberText(member));
        pointList.Items.Clear();
        if (file != null)
            foreach (FormationPoint point in file.Points) pointList.Items.Add(PointText(point));
        filling = false;
        if (members.Items.Count > 0) members.SelectedIndex = 0;
        else ShowMember();
        RefreshLinkLists(null, null);
        if (pointList.Items.Count > 0) pointList.SelectedIndex = 0;
        else ShowPoint();
        FillPhases();
        RefreshCanvas();
        UpdateFileMenu();
        if (file != null && dirtyIds.Contains(file.Id)) status.Text = L.T("Not saved yet.");
    }

    private void ShowMember()
    {
        memberEditor.Show(current != null && members.SelectedIndex >= 0 && members.SelectedIndex < current.Members.Count
            ? current.Members[members.SelectedIndex] : null);
    }

    private void ShowPoint()
    {
        pointEditor.Show(current != null && pointList.SelectedIndex >= 0 && pointList.SelectedIndex < current.Points.Count
            ? current.Points[pointList.SelectedIndex] : null);
    }

    private void ShowSummon()
    {
        JsonArray? lines = SummonLines();
        summonEditor.Show(lines != null && summons.SelectedIndex >= 0 && summons.SelectedIndex < lines.Count && lines[summons.SelectedIndex] is JsonObject line
            ? line : null);
    }

    private static string MemberText(FormationMember member)
    {
        FormationPlace place = member.Place ?? new FormationPlace();
        return member.Id + "  " + member.Unit + "  " + place.Kind + (place.Count > 1 ? " x" + place.Count : "") +
               (member.Ai.Length > 0 ? "  " + member.Ai : "");
    }

    private static string LinkText(FormationLink link)
    {
        if (link.Shield != null && link.Shield.Count > 0)
        {
            string whileWhat = link.WhileAlive ?? link.WhileRole ?? "";
            return L.T("Shield") + " " + string.Join(", ", link.Shield) + (whileWhat.Length > 0 ? " / " + whileWhat : "");
        }
        if (link.When != null && link.Do != null)
            return (link.When.Died ?? link.When.Role ?? "") + " -> " + link.Do.Kind + " " + link.Do.Target;
        return L.T("Link");
    }

    private static string PointText(FormationPoint point) =>
        point.Id + "   " + point.X.ToString("0.##") + ", " + point.Y.ToString("0.##");

    private void OnPieceEdited(ListBox box, int index, string text)
    {
        if (filling || current == null || index < 0) return;
        ReplaceItem(box, index, text);
        if (box == members)
        {
            linkEditor.Show(LinkOf(links), MemberIds());
            shieldEditor.Show(LinkOf(shields), MemberIds());
        }
        Touch();
        RefreshCanvas();
    }

    private void OnSummonEdited()
    {
        if (filling || current == null || summons.SelectedIndex < 0) return;
        JsonArray? lines = SummonLines();
        if (lines == null || summons.SelectedIndex >= lines.Count || lines[summons.SelectedIndex] is not JsonObject line) return;
        ReplaceItem(summons, summons.SelectedIndex, SummonText(line));
        AiDoc? brain = LeaderBrain();
        if (brain != null) brain.Dirty = true;
        status.Text = L.T("Phase summons are not saved yet. Save summons to the AI writes them.");
    }

    private void OnCanvasChanged()
    {
        if (current == null || filling) return;
        int keep = pointList.SelectedIndex;
        filling = true;
        pointList.Items.Clear();
        foreach (FormationPoint point in current.Points) pointList.Items.Add(PointText(point));
        if (keep >= 0 && keep < pointList.Items.Count) pointList.SelectedIndex = keep;
        filling = false;
        if (keep >= 0 && keep < current.Points.Count) pointEditor.Show(current.Points[keep]);
        else ShowPoint();
        Touch();
    }

    private void ReplaceItem(ListBox box, int index, string text)
    {
        if (index < 0 || index >= box.Items.Count || Equals(box.Items[index], text)) return;
        bool was = filling;
        filling = true;
        int keep = box.SelectedIndex;
        box.Items[index] = text;
        if (keep >= 0 && keep < box.Items.Count && box.SelectedIndex != keep) box.SelectedIndex = keep;
        filling = was;
    }

    private void RefreshCanvas()
    {
        canvas.File = current;
        canvas.Invalidate();
    }

    private void Touch()
    {
        if (filling || current == null) return;
        dirtyIds.Add(current.Id);
        RefreshCurrentLabel();
        status.Text = L.T("Not saved yet.");
        UpdateFileMenu();
    }

    private void UpdateFileMenu()
    {
        bool on = current != null;
        if (saveItem != null) saveItem.Enabled = on;
        if (duplicateItem != null) duplicateItem.Enabled = on;
        if (renameItem != null) renameItem.Enabled = on;
        if (deleteItem != null) deleteItem.Enabled = on;
    }

    private void NewFile()
    {
        FormationFile file = store.Add("formation");
        RefreshList(file.Id);
        status.Text = L.F("Saved {0}.", file.Id);
    }

    private void Duplicate()
    {
        if (current == null) return;
        Pull();
        FormationFile copy = store.Duplicate(current);
        RefreshList(copy.Id);
        status.Text = L.F("Saved {0}.", copy.Id);
    }

    private void Rename()
    {
        if (current == null) return;
        string? next = AiPrompt.Ask(FindForm()!, L.T("Rename"), L.T("New id"), current.Id);
        if (string.IsNullOrWhiteSpace(next)) return;
        try
        {
            dirtyIds.Remove(current.Id);
            store.Rename(current, next);
            RefreshList(current.Id);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), e.Message);
        }
    }

    private void Delete()
    {
        if (current == null) return;
        if (MessageBox.Show(FindForm(), L.F("Move \"{0}\" to the backup folder?", current.Id), L.T("Delete"),
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
        dirtyIds.Remove(current.Id);
        store.Delete(current);
        current = null;
        RefreshList(null);
    }

    private void Save()
    {
        if (current == null) return;
        Pull();
        try
        {
            store.Save(current);
            dirtyIds.Remove(current.Id);
            RefreshCurrentLabel();
            status.Text = L.F("Saved {0}.", current.Id);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), e.Message);
        }
    }

    private void ReloadFromDisk()
    {
        if (dirtyIds.Count > 0 && !Ui.Confirm(FindForm(), L.T("Reload the folder and drop changes that were not saved?"))) return;
        string? id = current?.Id;
        LoadFiles(id);
    }

    private void Pull()
    {
        if (current == null) return;
        current.Name = nameBox.Text;
        current.Description = descriptionBox.Text;
        current.Leader ??= new FormationWho();
        current.Leader.Unit = leaderUnit.Value;
        current.Leader.Ai = leaderAi.Text.Trim();
        current.Spacing = (float)spacing.Value;
        current.BindToUnit = bindToUnit.Checked;
    }

    private void AddMember()
    {
        if (current == null) return;
        string id = "m" + (current.Members.Count + 1);
        current.Members.Add(new FormationMember
        {
            Id = id,
            Unit = "pod",
            Ai = "world_formation_turret",
            Role = id,
            Place = new FormationPlace { Kind = "slot", Y = -4 }
        });
        Show(current);
        members.SelectedIndex = current.Members.Count - 1;
        Touch();
    }

    private void RemoveMember()
    {
        if (current == null || members.SelectedIndex < 0 || members.SelectedIndex >= current.Members.Count) return;
        current.Members.RemoveAt(members.SelectedIndex);
        Show(current);
        Touch();
    }

    private void CreateMemberAi()
    {
        if (current == null || members.SelectedIndex < 0 || members.SelectedIndex >= current.Members.Count) return;
        FormationMember member = current.Members[members.SelectedIndex];
        string wanted = "formation_" + current.Id + "_" + (member.Id.Length > 0 ? member.Id : member.Unit);
        var root = new JsonObject
        {
            ["name"] = (member.Unit.Length > 0 ? member.Unit : wanted),
            ["author"] = "",
            ["description"] = L.T("Created from the formation editor. It holds still and shoots; the formation places it."),
            ["initial"] = "main",
            ["units"] = new JsonArray(member.Unit),
            ["states"] = new JsonArray(new JsonObject
            {
                ["id"] = "main",
                ["move"] = new JsonObject { ["mode"] = "builtin", ["builtin"] = "turret" },
                ["fire"] = new JsonObject { ["enabled"] = true }
            })
        };
        try
        {
            AiDoc doc = aiStore.Add(AiKind.Brain, wanted, root, true, "world");
            member.Ai = doc.Id;
            Show(current);
            members.SelectedIndex = current.Members.FindIndex(m => ReferenceEquals(m, member));
            status.Text = L.F("Created the AI \"{0}\".", doc.Id);
            Touch();
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), e.Message);
        }
    }

    private IEnumerable<string> MemberIds()
    {
        yield return "leader";
        if (current == null) yield break;
        foreach (FormationMember member in current.Members)
            if (member.Id.Length > 0) yield return member.Id;
    }

    private sealed class LinkItem
    {
        public LinkItem(FormationLink link) { Link = link; }
        public FormationLink Link { get; }
        public override string ToString() => LinkText(Link);
    }

    private static bool IsShield(FormationLink link) =>
        (link.Shield != null && link.Shield.Count > 0) || link.Do?.Kind == "unshield";

    private static FormationLink? LinkOf(ListBox box) => (box.SelectedItem as LinkItem)?.Link;

    private void RefreshLinkLists(FormationLink? keepLink, FormationLink? keepShield)
    {
        bool was = filling;
        filling = true;
        links.Items.Clear();
        shields.Items.Clear();
        if (current != null)
        {
            foreach (FormationLink link in current.Links)
                (IsShield(link) ? shields : links).Items.Add(new LinkItem(link));
        }
        int linkIndex = IndexOf(links, keepLink);
        int shieldIndex = IndexOf(shields, keepShield);
        if (linkIndex < 0 && keepLink != null && IsShield(keepLink)) shieldIndex = IndexOf(shields, keepLink);
        if (shieldIndex < 0 && keepShield != null && !IsShield(keepShield)) linkIndex = IndexOf(links, keepShield);
        if (linkIndex < 0 && links.Items.Count > 0 && keepLink == null && keepShield == null) linkIndex = 0;
        if (shieldIndex < 0 && shields.Items.Count > 0 && keepLink == null && keepShield == null) shieldIndex = 0;
        filling = was;
        if (links.SelectedIndex != linkIndex)
        {
            if (linkIndex >= 0) links.SelectedIndex = linkIndex;
            else ShowLinkEditor(links, linkEditor, true);
        }
        else if (LinkOf(links) != shownLink) ShowLinkEditor(links, linkEditor, true);
        if (shields.SelectedIndex != shieldIndex)
        {
            if (shieldIndex >= 0) shields.SelectedIndex = shieldIndex;
            else ShowLinkEditor(shields, shieldEditor, false);
        }
        else if (LinkOf(shields) != shownShield) ShowLinkEditor(shields, shieldEditor, false);
    }

    private void ShowLinkEditor(ListBox box, LinkEditor editor, bool plain)
    {
        FormationLink? link = LinkOf(box);
        if (plain) shownLink = link;
        else shownShield = link;
        editor.Show(link, MemberIds());
    }

    private static int IndexOf(ListBox box, FormationLink? link)
    {
        if (link == null) return -1;
        for (int i = 0; i < box.Items.Count; i++)
            if (box.Items[i] is LinkItem item && ReferenceEquals(item.Link, link)) return i;
        return -1;
    }

    private void AddLink(bool shield)
    {
        if (current == null) return;
        var link = shield
            ? new FormationLink { Shield = new List<string> { "leader" }, Effect = "" }
            : new FormationLink { When = new FormationWhen { Died = "leader" }, Do = new FormationDo { Kind = "killAll" } };
        current.Links.Add(link);
        RefreshLinkLists(shield ? null : link, shield ? link : null);
        Touch();
    }

    private void RemoveLink(ListBox box)
    {
        if (current == null || LinkOf(box) is not FormationLink link) return;
        current.Links.Remove(link);
        RefreshLinkLists(null, null);
        Touch();
    }

    private void AddPoint()
    {
        if (current == null) return;
        current.Points.Add(new FormationPoint { Id = "p" + (current.Points.Count + 1), X = 0, Y = 6 });
        Show(current);
        pointList.SelectedIndex = current.Points.Count - 1;
        Touch();
    }

    private void RemovePoint()
    {
        if (current == null || pointList.SelectedIndex < 0 || pointList.SelectedIndex >= current.Points.Count) return;
        current.Points.RemoveAt(pointList.SelectedIndex);
        Show(current);
        Touch();
    }

    private void MakePolygon()
    {
        if (current == null) return;
        if (current.Points.Count > 0 && !Ui.Confirm(FindForm(), L.T("Replace the points with this polygon?"))) return;
        int count = (int)polygonCount.Value;
        float radius = (float)polygonRadius.Value;
        current.Points.Clear();
        for (int i = 0; i < count; i++)
        {
            float turn = i * 360f / count * MathF.PI / 180f;
            current.Points.Add(new FormationPoint
            {
                Id = "p" + (i + 1),
                X = MathF.Round(MathF.Sin(turn) * radius, 2),
                Y = MathF.Round(MathF.Cos(turn) * radius, 2)
            });
        }
        Show(current);
        Touch();
    }

    private void OpenLeader()
    {
        if (current == null || current.LeaderAi.Length == 0) return;
        context.OpenAiTab?.Invoke(current.LeaderAi);
    }

    private AiDoc? LeaderBrain()
    {
        if (current == null || current.LeaderAi.Length == 0) return null;
        return aiStore.Find(AiKind.Brain, current.LeaderAi);
    }

    private void FillPhases()
    {
        filling = true;
        phases.Items.Clear();
        summons.Items.Clear();
        AiDoc? brain = LeaderBrain();
        if (brain != null)
        {
            foreach (JsonObject state in Json.Objects(Json.ArrOrNull(brain.Root, "states")))
                phases.Items.Add(Json.Str(state, "id"));
        }
        filling = false;
        if (phases.Items.Count > 0) phases.SelectedIndex = 0;
        else { summonEditor.Show(null); }
    }

    private JsonObject? SelectedState()
    {
        AiDoc? brain = LeaderBrain();
        if (brain == null || phases.SelectedIndex < 0) return null;
        string id = phases.SelectedItem?.ToString() ?? "";
        return Json.Objects(Json.ArrOrNull(brain.Root, "states")).FirstOrDefault(s => Json.Str(s, "id") == id);
    }

    private JsonArray? SummonLines()
    {
        JsonObject? state = SelectedState();
        return state == null ? null : Json.ArrOrNull(state, "summons");
    }

    private void FillSummons()
    {
        summons.Items.Clear();
        JsonObject? state = SelectedState();
        if (state == null)
        {
            summonEditor.Show(null);
            return;
        }
        JsonArray? lines = Json.ArrOrNull(state, "summons");
        if ((lines == null || lines.Count == 0) && Json.Str(state, "summon").Length > 0)
        {
            AiDoc? brain = LeaderBrain();
            if (brain != null && !aiStore.IsProtected(brain)) lines = EnsureSummonList(state);
        }
        if (lines != null && lines.Count > 0)
        {
            foreach (JsonObject line in Json.Objects(lines)) summons.Items.Add(SummonText(line));
        }
        else if (Json.Str(state, "summon").Length > 0)
        {
            summons.Items.Add(Json.Str(state, "summon") + " x" + Json.Int(state, "summonCount", 1) + "  " + Json.Str(state, "summonPlace"));
        }
        if (summons.Items.Count > 0) summons.SelectedIndex = 0;
        else summonEditor.Show(null);
    }

    private static string SummonText(JsonObject line) =>
        Json.Str(line, "unit") + " x" + Json.Int(line, "count", 1) + "  " + Json.Str(line, "place") +
        (Json.Str(line, "role").Length > 0 ? "  " + Json.Str(line, "role") : "");

    private JsonArray EnsureSummonList(JsonObject state)
    {
        JsonArray? list = Json.ArrOrNull(state, "summons");
        if (list != null && list.Count > 0) return list;
        list = new JsonArray();
        if (Json.Str(state, "summon").Length > 0)
        {
            list.Add(new JsonObject
            {
                ["unit"] = Json.Str(state, "summon"),
                ["ai"] = Json.Str(state, "summonAi"),
                ["role"] = Json.Str(state, "summonRole"),
                ["place"] = Json.Str(state, "summonPlace").Length == 0 ? "behind" : Json.Str(state, "summonPlace"),
                ["count"] = Json.Int(state, "summonCount", 1),
                ["every"] = Json.Num(state, "summonEvery"),
                ["max"] = Json.Int(state, "summonMax"),
                ["distance"] = Json.Num(state, "summonDistance", 5),
                ["angle"] = Json.Num(state, "summonAngle")
            });
        }
        state["summons"] = list;
        foreach (string key in new[] { "summon", "summonAi", "summonRole", "summonPlace", "summonCount", "summonEvery", "summonMax", "summonDistance", "summonAngle" })
            state.Remove(key);
        return list;
    }

    private bool GuardSummon(out JsonObject? state)
    {
        state = SelectedState();
        AiDoc? brain = LeaderBrain();
        if (state == null || brain == null) return false;
        if (!aiStore.IsProtected(brain)) return true;
        Ui.ShowError(FindForm(), L.T("This AI is in the generic folder and cannot be edited. Create an AI for the leader first."));
        state = null;
        return false;
    }

    private void AddSummon()
    {
        if (!GuardSummon(out JsonObject? state) || state == null) return;
        JsonArray lines = EnsureSummonList(state);
        lines.Add(new JsonObject
        {
            ["unit"] = "pod", ["ai"] = "", ["role"] = "", ["place"] = "behind",
            ["count"] = 1, ["every"] = 0, ["max"] = 0, ["distance"] = 5, ["angle"] = 0
        });
        AiDoc? brain = LeaderBrain();
        if (brain != null) brain.Dirty = true;
        FillSummons();
        summons.SelectedIndex = lines.Count - 1;
        status.Text = L.T("Phase summons are not saved yet. Save summons to the AI writes them.");
    }

    private void RemoveSummon()
    {
        if (!GuardSummon(out JsonObject? state) || state == null || summons.SelectedIndex < 0) return;
        JsonArray lines = EnsureSummonList(state);
        if (summons.SelectedIndex >= lines.Count) return;
        lines.RemoveAt(summons.SelectedIndex);
        AiDoc? brain = LeaderBrain();
        if (brain != null) brain.Dirty = true;
        FillSummons();
        status.Text = L.T("Phase summons are not saved yet. Save summons to the AI writes them.");
    }

    private void SaveSummons()
    {
        AiDoc? brain = LeaderBrain();
        if (brain == null) return;
        try
        {
            if (aiStore.IsProtected(brain))
            {
                Ui.ShowError(FindForm(), L.T("This AI is in the generic folder and cannot be edited. Create an AI for the leader first."));
                return;
            }
            brain.Dirty = true;
            aiStore.Save(brain);
            status.Text = L.F("Saved the summons of \"{0}\".", brain.Id);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), e.Message);
        }
    }

    private static IEnumerable<string> UnitNames() =>
        EditorAssets.Current.Units.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase);

    private IEnumerable<string> AiNames() => aiStore.Brains.Select(b => b.Id);

    private static void Refill(ComboBox box, IEnumerable<string> items)
    {
        string text = box.Text;
        box.Items.Clear();
        foreach (string item in items) box.Items.Add(item);
        box.Text = text;
    }
}
