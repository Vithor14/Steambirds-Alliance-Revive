using System.Drawing.Drawing2D;

namespace SteambirdsLauncher.UI;

// Small round "?" button. Click it to read a longer explanation in a floating note, so the screens stay short.
internal sealed class HelpButton : Control
{
    private bool hover;
    private ToolStripDropDown? open;

    public HelpButton(string text, string? title = null)
    {
        HelpText = text;
        HelpTitle = title ?? "";
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        TabStop = false;
        Margin = new Padding(4, 4, 3, 3);
        Size = new Size(LogicalToDeviceUnits(20), LogicalToDeviceUnits(20));
    }

    public string HelpText { get; set; }
    public string HelpTitle { get; set; }

    protected override Size DefaultSize => new Size(20, 20);

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

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Toggle();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) Toggle();
        base.OnKeyDown(e);
    }

    private void Toggle()
    {
        if (open != null && !open.IsDisposed && open.Visible)
        {
            open.Close();
            return;
        }
        var panel = new HelpPanel(HelpTitle, HelpText, DeviceDpi);
        var host = new ToolStripControlHost(panel) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = panel.Size };
        var drop = new ToolStripDropDown
        {
            AutoClose = true,
            Padding = Padding.Empty,
            DropShadowEnabled = true,
            BackColor = Theme.P.Surface
        };
        drop.Items.Add(host);
        drop.Closed += (_, _) => BeginInvoke(new Action(() => drop.Dispose()));
        open = drop;

        Point at = PointToScreen(new Point(0, Height + 4));
        Rectangle area = Screen.FromControl(this).WorkingArea;
        if (at.X + panel.Width > area.Right) at.X = Math.Max(area.Left, area.Right - panel.Width - 8);
        if (at.Y + panel.Height > area.Bottom) at.Y = Math.Max(area.Top, PointToScreen(Point.Empty).Y - panel.Height - 4);
        drop.Show(at);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette p = Theme.P;
        Graphics g = e.Graphics;
        g.Clear(Dpi.BackdropOf(this));
        Dpi.Smooth(g);
        bool lit = hover || (open != null && !open.IsDisposed && open.Visible);
        var circle = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var fill = new SolidBrush(lit ? p.ButtonHover : p.Button)) g.FillEllipse(fill, circle);
        using (var pen = new Pen(lit ? p.Accent : p.BorderStrong)) g.DrawEllipse(pen, circle);
        TextRenderer.DrawText(g, "?", Theme.Heading(8.5f), ClientRectangle, lit ? p.AccentText : p.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    // The floating note.
    private sealed class HelpPanel : Control
    {
        private readonly string title;
        private readonly string body;
        private readonly int pad;

        public HelpPanel(string title, string body, int dpi)
        {
            this.title = title;
            this.body = body;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            float scale = Math.Max(1f, dpi / 96f);
            pad = (int)Math.Round(14 * scale);
            int width = (int)Math.Round(400 * scale);
            int textWidth = width - pad * 2;
            int titleHeight = title.Length == 0 ? 0 : TextRenderer.MeasureText(title, Theme.Heading(10f), new Size(textWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height + (int)(6 * scale);
            int bodyHeight = TextRenderer.MeasureText(body, Font, new Size(textWidth, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            Size = new Size(width, pad * 2 + titleHeight + bodyHeight);
            titleBlock = titleHeight;
        }

        private readonly int titleBlock;

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Palette p = Theme.P;
            Graphics g = e.Graphics;
            g.Clear(p.Surface);
            using (var border = new Pen(Theme.Blend(p.Border, p.Accent, 0.35f))) g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            using (var bar = new SolidBrush(p.Accent)) g.FillRectangle(bar, 0, 0, 3, Height);
            int textWidth = Width - pad * 2;
            if (title.Length > 0)
                TextRenderer.DrawText(g, title, Theme.Heading(10f), new Rectangle(pad, pad, textWidth, titleBlock), p.Text,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, body, Font, new Rectangle(pad, pad + titleBlock, textWidth, Height - pad * 2 - titleBlock), p.Text,
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
    }
}
