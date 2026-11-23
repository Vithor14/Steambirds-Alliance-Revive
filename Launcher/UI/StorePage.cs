using SteambirdsLauncher.Core;
using SteambirdsLauncher.Core.GameData;

namespace SteambirdsLauncher.UI;

// "Store" page of the Server tab: everything of stores.json in one place.
//  - General: purchases on/off, how prices are charged (gold and iron, or free), the discount on every price.
//  - Prices and products: every product of the game with its price, discount, stock and limit per account.
//  - Shelves: which stores are open and what each section sells.
// The file keeps its comments: only the six top-level values are replaced when saving. The server reads the file again by
// itself, also while it is running (stock sold so far is kept by the server in data\store_stock.json).
internal sealed class StorePage : UserControl
{
    private readonly LauncherContext context;
    private readonly ToolTip tip;
    private readonly Button saveButton;
    private readonly Button undoButton;
    private readonly Label statusLabel;
    private readonly Panel body;
    private readonly System.Windows.Forms.Timer watchTimer;

    private StoreRules rules = new StoreRules();
    private DateTime loadedStamp = DateTime.MinValue;
    private bool dirty;
    private bool loading;
    private bool warnedExternal;

    private CheckBox purchases = null!;
    private ComboBox priceMode = null!;
    private NumberBox discount = null!;
    private CheckBox preRelease = null!;
    private StoreProductsView? productsView;
    private StoreShelvesView? shelvesView;

    public StorePage(LauncherContext context)
    {
        this.context = context;
        tip = context.ToolTip;

        saveButton = Ui.Button(L.T("Save"), (_, _) => Save());
        undoButton = Ui.Button(L.T("Undo changes"), (_, _) => Reload());
        statusLabel = Ui.Note("");
        var toolbar = Ui.WrapFlow(saveButton, undoButton,
            Ui.Button(L.T("Open stores.json"), (_, _) => Ui.OpenFile(FindForm(), StoreRules.FilePath)),
            statusLabel);
        toolbar.Dock = DockStyle.Top;

        body = new Panel { Dock = DockStyle.Fill };
        Controls.Add(body);
        Controls.Add(toolbar);

        context.GameDataChanged += OnGameDataChanged;
        watchTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        watchTimer.Tick += (_, _) => CheckFile();
        watchTimer.Start();
        Reload();
    }

    // ------------------------------------------------------------------ loading

    private void Reload()
    {
        body.Controls.Clear();
        productsView = null;
        shelvesView = null;
        dirty = false;
        warnedExternal = false;

        if (!File.Exists(StoreRules.FilePath))
        {
            loadedStamp = DateTime.MinValue;
            UpdateButtons();
            Ui.SetStatus(statusLabel, "", TextRole.Muted);
            Label missing = Ui.Wrapping(L.T("stores.json does not exist yet: the server creates it the first time it starts (with comments that explain each setting). Start the server once, or create it now."), 700, gray: false);
            missing.Dock = DockStyle.Top;
            var create = Ui.Button(L.T("Create stores.json"), (_, _) => CreateFile());
            var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            panel.Controls.Add(missing);
            panel.Controls.Add(create);
            body.Controls.Add(panel);
            return;
        }

        try
        {
            string text = TextFiles.ReadKeepingBom(StoreRules.FilePath, out _);
            rules = StoreRules.Parse(text);
            loadedStamp = File.GetLastWriteTimeUtc(StoreRules.FilePath);
        }
        catch (Exception e)
        {
            UpdateButtons();
            Label error = Ui.Wrapping(L.F("Could not read stores.json: {0}", e.Message), 700, gray: false);
            error.Dock = DockStyle.Top;
            Ui.SetRole(error, TextRole.Bad);
            body.Controls.Add(error);
            return;
        }

        BuildTabs();
        UpdateButtons();
    }

    private void BuildTabs()
    {
        GameConfig data = context.GameData;
        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };

