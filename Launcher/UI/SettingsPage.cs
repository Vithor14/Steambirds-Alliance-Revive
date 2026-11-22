using System.Globalization;
using System.Text.Json;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// Builds a special editor for one key (the unlock checklist, the currency fields). null = use the default editor.
internal delegate SettingEditor? CustomEditorFactory(SettingsSection section, JsoncProperty property);

// Part of a server config file shown on a settings page.
internal sealed class SettingsSection
{
    public SettingsSection(string title, string fileName, Func<string, bool> includeKey)
    {
        Title = title;
        FileName = fileName;
        IncludeKey = includeKey;
    }

    public string Title { get; }
    public string FileName { get; }
    public Func<string, bool> IncludeKey { get; }

    // Text shown after saving instead of "the server reads the file again by itself" (network.json: the ports need a
    // restart of the server).
    public string? SavedNote { get; init; }
    public string FilePath => LauncherPaths.ServerFile(FileName);
    public Dictionary<string, CustomEditorFactory> CustomEditors { get; } = new Dictionary<string, CustomEditorFactory>(StringComparer.OrdinalIgnoreCase);

    public static Func<string, bool> Only(params string[] keys)
    {
        var set = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        return key => set.Contains(key);
    }

    public static Func<string, bool> AllExcept(params string[] keys)
    {
        var set = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        return key => !set.Contains(key);
    }
}

// Editor for the server config files (progression.json, items.json, enemies.json). The screen is built from the file
// itself: every top-level key becomes a field and the comment above it becomes its description, so new settings of
// the server show up here by themselves. Saving changes only the values that were edited and keeps the comments.
// The server reads these files again by itself (about once per second), also while it is running.
internal sealed class SettingsPage : UserControl
{
    private const int TitleColumnWidth = 330;

    private readonly SettingsSection[] sections;
    private readonly ToolTip toolTip;
    private readonly TableLayoutPanel grid;
    private readonly Panel scroller;
    private readonly Button saveButton;
    private readonly Button undoButton;
    private readonly Label statusLabel;
    private readonly Label introLabel;
    private readonly Control toolbar;
    private readonly List<SettingEditor> editors = new List<SettingEditor>();
    private readonly List<Label> wrappingLabels = new List<Label>();
    private readonly Dictionary<string, DateTime> loadedStamps = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    private readonly System.Windows.Forms.Timer watchTimer;
    private bool loading;
    private bool warnedExternalChange;

