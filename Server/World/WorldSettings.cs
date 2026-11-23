using System.Text.Json;
using System.Text.Json.Serialization;

// Host settings of the open world (world.json, next to dungeons.json and enemies.json).
// The file is created with the defaults the first time the server starts and read again when it changes:
// most values apply at once, the map ones (seed, radius, land fraction) apply the next time the world is built
// (the world is built when the first player arrives and discarded when the last one leaves).
public sealed class WorldSettings
{
    public string Note { get; set; } = "";

    // false: no portal to the open world in the Nexus and /world refuses.
    public bool Enabled { get; set; } = true;

    // Name of the world on the portal of the Nexus.
    public string Name { get; set; } = "Open World";

    // Players in the world at the same time (shown as (n/max) on the Nexus portal).
    public int MaxPlayers { get; set; } = 16;

    // Seed of the map. The same seed always gives the same world. 0 = a new random world every time it is built.
    public int Seed { get; set; } = 201219;

    // Size of the world: 1 = the client's default radius (650 units), 1.3333 = a third wider (the default here). The regions grow with it.
    public float SizeScale { get; set; } = 1.3333f;

    // Size of each island and biome (1 = the disk sizes of the game's config, 24.5 to 61 units). Bigger = fewer, larger regions with room for enemies.
    public float BiomeScale { get; set; } = 2.5f;

    // "original": the twelve zones laid out like the game's own world map (Ruined City in the center, Mecha Hunting Grounds, Graveyard,
    // Fishing Zone... around it). "random": islands made of random disks (BiomeScale and LandFraction apply to this one only).
    public string Layout { get; set; } = "original";

    // Build the world when the server starts (and again when the settings of the map change), before any player comes in, so the
    // launcher shows it and the first player does not wait. false: it is built when the first player arrives.
    public bool Pregenerate { get; set; } = true;

    // An invisible wall at the edge of the world: a plane that gets closer than EdgeMargin units to the border is stopped and put back.
    public bool EdgeWall { get; set; } = true;
    public float EdgeMargin { get; set; } = 24f;

    // Fraction (0 to 1) of the world's circle that is land; the rest is water.
    public float LandFraction { get; set; } = 0.6f;

    // Where players are born and the radius around it where no enemy spawns (units).
    public SpawnSettings Spawn { get; set; } = new SpawnSettings();

    // Where the portal to the world stands in the Nexus (u, v of the Nexus map).
    public NexusPortalSettings NexusPortal { get; set; } = new NexusPortalSettings();

    public EnemySettings Enemies { get; set; } = new EnemySettings();

    public CarrierSettings Carriers { get; set; } = new CarrierSettings();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownKeys { get; set; }

    public sealed class SpawnSettings
    {
        public float X { get; set; }
        public float Y { get; set; } = 8.5f;
        public float Rotation { get; set; }
        public float SafeZoneRadius { get; set; } = 70f;
    }

    public sealed class NexusPortalSettings
    {
        public bool Enabled { get; set; } = true;
        public float U { get; set; } = 58f;
        public float V { get; set; } = 9f;
        public float Rot { get; set; }
    }

    public sealed class EnemySettings
    {
        // false: an empty world (just the map).
        public bool Enabled { get; set; } = true;
        // Enemies exist only near the players: spawn sites closer than SpawnRadius get their enemies, enemies farther than
        // DespawnRadius from every player are taken away (units).
        public float SpawnRadius { get; set; } = 150f;
        public float DespawnRadius { get; set; } = 260f;
        // A player's client is only told about the enemies within this distance of their plane (units), so the radar does not
        // fill the screen with arrows for enemies far away. An enemy leaves the client a little beyond it. 0 = tell everybody
        // about every enemy. Keep it above wakeRadius, or enemies wake up before they show.
        public float ViewRadius { get; set; } = 90f;
        // Distance between spawn sites, in tiles (12 units). Smaller = more enemies.
        public float SiteSpacingTiles { get; set; } = 4f;
        // Most enemies alive in the world at once (the server limit is 140 per instance).
        public int MaxAlive { get; set; } = 100;
        // Most enemies in a group (a leader and its followers).
        public int GroupSizeMax { get; set; } = 4;
        // A sleeping enemy wakes up when a player comes this close (units).
        public float WakeRadius { get; set; } = 75f;
        // Multiplies the time a spawn site waits after its enemies died (the base is the region's
        // secondsPerStandardEnemyIn10x10 of the game's config times the enemies in the group).
        public float RespawnScale { get; set; } = 1f;
        public float HealthMultiplier { get; set; } = 1f;
        // Level of the enemies by region: added to the tier of the region (negative = easier).
        public int TierOffset { get; set; }
        // Names of enemy templates the world never spawns (for example a unit that does not work well yet).
        public List<string> Skip { get; set; } = new List<string>();
    }

