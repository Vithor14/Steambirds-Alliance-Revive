using System.Globalization;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.Core.GameData;

namespace SteambirdsLauncher.UI;

// Window to edit one account of the server (account.json + inventory.json of a player). Opened from the Accounts page only while
// the launcher's server is stopped. Saving copies both files to server\_backup first.
//
// Levels follow the game's tables (configBinary): changing a plane level puts its XP at the start of that level,
// and changing the pilot rank moves the pilot base XP to the start of that rank (like the /rank chat command).
internal sealed class AccountEditorForm : Form
{
    private readonly LauncherContext context;
    private readonly AccountStore store;
    private readonly GameConfig data;

    private readonly NumericUpDown rank;
    private readonly NumericUpDown baselineXp;
    private readonly Label pilotXpLabel;
    private readonly NumericUpDown kills;
    private readonly NumericUpDown bossKills;
    private readonly NumericUpDown planesLost;
    private readonly NumericUpDown highestPlaneLevel;
    private readonly DataGridView planesGrid;
    private readonly UnlockChecklist unlocks;
    private readonly CheckedListBox dungeons;
    private readonly CurrencyFields currencies;
    private readonly DataGridView ingredientsGrid;
    private readonly int originalRank;
    private readonly PortraitParts portraitParts = PortraitParts.Load();
    private readonly Label portraitText;
    private Dictionary<string, int> portrait;
    private bool updating;

    public AccountEditorForm(LauncherContext context, AccountStore store)
    {
        this.context = context;
        this.store = store;
        data = context.GameData;

        SuspendLayout();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = L.F("Edit account {0}", store.DisplayName);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 680);
        MinimumSize = new Size(760, 560);
        ShowInTaskbar = false;
        MinimizeBox = false;

        // ------------------------------------------------------------ pilot
        int topRank = data.PilotLevels?.TopLevel ?? 200;
        originalRank = store.PilotLevel;
        rank = Number(1, Math.Max(topRank, originalRank), originalRank);
        baselineXp = Number(0, int.MaxValue, store.PilotBaselineXp);
        pilotXpLabel = Ui.Note("");
        kills = Number(0, int.MaxValue, store.Kills);
        bossKills = Number(0, int.MaxValue, store.BossKills);
        planesLost = Number(0, int.MaxValue, store.PlanesLost);
        highestPlaneLevel = Number(0, 1000, store.HighestPlaneLevel);
        rank.ValueChanged += (_, _) => OnRankChanged();
        baselineXp.ValueChanged += (_, _) => UpdatePilotXp();

        TableLayoutPanel pilotTable = Ui.Table(3);
        pilotTable.Dock = DockStyle.Top;
        Ui.AddRow(pilotTable, Ui.Text(L.T("Pilot rank:")), rank, Ui.Note(data.HasXpTables
            ? L.T("Changing the rank puts the pilot XP at the start of that rank.")
            : L.T("The game was not found: the XP is not adjusted to the rank.")));
        Ui.AddRow(pilotTable, Ui.Text(L.T("Pilot base XP:")), baselineXp, pilotXpLabel);
        Ui.AddRow(pilotTable, Ui.Text(L.T("Kills:")), kills);
        Ui.AddRow(pilotTable, Ui.Text(L.T("Boss kills:")), bossKills);
        Ui.AddRow(pilotTable, Ui.Text(L.T("Planes lost:")), planesLost);
        Ui.AddRow(pilotTable, Ui.Text(L.T("Highest plane level:")), highestPlaneLevel);
        Label pilotHelp = Ui.Wrapping(L.T("The pilot XP shown in the game is the base XP plus part of the value of each hangar plane. The rank only goes up by itself: lowering it also lowers the base XP."), 760, gray: true);
        pilotHelp.Dock = DockStyle.Top;
        var pilotPage = Page(L.T("Pilot"), pilotHelp, pilotTable);

