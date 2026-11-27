using System.Text.Json.Nodes;
using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// Inline editors for one member, link, point or phase-summon line. They sit in the Formation tab; nothing here is a window.
internal static class FormationFields
{
    public static TableLayoutPanel Grid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Padding = new Padding(0, 4, 0, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        return grid;
    }

    public static void Add(TableLayoutPanel grid, string label, Control box, bool stretch = true)
    {
        int row = grid.RowCount;
        grid.RowCount = row + 1;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            MaximumSize = new Size(118, 0),
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 8, 0)
        }, 0, row);
        box.Margin = new Padding(0, 3, 8, 0);
        box.Anchor = stretch
            ? AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top
            : AnchorStyles.Left | AnchorStyles.Top;
        if (stretch) box.Width = Math.Max(box.Width, 160);
        grid.Controls.Add(box, 1, row);
    }

    // Turns a field (and its caption) on only when the current choice reads it. A gray field is kept in the file.
    public static void SetLive(Control box, bool on)
    {
        box.Enabled = on;
        // The choice may sit in a stack (the box, then the line that explains it) inside the grid cell.
        Control? cell = box;
        while (cell != null && cell.Parent is not TableLayoutPanel) cell = cell.Parent;
        if (cell?.Parent is not TableLayoutPanel grid) return;
        TableLayoutPanelCellPosition pos = grid.GetPositionFromControl(cell);
        if (pos.Row < 0) return;
        if (grid.GetControlFromPosition(0, pos.Row) is Control label)
            Ui.SetRole(label, on ? TextRole.Normal : TextRole.Muted);
    }

    // A short note at the top of a formation tab. It wraps when the page is resized.
    public static Label Explain(Control page, string text)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(2, 2, 8, 10)
        };
        Ui.SetRole(label, TextRole.Muted);
        void Fit(object? sender, EventArgs args)
        {
            int width = Math.Max(180, page.ClientSize.Width - 28);
            if (label.MaximumSize.Width != width) label.MaximumSize = new Size(width, 0);
        }
        page.Resize += Fit;
        page.HandleCreated += Fit;
        return label;
    }

    public static NumberBox Number(decimal value, decimal min, decimal max, decimal step)
    {
        return new NumberBox
        {
            Minimum = min,
            Maximum = max,
            Increment = step,
            DecimalPlaces = step < 1m ? 1 : 0,
            Value = value,
            Width = 76
        };
    }

    public static void SetNumber(NumberBox box, float value)
    {
        decimal next = (decimal)value;
        if (next < box.Minimum) next = box.Minimum;
        if (next > box.Maximum) next = box.Maximum;
        box.Value = next;
    }

    // One stored id, the words shown in the list, and the sentence under the list for that choice.
    public sealed class NamedChoice
    {
        public NamedChoice(string id, string label, string detail)
        {
            Id = id;
            Label = L.T(label);
            Detail = L.T(detail);
        }

        public string Id { get; }
        public string Label { get; }
        public string Detail { get; }
        public override string ToString() => Label;
    }

    public static ThemedComboBox Choices(params NamedChoice[] items)
    {
        var box = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, DropDownWidth = 460 };
        box.Items.AddRange(items);
        if (box.Items.Count > 0) box.SelectedIndex = 0;
        return box;
    }

    public static string IdOf(ComboBox box) => box.SelectedItem is NamedChoice choice ? choice.Id : box.SelectedItem?.ToString() ?? "";

    public static void SelectId(ComboBox box, string id)
    {
        for (int i = 0; i < box.Items.Count; i++)
        {
            if (box.Items[i] is NamedChoice choice && choice.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedIndex = i;
                return;
            }
        }
        if (box.Items.Count > 0 && box.DropDownStyle == ComboBoxStyle.DropDownList) box.SelectedIndex = 0;
    }

    // The combo, and under it a line that says what the current choice does. The line also becomes the tooltip.
    public static Control Stack(ThemedComboBox box, ToolTip? tip = null, Func<string>? extra = null)
    {
        var note = new Label { AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(0, 3, 0, 2) };
        Ui.SetRole(note, TextRole.Muted);
        void Show()
        {
            string text = extra != null ? extra() : box.SelectedItem is NamedChoice choice ? choice.Detail : "";
            note.Text = text;
            if (tip != null) tip.SetToolTip(box, text);
        }
        box.SelectedIndexChanged += (_, _) => Show();
        box.TextChanged += (_, _) => Show();
        Show();
        var panel = new Panel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        box.Dock = DockStyle.Top;
        box.Margin = new Padding(0);
        panel.Controls.Add(note);
        panel.Controls.Add(box);
        panel.Resize += (_, _) =>
        {
            int width = Math.Max(120, panel.Width - 8);
            if (note.MaximumSize.Width != width) note.MaximumSize = new Size(width, 0);
        };
        return panel;
    }

    public static ThemedComboBox ListChoice(params string[] items)
    {
        var box = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        box.Items.AddRange(items);
        if (box.Items.Count > 0) box.SelectedIndex = 0;
        return box;
    }

    public static ThemedComboBox TypedChoice(IEnumerable<string> items)
    {
        var box = new ThemedComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
            Width = 200
        };
        foreach (string item in items) box.Items.Add(item);
        return box;
    }
}

