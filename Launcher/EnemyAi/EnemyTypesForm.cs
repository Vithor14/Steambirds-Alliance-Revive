using SteambirdsAi;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// Adds and edits a type of enemies.json (the name /enemy uses), including which AI it runs and its death unit.
// The AI itself stays a file of the Enemy AI tab. Nothing here is a fight hardcoded into the game.
internal sealed class EnemyTypesForm : Form
{
    private readonly ListBox list = new ListBox { IntegralHeight = false, Dock = DockStyle.Fill };
    private readonly ThemedTextBox idBox = new ThemedTextBox { Width = 180 };
    private readonly ThemedTextBox nameBox = new ThemedTextBox { Width = 180 };
    private readonly ThemedTextBox templateBox = new ThemedTextBox { Width = 180 };
    private readonly ComboBox aiBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 180 };
    private readonly ThemedTextBox deathBox = new ThemedTextBox { Width = 180 };
    private readonly CheckBox bossBox = new CheckBox { AutoSize = true };
    private readonly List<EnemyTypeFile.Row> rows = new List<EnemyTypeFile.Row>();
    private bool filling;

    public EnemyTypesForm(IEnumerable<string> aiIds)
    {
        Text = L.T("Enemy types");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(640, 420);
        MinimizeBox = false;
        MaximizeBox = false;
        Font = SystemFonts.MessageBoxFont;

        aiBox.Items.Add("");
        foreach (string id in aiIds) aiBox.Items.Add(id);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(Ui.Primary(L.T("Close"), (_, _) => Close()));
        buttons.Controls.Add(Ui.Danger(L.T("Delete"), (_, _) => DeleteRow()));
        buttons.Controls.Add(Ui.Button(L.T("New"), (_, _) => NewRow()));
        buttons.Controls.Add(Ui.Primary(L.T("Save"), (_, _) => SaveRow()));

        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8, 0, 0, 0)
        };
        fields.Controls.Add(Ui.Bold(L.T("Enemy type")));
        fields.Controls.Add(Line(L.T("Id"), idBox));
        fields.Controls.Add(Line(L.T("Name"), nameBox));
        fields.Controls.Add(Line(L.T("Template"), templateBox));
        fields.Controls.Add(Line(L.T("AI"), aiBox));
        fields.Controls.Add(Line(L.T("Death unit"), deathBox));
        bossBox.Text = L.T("Boss");
        fields.Controls.Add(bossBox);
        fields.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            Text = L.T("The id is what /enemy uses. AI is a file from the Enemy AI tab. Death unit is the game unit left behind when this one dies (ufoDEATH for the Ambassador). Empty AI means the unit keeps the AI its folder gives it.")
        });

        root.Controls.Add(list, 0, 0);
        root.Controls.Add(fields, 1, 0);
        root.Controls.Add(buttons, 0, 1);
        root.SetColumnSpan(buttons, 2);
        Controls.Add(root);

        list.SelectedIndexChanged += (_, _) => ShowSelected();
        LoadRows();
    }

    private static Control Line(string label, Control box)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Width = 90, Padding = new Padding(0, 6, 0, 0) });
        row.Controls.Add(box);
        return row;
    }

    private void LoadRows()
    {
        rows.Clear();
        rows.AddRange(EnemyTypeFile.Read());
        list.Items.Clear();
        foreach (EnemyTypeFile.Row row in rows) list.Items.Add(row.Id);
        if (list.Items.Count > 0) list.SelectedIndex = 0;
    }

    private void ShowSelected()
    {
        filling = true;
        EnemyTypeFile.Row? row = Selected();
        idBox.Text = row?.Id ?? "";
        idBox.ReadOnly = row != null;
        nameBox.Text = row?.Name ?? "";
        templateBox.Text = row?.Template ?? "";
        aiBox.Text = row?.Ai ?? "";
        deathBox.Text = row?.DeathUnit ?? "";
        bossBox.Checked = row?.Boss ?? false;
        filling = false;
    }

    private EnemyTypeFile.Row? Selected() =>
        list.SelectedIndex >= 0 && list.SelectedIndex < rows.Count ? rows[list.SelectedIndex] : null;

    private void NewRow()
    {
        idBox.ReadOnly = false;
        idBox.Text = "";
        nameBox.Text = "";
        templateBox.Text = "auto:weak";
        aiBox.Text = "";
        deathBox.Text = "";
        bossBox.Checked = false;
        list.ClearSelected();
    }

    private void SaveRow()
    {
        if (filling) return;
        string id = AiCatalog.CleanId(idBox.Text);
        if (id.Length == 0)
        {
            Ui.ShowError(this, L.T("The id needs a letter or a digit."));
            return;
        }
        var row = new EnemyTypeFile.Row
        {
            Id = id,
            Name = nameBox.Text.Trim(),
            Template = templateBox.Text.Trim(),
            Ai = aiBox.Text.Trim(),
            DeathUnit = deathBox.Text.Trim(),
            Boss = bossBox.Checked
        };
        try
        {
            EnemyTypeFile.Save(row);
            LoadRows();
            int index = rows.FindIndex(r => r.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) list.SelectedIndex = index;
        }
        catch (Exception e)
        {
            Ui.ShowError(this, e.Message);
        }
    }

    private void DeleteRow()
    {
        EnemyTypeFile.Row? row = Selected();
        if (row == null) return;
        if (!Ui.Confirm(this, L.F("Delete the enemy type \"{0}\"?", row.Id))) return;
        try
        {
            EnemyTypeFile.Delete(row.Id);
            LoadRows();
        }
        catch (Exception e)
        {
            Ui.ShowError(this, e.Message);
        }
    }
}
