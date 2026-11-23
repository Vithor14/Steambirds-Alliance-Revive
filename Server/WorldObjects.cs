using System.Net;
using Lidgren.Network;

// Scenery objects the server creates on the client (they are not enemies):
//  - floor text (prefab 7, LoreText): LoreTextEntity reads (string textId, int seed) from the options.
//    The client looks textId up in the "lore" table; if it is not found, it shows the text itself
//    (TextMachine.GetLoreText uses the key as the default). The text shows when the plane flies over it.
//    "<action:N>" becomes the configured key (TextMachine.ReplacePlaceholders; Rewired ids:
//    1 = super speed, 2 = shoot, 3 = maneuver, 6 = special attack).
//  - portal (prefab 11): Portal.CreateEntOptions = RegisteredHost, flags byte (1 dungeon portal,
//    2 on, 4 dungeon exit), dungeon type string, float start, float end.
//    When entering an exit portal the client sends SendMarkDungeonFinished and ServerSendMeThroughPortal.
//  - removal (KillUnitsBatch), plane teleport (ClientTeleportToCoordinatesAfterTime) and chat.
//  - unit speech (ClientShowUnitChat, ID 7332): a bubble over the unit, the character portrait
//    (Dialogue format) and a chat line. The key comes from the client's "bark" table (Texts/<language>/bark),
//    so the text shows in the game's language, like the original.
//
// Everything spawns with the server as authority and on the player's team (0): with another team and templateHash 0,
// EntityHandler.SpawnUnit reads unitConfig.noScribing from a null config.
public static class WorldObjects
{
    public const ushort MsgSpawnUnitsBatch = 45862;
    public const ushort MsgKillUnitsBatch = 45863;
    public const ushort MsgClientTeleportToCoordinatesAfterTime = 50687;
    public const ushort MsgClientReceiveText = 14789;     // string text, byte type (0 = ServerSays)
    public const ushort MsgClientShowUnitChat = 7332;     // GeneratedDispatcher: case 7332

    // The client's AISpeechFormat.
    public const byte SpeechBig = 1;
    public const byte SpeechNoChat = 2;
    public const byte SpeechDialog = 4;                    // portrait + bubble + chat (DialogueManager)
    public const byte SpeechNoBubble = 0x10;
    public const byte SpeechTopBanner = 0x20;

    public const int PrefabLoreText = 7;                  // prefab_table.txt: 7 LoreText
    public const int PrefabPortal = 11;                   // prefab_table.txt: 11 Portal
    public const byte ReasonFizzled = 11;                 // KillReason.Fizzled
    public const byte ReasonDestroyedByDamage = 10;        // KillReason.DestroyedFromDamage

    private const byte DungeonPortal = 1;
    private const byte PortalOn = 2;
    private const byte ExitPortal = 4;

    // Own ids, far from the plane (1000), the clouds (2000+) and the enemies (100000+).
    private static int nextShortId = 50000;

    public static int NewShortId()
    {
        return Interlocked.Increment(ref nextShortId);
    }

