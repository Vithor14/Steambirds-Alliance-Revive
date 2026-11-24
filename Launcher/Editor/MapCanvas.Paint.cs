using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// Drawing of the map canvas. Floor pieces, walls and units use the pictures of the game assets when they exist
// (editor-assets folder) and flat colors and symbols when they do not.
internal sealed partial class MapCanvas
{
    private enum UnitRole { Enemy, Boss, MiniBoss, Chest, Wall, Ally, Decoration }

    private sealed class FloorStyle
    {
        public string Signature = "";
        public AssetPicture?[] Pieces = Array.Empty<AssetPicture?>();
        public AssetPicture? Wall;
        public Color FloorColor = Color.FromArgb(98, 106, 118);
        public Color WallColor = Color.FromArgb(66, 58, 54);
    }

    private sealed class UnitLook
    {
        public AssetPicture? Picture;
        public UnitRole Role;
        public double Radius = 1.6;
        public string Letter = "?";
    }

    private static readonly Color SelectionColor = Color.FromArgb(255, 214, 64);
    private static readonly Color[] AreaColors =
    {
        Color.FromArgb(70, 160, 230), Color.FromArgb(230, 120, 70), Color.FromArgb(110, 200, 120),
        Color.FromArgb(200, 110, 210), Color.FromArgb(230, 200, 70), Color.FromArgb(90, 200, 200)
    };

    private FloorStyle? style;
    private readonly Dictionary<string, UnitLook> looks = new Dictionary<string, UnitLook>();
    private Font? labelFont;

    private static Color RoleColor(UnitRole role) => role switch
    {
        UnitRole.Boss => Color.FromArgb(205, 60, 205),
        UnitRole.MiniBoss => Color.FromArgb(240, 140, 40),
        UnitRole.Chest => Color.FromArgb(235, 195, 45),
        UnitRole.Wall => Color.FromArgb(150, 150, 162),
        UnitRole.Ally => Color.FromArgb(70, 190, 90),
        UnitRole.Decoration => Color.FromArgb(100, 150, 215),
        _ => Color.FromArgb(226, 70, 70)
    };

    private Font LabelFont => labelFont ??= new Font(Font.FontFamily, 8f);

    // ------------------------------------------------------------------ styles and looks

    // Pictures and colors of the floor, from the floor pieces and the territory of the map.
    private void EnsureStyle()
    {
        JsonObject? floorJson = Json.ObjOrNull(Doc.Root, "floor");
        List<int> ids = Json.Ints(floorJson?["drawingPieces"]);
        string signature = string.Join(",", ids) + "|" + Doc.Territory + "|" + Doc.Terrain + "|" + UsePictures;
        if (style != null && style.Signature == signature) return;

        EditorAssets assets = EditorAssets.Current;
        var fresh = new FloorStyle { Signature = signature };
        if (UsePictures)
        {
            fresh.Pieces = ids.Select(id => assets.GroundPicture(assets.GroundName(id))).ToArray();

            TerritoryInfo? info = Territories.ForTerritory(Doc.Territory) ?? Territories.ForTerrain(Doc.Terrain);
            if (info != null)
            {
                fresh.Wall = assets.FirstGroundPicture(info.WallTag + "_wall_01", info.WallTag + "_wall_02", info.WallTag + "_wall_03",
                    info.GroundTag + "_wall_01");
            }
        }

        AssetPicture?[] withPicture = fresh.Pieces.Where(x => x != null).ToArray();
        if (withPicture.Length > 0)
        {
            fresh.FloorColor = Average(withPicture.Select(x => x!.Average));
        }
        if (fresh.Wall != null)
        {
            Color wall = fresh.Wall.Average;
            fresh.WallColor = Color.FromArgb(wall.R * 3 / 4, wall.G * 3 / 4, wall.B * 3 / 4);
        }
        style = fresh;
    }

    private static Color Average(IEnumerable<Color> colors)
    {
        int r = 0, g = 0, b = 0, n = 0;
        foreach (Color c in colors)
        {
            r += c.R;
            g += c.G;
            b += c.B;
            n++;
        }
        return n == 0 ? Color.Gray : Color.FromArgb(r / n, g / n, b / n);
    }

