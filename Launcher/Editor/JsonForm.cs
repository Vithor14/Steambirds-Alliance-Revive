using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

internal enum FieldKind
{
    Header,
    Text,
    MultiText,
    Int,
    Number,

    // Number that may be missing from the file (empty box = field removed).
    OptionalNumber,
    Bool,
    Choice,
    EditableChoice,
    IntList,
    StringList,
    Positions,

    // The gun mounts of a unit as a checklist (needs FieldSpec.Guns); stored as a list of indexes, empty = all of them.
    GunMounts
}

// One field of a property page: the key in the JSON object ("a.b" reaches the object "a" inside it), the label
// (English text; the form translates it), the kind of control and a few options.
internal sealed class FieldSpec
{
    public FieldSpec(string key, string label, FieldKind kind)
    {
        Key = key;
        Label = label;
        Kind = kind;
    }

    public string Key { get; }
    public string Label { get; }
    public FieldKind Kind { get; }

    // Tooltip (English text).
    public string Help { get; init; } = "";

    // Values of a Choice or EditableChoice, asked every time the form is bound.
    public Func<IEnumerable<string>>? Choices { get; init; }

    // Choice: an empty first entry (the field is removed from the file when chosen).
    public bool AllowEmpty { get; init; } = true;

    public double Min { get; init; } = -100000;
    public double Max { get; init; } = 100000;
    public double Default { get; init; }
    public bool DefaultBool { get; init; }
    public bool ReadOnly { get; init; }
    public double Increment { get; init; } = -1;

    // GunMounts: the guns of the unit the form is about (asked every time the form shows its values).
    public Func<IReadOnlyList<SteambirdsAi.AiGunStats>>? Guns { get; init; }

    // Positions: write only "u" and "v" (routes have no rotation).
    public bool NoRotation { get; init; }

    // Choice and EditableChoice: the picture of a choice (a unit, a floor piece), drawn next to its name in the list.
    public Func<string, AssetPicture?>? Picture { get; init; }

    // Text and choice fields: called inside the change, after the new value is written (the data, the old and the new value).
    public Action<JsonObject, string, string>? OnChanged { get; init; }

    public static FieldSpec Header(string label) => new FieldSpec("", label, FieldKind.Header);
}

// Property editor made from a list of fields. It shows the values of a JsonObject and writes the changes straight
// into it (calling "beforeChange" first, for the undo point, and "afterChange" after). Fields that are not in the
// file show their default value and are written only when the user changes them; an empty text removes the field.
internal sealed class JsonForm : Panel
{
    private sealed class Row
    {
        public FieldSpec Spec = null!;
        public Control Editor = null!;
        public Label? Caption;
    }

    private readonly List<Row> rows = new List<Row>();
    private readonly Action beforeChange;
    private readonly Action<FieldSpec> afterChange;
    private readonly ToolTip tip = new ToolTip { AutoPopDelay = 12000 };
    private readonly TableLayoutPanel table;
    private bool loading;

    public JsonForm(IEnumerable<FieldSpec> fields, Action beforeChange, Action<FieldSpec> afterChange)
    {
        this.beforeChange = beforeChange;
        this.afterChange = afterChange;
        AutoScroll = true;
        Dock = DockStyle.Fill;

        table = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(2, 2, 6, 2)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(table);

        foreach (FieldSpec spec in fields) AddRow(spec);
        SetEnabled(false);
    }

    public JsonObject? Data { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tip.Dispose();
        base.Dispose(disposing);
    }

    // ------------------------------------------------------------------ building

    private void AddRow(FieldSpec spec)
    {
        int rowIndex = table.RowCount;
        table.RowCount = rowIndex + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        if (spec.Kind == FieldKind.Header)
        {
            Label header = Ui.Bold(L.T(spec.Label));
            header.Margin = new Padding(3, rowIndex == 0 ? 3 : 12, 3, 2);
            table.Controls.Add(header, 0, rowIndex);
            table.SetColumnSpan(header, 2);
            return;
        }

        Control editor = CreateEditor(spec);
        var row = new Row { Spec = spec, Editor = editor };
        rows.Add(row);

        if (spec.Kind == FieldKind.Bool)
        {
            editor.Margin = new Padding(3, 3, 3, 1);
            table.Controls.Add(editor, 1, rowIndex);
        }
        else
        {
            Label caption = Ui.Text(L.T(spec.Label));
            caption.Anchor = spec.Kind is FieldKind.MultiText or FieldKind.Positions or FieldKind.GunMounts ? AnchorStyles.Top | AnchorStyles.Left : AnchorStyles.Left;
            row.Caption = caption;
            table.Controls.Add(caption, 0, rowIndex);
            table.Controls.Add(editor, 1, rowIndex);
            if (spec.Help.Length > 0) tip.SetToolTip(caption, L.T(spec.Help));
        }
        if (spec.Help.Length > 0) tip.SetToolTip(editor, L.T(spec.Help));
    }

