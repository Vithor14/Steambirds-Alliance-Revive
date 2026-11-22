using System.Text.Json;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// Starting money on the Accounts page: what every NEW account starts with (items.json "startingCurrencies": gold,
// iron, recycling scrap...). The money of an account that already exists is edited in the account editor window.

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
