using System.Text.Json.Nodes;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// Formation editor: who is born with a leader, where they sit, and the links between them. Each piece still has its own AI.
internal sealed class FormationTab : UserControl
{
    private readonly LauncherContext context;
    private readonly AiStore aiStore;
    private readonly FormationStore store;
    private readonly ListBox list = new ListBox { IntegralHeight = false, Width = 180, Dock = DockStyle.Fill };
    private readonly ThemedTextBox nameBox = new ThemedTextBox();
    private readonly ThemedTextBox descriptionBox = new ThemedTextBox();
    private readonly ThemedTextBox leaderUnit = new ThemedTextBox();
    private readonly ThemedTextBox leaderAi = new ThemedTextBox();
    private readonly NumberBox spacing = new NumberBox { Minimum = 0.4m, Maximum = 20m, Increment = 0.1m, DecimalPlaces = 1, Value = 1.7m, Width = 70 };
    private readonly CheckBox bindToUnit = new CheckBox { AutoSize = true };
    private readonly ListBox members = new ListBox { IntegralHeight = false, Height = 110 };
    private readonly ListBox links = new ListBox { IntegralHeight = false, Height = 90 };
    private readonly ComboBox phases = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly ListBox summons = new ListBox { IntegralHeight = false, Height = 70 };
    private readonly Label status = new Label { AutoSize = true, Tag = TextRole.Muted };
    private readonly FormationCanvas canvas;
    private FormationFile? current;
    private bool filling;

    public FormationTab(LauncherContext context, AiStore aiStore)
    {
        this.context = context;
        this.aiStore = aiStore;
        store = new FormationStore(LauncherPaths.AiFolder);
        canvas = new FormationCanvas { Dock = DockStyle.Fill };
        Dock = DockStyle.Fill;
        Build();
        Reload();
    }

