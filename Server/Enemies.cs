using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lidgren.Network;

// Enemies of the server: AI, waves (encounters), bosses with phases and chat commands.
//
// How the client treats an enemy (checked in the decompiled code):
//  - the enemy spawns with SpawnUnitsBatch: GenericUnit prefab (9), the UnitConfig's templateHash, aiTeam (2) team
//    and the SERVER as authority. On the client it stays "submissive" and only follows the positions that arrive.
//  - the original game's AI was removed from the client (AI.cs and ModalAI.cs are empty). That is why it is built
//    here: plane flight (minimum/maximum speed and turn per second of the UnitConfig), aim and fire rate of the guns.
//  - position, velocity, rotation and health go in UpdateUnitsBatch 10 times per second
//    (FieldUpdate: POS 1, VEL 2, ROT 4, HEALTH 8; health goes as a fraction from 0 to 1).
//  - the shot is a GunFire (62326) with ownerId = the enemy's shortId and mountIndex = the gun's index in the UnitConfig.
//    The client creates the bullets; if they hit the player's plane, the client itself takes its health off
//    (the player's plane is authoritative on the client: BulletScript.IsCollisionAuthoritative).
//  - the player's bullet that hits an enemy becomes ServerReportBulletHit: Game calls TakeDamage and,
//    when health runs out, a KillUnitsBatch (DestroyedFromDamage) goes out and the client blows the enemy up.
//
// Files (reread by themselves when they change):
//  - BepInEx\units_config.txt, written by the Steambirds Quieter plugin 1.14.0 or newer: name, hash,
//    health, speed, turn and guns of each UnitConfig. Without it the server does not know the templateHash
//    (Mono's string.GetHashCode, which .NET 8 does not reproduce).
//  - enemies.json, in the server folder: enemy types, bosses with phases and encounters (waves).
//    Without the file only the types registered in the code apply (the tutorial's).
//
// Chat commands in the game (the client sends them as SlashCommand): /enemies shows the list.
//
// Scripts (Tutorial): CreateFromScript creates a unit at a fixed point, with a group (to count
// who is still alive) and an area (rectangle of room tiles, see Ground). The area keeps the unit's flight
// inside the room; without an area the "arena" square of enemies.json applies.
// Extra behaviors for the script: "idle" (walls, chests, wrecks) and "escort" (an ally that
// follows the player or goes to a destination). "team": 1 puts the unit on the player's side (ally).
// "invulnerable" ignores damage; "opensAfterHitSeconds" makes a chest open after the first hit.
//
// Dungeons (Dungeons\DungeonServer.cs): boss phases can also give a shield ("shieldSeconds": no damage for a while)
// and a visual effect ("visualEffect": a configBinary "statefulEffects" name, sent in FieldUpdate EFFECTSTATE1 with
// the id MonoHash.StatefulEffectId). The dungeon server also uses the one-unit calls (GetHealth, RemoveUnit,
// SetUnitInvulnerable, SetVisualEffect, IsAsleep).
public static partial class Enemies
{
    // ================================================================== configuration (enemies.json)

    public sealed class EnemiesConfig
    {
        public bool ChatWarnings { get; set; } = true;
        // Sparks, camera shake and banners the server asks the client for (ScreenEffects.cs).
        public ScreenEffects.Settings Effects { get; set; } = new ScreenEffects.Settings();
        public float GlobalHealthMultiplier { get; set; } = 1f;
        public ArenaConfig Arena { get; set; } = new ArenaConfig();
        public Dictionary<string, EnemyType> Types { get; set; } = new Dictionary<string, EnemyType>();
        public List<EncounterConfig> Encounters { get; set; } = new List<EncounterConfig>();
    }

    public sealed class ArenaConfig
    {
        public float CenterX { get; set; }
        public float CenterY { get; set; }
        public float Limit { get; set; } = 40f;
    }

