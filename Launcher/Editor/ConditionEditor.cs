using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// One kind of condition a field accepts: "cleared:<room>", "death:<unit type>", "quest:<id>", or a word without
// argument ("onEnter"). Written in the file as the prefix, then ":" and the argument.
internal sealed class ConditionKind
{
    public ConditionKind(string prefix, string label, Func<IEnumerable<string>>? choices = null, bool noArgument = false)
    {
        Prefix = prefix;
        Label = label;
        Choices = choices;
        NoArgument = noArgument;
    }

    public string Prefix { get; }

    // English text of the drop-down (translated when shown).
    public string Label { get; }

    // The values of the argument as a drop-down (rooms, unit types). Null = the argument is typed.
    public Func<IEnumerable<string>>? Choices { get; }

    // The kind is the whole condition ("onEnter"): there is no argument.
    public bool NoArgument { get; }
}

// What a condition field accepts. Multiple: several conditions, all of them must be true (written joined with "+");
// otherwise exactly one, and a kind with the empty prefix stands for "nothing" (the field is removed from the file).
internal sealed class ConditionSpec
{
    public ConditionSpec(bool multiple, params ConditionKind[] kinds)
    {
        Multiple = multiple;
        Kinds = kinds;
    }

    public bool Multiple { get; }
    public IReadOnlyList<ConditionKind> Kinds { get; }
}

// A condition such as "cleared:room 1+death:boss" edited with drop-downs: for each condition a drop-down for its kind
// and a drop-down (or a box) for what it points to. Nobody has to remember the words or the order.
internal sealed class ConditionEditor : TableLayoutPanel
{
    private sealed class Part
    {
        public ConditionKind Kind = null!;
        public string Argument = "";
    }

    private static readonly ToolTip Tip = new ToolTip { AutoPopDelay = 12000 };

    private readonly ConditionSpec spec;
    private readonly List<Part> parts = new List<Part>();
    private readonly string addHelp;
    private bool building;

    public ConditionEditor(ConditionSpec spec, string addHelp)
    {
        this.spec = spec;
        this.addHelp = addHelp;
        ColumnCount = 1;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(0);
        Dock = DockStyle.Fill;
        Rebuild();
    }

    // The user changed the condition (the value is read with Value).
    public event Action? Edited;

    // The condition as written in the file ("" = none).
    public string Value
    {
        get => Join();
        set
        {
            if (value == Join()) return;
            Parse(value ?? "");
            Rebuild();
        }
    }

    // ------------------------------------------------------------------ text <-> parts

    private string Join()
    {
        var texts = new List<string>();
        foreach (Part part in parts)
        {
            if (part.Kind.Prefix.Length == 0) continue;
            if (part.Kind.NoArgument) texts.Add(part.Kind.Prefix);
            else if (part.Argument.Length > 0) texts.Add(part.Kind.Prefix + ":" + part.Argument);
        }
        return string.Join("+", texts);
    }

    private void Parse(string text)
    {
        parts.Clear();
        IEnumerable<string> pieces = spec.Multiple ? text.Split('+') : new[] { text };
        foreach (string raw in pieces)
        {
            string piece = raw.Trim();
            if (piece.Length == 0) continue;
            int colon = piece.IndexOf(':');
            string prefix = colon < 0 ? piece : piece.Substring(0, colon).Trim();
            string argument = colon < 0 ? "" : piece.Substring(colon + 1).Trim();
            ConditionKind? kind = spec.Kinds.FirstOrDefault(k => k.Prefix.Length > 0 && string.Equals(k.Prefix, prefix, StringComparison.OrdinalIgnoreCase));
            // Something written by hand that this field does not know: it is kept as it is, as a kind of its own.
            kind ??= new ConditionKind(prefix, prefix, null, colon < 0);
            parts.Add(new Part { Kind = kind, Argument = argument });
        }
        if (!spec.Multiple && parts.Count == 0) parts.Add(new Part { Kind = EmptyKind() });
    }

    private ConditionKind EmptyKind() => spec.Kinds.FirstOrDefault(k => k.Prefix.Length == 0) ?? spec.Kinds[0];

    // ------------------------------------------------------------------ controls

