using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// The picture of the open world with the players and carriers on it. Hovering a player (or a carrier) shows its data; hovering the
// map shows the coordinates and the region under the mouse. Clicking a player selects it (the list of the page follows).
internal sealed class WorldMapView : Control
{
    private const int PictureSize = 720;
    private const float EdgeSpace = 14f;
    private const float HitRadius = 11f;

    private WorldMapData? map;
    private WorldLiveData? live;
    private Bitmap? picture;
    private Color[] legendColors = Array.Empty<Color>();
    private Point mouse = new Point(-1, -1);

    public string Message { get; set; } = "";
    public string? SelectedPlayer { get; set; }
    public event Action<string?>? PlayerClicked;

    public WorldMapView()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        MinimumSize = new Size(260, 260);
    }

    // ------------------------------------------------------------------ colors

    public static Color ColorOf(WorldMapData.LegendEntry entry)
    {
        // Each zone of the game's world map has its own color (like the map the Sergeant shows in the tutorial).
        Color? zone = entry.Name switch
        {
            "beach" => Color.FromArgb(214, 160, 96),          // Ruined City
            "lowlands" => Color.FromArgb(82, 160, 104),       // Mecha Hunting Grounds
            "lowlands2" => Color.FromArgb(104, 94, 62),       // Local Guard Post
            "lowlands3" => Color.FromArgb(146, 84, 152),      // Poison Farms
            "midlands" => Color.FromArgb(214, 186, 116),      // Dog Base
            "midlands2" => Color.FromArgb(98, 78, 62),        // Graveyard
            "midlands3" => Color.FromArgb(84, 172, 172),      // Fishing Zone
            "highlands" => Color.FromArgb(84, 64, 116),       // Robot Factory
            "highlands2" => Color.FromArgb(196, 124, 136),    // Hospital Area
            "highlands3" => Color.FromArgb(172, 112, 92),     // Egyptian Dig
            "mountains" => Color.FromArgb(204, 176, 176),     // Noble Estates
            "highmountains" => Color.FromArgb(236, 236, 246), // Imperial Palace
            _ => null
        };
        if (zone.HasValue) return zone.Value;
        Color baseColor = entry.Base switch
        {
            "water" => Color.FromArgb(24, 64, 112),
            "canyon" => Color.FromArgb(84, 70, 58),
            "beach" => Color.FromArgb(226, 203, 144),
            "lowlands" => Color.FromArgb(104, 168, 88),
            "midlands" => Color.FromArgb(176, 166, 76),
            "highlands" => Color.FromArgb(150, 104, 160),
            "mountains" => Color.FromArgb(128, 130, 138),
            "highmountains" => Color.FromArgb(236, 238, 246),
            _ => Color.Gray
        };
        // The variants of a region (lowlands, lowlands2, lowlands3) are told apart by their brightness.
        float factor = entry.Variant == 2 ? 1.14f : entry.Variant == 3 ? 0.84f : 1f;
        return Color.FromArgb(Math.Min(255, (int)(baseColor.R * factor)), Math.Min(255, (int)(baseColor.G * factor)), Math.Min(255, (int)(baseColor.B * factor)));
    }

    private static readonly Color[] PlayerColors =
    {
        Color.FromArgb(255, 80, 80), Color.FromArgb(255, 200, 40), Color.FromArgb(60, 220, 120), Color.FromArgb(70, 170, 255),
        Color.FromArgb(230, 100, 255), Color.FromArgb(255, 140, 40), Color.FromArgb(40, 230, 220), Color.FromArgb(240, 240, 240)
    };

    public static Color ColorOfPlayer(string name)
    {
        int hash = 17;
        foreach (char c in name) hash = hash * 31 + c;
        return PlayerColors[(hash & 0x7fffffff) % PlayerColors.Length];
    }

    // ------------------------------------------------------------------ data

    public void SetMap(WorldMapData? data)
    {
        map = data;
        picture?.Dispose();
        picture = null;
        if (data != null)
        {
            legendColors = data.Legend.Select(ColorOf).ToArray();
            picture = BuildPicture(data);
        }
        Invalidate();
    }

    public void SetLive(WorldLiveData? data)
    {
        live = data;
        Invalidate();
    }

    // One pixel of the picture = 2 * radius / PictureSize units; the cell of each pixel comes from the same formulas as the server's Ground.cs.
    private Bitmap BuildPicture(WorldMapData data)
    {
        var pixels = new int[PictureSize * PictureSize];
        float units = 2f * data.Radius / PictureSize;
        for (int py = 0; py < PictureSize; py++)
        {
            for (int px = 0; px < PictureSize; px++)
            {
                float x = (px + 0.5f - PictureSize / 2f) * units;
                float y = (PictureSize / 2f - (py + 0.5f)) * units;
                if (x * x + y * y > data.Radius * data.Radius) continue;
                int code = data.CodeAt(x, y);
                if (code < 0 || code >= legendColors.Length) continue;
                pixels[py * PictureSize + px] = legendColors[code].ToArgb();
            }
        }
        var bitmap = new Bitmap(PictureSize, PictureSize, PixelFormat.Format32bppArgb);
        BitmapData bits = bitmap.LockBits(new Rectangle(0, 0, PictureSize, PictureSize), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bits.Scan0, pixels.Length);
        bitmap.UnlockBits(bits);
        return bitmap;
    }

    // ------------------------------------------------------------------ coordinates

    private RectangleF MapRect()
    {
        float side = Math.Max(50f, Math.Min(ClientSize.Width, ClientSize.Height) - 2f * EdgeSpace);
        return new RectangleF((ClientSize.Width - side) / 2f, (ClientSize.Height - side) / 2f, side, side);
    }

    private PointF ToScreen(float x, float y, RectangleF rect)
    {
        float diameter = 2f * (map?.Radius ?? 650f);
        return new PointF(rect.X + (x / diameter + 0.5f) * rect.Width, rect.Y + (0.5f - y / diameter) * rect.Height);
    }

    private PointF ToWorld(PointF screen, RectangleF rect)
    {
        float diameter = 2f * (map?.Radius ?? 650f);
        return new PointF((((screen.X - rect.X) / rect.Width) - 0.5f) * diameter, (0.5f - ((screen.Y - rect.Y) / rect.Height)) * diameter);
    }

    // ------------------------------------------------------------------ mouse

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        mouse = e.Location;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        mouse = new Point(-1, -1);
        Invalidate();
    }

    // Right click on the map (a point inside the world): the page opens its menu there (spawn a boss, a dungeon carrier).
    public event Action<PointF, Point>? MapRightClicked;

    // A cross where the menu was opened, until the menu closes (null = none).
    private PointF? marked;

    public void Mark(PointF? world)
    {
        marked = world;
        Invalidate();
    }

    // The legend entry (region, water or canyon) under a world position; null outside the world.
    public WorldMapData.LegendEntry? EntryAt(float x, float y)
    {
        if (map == null) return null;
        int code = map.CodeAt(x, y);
        return code >= 0 && code < map.Legend.Count ? map.Legend[code] : null;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Right || map == null) return;
        PointF world = ToWorld(e.Location, MapRect());
        if (world.X * world.X + world.Y * world.Y > map.Radius * map.Radius) return;
        MapRightClicked?.Invoke(world, e.Location);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || map == null) return;
        WorldLiveData.PlayerInfo? player = PlayerAt(e.Location, MapRect());
        SelectedPlayer = player?.Name;
        PlayerClicked?.Invoke(SelectedPlayer);
        Invalidate();
    }

    private WorldLiveData.PlayerInfo? PlayerAt(PointF where, RectangleF rect)
    {
        if (live == null) return null;
        WorldLiveData.PlayerInfo? best = null;
        float bestDistance = HitRadius * HitRadius;
        foreach (WorldLiveData.PlayerInfo p in live.Players)
        {
            if (!p.X.HasValue || !p.Y.HasValue) continue;
            PointF s = ToScreen(p.X.Value, p.Y.Value, rect);
            float d = (s.X - where.X) * (s.X - where.X) + (s.Y - where.Y) * (s.Y - where.Y);
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = p;
            }
        }
        return best;
    }

    private WorldLiveData.CarrierInfo? CarrierAt(PointF where, RectangleF rect)
    {
        if (live == null) return null;
        foreach (WorldLiveData.CarrierInfo c in live.Carriers)
        {
            PointF s = ToScreen(c.X, c.Y, rect);
            if ((s.X - where.X) * (s.X - where.X) + (s.Y - where.Y) * (s.Y - where.Y) <= HitRadius * HitRadius) return c;
        }
        return null;
    }

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Palette palette = Theme.P;
        g.Clear(palette.Input);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (map == null || picture == null)
        {
            using var brush = new SolidBrush(palette.Muted);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Message, Font, brush, new RectangleF(10, 10, Width - 20, Height - 20), format);
            return;
        }

        RectangleF rect = MapRect();
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.DrawImage(picture, rect);
        using (var rim = new Pen(Color.FromArgb(150, palette.Border), 1.5f)) g.DrawEllipse(rim, rect);

        // The spawn point.
        PointF spawn = ToScreen(map.Spawn.X, map.Spawn.Y, rect);
        using (var spawnPen = new Pen(Color.White, 2f))
        {
            g.DrawLine(spawnPen, spawn.X - 6, spawn.Y, spawn.X + 6, spawn.Y);
            g.DrawLine(spawnPen, spawn.X, spawn.Y - 6, spawn.X, spawn.Y + 6);
        }

        if (live != null && live.Active)
        {
            foreach (WorldLiveData.CarrierInfo carrier in live.Carriers) DrawCarrier(g, carrier, rect);
            foreach (WorldLiveData.PlayerInfo player in live.Players) DrawPlayer(g, player, rect);
        }

        if (marked.HasValue)
        {
            PointF at = ToScreen(marked.Value.X, marked.Value.Y, rect);
            using var shadow = new Pen(Color.FromArgb(220, 0, 0, 0), 4f);
            using var cross = new Pen(Color.FromArgb(255, 242, 98, 42), 2f);
            foreach (Pen pen in new[] { shadow, cross })
            {
                g.DrawLine(pen, at.X - 7, at.Y - 7, at.X + 7, at.Y + 7);
                g.DrawLine(pen, at.X - 7, at.Y + 7, at.X + 7, at.Y - 7);
            }
        }

        if (mouse.X >= 0) DrawHover(g, rect, palette);
    }

    private void DrawCarrier(Graphics g, WorldLiveData.CarrierInfo carrier, RectangleF rect)
    {
        PointF s = ToScreen(carrier.X, carrier.Y, rect);
        var diamond = new[] { new PointF(s.X, s.Y - 8), new PointF(s.X + 8, s.Y), new PointF(s.X, s.Y + 8), new PointF(s.X - 8, s.Y) };
        using var fill = new SolidBrush(Color.FromArgb(255, 150, 30));
        using var edge = new Pen(Color.Black, 1.5f);
        g.FillPolygon(fill, diamond);
        g.DrawPolygon(edge, diamond);
    }

    private void DrawPlayer(Graphics g, WorldLiveData.PlayerInfo player, RectangleF rect)
    {
        if (!player.X.HasValue || !player.Y.HasValue) return;
        PointF s = ToScreen(player.X.Value, player.Y.Value, rect);
        Color color = player.Alive ? ColorOfPlayer(player.Name) : Color.Gray;
        bool selected = player.Name == SelectedPlayer;
        if (selected)
        {
            using var ring = new Pen(Color.White, 2f);
            g.DrawEllipse(ring, s.X - 11, s.Y - 11, 22, 22);
        }
        if (player.Rotation.HasValue)
        {
            // 0 = +Y (up on the map), clockwise.
            double r = player.Rotation.Value * Math.PI / 180.0;
            using var heading = new Pen(color, 2.5f);
            g.DrawLine(heading, s.X, s.Y, s.X + (float)Math.Sin(r) * 14f, s.Y - (float)Math.Cos(r) * 14f);
        }
        using var fill = new SolidBrush(color);
        using var edge = new Pen(Color.Black, 1.5f);
        g.FillEllipse(fill, s.X - 6, s.Y - 6, 12, 12);
        g.DrawEllipse(edge, s.X - 6, s.Y - 6, 12, 12);

        using var textBrush = new SolidBrush(Color.White);
        using var shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
        using var smaller = new Font(Font.FontFamily, Font.Size - 0.5f, FontStyle.Bold);
        g.DrawString(player.Name, smaller, shadow, s.X + 9, s.Y - 7);
        g.DrawString(player.Name, smaller, textBrush, s.X + 8, s.Y - 8);
    }

    private void DrawHover(Graphics g, RectangleF rect, Theme.Palette palette)
    {
        var lines = new List<(string Text, bool Bold)>();
        WorldLiveData.PlayerInfo? player = live != null && live.Active ? PlayerAt(mouse, rect) : null;
        WorldLiveData.CarrierInfo? carrier = player == null && live != null && live.Active ? CarrierAt(mouse, rect) : null;

        if (player != null)
        {
            lines.Add((player.Name, true));
            string plane = player.PlaneClass.Length > 0
                ? player.PlaneClass + (player.PlaneVariant.Length > 0 && player.PlaneVariant != "default" ? " (" + player.PlaneVariant + ")" : "") +
                  (player.PlaneLevel.HasValue ? "  " + L.F("level {0}", player.PlaneLevel.Value) : "")
                : L.T("no plane yet");
            lines.Add((L.T("Plane: ") + plane, false));
            if (player.PilotRank.HasValue) lines.Add((L.F("Pilot rank: {0}", player.PilotRank.Value), false));
            if (player.Region.Length > 0)
                lines.Add((L.T("Zone: ") + player.Region + (player.Tier.HasValue ? "  " + L.F("(tier {0})", player.Tier.Value) : ""), false));
            if (player.X.HasValue && player.Y.HasValue)
                lines.Add((L.F("Position: {0}, {1}", player.X.Value.ToString("0"), player.Y.Value.ToString("0")), false));
            if (player.Distance.HasValue)
                lines.Add((L.F("From the center: {0} units  (elevation {1})", player.Distance.Value.ToString("0"), (player.Elevation ?? 0f).ToString("0")), false));
            if (player.Health.HasValue) lines.Add((L.F("Health: {0}%", (int)Math.Round(player.Health.Value * 100f)), false));
            lines.Add((player.Alive ? L.T("Flying") : L.T("Plane not flying"), false));
            lines.Add((L.F("Shots {0}  ·  hits {1}", player.Shots, player.Hits), false));
            lines.Add((L.F("In the world for {0}", Duration(player.SecondsInWorld)), false));
        }
        else if (carrier != null)
        {
            lines.Add((L.F("Carrier: {0}", carrier.Label), true));
            lines.Add((L.F("Position: {0}, {1}", carrier.X.ToString("0"), carrier.Y.ToString("0")), false));
            lines.Add((L.F("Leaves in {0}", Duration(carrier.SecondsLeft)), false));
        }
        else
        {
            PointF world = ToWorld(mouse, rect);
            if (world.X * world.X + world.Y * world.Y > map!.Radius * map.Radius) return;
            int code = map.CodeAt(world.X, world.Y);
            string region = code >= 0 && code < map.Legend.Count ? (map.Legend[code].Zone.Length > 0 ? map.Legend[code].Zone : map.Legend[code].Name) : "";
            float distance = MathF.Sqrt(world.X * world.X + world.Y * world.Y);
            lines.Add((L.F("{0}, {1}", world.X.ToString("0"), world.Y.ToString("0")), true));
            if (region.Length > 0) lines.Add((region, false));
            lines.Add((L.F("{0} units from the center", distance.ToString("0")), false));
        }

        using var bold = new Font(Font, FontStyle.Bold);
        float width = 0f, height = 0f;
        foreach ((string text, bool isBold) in lines)
        {
            SizeF size = g.MeasureString(text, isBold ? bold : Font);
            width = Math.Max(width, size.Width);
            height += size.Height;
        }
        width += 14f;
        height += 10f;
        float left = mouse.X + 16f, top = mouse.Y + 16f;
        if (left + width > Width - 4) left = mouse.X - 16f - width;
        if (top + height > Height - 4) top = Height - 4 - height;
        left = Math.Max(4f, left);
        top = Math.Max(4f, top);

        var box = new RectangleF(left, top, width, height);
        using (var back = new SolidBrush(Color.FromArgb(235, palette.Back))) g.FillRectangle(back, box);
        using (var border = new Pen(palette.Border)) g.DrawRectangle(border, box.X, box.Y, box.Width, box.Height);
        using var text2 = new SolidBrush(palette.Text);
        float y = top + 5f;
        foreach ((string text, bool isBold) in lines)
        {
            Font font = isBold ? bold : Font;
            g.DrawString(text, font, text2, left + 7f, y);
            y += g.MeasureString(text, font).Height;
        }
    }

    public static string Duration(int seconds)
    {
        seconds = Math.Max(0, seconds);
        return seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60}:{seconds % 60:00}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) picture?.Dispose();
        base.Dispose(disposing);
    }
}