    // Floor text at (x, y).
    public static void SendText(NetServer server, NetConnection connection, int shortId, float x, float y,
        string text, int seed, double now)
    {
        if (connection.Status != NetConnectionStatus.Connected) return;

        NetBuffer options = server.CreateMessage();
        options.Write(text);
        options.Write(seed);

        NetOutgoingMessage m = server.CreateMessage();
        WriteSpawn(m, server.UniqueIdentifier, now, PrefabLoreText, shortId, options, x, y, 0f);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // Dungeon exit portal, on, pointing to the "destination" server.
    public static void SendExitPortal(NetServer server, NetConnection connection, int shortId, float x, float y,
        IPEndPoint destination, long hostId, string hostName, string version, string[] tags, double now)
    {
        // empty dungeon type = default art (PortalCarrierPrefab); energy end 0 = on forever, doors open.
        SendPortal(server, connection, shortId, x, y, 0f, destination, hostId, hostName, version, tags,
            PortalOn | ExitPortal, "", 0f, 0f, now);
    }

    // Dungeon entrance (Portal flags 1 | 2): the client shows the dungeon name and tier from Texts/dungeons
    // (TextMachine.GetDungeonName/GetDungeonTier of dungeonType, e.g. "DungeonBomb") and "(players/max)" of the host.
    // A dungeon portal disappears when its power ends (timeFinish), so it goes with a far end: the countdown shows "--".
    public static void SendDungeonPortal(NetServer server, NetConnection connection, int shortId, float x, float y, float rotation,
        IPEndPoint destination, long hostId, string hostName, string version, string dungeonType, int maxPlayers, double now)
    {
        SendPortal(server, connection, shortId, x, y, rotation, destination, hostId, hostName, version, null,
            DungeonPortal | PortalOn, dungeonType, (float)now - 20f, (float)now + 1000000f, now, maxPlayers);
    }

    // Dungeon entrance that leaves after a while (Portal flags 1 | 2): the client shows the countdown to "endsAt" (server
    // clock, like "now") and the carrier closes its doors and goes away at 0. The server removes the entity afterwards.
    public static void SendTimedDungeonPortal(NetServer server, NetConnection connection, int shortId, float x, float y, float rotation,
        IPEndPoint destination, long hostId, string hostName, string version, string dungeonType, int maxPlayers, double now, double endsAt)
    {
        SendPortal(server, connection, shortId, x, y, rotation, destination, hostId, hostName, version, null,
            DungeonPortal | PortalOn, dungeonType, (float)now - 20f, (float)endsAt, now, maxPlayers);
    }

    // Portal to another world server that is not a dungeon (Portal flags 2): the client shows the host's name and (players/max).
    // "terrain" goes in the dungeon type field (the client builds the tag portal_<terrain> from it: "portal_basic" guides the player).
    public static void SendWorldPortal(NetServer server, NetConnection connection, int shortId, float x, float y, float rotation,
        IPEndPoint destination, long hostId, string hostName, string version, string terrain, int maxPlayers, double now)
    {
        SendPortal(server, connection, shortId, x, y, rotation, destination, hostId, hostName, version, null,
            PortalOn, terrain, 0f, 0f, now, maxPlayers);
    }

    // ReceiveCurrentSubterritoryName(string name): the region the player's plane is over (the client keeps it for its HUD and statistics).
    public const ushort MsgReceiveCurrentSubterritoryName = 62683;

    public static void SendSubterritoryName(NetServer server, NetConnection connection, string name)
    {
        if (connection.Status != NetConnectionStatus.Connected) return;

        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgReceiveCurrentSubterritoryName);
        m.Write(name);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // Portal (prefab 11) with Portal.CreateEntOptions: RegisteredHost, flags, dungeon type, power start and power end.
    public static void SendPortal(NetServer server, NetConnection connection, int shortId, float x, float y, float rotation,
        IPEndPoint destination, long hostId, string hostName, string version, string[]? tags, int flags, string dungeonType,
        float start, float end, double now, int maxPlayers = 10)
    {
        if (connection.Status != NetConnectionStatus.Connected) return;

        // The server keeps its endpoints as 127.0.0.1:<port>; a player on another PC gets the address their game
        // used to reach this server (Network/PlayerSessions.cs).
        destination = PlayerSessions.AddressFor(connection, destination);
        NetBuffer options = server.CreateMessage();
        PlayerData.WriteRegisteredHost(options, destination, hostId, hostName, version, maxPlayers, tags);
        options.Write((byte)flags);
        options.Write(dungeonType ?? "");
        options.Write(start);
        options.Write(end);

        NetOutgoingMessage m = server.CreateMessage();
        WriteSpawn(m, server.UniqueIdentifier, now, PrefabPortal, shortId, options, x, y, rotation);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // Nexus building (Hangar, StoreBuilding, Tavern, ...): an entity of the prefab with the building's own art.
    // Only the store has options (storeId, string); the others go with null unitOptions (ApplyEmptyEntOptions).
    public static void SendBuilding(NetServer server, NetConnection connection, int shortId, int prefab, float x, float y,
        float rotation, string? store, double now)
    {
        if (connection.Status != NetConnectionStatus.Connected) return;

        NetBuffer? options = null;
        if (!string.IsNullOrEmpty(store))
        {
            options = server.CreateMessage();
            options.Write(store);
        }

        NetOutgoingMessage m = server.CreateMessage();
        WriteSpawn(m, server.UniqueIdentifier, now, prefab, shortId, options, x, y, rotation);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // KillUnitsBatch of one object: (float time, shortId, KillInfo) with KillInfoSerializer:
    // reason, x and y in 16 bits in the +-2000 range, rotation in 8 bits in the +-180 range and the bullet result.
    public static void SendRemoval(NetServer server, NetConnection connection, int shortId, float x, float y, byte reason,
        double now)
    {
        if (connection.Status != NetConnectionStatus.Connected) return;

        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgKillUnitsBatch);
        m.WriteVariableUInt32(2u);                           // 1 item + 1 (0 = null list)
        m.Write((float)now);                               // time
        m.WriteVariableInt32(shortId);                       // shortId
        m.Write(reason);                                     // killReason
        m.WriteRangedSingle(Math.Clamp(x, -2000f, 2000f), -2000f, 2000f, 16);
        m.WriteRangedSingle(Math.Clamp(y, -2000f, 2000f), -2000f, 2000f, 16);
        m.WriteRangedSingle(0f, -180f, 180f, 8);             // rotation
        m.Write((byte)0);                                    // bulletHitResult = None
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // ClientTeleportToCoordinatesAfterTime(Vector2 coordinates, float rotation, float afterTime).
    // A rotation outside +-360 keeps the plane's current rotation.
    public static void SendTeleport(NetServer server, NetConnection connection, float x, float y, float rotation, float after)
    {
        if (connection.Status != NetConnectionStatus.Connected) return;

        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientTeleportToCoordinatesAfterTime);
        m.Write(x);
        m.Write(y);
        m.Write(rotation);
        m.Write(after);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // ClientShowUnitChat(int shortId, string barkKey, float lifetime, AISpeechFormat format, string portrait,
    // string audio, float randomBark). "portrait" is the portrait key (e.g. "sergeant", "bosscat");
    // randomBark (0..1) picks between the options of the line when it has more than one.
    public static void SendSpeech(NetServer server, NetConnection connection, int shortId, string key, float duration,
        byte format, string portrait, float rng)
    {
        if (connection.Status != NetConnectionStatus.Connected) return;

        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientShowUnitChat);
        m.Write(shortId);
        m.Write(key);
        m.Write(duration);
        m.Write(format);
        m.Write(portrait ?? "");
        m.Write("");          // audio
        m.Write(rng);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // ClientReceiveText with type ServerSays: it shows in the game chat.
    public static void SendChat(NetServer server, NetConnection connection, string text)
    {
        if (connection.Status != NetConnectionStatus.Connected) return;

        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientReceiveText);
        m.Write(text);
        m.Write((byte)0);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // SpawnUnitsBatch with one item, in the order of EntitySpawnStructSerializer.Write (see PlaneSpawn):
    // spawnTime, prefabId, shortId, templateHash, familyRootTemplateHash, parentShortId, teamIndex,
    // artReplacement, authority, unitOptions (bytes + 1) and FieldUpdate with position, velocity and rotation.
    private static void WriteSpawn(NetBuffer m, long authority, double now, int prefab, int shortId,
        NetBuffer? options, float x, float y, float rotation)
    {
        m.Write(MsgSpawnUnitsBatch);
        m.WriteVariableUInt32(2u);                           // 1 item + 1 (0 = null list)
        m.Write((float)now);                               // spawnTime
        m.WriteVariableInt32(prefab);                        // prefabId
        m.WriteVariableInt32(shortId);                       // shortId
        m.Write((short)0);                                   // templateHash (no UnitConfig)
        m.Write((short)0);                                   // familyRootTemplateHash
        m.WriteVariableInt32(0);                             // parentShortId
        m.WriteVariableInt32(PlaneSpawn.PlayerTeamIndex);  // the player's team (0)
        m.Write((short)0);                                   // artReplacement
        m.Write(authority);                                 // authority = server
        if (options == null)
        {
            m.WriteVariableInt32(0);                         // null unitOptions
        }
        else
        {
            m.WriteVariableInt32(options.LengthBytes + 1);    // unitOptions: bytes + 1
            m.Write(options);
        }
        // FieldUpdate: position, velocity (zero) and rotation. The velocity MUST go: the client extrapolates server
        // objects with position + velocity * time (UnitUpdateCollection.Extrapolate) and, without the field, uses the
        // "not set" value of FieldUpdate (1234567.9, -1234567.9). Since the portal has minUpdateInterval
        // 1000, the extrapolation is not limited and the carrier walked off diagonally (+x, -y), down the corridor.
        m.WriteVariableUInt32(1u | 2u | 4u);
        WriteScaled(m, x, 1024f);
        WriteScaled(m, y, 1024f);
        WriteScaled(m, 0f, 1024f);                      // velocity x
        WriteScaled(m, 0f, 1024f);                      // velocity y
        WriteScaled(m, rotation, 32f);
    }

    // The client's ScaledHelper.WriteScaled: variable integer of floor(value * scale).
    private static void WriteScaled(NetBuffer buf, float val, float scale)
    {
        buf.WriteVariableInt32((int)Math.Floor((double)val * scale));
    }
}