// One member of the formation: who, which unit, and where it sits.
internal sealed class MemberEditor : UserControl
{
    private readonly UnitTreeBox unit = new UnitTreeBox(UnitPickKind.GameUnit);
    private readonly ThemedComboBox ai;
    private readonly ThemedTextBox id = new ThemedTextBox();
    private readonly ThemedTextBox role = new ThemedTextBox();
    private readonly ThemedComboBox kind;
    private readonly ThemedComboBox motion;
    private readonly NumberBox x = FormationFields.Number(0, -40, 40, 0.5m);
    private readonly NumberBox y = FormationFields.Number(-4, -40, 40, 0.5m);
    private readonly NumberBox radius = FormationFields.Number(8, 0, 40, 0.5m);
    private readonly NumberBox spin = FormationFields.Number(0, -180, 180, 0.5m);
    private readonly NumberBox angle = FormationFields.Number(0, -360, 360, 1);
    private readonly NumberBox count = FormationFields.Number(1, 1, 30, 1);
    private readonly ThemedTextBox point = new ThemedTextBox();
    private readonly NumberBox pause = FormationFields.Number(0, 0, 20, 0.5m);
    private readonly NumberBox speed = FormationFields.Number(4, 0, 40, 0.5m);
    private readonly NumberBox orbit = FormationFields.Number(0, 0, 40, 0.5m);
    private FormationMember? member;
    private bool filling;

    public MemberEditor(ToolTip tip)
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        ai = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        kind = FormationFields.Choices(
            new FormationFields.NamedChoice("chain", "Behind along the path", "Each copy follows the path the leader already flew, one Spacing apart (set on the Formation tab). Turning the leader does not swing it sideways."),
            new FormationFields.NamedChoice("slot", "Bolted to an offset", "Stuck at x (right of the leader) and y (in front; negative is behind), and it turns with the leader. A sudden 90 degree turn moves it there instantly."),
            new FormationFields.NamedChoice("follow", "Flies to an offset", "The destination is the same x and y. The piece flies toward an area around that point and may stop to settle there, so a moving point does not make it loop past and come all the way around. A sudden turn does not teleport it. The Sergeant Pup pods do this."),
            new FormationFields.NamedChoice("ring", "Circle around the leader", "Count copies stand on a circle around the leader. Radius is the size, Angle is where the first sits, and Spin turns the circle (degrees per second)."),
            new FormationFields.NamedChoice("points", "Walks the points", "Uses the points of this formation. Motion chooses patrol, stay, fly to one, or orbit. A point's x is to the right of the leader and y is in front."),
            new FormationFields.NamedChoice("free", "Flies on its own", "Born near the leader, then its own AI flies it. The formation does not hold it. Only Count matters."));
        motion = FormationFields.Choices(
            new FormationFields.NamedChoice("patrol", "Patrol every point", "Walks from point to point at Speed, waits Pause seconds while it turns (Spin; 0 means 90 degrees per second), then takes the next edge."),
            new FormationFields.NamedChoice("fixed", "Stay on one point", "Stays on the point named in Point and does not walk the polygon. An empty Point uses this copy's number."),
            new FormationFields.NamedChoice("toward", "Fly to one point", "Flies in a straight line to that point at Speed, then stays there."),
            new FormationFields.NamedChoice("orbit", "Circle one point", "Orbits that point. Orbit radius is the size of the circle (0 means 2). Spin is degrees per second (0 means 40)."));