    private Control CreateEditor(FieldSpec spec)
    {
        switch (spec.Kind)
        {
            case FieldKind.Bool:
            {
                var box = new ThemedCheckBox { Text = L.T(spec.Label), AutoSize = true };
                box.CheckedChanged += (_, _) => CommitBool(spec, box);
                return box;
            }
            case FieldKind.Int:
            case FieldKind.Number:
            {
                bool whole = spec.Kind == FieldKind.Int;
                var number = new NumberBox
                {
                    DecimalPlaces = whole ? 0 : 2,
                    Minimum = (decimal)spec.Min,
                    Maximum = (decimal)spec.Max,
                    Increment = spec.Increment > 0 ? (decimal)spec.Increment : (whole ? 1m : 0.5m),
                    Width = 110,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(3, 2, 3, 2),
                    ThousandsSeparator = false
                };
                number.ValueChanged += (_, _) => CommitNumber(spec, number);
                return number;
            }
            case FieldKind.Choice:
            case FieldKind.EditableChoice:
            {
                var combo = new ThemedComboBox
                {
                    DropDownStyle = spec.Kind == FieldKind.Choice ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(3, 2, 3, 2),
                    DropDownWidth = 320
                };
                if (spec.Picture != null)
                {
                    Func<string, AssetPicture?> picture = spec.Picture;
                    combo.ItemImage = (text, size) => EditorAssets.Current.HasPictures ? picture(text)?.Thumb(size) ?? AssetPicture.Placeholder(size) : null;
                }
                if (spec.Kind == FieldKind.Choice) combo.SelectedIndexChanged += (_, _) => CommitText(spec, combo.Text);
                else
                {
                    combo.SelectionChangeCommitted += (_, _) => CommitText(spec, combo.Text);
                    combo.Validated += (_, _) => CommitText(spec, combo.Text);
                    combo.KeyDown += (_, e) => EnterCommits(e, () => CommitText(spec, combo.Text));
                }
                return combo;
            }
            case FieldKind.GunMounts:
            {
                var list = new GunMountList { Anchor = AnchorStyles.Left | AnchorStyles.Top };
                list.Changed += on => CommitGuns(spec, on);
                return list;
            }
            default:
            {
                bool multi = spec.Kind is FieldKind.MultiText or FieldKind.Positions;
                var text = new ThemedTextBox
                {
                    Dock = DockStyle.Fill,
                    Margin = new Padding(3, 2, 3, 2),
                    ReadOnly = spec.ReadOnly,
                    Multiline = multi,
                    AcceptsReturn = multi,
                    ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None,
                    Height = spec.Kind == FieldKind.Positions ? 84 : (multi ? 56 : 23)
                };
                if (multi) text.MinimumSize = new Size(60, text.Height);
                if (spec.Kind == FieldKind.Positions) text.Font = new Font(FontFamily.GenericMonospace, 9f);
                text.Validated += (_, _) => CommitTextBox(spec, text);
                if (!multi) text.KeyDown += (_, e) => EnterCommits(e, () => CommitTextBox(spec, text));
                return text;
            }
        }
    }

