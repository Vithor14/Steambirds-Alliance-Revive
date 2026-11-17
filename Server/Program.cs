using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Lidgren.Network;

// Values copied from the client code
const string AppId = "Steambirds-394"; // CopperConfig: Name + "-" + Version
const string ProtocolVersion = "394";  // EntityHandler.gameProtocolVersion
const string BuildVersion = "201219.1413";
const ushort MsgServerSetSessionTicket = 28035; // 0x6D83
const ushort MsgServerSendMeToTutorial = 4060;  // 0x0FDC
const ushort MsgServerSendMeToNexus = 28737;    // 0x7041: the player asked to go back to the Nexus
const ushort MsgServerSendMeThroughPortal = 43982; // 0xABCE: int portalShortId
const ushort MsgSendMarkDungeonFinished = 60494;   // 0xEC4E: entered a dungeon exit portal
const ushort MsgSendResetDungeonFinished = 60495;  // 0xEC4F: /reset_tutorial
const ushort MsgServerRequestSpawnPlayerPlane = 4940;    // 0x134C: string characterId, bool isAtHangar, GeneralID
const ushort MsgServerRequestChangePlayerPlane = 15839;  // 0x3DDF: string characterId (swap in the hangar)
const ushort MsgSlashCommand = 29486;                    // List<string> pieces (chat command)
const byte RoleNexus = 0;              // ServerRole.Nexus
const byte RoleDungeon = 2;            // ServerRole.Dungeon
const byte RoleTutorial = 3;           // ServerRole.Tutorial
const long HostIdNexus = 1;            // RegisteredHost.id of the Nexus (the same peerid of the account)
const long HostIdTutorial = 2;         // RegisteredHost.id of the tutorial
const long HostIdDungeon = 3;          // RegisteredHost.id of the dungeon server

var _lock = new object();
var clock = Stopwatch.StartNew();

// Files and folders with old Portuguese names (mapas, dados, contas, progression.json...) get their new names first.
LegacyFiles.Migrate(Directory.GetCurrentDirectory(), Log);

// Network settings (network.json, Network\): server name, who can join and the ports of this run.
ServerNetworkSettingsFile.Start(Log);
NetworkPorts ports = ServerNetworkSettingsFile.Ports;
int HttpPort = ports.Http;            // OZY_URL=http://<ip>:6443/ozy
int NexusPort = ports.Nexus;          // Nexus server (login, hangar, EULA)
int TutorialPort = ports.Tutorial;    // tutorial server
int DungeonPort = ports.Dungeon;      // dungeon server (maps\dungeon_*.json, list in dungeons.json)

// Accounts: one per player in accounts\<key>\ (account.json, inventory.json, acceptances.json), loaded at the login.
Accounts.Load(Log);

// Progression (Progression folder): reads the game's configBinary (XP tables, skills, unlocks, buffs)
// and the host's progression.json.
PilotProgression.Start(Log);

// Items (Items folder): catalog of the configBinary, items.json and the loot tables (loot_tables folder).
// Writes catalog\item_catalog.md when the game version changes. The inventories belong to each account.
InventoryHandler.Start(Log);

// Flight model of each class (planeName of the configBinary): walker and paladin fly in 8 directions and aim with the mouse,
// olfaithful always flies forward... (Planes\PlayerPlaneTemplates.cs).
PlayerPlaneTemplates.Load(Log);

// The server keeps its own addresses as 127.0.0.1; PlayerSessions.AddressFor swaps in the address that each player's game
// used to reach it (LAN, Radmin, Hamachi, internet) when sending.
var nexusAddress = new IPEndPoint(IPAddress.Loopback, NexusPort);
var dungeonAddress = new IPEndPoint(IPAddress.Loopback, DungeonPort);
var nexusTags = new[] { "nexus" };   // the "nexus" tag shows the Nexus loading screen

// Name of the tutorial the client asked for (PlayerClassConfig.basicTutorial): marked as finished together.
var requestedTutorial = Tutorial.DungeonName;

// Players who already got the buildings and the welcome message of the Nexus (Rebel City, Nexus).
var welcomedInNexus = new HashSet<long>();

void Log(string tag, string text)
{
    lock (_lock) Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [{tag}] {text}");
}

string RoleName(byte role) => role == RoleNexus ? "nexus" : role == RoleDungeon ? DungeonServer.Role : "tutorial";

// Chat text for a player.
string ChatText(NetConnection connection, string english, string portuguese) =>
    Game.GetLanguage(connection).StartsWith("pt", StringComparison.OrdinalIgnoreCase) ? portuguese : english;

// Chat message for every player of this UDP server, except "except".
void TellOthers(NetServer server, NetConnection? except, string english, string portuguese)
{
    foreach (NetConnection other in server.Connections)
    {
        if (ReferenceEquals(other, except) || other.Status != NetConnectionStatus.Connected) continue;
        if (PlayerSessions.Of(other) == null) continue;
        WorldObjects.SendChat(server, other, ChatText(other, english, portuguese));
    }
}

// Switches the client to the Nexus. With a live plane ClientSwitchToServerWithPlane goes out: the client stores the
// characterId and, at the Nexus, requests the spawn of that plane by itself (the same plane of the tutorial).
void SendToNexus(NetServer server, NetConnection connection, string reason)
{
    // Also called by the update loop (tutorial and dungeon exit): the account is the one of this connection's player.
    using Accounts.Scope accountScope = Accounts.UseFor(connection);
    string? characterId = Game.GetPlaneCharacterId(connection);
    Accounts.SavedPlane? plane = Accounts.GetPlane(characterId);
    IPEndPoint destination = PlayerSessions.AddressFor(connection, nexusAddress);

    var swap = server.CreateMessage();
    if (plane != null)
    {
        PlayerData.WriteClientSwitchToServerWithPlane(swap, destination, HostIdNexus, "Nexus", BuildVersion, nexusTags,
            Accounts.AccountId, plane.CharacterId, plane.Class, plane.Variant, plane.Level, plane.Xp);
    }
    else
    {
        PlayerData.WriteClientSwitchToServer(swap, destination, HostIdNexus, "Nexus", BuildVersion, nexusTags);
    }
    int bytes = swap.LengthBytes;
    server.SendMessage(swap, connection, NetDeliveryMethod.ReliableOrdered, 0);
    Log("NEXUS", $"{reason} -> {(plane != null ? "ClientSwitchToServerWithPlane" : "ClientSwitchToServer")} to {destination} " +
        $"({PlayerSessions.NameOf(connection)}, {bytes} bytes{(plane != null ? $", plane {plane.Class}/{plane.Variant} {plane.CharacterId}" : ", no plane")})");
}

