using System.Text.RegularExpressions;

// What the open world needs from the game's own data (read from the configBinary and ground_assets.txt):
//   territories     -> "Basic" (role World): elevation scale, disk sizes and the list of regions (beach, lowlands...)
//   territoryTiles  -> the tile kinds (ID, continuity group, canyon group, ground / cliff / fence / scatter tags)
//   groundAssets    -> per floor piece weight, flips and random scale / rotation
//   ground_assets.txt (plugin export) -> the ID of each floor piece
// The client put the pieces on the ground from these same tables (GroundAssetContainer, WorldLayout), so the server follows
// the same rules to choose them. The map itself (which tile is where) was made by the original server and is rebuilt
// in WorldGenerator.
public static class WorldData
{
    // FourPieceTile of the client: the same numbers go on the wire.
    public const byte PieceInside = 0;
    public const byte PieceOutside = 1;
    public const byte PieceSideA = 2;
    public const byte PieceSideB = 3;
    public const byte PieceCenter = 4;

    public sealed class StripRule
    {
        public int Distance;
        public int MinLength;
        public int MaxLength;
        public float Straightness;
        public float ChanceOfMissing;
    }

    public sealed class TileKind
    {
        public string Name = "";
        public byte Id;
        public string Continuity = "";
        public string CloseCanyon = "";
        public int CanyonSpacing;
        public string Ground = "";
        public string Cliff = "";
        public string Fence = "";
        public string Scatter = "";
        public float ScatterProbability;
        public float ScaleMin = 0.7f, ScaleMax = 1.3f;
        public float RotationMin = -180f, RotationMax = 180f;

        public bool SameContinuity(TileKind other) => Continuity == "_any_" || other.Continuity == "_any_" || Continuity == other.Continuity;
        public bool SameCanyonGroup(TileKind other) => CloseCanyon == "_any_" || other.CloseCanyon == "_any_" || CloseCanyon == other.CloseCanyon;
    }

    // A region of the Basic terrain: a tile kind plus the elevation band where it lives and its tier.
    public sealed class Region
    {
        public string Name = "";
        // "lowlands2" -> Base "lowlands" (the text of the HUD) and Variant 2 (1 when there is no digit).
        public string Base = "";
        // Name of the zone in the game's texts ("Mecha Hunting Grounds", "Graveyard"...); the region name when the texts do not have one.
        public string ZoneName = "";
        public int Variant = 1;
        public int SortOrder;
        public float MinElevation;
        public float MaxElevation;
        public int Tier;
        public float SecondsPerStandardEnemyIn10x10;
        public StripRule? Strip;
        public TileKind Tile = null!;
    }

    public sealed class Asset
    {
        public ushort Id;
        public string Name = "";
        public float Weight = 1f;
        public bool CanFlipX = true;
        public bool CanFlipY = true;
        public (float Min, float Max)? Scale;
        public (float Min, float Max)? Rotation;
    }

    // ------------------------------------------------------------------ parameters of the Basic terrain

    public static float MinDiskRadius = 24.5f;
    public static float MaxDiskRadius = 61.25f;
    public static float MaxOverlap = 0.9f;
    public static float DistancePerElevation = 8.575f;
    public static float OriginElevation = 100f;
    public static string RequiredPreTerritory = "beach";

    public static readonly List<Region> Regions = new List<Region>();
    public static readonly Dictionary<string, TileKind> Tiles = new Dictionary<string, TileKind>(StringComparer.Ordinal);
    public static TileKind Water = null!;
    public static TileKind CanyonFloor = null!;

    private static readonly Dictionary<(string Tag, byte Piece), List<Asset>> bottomPieces = new Dictionary<(string, byte), List<Asset>>();
    private static readonly Dictionary<string, List<Asset>> scatterPieces = new Dictionary<string, List<Asset>>(StringComparer.Ordinal);

    // Same expression as GroundAssetContainer.Init: "<tag>_<kind>[_<number>]".
    private static readonly Regex AssetName = new Regex("([^_]*?)_([^_]*?|[^_]*?_[^_]*?)(?:_([0-9]+)|)$");
    private static readonly Regex AssetLine = new Regex(@"^ID=(\d+)\s+name=(\S+)");

