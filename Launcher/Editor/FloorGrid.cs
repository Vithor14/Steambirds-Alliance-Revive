using System.Text;
using System.Text.Json.Nodes;

namespace SteambirdsLauncher.Editor;

// The floor of a map: the tiles that have floor and the closed (wall) sub-cells inside them.
//
// In the file ("floor.drawing") the floor is a text drawing, one character per sub-cell of 3 u:
//   '.' free floor, '#' wall, '-' no floor.
// A tile has floor when any of its 16 sub-cells is not '-'. When "wallsOnEdges" is on the server adds a wall on the
// edge sub-cells of a tile whose neighbour on that side has no floor. The editor keeps only the real walls that
// go beyond those automatic borders ("walls"), so moving or erasing a tile never leaves old border walls behind;
// the borders are written to the file again when it is saved.
internal sealed class FloorGrid
{
    private readonly HashSet<(int A, int B)> tiles = new HashSet<(int A, int B)>();
    private readonly HashSet<(int Si, int Sj)> walls = new HashSet<(int Si, int Sj)>();

    public const string DefaultLegend = ". free floor, # wall, - no floor. First line = last sj row (the far end of the map).";

    // "wallsOnEdges": automatic walls on the edges of the floor.
    public bool AutoBorders { get; set; } = true;

    public int TileCount => tiles.Count;
    public int WallCount => walls.Count;
    public IEnumerable<(int A, int B)> Tiles => tiles;
    public IEnumerable<(int Si, int Sj)> Walls => walls;

    public bool HasTile(int a, int b) => tiles.Contains((a, b));

    // True when something changed.
    public bool SetTile(int a, int b, bool on)
    {
        if (on) return tiles.Add((a, b));
        if (!tiles.Remove((a, b))) return false;
        int si0 = a * MapCoords.SubsPerTile;
        int sj0 = b * MapCoords.SubsPerTile;
        for (int i = 0; i < MapCoords.SubsPerTile; i++)
        {
            for (int j = 0; j < MapCoords.SubsPerTile; j++) walls.Remove((si0 + i, sj0 + j));
        }
        return true;
    }

    // A wall only exists inside a tile with floor. True when something changed.
    public bool SetWall(int si, int sj, bool on)
    {
        if (!on) return walls.Remove((si, sj));
        if (!HasTile(MapCoords.TileOfSub(si), MapCoords.TileOfSub(sj))) return false;
        return walls.Add((si, sj));
    }

    public bool IsAutoBorder(int si, int sj)
    {
        if (!AutoBorders) return false;
        int a = MapCoords.TileOfSub(si);
        int b = MapCoords.TileOfSub(sj);
        if (!HasTile(a, b)) return false;
        int i = si - a * MapCoords.SubsPerTile;
        int j = sj - b * MapCoords.SubsPerTile;
        int last = MapCoords.SubsPerTile - 1;
        return (i == 0 && !HasTile(a - 1, b)) ||
               (i == last && !HasTile(a + 1, b)) ||
               (j == 0 && !HasTile(a, b - 1)) ||
               (j == last && !HasTile(a, b + 1));
    }

    // Wall drawn by the editor and written to the file (own wall or automatic border).
    public bool IsWall(int si, int sj)
    {
        return walls.Contains((si, sj)) || IsAutoBorder(si, sj);
    }

    public bool IsOwnWall(int si, int sj) => walls.Contains((si, sj));

    // Sub-cell where a unit or a gate box can stand: floor, not a wall.
    public bool IsFreeSub(int si, int sj)
    {
        return HasTile(MapCoords.TileOfSub(si), MapCoords.TileOfSub(sj)) && !IsWall(si, sj);
    }

    public bool HasFloorAt(double u, double v) => HasTile(MapCoords.TileOf(u), MapCoords.TileOf(v));

    public void Clear()
    {
        tiles.Clear();
        walls.Clear();
    }

    // Tile range of the floor. False when there is no floor.
    public bool GetBounds(out int minA, out int maxA, out int minB, out int maxB)
    {
        minA = minB = int.MaxValue;
        maxA = maxB = int.MinValue;
        foreach ((int a, int b) in tiles)
        {
            if (a < minA) minA = a;
            if (a > maxA) maxA = a;
            if (b < minB) minB = b;
            if (b > maxB) maxB = b;
        }
        return tiles.Count > 0;
    }

    // Moves the whole floor by whole tiles.
    public void Shift(int tilesA, int tilesB)
    {
        if (tilesA == 0 && tilesB == 0) return;
        var movedTiles = tiles.Select(t => (t.A + tilesA, t.B + tilesB)).ToList();
        var movedWalls = walls.Select(w => (w.Si + tilesA * MapCoords.SubsPerTile, w.Sj + tilesB * MapCoords.SubsPerTile)).ToList();
        tiles.Clear();
        walls.Clear();
        foreach ((int a, int b) in movedTiles) tiles.Add((a, b));
        foreach ((int si, int sj) in movedWalls) walls.Add((si, sj));
    }

    // ------------------------------------------------------------------ file

    // Reads "floor" (wallsOnEdges and drawing). Characters other than . and # count as no floor.
    public void Load(JsonObject floorJson)
    {
        Clear();
        AutoBorders = Json.Bool(floorJson, "wallsOnEdges", true);
        if (floorJson["drawing"] is not JsonObject drawing || drawing["lines"] is not JsonArray rows) return;

        int si0 = Json.Int(drawing, "siStart");
        int sj0 = Json.Int(drawing, "sjStart");
        var closed = new List<(int Si, int Sj)>();
        for (int n = 0; n < rows.Count; n++)
        {
            string row = Json.AsString(rows[n]);
            int sj = sj0 + (rows.Count - 1 - n);
            for (int c = 0; c < row.Length; c++)
            {
                char ch = row[c];
                if (ch != '.' && ch != '#') continue;
                int si = si0 + c;
                tiles.Add((MapCoords.TileOfSub(si), MapCoords.TileOfSub(sj)));
                if (ch == '#') closed.Add((si, sj));
            }
        }

        foreach ((int si, int sj) in closed)
        {
            if (!IsAutoBorder(si, sj)) walls.Add((si, sj));
        }
    }

    // Writes "floor.wallsOnEdges" and "floor.drawing" (the other fields of "floor" are not touched).
    public void Save(JsonObject floorJson)
    {
        floorJson["wallsOnEdges"] = AutoBorders;
        if (!GetBounds(out int minA, out int maxA, out int minB, out int maxB))
        {
            floorJson.Remove("drawing");
            return;
        }

        int per = MapCoords.SubsPerTile;
        int si0 = minA * per;
        int sj0 = minB * per;
        int width = (maxA - minA + 1) * per;
        int height = (maxB - minB + 1) * per;

        var lines = new JsonArray();
        var line = new StringBuilder(width);
        for (int n = 0; n < height; n++)
        {
            int sj = sj0 + (height - 1 - n);
            line.Clear();
            for (int c = 0; c < width; c++)
            {
                int si = si0 + c;
                if (!HasTile(MapCoords.TileOfSub(si), MapCoords.TileOfSub(sj))) line.Append('-');
                else line.Append(IsWall(si, sj) ? '#' : '.');
            }
            lines.Add(JsonValue.Create(line.ToString()));
        }

        JsonObject drawing = Json.Obj(floorJson, "drawing");
        drawing["siStart"] = si0;
        drawing["sjStart"] = sj0;
        if (!Json.Has(drawing, "legend")) drawing["legend"] = DefaultLegend;
        drawing["lines"] = lines;
    }
}
