using Lidgren.Network;

// Floor and walls of the server: sends ClientReceiveTerrainTiles (ID 38376).
//
// The client's tile grid (GameClient.SetupTerrainTiles): 12-unit cells, centered at
// (0, 6 * sqrt(2)) and rotated 45 degrees. The vertex of cell (54, 54) falls exactly on the world origin.
// Here the grid positions (a, b) are measured in tiles from that vertex:
//   x = 12 * (a - b) / sqrt(2)      y = 12 * (a + b) / sqrt(2)
// The "a" axis points to the screen's (+x, +y) diagonal and the "b" axis to (-x, +y).
//
// Each tile has 4x4 wall sub-cells of 3 units: walls[i, j], with i on the a axis and j on the b axis
// (GroundPlacement.PlaceWalls). A floor tile gets a wall on the sides where the neighbor has no floor.
// Extra walls (pillars, narrowings) are closed sub-cells. The message to the client is written by
// Maps, from the map file (folder "maps"); the coordinates and the FloorPlan live here.
//
// A cell with no tile at all: the client draws the floor of the default territory (grass, water, white clouds by
// day; GroundPlacement.ChangeCell and GetEmptyWaterTile). That is why the plan can also have "empty" tiles
// around the floor: of the same territory as the map (light, fog, clouds and minimap color of the tutorial), but
// with the floor piece swapped for a territory with no floor. What is left is the dark abyss of the original game.
public static class Ground
{
    public const ushort MsgClientReceiveTerrainTiles = 38376; // GeneratedDispatcher.Pack_ClientReceiveTerrainTiles
    public const float CellSize = 12f;   // WorldLayout.cellSize e GroundPlacement.tileSize
    public const float WorldRadius = 650f;      // WorldLayout.worldRadius default
    public const int WallsPerCell = 4;          // wall sub-cells per tile (4x4), like the WorldLayout

    // The client's grid (SpatialGrid.Set) does not accept negative indexes.
    // WorldRadius / CellSize = 650 / 12 = 54: index of the grid center.
    public const int GridCenter = 54;

    // FourPieceTile: Inside=0, Outside=1, SideA=2, SideB=3, Center=4.
    private const byte PieceCenter = 4;

    // Tile types (terrain_config_*.txt, TerritoryTileConfigContainer).
    public const byte TutorialTile = 63;   // dungeontutorialwalker
    public const byte NexusTile = 37;      // nexus
    // Territory without floor (groundTag "_none_"): only used to pick the floor piece of the empty tiles.
    public const byte NoFloorTerritory = 74; // dungeonsky

    // GroundAsset IDs of ground_assets.txt.
    public static readonly ushort[] TutorialAssets = { 61856, 61857, 61858, 61851 }; // dungeontutorial_center_01..04

    // 12 / sqrt(2): one tile along a or b moves this much in x and in y.
    private static readonly float Scale = CellSize / MathF.Sqrt(2f);

    // ------------------------------------------------------------------ coordinates

    public static void ToWorld(float a, float b, out float x, out float y)
    {
        x = Scale * (a - b);
        y = Scale * (a + b);
    }

    public static void ToGrid(float x, float y, out float a, out float b)
    {
        a = (x + y) / (2f * Scale);
        b = (y - x) / (2f * Scale);
    }

