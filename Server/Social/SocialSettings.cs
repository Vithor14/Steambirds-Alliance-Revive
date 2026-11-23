using System.Text.Json;
using System.Text.Json.Serialization;

// Host settings of the social features (social.json, next to world.json and dungeons.json): friends, flocks (guilds) and the
// teleports to other players. A flat file (the launcher's settings page edits top-level keys). It is created with the defaults the
// first time the server starts and read again when it changes. Where a number is 0 or -1 the value of the game's own config
// ("guilds" file of the configBinary) is used, so a host who touches nothing gets the original rules.
public sealed class SocialSettings
{
    public string Note { get; set; } = "";

    // ---------------------------------------------------------------- friends

    // false: the friend list stays empty and "/friend" does nothing.
    public bool FriendsEnabled { get; set; } = true;

    // Most friends one player can have (blocked and muted players do not count).
    public int MaxFriends { get; set; } = 100;

    // ---------------------------------------------------------------- flocks

    // false: no flocks on this server (the Flock Hall answers with an error).
    public bool FlocksEnabled { get; set; } = true;

    // true (the game's own rule): creating a flock takes one founder token from the plane's cargo. The client only shows
    // the "create" page to a player who carries one; give it with /item create_flock_token or in the starting items.
    // false: nothing is taken (the client still needs the token in the cargo to open the page).
    public bool RequireFounderToken { get; set; } = true;

    // Most members of one flock. 0 = the game's value (60).
    public int MaxMembers { get; set; }

    // Most flocks one player can be in. 0 = the game's value (3).
    public int MaxFlocksPerPlayer { get; set; }

    // A player who joined at least RejoinAfterJoins flocks has to wait RejoinSeconds between two joins. -1 = the game's
    // values (5 joins, 86400 s); RejoinSeconds 0 turns the limit off.
    public int RejoinSeconds { get; set; } = -1;
    public int RejoinAfterJoins { get; set; } = -1;

    // A member muted by this many other members is banned from the flock. -1 = the game's value (5); 0 = never.
    public int AutoBanMutes { get; set; } = -1;

    // Seconds without playing before a member shows as inactive in the roster. -1 = the game's value (604800 = 7 days).
    public int InactiveSeconds { get; set; } = -1;

    // ---------------------------------------------------------------- teleport

    // The game's own teleport to a player on the same server (radar, friend list or /tp NAME) in the open world. false: the
    // client's map info turns it off there.
    public bool ToPlayersInWorld { get; set; } = true;

    // "Join Server" in the friend list and in the flock roster: go to the server where a friend or flockmate is (Nexus and
    // the open world; dungeons and the tutorial are private). false: the server answers "not joinable".
    public bool JoinPlayerServers { get; set; } = true;

    // After joining a server through a friend, the plane is born close to that friend instead of at the spawn point.
    public bool SpawnNearTarget { get; set; } = true;

    // How far from the friend (units) the plane is born when SpawnNearTarget is on.
    public float ArrivalDistance { get; set; } = 14f;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownKeys { get; set; }
}

public static class SocialSettingsFile
{
    public const string FileName = "social.json";

    private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly object gate = new object();
    private static SocialSettings current = new SocialSettings();
    private static DateTime readAt = DateTime.MinValue;
    private static DateTime nextCheck = DateTime.MinValue;
    private static Action<string, string> log = (_, _) => { };

    public static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), FileName);

    // Current settings; the file is checked again at most once per second.
    public static SocialSettings Current
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
                    log("SOCIAL", FilePath + " created with the default values");
                }
                catch (Exception e)
                {
                    log("SOCIAL", "could not create " + FilePath + ": " + e.Message);
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

            SocialSettings? read = JsonSerializer.Deserialize<SocialSettings>(File.ReadAllText(FilePath), ReadOptions);
            if (read == null) return;
            Fix(read);
            current = read;
            log("SOCIAL", (atStart ? "" : "read again: ") + FilePath + ": friends " + (read.FriendsEnabled ? "on" : "OFF") + ", flocks " +
                (read.FlocksEnabled ? "on" : "OFF") + ", teleport to players in the world " + (read.ToPlayersInWorld ? "on" : "OFF") +
                ", join player servers " + (read.JoinPlayerServers ? "on" : "OFF"));
            if (read.UnknownKeys != null && read.UnknownKeys.Count > 0)
                log("SOCIAL", "key(s) this version does not use in " + FileName + ": " + string.Join(", ", read.UnknownKeys.Keys));
        }
        catch (Exception e)
        {
            log("SOCIAL", "error in " + FilePath + " (" + e.Message + "): keeping the previous configuration");
        }
    }

    private static void Fix(SocialSettings s)
    {
        s.MaxFriends = Math.Clamp(s.MaxFriends, 1, 1000);
        s.MaxMembers = Math.Clamp(s.MaxMembers, 0, 500);
        s.MaxFlocksPerPlayer = Math.Clamp(s.MaxFlocksPerPlayer, 0, 50);
        s.ArrivalDistance = Math.Clamp(s.ArrivalDistance, 4f, 60f);
    }

    private const string DefaultFileText =
@"// Friends, flocks (guilds) and teleports to other players. Reloaded when it changes. Where a number says 0 or -1, the game's own
// value is used (its ""guilds"" config).
{
  // false: the friend list stays empty.
  ""friendsEnabled"": true,
  ""maxFriends"": 100,

  // false: no flocks on this server.
  ""flocksEnabled"": true,

  // true (the game's rule): creating a flock takes one founder token (create_flock_token) from the plane's cargo. The Flock Hall only
  // shows the create page to a player who carries one: give it with /item create_flock_token, or put it in the starting items.
  ""requireFounderToken"": true,

  // 0 = the game's value: 60 members per flock, 3 flocks per player.
  ""maxMembers"": 0,
  ""maxFlocksPerPlayer"": 0,

  // Who joined 5 flocks has to wait 86400 s between two joins (-1 = the game's values; rejoinSeconds 0 = no limit).
  ""rejoinSeconds"": -1,
  ""rejoinAfterJoins"": -1,

  // A member muted by 5 other members is banned from the flock (-1 = the game's value, 0 = never).
  ""autoBanMutes"": -1,

  // Seconds without playing before a member shows as inactive in the roster (-1 = 7 days).
  ""inactiveSeconds"": -1,

  // The game's own teleport to a player on the same server (radar, friend list, /tp NAME) in the open world.
  ""toPlayersInWorld"": true,

  // 'Join Server' in the friend list and in the flock roster: go to the Nexus or to the open world where a friend or flockmate is.
  ""joinPlayerServers"": true,

  // The plane is born close to that friend (instead of at the spawn point) and this many units away from the friend.
  ""spawnNearTarget"": true,
  ""arrivalDistance"": 14
}
";
}
