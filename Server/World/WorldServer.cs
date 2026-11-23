using System.Net;
using Lidgren.Network;

// Open world server (ServerRole.World, terrain "Basic", its own UDP port): one big map shared by every player on it.
//
// The map is built from a seed when the first player arrives (WorldGenerator) and discarded when the last one leaves. The game
// client already knows how to draw it (ClientReceiveTerrainTiles, the same message as the Nexus and the dungeons use); what the
// original server did and is rebuilt here:
//   - the map itself (WorldGenerator, WorldData: regions by elevation, disks, canyons, wall strips, floor pieces);
//   - the regions, with their name sent to the client while the plane flies over them (ReceiveCurrentSubterritoryName);
//   - the enemies, placed on spawn sites near the players only (WorldServer.Sites.cs; the types come from WorldEnemyPools);
//   - the dungeon carriers that appear from time to time (WorldServer.Carriers.cs);
//   - the way in (a portal in the Nexus, /world) and the way out (the client's "Return to Rebel City", /leave).
//
// Not yet: setpiece and event bosses, island quests, scheduled events, teleports to quest targets (see CHECKLIST.md, section 7).
public static partial class WorldServer
{
    public const string Role = "world";
    public const string Terrain = "Basic";
    public const string RelayRoom = "world";

    private const double TickInterval = 0.5;
    private const double EdgeCheckInterval = 0.1;

    // ------------------------------------------------------------------ links set by Program.cs

    // To the dungeon server for this dungeon type (a carrier of the world).
    public static Action<NetServer, NetConnection, string, string>? SwitchToDungeon;
    // To the world from anywhere (the /world command).
    public static Action<NetServer, NetConnection, string>? SwitchToWorld;
    // Back to the Nexus (a player the world cannot keep).
    public static Action<NetServer, NetConnection, string>? ReturnToNexus;

    private static IPEndPoint worldEndpoint = new IPEndPoint(IPAddress.Loopback, 1114);
    private static long worldHostId = 4;
    private static string gameVersion = "201219.1413";
    private static Action<string, string> logger = (_, _) => { };
    private static readonly Random random = new Random();

    // ------------------------------------------------------------------ state

    private sealed class Session
    {
        public NetServer Server = null!;
        public readonly List<NetConnection> Members = new List<NetConnection>();
        // Players that already got what exists in the world (units, carriers).
        public readonly HashSet<long> Synced = new HashSet<long>();
        public WorldMap? Map;
        public string LoadError = "";
        public int Seed;
        // Built before any player came in; the first player takes it over (WorldServer.Standby.cs).
        public bool Pregenerated;
        public string Signature = "";
        public double CreatedAt;
        public double NextTick;
        public double NextEdgeCheck;
        public float Radius = Ground.WorldRadius;
        // Last time each player was told they hit the edge of the world.
        public readonly Dictionary<long, double> EdgeWarned = new Dictionary<long, double>();
        public readonly Dictionary<long, double> EdgePushed = new Dictionary<long, double>();
        // When each player came in (for the world map page).
        public readonly Dictionary<long, double> JoinedAt = new Dictionary<long, double>();
        public readonly List<Site> Sites = new List<Site>();
        public readonly Dictionary<int, Carrier> Carriers = new Dictionary<int, Carrier>();
        public double NextCarrierAt;
        // Region name last sent to each player.
        public readonly Dictionary<long, string> RegionShown = new Dictionary<long, string>();
        // Players that must go back to the Nexus (the world could not keep them) and when.
        public readonly Dictionary<long, double> Returns = new Dictionary<long, double>();
        public int Kills;
        public int Spawned;
    }

    private static Session? session;
    private static int tickErrors;

    // The portal of the Nexus: one id for everybody.
    private static int nexusPortalId;

    // ================================================================== public entry points

