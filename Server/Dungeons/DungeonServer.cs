using System.Globalization;
using System.Net;
using Lidgren.Network;
using Info = MapInfo;

// Dungeon server (ServerRole.Dungeon, its own UDP port): runs any map file of the maps folder that has the dungeon
// sections (see maps\README.md and DungeonMapData). Same model as the walker tutorial: each connection gets its
// own session (its own copy of the dungeon), but nothing here is written for one dungeon in particular.
//
// What the client already supports and the server now does for dungeons:
//  - the welcome with the dungeon terrain (TerrainConfig of the client: territory, light, music) and the scenario, so
//    the HUD location label shows the dungeon name;
//  - the floor and walls of the map (ClientReceiveTerrainTiles) and the floor signs (ClientMapInfo events, also for
//    terrains that have no ClientMapInfo in the client);
//  - destroyable walls ("isWall" units) as gates: barricades the player shoots, locked doors that open by a condition
//    ("opensWhen": room cleared, unit type dead) and doors that close behind the player in boss rooms;
//  - rooms whose enemies wait in place until the player comes in, waves, minibosses and bosses with phases (weapons,
//    shields with a visual effect, summons), periodic summons, bosses that come back (sequences: Phoenix eggs,
//    Giant Egg -> Poison God), wrecks ("*DEATH" units) and boss barks (ClientShowUnitChat);
//  - treasure chests and the DPS chest (Big Treasure);
//  - checkpoints (where the plane is born again after a fall) and the exit carrier (Portal with the "dungeon exit"
//    flag): entering it the client sends SendMarkDungeonFinished (the dungeon goes to finishedDungeonTypes) and the
//    server takes the player back to the Nexus with the same plane.
//
// Several players share one instance: entering a dungeon type joins the running instance of that type (unless it is already
// finished), everybody sees the same units, gates and rooms and each other (PlayerRelay). When the last player leaves,
// the instance is discarded, so the next entry starts the dungeon from scratch.
//
// Not yet: keys being used up, dungeon modifiers, special scripts
// (The Dig drill, The Vaults key, Temple of Bast altar curses, ambushes) and the original state machines of the units.
//
// Chat commands: /dungeons, /dungeon X (anywhere); inside a dungeon /room [N], /boss, /leave, /restart, /dungeonhelp.
public static partial class DungeonServer
{
    public const string Role = "dungeon";

    private const double TickInterval = 0.1;
    private const float SpawnAheadTiles = 3.0f;      // a room is built when the player is this close (in tiles)
    private const float WakeMargin = -0.1f;          // the room wakes up a little before the player is fully inside
    private const float CloseBehindMargin = 0.75f;   // doors close behind only when the player is well inside
    private const double WreckSeconds = 6.0;
    private const double PortalDelay = 2.5;
    private const float SummonDistance = 5f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Random random = new Random();

    // ------------------------------------------------------------------ links set by Program.cs

    // Back to the Nexus with the same plane (and the reason, for the log).
    public static Action<NetServer, NetConnection, string>? ReturnToNexus;
    // To the dungeon server for this dungeon type (from the Nexus, or from a dungeon to another one).
    public static Action<NetServer, NetConnection, string, string>? SwitchToDungeon;

    private static IPEndPoint nexusEndpoint = new IPEndPoint(IPAddress.Loopback, 1111);
    private static long nexusHostId = 1;
    private static string gameVersion = "201219.1413";
    private static string[] nexusTags = { "nexus" };
    private static Action<string, string> logger = (_, _) => { };

    // ------------------------------------------------------------------ state

    private sealed class RoomState
    {
        public string Name = "";
        public int Index;
        public Ground.Rect Area;
        public DungeonMapData.EncounterDef Encounter = null!;
        public bool Spawned;
        public bool Awake;
        public bool Cleared;
        public bool Closed;
        public int Wave = -1;            // last wave that came in
        public double WaveAt = -1;       // when the next wave comes (-1 = none scheduled)

        public string Group => "room:" + Name;     // enemies that must die to clear the room
        public string Extra => "extra:" + Name;    // chests, walls, invulnerable things, wrecks
        public bool HasWaves => Encounter.Waves != null && Encounter.Waves.Count > 0;
        public bool WavesDone => !HasWaves || (Wave >= Encounter.Waves!.Count - 1 && WaveAt < 0);
    }