        void Tip(Control box, string text) => tip.SetToolTip(box, L.T(text));
        Tip(id, "Name links use for this piece. It stays on even before a unit is chosen.");
        Tip(unit, "The game unit that is born. The list is the same as the Enemy AI unit picker. With no unit, this member is not spawned and the other fields stay gray.");
        Tip(ai, "AI file this piece runs. The list is the AIs made for this unit, then the generic ones. Empty uses the AI of the unit.");
        Tip(role, "A name a link or a summonsAtMost condition can filter on, for example pod or outer.");
        Tip(kind, "Where this piece sits. chain follows behind (only Count is used; spacing is on the Formation tab). slot is bolted to x and y. follow uses the same x and y, but the piece flies toward an area around that point and may stop there. ring uses Count, Radius, Spin and Angle. points uses the polygon. free only uses Count.");
        Tip(count, "How many copies of this member are born. Used by chain, ring, free, and by points when Point is empty (each copy starts at the next vertex). A slot ignores it: the copies would sit on the same spot.");
        Tip(x, "Map units to the right of the leader. A slot is bolted there. follow flies toward an area around that point and may stop to settle there.");
        Tip(y, "Map units in front of the leader. Negative is behind. A slot is bolted there. follow flies toward an area around that point and may stop to settle there.");
        Tip(radius, "Size of the circle, in map units. Only a ring reads this.");
        Tip(spin, "Degrees per second. A ring spins the circle. On points, patrol uses it as the turn at a vertex (0 = 90) and orbit uses it as the spin (0 = 40).");
        Tip(angle, "Where the first piece of a ring sits, in degrees. The others are spaced evenly. Only a ring reads this.");
        Tip(motion, "Only for place points. patrol walks vertex to vertex. fixed stays on a point. toward flies to a point. orbit circles a point.");
        Tip(point, "Id of one point of this formation. Empty uses this copy's index, so Count can spread the copies. A named point puts every copy on that point.");
        Tip(pause, "Seconds the piece stands still at a vertex while it turns. Only points with motion patrol. 0 does not wait.");
        Tip(speed, "Map units per second along an edge or toward a point. Only points with motion patrol or toward. 0 uses 4.");
        Tip(orbit, "Radius of the circle around the point, in map units. Only points with motion orbit. 0 uses 2.");

        TableLayoutPanel grid = FormationFields.Grid();
        FormationFields.Add(grid, L.T("Id"), id);
        FormationFields.Add(grid, L.T("Unit"), unit);
        FormationFields.Add(grid, L.T("AI"), ai);
        FormationFields.Add(grid, L.T("Role"), role);
        FormationFields.Add(grid, L.T("Place"), FormationFields.Stack(kind, tip));
        FormationFields.Add(grid, L.T("Count"), count, false);
        FormationFields.Add(grid, "x", x, false);
        FormationFields.Add(grid, "y", y, false);
        FormationFields.Add(grid, L.T("Radius"), radius, false);
        FormationFields.Add(grid, L.T("Spin"), spin, false);
        FormationFields.Add(grid, L.T("Angle"), angle, false);
        FormationFields.Add(grid, L.T("Motion"), FormationFields.Stack(motion, tip));
        FormationFields.Add(grid, L.T("Point"), point);
        FormationFields.Add(grid, L.T("Pause (s)"), pause, false);
        FormationFields.Add(grid, L.T("Speed"), speed, false);
        FormationFields.Add(grid, L.T("Orbit radius"), orbit, false);
        Controls.Add(grid);

