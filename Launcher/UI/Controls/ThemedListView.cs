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

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        Theme.Palette p = Theme.P;
        Color back = RowBack(e.Item, e.ItemIndex, p);
        using var brush = new SolidBrush(back);
        e.Graphics.FillRectangle(brush, e.Bounds);
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        Theme.Palette p = Theme.P;
        ListViewItem item = e.Item!;
        // The first column's bounds start after the item image: fill it so the stripe has no gap.
        Color back = RowBack(item, e.ItemIndex, p);
        using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);

        Color fore = item.Selected ? p.SelectionText : e.SubItem?.ForeColor ?? ForeColor;
        Font font = e.SubItem?.Font ?? Font;
        Rectangle text = e.Bounds;
        text.Inflate(-Dpi.Px(this, 8), 0);
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? "", font, text, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private Color RowBack(ListViewItem item, int index, Theme.Palette p)
    {
        if (item.Selected) return Focused ? p.Selection : Theme.Blend(p.Input, p.Selection, 0.65f);
        return index % 2 == 1 ? Theme.Blend(p.Input, p.Surface, 0.55f) : p.Input;
    }
}