    // Once, when the server starts (the configBinary and the enemy catalog are already loaded).
    public static void Start(IPEndPoint endpoint, long hostId, string version, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            worldEndpoint = endpoint;
            worldHostId = hostId;
            gameVersion = version;
            logger = log;
        }
        WorldSettingsFile.Start(log);
        WorldLive.WriteInactive(log);
        WorldData.Load(log);
        WorldSettings s = WorldSettingsFile.Current;
        log("WORLD", "open world server ready on " + endpoint + " (" + (s.Enabled ? "on" : "OFF in world.json") + "; terrain " + Terrain + ")");
    }

    public static bool Enabled => WorldSettingsFile.Current.Enabled;

    // True when the world can be built (data read). The reason otherwise.
    public static bool Available(out string reason)
    {
        reason = "";
        if (!Enabled)
        {
            reason = "the open world is turned off in " + WorldSettingsFile.FileName;
            return false;
        }
        if (!WorldData.Ready && !WorldData.Load(logger))
        {
            reason = WorldData.LoadError;
            return false;
        }
        return true;
    }

    // ServerSetSessionTicket on the world server, before the welcome: the player joins the world (built if nobody is in it).
    public static string PrepareSession(NetServer server, NetConnection connection, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            Session? s = session;
            if (s != null && !s.Members.Any(c => c.Status == NetConnectionStatus.Connected && c != connection))
            {
                // The world built in advance (no members yet) is the one this player gets, if world.json still asks for it.
                if (s.Pregenerated && s.Map != null && s.Members.Count == 0 && s.Signature == Signature(WorldSettingsFile.Current))
                {
                    s.Pregenerated = false;
                    s.CreatedAt = now;
                    s.NextCarrierAt = now + 20.0;
                    s.Server = server;
                    log("WORLD", "[world] the world built in advance (seed " + s.Seed + ") goes to its first player");
                }
                else
                {
                    // Nobody else is really in it (leftovers of a dropped connection, or a standby world that no longer matches): a new world.
                    DropSession(s, log, quiet: s.Pregenerated);
                    s = null;
                }
            }

            if (s == null)
            {
                s = BuildSession(server, now, log);
                session = s;
            }

            if (!s.Members.Contains(connection)) s.Members.Add(connection);
            s.JoinedAt[connection.RemoteUniqueIdentifier] = now;
            NetConnection? primary = s.Members.FirstOrDefault(c => c != connection && c.Status == NetConnectionStatus.Connected);
            if (primary != null) Enemies.AddMember(server, primary, connection, Role, now);
            log("WORLD", "[world] " + PlayerSessions.NameOf(connection) + " entered the open world (" + s.Members.Count + " player(s))");
            return Terrain;
        }
    }

    // After the welcome: the floor of the map (in chunks) and what is wrong, if anything.
    public static void PlayerJoined(NetServer server, NetConnection connection, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            Session? s = session;
            if (s == null || !s.Members.Contains(connection)) return;
            s.Server = server;
            if (s.Map == null)
            {
                WorldObjects.SendChat(server, connection, "The open world did not load: " + s.LoadError + ". Going back to the Nexus.");
                s.Returns[connection.RemoteUniqueIdentifier] = now + 4.0;
                return;
            }
            int max = WorldSettingsFile.Current.MaxPlayers;
            if (s.Members.Count > max)
            {
                WorldObjects.SendChat(server, connection, "The open world is full (" + max + " players). Going back to the Nexus.");
                s.Returns[connection.RemoteUniqueIdentifier] = now + 3.0;
            }
            (int bytes, int messages) = WorldFloor.Send(server, connection, s.Map);
            log("FLOOR", "[world] ClientReceiveTerrainTiles sent (" + messages + " message(s), " + bytes + " bytes, " + s.Map.Summary + ")");
        }
    }

    // Radius of the world this connection plays in, for the welcome message (the client sizes its tile grid with it).
    // Before the player is in a session: the one world.json asks for.
    public static float RadiusFor(NetConnection? connection)
    {
        lock (Game.Lock)
        {
            Session? s = session;
            if (s != null && connection != null && s.Members.Contains(connection)) return s.Radius;
            return WorldMap.RadiusFor(WorldSettingsFile.Current.SizeScale);
        }
    }

    // Group of the players of the world (PlayerRelay).
    public static string? RoomKey(NetConnection? connection)
    {
        if (connection == null) return null;
        lock (Game.Lock)
        {
            return session != null && session.Members.Contains(connection) ? RelayRoom : null;
        }
    }

    // Where the plane is born: the spawn point of the map, a few units apart for each player.
    public static void SpawnPoint(NetConnection connection, out float x, out float y, out float rotation)
    {
        lock (Game.Lock)
        {
            x = 0f;
            y = 0f;
            rotation = 0f;
            Session? s = session;
            if (s?.Map == null || !s.Members.Contains(connection)) return;
            x = s.Map.SpawnX;
            y = s.Map.SpawnY;
            rotation = s.Map.SpawnRotation;
            int index = s.Members.IndexOf(connection);
            if (index > 0 || s.Members.Count > 1)
            {
                double angle = random.NextDouble() * Math.PI * 2.0;
                float distance = 3f + (float)random.NextDouble() * 5f;
                float nx = x + (float)Math.Sin(angle) * distance;
                float ny = y + (float)Math.Cos(angle) * distance;
                if (s.Map.IsFreeLand(nx, ny))
                {
                    x = nx;
                    y = ny;
                }
            }
        }
    }

    // The plane of the player was created in the world: the others' enemies and carriers reach it, and its region is told.
    public static void PlaneSpawned(NetServer server, NetConnection connection, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            Session? s = session;
            if (s == null || !s.Members.Contains(connection)) return;
            s.Server = server;
            Enemies.PlayerRespawned(connection);
            if (s.Map == null) return;

            long peer = connection.RemoteUniqueIdentifier;
            s.RegionShown.Remove(peer);
            if (s.Synced.Add(peer))
            {
                // A player who comes into a running world gets the units and carriers that already exist.
                Enemies.SendStateTo(connection, now);
                foreach (Carrier carrier in s.Carriers.Values) SendCarrier(s, connection, carrier, now);
                WorldSettings settings = WorldSettingsFile.Current;
                WorldObjects.SendChat(server, connection, "Welcome to " + settings.Name + "! The farther from the center, the stronger the enemies. " +
                    "The way back is the menu's Return to Rebel City.");
                log("WORLD", "[world] " + PlayerSessions.NameOf(connection) + " received the state of the world (" +
                    s.Sites.Count(x => x.Active) + " active site(s), " + s.Carriers.Count + " carrier(s))");
            }
        }
    }

    // Called by the UDP loop of the world server, many times per second.
    public static void Update(NetServer server, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            Session? stale = session;
            stale?.Members.RemoveAll(c => c.Status == NetConnectionStatus.Disconnected);
            try
            {
                MaintainStandby(server, now, log);
            }
            catch (Exception e)
            {
                nextStandbyTry = now + 15.0;
                log("WORLD", "the world could not be prepared in advance: " + e.GetType().Name + ": " + e.Message);
            }
            Session? s = session;
            if (s == null || s.Server != server || s.Pregenerated) return;
            // The edge wall is checked ten times per second, faster than the rest of the world.
            if (now >= s.NextEdgeCheck)
            {
                s.NextEdgeCheck = now + EdgeCheckInterval;
                EnforceEdge(s, now);
            }
            if (now < s.NextTick) return;
            s.NextTick = now + TickInterval;
            if (!s.Members.Any(c => c.Status == NetConnectionStatus.Connected)) return;
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
                    log("WORLD", "world error (" + tickErrors + "): " + e.GetType().Name + ": " + e.Message + " | " + stack);
                }
            }
        }
    }

    public static void Disconnected(NetConnection? connection, Action<string, string> log)
    {
        if (connection == null) return;
        lock (Game.Lock)
        {
            Session? s = session;
            if (s == null || !s.Members.Remove(connection)) return;
            long peer = connection.RemoteUniqueIdentifier;
            s.Synced.Remove(peer);
            s.RegionShown.Remove(peer);
            s.Returns.Remove(peer);
            s.EdgePushed.Remove(peer);
            s.EdgeWarned.Remove(peer);
            s.JoinedAt.Remove(peer);
            if (s.Members.Count > 0)
            {
                log("WORLD", "[world] a player left the open world (" + s.Members.Count + " left)");
                return;
            }
            DropSession(s, log);
        }
    }

    // ================================================================== session

    private static Session BuildSession(NetServer server, double now, Action<string, string> log)
    {
        WorldSettings settings = WorldSettingsFile.Current;
        var s = new Session { Server = server, CreatedAt = now, Radius = WorldMap.RadiusFor(settings.SizeScale), Signature = Signature(settings) };
        if (!Available(out string reason))
        {
            s.LoadError = reason;
            log("WORLD", "the open world could not be built: " + reason);
            return s;
        }

        try
        {
            s.Seed = settings.Seed != 0 ? settings.Seed : random.Next(1, int.MaxValue);
            s.Map = WorldGenerator.Build(s.Seed, settings.SizeScale, settings.BiomeScale, settings.Layout, settings.LandFraction, settings.Spawn.X, settings.Spawn.Y, settings.Spawn.Rotation,
                settings.Spawn.SafeZoneRadius, log);
            WorldEnemyPools.Build(settings.Enemies.Skip, log);
            WorldLive.WriteMap(s.Map, settings.Spawn.X, settings.Spawn.Y, log);
            BuildSites(s, settings);
            s.NextCarrierAt = now + 20.0;
            log("WORLD", "[world] new world: " + s.Map.Summary + "; " + s.Sites.Count + " spawn site(s)");
        }
        catch (Exception e)
        {
            s.Map = null;
            s.LoadError = e.GetType().Name + ": " + e.Message;
            log("WORLD", "the open world could not be built: " + e + "");
        }
        return s;
    }

    // Everything of the world goes: units, carriers and the map. The next player starts a new one.
    private static void DropSession(Session s, Action<string, string> log, bool quiet = false)
    {
        if (ReferenceEquals(session, s)) session = null;
        WorldLive.WriteInactive(log);
        if (quiet) return;
        log("WORLD", "[world] the open world is empty and was discarded: " + s.Kills + " enemy site(s) cleared, " + s.Spawned +
            " enemy group(s) spawned, " + s.Carriers.Count + " carrier(s) gone");
    }

    private static List<NetConnection> Connected(Session s)
    {
        return s.Members.Where(c => c.Status == NetConnectionStatus.Connected).ToList();
    }

    private static void Chat(Session s, string text)
    {
        foreach (NetConnection member in Connected(s)) WorldObjects.SendChat(s.Server, member, text);
    }

    // Position of each live plane of the world.
    private static List<(NetConnection Connection, float X, float Y)> Players(Session s)
    {
        var list = new List<(NetConnection, float, float)>();
        foreach (NetConnection c in Connected(s))
        {
            if (Game.GetPlane(c.RemoteUniqueIdentifier, out float x, out float y, out bool alive, out _) && alive)
                list.Add((c, x, y));
        }
        return list;
    }

    // ================================================================== the way in (Nexus)

    // The portal of the open world in the Nexus (once per Nexus connection).
    public static bool SendNexusPortal(NetServer server, NetConnection connection, double now, Action<string, string> log)
    {
        WorldSettings settings = WorldSettingsFile.Current;
        if (!settings.Enabled || !settings.NexusPortal.Enabled) return false;
        if (nexusPortalId == 0) nexusPortalId = WorldObjects.NewShortId();
        Ground.ToWorld(settings.NexusPortal.U / Ground.CellSize, settings.NexusPortal.V / Ground.CellSize, out float x, out float y);
        WorldObjects.SendWorldPortal(server, connection, nexusPortalId, x, y, settings.NexusPortal.Rot, worldEndpoint, worldHostId,
            settings.Name, gameVersion, Terrain, settings.MaxPlayers, now);
        log("WORLD", "[nexus] portal of the open world (" + nexusPortalId + ") at u " + settings.NexusPortal.U + ", v " + settings.NexusPortal.V);
        return true;
    }

    // ServerSendMeThroughPortal in the Nexus: is this the portal to the world?
    public static bool IsNexusPortal(int shortId)
    {
        return shortId != 0 && shortId == nexusPortalId && Enabled;
    }
}
