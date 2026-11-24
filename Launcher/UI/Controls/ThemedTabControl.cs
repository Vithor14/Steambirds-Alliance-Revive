namespace SteambirdsLauncher.UI;

// Something that shows one page at a time (tabs, the side navigation, the main header). The screenshot tool of the
// project walks every page through this.
internal interface IPageSwitcher
{
    int PageCount { get; }
    int SelectedPage { get; set; }
    string PageTitle(int index);
}

// Tab control drawn by the launcher: flat tabs with a steam-yellow line under the selected one, in the theme colors.
// Tabs of features that are not implemented yet show in gray and cannot be selected (their tooltip says why).
internal sealed class ThemedTabControl : TabControl
{
    private static readonly object NotImplementedMarker = new object();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<TabPage, string> PageIcons = new System.Runtime.CompilerServices.ConditionalWeakTable<TabPage, string>();
    private int hot = -1;

    public ThemedTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        ShowToolTips = true;
        Padding = new Point(16, 8);
        ItemSize = new Size(0, LogicalToDeviceUnits(34));
    }

    // Puts an icon (see Icons) before the name of a tab. The tab control reserves the room through an empty image.
    public void SetIcon(TabPage page, string glyph)
    {
        if (!Icons.Available || glyph.Length == 0) return;
        PageIcons.AddOrUpdate(page, glyph);
        if (ImageList == null)
        {
            var list = new ImageList { ImageSize = new Size(LogicalToDeviceUnits(22), 1) };
            list.Images.Add(new Bitmap(1, 1));
            ImageList = list;
        }
        page.ImageIndex = 0;
        Invalidate();
    }

    public static void MarkNotImplemented(TabPage page, string reason)
    {
        page.Tag = NotImplementedMarker;
        page.ToolTipText = reason;
        page.Enabled = false;
    }

    // A tab that cannot be used yet because the map (or whatever it edits) lacks something. The reason is its tooltip;
    // null opens the tab again.
    public static void SetLocked(TabPage page, string? reason, string openTip)
    {
        if (reason == null)
        {
            if (ReferenceEquals(page.Tag, NotImplementedMarker)) page.Tag = null;
            page.ToolTipText = openTip;
            page.Enabled = true;
            return;
        }
        page.Tag = NotImplementedMarker;
        page.ToolTipText = openTip.Length > 0 ? openTip + "\n" + reason : reason;
    }

    public static bool IsNotImplemented(TabPage page) => ReferenceEquals(page.Tag, NotImplementedMarker);

    protected override void OnSelecting(TabControlCancelEventArgs e)
    {
        if (e.TabPage != null && IsNotImplemented(e.TabPage)) e.Cancel = true;
        base.OnSelecting(e);
    }

    // The pages start right under the tab strip, with no frame around them.
    public override Rectangle DisplayRectangle
    {
        get
        {
            if (TabCount == 0) return base.DisplayRectangle;
            int top = StripBottom() + 1;
            return new Rectangle(0, top, Math.Max(0, Width), Math.Max(0, Height - top));
        }
    }

    private int StripBottom()
    {
        int bottom = 0;
        for (int i = 0; i < TabCount; i++) bottom = Math.Max(bottom, GetTabRect(i).Bottom);
        return bottom;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = -1;
        for (int i = 0; i < TabCount; i++)
        {
            if (GetTabRect(i).Contains(e.Location))
            {
                index = i;
                break;
            }
        }
        if (index != hot)
        {
            hot = index;
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (hot != -1)
        {
            hot = -1;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette p = Theme.P;
        Graphics g = e.Graphics;
        // TabControl ignores BackColor: the color comes from the theme.
        Color back = Theme.SurfaceFor(this);
        g.Clear(back);
        if (TabCount == 0) return;

        int bottom = StripBottom();
        using (var line = new Pen(p.Border)) g.DrawLine(line, 0, bottom, Width, bottom);

        int thickness = Math.Max(2, Dpi.Px(this, 2));
        for (int i = 0; i < TabCount; i++)
        {
            Rectangle tab = GetTabRect(i);
            bool selected = i == SelectedIndex;
            TabPage page = TabPages[i];
            Color fore = IsNotImplemented(page) ? Theme.Blend(back, p.Muted, 0.4f) : selected || i == hot ? p.Text : p.Muted;
            var area = new Rectangle(tab.X, tab.Y, tab.Width, tab.Height - thickness);
            if (PageIcons.TryGetValue(page, out string? glyph))
            {
                // The icon and the name, centered together.
                int slot = Dpi.Px(this, 21);
                int textWidth = TextRenderer.MeasureText(page.Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
                int x = tab.X + (tab.Width - slot - textWidth) / 2;
                Icons.Draw(g, glyph, new Rectangle(x, area.Y, slot - Dpi.Px(this, 6), area.Height), selected ? p.AccentText : fore, 11f);
                TextRenderer.DrawText(g, page.Text, Font, new Rectangle(x + slot, area.Y, textWidth + 4, area.Height), fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            }
            else
            {
                TextRenderer.DrawText(g, page.Text, Font, area, fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            if (selected)
            {
                using var accent = new SolidBrush(p.Accent);
                g.FillRectangle(accent, tab.X + Dpi.Px(this, 6), bottom - thickness, Math.Max(2, tab.Width - Dpi.Px(this, 12)), thickness);
            }
        }
    }
}
