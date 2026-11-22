using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Editor;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.EnemyAi;

// A list of JSON objects of an array (the states of a brain, the transitions of a state, the conditions of a transition)
// with Add / Duplicate / Delete / Up / Down. The text of each line comes from "describe".
internal sealed class ListEditor : Panel
{
    private readonly ListBox list;
    private readonly Button addButton;
    private readonly Button duplicateButton;
    private readonly Button deleteButton;
    private readonly Button upButton;
    private readonly Button downButton;
    private readonly Func<JsonObject> newItem;
    private readonly Func<JsonObject, string> describe;
    private JsonArray? array;
    private Func<JsonArray>? create;
    private bool loading;

    public ListEditor(Func<JsonObject> newItem, Func<JsonObject, string> describe, int height, bool canDuplicate = true)
    {
        this.newItem = newItem;
        this.describe = describe;
        Height = height;
        Dock = DockStyle.Fill;
        Margin = new Padding(0, 0, 0, 4);

        list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        list.SelectedIndexChanged += (_, _) =>
        {
            if (!loading) SelectionChanged?.Invoke(Current);
            UpdateButtons();
        };

        addButton = Ui.Button(L.T("Add"), (_, _) => Add());
        duplicateButton = Ui.Button(L.T("Duplicate"), (_, _) => Duplicate());
        deleteButton = Ui.Button(L.T("Delete"), (_, _) => Delete());
        upButton = Ui.Button("▲", (_, _) => MoveItem(-1));
        downButton = Ui.Button("▼", (_, _) => MoveItem(1));
        duplicateButton.Visible = canDuplicate;
        var buttons = Ui.Flow(addButton, duplicateButton, deleteButton, upButton, downButton);
        buttons.Dock = DockStyle.Bottom;

        Controls.Add(list);
        Controls.Add(buttons);
        UpdateButtons();
    }

    // Something was added, removed, moved or its text changed.
    public event Action? Edited;
    public event Action<JsonObject?>? SelectionChanged;

    public JsonObject? Current => list.SelectedIndex >= 0 && array != null && list.SelectedIndex < array.Count ? array[list.SelectedIndex] as JsonObject : null;
    public int Count => array?.Count ?? 0;

    // "create" makes the array in its parent when the first item is added (the array may not exist in the file yet).
    public void Bind(JsonArray? items, Func<JsonArray>? create, int select = -1)
    {
        array = items;
        this.create = create;
        Fill(select);
        SelectionChanged?.Invoke(Current);
    }

    public void Unbind() => Bind(null, null);

    // The text of the lines again (a value changed).
    public void RefreshText()
    {
        int at = list.SelectedIndex;
        Fill(at);
    }

    private void Fill(int select)
    {
        loading = true;
        try
        {
            list.Items.Clear();
            if (array != null)
            {
                foreach (JsonObject item in Json.Objects(array)) list.Items.Add(describe(item));
            }
            if (list.Items.Count > 0) list.SelectedIndex = Math.Clamp(select < 0 ? 0 : select, 0, list.Items.Count - 1);
        }
        finally
        {
            loading = false;
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool bound = array != null || create != null;
        bool any = Current != null;
        addButton.Enabled = bound;
        duplicateButton.Enabled = any;
        deleteButton.Enabled = any;
        upButton.Enabled = any && list.SelectedIndex > 0;
        downButton.Enabled = any && list.SelectedIndex < list.Items.Count - 1;
    }

    private JsonArray Ensure()
    {
        if (array == null && create != null) array = create();
        return array ?? new JsonArray();
    }

    private void Add()
    {
        JsonArray target = Ensure();
        target.Add(newItem());
        Fill(target.Count - 1);
        SelectionChanged?.Invoke(Current);
        Edited?.Invoke();
    }

    private void Duplicate()
    {
        if (array == null || Current == null) return;
        int at = list.SelectedIndex;
        array.Insert(at + 1, Current.DeepClone());
        Fill(at + 1);
        SelectionChanged?.Invoke(Current);
        Edited?.Invoke();
    }

    private void Delete()
    {
        if (array == null || Current == null) return;
        int at = list.SelectedIndex;
        array.RemoveAt(at);
        Fill(Math.Min(at, array.Count - 1));
        SelectionChanged?.Invoke(Current);
        Edited?.Invoke();
    }

    private void MoveItem(int delta)
    {
        if (array == null || Current == null) return;
        int at = list.SelectedIndex;
        int to = at + delta;
        if (to < 0 || to >= array.Count) return;
        JsonNode? node = array[at];
        array.RemoveAt(at);
        array.Insert(to, node);
        Fill(to);
        Edited?.Invoke();
    }
}
