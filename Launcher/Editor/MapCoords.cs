namespace SteambirdsLauncher.Editor;

// Coordinates of the map file (see maps\README.md).
//   u, v          units along the two axes of the floor grid (the map is drawn with u to the right and v up)
//   tile (a, b)   12 u wide: tile a goes from u = 12a to 12a + 12
//   slot (p, q)   6 u wide, 2 x 2 slots in a tile (floor pieces)
//   sub-cell (si, sj)   3 u wide, 4 x 4 sub-cells in a tile (walls and gates)
//   rot           degrees clockwise from the world +y; 315 faces +v (the far end of a dungeon), 135 faces -v (the entrance)
internal static class MapCoords
{
    public const double Tile = 12.0;
    public const double Slot = 6.0;
    public const double Sub = 3.0;
    public const int SubsPerTile = 4;

    // The client grid holds 54 tiles on each side of the origin: tiles a and b must be inside -54..53.
    public const int GridCenter = 54;
    public const int MinTile = -GridCenter;
    public const int MaxTile = GridCenter - 1;

    public const double FacePlusV = 315.0;
    public const double FaceMinusV = 135.0;

    // Rotation of the "up" of the canvas (+v) in the world rotation system.
    private const double CanvasOffset = 45.0;

    public static int Floor(double value) => (int)Math.Floor(value);

    public static int TileOf(double units) => Floor(units / Tile);
    public static int SubOf(double units) => Floor(units / Sub);
    public static int SlotOf(double units) => Floor(units / Slot);

    // Tile that holds a sub-cell index (floor division, so negative indexes work).
    public static int TileOfSub(int sub) => Floor(sub / (double)SubsPerTile);

    public static double NormalizeRotation(double degrees)
    {
        double r = degrees % 360.0;
        if (r < 0) r += 360.0;
        return r;
    }

    // Angle clockwise from "up" on the canvas (u right, v up) of an entity rotation.
    public static double CanvasAngle(double rot) => NormalizeRotation(rot + CanvasOffset);

    public static double RotationFromCanvasAngle(double angle) => NormalizeRotation(angle - CanvasOffset);

    public static bool InsideTileRange(int a, int b) => a >= MinTile && a <= MaxTile && b >= MinTile && b <= MaxTile;
}