    public SettingsPage(ToolTip toolTip, string intro, params SettingsSection[] sections)
    {
        this.toolTip = toolTip;
        this.sections = sections;

        saveButton = Ui.Button(L.T("Save"), (_, _) => Save());
        saveButton.Enabled = false;
        undoButton = Ui.Button(L.T("Undo changes"), (_, _) => Reload());
        undoButton.Enabled = false;
        statusLabel = Ui.Note("");

        toolbar = Ui.WrapFlow(saveButton, undoButton, statusLabel);
        toolbar.Dock = DockStyle.Top;

        introLabel = Ui.Wrapping(intro, 600, gray: true);
        introLabel.Dock = DockStyle.Top;
        introLabel.Padding = new Padding(0, 0, 0, 4);

        grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 8, 8)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, TitleColumnWidth));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroller.Controls.Add(grid);
        scroller.Resize += (_, _) => UpdateWrapWidths();

        // Fill first, then the Top controls (WinForms docks the last added control first).
        Controls.Add(scroller);
        Controls.Add(introLabel);
        Controls.Add(toolbar);

        watchTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        watchTimer.Tick += (_, _) => CheckExternalChanges();
        watchTimer.Start();

        Reload();
    }

    public bool HasUnsavedChanges => editors.Any(e => e.IsChanged);

    // Extra button in the toolbar, before the status text (for example a shortcut to a folder the settings point to).
    public void AddToolbarButton(string text, EventHandler onClick)
    {
        Button button = Ui.Button(text, onClick);
        toolbar.Controls.Add(button);
        toolbar.Controls.SetChildIndex(button, toolbar.Controls.GetChildIndex(statusLabel));
    }

    // Reads the files again (drops unsaved edits).
    public void Reload()
    {
        loading = true;
        scroller.SuspendLayout();
        grid.SuspendLayout();
        try
        {
            foreach (Control old in grid.Controls.Cast<Control>().ToList()) old.Dispose();
            grid.Controls.Clear();
            grid.RowStyles.Clear();
            grid.RowCount = 0;
            editors.Clear();
            wrappingLabels.Clear();
            loadedStamps.Clear();
            warnedExternalChange = false;

            foreach (SettingsSection section in sections) LoadSection(section);
        }
        finally
        {
            grid.ResumeLayout(true);
            scroller.ResumeLayout(true);
            loading = false;
        }
        UpdateWrapWidths();
        UpdateButtons();
    }

    private void LoadSection(SettingsSection section)
    {
        Label header = Ui.Bold(section.Title + "  —  " + section.FileName);
        header.Margin = new Padding(3, 10, 3, 4);
        Ui.AddRow(grid, header);
        grid.SetColumnSpan(header, 2);

        if (!File.Exists(section.FilePath))
        {
            AddFullWidthNote(L.F("File not found: {0}. The server creates it with the default values the first time it starts.", section.FilePath));
            return;
        }

        // The date is kept even when the file has an error, so it is only read again after it changes.
        loadedStamps[section.FilePath] = File.GetLastWriteTimeUtc(section.FilePath);
        List<JsoncProperty> properties;
        try
        {
            string text = TextFiles.ReadKeepingBom(section.FilePath, out _);
            properties = JsoncFile.ReadTopLevel(text);
        }
        catch (Exception e)
        {
            AddFullWidthNote(L.F("Could not read {0}: {1}. Fix the file (Settings tab, Files) and click Undo changes to read it again.", section.FileName, e.Message));
            return;
        }

        int shown = 0;
        foreach (JsoncProperty property in properties)
        {
            if (!section.IncludeKey(property.Key)) continue;
            SettingEditor editor = SettingEditor.Create(section, property);
            editor.Changed += () =>
            {
                if (!loading) UpdateButtons();
            };
            editors.Add(editor);
            AddEditorRows(editor);
            shown++;
        }
        if (shown == 0) AddFullWidthNote(L.T("None of the keys of this part were found in the file."));
    }

    private void AddEditorRows(SettingEditor editor)
    {
        Label title = Ui.Bold(SettingLabels.Title(editor.Key));
        title.MaximumSize = new Size(TitleColumnWidth - 10, 0);
        toolTip.SetToolTip(title, L.F("Key \"{0}\" in {1}", editor.Key, editor.Section.FileName));
        Ui.AddRow(grid, title, editor.Control);

        string description = SettingLabels.Description(editor.Key, editor.Property.Comment);
        if (description.Length > 0)
        {
            Label text = Ui.Wrapping(description, 600, gray: true);
            Ui.AddRow(grid, text);
            grid.SetColumnSpan(text, 2);
            wrappingLabels.Add(text);
        }
    }

    private void AddFullWidthNote(string text)
    {
        Label note = Ui.Wrapping(text, 600, gray: false);
        Ui.SetRole(note, TextRole.Warn);
        Ui.AddRow(grid, note);
        grid.SetColumnSpan(note, 2);
        wrappingLabels.Add(note);
    }

    private void UpdateWrapWidths()
    {
        int width = Math.Max(200, scroller.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 30);
        foreach (Label label in wrappingLabels) label.MaximumSize = new Size(width, 0);
        introLabel.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 20), 0);
    }

    private void UpdateButtons()
    {
        bool changed = HasUnsavedChanges;
        saveButton.Enabled = changed;
        undoButton.Enabled = changed;
        if (changed) Ui.SetStatus(statusLabel, L.T("Unsaved changes."), TextRole.Warn);
        else if (statusLabel.Text == L.T("Unsaved changes.")) statusLabel.Text = "";
    }

    private void Save()
    {
        // Commits a number typed in a NumericUpDown that still has the focus.
        ValidateChildren();

        List<string> errors = editors.Where(e => e.IsChanged).Select(e => e.Validate()).OfType<string>().ToList();
        if (errors.Count > 0)
        {
            Ui.ShowError(FindForm(), L.T("Fix these before saving:") + "\n\n" + string.Join("\n", errors));
            return;
        }

        var warnings = new List<string>();
        var notes = new List<string>();
        int files = 0;
        foreach (IGrouping<string, SettingEditor> group in editors.Where(e => e.IsChanged)
                     .GroupBy(e => e.Section.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            string path = group.Key;
            try
            {
                string text = TextFiles.ReadKeepingBom(path, out bool hasBom);
                string newline = text.Contains("\r\n") ? "\r\n" : "\n";
                var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (SettingEditor editor in group) changes[editor.Key] = editor.CurrentRaw(newline);
                string result = JsoncFile.ApplyChanges(text, changes, out List<string> missing);
                TextFiles.WriteAtomic(path, result, hasBom);
                files++;
                foreach (string? note in group.Select(e => e.Section.SavedNote).Distinct())
                    if (!string.IsNullOrEmpty(note) && !notes.Contains(note)) notes.Add(note);
                if (missing.Count > 0)
                    warnings.Add(L.F("{0}: the key(s) {1} are no longer in the file and were not saved.", Path.GetFileName(path), string.Join(", ", missing)));
            }
            catch (Exception e)
            {
                Ui.ShowError(FindForm(), L.F("Could not save {0}:\n{1}", path, e.Message));
                return;
            }
        }

        Reload();
        string saved = notes.Count > 0
            ? L.F("Saved at {0}.", DateTime.Now.ToString("HH:mm:ss")) + " " + string.Join(" ", notes)
            : L.F("Saved at {0}. The server reads the file again by itself.", DateTime.Now.ToString("HH:mm:ss"));
        Ui.SetStatus(statusLabel, files > 0 ? saved : L.T("Nothing to save."), TextRole.Good);
        if (warnings.Count > 0) Ui.ShowInfo(FindForm(), string.Join("\n", warnings));
    }

    // Another program (Notepad, another page of the launcher, the server creating a default file) changed a file.
    private void CheckExternalChanges()
    {
        bool changedOnDisk = sections.Any(s =>
        {
            bool exists = File.Exists(s.FilePath);
            bool known = loadedStamps.TryGetValue(s.FilePath, out DateTime stamp);
            if (!exists) return known;
            return !known || File.GetLastWriteTimeUtc(s.FilePath) != stamp;
        });
        if (!changedOnDisk) return;

        if (!HasUnsavedChanges)
        {
            Reload();
            return;
        }
        if (!warnedExternalChange)
        {
            warnedExternalChange = true;
            Ui.SetStatus(statusLabel, L.T("The file changed outside the launcher. Saving keeps the other changes and writes only the fields edited here."), TextRole.Warn);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            watchTimer.Stop();
            watchTimer.Dispose();
        }
        base.Dispose(disposing);
    }
}