    private static readonly object padlock = new object();

    public static bool Ready { get; private set; }
    public static string LoadError { get; private set; } = "";
    public static int AssetCount { get; private set; }

    // Reads everything. False (with LoadError) when something the world cannot do without is missing.
    public static bool Load(Action<string, string> log)
    {
        lock (padlock)
        {
            if (Ready) return true;
            try
            {
                if (!ConfigBinary.Loaded) throw new InvalidOperationException("the configBinary was not found (see the CONFIG lines at the start of the log)");
                LoadTerritory();
                LoadTiles();
                LoadAssets();
                BuildRegions();
                Ready = true;
                LoadError = "";
                log("WORLD", "game data: " + Regions.Count(r => r.SortOrder >= 0) + " regions (" +
                    string.Join(", ", Regions.Where(r => r.SortOrder >= 0).OrderBy(r => r.MinElevation).Select(r => r.Name)) + "), " +
                    Tiles.Count + " tile kinds, " + AssetCount + " floor pieces; disks " + MinDiskRadius + ".." + MaxDiskRadius +
                    ", " + DistancePerElevation + " units per elevation point from " + OriginElevation);
                return true;
            }
            catch (Exception e)
            {
                LoadError = e.Message;
                log("WORLD", "the world data did not load: " + e.Message);
                return false;
            }
        }
    }

    private static ConfigNode basic = ConfigNode.Missing;
    private static ConfigNode tileNodes = ConfigNode.Missing;
    private static ConfigNode assetConfigs = ConfigNode.Missing;

    private static void LoadTerritory()
    {
        basic = ConfigNode.Missing;
        foreach (ConfigNode t in ConfigBinary.Get("territories").Items)
        {
            if (string.Equals(t["name"].AsString(""), "Basic", StringComparison.OrdinalIgnoreCase)) basic = t;
        }
        if (basic.IsMissing) throw new InvalidDataException("'territories' of the configBinary has no 'Basic' terrain");
        MinDiskRadius = basic["minDiskRadius"].AsFloat(MinDiskRadius);
        MaxDiskRadius = basic["maxDiskRadius"].AsFloat(MaxDiskRadius);
        MaxOverlap = basic["maxAllowedOverlapOnSubterritory"].AsFloat(MaxOverlap);
        DistancePerElevation = Math.Max(0.1f, basic["distancePerElevation"].AsFloat(DistancePerElevation));
        OriginElevation = basic["originElevation"].AsFloat(OriginElevation);
        List<string> pre = basic["islandRequiredPreTerritories"].AsStringList();
        if (pre.Count > 0) RequiredPreTerritory = pre[0];
    }

    private static void LoadTiles()
    {
        tileNodes = ConfigBinary.Get("territoryTiles");
        if (tileNodes.IsMissing || !tileNodes.IsDictionary) throw new InvalidDataException("'territoryTiles' is missing in the configBinary");
        Tiles.Clear();
        foreach (KeyValuePair<string, ConfigNode> pair in tileNodes.Pairs)
        {
            int id = Inherited(pair.Key, "ID").AsInt(0);
            if (id <= 0 || id > 255) continue;
            var tile = new TileKind
            {
                Name = pair.Key,
                Id = (byte)id,
                Continuity = Inherited(pair.Key, "continuityGroup").AsString("default"),
                CloseCanyon = Inherited(pair.Key, "closeCanyonGroup").AsString("default"),
                CanyonSpacing = Inherited(pair.Key, "closeCanyonSpacing").AsInt(0),
                Ground = Inherited(pair.Key, "groundTag").AsString(""),
                Cliff = Inherited(pair.Key, "cliffTag").AsString(""),
                Fence = Inherited(pair.Key, "fenceTag").AsString(""),
                Scatter = Inherited(pair.Key, "scatterTag").AsString("_none_"),
                ScatterProbability = Inherited(pair.Key, "groundScatterProbability").AsFloat(0f)
            };
            ReadRange(Inherited(pair.Key, "groundScatterScale"), ref tile.ScaleMin, ref tile.ScaleMax);
            ReadRange(Inherited(pair.Key, "groundScatterRotation"), ref tile.RotationMin, ref tile.RotationMax);
            Tiles[pair.Key] = tile;
        }
        if (!Tiles.TryGetValue("water", out TileKind? water)) throw new InvalidDataException("'territoryTiles' has no 'water'");
        if (!Tiles.TryGetValue("canyonFloor", out TileKind? canyon)) throw new InvalidDataException("'territoryTiles' has no 'canyonFloor'");
        Water = water;
        CanyonFloor = canyon;
    }

