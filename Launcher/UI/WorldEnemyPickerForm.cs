using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.UI;

// "Add enemy" of the Enemies tab of the open world: pick the units of the game (filter by name) and the group they go to.
// The group is one that exists in the biome, or a new one: leader and followers (a group of the game) or solo (a spawn site
// creates one enemy of the list alone).
internal sealed class WorldEnemyPickerForm : Form
{
    public const int NewGroup = -1;
    public const int NewSoloGroup = -2;

    private readonly ThemedTextBox filterBox;
    private readonly ThemedListView list;
    private readonly ThemedComboBox groupBox;
    private readonly ThemedComboBox roleBox;
    private readonly List<int> groupChoices = new List<int>();
    private readonly List<GameUnit> units;
    private readonly List<WorldEnemyGroup> groups;

    // What was picked (empty when the window was closed without a choice).
    public List<string> ChosenUnits { get; } = new List<string>();
    public int ChosenGroup { get; private set; } = NewGroup;
    // The unit goes in "leaders" (otherwise "members": followers, or the units of a solo group).
    public bool AsLeader { get; private set; }

    public WorldEnemyPickerForm(string biome, List<WorldEnemyGroup> groups, int selectedGroup)
    {
        this.groups = groups;
        units = EditorAssets.Current.Units.Values.Where(u => !u.Wall).OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).ToList();

        Text = L.F("Add enemies to {0}", biome);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        ClientSize = new Size(640, 560);
        MinimumSize = new Size(480, 360);
        Padding = new Padding(10);

        groupBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        for (int i = 0; i < groups.Count; i++)
        {
            groupChoices.Add(i);
            groupBox.Items.Add(GroupLabel(groups[i], i));
        }
        groupChoices.Add(NewGroup);
        groupBox.Items.Add(L.T("New group: a leader and followers"));
        groupChoices.Add(NewSoloGroup);
        groupBox.Items.Add(L.T("New group: solo (one enemy of the list)"));
        groupBox.SelectedIndex = selectedGroup >= 0 && selectedGroup < groups.Count ? selectedGroup : groups.Count;
        groupBox.SelectedIndexChanged += (_, _) => UpdateRole();

        roleBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        roleBox.Items.Add(L.T("Follower"));
        roleBox.Items.Add(L.T("Leader"));
        roleBox.SelectedIndex = 0;

        FlowLayoutPanel choices = Ui.Flow(Ui.Text(L.T("Group:")), groupBox, Ui.Text(L.T("As:")), roleBox);
        choices.Dock = DockStyle.Top;
        choices.WrapContents = true;

        filterBox = new ThemedTextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = units.Count > 0 ? L.T("Filter the units of the game...") : L.T("Type the name of the unit...")
        };
        filterBox.TextChanged += (_, _) => Fill();

        list = Ui.Grid((L.T("Unit"), 220), (L.T("Tier"), 50), (L.T("Health"), 80), (L.T("Armed"), 60));
        list.MultiSelect = true;
        list.DoubleClick += (_, _) => Pick();
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = true;
            Pick();
        };

        Label hint = Ui.Wrapping(units.Count > 0
            ? L.T("Pick one or more units (Ctrl or Shift) and press Add. A unit that does not fit the biome still works: the enemy is the game's unit with the default AI.")
            : L.T("The unit list of the game is not available (run SteambirdsGameFiles.exe once). You can still type the exact name of a unit and press Add."), 600, gray: true);
        hint.Dock = DockStyle.Bottom;

        ThemedButton add = Ui.Primary(L.T("Add"), (_, _) => Pick());
        ThemedButton close = Ui.Button(L.T("Cancel"), (_, _) => Close());
        FlowLayoutPanel buttons = Ui.Flow(add, close);
        buttons.Dock = DockStyle.Bottom;
        buttons.FlowDirection = FlowDirection.RightToLeft;
        CancelButton = close;

        var spacer1 = new Panel { Dock = DockStyle.Top, Height = 8 };
        var spacer2 = new Panel { Dock = DockStyle.Top, Height = 8 };
        Controls.Add(Ui.Framed(list));
        Controls.Add(hint);
        Controls.Add(buttons);
        Controls.Add(spacer2);
        Controls.Add(filterBox);
        Controls.Add(spacer1);
        Controls.Add(choices);
        Theme.AttachWindow(this);
        UpdateRole();
        Fill();
    }

    private static string GroupLabel(WorldEnemyGroup group, int index)
    {
        string name = group.Name.Length > 0 ? group.Name : L.F("group {0}", index + 1);
        return (index + 1) + ". " + name + (group.Solo ? " (" + L.T("solo") + ")" : "") + "  -  " + (group.Leaders.Count + group.Members.Count);
    }

    // A solo group has no leaders; a new solo group neither.
    private void UpdateRole()
    {
        int choice = groupChoices[Math.Max(0, groupBox.SelectedIndex)];
        bool solo = choice == NewSoloGroup || (choice >= 0 && groups[choice].Solo);
        roleBox.Enabled = !solo;
        if (solo) roleBox.SelectedIndex = 0;
    }

    private void Fill()
    {
        string filter = filterBox.Text.Trim();
        list.BeginUpdate();
        list.Items.Clear();
        foreach (GameUnit unit in units)
        {
            if (filter.Length > 0 && unit.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
            var item = new ListViewItem(unit.Name) { Tag = unit.Name };
            item.SubItems.Add(unit.Tier.ToString());
            item.SubItems.Add(unit.MaxHealth.ToString("0"));
            item.SubItems.Add(unit.Armed ? L.T("yes") : "");
            list.Items.Add(item);
        }
        list.EndUpdate();
    }

    private void Pick()
    {
        ChosenUnits.Clear();
        foreach (ListViewItem item in list.SelectedItems) ChosenUnits.Add((string)item.Tag!);
        if (ChosenUnits.Count == 0)
        {
            string typed = filterBox.Text.Trim();
            // Nothing picked: with no unit list the typed name is the unit; with one, a single match is.
            if (units.Count == 0 && typed.Length > 0) ChosenUnits.Add(typed);
            else if (list.Items.Count == 1) ChosenUnits.Add((string)list.Items[0].Tag!);
        }
        if (ChosenUnits.Count == 0)
        {
            Ui.ShowInfo(this, L.T("Pick at least one unit."));
            return;
        }
        ChosenGroup = groupChoices[Math.Max(0, groupBox.SelectedIndex)];
        AsLeader = roleBox.Enabled && roleBox.SelectedIndex == 1;
        DialogResult = DialogResult.OK;
        Close();
    }
}