    private UnitLook LookOf(string typeName)
    {
        if (looks.TryGetValue(typeName, out UnitLook? known)) return known;

        JsonObject? type = Json.ObjOrNull(Json.ObjOrNull(Doc.Root, "types"), typeName);
        string template = Json.Str(type, "template");
        GameUnit? unit = EditorAssets.Current.FindUnit(template.Length > 0 ? template : typeName);

        string text = (typeName + " " + template).ToLowerInvariant();
        string behavior = EnemyAiEditors.FlightOf(type);
        UnitRole role = UnitRole.Enemy;
        if (type != null)
        {
            if (Json.Bool(type, "boss")) role = UnitRole.Boss;
            else if (Json.Bool(type, "miniboss")) role = UnitRole.MiniBoss;
            else if (Json.Num(type, "opensAfterHitSeconds") > 0 || text.Contains("treasure") || text.Contains("reward") ||
                     text.Contains("chest") || text.Contains("cache")) role = UnitRole.Chest;
            else if (behavior == "idle" && (text.Contains("wall") || text.Contains("gate"))) role = UnitRole.Wall;
            else if (Json.Int(type, "team", 2) == 1) role = UnitRole.Ally;
            else if (Json.Bool(type, "invulnerable") || behavior == "idle") role = UnitRole.Decoration;
        }

        var look = new UnitLook
        {
            Role = role,
            Radius = unit != null && unit.Radius > 0.4 ? unit.Radius : 1.6,
            Letter = typeName.Length > 0 ? typeName.Substring(0, 1).ToUpperInvariant() : "?"
        };
        if (UsePictures && unit != null && unit.Art.Length > 0) look.Picture = EditorAssets.Current.UnitPicture(unit.Art);
        looks[typeName] = look;
        return look;
    }

    private double UnitRadius(string typeName) => LookOf(typeName).Radius;

    // ------------------------------------------------------------------ rectangles in pixels