    public sealed class CarrierSettings
    {
        // Dungeon carriers (portals) that appear in the world from time to time, one of an open dungeon of dungeons.json.
        public bool Enabled { get; set; } = true;
        public int MaxAtOnce { get; set; } = 2;
        public float EverySeconds { get; set; } = 180f;
        // How long a carrier stays before it leaves (the client shows the countdown).
        public float LifetimeSeconds { get; set; } = 600f;
        // Distance from the spawn point where carriers may appear (units).
        public float MinDistance { get; set; } = 120f;
        public float MaxDistance { get; set; } = 900f;
    }
}

public static class WorldSettingsFile
{
    public const string FileName = "world.json";

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly object gate = new object();
    private static WorldSettings current = new WorldSettings();
    private static DateTime readAt = DateTime.MinValue;
    private static DateTime nextCheck = DateTime.MinValue;
    private static Action<string, string> log = (_, _) => { };

    public static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), FileName);

    // Current settings; the file is checked again at most once per second.
    public static WorldSettings Current
    {
        get
        {
            lock (gate)
            {
                if (DateTime.UtcNow >= nextCheck)
                {
                    nextCheck = DateTime.UtcNow.AddSeconds(1);
                    ReloadIfChanged(false);
                }
                return current;
            }
        }
    }

    public static void Start(Action<string, string> logger)
    {
        lock (gate)
        {
            log = logger;
            if (!File.Exists(FilePath))
            {
                try
                {
                    File.WriteAllText(FilePath, DefaultFileText);
                    log("WORLD", FilePath + " created with the default values");
                }
                catch (Exception e)
                {
                    log("WORLD", "could not create " + FilePath + ": " + e.Message);
                }
            }
            ReloadIfChanged(true);
        }
    }

    private static void ReloadIfChanged(bool atStart)
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            DateTime modified = File.GetLastWriteTimeUtc(FilePath);
            if (modified == readAt) return;
            readAt = modified;

            WorldSettings? read = JsonSerializer.Deserialize<WorldSettings>(File.ReadAllText(FilePath), ReadOptions);
            if (read == null) return;
            Fix(read);
            current = read;
            log("WORLD", (atStart ? "" : "read again: ") + FilePath + ": " + (read.Enabled ? "on" : "OFF") + ", '" + read.Name + "', seed " +
                (read.Seed == 0 ? "random" : read.Seed.ToString()) + ", " + read.MaxPlayers + " player(s), enemies " +
                (read.Enemies.Enabled ? "on" : "off") + ", carriers " + (read.Carriers.Enabled ? "on" : "off"));
            if (read.UnknownKeys != null && read.UnknownKeys.Count > 0)
                log("WORLD", "key(s) this version does not use in " + FileName + ": " + string.Join(", ", read.UnknownKeys.Keys));
        }
        catch (Exception e)
        {
            log("WORLD", "error in " + FilePath + " (" + e.Message + "): keeping the previous configuration");
        }
    }

    // Puts every value in a range the world can work with.
    private static void Fix(WorldSettings s)
    {
        s.Name = string.IsNullOrWhiteSpace(s.Name) ? "Open World" : s.Name.Trim();
        if (s.Name.Length > 32) s.Name = s.Name.Substring(0, 32);
        s.MaxPlayers = Math.Clamp(s.MaxPlayers, 1, 128);
        s.LandFraction = Math.Clamp(s.LandFraction, 0.1f, 1f);
        s.SizeScale = Math.Clamp(s.SizeScale, 1f, 4f);
        s.BiomeScale = Math.Clamp(s.BiomeScale, 1f, 6f);
        s.Layout = string.IsNullOrWhiteSpace(s.Layout) ? "original" : s.Layout.Trim().ToLowerInvariant();
        s.EdgeMargin = Math.Clamp(s.EdgeMargin, 6f, 120f);
        s.Spawn ??= new WorldSettings.SpawnSettings();
        s.Spawn.SafeZoneRadius = Math.Clamp(s.Spawn.SafeZoneRadius, 0f, 300f);
        s.NexusPortal ??= new WorldSettings.NexusPortalSettings();
        WorldSettings.EnemySettings e = s.Enemies ??= new WorldSettings.EnemySettings();
        e.SpawnRadius = Math.Clamp(e.SpawnRadius, 40f, 400f);
        e.DespawnRadius = Math.Clamp(e.DespawnRadius, e.SpawnRadius + 30f, 800f);
        e.ViewRadius = e.ViewRadius <= 0f ? 0f : Math.Clamp(e.ViewRadius, 40f, 800f);
        e.SiteSpacingTiles = Math.Clamp(e.SiteSpacingTiles, 1.5f, 30f);
        e.MaxAlive = Math.Clamp(e.MaxAlive, 1, 130);
        e.GroupSizeMax = Math.Clamp(e.GroupSizeMax, 1, 8);
        e.WakeRadius = Math.Clamp(e.WakeRadius, 10f, 300f);
        e.RespawnScale = Math.Clamp(e.RespawnScale, 0.05f, 100f);
        if (e.HealthMultiplier <= 0f) e.HealthMultiplier = 1f;
        e.Skip ??= new List<string>();
        WorldSettings.CarrierSettings c = s.Carriers ??= new WorldSettings.CarrierSettings();
        c.MaxAtOnce = Math.Clamp(c.MaxAtOnce, 0, 12);
        c.EverySeconds = Math.Max(10f, c.EverySeconds);
        c.LifetimeSeconds = Math.Max(30f, c.LifetimeSeconds);
        c.MinDistance = Math.Max(0f, c.MinDistance);
        c.MaxDistance = Math.Max(c.MinDistance + 50f, c.MaxDistance);
    }

    // Default file (the reader skips the comments).
    private const string DefaultFileText =
