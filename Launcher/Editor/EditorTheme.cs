using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// Colors of the menus and tool strips of the editor. They are read every time something is drawn, so changing the
// launcher theme needs nothing more than a redraw.
internal sealed class EditorColorTable : ProfessionalColorTable
{
    private static Theme.Palette P => Theme.P;

    public EditorColorTable()
    {
        UseSystemColors = false;
    }

    public override Color ToolStripDropDownBackground => P.Input;
    public override Color ImageMarginGradientBegin => P.Input;
    public override Color ImageMarginGradientMiddle => P.Input;
    public override Color ImageMarginGradientEnd => P.Input;
    public override Color MenuBorder => P.Border;
    public override Color MenuItemBorder => P.Selection;
    public override Color MenuItemSelected => P.Selection;
    public override Color MenuItemSelectedGradientBegin => P.Selection;
    public override Color MenuItemSelectedGradientEnd => P.Selection;
    public override Color MenuItemPressedGradientBegin => P.Header;
    public override Color MenuItemPressedGradientMiddle => P.Header;
    public override Color MenuItemPressedGradientEnd => P.Header;
    public override Color MenuStripGradientBegin => P.TabBack;
    public override Color MenuStripGradientEnd => P.TabBack;
    public override Color ToolStripBorder => P.Border;
    public override Color ToolStripGradientBegin => P.TabBack;
    public override Color ToolStripGradientMiddle => P.TabBack;
    public override Color ToolStripGradientEnd => P.TabBack;
    public override Color ToolStripContentPanelGradientBegin => P.Back;
    public override Color ToolStripContentPanelGradientEnd => P.Back;
    public override Color ToolStripPanelGradientBegin => P.Back;
    public override Color ToolStripPanelGradientEnd => P.Back;
    public override Color SeparatorDark => P.Border;
    public override Color SeparatorLight => P.Border;
    public override Color CheckBackground => P.Selection;
    public override Color CheckSelectedBackground => P.Selection;
    public override Color CheckPressedBackground => P.Selection;
    public override Color ButtonSelectedBorder => P.Selection;
    public override Color ButtonSelectedGradientBegin => P.Selection;
    public override Color ButtonSelectedGradientMiddle => P.Selection;
    public override Color ButtonSelectedGradientEnd => P.Selection;
    public override Color ButtonSelectedHighlight => P.Selection;
    public override Color ButtonPressedBorder => P.Selection;
    public override Color ButtonPressedGradientBegin => P.Header;
    public override Color ButtonPressedGradientMiddle => P.Header;
    public override Color ButtonPressedGradientEnd => P.Header;
    public override Color ButtonPressedHighlight => P.Header;
    public override Color ButtonCheckedGradientBegin => P.Header;
    public override Color ButtonCheckedGradientMiddle => P.Header;
    public override Color ButtonCheckedGradientEnd => P.Header;
    public override Color ButtonCheckedHighlight => P.Header;
    public override Color StatusStripGradientBegin => P.TabBack;
    public override Color StatusStripGradientEnd => P.TabBack;
}

// Renderer that makes MenuStrip and its drop-down menus follow the light and dark themes (the launcher's Theme class
// does not reach menus, because the drop-downs are not children of the window).
internal sealed class EditorRenderer : ToolStripProfessionalRenderer
{
    public EditorRenderer() : base(new EditorColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.P.Text : Theme.P.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled != false ? Theme.P.Text : Theme.P.Muted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        base.OnRenderItemCheck(e);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown) base.OnRenderToolStripBorder(e);
    }
}

// Button of the tool palette. Drawn by the editor, so it follows the theme and can show a check mark.
internal sealed class ToolButton : Control
{
    private bool hover;
    private bool isChecked;
    private bool available = true;

    public ToolButton(string text)
    {
        Text = text;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size(116, 24);
        Margin = new Padding(2, 1, 2, 1);
        Cursor = Cursors.Hand;
        TabStop = false;
    }

    public bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value) return;
            isChecked = value;
            Invalidate();
        }
    }

    // False = the tool cannot be used yet (the map lacks something). The button stays enabled for Windows, so its tip
    // still shows (a disabled control gets no mouse messages and no tooltip), but it is drawn faint and does nothing.
    public bool Available
    {
        get => available;
        set
        {
            if (available == value) return;
            available = value;
            Cursor = value ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnClick(EventArgs e)
    {
        if (available) base.OnClick(e);
    }

    // Keyboard hint drawn at the right side ("S", "F"...).
    public string Hint { get; set; } = "";

    protected override void OnMouseEnter(EventArgs e)
    {
        hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette p = Theme.P;
        Color back = isChecked ? p.Selection : hover && available ? p.ButtonHover : p.Button;
        Color line = isChecked ? p.Selection : p.Border;
        if (!available)
        {
            back = Theme.Blend(p.Back, p.Button, 0.25f);
            line = Theme.Blend(p.Back, p.Border, 0.3f);
        }
        e.Graphics.Clear(p.Back);
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, bounds);
        using (var pen = new Pen(line)) e.Graphics.DrawRectangle(pen, bounds);

        Color fore = !available ? Theme.Blend(p.Back, p.Muted, 0.4f) : isChecked ? p.SelectionText : p.Text;
        var textBounds = new Rectangle(6, 0, Width - 12, Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (Hint.Length > 0)
        {
            TextRenderer.DrawText(e.Graphics, Hint, Font, textBounds, isChecked ? fore : p.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