        // ------------------------------------------------------------ planes
        planesGrid = new DataGridView
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
        planesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "class", HeaderText = L.T("Class"), Width = 170, ReadOnly = true });
        planesGrid.Columns.Add(new DataGridViewComboBoxColumn { Name = "variant", HeaderText = L.T("Variant"), Width = 150, FlatStyle = FlatStyle.Flat });
        planesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "level", HeaderText = L.T("Level"), Width = 70 });
        planesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "xp", HeaderText = L.T("XP"), Width = 90 });
        planesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "banked", HeaderText = L.T("Banked pilot XP"), Width = 120 });
        planesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "id", HeaderText = L.T("Plane id"), ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        planesGrid.DataError += (_, e) => e.ThrowException = false;
        planesGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (planesGrid.IsCurrentCellDirty && planesGrid.CurrentCell is DataGridViewComboBoxCell)
                planesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        planesGrid.CellValueChanged += (_, e) => OnPlaneCellChanged(e.RowIndex, e.ColumnIndex);
        foreach (AccountStore.Plane plane in store.Planes) AddPlaneRow(plane);

        var removePlane = Ui.Button(L.T("Remove selected plane"), (_, _) => RemoveSelectedPlane());
        Label planesHelp = Ui.Wrapping(L.T("Changing the level puts the XP at the start of that level. New planes are created in the game (Hangar). Removing a plane also removes its equipped items."), 760, gray: true);
        var planesTop = Ui.WrapFlow(removePlane);
        planesTop.Dock = DockStyle.Top;
        planesHelp.Dock = DockStyle.Top;
        var planesPage = new TabPage(L.T("Planes")) { Padding = new Padding(8) };
        planesPage.Controls.Add(planesGrid);
        planesPage.Controls.Add(planesTop);
        planesPage.Controls.Add(planesHelp);

        // ------------------------------------------------------------ unlocks
        unlocks = new UnlockChecklist(data) { Dock = DockStyle.Top };
        unlocks.SetValue(store.Unlocks);
        var unlocksPage = Page(L.T("Unlocks"), Ui.Wrapping(L.T("Unlock items of the account. The server also adds the ones the plane levels and progression.json give when the player logs in."), 760, gray: true), unlocks);

        // ------------------------------------------------------------ dungeons
        dungeons = new CheckedListBox { CheckOnClick = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false };
        List<string> finished = store.FinishedDungeons;
        foreach (string dungeon in DungeonNames(finished)) dungeons.Items.Add(dungeon, finished.Contains(dungeon, StringComparer.OrdinalIgnoreCase));
        Label dungeonsHelp = Ui.Wrapping(L.T("Dungeons the account finished (the tutorial is DungeonTutorialWalker). /reset_tutorial in the game chat clears the tutorial too."), 760, gray: true);
        dungeonsHelp.Dock = DockStyle.Top;
        var dungeonsPage = new TabPage(L.T("Dungeons")) { Padding = new Padding(8) };
        dungeonsPage.Controls.Add(dungeons);
        dungeonsPage.Controls.Add(dungeonsHelp);

        // ------------------------------------------------------------ currencies and ingredients
        currencies = new CurrencyFields(data.Currencies) { Dock = DockStyle.Top };
        currencies.SetValue(store.Currencies);
        ingredientsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            EditMode = DataGridViewEditMode.EditOnEnter
        };
        ingredientsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ingredient", HeaderText = L.T("Ingredient"), ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        ingredientsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "amount", HeaderText = L.T("Amount"), Width = 100 });
        Dictionary<string, int> ownedIngredients = store.Ingredients;
        foreach (string ingredient in data.Ingredients.Concat(ownedIngredients.Keys).Distinct(StringComparer.Ordinal))
            ingredientsGrid.Rows.Add(ingredient, (ownedIngredients.TryGetValue(ingredient, out int amount) ? amount : 0).ToString(CultureInfo.InvariantCulture));
        var walletPage = new TabPage(L.T("Currencies and ingredients")) { Padding = new Padding(8) };
        Label currenciesTitle = Ui.Bold(L.T("Currencies"));
        currenciesTitle.Dock = DockStyle.Top;
        Label ingredientsTitle = Ui.Bold(L.T("Ingredients"));
        ingredientsTitle.Dock = DockStyle.Top;
        ingredientsTitle.Padding = new Padding(0, 8, 0, 2);
        walletPage.Controls.Add(ingredientsGrid);
        walletPage.Controls.Add(ingredientsTitle);
        walletPage.Controls.Add(currencies);
        walletPage.Controls.Add(currenciesTitle);
        if (!store.HasInventory)
        {
            Ui.Disable(currencies, ingredientsGrid);
            Label noInventory = Ui.Note(L.T("inventory.json does not exist yet: the server creates it at the first login."));
            noInventory.Dock = DockStyle.Top;
            walletPage.Controls.Add(noInventory);
        }

        // ------------------------------------------------------------ portrait
        portrait = store.Portrait;
        portraitText = Ui.Wrapping("", 760, gray: false);
        var rerollButton = Ui.Button(L.T("Reroll portrait"), (_, _) => RerollPortrait());
        rerollButton.Enabled = portraitParts.Available;
        Label portraitHelp = Ui.Wrapping(portraitParts.Available
            ? L.T("The pilot portrait is a set of pieces (slot, piece name and its id in the game). Reroll picks a new one with the same rules the game used; it is saved with Save. The player sees the new portrait the next time they join.")
            : L.F("The list of portrait pieces was not found ({0}): run SteambirdsGameFiles.exe to create it. The current portrait is shown, but it cannot be rolled again.", PortraitParts.FilePath), 760, gray: true);
        var portraitPage = Page(L.T("Portrait"), portraitHelp, Ui.Flow(portraitText));
        portraitPage.Controls.Add(rerollButton);
        rerollButton.Dock = DockStyle.Top;
        portraitPage.Controls.SetChildIndex(rerollButton, 1);   // between the help (top) and the pieces
        ShowPortrait();

        // ------------------------------------------------------------ window
        var tabs = new ThemedTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.AddRange(new[] { pilotPage, portraitPage, planesPage, unlocksPage, dungeonsPage, walletPage });

        var save = Ui.Button(L.T("Save"), (_, _) => SaveAndClose());
        var cancel = Ui.Button(L.T("Cancel"), (_, _) => Close());
        cancel.DialogResult = DialogResult.Cancel;
        var bottom = Ui.Flow(save, cancel, Ui.Note(L.T("A copy of the current files goes to server\\_backup before saving.")));
        bottom.Dock = DockStyle.Bottom;
        bottom.Padding = new Padding(6);

        Controls.Add(tabs);
        Controls.Add(bottom);
        // No AcceptButton: Enter in a number field must not save and close the window.
        CancelButton = cancel;
        ResumeLayout(false);
        PerformLayout();

        UpdatePilotXp();
        Theme.AttachWindow(this);
    }

    private static NumericUpDown Number(int minimum, int maximum, int value)
    {
        var number = new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Width = 130,
            ThousandsSeparator = true,
            TextAlign = HorizontalAlignment.Right,
            Margin = new Padding(3)
        };
        number.Value = Math.Clamp(value, minimum, maximum);
        return number;
    }

    private static TabPage Page(string title, Label help, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(8), AutoScroll = true };
        help.Dock = DockStyle.Top;
        content.Dock = DockStyle.Top;
        page.Controls.Add(content);
        page.Controls.Add(help);
        return page;
    }

    private void ShowPortrait()
    {
        portraitText.Text = portrait.Count == 0
            ? L.T("This pilot has no portrait yet: the server gives one the first time the player joins.")
            : portraitParts.Describe(portrait).Replace(", ", "\n");
    }

    private void RerollPortrait()
    {
        Dictionary<string, int>? rolled = portraitParts.Roll(new Random());
        if (rolled == null) return;
        portrait = rolled;
        ShowPortrait();
    }

    // Dungeon terrains: the ones of dungeons.json, the walker tutorial and the ones the account already has.
    private static List<string> DungeonNames(List<string> finished)
    {
        var names = new List<string> { "DungeonTutorialWalker" };
        try
        {
            if (DungeonCatalogFile.Exists) names.AddRange(DungeonCatalogFile.Load().Dungeons.Select(d => d.Type));
        }
        catch
        {
            // Without dungeons.json the list has only the tutorial and what the account has.
        }
        names.AddRange(finished);
        return names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ------------------------------------------------------------------ planes

    private void AddPlaneRow(AccountStore.Plane plane)
    {
        updating = true;
        try
        {
            GameConfig.PlaneClass? planeClass = data.FindClass(plane.PlaneClass);
            int index = planesGrid.Rows.Add(planeClass?.ToString() ?? plane.PlaneClass, "", plane.Level.ToString(CultureInfo.InvariantCulture),
                plane.Xp.ToString(CultureInfo.InvariantCulture), plane.BankedPilotXp.ToString(CultureInfo.InvariantCulture), plane.CharacterId);
            DataGridViewRow row = planesGrid.Rows[index];
            row.Tag = plane;

            var variants = new List<string> { "default" };
            if (planeClass != null) variants.AddRange(planeClass.Variants.Select(v => v.Key));
            if (plane.Variant.Length > 0) variants.Add(plane.Variant);
            var cell = (DataGridViewComboBoxCell)row.Cells["variant"];
            foreach (string variant in variants.Distinct(StringComparer.Ordinal)) cell.Items.Add(variant);
            cell.Value = plane.Variant.Length > 0 ? plane.Variant : "default";
        }
        finally
        {
            updating = false;
        }
    }

    private void OnPlaneCellChanged(int rowIndex, int columnIndex)
    {
        if (updating || rowIndex < 0 || columnIndex < 0) return;
        DataGridViewRow row = planesGrid.Rows[rowIndex];
        string column = planesGrid.Columns[columnIndex].Name;
        if (data.PlaneLevels == null) return;

        updating = true;
        try
        {
            if (column == "level" && TryInt(row.Cells["level"].Value, out int level))
            {
                level = Math.Clamp(level, 1, data.PlaneLevels.TopLevel);
                row.Cells["level"].Value = level.ToString(CultureInfo.InvariantCulture);
                row.Cells["xp"].Value = data.PlaneLevels.XpFromLevel(level).ToString(CultureInfo.InvariantCulture);
            }
            else if (column == "xp" && TryInt(row.Cells["xp"].Value, out int xp))
            {
                int fromXp = Math.Clamp(data.PlaneLevels.LevelFromXp(Math.Max(0, xp)), 1, data.PlaneLevels.TopLevel);
                row.Cells["level"].Value = fromXp.ToString(CultureInfo.InvariantCulture);
            }
        }
        finally
        {
            updating = false;
        }
        UpdatePilotXp();
    }

    private void RemoveSelectedPlane()
    {
        if (planesGrid.SelectedRows.Count == 0) return;
        DataGridViewRow row = planesGrid.SelectedRows[0];
        if (!Ui.Confirm(this, L.F("Remove the plane {0} from the account? Its equipped items go with it.", Convert.ToString(row.Cells["class"].Value))))
            return;
        planesGrid.Rows.Remove(row);
        UpdatePilotXp();
    }

    private List<AccountStore.Plane> PlanesFromGrid(List<string> errors)
    {
        var result = new List<AccountStore.Plane>();
        foreach (DataGridViewRow row in planesGrid.Rows)
        {
            if (row.Tag is not AccountStore.Plane original) continue;
            string name = Convert.ToString(row.Cells["class"].Value) ?? original.PlaneClass;
            if (!TryInt(row.Cells["level"].Value, out int level) || level < 1) errors.Add(L.F("{0}: invalid level.", name));
            if (!TryInt(row.Cells["xp"].Value, out int xp) || xp < 0) errors.Add(L.F("{0}: invalid XP.", name));
            if (!TryInt(row.Cells["banked"].Value, out int banked) || banked < 0) errors.Add(L.F("{0}: invalid banked pilot XP.", name));
            result.Add(new AccountStore.Plane
            {
                CharacterId = original.CharacterId,
                PlaneClass = original.PlaneClass,
                Variant = Convert.ToString(row.Cells["variant"].Value) ?? original.Variant,
                Level = level,
                Xp = xp,
                BankedPilotXp = banked
            });
        }
        return result;
    }

    private static bool TryInt(object? value, out int result)
    {
        string text = (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Trim().Replace(".", "").Replace(",", "");
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    // ------------------------------------------------------------------ pilot

    private IEnumerable<int> CurrentPlaneXps()
    {
        foreach (DataGridViewRow row in planesGrid.Rows)
        {
            if (TryInt(row.Cells["xp"].Value, out int xp)) yield return Math.Max(0, xp);
        }
    }

    private void OnRankChanged()
    {
        if (updating || !data.HasXpTables) return;
        updating = true;
        try
        {
            int baseline = data.BaselineForRankStart(CurrentPlaneXps().ToList(), (int)rank.Value);
            baselineXp.Value = Math.Clamp(baseline, (int)baselineXp.Minimum, int.MaxValue);
        }
        finally
        {
            updating = false;
        }
        UpdatePilotXp();
    }

    // Pilot XP the game shows: base XP + the part of the plane values that counts in real time.
    private void UpdatePilotXp()
    {
        float realtime = data.RealtimePilotXp(CurrentPlaneXps().ToList(), (int)rank.Value);
        int total = GameConfig.RoundToInt((float)baselineXp.Value + realtime);
        pilotXpLabel.Text = L.F("Pilot XP in the game: {0}", total.ToString("N0", CultureInfo.CurrentCulture));
    }

    // ------------------------------------------------------------------ save

    private void SaveAndClose()
    {
        ValidateChildren();
        planesGrid.EndEdit();
        ingredientsGrid.EndEdit();
        if (context.Server.IsActive)
        {
            Ui.ShowError(this, L.T("Stop the server to edit or remove the account: the server keeps the account in memory while it runs and would write over the changes."));
            return;
        }

        var errors = new List<string>();
        List<AccountStore.Plane> planes = PlanesFromGrid(errors);
        var ingredients = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (DataGridViewRow row in ingredientsGrid.Rows)
        {
            string id = Convert.ToString(row.Cells["ingredient"].Value) ?? "";
            if (!TryInt(row.Cells["amount"].Value, out int amount) || amount < 0) errors.Add(L.F("{0}: invalid amount.", id));
            else if (amount > 0 && id.Length > 0) ingredients[id] = amount;
        }
        if (errors.Count > 0)
        {
            Ui.ShowError(this, L.T("Fix these before saving:") + "\n\n" + string.Join("\n", errors));
            return;
        }

        try
        {
            int newRank = (int)rank.Value;
            store.PilotLevel = newRank;
            store.PilotBaselineXp = (int)baselineXp.Value;
            if (newRank != originalRank)
            {
                // The rank-up screen does not show again for a rank set here.
                store.LastLevelupShown = newRank;
                store.LastXpLevelupShown = GameConfig.RoundToInt((float)baselineXp.Value + data.RealtimePilotXp(planes.Select(p => p.Xp).ToList(), newRank));
            }
            store.Kills = (int)kills.Value;
            store.BossKills = (int)bossKills.Value;
            store.PlanesLost = (int)planesLost.Value;
            store.HighestPlaneLevel = Math.Max((int)highestPlaneLevel.Value, planes.Count > 0 ? planes.Max(p => p.Level) : 0);
            store.SetPlanes(planes);
            store.Portrait = portrait;
            store.Unlocks = unlocks.GetValue();
            store.FinishedDungeons = dungeons.CheckedItems.Cast<object>().Select(o => o.ToString() ?? "").Where(s => s.Length > 0).ToList();
            if (store.HasInventory)
            {
                store.Currencies = currencies.GetValue();
                store.Ingredients = ingredients;
            }
            string backup = store.Save();
            context.ServerNotice(L.F("account saved; previous copy in {0}", backup));
        }
        catch (Exception e)
        {
            Ui.ShowError(this, L.F("Could not save the account: {0}", e.Message));
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
