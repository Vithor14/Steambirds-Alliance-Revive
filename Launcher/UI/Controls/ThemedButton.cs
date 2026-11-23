using System.Drawing.Drawing2D;

namespace SteambirdsLauncher.UI;

internal enum ButtonKind
{
    Normal,   // sunk plate
    Primary,  // steam yellow: the one main action of a screen (Play, Start server, Save)
    Danger,   // red text, fills on hover (remove, delete)
    Quiet     // no plate until the mouse is over it (small actions inside lists and toolbars)
}

// Button drawn by the launcher: rounded, in the palette of the theme, with a hover and a pressed state.
internal sealed class ThemedButton : Button
{
    private ButtonKind kind;
    private bool hover;
    private bool pressed;
    private Font? boldFont;

    public ThemedButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = new Padding(3);
        Cursor = Cursors.Hand;
    }

    public ButtonKind Kind
    {
        get => kind;
        set
        {
            if (kind == value) return;
            kind = value;
            Invalidate();
        }
    }

    // Fixed look for the big call-to-action buttons.
    public float TextPoints { get; set; }

    private Font TextFont
    {
        get
        {
            if (TextPoints <= 0 && kind != ButtonKind.Primary) return Font;
            boldFont ??= new Font(Font.FontFamily, TextPoints > 0 ? TextPoints : Font.SizeInPoints, FontStyle.Bold, GraphicsUnit.Point);
            return boldFont;
        }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        boldFont?.Dispose();
        boldFont = null;
        base.OnFontChanged(e);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, TextFont, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | (UseMnemonic ? 0 : TextFormatFlags.NoPrefix));
        int height = Math.Max(Dpi.Px(this, 30), text.Height + Dpi.Px(this, 14));
        int width = Math.Max(Text.Length == 0 ? height : Dpi.Px(this, 68), text.Width + Dpi.Px(this, 30));
        return new Size(width + Padding.Horizontal, height + Padding.Vertical);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = false;
        pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        if (mevent.Button == MouseButtons.Left)
        {
            pressed = true;
            Invalidate();
        }
        base.OnMouseDown(mevent);
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        pressed = false;
        Invalidate();
        base.OnMouseUp(mevent);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette p = Theme.P;
        Graphics g = e.Graphics;
        Color backdrop = Dpi.BackdropOf(this);
        g.Clear(backdrop);
        Dpi.Smooth(g);

        bool live = Enabled;
        Color fill, border, fore;
        switch (kind)
        {
            case ButtonKind.Primary:
                fill = pressed ? p.AccentPressed : hover ? p.AccentHover : p.Accent;
                border = fill;
                fore = p.AccentInk;
                if (!live)
                {
                    fill = Theme.Blend(backdrop, p.Accent, 0.28f);
                    border = fill;
                    fore = Theme.Blend(fill, p.AccentInk, 0.55f);
                }
                break;
            case ButtonKind.Danger:
                fill = hover ? Theme.Blend(p.Button, p.Bad, pressed ? 0.35f : 0.20f) : p.Button;
                border = Theme.Blend(p.Border, p.Bad, hover ? 0.85f : 0.45f);
                fore = p.Bad;
                if (!live)
                {
                    fill = Theme.Blend(backdrop, p.Button, 0.5f);
                    border = Theme.Blend(backdrop, p.Border, 0.6f);
                    fore = Theme.Blend(backdrop, p.Muted, 0.7f);
                }
                break;
            case ButtonKind.Quiet:
                fill = hover ? (pressed ? p.ButtonHover : p.Button) : backdrop;
                border = fill;
                fore = p.Text;
                if (!live) fore = Theme.Blend(backdrop, p.Muted, 0.7f);
                break;
            default:
                fill = pressed ? Theme.Blend(p.ButtonHover, p.Text, 0.08f) : hover ? p.ButtonHover : p.Button;
                border = hover ? p.BorderStrong : p.Border;
                fore = p.Text;
                if (!live)
                {
                    fill = Theme.Blend(backdrop, p.Button, 0.5f);
                    border = Theme.Blend(backdrop, p.Border, 0.6f);
                    fore = Theme.Blend(backdrop, p.Muted, 0.7f);
                }
                break;
        }

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = Dpi.Px(this, 6);
        using (GraphicsPath path = Dpi.Rounded(bounds, radius))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            if (border != fill) g.DrawPath(pen, path);
        }

        Rectangle textBounds = Rectangle.Inflate(ClientRectangle, -Dpi.Px(this, 6), 0);
        textBounds.Offset(0, pressed && live ? 1 : 0);
        TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                                TextFormatFlags.EndEllipsis | (UseMnemonic ? 0 : TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Text, TextFont, textBounds, fore, flags);

        if (Focused && ShowFocusCues && live)
        {
            Rectangle ring = Rectangle.Inflate(bounds, -Dpi.Px(this, 3), -Dpi.Px(this, 3));
            using GraphicsPath path = Dpi.Rounded(ring, Math.Max(2, radius - 2));
            using var pen = new Pen(kind == ButtonKind.Primary ? p.AccentInk : p.AccentText, 1.4f);
            g.DrawPath(pen, path);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) boldFont?.Dispose();
        base.Dispose(disposing);
    }
}
