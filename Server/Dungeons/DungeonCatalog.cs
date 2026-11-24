using System.Globalization;
using System.Net;
using System.Text.Json;
using Lidgren.Network;

// Dungeons the host offers (dungeons.json, in the server folder; read again when it changes):
//   - which map file each dungeon uses (the community can make other versions of a dungeon and the host picks one);
//   - which dungeons are on (and from when: "opensAt"), the health multiplier of each one (difficulty);
//   - where the entrance portals (dungeon carriers) stand in the Nexus.
//
// The entrance is the client's own dungeon portal (Portal prefab 11 with the "dungeon portal" flag and the dungeon type,
// e.g. "DungeonBomb"): the client shows the name and tier from its Texts/dungeons table and the number of players.
// Flying into it sends ServerSendMeThroughPortal to the Nexus, which switches the client to the dungeon server.
// The chat command /dungeon X does the same without the portal.
public static class DungeonCatalog
{
    public const string FileName = "dungeons.json";

    // ================================================================== file

    public sealed class CatalogFile
    {
        public string Note { get; set; } = "";
        public NexusPortals NexusPortals { get; set; } = new NexusPortals();
        public int MaxPlayers { get; set; } = 10;
        public float AutoExitSeconds { get; set; } = 180f;
        // How players get into the dungeons: "both" (the carriers outside the city AND the Mission Transport at the end of
        // the main ramp), "portals" (only the carriers) or "missionTransport" (only the Mission Transport mission list).
        public string EntryMode { get; set; } = "both";
        // true: the doors of a boss room close behind the player until the room is clear (the original game did this;
        // here it trapped players, so it is off by default).
        public bool LockBossRooms { get; set; }
        // Dungeon enemies: multiplier on every fire rate, and the most bullets per second one unit may put out
        // (0 = no limit). The original units used one gun pattern at a time; the server fires all the guns of the template.
        public float FireRateScale { get; set; } = 1f;
        public float MaxBulletsPerSecond { get; set; } = 10f;
        // Map files (maps folder, without .json) of the Nexus (Rebel City) and of the walker tutorial. Empty or missing
        // file = the original map ("nexus" / "tutorial_walker"). A community version is made in the map editor.
        public string NexusMap { get; set; } = DefaultNexusMap;
        public string TutorialMap { get; set; } = DefaultTutorialMap;
        public List<Entry> Dungeons { get; set; } = new List<Entry>();
    }

    public const string DefaultNexusMap = "nexus";
    public const string DefaultTutorialMap = "tutorial_walker";

    public const string EntryBoth = "both";
    public const string EntryPortals = "portals";
    public const string EntryMissionTransport = "missionTransport";

    public sealed class NexusPortals
    {
        public bool Enabled { get; set; } = true;
        public UV Start { get; set; } = new UV { U = -18f, V = -21f };
        public UV Step { get; set; } = new UV { U = 15f, V = 0f };
        public float Rot { get; set; }
    }

    public sealed class UV
    {
        public float U { get; set; }
        public float V { get; set; }
    }

    public sealed class Entry
    {
        // Terrain of the client (territories[].name; also the key of Texts/dungeons): "DungeonBomb", "DungeonSneezy"...
        public string Type { get; set; } = "";
        // Map file in the maps folder, without .json.
        public string Map { get; set; } = "";
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public bool InNexus { get; set; } = true;
        public float HealthMultiplier { get; set; } = 1f;
        // Optional date and time (local time of the host, "yyyy-MM-dd HH:mm", e.g. "2026-11-20 18:00"): before it the
        // dungeon is closed.
        public string OpensAt { get; set; } = "";
        public string Note { get; set; } = "";

        public string Label => string.IsNullOrWhiteSpace(Name) ? Type : Name;

        public bool IsOpen(DateTime now, out string reason)
        {
            reason = "";
            if (!Enabled)
            {
                reason = "turned off in " + FileName;
                return false;
            }
            if (!string.IsNullOrWhiteSpace(OpensAt))
            {
                if (!DateTime.TryParse(OpensAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime opens))
                {
                    reason = "\"opensAt\" invalid (" + OpensAt + ")";
                    return false;
                }
                if (now < opens)
                {
                    reason = "opens at " + opens.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
                    return false;
                }
            }
            return true;
        }
    }

    // ================================================================== state

    private static CatalogFile catalog = Default();
    private static string loadedFrom = "";
    private static DateTime loadedDate = DateTime.MinValue;
    private static readonly object padlock = new object();
    private static Action<string, string> logger = (_, _) => { };

    // Nexus portals already sent: shortId -> dungeon type (the same for every connection that got it).
    private static readonly Dictionary<int, string> portalTypes = new Dictionary<int, string>();

    // Dungeon server address (for the RegisteredHost of the portals).
    private static IPEndPoint dungeonEndpoint = new IPEndPoint(IPAddress.Loopback, 1113);
    private static long dungeonHostId = 3;
    private static string gameVersion = "201219.1413";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static void Start(IPEndPoint endpoint, long hostId, string version, Action<string, string> log)
    {
        lock (padlock)
        {
            dungeonEndpoint = endpoint;
            dungeonHostId = hostId;
            gameVersion = version;
            logger = log;
        }
        Reload();
    }

