using System.Text.Json;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// Money on the Accounts page, two panels:
//  - StartingMoneyPanel: what every NEW account starts with (items.json "startingCurrencies": gold, iron, recycling scrap...).
//  - AccountMoneyPanel: the money of an account that already exists (its inventory.json "Currencies"); only while the
//    server is stopped, because the server keeps the inventories of the players who joined in memory and would write over it.

// Starting money of new accounts. Saves only its own key of items.json and keeps the comments of the file.
internal sealed class StartingMoneyPanel : UserControl
{
    private const string Key = "startingCurrencies";

    private readonly LauncherContext context;
    private readonly Label note;
    private readonly Label status;
    private readonly Button saveButton;
    private readonly Button undoButton;
    private readonly Panel fieldsHost;
    private CurrencyFields? fields;
    private Dictionary<string, int> baseline = new Dictionary<string, int>(StringComparer.Ordinal);
    private DateTime loadedStamp = DateTime.MinValue;
    private readonly System.Windows.Forms.Timer watchTimer;

    private static string FilePath => LauncherPaths.ServerFile("items.json");

    public StartingMoneyPanel(LauncherContext context)
    {
        this.context = context;
        note = Ui.Wrapping(L.T("Money a NEW account starts with, the first time a player joins the server. Accounts that already exist are not changed (use the panel of the selected account below). Gold and iron are what the stores charge."), 420, gray: true);
        note.Dock = DockStyle.Top;
        status = Ui.Note("");
        saveButton = Ui.Button(L.T("Save"), (_, _) => Save());
        undoButton = Ui.Button(L.T("Undo changes"), (_, _) => Reload());
        var buttons = Ui.WrapFlow(saveButton, undoButton, status);
        buttons.Dock = DockStyle.Bottom;
        fieldsHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        Controls.Add(fieldsHost);
        Controls.Add(buttons);
        Controls.Add(note);
        Resize += (_, _) => note.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 8), 0);

        context.GameDataChanged += OnGameDataChanged;
        watchTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        watchTimer.Tick += (_, _) => CheckFile();
        watchTimer.Start();
        Reload();
    }

    private bool Dirty => fields != null && !SameAmounts(fields.GetValue(), baseline);

    private void Reload()
    {
        fieldsHost.Controls.Clear();
        fields = null;
        if (!File.Exists(FilePath))
        {
            Label missing = Ui.Wrapping(L.T("items.json does not exist yet: the server creates it the first time it starts."), 400, gray: false);
            missing.Dock = DockStyle.Top;
            fieldsHost.Controls.Add(missing);
            UpdateButtons();
            return;
        }
        try
        {
            string text = TextFiles.ReadKeepingBom(FilePath, out _);
            JsoncProperty? property = JsoncFile.ReadTopLevel(text).FirstOrDefault(p => string.Equals(p.Key, Key, StringComparison.OrdinalIgnoreCase));
            baseline = property == null ? new Dictionary<string, int>(StringComparer.Ordinal) : ReadAmounts(property.RawValue) ?? new Dictionary<string, int>(StringComparer.Ordinal);
            loadedStamp = File.GetLastWriteTimeUtc(FilePath);
        }
        catch (Exception e)
        {
            Label error = Ui.Wrapping(L.F("Could not read items.json: {0}", e.Message), 400, gray: false);
            error.Dock = DockStyle.Top;
            Ui.SetRole(error, TextRole.Bad);
            fieldsHost.Controls.Add(error);
            UpdateButtons();
            return;
        }
        fields = new CurrencyFields(context.GameData.Currencies) { Dock = DockStyle.Top };
        fields.SetValue(baseline);
        baseline = fields.GetValue();
        fields.Changed += () =>
        {
            UpdateButtons();
            Ui.SetStatus(status, Dirty ? L.T("Not saved yet.") : "", TextRole.Warn);
        };
        fieldsHost.Controls.Add(fields);
        UpdateButtons();
        Ui.SetStatus(status, "", TextRole.Muted);
    }

    private void UpdateButtons()
    {
        saveButton.Enabled = Dirty;
        undoButton.Enabled = Dirty;
    }

    private void Save()
    {
        if (fields == null) return;
        ValidateChildren();
        try
        {
            string text = TextFiles.ReadKeepingBom(FilePath, out bool hasBom);
            string result = JsoncFile.ApplyChanges(text, new Dictionary<string, string> { [Key] = CurrencyFields.ToJson(fields.GetValue()) }, out List<string> missing);
            if (missing.Count > 0)
            {
                Ui.ShowError(FindForm(), L.T("items.json has no \"startingCurrencies\" key. Add it to the file (for example \"startingCurrencies\": {}) and try again."));
                return;
            }
            TextFiles.WriteAtomic(FilePath, result, hasBom);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not save items.json:\n{0}", e.Message));
            return;
        }
        baseline = fields.GetValue();
        loadedStamp = File.GetLastWriteTimeUtc(FilePath);
        UpdateButtons();
        Ui.SetStatus(status, L.F("Saved at {0}. The server reads the file again by itself.", DateTime.Now.ToString("HH:mm:ss")), TextRole.Good);
    }

    private void CheckFile()
    {
        DateTime stamp = File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;
        if (stamp != loadedStamp && !Dirty) Reload();
    }

    private void OnGameDataChanged()
    {
        if (!Dirty) Reload();
    }

    private static Dictionary<string, int>? ReadAmounts(string raw)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(raw, JsoncFile.ParseOptions);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Number) continue;
                result[property.Name] = property.Value.TryGetInt32(out int amount) ? amount : (int)Math.Round(property.Value.GetDouble());
            }
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool SameAmounts(Dictionary<string, int> a, Dictionary<string, int> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out int v) && v == p.Value);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            context.GameDataChanged -= OnGameDataChanged;
            watchTimer.Stop();
            watchTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

