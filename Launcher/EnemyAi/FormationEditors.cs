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
    private readonly ThemedComboBox unit;
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

    public MemberEditor(IEnumerable<string> units, IEnumerable<string> ais, ToolTip tip)
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        unit = FormationFields.TypedChoice(units);
        ai = FormationFields.TypedChoice(ais);
        kind = FormationFields.ListChoice("chain", "slot", "ring", "points", "free");
        motion = FormationFields.ListChoice("patrol", "fixed", "toward", "orbit");

        tip.SetToolTip(id, L.T("Id is the name links use."));
        tip.SetToolTip(unit, L.T("The game unit this piece is."));
        tip.SetToolTip(ai, L.T("AI file this piece runs. Empty uses the AI of the unit."));
        tip.SetToolTip(role, L.T("A name a link can filter on, for example pod or outer."));
        tip.SetToolTip(kind, L.T("chain follows behind, slot is a fixed offset, ring is a circle, points walks the polygon, free flies on its own."));
        tip.SetToolTip(motion, L.T("Only for place points: patrol the polygon, stay on a point, fly to one, or orbit one."));

        TableLayoutPanel grid = FormationFields.Grid();
        FormationFields.Add(grid, L.T("Id"), id);
        FormationFields.Add(grid, L.T("Unit"), unit);
        FormationFields.Add(grid, L.T("AI"), ai);
        FormationFields.Add(grid, L.T("Role"), role);
        FormationFields.Add(grid, L.T("Place"), kind);
        FormationFields.Add(grid, L.T("Count"), count, false);
        FormationFields.Add(grid, "x", x, false);
        FormationFields.Add(grid, "y", y, false);
        FormationFields.Add(grid, L.T("Radius"), radius, false);
        FormationFields.Add(grid, L.T("Spin"), spin, false);
        FormationFields.Add(grid, L.T("Angle"), angle, false);
        FormationFields.Add(grid, L.T("Motion"), motion);
        FormationFields.Add(grid, L.T("Point"), point);
        FormationFields.Add(grid, L.T("Pause (s)"), pause, false);
        FormationFields.Add(grid, L.T("Speed"), speed, false);
        FormationFields.Add(grid, L.T("Orbit radius"), orbit, false);
        Controls.Add(grid);

        id.TextChanged += (_, _) => Push();
        unit.TextChanged += (_, _) => Push();
        ai.TextChanged += (_, _) => Push();
        role.TextChanged += (_, _) => Push();
        point.TextChanged += (_, _) => Push();
        kind.SelectedIndexChanged += (_, _) => { ApplyKind(); Push(); };
        motion.SelectedIndexChanged += (_, _) => Push();
        foreach (NumberBox box in new[] { x, y, radius, spin, angle, count, pause, speed, orbit })
            box.ValueChanged += (_, _) => Push();
        Show(null);
    }

    public event Action? Changed;

    public void UseLists(IEnumerable<string> units, IEnumerable<string> ais)
    {
        Refill(unit, units);
        Refill(ai, ais);
    }

    public void Show(FormationMember? next)
    {
        member = next;
        filling = true;
        bool on = next != null;
        Enabled = on;
        FormationPlace place = next?.Place ?? new FormationPlace();
        id.Text = next?.Id ?? "";
        unit.Text = next?.Unit ?? "";
        ai.Text = next?.Ai ?? "";
        role.Text = next?.Role ?? "";
        Select(kind, place.Kind.Length == 0 ? "slot" : place.Kind);
        Select(motion, place.Motion.Length == 0 ? "patrol" : place.Motion);
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
        bool points = (kind.SelectedItem?.ToString() ?? "") == "points";
        motion.Enabled = point.Enabled = pause.Enabled = speed.Enabled = orbit.Enabled = Enabled && points;
    }

    private void Push()
    {
        if (filling || member == null) return;
        string nextId = id.Text.Trim();
        if (nextId.Length > 0) member.Id = nextId;
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
        if (member.Place.Kind == "points")
        {
            member.Place.Motion = motion.SelectedItem?.ToString() ?? "patrol";
            member.Place.Point = point.Text.Trim();
            member.Place.Pause = (float)pause.Value;
            member.Place.Speed = (float)speed.Value;
            member.Place.OrbitRadius = (float)orbit.Value;
        }
        else
        {
            member.Place.Motion = "";
            member.Place.Point = "";
            member.Place.Pause = 0f;
            member.Place.Speed = 0f;
            member.Place.OrbitRadius = 0f;
        }
        Changed?.Invoke();
    }

    private static void Select(ComboBox box, string value)
    {
        box.SelectedItem = value;
        if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0;
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
    private readonly ThemedComboBox kind = FormationFields.ListChoice("shield", "killAll", "unshield", "heal");
    private readonly ThemedTextBox who = new ThemedTextBox();
    private readonly ThemedComboBox hold = FormationFields.ListChoice("whileAlive", "whileRole", "whileAny", "always");
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

        tip.SetToolTip(who, L.T("Who is leader, a member id, or role:name. Several names are separated by commas."));
        tip.SetToolTip(effect, L.T("Who is leader, a member id, or role:name. tankShield is the player tank's bubble. Pick another look, or (none) for no bubble. The unit still takes no damage."));
        tip.SetToolTip(died, L.T("Member id whose death fires this link. leader is the leader."));
        tip.SetToolTip(health, L.T("The first living piece of the role loses the next shield when it falls under this percent."));

        TableLayoutPanel grid = FormationFields.Grid();
        FormationFields.Add(grid, L.T("Kind"), kind);
        FormationFields.Add(grid, L.T("Who"), who);
        FormationFields.Add(grid, L.T("While"), hold);
        FormationFields.Add(grid, L.T("While value"), holdValue);
        FormationFields.Add(grid, L.T("Shield look"), effect);
        FormationFields.Add(grid, L.T("When this dies"), died);
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
        hold.SelectedIndexChanged += (_, _) => Push();
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
        Select(kind, shield ? "shield" : next?.Do?.Kind == "heal" ? "heal" : next?.Do?.Kind == "unshield" ? "unshield" : "killAll");
        who.Text = shield ? string.Join(", ", next!.Shield!) : "leader";
        if (!string.IsNullOrWhiteSpace(next?.WhileRole)) { Select(hold, "whileRole"); holdValue.Text = next!.WhileRole!; }
        else if (next?.WhileAnyAlive != null && next.WhileAnyAlive.Count > 0) { Select(hold, "whileAny"); holdValue.Text = string.Join(", ", next.WhileAnyAlive); }
        else if (!string.IsNullOrWhiteSpace(next?.WhileAlive)) { Select(hold, "whileAlive"); holdValue.Text = next!.WhileAlive!; }
        else { Select(hold, "always"); holdValue.Text = ""; }
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
        string chosen = kind.SelectedItem?.ToString() ?? "shield";
        bool shield = chosen == "shield";
        who.Enabled = hold.Enabled = holdValue.Enabled = effect.Enabled = Enabled && shield;
        died.Enabled = Enabled && (chosen == "killAll" || chosen == "heal");
        role.Enabled = health.Enabled = Enabled && chosen == "unshield";
        target.Enabled = Enabled && (chosen == "unshield" || chosen == "heal");
        amount.Enabled = Enabled && chosen == "heal";
    }

    private void Push()
    {
        if (filling || link == null) return;
        string chosen = kind.SelectedItem?.ToString() ?? "shield";
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
            string mode = hold.SelectedItem?.ToString() ?? "always";
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

    private static void Select(ComboBox box, string value)
    {
        box.SelectedItem = value;
        if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0;
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
        tip.SetToolTip(x, L.T("x y (right, front)"));
        tip.SetToolTip(y, L.T("x y (right, front)"));
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
    private readonly ThemedComboBox unit;
    private readonly ThemedComboBox ai;
    private readonly ThemedTextBox role = new ThemedTextBox();
    private readonly ThemedComboBox place = FormationFields.ListChoice("behind", "front", "left", "right", "ring", "around");
    private readonly NumberBox count = FormationFields.Number(1, 1, 30, 1);
    private readonly NumberBox every = FormationFields.Number(0, 0, 600, 0.5m);
    private readonly NumberBox max = FormationFields.Number(0, 0, 100, 1);
    private readonly NumberBox distance = FormationFields.Number(5, 0, 80, 0.5m);
    private readonly NumberBox angle = FormationFields.Number(0, -360, 360, 5);
    private JsonObject? line;
    private bool filling;

    public SummonEditor(IEnumerable<string> units, IEnumerable<string> ais)
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        unit = FormationFields.TypedChoice(units);
        ai = FormationFields.TypedChoice(ais);
        TableLayoutPanel grid = FormationFields.Grid();
        FormationFields.Add(grid, L.T("Unit"), unit);
        FormationFields.Add(grid, L.T("AI"), ai);
        FormationFields.Add(grid, L.T("Role"), role);
        FormationFields.Add(grid, L.T("Place"), place);
        FormationFields.Add(grid, L.T("Count"), count, false);
        FormationFields.Add(grid, L.T("Again every (s)"), every, false);
        FormationFields.Add(grid, L.T("At most alive"), max, false);
        FormationFields.Add(grid, L.T("Distance"), distance, false);
        FormationFields.Add(grid, L.T("Angle"), angle, false);
        Controls.Add(grid);
        unit.TextChanged += (_, _) => Push();
        ai.TextChanged += (_, _) => Push();
        role.TextChanged += (_, _) => Push();
        place.SelectedIndexChanged += (_, _) => Push();
        foreach (NumberBox box in new[] { count, every, max, distance, angle })
            box.ValueChanged += (_, _) => Push();
        Show(null);
    }

    public event Action? Changed;

    public void UseLists(IEnumerable<string> units, IEnumerable<string> ais)
    {
        Refill(unit, units);
        Refill(ai, ais);
    }

    public void Show(JsonObject? next)
    {
        line = next;
        filling = true;
        Enabled = next != null;
        unit.Text = next == null ? "" : Json.Str(next, "unit");
        ai.Text = next == null ? "" : Json.Str(next, "ai");
        role.Text = next == null ? "" : Json.Str(next, "role");
        string where = next == null ? "behind" : Json.Str(next, "place");
        place.SelectedItem = where.Length == 0 ? "behind" : where;
        if (place.SelectedIndex < 0) place.SelectedIndex = 0;
        FormationFields.SetNumber(count, next == null ? 1 : Json.Int(next, "count", 1));
        FormationFields.SetNumber(every, next == null ? 0 : (float)Json.Num(next, "every"));
        FormationFields.SetNumber(max, next == null ? 0 : Json.Int(next, "max"));
        FormationFields.SetNumber(distance, next == null ? 5 : (float)Json.Num(next, "distance", 5));
        FormationFields.SetNumber(angle, next == null ? 0 : (float)Json.Num(next, "angle"));
        filling = false;
    }

    private void Push()
    {
        if (filling || line == null) return;
        line["unit"] = unit.Text.Trim();
        line["ai"] = ai.Text.Trim();
        line["role"] = role.Text.Trim();
        line["place"] = place.SelectedItem?.ToString() ?? "behind";
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