    private static void EnterCommits(KeyEventArgs e, Action commit)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;
        e.Handled = true;
        commit();
    }

    // ------------------------------------------------------------------ binding

    public void SetEnabled(bool enabled)
    {
        foreach (Row row in rows)
        {
            row.Editor.Enabled = enabled && !(row.Spec.ReadOnly && row.Spec.Kind != FieldKind.Bool && row.Editor is not TextBox);
            if (row.Caption != null) row.Caption.Enabled = enabled;
        }
    }

    // Shows the values of this object (null = nothing selected: the fields are disabled and empty).
    public void Bind(JsonObject? data)
    {
        Data = data;
        Refresh_();
    }

    // Reads the values again from the same object.
    public void RefreshValues() => Refresh_();

    private void Refresh_()
    {
        loading = true;
        try
        {
            SetEnabled(Data != null);
            foreach (Row row in rows) Load(row);
        }
        finally
        {
            loading = false;
        }
    }

    private void Load(Row row)
    {
        FieldSpec spec = row.Spec;
        JsonNode? node = Data == null ? null : Read(Data, spec.Key);
        switch (row.Editor)
        {
            case CheckBox box:
                box.Checked = node == null ? spec.DefaultBool : Json.AsBool(node, spec.DefaultBool);
                break;
            case NumberBox number:
            {
                double value = node == null ? spec.Default : Json.AsNumber(node, spec.Default);
                number.Value = (decimal)Math.Clamp(value, spec.Min, spec.Max);
                break;
            }
            case GunMountList guns:
                guns.Show(spec.Guns?.Invoke() ?? Array.Empty<SteambirdsAi.AiGunStats>(), Json.Ints(node).ToList());
                break;
            case ComboBox combo:
                LoadCombo(combo, spec, Json.AsString(node));
                break;
            case TextBox text:
                text.Text = TextOf(spec, node);
                break;
        }
    }

    private static void LoadCombo(ComboBox combo, FieldSpec spec, string current)
    {
        // Build the wanted list first (a set avoids the quadratic Items.Contains) and touch the combo only when it
        // differs: this runs on every change the canvas makes, and rebuilding long lists made painting freeze.
        var wanted = new List<string>();
        var seen = new HashSet<string>();
        if (spec.AllowEmpty || spec.Kind == FieldKind.EditableChoice)
        {
            wanted.Add("");
            seen.Add("");
        }
        IEnumerable<string> choices = spec.Choices?.Invoke() ?? Enumerable.Empty<string>();
        foreach (string choice in choices)
        {
            if (seen.Add(choice)) wanted.Add(choice);
        }
        if (current.Length > 0 && seen.Add(current)) wanted.Add(current);

        bool same = combo.Items.Count == wanted.Count;
        for (int i = 0; same && i < wanted.Count; i++) same = combo.Items[i] as string == wanted[i];
        if (!same)
        {
            combo.BeginUpdate();
            combo.Items.Clear();
            foreach (string item in wanted) combo.Items.Add(item);
            combo.EndUpdate();
        }

        if (spec.Kind == FieldKind.EditableChoice) combo.Text = current;
        else combo.SelectedItem = current.Length > 0 || seen.Contains("") ? current : null;
    }

    private static string TextOf(FieldSpec spec, JsonNode? node)
    {
        switch (spec.Kind)
        {
            case FieldKind.OptionalNumber:
                return node == null ? "" : Number(Json.AsNumber(node));
            case FieldKind.IntList:
                return string.Join(", ", Json.Ints(node).Select(i => i.ToString(CultureInfo.InvariantCulture)));
            case FieldKind.StringList:
                return string.Join(", ", Json.Strings(node));
            case FieldKind.Positions:
            {
                var text = new StringBuilder();
                foreach (JsonObject pos in Json.Objects(node as JsonArray))
                {
                    if (text.Length > 0) text.Append("\r\n");
                    text.Append(Number(Json.Num(pos, "u"))).Append(", ").Append(Number(Json.Num(pos, "v")));
                    if (!spec.NoRotation) text.Append(", ").Append(Number(Json.Num(pos, "rot", MapCoords.FaceMinusV)));
                }
                return text.ToString();
            }
            default:
                return Json.AsString(node);
        }
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ reading and writing the JSON

    private static JsonNode? Read(JsonObject data, string path)
    {
        JsonObject? parent = Resolve(data, path, false, out string leaf);
        return parent?[leaf];
    }

    private static JsonObject? Resolve(JsonObject data, string path, bool create, out string leaf)
    {
        string[] parts = path.Split('.');
        JsonObject current = data;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (current[parts[i]] is JsonObject child)
            {
                current = child;
                continue;
            }
            if (!create)
            {
                leaf = "";
                return null;
            }
            child = new JsonObject();
            current[parts[i]] = child;
            current = child;
        }
        leaf = parts[parts.Length - 1];
        return current;
    }

    // Removes the field and the objects around it that became empty.
    private static void Remove(JsonObject data, string path)
    {
        JsonObject? parent = Resolve(data, path, false, out string leaf);
        if (parent == null) return;
        parent.Remove(leaf);

        string[] parts = path.Split('.');
        for (int depth = parts.Length - 1; depth >= 1; depth--)
        {
            JsonObject holder = data;
            for (int i = 0; i < depth - 1; i++) holder = (JsonObject)holder[parts[i]]!;
            if (holder[parts[depth - 1]] is JsonObject inner && inner.Count == 0) holder.Remove(parts[depth - 1]);
            else break;
        }
    }

    private static void Write(JsonObject data, string path, JsonNode value)
    {
        JsonObject parent = Resolve(data, path, true, out string leaf)!;
        parent[leaf] = value;
    }

    // ------------------------------------------------------------------ commits

    private void Apply(FieldSpec spec, Action change)
    {
        if (loading || Data == null) return;
        beforeChange();
        change();
        afterChange(spec);
    }

    private void CommitBool(FieldSpec spec, CheckBox box)
    {
        if (loading || Data == null) return;
        JsonNode? old = Read(Data, spec.Key);
        bool current = old == null ? spec.DefaultBool : Json.AsBool(old, spec.DefaultBool);
        if (current == box.Checked && old != null) return;
        if (old == null && box.Checked == spec.DefaultBool) return;
        JsonObject data = Data;
        Apply(spec, () => Write(data, spec.Key, JsonValue.Create(box.Checked)!));
    }

    private void CommitNumber(FieldSpec spec, NumberBox number)
    {
        if (loading || Data == null) return;
        JsonObject data = Data;
        double value = (double)number.Value;
        JsonNode? old = Read(data, spec.Key);
        if (old != null && Math.Abs(Json.AsNumber(old) - value) < 1e-9) return;
        if (old == null && Math.Abs(spec.Default - value) < 1e-9) return;
        Apply(spec, () =>
        {
            if (spec.Kind == FieldKind.Int) Write(data, spec.Key, JsonValue.Create((int)Math.Round(value))!);
            else Write(data, spec.Key, JsonValue.Create(Json.Round(value))!);
        });
    }

    private void CommitGuns(FieldSpec spec, List<int> on)
    {
        if (loading || Data == null) return;
        JsonObject data = Data;
        List<int> old = Json.Ints(Read(data, spec.Key)).ToList();
        if (old.SequenceEqual(on)) return;
        Apply(spec, () =>
        {
            if (on.Count == 0) Remove(data, spec.Key);
            else Write(data, spec.Key, Json.IntArray(on));
        });
    }

    private void CommitText(FieldSpec spec, string text)
    {
        if (loading || Data == null) return;
        JsonObject data = Data;
        string value = text.Trim();
        string old = Json.AsString(Read(data, spec.Key));
        if (old == value) return;
        Apply(spec, () =>
        {
            if (value.Length == 0) Remove(data, spec.Key);
            else Write(data, spec.Key, JsonValue.Create(value)!);
            spec.OnChanged?.Invoke(data, old, value);
        });
        // The change may have written other fields (the display name follows the game unit): show them.
        if (spec.OnChanged != null) Refresh_();
    }

    private void CommitTextBox(FieldSpec spec, TextBox box)
    {
        if (loading || Data == null || spec.ReadOnly) return;
        JsonObject data = Data;
        string shown = box.Text;
        JsonNode? old = Read(data, spec.Key);
        if (TextOf(spec, old) == shown) return;

        switch (spec.Kind)
        {
            case FieldKind.OptionalNumber:
            {
                string trimmed = shown.Trim().Replace(',', '.');
                if (trimmed.Length == 0)
                {
                    Apply(spec, () => Remove(data, spec.Key));
                    break;
                }
                if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                    (old != null && Math.Abs(Json.AsNumber(old) - number) < 1e-9))
                {
                    box.Text = TextOf(spec, old);
                    break;
                }
                bool whole = number == Math.Floor(number) && Math.Abs(number) < 1e9;
                Apply(spec, () => Write(data, spec.Key, whole ? JsonValue.Create((long)number)! : JsonValue.Create(Json.Round(number))!));
                break;
            }
            case FieldKind.IntList:
            {
                var values = new List<int>();
                foreach (string part in shown.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) values.Add(value);
                }
                Apply(spec, () =>
                {
                    if (values.Count == 0) Remove(data, spec.Key);
                    else Write(data, spec.Key, Json.IntArray(values));
                });
                break;
            }
            case FieldKind.StringList:
            {
                List<string> values = shown.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                Apply(spec, () =>
                {
                    if (values.Count == 0) Remove(data, spec.Key);
                    else Write(data, spec.Key, Json.StringArray(values));
                });
                break;
            }
            case FieldKind.Positions:
            {
                JsonArray? oldArray = old as JsonArray;
                var positions = new JsonArray();
                int index = 0;
                foreach (string line in shown.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = line.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double u) ||
                        !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) continue;
                    double rot = MapCoords.FaceMinusV;
                    if (parts.Length >= 3 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double r)) rot = r;
                    else if (oldArray != null && index < oldArray.Count && oldArray[index] is JsonObject previous) rot = Json.Num(previous, "rot", rot);
                    if (spec.NoRotation) positions.Add(new JsonObject { ["u"] = Json.Round(u), ["v"] = Json.Round(v) });
                    else positions.Add(Json.NewPoint(u, v, rot));
                    index++;
                }
                Apply(spec, () => Write(data, spec.Key, positions));
                break;
            }
            default:
            {
                string value = shown.Trim();
                Apply(spec, () =>
                {
                    if (value.Length == 0) Remove(data, spec.Key);
                    else Write(data, spec.Key, JsonValue.Create(shown.TrimEnd())!);
                });
                break;
            }
        }
    }
}