        id.TextChanged += (_, _) => Push();
        unit.ValueChanged += () =>
        {
            if (filling) return;
            filling = true;
            AiFill.Apply(ai, unit.Value, AiFill.Id(ai));
            filling = false;
            ApplyKind();
            Push();
        };
        ai.SelectedIndexChanged += (_, _) => Push();
        role.TextChanged += (_, _) => Push();
        point.TextChanged += (_, _) => { ApplyKind(); Push(); };
        kind.SelectedIndexChanged += (_, _) => { ApplyKind(); Push(); };
        motion.SelectedIndexChanged += (_, _) => { ApplyKind(); Push(); };
        foreach (NumberBox box in new[] { x, y, radius, spin, angle, count, pause, speed, orbit })
            box.ValueChanged += (_, _) => Push();
        Show(null);
    }

    public event Action? Changed;

    public void Show(FormationMember? next)
    {
        member = next;
        filling = true;
        bool on = next != null;
        Enabled = on;
        FormationPlace place = next?.Place ?? new FormationPlace();
        id.Text = next?.Id ?? "";
        unit.Value = next?.Unit ?? "";
        AiFill.Apply(ai, next?.Unit ?? "", next?.Ai ?? "");
        role.Text = next?.Role ?? "";
        FormationFields.SelectId(kind, place.Kind.Length == 0 ? "slot" : place.Kind);
        FormationFields.SelectId(motion, place.Motion.Length == 0 ? "patrol" : place.Motion);
        FormationFields.SetNumber(x, place.X);
        FormationFields.SetNumber(y, place.Y);
        FormationFields.SetNumber(radius, place.Radius);
        FormationFields.SetNumber(spin, place.Spin);
        FormationFields.SetNumber(angle, place.Angle);
        FormationFields.SetNumber(count, place.Count <= 0 ? 1 : place.Count);
        point.Text = place.Point;
        FormationFields.SetNumber(pause, place.Pause);
        FormationFields.SetNumber(speed, place.Speed <= 0f ? 4f : place.Speed);
        FormationFields.SetNumber(orbit, place.OrbitRadius);
        filling = false;
        ApplyKind();
    }

    private void ApplyKind()
    {
        bool on = Enabled && member != null;
        bool hasUnit = on && unit.Value.Length > 0;
        string place = FormationFields.IdOf(kind);
        if (place.Length == 0) place = "slot";
        string move = FormationFields.IdOf(motion);
        if (move.Length == 0) move = "patrol";
        bool points = place == "points";
        bool namedPoint = point.Text.Trim().Length > 0;
        FormationFields.SetLive(id, on);
        FormationFields.SetLive(unit, on);
        FormationFields.SetLive(ai, hasUnit);
        FormationFields.SetLive(role, hasUnit);
        FormationFields.SetLive(kind, hasUnit);
        FormationFields.SetLive(count, hasUnit && (place is "chain" or "ring" or "free" || (points && !namedPoint)));
        FormationFields.SetLive(x, hasUnit && place is "slot" or "follow");
        FormationFields.SetLive(y, hasUnit && place is "slot" or "follow");
        FormationFields.SetLive(radius, hasUnit && place == "ring");
        FormationFields.SetLive(spin, hasUnit && (place == "ring" || (points && move is "patrol" or "orbit")));
        FormationFields.SetLive(angle, hasUnit && place == "ring");
        FormationFields.SetLive(motion, hasUnit && points);
        FormationFields.SetLive(point, hasUnit && points);
        FormationFields.SetLive(pause, hasUnit && points && move == "patrol");
        FormationFields.SetLive(speed, hasUnit && points && move is "patrol" or "toward");
        FormationFields.SetLive(orbit, hasUnit && points && move == "orbit");
    }

    private void Push()
    {
        if (filling || member == null) return;
        string nextId = id.Text.Trim();
        if (nextId.Length > 0) member.Id = nextId;
        member.Unit = unit.Value;
        member.Ai = AiFill.Id(ai);
        member.Role = role.Text.Trim();
        member.Place ??= new FormationPlace();
        member.Place.Kind = FormationFields.IdOf(kind);
        if (member.Place.Kind.Length == 0) member.Place.Kind = "slot";
        member.Place.X = (float)x.Value;
        member.Place.Y = (float)y.Value;
        member.Place.Radius = (float)radius.Value;
        member.Place.Spin = (float)spin.Value;
        member.Place.Angle = (float)angle.Value;
        member.Place.Count = (int)count.Value;
        // Kept even while the fields are gray, so changing Place back still has them. The motion code ignores
        // the ones the current place does not read.
        member.Place.Motion = FormationFields.IdOf(motion);
        if (member.Place.Motion.Length == 0) member.Place.Motion = "patrol";
        member.Place.Point = point.Text.Trim();
        member.Place.Pause = (float)pause.Value;
        member.Place.Speed = (float)speed.Value;
        member.Place.OrbitRadius = (float)orbit.Value;
        Changed?.Invoke();
    }

    private static void Refill(ComboBox box, IEnumerable<string> items)
    {
        string text = box.Text;
        box.Items.Clear();
        foreach (string item in items) box.Items.Add(item);
        box.Text = text;
    }
}

