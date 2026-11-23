// Builds the map of the open world from a seed.
//
// Reconstruction: the client brings the WorldLayout data structure and the tables (regions by elevation, disk sizes, canyon
// groups, ground pieces), but the program that filled the map ran on the original server and is in no client build. What is
// rebuilt here follows those tables:
//   1. Elevation falls with the distance from the center (originElevation, distancePerElevation); each elevation band of the
//      'Basic' terrain is a region (beach in the center, then lowlands, midlands, highlands, mountains outwards).
//   2. Land is made of overlapping disks (minDiskRadius .. maxDiskRadius, overlap up to maxAllowedOverlapOnSubterritory); each
//      disk is a variant of the region at its center. Around the center there is always a beach (islandRequiredPreTerritories).
//   3. MakeWideCanyons and the edge clip of the client's WorldLayout: a canyon floor cell between regions of different groups.
//   4. Walls: short strips inside each region (wallPlacement 'strip' of the region).
//   5. The floor pieces of every cell, chosen like WorldLayout.PlaceGroundAssetsForCell (cliff pieces where the continuity
//      group changes, scatter and fence pieces).
public static class WorldGenerator
{
    private sealed class Disk
    {
        public float X, Y, R;
        public WorldData.Region Region = null!;
    }

    // The client's PerimeterNeighbors (x, y) in tile steps.
    private static readonly (int Dx, int Dy)[] Perimeter =
    {
        (0, -1), (-1, -1), (-1, 0), (-1, 1), (0, 1), (1, 1), (1, 0), (1, -1)
    };

    private static readonly (int Di, int Dj)[] Neighbor4 = { (0, 1), (1, 0), (0, -1), (-1, 0) };

    public static WorldMap Build(int seed, float sizeScale, float biomeScale, string layout, float landFraction, float spawnX, float spawnY, float spawnRotation, float safeRadius,
        Action<string, string> log)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        float worldRadius = WorldMap.RadiusFor(sizeScale);
        float landLimit = worldRadius - 36f;
        float diskMin = WorldData.MinDiskRadius * biomeScale;
        float diskMax = WorldData.MaxDiskRadius * biomeScale;
        var map = new WorldMap(worldRadius) { Seed = seed, SpawnX = spawnX, SpawnY = spawnY, SpawnRotation = spawnRotation };
        var rng = new Random(seed);
        // "original": the zones of the game's own world map (WorldZones); anything else: islands made of random disks.
        bool original = string.Equals(layout, "original", StringComparison.OrdinalIgnoreCase) && WorldZones.Usable();

        // Center and mask of every cell.
        var cx = new float[map.Cells, map.Cells];
        var cy = new float[map.Cells, map.Cells];
        var reach = new float[map.Cells, map.Cells];
        int landable = 0;
        for (int i = 0; i < map.Cells; i++)
        {
            for (int j = 0; j < map.Cells; j++)
            {
                WorldMap.CenterOf(i - map.Half, j - map.Half, out float x, out float y);
                cx[i, j] = x;
                cy[i, j] = y;
                reach[i, j] = MathF.Sqrt(x * x + y * y);
                map.InWorld[i, j] = reach[i, j] <= worldRadius - 6f;
                map.Kind[i, j] = WorldData.Water;
                if (reach[i, j] <= landLimit) landable++;
            }
        }

        // ---- 1 and 2: disks of land
        var disks = new List<Disk>();
        var owner = new Disk?[map.Cells, map.Cells];
        var ownerScore = new float[map.Cells, map.Cells];
        for (int i = 0; i < map.Cells; i++)
        {
            for (int j = 0; j < map.Cells; j++) ownerScore[i, j] = float.MaxValue;
        }
        int land = 0;

        void Apply(Disk disk)
        {
            disks.Add(disk);
            ForEachCellNear(map, disk.X, disk.Y, disk.R, (i, j) =>
            {
                if (reach[i, j] > landLimit) return;
                float score = Dist(cx[i, j], cy[i, j], disk.X, disk.Y) / disk.R;
                if (score > 1f) return;
                if (owner[i, j] == null) land++;
                if (score < ownerScore[i, j])
                {
                    owner[i, j] = disk;
                    ownerScore[i, j] = score;
                }
            });
        }

        float Overlap(float x, float y, float r, out int covered)
        {
            int c = 0, already = 0;
            ForEachCellNear(map, x, y, r, (i, j) =>
            {
                if (reach[i, j] > landLimit || Dist(cx[i, j], cy[i, j], x, y) > r) return;
                c++;
                if (owner[i, j] != null) already++;
            });
            covered = c;
            return c == 0 ? 1f : already / (float)c;
        }

