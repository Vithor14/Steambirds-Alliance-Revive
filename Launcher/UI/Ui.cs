using System.Diagnostics;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// Small helpers to build the WinForms screens in code (no designer files, so every screen is plain C#).
internal static class Ui
{
    public static Label Text(string text) => new Label
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 7, 3, 3)
    };

    // One shared bold font (a new Font per label would leak GDI objects every time a page is rebuilt).
    public static Font BoldFont { get; } = new Font(Control.DefaultFont, FontStyle.Bold);

    public static Label Bold(string text)
    {
        Label label = Text(text);
        label.Font = BoldFont;
        return label;
    }

    public static Label Note(string text)
    {
        Label label = Text(text);
        SetRole(label, TextRole.Muted);
        return label;
    }

    // Label that wraps inside the given width (call again with a new width when the container is resized).
    public static Label Wrapping(string text, int width, bool gray)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = new Size(Math.Max(100, width), 0),
            Margin = new Padding(3, 2, 3, 6)
        };
        if (gray) SetRole(label, TextRole.Muted);
        return label;
    }

    // Text color with a meaning (gray note, green/red/yellow status) that survives theme changes.
    public static void SetRole(Control control, TextRole role) => Theme.SetRole(control, role);

    // Status text: changes the text and the color only when they changed (called every second).
    public static void SetStatus(Label label, string text, TextRole role)
    {
        if (label.Text != text) label.Text = text;
        Color color = Theme.ColorOf(role);
        if (label.ForeColor != color) SetRole(label, role);
    }

    public static ThemedButton Button(string text, EventHandler? onClick = null, ButtonKind kind = ButtonKind.Normal)
    {
        var button = new ThemedButton { Text = text, Kind = kind };
        if (onClick != null) button.Click += onClick;
        return button;
    }

    // The main action of a screen (steam yellow).
    public static ThemedButton Primary(string text, EventHandler? onClick = null) => Button(text, onClick, ButtonKind.Primary);

    // A destructive action (red text).
    public static ThemedButton Danger(string text, EventHandler? onClick = null) => Button(text, onClick, ButtonKind.Danger);

    // Small action with no plate until the mouse is over it.
    public static ThemedButton Quiet(string text, EventHandler? onClick = null) => Button(text, onClick, ButtonKind.Quiet);

    // Round "?" that opens a longer explanation.
    public static HelpButton Help(string text, string? title = null) => new HelpButton(text, title);

    // Label of a section inside a screen (bigger than a note, smaller than a card title).
    public static Label Heading(string text)
    {
        Label label = Text(text);
        label.Font = Theme.Heading(10.5f);
        label.Margin = new Padding(3, 10, 3, 4);
        return label;
    }

    // Gray "(coming soon)" tag for a feature that is not implemented in the project yet. Disabled controls do not
    // show tooltips in WinForms, so the explanation goes on this tag.
    public static Label ComingSoon(ToolTip tip, string reason)
    {
        Label label = Note(L.T("(coming soon)"));
        label.Font = new Font(Control.DefaultFont, FontStyle.Italic);
        label.Cursor = Cursors.Help;
        tip.SetToolTip(label, reason);
        return label;
    }

    // Disables the controls of a feature that is not implemented yet.
    public static void Disable(params Control[] controls)
    {
        foreach (Control control in controls) control.Enabled = false;
    }

    public static FlowLayoutPanel Flow(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        panel.Controls.AddRange(controls);
        return panel;
    }

    public static FlowLayoutPanel WrapFlow(params Control[] controls)
    {
        FlowLayoutPanel panel = Flow(controls);
        panel.WrapContents = true;
        return panel;
    }

    public static TableLayoutPanel Table(int columns)
    {
        return new TableLayoutPanel
        {
            ColumnCount = columns,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
    }

    // Adds a row of controls to a table (one control per column; null leaves the cell empty).
    public static int AddRow(TableLayoutPanel table, params Control?[] cells)
    {
        int row = table.RowCount;
        table.RowCount = row + 1;
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (int column = 0; column < cells.Length; column++)
        {
            Control? cell = cells[column];
            if (cell != null) table.Controls.Add(cell, column, row);
        }
        return row;
    }

    // A titled plate for a group of related controls (replaces the old GroupBox).
    public static Card Group(string title, Control content)
    {
        var card = new Card
        {
            Title = title,
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        // At the top, at its own height: when the card is taller (it shares a row with a taller one) the rows of a
        // table inside do not stretch.
        content.Dock = DockStyle.Top;
        card.Controls.Add(content);
        return card;
    }

    // A thin rounded frame around a list or any control that has no border of its own.
    public static Card Framed(Control content)
    {
        var card = new Card { ContentPadding = new Padding(4), Dock = DockStyle.Fill, Margin = Padding.Empty };
        content.Dock = DockStyle.Fill;
        card.Controls.Add(content);
        return card;
    }

    // Read-only list with columns. The last column stretches to the right edge.
    public static ThemedListView Grid(params (string Title, int Width)[] columns)
    {
        var list = new ThemedListView { Dock = DockStyle.Fill };
        foreach ((string title, int width) in columns) list.Columns.Add(title, width);
        return list;
    }

    // ------------------------------------------------------------------ dialogs and shell

    public static void ShowError(IWin32Window? owner, string text)
    {
        MessageBox.Show(owner, text, LauncherInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    public static void ShowInfo(IWin32Window? owner, string text)
    {
        MessageBox.Show(owner, text, LauncherInfo.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public static bool Confirm(IWin32Window? owner, string text)
    {
        return MessageBox.Show(owner, text, LauncherInfo.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }

    // Opens a web address in the default browser.
    public static void OpenUrl(IWin32Window? owner, string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            ShowError(owner, L.F("Could not open the link: {0}", e.Message) + "\n" + url);
        }
    }

    public static void OpenFolder(IWin32Window? owner, string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                ShowError(owner, L.F("The folder does not exist:\n{0}", path));
                return;
            }
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
        }
        catch (Exception e)
        {
            ShowError(owner, L.F("Could not open the folder: {0}", e.Message));
        }
    }

    // Opens a file with its default program (Notepad when there is none).
    public static void OpenFile(IWin32Window? owner, string path)
    {
        if (!File.Exists(path))
        {
            ShowError(owner, L.F("The file does not exist:\n{0}", path));
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true });
            }
            catch (Exception e)
            {
                ShowError(owner, L.F("Could not open the file: {0}", e.Message));
            }
        }
    }
}
