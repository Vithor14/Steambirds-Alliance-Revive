using System.Globalization;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Core.GameData;

namespace SteambirdsLauncher.UI;

// Checklist of unlock items: one line per plane class of the game (its unlock item, like "playerclass_medic"),
// plus a text field for other unlock items. Used for the starting unlocks and in the account editor.
internal sealed class UnlockChecklist : UserControl
{
    private sealed class Entry
    {
        public Entry(string id, string display)
        {
            Id = id;
            Display = display;
        }

        public string Id { get; }
        public string Display { get; }
        public override string ToString() => Display;
    }

    private readonly GameConfig data;
    private readonly CheckedListBox list;
    private readonly TextBox others;
    private bool loading;

    public event Action? Changed;

    public UnlockChecklist(GameConfig data)
    {
        this.data = data;
        list = new CheckedListBox
        {
            CheckOnClick = true,
            MultiColumn = true,
            ColumnWidth = 250,
            IntegralHeight = false,
            Dock = DockStyle.Top,
            BorderStyle = BorderStyle.FixedSingle
        };
        foreach (GameConfig.PlaneClass planeClass in data.Classes)
        {
            string display = planeClass + " — " + planeClass.UnlockItem + (planeClass.PreRelease ? " " + L.T("(pre-release)") : "");
            list.Items.Add(new Entry(planeClass.UnlockItem, display));
        }
        int rows = Math.Max(1, (int)Math.Ceiling(list.Items.Count / 3.0));
        list.Height = rows * list.ItemHeight + 6;
        list.ItemCheck += (_, _) =>
        {
            // ItemCheck comes before the check changes: tell the owner after it.
            if (!loading) BeginInvoke(new Action(() => Changed?.Invoke()));
        };

        others = new TextBox { Dock = DockStyle.Top, Margin = new Padding(3) };
        others.TextChanged += (_, _) =>
        {
            if (!loading) Changed?.Invoke();
        };
        Label othersLabel = Ui.Note(L.T("Other unlock items (separated by commas):"));
        othersLabel.Dock = DockStyle.Top;

        Controls.Add(others);
        Controls.Add(othersLabel);
        Controls.Add(list);
        Height = list.Height + othersLabel.PreferredHeight + others.PreferredHeight + 16;
        MinimumSize = new Size(300, Height);
    }

    public void SetValue(IEnumerable<string> values)
    {
        loading = true;
        try
        {
            var wanted = new HashSet<string>(values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => data.UnlockItemOf(v.Trim())),
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < list.Items.Count; i++)
            {
                var entry = (Entry)list.Items[i];
                bool on = wanted.Remove(entry.Id);
                list.SetItemChecked(i, on);
            }
            others.Text = string.Join(", ", wanted);
        }
        finally
        {
            loading = false;
        }
    }

    public List<string> GetValue()
    {
        var result = new List<string>();
        foreach (object item in list.CheckedItems)
        {
            if (item is Entry entry && !result.Contains(entry.Id)) result.Add(entry.Id);
        }
        foreach (string other in others.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string id = other.Trim().Trim('"');
            if (id.Length > 0 && !result.Contains(id, StringComparer.OrdinalIgnoreCase)) result.Add(id);
        }
        return result;
    }
}

// One number field per currency of the game (from the game's store config; "newscrap" is the recycling currency).
// Used for the starting currencies and in the account editor.
internal sealed class CurrencyFields : UserControl
{
    private readonly TableLayoutPanel table;
    private readonly Dictionary<string, NumericUpDown> fields = new Dictionary<string, NumericUpDown>(StringComparer.Ordinal);
    private bool loading;

    public event Action? Changed;

    public CurrencyFields(IEnumerable<string> currencies)
    {
        table = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        foreach (string currency in currencies) AddField(currency);
        Controls.Add(table);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    private NumericUpDown AddField(string currency)
    {
        if (fields.TryGetValue(currency, out NumericUpDown? existing)) return existing;
        var field = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 1_000_000_000,
            Width = 120,
            ThousandsSeparator = true,
            TextAlign = HorizontalAlignment.Right,
            Margin = new Padding(3)
        };
        field.ValueChanged += (_, _) =>
        {
            if (!loading) Changed?.Invoke();
        };
        Ui.AddRow(table, Ui.Text(currency), field);
        fields[currency] = field;
        return field;
    }

    public void SetValue(Dictionary<string, int> values)
    {
        loading = true;
        try
        {
            foreach (NumericUpDown field in fields.Values) field.Value = 0;
            foreach (KeyValuePair<string, int> pair in values)
            {
                NumericUpDown field = AddField(pair.Key);
                field.Value = Math.Clamp(pair.Value, (int)field.Minimum, (int)field.Maximum);
            }
        }
        finally
        {
            loading = false;
        }
    }

    // Only the currencies with an amount.
    public Dictionary<string, int> GetValue()
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, NumericUpDown> pair in fields)
        {
            int amount = (int)pair.Value.Value;
            if (amount != 0) result[pair.Key] = amount;
        }
        return result;
    }

    // JSON object for the config file: { "newscrap": 100 }.
    public static string ToJson(Dictionary<string, int> values)
    {
        if (values.Count == 0) return "{}";
        return "{ " + string.Join(", ", values.Select(p => JsoncFile.FormatString(p.Key) + ": " + p.Value.ToString(CultureInfo.InvariantCulture))) + " }";
    }
}