        WorldData.Region PickRegion(float x, float y)
        {
            float elevation = WorldData.ElevationAt(MathF.Sqrt(x * x + y * y) / sizeScale);
            List<WorldData.Region> options = WorldData.RegionsAtElevation(elevation);
            if (options.Count == 1) return options[0];
            // The variant (lowlands, lowlands2, lowlands3...) belongs to a big patch of the world, not to each disk, so a
            // biome is one wide region instead of many small ones.
            return options[VariantPatch(seed, x, y, VariantPatchUnits * biomeScale) % options.Count];
        }

        // The beach around the center: a big disk with a ring of smaller ones, all of the required region. It stays inside the
        // elevation band of the beach, so the first lowlands start right after it.
        List<WorldData.Region> beaches = WorldData.LandRegions().Where(r => r.Base == WorldData.RequiredPreTerritory).ToList();
        if (beaches.Count == 0) beaches = WorldData.LandRegions().Take(1).ToList();
        if (!original)
        {
        float beachBand = (WorldData.OriginElevation - beaches.Min(r => r.MinElevation)) * WorldData.DistancePerElevation * sizeScale;
        float core = Math.Clamp(beachBand * 0.95f, WorldData.MinDiskRadius, diskMax);
        Apply(new Disk { X = 0f, Y = 0f, R = core, Region = beaches[rng.Next(beaches.Count)] });
        for (int k = 0; k < 6; k++)
        {
            double angle = (k * 60.0 + rng.NextDouble() * 24.0) * Math.PI / 180.0;
            float ring = core * 0.8f;
            Apply(new Disk
            {
                X = (float)Math.Sin(angle) * ring,
                Y = (float)Math.Cos(angle) * ring,
                R = core * (0.5f + 0.25f * (float)rng.NextDouble()),
                Region = beaches[rng.Next(beaches.Count)]
            });
        }

        int target = (int)(landable * landFraction);
        int attempts = 0;
        while (land < target && attempts < 80000)
        {
            attempts++;
            double angle = rng.NextDouble() * Math.PI * 2.0;
            float distance = landLimit * MathF.Sqrt((float)rng.NextDouble());
            float x = (float)Math.Sin(angle) * distance;
            float y = (float)Math.Cos(angle) * distance;
            float r = diskMin + (diskMax - diskMin) * (float)rng.NextDouble();
            float overlap = Overlap(x, y, r, out int covered);
            if (covered < 3 || overlap > WorldData.MaxOverlap || covered * (1f - overlap) < 3f) continue;
            Apply(new Disk { X = x, Y = y, R = r, Region = PickRegion(x, y) });
        }
        }

        for (int i = 0; i < map.Cells; i++)
        {
            for (int j = 0; j < map.Cells; j++)
            {
                Disk? d = owner[i, j];
                if (d == null) continue;
                map.Kind[i, j] = d.Region.Tile;
                map.RegionAt[i, j] = d.Region;
            }
        }

        int zonesLand = original ? WorldZones.Assign(map, cx, cy, landLimit, seed) : 0;

        // ---- 3: canyon floor between regions of different groups (WorldLayout.MakeWideCanyons)
        int canyonCells = 0;
        for (int i = 0; i < map.Cells; i++)
        {
            for (int j = 0; j < map.Cells; j++)
            {
                WorldData.TileKind kind = map.Kind[i, j];
                if (kind == WorldData.CanyonFloor || kind.CloseCanyon == "_any_") continue;
                foreach ((int di, int dj) in Neighbor4)
                {
                    int ni = i + di, nj = j + dj;
                    if (ni < 0 || nj < 0 || ni >= map.Cells || nj >= map.Cells) continue;
                    WorldData.TileKind other = map.Kind[ni, nj];
                    if (other == WorldData.CanyonFloor || kind.SameCanyonGroup(other) || other.Id >= kind.Id) continue;
                    int spacing = Math.Max(1, kind.CanyonSpacing);
                    for (int l = 0; l < spacing; l++)
                    {
                        for (int m = 0; m < spacing; m++)
                        {
                            int ci = i + l, cj = j + m;
                            if (ci >= map.Cells || cj >= map.Cells) continue;
                            if (map.Kind[ci, cj] != WorldData.CanyonFloor) canyonCells++;
                            map.Kind[ci, cj] = WorldData.CanyonFloor;
                            map.RegionAt[ci, cj] = null;
                        }
                    }
                }
            }
        }