    private void Rebuild()
    {
        building = true;
        SuspendLayout();
        try
        {
            foreach (Control old in Controls.Cast<Control>().ToList())
            {
                Controls.Remove(old);
                old.Dispose();
            }
            if (!spec.Multiple && parts.Count == 0) parts.Add(new Part { Kind = EmptyKind() });
            foreach (Part part in parts) Controls.Add(BuildRow(part));
            if (spec.Multiple)
            {
                ThemedButton add = Ui.Quiet(L.T("+ Add a condition"), (_, _) => AddPart());
                add.AutoSize = false;
                add.Width = 150;
                add.Margin = new Padding(0, 2, 0, 2);
                add.Anchor = AnchorStyles.Left;
                Tip.SetToolTip(add, L.T(addHelp));
                Controls.Add(add);
            }
        }
        finally
        {
            RowStyles.Clear();
            RowCount = Controls.Count;
            for (int i = 0; i < Controls.Count; i++) RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ResumeLayout(true);
            building = false;
        }
    }

    private Control BuildRow(Part part)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Height = 28,
            Margin = new Padding(0, 1, 0, 1),
            Dock = DockStyle.Fill
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, spec.Multiple ? 30 : 0));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // The drop-down of the kinds: the ones of the field, and the kind of the part when it is a written-by-hand one.
        var kinds = spec.Kinds.ToList();
        if (!kinds.Contains(part.Kind)) kinds.Add(part.Kind);
        var kindBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 3, 1), DropDownWidth = 260 };
        foreach (ConditionKind kind in kinds) kindBox.Items.Add(L.T(kind.Label));
        kindBox.SelectedIndex = kinds.IndexOf(part.Kind);
        kindBox.SelectedIndexChanged += (_, _) =>
        {
            if (building || kindBox.SelectedIndex < 0) return;
            part.Kind = kinds[kindBox.SelectedIndex];
            part.Argument = part.Kind.Choices?.Invoke().FirstOrDefault() ?? "";
            Edited?.Invoke();
            Redraw();
        };
        row.Controls.Add(kindBox, 0, 0);

        if (!part.Kind.NoArgument)
        {
            Control argument = BuildArgument(part);
            row.Controls.Add(argument, 1, 0);
        }

        if (spec.Multiple)
        {
            ThemedButton remove = Ui.Quiet("x", (_, _) =>
            {
                parts.Remove(part);
                Edited?.Invoke();
                Redraw();
            });
            remove.AutoSize = false;
            remove.Dock = DockStyle.Fill;
            remove.Margin = new Padding(2, 1, 0, 1);
            Tip.SetToolTip(remove, L.T("Remove this condition."));
            row.Controls.Add(remove, 2, 0);
        }
        return row;
    }

    private Control BuildArgument(Part part)
    {
        Func<IEnumerable<string>>? choices = part.Kind.Choices;
        if (choices == null)
        {
            var box = new ThemedTextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 3, 2), Text = part.Argument };
            void Commit()
            {
                if (building || box.Text.Trim() == part.Argument) return;
                part.Argument = box.Text.Trim();
                Edited?.Invoke();
            }
            box.Validated += (_, _) => Commit();
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                e.Handled = true;
                Commit();
            };
            return box;
        }

        var combo = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(0, 1, 3, 1), DropDownWidth = 320 };
        var values = choices().ToList();
        // A value that does not exist any more (a deleted room) stays visible so the problem can be seen and fixed.
        if (part.Argument.Length > 0 && !values.Contains(part.Argument)) values.Add(part.Argument);
        foreach (string value in values) combo.Items.Add(value);
        combo.SelectedIndex = values.IndexOf(part.Argument);
        combo.SelectedIndexChanged += (_, _) =>
        {
            if (building || combo.SelectedIndex < 0) return;
            part.Argument = values[combo.SelectedIndex];
            Edited?.Invoke();
        };
        return combo;
    }

    private void AddPart()
    {
        ConditionKind kind = spec.Kinds.First(k => k.Prefix.Length > 0);
        parts.Add(new Part { Kind = kind, Argument = kind.Choices?.Invoke().FirstOrDefault() ?? "" });
        Edited?.Invoke();
        Redraw();
    }

    // The controls are made again after the event that asked for it ends (a control cannot dispose itself in its own event).
    private void Redraw()
    {
        if (IsHandleCreated) BeginInvoke(new Action(Rebuild));
        else Rebuild();
    }
}