    // Where the dungeon server is, for the carriers other servers (the open world) put in front of the player.
    public static IPEndPoint Endpoint => dungeonEndpoint;
    public static long HostId => dungeonHostId;
    public static string GameVersion => gameVersion;

    public static string FilePath()
    {
        string[] folders = { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (string folder in folders)
        {
            string path = Path.Combine(folder, FileName);
            if (File.Exists(path)) return path;
        }
        return Path.Combine(folders[0], FileName);
    }

    // Map test mode (MapTest): dungeons.json is ignored and the only dungeon is this entry.
    private static CatalogFile? onlyCatalog;

    public static void UseOnly(Entry entry)
    {
        lock (padlock)
        {
            onlyCatalog = new CatalogFile
            {
                Note = "map test",
                NexusPortals = new NexusPortals { Enabled = false },
                Dungeons = new List<Entry> { entry }
            };
        }
    }

    // Reads dungeons.json again if it changed. Without the file: Iron Lair and Bomb Factory, on.
    public static CatalogFile Reload()
    {
        lock (padlock)
        {
            if (onlyCatalog != null) return onlyCatalog;
            string path = FilePath();
            DateTime date = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (path == loadedFrom && date == loadedDate) return catalog;
            loadedFrom = path;
            loadedDate = date;

            if (!File.Exists(path))
            {
                catalog = Default();
                logger("DUNGEON", path + " does not exist: the default dungeons apply (" + string.Join(", ", catalog.Dungeons.Select(d => d.Type)) + ")");
                return catalog;
            }

            try
            {
                CatalogFile? read = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(path), Options);
                if (read == null) throw new InvalidDataException("empty file");
                read.Dungeons = (read.Dungeons ?? new List<Entry>()).Where(d => d != null && !string.IsNullOrWhiteSpace(d.Type)).ToList();
                foreach (Entry d in read.Dungeons)
                {
                    if (string.IsNullOrWhiteSpace(d.Map)) d.Map = DefaultMapFor(d.Type);
                    if (d.HealthMultiplier <= 0f) d.HealthMultiplier = 1f;
                }
                read.EntryMode = NormalizeEntryMode(read.EntryMode);
                if (read.FireRateScale <= 0f) read.FireRateScale = 1f;
                read.NexusPortals ??= new NexusPortals();
                read.NexusPortals.Start ??= new UV();
                read.NexusPortals.Step ??= new UV();
                catalog = read;
                DateTime now = DateTime.Now;
                logger("DUNGEON", path + ": " + catalog.Dungeons.Count + " dungeon(s); open: " +
                    string.Join(", ", catalog.Dungeons.Where(d => d.IsOpen(now, out _)).Select(d => d.Label + " (" + d.Map + ")")));
            }
            catch (Exception e)
            {
                logger("DUNGEON", "ERROR in " + path + ": " + (e is JsonException j ? "invalid JSON: " + j.Message : e.Message) +
                    " (the previous list stays)");
            }
            return catalog;
        }
    }

    public static IReadOnlyList<Entry> All()
    {
        return Reload().Dungeons;
    }

    public static int MaxPlayers => Math.Max(1, Reload().MaxPlayers);
    public static float AutoExitSeconds => Reload().AutoExitSeconds;
    public static string EntryMode => Reload().EntryMode;
    public static bool LockBossRooms => Reload().LockBossRooms;
    public static float FireRateScale => Reload().FireRateScale;
    public static float MaxBulletsPerSecond => Reload().MaxBulletsPerSecond;

    // Map file of the Nexus / of the tutorial chosen by the host (dungeons.json). A name without a file falls back to
    // the original map (logged once), so a typo never takes the Nexus or the tutorial down.
    public static string NexusMapId => ChosenMap(Reload().NexusMap, DefaultNexusMap, "nexusMap");
    public static string TutorialMapId => ChosenMap(Reload().TutorialMap, DefaultTutorialMap, "tutorialMap");

    private static readonly HashSet<string> missingMapsLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static string ChosenMap(string? chosen, string fallback, string field)
    {
        string id = (chosen ?? "").Trim();
        if (id.Length == 0 || string.Equals(id, fallback, StringComparison.OrdinalIgnoreCase)) return fallback;
        if (File.Exists(Maps.FilePath(id))) return id;
        lock (padlock)
        {
            if (missingMapsLogged.Add(field + ":" + id))
                logger("DUNGEON", FileName + ": \"" + field + "\" = \"" + id + "\" has no file in the maps folder (" +
                    Maps.FilePath(id) + "): the original map '" + fallback + "' is used");
        }
        return fallback;
    }

    // The carriers outside the city are offered.
    public static bool PortalsEnabled => Reload().EntryMode != EntryMissionTransport;
    // The Mission Transport (the mega carrier at the end of the main ramp) lists the dungeons.
    public static bool MissionTransportEnabled => Reload().EntryMode != EntryPortals;