    private void Build()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(8) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        var fileButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true };
        fileButtons.Controls.Add(Ui.Button(L.T("New"), (_, _) => NewFile()));
        fileButtons.Controls.Add(Ui.Button(L.T("Duplicate"), (_, _) => Duplicate()));
        fileButtons.Controls.Add(Ui.Button(L.T("Reload"), (_, _) => Reload()));
        var fileButtons2 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        fileButtons2.Controls.Add(Ui.Button(L.T("Rename"), (_, _) => Rename()));
        fileButtons2.Controls.Add(Ui.Danger(L.T("Delete"), (_, _) => Delete()));
        fileButtons2.Controls.Add(Ui.Primary(L.T("Save"), (_, _) => Save()));
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        bottom.Controls.Add(fileButtons2, 0, 0);
        bottom.Controls.Add(status, 0, 1);
        left.Controls.Add(fileButtons, 0, 0);
        left.Controls.Add(list, 0, 1);
        left.Controls.Add(bottom, 0, 2);
        list.SelectedIndexChanged += (_, _) => SelectListed();

        var editor = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Width = 460 };
        stack.Controls.Add(Ui.Bold(L.T("Formation")));
        stack.Controls.Add(Row(L.T("Name"), nameBox));
        descriptionBox.Width = 280;
        stack.Controls.Add(Row(L.T("Description"), descriptionBox));
        bindToUnit.Text = L.T("Use this formation for the leader unit when its AI is generic");
        stack.Controls.Add(bindToUnit);
        stack.Controls.Add(Row(L.T("Spacing"), spacing));
        stack.Controls.Add(Row(L.T("Leader unit"), leaderUnit));
        stack.Controls.Add(Row(L.T("Leader AI"), leaderAi));
        var open = Ui.Button(L.T("Open in Enemy AI"), (_, _) => OpenLeader());
        stack.Controls.Add(open);

        stack.Controls.Add(Ui.Bold(L.T("Members")));
        members.Width = 420;
        stack.Controls.Add(members);
        var memberButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        memberButtons.Controls.Add(Ui.Button(L.T("Add"), (_, _) => AddMember()));
        memberButtons.Controls.Add(Ui.Button(L.T("Edit member"), (_, _) => EditMember()));
        memberButtons.Controls.Add(Ui.Danger(L.T("Delete"), (_, _) => RemoveMember()));
        memberButtons.Controls.Add(Ui.Button(L.T("Create AI"), (_, _) => CreateMemberAi()));
        stack.Controls.Add(memberButtons);

        stack.Controls.Add(Ui.Bold(L.T("Links")));
        links.Width = 420;
        stack.Controls.Add(links);
        var linkButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 440 };
        linkButtons.Controls.Add(Ui.Button(L.T("Shield while someone lives"), (_, _) => AddShield()));
        linkButtons.Controls.Add(Ui.Button(L.T("Kill all on death"), (_, _) => AddKill()));
        linkButtons.Controls.Add(Ui.Button(L.T("Unshield the next at a health %"), (_, _) => AddUnshield()));
        linkButtons.Controls.Add(Ui.Button(L.T("Heal the leader on death"), (_, _) => AddHeal()));
        linkButtons.Controls.Add(Ui.Danger(L.T("Delete"), (_, _) => RemoveLink()));
        stack.Controls.Add(linkButtons);

        stack.Controls.Add(Ui.Bold(L.T("Phase summons (leader AI)")));
        stack.Controls.Add(phases);
        summons.Width = 420;
        stack.Controls.Add(summons);
        var summonButtons = new FlowLayoutPanel { AutoSize = true };
        summonButtons.Controls.Add(Ui.Button(L.T("Add"), (_, _) => AddSummon()));
        summonButtons.Controls.Add(Ui.Danger(L.T("Delete"), (_, _) => RemoveSummon()));
        summonButtons.Controls.Add(Ui.Button(L.T("Save"), (_, _) => SaveSummons()));
        stack.Controls.Add(summonButtons);
        phases.SelectedIndexChanged += (_, _) => FillSummons();

        editor.Controls.Add(stack);
        canvas.MinimumSize = new Size(280, 200);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(editor, 1, 0);
        root.Controls.Add(canvas, 2, 0);
        Controls.Add(root);

        nameBox.TextChanged += (_, _) => { if (!filling && current != null) current.Name = nameBox.Text; RefreshCanvas(); };
        descriptionBox.TextChanged += (_, _) => { if (!filling && current != null) current.Description = descriptionBox.Text; };
        leaderUnit.TextChanged += (_, _) => { if (!filling && current != null) current.Leader.Unit = leaderUnit.Text.Trim(); RefreshCanvas(); };
        leaderAi.TextChanged += (_, _) =>
        {
            if (filling || current == null) return;
            current.Leader.Ai = leaderAi.Text.Trim();
            FillPhases();
        };
        spacing.ValueChanged += (_, _) => { if (!filling && current != null) { current.Spacing = (float)spacing.Value; RefreshCanvas(); } };
        bindToUnit.CheckedChanged += (_, _) => { if (!filling && current != null) current.BindToUnit = bindToUnit.Checked; };
    }

    private static Control Row(string label, Control box)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 6, 0) });
        box.Width = Math.Max(box.Width, 180);
        row.Controls.Add(box);
        return row;
    }

    private void Reload()
    {
        string? id = current?.Id;
        store.Reload();
        list.Items.Clear();
        foreach (FormationFile file in store.Files) list.Items.Add(file.Id);
        int index = id == null ? -1 : store.Files.FindIndex(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (list.Items.Count > 0) list.SelectedIndex = index >= 0 ? index : 0;
        else Show(null);
        status.Text = store.Problems.Count == 0 ? L.F("{0} formation(s).", store.Files.Count) : store.Problems[0];
    }

    private void SelectListed()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= store.Files.Count) { Show(null); return; }
        Show(store.Files[list.SelectedIndex]);
    }

    private void Show(FormationFile? file)
    {
        current = file;
        filling = true;
        bool on = file != null;
        nameBox.Enabled = descriptionBox.Enabled = leaderUnit.Enabled = leaderAi.Enabled = spacing.Enabled = bindToUnit.Enabled = on;
        nameBox.Text = file?.Name ?? "";
        descriptionBox.Text = file?.Description ?? "";
        leaderUnit.Text = file?.Leader.Unit ?? "";
        leaderAi.Text = file?.LeaderAi ?? "";
        spacing.Value = (decimal)Math.Clamp(file?.Spacing ?? 1.7f, 0.4f, 20f);
        bindToUnit.Checked = file?.BindToUnit ?? true;
        members.Items.Clear();
        if (file != null)
            foreach (FormationMember member in file.Members)
                members.Items.Add(MemberText(member));
        links.Items.Clear();
        if (file != null)
            foreach (FormationLink link in file.Links)
                links.Items.Add(LinkText(link));
        filling = false;
        FillPhases();
        RefreshCanvas();
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

    private void RefreshCanvas()
    {
        canvas.File = current;
        canvas.Invalidate();
    }

    private void NewFile()
    {
        FormationFile file = store.Add("formation");
        Reload();
        list.SelectedIndex = store.Files.IndexOf(file);
    }

    private void Duplicate()
    {
        if (current == null) return;
        Pull();
        FormationFile copy = store.Duplicate(current);
        Reload();
        list.SelectedIndex = store.Files.FindIndex(f => f.Id == copy.Id);
    }

    private void Rename()
    {
        if (current == null) return;
        string? next = Prompt(L.T("New id"), current.Id);
        if (string.IsNullOrWhiteSpace(next)) return;
        try
        {
            store.Rename(current, next);
            Reload();
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
        store.Delete(current);
        current = null;
        Reload();
    }

    private void Save()
    {
        if (current == null) return;
        Pull();
        try
        {
            store.Save(current);
            status.Text = L.F("Saved {0}.", current.Id);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), e.Message);
        }
    }

    private void Pull()
    {
        if (current == null) return;
        current.Name = nameBox.Text;
        current.Description = descriptionBox.Text;
        current.Leader.Unit = leaderUnit.Text.Trim();
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
        EditMember();
    }

    private void EditMember()
    {
        if (current == null || members.SelectedIndex < 0 || members.SelectedIndex >= current.Members.Count) return;
        FormationMember member = current.Members[members.SelectedIndex];
        using var dialog = new MemberDialog(member);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        dialog.Apply(member);
        Show(current);
    }

    private void RemoveMember()
    {
        if (current == null || members.SelectedIndex < 0 || members.SelectedIndex >= current.Members.Count) return;
        current.Members.RemoveAt(members.SelectedIndex);
        Show(current);
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
            status.Text = L.F("Created the AI \"{0}\".", doc.Id);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), e.Message);
        }
    }

    private void AddShield()
    {
        if (current == null) return;
        current.Links.Add(new FormationLink
        {
            Shield = new List<string> { "leader" },
            WhileAlive = current.Members.FirstOrDefault()?.Id ?? "",
            Effect = ""
        });
        Show(current);
    }

    private void AddKill()
    {
        if (current == null) return;
        current.Links.Add(new FormationLink
        {
            When = new FormationWhen { Died = current.Members.LastOrDefault()?.Id ?? "tip" },
            Do = new FormationDo { Kind = "killAll" }
        });
        Show(current);
    }

    private void AddUnshield()
    {
        if (current == null) return;
        string role = current.Members.FirstOrDefault()?.Role ?? "vessel";
        current.Links.Add(new FormationLink
        {
            When = new FormationWhen { Role = role, Which = "firstAlive", HealthBelow = 33 },
            Do = new FormationDo { Kind = "unshield", Target = "next" }
        });
        Show(current);
    }

    private void AddHeal()
    {
        if (current == null) return;
        current.Links.Add(new FormationLink
        {
            When = new FormationWhen { Died = current.Members.FirstOrDefault()?.Id ?? "" },
            Do = new FormationDo { Kind = "heal", Target = "leader", Amount = 25 }
        });
        Show(current);
    }

    private void RemoveLink()
    {
        if (current == null || links.SelectedIndex < 0 || links.SelectedIndex >= current.Links.Count) return;
        current.Links.RemoveAt(links.SelectedIndex);
        Show(current);
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
        phases.Items.Clear();
        summons.Items.Clear();
        AiDoc? brain = LeaderBrain();
        if (brain == null) return;
        foreach (JsonObject state in Json.Objects(Json.ArrOrNull(brain.Root, "states")))
            phases.Items.Add(Json.Str(state, "id"));
        if (phases.Items.Count > 0) phases.SelectedIndex = 0;
    }

    private JsonObject? SelectedState()
    {
        AiDoc? brain = LeaderBrain();
        if (brain == null || phases.SelectedIndex < 0) return null;
        string id = phases.SelectedItem?.ToString() ?? "";
        return Json.Objects(Json.ArrOrNull(brain.Root, "states")).FirstOrDefault(s => Json.Str(s, "id") == id);
    }

    private void FillSummons()
    {
        summons.Items.Clear();
        JsonObject? state = SelectedState();
        if (state == null) return;
        foreach (JsonObject line in Json.Objects(Json.ArrOrNull(state, "summons")))
            summons.Items.Add(Json.Str(line, "unit") + " x" + Json.Int(line, "count", 1) + "  " + Json.Str(line, "place") +
                              (Json.Str(line, "role").Length > 0 ? "  " + Json.Str(line, "role") : ""));
    }

    private void AddSummon()
    {
        JsonObject? state = SelectedState();
        AiDoc? brain = LeaderBrain();
        if (state == null || brain == null) return;
        if (aiStore.IsProtected(brain))
        {
            Ui.ShowError(FindForm(), L.T("This AI is in the generic folder and cannot be edited. Create an AI for the leader first."));
            return;
        }
        JsonArray list = Json.ArrOrNull(state, "summons") ?? new JsonArray();
        state["summons"] = list;
        list.Add(new JsonObject
        {
            ["unit"] = "pod",
            ["ai"] = "",
            ["role"] = "",
            ["place"] = "behind",
            ["count"] = 1,
            ["every"] = 0,
            ["max"] = 0,
            ["distance"] = 5
        });
        FillSummons();
    }

    private void RemoveSummon()
    {
        JsonObject? state = SelectedState();
        if (state == null || summons.SelectedIndex < 0) return;
        JsonArray? list = Json.ArrOrNull(state, "summons");
        if (list == null || summons.SelectedIndex >= list.Count) return;
        list.RemoveAt(summons.SelectedIndex);
        FillSummons();
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

    private string? Prompt(string title, string value)
    {
        using var dialog = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(320, 96),
            MinimizeBox = false,
            MaximizeBox = false
        };
        var box = new ThemedTextBox { Text = value, Left = 12, Top = 12, Width = 296 };
        var ok = Ui.Primary(L.T("Save"), (_, _) => { dialog.DialogResult = DialogResult.OK; dialog.Close(); });
        ok.Left = 12;
        ok.Top = 48;
        dialog.Controls.Add(box);
        dialog.Controls.Add(ok);
        dialog.AcceptButton = ok;
        return dialog.ShowDialog(FindForm()) == DialogResult.OK ? box.Text.Trim() : null;
    }
}

