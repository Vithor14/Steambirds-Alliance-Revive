using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SteambirdsLauncher.UI;

// Combo box drawn by the launcher: flat field with a chevron, a themed list and, for the editable kind, the same frame
// around the system edit box. The mouse wheel scrolls the page instead of changing the choice by accident.
internal sealed class ThemedComboBox : ComboBox
{
    private const int WmPaint = 0x000F;
    private const int WmEraseBackground = 0x0014;
    private const int WmMouseWheel = 0x020A;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        public int Size;
        public Rect Item;
        public Rect Button;
        public int ButtonState;
        public IntPtr Combo;
        public IntPtr Edit;
        public IntPtr List;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr Hdc;
        public int Erase;
        public Rect Paint;
        public int Restore;
        public int IncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Reserved;
    }

    [DllImport("user32.dll")]
    private static extern bool GetComboBoxInfo(IntPtr window, ref ComboBoxInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr window, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr window, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private bool hover;
    private Func<string, int, Image?>? itemImage;

    public ThemedComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        ItemHeight = LogicalToDeviceUnits(20);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    // A picture drawn before the text of every choice (text, size in pixels -> picture; null = no room for one).
    // The rows are taller while it is set.
    public Func<string, int, Image?>? ItemImage
    {
        get => itemImage;
        set
        {
            itemImage = value;
            ItemHeight = LogicalToDeviceUnits(value == null ? 20 : 30);
        }
    }

    private int ImageSize => Dpi.Px(this, 24);

    // Draws the picture of a choice inside "slot" and returns the width it used (0 = none).
    private int DrawChoiceImage(Graphics g, string text, Rectangle slot)
    {
        Image? image = itemImage?.Invoke(text, ImageSize);
        if (image == null) return 0;
        int size = ImageSize;
        g.DrawImage(image, slot.X + Dpi.Px(this, 4), slot.Y + (slot.Height - size) / 2, size, size);
        return size + Dpi.Px(this, 6);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseWheel && !DroppedDown && Parent != null)
        {
            SendMessage(Parent.Handle, m.Msg, m.WParam, m.LParam);
            return;
        }
        if (m.Msg == WmEraseBackground)
        {
            m.Result = (IntPtr)1;
            return;
        }
        if (m.Msg == WmPaint)
        {
            var paint = new PaintStruct { Reserved = new byte[32] };
            IntPtr dc = BeginPaint(Handle, ref paint);
            try
            {
                using var g = Graphics.FromHdc(dc);
                PaintField(g);
            }
            finally
            {
                EndPaint(Handle, ref paint);
            }
            return;
        }
        base.WndProc(ref m);
    }

    private void PaintField(Graphics g)
    {
        // An editable box that nobody is typing in does not keep a blue selection.
        if (DropDownStyle == ComboBoxStyle.DropDown && !Focused && SelectionLength != 0) Select(0, 0);

        Theme.Palette p = Theme.P;
        Dpi.Smooth(g);
        Color backdrop = Dpi.BackdropOf(this);
        Color fill = Enabled ? p.Input : Theme.Blend(backdrop, p.Input, 0.6f);
        Color border = !Enabled ? Theme.Blend(backdrop, p.Border, 0.6f)
            : DroppedDown || Focused ? p.Accent : hover ? p.BorderStrong : p.Border;
        Color fore = Enabled ? p.Text : Theme.Blend(backdrop, p.Muted, 0.7f);

        Rectangle all = ClientRectangle;
        Region? saved = null;
        if (DropDownStyle == ComboBoxStyle.DropDown)
        {
            // The edit box paints itself: keep out of its area.
            var info = new ComboBoxInfo { Size = Marshal.SizeOf<ComboBoxInfo>() };
            if (GetComboBoxInfo(Handle, ref info) && info.Edit != IntPtr.Zero)
            {
                saved = g.Clip;
                var item = new Rectangle(info.Item.Left, info.Item.Top, info.Item.Right - info.Item.Left, info.Item.Bottom - info.Item.Top);
                g.SetClip(item, CombineMode.Exclude);
            }
        }

        g.Clear(backdrop);
        var bounds = new Rectangle(0, 0, all.Width - 1, all.Height - 1);
        using (GraphicsPath path = Dpi.Rounded(bounds, Dpi.Px(this, 4)))
        using (var brush = new SolidBrush(fill))
        using (var pen = new Pen(border))
        {
            g.FillPath(brush, path);
            g.DrawPath(pen, path);
        }

        int arrowWidth = Dpi.Px(this, 24);
        if (DropDownStyle == ComboBoxStyle.DropDownList)
        {
            string text = SelectedItem != null ? GetItemText(SelectedItem) ?? "" : Text;
            int pictureWidth = text.Length > 0 ? DrawChoiceImage(g, text, new Rectangle(0, 0, all.Width, all.Height)) : 0;
            var textBounds = new Rectangle(Dpi.Px(this, 8) + pictureWidth, 0, Math.Max(0, all.Width - arrowWidth - Dpi.Px(this, 8) - pictureWidth), all.Height);
            TextRenderer.DrawText(g, text, Font, textBounds, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        // Chevron.
        float cx = all.Width - arrowWidth / 2f - 1;
        float cy = all.Height / 2f;
        float half = Dpi.Px(this, 4f);
        using (var chevron = new Pen(Enabled ? (hover || DroppedDown ? p.Text : p.Muted) : fore, Math.Max(1.5f, Dpi.Px(this, 1.6f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        })
        {
            g.DrawLines(chevron, new[] { new PointF(cx - half, cy - half / 2), new PointF(cx, cy + half / 2), new PointF(cx + half, cy - half / 2) });
        }

        if (saved != null) g.Clip = saved;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        Theme.Palette p = Theme.P;
        bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        Color back = selected ? p.Selection : p.Input;
        Color fore = selected ? p.SelectionText : p.Text;
        using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
        string text = GetItemText(Items[e.Index]) ?? "";
        int pictureWidth = text.Length > 0 ? DrawChoiceImage(e.Graphics, text, e.Bounds) : 0;
        Rectangle textBounds = new Rectangle(e.Bounds.X + Dpi.Px(this, 8) + pictureWidth, e.Bounds.Y, e.Bounds.Width - Dpi.Px(this, 12) - pictureWidth, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, text, Font, textBounds, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // An editable box starts with its text selected in the system's blue: clear it until the user clicks in.
        BeginInvoke(new Action(() =>
        {
            if (!IsDisposed && !Focused && DropDownStyle == ComboBoxStyle.DropDown) SelectionLength = 0;
        }));
    }

    // Text set by code selects itself in the system's blue even without the focus: clear it unless the user is in the box.
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        ClearIdleSelection();
    }

    private void ClearIdleSelection()
    {
        if (DropDownStyle != ComboBoxStyle.DropDown || !IsHandleCreated) return;
        BeginInvoke(new Action(() =>
        {
            if (!IsDisposed && !Focused) Select(0, 0);
        }));
    }

    protected override void OnDropDown(EventArgs e)
    {
        base.OnDropDown(e);
        // The drop-down list is a window of its own: it gets the dark scroll bar too.
        var info = new ComboBoxInfo { Size = Marshal.SizeOf<ComboBoxInfo>() };
        if (GetComboBoxInfo(Handle, ref info) && info.List != IntPtr.Zero) NativeTheme.StyleHandle(info.List);
        Invalidate();
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        base.OnDropDownClosed(e);
        Invalidate();
    }

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

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
        ClearIdleSelection();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }
}