    public sealed class EnemyType
    {
        public string Name { get; set; } = "";
        public string Template { get; set; } = "auto:weak";
        public short Hash { get; set; }
        public string Behavior { get; set; } = "chase";
        public bool Boss { get; set; }
        public float Health { get; set; }
        public float MaxHealth { get; set; }
        public float HealthMultiplier { get; set; } = 1f;
        public float SpeedMultiplier { get; set; } = 1f;
        public float TurnMultiplier { get; set; } = 1f;
        public float FireRateMultiplier { get; set; } = 1f;
        public int DamageMultiple { get; set; } = 1;
        public float PreferredDistance { get; set; } = 12f;
        public float AggroRadius { get; set; } = 60f;
        // Behavior "kamikaze" (bombs that fly at the player) and "mine" (a bomb that stays where it was dropped): the unit dies
        // when it gets this close to the player. The explosion itself is whatever "onDeath" spawns in the dungeon map
        // (a short ring of bullets), so it can hurt. For a mine, pick a "visualEffect" ring (detectionZone_1..3) that matches.
        public float ContactRadius { get; set; } = 1.4f;
        public float ShotRange { get; set; }
        public float AimLead { get; set; }
        public float FirstShotDelay { get; set; } = 1.5f;
        public bool FreeAim { get; set; }
        public bool HomingBullets { get; set; }
        // Behavior "route": name of a path of the map ("routes" of the dungeon file). The unit flies along it, point after point,
        // and starts again at the first one (a boss that circles a ring). Filled in by DungeonMapData from "routes".
        public string Route { get; set; } = "";
        [JsonIgnore]
        public List<(float X, float Y)>? RoutePoints { get; set; }
        public List<int> Guns { get; set; } = new List<int>();
        public List<BossPhase> Phases { get; set; } = new List<BossPhase>();

        // Team of the unit: 2 = enemy (Team.aiTeam), 1 = the player's ally (Team.greenTeam).
        public int Team { get; set; } = 2;
        // Ignores damage (allies, wrecks). The script also turns this on and off per group.
        public bool Invulnerable { get; set; }
        // Greater than 0: chest. The first hit starts the count and it opens (disappears exploding) after this time.
        public float OpensAfterHitSeconds { get; set; }
        // No area: flies free, without the arena square (an ally that goes from one room to another).
        public bool FreeRoam { get; set; }
        // Visual effect shown on the unit from the start (name of a configBinary "statefulEffects" entry, e.g.
        // "tankShield"). Sent in FieldUpdate EFFECTSTATE1; the id is MonoHash.StatefulEffectId(name).
        public string VisualEffect { get; set; } = "";

        // Id of an AI file (ai\brains\<id>.json, made in the launcher's Enemy AI tab). Empty = the built-in "behavior" above.
        // A value that is not text (the old "ai" slot of the map editor) is ignored.
        [JsonConverter(typeof(AiReferenceConverter))]
        public string Ai { get; set; } = "";
    }