        var generalPage = new TabPage(L.T("General")) { Padding = new Padding(8) };
        generalPage.Controls.Add(BuildGeneral());
        tabs.TabPages.Add(generalPage);

        if (data.HasStore)
        {
            productsView = new StoreProductsView(data, rules, tip, MarkDirty) { Dock = DockStyle.Fill };
            var productsPage = new TabPage(L.T("Prices and products")) { Padding = new Padding(6) };
            productsPage.Controls.Add(productsView);
            tabs.TabPages.Add(productsPage);

            shelvesView = new StoreShelvesView(data, rules, tip, MarkDirty) { Dock = DockStyle.Fill };
            var shelvesPage = new TabPage(L.T("Shelves")) { Padding = new Padding(6) };
            shelvesPage.Controls.Add(shelvesView);
            tabs.TabPages.Add(shelvesPage);
        }
        else
        {
            var missing = new TabPage(L.T("Prices and products")) { Padding = new Padding(8) };
            Label note = Ui.Wrapping(L.T("The products and prices come from the game's configBinary, which the launcher could not read (set the game folder in the Settings tab, or run SteambirdsGameFiles.exe so the server has its own copy). The General tab still works."), 700, gray: false);
            note.Dock = DockStyle.Top;
            missing.Controls.Add(note);
            tabs.TabPages.Add(missing);
        }
        body.Controls.Clear();
        body.Controls.Add(tabs);
    }

    private Control BuildGeneral()
    {
        loading = true;
        purchases = new ThemedCheckBox { Text = L.T("Purchases enabled"), AutoSize = true, Checked = rules.PurchasesEnabled, Margin = new Padding(3, 6, 3, 3) };
        purchases.CheckedChanged += (_, _) => OnGeneralChanged();

        priceMode = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260, Margin = new Padding(3, 4, 3, 3) };
        priceMode.Items.Add(L.T("The account pays with its gold and iron"));
        priceMode.Items.Add(L.T("Free: every product costs 0"));
        priceMode.SelectedIndex = rules.Free ? 1 : 0;
        priceMode.SelectedIndexChanged += (_, _) => OnGeneralChanged();

        discount = new NumberBox
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(rules.DiscountPercent, 0, 100),
            Width = 80,
            TextAlign = HorizontalAlignment.Right,
            Margin = new Padding(3, 4, 3, 3)
        };
        discount.ValueChanged += (_, _) => OnGeneralChanged();

        preRelease = new ThemedCheckBox { Text = L.T("Sell pre-release products"), AutoSize = true, Checked = rules.ShowPreRelease, Margin = new Padding(3, 6, 3, 3) };
        preRelease.CheckedChanged += (_, _) => OnGeneralChanged();
        discount.Enabled = !rules.Free;
        loading = false;

        tip.SetToolTip(purchases, L.T("Off: the stores open, but every purchase is refused."));
        tip.SetToolTip(discount, L.T("Percent taken off every price. A product can have its own discount in the Prices and products tab."));
        tip.SetToolTip(preRelease, L.T("The game marks a few trails and emotes as pre-release and does not show them."));

        var table = Ui.Table(2);
        table.Dock = DockStyle.Top;
        Ui.AddRow(table, purchases);
        Ui.AddRow(table, Ui.Text(L.T("How prices are charged:")), priceMode);
        Ui.AddRow(table, Ui.Text(L.T("Discount on every price (%):")), discount);
        Ui.AddRow(table, preRelease);
        table.SetColumnSpan(purchases, 2);
        table.SetColumnSpan(preRelease, 2);

        Label help = Ui.Wrapping(L.T("Gold used to be bought with real money on Steam and iron came from pilot XP; neither exists here. With \"the account pays\", the host gives money in the Accounts tab (starting money of new accounts, or the money of an account that already exists) or with /currency gold 5000 in the game chat. With \"free\" nothing is charged and the game shows 100% off.\n\nPrices cannot go up: the game draws them from its own config, so the discount (0 to 100%) is the only dial. The Prices and products tab sets it for each product or for many at once, and shows what the player pays."), 760, gray: true);
        help.Dock = DockStyle.Top;
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        panel.Controls.Add(help);
        panel.Controls.Add(table);
        return panel;
    }

    // ------------------------------------------------------------------ editing

    private void OnGeneralChanged()
    {
        if (loading) return;
        rules.PurchasesEnabled = purchases.Checked;
        rules.PriceMode = priceMode.SelectedIndex == 1 ? "free" : "currency";
        rules.DiscountPercent = (int)discount.Value;
        rules.ShowPreRelease = preRelease.Checked;
        discount.Enabled = !rules.Free;
        productsView?.RefreshRules();
        MarkDirty();
    }

    private void MarkDirty()
    {
        dirty = true;
        UpdateButtons();
        Ui.SetStatus(statusLabel, L.T("Not saved yet."), TextRole.Warn);
    }

    private void UpdateButtons()
    {
        saveButton.Enabled = dirty;
        undoButton.Enabled = dirty;
    }

    // ------------------------------------------------------------------ saving

    private void Save()
    {
        ValidateChildren();
        try
        {
            string path = StoreRules.FilePath;
            string text = TextFiles.ReadKeepingBom(path, out bool hasBom);
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            Dictionary<string, string> values = rules.ToRawValues(newline);
            string result = JsoncFile.ApplyChanges(text, values, out List<string> missing);
            if (missing.Count > 0) result = StoreRules.AppendMissing(result, values, missing, newline);
            using (System.Text.Json.JsonDocument.Parse(result, JsoncFile.ParseOptions)) { }
            TextFiles.WriteAtomic(path, result, hasBom);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not save stores.json:\n{0}", e.Message));
            return;
        }
        dirty = false;
        loadedStamp = File.GetLastWriteTimeUtc(StoreRules.FilePath);
        warnedExternal = false;
        UpdateButtons();
        Ui.SetStatus(statusLabel, L.F("Saved at {0}. The server reads the file again by itself.", DateTime.Now.ToString("HH:mm:ss")), TextRole.Good);
    }

    // The server creates the file the first time it runs; this writes a small one when the host wants to edit before that.
    private void CreateFile()
    {
        try
        {
            string text = "// Stores of the server. Edited with the Store tab of the launcher; every setting is explained in docs\\stores.md." + Environment.NewLine +
                          "{" + Environment.NewLine +
                          "  \"purchasesEnabled\": true," + Environment.NewLine +
                          "  \"priceMode\": \"currency\"," + Environment.NewLine +
                          "  \"discountPercent\": 0," + Environment.NewLine +
                          "  \"showPreRelease\": false," + Environment.NewLine +
                          "  \"stores\": {}," + Environment.NewLine +
                          "  \"products\": {}" + Environment.NewLine +
                          "}" + Environment.NewLine;
            TextFiles.WriteAtomic(StoreRules.FilePath, text, false);
        }
        catch (Exception e)
        {
            Ui.ShowError(FindForm(), L.F("Could not create stores.json:\n{0}", e.Message));
            return;
        }
        Reload();
    }

    // Another program (Notepad, the server creating the file) changed stores.json.
    private void CheckFile()
    {
        bool exists = File.Exists(StoreRules.FilePath);
        DateTime stamp = exists ? File.GetLastWriteTimeUtc(StoreRules.FilePath) : DateTime.MinValue;
        if (stamp == loadedStamp) return;
        if (!dirty)
        {
            Reload();
            return;
        }
        if (!warnedExternal)
        {
            warnedExternal = true;
            Ui.SetStatus(statusLabel, L.T("stores.json changed outside the launcher. Saving writes the values of this page over it (Undo changes loads the file again)."), TextRole.Warn);
        }
    }

    private void OnGameDataChanged()
    {
        // The products come from the game: show them again (unless there are edits not saved).
        if (!dirty) Reload();
    }

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
