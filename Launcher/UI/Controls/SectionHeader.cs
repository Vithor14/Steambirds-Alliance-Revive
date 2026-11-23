namespace SteambirdsLauncher.UI;

// Heading of a part of a long screen: the name, a gray detail (the file it edits) and a thin line to the right edge.
internal sealed class SectionHeader : Control
{
    private string title = "";
    private string detail = "";

    public SectionHeader(string title, string detail = "")
    {
        this.title = title;
        this.detail = detail;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        TabStop = false;
        Margin = new Padding(0, 6, 0, 8);
        Height = LogicalToDeviceUnits(26);
    }

    public string Title
    {
        get => title;
        set
        {
            title = value;
            Invalidate();
        }
    }

    public string Detail
    {
        get => detail;
        set
        {
            detail = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette p = Theme.P;
        Graphics g = e.Graphics;
        g.Clear(Dpi.BackdropOf(this));
        Font font = Theme.Heading(11f);
        Size titleSize = TextRenderer.MeasureText(title, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, title, font, new Rectangle(0, 0, titleSize.Width + 2, Height), p.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        int x = titleSize.Width + Dpi.Px(this, 10);
        if (detail.Length > 0)
        {
            Size detailSize = TextRenderer.MeasureText(detail, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, detail, Font, new Rectangle(x, 0, detailSize.Width + 2, Height), p.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            x += detailSize.Width + Dpi.Px(this, 12);
        }
        using var line = new Pen(p.Border);
        g.DrawLine(line, x, Height / 2, Width, Height / 2);
    }
}
