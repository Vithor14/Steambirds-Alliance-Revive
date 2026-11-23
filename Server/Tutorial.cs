using System.Globalization;
using System.Net;
using Lidgren.Network;
using Map = MapInfo;

// Walker tutorial (DungeonTutorialWalker, "Training Grounds"), rebuilt in the server.
//
// Base used (client version 201219.1413 + the 2019 Open Beta video, channel Iczel Gaming):
//  - the map: a straight corridor that goes up and to the left on the screen. Start room with a pillar in the middle
//    (to dodge), narrow passage, hangar with the training turret, drone anteroom, crate wall that is
//    destroyed with shots, drone corridor, gas drone ring room, supply crate corridor, crate gate
//    ("Shoot this to proceed!"), final corridor, boss gate and the arena. The measures came from the video's minimap,
//    stitched frame by frame (1 pixel of the minimap at 1080p ~ 1.1 units, checked against the walker's speed:
//    3 units/s), and from the width of the gates: 4 wall crates side to side. Start room, hangar and ring room: 6 sub-cells
//    (18 units); corridors and the passage to the hangar: 4 (12); hangar and anteroom exits: 2 (6).
//    The exact layout and the script only existed on the server: in the client's
//    configBinary (including versions 190426 and 190828) the Dungeons/dungeon_tutorial files
//    and the units' AIs exist, but empty.
//  - the texts: the floor signs are the original keys of the client's "lore" table
//    (newtutorialwalkergroundsign1..10), created by the client itself with ClientMapInfo events
//    (like the "Basic training: COMPLETE!"), and the instructor's and boss's lines are keys of the "bark" table
//    (newtutorialally1.., tutorialBossDEATH...), shown with ClientShowUnitChat (portrait
//    "sergeant" / "bosscat"). The client translates them and swaps <action:N> for the configured key.
//  - the atmosphere: around the corridor there are "empty" tiles of the tutorial's territory (no floor). Without them the
//    client filled the rest of the world with grass, water and white clouds, and the tutorial looked like daytime.
//  - the hangar: five Training Turrets fixed to the left wall fire straight at the right wall
//    (behavior "fixed"); the player crosses dodging the bursts.
//  - the enemies are in the map from the start, idle and with no target (as in the original game). Each group
//    wakes up when the player gets close, when the script says so or when one of them takes a hit. The ring of
//    gas drones waits in a circle in the middle of its room and closes around the player when they enter.
//  - the crate walls (tutorialwall0, tutorialwall, tutorialwallboss) are "isWall" units: the
//    client itself creates their wall collider. They spawn with rotation 0, aligned with the floor grid
//    (the model already comes rotated to the grid), one per sub-cell, with no floor wall underneath.
//
// Nothing in the script gets stuck on a hidden enemy: the drones are not mandatory, the ring and the crate have a
// time limit and the gates only need a good hit when they are damageable.
//
// The map (floor, walls, points, areas, script lines, gates, checkpoints and floor signs) is in
// maps\tutorial_walker.json (format in maps\README.md). Only the script lives here, which uses the map by the
// name of each thing. The file is reloaded when it changes: it applies to the next session (a player joining or /restart).
//
// Coordinates: (a, b) in tiles (see Ground); in the file, u = 12a and v = 12b. b is the advance (on the screen: up
// and to the left), a is the side. Wall sub-cells of 3 units: si = 4a (side), sj = 4b (advance).
// Chat commands: /step N, /portal, /restart, /tutorialhelp.
public static partial class Tutorial
{
    public const string DungeonName = "DungeonTutorialWalker";

    private const string Role = "tutorial";
    private const double TickInterval = 0.1;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly Random rng = new Random();

    // ------------------------------------------------------------------ map

    // maps\tutorial_walker.json
    public const string MapId = "tutorial_walker";

    // Crate walls: rotation 0, aligned with the grid (the model already comes rotated to the grid).
    private const float CrateRotation = 0f;

    // Without entering the carrier, the server takes the player to the Nexus by itself after this time.
    private const double AutoExitAt = 90.0;

    // Last map that passed the check (Validate). A new file with an error does not take the tutorial down.
    private static Maps.Map? mapValid;
    private static Action<string, string> mapLog = (_, _) => { };

    // ------------------------------------------------------------------ portal destination

    private static IPEndPoint portalDestination = new IPEndPoint(IPAddress.Loopback, 1111);
    private static long nexusId = 1;
    private static string gameVersion = "201219.1413";
    private static string[] nexusTags = { "nexus" };

