using System.Drawing.Drawing2D;

namespace SteambirdsLauncher.UI;

// A group of related controls on a raised plate with a title. Replaces the GroupBox: rounded, in the palette of the
// theme, with a short steam-yellow mark before the title.
internal class Card : Panel
{
    private string title = "";
    private Padding contentPadding = new Padding(14, 12, 14, 12);

    public Card()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = Theme.P.Surface;
        Margin = new Padding(0, 0, 0, 10);
        ApplyPadding();
    }

    public string Title
    {
        get => title;
        set
        {
            title = value;
            ApplyPadding();
            Invalidate();
        }
    }

    // Space between the edge of the card and its content (the title row is added on top).
    public Padding ContentPadding
    {
        get => contentPadding;
        set
        {
            contentPadding = value;
            ApplyPadding();
        }
    }

    private int HeaderHeight => title.Length == 0 ? 0 : TextRenderer.MeasureText("Ag", Theme.Heading(10f), Size.Empty, TextFormatFlags.NoPadding).Height + 10;

    private void ApplyPadding()
    {
        Padding = new Padding(contentPadding.Left, contentPadding.Top + HeaderHeight, contentPadding.Right, contentPadding.Bottom);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        ApplyPadding();
        base.OnFontChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette p = Theme.P;
        Graphics g = e.Graphics;
        g.Clear(Dpi.BackdropOf(this));
        Dpi.Smooth(g);

        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (GraphicsPath path = Dpi.Rounded(bounds, Dpi.Px(this, 8)))
        using (var fill = new SolidBrush(p.Surface))
        using (var border = new Pen(p.Border))
        {
            g.FillPath(fill, path);
            g.DrawPath(border, path);
        }

        if (title.Length == 0) return;
        int left = contentPadding.Left;
        int top = contentPadding.Top - 2;
        Font font = Theme.Heading(10f);
        int height = HeaderHeight - 8;
        using (var mark = new SolidBrush(p.Accent))
            g.FillRectangle(mark, left, top + Dpi.Px(this, 3), Dpi.Px(this, 3), Math.Max(8, height - Dpi.Px(this, 6)));
        TextRenderer.DrawText(g, title, font, new Rectangle(left + Dpi.Px(this, 10), top, Width - left * 2, height), p.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
