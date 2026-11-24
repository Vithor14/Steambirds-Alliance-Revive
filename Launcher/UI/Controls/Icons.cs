using System.Drawing.Text;

namespace SteambirdsLauncher.UI;

// Small icons for the tabs and menus, from the icon font that ships with Windows 10 and 11 ("Segoe MDL2 Assets").
// They are drawn like text, so they take the color of the theme and the state of the tab. Nothing here comes from the
// game. If the PC has no such font, the icons are left out and the tabs show only their names.
internal static class Icons
{
    // Main tabs.
    public const string Play = "";
    public const string Server = "";
    public const string MapEditor = "";
    public const string EnemyAi = "";
    public const string Settings = "";

    // Sub-tabs and pages.
    public const string Servers = "";
    public const string Game = "";
    public const string Console = "";
    public const string General = "";
    public const string Files = "";
    public const string Progression = "";
    public const string Items = "";
    public const string Enemies = "";
    public const string Store = "";
    public const string Account = "";
    public const string NewAccount = "";
    public const string Friends = "";
    public const string Maps = "";
    public const string World = "";
    public const string Chat = "";   // ChatBubbles
    public const string Network ="";

    private static readonly Dictionary<float, Font> Fonts = new Dictionary<float, Font>();
    private static bool? available;

    public static bool Available
    {
        get
        {
            if (available.HasValue) return available.Value;
            using var families = new InstalledFontCollection();
            available = families.Families.Any(f => f.Name == "Segoe MDL2 Assets");
            return available.Value;
        }
    }

    private static Font FontOf(float points)
    {
        lock (Fonts)
        {
            if (!Fonts.TryGetValue(points, out Font? font))
            {
                font = new Font("Segoe MDL2 Assets", points, FontStyle.Regular, GraphicsUnit.Point);
                Fonts[points] = font;
            }
            return font;
        }
    }

    // Draws the icon centered in the given rectangle.
    public static void Draw(Graphics g, string glyph, Rectangle slot, Color color, float points = 11f)
    {
        if (!Available || glyph.Length == 0) return;
        TextRenderer.DrawText(g, glyph, FontOf(points), slot, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}