    // A field of a tile kind, looking through "basedOn" like the client's LoadUtils.SetParentDefaults.
    private static ConfigNode Inherited(string tile, string key)
    {
        string current = tile;
        for (int depth = 0; depth < 12 && current.Length > 0; depth++)
        {
            ConfigNode node = tileNodes[current];
            if (node.IsMissing) break;
            if (node.Has(key)) return node[key];
            current = node["basedOn"].AsString("");
        }
        return ConfigNode.Missing;
    }

    private static void ReadRange(ConfigNode node, ref float min, ref float max)
    {
        if (node.IsList && node.Count >= 2)
        {
            min = node[0].AsFloat(min);
            max = node[1].AsFloat(max);
        }
    }

    private static void BuildRegions()
    {
        Regions.Clear();
        foreach (ConfigNode r in basic["territories"].Items)
        {
            string name = r["name"].AsString("");
            if (name.Length == 0 || !Tiles.TryGetValue(name, out TileKind? tile)) continue;
            var region = new Region
            {
                Name = name,
                SortOrder = r["sortOrder"].AsInt(0),
                MinElevation = r["minElevation"].AsFloat(0f),
                MaxElevation = r["maxElevation"].AsFloat(100f),
                Tier = r["tier"].AsInt(0),
                SecondsPerStandardEnemyIn10x10 = r["secondsPerStandardEnemyIn10x10"].AsFloat(30f),
                Tile = tile
            };
            int digits = name.Length;
            while (digits > 0 && char.IsDigit(name[digits - 1])) digits--;
            region.Base = name.Substring(0, digits);
            region.ZoneName = ConfigBinary.Get("Texts/en-US/lore")["territory_tile_" + name + "_name"].AsString(name);
            region.Variant = digits < name.Length && int.TryParse(name.Substring(digits), out int v) ? v : 1;
            if (string.Equals(r["wallPlacement"].AsString(""), "strip", StringComparison.OrdinalIgnoreCase))
            {
                ConfigNode s = r["strip"];
                region.Strip = new StripRule
                {
                    Distance = Math.Max(2, s["distance"].AsInt(14)),
                    MinLength = Math.Max(1, s["minLength"].AsInt(3)),
                    MaxLength = Math.Max(1, s["maxLength"].AsInt(6)),
                    Straightness = Math.Clamp(s["straightness"].AsFloat(0.6f), 0f, 1f),
                    ChanceOfMissing = Math.Clamp(s["chanceOfMissing"].AsFloat(0f), 0f, 1f)
                };
                if (region.Strip.MaxLength < region.Strip.MinLength) region.Strip.MaxLength = region.Strip.MinLength;
            }
            Regions.Add(region);
        }
        if (!Regions.Any(r => r.SortOrder >= 0)) throw new InvalidDataException("the 'Basic' terrain has no regions");
        if (!Regions.Any(r => string.Equals(r.Name, RequiredPreTerritory, StringComparison.Ordinal)))
            throw new InvalidDataException("the starting region '" + RequiredPreTerritory + "' is not among the regions of 'Basic'");
    }

