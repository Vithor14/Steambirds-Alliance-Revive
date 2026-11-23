// The generated map of the open world: one tile kind per 12-unit cell (the client's subterritory grid), the wall sub-cells and,
// for every cell inside the world, the floor pieces already chosen (WorldGenerator fills it; WorldFloor writes it to the client).
//
// Coordinates are the same as Ground.cs: cell (a, b) measured in tiles from the vertex at the world origin; the client's
// grid index is Ground.GridCenter + a (and + b). Wall sub-cells are global: si = 4 * a + i, sj = 4 * b + j.
public sealed class WorldMap
{
    // Cells per axis of the client's grid (SpatialGrid: ceil(2 * radius / 12), made odd; 109 for 650 units, 217 for 1300)
    // and the index of its center (the client's grid does not take negative indexes: index = Half + a).
    public readonly int Cells;
    public readonly int Half;
    public const float SubCell = Ground.CellSize / Ground.WallsPerCell;   // 3 units

    // One floor piece (or scatter) of a tile, in the units of TerritoryTileSerializer.
    public struct Placement
    {
        public ushort Id;
        // Position inside the tile, 0..2 on each axis (the tile is two pieces wide).
        public float X, Y;
        public float? Rotation;
        // Zero = no scale in the message.
        public float ScaleX, ScaleY;
    }

    public sealed class Tile
    {
        public int A, B;
        public WorldData.TileKind Kind = null!;
        public readonly byte[] Pieces = new byte[4] { WorldData.PieceCenter, WorldData.PieceCenter, WorldData.PieceCenter, WorldData.PieceCenter };
        public readonly Placement[] Slots = new Placement[4];
        public readonly List<Placement> Scatter = new List<Placement>();
        public bool[,]? Walls;
        public float Distance;       // from the spawn point, to send the nearest tiles first
    }

    public int Seed { get; init; }
    public float Radius { get; }
    public string Summary { get; set; } = "";

    // Per cell [ia, ib] with ia = a + Half. Null region = water or canyon floor.
    public readonly WorldData.TileKind[,] Kind;
    public readonly WorldData.Region?[,] RegionAt;
    public readonly bool[,] InWorld;

    public WorldMap(float radius)
    {
        Radius = radius;
        int cells = (int)MathF.Ceiling(2f * radius / Ground.CellSize);
        if ((cells & 1) == 0) cells++;
        Cells = cells;
        Half = cells / 2;
        Kind = new WorldData.TileKind[cells, cells];
        RegionAt = new WorldData.Region?[cells, cells];
        InWorld = new bool[cells, cells];
    }

    // Size of the world for a scale (1 = the client's 650 units).
    public static float RadiusFor(float scale) => Ground.WorldRadius * scale;
    public readonly HashSet<(int Si, int Sj)> Walls = new HashSet<(int, int)>();
    public readonly List<Tile> Tiles = new List<Tile>();

    // Where the players are born.
    public float SpawnX, SpawnY, SpawnRotation;

    public bool InGrid(int a, int b)
    {
        return a >= -Half && a <= Half && b >= -Half && b <= Half;
    }

    public static (int A, int B) TileOf(float x, float y)
    {
        Ground.ToGrid(x, y, out float a, out float b);
        return ((int)MathF.Floor(a), (int)MathF.Floor(b));
    }

    public static (int Si, int Sj) SubCellOf(float x, float y)
    {
        Ground.ToGrid(x, y, out float a, out float b);
        return ((int)MathF.Floor(a * Ground.WallsPerCell), (int)MathF.Floor(b * Ground.WallsPerCell));
    }

    public static void CenterOf(int a, int b, out float x, out float y)
    {
        Ground.ToWorld(a + 0.5f, b + 0.5f, out x, out y);
    }

    public WorldData.TileKind KindAt(int a, int b)
    {
        return InGrid(a, b) ? Kind[a + Half, b + Half] : WorldData.Water;
    }

    public WorldData.Region? RegionOfTile(int a, int b)
    {
        return InGrid(a, b) ? RegionAt[a + Half, b + Half] : null;
    }

    public WorldData.Region? RegionOfPoint(float x, float y)
    {
        (int a, int b) = TileOf(x, y);
        return RegionOfTile(a, b);
    }

    public bool IsWall(float x, float y)
    {
        (int si, int sj) = SubCellOf(x, y);
        return Walls.Contains((si, sj));
    }

    // Land the planes can be placed on: inside the world, a region (not water or canyon) and not a wall sub-cell.
    public bool IsFreeLand(float x, float y)
    {
        (int a, int b) = TileOf(x, y);
        if (!InGrid(a, b) || RegionAt[a + Half, b + Half] == null) return false;
        return !IsWall(x, y);
    }

    // Land with no wall in a square of about "reach" units around it (for carriers and spawn sites).
    public bool IsOpenLand(float x, float y, float reach)
    {
        for (float dx = -reach; dx <= reach; dx += SubCell)
        {
            for (float dy = -reach; dy <= reach; dy += SubCell)
            {
                if (!IsFreeLand(x + dx, y + dy)) return false;
            }
        }
        return true;
    }

    public int LandCells()
    {
        int count = 0;
        for (int i = 0; i < Cells; i++)
        {
            for (int j = 0; j < Cells; j++)
            {
                if (InWorld[i, j] && RegionAt[i, j] != null) count++;
            }
        }
        return count;
    }
}