// Switches the client to the dungeon server, with the plane, for the dungeon "type" (client terrain, e.g. DungeonBomb).
// The next connection of this account to the dungeon server gets that dungeon (DungeonServer.SetPending).
void SendToDungeon(NetServer server, NetConnection connection, string type, string reason)
{
    using Accounts.Scope accountScope = Accounts.UseFor(connection);
    DungeonServer.SetPending(Accounts.AccountIdText, type);
    string? characterId = Game.GetPlaneCharacterId(connection);
    Accounts.SavedPlane? plane = Accounts.GetPlane(characterId);
    IPEndPoint destination = PlayerSessions.AddressFor(connection, dungeonAddress);

    var swap = server.CreateMessage();
    if (plane != null)
    {
        PlayerData.WriteClientSwitchToServerWithPlane(swap, destination, HostIdDungeon, type, BuildVersion, null,
            Accounts.AccountId, plane.CharacterId, plane.Class, plane.Variant, plane.Level, plane.Xp);
    }
    else
    {
        PlayerData.WriteClientSwitchToServer(swap, destination, HostIdDungeon, type, BuildVersion);
    }
    int bytes = swap.LengthBytes;
    server.SendMessage(swap, connection, NetDeliveryMethod.ReliableOrdered, 0);
    Log("DUNGEON", $"{reason} -> {(plane != null ? "ClientSwitchToServerWithPlane" : "ClientSwitchToServer")} to {destination} " +
        $"({type}, {PlayerSessions.NameOf(connection)}, {bytes} bytes{(plane != null ? $", plane {plane.Class}/{plane.Variant}" : ", no plane")})");
}

// Creates the player's plane in the world (SpawnUnitsBatch) and tells the game state, the progression, the enemies and the
// tutorial. Used by the normal spawn (ServerRequestSpawnPlayerPlane) and by the plane swap in the hangar.
// position: null = the server's spawn point (tutorial hangar or the Rebel City eagle).
// spawnDelay: added to the clock in the spawnTime. -1 = the client creates it at once; positive = the client waits that time
// (the plane swap uses it so the old plane leaves before the new one enters).
void SpawnPlane(NetServer server, NetConnection connection, byte role, string characterId, int shortId,
    (float X, float Y, float Rot)? position, float spawnDelay, string origin)
{
    using Accounts.Scope accountScope = Accounts.UseFor(connection);
    var idBytes = PlayerData.FromBase64Url(characterId);
    if (idBytes == null)
    {
        Log("SPAWN", $"[{RoleName(role)}] invalid characterId '{characterId}', spawn cancelled ({origin})");
        return;
    }

    var record = Accounts.GetPlane(characterId);
    if (record == null)
    {
        Log("SPAWN", "characterId is not in the account, using walker/default");
        record = new Accounts.SavedPlane { CharacterId = characterId, Class = "walker", Variant = "default" };
    }

    if (!PlayerData.ClassesNetIdShort.TryGetValue(record.Class, out var netClass) ||
        !PlayerData.VariantNetIdShort.TryGetValue(record.Variant, out var netVariant))
    {
        Log("SPAWN", $"class '{record.Class}' or variant '{record.Variant}' has no netIdShort, spawn cancelled ({origin})");
        return;
    }

    var options = server.CreateMessage();
    PlayerData.WritePlaneOptions(options, netClass, netVariant, Accounts.AccountId, idBytes);

    var peerId = connection.RemoteUniqueIdentifier;
    var currentTime = (float)clock.Elapsed.TotalSeconds;
    // In the tutorial the plane spawns in the hangar (or at the last checkpoint, after a fall);
    // in the Nexus, on top of the eagle of the Rebel City square. On a plane swap, where the old plane was.
    float spawnX, spawnY, spawnRot;
    if (position != null)
    {
        (spawnX, spawnY, spawnRot) = position.Value;
    }
    else if (role == RoleTutorial)
    {
        Tutorial.SpawnPoint(connection, out spawnX, out spawnY, out spawnRot);
    }
    else if (role == RoleDungeon)
    {
        DungeonServer.SpawnPoint(connection, out spawnX, out spawnY, out spawnRot);
    }
    else
    {
        Nexus.SpawnPoint(out spawnX, out spawnY, out spawnRot);
    }

    // UnitConfig of the plane: the class's planeName (walkerClassPlane, playerPlane...). It says whether the plane flies in
    // 8 directions or always forward and whether the guns follow the mouse.
    var model = PlayerPlaneTemplates.ForClass(record.Class);

    var response = server.CreateMessage();
    PlaneSpawn.WritePlaneSpawn(response, peerId, currentTime + spawnDelay, spawnX, spawnY, spawnRot, options, shortId,
        model.Hash);
    int spawnBytes = response.LengthBytes;
    server.SendMessage(response, connection, NetDeliveryMethod.ReliableOrdered, 0);
    Log("SPAWN", $"SpawnUnitsBatch sent ({origin}; {PlayerSessions.NameOf(connection)}; {spawnBytes} bytes, options {options.LengthBytes}B, unit {shortId}, " +
        $"class={record.Class}/{netClass}, variant={record.Variant}/{netVariant}, model {model.Describe()}, time {currentTime:F2}s, peer {peerId:X16}, " +
        $"pos ({spawnX:0.#}, {spawnY:0.#}))");

    // The game state (Game) now knows which unit is this player's plane.
    Game.PlaneCreated(connection, RoleName(role), shortId, spawnX, spawnY, clock.Elapsed.TotalSeconds, characterId, record.Class);
    // Progression: statistics of nearby players (pilot skills) from now on.
    PilotProgression.PlaneSpawned(server, connection, RoleName(role), characterId, clock.Elapsed.TotalSeconds);

    if (role == RoleTutorial)
    {
        // Tutorial: the script (Tutorial) starts or continues from where it stopped.
        Tutorial.PlaneSpawned(server, connection, clock.Elapsed.TotalSeconds, Log);
    }
    else if (role == RoleDungeon)
    {
        // Dungeon: the gates spawn and the first room waits for the player (or the progress continues after a fall).
        DungeonServer.PlaneSpawned(server, connection, clock.Elapsed.TotalSeconds, Log);
    }
    else
    {
        // Nexus: encounters of enemies.json for "nexus" (if any) and the arrival message.
        Enemies.PlayerPlaneSpawned(server, connection, RoleName(role), clock.Elapsed.TotalSeconds, Log);
        if (welcomedInNexus.Add(peerId))
        {
            // Buildings of the Nexus map (once per connection: respawning the plane does not send them again).
            Nexus.SendBuildings(server, connection, clock.Elapsed.TotalSeconds, Log);
            // Carriers of the dungeons open in dungeons.json (entering takes you to the dungeon server).
            DungeonCatalog.SendNexusPortals(server, connection, clock.Elapsed.TotalSeconds, Log);
            WorldObjects.SendChat(server, connection,
                $"Welcome to {Nexus.Name}! (Map rebuilt from Open Beta videos; the stores open, but buying does not work yet.)");
            ServerNetworkSettings network = ServerNetworkSettingsFile.Current;
            if (!string.IsNullOrWhiteSpace(network.WelcomeMessage)) WorldObjects.SendChat(server, connection, network.WelcomeMessage);
            List<string> online = PlayerSessions.OnlineNames();
            if (online.Count > 1)
            {
                WorldObjects.SendChat(server, connection, ChatText(connection,
                    $"{network.ServerName}: {online.Count} players online ({string.Join(", ", online)}). /players lists them.",
                    $"{network.ServerName}: {online.Count} players online ({string.Join(", ", online)}). /players shows the list."));
            }
        }
    }
}