// One field of a settings page, chosen by the type of the value in the file (or a custom editor of the section).
internal sealed class SettingEditor
{
    private readonly Func<string, string> currentRaw;
    private readonly Func<bool> isChanged;
    private readonly Func<string?> validate;

    private SettingEditor(SettingsSection section, JsoncProperty property, Control control,
        Func<string, string> currentRaw, Func<bool> isChanged, Func<string?> validate)
    {
        Section = section;
        Property = property;
        Control = control;
        this.currentRaw = currentRaw;
        this.isChanged = isChanged;
        this.validate = validate;
    }

    public SettingsSection Section { get; }
    public JsoncProperty Property { get; }
    public Control Control { get; }
    public string Key => Property.Key;
    public bool IsChanged => isChanged();

    public event Action? Changed;

    // Raw JSON text of the value, with the line breaks of the file.
    public string CurrentRaw(string newline) => currentRaw(newline);

    // Error text, or null when the value can be saved.
    public string? Validate() => validate();

    public void NotifyChanged() => Changed?.Invoke();

    // Editor for a custom control: currentRaw gives the JSON text of the value, isChanged compares with the file.
    public static SettingEditor Custom(SettingsSection section, JsoncProperty property, Control control,
        Func<string> currentRaw, Func<bool> isChanged)
    {
        return new SettingEditor(section, property, control, _ => currentRaw(), isChanged, () => null);
    }

