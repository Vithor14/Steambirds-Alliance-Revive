using System.Globalization;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// "Accounts" page of the Server tab: the accounts of this server, one per player (Steam account), in
// server\accounts\<key>\ (account.json + inventory.json). Each one can be edited (in its own window), get a new
// portrait or be removed while the server is stopped. The starting state of NEW accounts is on its own page
// (NewAccountsPage).
internal sealed class AccountsPage : UserControl
{
    private readonly LauncherContext context;
    private readonly ThemedListView summary;
    private readonly ThemedListView planes;
    private readonly Label accountStatus;
    private readonly Label serverNote;
    private readonly ThemedButton editButton;
    private readonly ThemedButton removeButton;
    private readonly ThemedButton folderButton;
    private readonly ThemedButton rerollButton;
    private readonly PortraitParts portraitParts = PortraitParts.Load();
    private readonly System.Windows.Forms.Timer watchTimer;
    private List<AccountStore.Summary> accounts = new List<AccountStore.Summary>();
    private DateTime loadedStamp = DateTime.MinValue;

    public AccountsPage(LauncherContext context)
    {
        this.context = context;

        summary = Ui.Grid((L.T("Player"), 170), (L.T("Steam ID"), 140), (L.T("Last played"), 120), (L.T("Pilot rank"), 80),
            (L.T("Planes"), 60), (L.T("Portrait"), 150), (L.T("Kills"), 60), (L.T("Bosses"), 60), (L.T("Planes lost"), 90), (L.T("Unlocks"), 70),
            (L.T("Finished dungeons"), 300));
        summary.SelectedIndexChanged += (_, _) => ShowSelected();
        summary.DoubleClick += (_, _) => EditAccount();
        planes = Ui.Grid((L.T("Plane"), 160), (L.T("Variant"), 100), (L.T("Level"), 60), (L.T("XP"), 80), (L.T("Plane id"), 230));

        accountStatus = Ui.Note("");
        editButton = Ui.Primary(L.T("Edit account..."), (_, _) => EditAccount());
        removeButton = Ui.Danger(L.T("Remove account"), (_, _) => RemoveAccount());
        rerollButton = Ui.Button(L.T("Reroll portrait"), (_, _) => RerollPortrait());
        folderButton = Ui.Button(L.T("Accounts folder"), (_, _) =>
        {
            Directory.CreateDirectory(AccountStore.AccountsFolder);
            Ui.OpenFolder(FindForm(), AccountStore.AccountsFolder);
        });

        FlowLayoutPanel buttons = Ui.WrapFlow(
            editButton, rerollButton, removeButton,
            Ui.Button(L.T("Refresh"), (_, _) => LoadAccounts(force: true)), folderButton,
            accountStatus);
        buttons.Dock = DockStyle.Top;

        serverNote = Ui.Wrapping("", 900, gray: false);
        serverNote.Dock = DockStyle.Top;
        Resize += (_, _) => serverNote.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 10), 0);

        Label planesTitle = Ui.Heading(L.T("Planes of the selected account"));
        planesTitle.Dock = DockStyle.Top;

        // Two lists share the height: the accounts above, the planes of the selected one below.
        var lists = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 52f));
        lists.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 48f));
        lists.Controls.Add(Ui.Framed(summary), 0, 0);
        lists.Controls.Add(planesTitle, 0, 1);
        lists.Controls.Add(Ui.Framed(planes), 0, 2);

        // Fill first; the Top controls added later end up above it, the last one at the very top.
        Controls.Add(lists);
        Controls.Add(serverNote);
        Controls.Add(buttons);

        context.ServerStateChanged += UpdateButtons;

        watchTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        watchTimer.Tick += (_, _) => LoadAccounts(force: false);
        watchTimer.Start();
        LoadAccounts(force: true);
    }

    // ------------------------------------------------------------------ the accounts

    private AccountStore.Summary? Selected =>
        summary.SelectedItems.Count > 0 && summary.SelectedItems[0].Tag is AccountStore.Summary chosen ? chosen : null;

    private void UpdateButtons()
    {
        bool serverRunning = context.Server.IsActive;
        bool selected = Selected != null;
        editButton.Enabled = selected && !serverRunning && Selected!.Error.Length == 0;
        removeButton.Enabled = selected && !serverRunning;
        rerollButton.Enabled = selected && !serverRunning && Selected!.Error.Length == 0 && portraitParts.Available;
        if (serverRunning)
        {
            serverNote.Text = L.T("Stop the server to edit or remove accounts: the server keeps the accounts of the players who joined in memory while it runs and would write over the changes.");
            Ui.SetRole(serverNote, TextRole.Warn);
        }
        else
        {
            serverNote.Text = L.T("One account per player (Steam account), saved in server\\accounts on this PC.");
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
                    account.SteamId.Length > 0 ? account.SteamId : account.Key,
                    account.LastSeen.HasValue ? account.LastSeen.Value.ToString("dd/MM/yyyy HH:mm") : "—",
                    pilot != null ? Number(pilot.Level) : "—",
                    data != null ? data.Planes.Count.ToString(CultureInfo.InvariantCulture) : "—",
                    pilot != null ? portraitParts.Describe(pilot.Portrait) : "—",
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
            accountStatus.Text = L.F("{0} account(s). Read at {1}.", accounts.Count, DateTime.Now.ToString("HH:mm:ss"));
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

    // New random portrait for the selected account, saved at once (copy in server\_backup).
    private void RerollPortrait()
    {
        AccountStore.Summary? chosen = Selected;
        if (chosen == null || !ServerStoppedOrWarn()) return;
        try
        {
            AccountStore store = AccountStore.Load(chosen.Folder);
            Dictionary<string, int>? rolled = portraitParts.Roll(new Random());
            if (rolled == null) return;
            store.Portrait = rolled;
            string backup = store.Save();
            context.ServerNotice(L.F("new portrait for {0}: {1}; previous copy in {2}", chosen.Name, portraitParts.Describe(rolled), backup));
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not save the account: {0}", e.Message));
        }
        LoadAccounts(force: true);
    }

    private void RemoveAccount()
    {
        AccountStore.Summary? chosen = Selected;
        if (chosen == null || !ServerStoppedOrWarn()) return;
        string question = L.F("Remove the account of {0}?\n\nIts pilot, planes, items and currencies are moved to server\\_backup. The player gets a new account the next time they join.", chosen.Name);
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
            watchTimer.Stop();
            watchTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}
