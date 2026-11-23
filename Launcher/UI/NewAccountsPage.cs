using System.Text.Json;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// "New accounts" page of the Server tab: the starting state of an account, the first time a player (Steam account)
// joins this server. Keys of progression.json and items.json the server already uses: the pilot rank, the unlocks
// (plane classes as pills), the ingredients and storage items, and the starting money (its own panel at the right).
internal sealed class NewAccountsPage : UserControl
{
    private readonly LauncherContext context;
    private readonly SettingsPage defaults;

    public NewAccountsPage(LauncherContext context)
    {
        this.context = context;

        var pilotSection = new SettingsSection(L.T("Pilot"), "progression.json", SettingsSection.Only("startingPilotLevel", "startingUnlocks"));
        pilotSection.CustomEditors["startingUnlocks"] = UnlockEditor;
        // The starting money has its own panel at the right (StartingMoneyPanel).
        var itemsSection = new SettingsSection(L.T("Ingredients and items"), "items.json",
            SettingsSection.Only("startingIngredients", "startingStorageItems"));
        defaults = new SettingsPage(context.ToolTip,
            L.T("Used for new accounts: the first time a player (Steam account) joins this server. Accounts that already exist are not changed. The other progression and item settings are in the Progression and Items and loot pages."),
            pilotSection, itemsSection)
        {
            Dock = DockStyle.Fill
        };

        var moneyCard = new Card { Title = L.T("Starting money of new accounts"), Dock = DockStyle.Fill, Margin = Padding.Empty };
        moneyCard.Controls.Add(new StartingMoneyPanel(context) { Dock = DockStyle.Fill });
        var right = new Panel { Dock = DockStyle.Right, Width = 340, Padding = new Padding(12, 0, 0, 0) };
        right.Controls.Add(moneyCard);

        Controls.Add(defaults);
        Controls.Add(right);

        context.GameDataChanged += OnGameDataChanged;
    }

    private SettingEditor? UnlockEditor(SettingsSection section, JsoncProperty property)
    {
        List<string>? values = ReadStringArray(property.RawValue);
        if (values == null) return null;
        var checklist = new UnlockChecklist(context.GameData) { Dock = DockStyle.Fill };
        checklist.SetValue(values);
        List<string> baseline = checklist.GetValue();
        SettingEditor editor = SettingEditor.Custom(section, property, checklist,
            () => ToJsonArray(checklist.GetValue()),
            () => !SameItems(checklist.GetValue(), baseline));
        checklist.Changed += editor.NotifyChanged;
        return editor;
    }

    private void OnGameDataChanged()
    {
        // The lists of classes and currencies come from the game: rebuild the editors (unless there are edits).
        if (!defaults.HasUnsavedChanges) defaults.Reload();
    }

    private static List<string>? ReadStringArray(string raw)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(raw, JsoncFile.ParseOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Array) return null;
            var result = new List<string>();
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) result.Add(item.GetString()!);
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ToJsonArray(List<string> values) =>
        values.Count == 0 ? "[]" : "[ " + string.Join(", ", values.Select(JsoncFile.FormatString)) + " ]";

    private static bool SameItems(List<string> a, List<string> b) =>
        a.Count == b.Count && a.All(x => b.Contains(x, StringComparer.OrdinalIgnoreCase));

    protected override void Dispose(bool disposing)
    {
        if (disposing) context.GameDataChanged -= OnGameDataChanged;
        base.Dispose(disposing);
    }
}
