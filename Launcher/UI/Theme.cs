using System.Runtime.CompilerServices;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.UI;

// Meaning of a text color; the actual color depends on the theme.
internal enum TextRole { Normal, Muted, Good, Bad, Warn }

// The launcher's look. Two themes with the colors of the project branding (Branding\README.md): the black plate
// (#151019), the steam yellow (#F8BA24) and the ember orange (#F2622A). The dark theme is the plate; the light one is
// parchment with the same black header. Nothing here comes from the game's own art.
//
// Theme.AttachWindow(form) colors a window and everything added to it later; Theme.Set switches the theme of every
// attached window at once. Labels with a meaning (gray notes, green/red status) keep it through Ui.SetRole.
internal static class Theme
{
    internal sealed class Palette
    {
        // Surfaces, from the deepest to the highest.
        public Color Back { get; init; }          // window and tab pages
        public Color Surface { get; init; }       // cards
        public Color SurfaceAlt { get; init; }    // headers of lists, hovered rows, buttons
        public Color Input { get; init; }         // text boxes, lists, combo boxes (sunk in the surface)
        public Color NavBack { get; init; }       // header of the main window

        // Text.
        public Color Text { get; init; }
        public Color Muted { get; init; }
        public Color NavText { get; init; }
        public Color NavMuted { get; init; }

        // Lines and controls.
        public Color Border { get; init; }
        public Color BorderStrong { get; init; }
        public Color Button { get; init; }
        public Color ButtonHover { get; init; }
        public Color TabBack { get; init; }
        public Color TabSelected { get; init; }
        public Color Header { get; init; }
        public Color Selection { get; init; }
        public Color SelectionText { get; init; }

        // Brand colors.
        public Color Accent { get; init; }        // steam yellow: fills
        public Color AccentHover { get; init; }
        public Color AccentPressed { get; init; }
        public Color AccentInk { get; init; }     // text on the accent
        public Color AccentText { get; init; }    // the accent as text or a line on Back / Surface
        public Color Ember { get; init; }

        // Meanings.
        public Color Good { get; init; }
        public Color Bad { get; init; }
        public Color Warn { get; init; }
    }

    private static readonly Palette Light = new Palette
    {
        Back = Color.FromArgb(242, 237, 225),
        Surface = Color.FromArgb(251, 248, 241),
        SurfaceAlt = Color.FromArgb(233, 226, 210),
        Input = Color.FromArgb(255, 255, 255),
        NavBack = Color.FromArgb(21, 16, 25),
        Text = Color.FromArgb(35, 29, 43),
        Muted = Color.FromArgb(111, 102, 121),
        NavText = Color.FromArgb(238, 232, 221),
        NavMuted = Color.FromArgb(154, 145, 168),
        Border = Color.FromArgb(210, 201, 181),
        BorderStrong = Color.FromArgb(179, 168, 143),
        Button = Color.FromArgb(239, 233, 218),
        ButtonHover = Color.FromArgb(229, 220, 199),
        TabBack = Color.FromArgb(242, 237, 225),
        TabSelected = Color.FromArgb(251, 248, 241),
        Header = Color.FromArgb(233, 226, 210),
        Selection = Color.FromArgb(246, 220, 142),
        SelectionText = Color.FromArgb(35, 29, 43),
        Accent = Color.FromArgb(248, 186, 36),
        AccentHover = Color.FromArgb(255, 201, 71),
        AccentPressed = Color.FromArgb(224, 164, 16),
        AccentInk = Color.FromArgb(21, 16, 25),
        AccentText = Color.FromArgb(148, 92, 0),
        Ember = Color.FromArgb(242, 98, 42),
        Good = Color.FromArgb(31, 122, 62),
        Bad = Color.FromArgb(179, 38, 30),
        Warn = Color.FromArgb(180, 84, 10)
    };