// One link: a shield that holds, or something that happens once (death, health, heal).
internal sealed class LinkEditor : UserControl
{
    private readonly ThemedComboBox kind = FormationFields.Choices(
        new FormationFields.NamedChoice("shield", "Shield these pieces", "The pieces named below take no damage while the condition holds. This does not shield players."),
        new FormationFields.NamedChoice("killAll", "Kill everyone in the formation", "When the piece named in Piece that dies dies, every other piece of this formation dies too. Players are not killed."),
        new FormationFields.NamedChoice("unshield", "Drop the next shield", "When the first living piece of Role falls under Health %, the next piece of that same role loses its shield."),
        new FormationFields.NamedChoice("heal", "Heal a piece of the formation", "When the piece named in Piece that dies dies, Target (a member id, or leader) gains Heal % of its health. Players are not healed."));
    private readonly ThemedTextBox who = new ThemedTextBox();
    private readonly ThemedComboBox hold = FormationFields.Choices(
        new FormationFields.NamedChoice("whileAlive", "While this one piece lives", "The shield stays up until this member id dies. Write leader for the leader. It drops when that one piece is dead."),
        new FormationFields.NamedChoice("whileRole", "While this role has a living piece", "The shield stays up while any piece with this role is alive. It drops when that role is wiped out."),
        new FormationFields.NamedChoice("whileAny", "While any of these pieces lives", "The shield stays up while at least one of these ids is alive. It drops when every one of them is dead. Separate ids with commas."),
        new FormationFields.NamedChoice("always", "For the whole fight", "This rule never drops the shield. The value under it is not used."));
    private readonly ThemedTextBox holdValue = new ThemedTextBox();
    private readonly ThemedComboBox effect = FormationFields.ListChoice();
    private readonly ThemedTextBox died = new ThemedTextBox();
    private readonly ThemedTextBox role = new ThemedTextBox();
    private readonly NumberBox health = FormationFields.Number(33, 0, 100, 1);
    private readonly ThemedTextBox target = new ThemedTextBox();
    private readonly NumberBox amount = FormationFields.Number(25, 0, 100, 5);
    private readonly Label ids = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Tag = TextRole.Muted };
    private FormationLink? link;
    private bool filling;

    public LinkEditor(ToolTip tip)
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        effect.Items.Add(L.T("(none)"));
        foreach (string name in EditorAssets.EffectChoices()) effect.Items.Add(name);
        effect.SelectedIndex = 0;

        void Tip(Control box, string text) => tip.SetToolTip(box, L.T(text));
        Tip(who, "Who is protected. leader, a member id, or role:name. Several names are separated by commas. Only a shield rule reads this. Players are not in this list.");
        Tip(holdValue, "The member id, the role, or the list of ids that Holds while is waiting on. Gray when the shield lasts the whole fight.");
        Tip(effect, "The bubble drawn while the shield is up. (none) draws nothing and the pieces still take no damage. tankShield is the player tank's bubble. Only a shield link reads this.");
        Tip(died, "A member id of this formation, or leader. When that piece dies, kill-everyone or heal fires. Players are not pieces of the formation.");
        Tip(role, "Role whose first living piece is watched. When its health falls under Health %, the next piece of that role loses its shield. Only unshield reads this.");
        Tip(health, "Percent of health. The unshield link fires when the watched piece falls under this. 33 means a third.");
        Tip(target, "Who is affected. For unshield, next is the following piece of the role. For heal, leader is the usual target. You can type a member id.");
        Tip(amount, "Percent of health the target gains when the chosen piece dies. Only a heal link reads this.");

        TableLayoutPanel grid = FormationFields.Grid();
        FormationFields.Add(grid, L.T("Rule"), FormationFields.Stack(kind, tip));
        FormationFields.Add(grid, L.T("Shielded pieces"), who);
        FormationFields.Add(grid, L.T("Holds while"), FormationFields.Stack(hold, tip));
        FormationFields.Add(grid, L.T("Waits for"), holdValue);
        FormationFields.Add(grid, L.T("Shield look"), FormationFields.Stack(effect, tip, EffectDetail));
        FormationFields.Add(grid, L.T("Piece that dies"), died);
        FormationFields.Add(grid, L.T("Role"), role);
        FormationFields.Add(grid, L.T("Health %"), health, false);
        FormationFields.Add(grid, L.T("Target"), target);
        FormationFields.Add(grid, L.T("Heal %"), amount, false);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        ids.Dock = DockStyle.Top;
        grid.Dock = DockStyle.Top;
        // Last added is laid out first, so the fields stay above the id hint.
        scroll.Controls.Add(ids);
        scroll.Controls.Add(grid);
        Controls.Add(scroll);

        kind.SelectedIndexChanged += (_, _) => { ApplyKind(); Push(); };
        hold.SelectedIndexChanged += (_, _) => { ApplyKind(); Push(); };
        effect.SelectedIndexChanged += (_, _) => Push();
        who.TextChanged += (_, _) => Push();
        holdValue.TextChanged += (_, _) => Push();
        died.TextChanged += (_, _) => Push();
        role.TextChanged += (_, _) => Push();
        target.TextChanged += (_, _) => Push();
        health.ValueChanged += (_, _) => Push();
        amount.ValueChanged += (_, _) => Push();
        Show(null, Array.Empty<string>());
    }

    public event Action? Changed;

    public void Show(FormationLink? next, IEnumerable<string> memberIds)
    {
        link = next;
        filling = true;
        Enabled = next != null;
        ids.Text = L.T("Member ids here: ") + string.Join(", ", memberIds);
        bool shield = next?.Shield != null && next.Shield.Count > 0;
        FormationFields.SelectId(kind, shield ? "shield" : next?.Do?.Kind == "heal" ? "heal" : next?.Do?.Kind == "unshield" ? "unshield" : "killAll");
        who.Text = shield ? string.Join(", ", next!.Shield!) : "leader";
        if (!string.IsNullOrWhiteSpace(next?.WhileRole)) { FormationFields.SelectId(hold, "whileRole"); holdValue.Text = next!.WhileRole!; }
        else if (next?.WhileAnyAlive != null && next.WhileAnyAlive.Count > 0) { FormationFields.SelectId(hold, "whileAny"); holdValue.Text = string.Join(", ", next.WhileAnyAlive); }
        else if (!string.IsNullOrWhiteSpace(next?.WhileAlive)) { FormationFields.SelectId(hold, "whileAlive"); holdValue.Text = next!.WhileAlive!; }
        else { FormationFields.SelectId(hold, "always"); holdValue.Text = ""; }
        string look = next?.Effect ?? "";
        effect.SelectedItem = look.Length == 0 ? effect.Items[0] : look;
        if (effect.SelectedIndex < 0) { effect.Items.Add(look); effect.SelectedItem = look; }
        died.Text = next?.When?.Died ?? "";
        role.Text = next?.When?.Role ?? "";
        float line = next?.When?.HealthBelow ?? 33f;
        FormationFields.SetNumber(health, line <= 1f ? line * 100f : line);
        target.Text = next?.Do?.Target ?? "";
        float heal = next?.Do?.Amount ?? 25f;
        FormationFields.SetNumber(amount, heal > 0f && heal <= 1f ? heal * 100f : heal > 0f ? heal : 25f);
        filling = false;
        ApplyKind();
    }

    private void ApplyKind()
    {
        bool on = Enabled && link != null;
        string chosen = FormationFields.IdOf(kind);
        if (chosen.Length == 0) chosen = "shield";
        bool shield = chosen == "shield";
        string mode = FormationFields.IdOf(hold);
        if (mode.Length == 0) mode = "always";
        FormationFields.SetLive(kind, on);
        FormationFields.SetLive(who, on && shield);
        FormationFields.SetLive(hold, on && shield);
        FormationFields.SetLive(holdValue, on && shield && mode != "always");
        FormationFields.SetLive(effect, on && shield);
        FormationFields.SetLive(died, on && (chosen == "killAll" || chosen == "heal"));
        FormationFields.SetLive(role, on && chosen == "unshield");
        FormationFields.SetLive(health, on && chosen == "unshield");
        FormationFields.SetLive(target, on && (chosen == "unshield" || chosen == "heal"));
        FormationFields.SetLive(amount, on && chosen == "heal");
    }

    private void Push()
    {
        if (filling || link == null) return;
        string chosen = FormationFields.IdOf(kind);
        if (chosen.Length == 0) chosen = "shield";
        link.Shield = null;
        link.WhileAlive = null;
        link.WhileRole = null;
        link.WhileAnyAlive = null;
        link.Effect = null;
        link.When = null;
        link.Do = null;
        if (chosen == "shield")
        {
            link.Shield = Split(who.Text);
            if (link.Shield.Count == 0) link.Shield.Add("leader");
            string mode = FormationFields.IdOf(hold);
            if (mode.Length == 0) mode = "always";
            if (mode == "whileAlive") link.WhileAlive = holdValue.Text.Trim();
            else if (mode == "whileRole") link.WhileRole = holdValue.Text.Trim();
            else if (mode == "whileAny") link.WhileAnyAlive = Split(holdValue.Text);
            string look = effect.SelectedItem?.ToString() ?? "";
            link.Effect = look == L.T("(none)") ? "" : look;
            Changed?.Invoke();
            return;
        }
        link.When = new FormationWhen();
        link.Do = new FormationDo { Kind = chosen };
        if (chosen == "killAll") link.When.Died = died.Text.Trim();
        else if (chosen == "unshield")
        {
            link.When.Role = role.Text.Trim();
            link.When.Which = "firstAlive";
            link.When.HealthBelow = (float)health.Value;
            link.Do.Target = target.Text.Trim().Length == 0 ? "next" : target.Text.Trim();
        }
        else
        {
            link.When.Died = died.Text.Trim();
            link.Do.Kind = "heal";
            link.Do.Target = target.Text.Trim().Length == 0 ? "leader" : target.Text.Trim();
            link.Do.Amount = (float)amount.Value;
        }
        Changed?.Invoke();
    }

    private string EffectDetail()
    {
        string look = effect.SelectedItem?.ToString() ?? "";
        if (look.Length == 0 || look == L.T("(none)"))
            return L.T("No bubble is drawn. The pieces still take no damage. tankShield is the player tank's bubble, so an enemy should pick another look.");
        return L.F("The bubble drawn on the shielded pieces is {0}.", look);
    }

    private static List<string> Split(string text) =>
        text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
}

