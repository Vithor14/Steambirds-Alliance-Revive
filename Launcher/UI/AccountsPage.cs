using System.Globalization;
using System.Text.Json;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// "Accounts" page of the Server tab.
//  - Starting state of new accounts: keys of progression.json and items.json the server already uses
//    (unlocks as a checklist, currencies as one field each).
//  - The accounts of this server: one per player (Steam account), in server\accounts\<key>\ (account.json +
//    inventory.json), plus the old single account server\account.json until the host logs in. Each one can be
//    edited (in its own window) or removed while the server is stopped.
internal sealed class AccountsPage : UserControl
{
    private readonly LauncherContext context;
    private readonly SettingsPage newAccounts;
    private readonly AccountMoneyPanel accountMoney;
    private readonly ListView summary;
    private readonly ListView planes;
    private readonly Label accountStatus;
    private readonly Label serverNote;
    private readonly Button editButton;
    private readonly Button removeButton;
    private readonly Button folderButton;
    private readonly System.Windows.Forms.Timer watchTimer;
    private List<AccountStore.Summary> accounts = new List<AccountStore.Summary>();
    private DateTime loadedStamp = DateTime.MinValue;

    public AccountsPage(LauncherContext context)
    {
        this.context = context;
        ToolTip tip = context.ToolTip;

        var pilotSection = new SettingsSection(L.T("Pilot"), "progression.json", SettingsSection.Only("startingPilotLevel", "startingUnlocks"));
        pilotSection.CustomEditors["startingUnlocks"] = UnlockEditor;
        // The starting money has its own panel at the right (StartingMoneyPanel).
        var itemsSection = new SettingsSection(L.T("Ingredients and items"), "items.json",
            SettingsSection.Only("startingIngredients", "startingStorageItems"));
        newAccounts = new SettingsPage(tip,
            L.T("Used for new accounts: the first time a player (Steam account) joins this server. The other progression and item settings are in the Progression and Items and loot tabs."),
            pilotSection, itemsSection)
        {
            Dock = DockStyle.Fill
        };

        summary = Ui.Grid((L.T("Player"), 170), (L.T("Steam ID"), 140), (L.T("Last played"), 120), (L.T("Pilot rank"), 80),
            (L.T("Planes"), 60), (L.T("Kills"), 60), (L.T("Bosses"), 60), (L.T("Planes lost"), 90), (L.T("Unlocks"), 70),
            (L.T("Finished dungeons"), 300));
        summary.Dock = DockStyle.Top;
        summary.Height = 130;
        summary.SelectedIndexChanged += (_, _) => ShowSelected();
        summary.DoubleClick += (_, _) => EditAccount();
        planes = Ui.Grid((L.T("Plane"), 160), (L.T("Variant"), 100), (L.T("Level"), 60), (L.T("XP"), 80), (L.T("Plane id"), 230));

        accountMoney = new AccountMoneyPanel(context) { Dock = DockStyle.Right, Width = 300, Padding = new Padding(8, 0, 0, 0) };
        accountStatus = Ui.Note("");
        editButton = Ui.Button(L.T("Edit account..."), (_, _) => EditAccount());
        removeButton = Ui.Button(L.T("Remove account"), (_, _) => RemoveAccount());
        folderButton = Ui.Button(L.T("Accounts folder"), (_, _) =>
        {
            Directory.CreateDirectory(AccountStore.AccountsFolder);
            Ui.OpenFolder(FindForm(), AccountStore.AccountsFolder);
        });

        var buttons = Ui.WrapFlow(
            Ui.Button(L.T("Refresh"), (_, _) => LoadAccounts(force: true)),
            editButton, removeButton, folderButton,
            accountStatus);
        buttons.Dock = DockStyle.Top;

        serverNote = Ui.Wrapping("", 900, gray: false);
        serverNote.Dock = DockStyle.Top;

        // Fill first; the Top controls added later end up above it, the last one at the very top.
        Label planesTitle = Ui.Bold(L.T("Planes of the selected account"));
        planesTitle.Dock = DockStyle.Top;
        planesTitle.Padding = new Padding(0, 6, 0, 2);
        var accountsPanel = new Panel { Dock = DockStyle.Fill };
        accountsPanel.Resize += (_, _) => serverNote.MaximumSize = new Size(Math.Max(200, accountsPanel.ClientSize.Width - 10), 0);
        accountsPanel.Controls.Add(planes);
        accountsPanel.Controls.Add(accountMoney);
        accountsPanel.Controls.Add(planesTitle);
        accountsPanel.Controls.Add(summary);
        accountsPanel.Controls.Add(serverNote);
        accountsPanel.Controls.Add(buttons);

        var accountsGroup = new GroupBox { Text = L.T("Accounts of this server"), Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6) };
        accountsGroup.Controls.Add(accountsPanel);
        var newAccountsGroup = new GroupBox { Text = L.T("Starting state of new accounts"), Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6) };
        newAccountsGroup.Controls.Add(newAccounts);
        var startingMoneyGroup = new GroupBox { Text = L.T("Starting money of new accounts"), Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6) };
        startingMoneyGroup.Controls.Add(new StartingMoneyPanel(context) { Dock = DockStyle.Fill });
        var topRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360f));
        topRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        topRow.Controls.Add(newAccountsGroup, 0, 0);
        topRow.Controls.Add(startingMoneyGroup, 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));
        root.Controls.Add(topRow, 0, 0);
        root.Controls.Add(accountsGroup, 0, 1);
        Controls.Add(root);

        context.ServerStateChanged += UpdateButtons;
        context.GameDataChanged += OnGameDataChanged;

        watchTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        watchTimer.Tick += (_, _) => LoadAccounts(force: false);
        watchTimer.Start();
        LoadAccounts(force: true);
    }

    // ------------------------------------------------------------------ editors of the starting state

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
        if (!newAccounts.HasUnsavedChanges) newAccounts.Reload();
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

    // ------------------------------------------------------------------ the accounts

    private AccountStore.Summary? Selected =>
        summary.SelectedItems.Count > 0 && summary.SelectedItems[0].Tag is AccountStore.Summary chosen ? chosen : null;

    private void UpdateButtons()
    {
        bool serverRunning = context.Server.IsActive;
        bool selected = Selected != null;
        editButton.Enabled = selected && !serverRunning && Selected!.Error.Length == 0;
        removeButton.Enabled = selected && !serverRunning;
        if (serverRunning)
        {
            serverNote.Text = L.T("Stop the server to edit or remove accounts: the server keeps the accounts of the players who joined in memory while it runs and would write over the changes.");
            Ui.SetRole(serverNote, TextRole.Warn);
        }
        else
        {
            serverNote.Text = accounts.Any(a => a.IsLegacy)
                ? L.T("One account per player (Steam account). The old account of the server (account.json) goes to the first player who joins from this PC.")
                : L.T("One account per player (Steam account), saved in server\\accounts on this PC.");
            Ui.SetRole(serverNote, TextRole.Muted);
        }
    }

    // Reads the accounts again when a file changed (the server saves them while the players play).
    private void LoadAccounts(bool force)
    {
        UpdateButtons();
        DateTime stamp = AccountStore.LatestChange();
        if (!force && stamp == loadedStamp) return;
        loadedStamp = stamp;

        string? selectedFolder = Selected?.Folder;
        summary.BeginUpdate();
        try
        {
            summary.Items.Clear();
            accounts = AccountStore.List();
            if (accounts.Count == 0)
            {
                accountStatus.Text = L.T("No accounts yet: each player gets one the first time they join the server.");
                return;
            }

            foreach (AccountStore.Summary account in accounts)
            {
                AccountFile.Account? data = account.Data;
                AccountFile.Pilot? pilot = data?.Pilot;
                var item = new ListViewItem(new[]
                {
                    account.Name,
                    account.SteamId.Length > 0 ? account.SteamId : (account.IsLegacy ? "—" : account.Key),
                    account.LastSeen.HasValue ? account.LastSeen.Value.ToString("dd/MM/yyyy HH:mm") : "—",
                    pilot != null ? Number(pilot.Level) : "—",
                    data != null ? data.Planes.Count.ToString(CultureInfo.InvariantCulture) : "—",
                    pilot != null ? Number(pilot.Kills) : "—",
                    pilot != null ? Number(pilot.BossKills) : "—",
                    pilot != null ? Number(pilot.PlanesLost) : "—",
                    pilot != null ? pilot.Unlocks.Count.ToString(CultureInfo.InvariantCulture) : "—",
                    account.Error.Length > 0 ? L.F("Could not read account.json: {0}", account.Error)
                        : data == null || data.FinishedDungeons.Count == 0 ? L.T("none") : string.Join(", ", data.FinishedDungeons)
                })
                {
                    Tag = account
                };
                summary.Items.Add(item);
            }

            ListViewItem? again = summary.Items.Cast<ListViewItem>()
                .FirstOrDefault(i => i.Tag is AccountStore.Summary a && string.Equals(a.Folder, selectedFolder, StringComparison.OrdinalIgnoreCase));
            (again ?? summary.Items[0]).Selected = true;
            accountStatus.Text = L.F("{0} account(s). Read at {1}.", accounts.Count(a => !a.IsLegacy), DateTime.Now.ToString("HH:mm:ss"));
        }
        catch (Exception e)
        {
            accountStatus.Text = L.F("Could not read the accounts: {0}", e.Message);
        }
        finally
        {
            summary.EndUpdate();
            ShowSelected();
        }
    }

    private void ShowSelected()
    {
        planes.BeginUpdate();
        try
        {
            planes.Items.Clear();
            AccountFile.Account? data = Selected?.Data;
            if (data == null) return;
            foreach (AccountFile.Plane plane in data.Planes)
            {
                string name = context.GameData.FindClass(plane.Class)?.ToString() ?? plane.Class;
                planes.Items.Add(new ListViewItem(new[] { name, plane.Variant, Number(plane.Level), Number(plane.Xp), plane.CharacterId }));
            }
        }
        finally
        {
            planes.EndUpdate();
            UpdateButtons();
            accountMoney.Show(Selected);
        }
    }

    private static string Number(double value) => value.ToString("0", CultureInfo.InvariantCulture);

    private bool ServerStoppedOrWarn()
    {
        if (!context.Server.IsActive) return true;
        Ui.ShowError(FindForm(), L.T("Stop the server to edit or remove accounts: the server keeps the accounts of the players who joined in memory while it runs and would write over the changes."));
        return false;
    }

    private void EditAccount()
    {
        AccountStore.Summary? chosen = Selected;
        if (chosen == null || !ServerStoppedOrWarn()) return;
        AccountStore store;
        try
        {
            store = AccountStore.Load(chosen.Folder);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not read the account: {0}", e.Message));
            return;
        }

        using var editor = new AccountEditorForm(context, store);
        if (editor.ShowDialog(FindForm()) == DialogResult.OK) LoadAccounts(force: true);
    }

    private void RemoveAccount()
    {
        AccountStore.Summary? chosen = Selected;
        if (chosen == null || !ServerStoppedOrWarn()) return;
        string question = chosen.IsLegacy
            ? L.T("Remove the old account of the server?\n\naccount.json and inventory.json (pilot, planes, items, currencies) are moved to server\\_backup.")
            : L.F("Remove the account of {0}?\n\nIts pilot, planes, items and currencies are moved to server\\_backup. The player gets a new account the next time they join.", chosen.Name);
        if (!Ui.Confirm(FindForm(), question)) return;
        try
        {
            string backup = AccountStore.Delete(chosen.Folder);
            context.ServerNotice(L.F("account of {0} removed; copy in {1}", chosen.Name, backup));
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not remove the account: {0}", e.Message));
        }
        LoadAccounts(force: true);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            context.ServerStateChanged -= UpdateButtons;
            context.GameDataChanged -= OnGameDataChanged;
            watchTimer.Stop();
            watchTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
