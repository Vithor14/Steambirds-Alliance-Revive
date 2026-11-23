using System.Runtime.InteropServices;

namespace SteambirdsLauncher.UI;

// Text box with a border in the theme colors (the system draws a bright frame that clashes with the dark theme) and a
// steam-yellow border while it has the focus.
internal sealed class ThemedTextBox : TextBox
{
    private const int WmPaint = 0x000F;
    private const int WmNcPaint = 0x0085;
    private const int WmMouseWheel = 0x020A;

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

    protected override void WndProc(ref Message m)
    {
        // A single-line box has nothing to scroll: the mouse wheel goes to the page behind it.
        if (m.Msg == WmMouseWheel && !Multiline && Parent != null)
        {
            SendMessage(Parent.Handle, m.Msg, m.WParam, m.LParam);
            return;
        }
        base.WndProc(ref m);
        // The system paints its own frame after ours in some cases (both messages): draw over it every time.
        if ((m.Msg == WmNcPaint || m.Msg == WmPaint) && BorderStyle == BorderStyle.FixedSingle) PaintBorder();
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
