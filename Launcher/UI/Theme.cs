using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SteambirdsLauncher.UI;

// Meaning of a text color; the actual color depends on the theme.
internal enum TextRole { Normal, Muted, Good, Bad, Warn }

// Light and dark themes. Theme.AttachWindow(form) colors a window and everything added to it later; Theme.Set switches
// the theme of every attached window at once. Labels with a meaning (gray notes, green/red status) keep their
// meaning through Ui.SetRole.
internal static class Theme
{
    internal sealed class Palette
    {
        public Color Back { get; init; }
        public Color Input { get; init; }
        public Color Text { get; init; }
        public Color Muted { get; init; }
        public Color Border { get; init; }
        public Color Button { get; init; }
        public Color ButtonHover { get; init; }
        public Color TabBack { get; init; }
        public Color TabSelected { get; init; }
        public Color Header { get; init; }
        public Color Selection { get; init; }
        public Color Good { get; init; }
        public Color Bad { get; init; }
        public Color Warn { get; init; }
    }

    private static readonly Palette Light = new Palette
    {
        Back = SystemColors.Control,
        Input = SystemColors.Window,
        Text = SystemColors.ControlText,
        Muted = SystemColors.GrayText,
        Border = SystemColors.ControlDark,
        Button = SystemColors.Control,
        ButtonHover = SystemColors.ControlLight,
        TabBack = SystemColors.Control,
        TabSelected = SystemColors.Window,
        Header = SystemColors.Control,
        Selection = SystemColors.Highlight,
        Good = Color.FromArgb(0, 120, 0),
        Bad = Color.FromArgb(190, 0, 0),
        Warn = Color.FromArgb(170, 105, 0)
    };

    private static readonly Palette DarkPalette = new Palette
    {
        Back = Color.FromArgb(32, 33, 36),
        Input = Color.FromArgb(45, 46, 50),
        Text = Color.FromArgb(225, 225, 225),
        Muted = Color.FromArgb(150, 150, 155),
        Border = Color.FromArgb(75, 76, 82),
        Button = Color.FromArgb(55, 56, 61),
        ButtonHover = Color.FromArgb(70, 72, 78),
        TabBack = Color.FromArgb(40, 41, 45),
        TabSelected = Color.FromArgb(58, 60, 66),
        Header = Color.FromArgb(50, 51, 56),
        Selection = Color.FromArgb(38, 79, 120),
        Good = Color.FromArgb(120, 210, 120),
        Bad = Color.FromArgb(255, 120, 110),
        Warn = Color.FromArgb(235, 190, 90)
    };

    private static readonly ConditionalWeakTable<Control, object> Attached = new ConditionalWeakTable<Control, object>();
    private static readonly ConditionalWeakTable<Control, object> Roles = new ConditionalWeakTable<Control, object>();
    private static readonly List<WeakReference<Form>> Windows = new List<WeakReference<Form>>();

    public static bool Dark { get; private set; }
    public static Palette P => Dark ? DarkPalette : Light;

    public static Color ColorOf(TextRole role) => role switch
    {
        TextRole.Muted => P.Muted,
        TextRole.Good => P.Good,
        TextRole.Bad => P.Bad,
        TextRole.Warn => P.Warn,
        _ => P.Text
    };

    // Switches the theme of every attached window.
    public static void Set(bool dark)
    {
        Dark = dark;
        foreach (WeakReference<Form> reference in Windows.ToList())
        {
            if (!reference.TryGetTarget(out Form? form) || form.IsDisposed)
            {
                Windows.Remove(reference);
                continue;
            }
            ApplyTree(form);
            ApplyTitleBar(form);
            form.Invalidate(true);
        }
    }

    // Colors a window and keeps coloring the controls added to it later.
    public static void AttachWindow(Form form)
    {
        Windows.Add(new WeakReference<Form>(form));
        Attach(form);
        if (form.IsHandleCreated) ApplyTitleBar(form);
        else form.HandleCreated += (_, _) => ApplyTitleBar(form);
    }

    public static void SetRole(Control control, TextRole role)
    {
        Roles.AddOrUpdate(control, role);
        control.ForeColor = ColorOf(role);
    }

    private static void Attach(Control control)
    {
        ApplyOne(control);
        if (Attached.TryGetValue(control, out _)) return;
        Attached.Add(control, true);
        control.ControlAdded += (_, e) =>
        {
            if (e.Control != null) Attach(e.Control);
        };
        foreach (Control child in control.Controls) Attach(child);
    }

    private static void ApplyTree(Control control)
    {
        ApplyOne(control);
        foreach (Control child in control.Controls) ApplyTree(child);
    }