// Money of the account selected in the list. Shows what is in inventory.json and saves it back (with a copy in server\_backup).
internal sealed class AccountMoneyPanel : UserControl
{
    private readonly LauncherContext context;
    private readonly Label title;
    private readonly Label note;
    private readonly Label status;
    private readonly Button saveButton;
    private readonly Button undoButton;
    private readonly Panel fieldsHost;
    private CurrencyFields? fields;
    private AccountStore.Summary? account;
    private Dictionary<string, int> baseline = new Dictionary<string, int>(StringComparer.Ordinal);
    private bool hasInventory;

    public AccountMoneyPanel(LauncherContext context)
    {
        this.context = context;
        title = Ui.Bold(L.T("Money of the selected account"));
        title.Dock = DockStyle.Top;
        note = Ui.Wrapping("", 250, gray: true);
        note.Dock = DockStyle.Top;
        status = Ui.Note("");
        saveButton = Ui.Button(L.T("Save money"), (_, _) => Save());
        undoButton = Ui.Button(L.T("Undo"), (_, _) => Show(account));
        var buttons = Ui.WrapFlow(saveButton, undoButton, status);
        buttons.Dock = DockStyle.Bottom;
        fieldsHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        Controls.Add(fieldsHost);
        Controls.Add(buttons);
        Controls.Add(note);
        Controls.Add(title);
        Resize += (_, _) => note.MaximumSize = new Size(Math.Max(120, ClientSize.Width - 8), 0);

        context.ServerStateChanged += UpdateButtons;
        Show(null);
    }

    private bool Dirty => fields != null && !SameAmounts(fields.GetValue(), baseline);

    // Shows the money of an account (null = nothing selected). Called when the selection changes.
    public void Show(AccountStore.Summary? chosen)
    {
        account = chosen;
        fieldsHost.Controls.Clear();
        fields = null;
        hasInventory = false;
        baseline = new Dictionary<string, int>(StringComparer.Ordinal);
        if (chosen == null || chosen.Error.Length > 0)
        {
            note.Text = L.T("Select an account in the list.");
            UpdateButtons();
            return;
        }

        try
        {
            AccountStore store = AccountStore.Load(chosen.Folder);
            hasInventory = store.HasInventory;
            if (hasInventory)
            {
                fields = new CurrencyFields(context.GameData.Currencies) { Dock = DockStyle.Top };
                fields.SetValue(store.Currencies);
                baseline = fields.GetValue();
                fields.Changed += () => { UpdateButtons(); Ui.SetStatus(status, Dirty ? L.T("Not saved yet.") : "", TextRole.Warn); };
                fieldsHost.Controls.Add(fields);
            }
        }
        catch (Exception e)
        {
            note.Text = L.F("Could not read the account: {0}", e.Message);
            UpdateButtons();
            return;
        }
        Ui.SetStatus(status, "", TextRole.Muted);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool serverRunning = context.Server.IsActive;
        bool canSave = fields != null && Dirty && !serverRunning;
        saveButton.Enabled = canSave;
        undoButton.Enabled = fields != null && Dirty;
        if (fields != null) fields.Enabled = !serverRunning;
        if (account == null || account.Error.Length > 0) return;
        if (!hasInventory) note.Text = L.T("This account has no inventory.json yet: the server creates it the first time the player joins.");
        else if (serverRunning) note.Text = L.T("The server is running: it keeps the money of the players in memory. Stop it to edit here, or give money in the game chat with /currency gold 5000.");
        else note.Text = L.F("Edit the money of {0} and press Save money (a copy goes to server\\_backup).", account.Name);
        Ui.SetRole(note, serverRunning ? TextRole.Warn : TextRole.Muted);
    }

    private void Save()
    {
        if (account == null || fields == null) return;
        if (context.Server.IsActive)
        {
            Ui.ShowError(FindForm(), L.T("Stop the server to edit or remove accounts: the server keeps the accounts of the players who joined in memory while it runs and would write over the changes."));
            return;
        }
        ValidateChildren();
        try
        {
            AccountStore store = AccountStore.Load(account.Folder);
            store.Currencies = fields.GetValue();
            string backup = store.Save();
            context.ServerNotice(L.F("money of {0} saved; previous copy in {1}", account.Name, backup));
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not save the account: {0}", e.Message));
            return;
        }
        baseline = fields.GetValue();
        UpdateButtons();
        Ui.SetStatus(status, L.F("Saved at {0}.", DateTime.Now.ToString("HH:mm:ss")), TextRole.Good);
    }

    private static bool SameAmounts(Dictionary<string, int> a, Dictionary<string, int> b) =>
        a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out int v) && v == p.Value);

    protected override void Dispose(bool disposing)
    {
        if (disposing) context.ServerStateChanged -= UpdateButtons;
        base.Dispose(disposing);
    }
}