// Chat commands decided by the network (network.json "chatCommands") and /players. True = handled or blocked here.
bool HandleNetworkCommand(NetServer server, NetIncomingMessage msg, byte role)
{
    var start = msg.Position;
    var pieces = new List<string>();
    try
    {
        uint countPlusOne = msg.ReadVariableUInt32();
        for (uint i = 0; i + 1 < countPlusOne && i < 8 && CopperReader.BitsLeft(msg) > 0; i++)
            pieces.Add(msg.ReadString() ?? "");
    }
    catch
    {
        // Malformed command: Game logs it.
    }
    finally
    {
        msg.Position = start;
    }
    if (pieces.Count == 0) return false;

    NetConnection connection = msg.SenderConnection;
    string command = pieces[0].Trim().ToLowerInvariant();
    if (command == "/players")
    {
        List<PlayerSession> online = PlayerSessions.Online();
        string list = string.Join(", ", online.Select(s => s.Name + (s.FromThisPc ? " (host)" : "")));
        WorldObjects.SendChat(server, connection, ChatText(connection,
            $"{online.Count} player(s) online: {list}", $"{online.Count} player(s) online: {list}"));
        return true;
    }

    // /leave always works: it is the exit of the dungeon and of the tutorial.
    if (command == "/leave") return false;

    string who = ServerNetworkSettingsFile.NormalizedCommands(ServerNetworkSettingsFile.Current.ChatCommands);
    PlayerSession? session = PlayerSessions.Of(connection);
    bool allowed = who == "everyone" || (who == "host" && session != null && session.FromThisPc);
    if (allowed) return false;

    Log("CHAT", $"[{RoleName(role)}] {PlayerSessions.NameOf(connection)} tried {command}: chat commands are off for this player (network.json chatCommands = {who})");
    WorldObjects.SendChat(server, connection, ChatText(connection,
        "Chat commands are turned off on this server.", "Chat commands are turned off on this server."));
    return true;
}

// ServerSetSessionTicket: binds the connection to the login session (account and name). With no known session, an account by IP.
PlayerSession BindSession(NetConnection connection, string ticket, string user, out string howFound)
{
    PlayerSession? session = PlayerSessions.Bind(connection, ticket, out howFound);
    if (session != null) return session;

    IPAddress? remote = connection.RemoteEndPoint?.Address;
    var (key, steamId, _) = SteamTicket.AccountKey(null, null, remote?.ToString());
    bool fromThisPc = PlayerSessions.IsThisPc(remote);
    string name = PlayerNamePath.Clean(user);
    Accounts.Account account = Accounts.GetOrCreate(key, name, steamId, fromThisPc, out string note);
    if (name.Length == 0) name = account.Name;
    PrepareAccount(account);
    IPAddress address = remote == null ? IPAddress.Loopback : PlayerSessions.LocalAddressToward(remote);
    session = PlayerSessions.Create(account, name, address, remote);
    PlayerSessions.BindTo(connection, session);
    howFound = "unknown sessid: account by IP " + remote + (note.Length > 0 ? " (" + note + ")" : "");
    return session;
}

// Once per loaded account: starting rank, levels and unlocks (PilotProgression) and the inventory (read now so the log shows it).
void PrepareAccount(Accounts.Account account)
{
    if (account.Prepared) return;
    using Accounts.Scope accountScope = Accounts.Use(account);
    PilotProgression.PrepareAccount();
    InventoryStore.Read(d => 0);
    account.Prepared = true;
}