    private sealed class GateState
    {
        public DungeonMapData.GateDef Def = null!;
        public int Index;
        public bool Side;
        public readonly List<(float X, float Y, int Id)> Pieces = new List<(float X, float Y, int Id)>();
        public bool Open;
        public bool ClosedBehind;
        public string ReopenRoom = "";

        public string Group => "gate:" + Index.ToString(CultureInfo.InvariantCulture);
        public float B => Side ? (Def.SjFrom + Def.SjTo + 1) * 0.5f / Ground.WallsPerCell : (Def.Sj + 0.5f) / Ground.WallsPerCell;
    }

    private sealed class Tracked
    {
        public int Id;
        public string Type = "";
        public RoomState Room = null!;
        public float X;
        public float Y;
        public double SpawnedAt;
        public double FirstHitAt = -1;
        public double NextSummonAt;
        public int SummonedBy;
        public string SequenceRoot = "";
        public int SequenceIndex = -1;
        public bool PhaseBarked;
        public bool Gone;
        public bool Removed;            // taken away by the dungeon itself (not a death)
    }

    private sealed class PendingAction
    {
        public double When;
        public string Name = "";
        public Action<double> Act = _ => { };
    }

    private sealed class Session
    {
        // Players in the instance. Peer and Connection are the first of them (they change if that player leaves).
        public readonly List<NetConnection> Members = new List<NetConnection>();
        // Players that already received the units, gates and objects that exist (a late joiner gets them once).
        public readonly HashSet<long> Synced = new HashSet<long>();
        public string Id = "";
        public long Peer;
        public NetServer Server = null!;
        public NetConnection Connection = null!;
        public DungeonCatalog.Entry Entry = null!;
        public Maps.Map? Map;
        public DungeonMapData.Loaded? Data;
        public Ground.FloorPlan? Plant;
        public string LoadError = "";

        public readonly List<RoomState> Rooms = new List<RoomState>();
        public readonly Dictionary<string, RoomState> RoomsByName = new Dictionary<string, RoomState>(StringComparer.OrdinalIgnoreCase);
        public readonly List<GateState> Gates = new List<GateState>();
        public readonly Dictionary<int, Tracked> Units = new Dictionary<int, Tracked>();
        public readonly HashSet<string> DeadTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<(RoomState Room, DungeonMapData.PlacedUnits Units)> Deferred = new List<(RoomState, DungeonMapData.PlacedUnits)>();
        public readonly List<PendingAction> Pending = new List<PendingAction>();
        public readonly List<(int Id, float X, float Y)> Objects = new List<(int Id, float X, float Y)>();

        public bool Started;
        public bool Finished;
        public bool Left;
        public int Checkpoint;
        public double NextTick;
        public double StartedAt;
        public int ExitPortal;
        public int Kills;
    }

    // One entry per player: an instance with several players appears once for each of them.
    private static readonly Dictionary<long, Session> sessions = new Dictionary<long, Session>();
    private static int nextSessionNumber = 1;
    private static readonly Dictionary<string, string> pendingByAccount = new Dictionary<string, string>(StringComparer.Ordinal);
    private static readonly HashSet<string> mapInfoTerrains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static int tickErrors;

    // ================================================================== public entry points