    // ------------------------------------------------------------------ state

    private sealed class Piece
    {
        public int Si;
        public int Sj;
        public float X;
        public float Y;
        public int ShortId;
    }

    private sealed class Gate
    {
        public int Index;
        public string Name = "";
        public string Type = "";
        public int Sj;
        public bool Damageable;
        public bool Open;
        public readonly List<Piece> Pieces = new List<Piece>();
    }

    private sealed class PendingAction
    {
        public double When;
        public string Name = "";
        public Action<double> Action = _ => { };
    }

    // A player on the tutorial server: the plan, their gates, the script and the units.
    private sealed class Session
    {
        public long Peer;
        public NetServer Server = null!;
        public NetConnection Connection = null!;
        public Maps.Map Map = null!;
        public Ground.FloorPlan FloorPlan = null!;
        public readonly List<Gate> Gates = new List<Gate>();
        public readonly List<(int Id, float X, float Y)> Objects = new List<(int Id, float X, float Y)>();
        public readonly List<PendingAction> Pending = new List<PendingAction>();
        // The script of the map (what happens and when). Null until the plane spawns.
        public MapScriptRunner Script = null!;

        public bool Started;
        public int Checkpoint;
        public double NextTick;
        public double StartAt;

        public bool Left;
        public int Portal;
        public float PortalX;
        public float PortalY;
    }

    private static readonly Dictionary<long, Session> sessions = new Dictionary<long, Session>();
    private static int tickErrors;

    // Wired by Program.cs: marks the tutorial as finished and sends the player to the Nexus with the plane.
    // Used when the player does not enter the carrier (automatic exit) and by the /leave command.
    public static Action<NetServer, NetConnection, string>? ExitToNexus;

    // ================================================================== public entry points

    // Called once when the server starts: Nexus address (the portal's destination) and default types.
    public static void Start(IPEndPoint nexus, long hostId, string version, string[] tags, Action<string, string> log)
    {
        Maps.Map map;
        lock (Game.Lock)
        {
            portalDestination = nexus;
            nexusId = hostId;
            gameVersion = version;
            nexusTags = tags;
            mapLog = log;
            map = MapForSession();
            RegisterDefaultTypes();
            MapInfo.RegisterMap(DungeonName, BuildMapInfo);
        }
        log("TUTORIAL", "tutorial script ready: " + map.Name + " (" + map.FilePath + "; " + map.Gates.Count +
            " crate walls, " + map.Checkpoints.Count + " checkpoints), exit portal to " + nexus);
    }

