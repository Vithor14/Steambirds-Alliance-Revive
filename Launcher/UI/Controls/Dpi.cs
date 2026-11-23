using System.Drawing.Drawing2D;

namespace SteambirdsLauncher.UI;

// Pixel sizes of the controls the launcher draws itself. Written for 96 DPI and scaled to the DPI of the window.
internal static class Dpi
{
    public static int Px(Control control, int pixels96) => (int)Math.Round(pixels96 * Factor(control));

    public static float Px(Control control, float pixels96) => pixels96 * Factor(control);

    public static float Factor(Control control) => Math.Max(1f, control.DeviceDpi / 96f);

    // Rounded rectangle path (radius in pixels).
    public static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    // First ancestor color that is not transparent: what is behind a control that draws rounded corners.
    public static Color BackdropOf(Control control)
    {
        for (Control? parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent.BackColor.A == 255) return parent.BackColor;
        }
        return Theme.P.Back;
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }
}