void HandleData(NetServer server, NetIncomingMessage msg, byte role)
{
    if (msg.LengthBytes < 2)
    {
        Log("UDP", $"data {msg.SenderEndPoint} {msg.LengthBytes}B (too short)");
        return;
    }

    // Everything this message does to the account (plane, XP, items, dungeons) goes to the account of this connection's player.
    using Accounts.Scope accountScope = Accounts.UseFor(msg.SenderConnection);

    var msgId = msg.ReadUInt16();

    // TimeSync.RequestDelta: ushort 1, double clientNetTime, int counter (14 bytes)
    if (msgId == 1 && msg.LengthBytes == 14)
    {
        var clientTime = msg.ReadDouble();
        var counter = msg.ReadInt32();

        var resp = server.CreateMessage();
        resp.Write((ushort)2);
        resp.Write(clientTime);
        resp.Write(counter);
        resp.Write((float)clock.Elapsed.TotalSeconds);
        server.SendMessage(resp, msg.SenderConnection, NetDeliveryMethod.Unreliable, 0);

        if (counter % 200 == 0) Log("SYNC", $"[{RoleName(role)}] RequestDelta #{counter} answered");
        return;
    }

    // ServerSetSessionTicket: ushort 28035, string sessionTicket, string clientVersion, string username
    if (msgId == MsgServerSetSessionTicket)
    {
        var session = msg.ReadString();
        var version = msg.ReadString();
        var user = msg.ReadString();
        NetConnection connection = msg.SenderConnection;

        PlayerSession player = BindSession(connection, session, user, out string howFound);
        using Accounts.Scope playerScope = Accounts.Use(player.Account);
        string name = player.Name;
        Log("HELLO", $"[{RoleName(role)}] {name} (account {player.Account.Key}, {howFound}{(player.FromThisPc ? ", this PC" : ", from " + connection.RemoteEndPoint)}), " +
            $"version={version}, client user={user}");

        // Server full: only those who were not online already count (switching servers does not count as joining again).
        int max = Math.Max(1, ServerNetworkSettingsFile.Current.MaxPlayers);
        if (role == RoleNexus && !PlayerSessions.IsOnline(player.Account, connection) && PlayerSessions.OnlineCount() > max)
        {
            PlayerSessions.Unbind(connection);
            Log("PLAYER", $"{name} refused: server full ({max} player(s))");
            connection.Disconnect($"Server full ({max} players)");
            return;
        }

        var procId = role == RoleNexus ? "revive-1" : role == RoleDungeon ? "revive-dungeon-1" : "revive-tutorial-1";
        // Dungeon server: the session is born before the welcome, which carries the dungeon's terrain (the client's TerrainConfig).
        string? dungeonTerrain = role == RoleDungeon
            ? DungeonServer.PrepareSession(server, connection, Accounts.AccountIdText, clock.Elapsed.TotalSeconds, Log)
            : null;
        // Expired buffs leave, a pilot without a portrait gets one and the unlocks are checked before the welcome.
        PilotProgression.PrepareForWelcome();
        InventoryHandler.PrepareForWelcome(RoleName(role));
        var planes = Accounts.Planes();
        var finished = Accounts.FinishedDungeons();
        var pilot = Accounts.Pilot();
        var acceptances = Accounts.Acceptances();
        var welcome = server.CreateMessage();
        PlayerData.WriteClientWelcome(welcome, Accounts.AccountId, name, acceptances, role, procId, planes, finished, pilot,
            dungeonTerrain, dungeonTerrain ?? "");
        int welcomeBytes = welcome.LengthBytes;
        int pilotXp = Accounts.Read(data => PilotProgression.ComputedPilotXp(data));
        server.SendMessage(welcome, connection, NetDeliveryMethod.ReliableOrdered, 0);
        Log("WELCOME", $"[{RoleName(role)}] ClientWelcomeMessage for {name} sent ({welcomeBytes} bytes, {acceptances.Count} acceptance(s), " +
            $"{planes.Count} plane(s) [{string.Join(", ", planes.Select(a => a.Class + " level " + a.Level))}], " +
            $"pilot rank {pilot.Level} ({pilotXp} XP, {pilot.Unlocks.Count} unlock(s)), " +
            $"finished dungeons: {(finished.Count == 0 ? "none" : string.Join(", ", finished))})");

        // Game needs to know who the player is to answer the data requests of their own account.
        Game.Identify(connection, RoleName(role), Accounts.AccountId, name, procId, clock.Elapsed.TotalSeconds);
        PilotProgression.PlayerIdentified(server, connection, RoleName(role), clock.Elapsed.TotalSeconds);
        // Ingredient wallet (ClientUpdateIngredients); the items already went in the PlayerData of the welcome.
        InventoryHandler.PlayerIdentified(server, connection, RoleName(role));

        // Floor: in the tutorial, the script's plan (rooms, corridors and gates); in the Nexus, the Rebel City.
        // Goes after the welcome, on the same reliable channel (guaranteed order).
        if (role == RoleTutorial)
        {
            Tutorial.PlayerJoined(server, connection, clock.Elapsed.TotalSeconds, Log);
        }
        else if (role == RoleDungeon)
        {
            DungeonServer.PlayerJoined(server, connection, clock.Elapsed.TotalSeconds, Log);
        }
        else
        {
            int floorBytes = Nexus.SendFloor(server, connection, out string floorSummary);
            Log("FLOOR", $"[nexus] ClientReceiveTerrainTiles sent ({floorBytes} bytes, {floorSummary})");
            // The other Nexus players find out (they do not see each other yet: each one has their own world).
            TellOthers(server, connection, $"{name} joined the server.", $"{name} joined the server.");
        }

        // ClientMapInfo of the terrain (client events: the original hints of the tutorial and the Nexus).
        var terrain = role == RoleTutorial ? Tutorial.DungeonName : role == RoleDungeon ? dungeonTerrain ?? "" : "Nexus";
        var mapInfo = server.CreateMessage();
        int infoBytes = MapInfo.WriteMessage(mapInfo, terrain, Log);
        if (infoBytes >= 0)
        {
            server.SendMessage(mapInfo, connection, NetDeliveryMethod.ReliableOrdered, 0);
            Log("MAPINFO", $"[{RoleName(role)}] ClientSetClientMapInfo of '{terrain}' sent ({infoBytes} bytes)");
        }
        return;
    }

    // ServerSendMeToTutorial: ushort 4060, string tutorialDungeon
    if (msgId == MsgServerSendMeToTutorial)
    {
        var tutorial = msg.ReadString();
        if (!string.IsNullOrWhiteSpace(tutorial)) requestedTutorial = tutorial;
        IPEndPoint destination = PlayerSessions.AddressFor(msg.SenderConnection, new IPEndPoint(IPAddress.Loopback, TutorialPort));
        Log("TUTORIAL", $"[{RoleName(role)}] {PlayerSessions.NameOf(msg.SenderConnection)} asked for '{tutorial}' -> ClientSwitchToServer to {destination}");

        var swap = server.CreateMessage();
        PlayerData.WriteClientSwitchToServer(swap, destination, HostIdTutorial, "revive-tutorial", BuildVersion);
        server.SendMessage(swap, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
        return;
    }

    // SendMarkDungeonFinished: the client entered an exit portal (Portal.TryToTeleport).
    // The dungeon goes into finishedDungeonTypes: in the Nexus the client stops asking for the tutorial.
    if (msgId == MsgSendMarkDungeonFinished)
    {
        if (role == RoleDungeon)
        {
            DungeonServer.MarkFinished(msg.SenderConnection, Log);
            return;
        }
        if (role != RoleTutorial)
        {
            Log("TUTORIAL", $"[{RoleName(role)}] SendMarkDungeonFinished outside the tutorial: ignored");
            return;
        }
        bool fresh = Accounts.MarkDungeonFinished(Tutorial.DungeonName);
        if (requestedTutorial != Tutorial.DungeonName) fresh |= Accounts.MarkDungeonFinished(requestedTutorial);
        Log("TUTORIAL", $"[tutorial] SendMarkDungeonFinished from {PlayerSessions.NameOf(msg.SenderConnection)}: " +
            $"{(fresh ? "tutorial marcado como terminado" : "o tutorial ja estava terminado")} " +
            $"(finished dungeons: {string.Join(", ", Accounts.FinishedDungeons())})");
        return;
    }

    // SendResetDungeonFinished: /reset_tutorial in the chat. The next login at the Nexus sends to the tutorial again.
    if (msgId == MsgSendResetDungeonFinished)
    {
        int removedCount = Accounts.ClearFinishedDungeons();
        Log("TUTORIAL", $"[{RoleName(role)}] SendResetDungeonFinished from {PlayerSessions.NameOf(msg.SenderConnection)}: {removedCount} dungeon(s) taken off the finished list");
        return;
    }

    // ServerSendMeThroughPortal(int portalShortId): ClientPortalResult and, if the portal is the tutorial's exit,
    // the switch to the Nexus with the plane.
    if (msgId == MsgServerSendMeThroughPortal)
    {
        int portal = msg.ReadInt32();

        // Nexus: dungeon carrier (DungeonCatalog) -> dungeon server with the plane.
        if (role == RoleNexus)
        {
            DungeonCatalog.Entry? entry = DungeonCatalog.NexusPortal(portal, out string portalReason);
            var response = server.CreateMessage();
            PlayerData.WriteClientPortalResult(response, portal, entry != null);
            server.SendMessage(response, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
            if (entry == null)
            {
                Log("PORTAL", $"[nexus] ServerSendMeThroughPortal({portal}): unknown portal{(portalReason.Length > 0 ? " (" + portalReason + ")" : "")} -> ClientPortalResult false");
                return;
            }
            Log("PORTAL", $"[nexus] ServerSendMeThroughPortal({portal}): carrier of {entry.Label} -> ClientPortalResult true");
            SendToDungeon(server, msg.SenderConnection, entry.Type, "[nexus] carrier of " + entry.Label);
            return;
        }

        // Dungeon: only this session's exit carrier, which leads to the Nexus.
        if (role == RoleDungeon)
        {
            bool exit = DungeonServer.IsExitPortal(msg.SenderConnection, portal);
            var response = server.CreateMessage();
            PlayerData.WriteClientPortalResult(response, portal, exit);
            server.SendMessage(response, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
            if (!exit)
            {
                Log("PORTAL", $"[dungeon] ServerSendMeThroughPortal({portal}): unknown portal -> ClientPortalResult false");
                return;
            }
            // Makes sure the mark is there even if SendMarkDungeonFinished did not arrive before.
            DungeonServer.MarkFinished(msg.SenderConnection, Log);
            Log("PORTAL", $"[dungeon] ServerSendMeThroughPortal({portal}): exit carrier -> ClientPortalResult true");
            SendToNexus(server, msg.SenderConnection, "[dungeon] dungeon exit through the carrier");
            return;
        }

        bool valid = role == RoleTutorial && Tutorial.IsExitPortal(msg.SenderConnection, portal);

        var result = server.CreateMessage();
        PlayerData.WriteClientPortalResult(result, portal, valid);
        server.SendMessage(result, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);

        if (!valid)
        {
            Log("PORTAL", $"[{RoleName(role)}] ServerSendMeThroughPortal({portal}): unknown portal -> ClientPortalResult false");
            return;
        }

        // Makes sure the mark is there even if SendMarkDungeonFinished did not arrive before.
        Accounts.MarkDungeonFinished(Tutorial.DungeonName);
        Log("PORTAL", $"[tutorial] ServerSendMeThroughPortal({portal}): exit portal -> ClientPortalResult true");
        SendToNexus(server, msg.SenderConnection, "[tutorial] tutorial exit through the portal");
        return;
    }

    // ServerSendMeToNexus: "go back to base" (the client's AskToReturnToNexus state).
    if (msgId == MsgServerSendMeToNexus)
    {
        if (role == RoleNexus)
        {
            Log("NEXUS", "[nexus] ServerSendMeToNexus in the Nexus itself: ignored");
            return;
        }
        SendToNexus(server, msg.SenderConnection, $"[{RoleName(role)}] ServerSendMeToNexus");
        return;
    }

    // ServerAcceptLegalNotice: ushort 54200, string legalNoticeId, LegalNoticeAcceptance
    if (msgId == PlayerData.MsgServerAcceptLegalNotice)
    {
        var legalId = msg.ReadString();
        var acceptance = new LegalAcceptance
        {
            Ticks = msg.ReadInt64(),
            TextHash = msg.ReadInt32(),
            Locale = msg.ReadString(),
            BuildVersion = msg.ReadString(),
            BuildCommit = msg.ReadString()
        };
        // Each player accepts the terms on their own account (accounts\<key>\acceptances.json).
        Dictionary<string, LegalAcceptance> acceptances = Accounts.RecordAcceptance(legalId, acceptance);
        Log("EULA", $"{PlayerSessions.NameOf(msg.SenderConnection)} accepted '{legalId}' (locale={acceptance.Locale}, version={acceptance.BuildVersion})");

        var update = server.CreateMessage();
        PlayerData.WriteClientUpdateLegalNotices(update, acceptances);
        server.SendMessage(update, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
        Log("EULA", $"ClientUpdateLegalNoticeAcceptances sent ({acceptances.Count} acceptance(s))");
        return;
    }

    // ServerRequestCreateCharacter: ushort 54849, string classId, string variantId
    if (msgId == PlayerData.MsgServerRequestCreateCharacter)
    {
        var classId = msg.ReadString();
        var variantId = msg.ReadString();
        if (string.IsNullOrEmpty(classId)) classId = "walker";
        if (string.IsNullOrEmpty(variantId)) variantId = "default";

        // Valid 16-byte GeneralID in base64url: the client converts it to the same text.
        var characterId = PlayerData.ToBase64Url(PlayerData.NewCharacterId());
        Accounts.AddPlane(characterId, classId, variantId);
        // Inventory of the new plane with the class's starting items (Items/InventoryStore).
        InventoryHandler.PlaneCreated(characterId, classId);
        Log("PLANE", $"[{RoleName(role)}] {PlayerSessions.NameOf(msg.SenderConnection)}: ServerRequestCreateCharacter class='{classId}' variant='{variantId}' -> {characterId}");

        var response = server.CreateMessage();
        PlayerData.WriteClientAddPlane(response, Accounts.AccountId, characterId, classId, variantId);
        int responseBytes = response.LengthBytes;
        server.SendMessage(response, msg.SenderConnection, NetDeliveryMethod.ReliableOrdered, 0);
        Log("PLANE", $"ClientAddPlane sent ({responseBytes} bytes)");
        // Every hangar plane counts toward pilot XP in real time (Pilot.computedXP): checks the rank.
        PilotProgression.PlaneCreated(server, msg.SenderConnection, RoleName(role));
        return;
    }

    // ServerRequestSpawnPlayerPlane: ushort 4940, string characterId, bool isAtHangar, GeneralID (ignored)
    if (msgId == MsgServerRequestSpawnPlayerPlane)
    {
        var characterId = msg.ReadString();
        Log("SPAWN", $"[{RoleName(role)}] {PlayerSessions.NameOf(msg.SenderConnection)}: ServerRequestSpawnPlayerPlane characterId='{characterId}'");
        SpawnPlane(server, msg.SenderConnection, role, characterId, PlaneSpawn.PlayerPlaneShortId, null, -1f, "spawn");
        return;
    }

    // ServerRequestChangePlayerPlane: ushort 15839, string characterId. The player chose another plane in the hangar
    // (GameClient.StartHangarPlaneSelection): the client stops the camera and waits for the server to remove the current plane
    // (KillUnitsBatch with KillReason.ChangePlane, which does not count as a death) and create the chosen one in the same place.
    // Without an answer the player stays frozen inside the hangar.
    if (msgId == MsgServerRequestChangePlayerPlane)
    {
        var characterId = msg.ReadString();
        var connection = msg.SenderConnection;
        var chosen = Accounts.GetPlane(characterId);
        if (chosen == null)
            Log("PLANE", $"[{RoleName(role)}] ServerRequestChangePlayerPlane: '{characterId}' is not in the account (the plane spawns as walker)");

        int newShortId = PlaneSpawn.PlayerPlaneShortId;
        (float X, float Y, float Rot)? place = null;
        float delay = -1f;
        if (Game.RemovePlaneForSwap(connection, out int old, out float x, out float y, out float rot))
        {
            var exit = server.CreateMessage();
            PlaneSpawn.WritePlaneSwap(exit, (float)clock.Elapsed.TotalSeconds - 1f, old, x, y, rot);
            server.SendMessage(exit, connection, NetDeliveryMethod.ReliableOrdered, 0);
            newShortId = PlaneSpawn.NextPlaneShortId(old);
            place = (x, y, rot);
            // The new plane comes in a little later: so the client removes the old one in one frame and creates the new one in another.
            delay = 0.25f;
        }
        // Shared cargo: the cargo copy of the plane chosen on the client may be old.
        InventoryHandler.PlaneChanged(server, connection, characterId);
        Log("PLANE", $"[{RoleName(role)}] ServerRequestChangePlayerPlane -> " +
            (chosen != null ? $"{chosen.Class}/{chosen.Variant} level {chosen.Level}" : characterId) +
            (place != null ? $": current plane left (ChangePlane), the new one enters as unit {newShortId} in the same place" : ": no live plane, spawns at the normal point"));
        SpawnPlane(server, connection, role, characterId, newShortId, place, delay, "swap in the hangar");
        return;
    }

    // Chat commands: network.json decides who can use them; /players lists who is online.
    if (msgId == MsgSlashCommand && HandleNetworkCommand(server, msg, role))
        return;

    // Items: inventory (move, equip, drop, recycle, consume, sort), loot pickup and the Workshop (reforge).
    if (InventoryHandler.Handle(server, msg, msgId, RoleName(role), clock.Elapsed.TotalSeconds, Log))
        return;

    // Pilot progression: skills, loadouts, portrait, buffs, career rewards and skill reports.
    if (ProgressionHandler.Handle(server, msg, msgId, RoleName(role), clock.Elapsed.TotalSeconds, Log))
        return;

    // All the other messages of the client's catalog: shots, player input, skills, store, RPCs,
    // unit batches... Game keeps the state, answers what needs an answer and logs the rest.
    if (Game.Handle(server, msg, msgId, RoleName(role), clock.Elapsed.TotalSeconds, Log))
        return;

    // ID that does not exist in the catalog (the client's GeneratedDispatcher): shows the raw bytes.
    var rest = msg.ReadBytes(msg.LengthBytes - 2);
    MessageLog.RecordUnhandled(msgId, rest, msg.LengthBytes,
        $"[{RoleName(role)}]", msg.SenderEndPoint.ToString(), t => Log("UDP", t));
}

void StartUdp(int port, byte role)
{
    var cfg = new NetPeerConfiguration(AppId) { Port = port, MaximumConnections = 128 };
    cfg.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
    cfg.EnableMessageType(NetIncomingMessageType.StatusChanged);
    cfg.EnableMessageType(NetIncomingMessageType.Data);
    cfg.EnableMessageType(NetIncomingMessageType.DebugMessage);
    cfg.EnableMessageType(NetIncomingMessageType.WarningMessage);
    cfg.EnableMessageType(NetIncomingMessageType.ErrorMessage);

    var server = new NetServer(cfg);
    server.Start();
    Log("UDP", $"[{RoleName(role)}] listening on port {port}");

    new Thread(() =>
    {
        while (true)
        {
            NetIncomingMessage? msg;
            while ((msg = server.ReadMessage()) != null)
            {
                switch (msg.MessageType)
                {
                    case NetIncomingMessageType.ConnectionApproval:
                    {
                        var hail = msg.ReadBytes(msg.LengthBytes);
                        IPAddress remote = PlayerSessions.Normalize(msg.SenderEndPoint.Address);
                        // network.json "allowOtherPlayers": false -> only this PC's game gets in.
                        if (!ServerNetworkSettingsFile.OpenToOtherPcs && !PlayerSessions.IsThisPc(remote))
                        {
                            Log("UDP", $"[{RoleName(role)}] connection from {msg.SenderEndPoint} refused: network.json allowOtherPlayers = false");
                            msg.SenderConnection.Deny("This server only accepts players from its own PC");
                            break;
                        }
                        Log("UDP", $"[{RoleName(role)}] approval from {msg.SenderEndPoint} hail={Convert.ToHexString(hail)}");
                        var response = server.CreateMessage();
                        response.Write(ProtocolVersion);
                        msg.SenderConnection.Approve(response);
                        break;
                    }
                    case NetIncomingMessageType.StatusChanged:
                    {
                        var status = (NetConnectionStatus)msg.ReadByte();
                        Log("UDP", $"[{RoleName(role)}] status {msg.SenderEndPoint}: {status} ({msg.ReadString()})");

                        // Connection closed: the session summary comes out and the player's state is discarded.
                        if (status == NetConnectionStatus.Disconnected)
                        {
                            var connection = msg.SenderConnection;
                            PlayerSession? whoLeft = PlayerSessions.Of(connection);
                            using (Accounts.UseFor(connection))
                            {
                                Game.Disconnected(connection, Log);
                                Enemies.Disconnected(connection, Log);
                                Tutorial.Disconnected(connection, Log);
                                DungeonServer.Disconnected(connection, Log);
                                PilotProgression.Disconnected(connection);
                                LootDrops.Disconnected(connection);
                            }
                            PlayerSessions.Unbind(connection);
                            // Only the Nexus thread touches this set.
                            if (role == RoleNexus && connection != null)
                                welcomedInNexus.Remove(connection.RemoteUniqueIdentifier);
                            if (whoLeft != null)
                            {
                                bool stillOnline = PlayerSessions.IsOnline(whoLeft.Account, null);
                                Log("PLAYER", $"{whoLeft.Name} left the {RoleName(role)}" +
                                    (stillOnline ? " (still online on another server)" : $"; {PlayerSessions.OnlineCount()} player(s) online"));
                                if (!stillOnline && role == RoleNexus)
                                    TellOthers(server, connection, $"{whoLeft.Name} left the server.", $"{whoLeft.Name} left the server.");
                            }
                        }
                        break;
                    }
                    case NetIncomingMessageType.Data:
                    {
                        // An error in a handler becomes a log line; the UDP thread keeps going.
                        var received = msg;
                        MessageLog.RunSafe($"data from {received.SenderEndPoint} [{RoleName(role)}]",
                            () => HandleData(server, received, role), t => Log("ERROR", t));
                        break;
                    }
                    case NetIncomingMessageType.DebugMessage:
                    case NetIncomingMessageType.WarningMessage:
                    case NetIncomingMessageType.ErrorMessage:
                        Log("UDP", $"{msg.MessageType}: {msg.ReadString()}");
                        break;
                }
                server.Recycle(msg);
            }

            // Enemies of this server: AI, shots, waves and UpdateUnitsBatch (20 times per second).
            Enemies.Update(server, RoleName(role), clock.Elapsed.TotalSeconds, Log);

            // Tutorial script: rooms, gates, boss, crates and the portal to the Nexus.
            if (role == RoleTutorial)
                Tutorial.Update(server, clock.Elapsed.TotalSeconds, Log);

            // Dungeon: rooms, gates, bosses, exit (DungeonServer).
            if (role == RoleDungeon)
                DungeonServer.Update(server, clock.Elapsed.TotalSeconds, Log);

            // Progression: players near each plane (ClientReceiveReliableStats), once per second.
            PilotProgression.Update(server, clock.Elapsed.TotalSeconds);

            // Loot bags whose time ran out leave the world.
            LootDrops.Update(server, clock.Elapsed.TotalSeconds);

            Thread.Sleep(5);
        }
    }) { IsBackground = true }.Start();
}

// ================================================================== HTTP (login)

static string? JsonText(JsonElement root, string name) =>
    root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

// Address of this server as the player's game knows it: the host of the OZY_URL it used (typed IP, found in the search
// or "localhost"). It is the one that goes in the login "procs" and in every server switch.
IPAddress ServerAddressForClient(HttpContext ctx)
{
    string host = ctx.Request.Host.Host.Trim('[', ']');
    if (IPAddress.TryParse(host, out IPAddress? ip))
    {
        ip = PlayerSessions.Normalize(ip);
        if (ip.AddressFamily == AddressFamily.InterNetwork && !ip.Equals(IPAddress.Any)) return ip;
        if (IPAddress.IsLoopback(ip)) return IPAddress.Loopback;
    }
    else if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
    {
        return IPAddress.Loopback;
    }
    else if (host.Length > 0)
    {
        try
        {
            IPAddress? resolved = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (resolved != null) return resolved;
        }
        catch
        {
        }
    }
    IPAddress? local = ctx.Connection.LocalIpAddress;
    if (local != null)
    {
        local = PlayerSessions.Normalize(local);
        if (local.AddressFamily == AddressFamily.InterNetwork && !local.Equals(IPAddress.Any)) return local;
    }
    IPAddress? client = ctx.Connection.RemoteIpAddress;
    return client != null ? PlayerSessions.LocalAddressToward(client) : IPAddress.Loopback;
}

// Ozy account answer: the Nexus proc points to the address the game used to reach it.
object AccountResponse(PlayerSession session)
{
    ServerNetworkSettings network = ServerNetworkSettingsFile.Current;
    return new
    {
        username = session.Name,
        userid = session.Account.IdText,
        sessid = session.Ticket,
        procs = new[]
        {
            new
            {
                ips = new[] { session.ServerAddress.ToString() },
                port = NexusPort,
                procid = "revive-1",
                role = "Nexus",
                scenario = "",
                meta = "",
                region = "UN.UN",
                procname = network.ServerName,
                started_by = "",
                players_connected = PlayerSessions.OnlineCount(),
                players_family = -1,
                players_max = Math.Max(1, network.MaxPlayers),
                is_closing = false,
                is_ready = true,
                protocol_version = ProtocolVersion,
                peerid = "1"
            }
        }
    };
}

// POST /ozy/user/login: who the player is (Steam ID from the ticket), their account, their name and a new session.
object Join(HttpContext ctx, string body, string? launcherName)
{
    string? steamTicket = null, guid = null, bodyUser = null;
    try
    {
        using var doc = JsonDocument.Parse(body);
        steamTicket = JsonText(doc.RootElement, "steam_ticket");
        guid = JsonText(doc.RootElement, "session_guid");
        bodyUser = JsonText(doc.RootElement, "username");
    }
    catch
    {
        // Empty or invalid body: account by IP.
    }

    IPAddress? client = ctx.Connection.RemoteIpAddress is IPAddress remote ? PlayerSessions.Normalize(remote) : null;
    var (key, steamId, how) = SteamTicket.AccountKey(steamTicket, guid, client?.ToString());
    bool fromThisPc = PlayerSessions.IsThisPc(client);
    string name = PlayerNamePath.Clean(launcherName ?? bodyUser);

    Accounts.Account account = Accounts.GetOrCreate(key, name, steamId, fromThisPc, out string note);
    if (name.Length == 0)
    {
        // No name in the launcher (game opened from outside): the name saved in the account, or "Pilot" + the end of the Steam ID.
        name = account.SavedName.Length > 0 ? account.SavedName
            : "Pilot " + (steamId != null && steamId.Length >= 4 ? steamId.Substring(steamId.Length - 4) : key.Substring(Math.Max(0, key.Length - 4)));
        Accounts.GetOrCreate(key, name, steamId, false, out _);
    }
    PrepareAccount(account);

    IPAddress serverAddress = ServerAddressForClient(ctx);
    PlayerSession session = PlayerSessions.Create(account, name, serverAddress, client);
    Log("LOGIN", $"{name} ({how}{(fromThisPc ? ", this PC" : ", from " + client)}) -> account {key}" +
        $"{(note.Length > 0 ? " (" + note + ")" : "")}, Nexus em {serverAddress}:{NexusPort}" +
        $"{(launcherName != null ? ", nome do launcher" : "")}");
    return AccountResponse(session);
}

// /ozy/user/self and /ozy/user/queue_check: the account of the X-Session header's session (or the last login of the same IP).
object SessionAccount(HttpContext ctx, string body, string? launcherName)
{
    string ticket = ctx.Request.Headers["X-Session"].ToString();
    IPAddress? client = ctx.Connection.RemoteIpAddress is IPAddress remote ? PlayerSessions.Normalize(remote) : null;
    PlayerSession? session = PlayerSessions.ByTicket(ticket) ?? PlayerSessions.LatestFrom(client);
    return session != null ? AccountResponse(session) : Join(ctx, body, launcherName);
}

// Server info for the launchers (GET /revive/info and the answer of the server search).
Dictionary<string, object?> ServerInfo()
{
    ServerNetworkSettings network = ServerNetworkSettingsFile.Current;
    List<string> names = PlayerSessions.OnlineNames();
    return new Dictionary<string, object?>
    {
        ["game"] = "steambirds-revive",
        ["infoVersion"] = 1,
        ["name"] = network.ServerName,
        ["build"] = BuildVersion,
        ["protocol"] = ProtocolVersion,
        ["players"] = names.Count,
        ["maxPlayers"] = Math.Max(1, network.MaxPlayers),
        ["playerNames"] = names,
        ["httpPort"] = HttpPort,
        ["nexusPort"] = NexusPort,
        ["openToOtherPcs"] = ServerNetworkSettingsFile.OpenToOtherPcs,
        ["acceptsPlayerName"] = true,
        ["accountsPerPlayer"] = true
    };
}

// Slot for a client upload: the file name joins the kind (errorlog, bug, death) and the time.
const string UploadPrefix = "/ozy/user_uploads/put/";
const string UploadsFolder = "client-uploads";
object UploadSlot(HttpContext ctx, string body)
{
    var type = "upload";
    try
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("path", out var p) && p.GetString() is { Length: > 0 } s) type = s;
    }
    catch { }
    type = new string(type.Where(char.IsLetterOrDigit).ToArray());
    if (type.Length == 0) type = "upload";
    var id = $"{type}{DateTime.Now:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";
    // The same address the game used to reach it (another PC does not reach the server's localhost).
    var baseUrl = $"http://{ctx.Request.Host}";
    return new
    {
        upload_url = $"{baseUrl}{UploadPrefix}{id}",
        finish_url = $"{baseUrl}/ozy/user_uploads/finish/{id}",
        download_url = $"{baseUrl}/{UploadsFolder}/{id}",
        uploadid = id
    };
}
string UploadExtension(string? mimeType) => (mimeType ?? "") switch
{
    var t when t.StartsWith("image/gif") => ".gif",
    var t when t.StartsWith("image/png") => ".png",
    var t when t.StartsWith("image/jpeg") => ".jpg",
    _ => ".txt"
};

var builder = WebApplication.CreateBuilder(args);
// allowOtherPlayers: listens on all the PC's networks (LAN, Radmin, Hamachi); otherwise, only on the PC itself.
builder.WebHost.UseUrls(ServerNetworkSettingsFile.OpenToOtherPcs ? $"http://*:{HttpPort}" : $"http://localhost:{HttpPort}");
var app = builder.Build();

app.MapFallback(async ctx =>
{
    var req = ctx.Request;

    // Player name chosen in the launcher: OZY_URL = http://<ip>:<port>/n/<name in hex>/ozy (Network/PlayerNamePath).
    var httpPath = PlayerNamePath.Split(req.Path.Value ?? "", out string? launcherName);

    // Client uploads (the error log the game sends by itself when something breaks, and the bug/death
    // GIFs): the client asks for a slot at /ozy/user_uploads/upload, PUTs the file to the
    // upload_url and then POSTs to the finish_url. Without a valid URL "Curl error 3: malformed" showed up.
    // The file goes to the client-uploads folder, without passing through the server log.
    if (httpPath.StartsWith(UploadPrefix))
    {
        var name = new string(httpPath.Substring(UploadPrefix.Length).Where(char.IsLetterOrDigit).ToArray());
        if (name.Length == 0) name = "upload";
        Directory.CreateDirectory(UploadsFolder);
        var destination = Path.Combine(UploadsFolder, name + UploadExtension(req.ContentType));
        await using (var fileName = File.Create(destination)) await req.Body.CopyToAsync(fileName);
        Log("HTTP", $"client upload saved at {Path.GetFullPath(destination)} ({new FileInfo(destination).Length} bytes)");
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{}");
        return;
    }

    // Server info for the launcher (Connect by IP and the server list).
    if (httpPath == "/revive/info")
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(ServerInfo()));
        return;
    }

    using var reader = new StreamReader(req.Body);
    var body = await reader.ReadToEndAsync();
    // Tickets and passwords come out redacted in the log.
    Log("HTTP", $"{req.Method} {httpPath}{req.QueryString} from {ctx.Connection.RemoteIpAddress} body={MessageLog.Redact(body)}");

    object response = httpPath switch
    {
        "/ozy/maintenance_info" => new { is_active = false, is_planned = false, timestamp_planned = 0, message = "" },
        "/ozy/alive" => new { ok = true },
        "/ozy/user/login" => Join(ctx, body, launcherName),
        "/ozy/user/self" or "/ozy/user/queue_check" => SessionAccount(ctx, body, launcherName),
        "/ozy/user_uploads/upload" => UploadSlot(ctx, body),
        _ => new { }
    };

    ctx.Response.ContentType = "application/json";
    await ctx.Response.WriteAsync(JsonSerializer.Serialize(response));
});