internal sealed class MemberDialog : Form
{
    private readonly ThemedTextBox id = new ThemedTextBox { Width = 160 };
    private readonly ThemedTextBox unit = new ThemedTextBox { Width = 160 };
    private readonly ThemedTextBox ai = new ThemedTextBox { Width = 160 };
    private readonly ThemedTextBox role = new ThemedTextBox { Width = 160 };
    private readonly ComboBox kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly NumberBox x = Num(-40, 40);
    private readonly NumberBox y = Num(-40, 40);
    private readonly NumberBox radius = Num(0, 40);
    private readonly NumberBox spin = Num(-180, 180);
    private readonly NumberBox angle = Num(-360, 360);
    private readonly NumberBox count = new NumberBox { Minimum = 1, Maximum = 30, Increment = 1, DecimalPlaces = 0, Value = 1, Width = 70 };

    public MemberDialog(FormationMember member)
    {
        Text = L.T("Member");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(420, 280);
        MinimizeBox = false;
        MaximizeBox = false;
        kind.Items.AddRange(new object[] { "chain", "slot", "ring", "free" });
        FormationPlace place = member.Place ?? new FormationPlace();
        id.Text = member.Id;
        unit.Text = member.Unit;
        ai.Text = member.Ai;
        role.Text = member.Role;
        kind.SelectedItem = place.Kind.Length == 0 ? "slot" : place.Kind;
        if (kind.SelectedIndex < 0) kind.SelectedIndex = 1;
        x.Value = (decimal)place.X;
        y.Value = (decimal)place.Y;
        radius.Value = (decimal)place.Radius;
        spin.Value = (decimal)place.Spin;
        angle.Value = (decimal)place.Angle;
        count.Value = Math.Clamp(place.Count <= 0 ? 1 : place.Count, 1, 30);

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, AutoSize = true, Padding = new Padding(8) };
        Add(grid, 0, L.T("Id"), id, L.T("Unit"), unit);
        Add(grid, 1, L.T("AI"), ai, L.T("Role"), role);
        Add(grid, 2, L.T("Place"), kind, L.T("Count"), count);
        Add(grid, 3, "x", x, "y", y);
        Add(grid, 4, L.T("Radius"), radius, L.T("Spin"), spin);
        Add(grid, 5, L.T("Angle"), angle, "", new Label());
        var ok = Ui.Primary(L.T("Save"), (_, _) => { DialogResult = DialogResult.OK; Close(); });
        ok.Left = 12;
        ok.Top = 230;
        Controls.Add(grid);
        Controls.Add(ok);
        AcceptButton = ok;
    }

    public void Apply(FormationMember member)
    {
        member.Id = id.Text.Trim();
        member.Unit = unit.Text.Trim();
        member.Ai = ai.Text.Trim();
        member.Role = role.Text.Trim();
        member.Place ??= new FormationPlace();
        member.Place.Kind = kind.SelectedItem?.ToString() ?? "slot";
        member.Place.X = (float)x.Value;
        member.Place.Y = (float)y.Value;
        member.Place.Radius = (float)radius.Value;
        member.Place.Spin = (float)spin.Value;
        member.Place.Angle = (float)angle.Value;
        member.Place.Count = (int)count.Value;
    }

    private static NumberBox Num(decimal min, decimal max) =>
        new NumberBox { Minimum = min, Maximum = max, Increment = 0.5m, DecimalPlaces = 1, Width = 70 };

    private static void Add(TableLayoutPanel grid, int row, string a, Control boxA, string b, Control boxB)
    {
        grid.Controls.Add(new Label { Text = a, AutoSize = true }, 0, row);
        grid.Controls.Add(boxA, 1, row);
        grid.Controls.Add(new Label { Text = b, AutoSize = true }, 2, row);
        grid.Controls.Add(boxB, 3, row);
    }
}