    public static string NormalizeEntryMode(string? text)
    {
        string t = (text ?? "").Trim().Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        if (t == "portals" || t == "portal" || t == "carriers") return EntryPortals;
        if (t == "missiontransport" || t == "mission" || t == "missions" || t == "transport") return EntryMissionTransport;
        return EntryBoth;
    }

    // Entry by terrain ("DungeonBomb"), map id ("dungeon_bomb"), name or a piece of them ("bomb").
    public static Entry? Find(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string t = text.Trim();
        List<Entry> list = Reload().Dungeons;
        Entry? exact = list.FirstOrDefault(d =>
            string.Equals(d.Type, t, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.Map, t, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.Name, t, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;
        return list.FirstOrDefault(d =>
            d.Type.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            d.Map.Contains(t, StringComparison.OrdinalIgnoreCase) ||
            d.Name.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    public static Entry? FirstOpen()
    {
        DateTime now = DateTime.Now;
        return Reload().Dungeons.FirstOrDefault(d => d.IsOpen(now, out _));
    }

    // ================================================================== Nexus portals

    // One dungeon carrier per open dungeon with "inNexus", in a row. Called once per Nexus connection.
    public static int SendNexusPortals(NetServer server, NetConnection connection, double now, Action<string, string> log)
    {
        CatalogFile cat = Reload();
        if (!cat.NexusPortals.Enabled) return 0;
        if (cat.EntryMode == EntryMissionTransport)
        {
            log("DUNGEON", "[nexus] entryMode is 'missionTransport': no dungeon carriers outside the city (the mega carrier lists them)");
            return 0;
        }
        DateTime clock = DateTime.Now;
        int sent = 0;
        int index = 0;
        var names = new List<string>();
        foreach (Entry d in cat.Dungeons)
        {
            if (!d.InNexus || !d.IsOpen(clock, out _)) continue;
            float u = cat.NexusPortals.Start.U + cat.NexusPortals.Step.U * index;
            float v = cat.NexusPortals.Start.V + cat.NexusPortals.Step.V * index;
            index++;
            Ground.ToWorld(u / Ground.CellSize, v / Ground.CellSize, out float x, out float y);
            int id = WorldObjects.NewShortId();
            lock (padlock) portalTypes[id] = d.Type;
            WorldObjects.SendDungeonPortal(server, connection, id, x, y, cat.NexusPortals.Rot, dungeonEndpoint, dungeonHostId,
                d.Label, gameVersion, d.Type, MaxPlayers, now);
            names.Add(d.Label + " " + id);
            sent++;
        }
        if (sent > 0) log("DUNGEON", "[nexus] " + sent + " dungeon carrier(s): " + string.Join(", ", names));
        return sent;
    }

    // ServerSendMeThroughPortal in the Nexus: the dungeon of the portal, if it is one of these and still open.
    public static Entry? NexusPortal(int shortId, out string reason)
    {
        reason = "";
        if (!PortalsEnabled)
        {
            reason = "entryMode is missionTransport";
            return null;
        }
        string? type;
        lock (padlock)
        {
            if (!portalTypes.TryGetValue(shortId, out type)) return null;
        }
        Entry? entry = Find(type);
        if (entry == null)
        {
            reason = type + " left the " + FileName;
            return null;
        }
        if (!entry.IsOpen(DateTime.Now, out reason)) return null;
        return entry;
    }

    public static string Describe(Entry d)
    {
        bool open = d.IsOpen(DateTime.Now, out string reason);
        return d.Label + " [" + d.Type + ", map " + d.Map + (Math.Abs(d.HealthMultiplier - 1f) > 0.001f ? ", health x" + Maps.Num(d.HealthMultiplier) : "") +
            "]" + (open ? "" : " (closed: " + reason + ")");
    }

    // ================================================================== defaults

    public static string DefaultMapFor(string type)
    {
        switch (type.ToLowerInvariant())
        {
            case "dungeonbomb": return "dungeon_bomb";
            case "dungeonsneezy": return "dungeon_sneezy";
            case "dungeonobstacle": return "dungeon_obstacle";
            case "dungeonarena1": return "dungeon_arena";
            case "dungeonarena2": return "dungeon_arena2";
            case "dungeongrind": return "dungeon_grind";
            case "dungeoneye": return "dungeon_eye";
            case "dungeonfire": return "dungeon_fire";
            case "dungeonice": return "dungeon_ice";
            case "dungeonsnake": return "dungeon_snake";
            case "dungeonthief": return "dungeon_thief";
            case "dungeonspace": return "dungeon_space";
            case "dungeontemple": return "dungeon_temple";
            case "dungeonrescue": return "dungeon_rescue";
            case "dungeonfinalboss": return "dungeon_finalboss";
            case "dungeonfinalboss2": return "dungeon_finalboss2";
            case "dungeon3brothers": return "dungeon_3brothers";
            default: return type.ToLowerInvariant();
        }
    }

    private static CatalogFile Default()
    {
        return new CatalogFile
        {
            Dungeons = new List<Entry>
            {
                new Entry { Type = "DungeonSneezy", Map = "dungeon_sneezy", Name = "Iron Lair" },
                new Entry { Type = "DungeonBomb", Map = "dungeon_bomb", Name = "Bomb Factory" }
            }
        };
    }
}