// Maps ("maps" folder): the Nexus's now, the tutorial's at its Start. Without them the server stops here with the reason.
Nexus.Start(Log);

// Tutorial script (default types and the exit portal's destination), then the unit catalog
// (BepInEx\units_config.txt) and enemies.json: both are reread by themselves when they change.
Tutorial.Start(nexusAddress, HostIdNexus, BuildVersion, nexusTags, Log);
// Tutorial exit without the carrier (automatic exit and /leave): as the exit portal would do.
Tutorial.ExitToNexus = (server, connection, reason) =>
{
    using Accounts.Scope accountScope = Accounts.UseFor(connection);
    Accounts.MarkDungeonFinished(Tutorial.DungeonName);
    if (requestedTutorial != Tutorial.DungeonName) Accounts.MarkDungeonFinished(requestedTutorial);
    SendToNexus(server, connection, reason);
};
Enemies.Start(Log);

// Dungeon server: dungeons.json (which dungeons, which map file, carriers in the Nexus) and the links
// going (Nexus carrier and /dungeon) and coming back (exit carrier, /leave and automatic exit).
DungeonServer.Start(dungeonAddress, HostIdDungeon, nexusAddress, HostIdNexus, BuildVersion, nexusTags, Log);
DungeonServer.ReturnToNexus = (server, connection, reason) => SendToNexus(server, connection, reason);
DungeonServer.SwitchToDungeon = (server, connection, type, reason) => SendToDungeon(server, connection, type, reason);

StartUdp(NexusPort, RoleNexus);
StartUdp(TutorialPort, RoleTutorial);
StartUdp(DungeonPort, RoleDungeon);

// Server search of the launchers (local network and VPNs): answers on the UDP port searchPort.
LanDiscovery.Start(ports.Search, ServerInfo, Log);

if (ServerNetworkSettingsFile.OpenToOtherPcs)
{
    List<IPAddress> addresses = PlayerSessions.LocalAddresses().Where(a => !IPAddress.IsLoopback(a)).ToList();
    Log("NETWORK", "other PCs join through: " + (addresses.Count == 0 ? "(no network found)" : string.Join(", ", addresses.Select(a => a + ":" + HttpPort))) +
        " (in the launcher: server list or Connect by IP)");
}
Log("HTTP", $"listening on http://{(ServerNetworkSettingsFile.OpenToOtherPcs ? "*" : "localhost")}:{HttpPort} (use OZY_URL=http://localhost:{HttpPort}/ozy)");
app.Run();