    // Reads the "ai" field as an id; anything that is not text is skipped instead of making the whole type unreadable.
    public sealed class AiReferenceConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String) return (reader.GetString() ?? "").Trim();
            reader.Skip();
            return "";
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    public sealed class BossPhase
    {
        public float BelowHealth { get; set; } = 0.5f;
        public string Behavior { get; set; } = "";
        // Changes the AI of the boss when the phase starts (an AI file id). Empty = keeps the one it has.
        [JsonConverter(typeof(AiReferenceConverter))]
        public string Ai { get; set; } = "";
        public float SpeedMultiplier { get; set; }
        public float FireRateMultiplier { get; set; }
        public List<int>? Guns { get; set; }
        public string Summon { get; set; } = "";
        public int SummonCount { get; set; }
        public string Message { get; set; } = "";
        // Speech of the phase: a key of the game's texts (ClientShowUnitChat speech bubble on the boss), like the barks of the dungeons.
        public string Bark { get; set; } = "";
        // Shield when the phase starts: the unit ignores damage for this many seconds (0 = no shield).
        // Same idea as the "toggle_safety" states of the SN-Z in build 190426 (StartEffect "Shielded" for 10 s).
        public float ShieldSeconds { get; set; }
        // Turns the homing bullets on or off from this phase on (empty = keeps what the unit has).
        public bool? HomingBullets { get; set; }
        // Visual effect while the shield lasts (statefulEffects name). Empty with a shield = "tankShield".
        public string VisualEffect { get; set; } = "";
    }

    public sealed class EncounterConfig
    {
        public string Name { get; set; } = "";
        public string Server { get; set; } = "tutorial";
        public bool Active { get; set; } = true;
        public float StartAfterSeconds { get; set; } = 5f;
        public bool Repeat { get; set; }
        public float RepeatInterval { get; set; } = 10f;
        public bool RestartWaveOnDeath { get; set; } = true;
        public string FinalMessage { get; set; } = "";
        public List<WaveConfig> Waves { get; set; } = new List<WaveConfig>();
    }

    public sealed class WaveConfig
    {
        public string Name { get; set; } = "";
        public float DelayBeforeSeconds { get; set; } = 4f;
        public string Message { get; set; } = "";
        public List<GroupConfig> Groups { get; set; } = new List<GroupConfig>();
    }

    public sealed class GroupConfig
    {
        public string Type { get; set; } = "";
        public int Count { get; set; } = 1;
        public float Distance { get; set; } = 28f;
        public float? X { get; set; }
        public float? Y { get; set; }
        public float Spread { get; set; } = 6f;
    }

    // ================================================================== catalog (units_config.txt)

    private sealed class GameUnit
    {
        public string Name = "";
        public short Hash;
        public int Tier;
        public float MaxHp;
        public float VelMin;
        public float VelMax;
        public float Turn;
        public float Radius;
        public bool Capsule;
        public float SpawnDuration;
        public bool Wall;
        public bool Player;
        public bool Boss;
        public int IslandMin = -1;
        public string Art = "";
        public bool Synthetic;
        public readonly List<GameGun> Guns = new List<GameGun>();

        public bool Armed => Guns.Any(a => a.Damage > 0f);
    }

    private sealed class GameGun
    {
        public int Index;
        public string Name = "";
        public int Mode;          // FireMode: 0 Aimed, 1 ArcRight, 2 ArcLeft, 3 Hose, 5 Sprinkler, 6 SprinklerTurnback, 7 JustShootDammit
        public float Angle;      // mountAngle, relative to the front of the unit
        public float Arc;        // firingArc
        public float FireRate;    // rof: shots per second inside the burst
        public int Burst;        // burst: shots per charge
        public float Reload;     // cooldown: wait after the last shot of the charge
        public float Damage;
        public float Speed;
        public float Duration;
        public float Range;
        public int Multi;         // multiBullets: bullets per shot (each one uses a bullet id)
    }

    // ================================================================== state

    private sealed class GunState
    {
        public GameGun Model = null!;
        public int Ammo;
        public double NextShot;
        public double ReloadUntil;
        public float Turn;        // current position of the spiral patterns
        public float Aim = float.NaN;      // last shot angle (relative to the unit's front), for the visual turret
        public double AimAt;               // when that shot was fired
        public float SentAim = float.NaN;  // angle the clients were last told (FIELDMASK_GUNS)
    }

    private sealed class Enemy
    {
        public int ShortId;
        public string TypeKey = "";
        public EnemyType Type = null!;
        public GameUnit Model = null!;
        public bool FromWave;
        public bool Boss;

        public float X;
        public float Y;
        public float Rot;
        public float Vel;
        public float HomeX;
        public float HomeY;

        public float Health;
        public float MaxHp;
        public float VelMin;
        public float VelMax;
        public float MaxTurn;

        public string Behavior = "chase";
        // Running AI file (null = the built-in behavior above). See Enemies.Brains.cs.
        public SteambirdsAi.AiRuntime? Ai;
        public double LastDamagedAt = -1;
        // Health actually taken from this enemy by each player (peer id), for the after-fight "your damage: X%".
        public readonly Dictionary<long, float> DamageBy = new Dictionary<long, float>();
        public float PhaseSpeedMult = 1f;
        public float PhaseFireRateMult = 1f;
        public List<int> ActiveGuns = new List<int>();
        public readonly List<GunState> Guns = new List<GunState>();
        public int PhasesApplied;
        public bool Homing;
        // Next point of the route (behavior "route").
        public int RouteIndex = -1;

        public int Team = EnemyTeam;
        public string Group = "";
        public Ground.Rect? Area;
        public bool FreeRoam;
        public bool Invulnerable;
        public double HitAt = -1;
        public bool HasDest;
        public float DestX;
        public float DestY;

        public int BulletCounter = 1;
        public int Shots;
        public double SpawnedAt;
        public double ActiveAt;
        public bool Aggro;
        // Pre-placed by the script: idle in place, with no target, until the script wakes the group, the player
        // reaches WakeRadius (0 = only by the script or by a hit) or someone in the group takes a hit.
        public bool Asleep;
        public float WakeRadius;
        public float OrbitDirection = 1f;
        public bool Charging = true;
        public double ManeuverChangeAt;
        public float PatrolX;
        public float PatrolY;
        public double NextPatrol;
        public bool HealthChanged = true;
        public double LastHealthSent;

        // Shield of a boss phase (EscudoSegundos): no damage until this time.
        public double ShieldUntil;
        // Visual effect (FieldUpdate EFFECTSTATE1): current id, the id outside shields and "send it again".
        public short EffectId;
        public short BaseEffect;
        public bool EffectChanged;

        // Stun (EMP of the bandit): no flight and no shots until this time (Enemies.Allies.cs).
        public double StunUntil;

        // Units of the player created by ServerSpawnGreenTeamAlly (turrets, mines): see Enemies.Allies.cs.
        public int AllyKind;
        public long OwnerPeer;
        // Short id of the owner's plane: sent as parentShortId so the client puts the unit in the plane's family
        // (the engineer's "teleportToTurret" maneuver looks for units of the same family).
        public int OwnerShortId;
        public double ExpiresAt;
    }

    // A player (one connection) with their enemies. In the tutorial each player has their own instance.
    private sealed class Instance
    {
        public long Peer;
        public NetServer Server = null!;
        public NetConnection Connection = null!;
        public string Role = "";

        public EncounterConfig? Encounter;
        public int CurrentWave = -1;
        public int WaveScheduled = -1;
        public double WaveScheduledFor;
        public bool Done;

        public readonly Dictionary<int, Enemy> Enemies = new Dictionary<int, Enemy>();
        // Most living enemies at once (the map's "values.maxEnemies" can raise it: SetMaxEnemies).
        public int MaxEnemies = MaxEnemiesPerInstance;
        public double LastTick;
        public double LastSend;

        // Players that share this instance (a dungeon with several players). Connection/Peer are the first of them:
        // everything the instance sends goes to all of them.
        public readonly List<NetConnection> Members = new List<NetConnection>();
        public readonly Dictionary<long, PlayerTrack> Tracks = new Dictionary<long, PlayerTrack>();

        // Interest radius (Enemies.Interest.cs): with Filtering on, each player only knows the units near their plane;
        // Known holds, per player (peer id), the short ids the client has been told about. Off = everybody knows everything.
        public bool Filtering;
        public readonly Dictionary<long, HashSet<int>> Known = new Dictionary<long, HashSet<int>>();

        // The player the AI is looking at right now: the nearest live one for each enemy (see FocusNearest).
        public bool HasPlayer;
        public bool PlayerAlive;
        public bool PlayerWasAlive;
        public int PlayerShortId;
        public long FocusPeer;
        public float PlayerX;
        public float PlayerY;
        public float PlayerVX;
        public float PlayerVY;

        public int Kills;
        public int BossesSent = -1;

        // The player's current room (script): wave enemies and commands spawn and fly inside it.
        public Ground.Rect? DefaultArea;
    }

    // Position of one player of the instance, with the velocity estimated from samples (lead aim).
    private sealed class PlayerTrack
    {
        public long Peer;
        public bool Has;
        public bool Alive;
        public int ShortId;
        public float X;
        public float Y;
        public float VX;
        public float VY;
        public bool HasSample;
        public float SampleX;
        public float SampleY;
        public double SampleAt;
    }

    // ================================================================== constants

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const float Rad = MathF.PI / 180f;

    private const ushort MsgSpawnUnitsBatch = 45862;
    private const ushort MsgKillUnitsBatch = 45863;
    private const ushort MsgUpdateUnitsBatch = 45864;
    private const ushort MsgGunFire = 62326;
    private const ushort MsgClientReceiveText = 14789;               // string text, byte type (0 = ServerSays)
    private const ushort MsgClientSetNumBossesRemaining = 18380;     // int count

    private const int PrefabGenericUnit = 9;     // prefab_table.txt: genericPrefabIndex=9
    private const int EnemyTeam = 2;       // Team.aiTeam (playerTeam 0, greenTeam 1, aiTeam 2)
    private const byte ReasonDestroyedByDamage = 10;  // KillReason.DestroyedFromDamage
    private const byte ReasonVanished = 11;             // KillReason.Fizzled
    private const byte ResultNone = 0;          // BulletHitResult.None
    private const byte ResultDamage = 1;            // BulletHitResult.Damage

    private const double TickInterval = 0.05;     // AI: 20 times per second
    private const double SendInterval = 0.1;     // UpdateUnitsBatch: 10 times per second
    private const int MaxEnemiesPerInstance = 140;   // dungeons: walls of the gates + the rooms near the player

    // Margins of a room's area, in units: the edge wall takes up 3 units (one sub-cell).
    private const float DestMargin = 5f;
    private const float HardMargin = 4f;
    // A unit on a route moves on to the next point when it gets this close to the current one (world units).
    private const float RouteArrival = 6f;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static readonly string[] Behaviors = { "chase", "circle", "charge", "turret", "patrol", "idle", "escort", "fixed", "kamikaze", "mine", "route" };

    // ================================================================== global data

    private static readonly Dictionary<long, Instance> instances = new Dictionary<long, Instance>();
    private static readonly Random rng = new Random();

    private static EnemiesConfig config = new EnemiesConfig();

    // Screen effects of enemies.json ("effects").
    public static ScreenEffects.Settings Effects => config.Effects ?? new ScreenEffects.Settings();
    private static bool configLoaded;

    // Types that a script registers in the code: they apply when enemies.json does not have a type with the same key.
    private static readonly Dictionary<string, EnemyType> defaultTypes = new Dictionary<string, EnemyType>(StringComparer.OrdinalIgnoreCase);
    private static DateTime configReadAt = DateTime.MinValue;

    private static Dictionary<string, GameUnit> catalog = new Dictionary<string, GameUnit>(StringComparer.Ordinal);
    private static DateTime catalogReadAt = DateTime.MinValue;
    private static bool warnedMissingCatalog;
    private static readonly Dictionary<string, GameUnit> resolvedTemplates = new Dictionary<string, GameUnit>();
    private static readonly HashSet<string> reportedFailures = new HashSet<string>();

    private static double nextFileCheck;
    private static int nextShortId = 100000;   // far from the ids of the plane (1000) and of the clouds (2000+)
    private static int tickErrors;

    // ================================================================== public entry points

    // Called once when the server starts: reads the catalog and enemies.json and shows what it found.
    public static void Start(Action<string, string> log)
    {
        lock (Game.Lock)
        {
            LoadCatalog(log);
            LoadAi(log);
            LoadConfig(log);
        }
    }

    // Program.cs reports it when it sends the SpawnUnitsBatch of the player's plane.
    // First time: the encounter configured for this server starts. After a death: the wave comes back.
    public static void PlayerPlaneSpawned(NetServer server, NetConnection connection, string role, double now,
        Action<string, string> log)
    {
        lock (Game.Lock)
        {
            Instance inst = GetInstance(server, connection, role, now);
            ResetSample(inst, connection);

            if (inst.Encounter == null)
            {
                EncounterConfig? encounter = PickEncounter(role);
                if (encounter == null)
                {
                    log("ENEMY", "[" + role + "] no active encounter for this server in enemies.json: no automatic enemies");
                    return;
                }
                StartEncounter(inst, encounter, now, log);
                return;
            }

            if (inst.WaveScheduled >= 0)
                inst.WaveScheduledFor = Math.Max(inst.WaveScheduledFor, now + 3.0);
        }
    }

    // Called by each server's UDP loop, several times per second.
    // Each instance only steps 20 times per second; updates go out 10 times per second.
    public static void Update(NetServer server, string role, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            try
            {
                if (now >= nextFileCheck)
                {
                    nextFileCheck = now + 2.0;
                    LoadCatalog(log);
                    LoadAi(log);
                    LoadConfig(log);
                }

                // Poison clouds hurt the enemies (Skills/CloudDamage.cs).
                CloudDamage.Update(now, log);

                // An instance with several players is in the dictionary once per player.
                foreach (Instance inst in instances.Values.Distinct().ToList())
                {
                    if (inst.Server != server) continue;
                    double step = now - inst.LastTick;
                    if (step < TickInterval) continue;
                    inst.LastTick = now;
                    foreach (NetConnection dead in inst.Members.Where(c => c.Status == NetConnectionStatus.Disconnected).ToList())
                    {
                        if (inst.Members.Count < 2) break;
                        inst.Members.Remove(dead);
                        inst.Tracks.Remove(dead.RemoteUniqueIdentifier);
                        if (inst.Connection == dead)
                        {
                            inst.Connection = inst.Members[0];
                            inst.Peer = inst.Connection.RemoteUniqueIdentifier;
                        }
                    }
                    if (inst.Connection.Status != NetConnectionStatus.Connected) continue;
                    UpdateInstance(inst, (float)Math.Min(0.25, step), now, log);
                }
            }
            catch (Exception e)
            {
                tickErrors++;
                if (tickErrors <= 5 || tickErrors % 200 == 0)
                {
                    string stack = (e.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim() ?? "";
                    log("ENEMY", "AI error (" + tickErrors + "): " + e.GetType().Name + ": " + e.Message + " | " + stack);
                }
            }
        }
    }

    // Game calls it on receiving ServerReportBulletHit or ServerReportUnitDamage.
    // Returns null if the target is not a server enemy; otherwise, a short text for the hit's log.
    public static string? TakeDamage(NetConnection? connection, int target, float damage, double now, Action<string, string> log)
    {
        if (connection == null) return null;

        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return null;
            if (!inst.Enemies.TryGetValue(target, out Enemy? enemy)) return null;

            if (damage <= 0f) return "enemy " + Describe(enemy) + " no damage";

            // A shot at someone who was waiting: the whole group wakes up.
            if (enemy.Asleep) WakeGroupInternal(inst, enemy, now);

            // Chest: the first hit starts opening it; health does not change (UpdateInstance opens it at the right time).
            if (enemy.Type.OpensAfterHitSeconds > 0f)
            {
                if (enemy.HitAt < 0)
                {
                    enemy.HitAt = now;
                    log("ENEMY", inst.Role + " " + Describe(enemy) + " hit: opens in " + Num(enemy.Type.OpensAfterHitSeconds) + "s");
                }
                return "bau " + Describe(enemy) + " opening";
            }

            if (enemy.Invulnerable) return "enemy " + Describe(enemy) + " invulnerable";
            if (now < enemy.ShieldUntil) return "enemy " + Describe(enemy) + " with a shield";

            // Only the health that was really left counts (the last shot does not get credit for overkill).
            float dealt = Math.Min(damage, Math.Max(0f, enemy.Health));
            long attacker = connection.RemoteUniqueIdentifier;
            enemy.DamageBy[attacker] = (enemy.DamageBy.TryGetValue(attacker, out float before) ? before : 0f) + dealt;

            enemy.Health -= damage;
            enemy.LastDamagedAt = now;
            enemy.HealthChanged = true;

            if (enemy.Health <= 0f)
            {
                enemy.Health = 0f;
                if (enemy.Team == EnemyTeam) inst.Kills++;
                Remove(inst, new List<Enemy> { enemy }, ReasonDestroyedByDamage, ResultDamage, now);
                log("ENEMY", inst.Role + " " + Describe(enemy) + " destroyed at (" + Num(enemy.X) + ", " + Num(enemy.Y) +
                    ") after " + enemy.Shots + " shot(s) | kills: " + inst.Kills +
                    ", left " + inst.Enemies.Count);
                if (enemy.Boss)
                {
                    Warn(inst, enemy.Type.Name + " was defeated!");
                    foreach (NetConnection member in inst.Members.ToList()) ScreenEffects.BossDeathShake(inst.Server, member);
                }
                // Progression (Progression/PilotProgression.cs): plane XP, "on kill" skills and the after-fight text
                // of the bosses. Only real enemies (the AI's team); script allies give no XP.
                // With several players in the instance, every one of them gets the XP and their own loot bags.
                if (enemy.Team == EnemyTeam)
                {
                    float totalDamage = enemy.DamageBy.Values.Sum();
                    foreach (NetConnection member in inst.Members.ToList())
                    {
                        // This player's share of the damage taken by the enemy (the killer gets all of it if nothing was recorded).
                        float share = totalDamage > 0f
                            ? (enemy.DamageBy.TryGetValue(member.RemoteUniqueIdentifier, out float mine) ? mine : 0f) / totalDamage
                            : (member == connection ? 1f : 0f);
                        bool lootAllowed = PilotProgression.EnemyKilled(inst.Server, member, inst.Role.Trim('[', ']'), enemy.Model.Name, enemy.Model.Hash,
                            enemy.Model.Tier, enemy.Model.MaxHp, enemy.Boss || enemy.Model.Boss, enemy.ShortId, enemy.X, enemy.Y, share, now);
                        // Loot (Items/LootDrops.cs and the loot_tables folder): bags where the enemy fell.
                        // A boss only drops loot for players who did at least the minimum share of the damage.
                        if (lootAllowed)
                            LootDrops.EnemyKilled(inst.Server, member, inst.Role.Trim('[', ']'), enemy.Model.Name, enemy.Model.Tier,
                                enemy.Boss || enemy.Model.Boss, enemy.X, enemy.Y, now, log);
                    }
                }
                return "enemy " + Describe(enemy) + " DESTROYED";
            }

            ApplyPhases(inst, enemy, now, log);
            return "enemy " + Describe(enemy) + " health " + Num(enemy.Health) + "/" + Num(enemy.MaxHp);
        }
    }

    // Area damage of a skill cloud (Skills/CloudDamage.cs): every enemy of the instance of this player whose body touches
    // the circle loses "damage" health. Returns how many enemies were hit. The kill, XP and loot go through TakeDamage.
    public static int DamageArea(NetConnection? connection, float x, float y, float radius, float damage, double now,
        Action<string, string> log)
    {
        if (connection == null || damage <= 0f) return 0;

        List<int> targets;
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return 0;
            targets = new List<int>();
            foreach (Enemy enemy in inst.Enemies.Values)
            {
                if (enemy.Team != EnemyTeam || enemy.Invulnerable || enemy.Type.OpensAfterHitSeconds > 0f) continue;   // chests do not open by poison
                float dx = enemy.X - x;
                float dy = enemy.Y - y;
                float reach = radius + Math.Max(0f, enemy.Model.Radius);
                if (dx * dx + dy * dy <= reach * reach) targets.Add(enemy.ShortId);
            }
        }

        int hit = 0;
        foreach (int target in targets)
        {
            if (TakeDamage(connection, target, damage, now, log) != null) hit++;
        }
        return hit;
    }

    // Chat SlashCommand. Returns false if the command is not an enemy one (Game only logs it).
    public static bool Command(NetServer server, NetConnection? connection, string role, List<string> pieces, double now,
        Action<string, string> log)
    {
        if (connection == null || pieces.Count == 0) return false;
        string command = pieces[0].Trim().ToLowerInvariant();
        string? arg = pieces.Count > 1 ? pieces[1].Trim() : null;

        lock (Game.Lock)
        {
            Instance inst;
            switch (command)
            {
                case "/enemies":
                case "/enemyhelp":
                    inst = GetInstance(server, connection, role, now);
                    Reply(inst, "Commands: /wave N, /next, /boss, /enemy TYPE [count], /spawn TEMPLATE [count], " +
                        "/clear, /reset, /encounter [name], /types, /catalog [filter], /brains");
                    Reply(inst, "Tutorial: /step N, /portal, /restart (help: /tutorialhelp)");
                    Reply(inst, "Now: " + StateText(inst));
                    break;

                case "/wave":
                {
                    inst = GetInstance(server, connection, role, now);
                    EncounterConfig? encounter = inst.Encounter ?? PickEncounter(role);
                    if (encounter == null || encounter.Waves.Count == 0) { Reply(inst, "No encounter with waves for this server."); break; }
                    if (!int.TryParse(arg, NumberStyles.Integer, Inv, out int number) || number < 1 || number > encounter.Waves.Count)
                    {
                        Reply(inst, "Use /wave N (from 1 to " + encounter.Waves.Count + ").");
                        break;
                    }
                    JumpToWave(inst, encounter, number - 1, now);
                    Reply(inst, "Wave " + number + " in 1 second.");
                    break;
                }

                case "/next":
                {
                    inst = GetInstance(server, connection, role, now);
                    EncounterConfig? encounter = inst.Encounter ?? PickEncounter(role);
                    if (encounter == null || encounter.Waves.Count == 0) { Reply(inst, "No encounter with waves for this server."); break; }
                    int next = inst.WaveScheduled >= 0 ? inst.WaveScheduled : inst.CurrentWave + 1;
                    if (next >= encounter.Waves.Count) next = 0;
                    JumpToWave(inst, encounter, next, now);
                    Reply(inst, "Wave " + (next + 1) + " in 1 second.");
                    break;
                }

                case "/boss":
                {
                    inst = GetInstance(server, connection, role, now);
                    EncounterConfig? encounter = inst.Encounter ?? PickEncounter(role);
                    if (encounter == null || encounter.Waves.Count == 0) { Reply(inst, "No encounter with waves for this server."); break; }
                    int index = encounter.Waves.Count - 1;
                    for (int i = 0; i < encounter.Waves.Count; i++)
                    {
                        if (encounter.Waves[i].Groups.Any(g => config.Types.TryGetValue(g.Type, out var t) && t.Boss)) { index = i; break; }
                    }
                    JumpToWave(inst, encounter, index, now);
                    Reply(inst, "Boss wave (" + (index + 1) + ") in 1 second.");
                    break;
                }

                case "/enemy":
                case "/spawn":
                {
                    inst = GetInstance(server, connection, role, now);
                    if (string.IsNullOrEmpty(arg))
                    {
                        Reply(inst, command == "/spawn" ? "Use /spawn TEMPLATENAME [count]" : "Use /enemy TYPE [count]. Types: " +
                            string.Join(", ", config.Types.Keys));
                        break;
                    }
                    int count = 1;
                    if (pieces.Count > 2) int.TryParse(pieces[2], NumberStyles.Integer, Inv, out count);
                    count = Math.Clamp(count, 1, 20);

                    string key;
                    EnemyType? type;
                    if (command == "/spawn")
                    {
                        key = "spawn:" + arg;
                        type = new EnemyType { Name = arg, Template = arg, Behavior = "chase", FreeAim = true };
                    }
                    else
                    {
                        key = arg;
                        type = GetEnemyType(arg);
                        if (type == null)
                        {
                            Reply(inst, "Type '" + arg + "' does not exist. Types: " + string.Join(", ", config.Types.Keys));
                            break;
                        }
                    }

                    var added2 = new List<Enemy>();
                    float angle = (float)(rng.NextDouble() * 360.0);
                    for (int i = 0; i < count; i++)
                    {
                        PositionNearPlayer(inst, angle + i * (360f / count), 20f, out float x, out float y);
                        Enemy? enemy = CreateEnemy(inst, key, type, x, y, false, now, log);
                        if (enemy != null) added2.Add(enemy);
                    }
                    SendSpawns(inst, added2, now);
                    Reply(inst, added2.Count > 0
                        ? added2.Count + "x " + Describe(added2[0]) + " (health " + Num(added2[0].MaxHp) + ")"
                        : "Could not create: " + FailureReason(type));
                    break;
                }

                case "/clear":
                {
                    inst = GetInstance(server, connection, role, now);
                    int total = inst.Enemies.Count;
                    Remove(inst, inst.Enemies.Values.ToList(), ReasonVanished, ResultNone, now);
                    inst.WaveScheduled = -1;
                    inst.Done = true;
                    Reply(inst, total + " enemy(ies) removed. Waves stopped: /reset or /wave N.");
                    break;
                }

                case "/reset":
                {
                    inst = GetInstance(server, connection, role, now);
                    EncounterConfig? encounter = inst.Encounter ?? PickEncounter(role);
                    if (encounter == null) { Reply(inst, "No encounter for this server."); break; }
                    StartEncounter(inst, encounter, now, log);
                    break;
                }

                case "/encounter":
                {
                    inst = GetInstance(server, connection, role, now);
                    if (string.IsNullOrEmpty(arg))
                    {
                        Reply(inst, "Encounters: " + string.Join(", ", config.Encounters.Select(e =>
                            e.Name + " (" + e.Server + (e.Active ? "" : ", off") + ", " + e.Waves.Count + " waves)")));
                        break;
                    }
                    EncounterConfig? encounter = config.Encounters.FirstOrDefault(e => string.Equals(e.Name, arg, StringComparison.OrdinalIgnoreCase));
                    if (encounter == null) { Reply(inst, "Encounter '" + arg + "' does not exist."); break; }
                    StartEncounter(inst, encounter, now, log);
                    break;
                }

                case "/types":
                {
                    inst = GetInstance(server, connection, role, now);
                    foreach (var pair in config.Types)
                    {
                        GameUnit? model = Resolver(pair.Value, out string reason);
                        Reply(inst, pair.Key + ": " + (model == null ? "NO TEMPLATE (" + reason + ")" : ModelText(model)) +
                            " | " + pair.Value.Behavior + (pair.Value.Boss ? ", boss" : ""));
                    }
                    break;
                }

                case "/brains":
                {
                    inst = GetInstance(server, connection, role, now);
                    ReplyBrains(inst);
                    break;
                }

                case "/catalog":
                {
                    inst = GetInstance(server, connection, role, now);
                    if (catalog.Count == 0) { Reply(inst, "Empty catalog: " + CatalogPath() + " does not exist yet."); break; }
                    var foundAll = catalog.Values
                        .Where(u => u.Armed && !u.Wall && !u.Player)
                        .Where(u => string.IsNullOrEmpty(arg) || u.Name.Contains(arg, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(u => u.MaxHp).ThenBy(u => u.Name, StringComparer.Ordinal)
                        .ToList();
                    Reply(inst, foundAll.Count + " armed unit(s)" + (string.IsNullOrEmpty(arg) ? "" : " with '" + arg + "'") +
                        (foundAll.Count > 15 ? " (showing 15; use /catalog FILTER)" : "") + ":");
                    foreach (GameUnit unit in foundAll.Take(15)) Reply(inst, ModelText(unit));
                    break;
                }

                default:
                    return false;
            }

            log("COMMAND", "[" + role + "] " + string.Join(" ", pieces));
            return true;
        }
    }

}