// One point of the polygon, in leader space (x to the right, y in front).
internal sealed class PointEditor : UserControl
{
    private readonly ThemedTextBox id = new ThemedTextBox();
    private readonly NumberBox x = FormationFields.Number(0, -80, 80, 0.5m);
    private readonly NumberBox y = FormationFields.Number(6, -80, 80, 0.5m);
    private FormationPoint? point;
    private bool filling;

    public PointEditor(ToolTip tip)
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        tip.SetToolTip(id, L.T("Name of this point. A member or a summon place refers to it. point:formation/id uses the file id plus this name."));
        tip.SetToolTip(x, L.T("Map units to the right of the leader. Negative is to the left."));
        tip.SetToolTip(y, L.T("Map units in front of the leader. Negative is behind."));
        TableLayoutPanel grid = FormationFields.Grid();
        FormationFields.Add(grid, L.T("Point id"), id);
        FormationFields.Add(grid, "x", x, false);
        FormationFields.Add(grid, "y", y, false);
        Controls.Add(grid);
        id.TextChanged += (_, _) => Push();
        x.ValueChanged += (_, _) => Push();
        y.ValueChanged += (_, _) => Push();
        Show(null);
    }

    public event Action? Changed;

    public void Show(FormationPoint? next)
    {
        point = next;
        filling = true;
        Enabled = next != null;
        id.Text = next?.Id ?? "";
        FormationFields.SetNumber(x, next?.X ?? 0f);
        FormationFields.SetNumber(y, next?.Y ?? 0f);
        filling = false;
    }

    private void Push()
    {
        if (filling || point == null) return;
        string next = id.Text.Trim();
        if (next.Length > 0) point.Id = next;
        point.X = (float)x.Value;
        point.Y = (float)y.Value;
        Changed?.Invoke();
    }
}