    // Rectangle of tiles: a from MinA to MaxA (MaxA not included), b from MinB to MaxB (MaxB not included).
    // The limits can be fractional (0.25 = one wall sub-cell): the floor uses only the integer part,
    // the AI areas use the exact value.
    public readonly struct Rect
    {
        public readonly float MinA;
        public readonly float MaxA;
        public readonly float MinB;
        public readonly float MaxB;

        public Rect(float minA, float maxA, float minB, float maxB)
        {
            MinA = minA;
            MaxA = maxA;
            MinB = minB;
            MaxB = maxB;
        }

        public float CenterA => (MinA + MaxA) * 0.5f;
        public float CenterB => (MinB + MaxB) * 0.5f;

        // Margin in tiles (0.25 = one wall sub-cell).
        public bool Contains(float a, float b, float margin)
        {
            return a >= MinA + margin && a <= MaxA - margin && b >= MinB + margin && b <= MaxB - margin;
        }

        // Clamps the point inside the rectangle, with a margin in tiles. Never inverts the limits.
        public void Confine(ref float a, ref float b, float margin)
        {
            a = Clamp(a, MinA + margin, MaxA - margin);
            b = Clamp(b, MinB + margin, MaxB - margin);
        }

        private static float Clamp(float v, float min, float max)
        {
            if (min > max)
            {
                float middle = (min + max) * 0.5f;
                return middle;
            }
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        public override string ToString()
        {
            return "a " + MinA + ".." + MaxA + ", b " + MinB + ".." + MaxB;
        }
    }

    // ------------------------------------------------------------------ plan

    // Set of floor tiles with edge walls and extra closed sub-cells. It comes from the drawing of a map
    // (Maps.Map.CreatePlan); whoever writes the message for the client is Maps.
    // Each tutorial player has their own plan.
    public sealed class FloorPlan
    {
        public readonly byte TileType;
        private readonly ushort[] assets;
        private readonly bool wallsOnEdges;
        private readonly HashSet<(int A, int B)> floor = new HashSet<(int A, int B)>();
        private readonly List<(int A, int B)> order = new List<(int A, int B)>();
        private readonly Dictionary<(int A, int B), HashSet<(int I, int J)>> extras =
            new Dictionary<(int A, int B), HashSet<(int I, int J)>>();

        // wallsOnEdges: automatic wall on the edge sub-cells of a tile whose neighbor on that side has no floor.
        public FloorPlan(byte tileType, ushort[] floorAssets, bool wallsOnEdges = true)
        {
            TileType = tileType;
            assets = floorAssets.Length > 0 ? floorAssets : TutorialAssets;
            this.wallsOnEdges = wallsOnEdges;
        }

        public int Count => order.Count;

        // Tiles with floor, in the order they came in.
        public IReadOnlyList<(int A, int B)> Tiles => order;

        public void AddTile(int a, int b)
        {
            if (floor.Add((a, b))) order.Add((a, b));
        }

        public void AddRect(Rect r)
        {
            for (int a = (int)MathF.Floor(r.MinA); a < r.MaxA; a++)
            {
                for (int b = (int)MathF.Floor(r.MinB); b < r.MaxB; b++)
                {
                    AddTile(a, b);
                }
            }
        }

        // Floor piece of the slot (k, l) of the tile: a deterministic variant of the drawing's pieces.
        public ushort DefaultPiece(int a, int b, int k, int l)
        {
            return assets[Variant(a, b, k, l, assets.Length)];
        }

        public bool HasFloor(int a, int b)
        {
            return floor.Contains((a, b));
        }

        // Sub-cell that is already an edge wall (the neighbor on that side has no floor).
        public bool IsEdgeWall(int a, int b, int i, int j)
        {
            if (!wallsOnEdges) return false;
            int last = WallsPerCell - 1;
            if (i == 0 && !HasFloor(a - 1, b)) return true;
            if (i == last && !HasFloor(a + 1, b)) return true;
            if (j == 0 && !HasFloor(a, b - 1)) return true;
            if (j == last && !HasFloor(a, b + 1)) return true;
            return false;
        }

        // Closes a sub-cell (extra wall). Only valid for tiles that have floor.
        public void CloseCell(int a, int b, int i, int j)
        {
            if (!HasFloor(a, b)) return;
            if (!extras.TryGetValue((a, b), out HashSet<(int I, int J)>? cells))
            {
                cells = new HashSet<(int I, int J)>();
                extras[(a, b)] = cells;
            }
            cells.Add((i, j));
        }

        // Opens an extra sub-cell. True if it was closed (the tile needs to be sent again).
        public bool OpenCell(int a, int b, int i, int j)
        {
            if (!extras.TryGetValue((a, b), out HashSet<(int I, int J)>? cells)) return false;
            bool opened = cells.Remove((i, j));
            if (cells.Count == 0) extras.Remove((a, b));
            return opened;
        }

        // Sub-cell by global position: si = 4 * a + i (a axis), sj = 4 * b + j (b axis).
        public static void Split(int sub, out int cell, out int inside)
        {
            cell = (int)MathF.Floor(sub / (float)WallsPerCell);
            inside = sub - cell * WallsPerCell;
        }

        public bool HasFloorAtSub(int si, int sj)
        {
            Split(si, out int a, out _);
            Split(sj, out int b, out _);
            return HasFloor(a, b);
        }

        public bool IsEdgeWallAtSub(int si, int sj)
        {
            Split(si, out int a, out int i);
            Split(sj, out int b, out int j);
            return HasFloor(a, b) && IsEdgeWall(a, b, i, j);
        }

        public void CloseSub(int si, int sj)
        {
            Split(si, out int a, out int i);
            Split(sj, out int b, out int j);
            CloseCell(a, b, i, j);
        }

        public bool OpenSub(int si, int sj)
        {
            Split(si, out int a, out int i);
            Split(sj, out int b, out int j);
            return OpenCell(a, b, i, j);
        }

        // Free sub-cell (has floor, is not an edge wall and was not closed).
        public bool SubIsFree(int si, int sj)
        {
            Split(si, out int a, out int i);
            Split(sj, out int b, out int j);
            if (!HasFloor(a, b) || IsEdgeWall(a, b, i, j)) return false;
            return !(extras.TryGetValue((a, b), out HashSet<(int I, int J)>? cells) && cells.Contains((i, j)));
        }

        public static (int A, int B) TileOfSub(int si, int sj)
        {
            Split(si, out int a, out _);
            Split(sj, out int b, out _);
            return (a, b);
        }

        public int ClosedCells()
        {
            int total = 0;
            foreach (HashSet<(int I, int J)> cells in extras.Values) total += cells.Count;
            return total;
        }

        // Edge walls (side without a neighbor with floor) plus the extra closed sub-cells of this tile.
        public bool[,] TileWalls(int a, int b)
        {
            var walls = new bool[WallsPerCell, WallsPerCell];
            for (int i = 0; i < WallsPerCell; i++)
            {
                for (int j = 0; j < WallsPerCell; j++)
                {
                    if (IsEdgeWall(a, b, i, j)) walls[i, j] = true;
                }
            }

            if (extras.TryGetValue((a, b), out HashSet<(int I, int J)>? cells))
            {
                foreach ((int I, int J) c in cells)
                {
                    if (c.I >= 0 && c.I < WallsPerCell && c.J >= 0 && c.J < WallsPerCell) walls[c.I, c.J] = true;
                }
            }
            return walls;
        }
    }

    // ------------------------------------------------------------------ maps

    // The ready-made plans (Rebel City and Training Grounds) live in files in the "maps" folder (Maps).

    // ------------------------------------------------------------------ helpers

    // Deterministic variant, so the floor does not always repeat the same asset.
    private static int Variant(int a, int b, int k, int l, int count)
    {
        int sum = a + b + k + l;
        return ((sum % count) + count) % count;
    }
}
