using System.Globalization;
using System.Text;
using Lidgren.Network;

// Handling of the messages the client sends during the game.
//
// Every message that exists in the MessageCatalog goes through here: the server keeps the state of each
// connected player (plane, position, buttons, shots, skills) and answers what the client keeps waiting for.
//
// What has a real answer:
//  - ServerEnterStore        -> ClientReceiveStoreContent (empty list of shelves, but not null);
//  - ServerRequestGiverQuests and ServerEnterQuestGiver -> ClientReceiveGiverQuests (no quest). The Pilot's
//    Pub and the Workshop only open the window (and release the docked plane) when this answer arrives;
//  - all RPCs                -> answer 52428 with the rpcId, so the client screen does not keep waiting
//                               (the inventory ones reach Items/InventoryHandler first and do not pass here);
//  - ServerUnitAction action 2 -> creates the skill cloud/blast (SpawnUnitsBatch) and ends it at the end of the
//                               duration, when the plugin has already written BepInEx\actions_config.txt.
//
// What only changes the state and shows in the log (the client expects no answer):
//  - shots (GunFire, ServerReportBulletHit), player input, maneuvers, super speed,
//    batches of client units (Spawn/Update/KillUnitsBatch), death request, language.
//
// Enemies (Enemies):
//  - ServerReportBulletHit and ServerReportUnitDamage on an enemy take health off it on the server;
//  - SlashCommand: first the progression ones (ProgressionHandler: /pilot, /xp, /level, /rank...), then those of
//    the dungeons (DungeonServer: /dungeons, /dungeon, /room...), those of the tutorial (Tutorial: /step, /portal,
//    /restart) and finally those of the enemies (/enemies, /wave, /boss...);
//  - the real death of the player's plane goes to PilotProgression: pilot XP, death screen
//    (ClientDeathSummaryInformation) and the plane leaves the account (permadeath), as the client does;
//  - GetPlane gives the position of the player's plane to the AI. The AI uses the same lock (Lock).
//
// The other catalog messages are decoded field by field and logged with their real name.
public static partial class Game
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const ushort MsgResyncSender = 3;
    private const ushort MsgFriendData = 12100;
    private const ushort MsgRequestFriendData = 12200;
    private const ushort MsgRemotePlayerData = 12509;
    private const ushort MsgSpawnGreenTeamAlly = 23584;
    private const ushort MsgRequestKill = 25386;
    private const ushort MsgCreateUnit = 26723;
    private const ushort MsgUnitAction = 26781;
    private const ushort MsgSlashCommand = 29486;
    private const ushort MsgLocale = 30011;
    private const ushort MsgReportDamageBoost = 13949;   // ServerReportDamageBoost (Skills\DamageBoost.cs)
    private const ushort MsgReportUnitDamage = 38890;
    private const ushort MsgSpawnUnitsBatch = 45862;
    private const ushort MsgKillUnitsBatch = 45863;
    private const ushort MsgUpdateUnitsBatch = 45864;
    private const ushort MsgPlayerInputOld = 49202;
    private const ushort MsgUnitManeuver = 52352;
    private const ushort MsgRpcReply = 52428;
    private const ushort MsgStoreContent = 54290;
    private const ushort MsgEnterStore = 54295;
    private const ushort MsgRequestGiverQuests = 54191;  // ServerRequestGiverQuests(string giverId)
    private const ushort MsgGiverQuests = 54192;         // ClientReceiveGiverQuests(string giverId, Dictionary)
    private const ushort MsgEnterQuestGiver = 54195;     // ServerEnterQuestGiver(string giverId)
    private const ushort MsgBulletHit = 61050;
    private const ushort MsgPlayerInput = 61257;
    private const ushort MsgProximityChat = 11732;       // ServerProximityChat(string text, short quickChatId)
    private const ushort MsgGunFire = 62326;

    private const int ActionDropCloud = 2;       // UnitScript.doDropCloudRaw
    private const int ActionSuperSpeed = 3;   // UnitScript, while super speed is on
    private const int SlotEngine = 2;            // EquipmentSlot.Engine
    private const int SlotMainGun = 4;           // EquipmentSlot.MainGun
    private const byte ReasonFizzled = 11;       // KillReason.Fizzled

    // isAdmin that goes in the player's own data (ClientReceiveRemotePlayerData).
    // true: the client does not knock down an idle player (without it, it disconnects by itself after
    // 120 s without pressing anything in the Nexus or the tutorial) and unlocks the admin chat commands.
    // false: behavior of a regular player.
    private const bool AdminAccount = true;

    // prefab_table.txt of this build (GlobalPrefabContainer.prefabTable). Only so the log is readable.
    private static readonly string[] PrefabNames =
    {
        "playerPlane", "Wall", "poisonCloud", "damageCloud", "healCloud", "shieldBlast", "rechargeCloud",
        "LoreText", "lootbag", "GenericUnit", "Docker", "Portal", "Hangar", "bullet", "StorageBuilding",
        "poisonBlast", "damageAura", "damageResistAura", "EnemyMirrorProxy", "healAura", "TradingStall",
        "StorageCarrier", "damageBlast", "Tavern", "StoreBuilding", "DungeonMarker", "Crafter",
        "PilotSchoolBuilding", "FlockHall", "CombatSimBuilding", "MegaCarrierBuilding"
    };

    // Log label for messages that are only decoded. What is not here comes out as [MSG].
    private static readonly Dictionary<ushort, string> Tags = new Dictionary<ushort, string>
    {
        [13289] = "EFFECT",   // ServerReportPlayerEffect
        [13947] = "SKILL",    // ServerReportPlayerSkillTrigger
        [13948] = "PLANE",    // ServerReportPlaneLevel
        [13949] = "SKILL",    // ServerReportDamageBoost
        [27910] = "SKILL",    // ServerReportCloudHeal
        [38890] = "SHOT",     // ServerReportUnitDamage
        [60850] = "EFFECT",   // ServerShowInstantEffect
        [65043] = "SKILL",    // TriggerOtherPlayerSkill
        [54296] = "STORE",
        [54297] = "STORE",
        [54298] = "STORE",
        [54300] = "STORE"
    };

    // ------------------------------------------------------------------ state

    // A unit of the world that the server knows.
    private sealed class Unit
    {
        public int ShortId;
        public int Prefab;
        public short Template;
        public int Team;
        public float X;
        public float Y;
        public float Rotation;
        public float Health;
        public float Energy;
        public bool HasHealth;
        public bool HasEnergy;
    }

    // A connected player (one UDP connection).
    private sealed class Player
    {
        public long Peer;
        public string Role = "";
        public string Language = "";

        // Who the player is (Program.cs reports it in ServerSetSessionTicket and on spawn).
        public byte[]? Account;
        public string Name = "";
        public string Server = "";
        public string? CharacterId;
        public string? Class;
        public double LastDataSent = -10;
        public int DataSent;

        public int PlaneShortId;
        public bool PlaneAlive;
        public bool HasPosition;
        public float X;
        public float Y;
        public float Rotation;
        public float Aim;
        public float Health;
        public float Energy;
        public bool HasHealth;
        public bool HasEnergy;

        public ushort SlotMask;
        public int State;

        public int Shots;
        public int Hits;
        public double Damage;
        public bool BurstOpen;
        public double BurstStart;
        public double LastShot;
        public int ShotsInBurst;
        public int HitsInBurst;
        public double DamageInBurst;

        public bool SuperOpen;
        public double SuperStart;
        public double LastSuper;

        public int Maneuvers;
        public int Clouds;
        public int Stores;
        public int Rpcs;

        public double NextSummary;
        public readonly Dictionary<int, Unit> Units = new Dictionary<int, Unit>();
        public readonly Dictionary<ushort, int> Count = new Dictionary<ushort, int>();
    }

    // A skill cloud/blast, as the plugin wrote it in actions_config.txt.
    private sealed class Cloud
    {
        public string Name = "";
        public short Hash;
        public string Prefab = "";
        public int PrefabIndex;
        public float Duration;
        public float FinalRadius;
        public bool AttachedToOwner;
        public bool Resolvable;
    }

    // Death of a unit created by the server, scheduled for a future time (end of a cloud).
    private sealed class PendingDeath
    {
        public double When;
        public NetServer Server = null!;
        public NetConnection Connection = null!;
        public int ShortId;
        public float X;
        public float Y;
        public string Name = "";
    }

    private static readonly object gate = new object();
    private static readonly Dictionary<long, Player> players = new Dictionary<long, Player>();

    // Lock shared with Enemies. A single lock for both keeps the UDP threads
    // (nexus and tutorial) from blocking each other while waiting.
    public static object Lock => gate;
    private static readonly List<PendingDeath> pendingDeaths = new List<PendingDeath>();

    private static Dictionary<string, Cloud> clouds = new Dictionary<string, Cloud>();
    private static Dictionary<int, string> maneuvers = new Dictionary<int, string>();
    private static DateTime actionsReadAt = DateTime.MinValue;
    private static bool warnedMissingActions;

    // Ids of units created by the server. The player's plane uses 1000 (PlaneSpawn).
    private static int nextShortId = 2000;

    // ------------------------------------------------------------------ public entry points

    // Handles a data message. Returns false when the ID does not exist in the catalog
    // (then Program.cs logs the raw bytes). "now" is the server clock in seconds,
    // the same one that goes in the TimeSync answers.
    public static bool Handle(NetServer server, NetIncomingMessage msg, ushort id, string role,
        double now, Action<string, string> log)
    {
        if (!MessageCatalog.ById.TryGetValue(id, out var info)) return false;

        lock (gate)
        {
            Player j = GetPlayer(msg.SenderConnection, role, now);
            int times = Tally(j, id);

            SendDueDeaths(now, log);
            CloseSequences(j, now, log);
            Summarize(j, now, log);

            if (info.RpcReturn != null)
            {
                ReplyRpc(server, msg, info, j, log);
                return true;
            }

            switch (id)
            {
                case MsgResyncSender:
                case MsgPlayerInputOld:
                    // Clock resync and old-format input: they only go into the count.
                    return true;

                case MsgPlayerInput:
                    HandleInput(j, msg, log);
                    return true;

                case MsgGunFire:
                    HandleShot(j, msg, now, log);
                    return true;

                case MsgBulletHit:
                    HandleHit(j, msg, now, log);
                    return true;

                case MsgSlashCommand:
                    HandleCommand(server, j, msg, role, now, log);
                    return true;

                case MsgReportUnitDamage:
                    // Maneuver or skill damage on an enemy; then it falls through to the generic log below.
                    HandleManeuverDamage(msg, now, log);
                    break;

                case MsgReportDamageBoost:
                    // The client applied a damage boost itself; then falls into the generic log below.
                    HandleClientBonus(j, msg, now);
                    break;

                case MsgUnitManeuver:
                    HandleManeuver(j, msg, log);
                    return true;

                case MsgUnitAction:
                    HandleUnitAction(server, j, msg, now, log);
                    return true;

                case MsgSpawnGreenTeamAlly:
                    log("SKILL", j.Role + " ally request (ServerSpawnGreenTeamAlly): " +
                        CopperReader.Describe(msg, info.Args) + " -> logged; the server does not create ally units yet");
                    return true;

                case MsgEnterStore:
                    HandleStore(server, j, msg, log);
                    return true;

                case MsgRequestGiverQuests:
                case MsgEnterQuestGiver:
                    HandleQuestGiver(server, j, msg, id == MsgEnterQuestGiver, log);
                    return true;

                case MsgLocale:
                    HandleLanguage(j, msg, log);
                    return true;

                case MsgRemotePlayerData:
                    HandlePlayerData(server, j, msg, times, now, log);
                    return true;

                case MsgRequestFriendData:
                    HandleFriendList(server, j, msg, log);
                    return true;

                case MsgSpawnUnitsBatch:
                    HandleSpawnBatch(j, msg, log);
                    return true;

                case MsgUpdateUnitsBatch:
                    HandleUpdateBatch(j, msg, now);
                    return true;

                case MsgProximityChat:
                    HandleProximityChat(j, msg, log);
                    return true;

                case MsgKillUnitsBatch:
                    HandleDeathBatch(server, j, msg, log);
                    return true;

                case MsgCreateUnit:
                    HandleUnitCreation(j, msg, log);
                    return true;

                case MsgRequestKill:
                    HandleDeathRequest(server, j, msg, log);
                    return true;
            }

            // Other catalog messages: decoded and logged the first 3 times and then every 50.
            if (times <= 3 || times % 50 == 0)
            {
                string tag = Tags.TryGetValue(id, out var foundOne) ? foundOne : "MSG";
                log(tag, j.Role + " " + info.Name + " x" + times + ": " + CopperReader.Describe(msg, info.Args));
            }
            return true;
        }
    }

    // Program.cs reports who the player is right after the ClientWelcomeMessage.
    // account: the 16 bytes of the accountId; server: the procId shown to other players.
    public static void Identify(NetConnection connection, string role, byte[] account, string name, string server, double now)
    {
        lock (gate)
        {
            Player j = GetPlayer(connection, role, now);
            j.Account = account;
            j.Name = name;
            j.Server = server;
        }
    }

    // Program.cs reports it when it sends the SpawnUnitsBatch of the player's plane.
    public static void PlaneCreated(NetConnection connection, string role, int shortId, float x, float y, double now,
        string characterId, string planeClass)
    {
        lock (gate)
        {
            Player j = GetPlayer(connection, role, now);
            j.CharacterId = characterId;
            j.Class = planeClass;
            j.PlaneShortId = shortId;
            j.PlaneAlive = true;
            j.HasPosition = true;
            j.X = x;
            j.Y = y;
            j.Units[shortId] = new Unit { ShortId = shortId, Prefab = PlaneSpawn.PlayerPlanePrefabId, X = x, Y = y };
        }
    }

    // Position of the player's plane on this connection, for the enemy AI.
    // False while the player has no known position yet.
    public static bool GetPlane(long peer, out float x, out float y, out bool alive, out int shortId)
    {
        lock (gate)
        {
            if (!players.TryGetValue(peer, out var j) || !j.HasPosition)
            {
                x = 0f;
                y = 0f;
                alive = false;
                shortId = 0;
                return false;
            }

            x = j.X;
            y = j.Y;
            alive = j.PlaneAlive;
            shortId = j.PlaneShortId;
            return true;
        }
    }

    // Plane swap in the hangar (ServerRequestChangePlayerPlane): returns the live plane of this connection (id, position
    // and rotation) and marks it as out of the world. The client sends back a KillUnitsBatch with ChangePlane afterwards; since the
    // new plane already has another shortId, this becomes just a "unit died" line, with no death screen.
    // False when there is no live plane (the new plane spawns at the normal spawn point).
    public static bool RemovePlaneForSwap(NetConnection connection, out int shortId, out float x, out float y, out float rotation)
    {
        lock (gate)
        {
            shortId = 0;
            x = 0f;
            y = 0f;
            rotation = 0f;
            if (!players.TryGetValue(connection.RemoteUniqueIdentifier, out var j) || !j.PlaneAlive || !j.HasPosition) return false;

            shortId = j.PlaneShortId;
            x = j.X;
            y = j.Y;
            rotation = j.Rotation;
            j.PlaneAlive = false;
            j.Units.Remove(shortId);
            return true;
        }
    }

    // characterId of the live plane of this connection (so the server switch carries the plane). Null without a plane.
    public static string? GetPlaneCharacterId(NetConnection? connection)
    {
        if (connection == null) return null;
        lock (gate)
        {
            if (!players.TryGetValue(connection.RemoteUniqueIdentifier, out var j)) return null;
            return j.PlaneAlive ? j.CharacterId : null;
        }
    }

    // The client's language (ServerSetLocalAndTimezone), for example "en-US". Empty until it arrives.
    public static string GetLanguage(NetConnection? connection)
    {
        if (connection == null) return "";
        lock (gate)
        {
            return players.TryGetValue(connection.RemoteUniqueIdentifier, out var j) ? j.Language : "";
        }
    }

    // Program.cs reports it when the connection drops: the session summary comes out and the state is discarded.
    public static void Disconnected(NetConnection? connection, Action<string, string> log)
    {
        if (connection == null) return;

        lock (gate)
        {
            long peer = connection.RemoteUniqueIdentifier;
            if (!players.TryGetValue(peer, out var j)) return;

            players.Remove(peer);
            DamageBoost.Forget(peer);
            pendingDeaths.RemoveAll(p => p.Connection == connection);
            log("SUMMARY", j.Role + " session ended. " + SummaryText(j));
        }
    }

}
