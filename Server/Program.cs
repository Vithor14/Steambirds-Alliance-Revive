using System.Diagnostics;
using System.Net;
using Lidgren.Network;

// Entry point of the server: shared state, startup order and the small helpers used by the other parts of `Program`.
// Parts: Program.Transfers.cs (server switches and plane spawn), Program.Messages.cs (client messages),
// Program.Udp.cs (UDP loop), Program.Http.cs (login and the launcher's server info).

public static partial class Program
{
    // Values copied from the client code
    private const string AppId = "Steambirds-394"; // CopperConfig: Name + "-" + Version
    private const string ProtocolVersion = "394";  // EntityHandler.gameProtocolVersion
    private const string BuildVersion = "201219.1413";
    private const ushort MsgServerSetSessionTicket = 28035; // 0x6D83
    private const ushort MsgServerSendMeToTutorial = 4060;  // 0x0FDC
    private const ushort MsgServerSendMeToNexus = 28737;    // 0x7041: the player asked to go back to the Nexus
    private const ushort MsgServerSendMeThroughPortal = 43982; // 0xABCE: int portalShortId
    private const ushort MsgSendMarkDungeonFinished = 60494;   // 0xEC4E: entered a dungeon exit portal
    private const ushort MsgSendResetDungeonFinished = 60495;  // 0xEC4F: /reset_tutorial
    private const ushort MsgServerRequestSpawnPlayerPlane = 4940;    // 0x134C: string characterId, bool isAtHangar, GeneralID
    private const ushort MsgServerRequestChangePlayerPlane = 15839;  // 0x3DDF: string characterId (swap in the hangar)
    private const ushort MsgSlashCommand = 29486;                    // List<string> pieces (chat command)
    private const byte RoleNexus = 0;              // ServerRole.Nexus
    private const byte RoleWorld = 1;              // ServerRole.World (the open world, terrain "Basic")
    private const byte RoleDungeon = 2;            // ServerRole.Dungeon
    private const byte RoleTutorial = 3;           // ServerRole.Tutorial
    private const long HostIdNexus = 1;            // RegisteredHost.id of the Nexus (the same peerid of the account)
    private const long HostIdTutorial = 2;         // RegisteredHost.id of the tutorial
    private const long HostIdDungeon = 3;          // RegisteredHost.id of the dungeon server
    private const long HostIdWorld = 4;            // RegisteredHost.id of the open world server

    // Shared state of the running server. Everything here is assigned once in Main, before any thread starts.
    private static readonly object LogLock = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly string[] NexusTags = { "nexus" };   // the "nexus" tag shows the Nexus loading screen

    private static NetworkPorts Ports = null!;
    private static int HttpPort;       // OZY_URL=http://<ip>:6443/ozy
    private static int NexusPort;      // Nexus server (login, hangar, EULA)
    private static int TutorialPort;   // tutorial server
    private static int DungeonPort;    // dungeon server (maps\dungeon_*.json, list in dungeons.json)
    private static int WorldPort;      // open world server (terrain Basic, world.json)

    // The server keeps its own addresses as 127.0.0.1; PlayerSessions.AddressFor swaps in the address that each player's game
    // used to reach it (LAN, Radmin, Hamachi, internet) when sending.
    private static IPEndPoint NexusAddress = null!;
    private static IPEndPoint DungeonAddress = null!;
    private static IPEndPoint WorldAddress = null!;

    // Name of the tutorial the client asked for (PlayerClassConfig.basicTutorial): marked as finished together.
    private static string RequestedTutorial = "";

    // Players who already got the buildings and the welcome message of the Nexus (Rebel City, Nexus).
    private static readonly HashSet<long> WelcomedInNexus = new();