        // The start is always open beach: the canyon ring never takes the cell the players are born on.
        (int sa, int sb) = WorldMap.TileOf(spawnX, spawnY);
        if (map.InGrid(sa, sb) && map.RegionAt[sa + map.Half, sb + map.Half] == null)
        {
            WorldData.Region start = beaches[0];
            map.Kind[sa + map.Half, sb + map.Half] = start.Tile;
            map.RegionAt[sa + map.Half, sb + map.Half] = start;
        }

        // ---- 4: wall strips
        float clearRadius = Math.Max(36f, safeRadius * 0.5f);
        foreach (WorldData.Region region in WorldData.Regions.Where(r => r.Strip != null))
            PlaceStrips(map, region, region.Strip!, rng, spawnX, spawnY, clearRadius);

        // ---- 5: the floor pieces of every cell
        int missing = 0;
        var missingTags = new HashSet<string>();
        for (int i = 0; i < map.Cells; i++)
        {
            for (int j = 0; j < map.Cells; j++)
            {
                if (!map.InWorld[i, j]) continue;
                map.Tiles.Add(MakeTile(map, i - map.Half, j - map.Half, seed, spawnX, spawnY, ref missing, missingTags));
            }
        }
        map.Tiles.Sort((p, q) => p.Distance.CompareTo(q.Distance));

