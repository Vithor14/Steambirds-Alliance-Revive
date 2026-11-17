using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// A list of things of the map (rooms, unit types, gates, points...) with Add / Duplicate / Delete buttons and, below,
// the form of the selected one. What the list holds comes from an ItemSource.
internal sealed class CollectionPanel : Panel
{
    private readonly MapSession session;
    private readonly ItemSource source;
    private readonly string newName;
    private readonly Action<string, string>? fieldRenamed;
    private readonly ListBox list;
    private readonly Button addButton;
    private readonly Button duplicateButton;
    private readonly Button deleteButton;
    private readonly TextBox? nameBox;
    private readonly Button? renameButton;
    private bool loading;
    private bool programmatic;

    public CollectionPanel(MapSession session, ItemSource source, IEnumerable<FieldSpec> fields, string newName,
        int listHeight = 100, Action<string, string>? fieldRenamed = null, Control? extra = null, int formHeight = 0)
    {
        this.session = session;
        this.source = source;
        this.newName = newName;
        this.fieldRenamed = fieldRenamed;
        Dock = DockStyle.Fill;

        var table = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Margin = new Padding(0), Padding = new Padding(0) };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(table);

        list = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            Margin = new Padding(3, 3, 3, 0),
            HorizontalScrollbar = true
        };
        list.SelectedIndexChanged += (_, _) =>
        {
            if (loading) return;
            OnSelectionChanged();
            if (!programmatic) UserSelected?.Invoke(Current);
        };
        AddRow(table, list, SizeType.Absolute, listHeight);

        addButton = Ui.Button(L.T("Add"), (_, _) => AddItem());
        duplicateButton = Ui.Button(L.T("Duplicate"), (_, _) => DuplicateItem());
        deleteButton = Ui.Button(L.T("Delete"), (_, _) => DeleteItem());
        duplicateButton.Visible = source.CanDuplicate;
        FlowLayoutPanel buttons = Ui.Flow(addButton, duplicateButton, deleteButton);
        AddRow(table, buttons, SizeType.AutoSize, 0);

        if (source.NamedByKey)
        {
            nameBox = new TextBox { Width = 150, Margin = new Padding(3, 4, 3, 3) };
            nameBox.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                e.Handled = true;
                RenameItem();
            };
            renameButton = Ui.Button(L.T("Rename"), (_, _) => RenameItem());
            AddRow(table, Ui.Flow(Ui.Text(L.T("Name")), nameBox, renameButton), SizeType.AutoSize, 0);
        }

        PropertyForm = new JsonForm(fields, session.Checkpoint, FormChanged);
        if (extra != null)
        {
            AddRow(table, PropertyForm, formHeight > 0 ? SizeType.Absolute : SizeType.Percent, formHeight > 0 ? formHeight : 45);
            AddRow(table, extra, SizeType.Percent, 55);
        }
        else
        {
            AddRow(table, PropertyForm, SizeType.Percent, 100);
        }
        UpdateButtons();
    }

    public JsonForm PropertyForm { get; }

    // The selected entry (null when nothing is selected).
    public ListItem? Current => list.SelectedItem as ListItem;

    // The selected entry changed, for any reason (the form is bound to it, the pages that depend on it update).
    public event Action<ListItem?>? ItemSelected;

    // The user clicked another entry in the list (not a selection made by the program).
    public event Action<ListItem?>? UserSelected;

    // The user changed a field of the selected entry (after the map was marked as changed).
    public event Action<FieldSpec, ListItem>? FieldChanged;

    // An entry was added or deleted.
    public event Action? ListChanged;

    private static void AddRow(TableLayoutPanel table, Control control, SizeType type, float size)
    {
        int row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(type, size));
        control.Dock = control is FlowLayoutPanel ? DockStyle.None : DockStyle.Fill;
        table.Controls.Add(control, 0, row);
    }

    // ------------------------------------------------------------------ list

    // Reads the list again from the map and keeps the selection when the entry is still there.
    public void Reload(JsonObject? keepData = null, string? keepKey = null)
    {
        ListItem? before = Current;
        JsonObject? wantData = keepData ?? before?.Data;
        string? wantKey = keepKey ?? (before != null && before.Key.Length > 0 ? before.Key : null);
        int wantIndex = list.SelectedIndex;

        loading = true;
        try
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (ListItem item in source.Load()) list.Items.Add(item);
            list.EndUpdate();

            int found = -1;
            for (int i = 0; i < list.Items.Count && found < 0; i++)
            {
                var item = (ListItem)list.Items[i]!;
                if (wantKey != null && item.Key == wantKey) found = i;
                else if (wantKey == null && wantData != null && ReferenceEquals(item.Data, wantData)) found = i;
            }
            if (found < 0 && list.Items.Count > 0 && wantIndex >= 0) found = Math.Min(wantIndex, list.Items.Count - 1);
            if (found >= 0) list.SelectedIndex = found;
        }
        finally
        {
            loading = false;
        }
        OnSelectionChanged();
    }

    public bool SelectData(JsonObject? data)
    {
        if (data == null) return false;
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (ReferenceEquals(((ListItem)list.Items[i]!).Data, data))
            {
                SelectIndex(i);
                return true;
            }
        }
        return false;
    }

    public bool SelectKey(string key)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (((ListItem)list.Items[i]!).Key == key)
            {
                SelectIndex(i);
                return true;
            }
        }
        return false;
    }

    public bool SelectText(string text)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (((ListItem)list.Items[i]!).Text == text)
            {
                SelectIndex(i);
                return true;
            }
        }
        return false;
    }

    private void SelectIndex(int index)
    {
        programmatic = true;
        try
        {
            list.SelectedIndex = index;
        }
        finally
        {
            programmatic = false;
        }
    }

    private void OnSelectionChanged()
    {
        ListItem? item = Current;
        PropertyForm.Bind(item?.Data);
        if (nameBox != null) nameBox.Text = item?.Key ?? "";
        UpdateButtons();
        ItemSelected?.Invoke(item);
    }

    private void UpdateButtons()
    {
        bool has = Current != null;
        duplicateButton.Enabled = has;
        deleteButton.Enabled = has;
        if (nameBox != null) nameBox.Enabled = has;
        if (renameButton != null) renameButton.Enabled = has;
    }

    // ------------------------------------------------------------------ buttons

    private void AddItem()
    {
        session.Checkpoint();
        ListItem? item = source.Add(newName);
        if (item == null) return;
        session.Touch();
        Reload(item.Data, item.Key.Length > 0 ? item.Key : null);
        ListChanged?.Invoke();
    }

    private void DuplicateItem()
    {
        ListItem? current = Current;
        if (current == null) return;
        session.Checkpoint();
        ListItem? item = source.Duplicate(current);
        if (item == null) return;
        session.Touch();
        Reload(item.Data, item.Key.Length > 0 ? item.Key : null);
        ListChanged?.Invoke();
    }

    private void DeleteItem()
    {
        ListItem? current = Current;
        if (current == null) return;
        session.Checkpoint();
        source.Remove(current);
        session.Touch();
        Reload();
        ListChanged?.Invoke();
    }

    private void RenameItem()
    {
        ListItem? current = Current;
        if (current == null || nameBox == null) return;
        string wanted = nameBox.Text.Trim();
        if (wanted.Length == 0 || wanted == current.Key) return;
        session.Checkpoint();
        if (!source.Rename(current, wanted))
        {
            Ui.ShowError(this, L.F("The name \"{0}\" is already used or is not valid.", wanted));
            nameBox.Text = current.Key;
            return;
        }
        session.Touch();
        Reload(null, wanted);
        ListChanged?.Invoke();
    }

    // ------------------------------------------------------------------ form

    private void FormChanged(FieldSpec spec)
    {
        ListItem? item = Current;
        if (item == null) return;

        source.Commit(item);
        string oldText = item.Text;
        string newText = source.TextOf(item);
        if (newText != oldText)
        {
            item.Text = newText;
            int index = list.SelectedIndex;
            if (index >= 0)
            {
                loading = true;
                try
                {
                    list.Items[index] = item;
                    list.SelectedIndex = index;
                }
                finally
                {
                    loading = false;
                }
            }
            if (!source.NamedByKey && fieldRenamed != null && oldText.Length > 0 && !oldText.StartsWith("(", StringComparison.Ordinal))
                fieldRenamed(oldText, newText);
        }
        session.Touch();
        FieldChanged?.Invoke(spec, item);
    }
}
