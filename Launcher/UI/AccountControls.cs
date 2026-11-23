using System.Globalization;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Core.GameData;

namespace SteambirdsLauncher.UI;

// Unlock items as compact pills: one per plane class of the game (its unlock item, like "playerclass_medic"), with
// "All" and "None" shortcuts, plus a text field for other unlock items. Used for the starting unlocks and in the
// account editor.
internal sealed class UnlockChecklist : UserControl
{
    private readonly GameConfig data;
    private readonly ChipList chips;
    private readonly ThemedTextBox others;
    private readonly Label count;
    private bool loading;

    public event Action? Changed;

    public UnlockChecklist(GameConfig data)
    {
        this.data = data;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(300, 0);

        chips = new ChipList { Dock = DockStyle.Top, Margin = Padding.Empty };
        foreach (GameConfig.PlaneClass planeClass in data.Classes)
        {
            string name = planeClass.Name.Length > 0 ? planeClass.Name : planeClass.Id;
            if (planeClass.PreRelease) name += " " + L.T("(pre-release)");
            chips.Add(new ChipList.Chip(planeClass.UnlockItem, name, planeClass.UnlockItem));
        }
        chips.Changed += () =>
        {
            UpdateCount();
            if (!loading) Changed?.Invoke();
        };

        count = Ui.Note("");
        count.Margin = new Padding(3, 8, 12, 3);
        ThemedButton all = Ui.Quiet(L.T("All"), (_, _) => chips.SetAll(true));
        ThemedButton none = Ui.Quiet(L.T("None"), (_, _) => chips.SetAll(false));
        FlowLayoutPanel header = Ui.Flow(count, all, none);
        header.Dock = DockStyle.Top;
        header.Padding = new Padding(0, 0, 0, 6);

        others = new ThemedTextBox { Dock = DockStyle.Top, Margin = new Padding(3) };
        others.TextChanged += (_, _) =>
        {
            if (!loading) Changed?.Invoke();
        };
        Label othersLabel = Ui.Note(L.T("Other unlock items (separated by commas):"));
        othersLabel.Dock = DockStyle.Top;
        othersLabel.Padding = new Padding(0, 10, 0, 2);

        // Docked from the bottom to the top: the last control added is the first one at the top.
        Controls.Add(others);
        Controls.Add(othersLabel);
        Controls.Add(chips);
        Controls.Add(header);
        UpdateCount();
    }

    private void UpdateCount() => count.Text = L.F("{0} of {1} unlocked", chips.CheckedCount, chips.Chips.Count);

    public void SetValue(IEnumerable<string> values)
    {
        loading = true;
        try
        {
            var wanted = new HashSet<string>(values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => data.UnlockItemOf(v.Trim())),
                StringComparer.OrdinalIgnoreCase);
            foreach (ChipList.Chip chip in chips.Chips) chips.SetChecked(chip.Id, wanted.Remove(chip.Id));
            others.Text = string.Join(", ", wanted);
            UpdateCount();
        }
        finally
        {
            loading = false;
        }
    }

    public List<string> GetValue()
    {
        var result = new List<string>();
        foreach (ChipList.Chip chip in chips.Chips)
        {
            if (chip.Checked && !result.Contains(chip.Id)) result.Add(chip.Id);
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
    private readonly Dictionary<string, NumberBox> fields = new Dictionary<string, NumberBox>(StringComparer.Ordinal);
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

    private NumberBox AddField(string currency)
    {
        if (fields.TryGetValue(currency, out NumberBox? existing)) return existing;
        var field = new NumberBox
        {
            CommitWhileTyping = true,
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
            foreach (NumberBox field in fields.Values) field.Value = 0;
            foreach (KeyValuePair<string, int> pair in values)
            {
                NumberBox field = AddField(pair.Key);
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
        foreach (KeyValuePair<string, NumberBox> pair in fields)
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