    // After the tutorial server's ClientWelcomeMessage: new session and the floor.
    public static void PlayerJoined(NetServer server, NetConnection connection, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            Session sess = NewSession(server, connection, now);
            SendPlan(sess, log);
        }
    }

    // Where the plane spawns: in the start room or, after a fall, at the last checkpoint.
    public static void SpawnPoint(NetConnection connection, out float x, out float y, out float rotation)
    {
        lock (Game.Lock)
        {
            int index = 0;
            Maps.Map map;
            if (sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? sess))
            {
                map = sess.Map;
                index = Math.Clamp(sess.Checkpoint, 0, map.Checkpoints.Count - 1);
            }
            else
            {
                map = MapForSession();
            }
            Maps.Point point = map.Checkpoints[index].Point;
            point.World(out x, out y);
            rotation = point.Rotation;
        }
    }

    // Program.cs reports it when it sends the SpawnUnitsBatch of the player's plane in the tutorial.
    // First time: the script starts. After a fall: the progress stays as it was.
    public static void PlaneSpawned(NetServer server, NetConnection connection, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            if (!sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? sess))
            {
                sess = NewSession(server, connection, now);
                SendPlan(sess, log);
            }
            sess.Server = server;
            sess.Connection = connection;
            Enemies.PlayerRespawned(connection);

            if (!sess.Started)
            {
                StartTutorial(sess, now, log);
                return;
            }
            log("TUTORIAL", "[tutorial] plane respawned at checkpoint '" + sess.Map.Checkpoints[sess.Checkpoint].Name +
                "' (step " + sess.Script.Stage + ", the progress stays)");
        }
    }

    // Called by the tutorial server's UDP loop, several times per second.
    public static void Update(NetServer server, double now, Action<string, string> log)
    {
        lock (Game.Lock)
        {
            foreach (Session sess in sessions.Values)
            {
                if (sess.Server != server) continue;
                if (now < sess.NextTick) continue;
                sess.NextTick = now + TickInterval;
                if (sess.Connection.Status != NetConnectionStatus.Connected) continue;

                try
                {
                    UpdateSession(sess, now, log);
                }
                catch (Exception e)
                {
                    tickErrors++;
                    if (tickErrors <= 5 || tickErrors % 200 == 0)
                    {
                        string stack = (e.StackTrace ?? "").Split('\n').FirstOrDefault()?.Trim() ?? "";
                        log("TUTORIAL", "script error (" + tickErrors + "): " + e.GetType().Name + ": " + e.Message + " | " + stack);
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
            if (!sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? sess)) return;
            sessions.Remove(connection.RemoteUniqueIdentifier);
            log("TUTORIAL", "[tutorial] session ended at step " + sess.Script.Stage + " (" + sess.Script.StageName() + ")");
        }
    }

    // ServerSendMeThroughPortal: only the exit portal created for this connection leads to the Nexus.
    public static bool IsExitPortal(NetConnection connection, int shortId)
    {
        lock (Game.Lock)
        {
            return shortId != 0 && sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? sess) && sess.Portal == shortId;
        }
    }

    // Tutorial chat commands. Returns false if the command is not from here.
    public static bool Command(NetServer server, NetConnection? connection, string role, List<string> pieces, double now,
        Action<string, string> log)
    {
        if (connection == null || pieces.Count == 0) return false;
        string command = pieces[0].Trim().ToLowerInvariant();
        if (command != "/step" && command != "/portal" && command != "/restart" && command != "/tutorialhelp" &&
            command != "/leave") return false;

        lock (Game.Lock)
        {
            if (!string.Equals(role, Role, StringComparison.OrdinalIgnoreCase) ||
                !sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? sess))
            {
                WorldObjects.SendChat(server, connection, "That command only works inside the tutorial.");
                log("COMMAND", "[" + role + "] " + string.Join(" ", pieces) + " (outside the tutorial)");
                return true;
            }

            switch (command)
            {
                case "/tutorialhelp":
                    WorldObjects.SendChat(server, connection, "/step N: 1 drones, 2 drone ring, 3 crate, 4 boss gate, 5 portal");
                    WorldObjects.SendChat(server, connection, "/portal: creates the portal to the Nexus near you. /restart: tutorial from scratch");
                    WorldObjects.SendChat(server, connection, "/leave: after the boss, goes to the Nexus without entering the carrier");
                    WorldObjects.SendChat(server, connection, "Now: step " + sess.Script.Stage + " (" + sess.Script.StageName() + ")");
                    break;

                case "/step":
                {
                    string? arg = pieces.Count > 1 ? pieces[1].Trim() : null;
                    if (!int.TryParse(arg, NumberStyles.Integer, Inv, out int number) || number < 1 || sess.Map.Script?.Steps == null || !sess.Map.Script.Steps.ContainsKey(number.ToString(Inv)))
                    {
                        WorldObjects.SendChat(server, connection, "Use /step N (1 to " + (sess.Map.Script?.Steps?.Count ?? 0) + ").");
                        break;
                    }
                    JumpToStage(sess, number, now, log);
                    break;
                }

                case "/portal":
                    if (sess.Portal != 0)
                    {
                        WorldObjects.SendChat(server, connection, "The portal to the Nexus already exists: flying to it.");
                        WorldObjects.SendTeleport(server, connection, sess.PortalX, sess.PortalY - 4f, 0f, 0.2f);
                        break;
                    }
                    if (!Game.GetPlane(sess.Peer, out float px, out float py, out bool alive, out _) || !alive)
                    {
                        WorldObjects.SendChat(server, connection, "No plane in the air to create the portal.");
                        break;
                    }
                    Ground.ToGrid(px, py, out float pa, out float pb);
                    CreatePortal(sess, pa, pb + 0.6f, now, log, AutoExitAt);
                    WorldObjects.SendChat(server, connection, "Portal to the Nexus created in front of you.");
                    break;

                case "/restart":
                    Restart(sess, now, log);
                    break;

                case "/leave":
                    if (!sess.Script.Completed)
                    {
                        WorldObjects.SendChat(server, connection, "Finish the tutorial first (or use /step 5).");
                        break;
                    }
                    LeaveAlone(sess, "/leave no chat", log);
                    break;
            }

            log("COMMAND", "[" + role + "] " + string.Join(" ", pieces));
            return true;
        }
    }

}
