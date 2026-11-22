using System.Globalization;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Core.GameData;

namespace SteambirdsLauncher.UI;

// "Prices and products" part of the Store page: every product of the game's stores with its price, and the host's rules
// for it (sold or not, discount, stock, limit per account). The window of the game draws the price itself from the game's
// config, so the only price the host can change is the percent off: the columns "now" show what the player pays.
internal sealed class StoreProductsView : UserControl
{
    private const int ColSold = 0;
    private const int ColName = 1;
    private const int ColId = 2;
    private const int ColCategory = 3;
    private const int ColGold = 4;
    private const int ColIron = 5;
    private const int ColDiscount = 6;
    private const int ColGoldNow = 7;
    private const int ColIronNow = 8;
    private const int ColStock = 9;
    private const int ColMax = 10;

    private readonly GameConfig data;
    private readonly StoreRules rules;
    private readonly Action changed;
    private readonly DataGridView grid;
    private readonly TextBox search;
    private readonly ComboBox category;
    private readonly CheckBox onlyRules;
    private readonly NumericUpDown bulkDiscount;
    private readonly NumericUpDown bulkStock;
    private readonly NumericUpDown bulkMax;
    private readonly Label count;
    private bool updating;

    public StoreProductsView(GameConfig data, StoreRules rules, ToolTip tip, Action changed)
    {
        this.data = data;
        this.rules = rules;
        this.changed = changed;

        search = new TextBox { Width = 180, PlaceholderText = L.T("Search name, id or tag"), Margin = new Padding(3, 4, 3, 3) };
        search.TextChanged += (_, _) => ApplyFilter();
        category = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = new Padding(3, 4, 3, 3) };
        category.Items.Add(L.T("All categories"));
        foreach (string name in data.StoreProducts.Select(p => p.Category).Where(c => c.Length > 0).Distinct().OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase))
            category.Items.Add(name);
        category.SelectedIndex = 0;
        category.SelectedIndexChanged += (_, _) => ApplyFilter();
        onlyRules = new CheckBox { Text = L.T("Only products with rules"), AutoSize = true, Margin = new Padding(8, 6, 3, 3) };
        onlyRules.CheckedChanged += (_, _) => ApplyFilter();
        count = Ui.Note("");
        var filterRow = Ui.WrapFlow(Ui.Text(L.T("Show:")), search, category, onlyRules, count);
        filterRow.Dock = DockStyle.Top;

        bulkDiscount = Number(100, 0);
        bulkStock = Number(1_000_000, 0);
        bulkMax = Number(1000, 0);
        var bulkRow = Ui.WrapFlow(
            Ui.Text(L.T("For the products shown:")),
            Ui.Text(L.T("discount %")), bulkDiscount,
            Ui.Button(L.T("Apply"), (_, _) => BulkDiscount(bulkDiscount.Value)),
            Ui.Button(L.T("Use the global discount"), (_, _) => BulkDiscount(null)),
            Ui.Text(L.T("stock")), bulkStock,
            Ui.Button(L.T("Apply"), (_, _) => BulkStock((int)bulkStock.Value)),
            Ui.Button(L.T("No limit"), (_, _) => BulkStock(0)),
            Ui.Text(L.T("max per account")), bulkMax,
            Ui.Button(L.T("Apply"), (_, _) => BulkMax((int)bulkMax.Value)),
            Ui.Button(L.T("Game's limit"), (_, _) => BulkMax(0)),
            Ui.Button(L.T("Sell all"), (_, _) => BulkSold(true)),
            Ui.Button(L.T("Stop selling all"), (_, _) => BulkSold(false)));
        bulkRow.Dock = DockStyle.Top;
        tip.SetToolTip(bulkDiscount, L.T("Percent taken off the price (0-100). The game shows it as \"x% off\"."));
        tip.SetToolTip(bulkStock, L.T("Units the whole server can sell (0 = no limit). \"/store restock\" in the game chat refills it."));
        tip.SetToolTip(bulkMax, L.T("Times each account can buy the product (0 = the game's own limit, if it has one)."));

        Label help = Ui.Wrapping(L.T("Prices are in the game's own config and the game window draws them itself, so a price can only go down: use the discount (0 to 100%, 100 = free). The columns \"now\" show what the player pays after the global discount of the General tab (or the discount of the product). A range like 750 … 3750 is an escalating price: each unit the account owns makes the next one cost more. Gold, Iron and Category come from the game and cannot be edited."), 1000, gray: true);
        help.Dock = DockStyle.Top;

        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            EditMode = DataGridViewEditMode.EditOnEnter
        };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "sold", HeaderText = L.T("Sold"), Width = 48 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = L.T("Product"), Width = 200, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = L.T("Id"), Width = 190, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "category", HeaderText = L.T("Category"), Width = 130, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "gold", HeaderText = L.T("Gold"), Width = 70, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "iron", HeaderText = L.T("Iron"), Width = 70, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "discount", HeaderText = L.T("Discount %"), Width = 80 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "goldNow", HeaderText = L.T("Gold now"), Width = 100, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ironNow", HeaderText = L.T("Iron now"), Width = 100, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "stock", HeaderText = L.T("Stock"), Width = 70 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "max", HeaderText = L.T("Max per account"), Width = 120 });
        grid.DataError += (_, e) => e.ThrowException = false;
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (_, e) => OnCellChanged(e.RowIndex, e.ColumnIndex);

        // Fill first; the Top controls added later end up above it, the last one at the very top.
        Controls.Add(grid);
        Controls.Add(help);
        Controls.Add(bulkRow);
        Controls.Add(filterRow);
        Rebuild();
    }

    private static NumericUpDown Number(int max, int value) => new NumericUpDown
    {
        Minimum = 0,
        Maximum = max,
        Value = value,
        Width = 80,
        TextAlign = HorizontalAlignment.Right,
        Margin = new Padding(3, 4, 3, 3)
    };

    // Fills the grid from the game's products and the current rules.
    public void Rebuild()
    {
        updating = true;
        try
        {
            grid.Rows.Clear();
            foreach (GameConfig.StoreProduct product in data.StoreProducts)
            {
                int index = grid.Rows.Add();
                DataGridViewRow row = grid.Rows[index];
                row.Tag = product;
                row.Cells[ColName].Value = product.Name;
                row.Cells[ColId].Value = product.Id;
                row.Cells[ColCategory].Value = product.Category;
                row.Cells[ColGold].Value = PriceText(product.Price("gold"), 0);
                row.Cells[ColIron].Value = PriceText(product.Price("iron"), 0);
                if (product.Disabled || product.PreRelease)
                    row.Cells[ColName].ToolTipText = product.Disabled ? L.T("The game marks this product as disabled.") : L.T("Pre-release in the game: sold only when \"Sell pre-release products\" is on.");
                FillRule(row);
            }
        }
        finally
        {
            updating = false;
        }
        ApplyFilter();
    }

    // Rewrites the columns that depend on the rules (also after the General tab changed the price mode or the global discount).
    public void RefreshRules()
    {
        updating = true;
        try
        {
            foreach (DataGridViewRow row in grid.Rows) FillRule(row);
        }
        finally
        {
            updating = false;
        }
    }

    private void FillRule(DataGridViewRow row)
    {
        var product = (GameConfig.StoreProduct)row.Tag!;
        rules.Products.TryGetValue(product.Id, out StoreProductRule? rule);
        row.Cells[ColSold].Value = rule?.Enabled ?? true;
        row.Cells[ColDiscount].Value = rule?.Discount?.ToString(CultureInfo.InvariantCulture) ?? "";
        row.Cells[ColStock].Value = rule != null && rule.Stock > 0 ? rule.Stock.ToString(CultureInfo.InvariantCulture) : "";
        row.Cells[ColMax].Value = rule != null && rule.MaxPerAccount > 0 ? rule.MaxPerAccount.ToString(CultureInfo.InvariantCulture) : "";
        int discount = rules.DiscountFor(product.Id);
        row.Cells[ColGoldNow].Value = PriceText(product.Price("gold"), discount);
        row.Cells[ColIronNow].Value = PriceText(product.Price("iron"), discount);
        row.Cells[ColDiscount].ToolTipText = rules.Free ? L.T("Free mode: every price is 0.") : L.F("Empty = the global discount ({0}%).", rules.DiscountPercent);
    }

    // "750", or "750 … 3750" for an escalating price; "" when the product has no price in that currency.
    private static string PriceText(GameConfig.StorePrice? price, int discount)
    {
        if (price == null) return "";
        if (!price.Escalating)
            return (discount == 0 ? price.Amount : price.Amount - price.Amount * discount / 100).ToString(CultureInfo.InvariantCulture);
        int first = price.FirstUnit(discount);
        int last = price.Amount - price.Amount * discount / 100;
        return first == last ? first.ToString(CultureInfo.InvariantCulture) : first.ToString(CultureInfo.InvariantCulture) + " … " + last.ToString(CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------ editing

    private void OnCellChanged(int rowIndex, int columnIndex)
    {
        if (updating || rowIndex < 0 || columnIndex < 0) return;
        DataGridViewRow row = grid.Rows[rowIndex];
        var product = (GameConfig.StoreProduct)row.Tag!;
        StoreProductRule rule = rules.ProductRule(product.Id);
        updating = true;
        try
        {
            switch (columnIndex)
            {
                case ColSold:
                    rule.Enabled = row.Cells[ColSold].Value is true;
                    break;
                case ColDiscount:
                {
                    string text = Convert.ToString(row.Cells[ColDiscount].Value, CultureInfo.InvariantCulture)?.Trim().TrimEnd('%').Trim() ?? "";
                    if (text.Length == 0) rule.Discount = null;
                    else if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)) rule.Discount = Math.Clamp(value, 0, 100);
                    row.Cells[ColDiscount].Value = rule.Discount?.ToString(CultureInfo.InvariantCulture) ?? "";
                    break;
                }
                case ColStock:
                    rule.Stock = ParseCount(row.Cells[ColStock].Value, rule.Stock);
                    row.Cells[ColStock].Value = rule.Stock > 0 ? rule.Stock.ToString(CultureInfo.InvariantCulture) : "";
                    break;
                case ColMax:
                    rule.MaxPerAccount = ParseCount(row.Cells[ColMax].Value, rule.MaxPerAccount);
                    row.Cells[ColMax].Value = rule.MaxPerAccount > 0 ? rule.MaxPerAccount.ToString(CultureInfo.InvariantCulture) : "";
                    break;
                default:
                    return;
            }
            int discount = rules.DiscountFor(product.Id);
            row.Cells[ColGoldNow].Value = PriceText(product.Price("gold"), discount);
            row.Cells[ColIronNow].Value = PriceText(product.Price("iron"), discount);
        }
        finally
        {
            updating = false;
        }
        changed();
    }

    // Empty = 0; something that is not a number keeps the old value.
    private static int ParseCount(object? cell, int old)
    {
        string text = Convert.ToString(cell, CultureInfo.InvariantCulture)?.Trim() ?? "";
        if (text.Length == 0) return 0;
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? Math.Max(0, value) : old;
    }

    // ------------------------------------------------------------------ filter and bulk changes

    private void ApplyFilter()
    {
        if (updating) return;
        string text = search.Text.Trim();
        string chosen = category.SelectedIndex > 0 ? category.Text : "";
        grid.CurrentCell = null;
        int shown = 0;
        foreach (DataGridViewRow row in grid.Rows)
        {
            var product = (GameConfig.StoreProduct)row.Tag!;
            bool visible = true;
            if (text.Length > 0)
            {
                visible = product.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) ||
                          product.Id.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                          product.Tags.Any(t => t.Contains(text, StringComparison.OrdinalIgnoreCase));
            }
            if (visible && chosen.Length > 0) visible = product.Category == chosen;
            if (visible && onlyRules.Checked) visible = rules.Products.TryGetValue(product.Id, out StoreProductRule? rule) && !rule.IsDefault;
            row.Visible = visible;
            if (visible) shown++;
        }
        count.Text = L.F("{0} of {1} products", shown, grid.Rows.Count);
    }

    private IEnumerable<DataGridViewRow> Shown() => grid.Rows.Cast<DataGridViewRow>().Where(r => r.Visible).ToList();

    private void BulkDiscount(decimal? value)
    {
        foreach (DataGridViewRow row in Shown()) rules.ProductRule(((GameConfig.StoreProduct)row.Tag!).Id).Discount = value.HasValue ? (int)value.Value : null;
        AfterBulk();
    }

    private void BulkStock(int value)
    {
        foreach (DataGridViewRow row in Shown()) rules.ProductRule(((GameConfig.StoreProduct)row.Tag!).Id).Stock = value;
        AfterBulk();
    }

    private void BulkMax(int value)
    {
        foreach (DataGridViewRow row in Shown()) rules.ProductRule(((GameConfig.StoreProduct)row.Tag!).Id).MaxPerAccount = value;
        AfterBulk();
    }

    private void BulkSold(bool sold)
    {
        foreach (DataGridViewRow row in Shown()) rules.ProductRule(((GameConfig.StoreProduct)row.Tag!).Id).Enabled = sold;
        AfterBulk();
    }

    private void AfterBulk()
    {
        RefreshRules();
        ApplyFilter();
        changed();
    }
}
