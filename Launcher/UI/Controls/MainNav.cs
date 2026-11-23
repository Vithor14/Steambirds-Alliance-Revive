using System.Drawing.Drawing2D;
using System.Reflection;

namespace SteambirdsLauncher.UI;

// Header of the main window: the project mark and name on the left, the main tabs, and the state of the server and the
// game on the right. Always plate black, in both themes. The page of the selected tab fills the rest of the window.
internal sealed class MainNav : Control, IPageSwitcher
{
    internal enum Side { Left, Right }

    private sealed class Item
    {
        public string Title = "";
        public Control Page = null!;
        public Side Side;
        public bool DividerBefore;
        public string Icon = "";
    }

    private sealed class HeaderLayout
    {
        public Rectangle Emblem;
        public Point BrandText;
        public readonly List<Rectangle> Items = new List<Rectangle>();
        public readonly List<int> Dividers = new List<int>();
        public readonly List<Rectangle> Pills = new List<Rectangle>();
        public bool CompactPills;
        public bool ShowBrand = true;
    }

    private readonly List<Item> items = new List<Item>();
    private readonly string[] pillText = { "", "" };
    private readonly TextRole[] pillRole = { TextRole.Muted, TextRole.Muted };
    private readonly string[] pillShort = { "", "" };
    private int selected = -1;
    private int hot = -1;
    private Bitmap? emblem;
    private Size emblemSize;

    public event EventHandler? SelectedPageChanged;

