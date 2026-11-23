using System.Globalization;
using System.Text.Json;
using Lidgren.Network;

// Maps of the server as files (folder "maps", one .json per map, format "steambirds-map" version 1).
// The format is described in docs\map-format.md. Summary:
//   - u, v coordinates in world units along the axes of the client's grid (u = 12 * a, v = 12 * b,
//     a and b in tiles as in Ground). In the world: x = (u - v) / sqrt(2), y = (u + v) / sqrt(2).
//     1 tile = 12 u, 1 floor piece slot = 6 u (p, q), 1 wall sub-cell = 3 u (si, sj).
//   - floor: "drawing" (sub-cells: . free floor, # wall, - no floor) with "drawingPieces" picked per slot,
//     and/or "pieces" (one piece per slot, with id and rotation). "voidMargin": tiles without floor around (abyss).
//   - decor (scatter with exact position), buildings (entities with art and dock), spawn.
//   - for scripts (like the tutorial's): points, groups of points, areas, lines (v), values, checkpoints,
//     gates (rows of crates) and floor signs (texts that the client creates by itself).
//
// The server reloads the file when it changes (the next time the map is requested: a player joining).
// If the new file has an error, the previous version stays and the error goes to the log.
public static class Maps
{
    public const string Format = "steambirds-map";
    public const int CurrentVersion = 1;
    public const string FolderName = "maps";

    private const byte PieceCenter = 4;          // FourPieceTile.Center
    private const byte FlagTerritory = 2;  // GroundAssetPlacement: territoryIDOverride
    private const byte FlagPrecision = 4;    // position x, y, z in the world (float)
    private const byte FlagRotation = 8;     // rotation (byte = degrees / 2)

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ================================================================== file (JSON)