    public static void Main(string[] args)
    {
        // Network settings (network.json, Network\): server name, who can join and the ports of this run.
        ServerNetworkSettingsFile.Start(Log);
        Ports = ServerNetworkSettingsFile.Ports;
        HttpPort = Ports.Http;
        NexusPort = Ports.Nexus;
        TutorialPort = Ports.Tutorial;
        DungeonPort = Ports.Dungeon;
        WorldPort = Ports.World;

        // Accounts: one per player in accounts\<key>\ (account.json, inventory.json, acceptances.json), loaded at the login.
        Accounts.Load(Log);

        // Progression (Progression folder): reads the game's configBinary (XP tables, skills, unlocks, buffs)
        // and the host's progression.json.
        PilotProgression.Start(Log);

        // Items (Items folder): catalog of the configBinary, items.json and the loot tables (loot_tables folder).
        // Writes catalog\item_catalog.md when the game version changes. The inventories belong to each account.
        InventoryHandler.Start(Log);

        // Stores (Stores folder): the game's store config (configBinary), stores.json and what each account bought (accounts\<key>\store.json).
        StoreHandler.Start(Log);

        // Flight model of each class (planeName of the configBinary): walker and paladin fly in 8 directions and aim with the mouse,
        // olfaithful always flies forward... (Planes\PlayerPlaneTemplates.cs).
        PlayerPlaneTemplates.Load(Log);

        NexusAddress = new IPEndPoint(IPAddress.Loopback, NexusPort);
        DungeonAddress = new IPEndPoint(IPAddress.Loopback, DungeonPort);
        WorldAddress = new IPEndPoint(IPAddress.Loopback, WorldPort);
        RequestedTutorial = Tutorial.DungeonName;

        var builder = WebApplication.CreateBuilder(args);
        // allowOtherPlayers: listens on all the PC's networks (LAN, Radmin, Hamachi); otherwise, only on the PC itself.
        builder.WebHost.UseUrls(ServerNetworkSettingsFile.OpenToOtherPcs ? $"http://*:{HttpPort}" : $"http://localhost:{HttpPort}");
        var app = builder.Build();
        MapHttp(app);

        // Maps ("maps" folder): the Nexus's now, the tutorial's at its Start. Without them the server stops here with the reason.
        Nexus.Start(Log);

        // Tutorial script (default types and the exit portal's destination), then the unit catalog
        // (BepInEx\units_config.txt) and enemies.json: both are reread by themselves when they change.
        Tutorial.Start(NexusAddress, HostIdNexus, BuildVersion, NexusTags, Log);
        // Tutorial exit without the carrier (automatic exit and /leave): as the exit portal would do.
        Tutorial.ExitToNexus = (server, connection, reason) =>
        {
            using Accounts.Scope accountScope = Accounts.UseFor(connection);
            Accounts.MarkDungeonFinished(Tutorial.DungeonName);
            if (RequestedTutorial != Tutorial.DungeonName) Accounts.MarkDungeonFinished(RequestedTutorial);
            SendToNexus(server, connection, reason);
        };
        Enemies.Start(Log);

        // Dungeon server: dungeons.json (which dungeons, which map file, carriers in the Nexus) and the links
        // going (Nexus carrier and /dungeon) and coming back (exit carrier, /leave and automatic exit).
        DungeonServer.Start(DungeonAddress, HostIdDungeon, NexusAddress, HostIdNexus, BuildVersion, NexusTags, Log);
        DungeonServer.ReturnToNexus = (server, connection, reason) => SendToNexus(server, connection, reason);
        DungeonServer.SwitchToDungeon = (server, connection, type, reason) => SendToDungeon(server, connection, type, reason);

        // Open world (World folder): the map is built from world.json when the first player arrives; the links are the way
        // in (Nexus portal and /world), the carriers that lead to dungeons and the way back.
        WorldServer.Start(WorldAddress, HostIdWorld, BuildVersion, Log);
        WorldServer.SwitchToWorld = (server, connection, reason) => SendToWorld(server, connection, reason);
        WorldServer.SwitchToDungeon = (server, connection, type, reason) => SendToDungeon(server, connection, type, reason);
        WorldServer.ReturnToNexus = (server, connection, reason) => SendToNexus(server, connection, reason);

        // Social (Social folder): friends, flocks and the way to a friend (social.json, flocks.json, accounts\<key>\social.json).
        // "Join Server" switches between the Nexus and the open world with the same links.
        SocialHandler.Start(Log);
        SocialHandler.SwitchToNexus = (server, connection, reason) => SendToNexus(server, connection, reason);
        SocialHandler.SwitchToWorld = (server, connection, reason) => SendToWorld(server, connection, reason);

        StartUdp(NexusPort, RoleNexus);
        StartUdp(TutorialPort, RoleTutorial);
        StartUdp(DungeonPort, RoleDungeon);
        StartUdp(WorldPort, RoleWorld);

        // Server search of the launchers (local network and VPNs): answers on the UDP port searchPort.
        LanDiscovery.Start(Ports.Search, ServerInfo, Log);

        if (ServerNetworkSettingsFile.OpenToOtherPcs)
        {
            List<IPAddress> addresses = PlayerSessions.LocalAddresses().Where(a => !IPAddress.IsLoopback(a)).ToList();
            Log("NETWORK", "other PCs join through: " + (addresses.Count == 0 ? "(no network found)" : string.Join(", ", addresses.Select(a => a + ":" + HttpPort))) +
                " (in the launcher: server list or Connect by IP)");
        }
        Log("HTTP", $"listening on http://{(ServerNetworkSettingsFile.OpenToOtherPcs ? "*" : "localhost")}:{HttpPort} (use OZY_URL=http://localhost:{HttpPort}/ozy)");
        app.Run();
    }


    static void Log(string tag, string text)
    {
        lock (LogLock) Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{tag}] {text}");
    }

    static string RoleName(byte role) => role == RoleNexus ? "nexus" : role == RoleDungeon ? DungeonServer.Role : role == RoleWorld ? WorldServer.Role : "tutorial";

    // Chat message for every player of this UDP server, except "except".
    static void TellOthers(NetServer server, NetConnection? except, string text)
    {
        foreach (NetConnection other in server.Connections)
        {
            if (ReferenceEquals(other, except) || other.Status != NetConnectionStatus.Connected) continue;
            if (PlayerSessions.Of(other) == null) continue;
            WorldObjects.SendChat(server, other, text);
        }
    }
}
