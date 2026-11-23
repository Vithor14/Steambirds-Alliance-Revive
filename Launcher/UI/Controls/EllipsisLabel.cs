namespace SteambirdsLauncher.UI;

// One line of text that never wraps: when it is too long it is cut in the middle ("c:\program files\...\depot"), so a
// folder path takes one row whatever the window width.
internal sealed class EllipsisLabel : Control
{
    public EllipsisLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        TabStop = false;
        Margin = new Padding(3, 4, 3, 2);
    }

#pragma warning disable CS8765 // the base setter accepts null; this one turns it into an empty text
    public override string Text
    {
        get => base.Text;
        set
        {
            value ??= "";
            if (base.Text == value) return;
            base.Text = value;
            Invalidate();
        }
    }
#pragma warning restore CS8765

    protected override Size DefaultSize => new Size(200, 24);

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText("Ag", Font, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(Math.Max(10, proposedSize.Width > 0 ? proposedSize.Width : Width), text.Height + Dpi.Px(this, 10));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Dpi.BackdropOf(this));
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.PathEllipsis |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }
}