// One phase-summon line of the leader's AI. The Formation tab saves the AI file; this only edits the line.
internal sealed class SummonEditor : UserControl
{
    private readonly UnitTreeBox unit = new UnitTreeBox(UnitPickKind.Summon);
    private readonly ThemedComboBox ai;
    private readonly ThemedComboBox role = FormationFields.TypedChoice(UnitLists.Roles());
    private readonly ThemedComboBox place = FormationFields.TypedChoice(UnitLists.Places());
    private readonly NumberBox count = FormationFields.Number(1, 0, 30, 1);
    private readonly NumberBox every = FormationFields.Number(0, 0, 600, 0.5m);
    private readonly NumberBox max = FormationFields.Number(0, 0, 100, 1);
    private readonly NumberBox distance = FormationFields.Number(5, 0, 80, 0.5m);
    private readonly NumberBox angle = FormationFields.Number(0, -360, 360, 5);
    private JsonObject? line;
    private bool filling;

    public SummonEditor(ToolTip tip, FormationFile? points = null)
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        ai = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        void Tip(Control box, string text) => tip.SetToolTip(box, L.T(text));
        Tip(unit, "The unit this line creates. Empty summons nothing, and the other fields stay gray. The list is the same as the Enemy AI unit picker.");
        Tip(ai, "AI the summoned units run. Made for this unit first, then the generic ones. Empty uses the AI of the unit. Gray when this line summons nothing.");
        Tip(role, "Role name, so a summonsAtMost condition or a formation link can count only this kind. Pick one from the formations or type a new one.");
        Tip(place, "behind, front, left, right, ring or around. A formation point is point:formation/id (x to the right, y in front). Empty means behind.");
        Tip(count, "How many appear each time. 0 summons nothing, and the other fields of this line stay gray. 1 to 30 summon that many.");
        Tip(every, "Seconds before this line summons again. 0 means only when the state starts.");
        Tip(max, "No new summons of this line while this many of them are alive. 0 means no limit.");
        Tip(distance, "How far from the summoner they appear. 0 means 5. Gray when Place is a formation point, which ignores this.");
        Tip(angle, "Where a ring or around starts, in degrees. Gray for every other place.");
        Refill(place, UnitLists.Places(points));
        TableLayoutPanel grid = FormationFields.Grid();
        FormationFields.Add(grid, L.T("Unit"), unit);
        FormationFields.Add(grid, L.T("AI"), ai);
        FormationFields.Add(grid, L.T("Role"), role);
        FormationFields.Add(grid, L.T("Place"), FormationFields.Stack(place, tip, SummonPlaceDetail));
        FormationFields.Add(grid, L.T("Count"), count, false);
        FormationFields.Add(grid, L.T("Again every (s)"), every, false);
        FormationFields.Add(grid, L.T("At most alive"), max, false);
        FormationFields.Add(grid, L.T("Distance"), distance, false);
        FormationFields.Add(grid, L.T("Angle"), angle, false);
        Controls.Add(grid);
        unit.ValueChanged += () =>
        {
            if (filling) return;
            filling = true;
            AiFill.Apply(ai, unit.Value, AiFill.Id(ai));
            filling = false;
            ApplyUse();
            Push();
        };
        ai.SelectedIndexChanged += (_, _) => Push();
        role.TextChanged += (_, _) => Push();
        place.TextChanged += (_, _) => { ApplyUse(); Push(); };
        count.ValueChanged += (_, _) => { ApplyUse(); Push(); };
        foreach (NumberBox box in new[] { every, max, distance, angle })
            box.ValueChanged += (_, _) => Push();
        Show(null);
    }

    public event Action? Changed;

    public void UseLists(FormationFile? points)
    {
        filling = true;
        Refill(role, UnitLists.Roles());
        Refill(place, UnitLists.Places(points));
        filling = false;
    }

    public void Show(JsonObject? next)
    {
        line = next;
        filling = true;
        Enabled = next != null;
        unit.Value = next == null ? "" : Json.Str(next, "unit");
        AiFill.Apply(ai, unit.Value, next == null ? "" : Json.Str(next, "ai"));
        role.Text = next == null ? "" : Json.Str(next, "role");
        string where = next == null ? "behind" : Json.Str(next, "place");
        place.Text = where.Length == 0 ? "behind" : where;
        FormationFields.SetNumber(count, next == null ? 1 : Json.Int(next, "count", 1));
        FormationFields.SetNumber(every, next == null ? 0 : (float)Json.Num(next, "every"));
        FormationFields.SetNumber(max, next == null ? 0 : Json.Int(next, "max"));
        FormationFields.SetNumber(distance, next == null ? 5 : (float)Json.Num(next, "distance", 5));
        FormationFields.SetNumber(angle, next == null ? 0 : (float)Json.Num(next, "angle"));
        filling = false;
        ApplyUse();
    }

    private string SummonPlaceDetail()
    {
        string where = place.Text.Trim().ToLowerInvariant();
        if (where.Length == 0 || where == "behind")
            return L.T("Behind the summoner, Distance away. Angle is not used. Empty means behind.");
        if (where == "front") return L.T("In front of the summoner, Distance away. Angle is not used.");
        if (where == "left") return L.T("To the summoner's left, Distance away. Angle is not used.");
        if (where == "right") return L.T("To the summoner's right, Distance away. Angle is not used.");
        if (where is "ring" or "around")
            return L.T("In a circle around the summoner. Distance is the radius and Angle is where the first one sits.");
        if (where.StartsWith("point:", StringComparison.Ordinal))
            return L.T("On that formation point (x to the right of the summoner, y in front). Distance and Angle are not used.");
        return L.T("A custom place. The usual names are behind, front, left, right, ring, around, or point:formation/id.");
    }

    private void ApplyUse()
    {
        bool on = Enabled && line != null;
        bool live = on && unit.Value.Length > 0 && count.Value > 0;
        string where = place.Text.Trim();
        bool pointPlace = where.StartsWith("point:", StringComparison.OrdinalIgnoreCase);
        bool ring = where.Equals("ring", StringComparison.OrdinalIgnoreCase) || where.Equals("around", StringComparison.OrdinalIgnoreCase);
        FormationFields.SetLive(unit, on);
        FormationFields.SetLive(count, on);
        FormationFields.SetLive(ai, live);
        FormationFields.SetLive(role, live);
        FormationFields.SetLive(place, live);
        FormationFields.SetLive(every, live);
        FormationFields.SetLive(max, live);
        FormationFields.SetLive(distance, live && !pointPlace);
        FormationFields.SetLive(angle, live && ring);
    }

    private void Push()
    {
        if (filling || line == null) return;
        line["unit"] = unit.Value;
        line["ai"] = AiFill.Id(ai);
        line["role"] = role.Text.Trim();
        line["place"] = place.Text.Trim().Length == 0 ? "behind" : place.Text.Trim();
        line["count"] = (int)count.Value;
        line["every"] = (double)every.Value;
        line["max"] = (int)max.Value;
        line["distance"] = (double)distance.Value;
        line["angle"] = (double)angle.Value;
        Changed?.Invoke();
    }

    private static void Refill(ComboBox box, IEnumerable<string> items)
    {
        string text = box.Text;
        box.Items.Clear();
        foreach (string item in items) box.Items.Add(item);
        box.Text = text;
    }
}
