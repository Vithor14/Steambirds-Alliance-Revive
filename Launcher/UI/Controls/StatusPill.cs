using System.Drawing.Drawing2D;

namespace SteambirdsLauncher.UI;

// A dot and a short text on a rounded plate tinted with the color of the state ("Running", "Not installed"...).
internal sealed class StatusPill : Control
{
    private string text = "";
    private TextRole role = TextRole.Muted;

    public StatusPill()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Margin = new Padding(3, 4, 3, 3);
        TabStop = false;
    }

    // Changes the text and the state, redrawing only when something changed (it is called every second).
    public void Set(string newText, TextRole newRole)
    {
        if (text == newText && role == newRole) return;
        bool widthChanged = text != newText;
        text = newText;
        role = newRole;
        if (widthChanged) UpdateSize();
        Invalidate();
    }

    public override string Text => text;

    private void UpdateSize()
    {
        Size size = Measure(this, text, Font);
        if (size != Size) Size = size;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        UpdateSize();
    }

    public override Size GetPreferredSize(Size proposedSize) => Measure(this, text, Font);

    public static Size Measure(Control scale, string text, Font font)
    {
        Size t = TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        return new Size(t.Width + Dpi.Px(scale, 34), Math.Max(Dpi.Px(scale, 24), t.Height + Dpi.Px(scale, 8)));
    }

    // Draws a pill at the given place (the main header draws its own on a dark background).
    public static void Draw(Graphics g, Control scale, Rectangle bounds, string text, TextRole role, Font font, Color backdrop, Color textColor)
    {
        Theme.Palette p = Theme.P;
        Color tone = role == TextRole.Normal ? p.Muted : Theme.ColorOf(role);
        Color fill = Theme.Blend(backdrop, tone, 0.14f);
        Color border = Theme.Blend(backdrop, tone, 0.38f);
        Dpi.Smooth(g);
        var rect = new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        using (GraphicsPath path = Dpi.Rounded(rect, rect.Height / 2))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }
        int dot = Dpi.Px(scale, 8);
        using (var dotBrush = new SolidBrush(tone))
            g.FillEllipse(dotBrush, rect.X + Dpi.Px(scale, 11), rect.Y + (rect.Height - dot) / 2, dot, dot);
        var textBounds = new Rectangle(rect.X + Dpi.Px(scale, 24), rect.Y, rect.Width - Dpi.Px(scale, 28), rect.Height);
        TextRenderer.DrawText(g, text, font, textBounds, textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Color backdrop = Dpi.BackdropOf(this);
        e.Graphics.Clear(backdrop);
        Draw(e.Graphics, this, new Rectangle(0, 0, Width, Height), text, role, Font, backdrop, Theme.P.Text);
    }
}
