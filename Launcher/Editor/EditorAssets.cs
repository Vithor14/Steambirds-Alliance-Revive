using System.Drawing.Imaging;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.Editor;

// A picture of the game asset drawn by SteambirdsGameFiles.exe: top view of the 3D model, lit from
// above, with the colors of the model. "Half" is half of the side of the picture in world units, so it is drawn at the
// size the object has in the game. The game textures are not in the picture (AssetRipper did not export them).
internal sealed class AssetPicture
{
    public AssetPicture(Bitmap image, double half)
    {
        Image = image;
        Half = half;
        Average = ComputeAverage(image);
    }

    private readonly Dictionary<int, Bitmap> thumbs = new Dictionary<int, Bitmap>();
    private Rectangle? content;

    public Bitmap Image { get; }
    public double Half { get; }
    public Color Average { get; }

    // The picture cut to what is drawn (the pictures have a lot of empty border) and fitted into a square of "size"
    // pixels, for the lists. Kept for the next time.
    public Bitmap Thumb(int size)
    {
        size = Math.Clamp(size, 8, 256);
        lock (thumbs)
        {
            if (thumbs.TryGetValue(size, out Bitmap? ready)) return ready;
            Rectangle box = content ??= ContentBounds(Image);
            var thumb = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                float scale = Math.Min((size - 2f) / box.Width, (size - 2f) / box.Height);
                float w = box.Width * scale;
                float h = box.Height * scale;
                g.DrawImage(Image, new RectangleF((size - w) / 2f, (size - h) / 2f, w, h), box, GraphicsUnit.Pixel);
            }
            thumbs[size] = thumb;
            return thumb;
        }
    }

    private static readonly Dictionary<int, Bitmap> Placeholders = new Dictionary<int, Bitmap>();

    // What a list shows for something that has no picture: a dotted square, so the names stay in line.
    public static Bitmap Placeholder(int size)
    {
        size = Math.Clamp(size, 8, 256);
        lock (Placeholders)
        {
            if (Placeholders.TryGetValue(size, out Bitmap? ready)) return ready;
            var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var pen = new Pen(Color.FromArgb(110, 128, 128, 128)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot })
            {
                g.DrawRectangle(pen, 2, 2, size - 5, size - 5);
            }
            Placeholders[size] = bitmap;
            return bitmap;
        }
    }

    // Smallest rectangle that holds every pixel that is not (almost) transparent.
    private static Rectangle ContentBounds(Bitmap image)
    {
        int minX = image.Width, minY = image.Height, maxX = -1, maxY = -1;
        BitmapData data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[data.Stride];
            for (int y = 0; y < image.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                for (int x = 0; x < image.Width; x++)
                {
                    if (row[x * 4 + 3] < 24) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    maxY = y;
                }
            }
        }
        finally
        {
            image.UnlockBits(data);
        }
        if (maxX < minX || maxY < minY) return new Rectangle(0, 0, image.Width, image.Height);
        return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static Color ComputeAverage(Bitmap image)
    {
        long r = 0, g = 0, b = 0, count = 0;
        int step = Math.Max(1, Math.Min(image.Width, image.Height) / 16);
        for (int y = 0; y < image.Height; y += step)
        {
            for (int x = 0; x < image.Width; x += step)
            {
                Color pixel = image.GetPixel(x, y);
                if (pixel.A < 128) continue;
                r += pixel.R;
                g += pixel.G;
                b += pixel.B;
                count++;
            }
        }
        if (count == 0) return Color.Gray;
        return Color.FromArgb((int)(r / count), (int)(g / count), (int)(b / count));
    }
}