    private static void LoadAssets()
    {
        assetConfigs = ConfigBinary.Get("groundAssets");
        string path = GameDataFiles.FilePath("ground_assets.txt");
        if (!File.Exists(path))
            throw new FileNotFoundException("ground_assets.txt was not found (looked in " + path + "). Run SteambirdsGameFiles.exe (it copies it from your " +
                "game) or copy BepInEx\\ground_assets.txt of the game to Server\\data");

        bottomPieces.Clear();
        scatterPieces.Clear();
        var seen = new HashSet<ushort>();
        int count = 0;
        foreach (string line in File.ReadLines(path))
        {
            Match line1 = AssetLine.Match(line);
            if (!line1.Success) continue;
            if (!ushort.TryParse(line1.Groups[1].Value, out ushort id) || !seen.Add(id)) continue;
            string name = line1.Groups[2].Value;
            count++;

            var asset = new Asset { Id = id, Name = name };
            ConfigNode config = assetConfigs[name];
            if (!config.IsMissing)
            {
                asset.Weight = config["weight"].AsFloat(1f);
                asset.CanFlipX = config["canFlipX"].AsBool(true);
                asset.CanFlipY = config["canFlipY"].AsBool(true);
                if (config["randomScale"].IsList && config["randomScale"].Count >= 2)
                    asset.Scale = (config["randomScale"][0].AsFloat(1f), config["randomScale"][1].AsFloat(1f));
                if (config["randomRotation"].IsList && config["randomRotation"].Count >= 2)
                    asset.Rotation = (config["randomRotation"][0].AsFloat(0f), config["randomRotation"][1].AsFloat(0f));
            }

            Match m = AssetName.Match(name);
            if (!m.Success) continue;
            string tag = m.Groups[1].Value;
            string kind = m.Groups[2].Value;
            byte? piece = kind switch
            {
                "center" => PieceCenter,
                "cliff_inside" => PieceInside,
                "cliff_outside" => PieceOutside,
                "cliff_side" => PieceSideA,
                _ => null
            };
            if (piece.HasValue)
            {
                AddBottom(tag, piece.Value, asset);
                if (piece.Value == PieceSideA) AddBottom(tag, PieceSideB, asset);
            }
            else if (kind != "wall" && kind != "dwall")
            {
                if (!scatterPieces.TryGetValue(tag, out List<Asset>? list))
                {
                    list = new List<Asset>();
                    scatterPieces[tag] = list;
                }
                list.Add(asset);
            }
        }
        AssetCount = count;
        if (count == 0) throw new InvalidDataException(path + " has no floor pieces");
    }

    private static void AddBottom(string tag, byte piece, Asset asset)
    {
        if (!bottomPieces.TryGetValue((tag, piece), out List<Asset>? list))
        {
            list = new List<Asset>();
            bottomPieces[(tag, piece)] = list;
        }
        list.Add(asset);
    }

    // ------------------------------------------------------------------ lookups

    public static List<Asset>? Bottom(string tag, byte piece)
    {
        return bottomPieces.TryGetValue((tag, piece), out List<Asset>? list) ? list : null;
    }

    public static List<Asset>? Scatters(string tag)
    {
        return scatterPieces.TryGetValue(tag, out List<Asset>? list) ? list : null;
    }

    // Elevation of a point at this distance from the world's center.
    public static float ElevationAt(float distance)
    {
        return OriginElevation - distance / DistancePerElevation;
    }

    // The regions the generator may put on land, from the center outwards.
    public static IEnumerable<Region> LandRegions()
    {
        return Regions.Where(r => r.SortOrder >= 0).OrderByDescending(r => r.MaxElevation).ThenBy(r => r.Variant);
    }

    // The variants of the region that holds this elevation (lowlands, lowlands2, lowlands3...).
    public static List<Region> RegionsAtElevation(float elevation)
    {
        // Half open (a band starts where the next one ends); the top band also holds the origin itself.
        List<Region> found = LandRegions().Where(r => elevation >= r.MinElevation &&
            (elevation < r.MaxElevation || (r.MaxElevation >= OriginElevation && elevation <= r.MaxElevation))).ToList();
        if (found.Count > 0) return found;
        // Past the ends of the scale: the nearest band.
        Region nearest = LandRegions().OrderBy(r => elevation > r.MaxElevation ? elevation - r.MaxElevation : r.MinElevation - elevation).First();
        return LandRegions().Where(r => r.Base == nearest.Base).ToList();
    }
}