    // Rectangle of a cell of "size" u whose corner is (cu, cv); neighbors share the same pixel edges (no gaps).
    private Rectangle CellRect(double u, double v, double size)
    {
        int x0 = (int)Math.Floor(ScreenX(u));
        int x1 = (int)Math.Floor(ScreenX(u + size));
        int y1 = (int)Math.Floor(ScreenY(v));
        int y0 = (int)Math.Floor(ScreenY(v + size));
        return new Rectangle(x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    // Draws the middle of the picture, as much as covers a cell of "cellSize" u (a floor piece is a bit bigger than
    // its slot, a wall bigger than its sub-cell).
    private static void DrawCropped(Graphics g, AssetPicture picture, Rectangle target, double cellSize)
    {
        double side = picture.Half * 2;
        double fraction = side <= 0 ? 1 : Math.Min(1.0, cellSize / side);
        float w = (float)(picture.Image.Width * fraction);
        float h = (float)(picture.Image.Height * fraction);
        var source = new RectangleF((picture.Image.Width - w) / 2f, (picture.Image.Height - h) / 2f, w, h);
        g.DrawImage(picture.Image, target, source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel);
    }

    // A piece of "floor.pieces": the picture and its rotation in degrees, counter-clockwise relative to the tile
    // (null = the game picks a spin by itself, so the piece is drawn without one).
    private readonly record struct FloorPiece(AssetPicture? Picture, double Rotation);

    private Dictionary<(int P, int Q), FloorPiece> PlacedPieces()
    {
        var result = new Dictionary<(int P, int Q), FloorPiece>();
        JsonArray? list = Json.ArrOrNull(Json.ObjOrNull(Doc.Root, "floor"), "pieces");
        if (list == null || list.Count == 0) return result;
        EditorAssets assets = EditorAssets.Current;
        foreach (JsonObject piece in Json.Objects(list))
        {
            string name = Json.Str(piece, "name");
            if (name.Length == 0) name = assets.GroundName(Json.Int(piece, "id"));
            result[(Json.Int(piece, "p"), Json.Int(piece, "q"))] = new FloorPiece(
                name.Length > 0 ? assets.GroundPicture(name) : null,
                Json.Num(piece, "rot"));
        }
        return result;
    }

    // Like DrawCropped, turned counter-clockwise by "degrees" around the middle of the cell (and kept inside it).
    private static void DrawCroppedRotated(Graphics g, AssetPicture picture, Rectangle target, double cellSize, double degrees)
    {
        if (Math.Abs(degrees) < 0.5)
        {
            DrawCropped(g, picture, target, cellSize);
            return;
        }
        GraphicsState state = g.Save();
        g.SetClip(target, CombineMode.Intersect);
        g.TranslateTransform(target.X + target.Width / 2f, target.Y + target.Height / 2f);
        g.RotateTransform((float)-degrees);
        DrawCropped(g, picture, new Rectangle(-target.Width / 2, -target.Height / 2, target.Width, target.Height), cellSize);
        g.Restore(state);
    }

    private static int Variant(int a, int b, int k, int l, int count)
    {
        unchecked
        {
            int hash = a * 73856093 ^ b * 19349663 ^ (k * 2 + l) * 83492791;
            hash ^= hash >> 13;
            return (hash & 0x7fffffff) % count;
        }
    }

    // ------------------------------------------------------------------ paint

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        Theme.Palette p = Theme.P;
        g.Clear(Theme.Dark ? Color.FromArgb(22, 23, 27) : Color.FromArgb(208, 213, 221));
        looks.Clear();
        EnsureStyle();
        FloorStyle floorStyle = style!;

        double u0 = MapU(0), u1 = MapU(Width), v1 = MapV(0), v0 = MapV(Height);

        g.SmoothingMode = SmoothingMode.None;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.InterpolationMode = InterpolationMode.Bilinear;
        if (ShowFloor) DrawFloor(g, floorStyle, u0, u1, v0, v1);
        if (ShowWalls) DrawWalls(g, floorStyle, u0, u1, v0, v1);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        if (ShowGrid) DrawGrid(g, p, u0, u1, v0, v1);

        List<MapObject> list = Objects();
        DrawRoutes(g, list);
        foreach (MapObject item in list)
        {
            if (!IsKindVisible(item.Kind)) continue;
            DrawObject(g, p, item);
        }
        if (selected != null && IsKindVisible(selected.Kind)) DrawSelection(g, selected);

        DrawToolPreview(g, p);
        if (ShowOverlay) DrawOverlay(g, p);
    }

    private void DrawFloor(Graphics g, FloorStyle floorStyle, double u0, double u1, double v0, double v1)
    {
        FloorGrid floor = Doc.Floor;
        double slotPx = MapCoords.Slot * scale;

        // Pieces placed one by one ("floor.pieces") are drawn over the tiles, with their rotation. A map can have only
        // pieces and no drawn floor (the Nexus), so they do not depend on the tiles.
        // Without pictures (or zoomed far out, or a piece the game pictures do not have) a piece is a flat square.
        Dictionary<(int P, int Q), FloorPiece> placed = PlacedPieces();
        if (floor.TileCount == 0 && placed.Count == 0) return;

        if (floor.TileCount > 0)
        {
            int a0 = Math.Max(MapCoords.TileOf(u0) - 1, -64), a1 = Math.Min(MapCoords.TileOf(u1) + 1, 64);
            int b0 = Math.Max(MapCoords.TileOf(v0) - 1, -64), b1 = Math.Min(MapCoords.TileOf(v1) + 1, 64);
            bool pictures = UsePictures && slotPx >= 12 && floorStyle.Pieces.Any(x => x != null);
            using var flat = new SolidBrush(floorStyle.FloorColor);
            for (int a = a0; a <= a1; a++)
            {
                for (int b = b0; b <= b1; b++)
                {
                    if (!floor.HasTile(a, b)) continue;
                    if (!pictures)
                    {
                        g.FillRectangle(flat, CellRect(a * MapCoords.Tile, b * MapCoords.Tile, MapCoords.Tile));
                        continue;
                    }
                    for (int k = 0; k < 2; k++)
                    {
                        for (int l = 0; l < 2; l++)
                        {
                            Rectangle slot = CellRect(a * MapCoords.Tile + k * MapCoords.Slot, b * MapCoords.Tile + l * MapCoords.Slot, MapCoords.Slot);
                            AssetPicture? picture = floorStyle.Pieces[Variant(a, b, k, l, floorStyle.Pieces.Length)];
                            if (picture == null) g.FillRectangle(flat, slot);
                            else DrawCropped(g, picture, slot, MapCoords.Slot);
                        }
                    }
                }
            }
        }

        var view = new Rectangle(0, 0, Width, Height);
        using var flatPieces = new SolidBrush(floorStyle.FloorColor);
        foreach (KeyValuePair<(int P, int Q), FloorPiece> pair in placed)
        {
            Rectangle slot = CellRect(pair.Key.P * MapCoords.Slot, pair.Key.Q * MapCoords.Slot, MapCoords.Slot);
            if (!slot.IntersectsWith(view)) continue;
            if (pair.Value.Picture == null || !UsePictures || slotPx < 12) g.FillRectangle(flatPieces, slot);
            else DrawCroppedRotated(g, pair.Value.Picture, slot, MapCoords.Slot, pair.Value.Rotation);
        }
    }

    private void DrawWalls(Graphics g, FloorStyle floorStyle, double u0, double u1, double v0, double v1)
    {
        FloorGrid floor = Doc.Floor;
        if (floor.TileCount == 0) return;
        int a0 = Math.Max(MapCoords.TileOf(u0) - 1, -64), a1 = Math.Min(MapCoords.TileOf(u1) + 1, 64);
        int b0 = Math.Max(MapCoords.TileOf(v0) - 1, -64), b1 = Math.Min(MapCoords.TileOf(v1) + 1, 64);

        bool pictures = UsePictures && floorStyle.Wall != null && MapCoords.Sub * scale >= 9;
        using var flat = new SolidBrush(floorStyle.WallColor);
        int per = MapCoords.SubsPerTile;
        for (int a = a0; a <= a1; a++)
        {
            for (int b = b0; b <= b1; b++)
            {
                if (!floor.HasTile(a, b)) continue;
                for (int i = 0; i < per; i++)
                {
                    for (int j = 0; j < per; j++)
                    {
                        int si = a * per + i, sj = b * per + j;
                        if (!floor.IsWall(si, sj)) continue;
                        Rectangle cell = CellRect(si * MapCoords.Sub, sj * MapCoords.Sub, MapCoords.Sub);
                        g.FillRectangle(flat, cell);
                        if (pictures) DrawCropped(g, floorStyle.Wall!, cell, MapCoords.Sub);
                    }
                }
            }
        }
    }

    private void DrawGrid(Graphics g, Theme.Palette p, double u0, double u1, double v0, double v1)
    {
        Color lineColor = Theme.Dark ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(40, 0, 0, 0);
        double tilePx = MapCoords.Tile * scale;
        using (var pen = new Pen(lineColor, 1f))
        {
            if (tilePx >= 10)
            {
                for (double u = Math.Ceiling(u0 / MapCoords.Tile) * MapCoords.Tile; u <= u1; u += MapCoords.Tile)
                    g.DrawLine(pen, ScreenX(u), 0, ScreenX(u), Height);
                for (double v = Math.Ceiling(v0 / MapCoords.Tile) * MapCoords.Tile; v <= v1; v += MapCoords.Tile)
                    g.DrawLine(pen, 0, ScreenY(v), Width, ScreenY(v));
            }
        }

        if (tilePx >= 56 && (tool == EditorTool.Walls || tool == EditorTool.Gate))
        {
            using var fine = new Pen(Color.FromArgb(Theme.Dark ? 22 : 28, Theme.Dark ? Color.White : Color.Black), 1f);
            for (double u = Math.Ceiling(u0 / MapCoords.Sub) * MapCoords.Sub; u <= u1; u += MapCoords.Sub)
                g.DrawLine(fine, ScreenX(u), 0, ScreenX(u), Height);
            for (double v = Math.Ceiling(v0 / MapCoords.Sub) * MapCoords.Sub; v <= v1; v += MapCoords.Sub)
                g.DrawLine(fine, 0, ScreenY(v), Width, ScreenY(v));
        }

        using (var axis = new Pen(Color.FromArgb(120, 90, 160, 240), 1f))
        {
            g.DrawLine(axis, ScreenX(0), 0, ScreenX(0), Height);
            g.DrawLine(axis, 0, ScreenY(0), Width, ScreenY(0));
        }

        // The edge of the game grid: floor outside it is not accepted.
        using (var limit = new Pen(Color.FromArgb(150, 230, 90, 90), 1.5f) { DashStyle = DashStyle.Dash })
        {
            float x0 = ScreenX(MapCoords.MinTile * MapCoords.Tile), x1 = ScreenX((MapCoords.MaxTile + 1) * MapCoords.Tile);
            float y0 = ScreenY((MapCoords.MaxTile + 1) * MapCoords.Tile), y1 = ScreenY(MapCoords.MinTile * MapCoords.Tile);
            g.DrawRectangle(limit, x0, y0, x1 - x0, y1 - y0);
        }
    }

    // ------------------------------------------------------------------ objects

    private void DrawRoutes(Graphics g, List<MapObject> list)
    {
        if (!ShowPoints) return;
        var byRoute = new Dictionary<string, List<MapObject>>();
        foreach (MapObject item in list)
        {
            if (item.Kind != ObjectKind.Route) continue;
            if (!byRoute.TryGetValue(item.Owner, out List<MapObject>? points))
            {
                points = new List<MapObject>();
                byRoute[item.Owner] = points;
            }
            points.Add(item);
        }
        using var pen = new Pen(Color.FromArgb(200, 255, 160, 60), 2f) { DashStyle = DashStyle.Dash };
        foreach (KeyValuePair<string, List<MapObject>> pair in byRoute)
        {
            for (int i = 1; i < pair.Value.Count; i++)
            {
                pair.Value[i - 1].GetPosition(out double ua, out double va);
                pair.Value[i].GetPosition(out double ub, out double vb);
                g.DrawLine(pen, ScreenX(ua), ScreenY(va), ScreenX(ub), ScreenY(vb));
            }
        }
    }

    private void DrawObject(Graphics g, Theme.Palette p, MapObject item)
    {
        item.GetPosition(out double u, out double v);
        float x = ScreenX(u), y = ScreenY(v);
        if (item.Kind != ObjectKind.Area && item.Kind != ObjectKind.Line && item.Kind != ObjectKind.Gate &&
            (x < -80 || y < -80 || x > Width + 80 || y > Height + 80)) return;

        switch (item.Kind)
        {
            case ObjectKind.Area:
                DrawArea(g, p, item);
                break;
            case ObjectKind.Line:
                DrawLine(g, p, item, y);
                break;
            case ObjectKind.Gate:
                DrawGate(g, p, item);
                break;
            case ObjectKind.Route:
                FillMarker(g, x, y, 4, Color.FromArgb(255, 160, 60));
                break;
            case ObjectKind.Decor:
                DrawDecor(g, p, item, x, y);
                break;
            case ObjectKind.GroupPoint:
                using (var pen = new Pen(Color.FromArgb(230, 120, 200, 120), 2f)) g.DrawEllipse(pen, x - 5, y - 5, 10, 10);
                DrawFacing(g, item, x, y, 12, Color.FromArgb(230, 120, 200, 120));
                if (scale >= 2) DrawLabel(g, p, item.Owner, x + 7, y - 6, Color.FromArgb(150, 220, 150));
                break;
            case ObjectKind.Point:
                DrawPoint(g, p, item, x, y);
                break;
            case ObjectKind.Checkpoint:
                FillSquare(g, x, y, 6, Color.FromArgb(70, 140, 235));
                DrawFacing(g, item, x, y, 14, Color.FromArgb(70, 140, 235));
                DrawLabel(g, p, item.Name, x + 8, y - 6, Color.FromArgb(120, 175, 245));
                break;
            case ObjectKind.Sign:
                DrawSign(g, p, item, x, y);
                break;
            case ObjectKind.Unit:
                DrawUnit(g, p, item, x, y);
                break;
            case ObjectKind.Entrance:
                DrawEntrance(g, p, item, x, y);
                break;
        }
    }

    private void DrawArea(Graphics g, Theme.Palette p, MapObject item)
    {
        Color color = AreaColors[item.Name.Sum(c => (int)c) % AreaColors.Length];
        float x0 = ScreenX(Json.Num(item.Data, "minU")), x1 = ScreenX(Json.Num(item.Data, "maxU"));
        float y0 = ScreenY(Json.Num(item.Data, "maxV")), y1 = ScreenY(Json.Num(item.Data, "minV"));
        using (var fill = new SolidBrush(Color.FromArgb(24, color))) g.FillRectangle(fill, x0, y0, x1 - x0, y1 - y0);
        using (var pen = new Pen(Color.FromArgb(180, color), 1.5f) { DashStyle = DashStyle.Dash })
            g.DrawRectangle(pen, x0, y0, x1 - x0, y1 - y0);
        DrawLabel(g, p, item.Name, x0 + 3, y0 + 2, color);
    }

    private void DrawLine(Graphics g, Theme.Palette p, MapObject item, float y)
    {
        using (var pen = new Pen(Color.FromArgb(170, 235, 205, 70), 1.5f) { DashStyle = DashStyle.DashDot })
            g.DrawLine(pen, 0, y, Width, y);
        DrawLabel(g, p, item.Name + " (v " + Json.Num(item.Data, item.Name).ToString("0.#") + ")", 4, y - 14, Color.FromArgb(235, 205, 70));
    }

    private void DrawGate(Graphics g, Theme.Palette p, MapObject item)
    {
        item.GateBounds(out double minU, out double maxU, out double minV, out double maxV);
        float x0 = ScreenX(minU), x1 = ScreenX(maxU), y0 = ScreenY(maxV), y1 = ScreenY(minV);
        if (x1 < 0 || y1 < 0 || x0 > Width || y0 > Height) return;

        bool barricade = Json.Bool(item.Data, "damageable");
        Color color = barricade ? Color.FromArgb(205, 140, 60) : Color.FromArgb(215, 75, 75);
        UnitLook look = LookOf(Json.Str(item.Data, "type"));
        double subPx = MapCoords.Sub * scale;
        if (look.Picture != null && subPx >= 8)
        {
            // One box per sub-cell, like in the game.
            AssetPicture picture = look.Picture;
            float half = (float)(picture.Half * scale);
            int startSub = Item(item, "siFrom", "sjFrom"), endSub = Item(item, "siTo", "sjTo");
            for (int n = startSub; n <= endSub; n++)
            {
                double cu = item.Vertical ? (Json.Int(item.Data, "si") + 0.5) * MapCoords.Sub : (n + 0.5) * MapCoords.Sub;
                double cv = item.Vertical ? (n + 0.5) * MapCoords.Sub : (Json.Int(item.Data, "sj") + 0.5) * MapCoords.Sub;
                g.DrawImage(picture.Image, ScreenX(cu) - half, ScreenY(cv) - half, half * 2, half * 2);
            }
        }
        else
        {
            using var fill = new SolidBrush(Color.FromArgb(150, color));
            g.FillRectangle(fill, x0, y0, x1 - x0, y1 - y0);
        }
        using (var pen = new Pen(color, 2f)) g.DrawRectangle(pen, x0, y0, x1 - x0, y1 - y0);

        string text = item.Name;
        string opens = Json.Str(item.Data, "opensWhen");
        if (opens.Length > 0) text += " (" + opens + ")";
        DrawLabel(g, p, text, x0, y0 - 14, color);
    }

    private static int Item(MapObject gate, string rowKey, string columnKey)
    {
        return Json.Int(gate.Data, gate.Vertical ? columnKey : rowKey);
    }

    private void DrawDecor(Graphics g, Theme.Palette p, MapObject item, float x, float y)
    {
        AssetPicture? picture = UsePictures ? EditorAssets.Current.GroundPicture(item.Name) : null;
        // Same rotation as the units: degrees clockwise in the world, turned to the canvas.
        GraphicsState state = g.Save();
        g.TranslateTransform(x, y);
        g.RotateTransform((float)MapCoords.CanvasAngle(item.Rotation));
        if (picture != null)
        {
            float half = (float)(picture.Half * scale);
            g.DrawImage(picture.Image, -half, -half, half * 2, half * 2);
        }
        else
        {
            using var pen = new Pen(Color.FromArgb(200, 150, 150, 170), 1.5f);
            g.DrawRectangle(pen, -5, -5, 10, 10);
            // A tick on the "front" so the rotation of a plain square can be seen.
            g.DrawLine(pen, 0, 0, 0, -9);
        }
        g.Restore(state);
        if (scale >= 2.5) DrawLabel(g, p, item.Name, x + 6, y - 6, Color.FromArgb(170, 170, 190));
    }

    private void DrawPoint(Graphics g, Theme.Palette p, MapObject item, float x, float y)
    {
        bool exit = item.Name == Doc.ExitPointName && Doc.IsDungeon;
        Color color = exit ? Color.FromArgb(80, 210, 100) : Color.FromArgb(70, 200, 215);
        var diamond = new[] { new PointF(x, y - 7), new PointF(x + 7, y), new PointF(x, y + 7), new PointF(x - 7, y) };
        using (var fill = new SolidBrush(Color.FromArgb(exit ? 200 : 150, color))) g.FillPolygon(fill, diamond);
        using (var pen = new Pen(color, 2f)) g.DrawPolygon(pen, diamond);
        DrawFacing(g, item, x, y, 15, color);
        DrawLabel(g, p, exit ? item.Name + " (" + L.T("exit") + ")" : item.Name, x + 9, y - 7, color);
    }

    private void DrawSign(Graphics g, Theme.Palette p, MapObject item, float x, float y)
    {
        Color color = Color.FromArgb(240, 215, 80);
        float radius = (float)(Json.Num(item.Data, "radius", 14) * scale);
        using (var pen = new Pen(Color.FromArgb(120, color), 1f) { DashStyle = DashStyle.Dot }) g.DrawEllipse(pen, x - radius, y - radius, radius * 2, radius * 2);
        FillSquare(g, x, y, 5, color);
        DrawLabel(g, p, item.Name.Length > 0 ? item.Name : L.T("sign"), x + 8, y - 6, color);
    }

    private void DrawEntrance(Graphics g, Theme.Palette p, MapObject item, float x, float y)
    {
        Color color = Color.FromArgb(70, 215, 110);
        double angle = MapCoords.CanvasAngle(item.Rotation);
        GraphicsState state = g.Save();
        g.TranslateTransform(x, y);
        g.RotateTransform((float)angle);
        var arrow = new[] { new PointF(0, -13), new PointF(9, 8), new PointF(0, 3), new PointF(-9, 8) };
        using (var fill = new SolidBrush(Color.FromArgb(215, color))) g.FillPolygon(fill, arrow);
        using (var pen = new Pen(Color.FromArgb(40, 120, 60), 1.5f)) g.DrawPolygon(pen, arrow);
        g.Restore(state);
        DrawLabel(g, p, L.T("Entrance"), x + 12, y - 8, color);
    }

    private void DrawUnit(Graphics g, Theme.Palette p, MapObject item, float x, float y)
    {
        UnitLook look = LookOf(item.Name);
        Color color = RoleColor(look.Role);
        float r = Math.Max(6f, (float)(look.Radius * scale));
        double angle = MapCoords.CanvasAngle(item.Rotation);

        GraphicsState state = g.Save();
        g.TranslateTransform(x, y);
        g.RotateTransform((float)angle);
        if (look.Picture != null)
        {
            float half = (float)(look.Picture.Half * scale);
            g.DrawImage(look.Picture.Image, -half, -half, half * 2, half * 2);
        }
        else
        {
            using (var fill = new SolidBrush(Color.FromArgb(item.InWave ? 120 : 200, color))) g.FillEllipse(fill, -r, -r, r * 2, r * 2);
            var nose = new[] { new PointF(0, -r - 4), new PointF(r * 0.55f, -r * 0.2f), new PointF(-r * 0.55f, -r * 0.2f) };
            using (var fill = new SolidBrush(Color.FromArgb(235, color))) g.FillPolygon(fill, nose);
        }
        g.Restore(state);

        using (var pen = new Pen(Color.FromArgb(item.InWave ? 140 : 230, color), 1.5f) { DashStyle = item.InWave ? DashStyle.Dot : DashStyle.Solid })
            g.DrawEllipse(pen, x - r, y - r, r * 2, r * 2);
        if (look.Picture == null && r >= 9)
        {
            using var font = new Font(Font.FontFamily, Math.Min(r * 0.8f, 14f), FontStyle.Bold, GraphicsUnit.Pixel);
            TextRenderer.DrawText(g, look.Letter, font, new Rectangle((int)(x - r), (int)(y - r), (int)(r * 2), (int)(r * 2)), Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
        if (scale >= 2.2) DrawLabel(g, p, item.Name, x + r + 2, y - 6, color);
    }

    // A short line from the object in the direction it faces.
    private void DrawFacing(Graphics g, MapObject item, float x, float y, float length, Color color)
    {
        if (!item.HasRotation || !Json.Has(item.Data, "rot")) return;
        double angle = MapCoords.CanvasAngle(item.Rotation) * Math.PI / 180.0;
        using var pen = new Pen(color, 2f);
        g.DrawLine(pen, x, y, x + (float)(Math.Sin(angle) * length), y - (float)(Math.Cos(angle) * length));
    }

    private static void FillMarker(Graphics g, float x, float y, float radius, Color color)
    {
        using var fill = new SolidBrush(color);
        g.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
    }

    private static void FillSquare(Graphics g, float x, float y, float half, Color color)
    {
        using var fill = new SolidBrush(color);
        g.FillRectangle(fill, x - half, y - half, half * 2, half * 2);
        using var pen = new Pen(Color.FromArgb(40, 40, 40), 1f);
        g.DrawRectangle(pen, x - half, y - half, half * 2, half * 2);
    }

    private void DrawLabel(Graphics g, Theme.Palette p, string text, float x, float y, Color color)
    {
        if (!ShowLabels || text.Length == 0) return;
        SizeF size = g.MeasureString(text, LabelFont);
        using (var back = new SolidBrush(Color.FromArgb(170, p.Back))) g.FillRectangle(back, x, y, size.Width, size.Height - 2);
        using var fore = new SolidBrush(color);
        g.DrawString(text, LabelFont, fore, x, y - 1);
    }

    // ------------------------------------------------------------------ selection, tool preview, overlay

    private void DrawSelection(Graphics g, MapObject item)
    {
        item.GetPosition(out double u, out double v);
        float x = ScreenX(u), y = ScreenY(v);
        using var pen = new Pen(SelectionColor, 2.5f);
        switch (item.Kind)
        {
            case ObjectKind.Area:
            {
                float x0 = ScreenX(Json.Num(item.Data, "minU")), x1 = ScreenX(Json.Num(item.Data, "maxU"));
                float y0 = ScreenY(Json.Num(item.Data, "maxV")), y1 = ScreenY(Json.Num(item.Data, "minV"));
                g.DrawRectangle(pen, x0, y0, x1 - x0, y1 - y0);
                using var handle = new SolidBrush(SelectionColor);
                foreach ((float cx, float cy) in new[] { (x0, y1), (x1, y1), (x1, y0), (x0, y0) })
                    g.FillRectangle(handle, cx - 4, cy - 4, 8, 8);
                break;
            }
            case ObjectKind.Gate:
            {
                item.GateBounds(out double minU, out double maxU, out double minV, out double maxV);
                g.DrawRectangle(pen, ScreenX(minU) - 3, ScreenY(maxV) - 3, ScreenX(maxU) - ScreenX(minU) + 6, ScreenY(minV) - ScreenY(maxV) + 6);
                break;
            }
            case ObjectKind.Line:
                g.DrawLine(pen, 0, y, Width, y);
                break;
            case ObjectKind.Unit:
            {
                float r = Math.Max(6f, (float)(LookOf(item.Name).Radius * scale)) + 4;
                g.DrawEllipse(pen, x - r, y - r, r * 2, r * 2);
                break;
            }
            default:
                g.DrawEllipse(pen, x - 13, y - 13, 26, 26);
                break;
        }
        if (item.HasRotation && Json.Has(item.Data, "rot")) DrawFacing(g, item, x, y, 26, SelectionColor);
    }

    private void DrawToolPreview(Graphics g, Theme.Palette p)
    {
        if (drag == DragMode.Rect)
        {
            int cell = tool == EditorTool.Floor || WallTileSized ? 12 : 3;
            int x0 = Math.Min(rectStart.X, rectEnd.X), x1 = Math.Max(rectStart.X, rectEnd.X);
            int y0 = Math.Min(rectStart.Y, rectEnd.Y), y1 = Math.Max(rectStart.Y, rectEnd.Y);
            float sx0 = ScreenX(x0 * cell), sx1 = ScreenX((x1 + 1) * cell), sy0 = ScreenY((y1 + 1) * cell), sy1 = ScreenY(y0 * cell);
            Color color = paintOn ? Color.FromArgb(90, 80, 220, 110) : Color.FromArgb(90, 230, 80, 80);
            using var fill = new SolidBrush(color);
            g.FillRectangle(fill, sx0, sy0, sx1 - sx0, sy1 - sy0);
            return;
        }
        if (drag == DragMode.Area)
        {
            float sx0 = ScreenX(Math.Min(dragStartU, dragLastU)), sx1 = ScreenX(Math.Max(dragStartU, dragLastU));
            float sy0 = ScreenY(Math.Max(dragStartV, dragLastV)), sy1 = ScreenY(Math.Min(dragStartV, dragLastV));
            using var pen = new Pen(SelectionColor, 1.5f) { DashStyle = DashStyle.Dash };
            g.DrawRectangle(pen, sx0, sy0, sx1 - sx0, sy1 - sy0);
            return;
        }
        if (drag == DragMode.GateLine)
        {
            using var pen = new Pen(SelectionColor, 3f);
            bool column = Math.Abs(dragLastV - dragStartV) > Math.Abs(dragLastU - dragStartU);
            if (column) g.DrawLine(pen, ScreenX(dragStartU), ScreenY(dragStartV), ScreenX(dragStartU), ScreenY(dragLastV));
            else g.DrawLine(pen, ScreenX(dragStartU), ScreenY(dragStartV), ScreenX(dragLastU), ScreenY(dragStartV));
            return;
        }
        if (!hoverValid || drag != DragMode.None) return;

        using var hover = new Pen(Color.FromArgb(220, SelectionColor), 1.5f);
        switch (tool)
        {
            case EditorTool.Floor:
            {
                Rectangle tile = CellRect(MapCoords.TileOf(hoverU) * MapCoords.Tile, MapCoords.TileOf(hoverV) * MapCoords.Tile, MapCoords.Tile);
                g.DrawRectangle(hover, tile);
                break;
            }
            case EditorTool.Walls:
            {
                double size = WallTileSized ? MapCoords.Tile : MapCoords.Sub;
                Rectangle cell = CellRect(MapCoords.Floor(hoverU / size) * size, MapCoords.Floor(hoverV / size) * size, size);
                g.DrawRectangle(hover, cell);
                break;
            }
            case EditorTool.Select:
                break;
            default:
            {
                float x = ScreenX(Snap(hoverU)), y = ScreenY(Snap(hoverV));
                g.DrawLine(hover, x - 8, y, x + 8, y);
                g.DrawLine(hover, x, y - 8, x, y + 8);
                break;
            }
        }
    }

    private void DrawOverlay(Graphics g, Theme.Palette p)
    {
        // Axes legend.
        using (var pen = new Pen(p.Text, 1.5f) { EndCap = LineCap.ArrowAnchor })
        {
            g.DrawLine(pen, 14, Height - 14, 44, Height - 14);
            g.DrawLine(pen, 14, Height - 14, 14, Height - 44);
        }
        DrawLabel(g, p, "u", 48, Height - 24, p.Text);
        DrawLabel(g, p, "v", 18, Height - 58, p.Text);

        bool hasPieces = (Json.ArrOrNull(Json.ObjOrNull(Doc.Root, "floor"), "pieces")?.Count ?? 0) > 0;
        if (Doc.Floor.TileCount == 0 && !hasPieces)
        {
            string hint = L.T("The map has no floor yet. Choose the Floor tool and paint with the left button.");
            SizeF size = g.MeasureString(hint, Font);
            using var brush = new SolidBrush(p.Muted);
            g.DrawString(hint, Font, brush, (Width - size.Width) / 2, Height / 2f - size.Height / 2);
        }
    }
}