@"// Open world of the server (terrain 'Basic'). Reloaded when it changes; the map values (seed, landFraction) apply the next
// time the world is built (when the first player arrives after everybody left).
{
  // false: no portal in the Nexus and /world refuses.
  ""enabled"": true,

  // Name on the Nexus portal.
  ""name"": ""Open World"",

  // Players in the world at the same time.
  ""maxPlayers"": 16,

  // Seed of the map: the same seed makes the same world. 0 = a new random world each time it is built.
  ""seed"": 201219,

  // Size of the world: 1 = the client's default radius (650 units), 1.3333 = a third wider. The regions grow with it.
  ""sizeScale"": 1.3333,

  // Size of each island and biome (1 = the disk sizes of the game's config, 24.5 to 61 units). Bigger = fewer, larger regions with room for enemies.
  ""biomeScale"": 2.5,

  // ""original"": the twelve zones laid out like the game's own world map (Ruined City in the center, Mecha Hunting Grounds, Graveyard,
  // Fishing Zone... around it). ""random"": islands made of random disks (biomeScale and landFraction apply to this one only).
  ""layout"": ""original"",

  // Build the world when the server starts (and again when the map settings change), before any player comes in, so the launcher
  // shows it and the first player does not wait. false: it is built when the first player arrives.
  ""pregenerate"": true,

  // Invisible wall at the edge of the world: a plane closer than edgeMargin units to the border is stopped and put back.
  ""edgeWall"": true,
  ""edgeMargin"": 24,

  // Fraction of the world's circle that is land (the rest is water).
  ""landFraction"": 0.6,

  // Where players are born (the center of the starting beach) and the radius around it with no enemies (units).
  ""spawn"": { ""x"": 0, ""y"": 8.5, ""rotation"": 0, ""safeZoneRadius"": 70 },

  // The portal to the world in the Nexus (u, v of the Nexus map, like the dungeon carriers of dungeons.json).
  ""nexusPortal"": { ""enabled"": true, ""u"": 58, ""v"": 9, ""rot"": 0 },

  // Enemies: they only exist near the players. The type of enemy comes from the region (beach, lowlands, midlands, highlands,
  // mountains, highmountains). The AI is the same as everywhere else (enemies.json / the Enemy AI tab).
  ""enemies"": {
    ""enabled"": true,
    ""spawnRadius"": 150,
    ""despawnRadius"": 260,
    // A client is only told about the enemies within viewRadius units of its plane (so the radar shows no arrows for the far ones).
    // 0 = every enemy to every player. Keep it above wakeRadius.
    ""viewRadius"": 90,
    ""siteSpacingTiles"": 4,
    ""maxAlive"": 100,
    ""groupSizeMax"": 4,
    ""wakeRadius"": 75,
    ""respawnScale"": 1.0,
    ""healthMultiplier"": 1.0,
    ""tierOffset"": 0,
    ""skip"": []
  },

  // Dungeon carriers that appear in the world (the dungeons come from dungeons.json).
  ""carriers"": {
    ""enabled"": true,
    ""maxAtOnce"": 2,
    ""everySeconds"": 180,
    ""lifetimeSeconds"": 600,
    ""minDistance"": 120,
    ""maxDistance"": 520
  }
}
";
}
