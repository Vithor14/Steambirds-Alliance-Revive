using SteambirdsLauncher.Core;
using SteambirdsLauncher.Core.GameData;

namespace SteambirdsLauncher.UI;

// "Shelves" part of the Store page: which stores are open and what each section of a store sells. The game's own rule
// puts products in a section by tag; "include" adds products to it and "exclude" takes them out (a product id or
// "tag:<tag>"). general_sales is the sale shelf: empty until products are included there.
internal sealed class StoreShelvesView : UserControl
{
    private const int ColStore = 0;
    private const int ColSection = 1;
    private const int ColName = 2;
    private const int ColType = 3;
    private const int ColCount = 4;
    private const int ColOn = 5;
    private const int ColInclude = 6;
    private const int ColExclude = 7;

    private readonly GameConfig data;
    private readonly StoreRules rules;
    private readonly Action changed;
    private readonly DataGridView stores;
    private readonly DataGridView sections;
    private bool updating;

    public StoreShelvesView(GameConfig data, StoreRules rules, ToolTip tip, Action changed)
    {
        this.data = data;
        this.rules = rules;
        this.changed = changed;

        Label storesHelp = Ui.Wrapping(L.T("Stores: untick one to close it (it opens empty and nothing can be bought there). The Hangar, Storage and Pilot School stores sell one slot each; the General Store sells everything else."), 1000, gray: true);
        storesHelp.Dock = DockStyle.Top;
        stores = NewGrid();
        stores.Columns.Add(new DataGridViewCheckBoxColumn { Name = "open", HeaderText = L.T("Open"), Width = 50 });
        stores.Columns.Add(new DataGridViewTextBoxColumn { Name = "store", HeaderText = L.T("Store"), Width = 200, ReadOnly = true });
        stores.Columns.Add(new DataGridViewTextBoxColumn { Name = "info", HeaderText = L.T("Where it opens"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        stores.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (stores.IsCurrentCellDirty && stores.CurrentCell is DataGridViewCheckBoxCell) stores.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        stores.CellValueChanged += (_, e) => OnStoreChanged(e.RowIndex, e.ColumnIndex);

        Label sectionsHelp = Ui.Wrapping(L.T("Sections: untick to hide a section. Include / Exclude take product ids or tag:<tag>, separated by commas (for example store_key_eye, tag:item_dye). Use the buttons to pick products from the game's list. Do not add products to a banner section (general_banner): it shows its first product as a big picture."), 1000, gray: true);
        sectionsHelp.Dock = DockStyle.Top;
        sections = NewGrid();
        sections.Columns.Add(new DataGridViewTextBoxColumn { Name = "store", HeaderText = L.T("Store"), Width = 140, ReadOnly = true });
        sections.Columns.Add(new DataGridViewTextBoxColumn { Name = "section", HeaderText = L.T("Section"), Width = 150, ReadOnly = true });
        sections.Columns.Add(new DataGridViewTextBoxColumn { Name = "name", HeaderText = L.T("Name in the game"), Width = 150, ReadOnly = true });
        sections.Columns.Add(new DataGridViewTextBoxColumn { Name = "type", HeaderText = L.T("Type"), Width = 70, ReadOnly = true });
        sections.Columns.Add(new DataGridViewTextBoxColumn { Name = "count", HeaderText = L.T("Products by the game's rule"), Width = 90, ReadOnly = true });
        sections.Columns.Add(new DataGridViewCheckBoxColumn { Name = "on", HeaderText = L.T("On"), Width = 40 });
        sections.Columns.Add(new DataGridViewTextBoxColumn { Name = "include", HeaderText = L.T("Include"), Width = 220 });
        sections.Columns.Add(new DataGridViewTextBoxColumn { Name = "exclude", HeaderText = L.T("Exclude"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        sections.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (sections.IsCurrentCellDirty && sections.CurrentCell is DataGridViewCheckBoxCell) sections.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        sections.CellValueChanged += (_, e) => OnSectionChanged(e.RowIndex, e.ColumnIndex);

        var buttons = Ui.WrapFlow(
            Ui.Button(L.T("Pick products to include..."), (_, _) => Pick(ColInclude)),
            Ui.Button(L.T("Pick products to exclude..."), (_, _) => Pick(ColExclude)));
        buttons.Dock = DockStyle.Top;

        var storesPanel = new Panel { Dock = DockStyle.Top, Height = 130 };
        storesPanel.Controls.Add(stores);

        // Fill first; the Top controls added later end up above it.
        Controls.Add(sections);
        Controls.Add(buttons);
        Controls.Add(sectionsHelp);
        Controls.Add(storesPanel);
        Controls.Add(storesHelp);
        Rebuild();
    }

    private static DataGridView NewGrid() => new DataGridView
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = false,
        EditMode = DataGridViewEditMode.EditOnEnter
    };

    public void Rebuild()
    {
        updating = true;
        try
        {
            stores.Rows.Clear();
            foreach (string storeId in data.StoreIds)
            {
                int index = stores.Rows.Add();
                DataGridViewRow row = stores.Rows[index];
                row.Tag = storeId;
                row.Cells[0].Value = !rules.Stores.TryGetValue(storeId, out StoreShopRule? shop) || shop.Enabled;
                row.Cells[1].Value = storeId;
                row.Cells[2].Value = StoreInfo(storeId);
            }

            sections.Rows.Clear();
            foreach (GameConfig.StoreSection section in data.StoreSections)
            {
                int index = sections.Rows.Add();
                DataGridViewRow row = sections.Rows[index];
                row.Tag = section;
                row.Cells[ColStore].Value = section.StoreId;
                row.Cells[ColSection].Value = section.Id;
                row.Cells[ColName].Value = section.Name + (section.Hidden ? " " + L.T("(hidden in the game)") : "");
                row.Cells[ColType].Value = section.Type.ToLowerInvariant();
                row.Cells[ColCount].Value = section.Products.ToString();
                StoreSectionRule? rule = FindRule(section);
                row.Cells[ColOn].Value = rule?.Enabled ?? true;
                row.Cells[ColInclude].Value = rule == null ? "" : string.Join(", ", rule.Include);
                row.Cells[ColExclude].Value = rule == null ? "" : string.Join(", ", rule.Exclude);
            }
        }
        finally
        {
            updating = false;
        }
    }

    private static string StoreInfo(string storeId)
    {
        switch (storeId)
        {
            case "store_general": return L.T("Store building of Rebel City (everything except slots)");
            case "store_hangar": return L.T("Hangar: the buy-a-slot button of the plane list");
            case "store_storage": return L.T("Storage window: the buy-space button");
            case "store_skill_loadouts": return L.T("Pilot School: the buy-a-loadout button");
            default: return "";
        }
    }

    private StoreSectionRule? FindRule(GameConfig.StoreSection section) =>
        rules.Stores.TryGetValue(section.StoreId, out StoreShopRule? shop) && shop.Sections.TryGetValue(section.Id, out StoreSectionRule? rule) ? rule : null;

    private void OnStoreChanged(int rowIndex, int columnIndex)
    {
        if (updating || rowIndex < 0 || columnIndex != 0) return;
        DataGridViewRow row = stores.Rows[rowIndex];
        rules.ShopRule((string)row.Tag!).Enabled = row.Cells[0].Value is true;
        changed();
    }

    private void OnSectionChanged(int rowIndex, int columnIndex)
    {
        if (updating || rowIndex < 0) return;
        DataGridViewRow row = sections.Rows[rowIndex];
        var section = (GameConfig.StoreSection)row.Tag!;
        StoreSectionRule rule = rules.SectionRule(section.StoreId, section.Id);
        switch (columnIndex)
        {
            case ColOn:
                rule.Enabled = row.Cells[ColOn].Value is true;
                break;
            case ColInclude:
                rule.Include = Split(row.Cells[ColInclude].Value);
                break;
            case ColExclude:
                rule.Exclude = Split(row.Cells[ColExclude].Value);
                break;
            default:
                return;
        }
        changed();
    }

    private static List<string> Split(object? cell)
    {
        string text = Convert.ToString(cell) ?? "";
        return text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().Trim('"')).Where(s => s.Length > 0).Distinct().ToList();
    }

    // Opens the list of the game's products and adds the chosen ones to the Include / Exclude cell of the selected section.
    private void Pick(int column)
    {
        if (sections.CurrentRow == null)
        {
            Ui.ShowInfo(FindForm(), L.T("Select a section first."));
            return;
        }
        using var picker = new ProductPicker(data);
        if (picker.ShowDialog(FindForm()) != DialogResult.OK || picker.Chosen.Count == 0) return;
        DataGridViewCell cell = sections.CurrentRow.Cells[column];
        List<string> current = Split(cell.Value);
        foreach (string id in picker.Chosen)
            if (!current.Contains(id)) current.Add(id);
        cell.Value = string.Join(", ", current);   // CellValueChanged saves it in the rules
    }

    // Small window with the game's products: search, tick or select, OK.
    private sealed class ProductPicker : Form
    {
        private readonly GameConfig data;
        private readonly ListBox list = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, IntegralHeight = false };
        private readonly TextBox search = new TextBox { Dock = DockStyle.Top, PlaceholderText = L.T("Search name, id or tag") };

        public List<string> Chosen { get; } = new List<string>();

        public ProductPicker(GameConfig data)
        {
            this.data = data;
            Text = L.T("Pick products");
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(520, 560);
            MinimumSize = new Size(380, 360);
            ShowInTaskbar = false;

            Label help = Ui.Wrapping(L.T("Select one or more products (Ctrl or Shift) and press Add."), 480, gray: true);
            help.Dock = DockStyle.Top;
            var ok = Ui.Button(L.T("Add"), (_, _) => Accept());
            ok.DialogResult = DialogResult.None;
            var cancel = Ui.Button(L.T("Cancel"));
            cancel.DialogResult = DialogResult.Cancel;
            var buttons = Ui.Flow(ok, cancel);
            buttons.Dock = DockStyle.Bottom;

            Controls.Add(list);
            Controls.Add(search);
            Controls.Add(help);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;
            search.TextChanged += (_, _) => Fill();
            list.DoubleClick += (_, _) => Accept();
            Fill();
        }

        private void Fill()
        {
            string text = search.Text.Trim();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (GameConfig.StoreProduct product in data.StoreProducts)
            {
                if (text.Length > 0 && !product.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase) &&
                    !product.Id.Contains(text, StringComparison.OrdinalIgnoreCase) &&
                    !product.Tags.Any(t => t.Contains(text, StringComparison.OrdinalIgnoreCase))) continue;
                list.Items.Add(product);
            }
            list.EndUpdate();
        }

        private void Accept()
        {
            foreach (object item in list.SelectedItems)
                if (item is GameConfig.StoreProduct product) Chosen.Add(product.Id);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