    private static void ApplyOne(Control control)
    {
        Palette p = P;
        Color fore = Roles.TryGetValue(control, out object? role) ? ColorOf((TextRole)role) : p.Text;

        switch (control)
        {
            case ConsoleView:
                // The consoles are dark in both themes; only their toolbar follows the theme.
                control.BackColor = p.Back;
                return;
            case RichTextBox:
                return;
            case Button button:
                button.ForeColor = fore;
                if (Dark)
                {
                    button.FlatStyle = FlatStyle.Flat;
                    button.BackColor = p.Button;
                    button.FlatAppearance.BorderColor = p.Border;
                    button.FlatAppearance.MouseOverBackColor = p.ButtonHover;
                    button.UseVisualStyleBackColor = false;
                }
                else
                {
                    button.FlatStyle = FlatStyle.Standard;
                    button.BackColor = SystemColors.Control;
                    button.UseVisualStyleBackColor = true;
                }
                return;
            case TextBoxBase or NumericUpDown or ListBox or TreeView:
                control.BackColor = p.Input;
                control.ForeColor = fore;
                return;
            case ComboBox combo:
                combo.BackColor = p.Input;
                combo.ForeColor = fore;
                combo.FlatStyle = Dark ? FlatStyle.Flat : FlatStyle.Standard;
                return;
            case ListView list:
                list.BackColor = p.Input;
                list.ForeColor = fore;
                list.Invalidate();
                return;
            case DataGridView grid:
                ApplyGrid(grid, p);
                return;
            case TabControl tabs:
                tabs.BackColor = p.Back;
                tabs.Invalidate();
                return;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                page.BackColor = p.Back;
                page.ForeColor = fore;
                return;
            case StatusStrip strip:
                strip.RenderMode = ToolStripRenderMode.System;
                strip.BackColor = p.TabBack;
                strip.ForeColor = p.Text;
                foreach (ToolStripItem item in strip.Items) item.ForeColor = item.Tag is TextRole r ? ColorOf(r) : p.Text;
                return;
            case CheckBox or RadioButton or Label or GroupBox:
                control.ForeColor = fore;
                if (control is not Label || control.BackColor != Color.Transparent) control.BackColor = p.Back;
                return;
            default:
                control.BackColor = p.Back;
                control.ForeColor = fore;
                return;
        }
    }

    private static void ApplyGrid(DataGridView grid, Palette p)
    {
        grid.EnableHeadersVisualStyles = !Dark;
        grid.BackgroundColor = p.Input;
        grid.GridColor = p.Border;
        grid.DefaultCellStyle.BackColor = p.Input;
        grid.DefaultCellStyle.ForeColor = p.Text;
        grid.DefaultCellStyle.SelectionBackColor = p.Selection;
        grid.DefaultCellStyle.SelectionForeColor = Dark ? Color.White : SystemColors.HighlightText;
        grid.ColumnHeadersDefaultCellStyle.BackColor = p.Header;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = p.Text;
        grid.RowHeadersDefaultCellStyle.BackColor = p.Header;
        grid.RowHeadersDefaultCellStyle.ForeColor = p.Text;
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (column.ReadOnly) column.DefaultCellStyle.ForeColor = p.Muted;
            if (column is DataGridViewComboBoxColumn combo) combo.FlatStyle = FlatStyle.Flat;
        }
    }

    // ------------------------------------------------------------------ dark title bar (Windows 10 2004+ / 11)

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private static void ApplyTitleBar(Form form)
    {
        try
        {
            int on = Dark ? 1 : 0;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE; 19 on Windows 10 before 2004.
            if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));
        }
        catch
        {
            // Older Windows: the title bar stays light.
        }
    }
}

// Tab control drawn by the launcher: follows the theme and shows the tabs of features that are not implemented
// yet in gray (they cannot be selected; their tooltip says why).
internal sealed class ThemedTabControl : TabControl
{
    private static readonly object NotImplementedMarker = new object();

    public ThemedTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        ShowToolTips = true;
        Padding = new Point(14, 5);
    }

    public static void MarkNotImplemented(TabPage page, string reason)
    {
        page.Tag = NotImplementedMarker;
        page.ToolTipText = reason;
        page.Enabled = false;
    }

    public static bool IsNotImplemented(TabPage page) => ReferenceEquals(page.Tag, NotImplementedMarker);

    protected override void OnSelecting(TabControlCancelEventArgs e)
    {
        if (e.TabPage != null && IsNotImplemented(e.TabPage)) e.Cancel = true;
        base.OnSelecting(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette p = Theme.P;
        e.Graphics.Clear(p.Back);
        if (TabCount == 0) return;

        Rectangle page = DisplayRectangle;
        page.Inflate(2, 2);
        using (var border = new Pen(p.Border)) e.Graphics.DrawRectangle(border, page.X, page.Y, page.Width - 1, page.Height - 1);

        for (int i = 0; i < TabCount; i++)
        {
            Rectangle tab = GetTabRect(i);
            bool selected = i == SelectedIndex;
            if (!selected) tab.Inflate(0, -1);
            using (var back = new SolidBrush(selected ? p.TabSelected : p.TabBack)) e.Graphics.FillRectangle(back, tab);
            using (var border = new Pen(p.Border)) e.Graphics.DrawRectangle(border, tab.X, tab.Y, tab.Width - 1, tab.Height - 1);
            TabPage tabPage = TabPages[i];
            Color fore = IsNotImplemented(tabPage) ? p.Muted : p.Text;
            TextRenderer.DrawText(e.Graphics, tabPage.Text, Font, tab, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
