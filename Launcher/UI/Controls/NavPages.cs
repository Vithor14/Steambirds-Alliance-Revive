namespace SteambirdsLauncher.UI;

// Side navigation: a list of pages grouped under small headings on the left, the selected page on the right.
// Used where there are many pages (Server, Settings). Similar pages sit together under one heading.
internal sealed class NavPages : Control, IPageSwitcher
{
    private sealed class Entry
    {
        public string Title = "";
        public Control? Page;
        public string Icon = "";
        public bool IsGroup => Page == null;
    }

    private readonly List<Entry> entries = new List<Entry>();
    private int selected = -1;
    private int hot = -1;

    public event EventHandler? SelectedPageChanged;

    public NavPages()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.ContainerControl, true);
        TabStop = true;
        Padding = new Padding(LogicalToDeviceUnits(NavWidth96), 0, 0, 0);
    }

    private const int NavWidth96 = 214;

    private int NavWidth => Dpi.Px(this, NavWidth96);

    // A plain Control does not take its Padding into account when it docks its children: the pages start after the list.
    public override Rectangle DisplayRectangle
    {
        get
        {
            Rectangle area = base.DisplayRectangle;
            return new Rectangle(area.X + Padding.Left, area.Y + Padding.Top, Math.Max(0, area.Width - Padding.Horizontal), Math.Max(0, area.Height - Padding.Vertical));
        }
    }

    // ------------------------------------------------------------------ pages

    public void AddGroup(string title) => entries.Add(new Entry { Title = title });

    public void AddPage(string title, Control page, string icon = "")
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        Controls.Add(page);
        entries.Add(new Entry { Title = title, Page = page, Icon = icon });
        if (selected < 0) SelectEntry(entries.Count - 1);
        Invalidate();
    }

    public int PageCount => entries.Count(e => !e.IsGroup);

    public int SelectedPage
    {
        get => selected < 0 ? -1 : entries.Take(selected).Count(e => !e.IsGroup);
        set
        {
            int seen = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].IsGroup) continue;
                if (++seen == value)
                {
                    SelectEntry(i);
                    return;
                }
            }
        }
    }

    public string PageTitle(int index) => entries.Where(e => !e.IsGroup).ElementAtOrDefault(index)?.Title ?? "";

    private void SelectEntry(int index)
    {
        if (index == selected || index < 0 || index >= entries.Count || entries[index].IsGroup) return;
        if (selected >= 0) entries[selected].Page!.Visible = false;
        selected = index;
        entries[index].Page!.Visible = true;
        Invalidate();
        SelectedPageChanged?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ layout

    private int GroupGap => Dpi.Px(this, 14);
    private int GroupHeight => Dpi.Px(this, 26);
    private int ItemHeight => Dpi.Px(this, 34);

    // Rectangle of every entry, from the top of the list.
    private List<Rectangle> Rows()
    {
        var rows = new List<Rectangle>(entries.Count);
        int y = Dpi.Px(this, 10);
        int width = NavWidth - Dpi.Px(this, 18);
        bool first = true;
        foreach (Entry entry in entries)
        {
            if (entry.IsGroup)
            {
                if (!first) y += GroupGap;
                rows.Add(new Rectangle(Dpi.Px(this, 10), y, width, GroupHeight));
                y += GroupHeight;
            }
            else
            {
                rows.Add(new Rectangle(Dpi.Px(this, 10), y, width, ItemHeight));
                y += ItemHeight + Dpi.Px(this, 2);
            }
            first = false;
        }
        return rows;
    }

    private int EntryAt(Point point)
    {
        List<Rectangle> rows = Rows();
        for (int i = 0; i < rows.Count; i++)
        {
            if (!entries[i].IsGroup && rows[i].Contains(point)) return i;
        }
        return -1;
    }

    // ------------------------------------------------------------------ input

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int index = EntryAt(e.Location);
        if (index != hot)
        {
            hot = index;
            Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        int index = EntryAt(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0)
        {
            Focus();
            SelectEntry(index);
        }
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        int direction = e.KeyCode switch { Keys.Down => 1, Keys.Up => -1, _ => 0 };
        if (direction != 0)
        {
            for (int i = selected + direction; i >= 0 && i < entries.Count; i += direction)
            {
                if (entries[i].IsGroup) continue;
                SelectEntry(i);
                break;
            }
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Home || e.KeyCode == Keys.End)
        {
            int start = e.KeyCode == Keys.Home ? 0 : entries.Count - 1;
            int step = e.KeyCode == Keys.Home ? 1 : -1;
            for (int i = start; i >= 0 && i < entries.Count; i += step)
            {
                if (entries[i].IsGroup) continue;
                SelectEntry(i);
                break;
            }
            e.Handled = true;
        }
        base.OnKeyDown(e);
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

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Palette p = Theme.P;
        Graphics g = e.Graphics;
        g.Clear(p.Back);
        int navWidth = NavWidth;
        using (var line = new Pen(p.Border)) g.DrawLine(line, navWidth - 1, 0, navWidth - 1, Height);

        List<Rectangle> rows = Rows();
        for (int i = 0; i < entries.Count; i++)
        {
            Entry entry = entries[i];
            Rectangle row = rows[i];
            if (entry.IsGroup)
            {
                TextRenderer.DrawText(g, entry.Title.ToUpperInvariant(), Theme.Heading(8f), new Rectangle(row.X + Dpi.Px(this, 10), row.Y, row.Width, row.Height), p.Muted,
                    TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                continue;
            }

            bool isSelected = i == selected;
            bool isHot = i == hot;
            Dpi.Smooth(g);
            if (isSelected || isHot)
            {
                Color fill = isSelected ? Theme.Blend(p.Back, p.Accent, Theme.Dark ? 0.13f : 0.22f) : p.Surface;
                using var path = Dpi.Rounded(row, Dpi.Px(this, 6));
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
            }
            if (isSelected)
            {
                using var bar = new SolidBrush(p.Accent);
                int barHeight = row.Height - Dpi.Px(this, 14);
                g.FillRectangle(bar, row.X, row.Y + (row.Height - barHeight) / 2, Dpi.Px(this, 3), barHeight);
            }
            Color fore = isSelected || isHot ? p.Text : p.Muted;
            int textLeft = row.X + Dpi.Px(this, 14);
            if (Icons.Available && entry.Icon.Length > 0)
            {
                Icons.Draw(g, entry.Icon, new Rectangle(row.X + Dpi.Px(this, 10), row.Y, Dpi.Px(this, 22), row.Height), isSelected ? p.AccentText : fore, 11f);
                textLeft = row.X + Dpi.Px(this, 38);
            }
            TextRenderer.DrawText(g, entry.Title, Font, new Rectangle(textLeft, row.Y, row.Right - textLeft - Dpi.Px(this, 4), row.Height), fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (isSelected && Focused && ShowFocusCues)
            {
                using var ring = new Pen(p.AccentText) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                g.DrawRectangle(ring, row.X + 1, row.Y + 1, row.Width - 3, row.Height - 3);
            }
        }
    }
}