// Draws the formation with the same placement the server uses. A right click damages the piece under the cursor.
internal sealed class FormationCanvas : Control
{
    private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 40 };
    private readonly List<(float X, float Y)> trail = new List<(float X, float Y)>();
    private readonly Dictionary<string, float> health = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> fired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> suppressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private float time;
    private FormationFile? file;

    public FormationCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(24, 28, 34);
        timer.Tick += (_, _) => { time += 0.04f; Invalidate(); };
        timer.Start();
        MouseDown += OnMouse;
    }

    public FormationFile? File
    {
        get => file;
        set
        {
            file = value;
            trail.Clear();
            health.Clear();
            seen.Clear();
            fired.Clear();
            suppressed.Clear();
            time = 0f;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) timer.Dispose();
        base.Dispose(disposing);
    }

    private void OnMouse(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right || file == null) return;
        (float lx, float ly, float rot, _) = LeaderPose();
        string? hit = Hit(e.X, e.Y, lx, ly, rot);
        if (hit == null) return;
        health.TryGetValue(hit, out float hp);
        if (!health.ContainsKey(hit)) hp = 1f;
        hp -= 0.25f;
        if (hp < 0f) hp = 0f;
        health[hit] = hp;
        Invalidate();
    }

    private void LeaderPose(out float x, out float y, out float rot, out float speed)
    {
        float angle = time * 28f;
        x = MathF.Sin(angle * AiMath.Rad) * 10f;
        y = MathF.Cos(angle * AiMath.Rad) * 7f;
        rot = angle + 90f;
        speed = 4f;
    }

    private (float X, float Y, float Rot, float Speed) LeaderPose()
    {
        LeaderPose(out float x, out float y, out float rot, out float speed);
        return (x, y, rot, speed);
    }

    private string? Hit(int mx, int my, float lx, float ly, float rot)
    {
        string? best = null;
        float bestD = 14f;
        void Consider(string id, float wx, float wy)
        {
            Point p = ToScreen(wx, wy);
            float d = MathF.Sqrt((p.X - mx) * (p.X - mx) + (p.Y - my) * (p.Y - my));
            if (d < bestD) { bestD = d; best = id; }
        }
        Consider("leader", lx, ly);
        if (file == null) return best;
        foreach (FormationSlot slot in file.Expand())
        {
            FormationMotion.Pose pose = PoseOf(slot, lx, ly, rot, 4f);
            Consider(slot.Id, pose.X, pose.Y);
        }
        return best;
    }

    private FormationMotion.Pose PoseOf(FormationSlot slot, float lx, float ly, float rot, float speed)
    {
        if (slot.Kind == "chain")
            return FormationMotion.AlongTrail(lx, ly, rot, speed, trail, (slot.ChainIndex + 1) * Math.Max(0.4f, file?.Spacing ?? 1.7f));
        if (slot.Kind == "ring")
            return FormationMotion.Ring(lx, ly, rot, speed, slot.Radius, slot.Angle, slot.Spin, time);
        if (slot.Kind == "free")
            return FormationMotion.Start(slot, lx, ly, rot, file?.Spacing ?? 1.7f);
        return FormationMotion.Slot(lx, ly, rot, speed, slot.X, slot.Y);
    }

    private Point ToScreen(float x, float y)
    {
        float scale = Math.Min(Width, Height) / 36f;
        return new Point((int)(Width / 2f + x * scale), (int)(Height / 2f - y * scale));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (file == null)
        {
            TextRenderer.DrawText(e.Graphics, L.T("Pick a formation."), Font, ClientRectangle, Color.Gainsboro,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        (float lx, float ly, float rot, float speed) = LeaderPose();
        if (trail.Count == 0 || AiMath.Distance(trail[^1].X, trail[^1].Y, lx, ly) >= 0.5f) trail.Add((lx, ly));
        if (trail.Count > 80) trail.RemoveRange(0, trail.Count - 80);

        var actors = new List<FormationActor>();
        var poses = new Dictionary<string, (float X, float Y)>(StringComparer.OrdinalIgnoreCase);
        void Actor(string id, string role, int order, bool leader)
        {
            health.TryGetValue(id, out float hp);
            if (!health.ContainsKey(id)) hp = 1f;
            actors.Add(new FormationActor
            {
                Id = id, Role = role, RoleOrder = order, IsLeader = leader, Alive = hp > 0f,
                HealthFraction = hp, ShieldSuppressed = suppressed.Contains(id)
            });
        }
        Actor("leader", "", 0, true);
        poses["leader"] = (lx, ly);
        var roleCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (FormationSlot slot in file.Expand())
        {
            roleCount.TryGetValue(slot.Role, out int n);
            roleCount[slot.Role] = n + 1;
            Actor(slot.Id, slot.Role, n, false);
            FormationMotion.Pose pose = PoseOf(slot, lx, ly, rot, speed);
            poses[slot.Id] = (pose.X, pose.Y);
        }
        var orders = new List<FormationOrder>();
        FormationRules.Evaluate(file, actors, seen, fired, orders);
        var shielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FormationOrder order in orders)
        {
            if (order.ClearShield) suppressed.Add(order.ActorId);
            if (order.Shield && !suppressed.Contains(order.ActorId)) shielded.Add(order.ActorId);
            if (order.Kill) health[order.ActorId] = 0f;
            if (order.HealFraction > 0f && health.TryGetValue(order.ActorId, out float hp) && hp > 0f)
                health[order.ActorId] = Math.Min(1f, hp + order.HealFraction);
        }

        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        foreach (KeyValuePair<string, (float X, float Y)> pair in poses)
        {
            Point p = ToScreen(pair.Value.X, pair.Value.Y);
            bool dead = health.TryGetValue(pair.Key, out float hp) && hp <= 0f;
            var brush = new SolidBrush(dead ? Color.DimGray : pair.Key == "leader" ? Color.OrangeRed : Color.SteelBlue);
            g.FillEllipse(brush, p.X - 8, p.Y - 8, 16, 16);
            brush.Dispose();
            if (shielded.Contains(pair.Key))
            {
                using var pen = new Pen(Color.DeepSkyBlue, 2f);
                g.DrawEllipse(pen, p.X - 12, p.Y - 12, 24, 24);
            }
            TextRenderer.DrawText(g, pair.Key, Font, new Point(p.X + 10, p.Y - 8), Color.WhiteSmoke);
        }
        TextRenderer.DrawText(g, L.T("Right click damages the piece under the cursor."), Font, new Rectangle(8, Height - 28, Width - 16, 22),
            Color.Gainsboro, TextFormatFlags.Left);
    }
}