    public static SettingEditor Create(SettingsSection section, JsoncProperty property)
    {
        if (section.CustomEditors.TryGetValue(property.Key, out CustomEditorFactory? factory))
        {
            SettingEditor? custom = factory(section, property);
            if (custom != null) return custom;
        }

        string title = SettingLabels.Title(property.Key);
        switch (property.Kind)
        {
            case JsoncKind.Bool:
            {
                bool original = property.RawValue.Trim() == "true";
                var box = new CheckBox { Text = L.T("On"), Checked = original, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) };
                var editor = new SettingEditor(section, property, box,
                    _ => JsoncFile.FormatBool(box.Checked),
                    () => box.Checked != original,
                    () => null);
                box.CheckedChanged += (_, _) => editor.NotifyChanged();
                return editor;
            }

            case JsoncKind.Number:
            {
                string raw = property.RawValue.Trim();
                if (!decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal original))
                    return CreateRaw(section, property, title);
                bool asDecimal = raw.Contains('.') || raw.Contains('e') || raw.Contains('E');
                int dot = raw.IndexOf('.');
                int decimals = asDecimal ? Math.Clamp(dot >= 0 ? raw.Length - dot - 1 : 2, 2, 4) : 0;
                var number = new NumericUpDown
                {
                    Minimum = -1_000_000_000m,
                    Maximum = 1_000_000_000m,
                    DecimalPlaces = decimals,
                    Increment = asDecimal ? 0.1m : 1m,
                    Width = 130,
                    TextAlign = HorizontalAlignment.Right,
                    Anchor = AnchorStyles.Left,
                    Margin = new Padding(3, 3, 3, 3)
                };
                number.Value = Math.Clamp(original, number.Minimum, number.Maximum);
                var editor = new SettingEditor(section, property, number,
                    _ => JsoncFile.FormatNumber(number.Value, asDecimal),
                    () => number.Value != original,
                    () => null);
                number.ValueChanged += (_, _) => editor.NotifyChanged();
                return editor;
            }

            case JsoncKind.String:
            {
                string original = JsoncFile.ReadStringValue(property.RawValue) ?? "";
                string[]? choices = SettingLabels.For(property.Key).Choices;
                Control input;
                if (choices != null)
                {
                    var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 220, Anchor = AnchorStyles.Left };
                    combo.Items.AddRange(choices.Cast<object>().ToArray());
                    combo.Text = original;
                    input = combo;
                }
                else
                {
                    input = new TextBox { Text = original, Width = 300, Anchor = AnchorStyles.Left };
                }
                var editor = new SettingEditor(section, property, input,
                    _ => JsoncFile.FormatString(input.Text),
                    () => input.Text != original,
                    () => null);
                input.TextChanged += (_, _) => editor.NotifyChanged();
                return editor;
            }

            default:
                return CreateRaw(section, property, title);
        }
    }

    // Lists, objects and anything else: edited as JSON text.
    private static SettingEditor CreateRaw(SettingsSection section, JsoncProperty property, string title)
    {
        string original = Normalize(property.RawValue);
        int lines = original.Split('\n').Length;
        var box = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = false,
            Font = new Font("Consolas", 9f),
            Text = original.Replace("\n", "\r\n"),
            Height = Math.Clamp(lines * 15 + 12, 28, 200),
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 3, 3, 3)
        };
        var editor = new SettingEditor(section, property, box,
            newline => Normalize(box.Text).Replace("\n", newline),
            () => Normalize(box.Text) != original,
            () =>
            {
                try
                {
                    using JsonDocument parsed = JsonDocument.Parse(box.Text, JsoncFile.ParseOptions);
                    return null;
                }
                catch (JsonException e)
                {
                    return L.F("\"{0}\": the value is not valid JSON ({1})", title, e.Message);
                }
            });
        box.TextChanged += (_, _) => editor.NotifyChanged();
        return editor;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Trim();
}