    // Once, when the server starts: addresses, the catalog (dungeons.json) and the ClientMapInfo of each dungeon terrain.
    public static void Start(IPEndPoint dungeonEndpoint, long dungeonHostId, IPEndPoint nexus, long nexusId, string version,
        string[] tagsOfNexus, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            nexusEndpoint = nexus;
            nexusHostId = nexusId;
            gameVersion = version;
            nexusTags = tagsOfNexus;
            logger = log;
        }
        DungeonCatalog.Start(dungeonEndpoint, dungeonHostId, version, log);
        lock (Game.Lock) RegisterMapInfos();
        log("DUNGEON", "dungeon server ready on " + dungeonEndpoint + "; dungeons: " +
            string.Join("; ", DungeonCatalog.All().Select(DungeonCatalog.Describe)));
    }

    // The Nexus is sending this account to a dungeon: the next connection of the account to the dungeon server gets it.
    public static void SetPending(string accountKey, string dungeonType)
    {
        lock (Game.Lock) pendingByAccount[accountKey] = dungeonType;
    }

    // ServerSetSessionTicket on the dungeon server, before the welcome: new session and the terrain for the welcome.
    public static string PrepareSession(NetServer server, NetConnection connection, string accountKey, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            pendingByAccount.TryGetValue(accountKey, out string? wanted);
            pendingByAccount.Remove(accountKey);
            DungeonCatalog.Entry entry = DungeonCatalog.Find(wanted) ?? DungeonCatalog.FirstOpen() ?? DungeonCatalog.All().FirstOrDefault() ??
                new DungeonCatalog.Entry { Type = "DungeonSneezy", Map = "dungeon_sneezy", Name = "Iron Lair" };
            if (wanted == null)
                log("DUNGEON", "[dungeon] connection with no requested dungeon (no carrier and no /dungeon): goes " + entry.Label);

            RegisterMapInfos();

            // Same dungeon already running with players in it: join it (a finished one is about to close, so it is not joined).
            Session? running = sessions.Values.Distinct().FirstOrDefault(x =>
                x.Server == server && x.Map != null && !x.Finished && !x.Left &&
                string.Equals(x.Entry.Type, entry.Type, StringComparison.OrdinalIgnoreCase) &&
                x.Members.Any(c => c.Status == NetConnectionStatus.Connected));
            if (running != null)
            {
                running.Members.Add(connection);
                sessions[connection.RemoteUniqueIdentifier] = running;
                Enemies.AddMember(server, running.Connection, connection, Role, now);
                log("DUNGEON", "[dungeon] " + PlayerSessions.NameOf(connection) + " joined the running " + entry.Label + " (" +
                    running.Members.Count + " players)");
                return entry.Type;
            }

            Session s = NewSession(server, connection, entry, now, log);
            sessions[s.Peer] = s;
            return entry.Type;
        }
    }

    // After the welcome: the floor of the map.
    public static void PlayerJoined(NetServer server, NetConnection connection, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            if (!sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? s)) return;
            if (s.Map == null || s.Plant == null)
            {
                Chat(s, "The dungeon map " + s.Entry.Label + " did not load: " + s.LoadError + ". Going back to the Nexus.");
                Schedule(s, now + 4.0, "returns because of an error", _ => LeaveToNexus(s, "dungeon map did not load", false));
                return;
            }
            NetOutgoingMessage m = server.CreateMessage();
            s.Map.WriteFloor(m, s.Plant, out string summary);
            int bytes = m.LengthBytes;
            server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
            log("FLOOR", "[dungeon] ClientReceiveTerrainTiles sent (" + bytes + " bytes, " + s.Map.Name + ": " + summary + ")");
        }
    }

    // Group of the players of this connection's dungeon instance (PlayerRelay). Null without a session.
    public static string? RoomKey(NetConnection? connection)
    {
        if (connection == null) return null;
        lock (Game.Lock)
        {
            return sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? s) ? s.Id : null;
        }
    }

    public static string? TerrainOf(NetConnection? connection)
    {
        if (connection == null) return null;
        lock (Game.Lock)
        {
            return sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? s) ? s.Entry.Type : null;
        }
    }

    // Where the plane is born: last checkpoint reached, or the "spawn" of the map.
    public static void SpawnPoint(NetConnection connection, out float x, out float y, out float rotation)
    {
        lock (Game.Lock)
        {
            x = 0f;
            y = 0f;
            rotation = 315f;
            if (!sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? s) || s.Map == null) return;
            Maps.Point p;
            if (s.Map.Checkpoints.Count > 0)
                p = s.Map.Checkpoints[Math.Clamp(s.Checkpoint, 0, s.Map.Checkpoints.Count - 1)].Point;
            else if (s.Map.HasSpawn)
                p = s.Map.Spawn;
            else
                return;
            p.World(out x, out y);
            rotation = p.Rotation;
        }
    }

    // The plane of the player was created on the dungeon server. First time: the dungeon starts.
    public static void PlaneSpawned(NetServer server, NetConnection connection, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            if (!sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? s)) return;
            s.Server = server;
            Enemies.PlayerRespawned(connection);
            if (s.Map == null || s.Data == null) return;
            if (!s.Started)
            {
                s.Synced.Add(connection.RemoteUniqueIdentifier);
                StartDungeon(s, now, log);
                return;
            }
            // A player that joined a running dungeon: the units, gate pieces and the boss counter that already exist.
            if (s.Synced.Add(connection.RemoteUniqueIdentifier))
            {
                Enemies.SendStateTo(connection, now);
                log("DUNGEON", "[dungeon] " + PlayerSessions.NameOf(connection) + " received the state of the running " + s.Entry.Label);
            }
            string checkpoint = s.Map.Checkpoints.Count > 0
                ? s.Map.Checkpoints[Math.Clamp(s.Checkpoint, 0, s.Map.Checkpoints.Count - 1)].Name
                : "spawn";
            log("DUNGEON", "[dungeon] plane respawned at checkpoint '" + checkpoint + "' of " + s.Entry.Label + " (the progress stays)");
        }
    }

    // Called by the UDP loop of the dungeon server, many times per second.
    public static void Update(NetServer server, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            foreach (Session s in sessions.Values.Distinct().ToList())
            {
                if (s.Server != server) continue;
                // A member whose connection died without a Disconnected for this session (the client reconnected).
                foreach (NetConnection dead in s.Members.Where(c => c.Status == NetConnectionStatus.Disconnected).ToList())
                {
                    if (s.Members.Count < 2) break;
                    s.Members.Remove(dead);
                    s.Synced.Remove(dead.RemoteUniqueIdentifier);
                    if (s.Connection == dead)
                    {
                        s.Connection = s.Members[0];
                        s.Peer = s.Connection.RemoteUniqueIdentifier;
                    }
                }
                if (now < s.NextTick) continue;
                s.NextTick = now + TickInterval;
                if (!s.Members.Any(c => c.Status == NetConnectionStatus.Connected)) continue;
                try
                {
                    Tick(s, now, log);
                }
                catch (Exception e)
                {
                    tickErrors++;
                    if (tickErrors <= 5 || tickErrors % 200 == 0)
                    {
                        string stack = (e.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim() ?? "";
                        log("DUNGEON", "dungeon error (" + tickErrors + "): " + e.GetType().Name + ": " + e.Message + " | " + stack);
                    }
                }
            }
        }
    }

    public static void Disconnected(NetConnection? connection, Action<string, string> log)
    {
        if (connection == null) return;
        lock (Game.Lock)
        {
            if (!sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? s)) return;
            if (!s.Members.Contains(connection)) return;   // an older connection of the same client (server switch)
            sessions.Remove(connection.RemoteUniqueIdentifier);
            s.Members.Remove(connection);
            s.Synced.Remove(connection.RemoteUniqueIdentifier);
            if (s.Members.Count > 0)
            {
                // Other players stay: the dungeon goes on with them.
                if (s.Connection == connection)
                {
                    s.Connection = s.Members[0];
                    s.Peer = s.Connection.RemoteUniqueIdentifier;
                }
                log("DUNGEON", "[dungeon] a player left " + s.Entry.Label + " (" + s.Members.Count + " left)");
                return;
            }
            // Empty: the instance is discarded (the next entry starts the dungeon from scratch).
            log("DUNGEON", "[dungeon] " + s.Entry.Label + " is empty and was reset: " + s.Rooms.Count(r => r.Cleared) + "/" + s.Rooms.Count +
                " room(s) cleared, " + s.Kills + " enemy(ies) killed" + (s.Finished ? ", dungeon completed" : ""));
        }
    }

    // ServerSendMeThroughPortal on the dungeon server: only the exit carrier of this connection takes to the Nexus.
    public static bool IsExitPortal(NetConnection connection, int shortId)
    {
        lock (Game.Lock)
        {
            return shortId != 0 && sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? s) && s.ExitPortal == shortId;
        }
    }

    // SendMarkDungeonFinished (the client entered the exit carrier) or the automatic exit: the dungeon type goes to
    // finishedDungeonTypes of the account. False if it was already there or there is no session.
    public static bool MarkFinished(NetConnection connection, Action<string, string> log)
    {
        using Accounts.Scope accountScope = Accounts.UseFor(connection);
        string type;
        lock (Game.Lock)
        {
            if (!sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? s)) return false;
            if (!s.Finished)
            {
                log("DUNGEON", "[dungeon] SendMarkDungeonFinished before the end of " + s.Entry.Label + ": ignored");
                return false;
            }
            type = s.Entry.Type;
        }
        bool added = Accounts.MarkDungeonFinished(type);
        log("DUNGEON", "[dungeon] " + type + (added ? " marked as finished in the account" : " was already finished in the account"));
        return added;
    }

}