// A unit of the game (one line "unit" of units_config.txt, written by the Steambirds Quieter plugin).
internal sealed class GameUnit
{
    public string Name { get; init; } = "";
    public int Tier { get; init; }
    public double MaxHealth { get; init; }
    public double Radius { get; init; }
    public bool Wall { get; init; }
    public bool Boss { get; init; }
    public int Weapons { get; init; }
    // Name of the prefab that draws the unit ("arte" column).
    public string Art { get; init; } = "";
    // Flight and guns, for the Enemy AI simulator.
    public double VelMin { get; init; }
    public double VelMax { get; init; }
    public double Turn { get; init; }
    public List<SteambirdsAi.AiGunStats> Guns { get; } = new List<SteambirdsAi.AiGunStats>();

    public bool Armed => Guns.Any(g => g.Damage > 0f);

    // What the simulator needs to fly and shoot this unit.
    public SteambirdsAi.AiUnitStats ToStats() => new SteambirdsAi.AiUnitStats
    {
        Name = Name,
        Art = Art,
        MaxHealth = (float)MaxHealth,
        VelMin = (float)VelMin,
        VelMax = (float)VelMax,
        Turn = (float)Turn,
        Radius = (float)Radius,
        Guns = Guns.ToList()
    };
}

// Everything the editor knows about the game assets: floor pieces and walls (names by id), the units (catalog and
// pictures). Nothing here is required: without the pictures the editor draws flat colors and symbols.
internal sealed class EditorAssets
{
    private static readonly Regex GroundLine = new Regex(@"^ID=(-?\d+)\s+name=(.*?)\s+fourPiece=", RegexOptions.Compiled);

    private readonly Dictionary<string, double> groundHalf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, double> unitHalf = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AssetPicture?> groundPictures = new Dictionary<string, AssetPicture?>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AssetPicture?> unitPictures = new Dictionary<string, AssetPicture?>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, string> groundNames = new Dictionary<int, string>();
    private readonly Dictionary<string, GameUnit> units = new Dictionary<string, GameUnit>(StringComparer.OrdinalIgnoreCase);

    private EditorAssets(string folder)
    {
        Folder = folder;
    }

    public static EditorAssets Current { get; private set; } = new EditorAssets("");

    // Names the game shows for its units (set by the editor tab from the game data).
    public static IReadOnlyDictionary<string, string> UnitNames { get; set; } = new Dictionary<string, string>();

    // The name the game shows for the unit a "template" refers to ("" when the game has no text for it).
    public static string UnitDisplayName(string template)
    {
        foreach (string option in template.Split('|'))
        {
            string id = option.Trim();
            if (id.Length == 0 || id.StartsWith("auto:", StringComparison.OrdinalIgnoreCase)) continue;
            if (UnitNames.TryGetValue(id, out string? text) && text.Length > 0) return text;
        }
        return "";
    }

    public string Folder { get; }
    public bool HasPictures { get; private set; }
    public bool HasGroundNames => groundNames.Count > 0;
    public bool HasUnits => units.Count > 0;
    public IReadOnlyDictionary<int, string> GroundNames => groundNames;
    public IReadOnlyDictionary<string, GameUnit> Units => units;

    public static string DefaultFolder => Path.Combine(LauncherPaths.BaseFolder, "editor-assets");

    // Reads what exists; never throws (a missing or broken file only leaves that part empty).
    public static EditorAssets Load(string? gameFolder)
    {
        var assets = new EditorAssets(DefaultFolder);
        assets.ReadThumbs();
        assets.ReadGroundNames(gameFolder);
        assets.ReadUnits(gameFolder);
        Current = assets;
        return assets;
    }

    // ------------------------------------------------------------------ names and catalogs

    public string GroundName(int id) => groundNames.TryGetValue(id, out string? name) ? name : "";

    // Unit of the game a "template" of the map refers to: the first option of "a|b|auto:fraco" that exists.
    public GameUnit? FindUnit(string template)
    {
        foreach (string option in template.Split('|'))
        {
            string name = option.Trim();
            if (name.Length == 0 || name.StartsWith("auto:", StringComparison.OrdinalIgnoreCase)) continue;
            if (units.TryGetValue(name, out GameUnit? unit)) return unit;
        }
        return null;
    }

