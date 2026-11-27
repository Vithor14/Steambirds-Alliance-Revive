using System.Runtime.InteropServices;

namespace SteambirdsLauncher.UI;

// Text box with a border in the theme colors (the system draws a bright frame that clashes with the dark theme) and a
// steam-yellow border while it has the focus.
internal sealed class ThemedTextBox : TextBox
{
    private const int WmPaint = 0x000F;
    private const int WmNcPaint = 0x0085;
    private const int WmMouseWheel = 0x020A;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
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
    private static extern IntPtr BeginPaint(IntPtr window, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr window, ref PaintStruct paint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    private bool hover;

    public ThemedTextBox()
    {
        BorderStyle = BorderStyle.FixedSingle;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // The system style paints a disabled box white with black text. Without it, BackColor is the theme color.
        NativeTheme.Strip(Handle);
        ApplyInputColors();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        if (IsHandleCreated) ApplyInputColors();
        Invalidate();
    }

    private void ApplyInputColors()
    {
        Theme.Palette p = Theme.P;
        Color back = Enabled ? p.Input : Theme.Blend(p.Surface, p.Input, 0.45f);
        Color fore = Enabled ? p.Text : p.Muted;
        if (BackColor != back) BackColor = back;
        if (ForeColor != fore) ForeColor = fore;
    }

    protected override void WndProc(ref Message m)
    {
        // A single-line box has nothing to scroll: the mouse wheel goes to the page behind it.
        if (m.Msg == WmMouseWheel && !Multiline && Parent != null)
        {
            SendMessage(Parent.Handle, m.Msg, m.WParam, m.LParam);
            return;
        }
        // A disabled box is painted by Windows in the system colors (white, black text). Draw it ourselves.
        if (!Enabled && m.Msg == WmPaint && IsHandleCreated)
        {
            var paint = new PaintStruct { Reserved = new byte[32] };
            IntPtr dc = BeginPaint(Handle, ref paint);
            try
            {
                using var g = Graphics.FromHdc(dc);
                PaintDisabled(g);
            }
            finally
            {
                EndPaint(Handle, ref paint);
            }
            PaintBorder();
            return;
        }
        base.WndProc(ref m);
        // The system paints its own frame after ours in some cases (both messages): draw over it every time.
        if ((m.Msg == WmNcPaint || m.Msg == WmPaint) && BorderStyle == BorderStyle.FixedSingle) PaintBorder();
    }

    private void PaintDisabled(Graphics g)
    {
        Theme.Palette p = Theme.P;
        Color back = Theme.Blend(p.Surface, p.Input, 0.45f);
        g.Clear(back);
        var flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl;
        if (Multiline) flags |= TextFormatFlags.WordBreak | TextFormatFlags.Top;
        else flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        var bounds = new Rectangle(2, 0, Math.Max(0, ClientSize.Width - 4), ClientSize.Height);
        TextRenderer.DrawText(g, Text, Font, bounds, p.Muted, flags);
    }

    private void PaintBorder()
    {
        if (!IsHandleCreated || Width < 3 || Height < 3) return;
        IntPtr dc = GetWindowDC(Handle);
        if (dc == IntPtr.Zero) return;
        try
        {
            Theme.Palette p = Theme.P;
            Color color = Focused ? p.Accent : hover ? p.BorderStrong : p.Border;
            using var g = Graphics.FromHdc(dc);
            using var pen = new Pen(color);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
        finally
        {
            ReleaseDC(Handle, dc);
        }
    }

    private void RedrawBorder()
    {
        if (IsHandleCreated) PaintBorder();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        RedrawBorder();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        RedrawBorder();
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hover = true;
        base.OnMouseEnter(e);
        RedrawBorder();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = false;
        base.OnMouseLeave(e);
        RedrawBorder();
    }
}
