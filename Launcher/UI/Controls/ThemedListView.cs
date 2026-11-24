namespace SteambirdsLauncher.UI;

// Read-only list with columns, drawn by the launcher: themed header, taller rows with a light stripe, a steam-brass
// selection, and a last column that stretches to the right edge (no bright strip after the last column).
internal sealed class ThemedListView : ListView
{
    private int lastColumnMinimum;
    private bool fitting;

    public ThemedListView()
    {
        View = View.Details;
        FullRowSelect = true;
        GridLines = false;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        MultiSelect = false;
        HideSelection = false;
        OwnerDraw = true;
        BorderStyle = BorderStyle.None;
        DoubleBuffered = true;
        SmallImageList = new ImageList { ImageSize = new Size(1, LogicalToDeviceUnits(26)) };
    }

    // Stretch the last column to fill the control.
    public bool FillLastColumn { get; set; } = true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginInvoke(new Action(FitLastColumn));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        FitLastColumn();
    }

    // The client area also shrinks when the vertical scroll bar appears.
    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        FitLastColumn();
    }

    protected override void OnColumnWidthChanged(ColumnWidthChangedEventArgs e)
    {
        base.OnColumnWidthChanged(e);
        FitLastColumn();
    }

    private void FitLastColumn()
    {
        if (fitting || !FillLastColumn || !IsHandleCreated || Columns.Count == 0 || View != View.Details) return;
        fitting = true;
        try
        {
            int last = Columns.Count - 1;
            if (lastColumnMinimum == 0) lastColumnMinimum = Math.Max(60, Columns[last].Width);
            int others = 0;
            for (int i = 0; i < last; i++) others += Columns[i].Width;
            int width = Math.Max(lastColumnMinimum, ClientSize.Width - others);
            if (Columns[last].Width != width) Columns[last].Width = width;
        }
        finally
        {
            fitting = false;
        }
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        Theme.Palette p = Theme.P;
        using (var back = new SolidBrush(p.Header)) e.Graphics.FillRectangle(back, e.Bounds);
        using (var line = new Pen(p.Border))
        {
            e.Graphics.DrawLine(line, e.Bounds.X, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            if (e.ColumnIndex < Columns.Count - 1)
                e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Y + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
        }
        Rectangle text = e.Bounds;
        text.Inflate(-Dpi.Px(this, 8), 0);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", Font, text, p.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    // Column that shows a small picture before its text, and where the picture comes from (the item and the size in pixels;
    // null = no picture, the room is kept so the names stay in line).
    public int PictureColumn { get; set; } = -1;
    public Func<ListViewItem, int, Image?>? PictureOf { get; set; }

    // The whole row is drawn here. Windows asks for a redraw of the item alone when the mouse goes over it (without the
    // sub-items), so painting only the background here, and the texts in OnDrawSubItem, made the texts vanish on hover.
    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        ListViewItem item = e.Item;
        int x = e.Bounds.X;
        for (int i = 0; i < Columns.Count; i++)
        {
            int width = Columns[i].Width;
            if (i >= item.SubItems.Count) break;
            DrawCell(e.Graphics, item, i, new Rectangle(x, e.Bounds.Y, width, e.Bounds.Height));
            x += width;
        }
        // What is right of the last column (and the gap of a row that has fewer cells than columns).
        if (x < e.Bounds.Right)
        {
            using var brush = new SolidBrush(RowBack(item, e.ItemIndex, Theme.P));
            e.Graphics.FillRectangle(brush, new Rectangle(x, e.Bounds.Y, e.Bounds.Right - x, e.Bounds.Height));
        }
    }

    // Drawn again by Windows when it asks for the cells one by one: the same picture as above.
    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        int index = e.ColumnIndex;
        int x = e.Item!.Bounds.X;
        for (int i = 0; i < index && i < Columns.Count; i++) x += Columns[i].Width;
        DrawCell(e.Graphics, e.Item, index, new Rectangle(x, e.Bounds.Y, Columns[index].Width, e.Bounds.Height));
    }

    private void DrawCell(Graphics g, ListViewItem item, int column, Rectangle bounds)
    {
        Theme.Palette p = Theme.P;
        using (var brush = new SolidBrush(RowBack(item, item.Index, p))) g.FillRectangle(brush, bounds);

        ListViewItem.ListViewSubItem sub = item.SubItems[column];
        Color fore = item.Selected ? p.SelectionText : sub.ForeColor;
        Rectangle text = bounds;
        text.Inflate(-Dpi.Px(this, 8), 0);
        if (column == PictureColumn && PictureOf != null)
        {
            int size = Math.Max(8, Math.Min(bounds.Height - Dpi.Px(this, 4), Dpi.Px(this, 24)));
            Image? picture = PictureOf(item, size);
            if (picture != null)
            {
                var target = new Rectangle(text.X, bounds.Y + (bounds.Height - size) / 2, size, size);
                g.DrawImage(picture, target);
            }
            int indent = size + Dpi.Px(this, 6);
            text.X += indent;
            text.Width = Math.Max(0, text.Width - indent);
        }
        TextRenderer.DrawText(g, sub.Text ?? "", sub.Font ?? Font, text, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private Color RowBack(ListViewItem item, int index, Theme.Palette p)
    {
        if (item.Selected) return Focused ? p.Selection : Theme.Blend(p.Input, p.Selection, 0.65f);
        return index % 2 == 1 ? Theme.Blend(p.Input, p.Surface, 0.55f) : p.Input;
    }
}