    // Picture of a game unit, from its name or from a "template" of the map (null without pictures).
    public AssetPicture? PictureOfUnit(string template)
    {
        if (!HasPictures || template.Length == 0) return null;
        GameUnit? unit = FindUnit(template);
        return unit == null || unit.Art.Length == 0 ? null : UnitPicture(unit.Art);
    }

    // Picture of a floor piece or decoration, by its name or, without a name, by its id in ground_assets.txt.
    public AssetPicture? PictureOfGround(string name, int id = -1)
    {
        if (!HasPictures) return null;
        if (name.Length == 0 && id >= 0) name = GroundName(id);
        return name.Length == 0 ? null : GroundPicture(name);
    }

    private void ReadThumbs()
    {
        try
        {
            string path = Path.Combine(Folder, "thumbs.json");
            if (!File.Exists(path)) return;
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            ReadHalves(document.RootElement, "ground", groundHalf);
            ReadHalves(document.RootElement, "units", unitHalf);
            HasPictures = groundHalf.Count > 0 || unitHalf.Count > 0;
        }
        catch
        {
            // No pictures: flat colors.
        }
    }

    private static void ReadHalves(JsonElement root, string section, Dictionary<string, double> target)
    {
        if (!root.TryGetProperty(section, out JsonElement list) || list.ValueKind != JsonValueKind.Object) return;
        foreach (JsonProperty entry in list.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Object &&
                entry.Value.TryGetProperty("half", out JsonElement half) && half.TryGetDouble(out double value))
                target[entry.Name] = value;
        }
    }

    private void ReadGroundNames(string? gameFolder)
    {
        var places = new List<string>
        {
            Path.Combine(Folder, "ground_assets.txt"),
            LauncherPaths.ServerFile(Path.Combine("data", "ground_assets.txt"))
        };
        if (!string.IsNullOrWhiteSpace(gameFolder)) places.Add(Path.Combine(GameInstall.BepInExFolder(gameFolder), "ground_assets.txt"));

        foreach (string path in places)
        {
            try
            {
                if (!File.Exists(path)) continue;
                foreach (string line in TextFiles.ReadLinesShared(path))
                {
                    Match match = GroundLine.Match(line);
                    if (!match.Success) continue;
                    if (int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                        groundNames[id] = match.Groups[2].Value;
                }
                if (groundNames.Count > 0) return;
            }
            catch
            {
                // Try the next place.
            }
        }
    }

    // units_config.txt: "unit <name> <hash> <tier> <maxHealth> <baseHealth> <minSpeed> <maxSpeed> <turn> <radius>
    // <capsule> <birthTime> <homeDistance> <wall> <player> <boss> <islandMin> <islandMax> <weapons> <art> <marks>", TAB separated.
    private void ReadUnits(string? gameFolder)
    {
        var places = new List<string>
        {
            LauncherPaths.ServerFile(Path.Combine("data", "units_config.txt")),
            Path.Combine(Folder, "units_config.txt")
        };
        if (!string.IsNullOrWhiteSpace(gameFolder)) places.Add(Path.Combine(GameInstall.BepInExFolder(gameFolder), "units_config.txt"));

        foreach (string path in places)
        {
            try
            {
                if (!File.Exists(path)) continue;
                foreach (string line in TextFiles.ReadLinesShared(path))
                {
                    if (line.StartsWith("gun\t", StringComparison.Ordinal))
                    {
                        // gun <unit> <index> <name> <mode> <angle> <arc> <slew> <rof> <burst> <cooldown> <damage> <speed> <duration> <range> <multi> <offsetX> <offsetY> <group> <bulletTurn> [gunType]
                        string[] g = line.Split('\t');
                        if (g.Length < 19 || !units.TryGetValue(g[1], out GameUnit? owner)) continue;
                        owner.Guns.Add(new SteambirdsAi.AiGunStats
                        {
                            Index = (int)ParseNumber(g[2]),
                            Name = g[3],
                            Mode = (int)ParseNumber(g[4]),
                            Angle = (float)ParseNumber(g[5]),
                            Arc = (float)ParseNumber(g[6]),
                            FireRate = (float)ParseNumber(g[8]),
                            Burst = Math.Max(1, (int)ParseNumber(g[9])),
                            Reload = (float)ParseNumber(g[10]),
                            Damage = (float)ParseNumber(g[11]),
                            Speed = (float)ParseNumber(g[12]),
                            Duration = (float)ParseNumber(g[13]),
                            Range = (float)ParseNumber(g[14]),
                            Multi = Math.Max(1, (int)ParseNumber(g[15])),
                            SlewRate = (float)ParseNumber(g[7]),
                            OffsetX = (float)ParseNumber(g[16]),
                            OffsetY = (float)ParseNumber(g[17]),
                            Group = (int)ParseNumber(g[18]),
                            BulletTurn = g.Length > 19 ? (float)ParseNumber(g[19]) : 0f,
                            GunType = g.Length > 20 ? g[20] : ""
                        });
                        continue;
                    }
                    if (!line.StartsWith("unit\t", StringComparison.Ordinal)) continue;
                    string[] f = line.Split('\t');
                    if (f.Length < 20) continue;
                    units[f[1]] = new GameUnit
                    {
                        Name = f[1],
                        Tier = (int)ParseNumber(f[3]),
                        MaxHealth = ParseNumber(f[4]),
                        VelMin = ParseNumber(f[6]),
                        VelMax = ParseNumber(f[7]),
                        Turn = ParseNumber(f[8]),
                        Radius = ParseNumber(f[9]),
                        Wall = f[13] == "1",
                        Boss = f[15] == "1",
                        Weapons = (int)ParseNumber(f[18]),
                        Art = f[19]
                    };
                }
                if (units.Count > 0) return;
            }
            catch
            {
                // Try the next place.
            }
        }
    }

    private static double ParseNumber(string text)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0;
    }

    // ------------------------------------------------------------------ pictures

    // Picture of a floor piece, wall or decoration by its name in the game (case does not matter). Null when there is none.
    public AssetPicture? GroundPicture(string name)
    {
        return LoadPicture(groundPictures, groundHalf, "ground", name);
    }

    // Picture of the art (prefab) of a unit.
    public AssetPicture? UnitPicture(string art)
    {
        return LoadPicture(unitPictures, unitHalf, "units", art);
    }

    // First picture that exists among the names ("tag_wall_01", "tag_wall_02"...).
    public AssetPicture? FirstGroundPicture(params string[] names)
    {
        foreach (string name in names)
        {
            AssetPicture? picture = GroundPicture(name);
            if (picture != null) return picture;
        }
        return null;
    }

    private AssetPicture? LoadPicture(Dictionary<string, AssetPicture?> cache, Dictionary<string, double> halves, string subfolder, string name)
    {
        if (name.Length == 0 || !HasPictures) return null;
        if (cache.TryGetValue(name, out AssetPicture? cached)) return cached;

        AssetPicture? picture = null;
        string key = name.ToLowerInvariant();
        if (halves.TryGetValue(key, out double half))
        {
            try
            {
                string path = Path.Combine(Folder, subfolder, key + ".png");
                if (File.Exists(path))
                {
                    // Copy into a bitmap of our own so the file is not kept open.
                    using Image loaded = Image.FromFile(path);
                    var copy = new Bitmap(loaded.Width, loaded.Height, PixelFormat.Format32bppPArgb);
                    using (Graphics graphics = Graphics.FromImage(copy)) graphics.DrawImage(loaded, 0, 0, loaded.Width, loaded.Height);
                    picture = new AssetPicture(copy, half);
                }
            }
            catch
            {
                picture = null;
            }
        }
        cache[name] = picture;
        return picture;
    }
}
