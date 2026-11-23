// The "original" layout of the open world: the twelve zones of the game's own world map (the map the Sergeant shows in the
// tutorial conversation), a few big contiguous zones instead of islands.
//
// What is from the game: the twelve zones and their names (Texts/lore: territory_tile_<tile>_name), which tile kind each one is
// (ruined city = beach, Mecha Hunting Grounds = lowlands, Local Guard Post = lowlands2, Poison Farms = lowlands3, Dog Base = midlands,
// Graveyard = midlands2, Fishing Zone = midlands3, Robot Factory = highlands, Hospital Area = highlands2, Egyptian Dig = highlands3,
// Noble Estates = mountains, Imperial Palace = highmountains) and where they sit on that map (Ruined City in the center, the lowland
// zones around it, the midland zones outside them, the highland zones and the nobles in the corners).
// What is rebuilt: the exact borders. Each zone is one or more sites of a weighted Voronoi diagram, with the border shaken by a smooth
// noise, so a zone is one wide region of one tile kind (the enemies of its folder, see WorldEnemyPools).
public static class WorldZones
{
    // A site: where it sits on the map (u to the right, v up, -1..1 across the picture of the game's map) and how big its zone is.
    public sealed record Site(string Tile, float U, float V, float Weight);

    public static readonly Site[] Original =
    {
        new Site("beach", 0.00f, -0.05f, 0.62f),          // Ruined City
        new Site("lowlands", -0.05f, 0.38f, 0.90f),       // Mecha Hunting Grounds (north of the center)
        new Site("lowlands3", -0.20f, -0.38f, 0.88f),     // Poison Farms (south-west of the center)
        new Site("lowlands2", 0.40f, -0.10f, 1.05f),      // Local Guard Post (east and south-east of the center)
        new Site("lowlands2", 0.34f, 0.24f, 0.65f),
        new Site("midlands2", -0.54f, 0.04f, 0.72f),      // Graveyard (the band on the west)
        new Site("midlands2", -0.44f, 0.50f, 0.55f),
        new Site("midlands2", -0.48f, -0.34f, 0.55f),
        new Site("midlands", 0.12f, 0.72f, 1.00f),        // Dog Base (north)
        new Site("midlands", -0.08f, -0.82f, 1.05f),      // Dog Base South
        new Site("midlands3", 0.74f, -0.28f, 0.80f),      // Fishing Zone (east)
        new Site("midlands3", 0.76f, 0.08f, 0.60f),
        new Site("highlands3", -0.80f, 0.55f, 1.00f),     // Egyptian Dig, the "Archeological Dig Site" (north-west corner)
        new Site("highlands2", -0.80f, -0.58f, 1.00f),    // Hospital Area (south-west corner)
        new Site("highlands", 0.88f, -0.70f, 0.85f),      // Robot Factory (south-east corner)
        new Site("mountains", 0.86f, 0.40f, 0.90f),       // Noble Estates (north-east)
        new Site("highmountains", 0.52f, 0.95f, 0.60f)    // Imperial Palace (far north-east)
    };

    // How far from the center the sites reach (fraction of the world radius): keeps the corners of the picture inside the circle.
    private const float Spread = 0.82f;
    // How much the borders wiggle (fraction of the world radius) and the size of one wiggle (fraction too).
    private const float WarpAmount = 0.07f;
    private const float WarpSize = 0.16f;

    // True when every tile kind of the template exists in the game's data.
    public static bool Usable()
    {
        return Original.All(s => WorldData.Regions.Any(r => r.Name == s.Tile));
    }

    // Gives every cell inside the land limit the zone it falls in. Returns the number of cells given.
    public static int Assign(WorldMap map, float[,] cx, float[,] cy, float landLimit, int seed)
    {
        float radius = map.Radius;
        var regions = Original.Select(s => WorldData.Regions.First(r => r.Name == s.Tile)).ToArray();
        float k = radius * Spread;
        float amount = radius * WarpAmount;
        float size = radius * WarpSize;
        int count = 0;
        for (int i = 0; i < map.Cells; i++)
        {
            for (int j = 0; j < map.Cells; j++)
            {
                if (!map.InWorld[i, j]) continue;
                float x = cx[i, j], y = cy[i, j];
                if (x * x + y * y > landLimit * landLimit) continue;
                float px = x + (Noise(seed, x / size, y / size, 1) - 0.5f) * 2f * amount;
                float py = y + (Noise(seed, x / size, y / size, 2) - 0.5f) * 2f * amount;
                int best = 0;
                float bestScore = float.MaxValue;
                for (int s = 0; s < Original.Length; s++)
                {
                    float dx = px - Original[s].U * k, dy = py - Original[s].V * k;
                    float score = MathF.Sqrt(dx * dx + dy * dy) / Original[s].Weight;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = s;
                    }
                }
                map.Kind[i, j] = regions[best].Tile;
                map.RegionAt[i, j] = regions[best];
                count++;
            }
        }
        return count;
    }

    // Smooth value noise, 0..1.
    private static float Noise(int seed, float x, float y, int channel)
    {
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        float a = Lattice(seed, ix, iy, channel), b = Lattice(seed, ix + 1, iy, channel);
        float c = Lattice(seed, ix, iy + 1, channel), d = Lattice(seed, ix + 1, iy + 1, channel);
        return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fy;
    }

    private static float Lattice(int seed, int x, int y, int channel)
    {
        unchecked
        {
            int h = seed * 73856093 ^ x * 19349663 ^ y * 83492791 ^ channel * 2654435;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return (h & 0xffff) / 65535f;
        }
    }
}