    private static readonly Palette DarkPalette = new Palette
    {
        Back = Color.FromArgb(24, 19, 32),
        Surface = Color.FromArgb(33, 27, 43),
        SurfaceAlt = Color.FromArgb(43, 36, 55),
        Input = Color.FromArgb(20, 16, 27),
        NavBack = Color.FromArgb(14, 10, 19),
        Text = Color.FromArgb(238, 232, 221),
        Muted = Color.FromArgb(165, 156, 179),
        NavText = Color.FromArgb(238, 232, 221),
        NavMuted = Color.FromArgb(154, 145, 168),
        Border = Color.FromArgb(59, 50, 72),
        BorderStrong = Color.FromArgb(90, 79, 107),
        Button = Color.FromArgb(43, 36, 55),
        ButtonHover = Color.FromArgb(56, 47, 71),
        TabBack = Color.FromArgb(24, 19, 32),
        TabSelected = Color.FromArgb(33, 27, 43),
        Header = Color.FromArgb(43, 36, 55),
        Selection = Color.FromArgb(94, 71, 14),
        SelectionText = Color.White,
        Accent = Color.FromArgb(248, 186, 36),
        AccentHover = Color.FromArgb(255, 204, 77),
        AccentPressed = Color.FromArgb(217, 160, 15),
        AccentInk = Color.FromArgb(21, 16, 25),
        AccentText = Color.FromArgb(248, 186, 36),
        Ember = Color.FromArgb(242, 98, 42),
        Good = Color.FromArgb(125, 214, 143),
        Bad = Color.FromArgb(255, 111, 97),
        Warn = Color.FromArgb(255, 162, 76)
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

    // Mix of two colors (0 = a, 1 = b).
    public static Color Blend(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * amount),
            (int)Math.Round(a.G + (b.G - a.G) * amount),
            (int)Math.Round(a.B + (b.B - a.B) * amount));
    }

    // The background a control sits on: a card is higher than the window.
    public static Color SurfaceFor(Control control)
    {
        for (Control? parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is Card) return P.Surface;
            if (parent is Form or TabPage) return P.Back;
        }
        return P.Back;
    }

    // ------------------------------------------------------------------ fonts

    private static readonly Dictionary<string, Font> FontCache = new Dictionary<string, Font>();

    // Font of titles and the brand: Bahnschrift (an industrial, DIN-like face that ships with Windows 10) when the PC has it.
    public static Font Heading(float points, FontStyle style = FontStyle.Regular)
    {
        string family = HeadingFamily;
        string key = family + "|" + points + "|" + (int)style;
        lock (FontCache)
        {
            if (!FontCache.TryGetValue(key, out Font? font))
            {
                font = new Font(family, points, style, GraphicsUnit.Point);
                FontCache[key] = font;
            }
            return font;
        }
    }

    private static string? headingFamily;

    private static string HeadingFamily
    {
        get
        {
            if (headingFamily != null) return headingFamily;
            using var families = new System.Drawing.Text.InstalledFontCollection();
            string[] names = families.Families.Select(f => f.Name).ToArray();
            headingFamily = names.Contains("Bahnschrift SemiBold") ? "Bahnschrift SemiBold"
                : names.Contains("Segoe UI Semibold") ? "Segoe UI Semibold" : "Segoe UI";
            return headingFamily;
        }
    }

    // ------------------------------------------------------------------ switching

    // Switches the theme of every attached window.
    public static void Set(bool dark)
    {
        Dark = dark;
        NativeTheme.SetDarkApp(dark);
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
        ApplyTree(control);
        Hook(control);
    }

    private static void Hook(Control control)
    {
        if (Attached.TryGetValue(control, out _)) return;
        Attached.Add(control, true);
        control.ControlAdded += (_, e) =>
        {
            if (e.Control != null) Attach(e.Control);
        };
        foreach (Control child in control.Controls) Hook(child);
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
        Color surface = SurfaceFor(control);

        switch (control)
        {
            case Form form:
                form.BackColor = p.Back;
                form.ForeColor = p.Text;
                return;
            case ConsoleView:
                // The consoles are dark in both themes; only their toolbar follows the theme.
                control.BackColor = surface;
                return;
            case RichTextBox:
                NativeTheme.StyleScrollBars(control);
                return;
            case EllipsisLabel path:
                path.ForeColor = fore;
                path.Invalidate();
                return;
            case ThemedButton or ThemedCheckBox or HelpButton or NavPages or MainNav or Card or ThemedTabControl or ChipList or StatusPill:
                // Drawn by the launcher from the palette: a redraw is enough.
                if (control is Card) control.BackColor = p.Surface;
                else if (control is ThemedTabControl) control.BackColor = surface;
                control.Invalidate();
                return;
            case Button button:
                button.ForeColor = fore;
                button.FlatStyle = FlatStyle.Flat;
                button.BackColor = p.Button;
                button.FlatAppearance.BorderColor = p.Border;
                button.FlatAppearance.MouseOverBackColor = p.ButtonHover;
                button.UseVisualStyleBackColor = false;
                return;
            case NumberBox number:
                number.BackColor = p.Input;
                number.ForeColor = fore;
                number.Invalidate();
                return;
            case TextBoxBase text:
                text.BackColor = p.Input;
                text.ForeColor = fore;
                if (text.Multiline) NativeTheme.StyleScrollBars(text);
                text.Invalidate();
                return;
            case ListBox or TreeView:
                control.BackColor = p.Input;
                control.ForeColor = fore;
                if (control is TreeView tree) tree.LineColor = p.Border;
                NativeTheme.StyleScrollBars(control);
                return;
            case ComboBox combo:
                combo.BackColor = p.Input;
                combo.ForeColor = fore;
                combo.FlatStyle = FlatStyle.Flat;
                combo.Invalidate();
                return;
            case ListView list:
                list.BackColor = p.Input;
                list.ForeColor = fore;
                NativeTheme.StyleScrollBars(list);
                list.Invalidate();
                return;
            case DataGridView grid:
                ApplyGrid(grid, p);
                return;
            case TabControl tabs:
                tabs.BackColor = surface;
                tabs.Invalidate();
                return;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                page.BackColor = surface;
                page.ForeColor = fore;
                return;
            case ToolStrip strip:
                // StatusStrip, MenuStrip and the other tool strips.
                strip.RenderMode = ToolStripRenderMode.ManagerRenderMode;
                strip.Renderer = new EditorRenderer();
                strip.BackColor = p.Back;
                strip.ForeColor = p.Text;
                foreach (ToolStripItem item in strip.Items) item.ForeColor = item.Tag is TextRole r ? ColorOf(r) : p.Text;
                return;
            case CheckBox or RadioButton or Label or GroupBox:
                control.ForeColor = fore;
                control.BackColor = surface;
                return;
            case SplitContainer split:
                split.BackColor = p.Border;
                split.Panel1.BackColor = surface;
                split.Panel2.BackColor = surface;
                return;
            case ScrollableControl scroll:
                control.BackColor = surface;
                control.ForeColor = fore;
                if (scroll.AutoScroll) NativeTheme.StyleScrollBars(control);
                return;
            default:
                control.BackColor = surface;
                control.ForeColor = fore;
                return;
        }
    }

    private static void ApplyGrid(DataGridView grid, Palette p)
    {
        grid.EnableHeadersVisualStyles = false;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.BackgroundColor = p.Input;
        grid.GridColor = p.Border;
        grid.DefaultCellStyle.BackColor = p.Input;
        grid.DefaultCellStyle.ForeColor = p.Text;
        grid.DefaultCellStyle.SelectionBackColor = p.Selection;
        grid.DefaultCellStyle.SelectionForeColor = p.SelectionText;
        grid.ColumnHeadersDefaultCellStyle.BackColor = p.Header;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = p.Text;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = p.Header;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = p.Text;
        grid.RowHeadersDefaultCellStyle.BackColor = p.Header;
        grid.RowHeadersDefaultCellStyle.ForeColor = p.Text;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = p.Header;
        grid.RowHeadersDefaultCellStyle.SelectionForeColor = p.Text;
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (column.ReadOnly) column.DefaultCellStyle.ForeColor = p.Muted;
            if (column is DataGridViewComboBoxColumn combo) combo.FlatStyle = FlatStyle.Flat;
        }
        NativeTheme.StyleScrollBars(grid);
    }

    // ------------------------------------------------------------------ tooltips

    // Tooltips drawn in the palette of the theme (the system ones are always bright yellow).
    public static void StyleToolTip(ToolTip tip)
    {
        tip.OwnerDraw = true;
        const TextFormatFlags flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top;
        tip.Popup += (_, e) =>
        {
            string text = e.AssociatedControl == null ? "" : tip.GetToolTip(e.AssociatedControl) ?? "";
            Size size = TextRenderer.MeasureText(text, Control.DefaultFont, new Size(420, 0), flags);
            e.ToolTipSize = new Size(size.Width + 20, size.Height + 14);
        };
        tip.Draw += (_, e) =>
        {
            Palette p = P;
            e.Graphics.Clear(Blend(p.Surface, p.SurfaceAlt, 0.5f));
            using (var border = new Pen(Blend(p.Border, p.Accent, 0.35f))) e.Graphics.DrawRectangle(border, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, Control.DefaultFont, new Rectangle(10, 7, e.Bounds.Width - 20, e.Bounds.Height - 14), p.Text, flags);
        };
    }

    // ------------------------------------------------------------------ title bar

    private static void ApplyTitleBar(Form form)
    {
        if (form is MainForm)
        {
            // The header of the main window is plate black in both themes, so its title bar is dark in both too.
            NativeTheme.DarkTitleBar(form.Handle, true);
            NativeTheme.ColorTitleBar(form.Handle, P.NavBack, P.NavText, P.NavBack);
            return;
        }
        NativeTheme.DarkTitleBar(form.Handle, Dark);
        NativeTheme.ColorTitleBar(form.Handle, P.Back, P.Text, P.Border);
    }
}