        int landCells = map.LandCells();
        var byRegion = new Dictionary<string, int>();
        for (int i = 0; i < map.Cells; i++)
        {
            for (int j = 0; j < map.Cells; j++)
            {
                WorldData.Region? r = map.RegionAt[i, j];
                if (r == null || !map.InWorld[i, j]) continue;
                byRegion[r.Base] = byRegion.GetValueOrDefault(r.Base) + 1;
            }
        }
        map.Summary = "seed " + seed + (original ? ", original zones" : ", random islands") + ", " + map.Tiles.Count + " tiles, " + landCells + " land cells (" +
            string.Join(", ", byRegion.OrderByDescending(p => p.Value).Select(p => p.Key + " " + p.Value)) + "), " + disks.Count +
            " disks, " + canyonCells + " canyon cells, " + map.Walls.Count + " wall sub-cells, built in " + watch.ElapsedMilliseconds + " ms";
        if (missing > 0)
            log("WORLD", "floor pieces missing for tag(s) " + string.Join(", ", missingTags.Take(8)) + " (" + missing + " slots left empty)");
        return map;
    }

    // ------------------------------------------------------------------ walls

    private static void PlaceStrips(WorldMap map, WorldData.Region region, WorldData.StripRule rule, Random rng,
        float spawnX, float spawnY, float clearRadius)
    {
        int d = rule.Distance;
        int limit = map.Half * Ground.WallsPerCell;
        int steps = limit / d + 1;
        var directions = new (int Di, int Dj)[] { (1, 0), (0, 1), (-1, 0), (0, -1) };
        for (int n = -steps; n <= steps; n++)
        {
            for (int m = -steps; m <= steps; m++)
            {
                int si = n * d + rng.Next(d);
                int sj = m * d + rng.Next(d);
                if (rng.NextDouble() < rule.ChanceOfMissing) continue;
                if (!WallAllowed(map, region, si, sj, spawnX, spawnY, clearRadius)) continue;

                int direction = rng.Next(4);
                int length = rng.Next(rule.MinLength, rule.MaxLength + 1);
                int ci = si, cj = sj;
                for (int k = 0; k < length; k++)
                {
                    if (!WallAllowed(map, region, ci, cj, spawnX, spawnY, clearRadius)) break;
                    map.Walls.Add((ci, cj));
                    if (rng.NextDouble() >= rule.Straightness) direction = (direction + (rng.Next(2) == 0 ? 1 : 3)) % 4;
                    ci += directions[direction].Di;
                    cj += directions[direction].Dj;
                }
            }
        }
    }

    private static bool WallAllowed(WorldMap map, WorldData.Region region, int si, int sj, float spawnX, float spawnY, float clearRadius)
    {
        Ground.FloorPlan.Split(si, out int a, out _);
        Ground.FloorPlan.Split(sj, out int b, out _);
        if (!map.InGrid(a, b) || !map.InWorld[a + map.Half, b + map.Half]) return false;
        if (map.RegionAt[a + map.Half, b + map.Half] != region) return false;
        Ground.ToWorld((si + 0.5f) / Ground.WallsPerCell, (sj + 0.5f) / Ground.WallsPerCell, out float x, out float y);
        return Dist(x, y, spawnX, spawnY) >= clearRadius;
    }

    // ------------------------------------------------------------------ floor pieces (WorldLayout.PlaceGroundAssetsForCell)

    private static WorldMap.Tile MakeTile(WorldMap map, int a, int b, int seed, float spawnX, float spawnY, ref int missing,
        HashSet<string> missingTags)
    {
        WorldData.TileKind kind = map.KindAt(a, b);
        var tile = new WorldMap.Tile { A = a, B = b, Kind = kind };
        WorldMap.CenterOf(a, b, out float x, out float y);
        tile.Distance = Dist(x, y, spawnX, spawnY);

        // Walls of the tile (4 x 4 sub-cells).
        for (int i = 0; i < Ground.WallsPerCell; i++)
        {
            for (int j = 0; j < Ground.WallsPerCell; j++)
            {
                if (!map.Walls.Contains((a * Ground.WallsPerCell + i, b * Ground.WallsPerCell + j))) continue;
                tile.Walls ??= new bool[Ground.WallsPerCell, Ground.WallsPerCell];
                tile.Walls[i, j] = true;
            }
        }
        // A canyon floor has no walls.
        if (kind == WorldData.CanyonFloor) tile.Walls = null;

        var rng = new Random(Mix(seed, a, b));

        var same = new bool[8];
        var fence = new bool[8];
        for (int n = 0; n < 8; n++)
        {
            WorldData.TileKind neighbor = map.KindAt(a + Perimeter[n].Dx, b + Perimeter[n].Dy);
            same[n] = kind.SameContinuity(neighbor);
            fence[n] = ReferenceEquals(kind, neighbor);
        }
        byte[] pieces = FourPieces(same);
        byte[] fencePieces = FourPieces(fence);
        Array.Copy(pieces, tile.Pieces, 4);

        // Slot (j, k) is index 2 * j + k, like groundTileAssets[j, k] of the client.
        for (int j = 0; j < 2; j++)
        {
            for (int k = 0; k < 2; k++)
            {
                int index = 2 * j + k;
                string tag = pieces[index] == WorldData.PieceCenter ? kind.Ground : kind.Cliff;
                List<WorldData.Asset>? list = WorldData.Bottom(tag, pieces[index]);
                if (list == null || list.Count == 0)
                {
                    if (tag != "_none_")
                    {
                        missing++;
                        missingTags.Add(tag + "/" + pieces[index]);
                    }
                    continue;
                }
                WorldData.Asset asset = Pick(list, rng);
                tile.Slots[index] = new WorldMap.Placement { Id = asset.Id, X = j + 0.5f, Y = k + 0.5f };
            }
        }

        if (!string.IsNullOrEmpty(kind.Fence) && kind.Fence != "_none_")
        {
            for (int j = 0; j < 2; j++)
            {
                for (int k = 0; k < 2; k++)
                {
                    byte piece = fencePieces[2 * j + k];
                    if (piece == WorldData.PieceCenter) continue;
                    List<WorldData.Asset>? list = WorldData.Bottom(kind.Fence, piece);
                    if (list == null || list.Count == 0) continue;
                    WorldData.Asset asset = Pick(list, rng);
                    bool flip = asset.CanFlipX && rng.Next(2) == 0;
                    tile.Scatter.Add(new WorldMap.Placement
                    {
                        Id = asset.Id,
                        X = j + 0.5f,
                        Y = k + 0.5f,
                        Rotation = TileRotation(j, k, piece),
                        ScaleX = 1f,
                        ScaleY = flip && piece != WorldData.PieceInside && piece != WorldData.PieceOutside ? -1f : 1f
                    });
                }
            }
        }

        bool allCenter = pieces.All(p => p == WorldData.PieceCenter);
        bool noWalls = tile.Walls == null;
        if (allCenter && noWalls && rng.NextDouble() < kind.ScatterProbability && kind.Scatter != "_none_")
        {
            List<WorldData.Asset>? list = WorldData.Scatters(kind.Scatter);
            if (list != null && list.Count > 0)
            {
                WorldData.Asset asset = Pick(list, rng);
                (float Min, float Max) rotation = asset.Rotation ?? (kind.RotationMin, kind.RotationMax);
                (float Min, float Max) scale = asset.Scale ?? (kind.ScaleMin, kind.ScaleMax);
                float flipX = asset.CanFlipX && rng.Next(2) == 0 ? -1f : 1f;
                float flipY = asset.CanFlipY && rng.Next(2) == 0 ? -1f : 1f;
                float size = scale.Min + (scale.Max - scale.Min) * (float)rng.NextDouble();
                tile.Scatter.Add(new WorldMap.Placement
                {
                    Id = asset.Id,
                    X = 1f + (float)rng.NextDouble() - 0.5f,
                    Y = 1f + (float)rng.NextDouble() - 0.5f,
                    Rotation = rotation.Min + (rotation.Max - rotation.Min) * (float)rng.NextDouble(),
                    ScaleX = flipX * size,
                    ScaleY = flipY * size
                });
            }
        }
        return tile;
    }

    // FourPieceTileUtils.FillTileArray: the four pieces of a tile from its eight neighbors.
    private static byte[] FourPieces(bool[] p)
    {
        // Order of the client's array: [0,0], [0,1], [1,0], [1,1] stored as index 2 * j + k.
        byte c00 = Select(p[0], p[1], p[2]);
        byte c01 = Select(p[2], p[3], p[4]);
        byte c11 = Select(p[4], p[5], p[6]);
        byte c10 = Select(p[6], p[7], p[0]);
        return new[] { c00, c01, c10, c11 };
    }

    private static byte Select(bool ls, bool corner, bool rs)
    {
        if (ls && rs) return corner ? WorldData.PieceCenter : WorldData.PieceInside;
        if (ls) return WorldData.PieceSideA;
        if (rs) return WorldData.PieceSideB;
        return WorldData.PieceOutside;
    }

    // FourPieceTileUtils.GetTileRotation.
    private static float TileRotation(int j, int k, byte piece)
    {
        if (piece == WorldData.PieceCenter) return 0f;
        float fromPosition = j == 0 && k == 1 ? -90f : j == 1 && k == 0 ? 90f : j == 1 && k == 1 ? -180f : 0f;
        return fromPosition + (piece == WorldData.PieceSideB ? 90f : 0f);
    }

    private static WorldData.Asset Pick(List<WorldData.Asset> list, Random rng)
    {
        if (list.Count == 1) return list[0];
        float total = 0f;
        foreach (WorldData.Asset a in list) total += Math.Max(0f, a.Weight);
        if (total <= 0f) return list[rng.Next(list.Count)];
        float roll = (float)rng.NextDouble() * total;
        foreach (WorldData.Asset a in list)
        {
            roll -= Math.Max(0f, a.Weight);
            if (roll <= 0f) return a;
        }
        return list[list.Count - 1];
    }

    // ------------------------------------------------------------------ helpers

    // Size of a variant patch of the world, in units, before the biome scale.
    private const float VariantPatchUnits = 110f;

    // Which variant patch a point is in: the nearest of jittered sites, one per square of "spacing" units. Same seed, same patches.
    private static int VariantPatch(int seed, float x, float y, float spacing)
    {
        int cx = (int)MathF.Floor(x / spacing), cy = (int)MathF.Floor(y / spacing);
        float best = float.MaxValue;
        int bestId = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int id = Mix(seed ^ 0x2f6e, cx + dx, cy + dy) & 0x7fffffff;
                float jx = 0.15f + 0.7f * ((id & 0xffff) / 65535f);
                float jy = 0.15f + 0.7f * (((id >> 8) & 0xffff) / 65535f);
                float px = (cx + dx + jx) * spacing, py = (cy + dy + jy) * spacing;
                float d = (px - x) * (px - x) + (py - y) * (py - y);
                if (d < best)
                {
                    best = d;
                    bestId = id;
                }
            }
        }
        return bestId;
    }

    private static int Mix(int seed, int a, int b)
    {
        unchecked
        {
            int h = seed * 73856093 ^ a * 19349663 ^ b * 83492791;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return h;
        }
    }

    private static float Dist(float x0, float y0, float x1, float y1)
    {
        float dx = x1 - x0, dy = y1 - y0;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // Calls the action for every cell whose center may be within r of the point (a square of cells around it).
    private static void ForEachCellNear(WorldMap map, float x, float y, float r, Action<int, int> action)
    {
        Ground.ToGrid(x, y, out float a, out float b);
        int span = (int)MathF.Ceiling(r / Ground.CellSize * 1.45f) + 2;
        int ca = (int)MathF.Floor(a), cb = (int)MathF.Floor(b);
        for (int i = ca - span; i <= ca + span; i++)
        {
            for (int j = cb - span; j <= cb + span; j++)
            {
                if (!map.InGrid(i, j)) continue;
                action(i + map.Half, j + map.Half);
            }
        }
    }
}