    public MainNav()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.ContainerControl, true);
        TabStop = true;
        Padding = new Padding(0, LogicalToDeviceUnits(HeaderHeight96), 0, 0);
    }

    private const int HeaderHeight96 = 58;

    private int HeaderHeight => Dpi.Px(this, HeaderHeight96);

    // A plain Control does not take its Padding into account when it docks its children: the pages start under the header.
    public override Rectangle DisplayRectangle
    {
        get
        {
            Rectangle area = base.DisplayRectangle;
            return new Rectangle(area.X + Padding.Left, area.Y + Padding.Top, Math.Max(0, area.Width - Padding.Horizontal), Math.Max(0, area.Height - Padding.Vertical));
        }
    }

    // "0.4.0" in "REVIVE · 0.4.0".
    public string Version { get; set; } = "";

    // ------------------------------------------------------------------ pages

    public void AddPage(string title, Control page, Side side = Side.Left, bool dividerBefore = false, string icon = "")
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        Controls.Add(page);
        items.Add(new Item { Title = title, Page = page, Side = side, DividerBefore = dividerBefore, Icon = icon });
        if (selected < 0) Select(items.Count - 1);
        Invalidate();
    }

    public int PageCount => items.Count;

    public int SelectedPage
    {
        get => selected;
        set => Select(value);
    }

    public string PageTitle(int index) => index >= 0 && index < items.Count ? items[index].Title : "";

    public void Select(int index)
    {
        if (index == selected || index < 0 || index >= items.Count) return;
        if (selected >= 0) items[selected].Page.Visible = false;
        selected = index;
        items[index].Page.Visible = true;
        Invalidate();
        SelectedPageChanged?.Invoke(this, EventArgs.Empty);
    }

    // Slot 0 = server, slot 1 = game. "shortText" is shown when the window is too narrow for the full text.
    public void SetStatus(int slot, string text, string shortText, TextRole role)
    {
        if (pillText[slot] == text && pillRole[slot] == role && pillShort[slot] == shortText) return;
        pillText[slot] = text;
        pillShort[slot] = shortText;
        pillRole[slot] = role;
        Invalidate(new Rectangle(0, 0, Width, HeaderHeight));
    }

    // ------------------------------------------------------------------ layout

    private Font ItemFont => Theme.Heading(10.5f);
    private Font BrandFont => Theme.Heading(11.5f);
    private Font SmallFont => Theme.Heading(8f);

    // The header gives up things in steps until everything fits: first the pills shrink to a dot and a word, then the
    // tabs get closer, then the name goes away and only the emblem stays.
    private HeaderLayout Arrange()
    {
        HeaderLayout layout = Build(0);
        for (int stage = 1; stage <= 3 && !Fits(layout); stage++) layout = Build(stage);
        return layout;
    }

    // The last tab on the left must end before the first pill.
    private bool Fits(HeaderLayout layout)
    {
        int lastLeftEnd = 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Side == Side.Left) lastLeftEnd = Math.Max(lastLeftEnd, layout.Items[i].Right);
        }
        return lastLeftEnd + Dpi.Px(this, 12) <= layout.Pills[0].X;
    }

    private HeaderLayout Build(int stage)
    {
        var layout = new HeaderLayout { CompactPills = stage >= 1, ShowBrand = stage < 3 };
        int itemPadding = Dpi.Px(this, stage >= 2 ? 22 : 34);
        int h = HeaderHeight;
        int x = Dpi.Px(this, 16);

        layout.Emblem = new Rectangle(x, (h - Dpi.Px(this, 40)) / 2, Dpi.Px(this, 56), Dpi.Px(this, 40));
        x = layout.Emblem.Right + Dpi.Px(this, 12);
        layout.BrandText = new Point(x, h / 2 - Dpi.Px(this, 18));
        if (layout.ShowBrand) x += TrackedWidth("STEAMBIRDS ALLIANCE", BrandFont, Dpi.Px(this, 1)) + Dpi.Px(this, 30);
        else x += Dpi.Px(this, 4);

        int leftEnd = x;
        foreach (Item item in items.Where(i => i.Side == Side.Left))
        {
            if (item.DividerBefore)
            {
                leftEnd += Dpi.Px(this, 8);
                layout.Dividers.Add(leftEnd);
                leftEnd += Dpi.Px(this, 10);
            }
            int width = TextRenderer.MeasureText(item.Title, ItemFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + itemPadding + IconSlot(item);
            layout.Items.Add(new Rectangle(leftEnd, 0, width, h));
            leftEnd += width;
        }

        // Right side, from the edge inwards: the right tabs, then the state pills.
        int right = Width - Dpi.Px(this, 10);
        var rightRects = new List<Rectangle>();
        foreach (Item item in items.Where(i => i.Side == Side.Right).Reverse())
        {
            int width = TextRenderer.MeasureText(item.Title, ItemFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + itemPadding + IconSlot(item);
            right -= width;
            rightRects.Insert(0, new Rectangle(right, 0, width, h));
        }
        if (rightRects.Count > 0)
        {
            layout.Dividers.Add(right - Dpi.Px(this, 4));
            right -= Dpi.Px(this, 12);
        }

        int pillHeight = Dpi.Px(this, 26);
        int pillTop = (h - pillHeight) / 2;
        int game = PillWidth(1, layout.CompactPills);
        int server = PillWidth(0, layout.CompactPills);
        right -= game;
        var gamePill = new Rectangle(right, pillTop, game, pillHeight);
        right -= Dpi.Px(this, 8) + server;
        var serverPill = new Rectangle(right, pillTop, server, pillHeight);
        layout.Pills.Add(serverPill);
        layout.Pills.Add(gamePill);

        // Items in the order they were added: left ones, then right ones.
        var all = new List<Rectangle>();
        int li = 0, ri = 0;
        foreach (Item item in items) all.Add(item.Side == Side.Left ? layout.Items[li++] : rightRects[ri++]);
        layout.Items.Clear();
        layout.Items.AddRange(all);
        return layout;
    }

    // Room an item gives to its icon (icon and gap), 0 when it has none.
    private int IconSlot(Item item) => Icons.Available && item.Icon.Length > 0 ? Dpi.Px(this, 25) : 0;

    // The icon and the name, centered together in the tab.
    private void DrawItemContent(Graphics g, Item item, Rectangle rect, int headerHeight, Color fore)
    {
        int slot = IconSlot(item);
        int textWidth = TextRenderer.MeasureText(item.Title, ItemFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
        int x = rect.X + (rect.Width - slot - textWidth) / 2;
        int height = headerHeight - Dpi.Px(this, 3);
        if (slot > 0) Icons.Draw(g, item.Icon, new Rectangle(x, 0, slot - Dpi.Px(this, 7), height), fore, 12f);
        TextRenderer.DrawText(g, item.Title, ItemFont, new Rectangle(x + slot, 0, textWidth + 4, height), fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private int PillWidth(int slot, bool compact)
    {
        string text = compact ? pillShort[slot] : pillText[slot];
        if (text.Length == 0 && !compact) text = pillText[slot];
        return StatusPill.Measure(this, text, Font).Width;
    }

    private int TrackedWidth(string text, Font font, int tracking)
    {
        int width = 0;
        foreach (char c in text) width += CharWidth(c, font) + tracking;
        return width;
    }

    private int CharWidth(char c, Font font) => c == ' '
        ? Dpi.Px(this, 6)
        : TextRenderer.MeasureText(c.ToString(), font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;

    private int DrawTracked(Graphics g, string text, Font font, Color color, int x, int y, int tracking)
    {
        foreach (char c in text)
        {
            if (c != ' ')
                TextRenderer.DrawText(g, c.ToString(), font, new Point(x, y), color, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            x += CharWidth(c, font) + tracking;
        }
        return x;
    }

    // ------------------------------------------------------------------ input

    private int ItemAt(Point point, HeaderLayout layout)
    {
        if (point.Y > HeaderHeight) return -1;
        for (int i = 0; i < layout.Items.Count; i++)
        {
            if (layout.Items[i].Contains(point)) return i;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = ItemAt(e.Location, Arrange());
        if (index != hot)
        {
            hot = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
            Invalidate(new Rectangle(0, 0, Width, HeaderHeight));
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (hot != -1)
        {
            hot = -1;
            Invalidate(new Rectangle(0, 0, Width, HeaderHeight));
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int index = ItemAt(e.Location, Arrange());
        if (e.Button == MouseButtons.Left && index >= 0) Select(index);
        base.OnMouseDown(e);
    }

    // Ctrl+Tab and Ctrl+Shift+Tab go through the main tabs, like the old tab control did. A tab control or a side
    // navigation inside a page gets the keys first.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (items.Count > 1 && (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab)))
        {
            int step = keyData.HasFlag(Keys.Shift) ? -1 : 1;
            Select((selected + step + items.Count) % items.Count);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate(new Rectangle(0, 0, Width, HeaderHeight));
    }

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette p = Theme.P;
        Graphics g = e.Graphics;
        g.Clear(p.Back);
        int h = HeaderHeight;
        using (var band = new SolidBrush(p.NavBack)) g.FillRectangle(band, 0, 0, Width, h);
        using (var baseLine = new Pen(Theme.Blend(p.NavBack, p.NavText, 0.10f))) g.DrawLine(baseLine, 0, h - 1, Width, h - 1);

        HeaderLayout layout = Arrange();
        DrawEmblem(g, layout.Emblem);

        if (layout.ShowBrand)
        {
            int y = layout.BrandText.Y;
            DrawTracked(g, "STEAMBIRDS ALLIANCE", BrandFont, p.NavText, layout.BrandText.X, y, Dpi.Px(this, 1));
            int x = DrawTracked(g, "REVIVE", SmallFont, p.Ember, layout.BrandText.X + Dpi.Px(this, 1), y + Dpi.Px(this, 21), Dpi.Px(this, 3));
            if (Version.Length > 0)
                TextRenderer.DrawText(g, "  ·  " + Version, SmallFont, new Point(x, y + Dpi.Px(this, 21)), p.NavMuted, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        using (var divider = new Pen(Theme.Blend(p.NavBack, p.NavText, 0.16f)))
        {
            foreach (int dx in layout.Dividers) g.DrawLine(divider, dx, h / 2 - Dpi.Px(this, 11), dx, h / 2 + Dpi.Px(this, 11));
        }

        for (int i = 0; i < items.Count; i++)
        {
            Rectangle rect = layout.Items[i];
            bool isSelected = i == selected;
            bool isHot = i == hot;
            Color fore = isSelected || isHot ? p.NavText : p.NavMuted;
            if (isHot && !isSelected)
            {
                using var back = new SolidBrush(Theme.Blend(p.NavBack, p.NavText, 0.06f));
                g.FillRectangle(back, rect.X, 0, rect.Width, h - 1);
            }
            DrawItemContent(g, items[i], rect, h, fore);
            if (isSelected)
            {
                int thickness = Math.Max(3, Dpi.Px(this, 3));
                using var accent = new SolidBrush(p.Accent);
                g.FillRectangle(accent, rect.X + Dpi.Px(this, 8), h - thickness, rect.Width - Dpi.Px(this, 16), thickness);
            }
        }

        for (int slot = 0; slot < 2; slot++)
        {
            string text = layout.CompactPills && pillShort[slot].Length > 0 ? pillShort[slot] : pillText[slot];
            if (text.Length == 0) continue;
            StatusPill.Draw(g, this, layout.Pills[slot], text, pillRole[slot], Font, p.NavBack, p.NavText);
        }
    }

    private void DrawEmblem(Graphics g, Rectangle target)
    {
        Image? source = LoadEmblem();
        if (source == null) return;
        if (emblem == null || emblemSize != target.Size)
        {
            emblem?.Dispose();
            emblem = new Bitmap(target.Width, target.Height);
            using Graphics scaled = Graphics.FromImage(emblem);
            scaled.InterpolationMode = InterpolationMode.HighQualityBicubic;
            scaled.CompositingQuality = CompositingQuality.HighQuality;
            scaled.PixelOffsetMode = PixelOffsetMode.HighQuality;
            // The picture has empty margins: only the part with the emblem is used.
            scaled.DrawImage(source, new Rectangle(0, 0, target.Width, target.Height), 10, 150, 1004, 720, GraphicsUnit.Pixel);
            emblemSize = target.Size;
        }
        g.DrawImageUnscaled(emblem, target.Location);
    }

    // The emblem of the project (Branding\icon.png, our own drawing), compiled into the exe.
    private static Image? emblemSource;
    private static bool emblemTried;

    private static Image? LoadEmblem()
    {
        if (emblemTried) return emblemSource;
        emblemTried = true;
        try
        {
            using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("brand-emblem.png");
            if (stream != null) emblemSource = new Bitmap(stream);
        }
        catch
        {
            emblemSource = null;
        }
        return emblemSource;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) emblem?.Dispose();
        base.Dispose(disposing);
    }
}
