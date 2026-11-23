using System.Runtime.InteropServices;

namespace SteambirdsLauncher.UI;

// Windows pieces a WinForms control cannot color by itself: dark scroll bars and list headers (the "DarkMode_Explorer"
// theme of Windows 10 1809+), and the colors of the title bar (Windows 11). Everything is optional: on an older Windows
// the call fails quietly and the control keeps the system look.
internal static class NativeTheme
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? subAppName, string? subIdList);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    // Tells Windows this program wants dark system pieces (scroll bars, menus) or the normal ones.
    public static void SetDarkApp(bool dark)
    {
        try
        {
            SetPreferredAppMode(dark ? 2 : 3); // 2 = force dark, 3 = force light
            FlushMenuThemes();
        }
        catch
        {
            // Windows before 1903.
        }
    }

    // Gives the scroll bars (and the native header) of a control the dark or the light look.
    public static void StyleScrollBars(Control control)
    {
        if (!control.IsHandleCreated)
        {
            control.HandleCreated += (_, _) => StyleScrollBars(control);
            return;
        }
        try
        {
            SetWindowTheme(control.Handle, Theme.Dark ? "DarkMode_Explorer" : null, null);
        }
        catch
        {
            // Older Windows.
        }
    }

    // Same for a window the control does not own (the drop-down list of a combo box).
    public static void StyleHandle(IntPtr handle)
    {
        try
        {
            SetWindowTheme(handle, Theme.Dark ? "DarkMode_Explorer" : null, null);
        }
        catch
        {
            // Older Windows.
        }
    }

    // Windows 11: caption and border in the colors of the launcher header. Windows 10 ignores these.
    public static void ColorTitleBar(IntPtr window, Color caption, Color text, Color border)
    {
        SetColor(window, 35, caption); // DWMWA_CAPTION_COLOR
        SetColor(window, 36, text);    // DWMWA_TEXT_COLOR
        SetColor(window, 34, border);  // DWMWA_BORDER_COLOR
    }

    private static void SetColor(IntPtr window, int attribute, Color color)
    {
        try
        {
            int colorRef = color.R | (color.G << 8) | (color.B << 16);
            DwmSetWindowAttribute(window, attribute, ref colorRef, sizeof(int));
        }
        catch
        {
            // Ignored.
        }
    }

    public static void DarkTitleBar(IntPtr window, bool dark)
    {
        try
        {
            int on = dark ? 1 : 0;
            // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE; 19 on Windows 10 before 2004.
            if (DwmSetWindowAttribute(window, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(window, 19, ref on, sizeof(int));
        }
        catch
        {
            // Older Windows: the title bar stays light.
        }
    }
}