    public sealed class MapFile
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Terrain { get; set; } = "";
        public int Territory { get; set; }
        public FloorFile? Floor { get; set; }
        public List<DecorFile>? Decor { get; set; }
        public List<BuildingFile>? Buildings { get; set; }
        public PointFile? Spawn { get; set; }
        public Dictionary<string, PointFile>? Points { get; set; }
        public Dictionary<string, List<PointFile>>? Groups { get; set; }
        public Dictionary<string, AreaFile>? Areas { get; set; }
        public Dictionary<string, float>? Lines { get; set; }
        public Dictionary<string, float>? Values { get; set; }
        public List<CheckpointFile>? Checkpoints { get; set; }
        public List<GateFile>? Gates { get; set; }
        public List<SignFile>? Signs { get; set; }
        // What happens in the map (rules, spawns, jumps): Scripting\MapScript.cs, docs/map-script.md.
        public MapScript.ScriptDef? Script { get; set; }
        // Solid places that were only scenery (the Rebel City props): each rectangle closes the wall sub-cells (3 u) whose
        // center is inside it, so planes cannot fly through. Written as walls on the tiles of the floor message.
        public List<CollisionFile>? Collisions { get; set; }
    }

    public sealed class CollisionFile
    {
        public string Name { get; set; } = "";
        public float MinU { get; set; }
        public float MaxU { get; set; }
        public float MinV { get; set; }
        public float MaxV { get; set; }
        public string Note { get; set; } = "";
    }

    public sealed class FloorFile
    {
        public bool WallsOnEdges { get; set; } = true;
        public int VoidMargin { get; set; }
        public int VoidTerritory { get; set; } = 74;
        public List<int>? DrawingPieces { get; set; }
        public DrawingFile? Drawing { get; set; }
        public List<PieceFile>? Pieces { get; set; }
    }

    public sealed class DrawingFile
    {
        public int SiStart { get; set; }
        public int SjStart { get; set; }
        public List<string>? Lines { get; set; }
    }

    public sealed class PieceFile
    {
        public int P { get; set; }
        public int Q { get; set; }
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public float? Rot { get; set; }
    }

    public sealed class DecorFile
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public float U { get; set; }
        public float V { get; set; }
        public float Z { get; set; } = 0.5f;
        public float Rot { get; set; }
    }

    public sealed class BuildingFile
    {
        public int Prefab { get; set; }
        public string Name { get; set; } = "";
        public float U { get; set; }
        public float V { get; set; }
        public float Rot { get; set; }
        public string? Store { get; set; }
    }

    public sealed class PointFile
    {
        public float U { get; set; }
        public float V { get; set; }
        public float Rot { get; set; }
    }

    public sealed class AreaFile
    {
        public float MinU { get; set; }
        public float MaxU { get; set; }
        public float MinV { get; set; }
        public float MaxV { get; set; }
    }

    public sealed class CheckpointFile
    {
        public string Name { get; set; } = "";
        public float ActiveAtV { get; set; }
        public float U { get; set; }
        public float V { get; set; }
        public float Rot { get; set; }
    }

    public sealed class GateFile
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public int Sj { get; set; }
        public int SiFrom { get; set; }
        public int SiTo { get; set; }
        public bool Damageable { get; set; }
    }

    public sealed class SignFile
    {
        public string Key { get; set; } = "";
        public float U { get; set; }
        public float V { get; set; }
        public float Radius { get; set; } = 14f;
        public float Rot { get; set; }
    }

    // ================================================================== loaded map

    // Point of the map: u, v in units; A and B in tiles (what Ground and the scripts use).
    public readonly record struct Point(float U, float V, float Rotation)
    {
        public float A => U / Ground.CellSize;
        public float B => V / Ground.CellSize;

        public void World(out float x, out float y) => Ground.ToWorld(A, B, out x, out y);
    }

    public readonly record struct Piece(ushort Id, string Name, float? Rotation);
    public sealed record Decor(ushort Id, string Name, Point Point, float Z);
    public sealed record Building(int Prefab, string Name, Point Point, string? Store);
    public sealed record Checkpoint(string Name, float ActiveAtB, Point Point);
    public sealed record Gate(string Name, string Type, int Sj, int SiFrom, int SiTo, bool Damageable);
    public sealed record Sign(string Key, Point Point, float Radius, float Rotation);

    public sealed class Map
    {
        public string Id = "";
        public string Name = "";
        public string Terrain = "";
        public string FilePath = "";
        public byte Territory;

        // Floor
        public bool WallsOnEdges = true;
        public int VoidMargin;
        public byte VoidTerritory = 74;
        public ushort[] DrawingPieces = Array.Empty<ushort>();
        public readonly List<(int A, int B)> DrawingTiles = new List<(int A, int B)>();
        public readonly List<(int Si, int Sj)> DrawingWalls = new List<(int Si, int Sj)>();
        public readonly Dictionary<(int P, int Q), Piece> Pieces = new Dictionary<(int P, int Q), Piece>();

        public readonly List<Decor> Decor = new List<Decor>();
        public readonly List<Building> Buildings = new List<Building>();
        public Point Spawn;
        public bool HasSpawn;

        public readonly Dictionary<string, Point> Points = new Dictionary<string, Point>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, List<Point>> Groups = new Dictionary<string, List<Point>>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, Ground.Rect> Areas =
            new Dictionary<string, Ground.Rect>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, float> Lines = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, float> Values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        public readonly List<Checkpoint> Checkpoints = new List<Checkpoint>();
        public readonly List<Gate> Gates = new List<Gate>();
        public readonly List<Sign> Signs = new List<Sign>();
        public MapScript.ScriptDef? Script;

        // Wall sub-cells closed by the "collisions" of the file, by tile (walls[i, j] as in Ground.FloorPlan.TileWalls).
        public readonly Dictionary<(int A, int B), bool[,]> CollisionWalls = new Dictionary<(int A, int B), bool[,]>();

        public void AddCollision(float minU, float maxU, float minV, float maxV)
        {
            const float Sub = Ground.CellSize / Ground.WallsPerCell;   // 3 u
            int si0 = (int)MathF.Ceiling(minU / Sub - 0.5f), si1 = (int)MathF.Floor(maxU / Sub - 0.5f);
            int sj0 = (int)MathF.Ceiling(minV / Sub - 0.5f), sj1 = (int)MathF.Floor(maxV / Sub - 0.5f);
            for (int si = si0; si <= si1; si++)
            {
                for (int sj = sj0; sj <= sj1; sj++)
                {
                    Ground.FloorPlan.Split(si, out int a, out int i);
                    Ground.FloorPlan.Split(sj, out int b, out int j);
                    if (!CollisionWalls.TryGetValue((a, b), out bool[,]? walls))
                    {
                        walls = new bool[Ground.WallsPerCell, Ground.WallsPerCell];
                        CollisionWalls[(a, b)] = walls;
                    }
                    walls[i, j] = true;
                }
            }
        }

        // ---------------------------------------------------------- lookups by name (scripts)

        public Point GetPoint(string name)
        {
            if (Points.TryGetValue(name, out Point p)) return p;
            throw Missing("point", name, "points");
        }

        public List<Point> GetGroup(string name)
        {
            if (Groups.TryGetValue(name, out List<Point>? g)) return g;
            throw Missing("group of points", name, "groups");
        }

        public Ground.Rect GetArea(string name)
        {
            if (Areas.TryGetValue(name, out Ground.Rect r)) return r;
            throw Missing("area", name, "areas");
        }

        // Line (v in units) returned in tiles (b), the way scripts compare the player's position.
        public float GetLineB(string name)
        {
            if (Lines.TryGetValue(name, out float v)) return v / Ground.CellSize;
            throw Missing("line", name, "lines");
        }

        public float GetValue(string name)
        {
            if (Values.TryGetValue(name, out float v)) return v;
            throw Missing("value", name, "values");
        }

        public Checkpoint GetCheckpoint(string name)
        {
            foreach (Checkpoint p in Checkpoints)
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            }
            throw Missing("checkpoint", name, "checkpoints");
        }

        private InvalidOperationException Missing(string what, string name, string section)
        {
            return new InvalidOperationException("the map '" + Id + "' (" + FilePath + ") does not have the " + what + " '" + name +
                "' (section \"" + section + "\")");
        }

        public string Summary()
        {
            int tiles = DrawingTiles.Count;
            return Pieces.Count + " floor pieces, " + (tiles > 0 ? tiles + " drawn tiles, " : "") +
                Decor.Count + " decor, " + Buildings.Count + " buildings" +
                (Signs.Count > 0 ? ", " + Signs.Count + " signs" : "") +
                (Points.Count + Groups.Count + Areas.Count > 0 ? ", " + Points.Count + " points, " + Areas.Count + " areas" : "");
        }

        // ---------------------------------------------------------- floor

        // Plan with the floor and the walls of the drawing (tiles with floor, edge walls and closed sub-cells).
        // Each tutorial player gets their own (the gate crates look for free sub-cells in it).
        public Ground.FloorPlan CreatePlan()
        {
            var plan = new Ground.FloorPlan(Territory, DrawingPieces, WallsOnEdges);
            foreach ((int A, int B) t in DrawingTiles) plan.AddTile(t.A, t.B);
            foreach ((int Si, int Sj) m in DrawingWalls)
            {
                if (!plan.IsEdgeWallAtSub(m.Si, m.Sj)) plan.CloseSub(m.Si, m.Sj);
            }
            return plan;
        }

        // The whole ClientReceiveTerrainTiles message (with the ID in front): drawing tiles (picked pieces and
        // plan walls), tiles with pieces, tiles with only decor and, at the end, the abyss around.
        public void WriteFloor(NetBuffer m, Ground.FloorPlan? plan, out string summary)
        {
            var decorByTile = new Dictionary<(int A, int B), List<Decor>>();
            foreach (Decor e in Decor)
            {
                var t = ((int)MathF.Floor(e.Point.U / Ground.CellSize), (int)MathF.Floor(e.Point.V / Ground.CellSize));
                if (!decorByTile.TryGetValue(t, out List<Decor>? list))
                {
                    list = new List<Decor>();
                    decorByTile[t] = list;
                }
                list.Add(e);
            }

            var piecesByTile = new Dictionary<(int A, int B), Piece?[,]>();
            foreach (KeyValuePair<(int P, int Q), Piece> pair in Pieces)
            {
                int a = FloorDiv(pair.Key.P, 2), b = FloorDiv(pair.Key.Q, 2);
                if (!piecesByTile.TryGetValue((a, b), out Piece?[,]? slots))
                {
                    slots = new Piece?[2, 2];
                    piecesByTile[(a, b)] = slots;
                }
                slots[pair.Key.P - 2 * a, pair.Key.Q - 2 * b] = pair.Value;
            }

            // Normal tiles: the ones of the drawing in the drawing's order, then the others (pieces and decor) by b and a.
            var tiles = new List<(int A, int B)>();
            var already = new HashSet<(int A, int B)>();
            if (plan != null)
            {
                foreach ((int A, int B) t in plan.Tiles)
                {
                    if (already.Add(t)) tiles.Add(t);
                }
            }
            var others = new HashSet<(int A, int B)>(piecesByTile.Keys);
            others.UnionWith(decorByTile.Keys);
            others.UnionWith(CollisionWalls.Keys);
            foreach ((int A, int B) t in others.OrderBy(x => x.B).ThenBy(x => x.A))
            {
                if (already.Add(t)) tiles.Add(t);
            }

            // Abyss: tiles without floor around all the floor (drawing and pieces), except those that already go as normal.
            var voidTiles = new List<(int A, int B)>();
            if (VoidMargin > 0)
            {
                var withFloor = new List<(int A, int B)>();
                if (plan != null) withFloor.AddRange(plan.Tiles);
                withFloor.AddRange(piecesByTile.Keys.OrderBy(x => x.B).ThenBy(x => x.A));
                var seen = new HashSet<(int A, int B)>(already);
                foreach ((int A, int B) t in withFloor)
                {
                    for (int dA = -VoidMargin; dA <= VoidMargin; dA++)
                    {
                        for (int db = -VoidMargin; db <= VoidMargin; db++)
                        {
                            var v = (t.A + dA, t.B + db);
                            if (Ground.GridCenter + v.Item1 < 0 || Ground.GridCenter + v.Item2 < 0) continue;
                            if (seen.Add(v)) voidTiles.Add(v);
                        }
                    }
                }
            }

            m.Write(Ground.MsgClientReceiveTerrainTiles);
            m.Write(Ground.CellSize);
            m.Write(Ground.WorldRadius);
            m.Write(Ground.WallsPerCell);
            m.WriteVariableUInt32((uint)(tiles.Count + voidTiles.Count + 1));   // TerritoryTile list: count + 1

            int totalDecor = 0;
            int tilesWithWalls = 0;
            foreach ((int a, int b) in tiles)
            {
                m.Write(Ground.GridCenter + a);
                m.Write(Ground.GridCenter + b);
                m.Write(Territory);
                for (int i = 0; i < 4; i++) m.Write(PieceCenter);

                bool drawnFloor = plan != null && plan.HasFloor(a, b);
                piecesByTile.TryGetValue((a, b), out Piece?[,]? slots);
                for (int k = 0; k < 2; k++)
                {
                    for (int l = 0; l < 2; l++)
                    {
                        Piece? piece = slots?[k, l];
                        if (piece.HasValue && piece.Value.Rotation.HasValue)
                        {
                            // With a rotation the client does not pick a spin or a mirror (negative scale) for the piece.
                            m.Write(FlagRotation);
                            m.Write(piece.Value.Id);
                            m.Write(DegreesToByte(piece.Value.Rotation.Value));
                        }
                        else if (piece.HasValue)
                        {
                            m.Write((byte)0);
                            m.Write(piece.Value.Id);
                        }
                        else if (drawnFloor)
                        {
                            m.Write((byte)0);
                            m.Write(plan!.DefaultPiece(a, b, k, l));
                        }
                        else
                        {
                            // Empty slot: asset 0 draws nothing.
                            m.Write((byte)0);
                            m.Write((ushort)0);
                        }
                        m.Write((sbyte)((k + 0.5f) * 2f));
                        m.Write((sbyte)((l + 0.5f) * 2f));
                    }
                }

                decorByTile.TryGetValue((a, b), out List<Decor>? list);
                int howMany = Math.Min(list?.Count ?? 0, 255);
                m.Write((byte)howMany);
                for (int i = 0; i < howMany; i++)
                {
                    // Scatter with full precision: position and rotation in the world (without the tile's 45 degrees).
                    Decor e = list![i];
                    e.Point.World(out float x, out float y);
                    m.Write((byte)(FlagRotation | FlagPrecision));
                    m.Write(e.Id);
                    m.Write(DegreesToByte(e.Point.Rotation));
                    m.Write(x);
                    m.Write(y);
                    m.Write(e.Z);
                }
                totalDecor += howMany;

                bool[,]? walls = drawnFloor ? plan!.TileWalls(a, b) : null;
                if (CollisionWalls.TryGetValue((a, b), out bool[,]? solid))
                {
                    // Solid props: their cells are closed on top of the drawing's walls (if any).
                    if (walls == null) walls = (bool[,])solid.Clone();
                    else
                    {
                        for (int i = 0; i < Ground.WallsPerCell; i++)
                        {
                            for (int j = 0; j < Ground.WallsPerCell; j++) walls[i, j] |= solid[i, j];
                        }
                    }
                }
                bool hasWalls = walls != null && HasAny(walls);
                m.Write(hasWalls);
                if (hasWalls)
                {
                    tilesWithWalls++;
                    m.Write((byte)Ground.WallsPerCell);
                    for (int i = 0; i < Ground.WallsPerCell; i++)
                    {
                        for (int j = 0; j < Ground.WallsPerCell; j++) m.Write(walls![i, j]);
                    }
                }
            }

            foreach ((int a, int b) in voidTiles)
            {
                // Empty tile: the map's territory, the 4 pieces with the territory without floor, no decor and no walls.
                m.Write(Ground.GridCenter + a);
                m.Write(Ground.GridCenter + b);
                m.Write(Territory);
                for (int i = 0; i < 4; i++) m.Write(PieceCenter);
                for (int k = 0; k < 2; k++)
                {
                    for (int l = 0; l < 2; l++)
                    {
                        m.Write(FlagTerritory);
                        m.Write((ushort)0);
                        m.Write(VoidTerritory);
                        m.Write((sbyte)((k + 0.5f) * 2f));
                        m.Write((sbyte)((l + 0.5f) * 2f));
                    }
                }
                m.Write((byte)0);
                m.Write(false);
            }

            summary = tiles.Count + " tiles (" + Pieces.Count + " pieces, " + totalDecor + " decor, " + tilesWithWalls +
                " with walls" + (plan != null ? ", " + plan.ClosedCells() + " closed sub-cells" : "") + ")" +
                (voidTiles.Count > 0 ? " e " + voidTiles.Count + " of abyss around" : "");
        }
    }

    // ================================================================== loading

    private sealed class Entry
    {
        public Map? Map;
        public string FilePath = "";
        public DateTime Data;
    }

    private static readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private static readonly object gate = new object();

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // The server's "maps" folder (where the server runs) or next to the executable.
    public static string FilePath(string id)
    {
        string fileName = id + ".json";
        string[] folders =
        {
            Path.Combine(Directory.GetCurrentDirectory(), FolderName),
            Path.Combine(AppContext.BaseDirectory, FolderName)
        };
        foreach (string folder in folders)
        {
            string path = Path.Combine(folder, fileName);
            if (File.Exists(path)) return path;
        }
        return Path.Combine(folders[0], fileName);
    }

    // The map by id (file name without .json). Reloads the file if it changed since the last read.
    // Without the file (or with an error and no previous version): InvalidOperationException with the reason.
    public static Map Get(string id, Action<string, string> log)
    {
        lock (gate)
        {
            string path = FilePath(id);
            DateTime data = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            cache.TryGetValue(id, out Entry? current);
            if (current != null && current.Map != null && current.FilePath == path && current.Data == data) return current.Map;

            try
            {
                if (!File.Exists(path)) throw new FileNotFoundException("file not found", path);
                Map fresh = Read(path);
                if (!string.Equals(fresh.Id, id, StringComparison.OrdinalIgnoreCase))
                    log("MAP", "warning: " + path + " has \"id\": \"" + fresh.Id + "\" (the file is called " + id + ".json)");
                cache[id] = new Entry { Map = fresh, FilePath = path, Data = data };
                log("MAP", (current?.Map == null ? "loaded" : "reloaded (the file changed)") + ": " + fresh.Name + " (" +
                    path + "): " + fresh.Summary());
                return fresh;
            }
            catch (Exception e)
            {
                string reason = e is JsonException j ? "invalid JSON: " + j.Message : e.Message;
                if (current?.Map != null)
                {
                    // The version that already worked stays; the error only shows again if the file changes once more.
                    current.Data = data;
                    current.FilePath = path;
                    log("MAP", "ERROR reloading " + path + ": " + reason + " (the previous version stays)");
                    return current.Map;
                }
                throw new InvalidOperationException("map '" + id + "' did not load (" + path + "): " + reason, e);
            }
        }
    }

    private static Map Read(string path)
    {
        string text = File.ReadAllText(path);
        MapFile? file = JsonSerializer.Deserialize<MapFile>(text, JsonOptions);
        if (file == null) throw new InvalidDataException("empty file");
        if (!string.Equals(file.Format, Format, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("\"format\" should be \"" + Format + "\" (got \"" + file.Format + "\")");
        if (file.Version < 1 || file.Version > CurrentVersion)
            throw new InvalidDataException("\"version\" " + file.Version + " not supported (this server reads up to " + CurrentVersion + ")");
        if (file.Territory < 0 || file.Territory > 255)
            throw new InvalidDataException("\"territory\" outside 0..255: " + file.Territory);

        var map = new Map
        {
            Id = file.Id,
            Name = string.IsNullOrWhiteSpace(file.Name) ? file.Id : file.Name,
            Terrain = file.Terrain,
            FilePath = path,
            Territory = (byte)file.Territory
        };

        FloorFile floor = file.Floor ?? new FloorFile();
        map.WallsOnEdges = floor.WallsOnEdges;
        map.VoidMargin = Math.Max(0, floor.VoidMargin);
        map.VoidTerritory = ToByte(floor.VoidTerritory, "floor.voidTerritory");
        map.DrawingPieces = (floor.DrawingPieces ?? new List<int>()).Select(i => Id16(i, "floor.drawingPieces")).ToArray();
        if (floor.Drawing != null) ReadDrawing(map, floor.Drawing);
        if (map.DrawingTiles.Count > 0 && map.DrawingPieces.Length == 0)
            throw new InvalidDataException("the drawing has floor, but \"floor.drawingPieces\" is empty");

        foreach (PieceFile p in floor.Pieces ?? new List<PieceFile>())
            map.Pieces[(p.P, p.Q)] = new Piece(Id16(p.Id, "floor.pieces"), p.Name, p.Rot);

        foreach (DecorFile e in file.Decor ?? new List<DecorFile>())
            map.Decor.Add(new Decor(Id16(e.Id, "decor"), e.Name, new Point(e.U, e.V, e.Rot), e.Z));

        foreach (BuildingFile p in file.Buildings ?? new List<BuildingFile>())
            map.Buildings.Add(new Building(p.Prefab, p.Name, new Point(p.U, p.V, p.Rot), string.IsNullOrEmpty(p.Store) ? null : p.Store));

        if (file.Spawn != null)
        {
            map.Spawn = Pt(file.Spawn);
            map.HasSpawn = true;
        }

        foreach (KeyValuePair<string, PointFile> pair in file.Points ?? new Dictionary<string, PointFile>())
            map.Points[pair.Key] = Pt(pair.Value);
        foreach (KeyValuePair<string, List<PointFile>> pair in file.Groups ?? new Dictionary<string, List<PointFile>>())
            map.Groups[pair.Key] = (pair.Value ?? new List<PointFile>()).Select(Pt).ToList();
        foreach (KeyValuePair<string, AreaFile> pair in file.Areas ?? new Dictionary<string, AreaFile>())
        {
            AreaFile r = pair.Value;
            float t = Ground.CellSize;
            map.Areas[pair.Key] = new Ground.Rect(r.MinU / t, r.MaxU / t, r.MinV / t, r.MaxV / t);
        }
        foreach (KeyValuePair<string, float> pair in file.Lines ?? new Dictionary<string, float>()) map.Lines[pair.Key] = pair.Value;
        foreach (KeyValuePair<string, float> pair in file.Values ?? new Dictionary<string, float>()) map.Values[pair.Key] = pair.Value;

        foreach (CheckpointFile p in file.Checkpoints ?? new List<CheckpointFile>())
            map.Checkpoints.Add(new Checkpoint(p.Name, p.ActiveAtV / Ground.CellSize, new Point(p.U, p.V, p.Rot)));

        foreach (GateFile p in file.Gates ?? new List<GateFile>())
        {
            if (p.SiTo < p.SiFrom) throw new InvalidDataException("gate '" + p.Name + "': siTo smaller than siFrom");
            map.Gates.Add(new Gate(p.Name, p.Type, p.Sj, p.SiFrom, p.SiTo, p.Damageable));
        }

        map.Script = file.Script;

        foreach (SignFile p in file.Signs ?? new List<SignFile>())
            map.Signs.Add(new Sign(p.Key, new Point(p.U, p.V, p.Rot), p.Radius, p.Rot));

        foreach (CollisionFile p in file.Collisions ?? new List<CollisionFile>())
        {
            if (p.MaxU < p.MinU || p.MaxV < p.MinV) throw new InvalidDataException("collision '" + p.Name + "': max smaller than min");
            map.AddCollision(p.MinU, p.MaxU, p.MinV, p.MaxV);
        }

        string? scriptError = MapScript.Validate(map);
        if (scriptError != null) throw new InvalidDataException("script: " + scriptError);

        return map;
    }

    // Drawing: each line is an sj row (the first line is the last row), each character an si sub-cell.
    // '.' free floor, '#' wall, '-' (or space) no floor. A tile has floor if any sub-cell of it is not '-'.
    private static void ReadDrawing(Map map, DrawingFile drawing)
    {
        List<string> lines = drawing.Lines ?? new List<string>();
        var cells = new Dictionary<(int Si, int Sj), char>();
        for (int n = 0; n < lines.Count; n++)
        {
            int sj = drawing.SjStart + (lines.Count - 1 - n);
            string line = lines[n] ?? "";
            for (int c = 0; c < line.Length; c++)
            {
                char ch = line[c];
                if (ch == '-' || ch == ' ') continue;
                if (ch != '.' && ch != '#')
                    throw new InvalidDataException("drawing: character '" + ch + "' on line " + (n + 1) + " (use . # or -)");
                cells[(drawing.SiStart + c, sj)] = ch;
            }
        }

        var tiles = new HashSet<(int A, int B)>();
        foreach (KeyValuePair<(int Si, int Sj), char> pair in cells.OrderBy(x => x.Key.Sj).ThenBy(x => x.Key.Si))
        {
            Ground.FloorPlan.Split(pair.Key.Si, out int a, out _);
            Ground.FloorPlan.Split(pair.Key.Sj, out int b, out _);
            if (tiles.Add((a, b))) map.DrawingTiles.Add((a, b));
            if (pair.Value == '#') map.DrawingWalls.Add(pair.Key);
        }
    }

    // ================================================================== floor signs (ClientMapInfo)

    // One event per sign: every second, with the plane within "radius" of the point, the client creates the text there
    // (lore_text_relative_pos with an absolute position). The client itself does not repeat a text that already exists.
    public static void AddSigns(MapInfo.MapInfoData info, Map map)
    {
        foreach (Sign sign in map.Signs)
        {
            sign.Point.World(out float x, out float y);
            info.Events.Add(MapInfo.Ev("repeatEvery_1", "",
                MapInfo.Cond(MapInfo.ConditionPlayerNear, "", x, y, sign.Radius),
                MapInfo.Reac("lore_text_relative_pos", sign.Key, "", x, y, 0f, sign.Rotation)));
        }
    }

    // ================================================================== helpers

    private static Point Pt(PointFile p) => new Point(p.U, p.V, p.Rot);

    private static ushort Id16(int val, string where)
    {
        if (val < 0 || val > ushort.MaxValue) throw new InvalidDataException(where + ": id " + val + " outside 0..65535");
        return (ushort)val;
    }

    private static byte ToByte(int val, string where)
    {
        if (val < 0 || val > 255) throw new InvalidDataException(where + ": " + val + " outside 0..255");
        return (byte)val;
    }

    private static bool HasAny(bool[,] walls)
    {
        foreach (bool m in walls)
        {
            if (m) return true;
        }
        return false;
    }

    public static byte DegreesToByte(float degrees)
    {
        float g = degrees % 360f;
        if (g < 0f) g += 360f;
        return (byte)(g / 2f);
    }

    private static int FloorDiv(int val, int divisor) => (int)MathF.Floor(val / (float)divisor);

    public static string Num(float val) => val.ToString("0.##", Inv);
}
