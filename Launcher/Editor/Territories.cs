namespace SteambirdsLauncher.Editor;

// What the editor knows about the terrain of each dungeon of the game (the "terrain" and "territory" of the map file):
// the tag of its floor and wall pieces and the floor pieces the generated maps use.
// Source: the build 190828 maps (the generated dungeons of the project) and the territory table of the game.
internal sealed class TerritoryInfo
{
    public TerritoryInfo(string terrain, int territory, string label, string groundTag, string wallTag)
    {
        Terrain = terrain;
        Territory = territory;
        Label = label;
        GroundTag = groundTag;
        WallTag = wallTag;
    }

    // "terrain": terrain of the client (also the "type" of dungeons.json).
    public string Terrain { get; }

    // "territory": TerritoryTileConfig of the tiles (light, fog, minimap color, walls).
    public int Territory { get; }

    public string Label { get; }

    // Prefix of the floor pieces (<tag>_center_01...) and of the walls (<tag>_wall_01...) of the game.
    public string GroundTag { get; }
    public string WallTag { get; }

    public int[] DefaultPieces => Territories.PiecesOf(GroundTag);

    public override string ToString() => Label + " (" + Terrain + ")";
}

internal static class Territories
{
    public static readonly TerritoryInfo[] Dungeons =
    {
        new TerritoryInfo("DungeonBomb", 41, "Bomb Factory", "dungeonfactory2", "dungeonfactory2"),
        new TerritoryInfo("DungeonObstacle", 44, "Laser Playground", "dungeonobstacle", "dungeonobstacle"),
        new TerritoryInfo("DungeonArena1", 64, "The Arena", "dungeonarena", "dungeonarena"),
        new TerritoryInfo("DungeonGrind", 43, "Home for Special Children", "dungeongrindhouse", "dungeongrindhouse"),
        new TerritoryInfo("DungeonEye", 42, "Crypt of Cathulhu", "dungeonpalace", "dungeonpalace"),
        new TerritoryInfo("DungeonFire", 36, "Reeducation Camp", "dungeonfire", "dungeonfire"),
        new TerritoryInfo("DungeonIce", 31, "Frozen Owl Monastery", "dungeonruinssnow", "dungeonruinssnow"),
        new TerritoryInfo("DungeonSnake", 34, "Venom Compound", "industrialboss", "dungeonfactory"),
        new TerritoryInfo("DungeonThief", 59, "The Vaults", "dungeonthief", "dungeonthief"),
        new TerritoryInfo("DungeonSpace", 58, "Super Secret Base", "dungeonmilitary", "dungeonmilitary"),
        new TerritoryInfo("DungeonTemple", 61, "Temple of Bast", "dungeonpalace", "dungeonpalace"),
        new TerritoryInfo("DungeonSneezy", 67, "Iron Lair", "industrialboss", "industrialboss"),
        new TerritoryInfo("DungeonArena2", 65, "The Elder Arena", "dungeonarena2", "dungeonarena2"),
        new TerritoryInfo("DungeonRescue", 45, "The Dig", "dungeoncave", "dungeoncave"),
        new TerritoryInfo("DungeonFinalBoss", 35, "The Great Leader's Command Center", "dungeonboss", "dungeonboss"),
        new TerritoryInfo("Dungeon3Brothers", 73, "The Three Brothers", "dungeonboss2", "dungeonboss2"),
        new TerritoryInfo("DungeonFinalBoss2", 73, "The Great Leader's Inner Sanctum", "dungeonboss2", "dungeonboss2")
    };

    // Territories of the maps that are not dungeons (only for drawing the floor).
    private static readonly TerritoryInfo[] Others =
    {
        new TerritoryInfo("Nexus", 37, "Rebel City", "nexus", "nexus"),
        new TerritoryInfo("TutorialWalker", 63, "Training Grounds", "dungeontutorial", "dungeontutorial")
    };

    // Floor pieces (GroundAsset ids, center_01..NN in this order) the generated dungeons use, by floor tag.
    private static readonly Dictionary<string, int[]> Pieces = new Dictionary<string, int[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["dungeonfactory2"] = new[] { 17626, 17623, 17624, 17621, 17622, 17619, 17620, 17633 },
        ["dungeonobstacle"] = new[] { 39691, 39690, 39689 },
        ["dungeonarena"] = new[] { 31643, 55660, 25809, 7626, 43311, 1792, 37477 },
        ["dungeonarena2"] = new[] { 42241, 42244, 42243, 42238, 42237 },
        ["dungeongrindhouse"] = new[] { 37616, 37615, 37614 },
        ["dungeonpalace"] = new[] { 45502, 45499, 45500, 45497 },
        ["dungeonfire"] = new[] { 29934, 29933 },
        ["dungeonruinssnow"] = new[] { 5754, 29771, 65456, 23937 },
        ["industrialboss"] = new[] { 58814, 28963, 64648 },
        ["dungeonthief"] = new[] { 48638, 18787, 54472 },
        ["dungeonmilitary"] = new[] { 20761, 20760, 20759, 20766 },
        ["dungeoncave"] = new[] { 34315, 34314 },
        ["dungeonboss"] = new[] { 42135, 42134, 42133, 42132, 42131, 42130, 42129, 42128, 42127, 6451, 6450, 6449 },
        ["dungeonboss2"] = new[] { 58053, 28202, 63887, 10700, 46385, 16534, 52219, 64568, 34717, 22369, 58054, 28203 },
        ["dungeontutorial"] = new[] { 61856, 61857, 61858, 61851 }
    };

    public static int[] PiecesOf(string groundTag)
    {
        return Pieces.TryGetValue(groundTag, out int[]? ids) ? ids : Array.Empty<int>();
    }

    public static TerritoryInfo? ForTerrain(string terrain)
    {
        foreach (TerritoryInfo info in Dungeons)
        {
            if (string.Equals(info.Terrain, terrain, StringComparison.OrdinalIgnoreCase)) return info;
        }
        foreach (TerritoryInfo info in Others)
        {
            if (string.Equals(info.Terrain, terrain, StringComparison.OrdinalIgnoreCase)) return info;
        }
        return null;
    }

    // First territory with this number (the file only has the number).
    public static TerritoryInfo? ForTerritory(int territory)
    {
        foreach (TerritoryInfo info in Dungeons)
        {
            if (info.Territory == territory) return info;
        }
        foreach (TerritoryInfo info in Others)
        {
            if (info.Territory == territory) return info;
        }
        return null;
    }
}
